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

internal class CrcGroup : IEquatable<CrcGroup>
{
    public string TargetFieldName { get; set; } = "";
    public string AlgorithmTypeFullName { get; set; } = "";
    public int BitWidth { get; set; }
    public ulong InitialValue { get; set; }
    public bool ValidateOnDeserialize { get; set; }
    /// <summary>Bit offset (within this type) of the CRC result field.</summary>
    public int CrcFieldBitOffset { get; set; }
    /// <summary>Bit length of the CRC result field.</summary>
    public int CrcFieldBitLength { get; set; }
    /// <summary>Primitive numeric type name of the CRC result field (e.g., "ushort", "uint").</summary>
    public string CrcFieldTypeName { get; set; } = "";
    /// <summary>Byte offset of the start of the included range (within this type's serialized bytes). Only meaningful when <see cref="HasDynamicInclude"/> is false.</summary>
    public int IncludeStartByte { get; set; }
    /// <summary>Byte offset one past the end of the included range. Only meaningful when <see cref="HasDynamicInclude"/> is false.</summary>
    public int IncludeEndByte { get; set; }
    /// <summary>True when any include field has runtime-variable bit length (polymorphic auto-length, terminated string, dynamic list, type parameter, etc.). Emitter uses runtime bit-offset locals instead of IncludeStartByte/IncludeEndByte.</summary>
    public bool HasDynamicInclude { get; set; }
    /// <summary>Member name of the first include field in declaration order. Emitter uses it to mark the CRC range start.</summary>
    public string FirstIncludeMemberName { get; set; } = "";
    /// <summary>Member name of the last include field in declaration order. Emitter uses it to mark the CRC range end.</summary>
    public string LastIncludeMemberName { get; set; } = "";

    public bool Equals(CrcGroup? other)
    {
        if (other is null) return false;
        return TargetFieldName == other.TargetFieldName
            && AlgorithmTypeFullName == other.AlgorithmTypeFullName
            && BitWidth == other.BitWidth
            && InitialValue == other.InitialValue
            && ValidateOnDeserialize == other.ValidateOnDeserialize
            && CrcFieldBitOffset == other.CrcFieldBitOffset
            && CrcFieldBitLength == other.CrcFieldBitLength
            && CrcFieldTypeName == other.CrcFieldTypeName
            && IncludeStartByte == other.IncludeStartByte
            && IncludeEndByte == other.IncludeEndByte
            && HasDynamicInclude == other.HasDynamicInclude
            && FirstIncludeMemberName == other.FirstIncludeMemberName
            && LastIncludeMemberName == other.LastIncludeMemberName;
    }

    public override bool Equals(object? obj) => Equals(obj as CrcGroup);
    public override int GetHashCode() => TargetFieldName.GetHashCode();
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
    /// <summary>
    /// True if the base type contains dynamic-length fields (terminated strings, manual IBitSerializable, etc.).
    /// </summary>
    public bool BaseHasDynamicLength { get; set; }

    /// <summary>
    /// CRC groups aggregated from [BitCrc] and [BitCrcInclude] attributes.
    /// </summary>
    public List<CrcGroup> CrcGroups { get; set; } = new();

    public bool Equals(TypeModel? other)
    {
        if (other is null) return false;
        return FullyQualifiedName == other.FullyQualifiedName
            && TotalBitLength == other.TotalBitLength
            && HasDynamicLength == other.HasDynamicLength
            && HasBitSerializableBaseType == other.HasBitSerializableBaseType
            && BaseBitLength == other.BaseBitLength
            && BaseHasDynamicLength == other.BaseHasDynamicLength
            && Fields.Count == other.Fields.Count
            && Fields.SequenceEqual(other.Fields, BitFieldModelComparer.Instance)
            && CrcGroups.Count == other.CrcGroups.Count
            && CrcGroups.SequenceEqual(other.CrcGroups);
    }

    public override bool Equals(object? obj) => Equals(obj as TypeModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = FullyQualifiedName.GetHashCode();
            hash = hash * 397 ^ TotalBitLength;
            hash = hash * 397 ^ HasDynamicLength.GetHashCode();
            hash = hash * 397 ^ HasBitSerializableBaseType.GetHashCode();
            hash = hash * 397 ^ BaseBitLength;
            hash = hash * 397 ^ BaseHasDynamicLength.GetHashCode();
            hash = hash * 397 ^ Fields.Count;
            return hash;
        }
    }
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
            && x.ValueConverterTypeFullName == y.ValueConverterTypeFullName
            && x.IsPotentiallyDynamic == y.IsPotentiallyDynamic
            && x.IsFixedString == y.IsFixedString
            && x.FixedStringByteLength == y.FixedStringByteLength
            && x.IsTerminatedString == y.IsTerminatedString
            && x.StringEncodingName == y.StringEncodingName
            && x.IsManualBitSerializable == y.IsManualBitSerializable
            && x.ListElementIsManualBitSerializable == y.ListElementIsManualBitSerializable
            && x.ListElementIsTypeParameter == y.ListElementIsTypeParameter
            && x.ListElementHasDynamicLength == y.ListElementHasDynamicLength
            && x.ListElementHasOwnContext == y.ListElementHasOwnContext
            && x.NestedHasOwnContext == y.NestedHasOwnContext
            && x.IsCrcResult == y.IsCrcResult
            && x.CrcAlgorithmTypeFullName == y.CrcAlgorithmTypeFullName
            && x.CrcInitialValue == y.CrcInitialValue
            && x.CrcValidateOnDeserialize == y.CrcValidateOnDeserialize
            && x.CrcTargetFieldName == y.CrcTargetFieldName
            && x.PadIfShort == y.PadIfShort
            && x.ConsumeRemaining == y.ConsumeRemaining
            && x.RelationKind == y.RelationKind;
    }

    public int GetHashCode(BitFieldModel obj) => obj.MemberName.GetHashCode();
}
