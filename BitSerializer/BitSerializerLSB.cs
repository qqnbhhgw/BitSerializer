#nullable enable

namespace BitSerializer;

public static class BitSerializerLSB
{
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IBitSerializable, new()
    {
        var result = new T();
        result.DeserializeLSB(bytes, 0);
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
        obj.SerializeLSB(bytes, 0);
        return bytes;
    }

    public static void Serialize<T>(T obj, Span<byte> bytes) where T : IBitSerializable
    {
        obj.SerializeLSB(bytes, 0);
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
