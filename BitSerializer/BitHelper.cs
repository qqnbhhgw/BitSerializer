using System.Runtime.CompilerServices;

namespace BitSerializer;

public static class BitHelperLSB
{
    public static T ValueLength<T>(ReadOnlySpan<byte> bytes, int startIndex, int length)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        return ValueRange<T>(bytes, startIndex, startIndex + length);
    }

    public static T ValueRange<T>(ReadOnlySpan<byte> bytes,
        int startIndex,
        int endIndex,
        [CallerArgumentExpression("startIndex")]
        string? startIndexCallerName = null,
        [CallerArgumentExpression("endIndex")] string? endIndexCallerName = null)
    {
        var bitCount = endIndex - startIndex;
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (bitCount <= 0 || bitCount > maxBits)
            throw new ArgumentException($"{endIndexCallerName} - {startIndexCallerName}的差值必须在1-{maxBits}之间");

        // 先提取为 ulong，然后转换为目标类型
        var result = ExtractBitsToULongLSB(bytes, startIndex, endIndex);
        return UnsafeConvertHelper.ConvertTo<T>(result);
    }

    private static ulong ExtractBitsToULongLSB(ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
    {
        ulong result = 0;
        var startByte = startIndex / 8;
        var startBit = startIndex % 8;
        var endByte = (endIndex - 1) / 8;

        // 优化：按字节处理
        for (var byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            var currentByte = bytes[byteIdx];
            var firstBit = byteIdx == startByte ? startBit : 0;
            var lastBit = byteIdx == endByte ? (endIndex - 1) % 8 : 7;

            for (var bit = firstBit; bit <= lastBit; bit++)
            {
                if (((currentByte >> bit) & 1) == 1)
                {
                    var resultBit = byteIdx * 8 + bit - startIndex;
                    result |= 1UL << resultBit;
                }
            }
        }

        return result;
    }
}

public class BitHelperMSB
{
    public static T ValueLength<T>(ReadOnlySpan<byte> bytes, int startIndex, int length)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        return ValueRange<T>(bytes, startIndex, startIndex + length);
    }

    public static T ValueRange<T>(ReadOnlySpan<byte> bytes,
        int startIndex,
        int endIndex,
        [CallerArgumentExpression("startIndex")]
        string startIndexCallerName = null,
        [CallerArgumentExpression("endIndex")] string endIndexCallerName = null)
    {
        var bitCount = endIndex - startIndex;
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (bitCount <= 0 || bitCount > maxBits)
            throw new ArgumentException($"{endIndexCallerName} - {startIndexCallerName}的差值必须在1-{maxBits}之间");

        // 先提取为 ulong，然后转换为目标类型
        var result = ExtractBitsToULongMSB(bytes, startIndex, endIndex);
        return UnsafeConvertHelper.ConvertTo<T>(result);
    }

    public static void SetValueLength<T>(Span<byte> bytes, int startIndex, int length, T value)
    {
        var maxBits = Unsafe.SizeOf<T>() * 8;
        if (length <= 0 || length > maxBits)
            throw new ArgumentException($"length 必须在1-{maxBits}之间");

        SetValueRange(bytes, startIndex, startIndex + length, value);
    }

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
        ulong result = 0;
        var bitCount = endIndex - startIndex;
        var startByte = startIndex / 8;
        var startBit = startIndex % 8;
        var endByte = (endIndex - 1) / 8;

        // MSB模式：字节内位序从高到低 (bit 7 是第一位，bit 0 是最后一位)
        for (var byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            var currentByte = bytes[byteIdx];
            var firstBit = byteIdx == startByte ? startBit : 0;
            var lastBit = byteIdx == endByte ? (endIndex - 1) % 8 : 7;

            for (var bit = firstBit; bit <= lastBit; bit++)
            {
                // MSB模式：bit 0 对应字节的 bit 7，bit 7 对应字节的 bit 0
                if (((currentByte >> (7 - bit)) & 1) == 1)
                {
                    // 计算在结果中的位置（从高位开始）
                    var bitPosition = byteIdx * 8 + bit - startIndex;
                    // 结果中的位位置：最高位在左边
                    result |= 1UL << (bitCount - 1 - bitPosition);
                }
            }
        }

        return result;
    }

    private static void WriteBitsFromULongMSB(Span<byte> bytes, int startIndex, int endIndex, ulong value)
    {
        var bitCount = endIndex - startIndex;
        var startByte = startIndex / 8;
        var startBit = startIndex % 8;
        var endByte = (endIndex - 1) / 8;

        // MSB模式：字节内位序从高到低 (bit 7 是第一位，bit 0 是最后一位)
        for (var byteIdx = startByte; byteIdx <= endByte && byteIdx < bytes.Length; byteIdx++)
        {
            var firstBit = byteIdx == startByte ? startBit : 0;
            var lastBit = byteIdx == endByte ? (endIndex - 1) % 8 : 7;

            for (var bit = firstBit; bit <= lastBit; bit++)
            {
                // 计算在值中的位置（从高位开始）
                var bitPosition = byteIdx * 8 + bit - startIndex;
                var valueBitPosition = bitCount - 1 - bitPosition;

                // 获取值的对应位
                var bitValue = (value >> valueBitPosition) & 1;

                // MSB模式：bit 0 对应字节的 bit 7，bit 7 对应字节的 bit 0
                var byteBitPosition = 7 - bit;

                if (bitValue == 1)
                {
                    // 设置位为1
                    bytes[byteIdx] |= (byte)(1 << byteBitPosition);
                }
                else
                {
                    // 设置位为0
                    bytes[byteIdx] &= (byte)~(1 << byteBitPosition);
                }
            }
        }
    }
}

