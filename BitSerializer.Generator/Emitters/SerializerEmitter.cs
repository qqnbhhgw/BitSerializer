using System.Collections.Generic;
using System.Linq;
using System.Text;
using BitSerializer.Generator.Models;

namespace BitSerializer.Generator.Emitters;

internal static class SerializerEmitter
{
    /// <summary>
    /// Emits statements that auto-backfill related fields before serialization:
    /// - List/array count fields: set from collection length (with overflow check)
    /// - Polymorphic discriminator fields: set from runtime type via [BitPoly] mappings
    /// </summary>
    internal static void EmitAutoBackfill(StringBuilder sb, List<BitFieldModel> fields)
    {
        foreach (var field in fields)
        {
            if (field.RelatedMemberName == null)
                continue;

            var relatedField = fields.Find(f => f.MemberName == field.RelatedMemberName);
            if (relatedField == null)
                continue;

            if (field.IsList && !field.FixedCount.HasValue)
            {
                // Backfill count field from collection length (null-safe: skip if collection is null)
                var lengthProp = field.IsArray ? "Length" : "Count";
                sb.AppendLine($"        if (this.{field.MemberName} != null)");
                sb.AppendLine("        {");
                // Overflow check: skip for bit widths >= 32 since List.Count/Array.Length is int
                if (relatedField.BitLength < 32)
                {
                    long maxValue = (1L << relatedField.BitLength) - 1;
                    sb.AppendLine($"            if (this.{field.MemberName}.{lengthProp} > {maxValue})");
                    sb.AppendLine($"                throw new global::System.InvalidOperationException($\"Collection '{field.MemberName}' has {{this.{field.MemberName}.{lengthProp}}} elements, which exceeds the maximum ({maxValue}) representable by the {relatedField.BitLength}-bit field '{relatedField.MemberName}'.\");");
                }
                sb.AppendLine($"            this.{relatedField.MemberName} = ({relatedField.MemberTypeName})this.{field.MemberName}.{lengthProp};");
                sb.AppendLine("        }");
            }
            else if (field.IsPolymorphic && field.PolyMappings is { Count: > 0 })
            {
                // Backfill discriminator field from runtime type
                bool first = true;
                foreach (var mapping in field.PolyMappings)
                {
                    var keyword = first ? "if" : "else if";
                    first = false;
                    sb.AppendLine($"        {keyword} (this.{field.MemberName} is {mapping.ConcreteTypeFullName})");
                    sb.AppendLine($"            this.{relatedField.MemberName} = ({relatedField.MemberTypeName}){mapping.TypeId};");
                }
            }
        }
    }

