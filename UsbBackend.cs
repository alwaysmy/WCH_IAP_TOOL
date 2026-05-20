using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WCHIAP.Backend;

// ============================================================================
// Native Interop — CH375 + WinUSB + SetupAPI
// ============================================================================
static class NativeMethods
{
    // CH375 (RevA)
    [DllImport("CH375DLL64.dll", EntryPoint = "CH375OpenDevice", SetLastError = true)]
    public static extern IntPtr CH375OpenDevice(uint iIndex);
    [DllImport("CH375DLL64.dll", EntryPoint = "CH375CloseDevice", SetLastError = true)]
    public static extern void CH375CloseDevice(uint iIndex);
    [DllImport("CH375DLL64.dll", EntryPoint = "CH375ReadData", SetLastError = true)]
    public static extern bool CH375ReadData(uint iIndex, IntPtr oBuffer, ref uint ioLength);
    [DllImport("CH375DLL64.dll", EntryPoint = "CH375WriteData", SetLastError = true)]
    public static extern bool CH375WriteData(uint iIndex, IntPtr iBuffer, ref uint ioLength);

    // SetupAPI
    public static readonly Guid GUID_DEVINTERFACE_USB_DEVICE =
        new(0xA5DCBF10, 0x6530, 0x11D2, 0x90, 0x1F, 0x00, 0xC0, 0x4F, 0xB9, 0x51, 0xED);
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint SPDRP_HARDWAREID = 0x00000001;
    public const uint SPDRP_SERVICE = 0x00000004;

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, ref uint PropertyRegDataType, IntPtr PropertyBuffer, uint PropertyBufferSize, ref uint RequiredSize);
    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

    // Kernel32
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    // WinUSB
    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Initialize(IntPtr DeviceHandle, out IntPtr InterfaceHandle);
    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_Free(IntPtr InterfaceHandle);
    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_WritePipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);
    [DllImport("winusb.dll", SetLastError = true)]
    public static extern bool WinUsb_ReadPipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);
}

[StructLayout(LayoutKind.Sequential)]
struct SP_DEVINFO_DATA
{
    public uint cbSize;
    public Guid ClassGuid;
    public uint DevInst;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential)]
struct SP_DEVICE_INTERFACE_DATA
{
    public int cbSize;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
struct SP_DEVICE_INTERFACE_DETAIL_DATA
{
    public int cbSize;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DevicePath;
}

// ============================================================================
// Backend types
// ============================================================================
enum BackendMode { Auto = 0, Ch375 = 1, WinUsb = 2 }
enum DeviceBackend { Unknown, Ch375, WinUsb }

class UsbDeviceEntry
{
    public uint Index { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Service { get; set; } = "";
    public DeviceBackend Backend { get; set; }
    public IntPtr Handle { get; set; }
    public string VidPidString => $"VID=0x{VendorId:X4}, PID=0x{ProductId:X4}";
}

abstract class IapUsbDevice : IDisposable
{
    public UsbDeviceEntry Info { get; protected set; } = new();
    protected IapUsbDevice(UsbDeviceEntry info) { Info = info; }
    public abstract bool Open();
    public abstract bool WritePipe(byte[] buf, ref uint len);
    public abstract bool ReadPipe(byte[] buf, ref uint len);
    public abstract void Dispose();

    public bool SendCmd(byte cmd)
    {
        byte[] buf = new byte[64]; buf[0] = cmd; uint len = 64;
        if (!WritePipe(buf, ref len)) return false;
        byte[] resp = new byte[64]; len = 64;
        return ReadPipe(resp, ref len) && resp[0] == 0;
    }

    public bool SendData(byte cmd, byte[] data)
    {
        int off = 0;
        while (off < data.Length)
        {
            int sz = Math.Min(62, data.Length - off);
            byte[] buf = new byte[64]; buf[0] = cmd; buf[1] = (byte)sz;
            Array.Copy(data, off, buf, 2, sz);
            uint len = 64;
            if (!WritePipe(buf, ref len)) return false;
            byte[] resp = new byte[64]; len = 64;
            if (!ReadPipe(resp, ref len) || resp[0] != 0) return false;
            off += sz;
        }
        return true;
    }

