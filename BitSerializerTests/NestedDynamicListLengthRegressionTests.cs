using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

public partial class NestedDynamicListLengthRegressionTests
{
    [Fact]
    public void GetTotalBitLength_NestedDynamicListsWithTrailingFields_ShouldIncludeEntireNestedPayload()
    {
        var message = BitSerializerMSB.Deserialize<Envelope>(CreatePayload());

        message.GetTotalBitLength().ShouldBe(CreatePayload().Length * 8);
    }

    [Fact]
    public void Serialize_NestedDynamicListsWithTrailingFields_ShouldRoundTrip()
    {
        var payload = CreatePayload();
        var message = BitSerializerMSB.Deserialize<Envelope>(payload);

        message.Body.SegmentIds.ShouldBe([(ushort)0x1001, (ushort)0x1002, (ushort)0x1003]);
        message.Body.MarkerType.ShouldBe((byte)0x02);
        message.Body.MarkerId.ShouldBe((ushort)0x3456);
        message.Body.OptionCount.ShouldBe((byte)0x02);
        message.Body.Options.Count.ShouldBe(2);
        message.Body.Options[0].Kind.ShouldBe((byte)0x11);
        message.Body.Options[0].Value.ShouldBe((byte)0x21);
        message.Body.Options[1].Kind.ShouldBe((byte)0x12);
        message.Body.Options[1].Value.ShouldBe((byte)0x22);
        message.Tail.ShouldBe((byte)0xFE);

        BitSerializerMSB.Serialize(message).ShouldBe(payload);
    }

    private static byte[] CreatePayload() =>
    [
        0xA1,
        0x03,
        0x10, 0x01,
        0x10, 0x02,
        0x10, 0x03,
        0x02,
        0x34, 0x56,
        0x02,
        0x11, 0x21,
        0x12, 0x22,
        0xFE
    ];

    [BitSerialize]
    public partial class Envelope
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField] public RouteBody Body { get; set; } = new();
        [BitField(8)] public byte Tail { get; set; }
    }

    [BitSerialize]
    public partial class RouteBody
    {
        [BitField(8)] public byte SegmentCount { get; set; }

        [BitField]
        [BitFieldRelated(nameof(SegmentCount))]
        public List<ushort> SegmentIds { get; set; } = [];

        [BitField(8)] public byte MarkerType { get; set; }
        [BitField(16)] public ushort MarkerId { get; set; }
        [BitField(8)] public byte OptionCount { get; set; }

        [BitField]
        [BitFieldRelated(nameof(OptionCount))]
        public List<RouteOption> Options { get; set; } = [];
    }

    [BitSerialize]
    public partial class RouteOption
    {
        [BitField(8)] public byte Kind { get; set; }
        [BitField(8)] public byte Value { get; set; }
    }
}
