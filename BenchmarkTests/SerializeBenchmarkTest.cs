using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BinarySerialization;
using BitSerializer;

namespace BenchmarkTests;

public enum DeviceStatus : byte
{
    Offline = 0,
    Online = 1,
    Error = 2,
    Maintenance = 3
}

[BitSerialize]
public partial class SensorReading
{
    [FieldOrder]
    [FieldBitLength(8)]
    [BitField(8)]
    public byte SensorId { get; set; }

    [FieldOrder]
    [FieldBitLength(16)]
    [BitField(16)]
    public ushort Value { get; set; }
}

[BitSerialize]
public partial class SerializeBenchmarkData
{
    [FieldOrder]
    [FieldBitLength(4)]
    [BitField(4)]
    public byte ByteValue1 { get; set; }

    [FieldOrder]
    [FieldBitLength(4)]
    [BitField(4)]
    public byte ByteValue2 { get; set; }

    [FieldOrder]
    [FieldBitLength(2)]
    [BitField(2)]
    public DeviceStatus Status { get; set; }

    [FieldOrder]
    [FieldBitLength(6)]
    [BitField(6)]
    public byte Reserved { get; set; }

    [FieldOrder]
    [FieldBitLength(8)]
    [BitField(8)]
    public ushort UShortValue1 { get; set; }

    [FieldOrder]
    [FieldBitLength(8)]
    [BitField(8)]
    public ushort UShortValue2 { get; set; }

    [FieldOrder]
    [FieldBitLength(16)]
    [BitField(16)]
    public int IntValue1 { get; set; }

    [FieldOrder]
    [FieldBitLength(16)]
    [BitField(16)]
    public int IntValue2 { get; set; }

    [FieldOrder]
    [BitField]
    public SensorReading Sensor { get; set; } = new();

    [FieldOrder]
    [FieldCount(2)]
    [BitField]
    [BitFieldCount(2)]
    public List<SensorReading> SensorArray { get; set; } = new();

    [FieldOrder]
    [FieldBitLength(32)]
    [BitField(32)]
    public long LongValue1 { get; set; }

    [FieldOrder]
    [FieldBitLength(32)]
    [BitField(32)]
    public long LongValue2 { get; set; }
}

// Bit layout (MSB, total 200 bits = 25 bytes):
// [0:3]   ByteValue1    4 bits
// [4:7]   ByteValue2    4 bits
// [8:9]   Status        2 bits
// [10:15] Reserved      6 bits
// [16:23] UShortValue1  8 bits
// [24:31] UShortValue2  8 bits
// [32:47] IntValue1    16 bits
// [48:63] IntValue2    16 bits
// [64:71] Sensor.Id     8 bits
// [72:87] Sensor.Value 16 bits
// [88:95] Array[0].Id   8 bits
// [96:111] Array[0].Val 16 bits
// [112:119] Array[1].Id  8 bits
// [120:135] Array[1].Val 16 bits
// [136:167] LongValue1  32 bits
// [168:199] LongValue2  32 bits
public static class BenchmarkDataSerializer
{
    public static void Serialize(SerializeBenchmarkData data, Span<byte> buffer)
    {
        buffer[0] = (byte)(data.ByteValue1 << 4 | data.ByteValue2);
        buffer[1] = (byte)((byte)data.Status << 6 | data.Reserved);
        buffer[2] = (byte)data.UShortValue1;
        buffer[3] = (byte)data.UShortValue2;

        BinaryPrimitives.WriteUInt16BigEndian(buffer[4..], (ushort)data.IntValue1);
        BinaryPrimitives.WriteUInt16BigEndian(buffer[6..], (ushort)data.IntValue2);

        buffer[8] = data.Sensor.SensorId;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[9..], data.Sensor.Value);

