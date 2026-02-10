namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldAttribute(int bitLength = int.MaxValue) : Attribute
{
    public int? BitLength { get; set; } = bitLength == int.MaxValue ? null : bitLength;
}
