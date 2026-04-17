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

    // ----------------------------------------------------------------------
    // Regression guards for ByteLength edge cases surfaced in code review:
    //   (a) zero-bit element type must raise, not dead-loop.
    //   (b) struct manual-IBitSerializable element must roundtrip without losing state.
    //   (c) manual-IBitSerializable element with a DECLARED bit width that does NOT
    //       match its runtime size must still backfill the correct byte total.
    // ----------------------------------------------------------------------

    /// <summary>
    /// Manual IBitSerializable that always reports zero bits. Simulates a degenerate
    /// CTCS/ETCS-shaped placeholder type; if misused inside a ByteLength list, the
    /// deserializer must throw rather than spin forever.
    /// </summary>
    public class ZeroBitElement : IBitSerializable
    {
        public int GetTotalBitLength() => 0;

        public int SerializeMSB(Span<byte> bytes, int bitOffset, object? context) => 0;
        public int SerializeMSB(Span<byte> bytes, int bitOffset) => 0;
        public int SerializeLSB(Span<byte> bytes, int bitOffset, object? context) => 0;
        public int SerializeLSB(Span<byte> bytes, int bitOffset) => 0;

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context) => 0;
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset) => 0;
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context) => 0;
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset) => 0;
    }

    [BitSerialize]
    public partial class ZeroBitEnvelope
    {
        [BitField] public ushort BudgetBytes { get; set; }

        [BitField]
        [BitFieldRelated(nameof(BudgetBytes), RelationKind = BitRelationKind.ByteLength)]
        public List<ZeroBitElement> Items { get; set; } = new();
    }

    [Fact]
    public void ByteLength_ZeroBitElement_ThrowsInsteadOfDeadLoop()
    {
        // Wire budget is non-zero but element reports 0 bits per read — the loop cannot make
        // progress. Must raise InvalidDataException with a clear message.
        byte[] bytes = new byte[] { 0x00, 0x02 /* BudgetBytes = 2 */, 0x00, 0x00 };
        var ex = Should.Throw<InvalidDataException>(() => BitSerializerMSB.Deserialize<ZeroBitEnvelope>(bytes));
        ex.Message.ShouldContain("0 consumed bits");
    }

    /// <summary>
    /// Struct element that implements IBitSerializable by hand. A naive casting loop would
    /// box it on every access and write into the box, leaving the caller's slot at default.
    /// </summary>
    public struct PackedI16 : IBitSerializable
    {
        public short Value;

        public int GetTotalBitLength() => 16;

        public int SerializeMSB(Span<byte> bytes, int bitOffset, object? context)
        {
            BitHelperMSB.SetValueLength<short>(bytes, bitOffset, 16, Value);
            return 16;
        }
        public int SerializeMSB(Span<byte> bytes, int bitOffset) => SerializeMSB(bytes, bitOffset, null);
        public int SerializeLSB(Span<byte> bytes, int bitOffset, object? context)
        {
            BitHelperLSB.SetValueLength<short>(bytes, bitOffset, 16, Value);
            return 16;
        }
        public int SerializeLSB(Span<byte> bytes, int bitOffset) => SerializeLSB(bytes, bitOffset, null);

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context)
        {
            Value = BitHelperMSB.ValueLength<short>(bytes, bitOffset, 16);
            return 16;
        }
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeMSB(bytes, bitOffset, null);
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context)
        {
            Value = BitHelperLSB.ValueLength<short>(bytes, bitOffset, 16);
            return 16;
        }
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeLSB(bytes, bitOffset, null);
    }

    [BitSerialize]
    public partial class StructElementEnvelope
    {
        [BitField] public ushort BudgetBytes { get; set; }

        [BitField]
        [BitFieldRelated(nameof(BudgetBytes), RelationKind = BitRelationKind.ByteLength)]
        public List<PackedI16> Items { get; set; } = new();
    }

    [Fact]
    public void ByteLength_StructManualElement_RoundTripPreservesValues()
    {
        var pkt = new StructElementEnvelope
        {
            Items = new List<PackedI16>
            {
                new() { Value = 0x0102 },
                new() { Value = unchecked((short)0xFFFE) },
                new() { Value = 0x00AA },
            },
        };
        byte[] bytes = BitSerializerMSB.Serialize(pkt);
        pkt.BudgetBytes.ShouldBe((ushort)6);
        bytes.Length.ShouldBe(8); // 2 budget + 3 * 2 elements

        var back = BitSerializerMSB.Deserialize<StructElementEnvelope>(bytes);
        back.BudgetBytes.ShouldBe((ushort)6);
        back.Items.Count.ShouldBe(3);
        back.Items[0].Value.ShouldBe((short)0x0102);
        back.Items[1].Value.ShouldBe(unchecked((short)0xFFFE));
        back.Items[2].Value.ShouldBe((short)0x00AA);
    }

    /// <summary>
    /// Manual IBitSerializable whose RUNTIME size (24 bits) differs from the per-field
    /// BitField(N) slot the user might declare. The serializer's dynamic-list loop
    /// advances by the real return value; the backfill must match that, not the declared N.
    /// </summary>
    public class ThreeByteElement : IBitSerializable
    {
        public byte A, B, C;

        public int GetTotalBitLength() => 24;

        public int SerializeMSB(Span<byte> bytes, int bitOffset, object? context)
        {
            BitHelperMSB.SetValueLength<byte>(bytes, bitOffset, 8, A);
            BitHelperMSB.SetValueLength<byte>(bytes, bitOffset + 8, 8, B);
            BitHelperMSB.SetValueLength<byte>(bytes, bitOffset + 16, 8, C);
            return 24;
        }
        public int SerializeMSB(Span<byte> bytes, int bitOffset) => SerializeMSB(bytes, bitOffset, null);
        public int SerializeLSB(Span<byte> bytes, int bitOffset, object? context) => SerializeMSB(bytes, bitOffset, context);
        public int SerializeLSB(Span<byte> bytes, int bitOffset) => SerializeMSB(bytes, bitOffset, null);

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context)
        {
            A = BitHelperMSB.ValueLength<byte>(bytes, bitOffset, 8);
            B = BitHelperMSB.ValueLength<byte>(bytes, bitOffset + 8, 8);
            C = BitHelperMSB.ValueLength<byte>(bytes, bitOffset + 16, 8);
            return 24;
        }
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeMSB(bytes, bitOffset, null);
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context) => DeserializeMSB(bytes, bitOffset, context);
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeMSB(bytes, bitOffset, null);
    }

    [BitSerialize]
    public partial class DeclaredWidthEnvelope
    {
        [BitField] public ushort BudgetBytes { get; set; }

        // Declare an explicit slot width of 16 on the list even though each ThreeByteElement
        // actually serializes to 24 bits at runtime. The pre-fix backfill multiplied Count by
        // the DECLARED width (16), so 2 elements would have backfilled BudgetBytes = 4 while
        // the serializer wrote 6 bytes — producing a truncated wire length. The post-fix
        // backfill sums GetTotalBitLength() per element, yielding 6 and matching the payload.
#pragma warning disable BITS023 // explicit bit length on variable content is intentional here
        [BitField(16)]
#pragma warning restore BITS023
        [BitFieldRelated(nameof(BudgetBytes), RelationKind = BitRelationKind.ByteLength)]
        public List<ThreeByteElement> Items { get; set; } = new();
    }

    [Fact]
    public void ByteLength_ManualElementWithDeclaredWidth_BackfillMatchesRuntimeSize()
    {
        var pkt = new DeclaredWidthEnvelope
        {
            Items = new List<ThreeByteElement>
            {
                new() { A = 0x10, B = 0x20, C = 0x30 },
                new() { A = 0x40, B = 0x50, C = 0x60 },
            },
        };
        byte[] bytes = BitSerializerMSB.Serialize(pkt);
        // Each ThreeByteElement runs 24 bits regardless of the [BitField(16)] declaration.
        // Runtime total: 2 * 24 = 48 bits = 6 bytes. BudgetBytes MUST equal 6 to stay
        // consistent with the payload; pre-fix would have produced 2*16/8 = 4 here.
        pkt.BudgetBytes.ShouldBe((ushort)6);
        bytes.Length.ShouldBe(2 + 6);

        var back = BitSerializerMSB.Deserialize<DeclaredWidthEnvelope>(bytes);
        back.BudgetBytes.ShouldBe((ushort)6);
        back.Items.Count.ShouldBe(2);
        back.Items[0].A.ShouldBe((byte)0x10);
        back.Items[0].C.ShouldBe((byte)0x30);
        back.Items[1].A.ShouldBe((byte)0x40);
        back.Items[1].C.ShouldBe((byte)0x60);
    }
}
