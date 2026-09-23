# BitSerializer

一个高性能的 .NET **位级别**二进制序列化库。通过 Attribute 声明字段的位长度，Source Generator 在编译期自动生成序列化/反序列化代码，零反射开销，适用于网络协议解析、嵌入式通信、二进制文件格式处理等场景。

## 特性

- **位级别精度** — 字段可以是任意位长度（如 4-bit、12-bit），不受字节边界限制
- **MSB / LSB 双模式** — 支持 MSB（高位优先）和 LSB（低位优先）两种位序编码
- **Attribute 驱动** — 通过特性标注即可声明序列化结构，无需手写编解码逻辑
- **自动推断位长** — 未指定位长时，自动根据类型推断（`byte` = 8, `ushort` = 16, `int` = 32 ...）
- **嵌套类型** — 支持嵌套复合类型的递归序列化
- **继承链支持** — 支持多层继承，包括通过未标记 `[BitSerialize]` 的中间抽象类型（如泛型基类）正确序列化
- **字符串支持** — 支持固定长度字符串（`[BitFixedString]`，可自定义 padding 字节）、NUL 终止字符串（`[BitTerminatedString]`）、长度前缀字符串（`[BitLengthPrefixString]`，length-prefix 8/16/32 bit）和**字段引用长度字符串**（`[BitLengthFieldString(nameof(NameLength))]`，长度由另一字段承载），支持 ASCII 和 UTF-8 编码
- **自定义序列化类型** — 支持实现 `IBitSerializable` 接口的自定义类型，无需 `[BitSerialize]` 标记
- **泛型类型参数** — 支持泛型类型参数字段的序列化，通过 `IBitSerializable` 接口在运行时分发
- **集合支持** — 支持 `List<T>` / `T[]` 的序列化，元素数量可动态关联或固定指定；也支持按**字节预算**驱动嵌套动态元素集合（`RelationKind = ByteLength`）
- **数组短包/剩余字节** — `[BitFieldCount(N, PadIfShort=true)]` 短数据自动补零；`[BitFieldConsumeRemaining]` 读取直到数据末尾
- **声明式 CRC** — `[BitCrc]` + `[BitCrcInclude]` 自动计算并回填 CRC 字段，内置 CRC-CCITT / CRC-16/ARC / CRC-32；`[BitCrc(WholeBuffer = true)]` 一键覆盖整 buffer 并用 `SkipHeadBytes` / `SkipTailBytes` 排除帧头/帧尾
- **字段级大小端覆盖** — `[BitField(Endian = BitEndian.Big/Little)]` 让单个数值字段独立选择字节序，方便跨混合大小端的真实协议
- **常量字段钉死与校验** — `[BitFieldValue(0x7E)]` 序列化时强制写入魔数、反序列化时自动校验，把帧头/帧尾/协议版本验证从 FluentValidation 前移到解码失败点
- **嵌套类型按字节预算** — `[BitFieldRelated(nameof(Length), RelationKind = ByteLength)]` 既适用于 List 也适用于嵌套 `[BitSerialize]` 类型，序列化时自动按 nested 类型的实际字节数回填长度字段，反序列化时按 wire 长度严格校验
- **自动回填关联字段** — 序列化时自动将集合长度写入关联的计数字段、将运行时类型写入多态判别字段，无需手动设置
- **多态类型** — 通过类型判别字段自动分发到具体子类
- **值转换器** — 支持自定义序列化/反序列化时的值变换，支持上下文感知重载
- **序列化上下文与生命周期钩子** — 支持通过上下文对象传递状态，以及序列化/反序列化前后的回调
- **record 类型支持** — 支持 `record class` 和 `record struct`
- **Source Generator** — 编译期自动生成序列化代码，零反射开销
- **编译期诊断** — 自动检测嵌套类型是否缺少 `[BitSerialize]` 标记，编译时报错
- **一包即用** — 只需引用 `Jlzeng.BitSerializer`，Source Generator 自动生效

## 环境要求

- **.NET 8.0 与 .NET 10.0（双 TFM / LTS）** — 从 `0.13.1` 起同时面向 `net8.0` 与 `net10.0` 多目标构建；.NET 8 LTS 预计约 2026-11-10 结束支持，建议新项目优先选用 .NET 10。

## 快速开始

### 基本用法

```csharp
using BitSerializer;

// 定义数据结构（需标记 [BitSerialize] 和 partial）
[BitSerialize]
public partial class Packet
{
    [BitField(8)]
    public byte Header { get; set; }

    [BitField(16)]
    public ushort Payload { get; set; }

    [BitField(8)]
    public byte Checksum { get; set; }
}

// 也支持 record 类型
[BitSerialize]
public partial record RecordPacket
{
    [BitField(8)]
    public byte Header { get; set; }

    [BitField(16)]
    public ushort Payload { get; set; }
}

// MSB 模式（高位优先，大端序）
var packet = new Packet { Header = 0xAB, Payload = 0x1234, Checksum = 0xCD };
byte[] bytes = BitSerializerMSB.Serialize(packet);
var result = BitSerializerMSB.Deserialize<Packet>(bytes);

// LSB 模式（低位优先，小端序）
byte[] lsbBytes = BitSerializerLSB.Serialize(packet);
var lsbResult = BitSerializerLSB.Deserialize<Packet>(lsbBytes);
```

### 高性能与零额外分配 API

