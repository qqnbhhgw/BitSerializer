using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BitSerializer.Generator.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BitSerializer.Generator.Analysis;

internal class AnalyzeResult : IEquatable<AnalyzeResult>
{
    public TypeModel? Model { get; set; }
    public Diagnostic? Diagnostic { get; set; }
    public List<Diagnostic> Warnings { get; set; } = new();

    public bool Equals(AnalyzeResult? other)
    {
        if (other is null) return false;
        if (!WarningsEqual(Warnings, other.Warnings)) return false;
        if (Model is not null && other.Model is not null) return Model.Equals(other.Model);
        if (Diagnostic is not null && other.Diagnostic is not null)
            return Diagnostic.Id == other.Diagnostic.Id
                && Diagnostic.Location == other.Diagnostic.Location;
        return false;
    }

    private static bool WarningsEqual(List<Diagnostic> a, List<Diagnostic> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].Location != b[i].Location) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as AnalyzeResult);
    public override int GetHashCode() => Model?.GetHashCode() ?? Diagnostic?.GetHashCode() ?? 0;
}

internal static class TypeAnalyzer
{
    private static readonly HashSet<SpecialType> SupportedSpecialTypes = new()
    {
        SpecialType.System_Byte, SpecialType.System_SByte,
        SpecialType.System_Int16, SpecialType.System_UInt16,
        SpecialType.System_Int32, SpecialType.System_UInt32,
        SpecialType.System_Int64, SpecialType.System_UInt64
    };

    public static AnalyzeResult? Analyze(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var syntax = (TypeDeclarationSyntax)context.TargetNode;

        // Check partial
        if (!syntax.Modifiers.Any(m => m.Text == "partial"))
        {
            return new AnalyzeResult
            {
                Diagnostic = Microsoft.CodeAnalysis.Diagnostic.Create(
                    DiagnosticDescriptors.MustBePartial,
                    syntax.Identifier.GetLocation(),
                    symbol.Name)
            };
        }

        // Collect containing types (for nested types)
        var containingTypes = new List<ContainingTypeInfo>();
        var containingType = symbol.ContainingType;
        while (containingType != null)
        {
            containingTypes.Insert(0, new ContainingTypeInfo
            {
                Name = containingType.Name,
                IsClass = containingType.TypeKind == TypeKind.Class,
                IsRecord = containingType.IsRecord
            });
            containingType = containingType.ContainingType;
        }

        // Check if any ancestor type has [BitSerialize] and collect intermediate fields
        bool hasBitSerializableBase = false;
        int baseBitLength = 0;
        bool baseHasDynamicLength = false;
        var intermediateFields = new List<ISymbol>();
        {
            var current = symbol.BaseType;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                if (HasAttribute(current, "BitSerializer.BitSerializeAttribute"))
                {
                    // Found a [BitSerialize] ancestor - it generates its own code
                    hasBitSerializableBase = true;
                    baseBitLength = CalculateNestedBitLength(current);
                    baseHasDynamicLength = HasDynamicLengthRecursive(current);
                    break;
                }
                // This intermediate base doesn't have [BitSerialize], collect its fields
                intermediateFields.InsertRange(0, GetSerializableMembers(current));
                current = current.BaseType;
            }
        }

