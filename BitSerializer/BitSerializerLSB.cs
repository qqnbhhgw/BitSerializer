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

    public static byte[] Serialize<T>(T obj)
    {
        return Instance.Serialize(obj);
    }

    public static void Serialize<T>(T obj, Span<byte> bytes)
    {
        Instance.Serialize(obj, bytes);
    }
}
