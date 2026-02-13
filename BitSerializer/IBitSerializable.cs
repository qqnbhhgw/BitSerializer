namespace BitSerializer;

public interface IBitSerializable
{
    int SerializeLSB(Span<byte> bytes, int bitOffset);
    int SerializeMSB(Span<byte> bytes, int bitOffset);
    int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset);
    int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset);
    int GetTotalBitLength();
}
