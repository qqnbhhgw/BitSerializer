# BitSerializer

一个高性能的 .NET **位级别**二进制序列化库。通过 Attribute 声明字段的位长度，自动生成基于 Expression Tree 的序列化/反序列化逻辑，适用于网络协议解析、嵌入式通信、二进制文件格式处理等场景。

## 特性

- **位级别精度** — 字段可以是任意位长度（如 4-bit、12-bit），不受字节边界限制
- **MSB / LSB 双模式** — 支持 MSB（高位优先）和 LSB（低位优先）两种位序编码
- **Attribute 驱动** — 通过特性标注即可声明序列化结构，无需手写编解码逻辑
- **自动推断位长** — 未指定位长时，自动根据类型推断（`byte` = 8, `ushort` = 16, `int` = 32 ...）
- **嵌套类型** — 支持嵌套复合类型的递归序列化
- **集合支持** — 支持 `List<T>` 的序列化，元素数量可动态关联或固定指定
- **多态类型** — 通过类型判别字段自动分发到具体子类
- **值转换器** — 支持自定义序列化/反序列化时的值变换
- **高性能** — Expression Tree 编译 + 缓存，避免反射开销

## 环境要求

- .NET 8.0+

## 快速开始

### 基本用法

```csharp
using BitSerializer;

// 定义数据结构
public class Packet
{
    [BitField(8)]
    public byte Header { get; set; }

    [BitField(16)]
    public ushort Payload { get; set; }

    [BitField(8)]
    public byte Checksum { get; set; }
}

// MSB 模式（高位优先，大端序）
var packet = new Packet { Header = 0xAB, Payload = 0x1234, Checksum = 0xCD };
byte[] bytes = BitSerializerMSB.Serialize(packet);
var result = BitSerializerMSB.Deserialize<Packet>(bytes);

// LSB 模式（低位优先，小端序）
byte[] lsbBytes = BitSerializerLSB.Serialize(packet);
var lsbResult = BitSerializerLSB.Deserialize<Packet>(lsbBytes);
```

> **MSB vs LSB**：两者的 API 完全一致，区别仅在于字节内的位序方向。MSB 适用于网络协议（大端序），LSB 适用于硬件寄存器、部分嵌入式协议（小端序）。

### 自动推断位长

不指定位长时，根据属性类型自动推断：

```csharp
public class AutoData
{
    [BitField]          // 自动推断为 8 bit
    public byte A { get; set; }

    [BitField]          // 自动推断为 16 bit
    public ushort B { get; set; }

    [BitField]          // 自动推断为 32 bit
    public int C { get; set; }
}
```

### 自定义位长度（非字节对齐）

字段可以跨越字节边界，实现紧凑的位打包：

```csharp
public class CompactData
{
    [BitField(4)]       // 高 4 位
    public byte NibbleHigh { get; set; }

    [BitField(4)]       // 低 4 位
    public byte NibbleLow { get; set; }

    [BitField(12)]      // 12 位跨字节
    public ushort TwelveBits { get; set; }

    [BitField(4)]
    public byte FourBits { get; set; }
}
// 总计 24 bits = 3 字节
```

### 枚举类型

枚举类型直接支持：

```csharp
public enum Status : byte
{
    Unknown = 0,
    Active = 1,
    Inactive = 2,
}

public class StatusPacket
{
    [BitField(8)]
    public Status CurrentStatus { get; set; }

    [BitField(16)]
    public ushort Code { get; set; }
}
```

### 嵌套类型

支持复合类型的递归序列化：

```csharp
public class Point
{
    [BitField(8)]
    public byte X { get; set; }

    [BitField(8)]
    public byte Y { get; set; }
}

public class Frame
{
    [BitField(8)]
    public byte Id { get; set; }

    [BitField]              // 自动推断嵌套类型的总位长
    public Point Position { get; set; } = new();

    [BitField(8)]
    public byte Flags { get; set; }
}
```

### 集合（List）

通过 `BitFieldRelated` 关联计数字段，或通过 `BitFiledCount` 指定固定数量：

