using System.Collections.Generic;

namespace BitSerializer.Generator.Models;

internal class PolyMapping
{
    public int TypeId { get; set; }
    public string ConcreteTypeName { get; set; } = "";
    public string ConcreteTypeFullName { get; set; } = "";
    /// <summary>
    /// True if the concrete [BitPoly] target type transitively contains a [BitField(Endian = ...)]
    /// override. Computed during field analysis from the actual ITypeSymbol so it sees types in
    /// referenced assemblies, not just the current ContainingAssembly (review round-12 P1).
    /// </summary>
    public bool HasEndianOverride { get; set; }
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

    // [BitFixedString(..., Padding = 0xNN)]: padding byte for short serialize / trim on deserialize.
    public byte FixedStringPadding { get; set; }

    // [BitLengthPrefixString]: encoded as <LengthBits>-bit byte-count + raw encoded bytes (no terminator)
    public bool IsLengthPrefixString { get; set; }
    public int LengthPrefixBits { get; set; }
    public int LengthPrefixMaxBytes { get; set; } // 0 = unlimited

    // [BitLengthFieldString(nameof(NameLength))]: string whose byte count lives in a SEPARATE
    // numeric field declared earlier in the type. Serializer auto-backfills the referenced field
    // with the encoded byte count; deserializer reads the count from `this.<LengthFieldMemberName>`
    // (already populated by an earlier field) and reads that many bytes.
    public bool IsLengthFieldString { get; set; }
    public string? LengthFieldMemberName { get; set; }
    public int LengthFieldMaxBytes { get; set; } // 0 = unlimited
    /// <summary>Cached bit width of the referenced length field; needed by the serializer's overflow check.</summary>
    public int LengthFieldBitWidth { get; set; }
    /// <summary>Cached primitive type name of the referenced length field (e.g. "byte", "ushort"). Used for casts.</summary>
    public string LengthFieldTypeName { get; set; } = "";

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

    // [BitCrc] WholeBuffer mode: CRC covers the entire type buffer, no [BitCrcInclude] required.
    public bool CrcWholeBuffer { get; set; }
    public int CrcSkipHeadBytes { get; set; }
    public int CrcSkipTailBytes { get; set; }

    // CRC include ([BitCrcInclude]): this field participates in a CRC calculation
    public string? CrcTargetFieldName { get; set; }

    // [BitFieldCount(N, PadIfShort=true)]: serialize always writes N, deserialize pads short data
    public bool PadIfShort { get; set; }

    // [BitFieldConsumeRemaining]: dynamic-length list/array consuming remaining bytes
    public bool ConsumeRemaining { get; set; }

    // [BitFieldRelated(RelationKind = ...)]: Count (default) = element count; ByteLength = byte budget.
    // Stored as int to avoid a Generator-side reference to the runtime enum; matches BitRelationKind values.
    public int RelationKind { get; set; }

    // [BitField(Endian = ...)]: 0=Inherit (follow outer method), 1=Big (force MSB), 2=Little (force LSB).
    // Stored as int to avoid a Generator-side reference to the runtime BitEndian enum.
    // Only meaningful for numeric/enum fields that are byte-aligned with bit width ∈ {8,16,32,64}.
    public int Endian { get; set; }

    // [BitFieldValue(constant, Verify=true)]: pin a constant value to a numeric/enum scalar field.
    // Serializer writes the constant to wire (and back to the property); deserializer reads + optionally
    // verifies. Only meaningful for numeric/enum scalars (BITS042). Stored as long to fit any integer
    // type up to ulong (which is rejected if it exceeds long.MaxValue; in practice protocol magic numbers
    // never need values > 2^63).
    public bool HasConstantValue { get; set; }
    public long ConstantValue { get; set; }
    public bool ConstantValueVerify { get; set; } = true;

    // True if the field's nested type (direct nested OR list element type) transitively contains
    // a [BitField(Endian = ...)] override. Computed during this field's analysis using the actual
    // ITypeSymbol, so types in referenced assemblies are seen too — review round-12 P1 closed the
    // ContainingAssembly-only gap in FindNestedEndianTypeForField. Polymorphic mappings carry their
    // own per-target flag in PolyMapping.HasEndianOverride.
    public bool NestedHasEndianOverride { get; set; }
    /// <summary>"nested member" / "list element" — human-readable culprit kind for diagnostics.</summary>
    public string? NestedEndianKind { get; set; }
    /// <summary>Fully qualified name of the nested/element type that carries the Endian override (for diagnostic text).</summary>
    public string? NestedEndianTypeFullName { get; set; }
}
