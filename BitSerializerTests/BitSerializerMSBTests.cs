using Shouldly;
using BitSerializer;

namespace BitSerializerTests;

public class BitSerializerMSBTests
{
    #region Test Models

    /// <summary>
    /// Simple test class with basic numeric types
    /// </summary>
    public class SimpleData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        public ushort Value { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    /// <summary>
    /// Test class with auto-inferred bit lengths
    /// </summary>
    public class AutoBitLengthData
    {
        [BitField]
        public byte ByteValue { get; set; }

        [BitField]
        public ushort UShortValue { get; set; }

        [BitField]
        public int IntValue { get; set; }
    }

    /// <summary>
    /// Test class with custom bit lengths (not aligned to byte boundaries)
    /// </summary>
    public class CustomBitLengthData
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

    /// <summary>
    /// Test enum for enum serialization tests
    /// </summary>
    public enum TestStatus : byte
    {
        Unknown = 0,
        Active = 1,
        Inactive = 2,
        Error = 3
    }

    /// <summary>
    /// Test class with enum type
    /// </summary>
    public class EnumData
    {
        [BitField(8)]
        public TestStatus Status { get; set; }

        [BitField(16)]
        public ushort Code { get; set; }
    }

    /// <summary>
    /// Nested inner class
    /// </summary>
    public class InnerData
    {
        [BitField(8)]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    /// <summary>
    /// Test class with nested type
    /// </summary>
    public class NestedData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public InnerData Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    /// <summary>
    /// Test class with List container
    /// </summary>
    public class ListData
    {
        [BitField(4)]
        public byte Count { get; set; }

        [BitField(4)]
        public byte Reserved { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    /// <summary>
    /// Test class with List of nested types
    /// </summary>
    public class ListNestedData
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFieldRelated(nameof(Count))]
        public List<InnerData> Items { get; set; } = new();
    }

    /// <summary>
    /// Test class with ignored field
    /// </summary>
    public class DataWithIgnored
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

    /// <summary>
    /// Simple test class with basic numeric types
    /// </summary>
    public class SimpleData2
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

        // Act
        var result = BitSerializerMSB.Deserialize<SimpleData2>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0xA);
        result.Value.ShouldBe((ushort)0x58);
        result.Footer.ShouldBe((byte)0x12);
    }

