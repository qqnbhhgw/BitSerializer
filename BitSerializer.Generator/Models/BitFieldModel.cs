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

    // v0.12.0: A field can now carry a SECOND [BitFieldRelated] binding with a different
    // RelationKind than the primary. The canonical use case is a polymorphic field that needs
    // BOTH a discriminator (RelationKind=Count, the primary) AND a byte-length carrier
    // (RelationKind=ByteLength, the secondary) — wire reader uses the discriminator to pick
    // the concrete poly type and the byte budget to know how many bytes to consume.
    // Slot is empty (null / 0) when only one [BitFieldRelated] is declared, preserving
    // backward compatibility with all v0.11.x and earlier consumers.
    public string? SecondaryRelatedMemberName { get; set; }
    public int SecondaryRelationKind { get; set; }

    /// <summary>
    /// Cached primitive type name of the SECONDARY [BitFieldRelated] carrier (e.g. "byte", "sbyte",
    /// "ushort"). Codex review round-2 P1: the deserializer must reinterpret signed carriers as
    /// their unsigned twin before widening to long (an 8-bit signed carrier holding the wire value
    /// 0xC8 reads as -56 and trips the post-deserialize byte-length verify; casting (byte)(sbyte) →
    /// byte 200 → long 200 recovers the unsigned interpretation). Mirrors LengthFieldTypeName.
    /// </summary>
    public string? SecondaryRelatedFieldTypeName { get; set; }
    /// <summary>Bit width of the secondary carrier (mirrors LengthFieldBitWidth; needed for overflow guards).</summary>
    public int SecondaryRelatedFieldBitWidth { get; set; }

    /// <summary>
    /// Optional ValueConverter attached to the SECONDARY [BitFieldRelated] binding (almost always
    /// a length converter for the ByteLength carrier — protocols that wire-encode length with an
    /// offset / shift / scale).
    /// Codex review round-2 P2: previously the analyzer always copied the primary (Count) binding's
    /// converter and silently dropped any converter declared on the secondary (ByteLength) binding,
    /// so protocols that encode length with a transform produced incompatible wire values while the
    /// attribute was accepted.
    /// </summary>
    public string? SecondaryValueConverterTypeFullName { get; set; }
    public bool SecondaryValueConverterHasSerialize { get; set; }
    public bool SecondaryValueConverterHasDeserialize { get; set; }
    public bool SecondaryValueConverterSerializeHasContext { get; set; }
    public bool SecondaryValueConverterDeserializeHasContext { get; set; }

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
    /// <summary>
    /// True if the user wrote the constant as a `ulong` literal whose value exceeds long.MaxValue
    /// (e.g. `0x8000_0000_0000_0000UL` or `ulong.MaxValue`). Such constants only have a meaningful
    /// representation in 64 bits — they MUST be on a BitLength=64 field, otherwise the serializer
    /// would silently truncate top bits and deserialize verification would always fail (BITS043).
    /// Tracked separately from the long-stored bit pattern because once we unchecked-cast to long
    /// the original "high-bit-only-because-unsigned" intent is indistinguishable from a signed -1.
    /// </summary>
    public bool ConstantValueIsUnsignedOverflow { get; set; }

    // True when this is a nested [BitSerialize] / manual IBitSerializable carrier whose underlying
    // type has dynamic-length content (terminated string, polymorphic auto-length, dynamic list, …),
    // even if the field overrides BitLength via explicit `[BitField(N)]`. BITS023 warns about this
    // pinning at the field level; BITS055 uses this flag to *reject* the same pattern when the
    // field is also a ByteLength carrier (auto-backfill would write N/8 instead of the runtime
    // size, while the nested deserializer's consumedBits-vs-budget assertion would fail at
    // round-trip).
    public bool NestedTypeHasDynamicContent { get; set; }

    /// <summary>
    /// True when the field's nested-type member is a reference type (or a generic type parameter
    /// with a `class` constraint) — i.e. comparing it to <c>null</c> or assigning <c>null</c> to it
    /// compiles. False for struct nested types and unconstrained / struct-constrained type
    /// parameters. Codex review round-7 P1: the ByteLength nested-carrier emit paths emit
    /// <c>if (memberAccess != null)</c> on the serializer and <c>memberAccess = null;</c> on the
    /// deserializer; both produce CS0019/CS0037/CS0403 against non-reference carriers. Defaults to
    /// true so non-nested fields and types that never reach the null-guarded path keep the existing
    /// emit shape.
    /// </summary>
    public bool NestedIsReferenceType { get; set; } = true;

    /// <summary>
    /// True when this model was synthesized from an ancestor [BitSerialize] class's [BitField]
    /// member rather than declared in the current type. v0.12.1: lookup helpers fall back to
    /// inherited fields so cross-inheritance [BitFieldRelated(nameof(BaseField))] references
    /// resolve at analysis time the same way generated `this.BaseField` reads resolve at runtime
    /// via C# property inheritance. Inherited fields are NOT iterated by the emit main loops
    /// (the ancestor's generated Serialize/Deserialize handles them via base.* calls), so they
    /// carry only the metadata needed by carrier-lookup callers (MemberName, MemberType*, BitLength,
    /// IsEnum/EnumUnderlyingTypeName, HasConstantValue, category flags); fields like BitStartIndex
    /// are not meaningful in the derived type's frame and stay zero.
    /// </summary>
    public bool IsInherited { get; set; }

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
