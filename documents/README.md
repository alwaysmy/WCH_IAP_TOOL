# WCH IAP Tool 项目文档

## 文档目录

| 文档 | 说明 |
|------|------|
| [01_项目概述.md](01_项目概述.md) | 项目整体介绍、功能概述 |
| [02_项目创建与编译.md](02_项目创建与编译.md) | 如何创建项目、编译运行 |
| [03_HEX转换功能.md](03_HEX转换功能.md) | HEX转BIN功能详细说明 |
| [04_USB设备查找.md](04_USB设备查找.md) | USB设备查找与自动检测 |
| [05_CLI版本.md](05_CLI版本.md) | CLI版本详细说明 |
| [06_GUI版本.md](06_GUI版本.md) | GUI版本详细说明 |
| [07_改进计划.md](07_改进计划.md) | 待改进功能和问题记录 |

## 项目简介

WCH IAP Tool 是一个用于沁恒（WCH）单片机IAP下载的上位机工具，支持通过USB接口进行程序下载。

## 项目结构

```
WCH_IAP_TOOL/
├── documents/                    # 文档目录
├── CH375_CH372_lib/             # CH375驱动库
│   ├── CH375DLL64.dll           # 64位驱动DLL
│   └── CH375DLL64.lib           # 链接库
├── WCHIAPToolCLI/               # CLI版本
│   ├── Program.cs
│   └── WCHIAPToolCLI.csproj
├── WCHIAPToolNew/               # GUI版本
│   ├── Form1.cs
│   └── WCHIAPToolNew.csproj
└── WchHexToBinConverter.cs      # HEX转BIN转换器（共用）
```

## 快速开始

### 编译CLI版本
```powershell
dotnet build .\WCHIAPToolCLI\WCHIAPToolCLI.csproj
```

### 编译GUI版本
```powershell
dotnet build .\WCHIAPToolNew\WCHIAPToolNew.csproj
```

### 运行
```powershell
# CLI版本
dotnet run --project .\WCHIAPToolCLI\WCHIAPToolCLI.csproj

# GUI版本
dotnet run --project .\WCHIAPToolNew\WCHIAPToolNew.csproj
```
