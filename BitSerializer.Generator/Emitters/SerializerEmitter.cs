using System.Text;
using BitSerializer.Generator.Models;

namespace BitSerializer.Generator.Emitters;

internal static class SerializerEmitter
{
    public static string EmitMethod(TypeModel model, string bitOrder)
    {
        var helper = bitOrder == "LSB" ? "global::BitSerializer.BitHelperLSB" : "global::BitSerializer.BitHelperMSB";
        var methodName = $"Serialize{bitOrder}";

        var sb = new StringBuilder();
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        sb.AppendLine($"    public {newKeyword}int {methodName}(global::System.Span<byte> bytes, int bitOffset)");
        sb.AppendLine("    {");

        if (model.HasBitSerializableBaseType)
        {
            sb.AppendLine($"        base.{methodName}(bytes, bitOffset);");
        }

        bool hasDynamic = false;
        string? dynamicVarName = null;
        var typeParamBitsExprs = new System.Collections.Generic.List<string>();

        foreach (var field in model.Fields)
        {
            var memberAccess = $"this.{field.MemberName}";

            if (field.IsList)
            {
                hasDynamic = true;
                dynamicVarName = $"_bitIndex_{field.MemberName}";
                EmitListSerialize(sb, field, helper, memberAccess, dynamicVarName);
            }
            else if (field.IsPolymorphic)
            {
                EmitPolymorphicSerialize(sb, field, helper, memberAccess, bitOrder);
            }
            else if (field.IsTypeParameter)
            {
                // Type parameter field: use interface dispatch
                sb.AppendLine($"        ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, bitOffset + {field.BitStartIndex});");
                typeParamBitsExprs.Add($"((global::BitSerializer.IBitSerializable)this.{field.MemberName}).GetTotalBitLength()");
            }
            else if (field.IsNestedType)
            {
                sb.AppendLine($"        {memberAccess}.{methodName}(bytes, bitOffset + {field.BitStartIndex});");
            }
            else if (field.IsNumericOrEnum)
            {
                EmitPrimitiveSerialize(sb, field, helper, memberAccess);
            }
        }

        var typeParamSuffix = typeParamBitsExprs.Count > 0
            ? " + " + string.Join(" + ", typeParamBitsExprs)
            : "";

        if (hasDynamic)
        {
            sb.AppendLine($"        return {dynamicVarName} - bitOffset{typeParamSuffix};");
        }
        else
        {
            sb.AppendLine($"        return {model.TotalBitLength}{typeParamSuffix};");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitPrimitiveSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null)
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength}, ({typeName}){field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess}));");
        }
        else if (field.IsEnum)
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength}, ({typeName}){memberAccess});");
        }
        else
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength}, {memberAccess});");
        }
    }

    private static void EmitListSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar)
    {
        int startBit = field.BitStartIndex;
        int elemBits = field.ListElementBitLength;

        if (field.FixedCount.HasValue)
        {
            sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            {memberAccess}[_i].{GetSerializeMethodFromHelper(helper)}(bytes, bitOffset + {startBit} + _i * {elemBits});");
            }
            else
            {
                sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, bitOffset + {startBit} + _i * {elemBits}, {elemBits}, {memberAccess}[_i]);");
            }
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = bitOffset + {startBit + field.FixedCount.Value * elemBits};");
        }
        else
        {
            sb.AppendLine($"        int {bitIndexVar} = bitOffset + {startBit};");
            sb.AppendLine($"        int _listCount_{field.MemberName} = (int)this.{field.RelatedMemberName};");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            {memberAccess}[_i].{GetSerializeMethodFromHelper(helper)}(bytes, {bitIndexVar});");
            }
            else
            {
                sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, {bitIndexVar}, {elemBits}, {memberAccess}[_i]);");
            }
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
        }
    }

    private static void EmitPolymorphicSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder)
    {
        var methodName = $"Serialize{bitOrder}";
        bool first = true;
        foreach (var mapping in field.PolyMappings!)
        {
            var keyword = first ? "if" : "else if";
            first = false;
            sb.AppendLine($"        {keyword} ({memberAccess} is {mapping.ConcreteTypeFullName} _poly_{mapping.TypeId})");
            sb.AppendLine("        {");
            sb.AppendLine($"            _poly_{mapping.TypeId}.{methodName}(bytes, bitOffset + {field.BitStartIndex});");
            sb.AppendLine("        }");
        }
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"No polymorphic type mapping found for type '{{({memberAccess})?.GetType().Name}}'\");");
        sb.AppendLine("        }");
    }

    private static string GetSerializeMethodFromHelper(string helper)
    {
        return helper.Contains("LSB") ? "SerializeLSB" : "SerializeMSB";
    }
}
