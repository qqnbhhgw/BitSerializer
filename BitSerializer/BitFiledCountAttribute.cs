namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFiledCountAttribute(int count) : Attribute
{
    public int Count { get; set; } = count;
}