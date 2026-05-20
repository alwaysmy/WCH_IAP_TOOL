# CH32V30x_IAP_RevB WinUSB 迁移计划

> 日期: 2026-05-20
> MCU 侧: 已完成 (`feature/custom-iap-winUSB`, RevB 固件)
> PC 侧: 待实现

---

## 一、背景

MCU 已改为 WinUSB 免驱枚举（Windows 识别为"WinUSB 设备"）。PC 上位机当前仍用 CH375DLL64.dll 通信，需增加 WinUSB 后端同时兼容 RevA (CH375) 和 RevB (WinUSB)。IAP 协议层（命令码/包格式/流程）完全不变。

## 二、参数设计

新增 `--ch375` / `--winusb` / `--auto`（默认），控制 USB 后端选择：

```bash
WCHIAPToolCLI -f app.hex              # --auto (默认): 按 VID/PID 搜，WinUSB 优先，失败回退 CH375
WCHIAPToolCLI -f app.hex --winusb     # 强制 WinUSB
WCHIAPToolCLI -f app.hex --ch375      # 强制 CH375 (RevA 兼容)
```

`--vid` / `--pid` 参数不变，GUI 的 VID/PID 过滤也不变。

## 三、设备搜索策略

**只走 VID/PID**，不引入 GUID。Windows 枚举完成后驱动信息在注册表，读即可判明。

```
SetupDiGetClassDevs(USB) → 枚举设备 by VID/PID
  └─ 读 Service 值:
      ├─ "WINUSB"  → WinUSB 路径
      ├─ 含 "CH375" → CH375 路径
      └─ 其他      → CH375 路径 + log 警告 "Unknown driver service: {name}"

--auto: 按 Service 自动选
--winusb / --ch375: 强制指定后端（Service 不匹配时报错提示）
```

**无需尝试打开**。实测验证（VID_4348, PID_55E0）：

| 驱动 | Service | CompatibleIDs |
|------|---------|---------------|
| CH375 (oem202.inf) | `CH375_A64` | USB\MS_COMP_WINUSB |
| WinUSB (winusb.inf) | `WINUSB` | USB\MS_COMP_WINUSB |

`CompatibleIDs` 含 `WINUSB` = 固件支持 WinUSB，与当前加载的驱动无关。Service 一字判明。

## 四、PC 侧架构

### 4.1 设备抽象

```csharp
abstract class IapUsbDevice : IDisposable
{
    public UsbDeviceEntry DeviceInfo { get; protected set; }
    public abstract bool Open();
    public abstract bool WritePipe(byte[] buf, ref uint len);
    public abstract bool ReadPipe(byte[] buf, ref uint len);
    public abstract void Close();
}

class Ch375Device : IapUsbDevice { ... }
class WinUsbDevice : IapUsbDevice { ... }
```

### 4.2 CH375 ↔ WinUSB 映射

| 操作 | CH375 | WinUSB |
|---|---|---|
| 搜索 | `CH375OpenDevice(0..15)` | `SetupDiGetClassDevs(USB)` → 枚举 → 过滤 VID/PID |
| 打开 | `CH375OpenDevice(i)` 返回值 | `CreateFile(path)` + `WinUsb_Initialize` |
| 写 (EP2 OUT) | `CH375WriteData(idx, buf, len)` | `WinUsb_WritePipe(h, 0x02, ...)` |
| 读 (EP2 IN) | `CH375ReadData(idx, buf, len)` | `WinUsb_ReadPipe(h, 0x82, ...)` |
| 关闭 | `CH375CloseDevice(i)` | `WinUsb_Free` + `CloseHandle` |
| VID/PID | `CH375GetUsbID` / `CH375GetDeviceName` | `SetupDiGetDeviceRegistryProperty(SPDRP_HARDWAREID)` |

### 4.3 改动文件

| 文件 | 改动 |
|---|---|
| `WCHIAPToolCLI/Program.cs` | 加 `--ch375/--winusb/--auto` 参数；添加 `IapUsbDevice` / `Ch375Device` / `WinUsbDevice` 类；`SearchDevices` 重写为 VID/PID 枚举；`RunIapMode` 用 `_device.WritePipe/ReadPipe` |
| `WCHIAPToolNew/Form1.cs` | 同样替换，GUI 加后端选择下拉/Radio |

### 4.4 不动的地方

- IAP 命令码 (0x80~0x84)、包格式 (64B)、流程 (ERASE→PROGRAM→VERIFY→END)
- JSON 输出 / 退出码 / 参数解析 / GUI 界面
- VID/PID 过滤、device name fallback 解析

## 五、WinUSB P/Invoke

```csharp
// SetupAPI
[DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

[DllImport("setupapi.dll", SetLastError = true)]
static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

[DllImport("setupapi.dll", SetLastError = true)]
static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, ref uint PropertyRegDataType, IntPtr PropertyBuffer, uint PropertyBufferSize, ref uint RequiredSize);

// WinUSB
[DllImport("winusb.dll", SetLastError = true)]
static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, out SafeFileHandle InterfaceHandle);

[DllImport("winusb.dll", SetLastError = true)]
static extern bool WinUsb_WritePipe(SafeFileHandle InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

[DllImport("winusb.dll", SetLastError = true)]
static extern bool WinUsb_ReadPipe(SafeFileHandle InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, out uint LengthTransferred, IntPtr Overlapped);

[DllImport("winusb.dll", SetLastError = true)]
static extern bool WinUsb_Free(SafeFileHandle InterfaceHandle);
```

## 六、实施步骤

1. CLI 加 `--ch375/--winusb/--auto` 参数解析
2. 封装 `IapUsbDevice` 抽象类 + `Ch375Device` + `WinUsbDevice`
3. 重写 `SearchDevices`：VID/PID 枚举 USB 设备，返回 device path 列表
4. `RunIapMode` 用 `_device` 接口替代 `CH375WriteData/ReadData` 直接调用
5. 编译，先后用 RevA (CH375) 和 RevB (WinUSB) 固件测试完整 IAP
6. GUI 同样修改

## 七、验证

- `--auto` 模式：RevB 设备 → WinUSB 通道 → 完整 IAP 流程
- `--auto` 模式：RevA 设备 → CH375 回退 → 完整 IAP 流程
- `--ch375` 强制 CH375
- `--winusb` 强制 WinUSB
- `--skip_prog` / `--json` / `--no-wait` 回归
