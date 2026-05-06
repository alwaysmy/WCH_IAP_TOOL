# WCH IAP Tool

通过 CH375 USB 桥接芯片为 WCH CH32 系列单片机烧录固件的 IAP 下载工具。

## 项目结构

```
WCH_IAP_TOOL/
├── documents/                    # 开发文档与使用指南
├── CH375_CH372_lib/             # CH375 驱动库
│   ├── CH375DLL64.dll           # 64位驱动 DLL
│   └── CH375DLL64.lib           # 链接库
├── WCHIAPToolCLI/               # CLI 命令行版本
│   ├── Program.cs
│   └── WCHIAPToolCLI.csproj
├── WCHIAPToolNew/               # GUI 桌面版本
│   ├── Form1.cs
│   └── WCHIAPToolNew.csproj
└── WchHexToBinConverter.cs      # HEX 转 BIN 转换器（共用）
```

## 快速开始

### CLI 版本

```powershell
# 编译
dotnet build .\WCHIAPToolCLI\WCHIAPToolCLI.csproj

# 运行（烧录固件）
.\WCHIAPToolCLI\bin\Debug\net9.0\WCHIAPToolCLI.exe --file firmware.hex

# 更多用法见使用指南
```

### GUI 版本

```powershell
dotnet build .\WCHIAPToolNew\WCHIAPToolNew.csproj
dotnet run --project .\WCHIAPToolNew\WCHIAPToolNew.csproj
```

## 文档

详见 [documents/](documents/) 目录。

## 依赖单片机固件

可选：
1. WCH 官方 IAP Demo
2. 官方修改版：https://github.com/alwaysmy/CH32V30x_IAP_RevA