`BitSerializerMSB` 和 `BitSerializerLSB` 提供相同的高性能入口：

```csharp
int size = BitSerializerMSB.GetRequiredByteCount(packet);
Span<byte> buffer = stackalloc byte[size];

OperationStatus writeStatus =
    BitSerializerMSB.TrySerialize(packet, buffer, out int bytesWritten);

var reusable = new Packet();
OperationStatus readStatus =
    BitSerializerMSB.TryDeserializeInto(buffer, reusable, out int bytesConsumed);
```

- `TrySerialize` 不扩容；目标过小时返回 `DestinationTooSmall`，成功时稳定热路径不产生库内临时分配。
- `TryDeserializeInto` 复用顶层对象、嵌套对象、数组和 List。数值/值类型 List 须有足够 `Capacity`；引用类型 List 还须预先填充足够 `Count`，且每个元素对象非空；数组须有足够长度，引用元素也须预创建，否则返回 `DestinationTooSmall`。
- `TryDeserialize(ref T, ...)` 也支持 struct 和 class；失败时对象可能已部分更新，需要事务语义时继续使用 `Deserialize<T>()`。
- 输入不足返回 `NeedMoreData`，预分配对象图容量不足返回 `DestinationTooSmall`，CRC/判别值/预算格式错误返回 `InvalidData`；用户 Hook 与 Converter 的业务异常不会被吞掉。
- 从 `0.13.0` 起，兼容 `Deserialize<T>()` 遇到未知多态判别值时抛 `InvalidDataException`（旧版本为 `InvalidOperationException`）。
- `0.13.1`：多目标 `net8.0` + `net10.0`（双 TFM / LTS），NuGet 包同时包含两套运行时程序集。
- 字符串反序列化仍必须创建最终 `string`；多态类型变化等语义要求创建新对象的情况不属于严格原地模式。
- 内置 CRC 和字符串序列化使用无临时对象路径。自定义 CRC 可额外实现 `IBitCrcAlgorithm<TSelf>`；数值转换器可实现 `IBitFieldValueConverter<TProperty, TWire>` 或 `IBitFieldValueConverter<TProperty, TWire, TContext>` 避免装箱。
- 首次 JIT、泛型初始化、用户 Hook/Context/Converter 内部行为和异常路径不计入稳定热路径的零分配承诺。

三泛型 Converter 的 `TContext` 来自模型的 `SerializeContext()` / `DeserializeContext()`，Generator 会直接生成强类型调用；`TWire` 决定 wire 读写类型，并在其位宽小于字段 BitLength 时报告 `BITS063`。

> **MSB vs LSB**：两者的 API 完全一致，区别仅在于字节内的位序方向。MSB 适用于网络协议（大端序），LSB 适用于硬件寄存器、部分嵌入式协议（小端序）。

### 性能实测

环境：Debian GNU/Linux，x64；BenchmarkDotNet；warmup 4 / iteration 10；载荷约 200 bits（`BenchmarkTests` 项目同一用例）。对比对象含手写 `Span` 编解码与 `BinarySerializer`。

**.NET 8.0.30 — Serialize**

| Method | Mean | Allocated |
|---|---:|---:|
| Manual_Ser | 8.153 ns | 0 B |
| BitSerializer_TrySer | 54.037 ns | 0 B |
| BitSerializer_Ser | 54.499 ns | 0 B |
| BinarySerializer_Ser | 14168 ns | 27928 B |

**.NET 8.0.30 — Deserialize**

| Method | Mean | Allocated |
|---|---:|---:|
| BitSerializer_TryDeInto | 53.521 ns | 0 B |
| Manual_De | 97.880 ns | 280 B |
| BitSerializer_De | 110.717 ns | 264 B |
| BinarySerializer_De | 24003 ns | 49016 B |

**.NET 10.0.11 — Serialize**

| Method | Mean | Allocated |
|---|---:|---:|
| Manual_Ser | 5.881 ns | 0 B |
| BitSerializer_TrySer | 61.984 ns | 0 B |
| BitSerializer_Ser | 63.533 ns | 0 B |
| BinarySerializer_Ser | 11476 ns | 27912 B |

**.NET 10.0.11 — Deserialize**

| Method | Mean | Allocated |
|---|---:|---:|
| BitSerializer_TryDeInto | 30.379 ns | 0 B |
| Manual_De | 81.965 ns | 280 B |
| BitSerializer_De | 112.110 ns | 264 B |
| BinarySerializer_De | 18061 ns | 46808 B |

要点：

- 相对 `BinarySerializer`：Serialize / Deserialize 仍是**数量级**差距，且 `TrySerialize` / `TryDeInto` 稳定热路径 **0 B** 分配。
- 相对手写 baseline：`Try*` API 约在同一数量级（数十 ns），用 Attribute + Source Generator 换可维护性。
- **.NET 10 上**：`TryDeInto` 约快 **43%**（53.5 → 30.4 ns）；Manual / BinarySerializer 也更快；本机上 `Ser` / `TrySer` 略慢约 **15%**（54 → 62 ns 量级）——如实记录，不强行夸大。
- 方法：**BenchmarkTests** 项目、相同 ~200 bits 载荷；不同机器/SDK 补丁结果会有波动。

`0.13.1`：多目标 `net8.0;net10.0`，CI 同步安装 8.0.x / 10.0.x SDK。

### 自动推断位长

PLACEHOLDER_SUFFIX_FROM_MAIN
