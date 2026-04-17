using System.Collections.Generic;

namespace BitSerializer.Generator.Models;

internal class PolyMapping
{
    public int TypeId { get; set; }
    public string ConcreteTypeName { get; set; } = "";
    public string ConcreteTypeFullName { get; set; } = "";
}

internal class BitFieldModel
{
    public string MemberName { get; set; } = "";
    public string MemberTypeName { get; set; } = "";
    public string MemberTypeFullName { get; set; } = "";
    public bool IsProperty { get; set; }
    public int BitStartIndex { get; set; }
    public int BitLength { get; set; }
    public bool IsNumericOrEnum { get; set; }
    public bool IsEnum { get; set; }
    public string? EnumUnderlyingTypeName { get; set; }
    public bool IsList { get; set; }
    public bool IsArray { get; set; }
    public string? ListElementTypeName { get; set; }
    public string? ListElementTypeFullName { get; set; }
    public int ListElementBitLength { get; set; }
    public bool ListElementIsNested { get; set; }
    public int? FixedCount { get; set; }
    public string? RelatedMemberName { get; set; }
    public bool IsNestedType { get; set; }
    public bool IsTypeParameter { get; set; }
    public bool IsPolymorphic { get; set; }
    public List<PolyMapping>? PolyMappings { get; set; }
    public string? ValueConverterTypeFullName { get; set; }
    public bool ValueConverterHasSerialize { get; set; }
    public bool ValueConverterHasDeserialize { get; set; }
    public bool ValueConverterSerializeHasContext { get; set; }
    public bool ValueConverterDeserializeHasContext { get; set; }
    public int PolymorphicBitLength { get; set; }
    public bool IsPotentiallyDynamic { get; set; }

    // String support
    public bool IsFixedString { get; set; }
    public int FixedStringByteLength { get; set; }
    public bool IsTerminatedString { get; set; }
    public string StringEncodingName { get; set; } = "ASCII";

    // Manual IBitSerializable support (without [BitSerialize])
    public bool IsManualBitSerializable { get; set; }

    // List element is manual IBitSerializable (needs interface dispatch)
    public bool ListElementIsManualBitSerializable { get; set; }

    // List element is a generic type parameter (needs Activator.CreateInstance)
    public bool ListElementIsTypeParameter { get; set; }

    // List element is [BitSerialize] with dynamic length (needs runtime offset tracking)
    public bool ListElementHasDynamicLength { get; set; }

    // Whether the list element type declares its own SerializeContext/DeserializeContext methods
    public bool ListElementHasOwnContext { get; set; }

    // Whether a nested (non-list) type declares its own SerializeContext/DeserializeContext methods
    public bool NestedHasOwnContext { get; set; }

    // CRC result field ([BitCrc]): this field will be auto-filled after serialization
    public bool IsCrcResult { get; set; }
    public string? CrcAlgorithmTypeFullName { get; set; }
    public ulong CrcInitialValue { get; set; }
    public bool CrcValidateOnDeserialize { get; set; }

    // CRC include ([BitCrcInclude]): this field participates in a CRC calculation
    public string? CrcTargetFieldName { get; set; }

    // [BitFieldCount(N, PadIfShort=true)]: serialize always writes N, deserialize pads short data
    public bool PadIfShort { get; set; }

    // [BitFieldConsumeRemaining]: dynamic-length list/array consuming remaining bytes
    public bool ConsumeRemaining { get; set; }

    // [BitFieldRelated(RelationKind = ...)]: Count (default) = element count; ByteLength = byte budget.
    // Stored as int to avoid a Generator-side reference to the runtime enum; matches BitRelationKind values.
    public int RelationKind { get; set; }
}