    [Fact]
    public void Deserialize_SimpleData_ShouldDeserializeCorrectly()
    {
        // Arrange: Header=0xAB, Value=0x1234, Footer=0xCD
        byte[] bytes = { 0xAB, 0x12, 0x34, 0xCD };

        // Act
        var result = BitSerializerMSB.Deserialize<SimpleData>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0xAB);
        result.Value.ShouldBe((ushort)0x1234);
        result.Footer.ShouldBe((byte)0xCD);
    }

    [Fact]
    public void Deserialize_AutoBitLengthData_ShouldInferBitLengthFromType()
    {
        // Arrange: byte=0x12, ushort=0x3456, int=0x789ABCDE
        byte[] bytes = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE };

        // Act
        var result = BitSerializerMSB.Deserialize<AutoBitLengthData>(bytes);

        // Assert
        result.ByteValue.ShouldBe((byte)0x12);
        result.UShortValue.ShouldBe((ushort)0x3456);
        result.IntValue.ShouldBe(0x789ABCDE);
    }

    [Fact]
    public void Deserialize_CustomBitLengthData_ShouldHandleNonByteAlignedBits()
    {
        // Arrange: 4 bits + 4 bits + 12 bits + 4 bits = 24 bits = 3 bytes
        // Binary: 1010 0101 | 0110 0111 | 1000 xxxx
        // NibbleHigh = 0xA (1010), NibbleLow = 0x5 (0101)
        // TwelveBits = 0x678 (0110 0111 1000), FourBits = remaining
        byte[] bytes = { 0xA5, 0x67, 0x80 };

        // Act
        var result = BitSerializerMSB.Deserialize<CustomBitLengthData>(bytes);

        // Assert
        result.NibbleHigh.ShouldBe((byte)0xA);
        result.NibbleLow.ShouldBe((byte)0x5);
        result.TwelveBits.ShouldBe((ushort)0x678);
    }

    #endregion

    #region Enum Tests

    [Fact]
    public void Deserialize_EnumData_ShouldDeserializeEnumCorrectly()
    {
        // Arrange: Status=Active(1), Code=0x1234
        byte[] bytes = { 0x01, 0x12, 0x34 };

        // Act
        var result = BitSerializerMSB.Deserialize<EnumData>(bytes);

        // Assert
        result.Status.ShouldBe(TestStatus.Active);
        result.Code.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void Deserialize_EnumData_AllEnumValues_ShouldWork()
    {
        // Test all enum values
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
        // Arrange: Header=0xAA, Inner.X=0x11, Inner.Y=0x22, Footer=0xBB
        byte[] bytes = { 0xAA, 0x11, 0x22, 0xBB };

        // Act
        var result = BitSerializerMSB.Deserialize<NestedData>(bytes);

        // Assert
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
        // Arrange: Count=3, Items=[0x11, 0x22, 0x33]
        byte[] bytes = [0x30, 0x11, 0x22, 0x33];

        // Act
        var result = BitSerializerMSB.Deserialize<ListData>(bytes);

        // Assert
        result.Count.ShouldBe((byte)3);
        result.Items.Count.ShouldBe(3);
        result.Items[0].ShouldBe((byte)0x11);
        result.Items[1].ShouldBe((byte)0x22);
        result.Items[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Deserialize_ListData_EmptyList_ShouldWork()
    {
        // Arrange: Count=0
        byte[] bytes = { 0x00 };

        // Act
        var result = BitSerializerMSB.Deserialize<ListData>(bytes);

        // Assert
        result.Count.ShouldBe((byte)0);
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_ListNestedData_ShouldDeserializeListOfNestedTypesCorrectly()
    {
        // Arrange: Count=2, Items=[{X=0x11, Y=0x22}, {X=0x33, Y=0x44}]
        byte[] bytes = { 0x02, 0x11, 0x22, 0x33, 0x44 };

        // Act
        var result = BitSerializerMSB.Deserialize<ListNestedData>(bytes);

        // Assert
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
        // Arrange: Value=0xAA, AnotherValue=0xBB (Description is ignored)
        byte[] bytes = { 0xAA, 0xBB };

        // Act
        var result = BitSerializerMSB.Deserialize<DataWithIgnored>(bytes);

        // Assert
        result.Value.ShouldBe((byte)0xAA);
        result.AnotherValue.ShouldBe((byte)0xBB);
        result.Description.ShouldBeEmpty(); // Default value, not deserialized
    }

    #endregion

    #region Caching Tests

    [Fact]
    public void Deserialize_CalledMultipleTimes_ShouldUseCachedDeserializer()
    {
        // Arrange
        byte[] bytes1 = { 0x01, 0x00, 0x01, 0x02 };
        byte[] bytes2 = { 0x02, 0x00, 0x02, 0x03 };

        // Act - Call multiple times to ensure caching works
        var result1 = BitSerializerMSB.Deserialize<SimpleData>(bytes1);
        var result2 = BitSerializerMSB.Deserialize<SimpleData>(bytes2);

        // Assert
        result1.Header.ShouldBe((byte)0x01);
        result2.Header.ShouldBe((byte)0x02);
    }

    #endregion

    #region ReadOnlySpan Overload Tests

    [Fact]
    public void Deserialize_WithReadOnlySpan_ShouldWork()
    {
        // Arrange
        byte[] bytes = { 0xAB, 0x12, 0x34, 0xCD };
        ReadOnlySpan<byte> span = bytes;

        // Act
        var result = BitSerializerMSB.Deserialize<SimpleData>(span);

        // Assert
        result.Header.ShouldBe((byte)0xAB);
        result.Value.ShouldBe((ushort)0x1234);
        result.Footer.ShouldBe((byte)0xCD);
    }

    #endregion

    #region Validation Tests

    /// <summary>
    /// Class without any attributes - should throw
    /// </summary>
    public class InvalidNoAttributes
    {
        public byte Value { get; set; }
    }

    /// <summary>
    /// Class with unsupported type without BitIgnore - should throw
    /// </summary>
    public class InvalidUnsupportedType
    {
        [BitField(8)]
        public byte Value { get; set; }

        [BitField]
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Class with List but no BitFieldRelated - should throw
    /// </summary>
    public class InvalidListNoRelated
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        public List<byte> Items { get; set; } = new();
    }

    [Fact]
    public void Deserialize_TypeWithoutAttributes_ShouldThrowException()
    {
        // Arrange
        byte[] bytes = { 0x01 };

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<InvalidNoAttributes>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("must have either BitFieldAttribute or BitIgnoreAttribute");
    }

    [Fact]
    public void Deserialize_TypeWithUnsupportedType_ShouldThrowException()
    {
        // Arrange
        byte[] bytes = { 0x01 };

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<InvalidUnsupportedType>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("unsupported type");
    }

    [Fact]
    public void Deserialize_ListWithoutRelatedAttribute_ShouldThrowException()
    {
        // Arrange
        byte[] bytes = { 0x01 };

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<InvalidListNoRelated>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("must have BitFieldRelatedAttribute");
    }

    #endregion

    #region Polymorphic Type Models

    /// <summary>
    /// Base class for polymorphic deserialization tests
    /// </summary>
    public class BaseMessage
    {
        [BitField(8)]
        public byte CommonField { get; set; }
    }

    /// <summary>
    /// Derived type A with additional byte field
    /// </summary>
    public class MessageTypeA : BaseMessage
    {
        [BitField(8)]
        public byte FieldA { get; set; }
    }

    /// <summary>
    /// Derived type B with additional ushort field
    /// </summary>
    public class MessageTypeB : BaseMessage
    {
        [BitField(16)]
        public ushort FieldB { get; set; }
    }

    /// <summary>
    /// Derived type C with multiple fields
    /// </summary>
    public class MessageTypeC : BaseMessage
    {
        [BitField(8)]
        public byte FieldC1 { get; set; }

        [BitField(8)]
        public byte FieldC2 { get; set; }
    }

    /// <summary>
    /// Container with polymorphic message field
    /// </summary>
    public class PolymorphicContainer
    {
        [BitField(8)]
        public byte MessageType { get; set; }

        [BitField(24)] // Max size of all polymorphic types (CommonField:8 + max(FieldA:8, FieldB:16, FieldC1+C2:16) = 24)
        [BitFieldRelated(nameof(MessageType))]
        [BitPoly(1, typeof(MessageTypeA))]
        [BitPoly(2, typeof(MessageTypeB))]
        [BitPoly(3, typeof(MessageTypeC))]
        public BaseMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// Container with auto-calculated bit length for polymorphic field
    /// </summary>
    public class PolymorphicContainerAutoLength
    {
        [BitField(8)]
        public byte MessageType { get; set; }

        [BitField] // Auto-calculate from max of all polymorphic types
        [BitFieldRelated(nameof(MessageType))]
        [BitPoly(1, typeof(MessageTypeA))]
        [BitPoly(2, typeof(MessageTypeB))]
        public BaseMessage Message { get; set; } = null!;
    }

    /// <summary>
    /// Invalid polymorphic container without BitFieldRelated
    /// </summary>
    public class InvalidPolymorphicNoRelated
    {
        [BitField(8)]
        public byte MessageType { get; set; }

        [BitField]
        [BitPoly(1, typeof(MessageTypeA))]
        public BaseMessage Message { get; set; } = null!;
    }

    #endregion

    #region Polymorphic Deserialization Tests

    [Fact]
    public void Deserialize_PolymorphicTypeA_ShouldDeserializeCorrectly()
    {
        // Arrange: MessageType=1 (TypeA), CommonField=0xAA, FieldA=0xBB
        // Padding to 24 bits for Message field
        byte[] bytes = { 0x01, 0xAA, 0xBB, 0x00 };

        // Act
        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
        result.MessageType.ShouldBe((byte)1);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldBeOfType<MessageTypeA>();
        result.Message.CommonField.ShouldBe((byte)0xAA);
        ((MessageTypeA)result.Message).FieldA.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Deserialize_PolymorphicTypeB_ShouldDeserializeCorrectly()
    {
        // Arrange: MessageType=2 (TypeB), CommonField=0xCC, FieldB=0x1234
        byte[] bytes = { 0x02, 0xCC, 0x12, 0x34 };

        // Act
        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
        result.MessageType.ShouldBe((byte)2);
        result.Message.ShouldNotBeNull();
        result.Message.ShouldBeOfType<MessageTypeB>();
        result.Message.CommonField.ShouldBe((byte)0xCC);
        ((MessageTypeB)result.Message).FieldB.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void Deserialize_PolymorphicTypeC_ShouldDeserializeCorrectly()
    {
        // Arrange: MessageType=3 (TypeC), CommonField=0xDD, FieldC1=0xEE, FieldC2=0xFF
        byte[] bytes = { 0x03, 0xDD, 0xEE, 0xFF };

        // Act
        var result = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
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
        // Arrange: MessageType=2 (TypeB has max length: 8+16=24 bits)
        // CommonField=0x11, FieldB=0x2233
        byte[] bytes = { 0x02, 0x11, 0x22, 0x33 };

        // Act
        var result = BitSerializerMSB.Deserialize<PolymorphicContainerAutoLength>(bytes);

        // Assert
        result.MessageType.ShouldBe((byte)2);
        result.Message.ShouldBeOfType<MessageTypeB>();
        result.Message.CommonField.ShouldBe((byte)0x11);
        ((MessageTypeB)result.Message).FieldB.ShouldBe((ushort)0x2233);
    }

    [Fact]
    public void Deserialize_PolymorphicAutoLength_TypeA_ShouldWork()
    {
        // Arrange: MessageType=1 (TypeA: 8+8=16 bits, but field is 24 bits from max)
        // CommonField=0x44, FieldA=0x55, padding
        byte[] bytes = { 0x01, 0x44, 0x55, 0x00 };

        // Act
        var result = BitSerializerMSB.Deserialize<PolymorphicContainerAutoLength>(bytes);

        // Assert
        result.MessageType.ShouldBe((byte)1);
        result.Message.ShouldBeOfType<MessageTypeA>();
        result.Message.CommonField.ShouldBe((byte)0x44);
        ((MessageTypeA)result.Message).FieldA.ShouldBe((byte)0x55);
    }

    [Fact]
    public void Deserialize_PolymorphicWithUnknownType_ShouldThrowException()
    {
        // Arrange: MessageType=99 (unknown type)
        byte[] bytes = { 0x63, 0x00, 0x00, 0x00 }; // 0x63 = 99

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("No polymorphic type mapping found");
        exception.Message.ShouldContain("99");
    }

    [Fact]
    public void Deserialize_PolymorphicWithoutRelatedAttribute_ShouldThrowException()
    {
        // Arrange
        byte[] bytes = { 0x01, 0x00, 0x00 };

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<InvalidPolymorphicNoRelated>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("must have BitFieldRelatedAttribute");
    }

    [Fact]
    public void Deserialize_PolymorphicCalledMultipleTimes_ShouldUseCachedMetadata()
    {
        // Arrange - different message types
        byte[] bytesTypeA = { 0x01, 0xAA, 0xBB, 0x00 };
        byte[] bytesTypeB = { 0x02, 0xCC, 0x12, 0x34 };

        // Act - Call multiple times to ensure caching works
        var result1 = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytesTypeA);
        var result2 = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytesTypeB);

        // Assert
        result1.Message.ShouldBeOfType<MessageTypeA>();
        result2.Message.ShouldBeOfType<MessageTypeB>();
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void Serialize_SimpleData_ShouldSerializeCorrectly()
    {
        // Arrange
        var data = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert
        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)0xAB);
        bytes[1].ShouldBe((byte)0x12);
        bytes[2].ShouldBe((byte)0x34);
        bytes[3].ShouldBe((byte)0xCD);
    }

    [Fact]
    public void SerializeDeserialize_SimpleData_ShouldRoundTrip()
    {
        // Arrange
        var original = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<SimpleData>(bytes);

        // Assert
        deserialized.Header.ShouldBe(original.Header);
        deserialized.Value.ShouldBe(original.Value);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void Serialize_CustomBitLengthData_ShouldHandleNonByteAlignedBits()
    {
        // Arrange
        var data = new CustomBitLengthData
        {
            NibbleHigh = 0xA,
            NibbleLow = 0x5,
            TwelveBits = 0x678,
            FourBits = 0x0
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert
        bytes.Length.ShouldBe(3);
        bytes[0].ShouldBe((byte)0xA5);
        bytes[1].ShouldBe((byte)0x67);
        bytes[2].ShouldBe((byte)0x80);
    }

    [Fact]
    public void SerializeDeserialize_CustomBitLengthData_ShouldRoundTrip()
    {
        // Arrange
        var original = new CustomBitLengthData
        {
            NibbleHigh = 0xA,
            NibbleLow = 0x5,
            TwelveBits = 0x678,
            FourBits = 0x0
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<CustomBitLengthData>(bytes);

        // Assert
        deserialized.NibbleHigh.ShouldBe(original.NibbleHigh);
        deserialized.NibbleLow.ShouldBe(original.NibbleLow);
        deserialized.TwelveBits.ShouldBe(original.TwelveBits);
    }

    [Fact]
    public void SerializeDeserialize_EnumData_ShouldRoundTrip()
    {
        // Arrange
        var original = new EnumData
        {
            Status = TestStatus.Active,
            Code = 0x1234
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<EnumData>(bytes);

        // Assert
        deserialized.Status.ShouldBe(original.Status);
        deserialized.Code.ShouldBe(original.Code);
    }

    [Fact]
    public void SerializeDeserialize_NestedData_ShouldRoundTrip()
    {
        // Arrange
        var original = new NestedData
        {
            Header = 0xAA,
            Inner = new InnerData { X = 0x11, Y = 0x22 },
            Footer = 0xBB
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<NestedData>(bytes);

        // Assert
        deserialized.Header.ShouldBe(original.Header);
        deserialized.Inner.X.ShouldBe(original.Inner.X);
        deserialized.Inner.Y.ShouldBe(original.Inner.Y);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_ListData_ShouldRoundTrip()
    {
        // Arrange
        var original = new ListData
        {
            Count = 3,
            Items = new List<byte> { 0x11, 0x22, 0x33 }
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListData>(bytes);

        // Assert
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
        // Arrange
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

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListData>(bytes);

        // Assert
        deserialized.Count.ShouldBe((byte)original.Items.Count);
        deserialized.Reserved.ShouldBe(original.Reserved);
        deserialized.Items.ShouldBe(original.Items);
    }

    [Fact]
    public void SerializeDeserialize_ListNestedData_ShouldRoundTrip()
    {
        // Arrange
        var original = new ListNestedData
        {
            Count = 2,
            Items = new List<InnerData>
            {
                new InnerData { X = 0x11, Y = 0x22 },
                new InnerData { X = 0x33, Y = 0x44 }
            }
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<ListNestedData>(bytes);

        // Assert
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
        // Arrange
        var original = new PolymorphicContainer
        {
            MessageType = 1,
            Message = new MessageTypeA { CommonField = 0xAA, FieldA = 0xBB }
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
        deserialized.MessageType.ShouldBe(original.MessageType);
        deserialized.Message.ShouldBeOfType<MessageTypeA>();
        deserialized.Message.CommonField.ShouldBe(original.Message.CommonField);
        ((MessageTypeA)deserialized.Message).FieldA.ShouldBe(((MessageTypeA)original.Message).FieldA);
    }

    [Fact]
    public void SerializeDeserialize_PolymorphicTypeB_ShouldRoundTrip()
    {
        // Arrange
        var original = new PolymorphicContainer
        {
            MessageType = 2,
            Message = new MessageTypeB { CommonField = 0xCC, FieldB = 0x1234 }
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
        deserialized.MessageType.ShouldBe(original.MessageType);
        deserialized.Message.ShouldBeOfType<MessageTypeB>();
        deserialized.Message.CommonField.ShouldBe(original.Message.CommonField);
        ((MessageTypeB)deserialized.Message).FieldB.ShouldBe(((MessageTypeB)original.Message).FieldB);
    }

    [Fact]
    public void SerializeDeserialize_PolymorphicTypeC_ShouldRoundTrip()
    {
        // Arrange
        var original = new PolymorphicContainer
        {
            MessageType = 3,
            Message = new MessageTypeC { CommonField = 0xDD, FieldC1 = 0xEE, FieldC2 = 0xFF }
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<PolymorphicContainer>(bytes);

        // Assert
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
        // Arrange
        var original = new AutoBitLengthData
        {
            ByteValue = 0x12,
            UShortValue = 0x3456,
            IntValue = 0x789ABCDE
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<AutoBitLengthData>(bytes);

        // Assert
        deserialized.ByteValue.ShouldBe(original.ByteValue);
        deserialized.UShortValue.ShouldBe(original.UShortValue);
        deserialized.IntValue.ShouldBe(original.IntValue);
    }

    [Fact]
    public void SerializeDeserialize_DataWithIgnored_ShouldIgnoreMarkedFields()
    {
        // Arrange
        var original = new DataWithIgnored
        {
            Value = 0xAA,
            Description = "This should be ignored",
            AnotherValue = 0xBB
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithIgnored>(bytes);

        // Assert
        deserialized.Value.ShouldBe(original.Value);
        deserialized.AnotherValue.ShouldBe(original.AnotherValue);
        deserialized.Description.ShouldBeEmpty(); // Ignored field should be default value
    }

    [Fact]
    public void Serialize_WithSpanOverload_ShouldWork()
    {
        // Arrange
        var data = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };
        var bytes = new byte[4];
        Span<byte> span = bytes;

        // Act
        BitSerializerMSB.Serialize(data, span);

        // Assert
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
        // Arrange
        var data = new SimpleData2
        {
            Header = 0xA,      // 4 bits: 1010
            Value = 0x58,       // 7 bits: 1011000
            Footer = 0x12       // 5 bits: 10010
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert - 4+7+5=16 bits = 2 bytes
        bytes.Length.ShouldBe(2);
        bytes[0].ShouldBe((byte)0xAB); // 1010 1011
        bytes[1].ShouldBe((byte)0x12); // 000 10010
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
        bytes[0].ShouldBe((byte)0x03); // Error = 3
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

        bytes[0].ShouldBe((byte)0x30); // Count=3 (4 bits) + Reserved=0 (4 bits)
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
        bytes[0].ShouldBe((byte)0x0F); // Count=0 (4 bits) + Reserved=0xF (4 bits)
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

        bytes[0].ShouldBe((byte)0x01); // MessageType
        bytes[1].ShouldBe((byte)0xAA); // CommonField
        bytes[2].ShouldBe((byte)0xBB); // FieldA
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

        bytes[0].ShouldBe((byte)0x02); // MessageType
        bytes[1].ShouldBe((byte)0xCC); // CommonField
        bytes[2].ShouldBe((byte)0x12); // FieldB high
        bytes[3].ShouldBe((byte)0x34); // FieldB low
    }

    [Fact]
    public void Serialize_WithSpanTooSmall_ShouldThrowException()
    {
        var data = new SimpleData
        {
            Header = 0xAB,
            Value = 0x1234,
            Footer = 0xCD
        };
        var bytes = new byte[2]; // Too small, need 4 bytes

        Should.Throw<ArgumentException>(() =>
        {
            Span<byte> span = bytes;
            BitSerializerMSB.Serialize(data, span);
        });
    }

    #endregion

    #region Signed and Larger Numeric Type Tests

    public class SignedByteData
    {
        [BitField]
        public sbyte Value1 { get; set; }

        [BitField]
        public sbyte Value2 { get; set; }
    }

    public class ShortData
    {
        [BitField]
        public short SignedValue { get; set; }

        [BitField]
        public ushort UnsignedValue { get; set; }
    }

    public class IntData
    {
        [BitField]
        public int SignedValue { get; set; }

        [BitField]
        public uint UnsignedValue { get; set; }
    }

    public class LongData
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

    public class EnumCustomBitLengthData
    {
        [BitField(4)]
        public TestStatus Status { get; set; } // 4-bit enum

        [BitField(4)]
        public byte Padding { get; set; }
    }

    public class EnumUShortData
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
            Value1 = -128, // sbyte.MinValue
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
            Status = TestStatus.Error, // value 3, fits in 4 bits
            Padding = 0xF
        };

        var bytes = BitSerializerMSB.Serialize(original);
        bytes.Length.ShouldBe(1); // 4+4 = 8 bits = 1 byte
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

    /// <summary>
    /// Converter that adds an offset of 10 during deserialization and subtracts 10 during serialization.
    /// E.g., raw byte 0x05 -> deserialized value 15; property value 15 -> serialized byte 0x05
    /// </summary>
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

    /// <summary>
    /// Converter that inverts bits during deserialization/serialization (XOR 0xFF).
    /// </summary>
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

    /// <summary>
    /// Converter for ushort that doubles on deserialize and halves on serialize.
    /// </summary>
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

    /// <summary>
    /// Simple data with a value converter on a primitive field.
    /// </summary>
    public class DataWithConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(8)]
        [BitFieldRelated(null, typeof(OffsetConverter))]
        public byte ConvertedValue { get; set; }

        [BitField(8)]
        public byte Footer { get; set; }
    }

    /// <summary>
    /// Data with multiple converted fields.
    /// </summary>
    public class DataWithMultipleConverters
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

    /// <summary>
    /// Data with ushort converter.
    /// </summary>
    public class DataWithUShortConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField(16)]
        [BitFieldRelated(null, typeof(DoubleConverter))]
        public ushort DoubledValue { get; set; }
    }

    /// <summary>
    /// Nested inner type with a converter on one of its fields.
    /// </summary>
    public class InnerDataWithConverter
    {
        [BitField(8)]
        [BitFieldRelated(null, typeof(OffsetConverter))]
        public byte X { get; set; }

        [BitField(8)]
        public byte Y { get; set; }
    }

    /// <summary>
    /// Container with a nested type that has converter fields.
    /// </summary>
    public class NestedDataWithConverter
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        public InnerDataWithConverter Inner { get; set; } = new();

        [BitField(8)]
        public byte Footer { get; set; }
    }

    /// <summary>
    /// A type that does NOT implement IBitFieldValueConverter - used for validation test.
    /// </summary>
    public class NotAConverter
    {
    }

    /// <summary>
    /// Invalid: uses a non-converter type as ValueConverterType.
    /// </summary>
    public class InvalidConverterTypeData
    {
        [BitField(8)]
        [BitFieldRelated(null, typeof(NotAConverter))]
        public byte Value { get; set; }
    }

    #endregion

    #region ValueConverter Deserialization Tests

    [Fact]
    public void Deserialize_WithOffsetConverter_ShouldApplyConversion()
    {
        // Arrange: Header=0xAA, raw ConvertedValue=0x05 (should become 15 after +10), Footer=0xBB
        byte[] bytes = { 0xAA, 0x05, 0xBB };

        // Act
        var result = BitSerializerMSB.Deserialize<DataWithConverter>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0xAA);
        result.ConvertedValue.ShouldBe((byte)15); // 5 + 10
        result.Footer.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Deserialize_WithMultipleConverters_ShouldApplyEachCorrectly()
    {
        // Arrange: OffsetField raw=0x0A (+10 -> 20), InvertedField raw=0xF0 (^0xFF -> 0x0F), NormalField=0x33
        byte[] bytes = { 0x0A, 0xF0, 0x33 };

        // Act
        var result = BitSerializerMSB.Deserialize<DataWithMultipleConverters>(bytes);

        // Assert
        result.OffsetField.ShouldBe((byte)20);     // 10 + 10
        result.InvertedField.ShouldBe((byte)0x0F);  // 0xF0 ^ 0xFF
        result.NormalField.ShouldBe((byte)0x33);     // no conversion
    }

    [Fact]
    public void Deserialize_WithUShortConverter_ShouldApplyConversion()
    {
        // Arrange: Header=0x01, raw DoubledValue=0x0064 (100, should become 200 after *2)
        byte[] bytes = { 0x01, 0x00, 0x64 };

        // Act
        var result = BitSerializerMSB.Deserialize<DataWithUShortConverter>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0x01);
        result.DoubledValue.ShouldBe((ushort)200); // 100 * 2
    }

    [Fact]
    public void Deserialize_NestedTypeWithConverter_ShouldApplyConversion()
    {
        // Arrange: Header=0xAA, Inner.X raw=0x05 (+10 -> 15), Inner.Y=0x22, Footer=0xBB
        byte[] bytes = { 0xAA, 0x05, 0x22, 0xBB };

        // Act
        var result = BitSerializerMSB.Deserialize<NestedDataWithConverter>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0xAA);
        result.Inner.X.ShouldBe((byte)15);  // 5 + 10
        result.Inner.Y.ShouldBe((byte)0x22);
        result.Footer.ShouldBe((byte)0xBB);
    }

    #endregion

    #region ValueConverter Serialization Tests

    [Fact]
    public void Serialize_WithOffsetConverter_ShouldApplyConversion()
    {
        // Arrange: ConvertedValue=15, should serialize as 5 (15-10)
        var data = new DataWithConverter
        {
            Header = 0xAA,
            ConvertedValue = 15,
            Footer = 0xBB
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x05); // 15 - 10
        bytes[2].ShouldBe((byte)0xBB);
    }

    [Fact]
    public void Serialize_WithMultipleConverters_ShouldApplyEachCorrectly()
    {
        // Arrange
        var data = new DataWithMultipleConverters
        {
            OffsetField = 20,     // should serialize as 10 (20-10)
            InvertedField = 0x0F, // should serialize as 0xF0 (0x0F ^ 0xFF)
            NormalField = 0x33
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert
        bytes[0].ShouldBe((byte)0x0A); // 20 - 10
        bytes[1].ShouldBe((byte)0xF0); // 0x0F ^ 0xFF
        bytes[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Serialize_NestedTypeWithConverter_ShouldApplyConversion()
    {
        // Arrange
        var data = new NestedDataWithConverter
        {
            Header = 0xAA,
            Inner = new InnerDataWithConverter { X = 15, Y = 0x22 }, // X should serialize as 5
            Footer = 0xBB
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(data);

        // Assert
        bytes[0].ShouldBe((byte)0xAA);
        bytes[1].ShouldBe((byte)0x05); // 15 - 10
        bytes[2].ShouldBe((byte)0x22);
        bytes[3].ShouldBe((byte)0xBB);
    }

    #endregion

    #region ValueConverter RoundTrip Tests

    [Fact]
    public void SerializeDeserialize_WithConverter_ShouldRoundTrip()
    {
        // Arrange
        var original = new DataWithConverter
        {
            Header = 0xAA,
            ConvertedValue = 25,
            Footer = 0xBB
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithConverter>(bytes);

        // Assert
        deserialized.Header.ShouldBe(original.Header);
        deserialized.ConvertedValue.ShouldBe(original.ConvertedValue);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_WithMultipleConverters_ShouldRoundTrip()
    {
        // Arrange
        var original = new DataWithMultipleConverters
        {
            OffsetField = 30,
            InvertedField = 0xAB,
            NormalField = 0x77
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithMultipleConverters>(bytes);

        // Assert
        deserialized.OffsetField.ShouldBe(original.OffsetField);
        deserialized.InvertedField.ShouldBe(original.InvertedField);
        deserialized.NormalField.ShouldBe(original.NormalField);
    }

    [Fact]
    public void SerializeDeserialize_NestedWithConverter_ShouldRoundTrip()
    {
        // Arrange
        var original = new NestedDataWithConverter
        {
            Header = 0x11,
            Inner = new InnerDataWithConverter { X = 50, Y = 0x99 },
            Footer = 0x22
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<NestedDataWithConverter>(bytes);

        // Assert
        deserialized.Header.ShouldBe(original.Header);
        deserialized.Inner.X.ShouldBe(original.Inner.X);
        deserialized.Inner.Y.ShouldBe(original.Inner.Y);
        deserialized.Footer.ShouldBe(original.Footer);
    }

    [Fact]
    public void SerializeDeserialize_WithUShortConverter_ShouldRoundTrip()
    {
        // Arrange
        var original = new DataWithUShortConverter
        {
            Header = 0x01,
            DoubledValue = 500
        };

        // Act
        var bytes = BitSerializerMSB.Serialize(original);
        var deserialized = BitSerializerMSB.Deserialize<DataWithUShortConverter>(bytes);

        // Assert
        deserialized.Header.ShouldBe(original.Header);
        deserialized.DoubledValue.ShouldBe(original.DoubledValue);
    }

    #endregion

    #region ValueConverter Validation Tests

    [Fact]
    public void Deserialize_WithInvalidConverterType_ShouldThrowException()
    {
        // Arrange
        byte[] bytes = { 0x01 };

        // Act & Assert
        var action = () => BitSerializerMSB.Deserialize<InvalidConverterTypeData>(bytes);
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain("must implement IBitFieldValueConverter");
    }

    #endregion

    #region BitFiledCountAttribute Models

    /// <summary>
    /// Test class with fixed count list (no related count field needed)
    /// </summary>
    public class FixedCountListData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFiledCount(3)]
        public List<byte> Items { get; set; } = new();
    }

    /// <summary>
    /// Test class with fixed count list of nested types
    /// </summary>
    public class FixedCountNestedListData
    {
        [BitField(8)]
        public byte Header { get; set; }

        [BitField]
        [BitFiledCount(2)]
        public List<InnerData> Items { get; set; } = new();
    }

    /// <summary>
    /// Test class where BitFiledCountAttribute coexists with BitFieldRelatedAttribute.
    /// BitFiledCountAttribute should take priority.
    /// </summary>
    public class FixedCountPriorityData
    {
        [BitField(8)]
        public byte Count { get; set; }

        [BitField]
        [BitFiledCount(2)]
        [BitFieldRelated(nameof(Count))]
        public List<byte> Items { get; set; } = new();
    }

    /// <summary>
    /// Test class with 4-bit element fixed count list
    /// </summary>
    public class FixedCountCustomBitData
    {
        [BitField(4)]
        public byte Prefix { get; set; }

        [BitField(4)]
        [BitFiledCount(3)]
        public List<byte> Nibbles { get; set; } = new();
    }

    #endregion

    #region BitFiledCountAttribute Deserialization Tests

    [Fact]
    public void Deserialize_FixedCountList_ShouldDeserializeCorrectly()
    {
        // Arrange: Header=0xAA, Items=[0x11, 0x22, 0x33] (fixed count=3)
        byte[] bytes = { 0xAA, 0x11, 0x22, 0x33 };

        // Act
        var result = BitSerializerMSB.Deserialize<FixedCountListData>(bytes);

        // Assert
        result.Header.ShouldBe((byte)0xAA);
        result.Items.Count.ShouldBe(3);
        result.Items[0].ShouldBe((byte)0x11);
        result.Items[1].ShouldBe((byte)0x22);
        result.Items[2].ShouldBe((byte)0x33);
    }

    [Fact]
    public void Deserialize_FixedCountNestedList_ShouldDeserializeCorrectly()
    {
        // Arrange: Header=0x01, Items=[{X=0x11,Y=0x22}, {X=0x33,Y=0x44}] (fixed count=2)
        byte[] bytes = { 0x01, 0x11, 0x22, 0x33, 0x44 };

        // Act
        var result = BitSerializerMSB.Deserialize<FixedCountNestedListData>(bytes);

        // Assert
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
        // Arrange: Count=5 (should be ignored), Items=[0xAA, 0xBB] (fixed count=2 takes priority)
        byte[] bytes = { 0x05, 0xAA, 0xBB };

        // Act
        var result = BitSerializerMSB.Deserialize<FixedCountPriorityData>(bytes);

        // Assert
        result.Count.ShouldBe((byte)5);
        result.Items.Count.ShouldBe(2); // Fixed count=2, not Count field value=5
        result.Items[0].ShouldBe((byte)0xAA);
        result.Items[1].ShouldBe((byte)0xBB);
    }

    #endregion

    #region BitFiledCountAttribute Serialization Tests

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

    #region BitFiledCountAttribute RoundTrip Tests

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
}
