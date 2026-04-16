namespace BitSerializer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldCountAttribute(int count) : Attribute
{
    public int Count { get; set; } = count;

    /// <summary>
    /// 数据不足时用默认值填充至 Count 个元素（仅对基本数值/枚举元素生效）。
    /// 序列化时始终写入 Count 个元素。
    /// </summary>
    public bool PadIfShort { get; set; } = false;
}