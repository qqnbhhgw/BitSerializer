#nullable enable

namespace BitSerializer;

public static class BitSerializerLSB
{
    private static readonly BitSerializerBase Instance = new(typeof(BitHelperLSB));

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : new()
    {
        return Instance.Deserialize<T>(bytes);
    }

    public static T Deserialize<T>(byte[] bytes) where T : new()
    {
        return Instance.Deserialize<T>(bytes);
    }

    public static object Deserialize(ReadOnlySpan<byte> bytes, Type type)
    {
        return Instance.Deserialize(bytes, type);
    }

    public static object Deserialize(byte[] bytes, Type type)
    {
        return Instance.Deserialize(bytes, type);
    }

    public static byte[] Serialize<T>(T obj)
    {
        return Instance.Serialize(obj);
    }

    public static void Serialize<T>(T obj, Span<byte> bytes)
    {
        Instance.Serialize(obj, bytes);
    }

    public static byte[] Serialize(object obj, Type type)
    {
        return Instance.Serialize(obj, type);
    }

    public static void Serialize(object obj, Type type, Span<byte> bytes)
    {
        Instance.Serialize(obj, type, bytes);
    }
}