    public static string EmitMethod(TypeModel model, string bitOrder)
    {
        var helper = bitOrder == "LSB" ? "global::BitSerializer.BitHelperLSB" : "global::BitSerializer.BitHelperMSB";
        var methodName = $"Serialize{bitOrder}";

        var sb = new StringBuilder();
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        sb.AppendLine($"    public {newKeyword}int {methodName}(global::System.Span<byte> bytes, int bitOffset, object? context)");
        sb.AppendLine("    {");
        sb.AppendLine("        OnSerializing(context);");

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

        // Apply list-level value converters before backfill (converters may change list length)
        foreach (var f in model.Fields)
        {
            if (f.IsList && f.ValueConverterTypeFullName != null && f.ValueConverterHasSerialize)
            {
                var ma = $"this.{f.MemberName}";
                var convertCall = f.ValueConverterSerializeHasContext
                    ? $"{f.ValueConverterTypeFullName}.OnSerializeConvert((object){ma}, context)"
                    : $"{f.ValueConverterTypeFullName}.OnSerializeConvert((object){ma})";
                sb.AppendLine($"        {ma} = ({f.MemberTypeFullName}){convertCall};");
            }
        }

        EmitAutoBackfill(sb, model.Fields);

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

            // If this field is the first include of a dynamic CRC group, capture the runtime start bit.
            foreach (var crc in model.CrcGroups)
            {
                if (crc.HasDynamicInclude && crc.FirstIncludeMemberName == field.MemberName)
                    sb.AppendLine($"        int _crcStartBit_{crc.TargetFieldName} = {offsetExpr};");
            }

            string? fieldEndVar = null;

            if (field.IsFixedString)
            {
                EmitSerializeConverter(sb, field, memberAccess);
                EmitFixedStringSerialize(sb, field, helper, memberAccess, offsetExpr);
            }
            else if (field.IsTerminatedString)
            {
                EmitSerializeConverter(sb, field, memberAccess);
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitTerminatedStringSerialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsList)
            {
                // List converter is applied before backfill (see pre-backfill loop above)
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitListSerialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsPolymorphic)
            {
                EmitSerializeConverter(sb, field, memberAccess);
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
                EmitSerializeConverter(sb, field, memberAccess);
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + ((global::BitSerializer.IBitSerializable){memberAccess}).{methodName}(bytes, {offsetExpr}, context);");
            }
            else if (field.IsNestedType)
            {
                EmitSerializeConverter(sb, field, memberAccess);
                string ctxArg = "context";
                string ibsExpr = $"(global::BitSerializer.IBitSerializable){memberAccess}";
                if (field.NestedHasOwnContext)
                {
                    sb.AppendLine($"        int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                    sb.AppendLine($"        var _nestedCtx_{field.MemberName} = ({ibsExpr}).SerializeContext();");
                    sb.AppendLine($"        ({ibsExpr}).BeforeSerialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                    ctxArg = $"_nestedCtx_{field.MemberName}";
                }
                string callExpr = field.IsManualBitSerializable
                    ? $"({ibsExpr}).{methodName}(bytes, {offsetExpr}, {ctxArg})"
                    : $"{memberAccess}.{methodName}(bytes, {offsetExpr}, {ctxArg})";

                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {callExpr};");
                }
                else
                {
                    sb.AppendLine($"        {callExpr};");
                }
                if (field.NestedHasOwnContext)
                {
                    sb.AppendLine($"        ({ibsExpr}).AfterSerialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                }
            }
            else if (field.IsNumericOrEnum)
            {
                // Primitive converter is handled inside EmitPrimitiveSerialize
                EmitPrimitiveSerialize(sb, field, helper, memberAccess, offsetExpr);
            }

            // If this field is the last include of a dynamic CRC group, capture the runtime end bit.
            foreach (var crc in model.CrcGroups)
            {
                if (crc.HasDynamicInclude && crc.LastIncludeMemberName == field.MemberName)
                {
                    string endExpr = usesRuntimeBitLength
                        ? fieldEndVar!
                        : $"{offsetExpr} + {field.BitLength}";
                    sb.AppendLine($"        int _crcEndBit_{crc.TargetFieldName} = {endExpr};");
                }
            }

            if (usesRuntimeBitLength)
            {
                runtimeOffsetVar = fieldEndVar!;
                runtimeStaticEnd = GetStaticFieldEnd(field);
            }
        }

        // Emit CRC computation blocks (one per CRC group). This runs AFTER all fields are serialized,
        // so the CRC result overwrites whatever the initial field value produced.
        foreach (var crc in model.CrcGroups)
        {
            var crcField = model.Fields.Find(f => f.MemberName == crc.TargetFieldName);
            if (crcField == null) continue;
            string crcOffsetExpr = BuildCrcFieldOffsetExpr(crcField, runtimeOffsetVar, runtimeStaticEnd);
            sb.AppendLine("        {");
            if (crc.HasDynamicInclude)
            {
                sb.AppendLine($"            if ((_crcStartBit_{crc.TargetFieldName} % 8) != 0 || (_crcEndBit_{crc.TargetFieldName} % 8) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException(\"CRC include range for '{crcField.MemberName}' is not byte-aligned at runtime (dynamic include field produced a non-integer-byte payload).\");");
                sb.AppendLine($"            int _crcStart = _crcStartBit_{crc.TargetFieldName} / 8;");
                sb.AppendLine($"            int _crcEnd   = _crcEndBit_{crc.TargetFieldName} / 8;");
            }
            else
            {
                sb.AppendLine($"            int _crcStart = (bitOffset / 8) + {crc.IncludeStartByte};");
                sb.AppendLine($"            int _crcEnd   = (bitOffset / 8) + {crc.IncludeEndByte};");
            }
            sb.AppendLine($"            var _crcAlgo = new {crc.AlgorithmTypeFullName}();");
            sb.AppendLine($"            _crcAlgo.Reset({crc.InitialValue}UL);");
            sb.AppendLine("            _crcAlgo.Update(bytes.Slice(_crcStart, _crcEnd - _crcStart));");
            string castType = crcField.IsEnum ? crcField.EnumUnderlyingTypeName! : crcField.MemberTypeName;
            sb.AppendLine($"            {castType} _crcVal = ({castType})_crcAlgo.Result;");
            sb.AppendLine($"            {helper}.SetValueLength<{castType}>(bytes, {crcOffsetExpr}, {crc.CrcFieldBitLength}, _crcVal);");
            if (!crcField.IsEnum)
                sb.AppendLine($"            this.{crcField.MemberName} = _crcVal;");
            else
                sb.AppendLine($"            this.{crcField.MemberName} = ({crcField.MemberTypeFullName})_crcVal;");
            sb.AppendLine("        }");
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

    private static string BuildCrcFieldOffsetExpr(BitFieldModel crcField, string? runtimeOffsetVar, int runtimeStaticEnd)
    {
        if (runtimeOffsetVar is null)
            return $"bitOffset + {crcField.BitStartIndex}";
        int diff = crcField.BitStartIndex - runtimeStaticEnd;
        return diff == 0 ? runtimeOffsetVar : $"{runtimeOffsetVar} + {diff}";
    }

    public static string EmitDelegationMethod(TypeModel model, string bitOrder)
    {
        var methodName = $"Serialize{bitOrder}";
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        return $"    public {newKeyword}int {methodName}(global::System.Span<byte> bytes, int bitOffset) => {methodName}(bytes, bitOffset, null);\n";
    }

    private static void EmitPrimitiveSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        if (field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize)
        {
            var convertCall = field.ValueConverterSerializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess}, context)"
                : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess})";
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, ({typeName}){convertCall});");
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
        bool hasCtx = field.ListElementHasOwnContext;
        // When element has own context, use element's context instead of parent's
        string ctxArg = hasCtx ? "_elemCtx" : "context";

