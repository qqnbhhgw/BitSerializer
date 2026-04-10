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
    object? SerializeContext() => null;

    void BeforeSerialize(object? serializeContext, Span<byte> afterSerializedBytes)
    {
    }

    void AfterSerialize(object? serializeContext, Span<byte> afterSerializedBytes)
    {
    }

    object? DeserializeContext() => null;

    void BeforeDeserialize(object? deserializeContext, ReadOnlySpan<byte> bytes)
    {
    }

    void AfterDeserialize(object? deserializeContext, ReadOnlySpan<byte> bytes)
    {
    }
}