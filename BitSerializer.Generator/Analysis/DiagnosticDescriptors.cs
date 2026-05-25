using Microsoft.CodeAnalysis;

namespace BitSerializer.Generator.Analysis;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingBitFieldOrIgnore = new(
        "BITS001",
        "Missing [BitField] or [BitIgnore]",
        "Member '{0}' in type '{1}' must have [BitField] or [BitIgnore]",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "BITS002",
        "Unsupported field type",
        "Member '{0}' has unsupported type '{1}'. Use [BitIgnore] to exclude.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ListMissingCountInfo = new(
        "BITS003",
        "List missing count info",
        "List member '{0}' requires [BitFieldRelated] or [BitFieldCount]",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidValueConverter = new(
        "BITS004",
        "Invalid value converter",
        "ValueConverterType '{0}' must implement IBitFieldValueConverter",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MustBePartial = new(
        "BITS005",
        "Type must be partial",
        "Type '{0}' must be declared partial to use [BitSerialize]",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NestedTypeMustBeBitSerializable = new(
        "BITS006",
        "Nested type must be [BitSerialize]",
        "Nested type '{0}' used in '{1}' must also have [BitSerialize]",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor PolymorphicMissingRelated = new(
        "BITS007",
        "Polymorphic missing discriminator",
        "Polymorphic member '{0}' requires [BitFieldRelated] with discriminator field",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RelatedMemberNotFound = new(
        "BITS008",
        "Related member not found",
        "Related member '{0}' not found in type '{1}'",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MissingBitFieldWithHelperAttribute = new(
        "BITS009",
        "Missing [BitField] on member with serialization helper attribute",
        "Member '{0}' has serialization helper attribute but is missing [BitField]",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FixedStringMustBeString = new(
        "BITS010",
        "[BitFixedString] on non-string member",
        "[BitFixedString] can only be applied to string members, but '{0}' in '{1}' is not a string",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TerminatedStringMustBeString = new(
        "BITS011",
        "[BitTerminatedString] on non-string member",
        "[BitTerminatedString] can only be applied to string members, but '{0}' in '{1}' is not a string",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FixedStringInvalidLength = new(
        "BITS012",
        "[BitFixedString] byte length must be positive",
        "[BitFixedString] on '{0}' in '{1}' has byte length {2}, which must be greater than 0",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TypeParameterMissingNewConstraint = new(
        "BITS013",
        "Type parameter missing new() constraint",
        "Type parameter '{0}' used in '{1}' of '{2}' requires a new() or struct constraint for deserialization",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UninitializedReferenceField = new(
        "BITS014",
        "Reference-type [BitField] member has no default value",
        "Member '{0}' in '{1}' is a reference type without a default value; it will be null before deserialization",
        "BitSerializer",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor CrcTargetFieldNotFound = new(
        "BITS015",
        "[BitCrcInclude] target field not found or not a CRC field",
        "[BitCrcInclude] on '{0}' references '{1}', which is not a valid [BitCrc] field in type '{2}'",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcAlgorithmInvalid = new(
        "BITS016",
        "[BitCrc] algorithm type does not implement IBitCrcAlgorithm",
        "[BitCrc] on '{0}' uses algorithm '{1}' which does not implement BitSerializer.IBitCrcAlgorithm or lacks a public parameterless constructor",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcIncludeRangeInvalid = new(
        "BITS017",
        "[BitCrcInclude] range is invalid",
        "CRC '{0}' in type '{1}' has invalid include range — included fields must be declared contiguously, the range must start on a byte boundary, and every included field must be byte-aligned at runtime (for polymorphic fields every [BitPoly] concrete type must have a total bit length that is a multiple of 8 and no dynamic content; for lists the element bit width must be a multiple of 8)",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcFieldMustBeByteAligned = new(
        "BITS018",
        "[BitCrc] result field must be byte-aligned",
        "[BitCrc] result field '{0}' in type '{1}' must be byte-aligned (bit offset divisible by 8) and have a bit length that is a multiple of 8",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ConsumeRemainingMustBeLast = new(
        "BITS019",
        "[BitFieldConsumeRemaining] must be on the last field with a primitive element type",
        "[BitFieldConsumeRemaining] on '{0}' in '{1}' must be the last declared [BitField] and its list/array element must be a numeric or enum type",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor PadIfShortMustBePrimitiveElement = new(
        "BITS020",
        "[BitFieldCount(PadIfShort=true)] requires a primitive element type",
        "[BitFieldCount(PadIfShort=true)] on '{0}' in '{1}' requires the list/array element to be a numeric or enum type",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcFieldCannotCombineWithConverterOrRelated = new(
        "BITS021",
        "[BitCrc] cannot combine with [BitFieldRelated] on the same field",
        "[BitCrc] field '{0}' in '{1}' also has [BitFieldRelated]; the CRC computation would overwrite the converter/related-field output. Remove one of the two attributes.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcInTypeWithDynamicBase = new(
        "BITS022",
        "[BitCrc] not allowed when the [BitSerialize] base type is dynamic-length",
        "Type '{0}' uses [BitCrc] but its [BitSerialize] base type has dynamic length (terminated string, type parameter, or dynamic list). CRC byte offsets would drift at runtime. Move the CRC into the base type, or make the base static-length.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthConverterMissingDirection = new(
        "BITS024",
        "[BitFieldRelated(RelationKind=ByteLength)] converter missing required method",
        "Member '{0}' in '{1}' uses RelationKind=ByteLength with converter '{2}', but the converter does not define both OnSerializeConvert and OnDeserializeConvert. Both directions are required to translate between collection byte length and the wire length field.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthConflictsWithFixedOrConsume = new(
        "BITS025",
        "[BitFieldRelated(RelationKind=ByteLength)] conflicts with [BitFieldCount] or [BitFieldConsumeRemaining]",
        "Member '{0}' in '{1}' declares RelationKind=ByteLength but also uses [BitFieldCount] or [BitFieldConsumeRemaining]. These drive the collection length through different mechanisms and cannot be combined.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthElementNotByteAligned = new(
        "BITS026",
        "[BitFieldRelated(RelationKind=ByteLength)] element bit width must be a positive multiple of 8",
        "Member '{0}' in '{1}' uses RelationKind=ByteLength but its element bit width ({2}) is not a positive multiple of 8. Byte-budget driven collections require byte-aligned elements with a non-zero size so that the budget can be divided into whole elements; zero-sized elements would cause the deserialization loop to spin forever.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthRequiresListOrArray = new(
        "BITS027",
        "[BitFieldRelated(RelationKind=ByteLength)] can only be applied to a list or array",
        "Member '{0}' in '{1}' declares RelationKind=ByteLength but is not a list or array. Byte-length driven length is only meaningful for collections.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ExplicitBitLengthOnVariableContent = new(
        "BITS023",
        "[BitField(N)] pins a fixed slot on content whose runtime size can vary",
        "Member '{0}' in '{1}' is declared with an explicit bit length of {2}, but its runtime content is {3}. The slot stays exactly {2} bits regardless of the actual object, so the serialized byte[] will contain trailing zero padding when the object is smaller, and will corrupt subsequent fields if the object is larger. Remove the explicit bit length to let GetTotalBitLength use the runtime size, or keep it only if you truly want a fixed slot.",
        "BitSerializer",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor FieldEndianRequiresByteAlignedScalar = new(
        "BITS028",
        "[BitField(Endian = ...)] requires a byte-aligned scalar field",
        "Member '{0}' in '{1}' specifies Endian = {2}, but field-level endianness only applies to numeric/enum scalars with byte-aligned offset (BitStartIndex % 8 == 0) and byte-multiple width (BitLength ∈ {{8,16,32,64}}). This field has BitStartIndex = {3}, BitLength = {4}, IsList = {5}, IsString = {6}, IsNested = {7}. Remove the Endian argument, or restructure the field so it lands on a byte boundary with a byte-multiple width.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcWholeBufferConflictsWithInclude = new(
        "BITS029",
        "[BitCrc(WholeBuffer = true)] cannot combine with [BitCrcInclude]",
        "[BitCrc(WholeBuffer = true)] on '{0}' in '{1}' is in WholeBuffer mode, but type '{1}' also has {2} field(s) marked with [BitCrcInclude] targeting this CRC. The two modes are mutually exclusive: WholeBuffer covers the entire type buffer (skipping SkipHead/SkipTail bytes), while [BitCrcInclude] declares an explicit range. Remove either WholeBuffer or the [BitCrcInclude] markers.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcWholeBufferSkipNegative = new(
        "BITS030",
        "[BitCrc] SkipHeadBytes / SkipTailBytes must be non-negative",
        "[BitCrc] on '{0}' in '{1}' has SkipHeadBytes = {2}, SkipTailBytes = {3}. Both must be ≥ 0. Also note: SkipTailBytes should cover at least the CRC field's own byte width plus any trailing frame fields — otherwise the CRC will read its own value back, producing unstable output.",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthPrefixStringMustBeString = new(
        "BITS031",
        "[BitLengthPrefixString] on non-string member",
        "[BitLengthPrefixString] can only be applied to string members, but '{0}' in '{1}' is not a string",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthPrefixStringInvalidBits = new(
        "BITS032",
        "[BitLengthPrefixString] LengthBits must be 8, 16, or 32",
        "[BitLengthPrefixString] on '{0}' in '{1}' has LengthBits = {2}; only 8, 16, or 32 are supported, matching the byte/ushort/uint length-prefix patterns used by typical protocols",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldEndianAfterDynamicContent = new(
        "BITS033",
        "[BitField(Endian = ...)] cannot follow a dynamic-length member",
        "Member '{0}' in '{1}' specifies Endian = {2}, but a preceding member ('{3}') has runtime-variable bit length (dynamic list, terminated string, type parameter, auto-length polymorphic, or dynamic [BitSerialize] base). The runtime bit offset of '{0}' can drift to a non-byte boundary depending on the previous content, in which case switching to BitHelperMSB/LSB would no longer represent a byte-order flip and could corrupt values. Place the Endian-overridden field before any dynamic-length member, or remove the Endian argument",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcWholeBufferDoesNotCoverCrcField = new(
        "BITS034",
        "[BitCrc(WholeBuffer = true)] SkipHead/SkipTail must cover the CRC field bytes",
        "[BitCrc(WholeBuffer = true)] on '{0}' in '{1}' has SkipHeadBytes = {2}, SkipTailBytes = {3}, but the CRC field occupies bytes [{4}, {5}) in the static {6}-byte layout. The CRC slot must fall entirely inside either [0, SkipHeadBytes) or [totalBytes - SkipTailBytes, totalBytes) so the CRC computation does not read the field's own (uninitialized or stale) bytes. SkipTailBytes must be ≥ {7} for a tail-positioned CRC, or SkipHeadBytes ≥ {5} for a head-positioned CRC",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor CrcWholeBufferTrailingDynamicField = new(
        "BITS035",
        "[BitCrc(WholeBuffer = true)] CRC slot is not provably excluded at runtime",
        "[BitCrc(WholeBuffer = true)] on '{0}' in '{1}' relies on a static SkipHead/SkipTail check, but member '{2}' has runtime-variable bit length (dynamic list, terminated string, type parameter, auto-length polymorphic, or fixed-count list of manual IBitSerializable elements without an explicit element bit width) and would shift the CRC slot off the protected side at runtime. To make a WholeBuffer layout sound, either (a) place all dynamic fields AFTER the CRC with SkipHeadBytes large enough to cover the CRC slot, or (b) place all dynamic fields BEFORE the CRC with SkipTailBytes large enough to cover the CRC slot and any trailing static fields. Mixed leading and trailing dynamic content around the CRC is not supported — switch to IncludeRange ([BitCrcInclude]) mode for those layouts",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthPrefixStringNegativeMaxBytes = new(
        "BITS036",
        "[BitLengthPrefixString] MaxBytes must be non-negative",
        "[BitLengthPrefixString] on '{0}' in '{1}' has MaxBytes = {2}; values < 0 are not allowed. Use 0 to mean unlimited (only the LengthBits capacity caps the byte count), or a positive value to cap encoded byte count",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldEndianOutOfRange = new(
        "BITS037",
        "[BitField(Endian = ...)] value is outside the BitEndian enum range",
        "Member '{0}' in '{1}' specifies Endian = {2}, which is not a defined BitEndian value (0 = Inherit, 1 = Big, 2 = Little). C# allows casting arbitrary integers into enums (e.g. `(BitEndian)3`), but the source generator silently falls back to Inherit for unknown values, masking the typo at runtime. Use one of BitEndian.Inherit / BitEndian.Big / BitEndian.Little",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthPrefixStringNotByteAligned = new(
        "BITS038",
        "[BitLengthPrefixString] field must start on a byte boundary",
        "[BitLengthPrefixString] on '{0}' in '{1}' has BitStartIndex = {2}, which is not a multiple of 8. A length-prefix string is fundamentally a byte stream (length prefix + raw encoded bytes), so writing it across a sub-byte boundary would corrupt wire compatibility with any reader that parses byte-aligned prefix + payload. Reorder fields so this attribute lands on a byte boundary, or pad preceding sub-byte fields",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthPrefixStringAfterDynamicContent = new(
        "BITS039",
        "[BitLengthPrefixString] cannot follow a member whose runtime size may not be byte-aligned",
        "[BitLengthPrefixString] on '{0}' in '{1}' is preceded by '{2}' which has runtime-variable bit length and is not provably byte-aligned (e.g. dynamic list of sub-byte elements, type parameter, polymorphic with non-byte-aligned concrete types, or a dynamic [BitSerialize] base). The static BITS038 check assumes the field starts on a byte boundary, but at runtime '{2}' can shift the offset by a non-byte multiple, breaking wire compatibility. Move the length-prefix string before any non-byte-aligned dynamic content, or restructure the preceding member so its runtime bit count is always a multiple of 8",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NestedEndianRequiresByteAlignedOffset = new(
        "BITS040",
        "Member containing [BitField(Endian = ...)] must be placed at a byte-aligned offset",
        "Member '{0}' in '{1}' embeds the [BitSerialize] type '{2}' (as {3}), which transitively contains a [BitField(Endian = ...)] field. The static bit offset of this member is {4} (BitStartIndex % 8 == {5}){6}, so the inner type's helper would run at a non-byte-aligned absolute offset and corrupt values instead of just flipping byte order. Place this member at a byte-aligned offset (and, for lists, choose an element bit width that is a multiple of 8), or remove the Endian override on the inner field",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NestedEndianAfterDynamicContent = new(
        "BITS041",
        "Member containing [BitField(Endian = ...)] cannot follow a runtime-variable member",
        "Member '{0}' in '{1}' embeds the [BitSerialize] type '{2}' (as {3}), which transitively contains a [BitField(Endian = ...)] field. A preceding member ('{4}') has runtime-variable bit length (dynamic list, terminated string, type parameter, auto-length polymorphic, or dynamic [BitSerialize] base), so the embedded type's bit offset can drift to a non-byte boundary at runtime and the inner endian helper swap would corrupt values. Place this member before any dynamic-length member, or remove the Endian override on the inner field",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldValueRequiresNumericScalar = new(
        "BITS042",
        "[BitFieldValue] requires a numeric / enum scalar field",
        "Member '{0}' in '{1}' carries [BitFieldValue(...)] but is not a numeric / enum scalar (IsList = {2}, IsString = {3}, IsNested = {4}, IsPolymorphic = {5}, IsTypeParameter = {6}). Pinning a constant value only makes sense on a wire-level integer/enum slot. Move the attribute to a numeric / enum scalar field, or remove it",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldValueOverflowsBitLength = new(
        "BITS043",
        "[BitFieldValue] constant does not fit in the field's bit length",
        "Member '{0}' in '{1}' is declared with BitLength = {2}, but [BitFieldValue({3})] requires {4} bits to represent (range: {5}..{6}). Increase BitLength, pick a smaller constant, or remove the attribute",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldValueConflictsWithOtherFeatures = new(
        "BITS044",
        "[BitFieldValue] cannot combine with [BitCrc] / [BitFieldRelated] / [BitFieldCount] / [BitPoly]",
        "Member '{0}' in '{1}' carries [BitFieldValue] together with {2}. These features would each overwrite the wire bytes the constant is supposed to pin, producing silently wrong output. Remove either [BitFieldValue] or the conflicting attribute",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldValueInvalidType = new(
        "BITS045",
        "[BitFieldValue] only accepts integer literals or enum members",
        "Member '{0}' in '{1}' has [BitFieldValue({2})], but only integer literals (byte/sbyte/short/ushort/int/uint/long/ulong) and enum members are accepted as the pinned constant. Floats / strings / Type / arrays are not meaningful as wire-level integer values",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthFieldStringMustBeString = new(
        "BITS046",
        "[BitLengthFieldString] on non-string member",
        "[BitLengthFieldString] can only be applied to string members, but '{0}' in '{1}' is not a string. To carry a separately-declared length for a non-string payload, use [BitFieldRelated(nameof(<lengthField>))] on a List/byte[] instead",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthFieldStringMissingTarget = new(
        "BITS047",
        "[BitLengthFieldString] target field not found or not a valid length scalar",
        "[BitLengthFieldString] on '{0}' in '{1}' references '{2}', which is not declared earlier in this type as a byte-aligned numeric scalar (allowed types: byte / sbyte / short / ushort / int / uint, bit width must be one of 8/16/32). Move the length field above the string field, and ensure its type is a byte-aligned integer",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthFieldStringNotByteAligned = new(
        "BITS048",
        "[BitLengthFieldString] field must start on a byte boundary",
        "[BitLengthFieldString] on '{0}' in '{1}' has BitStartIndex = {2}, which is not a multiple of 8. A length-field string is a raw byte stream and must start on a byte boundary to keep wire compatibility with byte-oriented readers. Reorder fields, or pad preceding sub-byte fields",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthFieldStringAfterDynamicContent = new(
        "BITS049",
        "[BitLengthFieldString] cannot follow a member whose runtime size may not be byte-aligned",
        "[BitLengthFieldString] on '{0}' in '{1}' is preceded by '{2}' which has runtime-variable bit length and is not provably byte-aligned. The static BITS048 check assumes the field starts on a byte boundary, but at runtime '{2}' can shift the offset by a non-byte multiple, breaking wire compatibility. Move the length-field string before any non-byte-aligned dynamic content",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor LengthFieldStringNegativeMaxBytes = new(
        "BITS050",
        "[BitLengthFieldString] MaxBytes must be non-negative",
        "[BitLengthFieldString] on '{0}' in '{1}' has MaxBytes = {2}; values < 0 are not allowed. Use 0 to mean unlimited (only the length field's bit width caps the byte count), or a positive value to cap encoded byte count",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthNestedNotByteAligned = new(
        "BITS051",
        "[BitFieldRelated(RelationKind=ByteLength)] nested type's static bit length must be a multiple of 8",
        "Member '{0}' in '{1}' is a static-length nested [BitSerialize] type with BitLength = {2}, which is not a multiple of 8. RelationKind=ByteLength expresses the carrier as a byte count, so the nested type's serialized footprint must be a whole number of bytes. Add or remove fields so the type's TotalBitLength becomes byte-aligned, or use a dynamic-length nested type (whose runtime byte count the generator can compute via GetTotalBitLength)",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthNestedLengthFieldOrdering = new(
        "BITS052",
        "[BitFieldRelated(RelationKind=ByteLength)] on a nested type requires the length field to be declared earlier",
        "Member '{0}' in '{1}' is a nested [BitSerialize] type using RelationKind=ByteLength, but the related length field '{2}' is not declared before it. The deserializer must know how many bytes to read for the nested payload before deserializing it, so the length field must appear earlier in declaration order. Move the length field above the nested carrier",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RelatedFieldDeclaredAfterDependent = new(
        "BITS053",
        "[BitFieldRelated] target field must be declared BEFORE the dependent field",
        "Member '{0}' in '{1}' references '{2}' via [BitFieldRelated], but '{2}' is declared AFTER '{0}'. Deserialization reads fields in declaration order, so when '{0}' tries to read '{2}' as a count / discriminator / byte-budget the carrier is still at its default value (the list ends up wrong-sized, the poly switch picks the wrong case, etc.). Move the declaration of '{2}' above '{0}'",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor FieldValueOnReferencedCarrier = new(
        "BITS054",
        "[BitFieldValue] field cannot also be a count / length / discriminator carrier",
        "Member '{0}' in '{1}' carries [BitFieldValue({2})] but is referenced by '{3}' as its {4}. At serialize time the auto-backfill (or runtime length computation) writes the *real* dependent size into '{0}', then the primitive write overwrites that with the pinned constant {2} — wire is left with {2} but the dependent payload is the real size, so deserialize reads the wrong budget / count / type-id and mis-parses or truncates. Remove [BitFieldValue] from '{0}', or drop the [BitFieldRelated]/[BitLengthFieldString] reference",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor ByteLengthFixedSlotOnDynamicNested = new(
        "BITS055",
        "[BitFieldRelated(ByteLength)] on a fixed-slot dynamic nested carrier never round-trips",
        "Member '{0}' in '{1}' is a {2} carrier using [BitFieldRelated(nameof({3}), RelationKind=ByteLength)], but it pins the nested type to a fixed {4}-bit slot via an explicit [BitField({4})]. Serialize back-fills '{3}' from the static slot size ({4}/8 = {5} bytes), then writes the nested object whose runtime size may be smaller or larger. The T4 nested deserializer validates `consumedBits == declaredBytes * 8` and will throw on round-trip whenever the runtime size differs. Remove the explicit [BitField({4})] so the back-fill uses GetTotalBitLength() instead, or replace the dynamic nested type with a fixed-size one",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TooManyRelatedAttributes = new(
        "BITS056",
        "Too many [BitFieldRelated] attributes on the same field",
        "Member '{0}' in '{1}' carries {2} [BitFieldRelated] attributes; the analyzer accepts at most 2 (one Count + one ByteLength). The canonical multi-binding pattern is a polymorphic field with a discriminator (Count) and a byte-budget carrier (ByteLength); other combinations would require >2 carrier reads at runtime which is not implemented",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleRelatedRequirePolymorphic = new(
        "BITS057",
        "Multiple [BitFieldRelated] attributes only supported on polymorphic fields",
        "Member '{0}' in '{1}' has 2 [BitFieldRelated] attributes but is not polymorphic (IsList = {2}, IsNested = {3}). Only [BitPoly]-decorated members can usefully bind both a discriminator and a byte-length carrier — list count + byte-length on the same field would be self-contradictory, and a non-polymorphic nested type already has its discriminator implicit in the type name",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleRelatedSameRelationKind = new(
        "BITS058",
        "Two [BitFieldRelated] attributes must use different RelationKinds",
        "Member '{0}' in '{1}' has 2 [BitFieldRelated] attributes both using RelationKind={2}. A second binding only makes sense when it carries an *orthogonal* piece of information; two carriers of the same kind would conflict (which one to backfill from? which to verify against?). Set the second attribute's RelationKind to the complementary value (Count + ByteLength)",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleRelatedSameCarrier = new(
        "BITS059",
        "Two [BitFieldRelated] attributes cannot point to the same carrier field",
        "Member '{0}' in '{1}' has 2 [BitFieldRelated] attributes both targeting '{2}'. The serializer back-fills the discriminator (Count) first and then the byte budget (ByteLength), so the second write overwrites the discriminator value the deserializer would read to pick the concrete polymorphic type; the wire ends up carrying byte-length instead of type-id and deserialize either dispatches the wrong type or throws 'No polymorphic type mapping found'. Use TWO separate carrier fields — one for the discriminator and a distinct one for the byte budget",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor MultipleRelatedRequiresAutoLengthPolymorphic = new(
        "BITS060",
        "Multi-binding [BitFieldRelated] requires auto-length polymorphic (no explicit BitField bit length)",
        "Member '{0}' in '{1}' has 2 [BitFieldRelated] attributes (Count + ByteLength) but also declares an explicit bit length [BitField({2})]. The secondary byte-length carrier was designed for *auto-length* polymorphic fields where the concrete poly type's runtime size determines the byte budget; a fixed-slot polymorphic field always writes exactly {2} bits regardless of the runtime type, so the byte carrier would always be a constant ({3} bytes) AND the deserializer's consumed-vs-declared verify is unreachable (the fixed-slot deserialize path doesn't track runtime bit consumption). Remove the explicit BitField bit length or drop the secondary [BitFieldRelated] binding",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor RelatedFieldNotFound = new(
        "BITS061",
        "[BitFieldRelated] target field does not exist in the enclosing type",
        "Member '{0}' in '{1}' references '{2}' via [BitFieldRelated] ({3}), but '{1}' does not declare a field named '{2}'. Typos like `nameof(Lenght)` would otherwise pass analysis and fail the generated code with CS1061 at compile time; reject up front with a BitSerializer diagnostic so the cause is obvious",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor UnknownRelationKind = new(
        "BITS062",
        "[BitFieldRelated] RelationKind value is not a known enum member",
        "Member '{0}' in '{1}' has a [BitFieldRelated] attribute with RelationKind={2}, which is not a valid BitRelationKind value (0 = Count, 1 = ByteLength). Out-of-range values from an explicit enum cast would otherwise silently make the multi-binding sort drop the second attribute and turn it into a no-op",
        "BitSerializer",
        DiagnosticSeverity.Error,
        true);

}
