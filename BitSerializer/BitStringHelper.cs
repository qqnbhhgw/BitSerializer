#nullable enable

using System.Text;

namespace BitSerializer;

public static class BitStringHelper
{
    public static int GetByteCount(string? value, BitStringEncoding encoding, int maxBytes = 0, bool stopAtNull = false)
    {
        ReadOnlySpan<char> chars = GetChars(value, stopAtNull);
        Encoding textEncoding = GetEncoding(encoding);
        int byteCount = textEncoding.GetByteCount(chars);
        if (maxBytes <= 0 || byteCount <= maxBytes)
            return byteCount;

        if (encoding == BitStringEncoding.ASCII)
            return maxBytes;

        int charCount = GetPrefixCharCount(chars, textEncoding, maxBytes);
        return textEncoding.GetByteCount(chars[..charCount]);
    }

    public static int Encode(string? value, BitStringEncoding encoding, Span<byte> destination, int maxBytes = 0, bool stopAtNull = false)
    {
        ReadOnlySpan<char> chars = GetChars(value, stopAtNull);
        Encoding textEncoding = GetEncoding(encoding);
        int byteLimit = maxBytes > 0 ? Math.Min(maxBytes, destination.Length) : destination.Length;
        if (encoding == BitStringEncoding.ASCII)
        {
            int asciiCharCount = Math.Min(chars.Length, byteLimit);
            return textEncoding.GetBytes(chars[..asciiCharCount], destination[..byteLimit]);
        }

        int charCount = GetPrefixCharCount(chars, textEncoding, byteLimit);
        return textEncoding.GetBytes(chars[..charCount], destination[..byteLimit]);
    }

    public static string Decode(ReadOnlySpan<byte> bytes, BitStringEncoding encoding)
        => GetEncoding(encoding).GetString(bytes);

    private static ReadOnlySpan<char> GetChars(string? value, bool stopAtNull)
    {
        ReadOnlySpan<char> chars = value.AsSpan();
        if (stopAtNull)
        {
            int terminator = chars.IndexOf('\0');
            if (terminator >= 0)
                chars = chars[..terminator];
        }
        return chars;
    }

    private static int GetPrefixCharCount(ReadOnlySpan<char> chars, Encoding encoding, int maxBytes)
    {
        if (chars.IsEmpty || maxBytes <= 0)
            return 0;
        if (encoding.GetByteCount(chars) <= maxBytes)
            return chars.Length;

        int low = 0;
        int high = chars.Length;
        while (low < high)
        {
            int middle = low + ((high - low + 1) >> 1);
            if (SplitsSurrogatePair(chars, middle))
                middle--;
            if (middle <= low)
                break;
            if (encoding.GetByteCount(chars[..middle]) <= maxBytes)
                low = middle;
            else
                high = middle - 1;
        }

        while (low > 0 && encoding.GetByteCount(chars[..low]) > maxBytes)
            low--;
        if (SplitsSurrogatePair(chars, low))
            low--;
        return low;
    }

    private static bool SplitsSurrogatePair(ReadOnlySpan<char> chars, int index)
        => index > 0 && index < chars.Length && char.IsHighSurrogate(chars[index - 1]) && char.IsLowSurrogate(chars[index]);

    private static Encoding GetEncoding(BitStringEncoding encoding)
        => encoding == BitStringEncoding.UTF8 ? Encoding.UTF8 : Encoding.ASCII;
}
