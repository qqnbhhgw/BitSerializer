namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldRelatedAttribute(string relatedMemberName, Type valueConverterType = null) : Attribute
{
    public string RelatedMemberName { get; set; } = relatedMemberName;

    public Type ValueConverterType { get; set; } = valueConverterType;

    /// <summary>
    /// 关联语义：默认 Count（按元素个数驱动，向后兼容）；
    /// 设为 ByteLength 时按字节预算驱动集合/字节数组的读取与回填。
    /// </summary>
    public BitRelationKind RelationKind { get; set; } = BitRelationKind.Count;
}
