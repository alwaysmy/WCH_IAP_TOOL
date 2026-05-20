# WinUSB 开发笔记

> 日期: 2026-05-20
> 分支: `feature/winusb-backend`

---

## 一、测试记录

### 测试环境
- Windows 10 x64
- 设备 VID=0x4348, PID=0x55E0, RevB WinUSB 固件
- CLI 编译: `dotnet build WCHIAPToolCLI/WCHIAPToolCLI.csproj`

### 测试用例

| # | 命令 | 结果 | 说明 |
|---|------|------|------|
| 1 | `--info --debug` | ✅ | 枚举到设备, service=WINUSB, backend=WinUsb |
| 2 | `--skip_prog --debug` | ✅ | 打开设备 → 发 END → 设备复位, 47ms |
| 3 | `-f CH32V30x_IAP.hex --no-wait` | ✅ | ERASE(1ms)→PROGRAM(691ms,6840B)→VERIFY(202ms)→END, total 954ms |

---

## 二、开发过程踩坑记录

### 坑 1: 命名空间不能直接放 P/Invoke

**错误**: `CS0116: 命名空间不能直接包含字段、方法或语句之类的成员`

**原因**: C# file-scoped namespace (`namespace WCHIAPToolCLI;`) 下面只能放类型（class/struct/enum），不能放方法。

**正确做法**: 建一个 `static class NativeMethods` 包住所有 DllImport 和常量，然后用 `using static WCHIAPToolCLI.NativeMethods;` 在类里直接用。

### 坑 2: `namespace` 下 DllImport 方法必须标记 `static`

**错误**: `CS0601: 必须在标记为"extern"的方法上指定 DllImport 属性，该方法为"static"`

**原因**: C# 的 `DllImport` 方法在 static class 里，方法本身也必须声明 `static`。缺了 `static` 关键字就报这个错。

### 坑 3: `SetupDiGetDeviceInfo` 不存在

**错误**: `Unable to find an entry point named 'SetupDiGetDeviceInfo' in DLL 'setupapi.dll'`

**原因**: 正确的 API 名是 `SetupDiGetDeviceInfoListDetail`，不是 `SetupDiGetDeviceInfo`。而且根本不需要这个 API——`SetupDiGetDeviceInterfaceDetail` 的最后一个参数直接传 `ref SP_DEVINFO_DATA` 就能同时拿到设备信息和接口路径。

### 坑 4: `SetupDiEnumDeviceInterfaces` 参数顺序

**错误**: `CS1620: 参数 2 必须与关键字"ref"一起传递`

**原因**: P/Invoke 签名写错了。正确签名为:
```csharp
[DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
public static extern bool SetupDiEnumDeviceInterfaces(
    IntPtr DeviceInfoSet,           // 设备信息集句柄
    IntPtr DeviceInfoData,          // 可选, 传 IntPtr.Zero
    ref Guid InterfaceClassGuid,    // 接口 GUID
    uint MemberIndex,               // 索引
    ref SP_DEVICE_INTERFACE_DATA    // 出参
);
```

### 坑 5: `SP_DEVICE_INTERFACE_DETAIL_DATA` 不能直接用 `Marshal.PtrToStructure`

**原因**: 这个结构是变长的（`DevicePath` 是 `ANYSIZE_ARRAY`），`Marshal.PtrToStructure<T>()` 读出来只有固定大小，路径字符串会截断。

**正确做法**: 
1. 先调一次 `SetupDiGetDeviceInterfaceDetail(buf=NULL, &requiredSize)` 拿大小
2. `Marshal.AllocHGlobal(requiredSize)` 分配 buffer
3. 手动在 buffer[0] 写入 `cbSize = IntPtr.Size == 8 ? 8 : 6`
4. 再调一次拿完整数据
5. 用 `Marshal.PtrToStringAuto(buf + 4)` 读路径字符串 (offset 4 是 cbSize DWORD 之后)

---

## 三、WinUSB 设备枚举正确流程

### 3.1 整体架构

```
NativeMethods (static class)
├── CH375 P/Invoke       (RevA 兼容)
├── SetupAPI P/Invoke    (设备枚举)
├── WinUSB P/Invoke      (设备通信)
└── 常量/GUID/Struct     

IapUsbDevice (抽象类)
├── Ch375UsbDevice       (RevA)
└── WinUsbDevice         (RevB)

SearchDevices → 枚举设备 → 读 Service → 判定 Backend
RunIapMode → 创建 IapUsbDevice → Open → WritePipe/ReadPipe
```

### 3.2 设备枚举步骤

1. **`SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_DEVICE, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE)`**
   - 不要用 `DIGCF_ALLCLASSES`，用 `DIGCF_DEVICEINTERFACE`
   - 第二个参数传 `null` 即可

