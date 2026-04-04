namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFixedStringAttribute(int byteLength) : Attribute
{
    public int ByteLength { get; set; } = byteLength;
    public BitStringEncoding Encoding { get; set; } = BitStringEncoding.ASCII;
}
