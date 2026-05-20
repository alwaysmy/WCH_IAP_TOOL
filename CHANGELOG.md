# Changelog

## 2026-05-20 — WinUSB Backend (feature/winusb-backend)

### Added
- **WinUSB 后端**: CLI 和 GUI 支持 WinUSB 免驱通信（替代 CH375 DLL）
- **`--auto` / `--ch375` / `--winusb` 参数** (CLI): 自动检测 / 强制 CH375 / 强制 WinUSB
- **`UsbBackend.cs`**: CLI/GUI 共享的 USB 后端抽象（`IapUsbDevice` → `Ch375UsbDevice` / `WinUsbDevice`）
- **`DeviceSearch.SearchDevices()`**: SetupDi 设备接口枚举，按 Service 注册表值判定后端
- **GUI 设备下拉框**: 显示 `[WinUsb]` / `[Ch375]` 标签
- **HEX 转换器**: 支持 Intel HEX type 0x02 Extended Segment Address 记录

### Fixed
- **HEX 转换 bug**: 缺少 type 0x02 处理导致 65536B 错误输出（修复后 96364B 与 BIN 一致）
- **GUI 下载阻塞**: 改为 `async/await + Task.Run` 后台线程，`LogMessage` 线程安全
- **GUI 定时器竞态**: 下载时暂停设备轮询，完成后恢复

### Known Issues
- **CH375 大文件下载挂死**: CH375DLL64.dll 在传输 >16KB 时 `CH375ReadData` 永久阻塞。小文件可用，大文件请走 WinUSB。

### MCU 固件 (CH32V30x_IAP_RevB)
- **WinUSB WCID 描述符** (MS OS 1.0): OS String (0xEE) + Compat ID (WINUSB) + Properties (DeviceInterfaceGUID)
- `bcdUSB` 改为 0x0200 (USB 2.0)
- 3 项代码审查修复: Properties null terminator、USBFS 错误变量、EP0 多包续传
- IAP 协议层 (命令码/包格式/端点) 不变
