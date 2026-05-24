namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFixedStringAttribute(int byteLength) : Attribute
{
    public int ByteLength { get; set; } = byteLength;
    public BitStringEncoding Encoding { get; set; } = BitStringEncoding.ASCII;

    /// <summary>
    /// 短缩字符串末尾的填充字节。默认 <c>0x00</c>（NUL），常见替代值为 <c>0x20</c>（ASCII 空格，
    /// 见 SCADA / Modbus 标签字段）。反序列化时会同样地把尾部连续的 Padding 字节 trim 掉。
    /// </summary>
    public byte Padding { get; set; } = 0x00;
}
