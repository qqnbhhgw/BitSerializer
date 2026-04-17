using BitSerializer;
using BitSerializer.CrcAlgorithms;
using Shouldly;

namespace BitSerializerTests;

public partial class BitSerializerCrcDynamicIncludeTests
{
    private static ushort ExpectedCrcCcitt(params byte[] data)
    {
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(data);
        return (ushort)algo.Result;
    }

    private static uint ExpectedCrc32(params byte[] data)
    {
        var algo = new Crc32();
        algo.Reset(0);
        algo.Update(data);
        return (uint)algo.Result;
    }

    #region Terminated string include

    [BitSerialize]
    public partial class TerminatedStringCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; } = 0xAA;

        [BitTerminatedString]
        [BitCrcInclude(nameof(Crc))]
        public string Tag { get; set; } = "";

        [BitField(16), BitCrc(typeof(CrcCcitt), ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void TerminatedStringCrc_RoundTrip()
    {
        var original = new TerminatedStringCrcPacket { Header = 0xAA, Tag = "hi" };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        // Wire: 0xAA, 'h', 'i', 0x00 (terminator), CRC16
        bytes.Length.ShouldBe(6);
        original.Crc.ShouldBe(ExpectedCrcCcitt(0xAA, (byte)'h', (byte)'i', 0x00));

        var rt = BitSerializerMSB.Deserialize<TerminatedStringCrcPacket>(bytes);
        rt.Header.ShouldBe((byte)0xAA);
        rt.Tag.ShouldBe("hi");
        rt.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void TerminatedStringCrc_CorruptedPayload_Throws()
    {
        var original = new TerminatedStringCrcPacket { Header = 0xAA, Tag = "hi" };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes[1] ^= 0xFF; // flip a byte of the string
        Should.Throw<InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<TerminatedStringCrcPacket>(bytes));
    }

    #endregion

    #region Dynamic list include (static byte element)

    [BitSerialize]
    public partial class DynamicListCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; } = 0x55;

        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        [BitCrcInclude(nameof(Crc))]
        public List<byte> Payload { get; set; } = new();

        [BitField(32), BitCrc(typeof(Crc32), ValidateOnDeserialize = true)]
        public uint Crc { get; set; }
    }

    [Fact]
    public void DynamicListCrc_RoundTrip()
    {
        var original = new DynamicListCrcPacket
        {
            Header = 0x55,
            Payload = new List<byte> { 0x10, 0x20, 0x30, 0x40 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        // Header + Count (auto-backfilled to 4) + 4 bytes + CRC32 = 1 + 1 + 4 + 4
        bytes.Length.ShouldBe(10);
        original.Count.ShouldBe((byte)4);
        original.Crc.ShouldBe(ExpectedCrc32(0x55, 0x04, 0x10, 0x20, 0x30, 0x40));

        var rt = BitSerializerMSB.Deserialize<DynamicListCrcPacket>(bytes);
        rt.Header.ShouldBe((byte)0x55);
        rt.Count.ShouldBe((byte)4);
        rt.Payload.ShouldBe(new List<byte> { 0x10, 0x20, 0x30, 0x40 });
        rt.Crc.ShouldBe(original.Crc);
    }

    #endregion

    #region Manual IBitSerializable include (runtime alignment assertion)

    public class AlignedManualPayload : IBitSerializable
    {
        public ushort Value { get; set; }
        public int GetTotalBitLength() => 16;
        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperMSB.SetValueLength<ushort>(bytes, bitOffset, 16, Value);
            return 16;
        }
        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperLSB.SetValueLength<ushort>(bytes, bitOffset, 16, Value);
            return 16;
        }
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperMSB.ValueLength<ushort>(bytes, bitOffset, 16);
            return 16;
        }
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperLSB.ValueLength<ushort>(bytes, bitOffset, 16);
            return 16;
        }
    }

    public class MisalignedManualPayload : IBitSerializable
    {
        public ushort Value { get; set; }
        public int GetTotalBitLength() => 13;
        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperMSB.SetValueLength<ushort>(bytes, bitOffset, 13, Value);
            return 13;
        }
        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperLSB.SetValueLength<ushort>(bytes, bitOffset, 13, Value);
            return 13;
        }
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperMSB.ValueLength<ushort>(bytes, bitOffset, 13);
            return 13;
        }
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperLSB.ValueLength<ushort>(bytes, bitOffset, 13);
            return 13;
        }
    }

    [BitSerialize]
    public partial class AlignedManualCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; } = 0x33;

        [BitField, BitCrcInclude(nameof(Crc))]
        public AlignedManualPayload Payload { get; set; } = new();

        [BitField(16), BitCrc(typeof(CrcCcitt), ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    [BitSerialize]
    public partial class MisalignedManualCrcPacket
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; } = 0x33;

        [BitField, BitCrcInclude(nameof(Crc))]
        public MisalignedManualPayload Payload { get; set; } = new();

        [BitField(16), BitCrc(typeof(CrcCcitt), ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void ManualPayloadCrc_Aligned_RoundTrips()
    {
        var original = new AlignedManualCrcPacket
        {
            Header = 0x33,
            Payload = new AlignedManualPayload { Value = 0x1234 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        bytes.Length.ShouldBe(5); // 1 + 2 + 2
        original.Crc.ShouldBe(ExpectedCrcCcitt(0x33, 0x12, 0x34));

        var rt = BitSerializerMSB.Deserialize<AlignedManualCrcPacket>(bytes);
        rt.Header.ShouldBe((byte)0x33);
        rt.Payload.Value.ShouldBe((ushort)0x1234);
        rt.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void ManualPayloadCrc_Misaligned_ThrowsAtSerialize()
    {
        var original = new MisalignedManualCrcPacket
        {
            Header = 0x33,
            Payload = new MisalignedManualPayload { Value = 0x1000 }
        };

        var ex = Should.Throw<InvalidDataException>(() =>
            BitSerializerMSB.Serialize(original));
        ex.Message.ShouldContain("not byte-aligned at runtime");
    }

    #endregion
}
