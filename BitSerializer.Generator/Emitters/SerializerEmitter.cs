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

        string? runtimeOffsetVar = null;
        int runtimeStaticEnd = 0;

        if (model.HasBitSerializableBaseType)
        {
            if (model.BaseHasDynamicLength)
            {
                sb.AppendLine($"        int _baseEndBit = bitOffset + base.{methodName}(bytes, bitOffset);");
                runtimeOffsetVar = "_baseEndBit";
                runtimeStaticEnd = model.BaseBitLength;
            }
            else
            {
                sb.AppendLine($"        base.{methodName}(bytes, bitOffset);");
            }
        }

        foreach (var field in model.Fields)
        {
            var memberAccess = $"this.{field.MemberName}";
            var usesRuntimeBitLength = UsesRuntimeBitLength(field);

            // Compute offset expression
            string offsetExpr;
            if (runtimeOffsetVar is null)
            {
                offsetExpr = $"bitOffset + {field.BitStartIndex}";
            }
            else
            {
                int diff = field.BitStartIndex - runtimeStaticEnd;
                offsetExpr = diff == 0 ? runtimeOffsetVar : $"{runtimeOffsetVar} + {diff}";
            }

            string? fieldEndVar = null;

            if (field.IsFixedString)
            {
                EmitFixedStringSerialize(sb, field, helper, memberAccess, offsetExpr);
            }
            else if (field.IsTerminatedString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitTerminatedStringSerialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsList)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitListSerialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsPolymorphic)
            {
                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    EmitPolymorphicSerialize(sb, field, helper, memberAccess, bitOrder, offsetExpr, fieldEndVar);
                }
                else
                {
                    EmitPolymorphicSerialize(sb, field, helper, memberAccess, bitOrder, offsetExpr);
                }
            }
            else if (field.IsTypeParameter)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr});");
            }
            else if (field.IsNestedType)
            {
                string callExpr = field.IsManualBitSerializable
                    ? $"((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr})"
                    : $"{memberAccess}.{methodName}(bytes, {offsetExpr})";

                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {callExpr};");
                }
                else
                {
                    sb.AppendLine($"        {callExpr};");
                }
            }
            else if (field.IsNumericOrEnum)
            {
                EmitPrimitiveSerialize(sb, field, helper, memberAccess, offsetExpr);
            }

            if (usesRuntimeBitLength)
            {
                runtimeOffsetVar = fieldEndVar!;
                runtimeStaticEnd = GetStaticFieldEnd(field);
            }
        }

        if (runtimeOffsetVar is not null)
        {
            string returnExpr = runtimeOffsetVar;
            int trailingBits = model.TotalBitLength - runtimeStaticEnd;
            if (trailingBits > 0)
                returnExpr = $"{runtimeOffsetVar} + {trailingBits}";
            sb.AppendLine($"        return {returnExpr} - bitOffset;");
        }
        else
        {
            sb.AppendLine($"        return {model.TotalBitLength};");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }

    private static void EmitPrimitiveSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null)
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, ({typeName}){field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess}));");
        }
        else if (field.IsEnum)
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, ({typeName}){memberAccess});");
        }
        else
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, {memberAccess});");
        }
    }

    private static void EmitListSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        int elemBits = field.ListElementBitLength;
        var serializeMethod = GetSerializeMethodFromHelper(helper);

        if (field.ListElementIsManualBitSerializable)
        {
            // Manual IBitSerializable elements: use interface dispatch with runtime offset tracking
            string countExpr;
            if (field.FixedCount.HasValue)
            {
                countExpr = field.FixedCount.Value.ToString();
            }
            else
            {
                countExpr = $"_listCount_{field.MemberName}";
                sb.AppendLine($"        int {countExpr} = (int)this.{field.RelatedMemberName};");
            }
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        for (int _i = 0; _i < {countExpr}; _i++)");
            sb.AppendLine("        {");
            sb.AppendLine($"            {bitIndexVar} += ((global::BitSerializer.IBitSerializable){memberAccess}[_i]).{serializeMethod}(bytes, {bitIndexVar});");
            sb.AppendLine("        }");
        }
        else if (field.FixedCount.HasValue)
        {
            sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            {memberAccess}[_i].{serializeMethod}(bytes, {offsetExpr} + _i * {elemBits});");
            }
            else
            {
                sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits}, {memberAccess}[_i]);");
            }
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
        }
        else
        {
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        int _listCount_{field.MemberName} = (int)this.{field.RelatedMemberName};");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementIsNested)
            {
                sb.AppendLine($"            {memberAccess}[_i].{serializeMethod}(bytes, {bitIndexVar});");
            }
            else
            {
                sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, {bitIndexVar}, {elemBits}, {memberAccess}[_i]);");
            }
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
        }
    }

    private static void EmitPolymorphicSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder, string offsetExpr, string? bitIndexVar = null)
    {
        var methodName = $"Serialize{bitOrder}";
        if (bitIndexVar != null)
        {
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
        }

        bool first = true;
        foreach (var mapping in field.PolyMappings!)
        {
            var keyword = first ? "if" : "else if";
            first = false;
            sb.AppendLine($"        {keyword} ({memberAccess} is {mapping.ConcreteTypeFullName} _poly_{mapping.TypeId})");
            sb.AppendLine("        {");
            if (bitIndexVar != null)
            {
                sb.AppendLine($"            {bitIndexVar} += _poly_{mapping.TypeId}.{methodName}(bytes, {offsetExpr});");
            }
            else
            {
                sb.AppendLine($"            _poly_{mapping.TypeId}.{methodName}(bytes, {offsetExpr});");
            }
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

    private static void EmitFixedStringSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        int byteLen = field.FixedStringByteLength;
        var name = field.MemberName;

        sb.AppendLine("        {");
        sb.AppendLine($"            byte[] _strBytes_{name} = {encoding}.GetBytes({memberAccess} ?? \"\");");
        sb.AppendLine($"            int _strLen_{name} = global::System.Math.Min(_strBytes_{name}.Length, {byteLen});");

        // For UTF-8: ensure we don't split multi-byte characters
        if (field.StringEncodingName == "UTF8")
        {
            sb.AppendLine($"            if (_strLen_{name} < _strBytes_{name}.Length)");
            sb.AppendLine("            {");
            sb.AppendLine($"                while (_strLen_{name} > 0 && (_strBytes_{name}[_strLen_{name} - 1] & 0xC0) == 0x80) _strLen_{name}--;");
            sb.AppendLine($"                if (_strLen_{name} > 0 && _strBytes_{name}[_strLen_{name} - 1] >= 0xC0) _strLen_{name}--;");
            sb.AppendLine("            }");
        }

        sb.AppendLine($"            for (int _si = 0; _si < _strLen_{name}; _si++)");
        sb.AppendLine($"                {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _si * 8, 8, _strBytes_{name}[_si]);");
        sb.AppendLine($"            for (int _si = _strLen_{name}; _si < {byteLen}; _si++)");
        sb.AppendLine($"                {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _si * 8, 8, 0);");
        sb.AppendLine("        }");
    }

    private static void EmitTerminatedStringSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        var name = field.MemberName;

        sb.AppendLine($"        byte[] _strBytes_{name} = {encoding}.GetBytes({memberAccess} ?? \"\");");
        sb.AppendLine($"        for (int _si_{name} = 0; _si_{name} < _strBytes_{name}.Length; _si_{name}++)");
        sb.AppendLine($"            {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _si_{name} * 8, 8, _strBytes_{name}[_si_{name}]);");
        sb.AppendLine($"        {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _strBytes_{name}.Length * 8, 8, 0);");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + (_strBytes_{name}.Length + 1) * 8;");
    }

    private static string GetEncodingExpression(string encodingName)
    {
        return encodingName == "UTF8"
            ? "global::System.Text.Encoding.UTF8"
            : "global::System.Text.Encoding.ASCII";
    }

    private static bool UsesRuntimeBitLength(BitFieldModel field)
    {
        return field.IsTypeParameter
               || field.IsPotentiallyDynamic
               || field.IsTerminatedString
               || (field.IsList && !field.FixedCount.HasValue)
               || (field.IsList && field.ListElementIsManualBitSerializable && field.ListElementBitLength == 0);
    }

    private static int GetStaticFieldEnd(BitFieldModel field)
    {
        if (field.IsTerminatedString)
        {
            return field.BitStartIndex;
        }

        if (field.IsList)
        {
            return field.BitStartIndex + (field.FixedCount ?? 0) * field.ListElementBitLength;
        }

        return field.BitStartIndex + field.BitLength;
    }
}
