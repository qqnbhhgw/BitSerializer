using System.Buffers;
using BitSerializer;
using BitSerializer.CrcAlgorithms;
using Shouldly;

namespace BitSerializerTests;

public partial class BitSerializerHighPerformanceTests
{
    [BitSerialize]
    public partial class HotPathData
    {
        [BitField(8)] public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<ushort> Values { get; set; } = new(8);

        [BitField] public HotNestedData Nested { get; set; } = new();
    }

    [BitSerialize]
    public partial class HotNestedData
    {
        [BitField(16)] public ushort Value { get; set; }
    }

    public sealed class TypedOffsetConverter : IBitFieldValueConverter<ushort, ushort>
    {
        public static ushort OnSerializeConvert(ushort value) => (ushort)(value + 1);
        public static ushort OnDeserializeConvert(ushort value) => (ushort)(value - 1);
    }

    [BitSerialize]
    public partial class TypedConverterData
    {
        [BitField(16)]
        [BitFieldRelated(null, typeof(TypedOffsetConverter))]
        public ushort Value { get; set; }
    }

    [BitSerialize]
    public partial class CrcHotPathData
    {
        [BitField(8), BitCrcInclude(nameof(Crc))] public byte First { get; set; }
        [BitField(8), BitCrcInclude(nameof(Crc))] public byte Second { get; set; }
        [BitField(16), BitCrc(typeof(CrcCcitt), ValidateOnDeserialize = true)] public ushort Crc { get; set; }
    }

    [BitSerialize]
    public partial class ArrayHotPathData
    {
        [BitField(8)] public byte Count { get; set; }

        [BitField, BitFieldRelated(nameof(Count))]
        public ushort[] Values { get; set; } = new ushort[4];
    }

    [BitSerialize]
    public partial class NestedListHotPathData
    {
        [BitField(8)] public byte Count { get; set; }

        [BitField, BitFieldRelated(nameof(Count))]
        public List<HotNestedData> Values { get; set; } = new();
    }

    [BitSerialize]
    public partial class ByteLengthNestedListHotPathData
    {
        [BitField(8)] public byte ByteLength { get; set; }

        [BitField, BitFieldRelated(nameof(ByteLength), RelationKind = BitRelationKind.ByteLength)]
        public List<HotNestedData> Values { get; set; } = new();
    }

    [BitSerialize]
    public partial class TerminatedStringHotPathData
    {
        [BitTerminatedString(Encoding = BitStringEncoding.UTF8)]
        public string Text { get; set; } = string.Empty;
    }

    [BitSerialize]
    public partial class LengthPrefixStringHotPathData
    {
        [BitLengthPrefixString(16, Encoding = BitStringEncoding.UTF8, MaxBytes = 16)]
        public string Text { get; set; } = string.Empty;
    }

    [BitSerialize]
    public partial class LengthFieldStringHotPathData
    {
        [BitField(8)] public byte Length { get; set; }

        [BitLengthFieldString(nameof(Length), Encoding = BitStringEncoding.UTF8, MaxBytes = 16)]
        public string Text { get; set; } = string.Empty;
    }

    public sealed class ShrinkingStringConverter : IBitFieldValueConverter<string, string>
    {
        public static string OnSerializeConvert(string value) => "W";
        public static string OnDeserializeConvert(string value) => value == "W" ? "property" : value;
    }

    [BitSerialize]
    public partial class ConvertedLengthFieldStringData
    {
        [BitField(8)] public byte Length { get; set; }

        [BitLengthFieldString(nameof(Length), Encoding = BitStringEncoding.ASCII, MaxBytes = 16)]
        [BitField]
        [BitFieldRelated(null, typeof(ShrinkingStringConverter))]
        public string Text { get; set; } = "property";
    }

    [BitSerialize]
    public partial class AsciiStringHotPathData
    {
        [BitFixedString(16, Encoding = BitStringEncoding.ASCII)]
        public string Fixed { get; set; } = string.Empty;

        [BitTerminatedString(Encoding = BitStringEncoding.ASCII)]
        public string Terminated { get; set; } = string.Empty;

        [BitLengthPrefixString(8, Encoding = BitStringEncoding.ASCII, MaxBytes = 16)]
        public string LengthPrefixed { get; set; } = string.Empty;
    }

    public sealed class ContextStringConverter : IBitFieldValueConverter<string, string, ConverterContext>
    {
        public static string OnSerializeConvert(string value, ConverterContext context)
            => context.Offset == 3 ? value.ToUpperInvariant() : string.Empty;

        public static string OnDeserializeConvert(string value, ConverterContext context)
            => context.Offset == 3 ? value.ToLowerInvariant() : string.Empty;
    }

    public sealed class ContextListConverter : IBitFieldValueConverter<List<byte>, List<byte>, ConverterContext>
    {
        public static List<byte> OnSerializeConvert(List<byte> value, ConverterContext context)
        {
            value[0] += (byte)context.Offset;
            return value;
        }

        public static List<byte> OnDeserializeConvert(List<byte> value, ConverterContext context)
        {
            value[0] -= (byte)context.Offset;
            return value;
        }
    }