2. **`SetupDiEnumDeviceInterfaces` 遍历接口**
   - 每次拿到 `SP_DEVICE_INTERFACE_DATA`
   - 第二个参数传 `IntPtr.Zero`（可选 DeviceInfoData）

3. **`SetupDiGetDeviceInterfaceDetail` 两遍调用**
   - 第一遍: detailBuf=NULL, 拿 requiredSize
   - 分配 buffer, 写 cbSize
   - 第二遍: 传入 buffer 和 `ref SP_DEVINFO_DATA`, 拿到设备路径和硬件信息

4. **读设备路径**
   - `devPath = Marshal.PtrToStringAuto(detailBuf + 4)`
   - offset 4 = sizeof(DWORD) = cbSize 偏移

5. **读 HardwareID 和 Service**
   - `SetupDiGetDeviceRegistryProperty(SPDRP_HARDWAREID)` → 提取 VID/PID
   - `SetupDiGetDeviceRegistryProperty(SPDRP_SERVICE)` → "WINUSB" / "CH375_A64" / 其他

### 3.3 Backend 判定

```
ReadDeviceProperty(SPDRP_SERVICE):
  ├─ "WINUSB"          → WinUsb
  ├─ Contains "CH375"  → Ch375
  └─ 其他              → Ch375 + log 警告
```

### 3.4 WinUsbDevice 打开流程

1. `CreateFile(devicePath, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_READ|FILE_SHARE_WRITE, NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL)`
   - `devicePath` 就是上一步拿到的路径
   - 返回 `-1` (INVALID_HANDLE_VALUE) 表示失败

2. `WinUsb_Initialize(fileHandle, out winUsbHandle)`
   - 传入上一步的 file handle
   - 失败时要 `CloseHandle(fileHandle)`

3. 之后用 `winUsbHandle` 进行读写

### 3.5 WinUsbDevice 读写

- **Write**: `WinUsb_WritePipe(winUsbHandle, 0x02, buf, len, out written, IntPtr.Zero)`
  - EP2 OUT 地址是 `0x02`
  - `written` 必须等于 `len`
- **Read**: `WinUsb_ReadPipe(winUsbHandle, 0x82, buf, len, out read, IntPtr.Zero)`
  - EP2 IN 地址是 `0x82`
  - `read` 是实际读到的字节数

### 3.6 Ch375UsbDevice（兼容层）

- `CH375OpenDevice(index)` 打开
- `CH375WriteData/ReadData` 读写，需要 `Marshal.AllocHGlobal` + `Marshal.Copy`
- `CH375CloseDevice` 关闭

---

## 四、CLI 参数新增

```
--auto     默认, 按 Service 自动选后端
--ch375    强制 CH375
--winusb   强制 WinUSB
--vid      过滤 VID (hex)
--pid      过滤 PID (hex)
```

---

## 五、遗留问题

**CH375 大文件下载挂死**

CH375 驱动 (`CH375DLL64.dll`) 在传输大文件（>~16KB）时，`CH375ReadData` 会在多次读写后永久阻塞。小文件（~6KB bootloader）偶尔能通过。WinUSB 无此问题。

| 后端 | 6KB | 64KB | 96KB |
|------|-----|------|------|
| WinUSB | ✅ | ✅ | ✅ |
| CH375 | ✅ | ❌ 挂死 | ❌ 挂死 |

**兼容策略**：CH375 后端保留（`--ch375` 参数），小文件可用。`--auto` 默认走 WinUSB。以后逆向 DLL 排查或直接废弃。

## 六、GUI 测试记录

| # | 测试 | 结果 | 固件 |
|---|------|------|------|
| 1 | 设备搜索 + 下拉框 | ✅ 显示 [WinUsb] | RevB IAP |
| 2 | HEX 转 BIN (AD7175) | ✅ 96364B, 与 obj BIN 一致 | CH32V30x_USB_AD7175 |
| 3 | BIN 直接下载 | ✅ | CH32V30x_USB_AD7175 |
| 4 | 完整下载流程 | ✅ WinUSB 可用 | CH32V30x_USB_AD7175 |

### 已知问题：GUI 下载时界面阻塞

下载在主线程执行，大文件时界面卡死。改善方案：`async/await` + `Task.Run` 把下载移到后台线程，`LogMessage` 用 `BeginInvoke` 切回 UI 线程更新日志。

## 七、待办

- [x] 完整 IAP 烧录测试 — WinUSB/CH375 均验证
- [x] GUI 同步改为 IapUsbDevice 接口 (WinUSB + CH375 双后端)
- [ ] GUI 下载异步化（防界面卡死）
- [ ] GUI 加 --ch375/--winusb/--auto 命令行参数
- [ ] --help 文本更新
- [ ] 删除临时 debug 日志行
