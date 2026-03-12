using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using WchHexConverter;

namespace WCHIAPToolCLI
{
    class Program
    {
        // CH375 DLL Import
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

        // IAP Command Definitions (Correct values from iap.h)
        private const byte CMD_IAP_PROM = 0x80;
        private const byte CMD_IAP_ERASE = 0x81;
        private const byte CMD_IAP_VERIFY = 0x82;
        private const byte CMD_IAP_END = 0x83;
        private const byte CMD_JUMP_IAP = 0x84;

        // Error Codes
        private const byte ERR_SUCCESS = 0x00;
        private const byte ERR_ERROR = 0x01;
        private const byte ERR_End = 0x02;

        private static uint selectedDeviceIndex = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("WCH IAP Tool CLI");
            Console.WriteLine("==================");
            Console.WriteLine();

            // Parse command line arguments
            if (args.Length > 0)
            {
                if (args[0] == "--test-hex" && args.Length > 1)
                {
                    TestHexConversion(args[1]);
                    return;
                }
                else if (args[0] == "--compare-bin" && args.Length > 2)
                {
                    CompareBinFiles(args[1], args[2]);
                    return;
                }
                else if (args[0] == "--help")
                {
                    PrintHelp();
                    return;
                }
            }

            try
            {
                uint version = CH375GetVersion();
                Console.WriteLine($"CH375 DLL Version: {version}");
                Console.WriteLine();

                Console.WriteLine("1. 搜索设备...");
                var devices = SearchDevices();

                if (devices.Count == 0)
                {
                    Console.WriteLine("未找到设备，按任意键退出...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine($"共找到 {devices.Count} 个设备");
                Console.WriteLine();

                // Use first device
                selectedDeviceIndex = devices[0].Index;
                Console.WriteLine($"选择设备: 索引={selectedDeviceIndex}, VID={devices[0].VendorId:X4}, PID={devices[0].ProductId:X4}");
                Console.WriteLine();

                // Test hex file conversion
                string hexFile = "CH32V30x_ft2232h_XilinxCable.hex";
                if (!File.Exists(hexFile))
                {
                    Console.WriteLine($"HEX文件不存在: {hexFile}");
                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine("2. 读取下载文件内容>>");
                Console.WriteLine("   HEX格式转换成BIN格式...");
                var hexResult = WchHexToBinConverter.ConvertHexToBin(hexFile);
                Console.WriteLine($"   已读取下载数据{hexResult.Data.Length}字节");
                Console.WriteLine($"   起始地址: 0x{hexResult.StartAddress:X8}");
                Console.WriteLine();

                // Open device
                Console.WriteLine("3. 打开下载接口>>");
                IntPtr handle = CH375OpenDevice(selectedDeviceIndex);
                if (handle == IntPtr.Zero)
                {
                    Console.WriteLine("   打开失败");
                    return;
                }
                Console.WriteLine("   打开成功");
                Console.WriteLine();

                // Erase
                Console.WriteLine("4. 擦除FLASH>>");
                if (!SendEraseCommand())
                {
                    Console.WriteLine("   失败");
                    CH375CloseDevice(selectedDeviceIndex);
                    return;
                }
                Console.WriteLine("   成功");
                Console.WriteLine();

                // Program
                Console.WriteLine("5. 写FLASH>>");
                if (!SendProgramData(hexResult.Data))
                {
                    Console.WriteLine("   失败");
                    CH375CloseDevice(selectedDeviceIndex);
                    return;
                }
                Console.WriteLine("   成功");
                Console.WriteLine();

                // Verify
                Console.WriteLine("6. Flash检验>>");
                if (!SendVerifyCommand(hexResult.Data))
                {
                    Console.WriteLine("   失败");
                    CH375CloseDevice(selectedDeviceIndex);
                    return;
                }
                Console.WriteLine("   成功");
                Console.WriteLine();

                // End
                Console.WriteLine("7. 下载结束>>");
                SendEndCommand();
                Console.WriteLine("   设置成功");
                Console.WriteLine();

                // Close
                Console.WriteLine("8. 关闭下载接口>>");
                try { CH375CloseDevice(selectedDeviceIndex); } catch { }
                Console.WriteLine("   已关闭。");
                Console.WriteLine();

                Console.WriteLine("IAP下载成功.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Error Code: {Marshal.GetLastWin32Error()}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void PrintHelp()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  WCHIAPToolCLI                           - 执行IAP下载");
            Console.WriteLine("  WCHIAPToolCLI --test-hex <file>         - 测试HEX文件转换");
            Console.WriteLine("  WCHIAPToolCLI --compare-bin <file1> <file2> - 对比两个BIN文件");
            Console.WriteLine("  WCHIAPToolCLI --help                    - 显示帮助信息");
        }

        static void TestHexConversion(string hexFile)
        {
            Console.WriteLine($"测试HEX文件转换: {hexFile}");
            Console.WriteLine();

            if (!File.Exists(hexFile))
            {
                Console.WriteLine($"文件不存在: {hexFile}");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(hexFile);
                Console.WriteLine($"HEX文件行数: {lines.Length}");
                Console.WriteLine();

                Console.WriteLine("前10行解析:");
                int lineCount = 0;
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
                        continue;

                    var data = WchHexToBinConverter.ParseHexLine(line);
                    if (data == null) continue;

                    byte recordType = data[3];
                    string recordTypeName = recordType switch
                    {
                        0x00 => "Data",
                        0x01 => "EOF",
                        0x02 => "ExtSegAddr",
                        0x03 => "StartSegAddr",
                        0x04 => "ExtLinAddr",
                        0x05 => "StartLinAddr",
                        _ => $"Unknown({recordType:X2})"
                    };

                    Console.WriteLine($"  Line {lineCount + 1}: Type={recordTypeName}, Len={data[0]}, Addr=0x{(data[1] << 8 | data[2]):X4}");

                    if (recordType == 0x00 && lineCount < 3)
                    {
                        Console.Write("    Data: ");
                        for (int i = 0; i < Math.Min(16, (int)data[0]); i++)
                        {
                            Console.Write($"{data[4 + i]:X2} ");
                        }
                        Console.WriteLine();
                    }

                    lineCount++;
                    if (lineCount >= 10) break;
                }

                Console.WriteLine();
                Console.WriteLine("最后5行解析:");
                var lastLines = new List<string>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && line[0] == ':')
                        lastLines.Add(line);
                }

                for (int i = Math.Max(0, lastLines.Count - 5); i < lastLines.Count; i++)
                {
                    var data = WchHexToBinConverter.ParseHexLine(lastLines[i]);
                    if (data == null) continue;

                    byte recordType = data[3];
                    string recordTypeName = recordType switch
                    {
                        0x00 => "Data",
                        0x01 => "EOF",
                        0x02 => "ExtSegAddr",
                        0x03 => "StartSegAddr",
                        0x04 => "ExtLinAddr",
                        0x05 => "StartLinAddr",
                        _ => $"Unknown({recordType:X2})"
                    };

                    Console.WriteLine($"  Type={recordTypeName}, Len={data[0]}, Addr=0x{(data[1] << 8 | data[2]):X4}");
                    if (recordType == 0x03)
                    {
                        Console.Write("    CS:IP = ");
                        Console.WriteLine($"0x{data[4]:X2}{data[5]:X2}:0x{data[6]:X2}{data[7]:X2}");
                    }
                }

                Console.WriteLine();
                var hexResult = WchHexToBinConverter.ConvertHexToBin(hexFile);

                Console.WriteLine($"转换成功!");
                Console.WriteLine($"  数据大小: {hexResult.Data.Length} 字节");
                Console.WriteLine($"  起始地址: 0x{hexResult.StartAddress:X8}");
                Console.WriteLine($"  结束地址: 0x{hexResult.StartAddress + (uint)hexResult.Data.Length:X8}");
                Console.WriteLine();

                Console.WriteLine("前64字节数据:");
                int showLen = Math.Min(64, hexResult.Data.Length);
                for (int i = 0; i < showLen; i++)
                {
                    Console.Write($"{hexResult.Data[i]:X2} ");
                    if ((i + 1) % 16 == 0) Console.WriteLine();
                }
                Console.WriteLine();

                string binFile = Path.ChangeExtension(hexFile, ".test.bin");
                File.WriteAllBytes(binFile, hexResult.Data);
                Console.WriteLine($"已保存BIN文件: {binFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换失败: {ex.Message}");
            }
        }

