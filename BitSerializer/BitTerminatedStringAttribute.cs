namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitTerminatedStringAttribute : Attribute
{
    public BitStringEncoding Encoding { get; set; } = BitStringEncoding.ASCII;
}
