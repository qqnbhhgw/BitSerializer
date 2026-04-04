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
                EmitFixedStringDeserialize(sb, field, helper, memberAccess, offsetExpr);
            }
            else if (field.IsTerminatedString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitTerminatedStringDeserialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsList)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitListDeserialize(sb, field, helper, memberAccess, fieldEndVar, bitOrder, offsetExpr);
            }
            else if (field.IsPolymorphic)
            {
                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder, offsetExpr, fieldEndVar);
                }
                else
                {
                    EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder, offsetExpr);
                }
            }
            else if (field.IsTypeParameter)
            {
                sb.AppendLine($"        {memberAccess} = ({field.MemberTypeName})global::System.Activator.CreateInstance(typeof({field.MemberTypeName}))!;");
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr});");
            }
            else if (field.IsNestedType)
            {
                sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");

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
                EmitPrimitiveDeserialize(sb, field, helper, memberAccess, offsetExpr);
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

        if (field.ListElementIsManualBitSerializable)
        {
            // Manual IBitSerializable elements: use interface dispatch with runtime offset tracking
            string countExpr;
            if (field.FixedCount.HasValue)
            {
                countExpr = field.FixedCount.Value.ToString();
                if (field.IsArray)
                    sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[{field.FixedCount.Value}];");
                else
                    sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>({field.FixedCount.Value});");
            }
            else
            {
                countExpr = $"_listCount_{field.MemberName}";
                sb.AppendLine($"        int {countExpr} = (int)this.{field.RelatedMemberName};");
                if (field.IsArray)
                    sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[{countExpr}];");
                else
                    sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>({countExpr});");
            }
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        for (int _i = 0; _i < {countExpr}; _i++)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
            sb.AppendLine($"            {bitIndexVar} += ((global::BitSerializer.IBitSerializable)_elem).{deserializeMethod}(bytes, {bitIndexVar});");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = _elem;");
            else
                sb.AppendLine($"            {memberAccess}.Add(_elem);");
            sb.AppendLine("        }");
        }
        else if (field.FixedCount.HasValue)
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

    private static void EmitPolymorphicDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder, string offsetExpr, string? bitIndexVar = null)
    {
        var deserializeMethod = $"Deserialize{bitOrder}";

        if (bitIndexVar != null)
        {
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
        }

        sb.AppendLine($"        switch ((int)this.{field.RelatedMemberName})");
        sb.AppendLine("        {");
        foreach (var mapping in field.PolyMappings!)
        {
            sb.AppendLine($"            case {mapping.TypeId}:");
            sb.AppendLine("            {");
            sb.AppendLine($"                var _poly = new {mapping.ConcreteTypeFullName}();");
            if (bitIndexVar != null)
            {
                sb.AppendLine($"                {bitIndexVar} += _poly.{deserializeMethod}(bytes, {offsetExpr});");
            }
            else
            {
                sb.AppendLine($"                _poly.{deserializeMethod}(bytes, {offsetExpr});");
            }
            sb.AppendLine($"                {memberAccess} = _poly;");
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine($"                throw new global::System.InvalidOperationException($\"No polymorphic type mapping found for discriminator value '{{(int)this.{field.RelatedMemberName}}}'\");");
        sb.AppendLine("        }");
    }

    private static void EmitFixedStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        int byteLen = field.FixedStringByteLength;
        var name = field.MemberName;

        sb.AppendLine("        {");
        sb.AppendLine($"            byte[] _strBytes_{name} = new byte[{byteLen}];");
        sb.AppendLine($"            for (int _si = 0; _si < {byteLen}; _si++)");
        sb.AppendLine($"                _strBytes_{name}[_si] = {helper}.ValueLength<byte>(bytes, {offsetExpr} + _si * 8, 8);");
        sb.AppendLine($"            int _strEnd_{name} = {byteLen};");
        sb.AppendLine($"            while (_strEnd_{name} > 0 && _strBytes_{name}[_strEnd_{name} - 1] == 0) _strEnd_{name}--;");
        sb.AppendLine($"            {memberAccess} = {encoding}.GetString(_strBytes_{name}, 0, _strEnd_{name});");
        sb.AppendLine("        }");
    }

    private static void EmitTerminatedStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        var name = field.MemberName;

        sb.AppendLine($"        var _strList_{name} = new global::System.Collections.Generic.List<byte>();");
        sb.AppendLine($"        byte _b_{name};");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
        sb.AppendLine($"        while ((_b_{name} = {helper}.ValueLength<byte>(bytes, {bitIndexVar}, 8)) != 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            _strList_{name}.Add(_b_{name});");
        sb.AppendLine($"            {bitIndexVar} += 8;");
        sb.AppendLine("        }");
        sb.AppendLine($"        {bitIndexVar} += 8;");
        sb.AppendLine($"        {memberAccess} = {encoding}.GetString(_strList_{name}.ToArray());");
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
