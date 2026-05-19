using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WchHexConverter;

namespace WCHIAPToolCLI;

// ============================================================================
// Exit Codes
// ============================================================================
enum ExitCode
{
    Success = 0,
    GeneralError = 1,
    NoDevice = 2,
    FileError = 3,
    EraseFailed = 4,
    ProgramFailed = 5,
    VerifyFailed = 6,
    Timeout = 7,
    CompareMismatch = 8,  // for --compare-bin when files differ
}

// ============================================================================
// CLI Args
// ============================================================================
class CliArgs
{
    public string FilePath { get; set; } = "";
    public int DeviceIndex { get; set; } = -1; // -1 = auto-select first matching
    public ushort VidFilter { get; set; } = 0x4348;
    public ushort PidFilter { get; set; } = 0x55E0;
    public bool Quiet { get; set; }
    public bool JsonOutput { get; set; }
    public bool NoWait { get; set; }
    public bool Debug { get; set; }
    public bool InfoOnly { get; set; }
    public bool SkipVerify { get; set; }
    public bool SkipProg { get; set; }
    public int TimeoutMs { get; set; } = 90000;
    public bool ShowHelp { get; set; }
    public string TestHexFile { get; set; } = "";
    public string CompareBinFile1 { get; set; } = "";
    public string CompareBinFile2 { get; set; } = "";
}

// ============================================================================
// JSON Output Models (avoid System.IO.FileInfo / GUI DeviceInfo collisions)
// ============================================================================
class IapResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = "";
    public UsbDeviceEntry? Device { get; set; }
    public FirmwareFileEntry? File { get; set; }
    public TimingEntry? Timing { get; set; }

    public string? Error => Success ? null : Message;
}

class FirmwareFileEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Type { get; set; } = "";
    public string StartAddress { get; set; } = "";
}

class TimingEntry
{
    public long EraseMs { get; set; }
    public long ProgramMs { get; set; }
    public long VerifyMs { get; set; }
    public long TotalMs { get; set; }
}

class UsbDeviceEntry
{
    public uint Index { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string Name { get; set; } = "";
    public string VidPidString => $"VID=0x{VendorId:X4}, PID=0x{ProductId:X4}";
}

// ============================================================================
// Main Program
// ============================================================================
class Program
{
    // --- CH375 DLL Imports --------------------------------------------------
    [DllImport("CH375DLL64.dll", EntryPoint = "CH375OpenDevice", SetLastError = true)]
    public static extern IntPtr CH375OpenDevice(uint iIndex);

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375CloseDevice", SetLastError = true)]
    public static extern void CH375CloseDevice(uint iIndex);

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375GetVersion", SetLastError = true)]
    public static extern uint CH375GetVersion();

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375GetUsbID", SetLastError = true)]
    public static extern uint CH375GetUsbID(uint iIndex);

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375GetDeviceName", SetLastError = true)]
    public static extern IntPtr CH375GetDeviceName(uint iIndex);

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375ReadData", SetLastError = true)]
    public static extern bool CH375ReadData(uint iIndex, IntPtr oBuffer, ref uint ioLength);

    [DllImport("CH375DLL64.dll", EntryPoint = "CH375WriteData", SetLastError = true)]
    public static extern bool CH375WriteData(uint iIndex, IntPtr iBuffer, ref uint ioLength);

    // --- IAP Protocol Constants ---------------------------------------------
    private const byte CMD_IAP_PROM = 0x80;
    private const byte CMD_IAP_ERASE = 0x81;
    private const byte CMD_IAP_VERIFY = 0x82;
    private const byte CMD_IAP_END = 0x83;
    private const byte CMD_JUMP_IAP = 0x84;

    private const byte ERR_SUCCESS = 0x00;
    private const byte ERR_ERROR = 0x01;
    private const byte ERR_End = 0x02;

    private const int USB_PACKET_SIZE = 64;
    private const int MAX_DATA_PER_PACKET = 62;

    // --- State --------------------------------------------------------------
    private static CliArgs _args = new();
    private static uint _selectedDeviceIndex;
    private static UsbDeviceEntry? _selectedDevice;
    private static FirmwareFileEntry? _loadedFile;
    private static readonly Stopwatch _totalTimer = new();
    private static readonly Stopwatch _opTimer = new();

