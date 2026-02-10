using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

public class BitSerializerLSBTests
{
    #region Test Models

    public class SimpleData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public ushort Value { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    public class CustomBitLengthData
    {
        [BitField(4)]
        public byte NibbleLow { get; set; }

        [BitField(4)]
        public byte NibbleHigh { get; set; }

        [BitField(12)]
        public ushort TwelveBits { get; set; }

        [BitField(4)]
        public byte FourBits { get; set; }
    }

    public enum TestStatus : byte
    {
        Unknown = 0,
        Active = 1,
        Inactive = 2,
        Error = 3
    }

    public class EnumData
    {
        [BitField(8)]
        public TestStatus Status { get; set; }

        [BitField(16)]
        public ushort Code { get; set; }
    }

    public class InnerData
    {
        [BitField(8)]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    public class NestedData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public InnerData Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    public class ListData
    {
        [BitField(4)]
        public byte Count { get; set; }

        [BitField(4)]
        public byte Reserved { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    public class DataWithIgnored
    {
        [BitField(8)]
        public byte Value { get; set; }

        [BitIgnore]
        public string Description { get; set; } = string.Empty;

        [BitField(8)]
        public byte AnotherValue { get; set; }
    }

    #endregion

    #region LSB Bit Order Verification

    [Fact]
    public void Deserialize_SimpleData_ShouldWorkWithLSBOrder()
    {
        // In LSB mode: bits are read from LSB to MSB within each byte
        // byte 0xAB = 10101011, LSB first means bit0=1, bit1=1, bit2=0, bit3=1, bit4=0, bit5=1, bit6=0, bit7=1
        byte[] bytes = [0xAB, 0xCD, 0xEF, 0x12];

        var result = BitSerializerLSB.Deserialize<SimpleData>(bytes);

        result.Header.ShouldBe((byte)0xAB);
        // LSB 16-bit value from bytes[1..2]: 0xCD, 0xEF → LSB order: 0xEFCD
        result.Value.ShouldBe((ushort)0xEFCD);
        result.Footer.ShouldBe((byte)0x12);
    }

    [Fact]
    public void Serialize_SimpleData_ShouldRoundTrip()
    {
        var original = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0x56
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<SimpleData>(bytes);

        result.Header.ShouldBe(original.Header);
        result.Value.ShouldBe(original.Value);
        result.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void LSB_And_MSB_ShouldProduceDifferentBytes_ForNonByteAligned()
    {
        // For non-byte-aligned data, MSB and LSB should produce different byte arrays
        var data = new CustomBitLengthData
        {
            NibbleLow = 0x0A,
            NibbleHigh = 0x05,
            TwelveBits = 0x123,
            FourBits = 0x0F
        };

        var lsbBytes = BitSerializerLSB.Serialize(data);
        var msbBytes = BitSerializerMSB.Serialize(data);

        // They should be different because bit ordering differs
        lsbBytes.ShouldNotBe(msbBytes);

        // But both should round-trip correctly with their own deserializer
        var lsbResult = BitSerializerLSB.Deserialize<CustomBitLengthData>(lsbBytes);
        lsbResult.NibbleLow.ShouldBe(data.NibbleLow);
        lsbResult.NibbleHigh.ShouldBe(data.NibbleHigh);
        lsbResult.TwelveBits.ShouldBe(data.TwelveBits);
        lsbResult.FourBits.ShouldBe(data.FourBits);

        var msbResult = BitSerializerMSB.Deserialize<CustomBitLengthData>(msbBytes);
        msbResult.NibbleLow.ShouldBe(data.NibbleLow);
        msbResult.NibbleHigh.ShouldBe(data.NibbleHigh);
        msbResult.TwelveBits.ShouldBe(data.TwelveBits);
        msbResult.FourBits.ShouldBe(data.FourBits);
    }

    #endregion

    #region Serialization Round-Trip Tests

    [Fact]
    public void Serialize_CustomBitLength_ShouldRoundTrip()
    {
        var original = new CustomBitLengthData
        {
            NibbleLow = 0x0C,
            NibbleHigh = 0x03,
            TwelveBits = 0xABC,
            FourBits = 0x07
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<CustomBitLengthData>(bytes);

        result.NibbleLow.ShouldBe(original.NibbleLow);
        result.NibbleHigh.ShouldBe(original.NibbleHigh);
        result.TwelveBits.ShouldBe(original.TwelveBits);
        result.FourBits.ShouldBe(original.FourBits);
    }

    [Fact]
    public void Serialize_EnumData_ShouldRoundTrip()
    {
        var original = new EnumData
        {
            Status = TestStatus.Active,
            Code = 12345
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<EnumData>(bytes);

        result.Status.ShouldBe(original.Status);
        result.Code.ShouldBe(original.Code);
    }

    [Fact]
    public void Serialize_NestedData_ShouldRoundTrip()
    {
        var original = new NestedData
        {
            Header = 0xFF,
            Inner = new InnerData { X = 100, Y = 200 },
            Footer = 0xAA
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<NestedData>(bytes);

        result.Header.ShouldBe(original.Header);
        result.Inner.X.ShouldBe(original.Inner.X);
        result.Inner.Y.ShouldBe(original.Inner.Y);
        result.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_ListData_ShouldRoundTrip()
    {
        var original = new ListData
        {
            Count = 3,
            Reserved = 0,
            Items = new List<byte> { 10, 20, 30 }
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<ListData>(bytes);

        result.Count.ShouldBe(original.Count);
        result.Items.Count.ShouldBe(3);
        result.Items[0].ShouldBe((byte)10);
        result.Items[1].ShouldBe((byte)20);
        result.Items[2].ShouldBe((byte)30);
    }

    [Fact]
    public void Serialize_DataWithIgnored_ShouldRoundTrip()
    {
        var original = new DataWithIgnored
        {
            Value = 0x42,
            Description = "should be ignored",
            AnotherValue = 0x99
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<DataWithIgnored>(bytes);

        result.Value.ShouldBe(original.Value);
        result.AnotherValue.ShouldBe(original.AnotherValue);
        result.Description.ShouldBe(string.Empty); // ignored, so default
    }

    #endregion

    #region LSB Specific Bit Pattern Tests

    [Fact]
    public void LSB_SingleByte_BitsAreReadFromLSB()
    {
        // Verify LSB bit ordering within a byte
        // 0b00000011 = 0x03
        // In LSB: bit0=1, bit1=1, rest=0 → 4-bit value from bit0-3 = 0b0011 = 3
        byte[] bytes = [0x03];

        var data = new CustomBitLengthData();
        // NibbleLow is first 4 bits: in LSB mode, bits 0-3 of byte 0
        // 0x03 = 0b00000011, bits 0-3 = 0011 = 3
        var result = BitSerializerLSB.Deserialize<CustomBitLengthData>(
            new byte[] { 0x03, 0x00, 0x00 });

        result.NibbleLow.ShouldBe((byte)3);
        result.NibbleHigh.ShouldBe((byte)0);
    }

    [Fact]
    public void LSB_CrossByteBoundary_ShouldWorkCorrectly()
    {
        // Test fields that cross byte boundaries in LSB mode
        var original = new CustomBitLengthData
        {
            NibbleLow = 0x0F,  // 4 bits
            NibbleHigh = 0x0F, // 4 bits
            TwelveBits = 0xFFF, // 12 bits crossing byte boundary
            FourBits = 0x0F    // 4 bits
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var result = BitSerializerLSB.Deserialize<CustomBitLengthData>(bytes);

        result.NibbleLow.ShouldBe((byte)0x0F);
        result.NibbleHigh.ShouldBe((byte)0x0F);
        result.TwelveBits.ShouldBe((ushort)0xFFF);
        result.FourBits.ShouldBe((byte)0x0F);
    }

    [Fact]
    public void Serialize_ReadOnlySpan_ShouldWork()
    {
        var original = new SimpleData
        {
            Header = 0x11,
            Value = 0x2233,
            Footer = 0x44
        };

        var bytes = BitSerializerLSB.Serialize(original);
        ReadOnlySpan<byte> span = bytes;
        var result = BitSerializerLSB.Deserialize<SimpleData>(span);

        result.Header.ShouldBe(original.Header);
        result.Value.ShouldBe(original.Value);
        result.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_ToSpan_ShouldWork()
    {
        var original = new SimpleData
        {
            Header = 0x55,
            Value = 0x6677,
            Footer = 0x88
        };

        var bytes = new byte[4];
        BitSerializerLSB.Serialize(original, bytes);
        var result = BitSerializerLSB.Deserialize<SimpleData>(bytes);

        result.Header.ShouldBe(original.Header);
        result.Value.ShouldBe(original.Value);
        result.Footer.ShouldBe(original.Footer);
    }

    #endregion
}
