using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public partial class BitSerializerLengthPrefixStringTests
{
    #region Test Models

    [BitSerialize]
    public partial class Utf8With16BitLength
    {
        [BitField(8)] public byte Header { get; set; }
        [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8)]
        public string StationName { get; set; } = "";
        [BitField(8)] public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class AsciiWith8BitLength
    {
        [BitLengthPrefixString(8, Encoding = BitStringEncoding.ASCII)]
        public string Tag { get; set; } = "";
    }

    [BitSerialize]
    public partial class Utf8WithMaxBytes
    {
        // MaxBytes = 4：4 字节 = 2 个 ASCII 字符 = 不到 2 个中文字符（中文 UTF-8 占 3 字节）
        [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8, MaxBytes = 4)]
        public string Bounded { get; set; } = "";
    }

    [BitSerialize]
    public partial class Utf8With32BitLength
    {
        [BitLengthPrefixString(32, Encoding = BitStringEncoding.UTF8)]
        public string Payload { get; set; } = "";
    }

    /// <summary>
    /// 8-bit 长度前缀 + MaxBytes 较小，便于测试越界触发异常。
    /// 8-bit 最大 255 字节；MaxBytes = 0 = 仅受 255 约束。
    /// </summary>
    [BitSerialize]
    public partial class OverflowPacket
    {
        [BitLengthPrefixString(8, Encoding = BitStringEncoding.ASCII)]
        public string Tag { get; set; } = "";
    }

    /// <summary>
    /// 两个连续 LengthPrefixString 字段，验证 bitIndex 正确传递给下一个字段。
    /// </summary>
    [BitSerialize]
    public partial class TwoStringsBackToBack
    {
        [BitLengthPrefixString(16)] public string Domain { get; set; } = "";
        [BitLengthPrefixString(16)] public string Text { get; set; } = "";
        [BitField(8)] public byte Trailer { get; set; }
    }

    #endregion

    [Fact]
    public void Utf8_Ascii_RoundTrips()
    {
        var original = new Utf8With16BitLength
        {
            Header = 0xAB,
            StationName = "Tokyo",
            Footer = 0xCD,
        };

        byte[] bytes = BitSerializerMSB.Serialize(original);

        // Layout: Header (1) + Length (2 MSB) + "Tokyo" (5) + Footer (1) = 9 bytes
        bytes.Length.ShouldBe(9);
        bytes[0].ShouldBe((byte)0xAB);
        // Length = 5 in big-endian
        ((bytes[1] << 8) | bytes[2]).ShouldBe(5);
        // "Tokyo" = 0x54 0x6F 0x6B 0x79 0x6F
        bytes[3].ShouldBe((byte)'T');
        bytes[4].ShouldBe((byte)'o');
        bytes[5].ShouldBe((byte)'k');
        bytes[6].ShouldBe((byte)'y');
        bytes[7].ShouldBe((byte)'o');
        bytes[8].ShouldBe((byte)0xCD);

        var result = BitSerializerMSB.Deserialize<Utf8With16BitLength>(bytes);
        result.Header.ShouldBe((byte)0xAB);
        result.StationName.ShouldBe("Tokyo");
        result.Footer.ShouldBe((byte)0xCD);
    }

    [Fact]
    public void Utf8_Chinese_RoundTrips()
    {
        // 北京 = E5 8C 97 E4 BA AC (6 bytes)
        var original = new Utf8With16BitLength
        {
            Header = 0x01,
            StationName = "北京",
            Footer = 0x02,
        };

        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(1 + 2 + 6 + 1);
        ((bytes[1] << 8) | bytes[2]).ShouldBe(6);

        var result = BitSerializerMSB.Deserialize<Utf8With16BitLength>(bytes);
        result.StationName.ShouldBe("北京");
    }

    [Fact]
    public void EmptyString_RoundTrips()
    {
        var original = new Utf8With16BitLength { Header = 0xFF, Footer = 0xEE, StationName = "" };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(1 + 2 + 0 + 1);
        ((bytes[1] << 8) | bytes[2]).ShouldBe(0);

        var result = BitSerializerMSB.Deserialize<Utf8With16BitLength>(bytes);
        result.StationName.ShouldBe("");
    }

    [Fact]
    public void NullString_SerializesAsEmpty()
    {
        var original = new Utf8With16BitLength { Header = 0xFF, Footer = 0xEE, StationName = null! };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        ((bytes[1] << 8) | bytes[2]).ShouldBe(0);
    }

    [Fact]
    public void Ascii_8BitLength_RoundTrips()
    {
        var original = new AsciiWith8BitLength { Tag = "ABC" };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(1 + 3);
        bytes[0].ShouldBe((byte)3);
        bytes[1].ShouldBe((byte)'A');

        var result = BitSerializerMSB.Deserialize<AsciiWith8BitLength>(bytes);
        result.Tag.ShouldBe("ABC");
    }

    [Fact]
    public void Utf8_32BitLength_RoundTripsLargeString()
    {
        var big = new string('x', 1000);
        var original = new Utf8With32BitLength { Payload = big };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(4 + 1000);

        var result = BitSerializerMSB.Deserialize<Utf8With32BitLength>(bytes);
        result.Payload.ShouldBe(big);
    }

    [Fact]
    public void MaxBytes_TruncatesAsciiAtBoundary()
    {
        var original = new Utf8WithMaxBytes { Bounded = "ABCDEFGHIJ" }; // 10 bytes
        byte[] bytes = BitSerializerMSB.Serialize(original);
        // MaxBytes = 4 → truncated to "ABCD"
        ((bytes[0] << 8) | bytes[1]).ShouldBe(4);
        bytes[2].ShouldBe((byte)'A');
        bytes[5].ShouldBe((byte)'D');

        var result = BitSerializerMSB.Deserialize<Utf8WithMaxBytes>(bytes);
        result.Bounded.ShouldBe("ABCD");
    }

    [Fact]
    public void MaxBytes_Utf8_AvoidsSplittingMultiByteChars()
    {
        // 北京 = E5 8C 97 E4 BA AC (6 bytes)
        // With MaxBytes=4, naive truncation gives E5 8C 97 E4 — but E4 starts a 3-byte sequence
        // that can't fit, so the truncation point should fall back to before E4 → 3 bytes ("北").
        var original = new Utf8WithMaxBytes { Bounded = "北京" };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        ((bytes[0] << 8) | bytes[1]).ShouldBe(3); // only "北" survives

        var result = BitSerializerMSB.Deserialize<Utf8WithMaxBytes>(bytes);
        result.Bounded.ShouldBe("北");
    }

    [Fact]
    public void Overflow_8BitLength_ThrowsWhenStringExceeds255Bytes()
    {
        var tooLong = new string('a', 300);
        var data = new OverflowPacket { Tag = tooLong };
        Should.Throw<System.InvalidOperationException>(() => BitSerializerMSB.Serialize(data));
    }

    [Fact]
    public void TwoStringsBackToBack_RoundTrips()
    {
        var original = new TwoStringsBackToBack
        {
            Domain = "speed",
            Text = "Hello, World",
            Trailer = 0xAB,
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        var result = BitSerializerMSB.Deserialize<TwoStringsBackToBack>(bytes);
        result.Domain.ShouldBe("speed");
        result.Text.ShouldBe("Hello, World");
        result.Trailer.ShouldBe((byte)0xAB);
    }

    [Fact]
    public void TwoStringsBackToBack_VaryingLengthsRoundTrip()
    {
        foreach (var (d, t) in new[] { ("", ""), ("a", "b"), ("北", "京"), ("very long " + new string('x', 100), "tiny") })
        {
            var original = new TwoStringsBackToBack { Domain = d, Text = t, Trailer = 0x42 };
            byte[] bytes = BitSerializerMSB.Serialize(original);
            var result = BitSerializerMSB.Deserialize<TwoStringsBackToBack>(bytes);
            result.Domain.ShouldBe(d);
            result.Text.ShouldBe(t);
            result.Trailer.ShouldBe((byte)0x42);
        }
    }

    [Fact]
    public void GetTotalBitLength_MatchesActualBytes()
    {
        var data = new Utf8With16BitLength
        {
            Header = 0x01,
            StationName = "上海", // 6 UTF-8 bytes
            Footer = 0x02,
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        var actual = data.GetTotalBitLength();
        actual.ShouldBe(bytes.Length * 8);
    }

    [Fact]
    public void GetTotalBitLength_AfterUtf8Truncation_MatchesActualBytes()
    {
        // Review P1: GetTotalBitLength used to cap at MaxBytes naively, but the serializer rolls back
        // to a multi-byte boundary. For "北京" + MaxBytes=4, serializer writes 3 bytes ("北"); the
        // predictor previously returned 4. Result: BitSerializerMSB.Serialize allocated a 6-byte
        // buffer (2 length + 4 payload) but only wrote 5, leaking a trailing 0x00.
        var data = new Utf8WithMaxBytes { Bounded = "北京" };
        byte[] bytes = BitSerializerMSB.Serialize(data);
        var predicted = data.GetTotalBitLength();

        // Actual layout: 2-byte length prefix + 3-byte "北" = 5 bytes.
        bytes.Length.ShouldBe(5);
        predicted.ShouldBe(bytes.Length * 8);
    }

    [Fact]
    public void GetTotalBitLength_NoTruncation_StillMatches()
    {
        // Sanity: when MaxBytes is large enough, predictor stays exact.
        var data = new Utf8WithMaxBytes { Bounded = "AB" }; // 2 ASCII bytes, under MaxBytes=4
        byte[] bytes = BitSerializerMSB.Serialize(data);
        data.GetTotalBitLength().ShouldBe(bytes.Length * 8);
    }

    [Fact]
    public void LSB_Encoding_FlipsLengthPrefixByteOrder()
    {
        var data = new Utf8With16BitLength { Header = 0xAA, StationName = "ABCD", Footer = 0xBB };
        byte[] msb = BitSerializerMSB.Serialize(data);
        byte[] lsb = BitSerializerLSB.Serialize(data);

        // Length = 4. MSB layout: [Header][00 04][...]. LSB layout: [Header][04 00][...].
        msb[1].ShouldBe((byte)0x00); msb[2].ShouldBe((byte)0x04);
        lsb[1].ShouldBe((byte)0x04); lsb[2].ShouldBe((byte)0x00);

        BitSerializerMSB.Deserialize<Utf8With16BitLength>(msb).StationName.ShouldBe("ABCD");
        BitSerializerLSB.Deserialize<Utf8With16BitLength>(lsb).StationName.ShouldBe("ABCD");
    }
}
