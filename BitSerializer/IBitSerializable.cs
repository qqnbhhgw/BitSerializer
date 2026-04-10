#nullable enable

namespace BitSerializer;

public interface IBitSerializable
{
    int SerializeLSB(Span<byte> bytes, int bitOffset);
    int SerializeMSB(Span<byte> bytes, int bitOffset);
    int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset);
    int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset);
    int GetTotalBitLength();
    int SerializeLSB(Span<byte> bytes, int bitOffset, object? context) => SerializeLSB(bytes, bitOffset);
    int SerializeMSB(Span<byte> bytes, int bitOffset, object? context) => SerializeMSB(bytes, bitOffset);
    int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context) => DeserializeLSB(bytes, bitOffset);
    int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context) => DeserializeMSB(bytes, bitOffset);
    /// <summary>
    /// 返回序列化上下文对象，传递给嵌套类型的序列化方法和值转换器。
    /// 上下文仅用于辅助序列化逻辑（如值转换），不应改变总位长度。
    /// </summary>
    object? SerializeContext() => null;

    /// <summary>
    /// 序列化前回调。不应改变总位长度。
    /// </summary>
    void BeforeSerialize(object? serializeContext, Span<byte> afterSerializedBytes)
    {
    }

    /// <summary>
    /// 序列化后回调。不应改变总位长度。
    /// </summary>
    void AfterSerialize(object? serializeContext, Span<byte> afterSerializedBytes)
    {
    }

    /// <summary>
    /// 返回反序列化上下文对象，传递给嵌套类型的反序列化方法和值转换器。
    /// 上下文仅用于辅助反序列化逻辑（如值转换），不应改变总位长度。
    /// </summary>
    object? DeserializeContext() => null;

    /// <summary>
    /// 反序列化前回调。不应改变总位长度。
    /// </summary>
    void BeforeDeserialize(object? deserializeContext, ReadOnlySpan<byte> bytes)
    {
    }

    /// <summary>
    /// 反序列化后回调。不应改变总位长度。
    /// </summary>
    void AfterDeserialize(object? deserializeContext, ReadOnlySpan<byte> bytes)
    {
    }
}