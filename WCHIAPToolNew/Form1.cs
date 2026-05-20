using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using WCHIAP.Backend;
using WchHexConverter;

namespace WCHIAPToolNew
{
    public partial class Form1 : Form
    {
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

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private List<DeviceInfo> devices = new List<DeviceInfo>();
        private uint selectedDeviceIndex = 0;
        private IapUsbDevice? _device;
        private BackendMode _backendMode = BackendMode.Auto;
        private System.Windows.Forms.Timer? _pollTimer;
        private int _lastDeviceCount = 0;

        private const byte CMD_IAP_PROM = 0x80;
        private const byte CMD_IAP_ERASE = 0x81;
        private const byte CMD_IAP_VERIFY = 0x82;
        private const byte CMD_IAP_END = 0x83;
        private const byte CMD_JUMP_IAP = 0x84;

        private const byte ERR_SUCCESS = 0x00;
        private const byte ERR_ERROR = 0x01;
        private const byte ERR_End = 0x02;

        // 文件历史记录
        private const string HistoryFileName = "filehistory.txt";
        private const int MaxHistoryCount = 20;
        private List<string> fileHistory = new List<string>();

        public Form1()
        {
            InitializeComponent();
            InitializeUI();
            SetupDragAndDrop();
            LoadFileHistory();
            this.FormClosing += Form1_FormClosing;
        }

        private void InitializeUI()
        {
            this.Text = "WCH IAP Tool";
            this.Size = new Size(800, 600);
            this.MinimumSize = new Size(600, 450);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular);

            CreateDeviceSection();
            CreateDownloadSection();
            CreateLogSection();

            SearchDevices();
            StartDevicePolling();
        }

        private void StartDevicePolling()
        {
            // CH375 使用自定义驱动，WM_DEVICECHANGE 不可靠，
            // 使用定时轮询检测设备插拔
            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 2000;
            _pollTimer.Tick += (s, e) =>
            {
                var tmp = DeviceSearch.SearchDevices(0x4348, 0x55E0);
                int currentCount = tmp.Count;

                if (currentCount != _lastDeviceCount)
                {
                    LogDebug($"设备数量变化: {_lastDeviceCount} -> {currentCount}");
                    _lastDeviceCount = currentCount;
                    SearchDevices();
                }
            };
            _pollTimer.Start();
        }

        private void CreateDeviceSection()
        {
            GroupBox deviceGroup = new GroupBox();
            deviceGroup.Text = "设备管理";
            deviceGroup.Location = new Point(12, 10);
            deviceGroup.Size = new Size(760, 80);
            deviceGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            ComboBox deviceComboBox = new ComboBox();
            deviceComboBox.Name = "deviceComboBox";
            deviceComboBox.Location = new Point(20, 30);
            deviceComboBox.Size = new Size(540, 30);
            deviceComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            deviceComboBox.Font = new Font("Microsoft YaHei", 10F, FontStyle.Regular);
            deviceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;
            deviceGroup.Controls.Add(deviceComboBox);

            Button downloadButton = new Button();
            downloadButton.Name = "downloadButton";
            downloadButton.Text = "下载程序";
            downloadButton.Location = new Point(580, 30);
            downloadButton.Size = new Size(170, 40);
            downloadButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            downloadButton.Click += DownloadButton_Click;
            downloadButton.Enabled = false;
            downloadButton.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            downloadButton.BackColor = Color.LightGray;
            deviceGroup.Controls.Add(downloadButton);

            this.Controls.Add(deviceGroup);
        }

        private void CreateDownloadSection()
        {
            GroupBox downloadGroup = new GroupBox();
            downloadGroup.Text = "程序下载";
            downloadGroup.Location = new Point(12, 100);
            downloadGroup.Size = new Size(760, 120);
            downloadGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Label filePathLabel = new Label();
            filePathLabel.Text = "文件:";
            filePathLabel.Location = new Point(15, 30);
            filePathLabel.Size = new Size(60, 40);
            filePathLabel.TextAlign = ContentAlignment.MiddleCenter;
            downloadGroup.Controls.Add(filePathLabel);

            // 使用ComboBox替代TextBox，支持历史记录下拉选择
            ComboBox filePathComboBox = new ComboBox();
            filePathComboBox.Name = "filePathComboBox";
            filePathComboBox.Location = new Point(80, 30);
            filePathComboBox.Size = new Size(540, 40);
            filePathComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            filePathComboBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
            filePathComboBox.DropDownStyle = ComboBoxStyle.DropDown;  // 允许编辑和下拉
            downloadGroup.Controls.Add(filePathComboBox);

            Button fileSelectButton = new Button();
            fileSelectButton.Text = "选择文件";
            fileSelectButton.Location = new Point(640, 30);
            fileSelectButton.Size = new Size(110, 40);
            fileSelectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            fileSelectButton.Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular);
            fileSelectButton.Click += FileSelectButton_Click;
            downloadGroup.Controls.Add(fileSelectButton);

