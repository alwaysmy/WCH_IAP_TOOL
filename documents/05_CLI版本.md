# CLI版本详细说明

## 1. 功能概述

CLI版本是命令行界面的IAP下载工具，适合批处理和自动化脚本使用。

## 2. 命令行参数

### 2.1 参数列表

| 参数 | 说明 |
|------|------|
| 无参数 | 执行IAP下载（使用默认hex文件） |
| `--test-hex <file>` | 测试HEX文件转换 |
| `--compare-bin <file1> <file2>` | 对比两个BIN文件 |
| `--help` | 显示帮助信息 |

### 2.2 使用示例

```powershell
# 执行IAP下载
.\WCHIAPToolCLI.exe

# 测试HEX转换
.\WCHIAPToolCLI.exe --test-hex firmware.hex

# 对比BIN文件
.\WCHIAPToolCLI.exe --compare-bin official.bin converted.bin

# 显示帮助
.\WCHIAPToolCLI.exe --help
```

## 3. 下载流程

### 3.1 完整流程输出

```
WCH IAP Tool CLI
==================

CH375 DLL Version: 49

1. 搜索设备...
   Device 0: VID=0x4348, PID=0x55E0, \\?\usb#vid_4348&pid_55e0...
   共找到 1 个设备

选择设备: 索引=0, VID=4348, PID=55E0

2. 读取下载文件内容>>
   HEX格式转换成BIN格式...
   已读取下载数据24684字节
   起始地址: 0x00005000

3. 打开下载接口>>
   打开成功

4. 擦除FLASH>>
   成功

5. 写FLASH>>
   已写入 4096 / 24684 字节
   已写入 8192 / 24684 字节
   ...
   已写入 24684 / 24684 字节
   成功

6. Flash检验>>
   成功

7. 下载结束>>
   设备已断开(正常，单片机已跳转到APP)
   设置成功

8. 关闭下载接口>>
   已关闭。

IAP下载成功.
```

## 4. HEX转换测试

### 4.1 测试输出

```powershell
.\WCHIAPToolCLI.exe --test-hex CH32V30x_ft2232h_XilinxCable.hex
```

输出：
```
测试HEX文件转换: CH32V30x_ft2232h_XilinxCable.hex

HEX文件行数: 1541

前10行解析:
  Line 1: Type=ExtLinAddr, Len=2, Addr=0x0000
  Line 2: Type=Data, Len=16, Addr=0x0000
    Data: 6F 20 90 68 00 50 00 00 00 00 00 00 08 79 00 00
  ...

最后5行解析:
  Type=Data, Len=16, Addr=0x3060
  Type=Data, Len=16, Addr=0x3070
  Type=Data, Len=4, Addr=0x3080
  Type=StartSegAddr, Len=4, Addr=0x0000
    CS:IP = 0050:0000
  Type=EOF, Len=0, Addr=0x0000

转换成功!
  数据大小: 24684 字节
  起始地址: 0x00005000
  结束地址: 0x0000B06C

前64字节数据:
6F 20 90 68 00 50 00 00 00 00 00 00 08 79 00 00
0A 79 00 00 00 00 00 00 86 7E 00 00 00 00 00 00
...

已保存BIN文件: CH32V30x_ft2232h_XilinxCable.test.bin
```

## 5. BIN文件对比

### 5.1 对比输出

```powershell
.\WCHIAPToolCLI.exe --compare-bin official.bin converted.bin
```

输出：
```
对比BIN文件:
  文件1: official.bin
  文件2: converted.bin

文件1大小: 24684 字节
文件2大小: 24684 字节

文件完全相同!
```

### 5.2 发现差异时输出

```
对比BIN文件:
  文件1: file1.bin
  文件2: file2.bin

文件1大小: 24684 字节
文件2大小: 24680 字节

文件大小不同，差异: 4 字节

发现 100 处数据差异

首次差异位置: 0x0002 (2)
文件1数据: 90
文件2数据: 50

差异位置附近数据对比:
文件1: 6F 20 90 68 00 50 00 00 00 00 00 00 08 79 00 00
文件2: 6F 20 50 68 00 50 00 00 00 00 00 00 04 79 00 00
       ^^ ^^
```

## 6. IAP协议实现

### 6.1 命令发送格式

```csharp
byte[] cmdBuffer = new byte[64];
cmdBuffer[0] = CMD_IAP_PROM;    // 命令码
cmdBuffer[1] = (byte)chunkSize; // 数据长度
Array.Copy(data, offset, cmdBuffer, 2, chunkSize); // 数据
```

### 6.2 响应处理

```csharp
byte[] responseBuffer = new byte[64];
// 读取响应
bool result = CH375ReadData(deviceIndex, bufferPtr, ref length);
// 检查响应码
if (responseBuffer[0] == ERR_SUCCESS) { /* 成功 */ }
```

### 6.3 END命令特殊处理

END命令发送后，单片机会跳转到APP，USB可能断开：

```csharp
static void SendEndCommand()
{
    // 发送END命令
    CH375WriteData(deviceIndex, bufferPtr, ref length);
    
    // 尝试读取响应
    bool result = CH375ReadData(deviceIndex, bufferPtr, ref length);
    
    if (result)
    {
        // 正常响应
        if (response == ERR_SUCCESS || response == ERR_End)
            Console.WriteLine("单片机响应成功");
    }
    else
    {
        // 设备断开也是正常的
        Console.WriteLine("设备已断开(正常，单片机已跳转到APP)");
    }
}
```

## 7. 代码结构

### 7.1 主要方法

| 方法 | 说明 |
|------|------|
| `Main()` | 入口点，参数解析 |
| `TestHexConversion()` | HEX转换测试 |
| `CompareBinFiles()` | BIN文件对比 |
| `SearchDevices()` | 搜索设备 |
| `SendEraseCommand()` | 发送擦除命令 |
| `SendProgramData()` | 发送程序数据 |
| `SendVerifyCommand()` | 发送校验命令 |
| `SendEndCommand()` | 发送结束命令 |

### 7.2 常量定义

```csharp
// IAP命令
private const byte CMD_IAP_PROM = 0x80;
private const byte CMD_IAP_ERASE = 0x81;
private const byte CMD_IAP_VERIFY = 0x82;
private const byte CMD_IAP_END = 0x83;
private const byte CMD_JUMP_IAP = 0x84;

// 响应码
private const byte ERR_SUCCESS = 0x00;
private const byte ERR_ERROR = 0x01;
private const byte ERR_End = 0x02;
```

## 8. 错误处理

### 8.1 常见错误

| 错误 | 原因 | 解决方法 |
|------|------|----------|
| 未找到设备 | 设备未连接 | 检查USB连接 |
| 打开失败 | 设备被占用 | 关闭其他程序 |
| 擦除失败 | Flash保护 | 检查单片机配置 |
| 写入失败 | 地址错误 | 检查HEX文件 |

### 8.2 异常捕获

```csharp
try
{
    // IAP操作
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Error Code: {Marshal.GetLastWin32Error()}");
}
```

## 9. 扩展使用

### 9.1 批处理脚本

```batch
@echo off
echo 开始下载...
WCHIAPToolCLI.exe
if %errorlevel% equ 0 (
    echo 下载成功
) else (
    echo 下载失败
)
```

### 9.2 PowerShell脚本

```powershell
# 下载并检查结果
$result = .\WCHIAPToolCLI.exe
if ($LASTEXITCODE -eq 0) {
    Write-Host "下载成功" -ForegroundColor Green
} else {
    Write-Host "下载失败" -ForegroundColor Red
}
```
