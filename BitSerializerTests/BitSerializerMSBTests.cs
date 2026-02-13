using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public partial class BitSerializerMSBTests
{
    #region Test Models

    [BitSerialize]
    public partial class SimpleData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public ushort Value { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class AutoBitLengthData
    {
        [BitField]
        public byte ByteValue { get; set; }

        [BitField]
        public ushort UShortValue { get; set; }

        [BitField]
        public int IntValue { get; set; }
    }

    [BitSerialize]
    public partial class CustomBitLengthData
    {
        [BitField(4)]
        public byte NibbleHigh { get; set; }

        [BitField(4)]
        public byte NibbleLow { get; set; }

        [BitField(12)]
        public ushort TwelveBits { get; set; }

        [BitField(4)]
        public byte FourBits { get; set; }
    }

    public enum TestStatus : byte
    {
        Unknown = 0,
        Active = 1,
        Inactive = 2,
        Error = 3
    }

    [BitSerialize]
    public partial class EnumData
    {
        [BitField(8)]
        public TestStatus Status { get; set; }

        [BitField(16)]
        public ushort Code { get; set; }
    }

    [BitSerialize]
    public partial class InnerData
    {
        [BitField(8)]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    [BitSerialize]
    public partial class NestedData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public InnerData Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class ListData
    {
        [BitField(4)]
        public byte Count { get; set; }

        [BitField(4)]
        public byte Reserved { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class ListNestedData
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<InnerData> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class DataWithIgnored
    {
        [BitField(8)]
        public byte Value { get; set; }

        [BitIgnore]
        public string Description { get; set; } = string.Empty;

        [BitField(8)]
        public byte AnotherValue { get; set; }
    }

    #endregion

    #region Basic Deserialization Tests

    [BitSerialize]
    public partial class SimpleData2
    {
        [BitField(4)]
        public byte Header { get; set; }

        [BitField(7)]
        public ushort Value { get; set; }

        [BitField(5)]
        public byte Footer { get; set; }
    }

    [Fact]
    public void Deserialize_SimpleData2_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0xAB, 0x12};

        var result = BitSerializerMSB.Deserialize<SimpleData2>(bytes);

        result.Header.ShouldBe((byte)0xA);
        result.Value.ShouldBe((ushort)0x58);
        result.Footer.ShouldBe((byte)0x12);
    }

    [Fact]
    public void Deserialize_SimpleData_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0xAB, 0x12, 0x34, 0xCD };

        var result = BitSerializerMSB.Deserialize<SimpleData>(bytes);

        result.Header.ShouldBe((byte)0xAB);
        result.Value.ShouldBe((ushort)0x1234);
        result.Footer.ShouldBe((byte)0xCD);
    }

    [Fact]
    public void Deserialize_AutoBitLengthData_ShouldInferBitLengthFromType()
    {
        byte[] bytes = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE };

        var result = BitSerializerMSB.Deserialize<AutoBitLengthData>(bytes);

        result.ByteValue.ShouldBe((byte)0x12);
        result.UShortValue.ShouldBe((ushort)0x3456);
        result.IntValue.ShouldBe(0x789ABCDE);
    }

    [Fact]
    public void Deserialize_CustomBitLengthData_ShouldHandleNonByteAlignedBits()
    {
        byte[] bytes = { 0xA5, 0x67, 0x80 };

        var result = BitSerializerMSB.Deserialize<CustomBitLengthData>(bytes);

        result.NibbleHigh.ShouldBe((byte)0xA);
        result.NibbleLow.ShouldBe((byte)0x5);
        result.TwelveBits.ShouldBe((ushort)0x678);
    }

    #endregion

    #region Enum Tests

    [Fact]
    public void Deserialize_EnumData_ShouldDeserializeEnumCorrectly()
    {
        byte[] bytes = { 0x01, 0x12, 0x34 };

        var result = BitSerializerMSB.Deserialize<EnumData>(bytes);

        result.Status.ShouldBe(TestStatus.Active);
        result.Code.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void Deserialize_EnumData_AllEnumValues_ShouldWork()
    {
        foreach (TestStatus status in Enum.GetValues<TestStatus>())
        {
            byte[] bytes = { (byte)status, 0x00, 0x00 };
            var result = BitSerializerMSB.Deserialize<EnumData>(bytes);
            result.Status.ShouldBe(status);
        }
    }

    #endregion

    #region Nested Type Tests

    [Fact]
    public void Deserialize_NestedData_ShouldDeserializeNestedTypeCorrectly()
    {
        byte[] bytes = { 0xAA, 0x11, 0x22, 0xBB };

        var result = BitSerializerMSB.Deserialize<NestedData>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.Inner.ShouldNotBeNull();
        result.Inner.X.ShouldBe((byte)0x11);
        result.Inner.Y.ShouldBe((byte)0x22);
        result.Footer.ShouldBe((byte)0xBB);
    }

    #endregion

    #region List Tests

    [Fact]
    public void Deserialize_ListData_ShouldDeserializeListCorrectly()
    {
        byte[] bytes = [0x30, 0x11, 0x22, 0x33];

        var result = BitSerializerMSB.Deserialize<ListData>(bytes);

        result.Count.ShouldBe((byte)3);
        result.Items.Count.ShouldBe(3);
        result.Items[0].ShouldBe((byte)0x11);
        result.Items[1].ShouldBe((byte)0x22);
        result.Items[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Deserialize_ListData_EmptyList_ShouldWork()
    {
        byte[] bytes = { 0x00 };

        var result = BitSerializerMSB.Deserialize<ListData>(bytes);

        result.Count.ShouldBe((byte)0);
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_ListNestedData_ShouldDeserializeListOfNestedTypesCorrectly()
    {
        byte[] bytes = { 0x02, 0x11, 0x22, 0x33, 0x44 };

        var result = BitSerializerMSB.Deserialize<ListNestedData>(bytes);

        result.Count.ShouldBe((byte)2);
        result.Items.Count.ShouldBe(2);
        result.Items[0].X.ShouldBe((byte)0x11);
        result.Items[0].Y.ShouldBe((byte)0x22);
        result.Items[1].X.ShouldBe((byte)0x33);
        result.Items[1].Y.ShouldBe((byte)0x44);
    }

    #endregion

    #region BitIgnore Tests

    [Fact]
    public void Deserialize_DataWithIgnored_ShouldIgnoreMarkedFields()
    {
        byte[] bytes = { 0xAA, 0xBB };

        var result = BitSerializerMSB.Deserialize<DataWithIgnored>(bytes);

        result.Value.ShouldBe((byte)0xAA);
        result.AnotherValue.ShouldBe((byte)0xBB);
        result.Description.ShouldBeEmpty();
    }

    #endregion

    #region Caching Tests

    [Fact]
    public void Deserialize_CalledMultipleTimes_ShouldUseCachedDeserializer()
    {
        byte[] bytes1 = { 0x01, 0x00, 0x01, 0x02 };
        byte[] bytes2 = { 0x02, 0x00, 0x02, 0x03 };

        var result1 = BitSerializerMSB.Deserialize<SimpleData>(bytes1);
        var result2 = BitSerializerMSB.Deserialize<SimpleData>(bytes2);

        result1.Header.ShouldBe((byte)0x01);
        result2.Header.ShouldBe((byte)0x02);
    }

    #endregion

    #region ReadOnlySpan Overload Tests

    [Fact]
    public void Deserialize_WithReadOnlySpan_ShouldWork()
    {
        byte[] bytes = { 0xAB, 0x12, 0x34, 0xCD };
        ReadOnlySpan<byte> span = bytes;

        var result = BitSerializerMSB.Deserialize<SimpleData>(span);

        result.Header.ShouldBe((byte)0xAB);
        result.Value.ShouldBe((ushort)0x1234);
        result.Footer.ShouldBe((byte)0xCD);
    }

    #endregion

    #region Polymorphic Type Models

    [BitSerialize]
    public partial class BaseMessage
    {
        [BitField(8)]
        public byte CommonField { get; set; }
    }

    [BitSerialize]
    public partial class MessageTypeA : BaseMessage
    {
        [BitField(8)]
        public byte FieldA { get; set; }
    }

    [BitSerialize]
    public partial class MessageTypeB : BaseMessage
    {
        [BitField(16)]
        public ushort FieldB { get; set; }
    }

    [BitSerialize]
    public partial class MessageTypeC : BaseMessage
    {
        [BitField(8)]
        public byte FieldC1 { get; set; }

        [BitField(8)]
        public byte FieldC2 { get; set; }
    }

    [BitSerialize]
    public partial class PolymorphicContainer
    {
        [BitField(8)]
        public byte MessageType { get; set; }

        [BitField(24)]
        [BitFieldRelated(nameof(MessageType))]
        [BitPoly(1, typeof(MessageTypeA))]
        [BitPoly(2, typeof(MessageTypeB))]
        [BitPoly(3, typeof(MessageTypeC))]
        public BaseMessage Message { get; set; } = null!;
    }

    [BitSerialize]
    public partial class PolymorphicContainerAutoLength
    {
        [BitField(8)]
        public byte MessageType { get; set; }

        [BitField]
        [BitFieldRelated(nameof(MessageType))]
        [BitPoly(1, typeof(MessageTypeA))]
        [BitPoly(2, typeof(MessageTypeB))]
        public BaseMessage Message { get; set; } = null!;
    }

    #endregion

    #region Polymorphic Deserialization Tests

    [Fact]
    public void Deserialize_PolymorphicTypeA_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0x01, 0xAA, 0xBB, 0x00 };

        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        result.MessageType.ShouldBe((byte)1);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldBeOfType<MessageTypeA>();
        result.Message.CommonField.ShouldBe((byte)0xAA);
        ((MessageTypeA)result.Message).FieldA.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Deserialize_PolymorphicTypeB_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0x02, 0xCC, 0x12, 0x34 };

        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        result.MessageType.ShouldBe((byte)2);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldBeOfType<MessageTypeB>();
        result.Message.CommonField.ShouldBe((byte)0xCC);
        ((MessageTypeB)result.Message).FieldB.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void Deserialize_PolymorphicTypeC_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0x03, 0xDD, 0xEE, 0xFF };

        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        result.MessageType.ShouldBe((byte)3);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldBeOfType<MessageTypeC>();
        result.Message.CommonField.ShouldBe((byte)0xDD);
        ((MessageTypeC)result.Message).FieldC1.ShouldBe((byte)0xEE);
        ((MessageTypeC)result.Message).FieldC2.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void Deserialize_PolymorphicAutoLength_ShouldCalculateBitLengthFromMaxType()
    {
        byte[] bytes = { 0x02, 0x11, 0x22, 0x33 };

        var result = BitSerializerMSB.Deserialize<PolymorphicContainerAutoLength>(bytes);

        result.MessageType.ShouldBe((byte)2);
        result.Message.ShouldBeOfType<MessageTypeB>();
        result.Message.CommonField.ShouldBe((byte)0x11);
        ((MessageTypeB)result.Message).FieldB.ShouldBe((ushort)0x2233);
    }

    [Fact]
    public void Deserialize_PolymorphicAutoLength_TypeA_ShouldWork()
    {
        byte[] bytes = { 0x01, 0x44, 0x55, 0x00 };

        var result = BitSerializerMSB.Deserialize<PolymorphicContainerAutoLength>(bytes);

        result.MessageType.ShouldBe((byte)1);
        result.Message.ShouldBeOfType<MessageTypeA>();
        result.Message.CommonField.ShouldBe((byte)0x44);
        ((MessageTypeA)result.Message).FieldA.ShouldBe((byte)0x55);
    }

    [Fact]
    public void Deserialize_PolymorphicWithUnknownType_ShouldThrowException()
    {
        byte[] bytes = { 0x63, 0x00, 0x00, 0x00 };

        var action = () => BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("No polymorphic type mapping found");
        exception.Message.ShouldContain("99");
    }

    [Fact]
    public void Deserialize_PolymorphicCalledMultipleTimes_ShouldUseCachedMetadata()
    {
        byte[] bytesTypeA = { 0x01, 0xAA, 0xBB, 0x00 };
        byte[] bytesTypeB = { 0x02, 0xCC, 0x12, 0x34 };

        var result1 = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytesTypeA);
        var result2 = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytesTypeB);

        result1.Message.ShouldBeOfType<MessageTypeA>();
        result2.Message.ShouldBeOfType<MessageTypeB>();
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void Serialize_SimpleData_ShouldSerializeCorrectly()
    {
        var data = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)0xAB);
        bytes[1].ShouldBe((byte)0x12);
        bytes[2].ShouldBe((byte)0x34);
        bytes[3].ShouldBe((byte)0xCD);
    }

    [Fact]
    public void SerializeDeserialize_SimpleData_ShouldRoundTrip()
    {
        var original = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<SimpleData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Value.ShouldBe(original.Value);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_CustomBitLengthData_ShouldHandleNonByteAlignedBits()
    {
        var data = new CustomBitLengthData
        {
            NibbleHigh = 0xA,
            NibbleLow = 0x5,
            TwelveBits = 0x678,
            FourBits = 0x0
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(3);
        bytes[0].ShouldBe((byte)0xA5);
        bytes[1].ShouldBe((byte)0x67);
        bytes[2].ShouldBe((byte)0x80);
    }

    [Fact]
    public void SerializeDeserialize_CustomBitLengthData_ShouldRoundTrip()
    {
        var original = new CustomBitLengthData
        {
            NibbleHigh = 0xA,
            NibbleLow = 0x5,
            TwelveBits = 0x678,
            FourBits = 0x0
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<CustomBitLengthData>(bytes);

        deserialized.NibbleHigh.ShouldBe(original.NibbleHigh);
        deserialized.NibbleLow.ShouldBe(original.NibbleLow);
        deserialized.TwelveBits.ShouldBe(original.TwelveBits);
    }

    [Fact]
    public void SerializeDeserialize_EnumData_ShouldRoundTrip()
    {
        var original = new EnumData
        {
            Status = TestStatus.Active,
            Code = 0x1234
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<EnumData>(bytes);

        deserialized.Status.ShouldBe(original.Status);
        deserialized.Code.ShouldBe(original.Code);
    }

    [Fact]
    public void SerializeDeserialize_NestedData_ShouldRoundTrip()
    {
        var original = new NestedData
        {
            Header = 0xAA,
            Inner = new InnerData { X = 0x11, Y = 0x22 },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<NestedData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Inner.X.ShouldBe(original.Inner.X);
        deserialized.Inner.Y.ShouldBe(original.Inner.Y);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_ListData_ShouldRoundTrip()
    {
        var original = new ListData
        {
            Count = 3,
            Items = new List<byte> { 0x11, 0x22, 0x33 }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListData>(bytes);

        deserialized.Count.ShouldBe(original.Count);
        deserialized.Items.Count.ShouldBe(original.Items.Count);
        for (int i = 0; i < original.Items.Count; i++)
        {
            deserialized.Items[i].ShouldBe(original.Items[i]);
        }
    }

    [Fact]
    public void SerializeDeserialize_ListData_EmptyList_ShouldRoundTrip()
    {
        var original = new ListData
        {
            Count = 2,
            Reserved = 0b00001111,
            Items =
            [
                0x11,
                0x22
            ]
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListData>(bytes);

        deserialized.Count.ShouldBe((byte)original.Items.Count);
        deserialized.Reserved.ShouldBe(original.Reserved);
        deserialized.Items.ShouldBe(original.Items);
    }

    [Fact]
    public void SerializeDeserialize_ListNestedData_ShouldRoundTrip()
    {
        var original = new ListNestedData
        {
            Count = 2,
            Items = new List<InnerData>
            {
                new InnerData { X = 0x11, Y = 0x22 },
                new InnerData { X = 0x33, Y = 0x44 }
            }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListNestedData>(bytes);

        deserialized.Count.ShouldBe(original.Count);
        deserialized.Items.Count.ShouldBe(original.Items.Count);
        for (int i = 0; i < original.Items.Count; i++)
        {
            deserialized.Items[i].X.ShouldBe(original.Items[i].X);
            deserialized.Items[i].Y.ShouldBe(original.Items[i].Y);
        }
    }

    [Fact]
    public void SerializeDeserialize_PolymorphicTypeA_ShouldRoundTrip()
    {
        var original = new PolymorphicContainer
        {
            MessageType = 1,
            Message = new MessageTypeA { CommonField = 0xAA, FieldA = 0xBB }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        deserialized.MessageType.ShouldBe(original.MessageType);
        deserialized.Message.ShouldBeOfType<MessageTypeA>();
        deserialized.Message.CommonField.ShouldBe(original.Message.CommonField);
        ((MessageTypeA)deserialized.Message).FieldA.ShouldBe(((MessageTypeA)original.Message).FieldA);
    }

    [Fact]
    public void SerializeDeserialize_PolymorphicTypeB_ShouldRoundTrip()
    {
        var original = new PolymorphicContainer
        {
            MessageType = 2,
            Message = new MessageTypeB { CommonField = 0xCC, FieldB = 0x1234 }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        deserialized.MessageType.ShouldBe(original.MessageType);
        deserialized.Message.ShouldBeOfType<MessageTypeB>();
        deserialized.Message.CommonField.ShouldBe(original.Message.CommonField);
        ((MessageTypeB)deserialized.Message).FieldB.ShouldBe(((MessageTypeB)original.Message).FieldB);
    }

    [Fact]
    public void SerializeDeserialize_PolymorphicTypeC_ShouldRoundTrip()
    {
        var original = new PolymorphicContainer
        {
            MessageType = 3,
            Message = new MessageTypeC { CommonField = 0xDD, FieldC1 = 0xEE, FieldC2 = 0xFF }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        deserialized.MessageType.ShouldBe(original.MessageType);
        deserialized.Message.ShouldBeOfType<MessageTypeC>();
        deserialized.Message.CommonField.ShouldBe(original.Message.CommonField);
        var typeCMsg = (MessageTypeC)deserialized.Message;
        var origTypeCMsg = (MessageTypeC)original.Message;
        typeCMsg.FieldC1.ShouldBe(origTypeCMsg.FieldC1);
        typeCMsg.FieldC2.ShouldBe(origTypeCMsg.FieldC2);
    }

    [Fact]
    public void SerializeDeserialize_AutoBitLengthData_ShouldRoundTrip()
    {
        var original = new AutoBitLengthData
        {
            ByteValue = 0x12,
            UShortValue = 0x3456,
            IntValue = 0x789ABCDE
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<AutoBitLengthData>(bytes);

        deserialized.ByteValue.ShouldBe(original.ByteValue);
        deserialized.UShortValue.ShouldBe(original.UShortValue);
        deserialized.IntValue.ShouldBe(original.IntValue);
    }

    [Fact]
    public void SerializeDeserialize_DataWithIgnored_ShouldIgnoreMarkedFields()
    {
        var original = new DataWithIgnored
        {
            Value = 0xAA,
            Description = "This should be ignored",
            AnotherValue = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithIgnored>(bytes);

        deserialized.Value.ShouldBe(original.Value);
        deserialized.AnotherValue.ShouldBe(original.AnotherValue);
        deserialized.Description.ShouldBeEmpty();
    }

    [Fact]
    public void Serialize_WithSpanOverload_ShouldWork()
    {
        var data = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };
        var bytes = new byte[4];
        Span<byte> span = bytes;

        BitSerializerMSB.Serialize(data, span);

        bytes[0].ShouldBe((byte)0xAB);
        bytes[1].ShouldBe((byte)0x12);
        bytes[2].ShouldBe((byte)0x34);
        bytes[3].ShouldBe((byte)0xCD);
    }

    #endregion

    #region Additional Serialization Tests

    [Fact]
    public void Serialize_SimpleData2_NonByteAligned_ShouldSerializeCorrectly()
    {
        var data = new SimpleData2
        {
            Header = 0xA,
            Value = 0x58,
            Footer = 0x12
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(2);
        bytes[0].ShouldBe((byte)0xAB);
        bytes[1].ShouldBe((byte)0x12);
    }

    [Fact]
    public void SerializeDeserialize_SimpleData2_ShouldRoundTrip()
    {
        var original = new SimpleData2
        {
            Header = 0xA,
            Value = 0x58,
            Footer = 0x12
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<SimpleData2>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Value.ShouldBe(original.Value);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_EnumData_ShouldSerializeCorrectly()
    {
        var data = new EnumData
        {
            Status = TestStatus.Error,
            Code = 0xABCD
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(3);
        bytes[0].ShouldBe((byte)0x03);
        bytes[1].ShouldBe((byte)0xAB);
        bytes[2].ShouldBe((byte)0xCD);
    }

    [Fact]
    public void Serialize_NestedData_ShouldSerializeCorrectly()
    {
        var data = new NestedData
        {
            Header = 0xAA,
            Inner = new InnerData { X = 0x11, Y = 0x22 },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x11);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Serialize_ListData_ShouldSerializeCorrectly()
    {
        var data = new ListData
        {
            Count = 3,
            Reserved = 0x0,
            Items = new List<byte> { 0x11, 0x22, 0x33 }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x30);
        bytes[1].ShouldBe((byte)0x11);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Serialize_ListData_EmptyList_ShouldSerializeCorrectly()
    {
        var data = new ListData
        {
            Count = 0,
            Reserved = 0xF,
            Items = new List<byte>()
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes.Length.ShouldBe(1);
        bytes[0].ShouldBe((byte)0x0F);
    }

    [Fact]
    public void Serialize_ListNestedData_ShouldSerializeCorrectly()
    {
        var data = new ListNestedData
        {
            Count = 2,
            Items = new List<InnerData>
            {
                new InnerData { X = 0x11, Y = 0x22 },
                new InnerData { X = 0x33, Y = 0x44 }
            }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x02);
        bytes[1].ShouldBe((byte)0x11);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0x33);
        bytes[4].ShouldBe((byte)0x44);
    }

    [Fact]
    public void Serialize_PolymorphicTypeA_ShouldSerializeCorrectly()
    {
        var data = new PolymorphicContainer
        {
            MessageType = 1,
            Message = new MessageTypeA { CommonField = 0xAA, FieldA = 0xBB }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x01);
        bytes[1].ShouldBe((byte)0xAA);
        bytes[2].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Serialize_PolymorphicTypeB_ShouldSerializeCorrectly()
    {
        var data = new PolymorphicContainer
        {
            MessageType = 2,
            Message = new MessageTypeB { CommonField = 0xCC, FieldB = 0x1234 }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x02);
        bytes[1].ShouldBe((byte)0xCC);
        bytes[2].ShouldBe((byte)0x12);
        bytes[3].ShouldBe((byte)0x34);
    }

    #endregion

    #region Signed and Larger Numeric Type Tests

    [BitSerialize]
    public partial class SignedByteData
    {
        [BitField]
        public sbyte Value1 { get; set; }

        [BitField]
        public sbyte Value2 { get; set; }
    }

    [BitSerialize]
    public partial class ShortData
    {
        [BitField]
        public short SignedValue { get; set; }

        [BitField]
        public ushort UnsignedValue { get; set; }
    }

    [BitSerialize]
    public partial class IntData
    {
        [BitField]
        public int SignedValue { get; set; }

        [BitField]
        public uint UnsignedValue { get; set; }
    }

    [BitSerialize]
    public partial class LongData
    {
        [BitField]
        public long SignedValue { get; set; }

        [BitField]
        public ulong UnsignedValue { get; set; }
    }

    public enum StatusUShort : ushort
    {
        None = 0,
        Running = 1,
        Stopped = 0xFFFF
    }

    [BitSerialize]
    public partial class EnumCustomBitLengthData
    {
        [BitField(4)]
        public TestStatus Status { get; set; }

        [BitField(4)]
        public byte Padding { get; set; }
    }

    [BitSerialize]
    public partial class EnumUShortData
    {
        [BitField]
        public StatusUShort Status { get; set; }

        [BitField(8)]
        public byte Extra { get; set; }
    }

    [Fact]
    public void SerializeDeserialize_SignedByteData_ShouldRoundTrip()
    {
        var original = new SignedByteData
        {
            Value1 = -1,
            Value2 = 127
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<SignedByteData>(bytes);

        deserialized.Value1.ShouldBe(original.Value1);
        deserialized.Value2.ShouldBe(original.Value2);
    }

    [Fact]
    public void SerializeDeserialize_SignedByteData_NegativeValues_ShouldRoundTrip()
    {
        var original = new SignedByteData
        {
            Value1 = -128,
            Value2 = -1
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<SignedByteData>(bytes);

        deserialized.Value1.ShouldBe(original.Value1);
        deserialized.Value2.ShouldBe(original.Value2);
    }

    [Fact]
    public void SerializeDeserialize_ShortData_ShouldRoundTrip()
    {
        var original = new ShortData
        {
            SignedValue = -12345,
            UnsignedValue = 0xABCD
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ShortData>(bytes);

        deserialized.SignedValue.ShouldBe(original.SignedValue);
        deserialized.UnsignedValue.ShouldBe(original.UnsignedValue);
    }

    [Fact]
    public void SerializeDeserialize_IntData_ShouldRoundTrip()
    {
        var original = new IntData
        {
            SignedValue = -123456789,
            UnsignedValue = 0xDEADBEEF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<IntData>(bytes);

        deserialized.SignedValue.ShouldBe(original.SignedValue);
        deserialized.UnsignedValue.ShouldBe(original.UnsignedValue);
    }

    [Fact]
    public void SerializeDeserialize_LongData_ShouldRoundTrip()
    {
        var original = new LongData
        {
            SignedValue = -1234567890123456789L,
            UnsignedValue = 0xDEADBEEFCAFEBABE
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<LongData>(bytes);

        deserialized.SignedValue.ShouldBe(original.SignedValue);
        deserialized.UnsignedValue.ShouldBe(original.UnsignedValue);
    }

    [Fact]
    public void SerializeDeserialize_LongData_BoundaryValues_ShouldRoundTrip()
    {
        var original = new LongData
        {
            SignedValue = long.MinValue,
            UnsignedValue = ulong.MaxValue
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<LongData>(bytes);

        deserialized.SignedValue.ShouldBe(original.SignedValue);
        deserialized.UnsignedValue.ShouldBe(original.UnsignedValue);
    }

    [Fact]
    public void SerializeDeserialize_EnumCustomBitLength_ShouldRoundTrip()
    {
        var original = new EnumCustomBitLengthData
        {
            Status = TestStatus.Error,
            Padding = 0xF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(1);
        var deserialized = BitSerializerMSB.Deserialize<EnumCustomBitLengthData>(bytes);

        deserialized.Status.ShouldBe(original.Status);
        deserialized.Padding.ShouldBe(original.Padding);
    }

    [Fact]
    public void SerializeDeserialize_EnumUShort_ShouldRoundTrip()
    {
        var original = new EnumUShortData
        {
            Status = StatusUShort.Stopped,
            Extra = 0x42
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<EnumUShortData>(bytes);

        deserialized.Status.ShouldBe(original.Status);
        deserialized.Extra.ShouldBe(original.Extra);
    }

    #endregion

    #region ValueConverter Models

    public class OffsetConverter : IBitFieldValueConverter
    {
        public static object OnDeserializeConvert(object formDataValue)
        {
            return (byte)((byte)formDataValue + 10);
        }

        public static object OnSerializeConvert(object propertyValue)
        {
            return (byte)((byte)propertyValue - 10);
        }
    }

    public class InvertConverter : IBitFieldValueConverter
    {
        public static object OnDeserializeConvert(object formDataValue)
        {
            return (byte)((byte)formDataValue ^ 0xFF);
        }

        public static object OnSerializeConvert(object propertyValue)
        {
            return (byte)((byte)propertyValue ^ 0xFF);
        }
    }

    public class DoubleConverter : IBitFieldValueConverter
    {
        public static object OnDeserializeConvert(object formDataValue)
        {
            return (ushort)((ushort)formDataValue * 2);
        }

        public static object OnSerializeConvert(object propertyValue)
        {
            return (ushort)((ushort)propertyValue / 2);
        }
    }

    [BitSerialize]
    public partial class DataWithConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(8)]
        [BitFieldRelated(null, typeof(OffsetConverter))]
        public byte ConvertedValue { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial class DataWithMultipleConverters
    {
        [BitField(8)]
        [BitFieldRelated(null, typeof(OffsetConverter))]
        public byte OffsetField { get; set; }

        [BitField(8)]
        [BitFieldRelated(null, typeof(InvertConverter))]
        public byte InvertedField { get; set; }

        [BitField(8)]
        public byte NormalField { get; set; }
    }

    [BitSerialize]
    public partial class DataWithUShortConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        [BitFieldRelated(null, typeof(DoubleConverter))]
        public ushort DoubledValue { get; set; }
    }

    [BitSerialize]
    public partial class InnerDataWithConverter
    {
        [BitField(8)]
        [BitFieldRelated(null, typeof(OffsetConverter))]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    [BitSerialize]
    public partial class NestedDataWithConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public InnerDataWithConverter Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    #endregion

    #region ValueConverter Deserialization Tests

    [Fact]
    public void Deserialize_WithOffsetConverter_ShouldApplyConversion()
    {
        byte[] bytes = { 0xAA, 0x05, 0xBB };

        var result = BitSerializerMSB.Deserialize<DataWithConverter>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.ConvertedValue.ShouldBe((byte)15);
        result.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Deserialize_WithMultipleConverters_ShouldApplyEachCorrectly()
    {
        byte[] bytes = { 0x0A, 0xF0, 0x33 };

        var result = BitSerializerMSB.Deserialize<DataWithMultipleConverters>(bytes);

        result.OffsetField.ShouldBe((byte)20);
        result.InvertedField.ShouldBe((byte)0x0F);
        result.NormalField.ShouldBe((byte)0x33);
    }

    [Fact]
    public void Deserialize_WithUShortConverter_ShouldApplyConversion()
    {
        byte[] bytes = { 0x01, 0x00, 0x64 };

        var result = BitSerializerMSB.Deserialize<DataWithUShortConverter>(bytes);

        result.Header.ShouldBe((byte)0x01);
        result.DoubledValue.ShouldBe((ushort)200);
    }

    [Fact]
    public void Deserialize_NestedTypeWithConverter_ShouldApplyConversion()
    {
        byte[] bytes = { 0xAA, 0x05, 0x22, 0xBB };

        var result = BitSerializerMSB.Deserialize<NestedDataWithConverter>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.Inner.X.ShouldBe((byte)15);
        result.Inner.Y.ShouldBe((byte)0x22);
        result.Footer.ShouldBe((byte)0xBB);
    }

    #endregion

    #region ValueConverter Serialization Tests

    [Fact]
    public void Serialize_WithOffsetConverter_ShouldApplyConversion()
    {
        var data = new DataWithConverter
        {
            Header = 0xAA,
            ConvertedValue = 15,
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x05);
        bytes[2].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Serialize_WithMultipleConverters_ShouldApplyEachCorrectly()
    {
        var data = new DataWithMultipleConverters
        {
            OffsetField = 20,
            InvertedField = 0x0F,
            NormalField = 0x33
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x0A);
        bytes[1].ShouldBe((byte)0xF0);
        bytes[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Serialize_NestedTypeWithConverter_ShouldApplyConversion()
    {
        var data = new NestedDataWithConverter
        {
            Header = 0xAA,
            Inner = new InnerDataWithConverter { X = 15, Y = 0x22 },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x05);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0xBB);
    }

    #endregion

    #region ValueConverter RoundTrip Tests

    [Fact]
    public void SerializeDeserialize_WithConverter_ShouldRoundTrip()
    {
        var original = new DataWithConverter
        {
            Header = 0xAA,
            ConvertedValue = 25,
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithConverter>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.ConvertedValue.ShouldBe(original.ConvertedValue);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_WithMultipleConverters_ShouldRoundTrip()
    {
        var original = new DataWithMultipleConverters
        {
            OffsetField = 30,
            InvertedField = 0xAB,
            NormalField = 0x77
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithMultipleConverters>(bytes);

        deserialized.OffsetField.ShouldBe(original.OffsetField);
        deserialized.InvertedField.ShouldBe(original.InvertedField);
        deserialized.NormalField.ShouldBe(original.NormalField);
    }

    [Fact]
    public void SerializeDeserialize_NestedWithConverter_ShouldRoundTrip()
    {
        var original = new NestedDataWithConverter
        {
            Header = 0x11,
            Inner = new InnerDataWithConverter { X = 50, Y = 0x99 },
            Footer = 0x22
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<NestedDataWithConverter>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Inner.X.ShouldBe(original.Inner.X);
        deserialized.Inner.Y.ShouldBe(original.Inner.Y);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_WithUShortConverter_ShouldRoundTrip()
    {
        var original = new DataWithUShortConverter
        {
            Header = 0x01,
            DoubledValue = 500
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithUShortConverter>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.DoubledValue.ShouldBe(original.DoubledValue);
    }

    #endregion

    #region BitFieldCountAttribute Models

    [BitSerialize]
    public partial class FixedCountListData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(3)]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class FixedCountNestedListData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(2)]
        public List<InnerData> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class FixedCountPriorityData
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldCount(2)]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial class FixedCountCustomBitData
    {
        [BitField(4)]
        public byte Prefix { get; set; }

        [BitField(4)]
        [BitFieldCount(3)]
        public List<byte> Nibbles { get; set; } = new();
    }

    #endregion

    #region BitFieldCountAttribute Deserialization Tests

    [Fact]
    public void Deserialize_FixedCountList_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0xAA, 0x11, 0x22, 0x33 };

        var result = BitSerializerMSB.Deserialize<FixedCountListData>(bytes);

        result.Header.ShouldBe((byte)0xAA);
        result.Items.Count.ShouldBe(3);
        result.Items[0].ShouldBe((byte)0x11);
        result.Items[1].ShouldBe((byte)0x22);
        result.Items[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Deserialize_FixedCountNestedList_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0x01, 0x11, 0x22, 0x33, 0x44 };

        var result = BitSerializerMSB.Deserialize<FixedCountNestedListData>(bytes);

        result.Header.ShouldBe((byte)0x01);
        result.Items.Count.ShouldBe(2);
        result.Items[0].X.ShouldBe((byte)0x11);
        result.Items[0].Y.ShouldBe((byte)0x22);
        result.Items[1].X.ShouldBe((byte)0x33);
        result.Items[1].Y.ShouldBe((byte)0x44);
    }

    [Fact]
    public void Deserialize_FixedCountPriority_ShouldUseFixedCountOverRelated()
    {
        byte[] bytes = { 0x05, 0xAA, 0xBB };

        var result = BitSerializerMSB.Deserialize<FixedCountPriorityData>(bytes);

        result.Count.ShouldBe((byte)5);
        result.Items.Count.ShouldBe(2);
        result.Items[0].ShouldBe((byte)0xAA);
        result.Items[1].ShouldBe((byte)0xBB);
    }

    #endregion

    #region BitFieldCountAttribute Serialization Tests

    [Fact]
    public void Serialize_FixedCountList_ShouldSerializeCorrectly()
    {
        var data = new FixedCountListData
        {
            Header = 0xAA,
            Items = new List<byte> { 0x11, 0x22, 0x33 }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x11);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Serialize_FixedCountNestedList_ShouldSerializeCorrectly()
    {
        var data = new FixedCountNestedListData
        {
            Header = 0x01,
            Items = new List<InnerData>
            {
                new InnerData { X = 0x11, Y = 0x22 },
                new InnerData { X = 0x33, Y = 0x44 }
            }
        };

        var bytes = BitSerializerMSB.Serialize(data);

        bytes[0].ShouldBe((byte)0x01);
        bytes[1].ShouldBe((byte)0x11);
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0x33);
        bytes[4].ShouldBe((byte)0x44);
    }

    #endregion

    #region BitFieldCountAttribute RoundTrip Tests

    [Fact]
    public void SerializeDeserialize_FixedCountList_ShouldRoundTrip()
    {
        var original = new FixedCountListData
        {
            Header = 0xCC,
            Items = new List<byte> { 0x01, 0x02, 0x03 }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<FixedCountListData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Items.Count.ShouldBe(3);
        deserialized.Items[0].ShouldBe(original.Items[0]);
        deserialized.Items[1].ShouldBe(original.Items[1]);
        deserialized.Items[2].ShouldBe(original.Items[2]);
    }

    [Fact]
    public void SerializeDeserialize_FixedCountNestedList_ShouldRoundTrip()
    {
        var original = new FixedCountNestedListData
        {
            Header = 0xFF,
            Items = new List<InnerData>
            {
                new InnerData { X = 0xAA, Y = 0xBB },
                new InnerData { X = 0xCC, Y = 0xDD }
            }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<FixedCountNestedListData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Items.Count.ShouldBe(2);
        deserialized.Items[0].X.ShouldBe(original.Items[0].X);
        deserialized.Items[0].Y.ShouldBe(original.Items[0].Y);
        deserialized.Items[1].X.ShouldBe(original.Items[1].X);
        deserialized.Items[1].Y.ShouldBe(original.Items[1].Y);
    }

    [Fact]
    public void SerializeDeserialize_FixedCountCustomBit_ShouldRoundTrip()
    {
        var original = new FixedCountCustomBitData
        {
            Prefix = 0x0A,
            Nibbles = new List<byte> { 0x01, 0x02, 0x03 }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<FixedCountCustomBitData>(bytes);

        deserialized.Prefix.ShouldBe(original.Prefix);
        deserialized.Nibbles.Count.ShouldBe(3);
        deserialized.Nibbles[0].ShouldBe(original.Nibbles[0]);
        deserialized.Nibbles[1].ShouldBe(original.Nibbles[1]);
        deserialized.Nibbles[2].ShouldBe(original.Nibbles[2]);
    }

    #endregion

    #region Array Support Models

    [BitSerialize]
    public partial class ArrayData
    {
        [BitField(4)]
        public byte Count { get; set; }

        [BitField(4)]
        public byte Reserved { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public byte[] Items { get; set; } = [];
    }

    [BitSerialize]
    public partial class FixedCountArrayData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFieldCount(3)]
        public byte[] Items { get; set; } = [];
    }

    [BitSerialize]
    public partial class NestedArrayData
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public InnerData[] Items { get; set; } = [];
    }

    [BitSerialize]
    public partial class FixedCountCustomBitArrayData
    {
        [BitField(4)]
        public byte Prefix { get; set; }

        [BitField(4)]
        [BitFieldCount(3)]
        public byte[] Nibbles { get; set; } = [];
    }

    #endregion

    #region Array Tests

    [Fact]
    public void SerializeDeserialize_ArrayData_ShouldRoundTrip()
    {
        var original = new ArrayData
        {
            Count = 3,
            Reserved = 0,
            Items = [0x11, 0x22, 0x33]
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ArrayData>(bytes);

        deserialized.Count.ShouldBe(original.Count);
        deserialized.Items.Length.ShouldBe(3);
        deserialized.Items[0].ShouldBe((byte)0x11);
        deserialized.Items[1].ShouldBe((byte)0x22);
        deserialized.Items[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Serialize_ArrayData_ShouldProduceSameBytesAsList()
    {
        var listData = new ListData
        {
            Count = 3,
            Reserved = 0,
            Items = [0x11, 0x22, 0x33]
        };
        var arrayData = new ArrayData
        {
            Count = 3,
            Reserved = 0,
            Items = [0x11, 0x22, 0x33]
        };

        var listBytes = BitSerializerMSB.Serialize(listData);
        var arrayBytes = BitSerializerMSB.Serialize(arrayData);

        arrayBytes.ShouldBe(listBytes);
    }

    [Fact]
    public void SerializeDeserialize_FixedCountArrayData_ShouldRoundTrip()
    {
        var original = new FixedCountArrayData
        {
            Header = 0xAA,
            Items = [0x11, 0x22, 0x33]
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<FixedCountArrayData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Items.Length.ShouldBe(3);
        deserialized.Items[0].ShouldBe(original.Items[0]);
        deserialized.Items[1].ShouldBe(original.Items[1]);
        deserialized.Items[2].ShouldBe(original.Items[2]);
    }

    [Fact]
    public void SerializeDeserialize_NestedArrayData_ShouldRoundTrip()
    {
        var original = new NestedArrayData
        {
            Count = 2,
            Items =
            [
                new InnerData { X = 0x11, Y = 0x22 },
                new InnerData { X = 0x33, Y = 0x44 }
            ]
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<NestedArrayData>(bytes);

        deserialized.Count.ShouldBe(original.Count);
        deserialized.Items.Length.ShouldBe(2);
        deserialized.Items[0].X.ShouldBe(original.Items[0].X);
        deserialized.Items[0].Y.ShouldBe(original.Items[0].Y);
        deserialized.Items[1].X.ShouldBe(original.Items[1].X);
        deserialized.Items[1].Y.ShouldBe(original.Items[1].Y);
    }

    [Fact]
    public void SerializeDeserialize_FixedCountCustomBitArrayData_ShouldRoundTrip()
    {
        var original = new FixedCountCustomBitArrayData
        {
            Prefix = 0x0A,
            Nibbles = [0x01, 0x02, 0x03]
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<FixedCountCustomBitArrayData>(bytes);

        deserialized.Prefix.ShouldBe(original.Prefix);
        deserialized.Nibbles.Length.ShouldBe(3);
        deserialized.Nibbles[0].ShouldBe(original.Nibbles[0]);
        deserialized.Nibbles[1].ShouldBe(original.Nibbles[1]);
        deserialized.Nibbles[2].ShouldBe(original.Nibbles[2]);
    }

    [Fact]
    public void Deserialize_ArrayData_EmptyArray_ShouldWork()
    {
        var original = new ArrayData
        {
            Count = 0,
            Reserved = 0xF,
            Items = []
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ArrayData>(bytes);

        deserialized.Count.ShouldBe((byte)0);
        deserialized.Items.Length.ShouldBe(0);
    }

    #endregion

    #region Record Type Models

    [BitSerialize]
    public partial record RecordClassData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public ushort Value { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial record struct RecordStructData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public ushort Value { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial record class RecordWithCustomBitLength
    {
        [BitField(4)]
        public byte NibbleHigh { get; set; }

        [BitField(4)]
        public byte NibbleLow { get; set; }

        [BitField(12)]
        public ushort TwelveBits { get; set; }
    }

    [BitSerialize]
    public partial record class RecordWithEnum
    {
        [BitField(8)]
        public TestStatus Status { get; set; }

        [BitField(16)]
        public ushort Code { get; set; }
    }

    [BitSerialize]
    public partial record class RecordInnerData
    {
        [BitField(8)]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    [BitSerialize]
    public partial record class RecordWithNested
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public RecordInnerData Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    [BitSerialize]
    public partial record class RecordWithList
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    [BitSerialize]
    public partial record class RecordWithIgnored
    {
        [BitField(8)]
        public byte Value { get; set; }

        [BitIgnore]
        public string Description { get; set; } = string.Empty;

        [BitField(8)]
        public byte AnotherValue { get; set; }
    }

    #endregion

    #region Record Class Tests

    [Fact]
    public void SerializeDeserialize_RecordClass_ShouldRoundTrip()
    {
        var original = new RecordClassData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordClassData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Value.ShouldBe(original.Value);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_RecordClass_ShouldProduceSameBytesAsClass()
    {
        var classData = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };
        var recordData = new RecordClassData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var classBytes = BitSerializerMSB.Serialize(classData);
        var recordBytes = BitSerializerMSB.Serialize(recordData);

        recordBytes.ShouldBe(classBytes);
    }

    [Fact]
    public void Deserialize_RecordClass_ShouldDeserializeCorrectly()
    {
        byte[] bytes = { 0xAB, 0x12, 0x34, 0xCD };

        var result = BitSerializerMSB.Deserialize<RecordClassData>(bytes);

        result.Header.ShouldBe((byte)0xAB);
        result.Value.ShouldBe((ushort)0x1234);
        result.Footer.ShouldBe((byte)0xCD);
    }

    #endregion

    #region Record Struct Tests

    [Fact]
    public void SerializeDeserialize_RecordStruct_ShouldRoundTrip()
    {
        var original = new RecordStructData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordStructData>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Value.ShouldBe(original.Value);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_RecordStruct_ShouldProduceSameBytesAsClass()
    {
        var classData = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };
        var recordData = new RecordStructData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        var classBytes = BitSerializerMSB.Serialize(classData);
        var recordBytes = BitSerializerMSB.Serialize(recordData);

        recordBytes.ShouldBe(classBytes);
    }

    #endregion

    #region Record with Features Tests

    [Fact]
    public void SerializeDeserialize_RecordWithCustomBitLength_ShouldRoundTrip()
    {
        var original = new RecordWithCustomBitLength
        {
            NibbleHigh = 0xA,
            NibbleLow = 0x5,
            TwelveBits = 0x678
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordWithCustomBitLength>(bytes);

        deserialized.NibbleHigh.ShouldBe(original.NibbleHigh);
        deserialized.NibbleLow.ShouldBe(original.NibbleLow);
        deserialized.TwelveBits.ShouldBe(original.TwelveBits);
    }

    [Fact]
    public void SerializeDeserialize_RecordWithEnum_ShouldRoundTrip()
    {
        var original = new RecordWithEnum
        {
            Status = TestStatus.Active,
            Code = 0x1234
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordWithEnum>(bytes);

        deserialized.Status.ShouldBe(original.Status);
        deserialized.Code.ShouldBe(original.Code);
    }

    [Fact]
    public void SerializeDeserialize_RecordWithNested_ShouldRoundTrip()
    {
        var original = new RecordWithNested
        {
            Header = 0xAA,
            Inner = new RecordInnerData { X = 0x11, Y = 0x22 },
            Footer = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordWithNested>(bytes);

        deserialized.Header.ShouldBe(original.Header);
        deserialized.Inner.X.ShouldBe(original.Inner.X);
        deserialized.Inner.Y.ShouldBe(original.Inner.Y);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_RecordWithList_ShouldRoundTrip()
    {
        var original = new RecordWithList
        {
            Count = 3,
            Items = new List<byte> { 0x11, 0x22, 0x33 }
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordWithList>(bytes);

        deserialized.Count.ShouldBe(original.Count);
        deserialized.Items.ShouldBe(original.Items);
    }

    [Fact]
    public void SerializeDeserialize_RecordWithIgnored_ShouldRoundTrip()
    {
        var original = new RecordWithIgnored
        {
            Value = 0xAA,
            Description = "This should be ignored",
            AnotherValue = 0xBB
        };

        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<RecordWithIgnored>(bytes);

        deserialized.Value.ShouldBe(original.Value);
        deserialized.AnotherValue.ShouldBe(original.AnotherValue);
        deserialized.Description.ShouldBeEmpty();
    }

    #endregion
}
