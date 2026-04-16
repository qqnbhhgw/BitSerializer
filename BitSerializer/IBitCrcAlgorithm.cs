namespace BitSerializer;

/// <summary>
/// CRC 算法抽象。BitSerializer 源码生成器在序列化完成后会：
/// 1. Reset(initialValue)
/// 2. Update(数据字节) - 可能多次调用
/// 3. 读取 Result 写入 CRC 字段
/// 实现类必须有无参构造函数。
/// </summary>
public interface IBitCrcAlgorithm
{
    int BitWidth { get; }

    void Reset(ulong initialValue);

    void Update(ReadOnlySpan<byte> data);

    ulong Result { get; }
}
