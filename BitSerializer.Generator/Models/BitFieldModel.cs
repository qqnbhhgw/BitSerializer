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
    public int PolymorphicBitLength { get; set; }
}