    [BitSerialize]
    public partial class ContextTypedNonPrimitiveConverterData
    {
        [BitFixedString(4, Padding = (byte)'_', Encoding = BitStringEncoding.ASCII), BitFieldRelated(null, typeof(ContextStringConverter))]
        public string Text { get; set; } = "abc";

        [BitField(8)] public byte Count { get; set; }

        [BitField, BitFieldRelated(nameof(Count), typeof(ContextListConverter))]
        public List<byte> Values { get; set; } = new() { 7 };

        [BitIgnore] public ConverterContext Context { get; } = new() { Offset = 3 };
        public object SerializeContext() => Context;
        public object DeserializeContext() => Context;
    }

    public sealed class WideWireConverter : IBitFieldValueConverter<byte, ushort>
    {
        public static ushort OnSerializeConvert(byte value) => (ushort)(0xAB00 | value);
        public static byte OnDeserializeConvert(ushort value) => unchecked((byte)value);
    }

    [BitSerialize]
    public partial class WideWireConverterData
    {
        [BitField(16), BitFieldRelated(null, typeof(WideWireConverter))]
        public byte Value { get; set; }
    }

    public sealed class ConverterContext
    {
        public ushort Offset { get; init; }
    }

    public sealed class ContextTypedConverter : IBitFieldValueConverter<ushort, ushort, ConverterContext>
    {
        public static ushort OnSerializeConvert(ushort value, ConverterContext context) => (ushort)(value + context.Offset);
        public static ushort OnDeserializeConvert(ushort value, ConverterContext context) => (ushort)(value - context.Offset);
    }

    [BitSerialize]
    public partial class ContextTypedConverterData
    {
        [BitField(16), BitFieldRelated(null, typeof(ContextTypedConverter))]
        public ushort Value { get; set; }

        [BitIgnore]
        public ConverterContext Context { get; } = new() { Offset = 3 };

        public object SerializeContext() => Context;
        public object DeserializeContext() => Context;
    }

    public sealed class ThrowingTypedConverter : IBitFieldValueConverter<byte, byte>
    {
        public static byte OnSerializeConvert(byte value) => value;
        public static byte OnDeserializeConvert(byte value) => throw new ArgumentOutOfRangeException(nameof(value));
    }

    [BitSerialize]
    public partial class ThrowingConverterData
    {
        [BitField(8), BitFieldRelated(null, typeof(ThrowingTypedConverter))]
        public byte Value { get; set; }
    }

    [BitSerialize]
    public partial class SignedAlignedData
    {
        [BitField(16)] public short ShortValue { get; set; }
        [BitField(32)] public int IntValue { get; set; }
        [BitField(64)] public long LongValue { get; set; }
    }

    [BitSerialize]
    public partial struct StructHotPathData
    {
        [BitField(16)] public ushort Value { get; set; }
    }

    [BitSerialize]
    public partial class ReferenceArrayHotPathData
    {
        [BitField(8)] public byte Count { get; set; }
        [BitField, BitFieldRelated(nameof(Count))]
        public HotNestedData[] Values { get; set; } = Array.Empty<HotNestedData>();
    }

    [BitSerialize]
    public partial class ZeroLengthNestedData
    {
        [BitField(8)] public byte Length { get; set; }
        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public HotNestedData? Value { get; set; }
    }

    public sealed class ManualByteLengthPayload : IBitSerializable
    {
        public byte Value { get; set; }
        public int BeforeDeserializeCalls { get; private set; }
        public int AfterDeserializeCalls { get; private set; }

        public int SerializeLSB(Span<byte> bytes, int bitOffset) => SerializeLSB(bytes, bitOffset, null);
        public int SerializeMSB(Span<byte> bytes, int bitOffset) => SerializeMSB(bytes, bitOffset, null);
        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeLSB(bytes, bitOffset, null);
        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset) => DeserializeMSB(bytes, bitOffset, null);
        public int GetTotalBitLength() => 8;
        public object DeserializeContext() => this;

        public void BeforeDeserialize(object? context, ReadOnlySpan<byte> bytes)
        {
            ReferenceEquals(context, this).ShouldBeTrue();
            BeforeDeserializeCalls++;
        }

        public void AfterDeserialize(object? context, ReadOnlySpan<byte> bytes)
        {
            ReferenceEquals(context, this).ShouldBeTrue();
            AfterDeserializeCalls++;
        }

        public int SerializeLSB(Span<byte> bytes, int bitOffset, object? context)
        {
            BitHelperLSB.SetValueLength<byte>(bytes, bitOffset, 8, Value);
            return 8;
        }

        public int SerializeMSB(Span<byte> bytes, int bitOffset, object? context)
        {
            BitHelperMSB.SetValueLength<byte>(bytes, bitOffset, 8, Value);
            return 8;
        }

