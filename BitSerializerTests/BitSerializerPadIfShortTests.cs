using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public partial class BitSerializerPadIfShortTests
{
    [BitSerialize]
    public partial class PaddedArrayPacket
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField, BitFieldCount(8, PadIfShort = true)]
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }

    [BitSerialize]
    public partial class FramedPaddedPacket
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField, BitFieldCount(8, PadIfShort = true)]
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();

        [BitField(8)]
        public byte Trailer { get; set; }
    }

    [Fact]
    public void PadIfShort_SerializeShortArray_PadsWithZeros()
    {
        var data = new PaddedArrayPacket
        {
            Header = 0xAA,
            Payload = new byte[] { 0x01, 0x02, 0x03 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(9);
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x01);
        bytes[2].ShouldBe((byte)0x02);
        bytes[3].ShouldBe((byte)0x03);
        for (int i = 4; i < 9; i++) bytes[i].ShouldBe((byte)0);
    }

    [Fact]
    public void PadIfShort_SerializeExactArray()
    {
        var data = new PaddedArrayPacket
        {
            Header = 0xAA,
            Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(9);
        bytes[1..9].ShouldBe(data.Payload);
    }

    [Fact]
    public void PadIfShort_DeserializeShortStream_PadsWithDefault()
    {
        // Only header + 4 bytes of payload = 5 bytes total (3 bytes short of full 9).
        byte[] shortBytes = new byte[] { 0xAA, 0x11, 0x22, 0x33, 0x44 };
        var result = BitSerializerMSB.Deserialize<PaddedArrayPacket>(shortBytes);
        result.Header.ShouldBe((byte)0xAA);
        result.Payload.Length.ShouldBe(8);
        result.Payload[0].ShouldBe((byte)0x11);
        result.Payload[1].ShouldBe((byte)0x22);
        result.Payload[2].ShouldBe((byte)0x33);
        result.Payload[3].ShouldBe((byte)0x44);
        for (int i = 4; i < 8; i++) result.Payload[i].ShouldBe((byte)0);
    }

    [Fact]
    public void PadIfShort_FullStream_RoundTrips()
    {
        var original = new PaddedArrayPacket
        {
            Header = 0x55,
            Payload = new byte[] { 0x10, 0x20 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var rt = BitSerializerMSB.Deserialize<PaddedArrayPacket>(bytes);
        rt.Header.ShouldBe(original.Header);
        rt.Payload.Length.ShouldBe(8);
        rt.Payload[0].ShouldBe((byte)0x10);
        rt.Payload[1].ShouldBe((byte)0x20);
        for (int i = 2; i < 8; i++) rt.Payload[i].ShouldBe((byte)0);
    }

    [Fact]
    public void PadIfShort_NullArray_WritesAllZeros()
    {
        var data = new PaddedArrayPacket { Header = 0xAA, Payload = null! };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(9);
        bytes[0].ShouldBe((byte)0xAA);
        for (int i = 1; i < 9; i++) bytes[i].ShouldBe((byte)0);
    }

    [Fact]
    public void PadIfShort_WithTrailerField_FullStreamRoundTrips()
    {
        // With a trailer after a PadIfShort field, the caller must pass the full expected byte length.
        var original = new FramedPaddedPacket
        {
            Header = 0x7E,
            Payload = new byte[] { 0xAA },
            Trailer = 0xCF
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(10);
        bytes[0].ShouldBe((byte)0x7E);
        bytes[1].ShouldBe((byte)0xAA);
        for (int i = 2; i < 9; i++) bytes[i].ShouldBe((byte)0);
        bytes[9].ShouldBe((byte)0xCF);

        var rt = BitSerializerMSB.Deserialize<FramedPaddedPacket>(bytes);
        rt.Header.ShouldBe((byte)0x7E);
        rt.Payload.Length.ShouldBe(8);
        rt.Payload[0].ShouldBe((byte)0xAA);
        rt.Trailer.ShouldBe((byte)0xCF);
    }
}
