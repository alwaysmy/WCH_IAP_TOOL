# WCH IAP Tool

为 WCH CH32 系列单片机烧录固件的 IAP 下载工具。MCU 通过 USB 模拟为 USB 设备，PC 端可选择 **WinUSB 免驱** 或 **CH375 驱动** 两种后端通信。

| 后端 | 固件版本 | 说明 |
|------|---------|------|
| **WinUSB** (推荐) | RevB | Windows 10+ 免驱，稳定可靠 |
| CH375 (兼容) | RevA | 需安装 CH375DLL64.dll，大文件（>16KB）可能出现 `ReadData` 阻塞 |

## 项目结构

```
WCH_IAP_TOOL/
├── documents/                    # 开发文档与使用指南
├── CH375_CH372_lib/             # CH375 驱动 DLL（RevA 兼容用）
│   └── CH375DLL64.dll
├── WCHIAPToolCLI/               # CLI 命令行版本 (.NET 9.0)
│   ├── Program.cs
│   └── WCHIAPToolCLI.csproj
├── WCHIAPToolNew/               # GUI 桌面版本 (WinForms, .NET 9.0)
│   ├── Form1.cs
│   └── WCHIAPToolNew.csproj
├── UsbBackend.cs                # WinUSB/CH375 双后端抽象（CLI/GUI 共用）
├── WchHexToBinConverter.cs      # HEX → BIN 转换器（共用）
└── CHANGELOG.md
```

## 快速开始

### CLI 版本

```powershell
# 编译
dotnet build .\WCHIAPToolCLI\WCHIAPToolCLI.csproj

# WinUSB 模式（默认 --auto，自动检测后端）
.\WCHIAPToolCLI\bin\Debug\net9.0\WCHIAPToolCLI.exe --file firmware.hex

# 强制 CH375（RevA 固件）
.\WCHIAPToolCLI\bin\Debug\net9.0\WCHIAPToolCLI.exe --file firmware.hex --ch375
```

### GUI 版本

```powershell
dotnet build .\WCHIAPToolNew\WCHIAPToolNew.csproj
dotnet run --project .\WCHIAPToolNew\WCHIAPToolNew.csproj
```

GUI 默认 auto 模式，设备下拉框显示 `[WinUsb]` / `[Ch375]` 标签。

## 配套 MCU 固件

- **RevB (推荐)**: WinUSB 免驱 — [CH32V30x_IAP_RevA](https://github.com/alwaysmy/CH32V30x_IAP_RevA) `main` 分支
- **RevA (兼容)**: CH375 驱动 — 同上仓库 `00172b9` 及之前版本

## 文档

详见 [documents/](documents/) 目录。

## 已知问题

- **CH375 大文件阻塞**: CH375DLL64.dll 在传输 >16KB 文件时 `CH375ReadData` 可能永久阻塞。推荐 WinUSB 模式。
