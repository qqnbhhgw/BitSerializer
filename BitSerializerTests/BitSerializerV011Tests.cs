using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

/// <summary>
/// v0.11.0 四大新特性的单元测试：
/// - T1: [BitFieldValue(const, Verify=true)] 钉死常量 + 解码校验
/// - T2: [BitFixedString(Padding = 0xNN)] 自定义填充字节
/// - T3: [BitLengthFieldString(nameof(...))] 字符串长度从另一字段读
/// - T4: [BitFieldRelated(RelationKind=ByteLength)] 扩展到嵌套类型
/// </summary>
public partial class BitSerializerV011Tests
{
    #region T1: BitFieldValue

    [BitSerialize]
    public partial class FrameWithMagic
    {
        [BitField(8), BitFieldValue(0x7E)] public byte Start { get; set; }
        [BitField(16)] public ushort Payload { get; set; }
        [BitField(8), BitFieldValue(0xCF)] public byte End { get; set; }
    }

    [Fact]
    public void T1_BitFieldValue_SerializeForcesConstant()
    {
        // 即使用户给 Start/End 设了别的值，序列化也写入常量。
        var src = new FrameWithMagic { Start = 0x00, Payload = 0x1234, End = 0x00 };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)0x7E);
        bytes[3].ShouldBe((byte)0xCF);
        // 同时回写到属性
        src.Start.ShouldBe((byte)0x7E);
        src.End.ShouldBe((byte)0xCF);
    }

    [Fact]
    public void T1_BitFieldValue_DeserializeVerifies()
    {
        var bytes = new byte[] { 0x7E, 0x12, 0x34, 0xCF };
        var dst = BitSerializerMSB.Deserialize<FrameWithMagic>(bytes);
        dst.Payload.ShouldBe((ushort)0x1234);
        dst.Start.ShouldBe((byte)0x7E);
        dst.End.ShouldBe((byte)0xCF);
    }

    [Fact]
    public void T1_BitFieldValue_VerifyThrowsOnMismatch()
    {
        var bad = new byte[] { 0xFF, 0x12, 0x34, 0xCF };
        Should.Throw<System.IO.InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<FrameWithMagic>(bad));
    }

    [BitSerialize]
    public partial class FrameWithoutVerify
    {
        [BitField(8), BitFieldValue(0xAA, Verify = false)] public byte Tag { get; set; }
        [BitField(8)] public byte Data { get; set; }
    }

    [Fact]
    public void T1_BitFieldValue_VerifyOptOut()
    {
        // Verify=false: 收到任何字节都接受，属性反映 wire 值
        var bytes = new byte[] { 0x55, 0x42 };
        var dst = BitSerializerMSB.Deserialize<FrameWithoutVerify>(bytes);
        dst.Tag.ShouldBe((byte)0x55);
        dst.Data.ShouldBe((byte)0x42);
    }

    public enum FrameKind : byte { Heartbeat = 0x01, Data = 0x02 }

    [BitSerialize]
    public partial class FrameWithEnumConstant
    {
        [BitField(8), BitFieldValue(FrameKind.Data)] public FrameKind Kind { get; set; }
        [BitField(8)] public byte Payload { get; set; }
    }

    [Fact]
    public void T1_BitFieldValue_EnumLiteralAccepted()
    {
        var src = new FrameWithEnumConstant { Kind = FrameKind.Heartbeat, Payload = 0x11 };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)0x02); // 强制为 Data
        src.Kind.ShouldBe(FrameKind.Data); // 回写

        var dst = BitSerializerMSB.Deserialize<FrameWithEnumConstant>(bytes);
        dst.Kind.ShouldBe(FrameKind.Data);
        dst.Payload.ShouldBe((byte)0x11);
    }

    #endregion

    #region T2: BitFixedString.Padding

    [BitSerialize]
    public partial class TagFrame
    {
        // ASCII 空格填充（SCADA / Modbus 风格）
        [BitFixedString(8, Encoding = BitStringEncoding.ASCII, Padding = 0x20)]
        public string Tag { get; set; } = "";
    }

    [Fact]
    public void T2_BitFixedString_SpacePadding()
    {
        var src = new TagFrame { Tag = "AI01" };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes.Length.ShouldBe(8);
        bytes[0].ShouldBe((byte)'A');
        bytes[1].ShouldBe((byte)'I');
        bytes[2].ShouldBe((byte)'0');
        bytes[3].ShouldBe((byte)'1');
        bytes[4].ShouldBe((byte)0x20);
        bytes[5].ShouldBe((byte)0x20);
        bytes[6].ShouldBe((byte)0x20);
        bytes[7].ShouldBe((byte)0x20);

        var dst = BitSerializerMSB.Deserialize<TagFrame>(bytes);
        dst.Tag.ShouldBe("AI01"); // 反序列化 trim 掉尾部 0x20
    }

    [BitSerialize]
    public partial class NulPaddedFrame
    {
        // 默认 NUL 填充 — 验证向后兼容
        [BitFixedString(4)] public string Code { get; set; } = "";
    }

    [Fact]
    public void T2_BitFixedString_DefaultPaddingStillNul()
    {
        var src = new NulPaddedFrame { Code = "OK" };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)'O');
        bytes[1].ShouldBe((byte)'K');
        bytes[2].ShouldBe((byte)0x00);
        bytes[3].ShouldBe((byte)0x00);

        var dst = BitSerializerMSB.Deserialize<NulPaddedFrame>(bytes);
        dst.Code.ShouldBe("OK");
    }

    #endregion

    #region T3: BitLengthFieldString

    [BitSerialize]
    public partial class StationFrame
    {
        [BitField(8)] public byte Header { get; set; }
        [BitField(8)] public byte NameLength { get; set; }
        [BitLengthFieldString(nameof(NameLength), Encoding = BitStringEncoding.UTF8)]
        public string Name { get; set; } = "";
        [BitField(8)] public byte Footer { get; set; }
    }

    [Fact]
    public void T3_LengthFieldString_BackfillsAndRoundtrips()
    {
        var src = new StationFrame { Header = 0xAB, Name = "Tokyo", Footer = 0xCD };
        var bytes = BitSerializerMSB.Serialize(src);
        // Header(1) + NameLength(1) + "Tokyo"(5) + Footer(1) = 8 bytes
        bytes.Length.ShouldBe(8);
        bytes[0].ShouldBe((byte)0xAB);
        bytes[1].ShouldBe((byte)5);              // NameLength 自动回填
        bytes[2].ShouldBe((byte)'T');
        bytes[6].ShouldBe((byte)'o');
        bytes[7].ShouldBe((byte)0xCD);
        src.NameLength.ShouldBe((byte)5);        // 序列化时 in-memory 对象也更新

        var dst = BitSerializerMSB.Deserialize<StationFrame>(bytes);
        dst.Header.ShouldBe((byte)0xAB);
        dst.NameLength.ShouldBe((byte)5);
        dst.Name.ShouldBe("Tokyo");
        dst.Footer.ShouldBe((byte)0xCD);
    }

    [Fact]
    public void T3_LengthFieldString_UnicodeRoundtrip()
    {
        // "北京" UTF-8 = 6 bytes
        var src = new StationFrame { Header = 0x01, Name = "北京", Footer = 0x02 };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[1].ShouldBe((byte)6);
        var dst = BitSerializerMSB.Deserialize<StationFrame>(bytes);
        dst.Name.ShouldBe("北京");
    }

    [BitSerialize]
    public partial class BoundedStationFrame
    {
        [BitField(16)] public ushort NameLength { get; set; }
        [BitLengthFieldString(nameof(NameLength), Encoding = BitStringEncoding.UTF8, MaxBytes = 4)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void T3_LengthFieldString_MaxBytesTruncatesAtUtf8Boundary()
    {
        // 4 bytes 容不下 "北京"（6 bytes），应该回退到 "北"（3 bytes）而不是切到半个字符
        var src = new BoundedStationFrame { Name = "北京" };
        var bytes = BitSerializerMSB.Serialize(src);
        var lengthRead = (ushort)((bytes[0] << 8) | bytes[1]);
        lengthRead.ShouldBe((ushort)3);
        src.NameLength.ShouldBe((ushort)3);
    }

    #endregion

    #region T4: ByteLength on nested type

    [BitSerialize]
    public partial class Payload
    {
        [BitField(8)] public byte A { get; set; }
        [BitField(8)] public byte B { get; set; }
        [BitField(16)] public ushort C { get; set; }
    }

    [BitSerialize]
    public partial class FrameWithNestedByteLength
    {
        [BitField(8)] public byte Sync { get; set; }
        [BitField(16)] public ushort Length { get; set; }                 // Content 的字节数
        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public Payload Content { get; set; } = new();
        [BitField(8)] public byte End { get; set; }
    }

    [Fact]
    public void T4_NestedByteLength_BackfillsAndRoundtrips()
    {
        var src = new FrameWithNestedByteLength
        {
            Sync = 0x7E,
            Content = new Payload { A = 0x11, B = 0x22, C = 0x3344 },
            End = 0xCF,
        };
        var bytes = BitSerializerMSB.Serialize(src);
        // Sync(1) + Length(2) + Content(4) + End(1) = 8 bytes
        bytes.Length.ShouldBe(8);
        bytes[0].ShouldBe((byte)0x7E);
        // Length 自动回填为 Content 字节数 = 4
        ((bytes[1] << 8) | bytes[2]).ShouldBe(4);
        src.Length.ShouldBe((ushort)4);
        bytes[3].ShouldBe((byte)0x11);
        bytes[4].ShouldBe((byte)0x22);
        bytes[7].ShouldBe((byte)0xCF);

        var dst = BitSerializerMSB.Deserialize<FrameWithNestedByteLength>(bytes);
        dst.Sync.ShouldBe((byte)0x7E);
        dst.Length.ShouldBe((ushort)4);
        dst.Content.A.ShouldBe((byte)0x11);
        dst.Content.B.ShouldBe((byte)0x22);
        dst.Content.C.ShouldBe((ushort)0x3344);
        dst.End.ShouldBe((byte)0xCF);
    }

    [Fact]
    public void T4_NestedByteLength_DeserializeRejectsBadLength()
    {
        // 构造一个 Length 字段说 8 字节（>= Content 实际 4），反序列化时严格检查会抛
        var bytes = new byte[] { 0x7E, 0x00, 0x08, 0x11, 0x22, 0x33, 0x44, 0xCF };
        Should.Throw<System.IO.InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<FrameWithNestedByteLength>(bytes));
    }

    /// <summary>动态嵌套（含动态 list 元素）：T4 必须用 GetTotalBitLength 而不是 BitLength。</summary>
    [BitSerialize]
    public partial class DynamicPayload
    {
        [BitField(8)] public byte Count { get; set; }
        [BitField(8), BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class FrameWithDynamicNested
    {
        [BitField(16)] public ushort Length { get; set; }
        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public DynamicPayload Body { get; set; } = new();
    }

    [Fact]
    public void T4_NestedByteLength_HandlesDynamicNested()
    {
        var src = new FrameWithDynamicNested
        {
            Body = new DynamicPayload { Items = new List<byte> { 0xAA, 0xBB, 0xCC } },
        };
        var bytes = BitSerializerMSB.Serialize(src);
        // Length(2) + Count(1) + Items(3) = 6 bytes; Body 总长 = 4 bytes (Count + 3 items)
        ((bytes[0] << 8) | bytes[1]).ShouldBe(4);
        src.Length.ShouldBe((ushort)4);

        var dst = BitSerializerMSB.Deserialize<FrameWithDynamicNested>(bytes);
        dst.Length.ShouldBe((ushort)4);
        dst.Body.Count.ShouldBe((byte)3);
        dst.Body.Items.ShouldBe(new byte[] { 0xAA, 0xBB, 0xCC });
    }

    #endregion

    #region Issue: derived getter + empty setter wire-value cache

    /// <summary>
    /// 下游 Issue 复现：BinarySerialization 惯例 "派生 getter + 空 setter" 上的 Length 字段。
    /// 之前反序列化时 generator 写 `this.Length = wire` 是 no-op，然后读 `this.Length` 又回 getter
    /// （返回 Bytes.Length = 0），导致 Bytes 总是空。修复后 wire 值缓存到 `_wire_<name>` 本地变量，
    /// 不依赖属性 setter 副作用。
    /// </summary>
    [BitSerialize]
    public partial class DerivedLengthRepro
    {
        [BitField(8)]
        public byte Length
        {
            get => (byte)(Bytes?.Length ?? 0);  // 派生 — 反映 Bytes 当前状态
            set { }                             // no-op — 不持久化 wire 值
        }

        [BitField]
        [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public byte[] Bytes { get; set; } = System.Array.Empty<byte>();
    }

    [Fact]
    public void Issue_DerivedGetterEmptySetter_DeserializeStillFillsBytes()
    {
        // Wire: Length=3, Bytes=[0xAA, 0xBB, 0xCC]
        var bytes = new byte[] { 0x03, 0xAA, 0xBB, 0xCC };
        var dst = BitSerializerMSB.Deserialize<DerivedLengthRepro>(bytes);
        // 修复前：Bytes 是空数组（setter 是 no-op，read-back 时 getter 返回 0）
        // 修复后：Bytes 正确填充 3 字节
        dst.Bytes.Length.ShouldBe(3);
        dst.Bytes[0].ShouldBe((byte)0xAA);
        dst.Bytes[1].ShouldBe((byte)0xBB);
        dst.Bytes[2].ShouldBe((byte)0xCC);
        // Length getter 现在也返回 3（因为 Bytes 已经填充）
        dst.Length.ShouldBe((byte)3);
    }

    [Fact]
    public void Issue_DerivedGetterEmptySetter_SerializeStillCorrect()
    {
        // 序列化方向之前就是对的（getter 返回真实长度），这里只是回归保护
        var src = new DerivedLengthRepro { Bytes = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 } };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)5);
        bytes.Length.ShouldBe(6);
    }

    /// <summary>
    /// 同一缺陷的 LengthFieldString 形态：长度字段使用派生 getter + 空 setter。
    /// </summary>
    [BitSerialize]
    public partial class DerivedLengthStringRepro
    {
        [BitField(8)]
        public byte NameLength
        {
            get => (byte)System.Text.Encoding.UTF8.GetByteCount(Name ?? "");
            set { }
        }

        [BitLengthFieldString(nameof(NameLength), Encoding = BitStringEncoding.UTF8)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Issue_DerivedGetterEmptySetter_LengthFieldString_Roundtrip()
    {
        var src = new DerivedLengthStringRepro { Name = "Hello" };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)5);
        var dst = BitSerializerMSB.Deserialize<DerivedLengthStringRepro>(bytes);
        dst.Name.ShouldBe("Hello");
    }

    /// <summary>
    /// 同一缺陷的多态判别字段形态：discriminator 使用派生 getter + 空 setter。
    /// </summary>
    public abstract class PolyDerivedBase { }

    [BitSerialize]
    public partial class PolyDerivedA : PolyDerivedBase
    {
        [BitField(8)] public byte ValueA { get; set; }
    }

    [BitSerialize]
    public partial class PolyDerivedB : PolyDerivedBase
    {
        [BitField(16)] public ushort ValueB { get; set; }
    }

    [BitSerialize]
    public partial class DerivedDiscriminatorRepro
    {
        [BitField(8)]
        public byte Kind
        {
            get => Payload is PolyDerivedB ? (byte)2 : (byte)1;
            set { }
        }

        [BitField(16)]
        [BitFieldRelated(nameof(Kind))]
        [BitPoly(1, typeof(PolyDerivedA))]
        [BitPoly(2, typeof(PolyDerivedB))]
        public PolyDerivedBase Payload { get; set; } = new PolyDerivedA();
    }

    [Fact]
    public void Issue_DerivedGetterEmptySetter_PolymorphicDiscriminator()
    {
        // Wire: Kind=2 → PolyDerivedB, ValueB=0x1234
        var bytes = new byte[] { 0x02, 0x12, 0x34 };
        var dst = BitSerializerMSB.Deserialize<DerivedDiscriminatorRepro>(bytes);
        // 修复前：switch 读 `this.Kind` 派生自 Payload，Payload 初始是 PolyDerivedA 所以总是走 case 1
        // 修复后：switch 读缓存的 _wire_Kind = 2，正确选 PolyDerivedB
        dst.Payload.ShouldBeOfType<PolyDerivedB>();
        ((PolyDerivedB)dst.Payload).ValueB.ShouldBe((ushort)0x1234);
    }

    #endregion

    #region Codex review fixes

    /// <summary>
    /// Codex P2: 接受 0x8000_0000_0000_0000UL 这样的 64 位常量（之前 `ul > long.MaxValue` 被 BITS045 拒）
    /// </summary>
    [BitSerialize]
    public partial class FullUlongMagic
    {
        [BitField(64), BitFieldValue(unchecked((long)0x8000000000000000UL))]
        public ulong Magic { get; set; }

        [BitField(8)] public byte Tail { get; set; }
    }

    [Fact]
    public void FieldValue_AcceptsFullUlongRange()
    {
        var src = new FullUlongMagic { Tail = 0xAB };
        var bytes = BitSerializerMSB.Serialize(src);
        // 0x8000_0000_0000_0000 在 MSB 下高位字节是 0x80
        bytes[0].ShouldBe((byte)0x80);
        for (int i = 1; i < 8; i++) bytes[i].ShouldBe((byte)0);
        bytes[8].ShouldBe((byte)0xAB);

        var dst = BitSerializerMSB.Deserialize<FullUlongMagic>(bytes);
        dst.Magic.ShouldBe(0x8000000000000000UL);
        dst.Tail.ShouldBe((byte)0xAB);
    }

    /// <summary>
    /// Codex P2: [BitFieldValue] + [BitCrcInclude] 允许共存（典型 DMI/Modbus 帧头被 CRC 覆盖的模式）
    /// </summary>
    [BitSerialize]
    public partial class FixedHeaderCovedByCrc
    {
        [BitField(8), BitFieldValue(0x7E)]
        [BitCrcInclude(nameof(Crc))]
        public byte FrameStart { get; set; }

        [BitField(8)]
        [BitCrcInclude(nameof(Crc))]
        public byte Payload { get; set; }

        [BitField(16)]
        [BitCrc(typeof(global::BitSerializer.CrcAlgorithms.Crc16Arc))]
        public ushort Crc { get; set; }
    }

    [Fact]
    public void FieldValue_CombinesWithCrcInclude_NoBITS044()
    {
        // 关键：编译过即代表 BITS044 没误报。运行一次确保 wire 输出对。
        var src = new FixedHeaderCovedByCrc { Payload = 0x42 };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)0x7E);
        bytes[1].ShouldBe((byte)0x42);
        var dst = BitSerializerMSB.Deserialize<FixedHeaderCovedByCrc>(bytes);
        dst.FrameStart.ShouldBe((byte)0x7E);
        dst.Payload.ShouldBe((byte)0x42);
    }

    /// <summary>
    /// Codex P1: 派生 getter + 空 setter 的 LIST 形态，dependent 在 related 之前声明应被 BITS053 拒绝。
    /// 这里只能用 generator 拒绝场景，无法在运行时直接验证 ── 用 reverse 顺序检测能否拒绝。
    /// 注意：BITS053 是 Error，故不能放编译可过的类型；用 Roslyn 测试代替更可靠，
    /// 这里改测 *正确顺序* 在新算法下仍工作。
    /// </summary>
    [BitSerialize]
    public partial class CorrectOrderingStillWorks
    {
        [BitField(8)] public byte Count { get; set; }

        [BitField(8), BitFieldRelated(nameof(Count))]
        public byte[] Items { get; set; } = System.Array.Empty<byte>();
    }

    [Fact]
    public void RelatedField_DeclaredBeforeDependent_StillWorksUnderNewCacheAlgorithm()
    {
        // 验证 emittedWireLocals 增量算法在正向顺序下行为不变
        var src = new CorrectOrderingStillWorks { Items = new byte[] { 1, 2, 3 } };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)3);
        var dst = BitSerializerMSB.Deserialize<CorrectOrderingStillWorks>(bytes);
        dst.Count.ShouldBe((byte)3);
        dst.Items.Length.ShouldBe(3);
        dst.Items[2].ShouldBe((byte)3);
    }

    /// <summary>
    /// Codex P2: [BitLengthFieldString] 用 sbyte 作长度字段，wire 上 0xC8 表示 200 字节。
    /// 修复前：sbyte 读回 -56，触发 `_lfsLenRaw < 0` 抛 InvalidDataException 拒绝合法 wire。
    /// 修复后：通过 (byte)(sbyte) 转 200 → long 200，正确读到 200 字节。
    /// </summary>
    [BitSerialize]
    public partial class SignedLengthCarrier
    {
        [BitField(8)] public sbyte NameLength { get; set; }

        [BitLengthFieldString(nameof(NameLength), Encoding = BitStringEncoding.UTF8)]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void LengthFieldString_SignedCarrier_ReinterpretsAsUnsigned()
    {
        // 构造一个 200 字节字符串
        var longStr = new string('A', 200);
        var src = new SignedLengthCarrier { Name = longStr };
        var bytes = BitSerializerMSB.Serialize(src);
        bytes[0].ShouldBe((byte)0xC8); // 200
        bytes.Length.ShouldBe(1 + 200);

        // 反序列化：修复前会抛 InvalidDataException(< 0)，修复后正常
        var dst = BitSerializerMSB.Deserialize<SignedLengthCarrier>(bytes);
        dst.Name.Length.ShouldBe(200);
        dst.Name[0].ShouldBe('A');
        dst.Name[199].ShouldBe('A');
    }

    #endregion
}
