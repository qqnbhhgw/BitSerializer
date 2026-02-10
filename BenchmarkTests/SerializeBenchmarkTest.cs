using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BinarySerialization;
using Microsoft.IO;
using BitSerializer;

namespace BitSerializer.BenchmarkTests;

public class SerializeBenchmarkData
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
    [FieldBitLength(32)]
    [BitField(32)]
    public long LongValue1 { get; set; }

    [FieldOrder]
    [FieldBitLength(32)]
    [BitField(32)]
    public long LongValue2 { get; set; }
}

public class BenchmarkDataSerializer
{
    public Func<SerializeBenchmarkData, byte[]> SerializeFunc => Serialize;

    public byte[] Serialize(SerializeBenchmarkData data)
    {
        Span<byte> buffer = stackalloc byte[16];

        buffer[0] = (byte)(data.ByteValue1 << 4 | data.ByteValue2);
        buffer[1] = (byte)(data.UShortValue1 >> 8);
        buffer[2] = (byte)(data.UShortValue2 >> 8);

        BinaryPrimitives.WriteUInt16BigEndian(buffer[3..], (ushort)(data.IntValue1 >> 16));
        BinaryPrimitives.WriteUInt16BigEndian(buffer[5..], (ushort)(data.IntValue2 >> 16));

        BinaryPrimitives.WriteUInt32BigEndian(buffer[7..], (uint)(data.LongValue1 >> 32));
        BinaryPrimitives.WriteUInt32BigEndian(buffer[11..], (uint)(data.LongValue2 >> 32));

        return buffer.ToArray();
    }
}

[SimpleJob(RuntimeMoniker.Net80, warmupCount: 4, iterationCount: 10)]
[MemoryDiagnoser]
[RankColumn]
public class SerializeBenchmark
{
    private readonly BinarySerializer _binarySerializer = new()
    {
        Endianness = Endianness.Big
    };

    private readonly SerializeBenchmarkData _data = new()
    {
        ByteValue1 = 0x0C,
        ByteValue2 = 0x0F,
        UShortValue1 = 0x0034,
        UShortValue2 = 0x0056,
        IntValue1 = 0x00001234,
        IntValue2 = 0x00005678,
        LongValue1 = 0x0000000012345678,
        LongValue2 = 0x000000009ABCDEF0,
    };

    private readonly BenchmarkDataSerializer _manualSerializer = new();

    private readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager = new();

    [Benchmark(Baseline = true)]
    public byte[] BinarySerializer()
    {
        var recyclableStream = _recyclableMemoryStreamManager.GetStream();
        _binarySerializer.Serialize(recyclableStream, _data);
        return recyclableStream.ToArray();
    }

    [Benchmark]
    public byte[] BitSerializer()
    {
        return BitSerializerMSB.Serialize(_data);
    }

    [Benchmark]
    public byte[] ManualSerializer()
    {
        return _manualSerializer.Serialize(_data);
    }

    [Benchmark]
    public byte[] ManualSerializerDelegate()
    {
        return _manualSerializer.SerializeFunc(_data);
    }
}
