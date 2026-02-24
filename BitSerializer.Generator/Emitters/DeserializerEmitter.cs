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
        var typeParamBitsExprs = new System.Collections.Generic.List<string>();

        bool afterDynamic = false;
        string lastDynamicVar = "";
        int lastDynamicStaticStart = 0;

        foreach (var field in model.Fields)
        {
            var memberAccess = $"this.{field.MemberName}";

            // Compute offset expression
            string offsetExpr;
            if (!afterDynamic)
            {
                offsetExpr = $"bitOffset + {field.BitStartIndex}";
            }
            else
            {
                int diff = field.BitStartIndex - lastDynamicStaticStart;
                offsetExpr = diff == 0 ? lastDynamicVar : $"{lastDynamicVar} + {diff}";
            }

            if (field.IsList)
            {
                hasDynamic = true;
                dynamicVarName = $"_bitIndex_{field.MemberName}";
                EmitListDeserialize(sb, field, helper, memberAccess, dynamicVarName, bitOrder, offsetExpr);

                if (!field.FixedCount.HasValue || afterDynamic)
                {
                    afterDynamic = true;
                    lastDynamicVar = dynamicVarName;
                    lastDynamicStaticStart = field.FixedCount.HasValue
                        ? field.BitStartIndex + field.FixedCount.Value * field.ListElementBitLength
                        : field.BitStartIndex;
                }
            }
            else if (field.IsPolymorphic)
            {
                EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder, offsetExpr);
            }
            else if (field.IsTypeParameter)
            {
                // Type parameter field: create instance via Activator and use interface dispatch
                sb.AppendLine($"        {memberAccess} = ({field.MemberTypeName})global::System.Activator.CreateInstance(typeof({field.MemberTypeName}))!;");
                sb.AppendLine($"        ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr});");
                typeParamBitsExprs.Add($"((global::BitSerializer.IBitSerializable)this.{field.MemberName}).GetTotalBitLength()");
            }
            else if (field.IsNestedType)
            {
                sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");
                sb.AppendLine($"        {memberAccess}.{methodName}(bytes, {offsetExpr});");
            }
            else if (field.IsNumericOrEnum)
            {
                EmitPrimitiveDeserialize(sb, field, helper, memberAccess, offsetExpr);
            }
        }

        var typeParamSuffix = typeParamBitsExprs.Count > 0
            ? " + " + string.Join(" + ", typeParamBitsExprs)
            : "";

        if (hasDynamic)
        {
            string returnExpr = dynamicVarName!;
            if (afterDynamic)
            {
                int trailingBits = model.TotalBitLength - lastDynamicStaticStart;
                if (trailingBits > 0)
                    returnExpr = $"{dynamicVarName} + {trailingBits}";
            }
            sb.AppendLine($"        return {returnExpr} - bitOffset{typeParamSuffix};");
        }
        else
        {
            sb.AppendLine($"        return {model.TotalBitLength}{typeParamSuffix};");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitPrimitiveDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null)
        {
            if (field.IsEnum)
            {
                sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){field.ValueConverterTypeFullName}.OnDeserializeConvert((object){helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}));");
            }
            else
            {
                sb.AppendLine($"        {memberAccess} = ({typeName}){field.ValueConverterTypeFullName}.OnDeserializeConvert((object){helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}));");
            }
        }
        else if (field.IsEnum)
        {
            sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength});");
        }
        else
        {
            sb.AppendLine($"        {memberAccess} = {helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength});");
        }
    }

    private static void EmitListDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string bitOrder, string offsetExpr)
    {
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
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits});");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else
            {
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits});");
                else
                    sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits}));");
            }
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
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
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
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

    private static void EmitPolymorphicDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder, string offsetExpr)
    {
        var deserializeMethod = $"Deserialize{bitOrder}";

        sb.AppendLine($"        switch ((int)this.{field.RelatedMemberName})");
        sb.AppendLine("        {");
        foreach (var mapping in field.PolyMappings!)
        {
            sb.AppendLine($"            case {mapping.TypeId}:");
            sb.AppendLine("            {");
            sb.AppendLine($"                var _poly = new {mapping.ConcreteTypeFullName}();");
            sb.AppendLine($"                _poly.{deserializeMethod}(bytes, {offsetExpr});");
            sb.AppendLine($"                {memberAccess} = _poly;");
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine($"                throw new global::System.InvalidOperationException($\"No polymorphic type mapping found for discriminator value '{{(int)this.{field.RelatedMemberName}}}'\");");
        sb.AppendLine("        }");
    }
}
