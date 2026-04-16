namespace BitSerializer;

/// <summary>
/// 标记 CRC 结果字段。被标记字段会在所有 [BitCrcInclude] 字段序列化完成后，
/// 由源码生成器自动填入计算出的 CRC 值。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitCrcAttribute(Type algorithmType) : Attribute
{
    public Type AlgorithmType { get; set; } = algorithmType;

    public ulong InitialValue { get; set; } = 0;

    public bool ValidateOnDeserialize { get; set; } = false;
}
