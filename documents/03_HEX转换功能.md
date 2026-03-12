# HEX转换功能

## 1. 功能概述

`WchHexToBinConverter` 类用于将Intel HEX格式文件转换为BIN格式，专为WCH单片机IAP下载设计。

## 2. Intel HEX格式简介

### 2.1 HEX文件结构

每行HEX记录格式：
```
:LLAAAATTDD...DDCC
```

| 字段 | 长度 | 说明 |
|------|------|------|
| `:` | 1 | 起始符 |
| `LL` | 2 | 数据长度（字节数） |
| `AAAA` | 4 | 16位地址 |
| `TT` | 2 | 记录类型 |
| `DD...DD` | 变长 | 数据 |
| `CC` | 2 | 校验和 |

### 2.2 记录类型

| 类型 | 值 | 说明 |
|------|-----|------|
| Data | 0x00 | 数据记录 |
| EOF | 0x01 | 文件结束 |
| ExtSegAddr | 0x02 | 扩展段地址 |
| StartSegAddr | 0x03 | 起始段地址（80x86专用） |
| ExtLinAddr | 0x04 | 扩展线性地址 |
| StartLinAddr | 0x05 | 起始线性地址 |

## 3. 实现细节

### 3.1 类结构

```csharp
namespace WchHexConverter
{
    public class WchHexToBinConverter
    {
        public class HexFileResult
        {
            public byte[] Data { get; set; }        // 转换后的BIN数据
            public uint StartAddress { get; set; }  // 起始地址
        }

        public static HexFileResult ConvertHexToBin(string hexFilePath);
        public static byte[] ParseHexLine(string line);
    }
}
```

### 3.2 转换流程

```
1. 读取HEX文件所有行
2. 遍历每行，解析记录类型
   ├─ 0x00 (Data): 收集数据段，记录地址范围
   ├─ 0x04 (ExtLinAddr): 更新基地址
   ├─ 0x03 (StartSegAddr): 忽略（WCH RISC-V不需要）
   ├─ 0x05 (StartLinAddr): 忽略
   └─ 0x01 (EOF): 结束解析
3. 计算最小/最大地址
4. 创建连续缓冲区，填充0xFF
5. 将数据段复制到缓冲区
6. 返回结果
```

### 3.3 关键代码

```csharp
public static HexFileResult ConvertHexToBin(string hexFilePath)
{
    var lines = File.ReadAllLines(hexFilePath);
    var dataSegments = new List<(uint address, byte[] data)>();
    uint baseAddress = 0;
    uint minAddress = uint.MaxValue;
    uint maxAddress = 0;

    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != ':')
            continue;

        var data = ParseHexLine(line);
        if (data == null) continue;

        byte recordType = data[3];

        switch (recordType)
        {
            case 0x00: // Data record
                uint address = baseAddress + (uint)((data[1] << 8) | data[2]);
                int dataLen = data[0];
                byte[] segmentData = new byte[dataLen];
                Array.Copy(data, 4, segmentData, 0, dataLen);
                dataSegments.Add((address, segmentData));

                if (address < minAddress) minAddress = address;
                if (address + (uint)dataLen > maxAddress) maxAddress = address + (uint)dataLen;
                break;

            case 0x04: // Extended linear address
                baseAddress = (uint)((data[4] << 8) | data[5]) << 16;
                break;

            case 0x01: // EOF
                goto EndOfFile;
        }
    }

EndOfFile:
    // 创建缓冲区并填充0xFF
    uint totalSize = maxAddress - minAddress;
    byte[] binData = new byte[totalSize];
    for (int i = 0; i < binData.Length; i++)
        binData[i] = 0xFF;

    // 复制数据段
    foreach (var segment in dataSegments)
    {
        uint offset = segment.address - minAddress;
        Array.Copy(segment.data, 0, binData, offset, segment.data.Length);
    }

    return new HexFileResult { Data = binData, StartAddress = minAddress };
}
```

## 4. 与之前实现的区别

### 4.1 早期版本的问题

**问题1：命令码错误**
```csharp
// 错误的实现
private const byte CMD_IAP_ERASE = 0x01;  // 错误
private const byte CMD_IAP_PROM = 0x02;   // 错误

// 正确的实现（从单片机iap.h获取）
private const byte CMD_IAP_ERASE = 0x81;  // 正确
private const byte CMD_IAP_PROM = 0x80;   // 正确
```

**问题2：地址处理错误**

早期版本没有正确处理HEX文件中的起始地址：
- 直接从地址0开始创建缓冲区
- 导致大量0xFF填充
- 转换结果与官方不一致

**问题3：文件大小差异**

| 版本 | 大小 | 原因 |
|------|------|------|
| 早期版本 | 24680字节 | 未包含Start Segment Address记录 |
| 官方工具 | 24684字节 | 包含4字节地址记录 |
| 当前版本 | 24684字节 | 正确处理所有数据记录 |

### 4.2 当前版本的改进

1. **正确处理扩展线性地址（0x04）**
   - 支持64KB以上地址空间
   - 正确计算实际物理地址

2. **返回起始地址**
   - `HexFileResult.StartAddress` 包含HEX文件中的最小地址
   - GUI版本自动使用此地址

3. **填充0xFF**
   - 空白区域填充0xFF（Flash擦除状态）
   - 符合单片机Flash特性

4. **忽略无关记录**
   - Start Segment Address (0x03)：80x86专用，RISC-V不需要
   - Start Linear Address (0x05)：不影响下载

## 5. 使用示例

### 5.1 CLI版本

```powershell
# 测试HEX转换
.\WCHIAPToolCLI.exe --test-hex firmware.hex

# 对比两个BIN文件
.\WCHIAPToolCLI.exe --compare-bin official.bin converted.bin
```

### 5.2 代码调用

```csharp
using WchHexConverter;

// 转换HEX文件
var result = WchHexToBinConverter.ConvertHexToBin("firmware.hex");

// 获取BIN数据
byte[] binData = result.Data;

// 获取起始地址
uint startAddress = result.StartAddress;

Console.WriteLine($"数据大小: {binData.Length} 字节");
Console.WriteLine($"起始地址: 0x{startAddress:X8}");
```

## 6. 测试验证

### 6.1 测试用例

使用 `CH32V30x_ft2232h_XilinxCable.hex` 测试：

```
转换成功!
  数据大小: 24684 字节
  起始地址: 0x00005000
  结束地址: 0x0000B06C
```

### 6.2 与官方对比

```
对比BIN文件:
  文件1: CH32V30x_ft2232h_XilinxCable_cankao.bin (官方)
  文件2: CH32V30x_ft2232h_XilinxCable.test.bin (本工具)

文件1大小: 24684 字节
文件2大小: 24684 字节

文件完全相同!
```

## 7. 错误处理

### 7.1 校验和验证

```csharp
private static bool VerifyChecksum(byte[] data)
{
    int sum = 0;
    for (int i = 0; i < data.Length - 1; i++)
    {
        sum += data[i];
    }
    sum = (~sum + 1) & 0xFF;
    return sum == data[data.Length - 1];
}
```

### 7.2 异常处理

- 文件不存在：`FileNotFoundException`
- 格式错误：`InvalidDataException`
- 校验和错误：`InvalidDataException`

## 8. 注意事项

1. **文件编码**：HEX文件应为ASCII编码
2. **地址对齐**：不需要特定对齐，转换器自动处理
3. **空白区域**：自动填充0xFF
4. **大文件支持**：支持任意大小的HEX文件
