# CLI 版本详细说明

## 1. 功能概述

CLI 版本是命令行界面的 IAP 下载工具，适合批处理、自动化脚本和 agent 调用。

主要特性：
- 完整 IAP 下载流程（擦除→编程→验证→结束）
- HEX/BIN 文件自动识别与转换
- VID/PID 过滤设备选择
- JSON 输出模式（agent/CI 集成）
- 可配置超时与跳过验证
- 工具命令（HEX 测试、BIN 对比）
- 退出码支持

## 2. 命令行参数

### 2.1 参数列表

| 参数 | 说明 |
|------|------|
| `--file, -f <path>` | 固件文件路径 (.hex 或 .bin)，也支持位置参数 |
| `--device, -d <index>` | 设备索引（默认：自动选择第一个匹配设备） |
| `--vid <hex>` | VID 过滤（默认：4348） |
| `--pid <hex>` | PID 过滤（默认：55E0） |
| `--quiet, -q` | 静默模式，仅输出关键信息 |
| `--json` | JSON 格式输出（供脚本/agent 解析） |
| `--no-wait` | 完成后不等待按键 |
| `--timeout <ms>` | 整体超时毫秒数（默认：30000） |
| `--skip-verify` | 跳过验证步骤 |
| `--debug` | 启用调试输出（输出到 stderr） |
| `--info` | 仅查询设备信息，不下载 |
| `--help, -h` | 显示帮助信息 |
| `--test-hex <file>` | 测试 HEX 文件转换 |
| `--compare-bin <a> <b>` | 对比两个 BIN 文件 |

### 2.2 使用示例

```powershell
# 基础 IAP 下载
.\WCHIAPToolCLI.exe --file firmware.hex

# 指定设备索引
.\WCHIAPToolCLI.exe -f firmware.bin -d 1 --no-wait

# JSON 输出（agent 集成）
.\WCHIAPToolCLI.exe --file app.hex --json

# 自定义 VID/PID 过滤
.\WCHIAPToolCLI.exe --vid 0x4348 --pid 0x55E0 --info

# 静默模式下载
.\WCHIAPToolCLI.exe -f firmware.hex -q --no-wait

# 跳过验证（快速测试）
.\WCHIAPToolCLI.exe -f firmware.bin --skip-verify

# HEX 转换测试
.\WCHIAPToolCLI.exe --test-hex firmware.hex

# BIN 对比
.\WCHIAPToolCLI.exe --compare-bin a.bin b.bin

# 位置参数（简化用法）
.\WCHIAPToolCLI.exe firmware.hex
```

## 3. 退出码

| 退出码 | 名称 | 说明 |
|--------|------|------|
| 0 | Success | 成功 |
| 1 | GeneralError | 一般错误 |
| 2 | NoDevice | 未找到匹配设备 |
| 3 | FileError | 文件错误（不存在、格式无效或为空） |
| 4 | EraseFailed | 擦除失败 |
| 5 | ProgramFailed | 编程失败 |
| 6 | VerifyFailed | 验证失败 |
| 7 | Timeout | 超时 |
| 8 | CompareMismatch | 文件不一致（--compare-bin） |

## 4. JSON 输出格式

`--json` 模式下，stdout 只输出一行 JSON，无任何日志污染。日志输出在 JSON 模式下完全静默，调试日志（--debug）输出到 stderr。

### 4.1 成功输出

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

### 4.2 失败输出

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

### 4.3 字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| success | bool | 是否成功 |
| exitCode | int | 退出码 |
| message | string | 结果消息 |
| device | object/null | 设备信息（index, vendorId, productId, name, vidPidString） |
| file | object/null | 文件信息（path, size, type, startAddress） |
| timing | object/null | 时间统计（eraseMs, programMs, verifyMs, totalMs） |
| error | string/null | 失败时与 message 相同，成功时为 null |

## 5. 下载流程

### 5.1 正常输出