    // ========================================================================
    // Main Entry
    // ========================================================================
    static int Main(string[] args)
    {
        try { File.Delete(_traceLogPath); } catch { }
        TraceLog($"=== WCHIAPToolCLI start, args: {string.Join(" ", args)} ===");
        try
        {
            _args = ParseArgs(args);

            // Tool modes (no device needed)
            if (_args.ShowHelp)
            {
                PrintHelp();
                return (int)ExitCode.Success;
            }
            if (!string.IsNullOrEmpty(_args.TestHexFile))
            {
                return (int)TestHexConversion(_args.TestHexFile);
            }
            if (!string.IsNullOrEmpty(_args.CompareBinFile1))
            {
                return (int)CompareBinFiles(_args.CompareBinFile1, _args.CompareBinFile2);
            }

            // Normal IAP mode
            return (int)RunIapMode();
        }
        catch (Exception ex)
        {
            var detail = _args.Debug
                ? $" (Win32 err: {Marshal.GetLastWin32Error()})"
                : "";
            OutputError(ExitCode.GeneralError, $"Unexpected error: {ex.Message}{detail}");
            WaitKeyIfNeeded();
            return (int)ExitCode.GeneralError;
        }
    }

    // ========================================================================
    // Argument Parsing
    // ========================================================================
    static CliArgs ParseArgs(string[] args)
    {
        var a = new CliArgs();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    a.ShowHelp = true;
                    break;

                case "--file":
                case "-f":
                    if (i + 1 < args.Length) a.FilePath = args[++i];
                    else throw new ArgumentException("--file requires a path argument");
                    break;

                case "--device":
                case "-d":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int devIdx))
                        a.DeviceIndex = devIdx;
                    else throw new ArgumentException("--device requires a numeric index");
                    break;

                case "--vid":
                    if (i + 1 < args.Length && TryParseHexUshort(args[++i], out ushort vid))
                        a.VidFilter = vid;
                    else throw new ArgumentException("--vid requires a hex value (e.g. 4348 or 0x4348)");
                    break;

                case "--pid":
                    if (i + 1 < args.Length && TryParseHexUshort(args[++i], out ushort pid))
                        a.PidFilter = pid;
                    else throw new ArgumentException("--pid requires a hex value (e.g. 55E0 or 0x55E0)");
                    break;

                case "--quiet":
                case "-q":
                    a.Quiet = true;
                    break;

                case "--json":
                    a.JsonOutput = true;
                    break;

                case "--no-wait":
                    a.NoWait = true;
                    break;

                case "--debug":
                    a.Debug = true;
                    break;

                case "--info":
                    a.InfoOnly = true;
                    break;

                case "--skip-verify":
                    a.SkipVerify = true;
                    break;

                case "--skip_prog":
                    a.SkipProg = true;
                    break;

                case "--timeout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int toMs))
                        a.TimeoutMs = toMs;
                    else throw new ArgumentException("--timeout requires a millisecond value");
                    break;

                case "--test-hex":
                    if (i + 1 < args.Length) a.TestHexFile = args[++i];
                    else throw new ArgumentException("--test-hex requires a file path");
                    break;

                case "--compare-bin":
                    if (i + 2 < args.Length)
                    {
                        a.CompareBinFile1 = args[++i];
                        a.CompareBinFile2 = args[++i];
                    }
                    else throw new ArgumentException("--compare-bin requires two file paths");
                    break;

