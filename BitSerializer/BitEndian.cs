namespace BitSerializer;

/// <summary>
/// 字段级大小端覆盖。仅对字节对齐 + 字节宽度倍数（8/16/32/64）的数值/枚举字段有效。
/// <list type="bullet">
/// <item><c>Inherit</c>：跟随外层 Serialize/Deserialize 方法（MSB → 大端，LSB → 小端）。默认值。</item>
/// <item><c>Big</c>：无视外层方法，始终按大端字节序读写。</item>
/// <item><c>Little</c>：无视外层方法，始终按小端字节序读写。</item>
/// </list>
/// 典型场景：整帧主体为大端（调用 SerializeMSB），但少数字段需要小端。
/// </summary>
public enum BitEndian
{
    Inherit = 0,
    Big = 1,
    Little = 2,
}
