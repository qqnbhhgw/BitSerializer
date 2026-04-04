using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public partial class BitSerializerStringAndCustomTypeTests
{
    #region Test Models - FixedString

    [BitSerialize]
    public partial class FixedStringData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitFixedString(10)]
        public string Name { get; set; } = "";

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class FixedStringUtf8Data
    {
        [BitFixedString(20, Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = "";

        [BitField(8)]
        public byte Tail { get; set; }
    }

    [BitSerialize]
    public partial class TwoFixedStringsData
    {
        [BitFixedString(5)]
        public string Tag { get; set; } = "";

        [BitFixedString(5)]
        public string Code { get; set; } = "";
    }

    #endregion

    #region Test Models - TerminatedString

    [BitSerialize]
    public partial class TerminatedStringData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitTerminatedString]
        public string Message { get; set; } = "";

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class MultiStringData
    {
        [BitFixedString(5)]
        public string Tag { get; set; } = "";

        [BitTerminatedString]
        public string Description { get; set; } = "";

        [BitField(16)]
        public ushort Code { get; set; }
    }

    [BitSerialize]
    public partial class TwoTerminatedStringsData
    {
        [BitTerminatedString]
        public string First { get; set; } = "";

        [BitTerminatedString]
        public string Second { get; set; } = "";

        [BitField(8)]
        public byte Tail { get; set; }
    }

    #endregion

    #region Test Models - Manual IBitSerializable

    public class KiloMeter : IBitSerializable
    {
        public double Value { get; set; }

        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            var str = Value.ToString("F3").PadLeft(10);
            var encoded = System.Text.Encoding.ASCII.GetBytes(str);
            for (int i = 0; i < 10; i++)
                BitHelperLSB.SetValueLength<byte>(bytes, bitOffset + i * 8, 8,
                    i < encoded.Length ? encoded[i] : (byte)0x20);
            return 80;
        }

        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            var str = Value.ToString("F3").PadLeft(10);
            var encoded = System.Text.Encoding.ASCII.GetBytes(str);
            for (int i = 0; i < 10; i++)
                BitHelperMSB.SetValueLength<byte>(bytes, bitOffset + i * 8, 8,
                    i < encoded.Length ? encoded[i] : (byte)0x20);
            return 80;
        }

        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            var buf = new byte[10];
            for (int i = 0; i < 10; i++)
                buf[i] = BitHelperLSB.ValueLength<byte>(bytes, bitOffset + i * 8, 8);
            Value = double.Parse(System.Text.Encoding.ASCII.GetString(buf).Trim());
            return 80;
        }

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            var buf = new byte[10];
            for (int i = 0; i < 10; i++)
                buf[i] = BitHelperMSB.ValueLength<byte>(bytes, bitOffset + i * 8, 8);
            Value = double.Parse(System.Text.Encoding.ASCII.GetString(buf).Trim());
            return 80;
        }

        public int GetTotalBitLength() => 80;
    }

    [BitSerialize]
    public partial class DataWithKiloMeter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(80)]
        public KiloMeter Distance { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    public class VersionSurrogate : IBitSerializable
    {
        public int Major { get; set; }
        public int Minor { get; set; }
        public int Build { get; set; }
        public int Revision { get; set; }

        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperLSB.SetValueLength<int>(bytes, bitOffset, 32, Major);
            BitHelperLSB.SetValueLength<int>(bytes, bitOffset + 32, 32, Minor);
            BitHelperLSB.SetValueLength<int>(bytes, bitOffset + 64, 32, Build);
            BitHelperLSB.SetValueLength<int>(bytes, bitOffset + 96, 32, Revision);
            return 128;
        }

        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperMSB.SetValueLength<int>(bytes, bitOffset, 32, Major);
            BitHelperMSB.SetValueLength<int>(bytes, bitOffset + 32, 32, Minor);
            BitHelperMSB.SetValueLength<int>(bytes, bitOffset + 64, 32, Build);
            BitHelperMSB.SetValueLength<int>(bytes, bitOffset + 96, 32, Revision);
            return 128;
        }

        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Major = BitHelperLSB.ValueLength<int>(bytes, bitOffset, 32);
            Minor = BitHelperLSB.ValueLength<int>(bytes, bitOffset + 32, 32);
            Build = BitHelperLSB.ValueLength<int>(bytes, bitOffset + 64, 32);
            Revision = BitHelperLSB.ValueLength<int>(bytes, bitOffset + 96, 32);
            return 128;
        }

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Major = BitHelperMSB.ValueLength<int>(bytes, bitOffset, 32);
            Minor = BitHelperMSB.ValueLength<int>(bytes, bitOffset + 32, 32);
            Build = BitHelperMSB.ValueLength<int>(bytes, bitOffset + 64, 32);
            Revision = BitHelperMSB.ValueLength<int>(bytes, bitOffset + 96, 32);
            return 128;
        }

        public int GetTotalBitLength() => 128;
    }

    [BitSerialize]
    public partial class DataWithVersion
    {
        [BitField(16)]
        public ushort Id { get; set; }

        [BitField(128)]
        public VersionSurrogate Version { get; set; } = new();

        [BitField(8)]
        public byte Flags { get; set; }
    }

    #endregion

    #region FixedString MSB Tests

    [Fact]
    public void FixedString_Deserialize_MSB()
    {
        // Header=0xAA, "Hello" (5 bytes) + 5 null padding, Footer=0xBB
        byte[] bytes = { 0xAA, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x00, 0x00, 0x00, 0x00, 0xBB };

        var result = BitSerializerMSB.Deserialize<FixedStringData>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.Name.ShouldBe("Hello");
        result.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void FixedString_Serialize_MSB()
    {
        var data = new FixedStringData
        {
            Header = 0xAA,
            Name = "Hello",
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(12); // 1 + 10 + 1
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x48); // 'H'
        bytes[2].ShouldBe((byte)0x65); // 'e'
        bytes[3].ShouldBe((byte)0x6C); // 'l'
        bytes[4].ShouldBe((byte)0x6C); // 'l'
        bytes[5].ShouldBe((byte)0x6F); // 'o'
        bytes[6].ShouldBe((byte)0x00); // padding
        bytes[7].ShouldBe((byte)0x00);
        bytes[8].ShouldBe((byte)0x00);
        bytes[9].ShouldBe((byte)0x00);
        bytes[10].ShouldBe((byte)0x00);
        bytes[11].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void FixedString_RoundTrip_MSB()
    {
        var original = new FixedStringData
        {
            Header = 0x01,
            Name = "TestStr",
            Footer = 0xFF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<FixedStringData>(bytes);

        restored.Header.ShouldBe(original.Header);
        restored.Name.ShouldBe(original.Name);
        restored.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void FixedString_Truncation_MSB()
    {
        var data = new FixedStringData
        {
            Header = 0x01,
            Name = "ThisIsLongerThanTenBytes",
            Footer = 0x02
        };

        var bytes = BitSerializerMSB.Serialize(data);
        var restored = BitSerializerMSB.Deserialize<FixedStringData>(bytes);

        restored.Name.ShouldBe("ThisIsLong"); // truncated to 10 bytes
        restored.Footer.ShouldBe((byte)0x02);
    }

    [Fact]
    public void FixedString_EmptyString_MSB()
    {
        var data = new FixedStringData
        {
            Header = 0xCC,
            Name = "",
            Footer = 0xDD
        };

        var bytes = BitSerializerMSB.Serialize(data);
        var restored = BitSerializerMSB.Deserialize<FixedStringData>(bytes);

        restored.Name.ShouldBe("");
        restored.Header.ShouldBe((byte)0xCC);
        restored.Footer.ShouldBe((byte)0xDD);
    }

    [Fact]
    public void FixedString_NullString_MSB()
    {
        var data = new FixedStringData
        {
            Header = 0x11,
            Name = null!,
            Footer = 0x22
        };

        var bytes = BitSerializerMSB.Serialize(data);
        var restored = BitSerializerMSB.Deserialize<FixedStringData>(bytes);

        restored.Name.ShouldBe("");
    }

    [Fact]
    public void FixedString_TwoStrings_RoundTrip_MSB()
    {
        var data = new TwoFixedStringsData
        {
            Tag = "AB",
            Code = "XY"
        };

        var bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(10); // 5 + 5

        var restored = BitSerializerMSB.Deserialize<TwoFixedStringsData>(bytes);
        restored.Tag.ShouldBe("AB");
        restored.Code.ShouldBe("XY");
    }

    [Fact]
    public void FixedString_GetTotalBitLength()
    {
        var data = new FixedStringData { Header = 0, Name = "x", Footer = 0 };
        data.GetTotalBitLength().ShouldBe(96); // 8 + 80 + 8
    }

    #endregion

    #region FixedString LSB Tests

    [Fact]
    public void FixedString_RoundTrip_LSB()
    {
        var original = new FixedStringData
        {
            Header = 0x01,
            Name = "Hello",
            Footer = 0xFF
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<FixedStringData>(bytes);

        restored.Header.ShouldBe(original.Header);
        restored.Name.ShouldBe(original.Name);
        restored.Footer.ShouldBe(original.Footer);
    }

    #endregion

    #region FixedString UTF8 Tests

    [Fact]
    public void FixedStringUtf8_RoundTrip_MSB()
    {
        var original = new FixedStringUtf8Data
        {
            Text = "Hello UTF8",
            Tail = 0xAA
        };

        var bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(21); // 20 + 1

        var restored = BitSerializerMSB.Deserialize<FixedStringUtf8Data>(bytes);
        restored.Text.ShouldBe("Hello UTF8");
        restored.Tail.ShouldBe((byte)0xAA);
    }

    #endregion

    #region TerminatedString MSB Tests

    [Fact]
    public void TerminatedString_Deserialize_MSB()
    {
        // Header=0xAA, "Hi" + null, Footer=0xBB
        byte[] bytes = { 0xAA, 0x48, 0x69, 0x00, 0xBB };

        var result = BitSerializerMSB.Deserialize<TerminatedStringData>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.Message.ShouldBe("Hi");
        result.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void TerminatedString_Serialize_MSB()
    {
        var data = new TerminatedStringData
        {
            Header = 0xAA,
            Message = "Hi",
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(5); // 1 + 2 + 1(null) + 1
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x48); // 'H'
        bytes[2].ShouldBe((byte)0x69); // 'i'
        bytes[3].ShouldBe((byte)0x00); // null terminator
        bytes[4].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void TerminatedString_RoundTrip_MSB()
    {
        var original = new TerminatedStringData
        {
            Header = 0x01,
            Message = "TestMessage",
            Footer = 0xFF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<TerminatedStringData>(bytes);

        restored.Header.ShouldBe(original.Header);
        restored.Message.ShouldBe(original.Message);
        restored.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void TerminatedString_EmptyString_MSB()
    {
        var data = new TerminatedStringData
        {
            Header = 0xCC,
            Message = "",
            Footer = 0xDD
        };

        var bytes = BitSerializerMSB.Serialize(data);
        bytes.Length.ShouldBe(3); // 1 + 1(null) + 1

        var restored = BitSerializerMSB.Deserialize<TerminatedStringData>(bytes);
        restored.Message.ShouldBe("");
        restored.Header.ShouldBe((byte)0xCC);
        restored.Footer.ShouldBe((byte)0xDD);
    }

    [Fact]
    public void TerminatedString_GetTotalBitLength()
    {
        var data = new TerminatedStringData { Header = 0, Message = "ABC", Footer = 0 };
        // 8 + (3+1)*8 + 8 = 48
        data.GetTotalBitLength().ShouldBe(48);
    }

    [Fact]
    public void TwoTerminatedStrings_RoundTrip_MSB()
    {
        var original = new TwoTerminatedStringsData
        {
            First = "Hello",
            Second = "World",
            Tail = 0xAB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<TwoTerminatedStringsData>(bytes);

        restored.First.ShouldBe("Hello");
        restored.Second.ShouldBe("World");
        restored.Tail.ShouldBe((byte)0xAB);
    }

    #endregion

    #region TerminatedString LSB Tests

    [Fact]
    public void TerminatedString_RoundTrip_LSB()
    {
        var original = new TerminatedStringData
        {
            Header = 0x01,
            Message = "Hello",
            Footer = 0xFF
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<TerminatedStringData>(bytes);

        restored.Header.ShouldBe(original.Header);
        restored.Message.ShouldBe(original.Message);
        restored.Footer.ShouldBe(original.Footer);
    }

    #endregion

    #region Mixed String Tests

    [Fact]
    public void MultiString_RoundTrip_MSB()
    {
        var original = new MultiStringData
        {
            Tag = "INFO",
            Description = "System running",
            Code = 0x1234
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<MultiStringData>(bytes);

        restored.Tag.ShouldBe("INFO");
        restored.Description.ShouldBe("System running");
        restored.Code.ShouldBe((ushort)0x1234);
    }

    #endregion

    #region Manual IBitSerializable - KiloMeter Tests

    [Fact]
    public void KiloMeter_RoundTrip_MSB()
    {
        var original = new DataWithKiloMeter
        {
            Header = 0x01,
            Distance = new KiloMeter { Value = 123.456 },
            Footer = 0xFF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(12); // 1 + 10 + 1

        var restored = BitSerializerMSB.Deserialize<DataWithKiloMeter>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Distance.Value.ShouldBe(123.456);
        restored.Footer.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void KiloMeter_RoundTrip_LSB()
    {
        var original = new DataWithKiloMeter
        {
            Header = 0x02,
            Distance = new KiloMeter { Value = 99.999 },
            Footer = 0xFE
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<DataWithKiloMeter>(bytes);

        restored.Header.ShouldBe((byte)0x02);
        restored.Distance.Value.ShouldBe(99.999);
        restored.Footer.ShouldBe((byte)0xFE);
    }

    [Fact]
    public void KiloMeter_GetTotalBitLength()
    {
        var data = new DataWithKiloMeter();
        data.GetTotalBitLength().ShouldBe(96); // 8 + 80 + 8
    }

    #endregion

    #region Manual IBitSerializable - VersionSurrogate Tests

    [Fact]
    public void VersionSurrogate_RoundTrip_MSB()
    {
        var original = new DataWithVersion
        {
            Id = 0x0001,
            Version = new VersionSurrogate { Major = 3, Minor = 14, Build = 159, Revision = 2653 },
            Flags = 0xAA
        };

        var bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(19); // 2 + 16 + 1

        var restored = BitSerializerMSB.Deserialize<DataWithVersion>(bytes);

        restored.Id.ShouldBe((ushort)0x0001);
        restored.Version.Major.ShouldBe(3);
        restored.Version.Minor.ShouldBe(14);
        restored.Version.Build.ShouldBe(159);
        restored.Version.Revision.ShouldBe(2653);
        restored.Flags.ShouldBe((byte)0xAA);
    }

    [Fact]
    public void VersionSurrogate_RoundTrip_LSB()
    {
        var original = new DataWithVersion
        {
            Id = 0x1234,
            Version = new VersionSurrogate { Major = 1, Minor = 2, Build = 3, Revision = 4 },
            Flags = 0x55
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<DataWithVersion>(bytes);

        restored.Id.ShouldBe((ushort)0x1234);
        restored.Version.Major.ShouldBe(1);
        restored.Version.Minor.ShouldBe(2);
        restored.Version.Build.ShouldBe(3);
        restored.Version.Revision.ShouldBe(4);
        restored.Flags.ShouldBe((byte)0x55);
    }

    [Fact]
    public void VersionSurrogate_GetTotalBitLength()
    {
        var data = new DataWithVersion();
        data.GetTotalBitLength().ShouldBe(152); // 16 + 128 + 8
    }

    #endregion
}