```
1. Searching devices...
Found 1 matching device(s):
  Device 0: VID=0x4348, PID=0x55E0, \\?\usb#vid_4348&pid_55e0#...
Selected: Device 0, VID=0x4348, PID=0x55E0

2. Loading file: firmware.hex
   Converting HEX to BIN...
   Size: 21684 bytes, Start: 0x00005000

3. Opening device...
   Device opened successfully

4. Erasing flash...
   Success (0ms)

5. Programming flash (21684 bytes)...
   Written 4096 / 21684 bytes (18.9%)
   Written 8192 / 21684 bytes (37.8%)
   Written 12288 / 21684 bytes (56.7%)
   Written 16384 / 21684 bytes (75.6%)
   Written 20480 / 21684 bytes (94.5%)
   Written 21684 / 21684 bytes (100.0%)
   Success (1649ms, 13117 B/s)

6. Verifying flash...
   Success (84ms)

7. Ending IAP session...
   Device reset to application

SUCCESS: IAP download completed successfully
Timing: Erase=0ms, Program=1649ms, Verify=84ms, Total=1815ms
```

### 5.2 流程说明

1. **搜索设备** — 遍历 0-15 号 USB 设备，使用 CH375OpenDevice 检测，通过设备名解析 VID/PID
2. **加载文件** — HEX 文件自动转换为 BIN，验证文件存在且非空
3. **打开设备** — 调用 CH375OpenDevice 打开选中设备
4. **擦除 Flash** — 发送 CMD_IAP_ERASE (0x81)，等待响应
5. **编程 Flash** — 每包 62 字节数据 + 2 字节头（命令+长度），每 4KB 报告进度
6. **验证 Flash** — 发送 CMD_IAP_VERIFY (0x82)，逐包比较
7. **结束会话** — 发送 CMD_IAP_END (0x83)，设备跳转到应用（USB 断开为预期行为）

## 6. IAP 协议实现

### 6.1 命令与常量

```csharp
const byte CMD_IAP_PROM   = 0x80;  // 编程
const byte CMD_IAP_ERASE  = 0x81;  // 擦除
const byte CMD_IAP_VERIFY = 0x82;  // 验证
const byte CMD_IAP_END    = 0x83;  // 结束
const byte CMD_JUMP_IAP   = 0x84;  // 跳转 IAP

const byte ERR_SUCCESS    = 0x00;  // 成功
const byte ERR_ERROR      = 0x01;  // 错误
const byte ERR_End        = 0x02;  // 结束响应
```

### 6.2 命令发送格式

每包 64 字节，首字节命令码，第二字节数据长度，后续为数据：

```csharp
byte[] cmdBuf = new byte[64];
cmdBuf[0] = CMD_IAP_PROM;          // 命令码
cmdBuf[1] = (byte)chunkSize;       // 数据长度（最多 62）
Array.Copy(data, offset, cmdBuf, 2, chunkSize);  // 数据
```

### 6.3 响应处理

```csharp
byte[] resp = new byte[64];
bool result = CH375ReadData(deviceIndex, ptr, ref len);
if (result && resp[0] == ERR_SUCCESS) { /* 成功 */ }
```

### 6.4 END 命令特殊处理

END 命令发送后单片机跳转到 APP，USB 端口断开，读取响应时会失败——这是预期行为：

```csharp
static void SendEndCommand()
{
    // 发送 END 命令
    CH375WriteData(deviceIndex, ptr, ref len);

    // 尝试读取响应（可能因设备断开而失败）
    if (CH375ReadData(deviceIndex, ptr, ref len) && len > 0)
    {
        // 正常响应
        if (resp[0] == ERR_SUCCESS || resp[0] == ERR_End)
            LogMessage("End response received");
    }
    else
    {
        // 设备断开——预期行为，单片机已跳转
        LogDebug("Device disconnected after END (expected)");
    }
}
```

## 7. 代码结构

### 7.1 主要类型与方法

