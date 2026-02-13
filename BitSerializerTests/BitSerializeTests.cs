using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

[BitSerialize]
public partial class TestData
{
    [BitField] [BitFieldCount(12)] public List<byte> NoMean { get; set; }

    [BitField] public ulong SysRunId { get; set; }
}

public class BitSerializeTests
{
    [Fact]
    public void Serialize_TestData_ShouldRoundTrip()
    {
        var original = new TestData
        {
            NoMean = [11, 22, 33, 44, 55, 66, 77, 88, 99, 110, 121, 132],
            SysRunId = 0x123456789ABCDEF0
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var result = BitSerializerMSB.Deserialize<TestData>(bytes);

        result.NoMean.ShouldBe(original.NoMean);
        result.SysRunId.ShouldBe(original.SysRunId);
    }
}
