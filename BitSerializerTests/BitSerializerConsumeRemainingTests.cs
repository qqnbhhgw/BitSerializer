using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public partial class BitSerializerConsumeRemainingTests
{
    [BitSerialize]
    public partial class TrailingBytesPacket
    {
        [BitField(8)]
        public byte Marker { get; set; }

        [BitField(16)]
        public ushort Kind { get; set; }

        [BitField, BitFieldConsumeRemaining]
        public byte[] Trailing { get; set; } = System.Array.Empty<byte>();
    }

    [Fact]
    public void ConsumeRemaining_SerializeWritesExactLength()
    {
        var data = new TrailingBytesPacket
        {
            Marker = 0x7E,
            Kind = 0xABCD,
            Trailing = new byte[] { 0x11, 0x22, 0x33, 0x44 }
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(3 + 4);
        bytes[0].ShouldBe((byte)0x7E);
        bytes[1].ShouldBe((byte)0xAB);
        bytes[2].ShouldBe((byte)0xCD);
        bytes[3].ShouldBe((byte)0x11);
        bytes[6].ShouldBe((byte)0x44);
    }

    [Fact]
    public void ConsumeRemaining_EmptyTrailing_OK()
    {
        var data = new TrailingBytesPacket
        {
            Marker = 0x01,
            Kind = 0x0002,
            Trailing = System.Array.Empty<byte>()
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(3);

        var rt = BitSerializerMSB.Deserialize<TrailingBytesPacket>(bytes);
        rt.Marker.ShouldBe((byte)0x01);
        rt.Kind.ShouldBe((ushort)0x0002);
        rt.Trailing.Length.ShouldBe(0);
    }

    [Fact]
    public void ConsumeRemaining_RoundTrip_VariousLengths()
    {
        foreach (int len in new[] { 0, 1, 5, 16, 257 })
        {
            var payload = new byte[len];
            for (int i = 0; i < len; i++) payload[i] = (byte)(i * 3 + 1);
            var original = new TrailingBytesPacket
            {
                Marker = 0xFF,
                Kind = 0x1234,
                Trailing = payload
            };
            byte[] bytes = BitSerializerMSB.Serialize(original);
            bytes.Length.ShouldBe(3 + len);
            var rt = BitSerializerMSB.Deserialize<TrailingBytesPacket>(bytes);
            rt.Marker.ShouldBe((byte)0xFF);
            rt.Kind.ShouldBe((ushort)0x1234);
            rt.Trailing.ShouldBe(payload);
        }
    }

    [BitSerialize]
    public partial class TrailingListPacket
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField, BitFieldConsumeRemaining]
        public System.Collections.Generic.List<byte> Rest { get; set; } = new();
    }

    [Fact]
    public void ConsumeRemaining_ListBacked_RoundTrip()
    {
        var original = new TrailingListPacket
        {
            Header = 0x5A,
            Rest = new System.Collections.Generic.List<byte> { 0xDE, 0xAD, 0xBE, 0xEF }
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(5);

        var rt = BitSerializerMSB.Deserialize<TrailingListPacket>(bytes);
        rt.Header.ShouldBe((byte)0x5A);
        rt.Rest.ShouldBe(original.Rest);
    }
}
