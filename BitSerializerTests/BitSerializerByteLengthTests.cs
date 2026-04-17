using System;
using System.Collections.Generic;
using System.IO;
using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

public partial class BitSerializerByteLengthTests
{
    // Converter emulating AppFrameLengthConverter from Stp.Protocol.Gal:
    // wireLength = payloadLength + 4, payloadLength = wireLength - 4.
    public class GalFrameLengthConverter : IBitFieldValueConverter
    {
        public static object OnSerializeConvert(object value)
            => (ushort)(Convert.ToUInt16(value) + 4);

        public static object OnDeserializeConvert(object value)
            => (ushort)(Convert.ToUInt16(value) - 4);
    }

    // Minimal equivalent of Stp.Protocol.Gal.AppFrameBase: ushort Length,
    // ushort AppFrameType, ushort Reserved, byte[] Data where Length = Data.Length + 4.
    [BitSerialize]
    public partial class AppFrameBase
    {
        [BitField] public ushort Length { get; set; }
        [BitField] public ushort AppFrameType { get; set; }
        [BitField] public ushort Reserved { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Length),
            ValueConverterType = typeof(GalFrameLengthConverter),
            RelationKind = BitRelationKind.ByteLength)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    // Minimal equivalent of Stp.Protocol.Gal.Gal: fixed header + ushort AppFrameLength +
    // List<AppFrameBase> AppFrames driven by total-byte budget (no converter).
    [BitSerialize]
    public partial class GalHeadStub
    {
        [BitField] public uint SrcId { get; set; }
        [BitField] public uint DestId { get; set; }
    }

    [BitSerialize]
    public partial class GalPacket
    {
        [BitField] public GalHeadStub Head { get; set; } = new();
        [BitField] public ushort AppFrameLength { get; set; }

        [BitField]
        [BitFieldRelated(nameof(AppFrameLength), RelationKind = BitRelationKind.ByteLength)]
        public List<AppFrameBase> AppFrames { get; set; } = new();
    }

    [Fact]
    public void AppFrameBase_RoundTrip_LengthIsDataLengthPlus4()
    {
        var frame = new AppFrameBase
        {
            AppFrameType = 0x0102,
            Reserved = 0x0000,
            Data = new byte[] { 0x11, 0x22, 0x33 },
        };

        byte[] bytes = BitSerializerMSB.Serialize(frame);

        // Backfill must set Length to Data.Length + 4 = 7.
        frame.Length.ShouldBe((ushort)7);
        // 2(Length) + 2(AppFrameType) + 2(Reserved) + 3(Data) = 9 bytes on the wire.
        bytes.Length.ShouldBe(9);
        // Length field big-endian.
        bytes[0].ShouldBe((byte)0x00);
        bytes[1].ShouldBe((byte)0x07);
        bytes[2].ShouldBe((byte)0x01);
        bytes[3].ShouldBe((byte)0x02);
        bytes[6].ShouldBe((byte)0x11);
        bytes[8].ShouldBe((byte)0x33);

        var back = BitSerializerMSB.Deserialize<AppFrameBase>(bytes);
        back.Length.ShouldBe((ushort)7);
        back.AppFrameType.ShouldBe((ushort)0x0102);
        back.Data.ShouldBe(new byte[] { 0x11, 0x22, 0x33 });
    }

    [Fact]
    public void AppFrameBase_EmptyData_IsAllowed()
    {
        var frame = new AppFrameBase
        {
            AppFrameType = 0x0055,
            Reserved = 0,
            Data = Array.Empty<byte>(),
        };
        byte[] bytes = BitSerializerMSB.Serialize(frame);
        frame.Length.ShouldBe((ushort)4);
        bytes.Length.ShouldBe(6);

        var back = BitSerializerMSB.Deserialize<AppFrameBase>(bytes);
        back.Data.ShouldNotBeNull();
        back.Data.Length.ShouldBe(0);
    }

    [Fact]
    public void AppFrameBase_DeserializeWithInvalidLengthThrows()
    {
        // Length=3 is below the converter floor (needs >= 4). The converter returns a
        // negative budget which the deserializer rejects before allocation.
        byte[] bytes = new byte[]
        {
            0x00, 0x03, // Length = 3 (wire)
            0x00, 0x00, // AppFrameType
            0x00, 0x00, // Reserved
        };
        Should.Throw<InvalidDataException>(() => BitSerializerMSB.Deserialize<AppFrameBase>(bytes));
    }

