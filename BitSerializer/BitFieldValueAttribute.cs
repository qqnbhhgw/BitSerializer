namespace BitSerializer;

/// <summary>
/// 钉死一个数值/枚举字段的常量值：
/// <list type="bullet">
///   <item>序列化时无视字段当前值，直接把 <see cref="Value"/> 写到 wire（同时反向回写到字段，保持
///   in-memory 对象与 wire 字节一致）。</item>
///   <item>反序列化时如果 <see cref="Verify"/> = <c>true</c>（默认）且读到的值不等于 <see cref="Value"/>，
///   抛 <see cref="System.IO.InvalidDataException"/>。</item>
/// </list>
/// 典型用法：DMI / ATP / ATS 帧的 FrameStart=0x7E、FrameEnd=0xCF、ProtocolVersion 等魔数字段，
/// 把原来散落在 FluentValidation 里的事后校验前移到解码失败点。仅支持字节对齐 + 字节倍数宽度的
/// 数值/枚举标量（BITS042），不能与 <c>[BitCrc]</c> / <c>[BitFieldRelated]</c> / <c>[BitFieldCount]</c> /
/// <c>[BitPoly]</c> 共用（BITS044）。常量必须能放进字段的 BitLength（BITS043）。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldValueAttribute(object value) : Attribute
{
    /// <summary>钉死的常量值。整型字面量（如 <c>0x7E</c>）或枚举字面量。</summary>
    public object Value { get; } = value;

    /// <summary>反序列化时校验读到的值与 <see cref="Value"/> 是否一致。默认 <c>true</c>。</summary>
    public bool Verify { get; set; } = true;
}
