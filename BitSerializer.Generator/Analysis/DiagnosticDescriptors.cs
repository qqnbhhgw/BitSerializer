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
        "CRC '{0}' in type '{1}' has invalid include range — included fields must be contiguous and byte-aligned at both ends",
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

}
