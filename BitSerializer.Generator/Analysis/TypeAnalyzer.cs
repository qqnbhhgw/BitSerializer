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

    public bool Equals(AnalyzeResult? other)
    {
        if (other is null) return false;
        if (Model is not null && other.Model is not null) return Model.Equals(other.Model);
        if (Diagnostic is not null && other.Diagnostic is not null)
            return Diagnostic.Id == other.Diagnostic.Id
                && Diagnostic.Location == other.Diagnostic.Location;
        return false;
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
                IsClass = containingType.TypeKind == TypeKind.Class
            });
            containingType = containingType.ContainingType;
        }

        // Check if base type also has [BitSerialize]
        bool hasBitSerializableBase = false;
        int baseBitLength = 0;
        if (symbol.BaseType != null && symbol.BaseType.SpecialType != SpecialType.System_Object)
        {
            hasBitSerializableBase = HasAttribute(symbol.BaseType, "BitSerializer.BitSerializeAttribute");
            if (hasBitSerializableBase)
            {
                baseBitLength = CalculateNestedBitLength(symbol.BaseType);
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
            ContainingTypes = containingTypes,
            HasBitSerializableBaseType = hasBitSerializableBase,
            BaseBitLength = baseBitLength
        };

        // Collect members in declaration order
        var members = GetSerializableMembers(symbol);
        int currentBitIndex = baseBitLength;

        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();

            var memberType = GetMemberType(member);
            if (memberType == null) continue;

            // Check for BitIgnore
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute"))
                continue;

            // Check for BitField
            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            if (bitFieldAttr == null)
                continue; // Diagnostic reported elsewhere

            var field = new BitFieldModel
            {
                MemberName = member.Name,
                IsProperty = member is IPropertySymbol,
                BitStartIndex = currentBitIndex,
            };

            // Get explicit bit length
            int? explicitBitLength = GetBitLengthFromAttribute(bitFieldAttr);

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
            }

            field.RelatedMemberName = relatedMemberName;
            field.ValueConverterTypeFullName = valueConverterFullName;

            // Check for BitFiledCount
            var countAttr = GetAttribute(member, "BitSerializer.BitFiledCountAttribute");
            if (countAttr != null && countAttr.ConstructorArguments.Length > 0)
            {
                field.FixedCount = (int)countAttr.ConstructorArguments[0].Value!;
            }

            // Check for BitPoly
            var polyAttrs = GetAttributes(member, "BitSerializer.BitPolyAttribute");
            if (polyAttrs.Count > 0)
            {
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
                            ConcreteTypeFullName = concreteType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
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
                }

                if (field.FixedCount.HasValue)
                {
                    int totalListBits = field.FixedCount.Value * field.ListElementBitLength;
                    currentBitIndex += totalListBits;
                    model.HasDynamicLength = false;
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
                currentBitIndex += field.BitLength;
            }
            else
            {
                // Unsupported type, skip (diagnostic will handle)
                continue;
            }

            model.Fields.Add(field);
        }

        model.TotalBitLength = currentBitIndex;
        return new AnalyzeResult { Model = model };
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
        var members = GetSerializableMembers((INamedTypeSymbol)type);
        int total = 0;
        foreach (var member in members)
        {
            var memberType = GetMemberType(member);
            if (memberType == null) continue;
            if (HasAttribute(member, "BitSerializer.BitIgnoreAttribute")) continue;

            var bitFieldAttr = GetAttribute(member, "BitSerializer.BitFieldAttribute");
            if (bitFieldAttr == null) continue;

            int? explicitBitLength = GetBitLengthFromAttribute(bitFieldAttr);

            if (IsListType(memberType, out var elemType, out _))
            {
                var countAttr = GetAttribute(member, "BitSerializer.BitFiledCountAttribute");
                if (countAttr != null)
                {
                    int count = (int)countAttr.ConstructorArguments[0].Value!;
                    int elemBits;
                    if (IsNumericOrEnum(elemType!))
                        elemBits = explicitBitLength ?? GetDefaultBitLength(elemType!);
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
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == fullName);
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

    private static INamedTypeSymbol? FindTypeByFullName(IAssemblySymbol assembly, string fullName)
    {
        // Remove "global::" prefix if present
        var name = fullName.Replace("global::", "");
        return assembly.GetTypeByMetadataName(name);
    }
}
