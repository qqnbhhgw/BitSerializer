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
}
