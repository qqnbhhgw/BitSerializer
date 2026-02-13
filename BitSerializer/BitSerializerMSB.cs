#nullable enable

using System.Runtime.CompilerServices;

namespace BitSerializer;

public static class BitSerializerMSB
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IBitSerializable, new()
    {
        var result = new T();
        result.DeserializeMSB(bytes, 0);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(byte[] bytes) where T : IBitSerializable, new()
    {
        return Deserialize<T>((ReadOnlySpan<byte>)bytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Deserialize(ReadOnlySpan<byte> bytes, Type type)
    {
        return BitSerializerRegistry.DeserializeMSB(bytes, type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object Deserialize(byte[] bytes, Type type)
    {
        return Deserialize((ReadOnlySpan<byte>)bytes, type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(T obj) where T : IBitSerializable
    {
        int totalBits = obj.GetTotalBitLength();
        var bytes = new byte[(totalBits + 7) / 8];
        obj.SerializeMSB(bytes, 0);
        return bytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Serialize<T>(T obj, Span<byte> bytes) where T : IBitSerializable
    {
        obj.SerializeMSB(bytes, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize(object obj, Type type)
    {
        return BitSerializerRegistry.SerializeMSB(obj, type);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Serialize(object obj, Type type, Span<byte> bytes)
    {
        BitSerializerRegistry.SerializeMSB(obj, type, bytes);
    }
}
