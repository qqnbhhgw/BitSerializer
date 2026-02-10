namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class BitPolyAttribute(int typId, Type type) : Attribute
{
    public int TypId { get; set; } = typId;
    public Type Type { get; set; } = type;
}
