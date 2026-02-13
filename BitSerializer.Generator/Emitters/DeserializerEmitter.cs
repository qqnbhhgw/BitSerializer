using System.Text;
using BitSerializer.Generator.Models;

namespace BitSerializer.Generator.Emitters;

internal static class DeserializerEmitter
{
    public static string EmitMethod(TypeModel model, string bitOrder)
    {
        var helper = bitOrder == "LSB" ? "global::BitSerializer.BitHelperLSB" : "global::BitSerializer.BitHelperMSB";
        var methodName = $"Deserialize{bitOrder}";

        var sb = new StringBuilder();
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        sb.AppendLine($"    public {newKeyword}int {methodName}(global::System.ReadOnlySpan<byte> bytes, int bitOffset)");
        sb.AppendLine("    {");

        if (model.HasBitSerializableBaseType)
        {
            sb.AppendLine($"        base.{methodName}(bytes, bitOffset);");
        }

        bool hasDynamic = false;
        string? dynamicVarName = null;

        foreach (var field in model.Fields)
        {
            var memberAccess = $"this.{field.MemberName}";

            if (field.IsList)
            {
                hasDynamic = true;
                dynamicVarName = $"_bitIndex_{field.MemberName}";
                EmitListDeserialize(sb, field, helper, memberAccess, dynamicVarName, bitOrder);
            }
            else if (field.IsPolymorphic)
            {
                EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder);
            }
            else if (field.IsNestedType)
            {
                sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");
                sb.AppendLine($"        {memberAccess}.{methodName}(bytes, bitOffset + {field.BitStartIndex});");
            }
            else if (field.IsNumericOrEnum)
            {
                EmitPrimitiveDeserialize(sb, field, helper, memberAccess);
            }
        }

        if (hasDynamic)
        {
            sb.AppendLine($"        return {dynamicVarName} - bitOffset;");
        }
        else
        {
            sb.AppendLine($"        return {model.TotalBitLength};");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitPrimitiveDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null)
        {
            if (field.IsEnum)
            {
                sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){field.ValueConverterTypeFullName}.OnDeserializeConvert((object){helper}.ValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength}));");
            }
            else
            {
                sb.AppendLine($"        {memberAccess} = ({typeName}){field.ValueConverterTypeFullName}.OnDeserializeConvert((object){helper}.ValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength}));");
            }
        }
        else if (field.IsEnum)
        {
            sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){helper}.ValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength});");
        }
        else
        {
            sb.AppendLine($"        {memberAccess} = {helper}.ValueLength<{typeName}>(bytes, bitOffset + {field.BitStartIndex}, {field.BitLength});");
        }
    }

    private static void EmitListDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string bitOrder)
    {
        int startBit = field.BitStartIndex;
        int elemBits = field.ListElementBitLength;
        var deserializeMethod = $"Deserialize{bitOrder}";
        var elemTypeFullName = field.ListElementTypeFullName;

        if (field.FixedCount.HasValue)
        {
            if (field.IsArray)
            {
                sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[{field.FixedCount.Value}];");
            }
            else
            {
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>({field.FixedCount.Value});");
            }
            sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, bitOffset + {startBit} + _i * {elemBits});");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else
            {
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, bitOffset + {startBit} + _i * {elemBits}, {elemBits});");
                else
                    sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, bitOffset + {startBit} + _i * {elemBits}, {elemBits}));");
            }
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = bitOffset + {startBit + field.FixedCount.Value * elemBits};");
        }
        else
        {
            sb.AppendLine($"        int _listCount_{field.MemberName} = (int)this.{field.RelatedMemberName};");
            if (field.IsArray)
            {
                sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[_listCount_{field.MemberName}];");
            }
            else
            {
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>(_listCount_{field.MemberName});");
            }
            sb.AppendLine($"        int {bitIndexVar} = bitOffset + {startBit};");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {bitIndexVar});");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else
            {
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
                else
                    sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
            }
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
        }
    }

    private static void EmitPolymorphicDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder)
    {
        var deserializeMethod = $"Deserialize{bitOrder}";

        sb.AppendLine($"        switch ((int)this.{field.RelatedMemberName})");
        sb.AppendLine("        {");
        foreach (var mapping in field.PolyMappings!)
        {
            sb.AppendLine($"            case {mapping.TypeId}:");
            sb.AppendLine("            {");
            sb.AppendLine($"                var _poly = new {mapping.ConcreteTypeFullName}();");
            sb.AppendLine($"                _poly.{deserializeMethod}(bytes, bitOffset + {field.BitStartIndex});");
            sb.AppendLine($"                {memberAccess} = _poly;");
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine($"                throw new global::System.InvalidOperationException($\"No polymorphic type mapping found for discriminator value '{{(int)this.{field.RelatedMemberName}}}'\");");
        sb.AppendLine("        }");
    }
}
