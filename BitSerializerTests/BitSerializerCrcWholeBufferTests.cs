using Shouldly;
using BitSerializer;
using BitSerializer.CrcAlgorithms;

namespace BitSerializerTests;

public partial class BitSerializerCrcWholeBufferTests
{
    #region Test Models

    /// <summary>
    /// 静态长度 + WholeBuffer：CRC 覆盖 Header + Payload (3 字节)，SkipTail=2 保护 CRC 自身。
    /// </summary>
    [BitSerialize]
    public partial class BasicWholeBufferPacket
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16)] public ushort Payload { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// DMI 风格：SkipHead + SkipTail 同时非零。Sync(1) + Type(1) + Length(2) + Crc(2) + EOF(2)。
    /// CRC 覆盖 Type + Length（下标 1..5），与 DMI 的 UartCrc16(bytes, 1, len-4) 对应。
    /// </summary>
    [BitSerialize]
    public partial class DmiLikeFrame
    {
        [BitField(8)] public byte Sync { get; set; }
        [BitField(8)] public byte Type { get; set; }
        [BitField(16)] public ushort Length { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipHeadBytes = 1, SkipTailBytes = 4)]
        public ushort Crc { get; set; }
        [BitField(16)] public ushort EndOfFrame { get; set; }
    }

    /// <summary>
    /// 动态 List + WholeBuffer：CRC 覆盖 Count + 全部 Data，SkipTail=2 保护 CRC。
    /// 这是 IncludeRange 模式表达不了的：List 是动态字段，[BitCrcInclude] 在 list 上工作但
    /// 仍受 BITS017 字节对齐限制（这里 byte list 满足，但用 WholeBuffer 模式更直观）。
    /// </summary>
    [BitSerialize]
    public partial class DynamicListWholeBufferPacket
    {
        [BitField(8)] public byte Count { get; set; }

        [BitField(8), BitFieldRelated(nameof(Count))]
        public List<byte> Data { get; set; } = new();

        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// 多态 + 动态子类型 + WholeBuffer：这是 BITS017 在 IncludeRange 模式下被卡死的典型场景。
    /// TypeBPayload 含 List&lt;byte&gt;（动态），传统 [BitCrcInclude] 路径会因子类型不字节对齐报错。
    /// </summary>
    public abstract class PolyPayloadBase { }

    [BitSerialize]
    public partial class PolyTypeA : PolyPayloadBase
    {
        [BitField(8)] public byte X { get; set; }
        [BitField(8)] public byte Y { get; set; }
    }

    [BitSerialize]
    public partial class PolyTypeB : PolyPayloadBase
    {
        [BitField(8)] public byte Marker { get; set; }
        [BitField(8), BitFieldRelated(nameof(Marker))]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class PolyWholeBufferPacket
    {
        [BitField(8)] public byte Discriminator { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Discriminator))]
        [BitPoly(1, typeof(PolyTypeA))]
        [BitPoly(2, typeof(PolyTypeB))]
        public PolyPayloadBase Payload { get; set; } = new PolyTypeA();

        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// WholeBuffer + ValidateOnDeserialize.
    /// </summary>
    [BitSerialize]
    public partial class ValidatingWholeBufferPacket
    {
        [BitField(8)] public byte A { get; set; }
        [BitField(8)] public byte B { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2, ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// 用户配置错误：SkipHead + SkipTail ≥ totalBytes，CRC 范围为空，序列化时抛运行时异常。
    /// </summary>
    [BitSerialize]
    public partial class DegenerateSkipPacket
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipHeadBytes = 10, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// 零长度 CRC 范围（review P2）：SkipHead + SkipTail == totalBytes，_crcEnd == _crcStart。
    /// 之前 `if (_crcEnd &lt; _crcStart)` 漏掉这种情况，CRC.Update 在空 span 上跑、返回 initial value，
    /// 静默错误。修复后用 `&lt;=` 应抛异常。
    /// totalBytes = 2 (just the CRC), SkipHead = 0, SkipTail = 2 → start=0, end=0 → empty.
    /// </summary>
    [BitSerialize]
    public partial class ZeroLengthCrcRangePacket
    {
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipHeadBytes = 0, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    /// <summary>
    /// Review P2：CRC 字段在 buffer 开头（少见但合法），SkipHeadBytes 覆盖 CRC slot。
    /// Crc[0..2) + Payload[2..3) + More[3..5) = 5 bytes. SkipHead=2 包含 CRC slot, SkipTail=0.
    /// CRC 范围 = [2, 5) 覆盖 Payload + More, 不包含 CRC slot 自身 → 合法。
    /// </summary>
    [BitSerialize]
    public partial class CrcAtHeadPacket
    {
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipHeadBytes = 2, SkipTailBytes = 0)]
        public ushort Crc { get; set; }
        [BitField(8)] public byte Payload { get; set; }
        [BitField(16)] public ushort More { get; set; }
    }

    /// <summary>
    /// 嵌套场景（review P1）：外层 1-bit 字段使内层 WholeBuffer 子帧从 bitOffset=1 开始。
    /// 没有字节对齐守卫的话，(bitOffset / 8) 会截断到上一个字节，导致 CRC 范围错位。
    /// 守卫应在序列化时抛 InvalidDataException 而非默默算错。
    /// </summary>
    [BitSerialize]
    public partial class InnerWholeBufferFrame
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(16)] public ushort Payload { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 2)]
        public ushort Crc { get; set; }
    }

    [BitSerialize]
    public partial class OuterWith1BitPrefix
    {
        [BitField(1)] public byte Flag { get; set; }
        [BitField(7)] public byte Padding { get; set; } // 让 Inner 从 bit 8 开始（字节对齐）
        [BitField] public InnerWholeBufferFrame Inner { get; set; } = new();
    }

    [BitSerialize]
    public partial class OuterWith1BitMisalignment
    {
        [BitField(1)] public byte Flag { get; set; } // 1 bit → Inner 从 bit 1 开始（不字节对齐）
        [BitField] public InnerWholeBufferFrame Inner { get; set; } = new();
    }

    /// <summary>
    /// Review round-5 P1：fixed-count list of manual IBitSerializable WITH explicit element bit
    /// length is statically sized — BITS035 must NOT reject it as "dynamic after CRC".
    /// </summary>
    public class FixedWidthManualItem : IBitSerializable
    {
        public byte A { get; set; }
        public int SerializeMSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperMSB.SetValueLength<byte>(bytes, bitOffset, 8, A);
            return 8;
        }
        public int SerializeLSB(Span<byte> bytes, int bitOffset)
        {
            BitHelperLSB.SetValueLength<byte>(bytes, bitOffset, 8, A);
            return 8;
        }
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            A = BitHelperMSB.ValueLength<byte>(bytes, bitOffset, 8);
            return 8;
        }
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset)
        {
            A = BitHelperLSB.ValueLength<byte>(bytes, bitOffset, 8);
            return 8;
        }
        public int GetTotalBitLength() => 8;
    }

    [BitSerialize]
    public partial class CrcBeforeFixedWidthManualList
    {
        [BitField(8)] public byte Header { get; set; }
        // SkipTail = 2 (CRC) + 3 (Items: 3 × 1 byte) = 5
        [BitField(16), BitCrc(typeof(CrcCcitt), WholeBuffer = true, SkipTailBytes = 5)]
        public ushort Crc { get; set; }
        [BitField(8), BitFieldCount(3)] // 显式 8-bit 宽度 → 静态大小可知
        public List<FixedWidthManualItem> Items { get; set; } = new();
    }

    #endregion

    #region Basic WholeBuffer

    [Fact]
    public void BasicWholeBuffer_AutoComputesCrcAcrossEntireBufferMinusCrcField()
    {
        var data = new BasicWholeBufferPacket { Header = 0xAB, Payload = 0x1234 };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        // CRC covers bytes[0..3] (Header + Payload, 3 bytes), CRC itself at [3..5] is skipped.
        var expected = new CrcCcitt();
        expected.Reset(0);
        expected.Update(new byte[] { 0xAB, 0x12, 0x34 });
        ushort want = (ushort)expected.Result;

        ushort written = (ushort)((bytes[3] << 8) | bytes[4]);
        written.ShouldBe(want);
        data.Crc.ShouldBe(want); // backfill
    }

    [Fact]
    public void BasicWholeBuffer_RoundTrips()
    {
        var original = new BasicWholeBufferPacket { Header = 0x7E, Payload = 0xBEEF };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<BasicWholeBufferPacket>(bytes);
        result.Header.ShouldBe(original.Header);
        result.Payload.ShouldBe(original.Payload);
        result.Crc.ShouldBe(original.Crc);
    }

    #endregion

    #region DMI-style SkipHead + SkipTail

    [Fact]
    public void DmiLikeFrame_CrcCoversTypeAndLengthOnly()
    {
        var data = new DmiLikeFrame
        {
            Sync = 0x7E,
            Type = 0x01,
            Length = 0x0010,
            EndOfFrame = 0xFEFE,
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        // Layout (8 bytes): [Sync=7E][Type=01][Length=00 10][Crc=?? ??][EOF=FE FE]
        bytes.Length.ShouldBe(8);
        bytes[0].ShouldBe((byte)0x7E);
        bytes[1].ShouldBe((byte)0x01);
        bytes[2].ShouldBe((byte)0x00);
        bytes[3].ShouldBe((byte)0x10);
        bytes[6].ShouldBe((byte)0xFE);
        bytes[7].ShouldBe((byte)0xFE);

        // CRC covers bytes[1..5] (Type + Length, 4 bytes).
        var expected = new CrcCcitt();
        expected.Reset(0);
        expected.Update(new byte[] { 0x01, 0x00, 0x10 });
        // wait: SkipHead=1, SkipTail=4, totalBytes=8 → range = [1..4) = 3 bytes.
        ushort want = (ushort)expected.Result;

        ushort written = (ushort)((bytes[4] << 8) | bytes[5]);
        written.ShouldBe(want);
    }

    [Fact]
    public void DmiLikeFrame_RoundTrips()
    {
        var original = new DmiLikeFrame
        {
            Sync = 0x7E,
            Type = 0xAB,
            Length = 0xCDEF,
            EndOfFrame = 0x1234,
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<DmiLikeFrame>(bytes);
        result.Sync.ShouldBe(original.Sync);
        result.Type.ShouldBe(original.Type);
        result.Length.ShouldBe(original.Length);
        result.Crc.ShouldBe(original.Crc);
        result.EndOfFrame.ShouldBe(original.EndOfFrame);
    }

    #endregion

    #region Dynamic List + WholeBuffer

    [Fact]
    public void DynamicListWholeBuffer_CoversCountAndAllListBytes()
    {
        var data = new DynamicListWholeBufferPacket
        {
            Data = new List<byte> { 0x11, 0x22, 0x33, 0x44, 0x55 },
        };
        byte[] bytes = BitSerializerMSB.Serialize(data);

        // After backfill: Count=5, Data=[11..55], Crc=?? ??
        // totalBytes = 1 + 5 + 2 = 8, SkipTail=2 → CRC covers [0..6) = Count + 5 data bytes.
        bytes.Length.ShouldBe(8);

        var expected = new CrcCcitt();
        expected.Reset(0);
        expected.Update(new byte[] { 0x05, 0x11, 0x22, 0x33, 0x44, 0x55 });
        ushort want = (ushort)expected.Result;
        ushort written = (ushort)((bytes[6] << 8) | bytes[7]);
        written.ShouldBe(want);
    }

    [Fact]
    public void DynamicListWholeBuffer_RoundTripsWithVaryingListLength()
    {
        foreach (var listLen in new[] { 0, 1, 3, 10 })
        {
            var original = new DynamicListWholeBufferPacket
            {
                Data = Enumerable.Range(0, listLen).Select(i => (byte)(0x80 + i)).ToList(),
            };
            byte[] bytes = BitSerializerMSB.Serialize(original);
            var result = BitSerializerMSB.Deserialize<DynamicListWholeBufferPacket>(bytes);
            result.Count.ShouldBe((byte)listLen);
            result.Data.Count.ShouldBe(listLen);
            result.Crc.ShouldBe(original.Crc);
            for (int i = 0; i < listLen; i++)
                result.Data[i].ShouldBe(original.Data[i]);
        }
    }

    #endregion

    #region Polymorphic + dynamic subtype + WholeBuffer

    [Fact]
    public void PolyWholeBuffer_StaticSubtype_RoundTrips()
    {
        var original = new PolyWholeBufferPacket
        {
            Payload = new PolyTypeA { X = 0xAA, Y = 0xBB },
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<PolyWholeBufferPacket>(bytes);
        result.Discriminator.ShouldBe((byte)1);
        var payload = result.Payload.ShouldBeOfType<PolyTypeA>();
        payload.X.ShouldBe((byte)0xAA);
        payload.Y.ShouldBe((byte)0xBB);
        result.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void PolyWholeBuffer_DynamicSubtype_RoundTrips()
    {
        // PolyTypeB contains a dynamic List<byte> — IncludeRange mode would reject this with BITS017.
        var original = new PolyWholeBufferPacket
        {
            Payload = new PolyTypeB
            {
                Items = new List<byte> { 0x11, 0x22, 0x33 },
            },
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<PolyWholeBufferPacket>(bytes);
        result.Discriminator.ShouldBe((byte)2);
        var payload = result.Payload.ShouldBeOfType<PolyTypeB>();
        payload.Marker.ShouldBe((byte)3);
        payload.Items.Count.ShouldBe(3);
        payload.Items[0].ShouldBe((byte)0x11);
        payload.Items[2].ShouldBe((byte)0x33);
        result.Crc.ShouldBe(original.Crc);
    }

    #endregion

    #region Validate on deserialize

    [Fact]
    public void ValidatingWholeBuffer_GoodCrcRoundTrips()
    {
        var original = new ValidatingWholeBufferPacket { A = 0x33, B = 0x44 };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<ValidatingWholeBufferPacket>(bytes);
        result.A.ShouldBe((byte)0x33);
        result.B.ShouldBe((byte)0x44);
    }

    [Fact]
    public void ValidatingWholeBuffer_CorruptedDataThrows()
    {
        var original = new ValidatingWholeBufferPacket { A = 0x33, B = 0x44 };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes[0] ^= 0xFF; // corrupt A
        Should.Throw<System.IO.InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<ValidatingWholeBufferPacket>(bytes));
    }

    #endregion

    #region Degenerate config

    [Fact]
    public void DegenerateSkipConfig_ThrowsAtRuntime()
    {
        // totalBytes = 1 + 2 = 3; SkipHead=10, SkipTail=2; effective end = 3-2 = 1, start = 0+10 = 10 → end < start.
        var data = new DegenerateSkipPacket { Header = 0xAB };
        Should.Throw<System.IO.InvalidDataException>(() => BitSerializerMSB.Serialize(data));
    }

    [Fact]
    public void ZeroLengthCrcRange_ThrowsInsteadOfReturningInitialValue()
    {
        // totalBytes = 2; SkipHead=0, SkipTail=2 → _crcStart=0, _crcEnd=0 → empty span.
        // Before P2 fix: CRC.Update(empty) returns InitialValue (silent misconfiguration).
        // After fix: <= guard throws InvalidDataException.
        var data = new ZeroLengthCrcRangePacket();
        var ex = Should.Throw<System.IO.InvalidDataException>(() => BitSerializerMSB.Serialize(data));
        ex.Message.ShouldContain("empty or negative");
    }

    [Fact]
    public void Crc_Before_FixedWidth_ManualList_Compiles_And_Round_Trips()
    {
        // BITS035 must classify "fixed-count manual IBitSerializable with explicit element width"
        // as static — list size 3×8 = 24 bits is known at compile time, no runtime drift.
        var original = new CrcBeforeFixedWidthManualList
        {
            Header = 0xAB,
            Items = new List<FixedWidthManualItem>
            {
                new() { A = 0x11 },
                new() { A = 0x22 },
                new() { A = 0x33 },
            },
        };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        // CRC covers bytes[0..1) = Header only (SkipTail=5 covers CRC[1..3) + Items[3..6))
        bytes.Length.ShouldBe(1 + 2 + 3);
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(new byte[] { 0xAB });
        ushort want = (ushort)algo.Result;
        ((bytes[1] << 8) | bytes[2]).ShouldBe(want);

        var result = BitSerializerMSB.Deserialize<CrcBeforeFixedWidthManualList>(bytes);
        result.Items.Count.ShouldBe(3);
        result.Items[2].A.ShouldBe((byte)0x33);
    }

    [Fact]
    public void CrcAtHead_With_Matching_SkipHead_RoundTrips()
    {
        // BITS034 covers tail-positioned CRCs naturally; this test exercises the head branch.
        var original = new CrcAtHeadPacket { Payload = 0xAB, More = 0x1234 };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        // CRC covers bytes[2..5) = Payload (1) + More (2).
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(new byte[] { 0xAB, 0x12, 0x34 });
        ushort want = (ushort)algo.Result;
        ((bytes[0] << 8) | bytes[1]).ShouldBe(want);

        var result = BitSerializerMSB.Deserialize<CrcAtHeadPacket>(bytes);
        result.Crc.ShouldBe(want);
        result.Payload.ShouldBe((byte)0xAB);
        result.More.ShouldBe((ushort)0x1234);
    }

    #endregion

    #region Byte alignment guards (review P1)

    [Fact]
    public void Nested_With_ByteAligned_BitOffset_Works()
    {
        // Outer: 1-bit Flag + 7-bit Padding = 8 bits → Inner starts at bit 8 (byte boundary).
        var data = new OuterWith1BitPrefix
        {
            Flag = 1,
            Padding = 0x42,
            Inner = new InnerWholeBufferFrame { Header = 0xAB, Payload = 0x1234 },
        };
        var bytes = BitSerializerMSB.Serialize(data);
        var result = BitSerializerMSB.Deserialize<OuterWith1BitPrefix>(bytes);
        result.Flag.ShouldBe((byte)1);
        result.Padding.ShouldBe((byte)0x42);
        result.Inner.Header.ShouldBe((byte)0xAB);
        result.Inner.Payload.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void Nested_With_NonByteAligned_BitOffset_ThrowsAtRuntime()
    {
        // Outer: 1-bit Flag → Inner starts at bit 1 (NOT byte-aligned).
        // Without the guard, WholeBuffer would silently compute the wrong CRC slice.
        var data = new OuterWith1BitMisalignment
        {
            Flag = 1,
            Inner = new InnerWholeBufferFrame { Header = 0xAB, Payload = 0x1234 },
        };
        var ex = Should.Throw<System.IO.InvalidDataException>(() => BitSerializerMSB.Serialize(data));
        ex.Message.ShouldContain("byte-aligned bitOffset");
    }

    #endregion
}