        var model = new TypeModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsClass = symbol.TypeKind == TypeKind.Class,
            IsRecord = symbol.IsRecord,
            IsOpenGeneric = symbol.TypeParameters.Length > 0,
            ContainingTypes = containingTypes,
            HasBitSerializableBaseType = hasBitSerializableBase,
            BaseBitLength = baseBitLength,
            BaseHasDynamicLength = baseHasDynamicLength,
            HasDynamicLength = baseHasDynamicLength
        };

        // Collect members: intermediate base fields first, then own declared members
        var members = new List<ISymbol>();
        members.AddRange(intermediateFields);
        members.AddRange(GetSerializableMembers(symbol));
        int currentBitIndex = baseBitLength;

        // Track CRC algorithm symbols per member name so we can validate after the field loop
        var crcAlgoByField = new Dictionary<string, INamedTypeSymbol?>();

        // Collect non-fatal warnings emitted during member analysis
        var warnings = new List<Diagnostic>();

        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();

            var memberType = GetMemberType(member);
            if (memberType == null) continue;

            // Check for BitIgnore
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute"))
                continue;

            // Check for BitField and string attributes
            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            var fixedStringAttr = GetAttribute(member, "BitSerializer.BitFixedStringAttribute");
            var terminatedStringAttr = GetAttribute(member, "BitSerializer.BitTerminatedStringAttribute");
            var lengthPrefixStringAttr = GetAttribute(member, "BitSerializer.BitLengthPrefixStringAttribute");
            var lengthFieldStringAttr = GetAttribute(member, "BitSerializer.BitLengthFieldStringAttribute");

            if (bitFieldAttr == null && fixedStringAttr == null && terminatedStringAttr == null
                && lengthPrefixStringAttr == null && lengthFieldStringAttr == null)
                continue;

            var field = new BitFieldModel
            {
                MemberName = member.Name,
                IsProperty = member is IPropertySymbol,
                BitStartIndex = currentBitIndex,
            };

            // Codex review (round-2) P2: [BitFieldValue] is only meaningful on numeric/enum scalars,
            // but all four string variants below `continue` BEFORE the per-field BitFieldValue
            // parsing block runs (around line ~620). Without this upfront guard a user writing
            // `[BitLengthFieldString(...), BitFieldValue(0x7E)] string Header { get; set; }`
            // gets neither constant pinning NOR a diagnostic — the attribute is silently dropped.
            // Reject with BITS042 (which already covers "non-scalar field carries [BitFieldValue]")
            // so the failure mode is loud at compile time.
            if ((fixedStringAttr != null || terminatedStringAttr != null
                 || lengthPrefixStringAttr != null || lengthFieldStringAttr != null)
                && GetAttribute(member, "BitSerializer.BitFieldValueAttribute") != null)
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.FieldValueRequiresNumericScalar,
                        member.Locations.FirstOrDefault(),
                        member.Name, symbol.Name,
                        false, true, false, false, false) // IsList, IsString, IsNested, IsPoly, IsTypeParameter
                };
            }

            // Handle [BitFixedString] (standalone, no [BitField] required)
            if (fixedStringAttr != null)
            {
                if (memberType.SpecialType != SpecialType.System_String)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FixedStringMustBeString,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name)
                    };
                }

                int byteLen = (int)fixedStringAttr.ConstructorArguments[0].Value!;
                string encodingName = "ASCII";
                byte padding = 0x00;
                foreach (var named in fixedStringAttr.NamedArguments)
                {
                    if (named.Key == "ByteLength" && named.Value.Value is int namedByteLen)
                        byteLen = namedByteLen;
                    else if (named.Key == "Encoding" && named.Value.Value is int encVal)
                        encodingName = encVal == 1 ? "UTF8" : "ASCII";
                    else if (named.Key == "Padding" && named.Value.Value is byte padVal)
                        padding = padVal;
                }
                if (byteLen <= 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FixedStringInvalidLength,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, byteLen)
                    };
                }

                field.IsFixedString = true;
                field.FixedStringByteLength = byteLen;
                field.FixedStringPadding = padding;
                field.StringEncodingName = encodingName;
                field.BitLength = byteLen * 8;
                field.MemberTypeName = "string";
                field.MemberTypeFullName = "string";
                currentBitIndex += field.BitLength;
                model.Fields.Add(field);
                continue;
            }

            // Handle [BitLengthPrefixString] (standalone, dynamic, no [BitField] required)
            if (lengthPrefixStringAttr != null)
            {
                if (memberType.SpecialType != SpecialType.System_String)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthPrefixStringMustBeString,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name)
                    };
                }

                int lengthBits = 16;
                string encodingName = "UTF8";
                int maxBytes = 0;
                if (lengthPrefixStringAttr.ConstructorArguments.Length > 0
                    && lengthPrefixStringAttr.ConstructorArguments[0].Value is int ctorBits)
                {
                    lengthBits = ctorBits;
                }
                foreach (var named in lengthPrefixStringAttr.NamedArguments)
                {
                    if (named.Key == "LengthBits" && named.Value.Value is int nbits)
                        lengthBits = nbits;
                    else if (named.Key == "Encoding" && named.Value.Value is int encVal)
                        encodingName = encVal == 1 ? "UTF8" : "ASCII";
                    else if (named.Key == "MaxBytes" && named.Value.Value is int mb)
                        maxBytes = mb;
                }

                if (lengthBits != 8 && lengthBits != 16 && lengthBits != 32)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthPrefixStringInvalidBits,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, lengthBits)
                    };
                }

                // BITS036: MaxBytes < 0 silently behaved as "unlimited" because emitters guard on > 0.
                // 0 is the documented unlimited sentinel; reject negatives so typos fail at compile time.
                if (maxBytes < 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthPrefixStringNegativeMaxBytes,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, maxBytes)
                    };
                }

                // BITS038 (review round-9 P2 + round-10 P2): a length-prefix string is a byte stream
                // (prefix + raw bytes), so writing it across a sub-byte boundary breaks wire
                // compatibility with any byte-aligned reader. Two checks:
                //
                // (a) Static layout: currentBitIndex (the field's nominal offset) must be a multiple
                //     of 8. Catches sub-byte predecessors with fixed widths (e.g. `[BitField(1)]`).
                //
                // (b) Runtime layout: any preceding dynamic field whose runtime bit count is not
                //     provably a multiple of 8 (e.g. dynamic list of 1-bit elements, runtime-sized
                //     polymorphic auto-length, manual IBitSerializable with unknown width) would
                //     shift this field to a non-byte-aligned offset at runtime — defeating BITS038's
                //     wire-compatibility guarantee. Conservative base-type dynamic length is rejected
                //     for the same reason (can't recursively prove byte-aligned size).
                if ((currentBitIndex % 8) != 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthPrefixStringNotByteAligned,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, currentBitIndex)
                    };
                }
                string? lpsLeadingCulprit = null;
                if (model.BaseHasDynamicLength)
                    lpsLeadingCulprit = "base type";
                else
                {
                    foreach (var prev in model.Fields)
                    {
                        if (!IsIncludeFieldDynamic(prev)) continue;
                        var cls = ClassifyIncludeAlignment(prev, symbol.ContainingAssembly);
                        if (cls != FieldAlignmentClass.Aligned)
                        {
                            lpsLeadingCulprit = prev.MemberName;
                            break;
                        }
                    }
                }
                if (lpsLeadingCulprit != null)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthPrefixStringAfterDynamicContent,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, lpsLeadingCulprit)
                    };
                }

                field.IsLengthPrefixString = true;
                field.LengthPrefixBits = lengthBits;
                field.LengthPrefixMaxBytes = maxBytes;
                field.StringEncodingName = encodingName;
                field.BitLength = 0; // dynamic
                field.MemberTypeName = "string";
                field.MemberTypeFullName = "string";
                model.HasDynamicLength = true;

                // Preserve [BitCrcInclude] so the CRC aggregator sees this field.
                var crcIncludeAttrLps = GetAttribute(member, "BitSerializer.BitCrcIncludeAttribute");
                if (crcIncludeAttrLps != null && crcIncludeAttrLps.ConstructorArguments.Length > 0)
                {
                    field.CrcTargetFieldName = crcIncludeAttrLps.ConstructorArguments[0].Value as string;
                }

                model.Fields.Add(field);
                continue;
            }

            // Handle [BitLengthFieldString(nameof(LengthField))]: string whose byte count is carried
            // by a separately-declared length field. Structurally mirrors [BitLengthPrefixString] but
            // the prefix lives in a peer field (must be declared earlier in the type) instead of inline.
            if (lengthFieldStringAttr != null)
            {
                if (memberType.SpecialType != SpecialType.System_String)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthFieldStringMustBeString,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name)
                    };
                }

                string lengthFieldName = lengthFieldStringAttr.ConstructorArguments[0].Value as string ?? "";
                string lfsEncodingName = "UTF8";
                int lfsMaxBytes = 0;
                foreach (var named in lengthFieldStringAttr.NamedArguments)
                {
                    if (named.Key == "Encoding" && named.Value.Value is int encVal)
                        lfsEncodingName = encVal == 1 ? "UTF8" : "ASCII";
                    else if (named.Key == "MaxBytes" && named.Value.Value is int mb)
                        lfsMaxBytes = mb;
                }

                // BITS050: MaxBytes < 0 — mirror BITS036 (BitLengthPrefixString) sentinel rule.
                if (lfsMaxBytes < 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthFieldStringNegativeMaxBytes,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, lfsMaxBytes)
                    };
                }

                // BITS047: the target field must already exist in model.Fields (earlier in declaration
                // order), be a byte-aligned numeric scalar, and have a bit width ∈ {8, 16, 32}.
                // Allowed primitive types: byte, sbyte, short, ushort, int, uint — these are the
                // typical wire-length carriers. Signed types are allowed for protocol-compat, but the
                // serializer's overflow check uses the unsigned-range maximum because byte counts are
                // never negative.
                var lengthField = model.Fields.Find(f => f.MemberName == lengthFieldName);
                bool lengthFieldValid = lengthField != null
                    && lengthField.IsNumericOrEnum
                    && !lengthField.IsList
                    && !lengthField.IsFixedString
                    && !lengthField.IsTerminatedString
                    && !lengthField.IsLengthPrefixString
                    && !lengthField.IsLengthFieldString
                    && !lengthField.IsNestedType
                    && !lengthField.IsPolymorphic
                    && !lengthField.IsTypeParameter
                    && (lengthField.BitLength == 8 || lengthField.BitLength == 16 || lengthField.BitLength == 32)
                    && (lengthField.BitStartIndex % 8) == 0
                    && IsAllowedLengthFieldType(lengthField.MemberTypeFullName);
                if (!lengthFieldValid)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthFieldStringMissingTarget,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, lengthFieldName)
                    };
                }

                // BITS048: static byte-alignment of the string slot itself.
                if ((currentBitIndex % 8) != 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthFieldStringNotByteAligned,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, currentBitIndex)
                    };
                }

                // BITS049: dynamic preceding fields that may shift the runtime offset off a byte boundary.
                string? lfsLeadingCulprit = null;
                if (model.BaseHasDynamicLength)
                    lfsLeadingCulprit = "base type";
                else
                {
                    foreach (var prev in model.Fields)
                    {
                        if (!IsIncludeFieldDynamic(prev)) continue;
                        var cls = ClassifyIncludeAlignment(prev, symbol.ContainingAssembly);
                        if (cls != FieldAlignmentClass.Aligned)
                        {
                            lfsLeadingCulprit = prev.MemberName;
                            break;
                        }
                    }
                }
                if (lfsLeadingCulprit != null)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.LengthFieldStringAfterDynamicContent,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, lfsLeadingCulprit)
                    };
                }

                field.IsLengthFieldString = true;
                field.LengthFieldMemberName = lengthFieldName;
                field.LengthFieldMaxBytes = lfsMaxBytes;
                field.LengthFieldBitWidth = lengthField!.BitLength;
                field.LengthFieldTypeName = lengthField.MemberTypeName;
                field.StringEncodingName = lfsEncodingName;
                field.BitLength = 0; // dynamic
                field.MemberTypeName = "string";
                field.MemberTypeFullName = "string";
                model.HasDynamicLength = true;

                // Preserve [BitCrcInclude] so the CRC aggregator sees this field.
                var crcIncludeAttrLfs = GetAttribute(member, "BitSerializer.BitCrcIncludeAttribute");
                if (crcIncludeAttrLfs != null && crcIncludeAttrLfs.ConstructorArguments.Length > 0)
                {
                    field.CrcTargetFieldName = crcIncludeAttrLfs.ConstructorArguments[0].Value as string;
                }

                model.Fields.Add(field);
                continue;
            }

            // Handle [BitTerminatedString] (standalone, always dynamic)
            if (terminatedStringAttr != null)
            {
                if (memberType.SpecialType != SpecialType.System_String)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.TerminatedStringMustBeString,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name)
                    };
                }

                string encodingName = "ASCII";
                foreach (var named in terminatedStringAttr.NamedArguments)
                {
                    if (named.Key == "Encoding" && named.Value.Value is int encVal)
                        encodingName = encVal == 1 ? "UTF8" : "ASCII";
                }

                field.IsTerminatedString = true;
                field.StringEncodingName = encodingName;
                field.BitLength = 0;
                field.MemberTypeName = "string";
                field.MemberTypeFullName = "string";
                model.HasDynamicLength = true;

                // Preserve [BitCrcInclude] on terminated strings so the CRC aggregator sees them.
                var crcIncludeAttrTerm = GetAttribute(member, "BitSerializer.BitCrcIncludeAttribute");
                if (crcIncludeAttrTerm != null && crcIncludeAttrTerm.ConstructorArguments.Length > 0)
                {
                    field.CrcTargetFieldName = crcIncludeAttrTerm.ConstructorArguments[0].Value as string;
                }

                model.Fields.Add(field);
                continue;
            }

            // Get explicit bit length from [BitField]
            int? explicitBitLength = GetBitLengthFromAttribute(bitFieldAttr!);

            // Read [BitField(Endian = ...)] named argument (0=Inherit, 1=Big, 2=Little).
            // Stored as int to match BitFieldModel.Endian; runtime BitEndian enum maps to the same values.
            foreach (var named in bitFieldAttr!.NamedArguments)
            {
                if (named.Key == "Endian" && named.Value.Value is int endianRaw)
                {
                    // BITS037: reject undefined enum values (e.g. `(BitEndian)3`). C# allows arbitrary
                    // ints into enums and ResolveFieldHelper would silently fall back to Inherit for
                    // anything other than 1/2, hiding the typo at runtime.
                    if (endianRaw < 0 || endianRaw > 2)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.FieldEndianOutOfRange,
                                member.Locations.FirstOrDefault(),
                                member.Name, symbol.Name, endianRaw)
                        };
                    }
                    field.Endian = endianRaw;
                }
            }

            // Check for BitFieldRelated
            var relatedAttr = GetAttribute(member, "BitSerializer.BitFieldRelatedAttribute");
            string? relatedMemberName = null;
            string? valueConverterFullName = null;

            if (relatedAttr != null)
            {
                if (relatedAttr.ConstructorArguments.Length > 0 &&
                    !relatedAttr.ConstructorArguments[0].IsNull)
                {
                    relatedMemberName = relatedAttr.ConstructorArguments[0].Value as string;
                }

                if (relatedAttr.ConstructorArguments.Length > 1 &&
                    !relatedAttr.ConstructorArguments[1].IsNull)
                {
                    var converterType = relatedAttr.ConstructorArguments[1].Value as INamedTypeSymbol;
                    if (converterType != null)
                    {
                        valueConverterFullName = converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                }

                // Also check named arguments (ValueConverterType = typeof(...), RelationKind = ...)
                foreach (var namedArg in relatedAttr.NamedArguments)
                {
                    if (valueConverterFullName == null
                        && namedArg.Key == "ValueConverterType"
                        && namedArg.Value.Value is INamedTypeSymbol namedConverterType)
                    {
                        valueConverterFullName = namedConverterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    else if (namedArg.Key == "RelationKind" && namedArg.Value.Value is int relKindRaw)
                    {
                        field.RelationKind = relKindRaw;
                    }
                }
            }

            field.RelatedMemberName = relatedMemberName;
            field.ValueConverterTypeFullName = valueConverterFullName;

            // Check if converter type has context-aware overloads (2-param OnSerializeConvert/OnDeserializeConvert)
            if (valueConverterFullName != null && relatedAttr != null)
            {
                INamedTypeSymbol? converterSymbol = null;
                if (relatedAttr.ConstructorArguments.Length > 1 && !relatedAttr.ConstructorArguments[1].IsNull)
                    converterSymbol = relatedAttr.ConstructorArguments[1].Value as INamedTypeSymbol;
                if (converterSymbol == null)
                {
                    foreach (var namedArg in relatedAttr.NamedArguments)
                    {
                        if (namedArg.Key == "ValueConverterType" && namedArg.Value.Value is INamedTypeSymbol ns)
                        {
                            converterSymbol = ns;
                            break;
                        }
                    }
                }
                if (converterSymbol != null)
                {
                    var serMethods = converterSymbol.GetMembers("OnSerializeConvert").OfType<IMethodSymbol>().ToList();
                    var deserMethods = converterSymbol.GetMembers("OnDeserializeConvert").OfType<IMethodSymbol>().ToList();
                    field.ValueConverterHasSerialize = serMethods.Count > 0;
                    field.ValueConverterHasDeserialize = deserMethods.Count > 0;
                    field.ValueConverterSerializeHasContext = serMethods.Any(m => m.Parameters.Length == 2);
                    field.ValueConverterDeserializeHasContext = deserMethods.Any(m => m.Parameters.Length == 2);
                }
            }

            // Check for BitFieldCount
            var countAttr = GetAttribute(member, "BitSerializer.BitFieldCountAttribute");
            if (countAttr != null && countAttr.ConstructorArguments.Length > 0)
            {
                field.FixedCount = (int)countAttr.ConstructorArguments[0].Value!;
                foreach (var named in countAttr.NamedArguments)
                {
                    if (named.Key == "PadIfShort" && named.Value.Value is bool padVal)
                        field.PadIfShort = padVal;
                }
            }

            // Check for [BitFieldValue(constant)]
            // (validated after IsList / IsNumericOrEnum / IsPolymorphic categorization below.)
            var fieldValueAttr = GetAttribute(member, "BitSerializer.BitFieldValueAttribute");
            if (fieldValueAttr != null && fieldValueAttr.ConstructorArguments.Length > 0)
            {
                var tc = fieldValueAttr.ConstructorArguments[0];
                long? parsed = null;
                // Codex review round-3 P2: track whether the source literal was an *unsigned* type
                // whose value exceeds long.MaxValue. In that case the `unchecked((long)ul)` cast
                // produces a negative-looking long that would pass the [signedMin, unsignedMax]
                // range check on a sub-64-bit field (e.g. BitLength=63 + ulong.MaxValue gets -1L,
                // which trivially fits the signed range). But the actual bit pattern needs 64 bits
                // and can never fit a <64-bit slot — the serializer would silently truncate top
                // bits while Verify=true at deserialize would always fail. Reject upfront.
                bool wasUnsignedOverflow = false;
                // Codex review P2: accept the FULL ulong range (e.g. 0x8000000000000000UL on a
                // 64-bit field). Store the wire-bit pattern as long via unchecked cast — emitters
                // already use `unchecked((T){literal}L)` so the negative-looking long round-trips
                // back to the user's intended unsigned bit pattern on serialize/deserialize.
                if (tc.Kind == TypedConstantKind.Primitive && tc.Value != null)
                {
                    switch (tc.Value)
                    {
                        case sbyte sb: parsed = sb; break;
                        case byte b: parsed = b; break;
                        case short s: parsed = s; break;
                        case ushort us: parsed = us; break;
                        case int i: parsed = i; break;
                        case uint ui: parsed = ui; break;
                        case long l: parsed = l; break;
                        case ulong ul:
                            parsed = unchecked((long)ul);
                            wasUnsignedOverflow = ul > long.MaxValue;
                            break;
                    }
                }
                else if (tc.Kind == TypedConstantKind.Enum && tc.Value != null)
                {
                    switch (tc.Value)
                    {
                        case sbyte sb: parsed = sb; break;
                        case byte b: parsed = b; break;
                        case short s: parsed = s; break;
                        case ushort us: parsed = us; break;
                        case int i: parsed = i; break;
                        case uint ui: parsed = ui; break;
                        case long l: parsed = l; break;
                        case ulong ul:
                            parsed = unchecked((long)ul);
                            wasUnsignedOverflow = ul > long.MaxValue;
                            break;
                    }
                }
                if (parsed == null)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldValueInvalidType,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, tc.Value?.ToString() ?? "<null>")
                    };
                }
                field.HasConstantValue = true;
                field.ConstantValue = parsed.Value;
                field.ConstantValueIsUnsignedOverflow = wasUnsignedOverflow;
                foreach (var named in fieldValueAttr.NamedArguments)
                {
                    if (named.Key == "Verify" && named.Value.Value is bool verifyVal)
                        field.ConstantValueVerify = verifyVal;
                }
            }

            // Check for [BitCrc]
            var crcAttr = GetAttribute(member, "BitSerializer.BitCrcAttribute");
            if (crcAttr != null && crcAttr.ConstructorArguments.Length > 0)
            {
                field.IsCrcResult = true;
                var algoType = crcAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
                crcAlgoByField[member.Name] = algoType;
                field.CrcAlgorithmTypeFullName = algoType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
                foreach (var named in crcAttr.NamedArguments)
                {
                    if (named.Key == "InitialValue")
                    {
                        if (named.Value.Value is ulong u) field.CrcInitialValue = u;
                        else if (named.Value.Value is long l) field.CrcInitialValue = unchecked((ulong)l);
                        else if (named.Value.Value is int i) field.CrcInitialValue = unchecked((ulong)i);
                        else if (named.Value.Value is uint ui) field.CrcInitialValue = ui;
                    }
                    else if (named.Key == "ValidateOnDeserialize" && named.Value.Value is bool b)
                    {
                        field.CrcValidateOnDeserialize = b;
                    }
                    else if (named.Key == "WholeBuffer" && named.Value.Value is bool wb)
                    {
                        field.CrcWholeBuffer = wb;
                    }
                    else if (named.Key == "SkipHeadBytes" && named.Value.Value is int sh)
                    {
                        field.CrcSkipHeadBytes = sh;
                    }
                    else if (named.Key == "SkipTailBytes" && named.Value.Value is int st)
                    {
                        field.CrcSkipTailBytes = st;
                    }
                }
            }

            // Check for [BitCrcInclude]
            var crcIncludeAttr = GetAttribute(member, "BitSerializer.BitCrcIncludeAttribute");
            if (crcIncludeAttr != null && crcIncludeAttr.ConstructorArguments.Length > 0)
            {
                field.CrcTargetFieldName = crcIncludeAttr.ConstructorArguments[0].Value as string;
            }

            // Check for [BitFieldConsumeRemaining]
            if (GetAttribute(member, "BitSerializer.BitFieldConsumeRemainingAttribute") != null)
            {
                field.ConsumeRemaining = true;
                model.HasDynamicLength = true;
            }

            // Check for BitPoly
            var polyAttrs = GetAttributes(member, "BitSerializer.BitPolyAttribute");
            if (polyAttrs.Count > 0)
            {
                // BITS007: [BitPoly] requires [BitFieldRelated] so deserialization can pick
                // the concrete type from a discriminator field.
                if (relatedAttr == null || string.IsNullOrEmpty(relatedMemberName))
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.PolymorphicMissingRelated,
                            member.Locations.FirstOrDefault(),
                            member.Name)
                    };
                }

                field.IsPolymorphic = true;
                field.PolyMappings = new List<PolyMapping>();
                foreach (var polyAttr in polyAttrs)
                {
                    if (polyAttr.ConstructorArguments.Length >= 2)
                    {
                        var typeId = (int)polyAttr.ConstructorArguments[0].Value!;
                        var concreteType = (INamedTypeSymbol)polyAttr.ConstructorArguments[1].Value!;
                        field.PolyMappings.Add(new PolyMapping
                        {
                            TypeId = typeId,
                            ConcreteTypeName = concreteType.Name,
                            ConcreteTypeFullName = concreteType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            // Compute Endian-override flag from the actual ITypeSymbol so types in
                            // referenced assemblies are seen (review round-12 P1).
                            HasEndianOverride = HasEndianOverrideRecursive(concreteType, new HashSet<string>())
                        });
                    }
                }
            }

            // Determine field category
            if (IsListType(memberType, out var elementType, out var isArray))
            {
                field.IsList = true;
                field.IsArray = isArray;
                field.ListElementTypeName = elementType!.Name;
                field.ListElementTypeFullName = elementType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (IsNumericOrEnum(elementType!))
                {
                    int elementBits = explicitBitLength ?? GetDefaultBitLength(elementType!);
                    field.ListElementBitLength = elementBits;
                    field.ListElementIsNested = false;
                    field.BitLength = explicitBitLength ?? 0;
                }
                else if (HasAttribute(elementType!, "BitSerializer.BitSerializeAttribute"))
                {
                    field.ListElementIsNested = true;
                    int nestedBits = CalculateNestedBitLength(elementType!);
                    field.ListElementBitLength = nestedBits;
                    field.BitLength = 0;
                    if (HasDynamicLengthRecursive(elementType!))
                        field.ListElementHasDynamicLength = true;
                    if (HasOwnContextMethods(elementType!))
                        field.ListElementHasOwnContext = true;
                    // Endian-override propagation from actual element symbol (review round-12 P1):
                    // covers element types in referenced assemblies, not just ContainingAssembly.
                    if (HasEndianOverrideRecursive(elementType!, new HashSet<string>()))
                    {
                        field.NestedHasEndianOverride = true;
                        field.NestedEndianKind = "list element";
                        field.NestedEndianTypeFullName = field.ListElementTypeFullName;
                    }
                }
                else if (ImplementsBitSerializable(elementType!))
                {
                    if (elementType is ITypeParameterSymbol elemTypeParam
                        && !elemTypeParam.HasConstructorConstraint
                        && !elemTypeParam.HasValueTypeConstraint)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.TypeParameterMissingNewConstraint,
                                member.Locations.FirstOrDefault(),
                                elemTypeParam.Name, member.Name, symbol.Name)
                        };
                    }
                    field.ListElementIsNested = true;
                    field.ListElementIsManualBitSerializable = true;
                    field.ListElementIsTypeParameter = elementType is ITypeParameterSymbol;
                    field.ListElementBitLength = explicitBitLength ?? 0;
                    field.BitLength = 0;
                    if (HasOwnContextMethods(elementType!))
                        field.ListElementHasOwnContext = true;
                }
                else
                {
                    // List element is a non-numeric type without [BitSerialize] or IBitSerializable
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.NestedTypeMustBeBitSerializable,
                            member.Locations.FirstOrDefault(),
                            elementType!.Name, symbol.Name)
                    };
                }

                if (field.ListElementHasDynamicLength)
                {
                    model.HasDynamicLength = true;
                    // Advance by static estimation so subsequent fields get correct BitStartIndex
                    if (field.FixedCount.HasValue && field.ListElementBitLength > 0)
                        currentBitIndex += field.FixedCount.Value * field.ListElementBitLength;
                }
                else if (field.FixedCount.HasValue && field.ListElementBitLength > 0)
                {
                    int totalListBits = field.FixedCount.Value * field.ListElementBitLength;
                    currentBitIndex += totalListBits;
                }
                else
                {
                    model.HasDynamicLength = true;
                }

                field.MemberTypeName = memberType.Name;
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else if (field.IsPolymorphic)
            {
                field.MemberTypeName = memberType.Name;
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                field.IsNestedType = true;

                // For polymorphic, use explicit bitLength or calculate from max poly type
                if (explicitBitLength.HasValue)
                {
                    field.BitLength = explicitBitLength.Value;
                    field.PolymorphicBitLength = explicitBitLength.Value;
                    // BITS023: explicit N is a fixed slot regardless of runtime poly type size
                    warnings.Add(Diagnostic.Create(
                        DiagnosticDescriptors.ExplicitBitLengthOnVariableContent,
                        member.Locations.FirstOrDefault(),
                        member.Name, symbol.Name, explicitBitLength.Value,
                        "polymorphic (the actual bit length depends on the runtime concrete type)"));
                }
                else
                {
                    int maxBits = 0;
                    foreach (var mapping in field.PolyMappings!)
                    {
                        var polyTypeSymbol = FindTypeByFullName(symbol.ContainingAssembly, mapping.ConcreteTypeFullName);
                        if (polyTypeSymbol != null)
                        {
                            int bits = CalculateNestedBitLength(polyTypeSymbol);
                            if (bits > maxBits) maxBits = bits;
                        }
                    }
                    field.BitLength = maxBits;
                    field.PolymorphicBitLength = maxBits;
                    // Auto-length polymorphic: actual bit length is determined by the runtime
                    // concrete type, so GetTotalBitLength must dispatch dynamically.
                    field.IsPotentiallyDynamic = true;
                    model.HasDynamicLength = true;
                }

                currentBitIndex += field.BitLength;
            }
            else if (IsNumericOrEnum(memberType))
            {
                field.IsNumericOrEnum = true;

                if (memberType.TypeKind == TypeKind.Enum)
                {
                    field.IsEnum = true;
                    var underlying = ((INamedTypeSymbol)memberType).EnumUnderlyingType!;
                    field.EnumUnderlyingTypeName = GetSimpleTypeName(underlying);
                    field.BitLength = explicitBitLength ?? GetDefaultBitLength(underlying);
                }
                else
                {
                    field.BitLength = explicitBitLength ?? GetDefaultBitLength(memberType);
                }

                field.MemberTypeName = GetSimpleTypeName(memberType);
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                currentBitIndex += field.BitLength;
            }
            else if (HasAttribute(memberType, "BitSerializer.BitSerializeAttribute"))
            {
                field.IsNestedType = true;
                field.MemberTypeName = memberType.Name;
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                int nestedBits = CalculateNestedBitLength(memberType);
                field.BitLength = explicitBitLength ?? nestedBits;
                bool nestedIsDynamic = HasDynamicLengthRecursive(memberType);
                // Capture regardless of explicit-BitLength override so BITS055 can later reject the
                // "dynamic nested + fixed slot + ByteLength carrier" combination that BITS023
                // currently only warns about (round-trip can't work — backfill writes N/8 but the
                // nested type emits its own runtime size, then the T4 nested deserializer's
                // consumedBits-vs-budget verify fires).
                field.NestedTypeHasDynamicContent = nestedIsDynamic;
                if (!explicitBitLength.HasValue && nestedIsDynamic)
                {
                    field.IsPotentiallyDynamic = true;
                    model.HasDynamicLength = true;
                }
                else if (explicitBitLength.HasValue && nestedIsDynamic)
                {
                    // BITS023: the nested type has dynamic content but is pinned to a fixed slot
                    warnings.Add(Diagnostic.Create(
                        DiagnosticDescriptors.ExplicitBitLengthOnVariableContent,
                        member.Locations.FirstOrDefault(),
                        member.Name, symbol.Name, explicitBitLength.Value,
                        $"a [BitSerialize] type '{memberType.Name}' with dynamic-length content"));
                }
                if (HasOwnContextMethods(memberType))
                    field.NestedHasOwnContext = true;
                // Endian-override propagation from actual nested symbol (review round-12 P1).
                if (HasEndianOverrideRecursive(memberType, new HashSet<string>()))
                {
                    field.NestedHasEndianOverride = true;
                    field.NestedEndianKind = "nested member";
                    field.NestedEndianTypeFullName = field.MemberTypeFullName;
                }
                currentBitIndex += field.BitLength;
            }
            else if (memberType is ITypeParameterSymbol typeParam &&
                     (typeParam.ConstraintTypes.Any(c => HasAttribute(c, "BitSerializer.BitSerializeAttribute")) ||
                      typeParam.ConstraintTypes.Any(c => ImplementsBitSerializable(c)) ||
                      ImplementsBitSerializable(memberType)))
            {
                if (!typeParam.HasConstructorConstraint && !typeParam.HasValueTypeConstraint)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.TypeParameterMissingNewConstraint,
                            member.Locations.FirstOrDefault(),
                            typeParam.Name, member.Name, symbol.Name)
                    };
                }
                // Type parameter with [BitSerialize] constraint (e.g., TExComLogSt : ExComLogStBase)
                // Bit length is unknown at compile time, use interface dispatch at runtime
                field.IsNestedType = true;
                field.IsTypeParameter = true;
                field.MemberTypeName = memberType.Name;
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                field.BitLength = 0; // Unknown at compile time
                model.HasDynamicLength = true;
            }
            else if (ImplementsBitSerializable(memberType))
            {
                // Manual IBitSerializable type (without [BitSerialize] attribute)
                field.IsManualBitSerializable = true;
                field.IsNestedType = true;
                field.MemberTypeName = memberType.Name;
                field.MemberTypeFullName = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // Manual impls are *always* runtime-decided (their Serialize/Deserialize chooses
                // how many bits to write), so the dynamic-content flag is unconditionally true —
                // BITS055 uses this to reject ByteLength carriers that lock such a member into a
                // fixed slot.
                field.NestedTypeHasDynamicContent = true;
                if (explicitBitLength.HasValue)
                {
                    field.BitLength = explicitBitLength.Value;
                    currentBitIndex += field.BitLength;
                    // BITS023: manual IBitSerializable write size is unknown to the generator
                    warnings.Add(Diagnostic.Create(
                        DiagnosticDescriptors.ExplicitBitLengthOnVariableContent,
                        member.Locations.FirstOrDefault(),
                        member.Name, symbol.Name, explicitBitLength.Value,
                        $"a manual IBitSerializable type '{memberType.Name}' (its Serialize/Deserialize decides the actual bit length at runtime)"));
                }
                else
                {
                    field.BitLength = 0;
                    field.IsPotentiallyDynamic = true;
                    model.HasDynamicLength = true;
                }
                if (HasOwnContextMethods(memberType))
                    field.NestedHasOwnContext = true;
            }
            else
            {
                // Non-numeric, non-list type without [BitSerialize] or IBitSerializable
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.NestedTypeMustBeBitSerializable,
                        member.Locations.FirstOrDefault(),
                        memberType.Name, symbol.Name)
                };
            }

            // [BitFieldValue] validation: only on numeric/enum scalars; must fit in BitLength;
            // mutually exclusive with [BitCrc] / [BitFieldRelated] / [BitFieldCount] / [BitPoly].
            if (field.HasConstantValue)
            {
                if (!field.IsNumericOrEnum || field.IsList || field.IsFixedString
                    || field.IsTerminatedString || field.IsLengthPrefixString
                    || field.IsNestedType || field.IsPolymorphic || field.IsTypeParameter)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldValueRequiresNumericScalar,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name,
                            field.IsList,
                            field.IsFixedString || field.IsTerminatedString || field.IsLengthPrefixString,
                            field.IsNestedType, field.IsPolymorphic, field.IsTypeParameter)
                    };
                }

                // Range check against BitLength. For 64-bit fields any long fits; for narrower
                // fields the constant must fit in either signed (two's complement) or unsigned range
                // — accept both because users may write 0x80 on a `sbyte` (= -128) without thinking
                // about signedness. Reject if the constant is outside [signedMin, unsignedMax].
                //
                // Codex review round-3 P2: when the source literal was a `ulong` > long.MaxValue
                // the unchecked-cast-to-long produced a negative-looking value that trivially
                // passes the signed range check on any BitLength ≥ 1. We need a separate
                // bit-pattern check for that case — the actual unsigned value only fits in 64
                // bits, so any narrower slot must reject it.
                if (field.ConstantValueIsUnsignedOverflow && field.BitLength < 64)
                {
                    // The actual unsigned value is unchecked((ulong)ConstantValue); for diagnostic
                    // text we want the original unsigned magnitude, not the negative-looking long.
                    ulong actualUnsigned = unchecked((ulong)field.ConstantValue);
                    long unsignedMaxForBitLen = (1L << field.BitLength) - 1;
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldValueOverflowsBitLength,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, field.BitLength,
                            $"0x{actualUnsigned:X}UL", 64,
                            (long)0, unsignedMaxForBitLen)
                    };
                }
                if (field.BitLength < 64)
                {
                    long signedMin = -(1L << (field.BitLength - 1));
                    long unsignedMax = (1L << field.BitLength) - 1;
                    if (field.ConstantValue < signedMin || field.ConstantValue > unsignedMax)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.FieldValueOverflowsBitLength,
                                member.Locations.FirstOrDefault(),
                                member.Name, symbol.Name, field.BitLength,
                                field.ConstantValue, ComputeBitsRequired(field.ConstantValue),
                                signedMin, unsignedMax)
                        };
                    }
                }

                // Codex review P2: do NOT list [BitCrcInclude] as a conflict. CRC-inclusion just
                // records that this field's bytes participate in a CRC range; it does not overwrite
                // the field's wire bytes. Combining [BitFieldValue(0x7E)] + [BitCrcInclude] is the
                // canonical "fixed magic header covered by CRC" pattern in DMI/Modbus frames.
                string? conflict = null;
                if (field.IsCrcResult) conflict = "[BitCrc]";
                else if (field.RelatedMemberName != null) conflict = "[BitFieldRelated]";
                else if (field.FixedCount.HasValue) conflict = "[BitFieldCount]";
                if (conflict != null)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldValueConflictsWithOtherFeatures,
                            member.Locations.FirstOrDefault(),
                            member.Name, symbol.Name, conflict)
                    };
                }
            }

            model.Fields.Add(field);
        }

        model.TotalBitLength = currentBitIndex;

        // BITS028 / BITS033 / BITS040 / BITS041: validate [BitField(Endian = ...)] usage,
        // both for fields declared with Endian directly and for nested [BitSerialize] members
        // that transitively contain such a field.
        //
        // The Endian override swaps MSB ↔ LSB helpers, which is only semantically a "byte-order
        // flip" when the runtime bit offset is a multiple of 8. Three places can break that:
        //   - The field's own static layout (BITS028 / BITS040): if the cumulative compile-time
        //     bit offset already isn't byte-aligned, the swap operates across byte boundaries.
        //   - A preceding member with runtime-variable bit length (BITS033 / BITS041) — even when
        //     the static layout is byte-aligned, the *runtime* cursor can drift past the field's
        //     declared static start. We reject conservatively unless the dynamic member is proven
        //     to always advance by a byte-multiple amount.
        //   - The parent that embeds this type calls our serializer with a non-byte-aligned
        //     bitOffset — which the parent's analysis must reject in turn (BITS040/041 on the
        //     parent's side), giving us a transitive guarantee from the top-level entry.
        //
        // The same checks apply to nested fields whose type transitively contains an Endian
        // override: the inner type expects its bitOffset to be byte-aligned, and the parent's
        // static + dynamic layout is what delivers it.
        for (int idx = 0; idx < model.Fields.Count; idx++)
        {
            var f = model.Fields[idx];
            bool isDirectEndian = f.Endian != 0;
            string? nestedEndianTypeFullName = null;
            string? nestedKind = null; // human-readable "nested member" / "list element" / "polymorphic mapping"
            if (!isDirectEndian)
            {
                // Use the per-field flags computed during this field's analysis from the actual
                // ITypeSymbol — that covers types in referenced assemblies (review round-12 P1).
                // ContainingAssembly-only reverse lookup is gone.
                if (f.NestedHasEndianOverride)
                {
                    nestedKind = f.NestedEndianKind;
                    nestedEndianTypeFullName = f.NestedEndianTypeFullName;
                }
                else if (f.IsPolymorphic && f.PolyMappings != null)
                {
                    foreach (var mapping in f.PolyMappings)
                    {
                        if (mapping.HasEndianOverride)
                        {
                            nestedKind = "polymorphic mapping";
                            nestedEndianTypeFullName = mapping.ConcreteTypeFullName;
                            break;
                        }
                    }
                }
            }
            bool isNestedTransitive = nestedEndianTypeFullName != null;
            if (!isDirectEndian && !isNestedTransitive) continue;

            bool byteAlignedOffset = (f.BitStartIndex % 8) == 0;

            if (isDirectEndian)
            {
                bool isScalar = f.IsNumericOrEnum
                                && !f.IsList
                                && !f.IsFixedString
                                && !f.IsTerminatedString
                                && !f.IsNestedType
                                && !f.IsPolymorphic
                                && !f.IsTypeParameter;
                bool byteMultipleWidth = f.BitLength == 8 || f.BitLength == 16 || f.BitLength == 32 || f.BitLength == 64;

                if (!isScalar || !byteAlignedOffset || !byteMultipleWidth)
                {
                    string endianName = f.Endian == 1 ? "Big" : f.Endian == 2 ? "Little" : f.Endian.ToString();
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldEndianRequiresByteAlignedScalar,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, endianName,
                            f.BitStartIndex, f.BitLength,
                            f.IsList, f.IsFixedString || f.IsTerminatedString, f.IsNestedType || f.IsPolymorphic)
                    };
                }
            }
            else
            {
                // Nested transitive: the inner type must be entered at a byte-aligned offset.
                // For lists, the per-element stride must also be a byte multiple, otherwise odd-
                // indexed elements would land at non-byte-aligned offsets even if the field start
                // is fine.
                bool listElementByteAligned = !f.IsList
                    || (f.ListElementBitLength > 0 && (f.ListElementBitLength % 8) == 0);
                if (!byteAlignedOffset || !listElementByteAligned)
                {
                    string extra = !listElementByteAligned
                        ? $", and the list element bit width ({f.ListElementBitLength}) is not a multiple of 8"
                        : "";
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.NestedEndianRequiresByteAlignedOffset,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, nestedEndianTypeFullName, nestedKind,
                            f.BitStartIndex, (f.BitStartIndex % 8), extra)
                    };
                }
            }

            // BITS033 / BITS039: look at every preceding dynamic-length member for byte alignment safety.
            //
            // Review P2: a dynamic field whose runtime size is always a multiple of 8 bits (e.g.
            // [BitTerminatedString]: encoded bytes + 1-byte NUL; List<byte>/List<ushort> with
            // byte-multiple element width) keeps subsequent fields on byte boundaries — switching
            // helpers there is still a byte-order flip, not a corruption. We reject only when the
            // dynamic content can change the byte alignment of subsequent fields.
            //
            // Base-type dynamic length is conservatively rejected: we'd have to recursively prove
            // the base's runtime size is always byte-aligned, which isn't tracked at this layer.
            string? dynamicCulpritName = null;
            if (model.BaseHasDynamicLength)
                dynamicCulpritName = "base type";
            else
            {
                for (int j = 0; j < idx; j++)
                {
                    var prev = model.Fields[j];
                    if (!IsIncludeFieldDynamic(prev)) continue; // static field — offset cursor is precise
                    var cls = ClassifyIncludeAlignment(prev, symbol.ContainingAssembly);
                    if (cls != FieldAlignmentClass.Aligned)
                    {
                        dynamicCulpritName = prev.MemberName;
                        break;
                    }
                }
            }
            if (dynamicCulpritName != null)
            {
                if (isDirectEndian)
                {
                    string endianName = f.Endian == 1 ? "Big" : f.Endian == 2 ? "Little" : f.Endian.ToString();
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldEndianAfterDynamicContent,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, endianName, dynamicCulpritName)
                    };
                }
                else
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.NestedEndianAfterDynamicContent,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, nestedEndianTypeFullName, nestedKind, dynamicCulpritName)
                    };
                }
            }
        }

        // Validate PadIfShort: requires primitive element type
        foreach (var f in model.Fields)
        {
            if (f.PadIfShort)
            {
                if (!f.IsList || f.ListElementIsNested || f.ListElementIsManualBitSerializable || f.ListElementIsTypeParameter)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.PadIfShortMustBePrimitiveElement,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name)
                    };
                }
            }
        }

        // Validate ConsumeRemaining: must be on last field, list element must be primitive
        {
            int lastConsumeIdx = -1;
            for (int i = 0; i < model.Fields.Count; i++)
            {
                if (model.Fields[i].ConsumeRemaining) lastConsumeIdx = i;
            }
            if (lastConsumeIdx >= 0)
            {
                var f = model.Fields[lastConsumeIdx];
                bool invalid = lastConsumeIdx != model.Fields.Count - 1
                    || !f.IsList
                    || f.ListElementIsNested
                    || f.ListElementIsManualBitSerializable
                    || f.ListElementIsTypeParameter;
                if (invalid)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.ConsumeRemainingMustBeLast,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name)
                    };
                }
            }
        }

        // BITS024..027 + BITS051..052: validate RelationKind=ByteLength usage (Enhancement A + B,
        // T4 nested-type extension). The base predicate is "must be a list OR a nested [BitSerialize]
        // type whose serialized size is byte-aligned" — both shapes need a count that matches the
        // bytes actually written.
        foreach (var f in model.Fields)
        {
            if (f.RelationKind != 1) continue; // 1 == ByteLength

            // BITS027 (now relaxed): list/array OR nested [BitSerialize] (incl. type-parameter and
            // manual IBitSerializable carriers). Anything else still rejected.
            bool isNestedCarrier = f.IsNestedType || f.IsTypeParameter;
            if (!f.IsList && !isNestedCarrier)
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.ByteLengthRequiresListOrArray,
                        symbol.Locations.FirstOrDefault(),
                        f.MemberName, symbol.Name)
                };
            }

            // BITS025: conflicts with FixedCount or ConsumeRemaining (list-only; nested types don't
            // have FixedCount, but guard anyway to keep the predicate uniform).
            if (f.FixedCount.HasValue || f.ConsumeRemaining)
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.ByteLengthConflictsWithFixedOrConsume,
                        symbol.Locations.FirstOrDefault(),
                        f.MemberName, symbol.Name)
                };
            }

            if (f.IsList)
            {
                // BITS026: static-stride elements (numeric/enum, static nested) must be a POSITIVE
                // multiple of 8. A zero-width static element would make the while-loop unable to
                // advance at runtime (0-bit [BitSerialize] types such as CTCS/ETCS placeholders).
                // Dynamic-length elements are validated at runtime (budget under-run / over-run and
                // the emitter's "_consumed <= 0" guard).
                bool elemIsDynamic = f.ListElementHasDynamicLength
                                      || (f.ListElementIsManualBitSerializable && f.ListElementBitLength == 0);
                if (!elemIsDynamic && (f.ListElementBitLength <= 0 || (f.ListElementBitLength % 8) != 0))
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.ByteLengthElementNotByteAligned,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, f.ListElementBitLength)
                    };
                }
            }
            else
            {
                // T4 nested carrier: if the nested type has a *static* bit length we can verify
                // byte-alignment at compile time (BITS051). For dynamic nested types the runtime
                // serializer guard catches non-byte-aligned writes.
                if (!f.IsPotentiallyDynamic && !f.IsTypeParameter && f.BitLength > 0 && (f.BitLength % 8) != 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.ByteLengthNestedNotByteAligned,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, f.BitLength)
                    };
                }

                // Codex review round-6 P2 (BITS055): explicit [BitField(N)] on a dynamic nested
                // carrier that ALSO uses RelationKind=ByteLength can never round-trip. The serializer
                // back-fills the length from the static slot size (N/8), then the nested type writes
                // its runtime size (possibly different). The T4 nested deserializer
                // (EmitNestedByteLengthVerify) asserts `consumedBits == declaredBytes * 8` and throws
                // InvalidDataException on any mismatch. BITS023 currently emits a *warning* about
                // pinning a dynamic nested type to a fixed slot — when that slot is the byte budget
                // for a peer length field, escalate to a hard error.
                //
                // Detection: NestedTypeHasDynamicContent==true (set by the field analysis branches
                // unconditionally regardless of explicit-BitLength override) AND the field's runtime
                // size is *not* tracked dynamically (i.e. !IsPotentiallyDynamic — explicit-BitLength
                // suppressed the dynamic flag). Type parameters always go through GetTotalBitLength
                // so they don't hit this corruption path.
                if (!f.IsTypeParameter && !f.IsPotentiallyDynamic
                    && f.NestedTypeHasDynamicContent && f.BitLength > 0)
                {
                    string carrierKind = f.IsManualBitSerializable
                        ? "manual IBitSerializable"
                        : "nested [BitSerialize]";
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.ByteLengthFixedSlotOnDynamicNested,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, carrierKind,
                            f.RelatedMemberName ?? "", f.BitLength, f.BitLength / 8)
                    };
                }
            }

            // BITS024: if converter is supplied it must provide both directions.
            if (f.ValueConverterTypeFullName != null
                && (!f.ValueConverterHasSerialize || !f.ValueConverterHasDeserialize))
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.ByteLengthConverterMissingDirection,
                        symbol.Locations.FirstOrDefault(),
                        f.MemberName, symbol.Name, f.ValueConverterTypeFullName)
                };
            }

            // T4 (BITS052): the related length field must be declared earlier in the type so
            // the deserializer can read it before the carrier (it has to know how many bytes to
            // consume). The list ByteLength path is fine even when the length field comes after,
            // because the list's count is independently derivable from the wire (consume-until-end),
            // but for a nested type we have no such fallback — the byte count IS the framing.
            if (isNestedCarrier && f.RelatedMemberName != null)
            {
                int relatedIdx = model.Fields.FindIndex(x => x.MemberName == f.RelatedMemberName);
                int carrierIdx = model.Fields.FindIndex(x => x.MemberName == f.MemberName);
                if (relatedIdx < 0 || relatedIdx >= carrierIdx)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.ByteLengthNestedLengthFieldOrdering,
                            symbol.Locations.FirstOrDefault(),
                            f.MemberName, symbol.Name, f.RelatedMemberName ?? "")
                    };
                }
            }
        }

        // Codex review P1: BITS053 — every [BitFieldRelated] (count / discriminator / byte-budget)
        // and [BitLengthFieldString] dependent reads the related field's wire value at
        // deserialize time, but deserialization runs in declaration order. If the related field is
        // declared AFTER the dependent, the wire value isn't on the property yet (and the
        // generator's `_wire_<name>` local doesn't exist) — the dependent silently uses the
        // default value (0 / null) and produces an empty list / wrong poly case / mis-sized
        // payload. BITS052 already covers the nested-ByteLength sub-case; this is the general
        // gate for list count / list ByteLength / polymorphic discriminator / length-field-string.
        for (int dependentIdx = 0; dependentIdx < model.Fields.Count; dependentIdx++)
        {
            var dep = model.Fields[dependentIdx];
            string? referencedName = null;
            // Codex review round-5 P2: include nested-type and type-parameter ByteLength carriers
            // (T4). The original condition `IsList || IsPolymorphic` missed them, so a
            // [BitFieldRelated(nameof(Len), ByteLength)] on a nested [BitSerialize] type could pair
            // with [BitFieldValue(...)] on Len and silently corrupt wire output (backfill writes
            // real byte count, primitive write overwrites with constant). The same broad set used
            // by ComputeReferencedFieldNames in DeserializerEmitter — keep them in sync.
            if (dep.RelatedMemberName != null
                && (dep.IsList || dep.IsPolymorphic
                    || ((dep.IsNestedType || dep.IsTypeParameter) && dep.RelationKind == 1)))
            {
                referencedName = dep.RelatedMemberName;
            }
            else if (dep.IsLengthFieldString && dep.LengthFieldMemberName != null)
            {
                // LengthFieldString's own BITS047 already enforces ordering via model.Fields.Find
                // at parse time, but emit this check too so the diagnostic IDs are consistent for
                // downstream tooling.
                referencedName = dep.LengthFieldMemberName;
            }
            if (referencedName == null) continue;

            int relatedIdx = model.Fields.FindIndex(x => x.MemberName == referencedName);
            if (relatedIdx >= 0 && relatedIdx >= dependentIdx)
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.RelatedFieldDeclaredAfterDependent,
                        symbol.Locations.FirstOrDefault(),
                        dep.MemberName, symbol.Name, referencedName)
                };
            }

            // Codex review round-4 P2: the inverse of BITS044. The referenced carrier must NOT
            // have [BitFieldValue(...)]. At serialize time EmitAutoBackfill / EmitLengthFieldStringBackfill
            // writes the *real* dependent size into the carrier; then EmitPrimitiveSerialize for the
            // carrier overwrites that with the pinned constant. Wire ends up holding the constant
            // (e.g. magic 0x7E) but the dependent payload bytes reflect the real size — deserialize
            // reads the constant as the budget and either truncates or mis-parses subsequent fields.
            //
            // BITS044 only catches the same-field case ([BitFieldValue] on a field that ALSO carries
            // [BitFieldRelated]/[BitFieldCount]); the cross-field "carrier referenced by dependent"
            // case has no diagnostic and silently corrupts wire output.
            if (relatedIdx >= 0)
            {
                var carrier = model.Fields[relatedIdx];
                if (carrier.HasConstantValue)
                {
                    string referenceKind = dep.IsLengthFieldString ? "string byte-count carrier"
                        : dep.IsPolymorphic ? "polymorphic discriminator"
                        : ((dep.IsNestedType || dep.IsTypeParameter) && dep.RelationKind == 1) ? "nested byte-length carrier"
                        : (dep.RelationKind == 1 ? "byte-length carrier" : "count carrier");
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.FieldValueOnReferencedCarrier,
                            symbol.Locations.FirstOrDefault(),
                            carrier.MemberName, symbol.Name,
                            $"0x{unchecked((ulong)carrier.ConstantValue):X}",
                            dep.MemberName, referenceKind)
                    };
                }
            }
        }

        // BITS022: reject CRC in types whose [BitSerialize] base is dynamic-length.
        // Compile-time bit offsets used by the CRC emit would drift at runtime.
        if (model.HasBitSerializableBaseType && model.BaseHasDynamicLength
            && model.Fields.Any(f => f.IsCrcResult))
        {
            return new AnalyzeResult
            {
                Diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.CrcInTypeWithDynamicBase,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name)
            };
        }

        // BITS021: [BitCrc] must not combine with [BitFieldRelated] (converter output gets clobbered).
        foreach (var f in model.Fields)
        {
            if (f.IsCrcResult && (f.RelatedMemberName != null || f.ValueConverterTypeFullName != null))
            {
                return new AnalyzeResult
                {
                    Diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.CrcFieldCannotCombineWithConverterOrRelated,
                        symbol.Locations.FirstOrDefault(),
                        f.MemberName, symbol.Name)
                };
            }
        }

        // Aggregate CRC groups and validate
        {
            var includesByTarget = new Dictionary<string, List<BitFieldModel>>();
            foreach (var f in model.Fields)
            {
                if (f.CrcTargetFieldName != null)
                {
                    if (!includesByTarget.TryGetValue(f.CrcTargetFieldName, out var list))
                    {
                        list = new List<BitFieldModel>();
                        includesByTarget[f.CrcTargetFieldName] = list;
                    }
                    list.Add(f);
                }
            }

            foreach (var crcField in model.Fields)
            {
                if (!crcField.IsCrcResult) continue;

                // Validate CRC field byte alignment
                if ((crcField.BitStartIndex % 8) != 0 || (crcField.BitLength % 8) != 0 || crcField.BitLength == 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcFieldMustBeByteAligned,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, symbol.Name)
                    };
                }

                // Resolve algorithm type and validate IBitCrcAlgorithm implementation
                crcAlgoByField.TryGetValue(crcField.MemberName, out var algoSym);
                int algoBitWidth = 0;
                if (algoSym == null
                    || !algoSym.AllInterfaces.Any(i => i.ToDisplayString() == "BitSerializer.IBitCrcAlgorithm")
                    || !algoSym.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcAlgorithmInvalid,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, algoSym?.Name ?? "<unknown>")
                    };
                }
                // Try to read BitWidth from a literal property getter if possible; otherwise assume matches field length
                algoBitWidth = TryGetCrcBitWidth(algoSym) ?? crcField.BitLength;
                if (algoBitWidth != crcField.BitLength)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcFieldMustBeByteAligned,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, symbol.Name)
                    };
                }

                // WholeBuffer mode: CRC covers the entire type buffer (totalBytes - SkipHead - SkipTail).
                // Bypasses all [BitCrcInclude] aggregation and BITS017 alignment checks — the user opts
                // into runtime byte alignment by virtue of choosing this mode for protocols whose CRC is
                // computed across dynamic/polymorphic content (e.g. DMI's UartCrc16(bytes, 1, len-4)).
                if (crcField.CrcWholeBuffer)
                {
                    // BITS029: WholeBuffer is mutually exclusive with [BitCrcInclude].
                    if (includesByTarget.TryGetValue(crcField.MemberName, out var conflictingIncludes)
                        && conflictingIncludes.Count > 0)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcWholeBufferConflictsWithInclude,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name, conflictingIncludes.Count)
                        };
                    }
                    // BITS030: skip offsets must be non-negative.
                    if (crcField.CrcSkipHeadBytes < 0 || crcField.CrcSkipTailBytes < 0)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcWholeBufferSkipNegative,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name,
                                crcField.CrcSkipHeadBytes, crcField.CrcSkipTailBytes)
                        };
                    }

                    // BITS034: CRC field's static byte slot must be fully inside SkipHead or SkipTail.
                    // Otherwise CRC.Update() reads the CRC field's own bytes (uninitialized or stale)
                    // and produces protocol-invalid output (review P2 — codex flagged 16-bit CRC + SkipTail=1).
                    int crcSlotStartByte = crcField.BitStartIndex / 8;
                    int crcSlotEndByteExcl = (crcField.BitStartIndex + crcField.BitLength) / 8;
                    int staticTotalBytes = model.TotalBitLength / 8;
                    int bytesFromCrcStartToStaticEnd = (model.TotalBitLength - crcField.BitStartIndex) / 8;

                    bool crcStaticallyInHead = crcSlotEndByteExcl <= crcField.CrcSkipHeadBytes;
                    bool crcStaticallyInTail = crcField.CrcSkipTailBytes >= bytesFromCrcStartToStaticEnd;

                    if (!crcStaticallyInHead && !crcStaticallyInTail)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcWholeBufferDoesNotCoverCrcField,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name,
                                crcField.CrcSkipHeadBytes, crcField.CrcSkipTailBytes,
                                crcSlotStartByte, crcSlotEndByteExcl,
                                staticTotalBytes,
                                bytesFromCrcStartToStaticEnd)
                        };
                    }

                    // BITS035: BITS034's "CRC slot inside SkipHead/SkipTail" check is purely static.
                    // It only stays sound at runtime if no dynamic field can shift the slot off its
                    // assumed side (review round-7 P1):
                    //  - head mode: any LEADING dynamic field shifts the CRC slot past SkipHeadBytes
                    //  - tail mode: any TRAILING dynamic field extends the buffer end, so SkipTailBytes
                    //    no longer covers the CRC slot
                    // We accept the layout only when at least one side is both statically covered AND
                    // free of the dynamic content that would invalidate that side's static reasoning.
                    int crcIdx = model.Fields.IndexOf(crcField);
                    string? leadingDynamicCulprit = null;
                    for (int k = 0; k < crcIdx; k++)
                    {
                        if (IsIncludeFieldDynamic(model.Fields[k]))
                        {
                            leadingDynamicCulprit = model.Fields[k].MemberName;
                            break;
                        }
                    }
                    string? trailingDynamicCulprit = null;
                    for (int k = crcIdx + 1; k < model.Fields.Count; k++)
                    {
                        if (IsIncludeFieldDynamic(model.Fields[k]))
                        {
                            trailingDynamicCulprit = model.Fields[k].MemberName;
                            break;
                        }
                    }
                    bool headModeSound = crcStaticallyInHead && leadingDynamicCulprit == null;
                    bool tailModeSound = crcStaticallyInTail && trailingDynamicCulprit == null;
                    if (!headModeSound && !tailModeSound)
                    {
                        // Report whichever side the user appeared to intend.
                        string culprit = crcStaticallyInHead ? leadingDynamicCulprit! : trailingDynamicCulprit!;
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcWholeBufferTrailingDynamicField,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name, culprit)
                        };
                    }

                    model.CrcGroups.Add(new CrcGroup
                    {
                        TargetFieldName = crcField.MemberName,
                        AlgorithmTypeFullName = crcField.CrcAlgorithmTypeFullName ?? "",
                        BitWidth = crcField.BitLength,
                        InitialValue = crcField.CrcInitialValue,
                        ValidateOnDeserialize = crcField.CrcValidateOnDeserialize,
                        CrcFieldBitOffset = crcField.BitStartIndex,
                        CrcFieldBitLength = crcField.BitLength,
                        CrcFieldTypeName = crcField.IsEnum ? crcField.EnumUnderlyingTypeName! : crcField.MemberTypeName,
                        IsWholeBuffer = true,
                        SkipHeadBytes = crcField.CrcSkipHeadBytes,
                        SkipTailBytes = crcField.CrcSkipTailBytes,
                    });
                    continue;
                }

                // Validate include range: must have at least one include
                if (!includesByTarget.TryGetValue(crcField.MemberName, out var includes) || includes.Count == 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcIncludeRangeInvalid,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, symbol.Name)
                    };
                }

                // Declaration-order contiguity: the include fields must form a contiguous subsequence of model.Fields.
                var includeSet = new HashSet<BitFieldModel>(includes);
                int firstIdx = int.MaxValue;
                int lastIdx = -1;
                for (int k = 0; k < model.Fields.Count; k++)
                {
                    if (includeSet.Contains(model.Fields[k]))
                    {
                        if (k < firstIdx) firstIdx = k;
                        if (k > lastIdx) lastIdx = k;
                    }
                }
                for (int k = firstIdx; k <= lastIdx; k++)
                {
                    if (!includeSet.Contains(model.Fields[k]))
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcIncludeRangeInvalid,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name)
                        };
                    }
                }

                var orderedIncludes = new List<BitFieldModel>();
                for (int k = firstIdx; k <= lastIdx; k++)
                    orderedIncludes.Add(model.Fields[k]);

                // Range start must be byte-aligned in the static nominal layout.
                int rangeStartBit = orderedIncludes[0].BitStartIndex;
                if ((rangeStartBit % 8) != 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcIncludeRangeInvalid,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, symbol.Name)
                    };
                }

                // Classify each include for byte alignment and tally nominal static bits for the pure-static fast path.
                bool hasDynamicInclude = false;
                int nominalTotalBits = 0;
                foreach (var inc in orderedIncludes)
                {
                    var cls = ClassifyIncludeAlignment(inc, symbol.ContainingAssembly);
                    if (cls == FieldAlignmentClass.Rejected)
                    {
                        return new AnalyzeResult
                        {
                            Diagnostic = Diagnostic.Create(
                                DiagnosticDescriptors.CrcIncludeRangeInvalid,
                                symbol.Locations.FirstOrDefault(),
                                crcField.MemberName, symbol.Name)
                        };
                    }
                    if (IsIncludeFieldDynamic(inc))
                        hasDynamicInclude = true;
                    nominalTotalBits += GetIncludeFieldStaticBits(inc);
                }

                // Pure-static group: cumulative bit span must end byte-aligned.
                if (!hasDynamicInclude && ((rangeStartBit + nominalTotalBits) % 8) != 0)
                {
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcIncludeRangeInvalid,
                            symbol.Locations.FirstOrDefault(),
                            crcField.MemberName, symbol.Name)
                    };
                }

                model.CrcGroups.Add(new CrcGroup
                {
                    TargetFieldName = crcField.MemberName,
                    AlgorithmTypeFullName = crcField.CrcAlgorithmTypeFullName ?? "",
                    BitWidth = crcField.BitLength,
                    InitialValue = crcField.CrcInitialValue,
                    ValidateOnDeserialize = crcField.CrcValidateOnDeserialize,
                    CrcFieldBitOffset = crcField.BitStartIndex,
                    CrcFieldBitLength = crcField.BitLength,
                    CrcFieldTypeName = crcField.IsEnum ? crcField.EnumUnderlyingTypeName! : crcField.MemberTypeName,
                    IncludeStartByte = hasDynamicInclude ? 0 : rangeStartBit / 8,
                    IncludeEndByte = hasDynamicInclude ? 0 : (rangeStartBit + nominalTotalBits) / 8,
                    HasDynamicInclude = hasDynamicInclude,
                    FirstIncludeMemberName = orderedIncludes[0].MemberName,
                    LastIncludeMemberName = orderedIncludes[orderedIncludes.Count - 1].MemberName
                });
            }

            // Validate every [BitCrcInclude] references a valid [BitCrc] result field
            foreach (var kvp in includesByTarget)
            {
                var target = model.Fields.Find(f => f.MemberName == kvp.Key && f.IsCrcResult);
                if (target == null)
                {
                    var firstInclude = kvp.Value[0];
                    return new AnalyzeResult
                    {
                        Diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.CrcTargetFieldNotFound,
                            symbol.Locations.FirstOrDefault(),
                            firstInclude.MemberName, kvp.Key, symbol.Name)
                    };
                }
            }
        }

        return new AnalyzeResult { Model = model, Warnings = warnings };
    }

    /// <summary>
    // FindNestedEndianTypeForField was removed in review round-12: it used FindTypeByFullName which
    // is bound to a single IAssemblySymbol, so types declared in referenced assemblies fell through
    // as "no Endian override". The detection now happens at field-analysis time using the actual
    // ITypeSymbol passed to HasEndianOverrideRecursive, and the result is cached on
    // BitFieldModel.NestedHasEndianOverride / PolyMapping.HasEndianOverride.

    /// <summary>
    /// True if <paramref name="type"/> has any [BitField(Endian = Big | Little)] override, either
    /// directly on its members, in its [BitSerialize] base chain, or transitively through a nested
    /// [BitSerialize] member type (including list elements and [BitPoly] mappings). Manual
    /// IBitSerializable members and generic type parameters are treated as having no Endian
    /// override — the generator cannot introspect them.
    /// </summary>
    private static bool HasEndianOverrideRecursive(ITypeSymbol type, HashSet<string> visited)
    {
        if (type is not INamedTypeSymbol named) return false;

        var key = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!visited.Add(key)) return false; // already in progress on this branch — break cycles

        var baseT = named.BaseType;
        while (baseT != null && baseT.SpecialType != SpecialType.System_Object)
        {
            if (TypeOwnHasEndianOverride(baseT, visited)) return true;
            baseT = baseT.BaseType;
        }

        return TypeOwnHasEndianOverride(named, visited);
    }

    private static bool TypeOwnHasEndianOverride(INamedTypeSymbol type, HashSet<string> visited)
    {
        foreach (var member in GetSerializableMembers(type))
        {
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute")) continue;
            var memberType = GetMemberType(member);
            if (memberType == null) continue;

            // String attributes don't accept Endian; skip their carriers entirely.
            if (GetAttribute(member, "BitSerializer.BitFixedStringAttribute") != null) continue;
            if (GetAttribute(member, "BitSerializer.BitTerminatedStringAttribute") != null) continue;

            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            if (bitFieldAttr == null) continue;

            // Direct Endian override on this member.
            foreach (var namedArg in bitFieldAttr.NamedArguments)
            {
                if (namedArg.Key == "Endian" && namedArg.Value.Value is int endianRaw && endianRaw != 0)
                    return true;
            }

            // Recurse into nested [BitSerialize] types (list elements, composition). Manual
            // IBitSerializable / type-parameter targets are opaque and treated as no-Endian.
            if (IsListType(memberType, out var elemType, out _) && elemType != null
                && HasAttribute(elemType, "BitSerializer.BitSerializeAttribute"))
            {
                if (HasEndianOverrideRecursive(elemType, visited)) return true;
            }
            else if (HasAttribute(memberType, "BitSerializer.BitSerializeAttribute"))
            {
                if (HasEndianOverrideRecursive(memberType, visited)) return true;
            }

            // Polymorphic mappings — drill into each [BitPoly] concrete target.
            foreach (var polyAttr in GetAttributes(member, "BitSerializer.BitPolyAttribute"))
            {
                if (polyAttr.ConstructorArguments.Length >= 2
                    && polyAttr.ConstructorArguments[1].Value is INamedTypeSymbol concrete
                    && HasEndianOverrideRecursive(concrete, visited))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the nominal static bit footprint of a field participating in a CRC include range,
    /// matching the layout calculation in <see cref="Analyze"/> (so pure-static groups can tally
    /// IncludeStartByte/IncludeEndByte). Dynamic-length fields that do not advance the static
    /// layout cursor return 0.
    /// </summary>
    private static int GetIncludeFieldStaticBits(BitFieldModel f)
    {
        if (f.IsTerminatedString) return 0;
        if (f.IsLengthPrefixString) return 0;
        if (f.IsLengthFieldString) return 0;
        if (f.IsTypeParameter) return 0;
        if (f.IsList)
        {
            if (f.ConsumeRemaining) return 0;
            if (!f.FixedCount.HasValue) return 0;
            if (f.ListElementHasDynamicLength) return 0;
            if (f.ListElementBitLength == 0) return 0;
            return f.FixedCount.Value * f.ListElementBitLength;
        }
        if (f.IsPolymorphic) return f.PolymorphicBitLength > 0 ? f.PolymorphicBitLength : f.BitLength;
        if (f.IsPotentiallyDynamic) return 0;
        return f.BitLength;
    }

    /// <summary>
    /// Classification for runtime byte alignment of a CRC-include field.
    /// </summary>
    private enum FieldAlignmentClass
    {
        /// <summary>Runtime byte alignment is statically provable.</summary>
        Aligned,
        /// <summary>Provably violates byte alignment — emit BITS017 at compile time.</summary>
        Rejected,
        /// <summary>Can't prove at compile time; defer to an emitted runtime assertion.</summary>
        RuntimeOnly
    }

    /// <summary>
    /// True if the include field has runtime-variable serialized bit length.
    /// </summary>
    private static bool IsIncludeFieldDynamic(BitFieldModel f)
    {
        if (f.IsTerminatedString) return true;
        if (f.IsLengthPrefixString) return true;
        if (f.IsLengthFieldString) return true;
        if (f.IsTypeParameter) return true;
        if (f.IsPotentiallyDynamic) return true;
        if (f.IsList)
        {
            if (f.ConsumeRemaining) return true;
            if (!f.FixedCount.HasValue) return true;
            if (f.ListElementHasDynamicLength) return true;
            // Review round-4 follow-up P1: fixed-count list whose element is a manual IBitSerializable
            // without an explicit [BitField(N)] element bit width — SerializerEmitter falls back to the
            // runtime-offset path (EmitListSerialize "Dynamic: use runtime offset tracking via interface
            // dispatch"), so the trailing fields' real start can drift. WholeBuffer CRC relies on this
            // predicate to detect "dynamic-after-CRC" (BITS035), so the omission would let such a list
            // sit after the CRC and silently corrupt the CRC range. The same predicate gates the
            // LengthPrefixString byte-alignment classifier (BITS038/BITS039) and the BITS033
            // per-field Endian drift detector, so keeping it correct here also keeps both downstream
            // diagnostics sound.
            if (f.ListElementIsManualBitSerializable && f.ListElementBitLength == 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Decides whether a CRC-include field contributes a byte-aligned chunk of bytes at runtime.
    /// Static cases are verified by bit-width arithmetic; polymorphic auto-length is verified by
    /// inspecting every concrete [BitPoly] mapping; non-provable dynamic cases fall through to
    /// runtime assertion emitted by the serializer.
    /// </summary>
    private static FieldAlignmentClass ClassifyIncludeAlignment(BitFieldModel f, IAssemblySymbol assembly)
    {
        // Pure static (non-dynamic) includes: require the nominal footprint be byte-aligned.
        if (!IsIncludeFieldDynamic(f))
        {
            int effectiveBits = f.IsList && f.FixedCount.HasValue
                ? f.FixedCount.Value * f.ListElementBitLength
                : f.BitLength;
            return (effectiveBits % 8 == 0) ? FieldAlignmentClass.Aligned : FieldAlignmentClass.Rejected;
        }

        // Terminated string: encoded bytes + 1-byte terminator — always byte-aligned.
        if (f.IsTerminatedString)
            return FieldAlignmentClass.Aligned;

        // Length-prefix string: lengthBits (∈ {8,16,32}) + encoded bytes — both byte-aligned.
        if (f.IsLengthPrefixString)
            return FieldAlignmentClass.Aligned;

        // Length-field string: only encoded bytes here (the length lives in a peer field that was
        // already classified in a previous iteration). Always byte-aligned.
        if (f.IsLengthFieldString)
            return FieldAlignmentClass.Aligned;

        // Generic type parameter: concrete T not known at compile time.
        if (f.IsTypeParameter)
            return FieldAlignmentClass.RuntimeOnly;

        // Polymorphic (auto-length): every [BitPoly] concrete type must be byte-aligned and non-dynamic.
        if (f.IsPolymorphic && f.PolyMappings != null && f.PolyMappings.Count > 0)
        {
            // Explicit-bit-length polymorphic is a fixed slot and caught by the static branch above;
            // here we only handle the auto-length form (IsPotentiallyDynamic == true).
            foreach (var mapping in f.PolyMappings)
            {
                var concrete = FindTypeByFullName(assembly, mapping.ConcreteTypeFullName);
                if (concrete == null)
                    return FieldAlignmentClass.RuntimeOnly;
                if (HasDynamicLengthRecursive(concrete))
                    return FieldAlignmentClass.Rejected;
                if (CalculateNestedBitLength(concrete) % 8 != 0)
                    return FieldAlignmentClass.Rejected;
            }
            return FieldAlignmentClass.Aligned;
        }

        // Dynamic list.
        if (f.IsList)
        {
            // ConsumeRemaining fills to buffer end (byte-aligned by buffer construction).
            if (f.ConsumeRemaining)
                return FieldAlignmentClass.Aligned;
            // Manual IBitSerializable without explicit element bit length: stride is decided by user
            // code at runtime. ListElementBitLength=0 here means "unknown", not "zero"; and the manual
            // impl can write arbitrary bits, so element-type inspection isn't safe either.
            bool elemIsManualUnknownWidth = f.ListElementIsManualBitSerializable && f.ListElementBitLength == 0;
            if (!f.ListElementHasDynamicLength && !elemIsManualUnknownWidth)
            {
                return (f.ListElementBitLength % 8 == 0)
                    ? FieldAlignmentClass.Aligned
                    : FieldAlignmentClass.Rejected;
            }
            if (elemIsManualUnknownWidth)
            {
                // Can't statically prove alignment for manual IBitSerializable — defer to runtime.
                return FieldAlignmentClass.RuntimeOnly;
            }
            // Dynamic [BitSerialize] element: attempt static proof via element type inspection.
            if (f.ListElementTypeFullName != null)
            {
                var elemType = FindTypeByFullName(assembly, f.ListElementTypeFullName);
                if (elemType != null
                    && !HasDynamicLengthRecursive(elemType)
                    && CalculateNestedBitLength(elemType) % 8 == 0)
                    return FieldAlignmentClass.Aligned;
            }
            return FieldAlignmentClass.RuntimeOnly;
        }

        // Nested [BitSerialize] with dynamic content, or manual IBitSerializable without explicit length.
        return FieldAlignmentClass.RuntimeOnly;
    }

    /// <summary>
    /// Attempts to read a constant BitWidth from the algorithm type. Returns null if non-literal.
    /// </summary>
    private static int? TryGetCrcBitWidth(INamedTypeSymbol algoSym)
    {
        // Walk declared properties named BitWidth with an expression-bodied getter returning an int literal
        foreach (var m in algoSym.GetMembers("BitWidth"))
        {
            if (m is IPropertySymbol prop)
            {
                foreach (var sr in prop.DeclaringSyntaxReferences)
                {
                    var syn = sr.GetSyntax();
                    // Look for "=> <int literal>" or "{ get { return <literal>; } }"
                    var text = syn.ToString();
                    // Cheap heuristic: match "=> 16" or "=> 32"
                    var idx = text.IndexOf("=>");
                    if (idx >= 0)
                    {
                        var tail = text.Substring(idx + 2).Trim().TrimEnd(';', '}').Trim();
                        if (int.TryParse(tail, out var v)) return v;
                    }
                    var retIdx = text.IndexOf("return");
                    if (retIdx >= 0)
                    {
                        var tail = text.Substring(retIdx + 6).Trim().TrimEnd(';', '}').Trim();
                        if (int.TryParse(tail, out var v)) return v;
                    }
                }
            }
        }
        return null;
    }

    private static IReadOnlyList<ISymbol> GetSerializableMembers(INamedTypeSymbol typeSymbol)
    {
        var result = new List<ISymbol>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol prop && !prop.IsStatic && !prop.IsIndexer &&
                prop.DeclaredAccessibility == Accessibility.Public)
            {
                result.Add(member);
            }
            else if (member is IFieldSymbol field && !field.IsStatic && !field.IsImplicitlyDeclared &&
                     field.DeclaredAccessibility == Accessibility.Public)
            {
                result.Add(member);
            }
        }
        return result;
    }

    private static ITypeSymbol? GetMemberType(ISymbol member)
    {
        return member switch
        {
            IPropertySymbol prop => prop.Type,
            IFieldSymbol field => field.Type,
            _ => null
        };
    }

    private static bool IsNumericOrEnum(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return true;
        return SupportedSpecialTypes.Contains(type.SpecialType);
    }

    private static bool IsListType(ITypeSymbol type, out ITypeSymbol? elementType, out bool isArray)
    {
        elementType = null;
        isArray = false;
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            isArray = true;
            return true;
        }
        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.ConstructedFrom.ToDisplayString().StartsWith("System.Collections.Generic.List"))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }
        return false;
    }

    private static int GetDefaultBitLength(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            var underlying = ((INamedTypeSymbol)type).EnumUnderlyingType!;
            return GetDefaultBitLength(underlying);
        }
        return type.SpecialType switch
        {
            SpecialType.System_Byte => 8,
            SpecialType.System_SByte => 8,
            SpecialType.System_Int16 => 16,
            SpecialType.System_UInt16 => 16,
            SpecialType.System_Int32 => 32,
            SpecialType.System_UInt32 => 32,
            SpecialType.System_Int64 => 64,
            SpecialType.System_UInt64 => 64,
            _ => 0
        };
    }

    private static int CalculateNestedBitLength(ITypeSymbol type)
    {
        var namedType = (INamedTypeSymbol)type;
        int total = 0;

        // Walk up the base type chain to include all ancestor bits
        if (namedType.BaseType != null &&
            namedType.BaseType.SpecialType != SpecialType.System_Object)
        {
            total += CalculateNestedBitLength(namedType.BaseType);
        }

        var members = GetSerializableMembers(namedType);
        foreach (var member in members)
        {
            var memberType = GetMemberType(member);
            if (memberType == null) continue;
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute")) continue;

            // Check for string attributes first
            var fixedStrAttr = GetAttribute(member, "BitSerializer.BitFixedStringAttribute");
            if (fixedStrAttr != null)
            {
                int byteLen = (int)fixedStrAttr.ConstructorArguments[0].Value!;
                foreach (var named in fixedStrAttr.NamedArguments)
                {
                    if (named.Key == "ByteLength" && named.Value.Value is int namedByteLen)
                        byteLen = namedByteLen;
                }
                total += byteLen * 8;
                continue;
            }
            var termStrAttr = GetAttribute(member, "BitSerializer.BitTerminatedStringAttribute");
            if (termStrAttr != null)
                continue; // dynamic, contributes 0 to static total
            var lpsStrAttr = GetAttribute(member, "BitSerializer.BitLengthPrefixStringAttribute");
            if (lpsStrAttr != null)
                continue; // dynamic, contributes 0 to static total

            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            if (bitFieldAttr == null) continue;

            int? explicitBitLength = GetBitLengthFromAttribute(bitFieldAttr);

            // Polymorphic fields: the declared type may be an abstract base (without [BitSerialize]),
            // so check [BitPoly] on the member itself, independent of the declared type.
            var memberPolyAttrs = GetAttributes(member, "BitSerializer.BitPolyAttribute");
            if (memberPolyAttrs.Count > 0 && !IsListType(memberType, out _, out _))
            {
                if (explicitBitLength.HasValue)
                {
                    total += explicitBitLength.Value;
                }
                else
                {
                    int maxBits = 0;
                    foreach (var polyAttr in memberPolyAttrs)
                    {
                        if (polyAttr.ConstructorArguments.Length >= 2)
                        {
                            var concreteType = polyAttr.ConstructorArguments[1].Value as INamedTypeSymbol;
                            if (concreteType != null)
                            {
                                int bits = CalculateNestedBitLength(concreteType);
                                if (bits > maxBits) maxBits = bits;
                            }
                        }
                    }
                    total += maxBits;
                }
                continue;
            }

            if (IsListType(memberType, out var elemType, out _))
            {
                var countAttr = GetAttribute(member, "BitSerializer.BitFieldCountAttribute");
                if (countAttr != null)
                {
                    int count = (int)countAttr.ConstructorArguments[0].Value!;
                    int elemBits;
                    if (IsNumericOrEnum(elemType!))
                        elemBits = explicitBitLength ?? GetDefaultBitLength(elemType!);
                    else if (ImplementsBitSerializable(elemType!) && !HasAttribute(elemType!, "BitSerializer.BitSerializeAttribute"))
                        elemBits = explicitBitLength ?? 0;
                    else
                        elemBits = CalculateNestedBitLength(elemType!);
                    total += count * elemBits;
                }
                // dynamic list contributes 0 to static total
            }
            else if (IsNumericOrEnum(memberType))
            {
                if (memberType.TypeKind == TypeKind.Enum)
                {
                    var underlying = ((INamedTypeSymbol)memberType).EnumUnderlyingType!;
                    total += explicitBitLength ?? GetDefaultBitLength(underlying);
                }
                else
                {
                    total += explicitBitLength ?? GetDefaultBitLength(memberType);
                }
            }
            else if (HasAttribute(memberType, "BitSerializer.BitSerializeAttribute"))
            {
                int nested = CalculateNestedBitLength(memberType);
                total += explicitBitLength ?? nested;
            }
            else if (ImplementsBitSerializable(memberType))
            {
                total += explicitBitLength ?? 0;
            }
        }
        return total;
    }

    private static int? GetBitLengthFromAttribute(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length > 0)
        {
            var val = attr.ConstructorArguments[0].Value;
            if (val is int intVal && intVal != int.MaxValue)
                return intVal;
        }
        return null;
    }

    private static bool HasAttribute(ISymbol symbol, string fullName)
    {
        if (symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == fullName))
            return true;

        // For constructed generic types in the same compilation, GetAttributes()
        // may return empty. Fall back to checking the syntax tree.
        var targetSymbol = symbol;
        if (symbol is INamedTypeSymbol namedType &&
            !SymbolEqualityComparer.Default.Equals(namedType, namedType.OriginalDefinition))
        {
            targetSymbol = namedType.OriginalDefinition;
        }

        // Extract the short attribute name from fully qualified name (e.g., "BitSerialize" from "BitSerializer.BitSerializeAttribute")
        var shortName = fullName;
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot >= 0) shortName = fullName.Substring(lastDot + 1);
        var shortNameNoSuffix = shortName.EndsWith("Attribute")
            ? shortName.Substring(0, shortName.Length - "Attribute".Length)
            : shortName;

        foreach (var syntaxRef in targetSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is TypeDeclarationSyntax typeDecl)
            {
                foreach (var attrList in typeDecl.AttributeLists)
                {
                    foreach (var attr in attrList.Attributes)
                    {
                        var name = attr.Name.ToString();
                        if (name == shortName || name == shortNameNoSuffix)
                            return true;
                    }
                }
            }
        }

        return false;
    }

    private static AttributeData? GetAttribute(ISymbol symbol, string fullName)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == fullName);
    }

    private static List<AttributeData> GetAttributes(ISymbol symbol, string fullName)
    {
        return symbol.GetAttributes().Where(a =>
            a.AttributeClass?.ToDisplayString() == fullName).ToList();
    }

    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            _ => type.Name
        };
    }

    private static bool HasDynamicLengthRecursive(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType) return false;

        // Check base type
        if (namedType.BaseType != null && namedType.BaseType.SpecialType != SpecialType.System_Object)
        {
            if (HasDynamicLengthRecursive(namedType.BaseType)) return true;
        }

        var members = GetSerializableMembers(namedType);
        foreach (var member in members)
        {
            var memberType = GetMemberType(member);
            if (memberType == null) continue;
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute")) continue;
            // Check for string attributes
            var fixedStrAttr2 = GetAttribute(member, "BitSerializer.BitFixedStringAttribute");
            var termStrAttr2 = GetAttribute(member, "BitSerializer.BitTerminatedStringAttribute");
            var lpsStrAttr2 = GetAttribute(member, "BitSerializer.BitLengthPrefixStringAttribute");
            // Codex review round-7 P1: [BitLengthFieldString] (T3) is also dynamic — without this
            // check, a parent containing a nested type that uses LFS would treat the nested type as
            // static, place subsequent fields at compile-time offsets, and corrupt the layout
            // whenever the LFS payload has non-zero length.
            var lfsStrAttr2 = GetAttribute(member, "BitSerializer.BitLengthFieldStringAttribute");

            if (termStrAttr2 != null) return true; // always dynamic
            if (lpsStrAttr2 != null) return true; // always dynamic
            if (lfsStrAttr2 != null) return true; // always dynamic (encoded bytes follow a peer length carrier)
            if (fixedStrAttr2 != null) continue; // fixed, not dynamic

            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            if (bitFieldAttr == null) continue;

            if (memberType is ITypeParameterSymbol) return true;

            if (IsListType(memberType, out var listElemType, out _))
            {
                var countAttr = GetAttribute(member, "BitSerializer.BitFieldCountAttribute");
                if (countAttr == null) return true; // dynamic list
                // Fixed-count list with manual IBitSerializable elements and no explicit bit length is dynamic
                if (listElemType != null && ImplementsBitSerializable(listElemType)
                    && !HasAttribute(listElemType, "BitSerializer.BitSerializeAttribute"))
                {
                    int? explBitLen = GetBitLengthFromAttribute(bitFieldAttr);
                    if (!explBitLen.HasValue) return true;
                }
                // Fixed-count list with [BitSerialize] elements that are themselves dynamic
                if (listElemType != null && HasAttribute(listElemType, "BitSerializer.BitSerializeAttribute"))
                {
                    if (HasDynamicLengthRecursive(listElemType)) return true;
                }
                continue;
            }

            var polyAttrs = GetAttributes(member, "BitSerializer.BitPolyAttribute");
            if (polyAttrs.Count > 0)
            {
                // Auto-length polymorphic: the runtime type determines the bit length,
                // so the containing type always has a dynamic length.
                int? explBits = GetBitLengthFromAttribute(bitFieldAttr);
                if (!explBits.HasValue) return true;
                foreach (var polyAttr in polyAttrs)
                {
                    if (polyAttr.ConstructorArguments.Length >= 2)
                    {
                        var concreteType = polyAttr.ConstructorArguments[1].Value as INamedTypeSymbol;
                        if (concreteType != null && HasDynamicLengthRecursive(concreteType))
                            return true;
                    }
                }
                continue;
            }

            if (ImplementsBitSerializable(memberType))
            {
                int? explBitLen = GetBitLengthFromAttribute(bitFieldAttr);
                if (!explBitLen.HasValue) return true; // dynamic
                continue;
            }

            if (HasAttribute(memberType, "BitSerializer.BitSerializeAttribute"))
            {
                if (HasDynamicLengthRecursive(memberType)) return true;
            }
        }

        return false;
    }

    private static bool ImplementsBitSerializable(ITypeSymbol type)
    {
        return type.AllInterfaces.Any(i =>
            i.ToDisplayString() == "BitSerializer.IBitSerializable");
    }

    /// <summary>
    /// Checks if the type declares its own SerializeContext() or DeserializeContext() methods
    /// (not just the default interface implementations from IBitSerializable).
    /// </summary>
    private static bool HasOwnContextMethods(ITypeSymbol type)
    {
        return type.GetMembers("SerializeContext").OfType<IMethodSymbol>()
                   .Any(m => m.Parameters.Length == 0 && !m.IsStatic)
            || type.GetMembers("DeserializeContext").OfType<IMethodSymbol>()
                   .Any(m => m.Parameters.Length == 0 && !m.IsStatic);
    }

    /// <summary>
    /// True when <paramref name="typeFullName"/> is one of the integer scalars accepted as a
    /// [BitLengthFieldString] length carrier (byte/sbyte/short/ushort/int/uint). We accept signed
    /// types because legacy protocol layouts often declare lengths as `int`, even though byte
    /// counts can't actually be negative; the serializer's overflow check still uses the
    /// unsigned-range maximum to prevent silent truncation.
    /// </summary>
    private static bool IsAllowedLengthFieldType(string typeFullName)
    {
        switch (typeFullName)
        {
            case "byte":
            case "sbyte":
            case "short":
            case "ushort":
            case "int":
            case "uint":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Bits needed to represent <paramref name="value"/> in two's-complement form. Used by BITS043
    /// to report the minimum BitLength the user needs to expand to.
    /// </summary>
    private static int ComputeBitsRequired(long value)
    {
        if (value == 0) return 1;
        if (value > 0)
        {
            int bits = 0;
            long v = value;
            while (v > 0) { v >>= 1; bits++; }
            return bits;
        }
        // negative: two's-complement representation
        int n = 0;
        long u = ~value; // bits needed for the positive complement, plus 1 for sign
        while (u > 0) { u >>= 1; n++; }
        return n + 1;
    }

    private static INamedTypeSymbol? FindTypeByFullName(IAssemblySymbol assembly, string fullName)
    {
        // Remove "global::" prefix if present
        var name = fullName.Replace("global::", "");

        // Strip arity suffix from generic types (e.g. "List<byte>" -> "List`1") — we never resolve
        // open generics from a display name. For the closed-generic resolution that we'd need, the
        // caller should fall back to the symbol from analysis time. For our internal callers this
        // is fine because we only resolve nominally [BitSerialize] types and their poly mappings.
        var sym = assembly.GetTypeByMetadataName(name);
        if (sym != null) return sym;

        // CLR metadata uses '+' as the nested-type separator, while SymbolDisplayFormat.Fully-
        // QualifiedFormat emits '.'. Walk the dotted name from right to left, progressively
        // converting trailing dots to '+' until a metadata lookup succeeds.
        var parts = name.Split('.');
        for (int splitPoint = parts.Length - 1; splitPoint > 0; splitPoint--)
        {
            var head = string.Join(".", parts, 0, splitPoint);
            var tail = string.Join("+", parts, splitPoint, parts.Length - splitPoint);
            sym = assembly.GetTypeByMetadataName(head + "+" + tail);
            if (sym != null) return sym;
        }
        return null;
    }
}
