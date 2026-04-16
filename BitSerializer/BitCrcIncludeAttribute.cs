namespace BitSerializer;

/// <summary>
/// 标记该字段参与 CRC 计算，关联到同类型内某个 [BitCrc] 字段。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitCrcIncludeAttribute(string targetFieldName) : Attribute
{
    public string TargetFieldName { get; set; } = targetFieldName;
}