        var ibsElem = $"(global::BitSerializer.IBitSerializable){memberAccess}[_i]";

        // [BitFieldConsumeRemaining]: write array.Length elements (dynamic)
        if (field.ConsumeRemaining)
        {
            var lenProp = field.IsArray ? "Length" : "Count";
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        int _listCount_{field.MemberName} = {memberAccess}?.{lenProp} ?? 0;");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeFullName}>(bytes, {bitIndexVar}, {elemBits}, {memberAccess}[_i]);");
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
            return;
        }

        // [BitFieldCount(N, PadIfShort=true)]: always write N elements, pad trailing with default
        if (field.PadIfShort && field.FixedCount.HasValue)
        {
            var lenProp = field.IsArray ? "Length" : "Count";
            sb.AppendLine($"        int _padLen_{field.MemberName} = {memberAccess}?.{lenProp} ?? 0;");
            sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
            sb.AppendLine("        {");
            sb.AppendLine($"            {field.ListElementTypeFullName} _padVal_{field.MemberName} = _i < _padLen_{field.MemberName} ? {memberAccess}[_i] : default({field.ListElementTypeFullName});");
            sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeFullName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits}, _padVal_{field.MemberName});");
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
            return;
        }

        if (field.ListElementIsManualBitSerializable)
        {
            if (field.FixedCount.HasValue && elemBits > 0)
            {
                // Fixed-count manual IBitSerializable with declared element width: use fixed stride
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, $"{offsetExpr} + _i * {elemBits}");
                sb.AppendLine($"            ({ibsElem}).{serializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg});");
                if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
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
                }
                else
                {
                    countExpr = $"_listCount_{field.MemberName}";
                    sb.AppendLine($"        int {countExpr} = (int)this.{field.RelatedMemberName};");
                }
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
                sb.AppendLine($"        for (int _i = 0; _i < {countExpr}; _i++)");
                sb.AppendLine("        {");
                if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += ({ibsElem}).{serializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
                sb.AppendLine("        }");
            }
        }
        else if (field.FixedCount.HasValue)
        {
            if (field.ListElementHasDynamicLength)
            {
                // Dynamic nested elements: use runtime offset tracking via return value
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += {memberAccess}[_i].{serializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
                sb.AppendLine("        }");
            }
            else
            {
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                if (field.ListElementIsNested)
                {
                    if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, $"{offsetExpr} + _i * {elemBits}");
                    sb.AppendLine($"            {memberAccess}[_i].{serializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg});");
                    if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
                }
                else
                {
                    sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits}, {memberAccess}[_i]);");
                }
                sb.AppendLine("        }");
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
            }
        }
        else
        {
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        int _listCount_{field.MemberName} = (int)this.{field.RelatedMemberName};");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementHasDynamicLength)
            {
                if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += {memberAccess}[_i].{serializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
            }
            else if (field.ListElementIsNested)
            {
                if (hasCtx) EmitSerializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {memberAccess}[_i].{serializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitSerializeElementContextAfter(sb, ibsElem);
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }
            else
            {
                sb.AppendLine($"            {helper}.SetValueLength<{field.ListElementTypeName}>(bytes, {bitIndexVar}, {elemBits}, {memberAccess}[_i]);");
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }
            sb.AppendLine("        }");
        }
    }

    private static void EmitSerializeElementContextBefore(StringBuilder sb, string elemIbsExpr, string elemBitOffsetExpr, string indent = "            ")
    {
        sb.AppendLine($"{indent}int _elemBitOff = {elemBitOffsetExpr};");
        sb.AppendLine($"{indent}var _elemCtx = ({elemIbsExpr}).SerializeContext();");
        sb.AppendLine($"{indent}({elemIbsExpr}).BeforeSerialize(_elemCtx, bytes.Slice(_elemBitOff / 8));");
    }

    private static void EmitSerializeElementContextAfter(StringBuilder sb, string elemIbsExpr, string indent = "            ")
    {
        sb.AppendLine($"{indent}({elemIbsExpr}).AfterSerialize(_elemCtx, bytes.Slice(_elemBitOff / 8));");
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
                sb.AppendLine($"            {bitIndexVar} += _poly_{mapping.TypeId}.{methodName}(bytes, {offsetExpr}, context);");
            }
            else
            {
                sb.AppendLine($"            _poly_{mapping.TypeId}.{methodName}(bytes, {offsetExpr}, context);");
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
            sb.AppendLine($"                int _lcs_{name} = _strLen_{name} - 1;");
            sb.AppendLine($"                while (_lcs_{name} > 0 && (_strBytes_{name}[_lcs_{name}] & 0xC0) == 0x80) _lcs_{name}--;");
            sb.AppendLine($"                byte _lead_{name} = _strBytes_{name}[_lcs_{name}];");
            sb.AppendLine($"                int _seqLen_{name} = _lead_{name} < 0x80 ? 1 : (_lead_{name} & 0xE0) == 0xC0 ? 2 : (_lead_{name} & 0xF0) == 0xE0 ? 3 : (_lead_{name} & 0xF8) == 0xF0 ? 4 : 1;");
            sb.AppendLine($"                if (_lcs_{name} + _seqLen_{name} > _strLen_{name}) _strLen_{name} = _lcs_{name};");
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
        sb.AppendLine($"        int _strWriteLen_{name} = global::System.Array.IndexOf(_strBytes_{name}, (byte)0);");
        sb.AppendLine($"        if (_strWriteLen_{name} < 0) _strWriteLen_{name} = _strBytes_{name}.Length;");
        sb.AppendLine($"        for (int _si_{name} = 0; _si_{name} < _strWriteLen_{name}; _si_{name}++)");
        sb.AppendLine($"            {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _si_{name} * 8, 8, _strBytes_{name}[_si_{name}]);");
        sb.AppendLine($"        {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _strWriteLen_{name} * 8, 8, 0);");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + (_strWriteLen_{name} + 1) * 8;");
    }

    private static string GetEncodingExpression(string encodingName)
    {
        return encodingName == "UTF8"
            ? "global::System.Text.Encoding.UTF8"
            : "global::System.Text.Encoding.ASCII";
    }

    private static void EmitSerializeConverter(StringBuilder sb, BitFieldModel field, string memberAccess)
    {
        if (field.ValueConverterTypeFullName == null || !field.ValueConverterHasSerialize) return;
        var convertCall = field.ValueConverterSerializeHasContext
            ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess}, context)"
            : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess})";
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
