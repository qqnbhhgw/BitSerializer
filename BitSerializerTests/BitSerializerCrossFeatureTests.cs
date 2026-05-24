using Shouldly;
using BitSerializer;
using BitSerializer.CrcAlgorithms;

namespace BitSerializerTests;

/// <summary>
/// 三大新特性 (PR #2/3/4: Endian / WholeBuffer CRC / LengthPrefixString) 的两两 / 三方组合烟雾测试。
/// 单 feature 的 happy/error path 已在各自文件里覆盖，这里只验证组合不会出现：
/// (a) 编译期误报 (false-positive diagnostic), (b) 运行期 byte 流互相破坏。
/// </summary>
public partial class BitSerializerCrossFeatureTests
{
    #region Test Models

    /// <summary>
    /// Endian 字段紧跟在 LengthPrefixString 之后：LPS 是 byte-aligned 动态字段（编码后 byte 数总是 8 倍），
    /// 后置的 Endian scalar 不应触发 BITS033（前置动态非字节对齐）。
    /// </summary>
    [BitSerialize]
    public partial class LpsBeforeEndianScalar
    {
        [BitField(8)] public byte Header { get; set; }
        [BitLengthPrefixString(8, Encoding = BitStringEncoding.ASCII)]
        public string Tag { get; set; } = "";
        [BitField(32, Endian = BitEndian.Little)] public uint LittleAfter { get; set; }
        [BitField(16, Endian = BitEndian.Big)] public ushort BigAfter { get; set; }
    }

