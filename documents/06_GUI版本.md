# GUI版本详细说明

## 1. 功能概述

GUI版本提供图形界面的IAP下载工具，具有以下特点：
- **WinUSB + CH375 双后端**：自动检测驱动类型，兼容 RevA/RevB 固件
- 自动检测USB设备（SetupDi 枚举 + Service 注册表判定）
- 支持文件拖放、HEX/BIN 自动转换
- 实时日志显示
- **异步下载**：不阻塞界面

## 2. 界面布局

```
┌─────────────────────────────────────────────────────────────┐
│ 设备管理                                                     │
│ ┌─────────────────────────────────────┐ ┌─────────────────┐ │
│ │ 设备列表                             │ │ 未检测到设备    │ │
│ │ 设备 0: VID=4348, PID=55E0, ...     │ │                 │ │
│ │                                     │ │ [下载程序]      │ │
│ └─────────────────────────────────────┘ └─────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│ 程序下载                                                     │
│ 文件路径: [________________________] [选择文件]              │
│ 支持 .bin 和 .hex 文件，hex文件会自动转换                    │
├─────────────────────────────────────────────────────────────┤
│ 日志信息                                                     │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [22:10:58] 设备节点变化事件                              │ │
│ │ [22:10:58] SearchDevices() 被调用                       │ │
│ │ [22:10:58] 共找到 1 个有效设备                           │ │
│ │ ...                                                     │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## 3. 功能区域

### 3.1 设备管理区

| 控件 | 说明 |
|------|------|
| 设备列表 | 显示所有检测到的设备 |
| 状态标签 | 显示设备连接状态 |
| 下载按钮 | 执行程序下载 |

### 3.2 程序下载区

| 控件 | 说明 |
|------|------|
| 文件路径输入框 | 显示选择的文件路径 |
| 选择文件按钮 | 打开文件选择对话框 |
| 提示标签 | 显示支持的文件格式 |

### 3.3 日志区

| 控件 | 说明 |
|------|------|
| 日志文本框 | 显示操作日志，带时间戳 |

## 4. 使用方法

### 4.1 选择设备

1. 程序启动时自动搜索设备
2. 设备插入/拔出时自动更新列表
3. 点击列表选择目标设备
4. 状态标签显示"设备已就绪"

### 4.2 选择文件

**方法一：点击选择**
1. 点击"选择文件"按钮
2. 在对话框中选择文件
3. 支持 .bin 和 .hex 文件

**方法二：拖放文件**
1. 从资源管理器拖动文件到窗口
2. 文件路径自动填入

### 4.3 开始下载

1. 确保设备已选择
2. 确保文件已选择
3. 点击"下载程序"按钮
4. 观察日志输出

## 5. 自动设备检测

### 5.1 WinUSB + CH375 双后端

GUI 通过共享文件 `UsbBackend.cs` 实现双后端抽象：

- `IapUsbDevice` 抽象类：`Open()` / `WritePipe()` / `ReadPipe()` / `Dispose()` + `SendCmd()` / `SendData()` / `SendEnd()`
- `Ch375UsbDevice`：CH375DLL64.dll 后端（RevA 兼容）
- `WinUsbDevice`：WinUSB API 后端（RevB）

**默认 `--auto` 模式**：通过 SetupDi 读 Service 注册表值判定后端（`WINUSB` → WinUSB, `CH375*` → CH375）。下拉框显示 `[WinUsb]` / `[Ch375]` 标签。

### 5.2 检测机制

GUI 版本采用**三层检测**策略确保设备插拔及时响应：

| 层 | 机制 | 说明 |
|----|------|------|
| 定时轮询 | 每 2 秒扫描设备数量，变化时刷新 | 主力，不漏任何变化 |
| WM_DEVICECHANGE | Windows 消息触发 | 辅助，快速响应 |
| BeginInvoke | 延迟到消息队列处理后执行 | 等待 USB 栈枚举完成 |

代码实现：

```csharp
// 1. 定时轮询：窗体启动时开启
private void StartDevicePolling()
{
    _pollTimer = new System.Windows.Forms.Timer();
    _pollTimer.Interval = 2000;
    _pollTimer.Tick += (s, e) =>
    {
        int currentCount = 0;
        for (uint i = 0; i < 16; i++)
        {
            IntPtr h = CH375OpenDevice(i);
            bool valid = h != IntPtr.Zero && h.ToInt32() != -1;
            if (h != IntPtr.Zero && h.ToInt32() != -1)
                CH375CloseDevice(i);
            if (valid) currentCount++;
        }
        if (currentCount != _lastDeviceCount)
        {
            _lastDeviceCount = currentCount;
            SearchDevices();
        }
    };
    _pollTimer.Start();
}

