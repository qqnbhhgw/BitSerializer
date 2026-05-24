namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldAttribute(int bitLength = int.MaxValue) : Attribute
{
    public int? BitLength { get; set; } = bitLength == int.MaxValue ? null : bitLength;

    /// <summary>
    /// 字段级大小端覆盖。默认 <see cref="BitEndian.Inherit"/> = 跟随外层 Serialize/Deserialize 方法。
    /// 设为 <see cref="BitEndian.Big"/> 或 <see cref="BitEndian.Little"/> 时，该字段无视外层方法，
    /// 始终按指定字节序读写。仅对字节对齐（BitStartIndex % 8 == 0）且字节宽度倍数（BitLength ∈ {8,16,32,64}）
    /// 的数值/枚举字段有效；其他场景由源生成器报 BITS028 错误。
    /// </summary>
    public BitEndian Endian { get; set; } = BitEndian.Inherit;
}