internal static class UnsafeConvertHelper
{
    public static T ConvertTo<T>(ulong value)
    {
        // 处理枚举类型 - 根据底层类型转换，避免装箱
        if (typeof(T).IsEnum)
        {
            var underlyingType = Enum.GetUnderlyingType(typeof(T));
            if (underlyingType == typeof(byte))
            {
                var v = (byte)value;
                return Unsafe.As<byte, T>(ref v);
            }

            if (underlyingType == typeof(sbyte))
            {
                var v = (sbyte)value;
                return Unsafe.As<sbyte, T>(ref v);
            }

            if (underlyingType == typeof(short))
            {
                var v = (short)value;
                return Unsafe.As<short, T>(ref v);
            }

            if (underlyingType == typeof(ushort))
            {
                var v = (ushort)value;
                return Unsafe.As<ushort, T>(ref v);
            }

            if (underlyingType == typeof(int))
            {
                var v = (int)value;
                return Unsafe.As<int, T>(ref v);
            }

            if (underlyingType == typeof(uint))
            {
                var v = (uint)value;
                return Unsafe.As<uint, T>(ref v);
            }

            if (underlyingType == typeof(long))
            {
                var v = (long)value;
                return Unsafe.As<long, T>(ref v);
            }

            if (underlyingType == typeof(ulong))
            {
                return Unsafe.As<ulong, T>(ref value);
            }
        }

        // 处理数值类型 - 使用 Unsafe.As 避免装箱
        if (typeof(T) == typeof(byte))
        {
            var v = (byte)value;
            return Unsafe.As<byte, T>(ref v);
        }

        if (typeof(T) == typeof(sbyte))
        {
            var v = (sbyte)value;
            return Unsafe.As<sbyte, T>(ref v);
        }

        if (typeof(T) == typeof(short))
        {
            var v = (short)value;
            return Unsafe.As<short, T>(ref v);
        }

        if (typeof(T) == typeof(ushort))
        {
            var v = (ushort)value;
            return Unsafe.As<ushort, T>(ref v);
        }

        if (typeof(T) == typeof(int))
        {
            var v = (int)value;
            return Unsafe.As<int, T>(ref v);
        }

        if (typeof(T) == typeof(uint))
        {
            var v = (uint)value;
            return Unsafe.As<uint, T>(ref v);
        }

        if (typeof(T) == typeof(long))
        {
            var v = (long)value;
            return Unsafe.As<long, T>(ref v);
        }

        if (typeof(T) == typeof(ulong))
        {
            return Unsafe.As<ulong, T>(ref value);
        }

        throw new NotSupportedException($"不支持的类型: {typeof(T)}");
    }

    public static ulong ConvertFrom<T>(T value)
    {
        // 处理枚举类型
        if (typeof(T).IsEnum)
        {
            var underlyingType = Enum.GetUnderlyingType(typeof(T));
            if (underlyingType == typeof(byte))
            {
                var v = Unsafe.As<T, byte>(ref value);
                return v;
            }

            if (underlyingType == typeof(sbyte))
            {
                var v = Unsafe.As<T, sbyte>(ref value);
                return (ulong)v;
            }

            if (underlyingType == typeof(short))
            {
                var v = Unsafe.As<T, short>(ref value);
                return (ulong)v;
            }

            if (underlyingType == typeof(ushort))
            {
                var v = Unsafe.As<T, ushort>(ref value);
                return v;
            }

            if (underlyingType == typeof(int))
            {
                var v = Unsafe.As<T, int>(ref value);
                return (ulong)v;
            }

            if (underlyingType == typeof(uint))
            {
                var v = Unsafe.As<T, uint>(ref value);
                return v;
            }

            if (underlyingType == typeof(long))
            {
                var v = Unsafe.As<T, long>(ref value);
                return (ulong)v;
            }

            if (underlyingType == typeof(ulong))
            {
                return Unsafe.As<T, ulong>(ref value);
            }
        }

        // 处理数值类型
        if (typeof(T) == typeof(byte))
        {
            return Unsafe.As<T, byte>(ref value);
        }

        if (typeof(T) == typeof(sbyte))
        {
            var v = Unsafe.As<T, sbyte>(ref value);
            return (ulong)v;
        }

        if (typeof(T) == typeof(short))
        {
            var v = Unsafe.As<T, short>(ref value);
            return (ulong)v;
        }

        if (typeof(T) == typeof(ushort))
        {
            return Unsafe.As<T, ushort>(ref value);
        }

        if (typeof(T) == typeof(int))
        {
            var v = Unsafe.As<T, int>(ref value);
            return (ulong)v;
        }

        if (typeof(T) == typeof(uint))
        {
            return Unsafe.As<T, uint>(ref value);
        }

        if (typeof(T) == typeof(long))
        {
            var v = Unsafe.As<T, long>(ref value);
            return (ulong)v;
        }

        if (typeof(T) == typeof(ulong))
        {
            return Unsafe.As<T, ulong>(ref value);
        }

        throw new NotSupportedException($"不支持的类型: {typeof(T)}");
    }
}
