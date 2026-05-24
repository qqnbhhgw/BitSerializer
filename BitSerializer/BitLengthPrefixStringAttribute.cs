namespace BitSerializer;

/// <summary>
/// 长度前缀字符串：先写 <see cref="LengthBits"/> 位的字节数前缀，再写按 <see cref="Encoding"/>
/// 编码的字节流。无终止符。该 attribute 是手写 "ushort Length + byte[] Bytes" 双字段模式的语法糖：
/// <code>
/// // 之前（手写）
/// [BitField(16)] public ushort StationNameUtf8Length { get; set; }
/// [BitField(8), BitFieldRelated(nameof(StationNameUtf8Length))]
/// public byte[] StationNameUtf8 { get; set; }
///
/// // 现在
/// [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8, MaxBytes = 64)]
/// public string StationName { get; set; }
/// </code>
/// 序列化时若 UTF-8 字节数超过 <see cref="MaxBytes"/>，自动在多字节字符边界处截断；
/// 若超过 <see cref="LengthBits"/> 能表达的最大值，抛 InvalidOperationException。
/// 字段必须字节对齐（BitStartIndex % 8 == 0），LengthBits ∈ {8, 16, 32}，否则报 BITS031/032。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitLengthPrefixStringAttribute(int lengthBits = 16) : Attribute
{
    /// <summary>长度前缀位宽（必须 ∈ {8, 16, 32}）。默认 16。</summary>
    public int LengthBits { get; set; } = lengthBits;

    /// <summary>字符串编码。默认 UTF-8（长度前缀场景的常用编码）。</summary>
    public BitStringEncoding Encoding { get; set; } = BitStringEncoding.UTF8;

    /// <summary>
    /// 编码后字节数上限。0 = 不限（仅受 LengthBits 的最大值约束）。
    /// UTF-8 编码下，截断点会自动避开多字节字符中间。
    /// </summary>
    public int MaxBytes { get; set; } = 0;
}
