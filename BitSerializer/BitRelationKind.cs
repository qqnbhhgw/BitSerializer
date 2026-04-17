namespace BitSerializer;

/// <summary>
/// 关联字段的语义：关联字段承载的是元素个数、还是字节/位预算。
/// </summary>
public enum BitRelationKind
{
    /// <summary>关联字段的值 = 集合元素个数（默认，0.8.x 行为）。</summary>
    Count = 0,

    /// <summary>关联字段的值 = 目标字段占用的字节数（支持嵌套动态元素）。</summary>
    ByteLength = 1,
}
