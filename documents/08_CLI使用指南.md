# WCH IAP Tool CLI — 使用指南

## 简介

WCH IAP Tool CLI 是一款命令行 IAP（In-Application Programming）下载工具，支持 **WinUSB 免驱** 和 **CH375 桥接** 两种通信后端，兼容 RevA / RevB 固件。

> 适用场景：自动化烧录、产线测试、CI/CD 集成、脚本化部署。

## 快速开始

### 前提条件

- Windows x64 系统
- **WinUSB 模式**：Windows 10+ 自动识别，无需额外驱动
- **CH375 模式**：CH375DLL64.dll 与可执行文件在同一目录
- CH32 单片机已通过 USB 连接（IAP bootloader 模式）
- 默认 `--auto` 模式自动检测后端

### 最小化使用

```powershell
# 直接烧录 HEX 文件
WCHIAPToolCLI.exe --file firmware.hex

# 更简洁的位置参数形式
WCHIAPToolCLI.exe firmware.hex
```

### 脚本中调用

```powershell
# JSON 模式供脚本解析
$result = WCHIAPToolCLI.exe --file firmware.hex --json --no-wait | ConvertFrom-Json
if ($result.success) { "OK" } else { exit $result.exitCode }
```

## 完整参数参考

### 下载参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--file, -f <path>` | string | — | 固件文件路径（.hex 或 .bin），也支持位置参数 |
| `--device, -d <index>` | int | 自动选择 | 设备索引，仅在有多个设备时使用 |
| `--vid <hex>` | hex | 4348 | USB VID 过滤 |
| `--pid <hex>` | hex | 55E0 | USB PID 过滤 |
| `--timeout <ms>` | int | 90000 | 整体超时时间（毫秒） |
| `--skip-verify` | flag | false | 跳过验证步骤（加快烧录，不推荐生产环境使用） |
| `--skip_prog` | flag | false | 跳过擦除/编程/校验，仅发结束命令退出 bootloader |

### 后端选择

| 参数 | 说明 |
|------|------|
| `--auto` | **默认**，自动检测后端（Service 注册表 WINUSB/CH375） |
| `--ch375` | 强制 CH375 后端（RevA 固件） |
| `--winusb` | 强制 WinUSB 后端（RevB 固件，需 Win10+） |

### 输出控制

| 参数 | 说明 |
|------|------|
| `--quiet, -q` | 静默模式，仅输出阶段标题和结果 |
| `--json` | JSON 格式输出到 stdout，所有日志静默 |
| `--debug` | 调试日志输出到 stderr |
| `--no-wait` | 完成后不等待按键，立即退出 |
| `--info` | 仅查询设备信息，不执行下载 |
| `--help, -h` | 显示帮助信息 |

### 工具命令

| 参数 | 说明 |
|------|------|
| `--test-hex <file>` | 解析并显示 HEX 文件信息 |
| `--compare-bin <a> <b>` | 逐字节对比两个 BIN 文件 |

## 退出码

| 码 | 含义 | 说明 |
|----|------|------|
| 0 | 成功 | 下载/操作成功完成 |
| 1 | 一般错误 | 未归类异常 |
| 2 | 未找到设备 | 无匹配 VID/PID 的 USB 设备 |
| 3 | 文件错误 | 文件不存在或内容为空 |
| 4 | 擦除失败 | Flash 擦除失败 |
| 5 | 编程失败 | Flash 写入失败 |
| 6 | 验证失败 | 写入内容与原始数据不一致 |
| 7 | 超时 | 整体操作超时 |
| 8 | 文件不一致 | `--compare-bin` 对比结果不一致 |

## JSON 输出格式

### 成功响应

```json
{
  "success": true,
  "exitCode": 0,
  "message": "IAP download completed successfully",
  "device": {
    "index": 0,
    "vendorId": 17224,
    "productId": 21984,
    "name": "\\\\?\\usb#vid_4348&pid_55e0#...",
    "vidPidString": "VID=0x4348, PID=0x55E0"
  },
  "file": {
    "path": "D:\\firmware.hex",
    "size": 21684,
    "type": "hex",
    "startAddress": "0x00005000"
  },
  "timing": {
    "eraseMs": 0,
    "programMs": 1649,
    "verifyMs": 84,
    "totalMs": 1815
  },
  "error": null
}
```

