namespace BitSerializer;

/// <summary>
/// 字符串编码字节数由 <b>另一个字段</b> 承载（不内联在字符串前）。区别于
/// <see cref="BitLengthPrefixStringAttribute"/>（length 直接前缀在字符串里）：
/// <list type="bullet">
///   <item>序列化时按 <see cref="Encoding"/> 编码字符串，把字节数自动回填到 <see cref="LengthFieldName"/>
///   引用的字段，然后写裸字节流（不带终止符）。</item>
///   <item>反序列化时从 <see cref="LengthFieldName"/> 字段读字节数（该字段必须先于本字段声明），
///   再按此读相应字节并按编码解码。</item>
/// </list>
/// 典型协议：DMI 站名 / 提示文本，length 字段位置和字符串位置由协议固定。
/// <code>
/// [BitField(8)] public byte NameLength { get; set; }              // 长度字段
/// [BitLengthFieldString(nameof(NameLength), Encoding = BitStringEncoding.UTF8)]
/// public string Name { get; set; }
/// </code>
/// 约束：
/// <list type="bullet">
///   <item>LengthFieldName 引用的字段必须是 <b>已声明的、byte/ushort/uint/int 标量</b>（BITS046）</item>
///   <item>引用字段必须 <b>先于</b> 本字符串字段声明（BITS047）</item>
///   <item>字符串字段必须字节对齐（BITS048）且不能跟在运行时偏移可能漂移的字段后（BITS049）</item>
///   <item><see cref="MaxBytes"/> 必须 ≥ 0（BITS050），<c>0</c> = 不限（仅受长度字段位宽约束）</item>
/// </list>
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitLengthFieldStringAttribute(string lengthFieldName) : Attribute
{
    /// <summary>承载字符串字节数的字段名（同 <c>nameof(...)</c>）。</summary>
    public string LengthFieldName { get; } = lengthFieldName;

    /// <summary>字符串编码。默认 UTF-8。</summary>
    public BitStringEncoding Encoding { get; set; } = BitStringEncoding.UTF8;

    /// <summary>
    /// 编码后字节数上限。<c>0</c> = 不限（仅受 LengthField 类型最大值约束）。
    /// UTF-8 编码下截断点会自动避开多字节字符中间。
    /// </summary>
    public int MaxBytes { get; set; } = 0;
}