// 2. WM_DEVICECHANGE：消息触发
protected override void WndProc(ref Message m)
{
    if (m.Msg == WM_DEVICECHANGE)
    {
        int eventType = m.WParam.ToInt32();
        if (eventType == DBT_DEVICEARRIVAL ||
            eventType == DBT_DEVICEREMOVECOMPLETE ||
            eventType == DBT_DEVNODES_CHANGED)
        {
            LogDebug($"WM_DEVICECHANGE: 0x{eventType:X4}");
            // 3. BeginInvoke 延迟搜索，等待 USB 栈完成
            BeginInvoke(() => { SearchDevices(); });
            return;
        }
    }
    base.WndProc(ref m);
}
```

### 5.2 状态更新

| 状态 | 条件 | 颜色 |
|------|------|------|
| 未检测到设备 | devices.Count == 0 | 灰色 |
| 已检测到 X 个设备，请选择 | 有设备但未选择 | 蓝色 |
| 设备已就绪 | 已选择设备 | 绿色 |

## 6. 下载流程

### 6.1 流程步骤

**异步执行**：`DownloadButton_Click` → `async void`, `await Task.Run(DownloadProgram)` 后台线程下载。`LogMessage` 通过 `InvokeRequired` + `BeginInvoke` 线程安全写 UI。下载前暂停轮询定时器防竞态。

```
1. 提取 UI 参数（文件路径、选定设备）→ 副本
2. Task.Run(DownloadProgram) 后台线程
3. HEX→BIN 转换 → DeviceSearch 获取后端 → IapUsbDevice.Open
4. SendCmd(ERASE) → SendData(PROM) → SendData(VERIFY) → SendEnd
5. finally: 恢复定时器 + 启用按钮
```

### 6.2 日志输出示例

```
[22:15:30] 开始下载程序...
[22:15:30] 文件路径: D:\firmware.hex
[22:15:30] 选择的设备: 设备 0: VID=4348, PID=55E0, ...
[22:15:30] 检测到HEX文件，正在转换...
[22:15:30] HEX转换完成，BIN大小: 24684 字节
[22:15:30] 起始地址: 0x00005000
[22:15:30] 发送擦除命令...
[22:15:30] 发送程序数据...
[22:15:31] 已发送 4096 / 24684 字节
[22:15:31] 已发送 8192 / 24684 字节
...
[22:15:32] 已发送 24684 / 24684 字节
[22:15:32] 发送验证命令...
[22:15:32] 发送结束命令...
[22:15:32] 设备已断开(正常，单片机已跳转到APP)
[22:15:32] 下载完成
```

## 7. 代码结构

### 7.1 主要方法

| 方法 / 类型 | 说明 |
|------|------|
| `IapUsbDevice` (UsbBackend.cs) | USB 后端抽象基类，`SendCmd/SendData/SendEnd` |
| `Ch375UsbDevice` / `WinUsbDevice` | CH375 / WinUSB 具体实现 |
| `DeviceSearch.SearchDevices()` | SetupDi 枚举 + Service 判定 |
| `InitializeUI()` | 初始化界面 |
| `StartDevicePolling()` | 设备轮询（下载时暂停） |
| `SearchDevices()` | GUI 设备搜索（调用 DeviceSearch） |
| `DownloadProgram(string, UsbDeviceEntry)` | 后台下载（参数传入，不碰 UI） |
| `DownloadButton_Click()` | async void 入口，提取参数 + Task.Run |
| `LogMessage()` / `LogDebug()` | InvokeRequired 线程安全写日志 |

### 7.2 控件命名

| 控件 | Name属性 |
|------|----------|
| 设备列表 | deviceList |
| 状态标签 | statusLabel |
| 下载按钮 | downloadButton |
| 文件路径输入框 | filePathTextBox |
| 日志文本框 | logTextBox |

## 8. 文件拖放实现

### 8.1 启用拖放

```csharp
private void SetupDragAndDrop()
{
    this.AllowDrop = true;
    this.DragEnter += Form1_DragEnter;
    this.DragDrop += Form1_DragDrop;
}
```

### 8.2 处理拖放事件

```csharp
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
        filePathTextBox.Text = files[0];
        LogMessage($"文件已拖放: {files[0]}");
    }
}
```

## 9. 调试模式

### 9.1 启用调试输出

使用 `--debug` 或 `-d` 参数启动程序，日志中显示调试信息：

```
.\WCHIAPToolNew.exe --debug
```

调试日志输出示例：

```
[DEBUG] SearchDevices() 被调用
[DEBUG] CH375OpenDevice(0) = 0x000001A2B3C4D5E6
[DEBUG] 设备0: VID=0x4348, PID=0x55E0, Name=...
[DEBUG] 共找到 1 个有效设备
[DEBUG] 设备节点变化事件
```

### 9.2 关闭调试输出

发布时不带 `--debug` 参数即可关闭调试日志。

## 10. 窗口属性

### 10.1 默认设置

```csharp
this.Text = "WCH IAP Tool";
this.Size = new Size(800, 500);
this.FormBorderStyle = FormBorderStyle.Sizable;
this.StartPosition = FormStartPosition.CenterScreen;
```

### 10.2 控件锚点

| 控件 | Anchor |
|------|--------|
| 设备列表 | Top, Left, Bottom |
| 状态标签 | Top, Right |
| 下载按钮 | Top, Right |
| 文件路径输入框 | Top, Left, Right |
| 选择文件按钮 | Top, Right |
| 日志文本框 | Top, Left, Right, Bottom |

## 11. 常见问题

### 11.1 设备列表为空

**可能原因：**
1. 设备未连接
2. 驱动未安装
3. 设备被其他程序占用

**解决方法：**
1. 检查USB连接
2. 安装CH375驱动
3. 关闭其他使用设备的程序

### 11.2 下载按钮灰色

**可能原因：**
1. 未检测到设备
2. 未选择设备

**解决方法：**
1. 连接设备
2. 在设备列表中点击选择设备

### 11.3 下载失败

**可能原因：**
1. 文件格式错误
2. Flash保护
3. 设备断开

**解决方法：**
1. 检查文件是否为有效的BIN/HEX
2. 检查单片机配置
3. 重新连接设备

## 12. 与CLI版本的差异

| 特性 | CLI版本 | GUI版本 |
|------|---------|---------|
| 界面 | 命令行 | 图形界面 |
| 设备选择 | 自动选择第一个 | 列表选择 |
| 文件选择 | 命令行参数 | 对话框/拖放 |
| 日志输出 | 控制台 | 文本框 |
| 自动检测 | 无 | 支持 |
| HEX测试 | 支持 | 不支持 |
| BIN对比 | 支持 | 不支持 |