    /// <summary>
    /// Endian 字段紧接 LengthPrefixString：先 Big-endian 头 → LPS → Little-endian 尾。
    /// 验证两侧 byte order 各自独立切换，互不污染。
    /// </summary>
    [BitSerialize]
    public partial class EndianAroundLps
    {
        [BitField(32, Endian = BitEndian.Big)] public uint BigHeader { get; set; }
        [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8)]
        public string Name { get; set; } = "";
        [BitField(32, Endian = BitEndian.Little)] public uint LittleTrailer { get; set; }
    }

    /// <summary>
    /// WholeBuffer CRC 覆盖含 LengthPrefixString 的 buffer。
    /// LPS 在 CRC 之前（leading 动态），SkipTailBytes ≥ CRC slot 长度。
    /// 这是 BITS035 文档里允许的 "所有动态都在 CRC 前 + SkipTail 保护 CRC" 模式。
    /// </summary>
    [BitSerialize]
    public partial class WholeBufferCrcWithLeadingLps
    {
        [BitField(8)] public byte Sync { get; set; }
        [BitLengthPrefixString(8, Encoding = BitStringEncoding.ASCII)]
        public string Tag { get; set; } = "";
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// WholeBuffer CRC 覆盖含 per-field Endian 标量字段的 buffer。
    /// CRC 读原始 byte，所以 byte-order 翻转后内容对 CRC 是透明的；只验证 CRC 计算稳定不抛。
    /// </summary>
    [BitSerialize]
    public partial class WholeBufferCrcWithEndianFields
    {
        [BitField(32, Endian = BitEndian.Big)] public uint BigField { get; set; }
        [BitField(32, Endian = BitEndian.Little)] public uint LittleField { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// 三特性同框：Endian header → LPS payload → CRC 全 buffer。
    /// </summary>
    [BitSerialize]
    public partial class AllThreeCombined
    {
        [BitField(32, Endian = BitEndian.Little)] public uint Magic { get; set; }
        [BitField(8)] public byte Type { get; set; }
        [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8)]
        public string Body { get; set; } = "";
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    #endregion

    [Fact]
    public void LpsBeforeEndianScalar_Roundtrip()
    {
        var src = new LpsBeforeEndianScalar
        {
            Header = 0xAB,
            Tag = "X1",
            LittleAfter = 0x11223344u,
            BigAfter = 0x55AA,
        };
        byte[] bytes = BitSerializerMSB.Serialize(src);
        var dst = BitSerializerMSB.Deserialize<LpsBeforeEndianScalar>(bytes);
        dst.Header.ShouldBe(src.Header);
        dst.Tag.ShouldBe(src.Tag);
        dst.LittleAfter.ShouldBe(src.LittleAfter);
        dst.BigAfter.ShouldBe(src.BigAfter);
    }

    [Fact]
    public void EndianAroundLps_ByteOrderRespectedOnBothSides()
    {
        var src = new EndianAroundLps
        {
            BigHeader = 0x01020304u,
            Name = "test",
            LittleTrailer = 0x0A0B0C0Du,
        };
        byte[] bytes = BitSerializerMSB.Serialize(src);

        // BigHeader: 4 bytes MSB first
        bytes[0].ShouldBe((byte)0x01);
        bytes[1].ShouldBe((byte)0x02);
        bytes[2].ShouldBe((byte)0x03);
        bytes[3].ShouldBe((byte)0x04);

        // LengthPrefix (16 bits big-endian) + "test" (4 bytes UTF-8)
        bytes[4].ShouldBe((byte)0x00);
        bytes[5].ShouldBe((byte)0x04);
        bytes[6].ShouldBe((byte)'t');
        bytes[7].ShouldBe((byte)'e');
        bytes[8].ShouldBe((byte)'s');
        bytes[9].ShouldBe((byte)'t');

        // LittleTrailer: 4 bytes LSB first → 0x0D 0x0C 0x0B 0x0A
        bytes[10].ShouldBe((byte)0x0D);
        bytes[11].ShouldBe((byte)0x0C);
        bytes[12].ShouldBe((byte)0x0B);
        bytes[13].ShouldBe((byte)0x0A);

        var dst = BitSerializerMSB.Deserialize<EndianAroundLps>(bytes);
        dst.BigHeader.ShouldBe(src.BigHeader);
        dst.Name.ShouldBe(src.Name);
        dst.LittleTrailer.ShouldBe(src.LittleTrailer);
    }

    [Fact]
    public void WholeBufferCrcWithLeadingLps_Roundtrip()
    {
        var src = new WholeBufferCrcWithLeadingLps
        {
            Sync = 0xFE,
            Tag = "HELLO",
        };
        byte[] bytes = BitSerializerMSB.Serialize(src);
        var dst = BitSerializerMSB.Deserialize<WholeBufferCrcWithLeadingLps>(bytes);
        dst.Sync.ShouldBe(src.Sync);
        dst.Tag.ShouldBe(src.Tag);
        dst.Crc.ShouldBe(src.Crc);
    }

    [Fact]
    public void WholeBufferCrcWithEndianFields_Roundtrip()
    {
        var src = new WholeBufferCrcWithEndianFields
        {
            BigField = 0x11223344u,
            LittleField = 0xAABBCCDDu,
        };
        byte[] bytes = BitSerializerMSB.Serialize(src);

        // Big: MSB first
        bytes[0].ShouldBe((byte)0x11);
        bytes[1].ShouldBe((byte)0x22);
        bytes[2].ShouldBe((byte)0x33);
        bytes[3].ShouldBe((byte)0x44);
        // Little: LSB first
        bytes[4].ShouldBe((byte)0xDD);
        bytes[5].ShouldBe((byte)0xCC);
        bytes[6].ShouldBe((byte)0xBB);
        bytes[7].ShouldBe((byte)0xAA);

        var dst = BitSerializerMSB.Deserialize<WholeBufferCrcWithEndianFields>(bytes);
        dst.BigField.ShouldBe(src.BigField);
        dst.LittleField.ShouldBe(src.LittleField);
        dst.Crc.ShouldBe(src.Crc);
    }

    [Fact]
    public void AllThreeCombined_Roundtrip()
    {
        var src = new AllThreeCombined
        {
            Magic = 0xDEADBEEFu,
            Type = 0x42,
            Body = "中文与 ASCII 混排",
        };
        byte[] bytes = BitSerializerMSB.Serialize(src);

        // Little-endian magic: LSB first
        bytes[0].ShouldBe((byte)0xEF);
        bytes[1].ShouldBe((byte)0xBE);
        bytes[2].ShouldBe((byte)0xAD);
        bytes[3].ShouldBe((byte)0xDE);
        bytes[4].ShouldBe((byte)0x42);

        var dst = BitSerializerMSB.Deserialize<AllThreeCombined>(bytes);
        dst.Magic.ShouldBe(src.Magic);
        dst.Type.ShouldBe(src.Type);
        dst.Body.ShouldBe(src.Body);
        dst.Crc.ShouldBe(src.Crc);
    }
}