```csharp
// 动态数量：关联计数字段
public class DynamicList
{
    [BitField(4)]
    public byte Count { get; set; }

    [BitField(4)]
    public byte Reserved { get; set; }

    [BitField]
    [BitFieldRelated(nameof(Count))]        // 元素数量由 Count 字段决定
    public List<byte> Items { get; set; } = new();
}

// 固定数量
public class FixedList
{
    [BitField]
    [BitFiledCount(3)]                      // 固定 3 个元素
    public List<byte> Items { get; set; } = new();
}
```

### 多态类型

通过 `BitPoly` 特性实现基于判别值的类型分发：

```csharp
public class BaseMessage
{
    [BitField(8)]
    public byte CommonField { get; set; }
}

public class MessageA : BaseMessage
{
    [BitField(8)]
    public byte FieldA { get; set; }
}

public class MessageB : BaseMessage
{
    [BitField(16)]
    public ushort FieldB { get; set; }
}

public class Container
{
    [BitField(8)]
    public byte MessageType { get; set; }           // 类型判别字段

    [BitField(24)]                                  // 需指定所有子类的最大位长
    [BitFieldRelated(nameof(MessageType))]          // 关联判别字段
    [BitPoly(1, typeof(MessageA))]                  // MessageType=1 → MessageA
    [BitPoly(2, typeof(MessageB))]                  // MessageType=2 → MessageB
    public BaseMessage Message { get; set; }
}
```

### 值转换器

实现 `IBitFieldValueConverter` 接口，自定义序列化/反序列化时的值变换：

```csharp
public class DoubleConverter : IBitFieldValueConverter
{
    public static object OnDeserializeConvert(object value)
    {
        return (byte)((byte)value * 2);     // 反序列化时乘 2
    }

    public static object OnSerializeConvert(object value)
    {
        return (byte)((byte)value / 2);     // 序列化时除 2
    }
}

public class ConvertedData
{
    [BitField(8)]
    [BitFieldRelated(nameof(Value), typeof(DoubleConverter))]
    public byte Value { get; set; }
}
```

### 忽略字段

使用 `BitIgnore` 跳过不需要序列化的字段：

```csharp
public class MixedData
{
    [BitField(8)]
    public byte Value { get; set; }

    [BitIgnore]                         // 不参与序列化
    public string Description { get; set; } = "";

    [BitField(8)]
    public byte AnotherValue { get; set; }
}
```

## Attribute 参考

| 特性 | 说明 |
|------|------|
| `[BitField(n)]` | 声明字段参与序列化，`n` 为位长度（可选，不指定则自动推断） |
| `[BitFieldRelated(name)]` | 关联另一个字段（用于 List 计数或多态判别） |
| `[BitFieldRelated(name, converterType)]` | 关联字段并指定值转换器 |
| `[BitFiledCount(n)]` | 指定 List 的固定元素数量 |
| `[BitPoly(id, type)]` | 多态映射：当判别值为 `id` 时反序列化为 `type` |
| `[BitIgnore]` | 忽略该字段，不参与序列化/反序列化 |

## 支持的数据类型

- 整数类型：`byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`
- 枚举类型（任意底层整数类型）
- 嵌套的 BitField 类
- `List<T>`（T 为上述支持的类型）

## API 参考

| 类 | 说明 |
|---|---|
| `BitSerializerMSB` | MSB 模式序列化器（高位优先，大端字节序） |
| `BitSerializerLSB` | LSB 模式序列化器（低位优先，小端字节序） |

两者提供完全相同的 API：

```csharp
// 序列化
byte[] bytes = BitSerializerMSB.Serialize(obj);       // 返回 byte[]
BitSerializerMSB.Serialize(obj, spanBuffer);           // 写入 Span<byte>

// 反序列化
T result = BitSerializerMSB.Deserialize<T>(bytes);     // 从 byte[]
T result = BitSerializerMSB.Deserialize<T>(span);      // 从 ReadOnlySpan<byte>
```

将 `MSB` 替换为 `LSB` 即可切换为低位优先模式。

## 许可证

MIT License
