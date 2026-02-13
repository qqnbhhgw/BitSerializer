using System;
using System.Collections.Generic;
using System.Linq;

namespace BitSerializer.Generator.Models;

internal class ContainingTypeInfo : IEquatable<ContainingTypeInfo>
{
    public string Name { get; set; } = "";
    public bool IsClass { get; set; }
    public bool IsRecord { get; set; }

    public bool Equals(ContainingTypeInfo? other)
    {
        if (other is null) return false;
        return Name == other.Name && IsClass == other.IsClass && IsRecord == other.IsRecord;
    }

    public override bool Equals(object? obj) => Equals(obj as ContainingTypeInfo);
    public override int GetHashCode() => Name.GetHashCode();
}

internal class TypeModel : IEquatable<TypeModel>
{
    public string Namespace { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string FullyQualifiedName { get; set; } = "";
    public bool IsClass { get; set; }
    public bool IsRecord { get; set; }
    public List<BitFieldModel> Fields { get; set; } = new();
    public int TotalBitLength { get; set; }
    public bool HasDynamicLength { get; set; }
    /// <summary>
    /// Containing types from outermost to innermost (for nested types).
    /// </summary>
    /// <summary>
    /// Containing types from outermost to innermost (for nested types).
    /// </summary>
    public List<ContainingTypeInfo> ContainingTypes { get; set; } = new();
    /// <summary>
    /// True if this is an open generic type (has unresolved type parameters).
    /// </summary>
    public bool IsOpenGeneric { get; set; }
    /// <summary>
    /// True if the base type also has [BitSerialize] (needs 'new' keyword on methods).
    /// </summary>
    public bool HasBitSerializableBaseType { get; set; }
    /// <summary>
    /// The total bit length of the base type (for derived types with [BitSerialize] base).
    /// </summary>
    public int BaseBitLength { get; set; }

    public bool Equals(TypeModel? other)
    {
        if (other is null) return false;
        return FullyQualifiedName == other.FullyQualifiedName
            && TotalBitLength == other.TotalBitLength
            && HasDynamicLength == other.HasDynamicLength
            && Fields.Count == other.Fields.Count
            && Fields.SequenceEqual(other.Fields, BitFieldModelComparer.Instance);
    }

    public override bool Equals(object? obj) => Equals(obj as TypeModel);
    public override int GetHashCode() => FullyQualifiedName.GetHashCode();
}

internal class BitFieldModelComparer : IEqualityComparer<BitFieldModel>
{
    public static readonly BitFieldModelComparer Instance = new();

    public bool Equals(BitFieldModel? x, BitFieldModel? y)
    {
        if (x is null && y is null) return true;
        if (x is null || y is null) return false;
        return x.MemberName == y.MemberName
            && x.BitStartIndex == y.BitStartIndex
            && x.BitLength == y.BitLength
            && x.MemberTypeFullName == y.MemberTypeFullName
            && x.IsList == y.IsList
            && x.IsNestedType == y.IsNestedType
            && x.IsTypeParameter == y.IsTypeParameter
            && x.IsPolymorphic == y.IsPolymorphic
            && x.FixedCount == y.FixedCount
            && x.RelatedMemberName == y.RelatedMemberName
            && x.ValueConverterTypeFullName == y.ValueConverterTypeFullName;
    }

    public int GetHashCode(BitFieldModel obj) => obj.MemberName.GetHashCode();
}
