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

            // If this field is the first include of a dynamic CRC group that validates on deserialize,
            // capture the runtime start bit.
            foreach (var crc in model.CrcGroups)
            {
                if (crc.HasDynamicInclude && crc.ValidateOnDeserialize && crc.FirstIncludeMemberName == field.MemberName)
                    sb.AppendLine($"        int _crcStartBit_{crc.TargetFieldName} = {offsetExpr};");
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
                    string ctxArg = "context";
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                        sb.AppendLine($"        var _nestedCtx_{field.MemberName} = {localVar}.DeserializeContext();");
                        sb.AppendLine($"        {localVar}.BeforeDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                        ctxArg = $"_nestedCtx_{field.MemberName}";
                    }
                    if (usesRuntimeBitLength)
                    {
                        fieldEndVar = $"_bitIndex_{field.MemberName}";
                        sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {localVar}.{methodName}(bytes, {offsetExpr}, {ctxArg});");
                    }
                    else
                    {
                        sb.AppendLine($"        {localVar}.{methodName}(bytes, {offsetExpr}, {ctxArg});");
                    }
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        {localVar}.AfterDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                    }
                    sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){localVar};");
                }
                else
                {
                    sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");
                    string ctxArg = "context";
                    string ibsExpr = $"(global::BitSerializer.IBitSerializable){memberAccess}";
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                        sb.AppendLine($"        var _nestedCtx_{field.MemberName} = ({ibsExpr}).DeserializeContext();");
                        sb.AppendLine($"        ({ibsExpr}).BeforeDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                        ctxArg = $"_nestedCtx_{field.MemberName}";
                    }
                    string callExpr = $"{memberAccess}.{methodName}(bytes, {offsetExpr}, {ctxArg})";

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
                        sb.AppendLine($"        ({ibsExpr}).AfterDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                    }
                }
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsNumericOrEnum)
            {
                // Primitive converter is handled inside EmitPrimitiveDeserialize
                EmitPrimitiveDeserialize(sb, field, helper, memberAccess, offsetExpr);
            }

            // If this field is the last include of a dynamic CRC group that validates on deserialize,
            // capture the runtime end bit.
            foreach (var crc in model.CrcGroups)
            {
                if (crc.HasDynamicInclude && crc.ValidateOnDeserialize && crc.LastIncludeMemberName == field.MemberName)
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

        // Emit CRC validation blocks for groups with ValidateOnDeserialize=true
        foreach (var crc in model.CrcGroups)
        {
            if (!crc.ValidateOnDeserialize) continue;
            var crcField = model.Fields.Find(f => f.MemberName == crc.TargetFieldName);
            if (crcField == null) continue;
            sb.AppendLine("        {");
            if (crc.HasDynamicInclude)
            {
                sb.AppendLine($"            if ((_crcStartBit_{crc.TargetFieldName} % 8) != 0 || (_crcEndBit_{crc.TargetFieldName} % 8) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException(\"CRC include range for '{crcField.MemberName}' is not byte-aligned at runtime on Deserialize (dynamic include field produced a non-integer-byte payload).\");");
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
            sb.AppendLine($"            {castType} _crcExpected = ({castType})_crcAlgo.Result;");
            string actualExpr = crcField.IsEnum
                ? $"({castType})this.{crcField.MemberName}"
                : $"this.{crcField.MemberName}";
            sb.AppendLine($"            if ({actualExpr} != _crcExpected)");
            sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC mismatch on '{crcField.MemberName}': expected 0x{{_crcExpected:X}} but read 0x{{{actualExpr}:X}}\");");
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
        bool hasCtx = field.ListElementHasOwnContext;
        string ctxArg = hasCtx ? "_elemCtx" : "context";

        var ibsElem = $"(global::BitSerializer.IBitSerializable)_elem";

        // [BitFieldRelated(..., RelationKind = ByteLength)]: byte-budget driven collection.
        // RelationKind is stored as int (1 = ByteLength) to keep the generator runtime-independent.
        if (field.RelationKind == 1
            && !field.ConsumeRemaining
            && !field.FixedCount.HasValue
            && field.RelatedMemberName != null)
        {
            EmitListDeserializeByteLength(sb, field, helper, memberAccess, bitIndexVar, offsetExpr,
                elemBits, elemTypeFullName!, hasCtx, ctxArg, deserializeMethod);
            return;
        }

        // [BitFieldConsumeRemaining]: read until end of buffer
        if (field.ConsumeRemaining)
        {
            sb.AppendLine($"        int _crStart_{field.MemberName} = {offsetExpr};");
            sb.AppendLine($"        int _crRemainBits_{field.MemberName} = bytes.Length * 8 - _crStart_{field.MemberName};");
            sb.AppendLine($"        int _crCount_{field.MemberName} = _crRemainBits_{field.MemberName} > 0 ? _crRemainBits_{field.MemberName} / {elemBits} : 0;");
            if (field.IsArray)
                sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[_crCount_{field.MemberName}];");
            else
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>(_crCount_{field.MemberName});");
            sb.AppendLine($"        int {bitIndexVar} = _crStart_{field.MemberName};");
            sb.AppendLine($"        for (int _i = 0; _i < _crCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
            else
                sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
            return;
        }

        // [BitFieldCount(N, PadIfShort=true)]: read up to N, pad with default if data is short
        if (field.PadIfShort && field.FixedCount.HasValue)
        {
            int byteSize = field.FixedCount.Value * elemBits / 8;
            sb.AppendLine($"        int _pfsAvailBits_{field.MemberName} = bytes.Length * 8 - ({offsetExpr});");
            sb.AppendLine($"        int _pfsAvailElems_{field.MemberName} = _pfsAvailBits_{field.MemberName} > 0 ? _pfsAvailBits_{field.MemberName} / {elemBits} : 0;");
            sb.AppendLine($"        if (_pfsAvailElems_{field.MemberName} > {field.FixedCount.Value}) _pfsAvailElems_{field.MemberName} = {field.FixedCount.Value};");
            if (field.IsArray)
                sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[{field.FixedCount.Value}];");
            else
            {
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>({field.FixedCount.Value});");
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++) {memberAccess}.Add(default({elemTypeFullName}));");
            }
            sb.AppendLine($"        for (int _i = 0; _i < _pfsAvailElems_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits});");
            else
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {offsetExpr} + _i * {elemBits}, {elemBits});");
            sb.AppendLine("        }");
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {field.FixedCount.Value * elemBits};");
            return;
        }

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
                if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", $"{offsetExpr} + _i * {elemBits}");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg});");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");
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
                if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");
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
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
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
                    if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, $"{offsetExpr} + _i * {elemBits}");
                    sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg});");
                    if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
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
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            {bitIndexVar} += _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
                if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else if (field.ListElementIsNested)
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
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

    /// <summary>
    /// Emits the byte-budget driven list/array deserialization.
    /// The wire value from the related field is optionally routed through a length converter
    /// (Enhancement A), then the collection is filled until the resulting byte budget is
    /// consumed (Enhancement B). Over-run and under-run both raise InvalidDataException.
    /// </summary>
    private static void EmitListDeserializeByteLength(
        StringBuilder sb, BitFieldModel field, string helper, string memberAccess,
        string bitIndexVar, string offsetExpr,
        int elemBits, string elemTypeFullName, bool hasCtx, string ctxArg, string deserializeMethod)
    {
        var name = field.MemberName;
        var ibsElem = $"(global::BitSerializer.IBitSerializable)_elem";

        // Wire value -> byte budget, optionally via converter
        string wireExpr = $"this.{field.RelatedMemberName}";
        string budgetExpr = field.ValueConverterTypeFullName != null && field.ValueConverterHasDeserialize
            ? (field.ValueConverterDeserializeHasContext
                ? $"global::System.Convert.ToInt32({field.ValueConverterTypeFullName}.OnDeserializeConvert((object){wireExpr}, context))"
                : $"global::System.Convert.ToInt32({field.ValueConverterTypeFullName}.OnDeserializeConvert((object){wireExpr}))")
            : $"(int){wireExpr}";

        sb.AppendLine($"        int _budgetBytes_{name} = {budgetExpr};");
        sb.AppendLine($"        if (_budgetBytes_{name} < 0) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' is negative ({{_budgetBytes_{name}}}).\");");
        sb.AppendLine($"        int _endBit_{name} = {offsetExpr} + _budgetBytes_{name} * 8;");
        sb.AppendLine($"        if (_endBit_{name} > bytes.Length * 8) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' ({{_budgetBytes_{name}}} bytes) exceeds the remaining data ({{bytes.Length - ({offsetExpr}) / 8}} bytes).\");");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");

        // byte[] / numeric/enum element: fixed element width; budget must divide elemBits.
        if (!field.ListElementIsNested && !field.ListElementIsManualBitSerializable)
        {
            sb.AppendLine($"        if ((_budgetBytes_{name} * 8) % {elemBits} != 0) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' ({{_budgetBytes_{name}}} bytes) is not a multiple of the {elemBits}-bit element size.\");");
            sb.AppendLine($"        int _elemCount_{name} = (_budgetBytes_{name} * 8) / {elemBits};");
            if (field.IsArray)
                sb.AppendLine($"        {memberAccess} = new {elemTypeFullName}[_elemCount_{name}];");
            else
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>(_elemCount_{name});");
            sb.AppendLine($"        for (int _i = 0; _i < _elemCount_{name}; _i++)");
            sb.AppendLine("        {");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
            else
                sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
            return;
        }

        // Nested / manual-IBitSerializable element: while-loop until budget consumed.
        //   - ListElementIsNested && !HasDynamicLength: static-stride generated type (class) -> fixed bits.
        //   - ListElementHasDynamicLength: generated type (class) with dynamic length -> return value drives advance.
        //   - ListElementIsManualBitSerializable: hand-written IBitSerializable, possibly a struct.
        //     We MUST declare _elem as IBitSerializable (not as the concrete type) — otherwise casting a
        //     struct to the interface inside the loop boxes a copy and the actual _elem stays at default.
        //     The Count-driven branch upstream already takes this approach; mirror it here.
        bool manualElem = field.ListElementIsManualBitSerializable;
        bool nestedDynamicElem = field.ListElementHasDynamicLength;
        bool nestedStaticElem = !manualElem && !nestedDynamicElem;
        bool isArray = field.IsArray;

        // Arrays with budget-driven nested elements: collect into a List first, then copy.
        string collector = isArray ? $"_buf_{name}" : memberAccess;
        if (isArray)
            sb.AppendLine($"        var {collector} = new global::System.Collections.Generic.List<{elemTypeFullName}>();");
        else
            sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>();");

        sb.AppendLine($"        while ({bitIndexVar} < _endBit_{name})");
        sb.AppendLine("        {");

        if (manualElem)
        {
            // Interface-typed local so struct elements are boxed exactly once; the
            // Deserialize call mutates the box and we unbox back to the concrete type on Add.
            if (field.ListElementIsTypeParameter)
                sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = ({elemTypeFullName})global::System.Activator.CreateInstance(typeof({elemTypeFullName}))!;");
            else
                sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = new {elemTypeFullName}();");

            if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", bitIndexVar);

            sb.AppendLine($"            int _consumed_{name} = _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
            sb.AppendLine($"            if (_consumed_{name} <= 0) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' reported 0 consumed bits; byte-length budget loop cannot advance (element type may be zero-sized or its Deserialize returned a non-positive value).\");");
            sb.AppendLine($"            if ({bitIndexVar} + _consumed_{name} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
            sb.AppendLine($"            {bitIndexVar} += _consumed_{name};");

            if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");

            // Unbox back to the concrete element type.
            if (isArray)
                sb.AppendLine($"            {collector}.Add(({elemTypeFullName})_elem);");
            else
                sb.AppendLine($"            {memberAccess}.Add(({elemTypeFullName})_elem);");
        }
        else
        {
            // Generated [BitSerialize] type (always a class) — concrete-typed local, no boxing concern.
            sb.AppendLine($"            var _elem = new {elemTypeFullName}();");

            if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);

            if (nestedDynamicElem)
            {
                sb.AppendLine($"            int _consumed_{name} = _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                sb.AppendLine($"            if (_consumed_{name} <= 0) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' reported 0 consumed bits; byte-length budget loop cannot advance.\");");
                sb.AppendLine($"            if ({bitIndexVar} + _consumed_{name} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
                sb.AppendLine($"            {bitIndexVar} += _consumed_{name};");
            }
            else // nestedStaticElem
            {
                // Static-stride nested element — BITS026 has already rejected elemBits <= 0 or non-byte-aligned.
                sb.AppendLine($"            if ({bitIndexVar} + {elemBits} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
                sb.AppendLine($"            _elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg});");
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }

            if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);

            if (isArray)
                sb.AppendLine($"            {collector}.Add(_elem);");
            else
                sb.AppendLine($"            {memberAccess}.Add(_elem);");
        }

        sb.AppendLine("        }");

        sb.AppendLine($"        if ({bitIndexVar} != _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' left {{_endBit_{name} - {bitIndexVar}}} bit(s) of unread data.\");");

        if (isArray)
            sb.AppendLine($"        {memberAccess} = {collector}.ToArray();");
    }

    private static void EmitDeserializeElementContextBefore(StringBuilder sb, string elemIbsExpr, string elemBitOffsetExpr, string indent = "            ")
    {
        sb.AppendLine($"{indent}int _elemBitOff = {elemBitOffsetExpr};");
        sb.AppendLine($"{indent}var _elemCtx = ({elemIbsExpr}).DeserializeContext();");
        sb.AppendLine($"{indent}({elemIbsExpr}).BeforeDeserialize(_elemCtx, bytes.Slice(_elemBitOff / 8));");
    }

    private static void EmitDeserializeElementContextAfter(StringBuilder sb, string elemIbsExpr, string indent = "            ")
    {
        sb.AppendLine($"{indent}({elemIbsExpr}).AfterDeserialize(_elemCtx, bytes.Slice(_elemBitOff / 8));");
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
        // RelationKind=ByteLength on a list turns the converter into a length converter, which
        // is applied on the related wire field (not the collection value). Skip the list-value
        // post-convert step in that mode.
        if (field.IsList && field.RelationKind == 1) return;
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
