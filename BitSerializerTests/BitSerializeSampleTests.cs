using BitSerializer;
using BitSerializer.CrcAlgorithms;
using Shouldly;

namespace BitSerializerTests;

public abstract class AbstractField
{
}

[BitSerialize]
public partial class Filed1 : AbstractField
{
    [BitField] public byte Value { get; set; }
}

[BitSerialize]
public partial class Filed2 : AbstractField
{
    [BitField] public ushort Value { get; set; }
}

[BitSerialize]
public partial class Filed3 : AbstractField
{
    [BitField] public uint Value { get; set; }
}

[BitSerialize]
public partial class MyClassBase
{
    /// <summary>
    /// 帧头标志位
    /// 固定值：0x7E
    /// </summary>
    [BitField]
    public byte FrameStart { get; set; } = 0x7E;

    [BitField] public byte FiledType { get; set; }

    [BitField]
    [BitFieldRelated(nameof(FiledType))]
    [BitPoly(1, typeof(Filed1))]
    [BitPoly(2, typeof(Filed2))]
    [BitPoly(3, typeof(Filed3))]
    public AbstractField Field { get; set; } = new Filed1();
}

[BitSerialize]
public partial class MyClass : MyClassBase
{
    /// <summary>
    /// 直到 ValidUntilPeriod 所示的周期前此帧内容均有效
    /// </summary>
    [BitIgnore]
    public long RcvPeriodCount { get; set; } = 0;
}

public partial class BitSerializeSampleTests
{
    [Fact]
    public void TestBitSerializeGetSameBitLengthMyClass()
    {
        var obj = new MyClass();
        obj.GetTotalBitLength().ShouldBe((obj as MyClassBase).GetTotalBitLength());
    }

    [Fact]
    public void TestBitSerializeShouldUseRealPolyObjToGetLength()
    {
        var obj1 = new MyClass()
        {
            Field = new Filed1
            {
                Value = 0x12
            }
        };

        obj1.GetTotalBitLength().ShouldBe(8 + 8 + 8);

        var obj2 = new MyClass
        {
            Field = new Filed2
            {
                Value = 0x1234
            }
        };

        obj2.GetTotalBitLength().ShouldBe(8 + 8 + 16);

        var obj3 = new MyClass()
        {
            Field = new Filed3
            {
                Value = 0x12345678
            }
        };

        obj3.GetTotalBitLength().ShouldBe(8 + 8 + 32);
    }

    #region ComplexeCrc

    [BitSerialize]
    public partial class MyClassCrc
    {
        /// <summary>
        /// 帧头标志位
        /// 固定值：0x7E
        /// </summary>
        [BitField]
        [BitCrcInclude(nameof(Crc))]
        public byte FrameStart { get; set; } = 0x7E;

        [BitField]
        [BitCrcInclude(nameof(Crc))]
        public byte FiledType { get; set; }

        [BitField]
        [BitCrcInclude(nameof(Crc))]
        [BitFieldRelated(nameof(FiledType))]
        [BitPoly(1, typeof(Filed1))]
        [BitPoly(2, typeof(Filed2))]
        [BitPoly(3, typeof(Filed3))]
        public AbstractField Field { get; set; } = new Filed1();

        [BitField, BitCrc(typeof(CrcCcitt), InitialValue = 0, ValidateOnDeserialize = true)]
        public ushort Crc { get; set; }
    }

    private static ushort ExpectedCrcCcitt(params byte[] data)
    {
        var algo = new CrcCcitt();
        algo.Reset(0);
        algo.Update(data);
        return (ushort)algo.Result;
    }

    [Fact]
    public void TestComplexCrc_Filed1_RoundTrip()
    {
        var original = new MyClassCrc { Field = new Filed1 { Value = 0x12 } };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        // Wire order: FrameStart(0x7E) + FiledType(1) + Filed1.Value(0x12) + Crc(16)
        bytes.Length.ShouldBe(5);
        original.Crc.ShouldBe(ExpectedCrcCcitt(0x7E, 1, 0x12));

        var rt = BitSerializerMSB.Deserialize<MyClassCrc>(bytes);
        rt.FrameStart.ShouldBe((byte)0x7E);
        rt.FiledType.ShouldBe((byte)1);
        rt.Field.ShouldBeOfType<Filed1>();
        ((Filed1)rt.Field).Value.ShouldBe((byte)0x12);
        rt.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void TestComplexCrc_Filed2_RoundTrip()
    {
        var original = new MyClassCrc { Field = new Filed2 { Value = 0x1234 } };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        bytes.Length.ShouldBe(6);
        original.Crc.ShouldBe(ExpectedCrcCcitt(0x7E, 2, 0x12, 0x34));

        var rt = BitSerializerMSB.Deserialize<MyClassCrc>(bytes);
        rt.FiledType.ShouldBe((byte)2);
        rt.Field.ShouldBeOfType<Filed2>();
        ((Filed2)rt.Field).Value.ShouldBe((ushort)0x1234);
        rt.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void TestComplexCrc_Filed3_RoundTrip()
    {
        var original = new MyClassCrc { Field = new Filed3 { Value = 0x12345678 } };
        byte[] bytes = BitSerializerMSB.Serialize(original);

        bytes.Length.ShouldBe(8);
        original.Crc.ShouldBe(ExpectedCrcCcitt(0x7E, 3, 0x12, 0x34, 0x56, 0x78));

        var rt = BitSerializerMSB.Deserialize<MyClassCrc>(bytes);
        rt.FiledType.ShouldBe((byte)3);
        rt.Field.ShouldBeOfType<Filed3>();
        ((Filed3)rt.Field).Value.ShouldBe((uint)0x12345678);
        rt.Crc.ShouldBe(original.Crc);
    }

    [Fact]
    public void TestComplexCrc_CorruptedPayload_ThrowsOnDeserialize()
    {
        var original = new MyClassCrc { Field = new Filed2 { Value = 0x1234 } };
        byte[] bytes = BitSerializerMSB.Serialize(original);
        bytes[2] ^= 0xFF; // flip one byte inside the polymorphic payload

        Should.Throw<InvalidDataException>(() =>
            BitSerializerMSB.Deserialize<MyClassCrc>(bytes));
    }

    #endregion
}