namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldCountAttribute(int count) : Attribute
{
    public int Count { get; set; } = count;
}