using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace BitSerializer;

public static class BitHelperLSB
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ValueLength<T>(ReadOnlySpan<byte> bytes, int startIndex, int length)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        return ValueRange<T>(bytes, startIndex, startIndex + length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ValueRange<T>(ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
    {
        // 先提取为 ulong，然后转换为目标类型
        var result = ExtractBitsToULongLSB(bytes, startIndex, endIndex);
        return UnsafeConvertHelper.ConvertTo<T>(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetValueLength<T>(Span<byte> bytes, int startIndex, int length, T value)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        SetValueRange(bytes, startIndex, startIndex + length, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetValueRange<T>(Span<byte> bytes, int startIndex, int endIndex, T value)
    {
        // 转换值为 ulong
        var ulongValue = UnsafeConvertHelper.ConvertFrom(value);

        // 写入位到字节数组
        WriteBitsFromULongLSB(bytes, startIndex, endIndex, ulongValue);
    }

    private static ulong ExtractBitsToULongLSB(ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
    {
        int bitCount = endIndex - startIndex;
        int startByte = startIndex >> 3;
        int startBit = startIndex & 7;

        // 快速路径：单次 8 字节读取 + 移位 + 掩码
        if (startByte + 8 <= bytes.Length && startBit + bitCount <= 64)
        {
            ulong raw = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(startByte));
            ulong mask = bitCount == 64 ? ulong.MaxValue : (1UL << bitCount) - 1;
            return (raw >> startBit) & mask;
        }

        // 通用路径：按字节掩码提取
        ulong result = 0;
        int endByte = (endIndex - 1) >> 3;
        int resultShift = 0;
        for (int byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            int lo = byteIdx == startByte ? startBit : 0;
            int hi = byteIdx == endByte ? (endIndex - 1) & 7 : 7;
            int bits = hi - lo + 1;
            ulong extracted = (ulong)((bytes[byteIdx] >> lo) & ((1 << bits) - 1));
            result |= extracted << resultShift;
            resultShift += bits;
        }

        return result;
    }

    private static void WriteBitsFromULongLSB(Span<byte> bytes, int startIndex, int endIndex, ulong value)
    {
        int bitCount = endIndex - startIndex;
        int startByte = startIndex >> 3;
        int startBit = startIndex & 7;

        // 快速路径：单次 8 字节读-改-写
        if (startByte + 8 <= bytes.Length && startBit + bitCount <= 64)
        {
            ulong mask = (bitCount == 64 ? ulong.MaxValue : (1UL << bitCount) - 1) << startBit;
            ulong raw = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(startByte));
            raw = (raw & ~mask) | ((value << startBit) & mask);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(startByte), raw);
            return;
        }

        // 通用路径：按字节掩码写入
        int endByte = (endIndex - 1) >> 3;
        int valueShift = 0;
        for (int byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            int lo = byteIdx == startByte ? startBit : 0;
            int hi = byteIdx == endByte ? (endIndex - 1) & 7 : 7;
            int bits = hi - lo + 1;
            int mask = ((1 << bits) - 1) << lo;
            byte valueBits = (byte)(((value >> valueShift) & ((1UL << bits) - 1)) << lo);
            bytes[byteIdx] = (byte)((bytes[byteIdx] & ~mask) | valueBits);
            valueShift += bits;
        }
    }
}

public class BitHelperMSB
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ValueLength<T>(ReadOnlySpan<byte> bytes, int startIndex, int length)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        return ValueRange<T>(bytes, startIndex, startIndex + length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ValueRange<T>(ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
    {
        // 先提取为 ulong，然后转换为目标类型
        return UnsafeConvertHelper.ConvertTo<T>(ExtractBitsToULongMSB(bytes, startIndex, endIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetValueLength<T>(Span<byte> bytes, int startIndex, int length, T value)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        SetValueRange(bytes, startIndex, startIndex + length, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetValueRange<T>(Span<byte> bytes, int startIndex, int endIndex, T value)
    {
        var bitCount = endIndex - startIndex;
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (bitCount <= 0 || bitCount > maxBits)
            throw new ArgumentException($"位长度必须在1-{maxBits}之间");

        // 转换值为 ulong
        var ulongValue = UnsafeConvertHelper.ConvertFrom(value);

        // 写入位到字节数组
        WriteBitsFromULongMSB(bytes, startIndex, endIndex, ulongValue);
    }

    private static ulong ExtractBitsToULongMSB(ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
    {
        int bitCount = endIndex - startIndex;
        int startByte = startIndex >> 3;
        int startBit = startIndex & 7;

        // 快速路径：单次 8 字节读取
        if (startByte + 8 <= bytes.Length && startBit + bitCount <= 64)
        {
            ulong raw = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(startByte));
            int shift = 64 - startBit - bitCount;
            ulong mask = bitCount == 64 ? ulong.MaxValue : (1UL << bitCount) - 1;
            return (raw >> shift) & mask;
        }

        // 通用路径：按字节掩码提取
        ulong result = 0;
        int endByte = (endIndex - 1) >> 3;
        int fieldPos = 0;
        for (int byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            int lo = byteIdx == startByte ? startBit : 0;
            int hi = byteIdx == endByte ? (endIndex - 1) & 7 : 7;
            int bits = hi - lo + 1;
            // MSB: 字节内 bit lo 对应 byte bit (7-lo)，提取 [7-hi..7-lo]
            ulong extracted = (ulong)((bytes[byteIdx] >> (7 - hi)) & ((1 << bits) - 1));
            int shift2 = bitCount - fieldPos - bits;
            result |= extracted << shift2;
            fieldPos += bits;
        }

        return result;
    }

    private static void WriteBitsFromULongMSB(Span<byte> bytes, int startIndex, int endIndex, ulong value)
    {
        int bitCount = endIndex - startIndex;
        int startByte = startIndex >> 3;
        int startBit = startIndex & 7;

        // 快速路径：单次 8 字节读-改-写
        if (startByte + 8 <= bytes.Length && startBit + bitCount <= 64)
        {
            int shift = 64 - startBit - bitCount;
            ulong mask = (bitCount == 64 ? ulong.MaxValue : (1UL << bitCount) - 1) << shift;
            ulong raw = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(startByte));
            raw = (raw & ~mask) | ((value << shift) & mask);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(startByte), raw);
            return;
        }

        // 通用路径：按字节掩码写入
        int endByte = (endIndex - 1) >> 3;
        int fieldPos = 0;
        for (int byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            int lo = byteIdx == startByte ? startBit : 0;
            int hi = byteIdx == endByte ? (endIndex - 1) & 7 : 7;
            int bits = hi - lo + 1;
            // 从 value 中提取对应位（MSB 序）
            int valueShift = bitCount - fieldPos - bits;
            byte valueBits = (byte)((value >> valueShift) & ((1UL << bits) - 1));
            // 写入字节的 [7-hi..7-lo] 位置
            int byteShift = 7 - hi;
            int mask = ((1 << bits) - 1) << byteShift;
            bytes[byteIdx] = (byte)((bytes[byteIdx] & ~mask) | (valueBits << byteShift));
            fieldPos += bits;
        }
    }
}

public static class UnsafeConvertHelper
{
    /// <summary>
    /// 将 ulong 转换为目标类型 T。
    /// 利用 LE 内存布局，ulong 的低字节直接对应小类型的值，无需逐类型分支。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ConvertTo<T>(ulong value)
    {
        return Unsafe.As<ulong, T>(ref value);
    }

    /// <summary>
    /// 将类型 T 的值转换为 ulong。
    /// 先清零再写入，确保高位字节为 0（零扩展）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ConvertFrom<T>(T value)
    {
        ulong result = 0;
        Unsafe.As<ulong, T>(ref result) = value;
        return result;
    }
}