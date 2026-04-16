using Shouldly;
using BitSerializer;
using BitSerializer.CrcAlgorithms;

namespace BitSerializerTests;

public partial class BitSerializerCrcTests
{
    #region Algorithm golden vectors

    [Fact]
    public void CrcCcitt_MatchesKnownVectors()
    {
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 }); // "123456789"
        algo.Result.ShouldBe(0x31C3UL);
    }

    [Fact]
    public void Crc16Arc_MatchesKnownVectors()
    {
        var algo = new Crc16Arc();
        algo.Reset(0);
        algo.Update(new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 });
        algo.Result.ShouldBe(0xBB3DUL);
    }

    [Fact]
    public void Crc32_MatchesKnownVectors()
    {
        var algo = new Crc32();
        algo.Reset(0);
        algo.Update(new byte[] { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 });
        algo.Result.ShouldBe(0xCBF43926UL);
    }

    #endregion

    #region Single CRC

    [BitSerialize]
    public partial class SimpleCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; }

        [BitField(16), BitCrcInclude(nameof(Crc))]
        public ushort Payload { get; set; }

        [BitField(16), BitCrc(typeof(CrcCcitt), InitialValue = 0)]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void SimpleCrcPacket_AutoComputesCrcOnSerialize()
    {
        var data = new SimpleCrcPacket { Header = 0xAB, Payload = 0x1234 };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        var expected = new CrcCcitt();
        expected.Reset(0);
        expected.Update(new byte[] { 0xAB, 0x12, 0x34 });
        ushort want = (ushort)expected.Result;

        // CRC is written at bytes[3..5] (after 3 bytes of payload)
        ushort written = (ushort)((bytes[3] << 8) | bytes[4]);
        written.ShouldBe(want);
        data.Crc.ShouldBe(want); // backfill
    }

    [Fact]
    public void SimpleCrcPacket_RoundTrip()
    {
        var original = new SimpleCrcPacket { Header = 0x7E, Payload = 0xBEEF };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<SimpleCrcPacket>(bytes);
        result.Header.ShouldBe(original.Header);
        result.Payload.ShouldBe(original.Payload);
        result.Crc.ShouldBe(original.Crc);
    }

    #endregion

    #region CRC with validation

    [BitSerialize]
    public partial class ValidatingCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte A { get; set; }

        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte B { get; set; }

        [BitField(16), BitCrc(typeof(CrcCcitt), ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void ValidatingCrcPacket_GoodCrcRoundTrips()
    {
        var original = new ValidatingCrcPacket { A = 0x11, B = 0x22 };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<ValidatingCrcPacket>(bytes);
        result.A.ShouldBe((byte)0x11);
        result.B.ShouldBe((byte)0x22);
    }

    [Fact]
    public void ValidatingCrcPacket_CorruptedCrcThrows()
    {
        var original = new ValidatingCrcPacket { A = 0x11, B = 0x22 };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes[2] ^= 0xFF; // flip CRC bytes
        Should.Throw<System.IO.InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<ValidatingCrcPacket>(bytes));
    }

    #endregion

    #region Enum CRC field

    public enum CrcTag : ushort
    {
        None = 0,
        Ok = 0x1234,
        Bad = 0xDEAD,
    }

    [BitSerialize]
    public partial class EnumCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Tag))]
        public byte A { get; set; }

        [BitField(8), BitCrcInclude(nameof(Tag))]
        public byte B { get; set; }

        [BitField(16), BitCrc(typeof(CrcCcitt))]
        public CrcTag Tag { get; set; }
    }

    [Fact]
    public void EnumCrc_RoundTripAndBackfill()
    {
        var data = new EnumCrcPacket { A = 0x11, B = 0x22, Tag = CrcTag.None };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        var expected = new CrcCcitt();
        expected.Reset(0);
        expected.Update(new byte[] { 0x11, 0x22 });
        ((ushort)data.Tag).ShouldBe((ushort)expected.Result);

        var rt = BitSerializerMSB.Deserialize<EnumCrcPacket>(bytes);
        rt.A.ShouldBe((byte)0x11);
        rt.B.ShouldBe((byte)0x22);
        rt.Tag.ShouldBe(data.Tag);
    }

    #endregion

    #region CRC-32

    [BitSerialize]
    public partial class Crc32Packet
    {
        [BitField(8), BitCrcInclude(nameof(Check))]
        public byte A { get; set; }

        [BitField(8), BitCrcInclude(nameof(Check))]
        public byte B { get; set; }

        [BitField(32), BitCrc(typeof(Crc32))]
        public uint Check { get; set; }
    }

    [Fact]
    public void Crc32Packet_RoundTrip()
    {
        var original = new Crc32Packet { A = 0xDE, B = 0xAD };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        var expected = new Crc32();
        expected.Reset(0);
        expected.Update(new byte[] { 0xDE, 0xAD });
        original.Check.ShouldBe((uint)expected.Result);

        var result = BitSerializerMSB.Deserialize<Crc32Packet>(bytes);
        result.Check.ShouldBe(original.Check);
    }

    #endregion

    #region Nested CRC (ATP-like: outer frame wraps inner CRC'd content)

    [BitSerialize]
    public partial class InnerContent
    {
        [BitField(8), BitCrcInclude(nameof(InnerCrc))]
        public byte DataA { get; set; }

        [BitField(8), BitCrcInclude(nameof(InnerCrc))]
        public byte DataB { get; set; }

        [BitField(16), BitCrc(typeof(CrcCcitt))]
        public ushort InnerCrc { get; set; }
    }

    [BitSerialize]
    public partial class OuterFrame
    {
        [BitField(8)]
        public byte FrameStart { get; set; } = 0x7E;

        [BitField, BitCrcInclude(nameof(OuterCrc))]
        public InnerContent Content { get; set; } = new();

        [BitField(16), BitCrc(typeof(CrcCcitt))]
        public ushort OuterCrc { get; set; }

        [BitField(8)]
        public byte FrameEnd { get; set; } = 0xCF;
    }

    [Fact]
    public void NestedCrc_BothLevelsComputed()
    {
        var frame = new OuterFrame
        {
            Content = new InnerContent { DataA = 0x01, DataB = 0x02 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(frame);

        var innerCrc = new CrcCcitt();
        innerCrc.Reset(0);
        innerCrc.Update(new byte[] { 0x01, 0x02 });
        frame.Content.InnerCrc.ShouldBe((ushort)innerCrc.Result);

        var outerCrc = new CrcCcitt();
        outerCrc.Reset(0);
        // Outer CRC covers Content bytes (DataA, DataB, InnerCrc high, InnerCrc low)
        byte[] innerBytes = new byte[] { 0x01, 0x02, (byte)(frame.Content.InnerCrc >> 8), (byte)frame.Content.InnerCrc };
        outerCrc.Update(innerBytes);
        frame.OuterCrc.ShouldBe((ushort)outerCrc.Result);

        bytes[0].ShouldBe((byte)0x7E);
        bytes[^1].ShouldBe((byte)0xCF);

        var rt = BitSerializerMSB.Deserialize<OuterFrame>(bytes);
        rt.Content.DataA.ShouldBe((byte)0x01);
        rt.Content.DataB.ShouldBe((byte)0x02);
        rt.Content.InnerCrc.ShouldBe(frame.Content.InnerCrc);
        rt.OuterCrc.ShouldBe(frame.OuterCrc);
    }

    #endregion
}
