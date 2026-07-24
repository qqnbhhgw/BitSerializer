#nullable enable

using System.Buffers;
using System.Runtime.CompilerServices;

namespace BitSerializer;

public static class BitSerializerMSB
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetRequiredByteCount<T>(T obj) where T : IBitSerializable
        => (obj.GetTotalBitLength() + 7) / 8;

    public static OperationStatus TrySerialize<T>(T obj, Span<byte> destination, out int bytesWritten)
        where T : IBitSerializable
    {
        int requiredBytes = GetRequiredByteCount(obj);
        bytesWritten = 0;
        if (destination.Length < requiredBytes)
            return OperationStatus.DestinationTooSmall;

        var target = destination[..requiredBytes];
        target.Clear();
        var ctx = obj.SerializeContext();
        obj.BeforeSerialize(ctx, target);
        obj.SerializeMSB(target, 0, ctx);
        obj.AfterSerialize(ctx, target);
        bytesWritten = requiredBytes;
        return OperationStatus.Done;
    }

    public static OperationStatus TryDeserialize<T>(ReadOnlySpan<byte> source, ref T value, out int bytesConsumed)
        where T : IBitSerializable
    {
        bytesConsumed = 0;
        var ctx = value.DeserializeContext();
        value.BeforeDeserialize(ctx, source);
        int bitsConsumed;
        try
        {
            bitsConsumed = value.DeserializeMSBInto(source, 0, ctx, true);
        }
        catch (BitSerializationCapacityException)
        {
            return OperationStatus.DestinationTooSmall;
        }
        catch (BitSerializationNeedMoreDataException)
        {
            return OperationStatus.NeedMoreData;
        }
        catch (System.IO.InvalidDataException)
        {
            return OperationStatus.InvalidData;
        }
        if (bitsConsumed > source.Length * 8)
            return OperationStatus.NeedMoreData;
        value.AfterDeserialize(ctx, source);
        bytesConsumed = (bitsConsumed + 7) / 8;
        return OperationStatus.Done;
    }

    public static OperationStatus TryDeserializeInto<T>(ReadOnlySpan<byte> source, T destination, out int bytesConsumed)
        where T : class, IBitSerializable
    {
        ArgumentNullException.ThrowIfNull(destination);
        return TryDeserialize(source, ref destination, out bytesConsumed);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IBitSerializable, new()
    {
        var result = new T();
        var ctx = result.DeserializeContext();
        result.BeforeDeserialize(ctx, bytes);
        try
        {
            result.DeserializeMSB(bytes, 0, ctx);
        }
        catch (BitSerializationNeedMoreDataException exception)
        {
            throw new System.IO.InvalidDataException(exception.Message, exception);
        }
        result.AfterDeserialize(ctx, bytes);
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
        var bytes = new byte[GetRequiredByteCount(obj)];
        TrySerialize(obj, bytes, out _);
        return bytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Serialize<T>(T obj, Span<byte> bytes) where T : IBitSerializable
    {
        int requiredBytes = GetRequiredByteCount(obj);
        if (bytes.Length < requiredBytes)
            throw new ArgumentException($"Destination requires at least {requiredBytes} bytes.", nameof(bytes));
        var ctx = obj.SerializeContext();
        obj.BeforeSerialize(ctx, bytes);
        obj.SerializeMSB(bytes, 0, ctx);
        obj.AfterSerialize(ctx, bytes);
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
