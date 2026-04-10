namespace BitSerializer;

internal interface ITypeSerializer
{
    object Deserialize(ReadOnlySpan<byte> bytes, bool isMSB);
    void Serialize(object obj, Span<byte> bytes, bool isMSB);
    int GetBitLength(object obj);
}

internal class TypeSerializer<T> : ITypeSerializer where T : IBitSerializable, new()
{
    public object Deserialize(ReadOnlySpan<byte> bytes, bool isMSB)
    {
        var result = new T();
        var ctx = result.DeserializeContext();
        result.BeforeDeserialize(ctx, bytes);
        if (isMSB)
            result.DeserializeMSB(bytes, 0, ctx);
        else
            result.DeserializeLSB(bytes, 0, ctx);
        result.AfterDeserialize(ctx, bytes);
        return result;
    }

    public void Serialize(object obj, Span<byte> bytes, bool isMSB)
    {
        var typed = (T)obj;
        var ctx = typed.SerializeContext();
        typed.BeforeSerialize(ctx, bytes);
        if (isMSB)
            typed.SerializeMSB(bytes, 0, ctx);
        else
            typed.SerializeLSB(bytes, 0, ctx);
        typed.AfterSerialize(ctx, bytes);
    }

    public int GetBitLength(object obj) => ((T)obj).GetTotalBitLength();
}

public static class BitSerializerRegistry
{
    private static readonly Dictionary<Type, ITypeSerializer> Serializers = new();

    public static void Register<T>() where T : IBitSerializable, new()
    {
        Serializers[typeof(T)] = new TypeSerializer<T>();
    }

    internal static object DeserializeLSB(ReadOnlySpan<byte> bytes, Type type)
    {
        return GetSerializer(type).Deserialize(bytes, false);
    }

    internal static object DeserializeMSB(ReadOnlySpan<byte> bytes, Type type)
    {
        return GetSerializer(type).Deserialize(bytes, true);
    }

    internal static byte[] SerializeLSB(object obj, Type type)
    {
        var serializer = GetSerializer(type);
        int totalBits = serializer.GetBitLength(obj);
        var bytes = new byte[(totalBits + 7) / 8];
        serializer.Serialize(obj, bytes, false);
        return bytes;
    }

    internal static byte[] SerializeMSB(object obj, Type type)
    {
        var serializer = GetSerializer(type);
        int totalBits = serializer.GetBitLength(obj);
        var bytes = new byte[(totalBits + 7) / 8];
        serializer.Serialize(obj, bytes, true);
        return bytes;
    }

    internal static void SerializeLSB(object obj, Type type, Span<byte> bytes)
    {
        GetSerializer(type).Serialize(obj, bytes, false);
    }

    internal static void SerializeMSB(object obj, Type type, Span<byte> bytes)
    {
        GetSerializer(type).Serialize(obj, bytes, true);
    }

    private static ITypeSerializer GetSerializer(Type type)
    {
        if (!Serializers.TryGetValue(type, out var serializer))
            throw new InvalidOperationException($"Type '{type.Name}' is not registered. Add [BitSerialize] attribute.");
        return serializer;
    }
}