    public void SendEnd()
    {
        try { byte[] buf = new byte[64]; buf[0] = 0x83; uint len = 64; WritePipe(buf, ref len); } catch { }
    }
}

class Ch375UsbDevice : IapUsbDevice
{
    private uint _index;
    public Ch375UsbDevice(UsbDeviceEntry info) : base(info) { _index = info.Index; }
    public override bool Open()
    {
        IntPtr invalid = new IntPtr(-1);
        for (uint i = 0; i < 16; i++)
        {
            IntPtr h = NativeMethods.CH375OpenDevice(i);
            if (h != IntPtr.Zero && h != invalid)
            {
                _index = i;
                Info.Handle = h;
                Info.Index = i;
                return true;
            }
        }
        return false;
    }
    public override bool WritePipe(byte[] buf, ref uint len)
    {
        IntPtr ptr = Marshal.AllocHGlobal((int)len);
        try { Marshal.Copy(buf, 0, ptr, (int)len); return NativeMethods.CH375WriteData(_index, ptr, ref len); }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    public override bool ReadPipe(byte[] buf, ref uint len)
    {
        IntPtr ptr = Marshal.AllocHGlobal((int)len);
        try { bool ok = NativeMethods.CH375ReadData(_index, ptr, ref len); Marshal.Copy(ptr, buf, 0, (int)len); return ok; }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    public override void Dispose() { NativeMethods.CH375CloseDevice(_index); }
}

class WinUsbDevice : IapUsbDevice
{
    private IntPtr _winUsbHandle = IntPtr.Zero;
    private IntPtr _fileHandle = IntPtr.Zero;
    public WinUsbDevice(UsbDeviceEntry info) : base(info) { }
    public override bool Open()
    {
        uint GENERIC_RW = 0x80000000 | 0x40000000;
        uint FILE_SHARE_RW = 0x00000001 | 0x00000002;
        _fileHandle = NativeMethods.CreateFile(Info.Name, GENERIC_RW, FILE_SHARE_RW, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (_fileHandle == new IntPtr(-1)) { _fileHandle = IntPtr.Zero; return false; }
        if (!NativeMethods.WinUsb_Initialize(_fileHandle, out _winUsbHandle)) { NativeMethods.CloseHandle(_fileHandle); _fileHandle = IntPtr.Zero; return false; }
        Info.Handle = _winUsbHandle;
        return true;
    }
    public override bool WritePipe(byte[] buf, ref uint len) => NativeMethods.WinUsb_WritePipe(_winUsbHandle, 0x02, buf, len, out uint w, IntPtr.Zero) && w == len;
    public override bool ReadPipe(byte[] buf, ref uint len) => NativeMethods.WinUsb_ReadPipe(_winUsbHandle, 0x82, buf, len, out len, IntPtr.Zero);
    public override void Dispose()
    {
        if (_winUsbHandle != IntPtr.Zero) { NativeMethods.WinUsb_Free(_winUsbHandle); _winUsbHandle = IntPtr.Zero; }
        if (_fileHandle != IntPtr.Zero) { NativeMethods.CloseHandle(_fileHandle); _fileHandle = IntPtr.Zero; }
    }
}

// ============================================================================
// Device Search — VID/PID enumeration + Service-based backend detection
// ============================================================================
static class DeviceSearch
{
    static string? ReadDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA devInfo, uint property)
    {
        uint required = 0, regType = 0;
        NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfo, property, ref regType, IntPtr.Zero, 0, ref required);
        if (required == 0) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)required);
        try
        {
            if (NativeMethods.SetupDiGetDeviceRegistryProperty(deviceInfoSet, ref devInfo, property, ref regType, buf, required, ref required))
                return Marshal.PtrToStringAuto(buf) ?? "";
        }
        finally { Marshal.FreeHGlobal(buf); }
        return null;
    }

    static (ushort vid, ushort pid) ParseVidPid(string? hwIds)
    {
        if (string.IsNullOrEmpty(hwIds)) return (0, 0);
        var m = Regex.Match(hwIds, @"VID_([0-9a-fA-F]{4}).*PID_([0-9a-fA-F]{4})", RegexOptions.IgnoreCase);
        if (m.Success) return (Convert.ToUInt16(m.Groups[1].Value, 16), Convert.ToUInt16(m.Groups[2].Value, 16));
        return (0, 0);
    }

    static DeviceBackend DetectBackend(string? service)
    {
        if (string.IsNullOrEmpty(service)) return DeviceBackend.Unknown;
        if (service.Contains("WinUSB", StringComparison.OrdinalIgnoreCase)) return DeviceBackend.WinUsb;
        if (service.Contains("CH375", StringComparison.OrdinalIgnoreCase)) return DeviceBackend.Ch375;
        return DeviceBackend.Unknown;
    }

    public static List<UsbDeviceEntry> SearchDevices(ushort vidFilter, ushort pidFilter, Action<string>? logDebug = null)
    {
        var devices = new List<UsbDeviceEntry>();
        var guid = NativeMethods.GUID_DEVINTERFACE_USB_DEVICE;
        uint DIGCF_DEVICEINTERFACE = 0x00000010;
        IntPtr devInfoSet = NativeMethods.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (devInfoSet == new IntPtr(-1)) return devices;

        try
        {
            SP_DEVICE_INTERFACE_DATA devIfData = new();
            devIfData.cbSize = Marshal.SizeOf(devIfData);
            for (uint i = 0; NativeMethods.SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref guid, i, ref devIfData); i++)
            {
                uint required = 0;
                SP_DEVINFO_DATA dummyInfo = new();
                dummyInfo.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
                NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfoSet, ref devIfData, IntPtr.Zero, 0, ref required, ref dummyInfo);
                if (required == 0) continue;

                IntPtr detailBuf = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);
                    SP_DEVINFO_DATA devInfo = new() { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(devInfoSet, ref devIfData, detailBuf, required, ref required, ref devInfo))
                        continue;

                    string devPath = Marshal.PtrToStringAuto(detailBuf + 4) ?? "";
                    string? hwIds = ReadDeviceProperty(devInfoSet, ref devInfo, NativeMethods.SPDRP_HARDWAREID);
                    string? service = ReadDeviceProperty(devInfoSet, ref devInfo, NativeMethods.SPDRP_SERVICE);

                    var (vid, pid) = ParseVidPid(hwIds);
                    if (vid == 0 && pid == 0) (vid, pid) = ParseVidPid(devPath);
                    if (vid != vidFilter || pid != pidFilter) continue;

                    var backend = DetectBackend(service);
                    if (backend == DeviceBackend.Unknown)
                    {
                        logDebug?.Invoke($"Unknown service '{service ?? "(null)"}' for VID=0x{vid:X4} PID=0x{pid:X4}, defaulting to CH375");
                        backend = DeviceBackend.Ch375;
                    }

                    devices.Add(new UsbDeviceEntry
                    {
                        Index = i,
                        VendorId = vid,
                        ProductId = pid,
                        Name = devPath,
                        Service = service ?? "",
                        Backend = backend,
                    });
                }
                finally { Marshal.FreeHGlobal(detailBuf); }
            }
        }
        finally { NativeMethods.SetupDiDestroyDeviceInfoList(devInfoSet); }

        logDebug?.Invoke($"Search result: {devices.Count} device(s) matched");
        return devices;
    }
}