        static void CompareBinFiles(string file1, string file2)
        {
            Console.WriteLine($"对比BIN文件:");
            Console.WriteLine($"  文件1: {file1}");
            Console.WriteLine($"  文件2: {file2}");
            Console.WriteLine();

            if (!File.Exists(file1))
            {
                Console.WriteLine($"文件不存在: {file1}");
                return;
            }
            if (!File.Exists(file2))
            {
                Console.WriteLine($"文件不存在: {file2}");
                return;
            }

            byte[] data1 = File.ReadAllBytes(file1);
            byte[] data2 = File.ReadAllBytes(file2);

            Console.WriteLine($"文件1大小: {data1.Length} 字节");
            Console.WriteLine($"文件2大小: {data2.Length} 字节");
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
                Console.WriteLine($"文件大小不同，差异: {Math.Abs(data1.Length - data2.Length)} 字节");
            }

            if (diffCount == 0 && data1.Length == data2.Length)
            {
                Console.WriteLine("文件完全相同!");
            }
            else
            {
                Console.WriteLine($"发现 {diffCount} 处数据差异");

                if (firstDiff >= 0)
                {
                    Console.WriteLine();
                    Console.WriteLine($"首次差异位置: 0x{firstDiff:X4} ({firstDiff})");
                    Console.WriteLine($"文件1数据: {data1[firstDiff]:X2}");
                    Console.WriteLine($"文件2数据: {data2[firstDiff]:X2}");

                    Console.WriteLine();
                    Console.WriteLine("差异位置附近数据对比:");
                    int start = Math.Max(0, firstDiff - 8);
                    int end = Math.Min(minLen, firstDiff + 8);

                    Console.Write("文件1: ");
                    for (int i = start; i < end; i++)
                    {
                        Console.Write($"{data1[i]:X2} ");
                    }
                    Console.WriteLine();

                    Console.Write("文件2: ");
                    for (int i = start; i < end; i++)
                    {
                        Console.Write($"{data2[i]:X2} ");
                    }
                    Console.WriteLine();

                    Console.Write("       ");
                    for (int i = start; i < end; i++)
                    {
                        Console.Write(data1[i] != data2[i] ? "^^ " : "   ");
                    }
                    Console.WriteLine();
                }

                Console.WriteLine();
                Console.WriteLine("前10处差异:");
                int shown = 0;
                for (int i = 0; i < minLen && shown < 10; i++)
                {
                    if (data1[i] != data2[i])
                    {
                        Console.WriteLine($"  偏移 0x{i:X4}: 文件1={data1[i]:X2}, 文件2={data2[i]:X2}, 差异={Math.Abs(data1[i] - data2[i]):X2}");
                        shown++;
                    }
                }
            }
        }

