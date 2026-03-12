# USB设备查找功能

## 1. 功能概述

USB设备查找功能用于检测连接到电脑的WCH IAP设备，支持：
- 手动搜索设备
- 自动检测设备插入/拔出（仅GUI版本）

## 2. CH375驱动库

### 2.1 DLL函数

本项目使用CH375驱动库与USB设备通信：

```csharp
[DllImport("CH375DLL64.dll")]
public static extern IntPtr CH375OpenDevice(uint iIndex);

[DllImport("CH375DLL64.dll")]
public static extern void CH375CloseDevice(uint iIndex);

[DllImport("CH375DLL64.dll")]
public static extern uint CH375GetUsbID(uint iIndex);

[DllImport("CH375DLL64.dll")]
public static extern IntPtr CH375GetDeviceName(uint iIndex);

[DllImport("CH375DLL64.dll")]
public static extern bool CH375ReadData(uint iIndex, IntPtr oBuffer, ref uint ioLength);

[DllImport("CH375DLL64.dll")]
public static extern bool CH375WriteData(uint iIndex, IntPtr iBuffer, ref uint ioLength);
```

### 2.2 函数说明

| 函数 | 说明 |
|------|------|
| CH375OpenDevice | 打开指定索引的设备 |
| CH375CloseDevice | 关闭设备 |
| CH375GetUsbID | 获取USB VID/PID |
| CH375GetDeviceName | 获取设备名称 |
| CH375ReadData | 读取数据 |
| CH375WriteData | 写入数据 |

## 3. 设备搜索实现

### 3.1 搜索流程

```
1. 遍历设备索引 0-15
2. 尝试打开每个索引
3. 获取USB ID（VID/PID）
4. 获取设备名称
5. 验证设备有效性
6. 添加到设备列表
```

### 3.2 关键代码

```csharp
private void SearchDevices()
{
    devices.Clear();
    
    for (uint i = 0; i < 16; i++)
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
                devices.Add(new DeviceInfo
                {
                    Index = i,
                    VendorId = vendorId,
                    ProductId = productId,
                    Name = deviceName
                });
            }
        }
    }
}
```

### 3.3 设备信息结构

```csharp
public class DeviceInfo
{
    public uint Index { get; set; }        // 设备索引
    public ushort VendorId { get; set; }   // USB VID
    public ushort ProductId { get; set; }  // USB PID
    public string Name { get; set; }       // 设备名称
}
```

## 4. 自动设备检测（GUI版本）

### 4.1 Windows消息机制

GUI版本通过监听Windows消息实现自动检测：

```csharp
private const int WM_DEVICECHANGE = 0x0219;
private const int DBT_DEVICEARRIVAL = 0x8000;
private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
private const int DBT_DEVNODES_CHANGED = 0x0007;

protected override void WndProc(ref Message m)
{
    base.WndProc(ref m);

    if (m.Msg == WM_DEVICECHANGE)
    {
        int eventType = m.WParam.ToInt32();
        if (eventType == DBT_DEVICEARRIVAL ||
            eventType == DBT_DEVICEREMOVECOMPLETE ||
            eventType == DBT_DEVNODES_CHANGED)
        {
            SearchDevices();
        }
    }
}
```

### 4.2 事件类型说明

| 事件 | 值 | 说明 |
|------|-----|------|
| DBT_DEVICEARRIVAL | 0x8000 | 设备插入 |
| DBT_DEVICEREMOVECOMPLETE | 0x8004 | 设备移除完成 |
| DBT_DEVNODES_CHANGED | 0x0007 | 设备节点变化 |

**注意：** CH375设备通常触发 `DBT_DEVNODES_CHANGED` 事件，而不是 `DBT_DEVICEARRIVAL`。

### 4.3 早期版本的问题

早期版本只监听了 `DBT_DEVICEARRIVAL` 和 `DBT_DEVICEREMOVECOMPLETE`，导致无法检测到CH375设备变化。

**修复：** 添加对 `DBT_DEVNODES_CHANGED` (0x0007) 的处理。

## 5. 设备状态管理

### 5.1 状态定义

| 状态 | 条件 | 显示 |
|------|------|------|
| 未检测到设备 | devices.Count == 0 | "未检测到设备"（灰色） |
| 设备未选择 | devices.Count > 0 && 未选择 | "已检测到 X 个设备，请选择"（蓝色） |
| 设备已就绪 | 已选择设备 | "设备已就绪"（绿色） |

### 5.2 按钮状态控制

```csharp
private void UpdateDownloadButtonState()
{
    if (devices.Count == 0)
    {
        downloadButton.Enabled = false;
        statusLabel.Text = "未检测到设备";
        statusLabel.ForeColor = Color.Gray;
    }
    else if (deviceList.SelectedIndex < 0)
    {
        downloadButton.Enabled = false;
        statusLabel.Text = $"已检测到 {devices.Count} 个设备，请选择";
        statusLabel.ForeColor = Color.Blue;
    }
    else
    {
        downloadButton.Enabled = true;
        statusLabel.Text = "设备已就绪";
        statusLabel.ForeColor = Color.Green;
    }
}
```

## 6. 目标设备识别

### 6.1 WCH IAP设备特征

| 属性 | 值 |
|------|-----|
| VID | 0x4348 |
| PID | 0x55E0 |
| 设备名称 | USB设备路径 |

### 6.2 设备路径格式

```
\\?\usb#vid_4348&pid_55e0#7&33471c36&1&3#{5e7f6bdf-1ce5-4d78-bbcf-d20c44329f7d}
```

## 7. 使用示例

### 7.1 CLI版本

```
1. 搜索设备...
   Device 0: VID=0x4348, PID=0x55E0, \\?\usb#vid_4348&pid_55e0...
   共找到 1 个设备

选择设备: 索引=0, VID=4348, PID=55E0
```

### 7.2 GUI版本

- 程序启动时自动搜索
- 设备插入/拔出时自动更新列表
- 自动选择第一个设备
- 下载按钮根据状态自动启用/禁用

## 8. 常见问题

### 8.1 找不到设备

**可能原因：**
1. 设备未连接
2. 驱动未安装
3. 设备索引超出范围

**解决方法：**
1. 检查USB连接
2. 安装CH375驱动
3. 增加搜索索引范围

### 8.2 设备打开失败

**可能原因：**
1. 设备被其他程序占用
2. 权限不足

**解决方法：**
1. 关闭其他使用该设备的程序
2. 以管理员身份运行

### 8.3 自动检测不工作

**可能原因：**
1. 未正确处理 `DBT_DEVNODES_CHANGED` 事件

**解决方法：**
确保代码包含对 0x0007 事件的处理。
