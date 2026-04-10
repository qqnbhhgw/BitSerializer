#nullable enable

namespace BitSerializer;

public static class BitSerializerLSB
{
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IBitSerializable, new()
    {
        var result = new T();
        var ctx = result.DeserializeContext();
        result.BeforeDeserialize(ctx, bytes);
        result.DeserializeLSB(bytes, 0, ctx);
        result.AfterDeserialize(ctx, bytes);
        return result;
    }

    public static T Deserialize<T>(byte[] bytes) where T : IBitSerializable, new()
    {
        return Deserialize<T>((ReadOnlySpan<byte>)bytes);
    }

    public static object Deserialize(ReadOnlySpan<byte> bytes, Type type)
    {
        return BitSerializerRegistry.DeserializeLSB(bytes, type);
    }

    public static object Deserialize(byte[] bytes, Type type)
    {
        return Deserialize((ReadOnlySpan<byte>)bytes, type);
    }

    public static byte[] Serialize<T>(T obj) where T : IBitSerializable
    {
        int totalBits = obj.GetTotalBitLength();
        var bytes = new byte[(totalBits + 7) / 8];
        var ctx = obj.SerializeContext();
        obj.BeforeSerialize(ctx, bytes);
        obj.SerializeLSB(bytes, 0, ctx);
        obj.AfterSerialize(ctx, bytes);
        return bytes;
    }

    public static void Serialize<T>(T obj, Span<byte> bytes) where T : IBitSerializable
    {
        var ctx = obj.SerializeContext();
        obj.BeforeSerialize(ctx, bytes);
        obj.SerializeLSB(bytes, 0, ctx);
        obj.AfterSerialize(ctx, bytes);
    }

    public static byte[] Serialize(object obj, Type type)
    {
        return BitSerializerRegistry.SerializeLSB(obj, type);
    }

    public static void Serialize(object obj, Type type, Span<byte> bytes)
    {
        BitSerializerRegistry.SerializeLSB(obj, type, bytes);
    }
}