        buffer[11] = data.SensorArray[0].SensorId;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[12..], data.SensorArray[0].Value);
        buffer[14] = data.SensorArray[1].SensorId;
        BinaryPrimitives.WriteUInt16BigEndian(buffer[15..], data.SensorArray[1].Value);

        BinaryPrimitives.WriteUInt32BigEndian(buffer[17..], (uint)data.LongValue1);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[21..], (uint)data.LongValue2);
    }

    public static SerializeBenchmarkData Deserialize(ReadOnlySpan<byte> buffer)
    {
        var data = new SerializeBenchmarkData
        {
            ByteValue1 = (byte)(buffer[0] >> 4),
            ByteValue2 = (byte)(buffer[0] & 0x0F),
            Status = (DeviceStatus)(buffer[1] >> 6),
            Reserved = (byte)(buffer[1] & 0x3F),
            UShortValue1 = buffer[2],
            UShortValue2 = buffer[3],
            IntValue1 = BinaryPrimitives.ReadUInt16BigEndian(buffer[4..]),
            IntValue2 = BinaryPrimitives.ReadUInt16BigEndian(buffer[6..]),
            Sensor = new SensorReading
            {
                SensorId = buffer[8],
                Value = BinaryPrimitives.ReadUInt16BigEndian(buffer[9..]),
            },
            SensorArray = new List<SensorReading>
            {
                new()
                {
                    SensorId = buffer[11],
                    Value = BinaryPrimitives.ReadUInt16BigEndian(buffer[12..]),
                },
                new()
                {
                    SensorId = buffer[14],
                    Value = BinaryPrimitives.ReadUInt16BigEndian(buffer[15..]),
                },
            },
            LongValue1 = BinaryPrimitives.ReadUInt32BigEndian(buffer[17..]),
            LongValue2 = BinaryPrimitives.ReadUInt32BigEndian(buffer[21..]),
        };
        return data;
    }
}

[SimpleJob(RuntimeMoniker.Net80, warmupCount: 4, iterationCount: 10)]
[MemoryDiagnoser]
[RankColumn]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SerializeBenchmark
{
    private const int TotalBits = 200;
    private const int TotalBytes = TotalBits / 8;

    private readonly BinarySerializer _binarySerializer = new()
    {
        Endianness = Endianness.Big
    };

    private readonly SerializeBenchmarkData _data = new()
    {
        ByteValue1 = 0x0C,
        ByteValue2 = 0x0F,
        Status = DeviceStatus.Error,
        Reserved = 0x1A,
        UShortValue1 = 0x34,
        UShortValue2 = 0x56,
        IntValue1 = 0x1234,
        IntValue2 = 0x5678,
        Sensor = new SensorReading { SensorId = 0x01, Value = 0xABCD },
        SensorArray = new List<SensorReading>
        {
            new() { SensorId = 0x02, Value = 0x1111 },
            new() { SensorId = 0x03, Value = 0x2222 },
        },
        LongValue1 = 0x12345678,
        LongValue2 = 0x9ABCDEF0,
    };

    private readonly byte[] _buffer = new byte[TotalBytes];

    [GlobalSetup]
    public void Setup()
    {
        BitSerializerMSB.Serialize(_data, _buffer);
    }

    // ── Serialize ──

    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public void BitSerializer_Ser()
    {
        BitSerializerMSB.Serialize(_data, _buffer);
    }

    [Benchmark]
    [BenchmarkCategory("Serialize")]
    public void BinarySerializer_Ser()
    {
        using var ms = new MemoryStream(_buffer);
        _binarySerializer.Serialize(ms, _data);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Serialize")]
    public void Manual_Ser()
    {
        BenchmarkDataSerializer.Serialize(_data, _buffer);
    }

    // ── Deserialize ──

    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public SerializeBenchmarkData BitSerializer_De()
    {
        return BitSerializerMSB.Deserialize<SerializeBenchmarkData>(_buffer);
    }

    [Benchmark]
    [BenchmarkCategory("Deserialize")]
    public SerializeBenchmarkData BinarySerializer_De()
    {
        using var ms = new MemoryStream(_buffer);
        return _binarySerializer.Deserialize<SerializeBenchmarkData>(ms);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Deserialize")]
    public SerializeBenchmarkData Manual_De()
    {
        return BenchmarkDataSerializer.Deserialize(_buffer);
    }
}
