namespace BitSerializer;

/// <summary>
/// 关联字段绑定。同一字段最多可贴 2 个 <see cref="BitFieldRelatedAttribute"/>，但两个的
/// <see cref="RelationKind"/> 必须不同 — 典型组合是 polymorphic 字段同时绑定：
/// <list type="number">
///   <item>判别字段 (<see cref="BitRelationKind.Count"/>) — 决定运行时具体类型</item>
///   <item>字节预算字段 (<see cref="BitRelationKind.ByteLength"/>) — 决定 payload 占多少字节</item>
/// </list>
/// <code>
/// [BitField(8)]  public byte Type;
/// [BitField(16)] public ushort Length;
///
/// [BitField]
/// [BitFieldRelated(nameof(Type))]                                       // 判别字段
/// [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)] // 字节预算
/// [BitPoly(1, typeof(SubFrameA))]
/// [BitPoly(2, typeof(SubFrameB))]
/// public FrameBase Content { get; set; }
/// </code>
/// 详细的合法组合矩阵由 BITS056..058 在编译期校验。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class BitFieldRelatedAttribute(string relatedMemberName, Type valueConverterType = null) : Attribute
{
    public string RelatedMemberName { get; set; } = relatedMemberName;

    public Type ValueConverterType { get; set; } = valueConverterType;

    /// <summary>
    /// 关联语义：默认 Count（按元素个数驱动，向后兼容）；
    /// 设为 ByteLength 时按字节预算驱动集合/字节数组/嵌套类型的读取与回填。
    /// </summary>
    public BitRelationKind RelationKind { get; set; } = BitRelationKind.Count;
}
