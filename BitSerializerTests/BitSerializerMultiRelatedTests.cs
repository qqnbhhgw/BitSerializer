using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

/// <summary>
/// v0.12.0: [BitFieldRelated] AllowMultiple. Polymorphic field + byte-length budget — the
/// canonical "base class with Type and Length" protocol pattern that triggered this work.
/// </summary>
public partial class BitSerializerMultiRelatedTests
{
    /// <summary>
    /// Abstract base — purely a polymorphic anchor. Generator does not [BitSerialize] this; each
    /// concrete subclass implements IBitSerializable via its own [BitSerialize].
    /// </summary>
    public abstract class FrameContent { }

    [BitSerialize]
    public partial class SubFrameA : FrameContent
    {
        [BitField(8)] public byte ValueA { get; set; }
    }

    [BitSerialize]
    public partial class SubFrameB : FrameContent
    {
        [BitField(16)] public ushort ValueB1 { get; set; }
        [BitField(16)] public ushort ValueB2 { get; set; }
    }

    [BitSerialize]
    public partial class FrameHeader
    {
        [BitField(8)] public byte Type { get; set; }
        [BitField(16)] public ushort Length { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Type))] // discriminator
        [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)] // byte budget
        [BitPoly(1, typeof(SubFrameA))]
        [BitPoly(2, typeof(SubFrameB))]
        public FrameContent Content { get; set; } = new SubFrameA();
    }

    [Fact]
    public void Polymorphic_WithByteLengthBudget_BackfillsBothCarriers_SubFrameA()
    {
        var frame = new FrameHeader { Content = new SubFrameA { ValueA = 0x42 } };
        var bytes = BitSerializerMSB.Serialize(frame);

        // Wire: Type=1, Length=1, ValueA=0x42  → 4 bytes total
        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)1);            // Type discriminator backfill
        bytes[1].ShouldBe((byte)0);            // Length high (1 → 0x0001 MSB)
        bytes[2].ShouldBe((byte)1);            // Length low (1 byte payload)
        bytes[3].ShouldBe((byte)0x42);         // SubFrameA.ValueA

        // Round-trip
        var dst = BitSerializerMSB.Deserialize<FrameHeader>(bytes);
        dst.Type.ShouldBe((byte)1);
        dst.Length.ShouldBe((ushort)1);
        dst.Content.ShouldBeOfType<SubFrameA>();
        ((SubFrameA)dst.Content).ValueA.ShouldBe((byte)0x42);
    }

    [Fact]
    public void Polymorphic_WithByteLengthBudget_BackfillsBothCarriers_SubFrameB()
    {
        var frame = new FrameHeader { Content = new SubFrameB { ValueB1 = 0x1122, ValueB2 = 0x3344 } };
        var bytes = BitSerializerMSB.Serialize(frame);

        // Wire: Type=2, Length=4, ValueB1=0x1122, ValueB2=0x3344  → 7 bytes total
        bytes.Length.ShouldBe(7);
        bytes[0].ShouldBe((byte)2);            // Type discriminator
        bytes[1].ShouldBe((byte)0);            // Length high (4 → 0x0004)
        bytes[2].ShouldBe((byte)4);            // Length low
        bytes[3].ShouldBe((byte)0x11);         // ValueB1 high
        bytes[4].ShouldBe((byte)0x22);         // ValueB1 low
        bytes[5].ShouldBe((byte)0x33);
        bytes[6].ShouldBe((byte)0x44);

        var dst = BitSerializerMSB.Deserialize<FrameHeader>(bytes);
        dst.Type.ShouldBe((byte)2);
        dst.Length.ShouldBe((ushort)4);
        dst.Content.ShouldBeOfType<SubFrameB>();
        ((SubFrameB)dst.Content).ValueB1.ShouldBe((ushort)0x1122);
        ((SubFrameB)dst.Content).ValueB2.ShouldBe((ushort)0x3344);
    }

    /// <summary>
    /// Mismatched Length carrier (tampered wire) — deserializer must catch via the budget verify.
    /// </summary>
    [Fact]
    public void Polymorphic_WithByteLengthBudget_DetectsLengthMismatchOnDeserialize()
    {
        // Manually craft: Type=2 (SubFrameB, expects 4 bytes), Length=999 (lie)
        // The verify after the switch should catch (4 bytes consumed != 999 declared).
        var bytes = new byte[] { 2, 0x03, 0xE7, 0x11, 0x22, 0x33, 0x44 };
        var ex = Should.Throw<System.IO.InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<FrameHeader>(bytes));
        ex.Message.ShouldContain("Content");
        ex.Message.ShouldContain("byte-length");
    }

    /// <summary>
    /// Source order independence: write the ByteLength binding first, Count second. Canonical
    /// ordering in the analyzer should put Count as primary regardless of declaration order.
    /// </summary>
    [BitSerialize]
    public partial class FrameHeaderReversedOrder
    {
        [BitField(8)] public byte Type { get; set; }
        [BitField(16)] public ushort Length { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)] // ByteLength first
        [BitFieldRelated(nameof(Type))]                                                // Count second
        [BitPoly(1, typeof(SubFrameA))]
        [BitPoly(2, typeof(SubFrameB))]
        public FrameContent Content { get; set; } = new SubFrameA();
    }

    [Fact]
    public void Polymorphic_MultiRelated_SourceOrderIndependent()
    {
        var frame = new FrameHeaderReversedOrder { Content = new SubFrameA { ValueA = 0xAB } };
        var bytes = BitSerializerMSB.Serialize(frame);
        bytes[0].ShouldBe((byte)1);    // Type
        bytes[3].ShouldBe((byte)0xAB); // ValueA
        var dst = BitSerializerMSB.Deserialize<FrameHeaderReversedOrder>(bytes);
        dst.Content.ShouldBeOfType<SubFrameA>();
        ((SubFrameA)dst.Content).ValueA.ShouldBe((byte)0xAB);
    }
}
