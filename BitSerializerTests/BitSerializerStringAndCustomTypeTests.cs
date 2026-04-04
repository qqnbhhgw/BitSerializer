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

    #region Issue 1 - Dynamic base + derived field offset

    // Base class with a terminated string (dynamic length)
    [BitSerialize]
    public partial class DynamicBase
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitTerminatedString]
        public string Name { get; set; } = "";

        [BitField(8)]
        public byte BaseTrailer { get; set; }
    }

    // Derived class: fields must start after base's actual (dynamic) end
    [BitSerialize]
    public partial class DerivedFromDynamic : DynamicBase
    {
        [BitField(16)]
        public ushort DerivedValue { get; set; }

        [BitField(8)]
        public byte DerivedTrailer { get; set; }
    }

    // Base class with manual IBitSerializable (dynamic length, no explicit bits)
    [BitSerialize]
    public partial class ManualBase
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public KiloMeter Distance { get; set; } = new();

        [BitField(8)]
        public byte BaseTrailer { get; set; }
    }

    // Derived class from manual IBitSerializable base
    [BitSerialize]
    public partial class DerivedFromManualBase : ManualBase
    {
        [BitField(16)]
        public ushort DerivedValue { get; set; }
    }

    [Fact]
    public void DynamicBase_DerivedFieldOffset_RoundTrip_MSB()
    {
        var original = new DerivedFromDynamic
        {
            Header = 0xAA,
            Name = "Hello",
            BaseTrailer = 0xBB,
            DerivedValue = 0x1234,
            DerivedTrailer = 0xCC
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DerivedFromDynamic>(bytes);

        restored.Header.ShouldBe((byte)0xAA);
        restored.Name.ShouldBe("Hello");
        restored.BaseTrailer.ShouldBe((byte)0xBB);
        restored.DerivedValue.ShouldBe((ushort)0x1234);
        restored.DerivedTrailer.ShouldBe((byte)0xCC);
    }

    [Fact]
    public void DynamicBase_DerivedFieldOffset_RoundTrip_LSB()
    {
        var original = new DerivedFromDynamic
        {
            Header = 0xAA,
            Name = "Test",
            BaseTrailer = 0xBB,
            DerivedValue = 0x5678,
            DerivedTrailer = 0xDD
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<DerivedFromDynamic>(bytes);

        restored.Header.ShouldBe((byte)0xAA);
        restored.Name.ShouldBe("Test");
        restored.BaseTrailer.ShouldBe((byte)0xBB);
        restored.DerivedValue.ShouldBe((ushort)0x5678);
        restored.DerivedTrailer.ShouldBe((byte)0xDD);
    }

    [Fact]
    public void DynamicBase_DerivedFieldOffset_EmptyString_MSB()
    {
        var original = new DerivedFromDynamic
        {
            Header = 0x01,
            Name = "",
            BaseTrailer = 0x02,
            DerivedValue = 0xABCD,
            DerivedTrailer = 0x03
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DerivedFromDynamic>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Name.ShouldBe("");
        restored.BaseTrailer.ShouldBe((byte)0x02);
        restored.DerivedValue.ShouldBe((ushort)0xABCD);
        restored.DerivedTrailer.ShouldBe((byte)0x03);
    }

    [Fact]
    public void DynamicBase_GetTotalBitLength()
    {
        var data = new DerivedFromDynamic
        {
            Name = "Hi" // 2 chars + null terminator = 3 bytes = 24 bits
        };
        // Header(8) + TerminatedString("Hi" = 24) + BaseTrailer(8) + DerivedValue(16) + DerivedTrailer(8) = 64
        data.GetTotalBitLength().ShouldBe(64);
    }

    [Fact]
    public void ManualBase_DerivedFieldOffset_RoundTrip_MSB()
    {
        var original = new DerivedFromManualBase
        {
            Header = 0x01,
            Distance = new KiloMeter { Value = 42.5 },
            BaseTrailer = 0xFF,
            DerivedValue = 0x9876
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DerivedFromManualBase>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Distance.Value.ShouldBe(42.5);
        restored.BaseTrailer.ShouldBe((byte)0xFF);
        restored.DerivedValue.ShouldBe((ushort)0x9876);
    }

    #endregion

    #region Issue 2 - UTF-8 multi-byte character truncation

    [BitSerialize]
    public partial class Utf8TruncationData
    {
        [BitFixedString(5, Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = "";
    }

    [BitSerialize]
    public partial class Utf8TruncationData3
    {
        [BitFixedString(3, Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = "";
    }

    [BitSerialize]
    public partial class Utf8TruncationData4
    {
        [BitFixedString(4, Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = "";
    }

    [BitSerialize]
    public partial class Utf8ExactFitData
    {
        [BitFixedString(6, Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = "";
    }

    [Fact]
    public void Utf8FixedString_Truncation_CJK_RoundTrip_MSB()
    {
        // "你好" = E4 BD A0 E5 A5 BD (6 bytes), byteLen=5
        // Should truncate to "你" (3 bytes), not split "好"
        var original = new Utf8TruncationData { Text = "你好" };
        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<Utf8TruncationData>(bytes);
        restored.Text.ShouldBe("你");
    }

    [Fact]
    public void Utf8FixedString_Truncation_MixedAsciiCJK_RoundTrip_MSB()
    {
        // "ab你" = 61 62 E4 BD A0 (5 bytes), byteLen=4
        // Should truncate to "ab" (2 bytes), not split "你"
        var original = new Utf8TruncationData4 { Text = "ab你" };
        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<Utf8TruncationData4>(bytes);
        restored.Text.ShouldBe("ab");
    }

    [Fact]
    public void Utf8FixedString_ExactFit_CJK_RoundTrip_MSB()
    {
        // "你好" = 6 bytes, byteLen=6 → exact fit, no truncation
        var original = new Utf8ExactFitData { Text = "你好" };
        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<Utf8ExactFitData>(bytes);
        restored.Text.ShouldBe("你好");
    }

    [Fact]
    public void Utf8FixedString_Truncation_CompleteCharAtBoundary_RoundTrip_MSB()
    {
        // "你a" = E4 BD A0 61 (4 bytes), byteLen=3
        // "你" fits exactly in 3 bytes, should keep it (not drop to empty)
        var original = new Utf8TruncationData3 { Text = "你a" };
        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<Utf8TruncationData3>(bytes);
        restored.Text.ShouldBe("你");
    }

    [Fact]
    public void Utf8FixedString_AsciiOnly_NoTruncation_RoundTrip_MSB()
    {
        // ASCII strings should not be affected by the UTF-8 fix
        var original = new Utf8TruncationData { Text = "Hello" };
        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<Utf8TruncationData>(bytes);
        restored.Text.ShouldBe("Hello");
    }

    #endregion

    #region Issue 3 - IBitSerializable List/Array elements

    [BitSerialize]
    public partial class DataWithKiloMeterList
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<KiloMeter> Distances { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class DataWithKiloMeterArray
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(2)]
        public KiloMeter[] Distances { get; set; } = [];

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [Fact]
    public void KiloMeterList_RoundTrip_MSB()
    {
        var original = new DataWithKiloMeterList
        {
            Count = 2,
            Distances = new List<KiloMeter>
            {
                new KiloMeter { Value = 1.5 },
                new KiloMeter { Value = 99.999 }
            },
            Footer = 0xEE
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DataWithKiloMeterList>(bytes);

        restored.Count.ShouldBe((byte)2);
        restored.Distances.Count.ShouldBe(2);
        restored.Distances[0].Value.ShouldBe(1.5);
        restored.Distances[1].Value.ShouldBe(99.999);
        restored.Footer.ShouldBe((byte)0xEE);
    }

    [Fact]
    public void KiloMeterList_RoundTrip_LSB()
    {
        var original = new DataWithKiloMeterList
        {
            Count = 1,
            Distances = new List<KiloMeter>
            {
                new KiloMeter { Value = 42.0 }
            },
            Footer = 0xAA
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<DataWithKiloMeterList>(bytes);

        restored.Count.ShouldBe((byte)1);
        restored.Distances.Count.ShouldBe(1);
        restored.Distances[0].Value.ShouldBe(42.0);
        restored.Footer.ShouldBe((byte)0xAA);
    }

    [Fact]
    public void KiloMeterArray_RoundTrip_MSB()
    {
        var original = new DataWithKiloMeterArray
        {
            Header = 0x01,
            Distances = new[]
            {
                new KiloMeter { Value = 10.0 },
                new KiloMeter { Value = 20.0 }
            },
            Footer = 0x02
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DataWithKiloMeterArray>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Distances.Length.ShouldBe(2);
        restored.Distances[0].Value.ShouldBe(10.0);
        restored.Distances[1].Value.ShouldBe(20.0);
        restored.Footer.ShouldBe((byte)0x02);
    }

    [Fact]
    public void KiloMeterList_GetTotalBitLength()
    {
        var data = new DataWithKiloMeterList
        {
            Count = 3,
            Distances = new List<KiloMeter>
            {
                new KiloMeter(), new KiloMeter(), new KiloMeter()
            }
        };
        // Count(8) + 3 * KiloMeter(80) + Footer(8) = 256
        data.GetTotalBitLength().ShouldBe(256);
    }

    [Fact]
    public void KiloMeterArray_GetTotalBitLength()
    {
        var data = new DataWithKiloMeterArray
        {
            Distances = new[] { new KiloMeter(), new KiloMeter() }
        };
        // Header(8) + 2 * KiloMeter(80) + Footer(8) = 176
        data.GetTotalBitLength().ShouldBe(176);
    }

    #endregion

    #region Fix - Struct IBitSerializable boxing

    // Struct implementing IBitSerializable: tests that deserialization
    // does not lose data due to interface boxing of value types
    public struct CompactInt16 : IBitSerializable
    {
        public short Value { get; set; }

        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperLSB.SetValueLength<short>(bytes, bitOffset, 16, Value);
            return 16;
        }

        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperMSB.SetValueLength<short>(bytes, bitOffset, 16, Value);
            return 16;
        }

        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperLSB.ValueLength<short>(bytes, bitOffset, 16);
            return 16;
        }

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            Value = BitHelperMSB.ValueLength<short>(bytes, bitOffset, 16);
            return 16;
        }

        public int GetTotalBitLength() => 16;
    }

    [BitSerialize]
    public partial class DataWithStructField
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public CompactInt16 Data { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class DataWithStructList
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<CompactInt16> Items { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class DataWithStructArray
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(3)]
        public CompactInt16[] Items { get; set; } = [];

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [Fact]
    public void StructField_RoundTrip_MSB()
    {
        var original = new DataWithStructField
        {
            Header = 0xAA,
            Data = new CompactInt16 { Value = 12345 },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DataWithStructField>(bytes);

        restored.Header.ShouldBe((byte)0xAA);
        restored.Data.Value.ShouldBe((short)12345);
        restored.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void StructList_RoundTrip_MSB()
    {
        var original = new DataWithStructList
        {
            Count = 2,
            Items = new List<CompactInt16>
            {
                new CompactInt16 { Value = 100 },
                new CompactInt16 { Value = -200 }
            },
            Footer = 0xCC
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DataWithStructList>(bytes);

        restored.Count.ShouldBe((byte)2);
        restored.Items.Count.ShouldBe(2);
        restored.Items[0].Value.ShouldBe((short)100);
        restored.Items[1].Value.ShouldBe((short)-200);
        restored.Footer.ShouldBe((byte)0xCC);
    }

    [Fact]
    public void StructArray_RoundTrip_LSB()
    {
        var original = new DataWithStructArray
        {
            Header = 0x01,
            Items = new[]
            {
                new CompactInt16 { Value = 1000 },
                new CompactInt16 { Value = 2000 },
                new CompactInt16 { Value = 3000 }
            },
            Footer = 0x02
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<DataWithStructArray>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Items.Length.ShouldBe(3);
        restored.Items[0].Value.ShouldBe((short)1000);
        restored.Items[1].Value.ShouldBe((short)2000);
        restored.Items[2].Value.ShouldBe((short)3000);
        restored.Footer.ShouldBe((byte)0x02);
    }

    #endregion

    #region Dynamic [BitSerialize] list elements

    // Inner type with terminated string → dynamic length
    [BitSerialize]
    public partial class DynamicInner
    {
        [BitField(8)]
        public byte Tag { get; set; }

        [BitTerminatedString]
        public string Name { get; set; } = "";

        [BitField(8)]
        public byte Marker { get; set; }
    }

    // Fixed-count list of dynamic elements
    [BitSerialize]
    public partial class FixedCountDynamicList
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(2)]
        public DynamicInner[] Items { get; set; } = [];

        [BitField(8)]
        public byte Footer { get; set; }
    }

    // Dynamic-count list of dynamic elements
    [BitSerialize]
    public partial class DynamicCountDynamicList
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<DynamicInner> Items { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [Fact]
    public void FixedCountDynamicList_RoundTrip_MSB()
    {
        var original = new FixedCountDynamicList
        {
            Header = 0xAA,
            Items = new[]
            {
                new DynamicInner { Tag = 0x01, Name = "Hi", Marker = 0x11 },
                new DynamicInner { Tag = 0x02, Name = "Hello", Marker = 0x22 }
            },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<FixedCountDynamicList>(bytes);

        restored.Header.ShouldBe((byte)0xAA);
        restored.Items.Length.ShouldBe(2);
        restored.Items[0].Tag.ShouldBe((byte)0x01);
        restored.Items[0].Name.ShouldBe("Hi");
        restored.Items[0].Marker.ShouldBe((byte)0x11);
        restored.Items[1].Tag.ShouldBe((byte)0x02);
        restored.Items[1].Name.ShouldBe("Hello");
        restored.Items[1].Marker.ShouldBe((byte)0x22);
        restored.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void FixedCountDynamicList_RoundTrip_LSB()
    {
        var original = new FixedCountDynamicList
        {
            Header = 0x01,
            Items = new[]
            {
                new DynamicInner { Tag = 0xAA, Name = "X", Marker = 0xBB },
                new DynamicInner { Tag = 0xCC, Name = "YZ", Marker = 0xDD }
            },
            Footer = 0xFF
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<FixedCountDynamicList>(bytes);

        restored.Header.ShouldBe((byte)0x01);
        restored.Items[0].Tag.ShouldBe((byte)0xAA);
        restored.Items[0].Name.ShouldBe("X");
        restored.Items[0].Marker.ShouldBe((byte)0xBB);
        restored.Items[1].Tag.ShouldBe((byte)0xCC);
        restored.Items[1].Name.ShouldBe("YZ");
        restored.Items[1].Marker.ShouldBe((byte)0xDD);
        restored.Footer.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void FixedCountDynamicList_GetTotalBitLength()
    {
        var data = new FixedCountDynamicList
        {
            Header = 0x01,
            Items = new[]
            {
                new DynamicInner { Tag = 0, Name = "AB", Marker = 0 },   // 8 + (2+1)*8 + 8 = 40
                new DynamicInner { Tag = 0, Name = "CDEF", Marker = 0 }  // 8 + (4+1)*8 + 8 = 56
            },
            Footer = 0x02
        };
        // Header(8) + Items[0](40) + Items[1](56) + Footer(8) = 112
        data.GetTotalBitLength().ShouldBe(112);
    }

    [Fact]
    public void DynamicCountDynamicList_RoundTrip_MSB()
    {
        var original = new DynamicCountDynamicList
        {
            Count = 3,
            Items = new List<DynamicInner>
            {
                new DynamicInner { Tag = 0x01, Name = "A", Marker = 0x11 },
                new DynamicInner { Tag = 0x02, Name = "BB", Marker = 0x22 },
                new DynamicInner { Tag = 0x03, Name = "CCC", Marker = 0x33 }
            },
            Footer = 0xFF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<DynamicCountDynamicList>(bytes);

        restored.Count.ShouldBe((byte)3);
        restored.Items.Count.ShouldBe(3);
        restored.Items[0].Tag.ShouldBe((byte)0x01);
        restored.Items[0].Name.ShouldBe("A");
        restored.Items[0].Marker.ShouldBe((byte)0x11);
        restored.Items[1].Tag.ShouldBe((byte)0x02);
        restored.Items[1].Name.ShouldBe("BB");
        restored.Items[1].Marker.ShouldBe((byte)0x22);
        restored.Items[2].Tag.ShouldBe((byte)0x03);
        restored.Items[2].Name.ShouldBe("CCC");
        restored.Items[2].Marker.ShouldBe((byte)0x33);
        restored.Footer.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void DynamicCountDynamicList_GetTotalBitLength()
    {
        var data = new DynamicCountDynamicList
        {
            Count = 2,
            Items = new List<DynamicInner>
            {
                new DynamicInner { Tag = 0, Name = "", Marker = 0 },    // 8 + 8 + 8 = 24
                new DynamicInner { Tag = 0, Name = "XYZ", Marker = 0 }  // 8 + (3+1)*8 + 8 = 48
            },
            Footer = 0
        };
        // Count(8) + Items[0](24) + Items[1](48) + Footer(8) = 88
        data.GetTotalBitLength().ShouldBe(88);
    }

    #endregion

    #region Regression - Nested container with fixed-count dynamic [BitSerialize] list

    // Outer type nests a type that contains a fixed-count list of dynamic elements.
    // HasDynamicLengthRecursive must propagate dynamic-ness through the list
    // so that Outer uses runtime offsets for trailing fields.
    [BitSerialize]
    public partial class OuterWithNestedDynamicList
    {
        [BitField(8)]
        public byte Prefix { get; set; }

        [BitField]
        public FixedCountDynamicList Middle { get; set; } = new();

        [BitField(8)]
        public byte Suffix { get; set; }
    }

    [Fact]
    public void NestedDynamicList_RoundTrip_MSB()
    {
        var original = new OuterWithNestedDynamicList
        {
            Prefix = 0xAA,
            Middle = new FixedCountDynamicList
            {
                Header = 0x01,
                Items = new[]
                {
                    new DynamicInner { Tag = 0x10, Name = "Hi", Marker = 0x11 },
                    new DynamicInner { Tag = 0x20, Name = "World", Marker = 0x22 }
                },
                Footer = 0x02
            },
            Suffix = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var restored = BitSerializerMSB.Deserialize<OuterWithNestedDynamicList>(bytes);

        restored.Prefix.ShouldBe((byte)0xAA);
        restored.Middle.Header.ShouldBe((byte)0x01);
        restored.Middle.Items.Length.ShouldBe(2);
        restored.Middle.Items[0].Tag.ShouldBe((byte)0x10);
        restored.Middle.Items[0].Name.ShouldBe("Hi");
        restored.Middle.Items[0].Marker.ShouldBe((byte)0x11);
        restored.Middle.Items[1].Tag.ShouldBe((byte)0x20);
        restored.Middle.Items[1].Name.ShouldBe("World");
        restored.Middle.Items[1].Marker.ShouldBe((byte)0x22);
        restored.Middle.Footer.ShouldBe((byte)0x02);
        restored.Suffix.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void NestedDynamicList_RoundTrip_LSB()
    {
        var original = new OuterWithNestedDynamicList
        {
            Prefix = 0x01,
            Middle = new FixedCountDynamicList
            {
                Header = 0xAA,
                Items = new[]
                {
                    new DynamicInner { Tag = 0x10, Name = "X", Marker = 0x11 },
                    new DynamicInner { Tag = 0x20, Name = "YZ", Marker = 0x22 }
                },
                Footer = 0xBB
            },
            Suffix = 0xFF
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var restored = BitSerializerLSB.Deserialize<OuterWithNestedDynamicList>(bytes);

        restored.Prefix.ShouldBe((byte)0x01);
        restored.Middle.Header.ShouldBe((byte)0xAA);
        restored.Middle.Items[0].Name.ShouldBe("X");
        restored.Middle.Items[1].Name.ShouldBe("YZ");
        restored.Middle.Footer.ShouldBe((byte)0xBB);
        restored.Suffix.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void NestedDynamicList_GetTotalBitLength()
    {
        var data = new OuterWithNestedDynamicList
        {
            Middle = new FixedCountDynamicList
            {
                Items = new[]
                {
                    new DynamicInner { Name = "AB" },   // 8 + (2+1)*8 + 8 = 40
                    new DynamicInner { Name = "CDEF" }  // 8 + (4+1)*8 + 8 = 56
                }
            }
        };
        // Prefix(8) + Middle.Header(8) + Items[0](40) + Items[1](56) + Middle.Footer(8) + Suffix(8) = 128
        data.GetTotalBitLength().ShouldBe(128);
    }

    #endregion
}
