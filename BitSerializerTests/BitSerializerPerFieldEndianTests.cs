using Shouldly;
using BitSerializer;
using BitSerializer.CrcAlgorithms;

namespace BitSerializerTests;

public partial class BitSerializerPerFieldEndianTests
{
    #region Test Models

    /// <summary>
    /// 外层方法是 MSB（大端），但 ValueLittle 字段强制小端。
    /// 用于验证：同一帧内大端主体 + 个别小端字段（DMI 协议的典型模式）。
    /// </summary>
    [BitSerialize]
    public partial class MixedBigWithLittleField
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16)] public ushort ValueBig { get; set; }              // 跟随外层 MSB → 大端
        [BitField(16, Endian = BitEndian.Little)] public ushort ValueLittle { get; set; } // 强制小端
        [BitField(8)] public byte Footer { get; set; }
    }

    /// <summary>
    /// 外层 LSB（小端），单独字段强制大端。镜像测试。
    /// </summary>
    [BitSerialize]
    public partial class MixedLittleWithBigField
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16)] public ushort ValueLittle { get; set; }           // 跟随外层 LSB → 小端
        [BitField(16, Endian = BitEndian.Big)] public ushort ValueBig { get; set; } // 强制大端
        [BitField(8)] public byte Footer { get; set; }
    }

    /// <summary>
    /// 同字段同时显式 Endian = Big 与 Endian = Little，验证三种基础类型 + 多字段连续小端覆盖。
    /// 模拟 DmiOutContent 中连续多个 [FieldEndianness(Little)] 字段的场景。
    /// </summary>
    [BitSerialize]
    public partial class MultipleLittleOverrides
    {
        [BitField(16, Endian = BitEndian.Little)] public ushort A { get; set; }
        [BitField(32, Endian = BitEndian.Little)] public uint B { get; set; }
        [BitField(8, Endian = BitEndian.Little)] public byte C { get; set; }
    }

    /// <summary>
    /// 显式 Endian = Inherit（与不写 Endian 等价）。验证默认值。
    /// </summary>
    [BitSerialize]
    public partial class InheritIsDefault
    {
        [BitField(16, Endian = BitEndian.Inherit)] public ushort A { get; set; }
        [BitField(16)] public ushort B { get; set; }
    }

    /// <summary>
    /// 枚举字段 + endian 覆盖（DMI 中枚举字段也混用大小端）。
    /// </summary>
    public enum SegmentKind : ushort
    {
        Track = 1,
        Switch = 2,
        Crossing = 3,
    }

    [BitSerialize]
    public partial class EnumWithLittleEndian
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16, Endian = BitEndian.Little)] public SegmentKind Kind { get; set; }
    }

    #endregion

    [Fact]
    public void MSB_Frame_With_Little_Field_Should_Flip_Only_That_Field()
    {
        var data = new MixedBigWithLittleField
        {
            Header = 0xAB,
            ValueBig = 0x1234,
            ValueLittle = 0x5678,
            Footer = 0xCD,
        };

        var bytes = BitSerializerMSB.Serialize(data);

        // Layout:
        //   [0]    Header        = 0xAB
        //   [1..2] ValueBig (BE) = 0x12 0x34
        //   [3..4] ValueLittle (LE) = 0x78 0x56  ← 字段级小端，字节序反转
        //   [5]    Footer        = 0xCD
        bytes.ShouldBe(new byte[] { 0xAB, 0x12, 0x34, 0x78, 0x56, 0xCD });
    }

    [Fact]
    public void MSB_Frame_With_Little_Field_Should_Round_Trip()
    {
        var original = new MixedBigWithLittleField
        {
            Header = 0xAB,
            ValueBig = 0x1234,
            ValueLittle = 0x5678,
            Footer = 0xCD,
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var roundTripped = BitSerializerMSB.Deserialize<MixedBigWithLittleField>(bytes);

        roundTripped.Header.ShouldBe(original.Header);
        roundTripped.ValueBig.ShouldBe(original.ValueBig);
        roundTripped.ValueLittle.ShouldBe(original.ValueLittle);
        roundTripped.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void LSB_Frame_With_Big_Field_Should_Flip_Only_That_Field()
    {
        var data = new MixedLittleWithBigField
        {
            Header = 0xAB,
            ValueLittle = 0x1234,
            ValueBig = 0x5678,
            Footer = 0xCD,
        };

        var bytes = BitSerializerLSB.Serialize(data);

        // Layout:
        //   [0]    Header        = 0xAB
        //   [1..2] ValueLittle (LE) = 0x34 0x12
        //   [3..4] ValueBig (BE)    = 0x56 0x78  ← 字段级大端，字节序保持
        //   [5]    Footer        = 0xCD
        bytes.ShouldBe(new byte[] { 0xAB, 0x34, 0x12, 0x56, 0x78, 0xCD });
    }

    [Fact]
    public void LSB_Frame_With_Big_Field_Should_Round_Trip()
    {
        var original = new MixedLittleWithBigField
        {
            Header = 0xAB,
            ValueLittle = 0x1234,
            ValueBig = 0x5678,
            Footer = 0xCD,
        };

        var bytes = BitSerializerLSB.Serialize(original);
        var roundTripped = BitSerializerLSB.Deserialize<MixedLittleWithBigField>(bytes);

        roundTripped.Header.ShouldBe(original.Header);
        roundTripped.ValueLittle.ShouldBe(original.ValueLittle);
        roundTripped.ValueBig.ShouldBe(original.ValueBig);
        roundTripped.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Multiple_Little_Overrides_In_MSB_Frame_Should_All_Be_Little()
    {
        var data = new MultipleLittleOverrides
        {
            A = 0x1122,
            B = 0x33445566,
            C = 0x77,
        };

        var bytes = BitSerializerMSB.Serialize(data);

        // Every field overridden to Little — outer MSB has no effect on these fields.
        bytes.ShouldBe(new byte[] { 0x22, 0x11, 0x66, 0x55, 0x44, 0x33, 0x77 });
    }

    [Fact]
    public void Multiple_Little_Overrides_Should_Round_Trip_Under_Both_Methods()
    {
        var original = new MultipleLittleOverrides
        {
            A = 0xABCD,
            B = 0x12345678,
            C = 0xEF,
        };

        // 字段级 Endian 不受外层方法影响，两套方法产生相同字节。
        var msbBytes = BitSerializerMSB.Serialize(original);
        var lsbBytes = BitSerializerLSB.Serialize(original);
        msbBytes.ShouldBe(lsbBytes);

        var fromMsb = BitSerializerMSB.Deserialize<MultipleLittleOverrides>(msbBytes);
        var fromLsb = BitSerializerLSB.Deserialize<MultipleLittleOverrides>(lsbBytes);
        fromMsb.A.ShouldBe(original.A);
        fromMsb.B.ShouldBe(original.B);
        fromMsb.C.ShouldBe(original.C);
        fromLsb.A.ShouldBe(original.A);
        fromLsb.B.ShouldBe(original.B);
        fromLsb.C.ShouldBe(original.C);
    }

    [Fact]
    public void Inherit_Endian_Is_Identical_To_Omitting_Argument()
    {
        var data = new InheritIsDefault { A = 0x1234, B = 0x5678 };

        var msb = BitSerializerMSB.Serialize(data);
        var lsb = BitSerializerLSB.Serialize(data);

        msb.ShouldBe(new byte[] { 0x12, 0x34, 0x56, 0x78 });
        lsb.ShouldBe(new byte[] { 0x34, 0x12, 0x78, 0x56 });
    }

    [Fact]
    public void Enum_Field_With_Little_Endian_Should_Flip_Bytes()
    {
        var data = new EnumWithLittleEndian
        {
            Header = 0x01,
            Kind = SegmentKind.Switch, // 2
        };

        var bytes = BitSerializerMSB.Serialize(data);

        // Enum widens to underlying ushort; little-endian → low byte first.
        bytes.ShouldBe(new byte[] { 0x01, 0x02, 0x00 });

        var roundTripped = BitSerializerMSB.Deserialize<EnumWithLittleEndian>(bytes);
        roundTripped.Kind.ShouldBe(SegmentKind.Switch);
    }

    #region CRC field endian override (PR review P1)

    /// <summary>
    /// CRC 字段标 Endian = Little，外层 SerializeMSB 时 CRC 也应按小端写入 buffer，
    /// 否则审核意见所指的"CRC 块用 outer helper 重写覆盖 ResolveFieldHelper 选择"会
    /// 导致 wire 字节序错乱、deserialize 校验失败。
    /// </summary>
    [BitSerialize]
    public partial class CrcFieldWithLittleEndian
    {
        [BitField(8), BitCrcInclude(nameof(Crc))]
        public byte Header { get; set; }

        [BitField(16), BitCrcInclude(nameof(Crc))]
        public ushort Payload { get; set; }

        [BitField(16, Endian = BitEndian.Little), BitCrc(typeof(CrcCcitt), InitialValue = 0, ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void CrcField_With_Little_Endian_Writes_LittleEndian_Bytes_Under_MSB_Frame()
    {
        var data = new CrcFieldWithLittleEndian { Header = 0xAB, Payload = 0x1234 };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        // CRC over [0xAB, 0x12, 0x34] via CrcCcitt.
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(new byte[] { 0xAB, 0x12, 0x34 });
        ushort want = (ushort)algo.Result;

        // CRC field is little-endian: low byte at [3], high byte at [4].
        bytes[3].ShouldBe((byte)(want & 0xFF));
        bytes[4].ShouldBe((byte)((want >> 8) & 0xFF));
        // Sanity: data.Crc backfill matches.
        data.Crc.ShouldBe(want);
    }

    [Fact]
    public void CrcField_With_Little_Endian_Round_Trips_With_Validation()
    {
        // ValidateOnDeserialize = true: if serializer wrote CRC in the wrong byte order,
        // deserializer would read a different value and throw InvalidDataException.
        var original = new CrcFieldWithLittleEndian { Header = 0x7E, Payload = 0xBEEF };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        var result = BitSerializerMSB.Deserialize<CrcFieldWithLittleEndian>(bytes);
        result.Header.ShouldBe(original.Header);
        result.Payload.ShouldBe(original.Payload);
        result.Crc.ShouldBe(original.Crc);
    }

    #endregion
}