### 失败响应

```json
{
  "success": false,
  "exitCode": 2,
  "message": "No device found with VID=0x4348, PID=0x55E0",
  "device": null,
  "file": null,
  "timing": null,
  "error": "No device found with VID=0x4348, PID=0x55E0"
}
```

`vendorId`/`productId` 以十进制整数表示，`vidPidString` 提供十六进制可读形式。

## 使用场景

### 1. 产线批量烧录

```batch
@echo off
for %%f in (.\firmware\*.hex) do (
    echo Burning %%f...
    WCHIAPToolCLI.exe %%f --no-wait --quiet
    if errorlevel 1 (
        echo FAILED: %%f
        exit /b 1
    )
)
echo All devices programmed successfully
```

### 2. CI/CD 固件部署

```yaml
# .gitlab-ci.yml
flash:
  script:
    - WCHIAPToolCLI.exe --file build/output.hex --json --no-wait
  artifacts:
    reports:
      metrics: flash-result.json
```

### 3. Agent 自动化调用

```powershell
# 被 AI agent / 脚手架工具调用
function Invoke-IapFlash {
    param($HexPath)
    $json = .\WCHIAPToolCLI.exe --file $HexPath --json --no-wait
    return $json | ConvertFrom-Json
}
```

### 4. 退出 Bootloader（不更新固件）

```powershell
# 如果触发了 bootloader 但不想更新，直接发 END 命令跳回 APP
.\WCHIAPToolCLI.exe --skip_prog --no-wait
```

适用场景：远程通过 SCPI（如 `SYSTem:UPDate`）触发了 IAP 跳转，但决定不更新固件时，无需传文件即可让设备退出 bootloader 回到应用模式。

### 5. 设备信息查询

```powershell
# 确认设备连接状态
.\WCHIAPToolCLI.exe --info
```

## 常见问题

### Q: "No device found" 但设备已连接

可能原因：
1. VID/PID 不匹配——用 `--vid 0x4348 --pid 0x55E0` 确认
2. 单片机不在 IAP bootloader 模式——检查 BOOT 引脚或应用是否调用了 IAP 跳转
3. CH375DLL64.dll 缺失——确认该 DLL 与 exe 在同一目录

### Q: 下载过程中卡住

使用 `--timeout` 设置超时防止无限等待。用 `--debug` 获取详细诊断信息：

```powershell
WCHIAPToolCLI.exe --file firmware.hex --debug --no-wait 2> debug.log
```

### Q: 下载完成后设备不启动

确认固件起始地址正确——HEX 文件包含地址信息，BIN 文件需要确保烧录地址正确。

### Q: 输出乱码或格式异常

JSON 模式下 stdout 仅输出一行 JSON。如果看到非 JSON 内容，检查是否有其他程序（如 IDE 输出窗口）干扰。

## 技术说明

### 通信协议

- USB 包大小：64 字节
- 数据载荷：62 字节/包
- 命令集：ERASE(0x81) → PROGRAM(0x80) → VERIFY(0x82) → END(0x83)
- 进度报告：每 4KB

### VID/PID 检测

工具始终从 USB 设备路径名（`\\?\usb#vid_4348&pid_55e0#...`）解析 VID/PID，该信息由 Windows USB 子系统提供，是权威来源。`CH375GetUsbID()` 的返回值仅作为设备名为空时的兜底。

### 依赖

- .NET 9.0 运行时
- CH375DLL64.dll（沁恒官方驱动）
- 仅 Windows x64

## 修订历史

| 日期 | 版本 | 说明 |
|------|------|------|
| 2026-05-07 | 1.0 | 初始版本，覆盖 CLI 完整功能 |
| 2026-05-19 | 1.1 | 新增 `--skip_prog` 参数说明 |