                default:
                    if (Path.HasExtension(args[i]) && string.IsNullOrEmpty(a.FilePath))
                        a.FilePath = args[i];
                    else
                        LogMessage($"Warning: Unknown argument '{args[i]}'", withTimestamp: true);
                    break;
            }
        }

        return a;
    }

    static bool TryParseHexUshort(string raw, out ushort value)
    {
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            raw = raw.Substring(2);
        return ushort.TryParse(raw, NumberStyles.HexNumber, null, out value);
    }

    // ========================================================================
    // IAP Mode Main Flow
    // ========================================================================
    static ExitCode RunIapMode()
    {
        _totalTimer.Restart();

        // --- 1. Search devices ---
        LogMessage("1. Searching devices...", forceOutput: true);
        uint version = CH375GetVersion();
        LogDebug($"CH375 DLL Version: {version}");

        var devices = SearchDevices(_args.VidFilter, _args.PidFilter);

        if (devices.Count == 0)
        {
            OutputError(ExitCode.NoDevice,
                $"No device found with VID=0x{_args.VidFilter:X4}, PID=0x{_args.PidFilter:X4}");
            return ExitCode.NoDevice;
        }

        LogMessage($"Found {devices.Count} matching device(s):", forceOutput: true);
        foreach (var d in devices)
        {
            LogMessage($"  Device {d.Index}: {d.VidPidString}, {d.Name}", forceOutput: true);
        }

        // Select device
        if (_args.DeviceIndex >= 0)
        {
            _selectedDevice = devices.Find(d => d.Index == (uint)_args.DeviceIndex);
            if (_selectedDevice == null)
            {
                OutputError(ExitCode.NoDevice,
                    $"Device index {_args.DeviceIndex} not found in matching devices");
                return ExitCode.NoDevice;
            }
        }
        else
        {
            _selectedDevice = devices[0];
        }

        _selectedDeviceIndex = _selectedDevice.Index;
        TraceLog($"SELECTED device index={_selectedDevice.Index} VID=0x{_selectedDevice.VendorId:X4} PID=0x{_selectedDevice.ProductId:X4}");
        LogMessage($"Selected: Device {_selectedDevice.Index}, {_selectedDevice.VidPidString}",
            forceOutput: true);

        // --info mode: just show device info and exit
        if (_args.InfoOnly)
        {
            OutputSuccess("Device information retrieved");
            return ExitCode.Success;
        }

        // --- 2. Load file ---
        byte[]? fileContent = null;
        string fileType = "";
        uint startAddress = 0;

        if (!_args.SkipProg)
        {
            if (string.IsNullOrEmpty(_args.FilePath))
            {
                OutputError(ExitCode.FileError, "No file specified. Use --file <path> or pass file as argument.");
                return ExitCode.FileError;
            }

            if (!File.Exists(_args.FilePath))
            {
                OutputError(ExitCode.FileError, $"File not found: {_args.FilePath}");
                return ExitCode.FileError;
            }

            LogMessage($"2. Loading file: {_args.FilePath}", forceOutput: true);

            if (_args.FilePath.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
            {
                LogMessage("   Converting HEX to BIN...");
                var hexResult = WchHexToBinConverter.ConvertHexToBin(_args.FilePath);
                fileContent = hexResult.Data;
                startAddress = hexResult.StartAddress;
                fileType = "hex";

                if (fileContent.Length == 0)
                {
                    OutputError(ExitCode.FileError, "HEX file contains no data");
                    return ExitCode.FileError;
                }

                LogMessage($"   Size: {fileContent.Length} bytes, Start: 0x{startAddress:X8}");
            }
            else
            {
                fileContent = File.ReadAllBytes(_args.FilePath);
                fileType = "bin";
                LogMessage($"   Size: {fileContent.Length} bytes (raw binary)");
            }

            _loadedFile = new FirmwareFileEntry
            {
                Path = _args.FilePath,
                Size = fileContent.Length,
                Type = fileType,
                StartAddress = $"0x{startAddress:X8}",
            };
        }
        else
        {
            LogMessage($"2. Skip file loading (--skip_prog)");
        }

        // --- 3. Open device ---
        LogMessage("3. Opening device...", forceOutput: true);
        TraceLog($"OPENING device index={_selectedDeviceIndex}");
        IntPtr handle = CH375OpenDevice(_selectedDeviceIndex);
        TraceLog($"OPEN result handle=0x{handle.ToInt64():X8}");
        if (handle == IntPtr.Zero)
        {
            TraceLog("OPEN FAILED");
            OutputError(ExitCode.GeneralError, "Failed to open device");
            return ExitCode.GeneralError;
        }
        LogMessage("   Device opened successfully");

        try
        {
            // --- 4. Erase ---
            long eraseMs = 0, programMs = 0, verifyMs = 0;
            if (!_args.SkipProg)
            {
                LogMessage("4. Erasing flash...", forceOutput: true);
                _opTimer.Restart();
                if (!SendEraseCommand())
                {
                    OutputError(ExitCode.EraseFailed, "Erase command failed");
                    return ExitCode.EraseFailed;
                }
                eraseMs = _opTimer.ElapsedMilliseconds;
                LogMessage($"   Success ({eraseMs}ms)");

                if (_totalTimer.ElapsedMilliseconds > _args.TimeoutMs)
                {
                    OutputError(ExitCode.Timeout, "Timeout exceeded after erase");
                    return ExitCode.Timeout;
                }

                // --- 5. Program ---
                LogMessage($"5. Programming flash ({fileContent!.Length} bytes)...", forceOutput: true);
                _opTimer.Restart();
                if (!SendProgramData(fileContent))
                {
                    OutputError(ExitCode.ProgramFailed, "Program command failed");
                    return ExitCode.ProgramFailed;
                }
                programMs = _opTimer.ElapsedMilliseconds;
                LogMessage($"   Success ({programMs}ms, {fileContent.Length * 1000L / Math.Max(programMs, 1)} B/s)");

                if (_totalTimer.ElapsedMilliseconds > _args.TimeoutMs)
                {
                    OutputError(ExitCode.Timeout, "Timeout exceeded after program");
                    return ExitCode.Timeout;
                }

                // --- 6. Verify ---
                if (!_args.SkipVerify)
                {
                    LogMessage("6. Verifying flash...", forceOutput: true);
                    _opTimer.Restart();
                    if (!SendVerifyCommand(fileContent))
                    {
                        OutputError(ExitCode.VerifyFailed, "Verify failed - flash content mismatch");
                        return ExitCode.VerifyFailed;
                    }
                    verifyMs = _opTimer.ElapsedMilliseconds;
                    LogMessage($"   Success ({verifyMs}ms)");
                }
                else
                {
                    LogMessage("6. Verify skipped (--skip-verify)", forceOutput: true);
                }
            }
            else
            {
                LogMessage("4-6. Skipped (--skip_prog)", forceOutput: true);
            }

            // --- 7. End ---
            LogMessage("7. Ending IAP session...", forceOutput: true);
            SendEndCommand();
            LogMessage("   Device reset to application");

            // --- Success ---
            _totalTimer.Stop();
            var timing = new TimingEntry
            {
                EraseMs = eraseMs,
                ProgramMs = programMs,
                VerifyMs = verifyMs,
                TotalMs = _totalTimer.ElapsedMilliseconds,
            };

            OutputSuccess("IAP download completed successfully", timing);
            return ExitCode.Success;
        }
        finally
        {
            LogDebug("Closing device...");
            try { CH375CloseDevice(_selectedDeviceIndex); } catch { /* ignore close errors */ }
            LogDebug("Device closed");
        }
    }

    // ========================================================================
    // Device Search (with VID/PID filtering and device-name fallback)
    // ========================================================================
    static List<UsbDeviceEntry> SearchDevices(ushort vidFilter, ushort pidFilter)
    {
        var devices = new List<UsbDeviceEntry>();
        var invalidHandle = new IntPtr(-1);

        for (uint i = 0; i < 16; i++)
        {
            IntPtr searchHandle = IntPtr.Zero;
            try
            {
                searchHandle = CH375OpenDevice(i);
                LogDebug($"CH375OpenDevice({i}) = 0x{searchHandle.ToInt64():X8}");

                if (searchHandle == IntPtr.Zero || searchHandle == invalidHandle)
                    continue;

                uint usbId = CH375GetUsbID(i);
                ushort vendorId = (ushort)(usbId >> 16);
                ushort productId = (ushort)(usbId & 0xFFFF);
                IntPtr deviceNamePtr = CH375GetDeviceName(i);
                string deviceName = Marshal.PtrToStringAnsi(deviceNamePtr) ?? "";

                LogDebug($"Raw: VID=0x{vendorId:X4}, PID=0x{productId:X4}, Name={deviceName}");

                // Fallback: parse VID/PID from device name.
                // Device name is the authoritative source (raw USB path),
                // because CH375GetUsbID may return swapped bytes on some DLL versions.
                // Device name format: \\?\usb#vid_4348&pid_55e0#...
                if (!string.IsNullOrEmpty(deviceName))
                {
                    var vidMatch = Regex.Match(deviceName,
                        @"vid_([0-9a-fA-F]{4})", RegexOptions.IgnoreCase);
                    var pidMatch = Regex.Match(deviceName,
                        @"pid_([0-9a-fA-F]{4})", RegexOptions.IgnoreCase);

                    if (vidMatch.Success && pidMatch.Success)
                    {
                        vendorId = Convert.ToUInt16(vidMatch.Groups[1].Value, 16);
                        productId = Convert.ToUInt16(pidMatch.Groups[1].Value, 16);
                        LogDebug($"Parsed from device name: VID=0x{vendorId:X4}, PID=0x{productId:X4}");
                    }
                }

                if (vendorId == 0 && productId == 0)
                    continue;
                if (vendorId != vidFilter || productId != pidFilter)
                    continue;
                if (string.IsNullOrEmpty(deviceName))
                    continue;

                devices.Add(new UsbDeviceEntry
                {
                    Index = i,
                    VendorId = vendorId,
                    ProductId = productId,
                    Name = deviceName,
                });
            }
            catch (Exception ex)
            {
                LogDebug($"Error checking device {i}: {ex.Message}");
            }
            finally
            {
                if (searchHandle != IntPtr.Zero && searchHandle != invalidHandle)
                    CH375CloseDevice(i);
            }
        }

        LogDebug($"Search result: {devices.Count} device(s) matched");
        return devices;
    }

    // ========================================================================
    // IAP Protocol Commands
    // ========================================================================

    static bool SendEraseCommand()
    {
        try
        {
            return SendCommandAndCheck(CMD_IAP_ERASE);
        }
        catch (Exception ex)
        {
            LogDebug($"Erase exception: {ex.Message}");
            return false;
        }
    }

    static bool SendCommandAndCheck(byte cmd, byte[]? payload = null, int payloadLen = 0)
    {
        byte[] cmdBuf = new byte[USB_PACKET_SIZE];
        cmdBuf[0] = cmd;
        cmdBuf[1] = (byte)(payloadLen > 0 ? payloadLen : 0);
        if (payload != null && payloadLen > 0)
            Array.Copy(payload, 0, cmdBuf, 2, Math.Min(payloadLen, MAX_DATA_PER_PACKET));

        uint len = (uint)cmdBuf.Length;
        IntPtr ptr = Marshal.AllocHGlobal((int)len);
        try
        {
            Marshal.Copy(cmdBuf, 0, ptr, (int)len);
            TraceLog($"CMD WRITE cmd=0x{cmd:X2}");
            if (!CH375WriteData(_selectedDeviceIndex, ptr, ref len))
            {
                TraceLog("CMD WRITE FAILED");
                return false;
            }
            TraceLog("CMD WRITE OK");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        byte[] resp = new byte[USB_PACKET_SIZE];
        len = (uint)resp.Length;
        ptr = Marshal.AllocHGlobal((int)len);
        try
        {
            TraceLog("CMD READ start");
            if (!CH375ReadData(_selectedDeviceIndex, ptr, ref len))
            {
                TraceLog("CMD READ FAILED");
                return false;
            }
            TraceLog("CMD READ OK");
            Marshal.Copy(ptr, resp, 0, (int)len);
            return resp[0] == ERR_SUCCESS;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    static bool SendProgramData(byte[] fileContent)
    {
        try
        {
            int offset = 0;
            int lastReport = 0;

            while (offset < fileContent.Length)
            {
                int chunkSize = Math.Min(MAX_DATA_PER_PACKET, fileContent.Length - offset);

                byte[] cmdBuf = new byte[USB_PACKET_SIZE];
                cmdBuf[0] = CMD_IAP_PROM;
                cmdBuf[1] = (byte)chunkSize;
                Array.Copy(fileContent, offset, cmdBuf, 2, chunkSize);

                uint len = (uint)cmdBuf.Length;
                IntPtr ptr = Marshal.AllocHGlobal((int)len);
                try
                {
                    Marshal.Copy(cmdBuf, 0, ptr, (int)len);
                    TraceLog($"PROG WRITE offset=0x{offset:X} size={chunkSize}");
                    if (!CH375WriteData(_selectedDeviceIndex, ptr, ref len))
                    {
                        TraceLog("PROG WRITE FAILED");
                        return false;
                    }
                    TraceLog("PROG WRITE OK");
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                len = (uint)USB_PACKET_SIZE;
                ptr = Marshal.AllocHGlobal((int)len);
                try
                {
                    TraceLog("PROG READ start");
                    if (!CH375ReadData(_selectedDeviceIndex, ptr, ref len))
                    {
                        TraceLog("PROG READ FAILED");
                        return false;
                    }
                    TraceLog("PROG READ OK");
                    byte[] resp = new byte[(int)len];
                    Marshal.Copy(ptr, resp, 0, (int)len);
                    if (resp[0] != ERR_SUCCESS)
                    {
                        LogDebug($"Program chunk at offset 0x{offset:X} failed: response 0x{resp[0]:X2}");
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                offset += chunkSize;

                if (offset - lastReport >= 4096 || offset == fileContent.Length)
                {
                    LogMessage($"   Written {offset} / {fileContent.Length} bytes ({(offset * 100.0 / fileContent.Length):F1}%)");
                    lastReport = offset;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"Program exception: {ex.Message}");
            return false;
        }
    }

    static bool SendVerifyCommand(byte[] fileContent)
    {
        try
        {
            int offset = 0;

            while (offset < fileContent.Length)
            {
                int chunkSize = Math.Min(MAX_DATA_PER_PACKET, fileContent.Length - offset);

                byte[] cmdBuf = new byte[USB_PACKET_SIZE];
                cmdBuf[0] = CMD_IAP_VERIFY;
                cmdBuf[1] = (byte)chunkSize;
                Array.Copy(fileContent, offset, cmdBuf, 2, chunkSize);

                uint len = (uint)cmdBuf.Length;
                IntPtr ptr = Marshal.AllocHGlobal((int)len);
                try
                {
                    Marshal.Copy(cmdBuf, 0, ptr, (int)len);
                    TraceLog($"VERIFY WRITE offset=0x{offset:X}");
                    if (!CH375WriteData(_selectedDeviceIndex, ptr, ref len))
                    {
                        TraceLog("VERIFY WRITE FAILED");
                        return false;
                    }
                    TraceLog("VERIFY WRITE OK");
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                len = (uint)USB_PACKET_SIZE;
                ptr = Marshal.AllocHGlobal((int)len);
                try
                {
                    TraceLog("VERIFY READ start");
                    if (!CH375ReadData(_selectedDeviceIndex, ptr, ref len))
                    {
                        TraceLog("VERIFY READ FAILED");
                        return false;
                    }
                    TraceLog("VERIFY READ OK");
                    byte[] resp = new byte[(int)len];
                    Marshal.Copy(ptr, resp, 0, (int)len);
                    if (resp[0] != ERR_SUCCESS)
                    {
                        LogDebug($"Verify mismatch at offset 0x{offset:X}: response 0x{resp[0]:X2}");
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                offset += chunkSize;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogDebug($"Verify exception: {ex.Message}");
            return false;
        }
    }

    static void SendEndCommand()
    {
        try
        {
            byte[] cmdBuf = new byte[USB_PACKET_SIZE];
            cmdBuf[0] = CMD_IAP_END;
            cmdBuf[1] = 0x00;

            uint len = (uint)cmdBuf.Length;
            IntPtr ptr = Marshal.AllocHGlobal((int)len);
            try
            {
                Marshal.Copy(cmdBuf, 0, ptr, (int)len);
                TraceLog("END WRITE");
                if (!CH375WriteData(_selectedDeviceIndex, ptr, ref len))
                {
                    LogDebug("SendEndCommand: write failed");
                    TraceLog("END WRITE FAILED");
                    return;
                }
                TraceLog("END WRITE OK");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            TraceLog("END sent, device resets");
            LogDebug("End command sent, device will reset to application");
        }
        catch (Exception ex)
        {
            LogDebug($"End command: device disconnected (expected): {ex.Message}");
        }
    }

    // ========================================================================
    // Output Helpers
    // ========================================================================

    // Persistent file log for diagnosing USB hangs. Always flushes.
    private static readonly string _traceLogPath =
        Path.Combine(Path.GetTempPath(), "WCHIAPToolCLI_trace.log");
    private static readonly object _traceLock = new();
    static void TraceLog(string msg)
    {
        lock (_traceLock)
        {
            try { File.AppendAllText(_traceLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
            catch { }
        }
    }

    // In JSON mode, suppress ALL LogMessage output to keep stdout clean.
    // Only OutputSuccess / OutputError write to stdout in JSON mode.
    static void LogMessage(string message, bool forceOutput = false, bool withTimestamp = false)
    {
        if (_args.JsonOutput)
            return;

        if (_args.Quiet && !forceOutput)
            return;

        if (withTimestamp || _args.Debug)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        else
            Console.WriteLine(message);
    }

    static void LogDebug(string message)
    {
        if (_args.Debug && !_args.JsonOutput)
            Console.Error.WriteLine($"[DEBUG {DateTime.Now:HH:mm:ss}] {message}");
    }

    static void OutputError(ExitCode code, string message)
    {
        if (_args.JsonOutput)
        {
            var result = new IapResult
            {
                Success = false,
                ExitCode = (int)code,
                Message = message,
                Device = _selectedDevice,
            };
            Console.WriteLine(JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = false }));
        }
        else
        {
            Console.Error.WriteLine($"[ERROR] {message}");
            if (!_args.Quiet)
                Console.WriteLine($"Exit code: {(int)code} ({code})");
        }
    }

    static void OutputSuccess(string message, TimingEntry? timing = null)
    {
        if (_args.JsonOutput)
        {
            var result = new IapResult
            {
                Success = true,
                ExitCode = 0,
                Message = message,
                Device = _selectedDevice,
                File = _loadedFile,
                Timing = timing,
            };
            Console.WriteLine(JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = false }));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"SUCCESS: {message}");
            if (timing != null)
            {
                Console.WriteLine($"Timing: Erase={timing.EraseMs}ms, Program={timing.ProgramMs}ms, " +
                    $"Verify={timing.VerifyMs}ms, Total={timing.TotalMs}ms");
            }
        }

        WaitKeyIfNeeded();
    }

    // ========================================================================
    // Help
    // ========================================================================
    static void PrintHelp()
    {
        Console.WriteLine(@"WCH IAP Tool CLI - Scriptable IAP Programming Tool
=========================================================

Usage:
  WCHIAPToolCLI [options] [file]

Options:
  --file, -f <path>      Firmware file to download (.hex or .bin)
  --device, -d <index>   Device index (default: auto-select first matching)
  --vid <hex>            VID filter (default: 4348)
  --pid <hex>            PID filter (default: 55E0)
  --quiet, -q            Suppress non-essential output
  --json                 Output result as JSON (for agent/script parsing)
  --no-wait              Exit immediately, don't wait for keypress
  --timeout <ms>         Overall timeout in milliseconds (default: 30000)
  --skip-verify          Skip flash verification step
  --skip_prog            Skip erase/program/verify (just send END to exit bootloader)
  --debug                Enable debug output (to stderr)
  --info                 Show device information only, no download
  --help, -h             Show this help

Tool commands (no device required):
  --test-hex <file>      Parse and display HEX file information
  --compare-bin <a> <b>  Compare two binary files byte-by-byte

Exit Codes:
  0  Success
  1  General error
  2  No matching device found
  3  File error (not found, invalid, or empty)
  4  Erase failed
  5  Program failed
  6  Verify failed
  7  Timeout exceeded
  8  Files differ (--compare-bin)

Examples:
  WCHIAPToolCLI --file firmware.hex
  WCHIAPToolCLI -f firmware.bin -d 1 --no-wait
  WCHIAPToolCLI --file app.hex --json
  WCHIAPToolCLI --vid 0x4348 --pid 0x55E0 --info
  WCHIAPToolCLI --test-hex firmware.hex
  WCHIAPToolCLI --compare-bin a.bin b.bin
  WCHIAPToolCLI firmware.hex       (positional file path)
");
    }

    // ========================================================================
    // Tool Commands (--test-hex, --compare-bin) — now return ExitCode
    // ========================================================================

    static ExitCode TestHexConversion(string hexFile)
    {
        Console.WriteLine($"Testing HEX file: {hexFile}");
        Console.WriteLine();

        if (!File.Exists(hexFile))
        {
            Console.Error.WriteLine($"File not found: {hexFile}");
            WaitKeyIfNeeded();
            return ExitCode.FileError;
        }

        try
        {
            var lines = File.ReadAllLines(hexFile);
            Console.WriteLine($"Total lines: {lines.Length}");
            Console.WriteLine();

            Console.WriteLine("First 10 data records:");
            int lineCount = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                    continue;

                var data = WchHexToBinConverter.ParseHexLine(line);
                if (data == null) continue;

                byte recordType = data[3];
                string typeName = recordType switch
                {
                    0x00 => "DATA",
                    0x01 => "EOF",
                    0x02 => "EXT_SEG_ADDR",
                    0x03 => "START_SEG_ADDR",
                    0x04 => "EXT_LIN_ADDR",
                    0x05 => "START_LIN_ADDR",
                    _ => $"UNKNOWN(0x{recordType:X2})",
                };

                uint addr = (uint)((data[1] << 8) | data[2]);
                Console.Write($"  [{lineCount + 1}] {typeName,-16} Len={data[0],2}  Addr=0x{addr:X4}");

                if (recordType == 0x00 && data[0] > 0 && lineCount < 5)
                {
                    Console.Write("  Data: ");
                    int show = Math.Min(16, (int)data[0]);
                    for (int i = 0; i < show; i++)
                        Console.Write($"{data[4 + i]:X2} ");
                }
                Console.WriteLine();

                lineCount++;
                if (lineCount >= 10) break;
            }

            Console.WriteLine();

            var lastLines = new List<string>();
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && line[0] == ':')
                    lastLines.Add(line);
            }

            Console.WriteLine("Last 5 records:");
            for (int i = Math.Max(0, lastLines.Count - 5); i < lastLines.Count; i++)
            {
                var data = WchHexToBinConverter.ParseHexLine(lastLines[i]);
                if (data == null) continue;

                byte recordType = data[3];
                string typeName = recordType switch
                {
                    0x00 => "DATA", 0x01 => "EOF",
                    0x04 => "EXT_LIN_ADDR", 0x05 => "START_LIN_ADDR",
                    _ => $"TYPE_{recordType:X2}",
                };

                Console.WriteLine($"  {typeName,-16} Len={data[0],2}");
            }

            Console.WriteLine();

            var hexResult = WchHexToBinConverter.ConvertHexToBin(hexFile);
            Console.WriteLine("Conversion result:");
            Console.WriteLine($"  Data size:    {hexResult.Data.Length} bytes ({hexResult.Data.Length / 1024.0:F1} KB)");
            Console.WriteLine($"  Start address: 0x{hexResult.StartAddress:X8}");
            Console.WriteLine($"  End address:   0x{hexResult.StartAddress + (uint)hexResult.Data.Length:X8}");
            Console.WriteLine();

            Console.WriteLine("First 64 bytes of BIN data:");
            int showLen = Math.Min(64, hexResult.Data.Length);
            for (int i = 0; i < showLen; i++)
            {
                Console.Write($"{hexResult.Data[i]:X2} ");
                if ((i + 1) % 16 == 0) Console.WriteLine();
            }
            Console.WriteLine();

            string binFile = Path.ChangeExtension(hexFile, ".test.bin");
            File.WriteAllBytes(binFile, hexResult.Data);
            Console.WriteLine($"Test BIN saved to: {binFile}");

            WaitKeyIfNeeded();
            return ExitCode.Success;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Conversion failed: {ex.Message}");
            WaitKeyIfNeeded();
            return ExitCode.FileError;
        }
    }

    static ExitCode CompareBinFiles(string file1, string file2)
    {
        Console.WriteLine($"Comparing BIN files:");
        Console.WriteLine($"  File 1: {file1}");
        Console.WriteLine($"  File 2: {file2}");
        Console.WriteLine();

        if (!File.Exists(file1))
        {
            Console.Error.WriteLine($"File not found: {file1}");
            WaitKeyIfNeeded();
            return ExitCode.FileError;
        }
        if (!File.Exists(file2))
        {
            Console.Error.WriteLine($"File not found: {file2}");
            WaitKeyIfNeeded();
            return ExitCode.FileError;
        }

        byte[] data1 = File.ReadAllBytes(file1);
        byte[] data2 = File.ReadAllBytes(file2);

        Console.WriteLine($"File 1 size: {data1.Length} bytes");
        Console.WriteLine($"File 2 size: {data2.Length} bytes");
        Console.WriteLine();

        int minLen = Math.Min(data1.Length, data2.Length);
        int diffCount = 0;
        int firstDiff = -1;

        for (int i = 0; i < minLen; i++)
        {
            if (data1[i] != data2[i])
            {
                diffCount++;
                if (firstDiff < 0) firstDiff = i;
            }
        }

        if (data1.Length != data2.Length)
        {
            Console.WriteLine($"Size difference: {Math.Abs(data1.Length - data2.Length)} bytes");
        }

        if (diffCount == 0 && data1.Length == data2.Length)
        {
            Console.WriteLine("Files are identical.");
            WaitKeyIfNeeded();
            return ExitCode.Success;
        }
        else
        {
            Console.WriteLine($"Found {diffCount} difference(s)");

            if (firstDiff >= 0)
            {
                Console.WriteLine();
                Console.WriteLine($"First difference at offset 0x{firstDiff:X4} ({firstDiff}):");
                Console.WriteLine($"  File 1: 0x{data1[firstDiff]:X2}  File 2: 0x{data2[firstDiff]:X2}");

                Console.WriteLine();
                Console.WriteLine("Context around first difference:");
                int ctx = Math.Max(0, firstDiff - 8);
                int ctxEnd = Math.Min(minLen, firstDiff + 8);

                Console.Write("  File 1: ");
                for (int i = ctx; i < ctxEnd; i++) Console.Write($"{data1[i]:X2} ");
                Console.WriteLine();

                Console.Write("  File 2: ");
                for (int i = ctx; i < ctxEnd; i++) Console.Write($"{data2[i]:X2} ");
                Console.WriteLine();

                Console.Write("          ");
                for (int i = ctx; i < ctxEnd; i++)
                    Console.Write(data1[i] != data2[i] ? "^^ " : "   ");
                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine("First 10 differences:");
            int shown = 0;
            for (int i = 0; i < minLen && shown < 10; i++)
            {
                if (data1[i] != data2[i])
                {
                    Console.WriteLine($"  0x{i:X4}: {data1[i]:X2} -> {data2[i]:X2}  (delta={Math.Abs(data1[i] - data2[i]):X2})");
                    shown++;
                }
            }

            WaitKeyIfNeeded();
            return ExitCode.CompareMismatch;
        }
    }

    static void WaitKeyIfNeeded()
    {
        if (_args.NoWait || _args.JsonOutput)
            return;

        try
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        catch (InvalidOperationException)
        {
            // Console input is redirected (piped/automated) — skip
        }
    }
}