            Label infoLabel = new Label();
            infoLabel.Name = "infoLabel";
            infoLabel.Text = "支持 .bin 和 .hex 文件，hex文件会自动转换。支持文件拖放。";
            infoLabel.Location = new Point(60, 70);
            infoLabel.Size = new Size(680, 25);
            infoLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            infoLabel.ForeColor = Color.Gray;
            infoLabel.Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular);
            downloadGroup.Controls.Add(infoLabel);

            this.Controls.Add(downloadGroup);
        }

        private void CreateLogSection()
        {
            GroupBox logGroup = new GroupBox();
            logGroup.Text = "日志信息";
            logGroup.Location = new Point(12, 230);
            logGroup.Size = new Size(760, 320);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            TextBox logTextBox = new TextBox();
            logTextBox.Name = "logTextBox";
            logTextBox.Multiline = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.ReadOnly = true;
            logTextBox.Location = new Point(15, 25);
            logTextBox.Size = new Size(730, 280);
            logTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            logTextBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
            logTextBox.BackColor = Color.White;
            logGroup.Controls.Add(logTextBox);

            this.Controls.Add(logGroup);
        }

        // 加载文件历史记录
        private void LoadFileHistory()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string exeDir = Path.GetDirectoryName(exePath) ?? "";
                string filePath = Path.Combine(exeDir, HistoryFileName);

                fileHistory.Clear();

                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (!string.IsNullOrEmpty(trimmedLine) && File.Exists(trimmedLine))
                        {
                            // 避免重复
                            if (!fileHistory.Contains(trimmedLine))
                            {
                                fileHistory.Add(trimmedLine);
                            }
                        }
                    }
                }

                // 填充到ComboBox
                ComboBox filePathComboBox = this.Controls.Find("filePathComboBox", true).FirstOrDefault() as ComboBox;
                if (filePathComboBox != null)
                {
                    filePathComboBox.Items.Clear();
                    foreach (string path in fileHistory)
                    {
                        filePathComboBox.Items.Add(path);
                    }

                    // 自动选择最后一个（最后使用的）
                    if (filePathComboBox.Items.Count > 0)
                    {
                        filePathComboBox.SelectedIndex = filePathComboBox.Items.Count - 1;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"加载文件历史记录失败: {ex.Message}");
            }
        }

        // 保存文件历史记录
        private void SaveFileHistory(string newPath)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath) || !File.Exists(newPath))
                    return;

                // 如果已存在，先移除（移到末尾）
                fileHistory.Remove(newPath);
                
                // 添加到末尾
                fileHistory.Add(newPath);

                // 如果超出最大数量，移除最早的
                while (fileHistory.Count > MaxHistoryCount)
                {
                    fileHistory.RemoveAt(0);
                }

                // 写入文件
                string exePath = Application.ExecutablePath;
                string exeDir = Path.GetDirectoryName(exePath) ?? "";
                string filePath = Path.Combine(exeDir, HistoryFileName);
                File.WriteAllLines(filePath, fileHistory);
            }
            catch (Exception ex)
            {
                LogDebug($"保存文件历史记录失败: {ex.Message}");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 保存当前路径到历史记录
            ComboBox filePathComboBox = this.Controls.Find("filePathComboBox", true).FirstOrDefault() as ComboBox;
            if (filePathComboBox != null && !string.IsNullOrEmpty(filePathComboBox.Text))
            {
                SaveFileHistory(filePathComboBox.Text);
            }
        }

        private void SetupDragAndDrop()
        {
            this.AllowDrop = true;
            this.DragEnter += Form1_DragEnter;
            this.DragDrop += Form1_DragDrop;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int eventType = m.WParam.ToInt32();
                if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE ||
                    eventType == DBT_DEVNODES_CHANGED)
                {
                    LogDebug($"WM_DEVICECHANGE: 0x{eventType:X4}");
                    // 延迟搜索，等待 USB 栈完成设备枚举
                    BeginInvoke(() => { SearchDevices(); });
                    return;
                }
            }

            base.WndProc(ref m);
        }

        private void DeviceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateDownloadButtonState();
        }

        private void UpdateDownloadButtonState()
        {
            Button downloadButton = this.Controls.Find("downloadButton", true).FirstOrDefault() as Button;
            ComboBox deviceComboBox = this.Controls.Find("deviceComboBox", true).FirstOrDefault() as ComboBox;

            if (downloadButton == null || deviceComboBox == null)
                return;

            if (devices.Count == 0 || deviceComboBox.SelectedIndex < 0)
            {
                downloadButton.Enabled = false;
                downloadButton.BackColor = Color.LightGray;
            }
            else
            {
                downloadButton.Enabled = true;
                downloadButton.BackColor = Color.LightGreen;
            }
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                ComboBox filePathComboBox = this.Controls.Find("filePathComboBox", true).FirstOrDefault() as ComboBox;
                if (filePathComboBox != null)
                {
                    filePathComboBox.Text = files[0];
                    LogMessage($"文件已拖放: {files[0]}");
                }
            }
        }

        private void FileSelectButton_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Bin files (*.bin)|*.bin|Hex files (*.hex)|*.hex|All files (*.*)|*.*";
            openFileDialog.Title = "选择下载文件";
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                ComboBox filePathComboBox = this.Controls.Find("filePathComboBox", true).FirstOrDefault() as ComboBox;
                if (filePathComboBox != null)
                {
                    string selectedPath = openFileDialog.FileName;
                    filePathComboBox.Text = selectedPath;
                    
                    // 添加到历史记录
                    if (!fileHistory.Contains(selectedPath))
                    {
                        fileHistory.Add(selectedPath);
                        filePathComboBox.Items.Add(selectedPath);
                    }
                    
                    LogMessage($"文件已选择: {selectedPath}");
                }
            }
        }

        private void DownloadButton_Click(object sender, EventArgs e)
        {
            DownloadProgram();
        }

        private void SearchDevices()
        {
            LogDebug("SearchDevices() 被调用");
            devices.Clear();
            ComboBox deviceComboBox = this.Controls.Find("deviceComboBox", true).FirstOrDefault() as ComboBox;
            if (deviceComboBox == null) return;

            deviceComboBox.Items.Clear();

            try
            {
                devices.Clear();
                var usbDevs = DeviceSearch.SearchDevices(0x4348, 0x55E0, msg => LogDebug(msg));
                foreach (var d in usbDevs)
                {
                    devices.Add(new DeviceInfo { Index = d.Index, VendorId = d.VendorId, ProductId = d.ProductId, Name = d.Name });
                    deviceComboBox.Items.Add($"设备 {d.Index} [{d.Backend}]");
                }
                int foundCount = devices.Count;
                LogDebug($"DeviceSearch found: {foundCount}");
                LogDebug($"共找到 {foundCount} 个有效设备");

                if (deviceComboBox.Items.Count > 0)
                {
                    deviceComboBox.SelectedIndex = 0;
                }
                else
                {
                    deviceComboBox.Items.Add("未检测到设备");
                    deviceComboBox.SelectedIndex = 0;
                    deviceComboBox.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"搜索设备时出错: {ex.Message}");
                deviceComboBox.Items.Add("未检测到设备");
                deviceComboBox.SelectedIndex = 0;
                deviceComboBox.Enabled = false;
            }

            UpdateDownloadButtonState();
        }

        private void DownloadProgram()
        {
            ComboBox filePathComboBox = this.Controls.Find("filePathComboBox", true).FirstOrDefault() as ComboBox;
            ComboBox deviceComboBox = this.Controls.Find("deviceComboBox", true).FirstOrDefault() as ComboBox;

            if (filePathComboBox == null || deviceComboBox == null)
            {
                LogMessage("控件未找到");
                return;
            }

            string filePath = filePathComboBox.Text;

            if (string.IsNullOrEmpty(filePath))
            {
                LogMessage("请选择下载文件");
                return;
            }

            if (!File.Exists(filePath))
            {
                LogMessage("文件不存在");
                return;
            }

            if (deviceComboBox.SelectedIndex < 0 || devices.Count == 0)
            {
                LogMessage("请选择设备");
                return;
            }

            try
            {
                selectedDeviceIndex = (uint)deviceComboBox.SelectedIndex;

                LogMessage($"开始下载程序...");
                LogMessage($"文件路径: {filePath}");
                
                // 保存到历史记录
                SaveFileHistory(filePath);
                
                if (selectedDeviceIndex < devices.Count)
                {
                    var device = devices[(int)selectedDeviceIndex];
                    LogMessage($"选择的设备: 设备 {device.Index}, VID={device.VendorId:X4}, PID={device.ProductId:X4}");
                }

                byte[] fileContent;
                if (filePath.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
                {
                    LogMessage("检测到HEX文件，正在转换...");
                    var hexResult = WchHexToBinConverter.ConvertHexToBin(filePath);
                    fileContent = hexResult.Data;
                    LogMessage($"HEX转换完成，BIN大小: {fileContent.Length} 字节");
                    if (hexResult.StartAddress > 0)
                    {
                        LogMessage($"起始地址: 0x{hexResult.StartAddress:X8}");
                    }
                }
                else
                {
                    fileContent = File.ReadAllBytes(filePath);
                    LogMessage($"文件大小: {fileContent.Length} 字节");
                }

                // Re-search to get full device info (backend + path)
                var usbList = DeviceSearch.SearchDevices(0x4348, 0x55E0);
                var devEntry = usbList.Count > 0 ? usbList[0] : new UsbDeviceEntry { Name = "" };
                var backend = _backendMode == BackendMode.Auto ? devEntry.Backend
                    : (_backendMode == BackendMode.WinUsb ? DeviceBackend.WinUsb : DeviceBackend.Ch375);
                LogMessage($"后端: {backend}");

                _device = backend == DeviceBackend.WinUsb ? new WinUsbDevice(devEntry) : new Ch375UsbDevice(devEntry);
                if (!_device.Open()) { LogMessage("无法打开设备"); return; }

                if (!_device.SendCmd(CMD_IAP_ERASE)) { LogMessage("擦除失败"); return; }
                if (!_device.SendData(CMD_IAP_PROM, fileContent)) { LogMessage("编程失败"); return; }
                if (!_device.SendData(CMD_IAP_VERIFY, fileContent)) { LogMessage("验证失败"); return; }
                _device.SendEnd();

                LogMessage("下载完成");
            }
            catch (Exception ex)
            {
                LogMessage($"下载时出错: {ex.Message}");
            }
        }

        private bool SendEraseCommand()
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
                LogMessage($"发送擦除命令时出错: {ex.Message}");
                return false;
            }
        }

        private bool SendProgramData(byte[] fileContent)
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
                        LogMessage($"已发送 {offset} / {fileContent.Length} 字节");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"发送程序数据时出错: {ex.Message}");
                return false;
            }
        }

        private bool SendVerifyCommand(byte[] fileContent)
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
                LogMessage($"发送验证命令时出错: {ex.Message}");
                return false;
            }
        }

        private bool SendEndCommand()
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
                    LogMessage("发送结束命令失败");
                    return false;
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
                        LogMessage("单片机响应成功");
                    }
                    else
                    {
                        LogMessage($"单片机响应: 0x{response:X2}");
                    }
                }
                else
                {
                    Marshal.FreeHGlobal(bufferPtr);
                    LogMessage("设备已断开(正常，单片机已跳转到APP)");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"设备已断开: {ex.Message}");
                return true;
            }
        }

        private void LogMessage(string message)
        {
            TextBox logTextBox = this.Controls.Find("logTextBox", true).FirstOrDefault() as TextBox;
            if (logTextBox != null)
            {
                logTextBox.AppendText($"[{DateTime.Now.ToString("HH:mm:ss")}] {message}\r\n");
                logTextBox.ScrollToCaret();
            }
        }

        private void LogDebug(string message)
        {
            if (Program.DebugMode)
            {
                LogMessage($"[DEBUG] {message}");
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