| 类型/方法 | 说明 |
|-----------|------|
| `ExitCode` 枚举 | 退出码（0-8） |
| `CliArgs` 类 | 命令行参数解析结果 |
| `IapResult` 类 | JSON 输出模型 |
| `Main()` | 入口，路由到对应模式 |
| `ParseArgs()` | 参数解析 |
| `RunIapMode()` | 主流程：搜索→加载→打开→擦除→编程→验证→结束 |
| `SearchDevices()` | 搜索 USB 设备（VID/PID 过滤） |
| `SendEraseCommand()` | 发送擦除命令 |
| `SendCommandAndCheck()` | 通用命令发送+响应检查 |
| `SendProgramData()` | 分块发送编程数据 |
| `SendVerifyCommand()` | 发送验证命令 |
| `SendEndCommand()` | 发送结束命令 |
| `LogMessage()` | 普通日志（JSON 模式静默） |
| `LogDebug()` | 调试日志（输出到 stderr） |
| `OutputError()` | 错误输出 |
| `OutputSuccess()` | 成功输出 |
| `TestHexConversion()` | HEX 转换测试 |
| `CompareBinFiles()` | BIN 对比工具 |

### 7.2 共享代码

- `WchHexToBinConverter.cs` — HEX 转 BIN 转换器，与 GUI 项目共享

## 8. VID/PID 检测策略

设备搜索采用双层策略：

1. 调用 `CH375GetUsbID()` 获取 VID/PID（高/低 16 位）
2. **始终**从设备名解析 VID/PID（格式：`\\?\usb#vid_4348&pid_55e0#...`）

设备名（USB 路径）是权威来源——由 Windows USB 子系统直接生成。某些 DLL 版本中 `CH375GetUsbID` 可能返回字节交换值（如 `0x55E04348` 而非 `0x434855E0`），此时设备名解析作为修正。

## 9. 脚本集成

### 9.1 批处理脚本

```batch
@echo off
WCHIAPToolCLI.exe --file firmware.hex --no-wait
if %errorlevel% equ 0 (
    echo Download succeeded
) else (
    echo Download failed with code %errorlevel%
    exit /b %errorlevel%
)
```

### 9.2 PowerShell 脚本

```powershell
$result = .\WCHIAPToolCLI.exe --file firmware.hex --json --no-wait
$json = $result | ConvertFrom-Json
if ($json.success) {
    Write-Host "Downloaded $($json.file.size) bytes in $($json.timing.totalMs)ms"
} else {
    Write-Error "Failed: $($json.message)"
    exit $json.exitCode
}
```

### 9.3 CI/CD 集成（GitHub Actions / GitLab CI）

```yaml
- name: Flash firmware
  shell: pwsh
  run: |
    $result = .\WCHIAPToolCLI.exe --file firmware.hex --json --no-wait
    $json = $result | ConvertFrom-Json
    if (-not $json.success) { exit $json.exitCode }
```

## 10. 错误处理

### 10.1 常见错误

| 错误 | 原因 | 解决方法 |
|------|------|----------|
| Exit code 2 (NoDevice) | 未找到设备 | 检查 USB 连接、VID/PID 是否正确 |
| Exit code 3 (FileError) | 文件不存在或为空 | 检查文件路径 |
| Exit code 4 (EraseFailed) | 擦除失败 | 检查单片机 Flash 保护配置 |
| Exit code 5 (ProgramFailed) | 编程失败 | 检查地址范围是否匹配 |
| Exit code 6 (VerifyFailed) | 验证不一致 | Flash 写入异常，尝试重新下载 |
| Exit code 7 (Timeout) | 超时 | 增大 --timeout 参数或检查 USB 连接 |

### 10.2 调试模式

使用 `--debug` 启用详细日志，输出到 stderr：

```powershell
.\WCHIAPToolCLI.exe --file firmware.hex --debug --no-wait 2> debug.log
```
