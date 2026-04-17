using BitSerializer;
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

public class BitSerializeSampleTests
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
}