        public int DeserializeLSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context)
        {
            Value = BitHelperLSB.ValueLength<byte>(bytes, bitOffset, 8);
            return 8;
        }

        public int DeserializeMSB(ReadOnlySpan<byte> bytes, int bitOffset, object? context)
        {
            Value = BitHelperMSB.ValueLength<byte>(bytes, bitOffset, 8);
            return 8;
        }
    }

    [BitSerialize]
    public partial class ManualByteLengthEnvelope
    {
        [BitField(8)] public byte Length { get; set; }

        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public ManualByteLengthPayload Value { get; set; } = null!;
    }

    [BitSerialize]
    public partial class NegativeNestedBudgetEnvelope
    {
        [BitField(8)] public sbyte Length { get; set; }

        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public HotNestedData Value { get; set; } = null!;
    }

    [BitSerialize]
    public partial class ContextByteLengthPayload
    {
        [BitField(8)] public byte Value { get; set; }
        [BitIgnore] public int BeforeDeserializeCalls { get; private set; }
        [BitIgnore] public int AfterDeserializeCalls { get; private set; }

        public object DeserializeContext() => this;

        public void BeforeDeserialize(object? context, ReadOnlySpan<byte> bytes)
        {
            ReferenceEquals(context, this).ShouldBeTrue();
            BeforeDeserializeCalls++;
        }

        public void AfterDeserialize(object? context, ReadOnlySpan<byte> bytes)
        {
            ReferenceEquals(context, this).ShouldBeTrue();
            AfterDeserializeCalls++;
        }
    }

    [BitSerialize]
    public partial class ContextByteLengthEnvelope
    {
        [BitField(8)] public byte Length { get; set; }

        [BitField, BitFieldRelated(nameof(Length), RelationKind = BitRelationKind.ByteLength)]
        public ContextByteLengthPayload Value { get; set; } = null!;
    }

    [BitSerialize]
    public partial class TerminalPadData
    {
        [BitField, BitFieldCount(8, PadIfShort = true)]
        public List<byte> Values { get; set; } = new(8);
    }

    [BitSerialize]
    public partial class PartialByteData
    {
        [BitField(1)] public byte Value { get; set; }
    }

    [BitSerialize]
    public partial class ThrowingHookData
    {
        [BitField(8)] public byte Value { get; set; }
        public void BeforeDeserialize(object? context, ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("hook failure");
    }

    [BitSerialize]
    public partial class SpanHookData
    {
        [BitField(8)] public byte Value { get; set; }
        [BitIgnore] public int SeenLength { get; private set; }
        public void BeforeSerialize(object? context, Span<byte> bytes) => SeenLength = bytes.Length;
    }

    [BitSerialize]
    public partial class HookHotPathData
    {
        [BitField(8)] public byte Value { get; set; }
        public static readonly object SharedContext = new();
        public object SerializeContext() => SharedContext;
        public object DeserializeContext() => SharedContext;
        public void BeforeSerialize(object? context, Span<byte> bytes) { }
        public void AfterSerialize(object? context, Span<byte> bytes) { }
        public void BeforeDeserialize(object? context, ReadOnlySpan<byte> bytes) { }
        public void AfterDeserialize(object? context, ReadOnlySpan<byte> bytes) { }
    }

    [BitSerialize]
    public partial class PolyHotPathBase
    {
        [BitField(8)] public byte Value { get; set; }
    }

    [BitSerialize]
    public partial class PolyHotPathDerived : PolyHotPathBase
    {
        [BitField(8)] public byte Tail { get; set; }
    }

    [BitSerialize]
    public partial class PolyHotPathEnvelope
    {
        [BitField(8)] public byte Kind { get; set; }
        [BitField, BitFieldRelated(nameof(Kind)), BitPoly(1, typeof(PolyHotPathDerived))]
        public PolyHotPathBase Value { get; set; } = new PolyHotPathDerived();
    }

    [Fact]
    public void TrySerialize_ReportsRequiredSizeAndMatchesCompatibilityApi()
    {
        var value = CreateHotPathData();
        int required = BitSerializerMSB.GetRequiredByteCount(value);
        var destination = new byte[required];

        BitSerializerMSB.TrySerialize(value, destination.AsSpan(0, required - 1), out int shortWritten)
            .ShouldBe(OperationStatus.DestinationTooSmall);
        shortWritten.ShouldBe(0);

        BitSerializerMSB.TrySerialize(value, destination, out int bytesWritten).ShouldBe(OperationStatus.Done);
        bytesWritten.ShouldBe(required);
        destination.ShouldBe(BitSerializerMSB.Serialize(value));
    }

    [Fact]
    public void TrySerialize_NullByteLengthNestedUsesActualSize()
    {
        var value = new ZeroLengthNestedData();
        var msb = new byte[1];
        var lsb = new byte[1];

        BitSerializerMSB.GetRequiredByteCount(value).ShouldBe(1);
        BitSerializerLSB.GetRequiredByteCount(value).ShouldBe(1);
        BitSerializerMSB.TrySerialize(value, msb, out int msbWritten).ShouldBe(OperationStatus.Done);
        BitSerializerLSB.TrySerialize(value, lsb, out int lsbWritten).ShouldBe(OperationStatus.Done);

        msbWritten.ShouldBe(1);
        lsbWritten.ShouldBe(1);
        msb.ShouldBe(new byte[] { 0 });
        lsb.ShouldBe(new byte[] { 0 });
    }

    [Fact]
    public void LengthFieldStringConverterRunsBeforeBackfill()
    {
        var msbValue = new ConvertedLengthFieldStringData();
        var lsbValue = new ConvertedLengthFieldStringData();
        var msb = new byte[BitSerializerMSB.GetRequiredByteCount(msbValue)];
        var lsb = new byte[BitSerializerLSB.GetRequiredByteCount(lsbValue)];

        BitSerializerMSB.TrySerialize(msbValue, msb, out int msbWritten).ShouldBe(OperationStatus.Done);
        BitSerializerLSB.TrySerialize(lsbValue, lsb, out int lsbWritten).ShouldBe(OperationStatus.Done);

        msbWritten.ShouldBe(2);
        lsbWritten.ShouldBe(2);
        msb[..2].ShouldBe(new byte[] { 1, (byte)'W' });
        lsb[..2].ShouldBe(new byte[] { 1, (byte)'W' });
        BitSerializerMSB.Deserialize<ConvertedLengthFieldStringData>(msb.AsSpan(0, msbWritten)).Text.ShouldBe("property");
        BitSerializerLSB.Deserialize<ConvertedLengthFieldStringData>(lsb.AsSpan(0, lsbWritten)).Text.ShouldBe("property");
    }

    [Fact]
    public void TryDeserializeInto_ReusesNestedObjectAndListStorage()
    {
        var source = CreateHotPathData();
        byte[] bytes = BitSerializerMSB.Serialize(source);
        var destination = new HotPathData
        {
            Values = new List<ushort>(8),
            Nested = new HotNestedData()
        };
        var list = destination.Values;
        var nested = destination.Nested;

        BitSerializerMSB.TryDeserializeInto(bytes, destination, out int bytesConsumed)
            .ShouldBe(OperationStatus.Done);

        bytesConsumed.ShouldBe(bytes.Length);
        ReferenceEquals(destination.Values, list).ShouldBeTrue();
        ReferenceEquals(destination.Nested, nested).ShouldBeTrue();
        destination.Values.ShouldBe(source.Values);
        destination.Nested.Value.ShouldBe(source.Nested.Value);
    }

    [Fact]
    public void TryDeserializeInto_ReturnsDestinationTooSmallForListCapacity()
    {
        byte[] bytes = BitSerializerMSB.Serialize(CreateHotPathData());
        var destination = new HotPathData
        {
            Values = new List<ushort>(1),
            Nested = new HotNestedData()
        };

        BitSerializerMSB.TryDeserializeInto(bytes, destination, out _)
            .ShouldBe(OperationStatus.DestinationTooSmall);
    }

    [Fact]
    public void CapacityException_IdentifiesListArrayAndNullElementFailures()
    {
        var listException = Should.Throw<BitSerializationCapacityException>(() =>
            ((IBitSerializable)new NestedListHotPathData { Values = new List<HotNestedData>() })
            .DeserializeMSBInto(new byte[] { 1, 0, 7 }, 0, null, true));
        listException.Message.ShouldContain("insufficient list count");

        var arrayException = Should.Throw<BitSerializationCapacityException>(() =>
            ((IBitSerializable)new ReferenceArrayHotPathData { Values = Array.Empty<HotNestedData>() })
            .DeserializeMSBInto(new byte[] { 1, 0, 7 }, 0, null, true));
        arrayException.Message.ShouldContain("insufficient array length");

        var nullElementException = Should.Throw<BitSerializationCapacityException>(() =>
            ((IBitSerializable)new NestedListHotPathData { Values = new List<HotNestedData> { null! } })
            .DeserializeMSBInto(new byte[] { 1, 0, 7 }, 0, null, true));
        nullElementException.Message.ShouldContain("null reusable element at index 0");
    }

    [Fact]
    public void TryDeserialize_UpdatesStructByReference()
    {
        var source = new StructHotPathData { Value = 0x1234 };
        byte[] bytes = BitSerializerMSB.Serialize(source);
        var msbDestination = new StructHotPathData();
        var lsbDestination = new StructHotPathData();
        byte[] lsbBytes = BitSerializerLSB.Serialize(source);

        BitSerializerMSB.TryDeserialize(bytes, ref msbDestination, out int msbConsumed)
            .ShouldBe(OperationStatus.Done);
        BitSerializerLSB.TryDeserialize(lsbBytes, ref lsbDestination, out int lsbConsumed)
            .ShouldBe(OperationStatus.Done);

        msbDestination.Value.ShouldBe((ushort)0x1234);
        lsbDestination.Value.ShouldBe((ushort)0x1234);
        msbConsumed.ShouldBe(bytes.Length);
        lsbConsumed.ShouldBe(lsbBytes.Length);
    }

    [Fact]
    public void LsbHighPerformanceApis_MatchCompatibilityAndReuseStorage()
    {
        var source = CreateHotPathData();
        var buffer = new byte[BitSerializerLSB.GetRequiredByteCount(source)];
        var destination = new HotPathData
        {
            Values = new List<ushort>(8),
            Nested = new HotNestedData()
        };
        var nested = destination.Nested;
        var values = destination.Values;

        BitSerializerLSB.TrySerialize(source, buffer, out int bytesWritten).ShouldBe(OperationStatus.Done);
        buffer.ShouldBe(BitSerializerLSB.Serialize(source));
        BitSerializerLSB.TryDeserializeInto(buffer, destination, out int bytesConsumed).ShouldBe(OperationStatus.Done);

        bytesWritten.ShouldBe(buffer.Length);
        bytesConsumed.ShouldBe(buffer.Length);
        ReferenceEquals(destination.Nested, nested).ShouldBeTrue();
        ReferenceEquals(destination.Values, values).ShouldBeTrue();
        destination.Values.ShouldBe(source.Values);
        destination.Nested.Value.ShouldBe(source.Nested.Value);
    }

    [Fact]
    public void TryDeserializeInto_ReusesArraysAndNestedListElements()
    {
        var arraySource = new ArrayHotPathData { Values = new ushort[] { 1, 2, 3 } };
        byte[] arrayBytes = BitSerializerMSB.Serialize(arraySource);
        var arrayDestination = new ArrayHotPathData { Values = new ushort[4] };
        var array = arrayDestination.Values;

        BitSerializerMSB.TryDeserializeInto(arrayBytes, arrayDestination, out _).ShouldBe(OperationStatus.Done);
        ReferenceEquals(arrayDestination.Values, array).ShouldBeTrue();
        arrayDestination.Values[..3].ShouldBe(new ushort[] { 1, 2, 3 });

        var listSource = new NestedListHotPathData
        {
            Values = new List<HotNestedData> { new() { Value = 0x1234 }, new() { Value = 0x5678 } }
        };
        byte[] listBytes = BitSerializerMSB.Serialize(listSource);
        var first = new HotNestedData();
        var second = new HotNestedData();
        var listDestination = new NestedListHotPathData
        {
            Values = new List<HotNestedData> { first, second }
        };

        BitSerializerMSB.TryDeserializeInto(listBytes, listDestination, out _).ShouldBe(OperationStatus.Done);
        ReferenceEquals(listDestination.Values[0], first).ShouldBeTrue();
        ReferenceEquals(listDestination.Values[1], second).ShouldBeTrue();
        first.Value.ShouldBe((ushort)0x1234);
        second.Value.ShouldBe((ushort)0x5678);
    }

    [Fact]
    public void TryDeserializeInto_ReusesByteLengthNestedListElementsWithoutAllocating()
    {
        var source = new ByteLengthNestedListHotPathData
        {
            Values = new List<HotNestedData> { new() { Value = 0x1234 }, new() { Value = 0x5678 } }
        };
        var buffer = new byte[BitSerializerMSB.GetRequiredByteCount(source)];
        BitSerializerMSB.TrySerialize(source, buffer, out _).ShouldBe(OperationStatus.Done);

        var destination = new ByteLengthNestedListHotPathData
        {
            Values = new List<HotNestedData> { new(), new() }
        };
        var first = destination.Values[0];
        var second = destination.Values[1];

        for (int index = 0; index < 100; index++)
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        ReferenceEquals(destination.Values[0], first).ShouldBeTrue();
        ReferenceEquals(destination.Values[1], second).ShouldBeTrue();
        first.Value.ShouldBe((ushort)0x1234);
        second.Value.ShouldBe((ushort)0x5678);
    }

    [Fact]
    public void HighPerformanceApis_ReturnExpectedStatusesForShortAndInvalidInput()
    {
        var value = CreateHotPathData();
        byte[] full = BitSerializerMSB.Serialize(value);
        var destination = new HotPathData
        {
            Values = new List<ushort>(8),
            Nested = new HotNestedData()
        };

        BitSerializerMSB.TryDeserializeInto(full.AsSpan(0, full.Length - 1), destination, out _)
            .ShouldBe(OperationStatus.NeedMoreData);

        var crc = new CrcHotPathData { First = 0x12, Second = 0x34 };
        byte[] crcBytes = BitSerializerMSB.Serialize(crc);
        crcBytes[0] ^= 0x01;
        BitSerializerMSB.TryDeserializeInto(crcBytes, new CrcHotPathData(), out _)
            .ShouldBe(OperationStatus.InvalidData);
    }

    [Fact]
    public void AllStringKinds_TrySerializeMatchCompatibilityApis()
    {
        AssertMbsLsbString(new BitSerializerStringAndCustomTypeTests.FixedStringUtf8Data { Text = "Hello UTF8", Tail = 0xAA });
        AssertMbsLsbString(new TerminatedStringHotPathData { Text = "Hello UTF8" });
        AssertMbsLsbString(new LengthPrefixStringHotPathData { Text = "Hello UTF8" });
        AssertMbsLsbString(new LengthFieldStringHotPathData { Text = "Hello UTF8" });
    }

    [Fact]
    public void HotPaths_AreAllocationFreeAfterWarmup()
    {
        var source = CreateHotPathData();
        var destination = new HotPathData
        {
            Values = new List<ushort>(8),
            Nested = new HotNestedData()
        };
        var buffer = new byte[BitSerializerMSB.GetRequiredByteCount(source)];

        for (int index = 0; index < 100; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
    }

    [Fact]
    public void StronglyTypedConverter_SerializesWithoutBoxing()
    {
        var value = new TypedConverterData { Value = 0x1234 };
        var buffer = new byte[2];

        for (int index = 0; index < 100; index++)
            BitSerializerMSB.TrySerialize(value, buffer, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
            BitSerializerMSB.TrySerialize(value, buffer, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        buffer.ShouldBe(new byte[] { 0x12, 0x35 });
    }

    [Fact]
    public void CheckedConsumer_SignedFieldsAndWideWireConverterRoundTrip()
    {
        var signed = new SignedAlignedData { ShortValue = -2, IntValue = -3, LongValue = -4 };
        byte[] signedBytes = BitSerializerMSB.Serialize(signed);
        var signedDestination = new SignedAlignedData();
        BitSerializerMSB.TryDeserializeInto(signedBytes, signedDestination, out _).ShouldBe(OperationStatus.Done);
        signedDestination.ShortValue.ShouldBe((short)-2);
        signedDestination.IntValue.ShouldBe(-3);
        signedDestination.LongValue.ShouldBe(-4L);

        var wide = new WideWireConverterData { Value = 0xCD };
        byte[] wideBytes = BitSerializerMSB.Serialize(wide);
        wideBytes.ShouldBe(new byte[] { 0xAB, 0xCD });
        BitSerializerMSB.Deserialize<WideWireConverterData>(wideBytes).Value.ShouldBe((byte)0xCD);
    }

    [Fact]
    public void ContextTypedConverter_RoundTripsWithoutAllocating()
    {
        var source = new ContextTypedConverterData { Value = 0x1234 };
        var destination = new ContextTypedConverterData();
        var buffer = new byte[2];

        for (int index = 0; index < 100; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
        buffer.ShouldBe(new byte[] { 0x12, 0x37 });
        destination.Value.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void ContextTypedStringAndListConverters_RoundTripThroughTypedOverloads()
    {
        var source = new ContextTypedNonPrimitiveConverterData();
        byte[] bytes = BitSerializerMSB.Serialize(source);
        bytes.ShouldBe(new byte[] { (byte)'A', (byte)'B', (byte)'C', (byte)'_', 1, 10 });

        var result = BitSerializerMSB.Deserialize<ContextTypedNonPrimitiveConverterData>(bytes);
        result.Text.ShouldBe("abc");
        result.Values.ShouldBe(new byte[] { 7 });
    }

    [Fact]
    public void TryDeserialize_UsesPreciseStatusAndPreservesBusinessExceptions()
    {
        var shortString = new LengthPrefixStringHotPathData();
        BitSerializerMSB.TryDeserializeInto(new byte[] { 0, 2, (byte)'A' }, shortString, out _)
            .ShouldBe(OperationStatus.NeedMoreData);

        var throwingConverter = new ThrowingConverterData();
        Should.Throw<ArgumentOutOfRangeException>(() =>
            BitSerializerMSB.TryDeserializeInto(new byte[] { 1 }, throwingConverter, out _));
        Should.Throw<InvalidOperationException>(() =>
            BitSerializerMSB.TryDeserializeInto(new byte[] { 1 }, new ThrowingHookData(), out _));
    }

    [Fact]
    public void ReusePathsTrimStaleCollectionTailsAndRepeatZeroLengthNestedReads()
    {
        var listDestination = new NestedListHotPathData
        {
            Values = new List<HotNestedData> { new(), new() { Value = 99 } }
        };
        byte[] listBytes = BitSerializerMSB.Serialize(new NestedListHotPathData
        {
            Values = new List<HotNestedData> { new() { Value = 7 } }
        });
        BitSerializerMSB.TryDeserializeInto(listBytes, listDestination, out _).ShouldBe(OperationStatus.Done);
        listDestination.Values.Count.ShouldBe(1);
        listDestination.Values[0].Value.ShouldBe((ushort)7);

        var arrayDestination = new ReferenceArrayHotPathData
        {
            Values = new[] { new HotNestedData(), new HotNestedData { Value = 99 } }
        };
        byte[] arrayBytes = BitSerializerMSB.Serialize(new ReferenceArrayHotPathData
        {
            Values = new[] { new HotNestedData { Value = 8 } }
        });
        BitSerializerMSB.TryDeserializeInto(arrayBytes, arrayDestination, out _).ShouldBe(OperationStatus.Done);
        arrayDestination.Values[0].Value.ShouldBe((ushort)8);
        arrayDestination.Values[1].ShouldBeNull();

        var zero = new ZeroLengthNestedData { Value = new HotNestedData() };
        BitSerializerMSB.TryDeserializeInto(new byte[] { 0, 0 }, zero, out _).ShouldBe(OperationStatus.Done);
        BitSerializerMSB.TryDeserializeInto(new byte[] { 0, 0 }, zero, out _).ShouldBe(OperationStatus.Done);
        zero.Value.ShouldBeNull();
    }

    [Fact]
    public void TryDeserializeInto_ManualByteLengthNullTargetReturnsDestinationTooSmall()
    {
        var destination = new ManualByteLengthEnvelope();

        BitSerializerMSB.TryDeserializeInto(new byte[] { 1, 0x7B }, destination, out int bytesConsumed)
            .ShouldBe(OperationStatus.DestinationTooSmall);

        bytesConsumed.ShouldBe(0);

        var payload = new ManualByteLengthPayload();
        destination.Value = payload;
        BitSerializerMSB.TryDeserializeInto(new byte[] { 1, 0x7B }, destination, out bytesConsumed)
            .ShouldBe(OperationStatus.Done);

        bytesConsumed.ShouldBe(2);
        payload.Value.ShouldBe((byte)0x7B);
        payload.BeforeDeserializeCalls.ShouldBe(1);
        payload.AfterDeserializeCalls.ShouldBe(1);
    }

    [Fact]
    public void TryDeserializeInto_ManualByteLengthZeroBudgetAcceptsNullTarget()
    {
        var destination = new ManualByteLengthEnvelope();

        BitSerializerMSB.TryDeserializeInto(new byte[] { 0 }, destination, out int bytesConsumed)
            .ShouldBe(OperationStatus.Done);

        bytesConsumed.ShouldBe(1);
        destination.Value.ShouldBeNull();
    }

    [Fact]
    public void TryDeserializeInto_ContextByteLengthChecksTargetBeforeHooks()
    {
        var missing = new ContextByteLengthEnvelope();
        BitSerializerMSB.TryDeserializeInto(new byte[] { 1, 0x7B }, missing, out _)
            .ShouldBe(OperationStatus.DestinationTooSmall);

        var empty = new ContextByteLengthEnvelope();
        BitSerializerMSB.TryDeserializeInto(new byte[] { 0 }, empty, out _)
            .ShouldBe(OperationStatus.Done);
        empty.Value.ShouldBeNull();

        var payload = new ContextByteLengthPayload();
        var destination = new ContextByteLengthEnvelope { Value = payload };
        BitSerializerMSB.TryDeserializeInto(new byte[] { 1, 0x7B }, destination, out int bytesConsumed)
            .ShouldBe(OperationStatus.Done);

        bytesConsumed.ShouldBe(2);
        payload.Value.ShouldBe((byte)0x7B);
        payload.BeforeDeserializeCalls.ShouldBe(1);
        payload.AfterDeserializeCalls.ShouldBe(1);
    }

    [Fact]
    public void TryDeserializeInto_NegativeNestedBudgetReturnsInvalidData()
    {
        var destination = new NegativeNestedBudgetEnvelope { Value = new HotNestedData() };

        BitSerializerMSB.TryDeserializeInto(new byte[] { 0xFF, 0, 0 }, destination, out int bytesConsumed)
            .ShouldBe(OperationStatus.InvalidData);

        bytesConsumed.ShouldBe(0);
    }

    [Fact]
    public void PadIfShortTryDeserializeReturnsDoneAndActualConsumedBytes()
    {
        var destination = new TerminalPadData { Values = new List<byte>(8) };
        BitSerializerMSB.TryDeserializeInto(new byte[] { 1, 2 }, destination, out int consumed)
            .ShouldBe(OperationStatus.Done);
        consumed.ShouldBe(2);
        destination.Values.ShouldBe(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 });
    }

    [Fact]
    public void CompatibilitySpanSerializePassesFullSpanToHooks()
    {
        var msb = new SpanHookData();
        var lsb = new SpanHookData();
        BitSerializerMSB.Serialize(msb, new byte[8]);
        BitSerializerLSB.Serialize(lsb, new byte[8]);
        msb.SeenLength.ShouldBe(8);
        lsb.SeenLength.ShouldBe(8);
    }

    [Fact]
    public void AsciiTruncationMatchesEncodingByteSemantics()
    {
        Span<byte> destination = stackalloc byte[1];
        BitStringHelper.GetByteCount("😀", BitStringEncoding.ASCII, 1).ShouldBe(1);
        BitStringHelper.Encode("😀", BitStringEncoding.ASCII, destination, 1).ShouldBe(1);
        destination[0].ShouldBe((byte)'?');
    }

    [Fact]
    public void CompatibilitySpanSerialize_ClearsRequiredRangeOnly()
    {
        var value = new PartialByteData { Value = 1 };
        byte[] msb = Enumerable.Repeat((byte)0xFF, 3).ToArray();
        byte[] lsb = Enumerable.Repeat((byte)0xFF, 3).ToArray();

        BitSerializerMSB.Serialize(value, msb);
        BitSerializerLSB.Serialize(value, lsb);

        msb.ShouldBe(new byte[] { 0x80, 0xFF, 0xFF });
        lsb.ShouldBe(new byte[] { 0x01, 0xFF, 0xFF });
    }

    [Fact]
    public void RegistryCompatibilitySpanSerialize_ClearsRequiredRangeOnly()
    {
        var value = new PartialByteData { Value = 1 };
        byte[] destination = Enumerable.Repeat((byte)0xFF, 3).ToArray();

        BitSerializerMSB.Serialize(value, typeof(PartialByteData), destination);

        destination.ShouldBe(new byte[] { 0x80, 0xFF, 0xFF });
    }

    [Fact]
    public void TerminatedStringSerialize_DoesNotWritePastItsField()
    {
        var value = new TerminatedStringHotPathData { Text = "abc" };
        byte[] destination = Enumerable.Repeat((byte)0xCC, 16).ToArray();

        BitSerializerMSB.Serialize(value, destination);

        destination[..4].ShouldBe(new byte[] { (byte)'a', (byte)'b', (byte)'c', 0 });
        destination[4..].ShouldAllBe(item => item == 0xCC);
    }

    [Fact]
    public void StringAndBuiltInCrcSerialization_AreAllocationFreeAfterWarmup()
    {
        var stringData = new BitSerializerStringAndCustomTypeTests.FixedStringUtf8Data
        {
            Text = "Hello UTF8",
            Tail = 0xAA
        };
        var stringBuffer = new byte[BitSerializerMSB.GetRequiredByteCount(stringData)];
        var crcData = new CrcHotPathData { First = 0x12, Second = 0x34 };
        var crcBuffer = new byte[BitSerializerMSB.GetRequiredByteCount(crcData)];
        var asciiData = new AsciiStringHotPathData
        {
            Fixed = "fixed ASCII",
            Terminated = "terminated ASCII",
            LengthPrefixed = "length ASCII"
        };
        var asciiBuffer = new byte[BitSerializerMSB.GetRequiredByteCount(asciiData)];

        for (int index = 0; index < 100; index++)
        {
            BitSerializerMSB.TrySerialize(stringData, stringBuffer, out _);
            BitSerializerMSB.TrySerialize(crcData, crcBuffer, out _);
            BitSerializerMSB.TrySerialize(asciiData, asciiBuffer, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            BitSerializerMSB.TrySerialize(stringData, stringBuffer, out _);
            BitSerializerMSB.TrySerialize(crcData, crcBuffer, out _);
            BitSerializerMSB.TrySerialize(asciiData, asciiBuffer, out _);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
    }

    [Fact]
    public void FeatureMatrix_HotPathsAreAllocationFree()
    {
        AssertZeroAllocationRoundTrip(new TypedConverterData { Value = 0x1234 }, new TypedConverterData());
        AssertZeroAllocationRoundTrip(new ContextTypedConverterData { Value = 0x1234 }, new ContextTypedConverterData());
        AssertZeroAllocationRoundTrip(
            new ArrayHotPathData { Values = new ushort[] { 1, 2, 3 } },
            new ArrayHotPathData { Values = new ushort[3] });
        AssertZeroAllocationRoundTrip(
            new NestedListHotPathData { Values = new List<HotNestedData> { new() { Value = 1 }, new() { Value = 2 } } },
            new NestedListHotPathData { Values = new List<HotNestedData> { new(), new() } });
        AssertZeroAllocationMsbRoundTrip(
            new ByteLengthNestedListHotPathData { Values = new List<HotNestedData> { new() { Value = 1 }, new() { Value = 2 } } },
            new ByteLengthNestedListHotPathData { Values = new List<HotNestedData> { new(), new() } });
        AssertZeroAllocationRoundTrip(
            new ReferenceArrayHotPathData { Values = new[] { new HotNestedData { Value = 1 }, new HotNestedData { Value = 2 } } },
            new ReferenceArrayHotPathData { Values = new[] { new HotNestedData(), new HotNestedData() } });
        AssertZeroAllocationRoundTrip(new HookHotPathData { Value = 7 }, new HookHotPathData());
        AssertZeroAllocationRoundTrip(
            new PolyHotPathEnvelope { Value = new PolyHotPathDerived { Value = 1, Tail = 2 } },
            new PolyHotPathEnvelope { Value = new PolyHotPathDerived() });
    }

    private static HotPathData CreateHotPathData() => new()
    {
        Values = new List<ushort> { 0x1234, 0x5678, 0x9ABC },
        Nested = new HotNestedData { Value = 0xDEF0 }
    };

    private static void AssertMbsLsbString<T>(T value) where T : IBitSerializable
    {
        var msb = new byte[BitSerializerMSB.GetRequiredByteCount(value)];
        var lsb = new byte[BitSerializerLSB.GetRequiredByteCount(value)];

        BitSerializerMSB.TrySerialize(value, msb, out _).ShouldBe(OperationStatus.Done);
        BitSerializerLSB.TrySerialize(value, lsb, out _).ShouldBe(OperationStatus.Done);
        msb.ShouldBe(BitSerializerMSB.Serialize(value));
        lsb.ShouldBe(BitSerializerLSB.Serialize(value));
    }

    private static void AssertZeroAllocationRoundTrip<T>(T source, T destination)
        where T : class, IBitSerializable
    {
        var msb = new byte[BitSerializerMSB.GetRequiredByteCount(source)];
        var lsb = new byte[BitSerializerLSB.GetRequiredByteCount(source)];
        for (int index = 0; index < 100; index++)
        {
            BitSerializerMSB.TrySerialize(source, msb, out _);
            BitSerializerMSB.TryDeserializeInto(msb, destination, out _);
            BitSerializerLSB.TrySerialize(source, lsb, out _);
            BitSerializerLSB.TryDeserializeInto(lsb, destination, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            BitSerializerMSB.TrySerialize(source, msb, out _);
            BitSerializerMSB.TryDeserializeInto(msb, destination, out _);
            BitSerializerLSB.TrySerialize(source, lsb, out _);
            BitSerializerLSB.TryDeserializeInto(lsb, destination, out _);
        }
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    private static void AssertZeroAllocationMsbRoundTrip<T>(T source, T destination)
        where T : class, IBitSerializable
    {
        var buffer = new byte[BitSerializerMSB.GetRequiredByteCount(source)];
        for (int index = 0; index < 100; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1_000; index++)
        {
            BitSerializerMSB.TrySerialize(source, buffer, out _);
            BitSerializerMSB.TryDeserializeInto(buffer, destination, out _);
        }
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }
}
