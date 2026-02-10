namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldRelatedAttribute(string relatedMemberName, Type valueConverterType = null) : Attribute
{
    public string RelatedMemberName { get; set; } = relatedMemberName;

    public Type ValueConverterType { get; set; } = valueConverterType;
}