    [Fact]
    public void GalPacket_RoundTrip_BudgetDrivenFrames()
    {
        var pkt = new GalPacket
        {
            Head = new GalHeadStub { SrcId = 0x01020304, DestId = 0x05060708 },
            AppFrames = new List<AppFrameBase>
            {
                new() { AppFrameType = 0x0001, Reserved = 0, Data = new byte[] { 0xAA } },
                new() { AppFrameType = 0x0002, Reserved = 0, Data = new byte[] { 0xBB, 0xCC } },
                new() { AppFrameType = 0x0003, Reserved = 0, Data = Array.Empty<byte>() },
            }
        };

        byte[] bytes = BitSerializerMSB.Serialize(pkt);

        // Three frames serialize to 7 + 8 + 6 = 21 bytes.
        pkt.AppFrameLength.ShouldBe((ushort)21);
        // Total = 4(SrcId) + 4(DestId) + 2(AppFrameLength) + 21 = 31 bytes.
        bytes.Length.ShouldBe(31);

        var back = BitSerializerMSB.Deserialize<GalPacket>(bytes);
        back.AppFrameLength.ShouldBe((ushort)21);
        back.AppFrames.Count.ShouldBe(3);
        back.AppFrames[0].Data.ShouldBe(new byte[] { 0xAA });
        back.AppFrames[1].Data.ShouldBe(new byte[] { 0xBB, 0xCC });
        back.AppFrames[2].Data.ShouldBe(Array.Empty<byte>());
    }

    [Fact]
    public void GalPacket_EmptyFrames_AppFrameLengthIsZero()
    {
        var pkt = new GalPacket
        {
            Head = new GalHeadStub { SrcId = 1, DestId = 2 },
            AppFrames = new List<AppFrameBase>(),
        };
        byte[] bytes = BitSerializerMSB.Serialize(pkt);
        pkt.AppFrameLength.ShouldBe((ushort)0);
        bytes.Length.ShouldBe(10); // 4 + 4 + 2

        var back = BitSerializerMSB.Deserialize<GalPacket>(bytes);
        back.AppFrames.ShouldNotBeNull();
        back.AppFrames.Count.ShouldBe(0);
    }

    [Fact]
    public void GalPacket_BudgetUnderrun_ThrowsInvalidData()
    {
        // Build bytes where AppFrameLength is larger than the actual frames. Strategy:
        // serialize a valid packet and then bump AppFrameLength by 1 — the deserializer
        // should over-run end-of-buffer and raise.
        var pkt = new GalPacket
        {
            Head = new GalHeadStub { SrcId = 1, DestId = 2 },
            AppFrames = new List<AppFrameBase>
            {
                new() { AppFrameType = 1, Reserved = 0, Data = new byte[] { 0xAA } },
            }
        };
        byte[] bytes = BitSerializerMSB.Serialize(pkt);
        // AppFrameLength is big-endian at offset 8.
        ushort wireLen = (ushort)((bytes[8] << 8) | bytes[9]);
        ushort bumped = (ushort)(wireLen + 4);
        bytes[8] = (byte)(bumped >> 8);
        bytes[9] = (byte)(bumped & 0xFF);

        Should.Throw<InvalidDataException>(() => BitSerializerMSB.Deserialize<GalPacket>(bytes));
    }

    // Pure byte[] + ByteLength without converter: length field IS the byte count.
    [BitSerialize]
    public partial class RawByteBlock
    {
        [BitField] public ushort Length { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    [Fact]
    public void RawByteBlock_ByteLengthWithoutConverter_RoundTrip()
    {
        var b = new RawByteBlock { Payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } };
        byte[] bytes = BitSerializerMSB.Serialize(b);
        b.Length.ShouldBe((ushort)4);
        bytes.Length.ShouldBe(6);

        var back = BitSerializerMSB.Deserialize<RawByteBlock>(bytes);
        back.Length.ShouldBe((ushort)4);
        back.Payload.ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }
}