        static List<DeviceInfo> SearchDevices()
        {
            var devices = new List<DeviceInfo>();

            for (uint i = 0; i < 16; i++)
            {
                try
                {
                    IntPtr handle = CH375OpenDevice(i);
                    if (handle != IntPtr.Zero && handle.ToInt32() != -1)
                    {
                        uint usbId = CH375GetUsbID(i);
                        ushort vendorId = (ushort)(usbId >> 16);
                        ushort productId = (ushort)(usbId & 0xFFFF);
                        IntPtr deviceNamePtr = CH375GetDeviceName(i);
                        string deviceName = Marshal.PtrToStringAnsi(deviceNamePtr);

                        if (vendorId != 0 && productId != 0 && !string.IsNullOrEmpty(deviceName))
                        {
                            var device = new DeviceInfo
                            {
                                Index = i,
                                VendorId = vendorId,
                                ProductId = productId,
                                Name = deviceName
                            };
                            devices.Add(device);

                            Console.WriteLine($"   Device {i}: VID=0x{vendorId:X4}, PID=0x{productId:X4}, {deviceName}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   Error checking device {i}: {ex.Message}");
                }
            }

            return devices;
        }

        static bool SendEraseCommand()
        {
            try
            {
                byte[] cmdBuffer = new byte[64];
                cmdBuffer[0] = CMD_IAP_ERASE;
                cmdBuffer[1] = 0x00;

                uint length = (uint)cmdBuffer.Length;
                IntPtr bufferPtr = Marshal.AllocHGlobal((int)length);
                Marshal.Copy(cmdBuffer, 0, bufferPtr, (int)length);

                bool result = CH375WriteData(selectedDeviceIndex, bufferPtr, ref length);
                Marshal.FreeHGlobal(bufferPtr);

                if (!result) return false;

                byte[] responseBuffer = new byte[64];
                length = (uint)responseBuffer.Length;
                bufferPtr = Marshal.AllocHGlobal((int)length);
                result = CH375ReadData(selectedDeviceIndex, bufferPtr, ref length);
                Marshal.Copy(bufferPtr, responseBuffer, 0, (int)length);
                Marshal.FreeHGlobal(bufferPtr);

                return result && responseBuffer[0] == ERR_SUCCESS;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   擦除命令出错: {ex.Message}");
                return false;
            }
        }

        static bool SendProgramData(byte[] fileContent)
        {
            try
            {
                int offset = 0;
                while (offset < fileContent.Length)
                {
                    int chunkSize = Math.Min(62, fileContent.Length - offset);

                    byte[] cmdBuffer = new byte[64];
                    cmdBuffer[0] = CMD_IAP_PROM;
                    cmdBuffer[1] = (byte)chunkSize;
                    Array.Copy(fileContent, offset, cmdBuffer, 2, chunkSize);

                    uint length = (uint)cmdBuffer.Length;
                    IntPtr bufferPtr = Marshal.AllocHGlobal((int)length);
                    Marshal.Copy(cmdBuffer, 0, bufferPtr, (int)length);

                    bool result = CH375WriteData(selectedDeviceIndex, bufferPtr, ref length);
                    Marshal.FreeHGlobal(bufferPtr);

                    if (!result) return false;

                    byte[] responseBuffer = new byte[64];
                    length = (uint)responseBuffer.Length;
                    bufferPtr = Marshal.AllocHGlobal((int)length);
                    result = CH375ReadData(selectedDeviceIndex, bufferPtr, ref length);
                    Marshal.Copy(bufferPtr, responseBuffer, 0, (int)length);
                    Marshal.FreeHGlobal(bufferPtr);

                    if (!result || responseBuffer[0] != ERR_SUCCESS) return false;

                    offset += chunkSize;
                    if (offset % 4096 == 0 || offset == fileContent.Length)
                    {
                        Console.WriteLine($"   已写入 {offset} / {fileContent.Length} 字节");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   写入数据出错: {ex.Message}");
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
                    int chunkSize = Math.Min(62, fileContent.Length - offset);

                    byte[] cmdBuffer = new byte[64];
                    cmdBuffer[0] = CMD_IAP_VERIFY;
                    cmdBuffer[1] = (byte)chunkSize;
                    Array.Copy(fileContent, offset, cmdBuffer, 2, chunkSize);

                    uint length = (uint)cmdBuffer.Length;
                    IntPtr bufferPtr = Marshal.AllocHGlobal((int)length);
                    Marshal.Copy(cmdBuffer, 0, bufferPtr, (int)length);

                    bool result = CH375WriteData(selectedDeviceIndex, bufferPtr, ref length);
                    Marshal.FreeHGlobal(bufferPtr);

                    if (!result) return false;

                    byte[] responseBuffer = new byte[64];
                    length = (uint)responseBuffer.Length;
                    bufferPtr = Marshal.AllocHGlobal((int)length);
                    result = CH375ReadData(selectedDeviceIndex, bufferPtr, ref length);
                    Marshal.Copy(bufferPtr, responseBuffer, 0, (int)length);
                    Marshal.FreeHGlobal(bufferPtr);

                    if (!result || responseBuffer[0] != ERR_SUCCESS) return false;

                    offset += chunkSize;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   验证出错: {ex.Message}");
                return false;
            }
        }

        static void SendEndCommand()
        {
            try
            {
                byte[] cmdBuffer = new byte[64];
                cmdBuffer[0] = CMD_IAP_END;
                cmdBuffer[1] = 0x00;

                uint length = (uint)cmdBuffer.Length;
                IntPtr bufferPtr = Marshal.AllocHGlobal((int)length);
                Marshal.Copy(cmdBuffer, 0, bufferPtr, (int)length);

                bool result = CH375WriteData(selectedDeviceIndex, bufferPtr, ref length);
                Marshal.FreeHGlobal(bufferPtr);

                if (!result)
                {
                    Console.WriteLine("   发送结束命令失败");
                    return;
                }

                byte[] responseBuffer = new byte[64];
                length = (uint)responseBuffer.Length;
                bufferPtr = Marshal.AllocHGlobal((int)length);
                result = CH375ReadData(selectedDeviceIndex, bufferPtr, ref length);

                if (result)
                {
                    Marshal.Copy(bufferPtr, responseBuffer, 0, (int)length);
                    byte response = responseBuffer[0];
                    Marshal.FreeHGlobal(bufferPtr);

                    if (response == ERR_SUCCESS || response == ERR_End)
                    {
                        Console.WriteLine("   单片机响应成功");
                    }
                    else
                    {
                        Console.WriteLine($"   单片机响应: 0x{response:X2}");
                    }
                }
                else
                {
                    Marshal.FreeHGlobal(bufferPtr);
                    Console.WriteLine("   设备已断开(正常，单片机已跳转到APP)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   设备已断开: {ex.Message}");
            }
        }
    }

    public class DeviceInfo
    {
        public uint Index { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public string Name { get; set; }
    }
}
