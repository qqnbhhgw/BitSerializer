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
        sb.AppendLine($"    public {newKeyword}int {methodName}(global::System.ReadOnlySpan<byte> bytes, int bitOffset, object? context)");
        sb.AppendLine("    {");
        sb.AppendLine("        OnDeserializing(context);");

        string? runtimeOffsetVar = null;
        int runtimeStaticEnd = 0;

        if (model.HasBitSerializableBaseType)
        {
            if (model.BaseHasDynamicLength)
            {
                sb.AppendLine($"        int _baseEndBit = bitOffset + base.{methodName}(bytes, bitOffset, context);");
                runtimeOffsetVar = "_baseEndBit";
                runtimeStaticEnd = model.BaseBitLength;
            }
            else
            {
                sb.AppendLine($"        base.{methodName}(bytes, bitOffset, context);");
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
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsTerminatedString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitTerminatedStringDeserialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsList)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitListDeserialize(sb, field, helper, memberAccess, fieldEndVar, bitOrder, offsetExpr);
                EmitDeserializeConverter(sb, field, memberAccess);
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
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsTypeParameter)
            {
                sb.AppendLine($"        {memberAccess} = ({field.MemberTypeName})global::System.Activator.CreateInstance(typeof({field.MemberTypeName}))!;");
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr}, context);");
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsNestedType)
            {
                if (field.IsManualBitSerializable)
                {
                    // Use interface dispatch so explicit IBitSerializable implementations compile.
                    // Declaring the local as the interface type boxes value types; after the
                    // call mutates the boxed copy we cast back to unbox the updated value.
                    var localVar = $"_nested_{field.MemberName}";
                    sb.AppendLine($"        global::BitSerializer.IBitSerializable {localVar} = new {field.MemberTypeFullName}();");
                    if (usesRuntimeBitLength)
                    {
                        fieldEndVar = $"_bitIndex_{field.MemberName}";
                        sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {localVar}.{methodName}(bytes, {offsetExpr}, context);");
                    }
                    else
                    {
                        sb.AppendLine($"        {localVar}.{methodName}(bytes, {offsetExpr}, context);");
                    }
                    sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){localVar};");
                }
                else
                {
                    sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");
                    string callExpr = $"{memberAccess}.{methodName}(bytes, {offsetExpr}, context)";

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
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsNumericOrEnum)
            {
                // Primitive converter is handled inside EmitPrimitiveDeserialize
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

    public static string EmitDelegationMethod(TypeModel model, string bitOrder)
    {
        var methodName = $"Deserialize{bitOrder}";
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        return $"    public {newKeyword}int {methodName}(global::System.ReadOnlySpan<byte> bytes, int bitOffset) => {methodName}(bytes, bitOffset, null);\n";
    }

    private static void EmitPrimitiveDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null && field.ValueConverterHasDeserialize)
        {
            var rawValue = $"(object){helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength})";
            var convertCall = field.ValueConverterDeserializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({rawValue}, context)"
                : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({rawValue})";
            var castType = field.IsEnum ? field.MemberTypeFullName : typeName;
            sb.AppendLine($"        {memberAccess} = ({castType}){convertCall};");
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
            if (field.FixedCount.HasValue && elemBits > 0)
            {
                // Fixed-count manual IBitSerializable with declared element width: use fixed stride
                if (field.IsArray)
                    sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[{field.FixedCount.Value}];");
                else
                    sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>({field.FixedCount.Value});");
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                sb.AppendLine(field.ListElementIsTypeParameter
                    ? $"            global::BitSerializer.IBitSerializable _elem = ({elemTypeFullName})global::System.Activator.CreateInstance(typeof({elemTypeFullName}))!;"
                    : $"            global::BitSerializer.IBitSerializable _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, context);");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = ({elemTypeFullName})_elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(({elemTypeFullName})_elem);");
                sb.AppendLine("        }");
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
            }
            else
            {
                // Dynamic: use runtime offset tracking via interface dispatch
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
                sb.AppendLine(field.ListElementIsTypeParameter
                    ? $"            global::BitSerializer.IBitSerializable _elem = ({elemTypeFullName})global::System.Activator.CreateInstance(typeof({elemTypeFullName}))!;"
                    : $"            global::BitSerializer.IBitSerializable _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, context);");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = ({elemTypeFullName})_elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(({elemTypeFullName})_elem);");
                sb.AppendLine("        }");
            }
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
            if (field.ListElementHasDynamicLength)
            {
                // Dynamic nested elements: use runtime offset tracking via return value
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, context);");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                if (field.ListElementIsNested)
                {
                    sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                    sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, context);");
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
            if (field.ListElementHasDynamicLength)
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, context);");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else if (field.ListElementIsNested)
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {bitIndexVar}, context);");
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }
            else
            {
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
                else
                    sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }
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
                sb.AppendLine($"                {bitIndexVar} += _poly.{deserializeMethod}(bytes, {offsetExpr}, context);");
            }
            else
            {
                sb.AppendLine($"                _poly.{deserializeMethod}(bytes, {offsetExpr}, context);");
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

    private static void EmitDeserializeConverter(StringBuilder sb, BitFieldModel field, string memberAccess)
    {
        if (field.ValueConverterTypeFullName == null || !field.ValueConverterHasDeserialize) return;
        var convertCall = field.ValueConverterDeserializeHasContext
            ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert((object){memberAccess}, context)"
            : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert((object){memberAccess})";
        sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){convertCall};");
    }

    private static bool UsesRuntimeBitLength(BitFieldModel field)
    {
        return field.IsTypeParameter
               || field.IsPotentiallyDynamic
               || field.IsTerminatedString
               || (field.IsList && !field.FixedCount.HasValue)
               || (field.IsList && field.ListElementIsManualBitSerializable && field.ListElementBitLength == 0)
               || (field.IsList && field.ListElementHasDynamicLength);
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
