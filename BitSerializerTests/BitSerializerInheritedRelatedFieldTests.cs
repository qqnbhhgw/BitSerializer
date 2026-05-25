using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

/// <summary>
/// v0.12.1 Issue: BITS061 误伤跨继承链的 [BitFieldRelated] carrier 引用。
///
/// 原 Issue 文档：D:\CARS\stp\Stp.Peripheral.Atp.Protocol\BitSerializer-Issue-InheritedRelatedField.md
/// 0.11.0 编译通过（generator 忽略找不到的 carrier，silently skip backfill），0.12.0 BITS061
/// 把这种合法用法误杀。修复策略：
///
/// 1. TypeAnalyzer 沿 [BitSerialize] 祖先链收集 [BitField] 元数据进 TypeModel.InheritedFields
/// 2. 所有 carrier lookup 站点 fallback 到 InheritedFields → BITS061 / 顺序检查 / 常量冲突检查通过
/// 3. EmitAutoBackfill 对 inherited carrier 静默 skip ——
///    base.Serialize 早于派生类主循环写 wire, property-level backfill 来不及。
///    用户自行 set carrier value before Serialize (或由 BinarySerialization 双标注的 peer 处理)。
/// 4. Deserialize 端不受影响 —— base.Deserialize 已读 carrier 到 property, 派生类后续读 property 走
///    C# inheritance 拿到正确值。
///
/// 真正的 typo (nameof(Lenght)) 在 inherited 里也找不到, BITS061 仍然拦住.
/// </summary>
public partial class BitSerializerInheritedRelatedFieldTests
{
    // ============ minimal repro：Issue 文档的两层继承例子 ============

    [BitSerialize]
    public partial class InheritBase
    {
        [BitField(16)] public ushort Length { get; set; }
    }

    [BitSerialize]
    public partial class InheritDerived : InheritBase
    {
        [BitField]
        [BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public byte[] Payload { get; set; } = [];
    }

    [Fact]
    public void InheritedCarrier_Minimal_CompilesAndRoundTrips()
    {
        // 用户手动 set carrier (跨继承场景下 BitSerializer 不能自动 backfill — base.Serialize 早于
        // 派生类主循环写 wire). 这就是 0.11.0 用户的实际用法 (BinarySerialization 等 peer 负责 length).
        var src = new InheritDerived
        {
            Length = 4,                                                   // ← 手动设
            Payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD },
        };
        var bytes = BitSerializerMSB.Serialize(src);
        // Length(2) + Payload(4) = 6 bytes
        bytes.Length.ShouldBe(6);
        ((bytes[0] << 8) | bytes[1]).ShouldBe(4);
        bytes[2].ShouldBe((byte)0xAA);
        bytes[5].ShouldBe((byte)0xDD);

        var dst = BitSerializerMSB.Deserialize<InheritDerived>(bytes);
        dst.Length.ShouldBe((ushort)4);
        dst.Payload.ShouldBe(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });
    }

    // ============ 三层继承：模拟 Issue 真实场景 (AtpFrameContent → AtpInContent → AtpChannelContent → AtpBtmContent) ============

    [BitSerialize]
    public partial class AtpLikeRoot
    {
        [BitField(8)] public byte FrameType { get; set; }
    }

    [BitSerialize]
    public partial class AtpLikeIn : AtpLikeRoot
    {
        [BitField(64)] public ulong PlatformTimestamp { get; set; }
        [BitField(16)] public ushort PacketLength { get; set; }   // ← 长度字段在祖父类
    }

    [BitSerialize]
    public partial class AtpLikeChannel : AtpLikeIn
    {
        [BitField(64)] public ulong AtpTimeStamp { get; set; }
    }

    [BitSerialize]
    public sealed partial class AtpLikeBtm : AtpLikeChannel
    {
        [BitField]
        [BitFieldRelated(nameof(PacketLength), RelationKind = BitRelationKind.ByteLength)]
        public byte[] BtmBytes { get; set; } = [];
    }

    [Fact]
    public void InheritedCarrier_GrandparentField_RoundTrip()
    {
        var src = new AtpLikeBtm
        {
            FrameType = 0x42,
            PlatformTimestamp = 0x0123456789ABCDEFUL,
            PacketLength = 5,                                  // ← 用户手动 set (跨继承不能 auto-backfill)
            AtpTimeStamp = 0xFEDCBA9876543210UL,
            BtmBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x55 },
        };
        var bytes = BitSerializerMSB.Serialize(src);

        // Layout: FrameType(1) + PlatformTimestamp(8) + PacketLength(2) + AtpTimeStamp(8) + BtmBytes(5) = 24
        bytes.Length.ShouldBe(24);
        bytes[0].ShouldBe((byte)0x42);
        ((bytes[9] << 8) | bytes[10]).ShouldBe(5);
        bytes[19].ShouldBe((byte)0xDE);
        bytes[23].ShouldBe((byte)0x55);

        // Deserialize 端: base.Deserialize 读 PacketLength 到 property, 派生类 BtmBytes 用之做字节预算
        var dst = BitSerializerMSB.Deserialize<AtpLikeBtm>(bytes);
        dst.FrameType.ShouldBe((byte)0x42);
        dst.PlatformTimestamp.ShouldBe(0x0123456789ABCDEFUL);
        dst.PacketLength.ShouldBe((ushort)5);
        dst.AtpTimeStamp.ShouldBe(0xFEDCBA9876543210UL);
        dst.BtmBytes.ShouldBe(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x55 });
    }

    // ============ 反例：跨继承 carrier 不 auto-backfill 也不报错 (silent skip) ============

    [Fact]
    public void InheritedCarrier_NoBackfill_LengthStaysZero()
    {
        // 用户没 set PacketLength, BitSerializer 不会自动回填 — base.Serialize 早于派生 backfill
        // 触发点. 这是 0.11.0 一致的行为, 修复 Issue 后保留.
        var src = new AtpLikeBtm
        {
            BtmBytes = new byte[] { 0x11, 0x22, 0x33 },
        };
        var bytes = BitSerializerMSB.Serialize(src);
        // PacketLength 是默认 0, 不会被 backfill 到 3
        ((bytes[9] << 8) | bytes[10]).ShouldBe(0);
        src.PacketLength.ShouldBe((ushort)0);
        // wire 仍然写出 BtmBytes (RelationKind.ByteLength 不影响 serialize 写 length, 只影响 backfill)
        bytes[19].ShouldBe((byte)0x11);
        bytes[20].ShouldBe((byte)0x22);
        bytes[21].ShouldBe((byte)0x33);
    }
}
