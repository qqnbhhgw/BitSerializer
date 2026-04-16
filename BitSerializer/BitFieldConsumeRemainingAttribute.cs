namespace BitSerializer;

/// <summary>
/// 标记 List/Array 字段消费剩余所有字节（反序列化时读到数据末尾）。
/// 序列化时按集合实际长度写入。必须是类型的最后一个字段，元素必须是基本数值/枚举类型。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitFieldConsumeRemainingAttribute : Attribute
{
}
