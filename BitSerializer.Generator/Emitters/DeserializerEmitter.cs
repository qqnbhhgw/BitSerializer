using System.Collections.Generic;
using System.Text;
using BitSerializer.Generator.Models;

namespace BitSerializer.Generator.Emitters;

internal static class DeserializerEmitter
{
    public static string EmitMethod(TypeModel model, string bitOrder)
        => EmitMethod(model, bitOrder, false);

    public static string EmitIntoMethod(TypeModel model, string bitOrder)
        => EmitMethod(model, bitOrder, true);

    private static string EmitMethod(TypeModel model, string bitOrder, bool reuseExisting)
    {
        var helper = bitOrder == "LSB" ? "global::BitSerializer.BitHelperLSB" : "global::BitSerializer.BitHelperMSB";
        var deserializeMethod = $"Deserialize{bitOrder}";
        var methodName = reuseExisting ? $"Deserialize{bitOrder}Into" : deserializeMethod;

        var sb = new StringBuilder();
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        string reuseParameter = reuseExisting ? ", bool reuseOnly" : "";
        bool terminalPadIfShort = model.Fields.Count > 0 && model.Fields[model.Fields.Count - 1].PadIfShort;
        sb.AppendLine($"    public {newKeyword}int {methodName}(global::System.ReadOnlySpan<byte> bytes, int bitOffset, object? context{reuseParameter})");
        sb.AppendLine("    {");
        sb.AppendLine("        OnDeserializing(context);");

        // Issue: fields with "derived getter + empty setter" don't keep the wire value, so any later
        // field that reads `this.<related>` to know its own size/discriminator/budget sees a stale
        // computed value instead of what was actually on the wire. Fix: for every field that is
        // referenced by a downstream field (list count / list ByteLength / nested ByteLength /
        // length-field string / polymorphic discriminator), also stash the just-deserialized value
        // into a `_wire_<name>` local at method scope. Dependent emit sites prefer that local over
        // `this.<name>`. The local survives the no-op setter and is independent of the property's
        // getter side effects.
        //
        // Codex review P1: `_wire_<name>` is only declared at the moment the referenced field is
        // deserialized. If a dependent field is declared BEFORE its related field (the
        // analyzer doesn't currently enforce order), the generated `_wire_<name>` reference would
        // land before its declaration → CS0841. To stay forward-compatible with that layout we
        // track the cache as a *runtime* set that grows during the per-field emit loop, and
        // ReadFieldExpr falls back to `this.<name>` when the local isn't yet declared. The
        // fallback evaluates the property's *uninitialized* default rather than the wire value, so
        // we also emit BITS051 to flag the broken ordering at compile time — but the runtime
        // fallback keeps the generator from producing uncompilable output.
        var referencedFieldNames = ComputeReferencedFieldNames(model);
        var emittedWireLocals = new HashSet<string>();

        string? runtimeOffsetVar = null;
        int runtimeStaticEnd = 0;

        if (model.HasBitSerializableBaseType)
        {
            if (model.BaseHasDynamicLength)
            {
                string baseArgs = reuseExisting ? "bytes, bitOffset, context, reuseOnly" : "bytes, bitOffset, context";
                sb.AppendLine($"        int _baseEndBit = bitOffset + base.{methodName}({baseArgs});");
                runtimeOffsetVar = "_baseEndBit";
                runtimeStaticEnd = model.BaseBitLength;
            }
            else
            {
                string baseArgs = reuseExisting ? "bytes, bitOffset, context, reuseOnly" : "bytes, bitOffset, context";
                sb.AppendLine($"        base.{methodName}({baseArgs});");
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
            else if (field.IsLengthPrefixString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitLengthPrefixStringDeserialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsLengthFieldString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitLengthFieldStringDeserialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr, emittedWireLocals);
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsList)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitListDeserialize(sb, field, helper, memberAccess, fieldEndVar, bitOrder, offsetExpr, emittedWireLocals, reuseExisting);
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsPolymorphic)
            {
                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder, offsetExpr, fieldEndVar, emittedWireLocals, reuseExisting);
                }
                else
                {
                    EmitPolymorphicDeserialize(sb, field, helper, memberAccess, bitOrder, offsetExpr, null, emittedWireLocals, reuseExisting);
                }
                EmitDeserializeConverter(sb, field, memberAccess);
            }
            else if (field.IsTypeParameter)
            {
                if (reuseExisting)
                {
                    if (field.NestedIsReferenceType && !(field.RelationKind == 1 && field.RelatedMemberName != null))
                    {
                        sb.AppendLine($"        if ({memberAccess} == null)");
                        sb.AppendLine($"            throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", 1);");
                    }
                }
                else
                {
                    sb.AppendLine($"        {memberAccess} = ({field.MemberTypeName})global::System.Activator.CreateInstance(typeof({field.MemberTypeName}))!;");
                }
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                if (field.RelationKind == 1 && field.RelatedMemberName != null)
                {
                    string typeParamCall = reuseExisting
                        ? $"((global::BitSerializer.IBitSerializable){memberAccess}).{deserializeMethod}Into(bytes, {offsetExpr}, context, reuseOnly)"
                        : $"((global::BitSerializer.IBitSerializable){memberAccess}).{deserializeMethod}(bytes, {offsetExpr}, context)";
                    EmitNestedByteLengthRead(sb, field, helper, memberAccess, fieldEndVar, offsetExpr, deserializeMethod, typeParamCall, emittedWireLocals, reuseExisting);
                }
                else
                {
                    string call = reuseExisting
                        ? $"((global::BitSerializer.IBitSerializable){memberAccess}).{deserializeMethod}Into(bytes, {offsetExpr}, context, reuseOnly)"
                        : $"((global::BitSerializer.IBitSerializable){memberAccess}).{deserializeMethod}(bytes, {offsetExpr}, context)";
                    sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {call};");
                }
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
                    if (reuseExisting)
                    {
                        if (field.NestedIsReferenceType && !(field.RelationKind == 1 && field.RelatedMemberName != null))
                        {
                            sb.AppendLine($"        if ({memberAccess} == null)");
                            sb.AppendLine($"            throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", 1);");
                        }
                        sb.AppendLine($"        global::BitSerializer.IBitSerializable {localVar} = (global::BitSerializer.IBitSerializable){memberAccess};");
                    }
                    else
                    {
                        sb.AppendLine($"        global::BitSerializer.IBitSerializable {localVar} = new {field.MemberTypeFullName}();");
                    }
                    string ctxArg = "context";
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                        sb.AppendLine($"        var _nestedCtx_{field.MemberName} = {localVar}.DeserializeContext();");
                        sb.AppendLine($"        {localVar}.BeforeDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                        ctxArg = $"_nestedCtx_{field.MemberName}";
                    }
                    if (field.RelationKind == 1 && field.RelatedMemberName != null)
                    {
                        fieldEndVar = $"_bitIndex_{field.MemberName}";
                        EmitNestedByteLengthReadInterface(sb, field, fieldEndVar, offsetExpr, deserializeMethod, localVar, ctxArg, emittedWireLocals, reuseExisting);
                    }
                    else if (usesRuntimeBitLength)
                    {
                        fieldEndVar = $"_bitIndex_{field.MemberName}";
                        string call = reuseExisting
                            ? $"{localVar}.{deserializeMethod}Into(bytes, {offsetExpr}, {ctxArg}, reuseOnly)"
                            : $"{localVar}.{deserializeMethod}(bytes, {offsetExpr}, {ctxArg})";
                        sb.AppendLine($"        int {fieldEndVar} = {offsetExpr} + {call};");
                    }
                    else
                    {
                        string call = reuseExisting
                            ? $"{localVar}.{deserializeMethod}Into(bytes, {offsetExpr}, {ctxArg}, reuseOnly)"
                            : $"{localVar}.{deserializeMethod}(bytes, {offsetExpr}, {ctxArg})";
                        sb.AppendLine($"        {call};");
                    }
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        {localVar}.AfterDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                    }
                    sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){localVar};");
                }
                else
                {
                    if (reuseExisting && field.NestedIsReferenceType && !(field.RelationKind == 1 && field.RelatedMemberName != null))
                    {
                        sb.AppendLine($"        if ({memberAccess} == null)");
                        sb.AppendLine("        {");
                        sb.AppendLine($"            if (reuseOnly) throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", 1);");
                        sb.AppendLine($"            {memberAccess} = new {field.MemberTypeFullName}();");
                        sb.AppendLine("        }");
                    }
                    else if (!reuseExisting)
                    {
                        sb.AppendLine($"        {memberAccess} = new {field.MemberTypeFullName}();");
                    }
                    string ctxArg = "context";
                    string ibsExpr = $"(global::BitSerializer.IBitSerializable){memberAccess}";
                    if (field.NestedHasOwnContext)
                    {
                        sb.AppendLine($"        int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                        sb.AppendLine($"        var _nestedCtx_{field.MemberName} = ({ibsExpr}).DeserializeContext();");
                        sb.AppendLine($"        ({ibsExpr}).BeforeDeserialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                        ctxArg = $"_nestedCtx_{field.MemberName}";
                    }
                    string callExpr = reuseExisting
                        ? $"{memberAccess}.{deserializeMethod}Into(bytes, {offsetExpr}, {ctxArg}, reuseOnly)"
                        : $"{memberAccess}.{deserializeMethod}(bytes, {offsetExpr}, {ctxArg})";

                    if (field.RelationKind == 1 && field.RelatedMemberName != null)
                    {
                        fieldEndVar = $"_bitIndex_{field.MemberName}";
                        EmitNestedByteLengthRead(sb, field, helper, memberAccess, fieldEndVar, offsetExpr, deserializeMethod, callExpr, emittedWireLocals, reuseExisting);
                    }
                    else if (usesRuntimeBitLength)
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
                // Primitive converter is handled inside EmitPrimitiveDeserialize.
                // [BitField(Endian = ...)] override mirrors the serializer side.
                var fieldHelper = ResolveFieldHelper(helper, field.Endian);
                EmitPrimitiveDeserialize(sb, field, fieldHelper, memberAccess, offsetExpr, referencedFieldNames);
            }

            // Mark this field's wire local as now-in-scope so a subsequent dependent field's
            // ReadFieldExpr returns `_wire_<name>` instead of falling back to `this.<name>`.
            // Only meaningful for numeric/enum fields (the only ones that can be length carriers
            // or polymorphic discriminators) but applying it uniformly keeps the bookkeeping simple.
            if (field.IsNumericOrEnum && referencedFieldNames.Contains(field.MemberName))
                emittedWireLocals.Add(field.MemberName);

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
            if (crc.IsWholeBuffer)
            {
                // WholeBuffer mode mirrors SerializerEmitter — span the whole type buffer minus skip ranges.
                // Same byte-alignment guards as the serializer (review P1): without them, division-by-8
                // truncation would silently include neighbor bytes or drop a trailing partial byte,
                // producing a CRC mismatch that doesn't surface the real cause.
                string endBitExpr = BuildEndBitExpr(model, runtimeOffsetVar, runtimeStaticEnd);
                sb.AppendLine($"            if ((bitOffset & 7) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer requires byte-aligned bitOffset on Deserialize; got {{bitOffset}} (bitOffset & 7 = {{bitOffset & 7}}).\");");
                sb.AppendLine($"            int _crcEndBit_{crc.TargetFieldName}_wb = {endBitExpr};");
                sb.AppendLine($"            if ((_crcEndBit_{crc.TargetFieldName}_wb & 7) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer requires the type's read total bit length to be byte-aligned on Deserialize; got {{_crcEndBit_{crc.TargetFieldName}_wb - bitOffset}} bits ({{(_crcEndBit_{crc.TargetFieldName}_wb - bitOffset) & 7}} bits past the last byte boundary).\");");
                sb.AppendLine($"            int _crcStart = (bitOffset / 8) + {crc.SkipHeadBytes};");
                sb.AppendLine($"            int _crcEnd   = (_crcEndBit_{crc.TargetFieldName}_wb / 8) - {crc.SkipTailBytes};");
                // Reject empty CRC range too (review P2) — mirror serializer side for ValidateOnDeserialize.
                sb.AppendLine($"            if (_crcEnd <= _crcStart)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer range is empty or negative on Deserialize: SkipHeadBytes ({crc.SkipHeadBytes}) + SkipTailBytes ({crc.SkipTailBytes}) ≥ total read bytes ({{(_crcEndBit_{crc.TargetFieldName}_wb - bitOffset) / 8}}).\");");
            }
            else if (crc.HasDynamicInclude)
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
            if (crc.SupportsStaticCompute)
            {
                sb.AppendLine($"            ulong _crcResult = {crc.AlgorithmTypeFullName}.Compute(bytes.Slice(_crcStart, _crcEnd - _crcStart), {crc.InitialValue}UL);");
            }
            else
            {
                sb.AppendLine($"            var _crcAlgo = new {crc.AlgorithmTypeFullName}();");
                sb.AppendLine($"            _crcAlgo.Reset({crc.InitialValue}UL);");
                sb.AppendLine("            _crcAlgo.Update(bytes.Slice(_crcStart, _crcEnd - _crcStart));");
                sb.AppendLine("            ulong _crcResult = _crcAlgo.Result;");
            }
            string castType = crcField.IsEnum ? crcField.EnumUnderlyingTypeName! : crcField.MemberTypeName;
            sb.AppendLine($"            {castType} _crcExpected = ({castType})_crcResult;");
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
            string consumedExpr = $"{returnExpr} - bitOffset";
            if (terminalPadIfShort)
                consumedExpr = $"global::System.Math.Min({consumedExpr}, global::System.Math.Max(0, bytes.Length * 8 - bitOffset))";
            sb.AppendLine($"        return {consumedExpr};");
        }
        else
        {
            string consumedExpr = model.TotalBitLength.ToString();
            if (terminalPadIfShort)
                consumedExpr = $"global::System.Math.Min({consumedExpr}, global::System.Math.Max(0, bytes.Length * 8 - bitOffset))";
            sb.AppendLine($"        return {consumedExpr};");
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

    private static void EmitPrimitiveDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr, HashSet<string>? referencedFieldNames = null)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;
        // Codex review P1 (forward-ref fix): `referencedFieldNames` is the *static* set of fields
        // that some downstream dependent reads. Whether to emit `_wire_<name>` is decided here from
        // that set; the actual "is the local in scope right now?" tracking lives in
        // `emittedWireLocals` (mutated in the main loop AFTER this method returns).
        bool cacheWire = referencedFieldNames != null && referencedFieldNames.Contains(field.MemberName);
        string wireLocal = $"_wire_{field.MemberName}";

        // [BitFieldValue(constant, Verify=true)]: read the value, optionally throw on mismatch, then
        // set the property (so callers see the actual wire value for diagnostics).
        if (field.HasConstantValue)
        {
            string name = field.MemberName;
            string expected = $"unchecked(({typeName}){field.ConstantValue}L)";
            sb.AppendLine($"        {typeName} _fixedVal_{name} = {BuildPrimitiveRead(field, helper, typeName, offsetExpr)};");
            if (field.ConstantValueVerify)
            {
                sb.AppendLine($"        if (_fixedVal_{name} != {expected})");
                sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Field '{name}' expected pinned constant 0x{{({expected}):X}} but read 0x{{_fixedVal_{name}:X}}\");");
            }
            string assignExpr = field.IsEnum
                ? $"({field.MemberTypeFullName})_fixedVal_{name}"
                : $"_fixedVal_{name}";
            sb.AppendLine($"        {memberAccess} = {assignExpr};");
            if (cacheWire)
            {
                // Cache the post-cast property-typed value so dependent reads see it even if the
                // property has a no-op setter. For enum carriers we cache the underlying integer.
                sb.AppendLine($"        var {wireLocal} = {assignExpr};");
            }
            return;
        }

        string valueExpr;
        if (field.ValueConverterTypeFullName != null && field.ValueConverterHasDeserialize)
        {
            var typedRawValue = BuildPrimitiveRead(field, helper, field.ValueConverterWireTypeFullName ?? typeName, offsetExpr);
            var rawValue = $"(object){BuildPrimitiveRead(field, helper, typeName, offsetExpr)}";
            var convertCall = field.ValueConverterIsStronglyTyped
                ? field.ValueConverterIsContextTyped
                    ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({typedRawValue}, ({field.ValueConverterContextTypeFullName})context!)"
                    : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({typedRawValue})"
                : field.ValueConverterDeserializeHasContext
                    ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({rawValue}, context)"
                    : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({rawValue})";
            var castType = field.IsEnum ? field.MemberTypeFullName : typeName;
            valueExpr = $"({castType}){convertCall}";
        }
        else if (field.IsEnum)
        {
            valueExpr = $"({field.MemberTypeFullName}){BuildPrimitiveRead(field, helper, typeName, offsetExpr)}";
        }
        else
        {
            valueExpr = BuildPrimitiveRead(field, helper, typeName, offsetExpr);
        }

        if (cacheWire)
        {
            // Stash the wire value into a method-scope local BEFORE the property assignment so a
            // downstream field that reads `_wire_<name>` is independent of any side effects on the
            // property setter (including the no-op pattern from BinarySerialization's derived
            // length / discriminator getters).
            sb.AppendLine($"        var {wireLocal} = {valueExpr};");
            sb.AppendLine($"        {memberAccess} = {wireLocal};");
        }
        else
        {
            sb.AppendLine($"        {memberAccess} = {valueExpr};");
        }
    }

    /// <summary>
    /// Builds the set of field names that need their wire value cached because a downstream field
    /// reads them via `[BitFieldRelated(...)]` (list count, list ByteLength, polymorphic discriminator,
    /// nested ByteLength) or `[BitLengthFieldString(nameof(...))]`. Fields outside this set don't pay
    /// for the cache local.
    /// </summary>
    private static HashSet<string> ComputeReferencedFieldNames(TypeModel model)
    {
        var set = new HashSet<string>();
        foreach (var f in model.Fields)
        {
            if (f.RelatedMemberName != null
                && (f.IsList || f.IsPolymorphic || f.IsNestedType || f.IsTypeParameter))
            {
                set.Add(f.RelatedMemberName);
            }
            // v0.12.0: secondary [BitFieldRelated] binding (polymorphic + ByteLength budget) —
            // the byte-length carrier needs the same wire-cache treatment as any other dependent
            // reference, otherwise a "derived getter + empty setter" carrier would read stale
            // values on deserialize (Issue resolved in v0.10.x for the single-binding case).
            if (f.SecondaryRelatedMemberName != null)
            {
                set.Add(f.SecondaryRelatedMemberName);
            }
            if (f.IsLengthFieldString && f.LengthFieldMemberName != null)
            {
                set.Add(f.LengthFieldMemberName);
            }
        }
        return set;
    }

    /// <summary>
    /// Returns the expression a downstream field should use to read the value of <paramref name="memberName"/>.
    /// Prefers the cached `_wire_<name>` local when ComputeReferencedFieldNames included it (which is
    /// always true for legitimate references — defensive fallback `this.<member>` keeps the generator
    /// compiling if a future code path forgets to mark the field).
    /// </summary>
    private static string ReadFieldExpr(HashSet<string>? emittedWireLocals, string memberName)
    {
        return emittedWireLocals != null && emittedWireLocals.Contains(memberName)
            ? $"_wire_{memberName}"
            : $"this.{memberName}";
    }

    /// <summary>
    /// Returns the unsigned twin of a primitive integer type name. Used by
    /// [BitLengthFieldString] to reinterpret a signed length carrier's bit pattern as the
    /// unsigned byte count the serializer wrote with bit-width semantics.
    /// </summary>
    private static string UnsignedTwinOf(string typeName) => typeName switch
    {
        "sbyte" => "byte",
        "short" => "ushort",
        "int" => "uint",
        "long" => "ulong",
        _ => typeName, // already unsigned, or unknown — leave as-is (BITS046/047 prevents arrival)
    };

    private static void EmitListDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string bitOrder, string offsetExpr, HashSet<string>? emittedWireLocals = null, bool reuseExisting = false)
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
                elemBits, elemTypeFullName!, hasCtx, ctxArg, deserializeMethod, emittedWireLocals, reuseExisting);
            return;
        }

        // [BitFieldConsumeRemaining]: read until end of buffer
        if (field.ConsumeRemaining)
        {
            sb.AppendLine($"        int _crStart_{field.MemberName} = {offsetExpr};");
            sb.AppendLine($"        int _crRemainBits_{field.MemberName} = bytes.Length * 8 - _crStart_{field.MemberName};");
            sb.AppendLine($"        int _crCount_{field.MemberName} = _crRemainBits_{field.MemberName} > 0 ? _crRemainBits_{field.MemberName} / {elemBits} : 0;");
            EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, $"_crCount_{field.MemberName}", reuseExisting);
            sb.AppendLine($"        int {bitIndexVar} = _crStart_{field.MemberName};");
            sb.AppendLine($"        for (int _i = 0; _i < _crCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
            else
                sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
            EmitCollectionFinalization(sb, field, memberAccess, $"_crCount_{field.MemberName}", reuseExisting);
            return;
        }

        // [BitFieldCount(N, PadIfShort=true)]: read up to N, pad with default if data is short
        if (field.PadIfShort && field.FixedCount.HasValue)
        {
            int byteSize = field.FixedCount.Value * elemBits / 8;
            sb.AppendLine($"        int _pfsAvailBits_{field.MemberName} = bytes.Length * 8 - ({offsetExpr});");
            sb.AppendLine($"        int _pfsAvailElems_{field.MemberName} = _pfsAvailBits_{field.MemberName} > 0 ? _pfsAvailBits_{field.MemberName} / {elemBits} : 0;");
            sb.AppendLine($"        if (_pfsAvailElems_{field.MemberName} > {field.FixedCount.Value}) _pfsAvailElems_{field.MemberName} = {field.FixedCount.Value};");
            EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, field.FixedCount.Value.ToString(), reuseExisting);
            if (!field.IsArray)
            {
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
            EmitCollectionFinalization(sb, field, memberAccess, field.FixedCount.Value.ToString(), reuseExisting);
            return;
        }

        if (field.ListElementIsManualBitSerializable)
        {
            if (field.FixedCount.HasValue && elemBits > 0)
            {
                // Fixed-count manual IBitSerializable with declared element width: use fixed stride
                EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, field.FixedCount.Value.ToString(), reuseExisting);
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                EmitManualElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", $"{offsetExpr} + _i * {elemBits}");
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg})";
                sb.AppendLine($"            {call};");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");
                if (reuseExisting && field.ListElementIsReferenceType)
                {
                }
                else if (field.IsArray)
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
                    EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, field.FixedCount.Value.ToString(), reuseExisting);
                }
                else
                {
                    countExpr = $"_listCount_{field.MemberName}";
                    sb.AppendLine($"        int {countExpr} = (int)({ReadFieldExpr(emittedWireLocals, field.RelatedMemberName!)});");
                    EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, countExpr, reuseExisting);
                }
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
                sb.AppendLine($"        for (int _i = 0; _i < {countExpr}; _i++)");
                sb.AppendLine("        {");
                EmitManualElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", bitIndexVar);
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            {bitIndexVar} += {call};");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");
                if (reuseExisting && field.ListElementIsReferenceType)
                {
                }
                else if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = ({elemTypeFullName})_elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(({elemTypeFullName})_elem);");
                sb.AppendLine("        }");
            }
        }
        else if (field.FixedCount.HasValue)
        {
            EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, field.FixedCount.Value.ToString(), reuseExisting);
            if (field.ListElementHasDynamicLength)
            {
                // Dynamic nested elements: use runtime offset tracking via return value
                sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
                sb.AppendLine($"        for (int _i = 0; _i < {field.FixedCount.Value}; _i++)");
                sb.AppendLine("        {");
                EmitNestedElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            {bitIndexVar} += {call};");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
                if (reuseExisting && field.ListElementIsReferenceType)
                {
                }
                else if (field.IsArray)
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
                    EmitNestedElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                    if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, $"{offsetExpr} + _i * {elemBits}");
                    string call = reuseExisting && field.ListElementIsReferenceType
                        ? $"_elem.{deserializeMethod}Into(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg}, reuseOnly)"
                        : $"_elem.{deserializeMethod}(bytes, {offsetExpr} + _i * {elemBits}, {ctxArg})";
                    sb.AppendLine($"            {call};");
                    if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
                    if (reuseExisting && field.ListElementIsReferenceType)
                    {
                    }
                    else if (field.IsArray)
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
            sb.AppendLine($"        int _listCount_{field.MemberName} = (int)({ReadFieldExpr(emittedWireLocals, field.RelatedMemberName!)});");
            EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName!, $"_listCount_{field.MemberName}", reuseExisting);
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        for (int _i = 0; _i < _listCount_{field.MemberName}; _i++)");
            sb.AppendLine("        {");
            if (field.ListElementHasDynamicLength)
            {
                EmitNestedElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            {bitIndexVar} += {call};");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
                if (reuseExisting && field.ListElementIsReferenceType)
                {
                }
                else if (field.IsArray)
                    sb.AppendLine($"            {memberAccess}[_i] = _elem;");
                else
                    sb.AppendLine($"            {memberAccess}.Add(_elem);");
            }
            else if (field.ListElementIsNested)
            {
                EmitNestedElementCreation(sb, field, memberAccess, elemTypeFullName!, reuseExisting);
                if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            {call};");
                if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);
                if (reuseExisting && field.ListElementIsReferenceType)
                {
                }
                else if (field.IsArray)
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

        string finalCountExpr = field.FixedCount.HasValue
            ? field.FixedCount.Value.ToString()
            : $"_listCount_{field.MemberName}";
        EmitCollectionFinalization(sb, field, memberAccess, finalCountExpr, reuseExisting);
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
        int elemBits, string elemTypeFullName, bool hasCtx, string ctxArg, string deserializeMethod,
        HashSet<string>? emittedWireLocals = null, bool reuseExisting = false)
    {
        var name = field.MemberName;
        var ibsElem = $"(global::BitSerializer.IBitSerializable)_elem";

        // Wire value -> byte budget, optionally via converter. Use the cached `_wire_<related>`
        // local when available so the read is independent of any property setter side effects
        // (Issue: derived getter + empty setter pattern).
        string wireExpr = ReadFieldExpr(emittedWireLocals, field.RelatedMemberName!);
        string budgetExpr = field.ValueConverterTypeFullName != null && field.ValueConverterHasDeserialize
            ? (field.ValueConverterDeserializeHasContext
                ? $"global::System.Convert.ToInt32({field.ValueConverterTypeFullName}.OnDeserializeConvert((object){wireExpr}, context))"
                : $"global::System.Convert.ToInt32({field.ValueConverterTypeFullName}.OnDeserializeConvert((object){wireExpr}))")
            : $"(int){wireExpr}";

        sb.AppendLine($"        int _budgetBytes_{name} = {budgetExpr};");
        sb.AppendLine($"        if (_budgetBytes_{name} < 0) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' is negative ({{_budgetBytes_{name}}}).\");");
        sb.AppendLine($"        int _endBit_{name} = {offsetExpr} + _budgetBytes_{name} * 8;");
        sb.AppendLine($"        if (_endBit_{name} > bytes.Length * 8) throw new global::BitSerializer.BitSerializationNeedMoreDataException($\"Byte-length budget for '{name}' ({{_budgetBytes_{name}}} bytes) exceeds the remaining data ({{bytes.Length - ({offsetExpr}) / 8}} bytes).\");");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");

        // byte[] / numeric/enum element: fixed element width; budget must divide elemBits.
        if (!field.ListElementIsNested && !field.ListElementIsManualBitSerializable)
        {
            sb.AppendLine($"        if ((_budgetBytes_{name} * 8) % {elemBits} != 0) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' ({{_budgetBytes_{name}}} bytes) is not a multiple of the {elemBits}-bit element size.\");");
            sb.AppendLine($"        int _elemCount_{name} = (_budgetBytes_{name} * 8) / {elemBits};");
            EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName, $"_elemCount_{name}", reuseExisting);
            sb.AppendLine($"        for (int _i = 0; _i < _elemCount_{name}; _i++)");
            sb.AppendLine("        {");
            if (field.IsArray)
                sb.AppendLine($"            {memberAccess}[_i] = {helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits});");
            else
                sb.AppendLine($"            {memberAccess}.Add({helper}.ValueLength<{elemTypeFullName}>(bytes, {bitIndexVar}, {elemBits}));");
            sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            sb.AppendLine("        }");
            EmitCollectionFinalization(sb, field, memberAccess, $"_elemCount_{name}", reuseExisting);
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

        // Nested arrays have no upfront element count. Compatibility mode keeps the historical
        // collector; strict mode writes into the caller-provided array and checks each slot.
        string collector = isArray ? $"_buf_{name}" : memberAccess;
        if (isArray && !reuseExisting)
            sb.AppendLine($"        var {collector} = new global::System.Collections.Generic.List<{elemTypeFullName}>();");
        else if (isArray)
        {
            sb.AppendLine($"        if ({memberAccess} == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", 1);");
            sb.AppendLine($"        int _arrayIndex_{name} = 0;");
        }
        else
        {
            if (!reuseExisting)
            {
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elemTypeFullName}>();");
            }
            else if (field.ListElementIsReferenceType)
            {
                sb.AppendLine($"        if ({memberAccess} == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", 1);");
                sb.AppendLine($"        int _listIndex_{name} = 0;");
            }
            else
            {
                EmitCollectionPreparation(sb, field, memberAccess, elemTypeFullName, "0", true);
            }
        }

        sb.AppendLine($"        while ({bitIndexVar} < _endBit_{name})");
        sb.AppendLine("        {");

        if (manualElem)
        {
            // Interface-typed local so struct elements are boxed exactly once; the
            // Deserialize call mutates the box and we unbox back to the concrete type on Add.
            if (reuseExisting && field.ListElementIsReferenceType)
            {
                string indexExpr = isArray ? $"_arrayIndex_{name}" : $"_listIndex_{name}";
                string lengthExpr = isArray ? $"{memberAccess}.Length" : $"{memberAccess}.Count";
                string sizeReason = isArray ? "has insufficient array length" : "has insufficient list count";
                sb.AppendLine($"            if ({indexExpr} >= {lengthExpr}) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", {indexExpr} + 1, \"{sizeReason}\");");
                sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = (global::BitSerializer.IBitSerializable){memberAccess}[{indexExpr}];");
                sb.AppendLine($"            if (_elem == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", {indexExpr} + 1, $\"contains a null reusable element at index {{{indexExpr}}}\");");
            }
            else if (field.ListElementIsTypeParameter)
            {
                sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = ({elemTypeFullName})global::System.Activator.CreateInstance(typeof({elemTypeFullName}))!;");
            }
            else
            {
                sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = new {elemTypeFullName}();");
            }

            if (hasCtx) EmitDeserializeElementContextBefore(sb, "_elem", bitIndexVar);

            string manualCall = reuseExisting && field.ListElementIsReferenceType
                ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
            sb.AppendLine($"            int _consumed_{name} = {manualCall};");
            sb.AppendLine($"            if (_consumed_{name} <= 0) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' reported 0 consumed bits; byte-length budget loop cannot advance (element type may be zero-sized or its Deserialize returned a non-positive value).\");");
            sb.AppendLine($"            if ({bitIndexVar} + _consumed_{name} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
            sb.AppendLine($"            {bitIndexVar} += _consumed_{name};");

            if (hasCtx) EmitDeserializeElementContextAfter(sb, "_elem");

            // Unbox back to the concrete element type.
            if (reuseExisting && field.ListElementIsReferenceType)
            {
                if (isArray)
                    sb.AppendLine($"            _arrayIndex_{name}++;");
                else
                    sb.AppendLine($"            _listIndex_{name}++;");
            }
            else if (isArray && reuseExisting)
            {
                sb.AppendLine($"            if (_arrayIndex_{name} >= {memberAccess}.Length) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", _arrayIndex_{name} + 1, \"has insufficient array length\");");
                sb.AppendLine($"            {memberAccess}[_arrayIndex_{name}++] = ({elemTypeFullName})_elem;");
            }
            else if (isArray)
                sb.AppendLine($"            {collector}.Add(({elemTypeFullName})_elem);");
            else
                sb.AppendLine($"            {memberAccess}.Add(({elemTypeFullName})_elem);");
        }
        else
        {
            // Generated [BitSerialize] type (always a class) — concrete-typed local, no boxing concern.
            if (reuseExisting && field.ListElementIsReferenceType)
            {
                string indexExpr = isArray ? $"_arrayIndex_{name}" : $"_listIndex_{name}";
                string lengthExpr = isArray ? $"{memberAccess}.Length" : $"{memberAccess}.Count";
                string sizeReason = isArray ? "has insufficient array length" : "has insufficient list count";
                sb.AppendLine($"            if ({indexExpr} >= {lengthExpr}) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", {indexExpr} + 1, \"{sizeReason}\");");
                sb.AppendLine($"            var _elem = {memberAccess}[{indexExpr}];");
                sb.AppendLine($"            if (_elem == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", {indexExpr} + 1, $\"contains a null reusable element at index {{{indexExpr}}}\");");
            }
            else
            {
                sb.AppendLine($"            var _elem = new {elemTypeFullName}();");
            }

            if (hasCtx) EmitDeserializeElementContextBefore(sb, ibsElem, bitIndexVar);

            if (nestedDynamicElem)
            {
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            int _consumed_{name} = {call};");
                sb.AppendLine($"            if (_consumed_{name} <= 0) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' reported 0 consumed bits; byte-length budget loop cannot advance.\");");
                sb.AppendLine($"            if ({bitIndexVar} + _consumed_{name} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
                sb.AppendLine($"            {bitIndexVar} += _consumed_{name};");
            }
            else // nestedStaticElem
            {
                // Static-stride nested element — BITS026 has already rejected elemBits <= 0 or non-byte-aligned.
                sb.AppendLine($"            if ({bitIndexVar} + {elemBits} > _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Element at offset {{{bitIndexVar}}} in '{name}' overruns the {{_budgetBytes_{name}}}-byte budget.\");");
                string call = reuseExisting && field.ListElementIsReferenceType
                    ? $"_elem.{deserializeMethod}Into(bytes, {bitIndexVar}, {ctxArg}, reuseOnly)"
                    : $"_elem.{deserializeMethod}(bytes, {bitIndexVar}, {ctxArg})";
                sb.AppendLine($"            {call};");
                sb.AppendLine($"            {bitIndexVar} += {elemBits};");
            }

            if (hasCtx) EmitDeserializeElementContextAfter(sb, ibsElem);

            if (reuseExisting && field.ListElementIsReferenceType)
            {
                if (isArray)
                    sb.AppendLine($"            _arrayIndex_{name}++;");
                else
                    sb.AppendLine($"            _listIndex_{name}++;");
            }
            else if (isArray && reuseExisting)
            {
                sb.AppendLine($"            if (_arrayIndex_{name} >= {memberAccess}.Length) throw new global::BitSerializer.BitSerializationCapacityException(\"{name}\", _arrayIndex_{name} + 1, \"has insufficient array length\");");
                sb.AppendLine($"            {memberAccess}[_arrayIndex_{name}++] = _elem;");
            }
            else if (isArray)
                sb.AppendLine($"            {collector}.Add(_elem);");
            else
                sb.AppendLine($"            {memberAccess}.Add(_elem);");
        }

        sb.AppendLine("        }");

        sb.AppendLine($"        if ({bitIndexVar} != _endBit_{name}) throw new global::System.IO.InvalidDataException($\"Byte-length budget for '{name}' left {{_endBit_{name} - {bitIndexVar}}} bit(s) of unread data.\");");

        if (reuseExisting && (isArray || field.ListElementIsReferenceType))
        {
            string countExpr = isArray ? $"_arrayIndex_{name}" : $"_listIndex_{name}";
            EmitCollectionFinalization(sb, field, memberAccess, countExpr, true);
        }

        if (isArray && !reuseExisting)
            sb.AppendLine($"        {memberAccess} = {collector}.ToArray();");
    }

    private static string BuildPrimitiveRead(BitFieldModel field, string helper, string typeName, string offsetExpr)
    {
        string requiredEnd = $"({offsetExpr}) + {field.BitLength}";
        string insufficient = $"{requiredEnd} > bytes.Length * 8";
        string needMore = $"throw new global::BitSerializer.BitSerializationNeedMoreDataException(\"Field '{field.MemberName}' requires {field.BitLength} bits.\")";
        if ((field.BitStartIndex & 7) != 0 || (field.BitLength != 8 && field.BitLength != 16 && field.BitLength != 32 && field.BitLength != 64))
            return $"({insufficient} ? {needMore} : {helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}))";

        string byteOffset = $"({offsetExpr}) / 8";
        if (field.BitLength == 8)
            return $"({insufficient} ? {needMore} : ((({offsetExpr}) & 7) == 0 ? ({typeName})bytes[{byteOffset}] : {helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength})))";

        string endian = helper.Contains("LSB") ? "LittleEndian" : "BigEndian";
        string methodType = field.BitLength == 16 ? "UInt16" : field.BitLength == 32 ? "UInt32" : "UInt64";
        return $"({insufficient} ? {needMore} : ((({offsetExpr}) & 7) == 0 ? unchecked(({typeName})global::System.Buffers.Binary.BinaryPrimitives.Read{methodType}{endian}(bytes.Slice({byteOffset}))) : {helper}.ValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength})))";
    }

    private static void EmitCollectionPreparation(StringBuilder sb, BitFieldModel field, string memberAccess, string elementType, string countExpr, bool reuseExisting)
    {
        if (!reuseExisting)
        {
            if (field.IsArray)
                sb.AppendLine($"        {memberAccess} = new {elementType}[{countExpr}];");
            else
                sb.AppendLine($"        {memberAccess} = new global::System.Collections.Generic.List<{elementType}>({countExpr});");
            return;
        }

        if (field.IsArray)
        {
            sb.AppendLine($"        if ({memberAccess} == null || {memberAccess}.Length < {countExpr})");
            sb.AppendLine($"            throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", {countExpr}, \"has insufficient array length\");");
        }
        else
        {
            string capacityProperty = field.ListElementIsNested && field.ListElementIsReferenceType ? "Count" : "Capacity";
            sb.AppendLine($"        if ({memberAccess} == null || {memberAccess}.{capacityProperty} < {countExpr})");
            string reason = capacityProperty == "Count" ? "has insufficient list count" : "has insufficient list capacity";
            sb.AppendLine($"            throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", {countExpr}, \"{reason}\");");
            if (!(field.ListElementIsNested && field.ListElementIsReferenceType))
                sb.AppendLine($"        {memberAccess}.Clear();");
        }
    }

    private static void EmitCollectionFinalization(StringBuilder sb, BitFieldModel field, string memberAccess, string countExpr, bool reuseExisting)
    {
        if (!reuseExisting)
            return;

        if (field.IsArray)
        {
            sb.AppendLine($"        if ({memberAccess}.Length > {countExpr}) global::System.Array.Clear({memberAccess}, {countExpr}, {memberAccess}.Length - ({countExpr}));");
        }
        else if (field.ListElementIsReferenceType)
        {
            sb.AppendLine($"        if ({memberAccess}.Count > {countExpr}) {memberAccess}.RemoveRange({countExpr}, {memberAccess}.Count - ({countExpr}));");
        }
    }

    private static void EmitNestedElementCreation(StringBuilder sb, BitFieldModel field, string memberAccess, string elementType, bool reuseExisting)
    {
        if (reuseExisting && field.ListElementIsReferenceType)
        {
            sb.AppendLine($"            var _elem = {memberAccess}[_i];");
            sb.AppendLine($"            if (_elem == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", _i + 1, $\"contains a null reusable element at index {{_i}}\");");
        }
        else
        {
            sb.AppendLine($"            var _elem = new {elementType}();");
        }
    }

    private static void EmitManualElementCreation(StringBuilder sb, BitFieldModel field, string memberAccess, string elementType, bool reuseExisting)
    {
        if (reuseExisting && field.ListElementIsReferenceType)
        {
            sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = (global::BitSerializer.IBitSerializable){memberAccess}[_i];");
            sb.AppendLine($"            if (_elem == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", _i + 1, $\"contains a null reusable element at index {{_i}}\");");
        }
        else if (field.ListElementIsTypeParameter)
        {
            sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = ({elementType})global::System.Activator.CreateInstance(typeof({elementType}))!;");
        }
        else
        {
            sb.AppendLine($"            global::BitSerializer.IBitSerializable _elem = new {elementType}();");
        }
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

    private static void EmitPolymorphicDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitOrder, string offsetExpr, string? bitIndexVar = null, HashSet<string>? emittedWireLocals = null, bool reuseExisting = false)
    {
        var deserializeMethod = $"Deserialize{bitOrder}";

        if (bitIndexVar != null)
        {
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
        }

        // Use the cached `_wire_<discriminator>` local so a derived-getter + empty-setter
        // discriminator (Issue: BinarySerialization-style "computed type id") doesn't make the switch
        // dispatch read back the *previous* runtime type's id instead of what was on the wire.
        string discriminatorExpr = ReadFieldExpr(emittedWireLocals, field.RelatedMemberName!);
        sb.AppendLine($"        switch ((int)({discriminatorExpr}))");
        sb.AppendLine("        {");
        foreach (var mapping in field.PolyMappings!)
        {
            sb.AppendLine($"            case {mapping.TypeId}:");
            sb.AppendLine("            {");
            if (reuseExisting)
            {
                sb.AppendLine($"                if ({memberAccess} is not {mapping.ConcreteTypeFullName} _poly)");
                sb.AppendLine("                {");
                sb.AppendLine($"                    if (reuseOnly) throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", 1);");
                sb.AppendLine($"                    _poly = new {mapping.ConcreteTypeFullName}();");
                sb.AppendLine("                }");
            }
            else
            {
                sb.AppendLine($"                var _poly = new {mapping.ConcreteTypeFullName}();");
            }
            if (bitIndexVar != null)
            {
                string call = reuseExisting
                    ? $"_poly.{deserializeMethod}Into(bytes, {offsetExpr}, context, reuseOnly)"
                    : $"_poly.{deserializeMethod}(bytes, {offsetExpr}, context)";
                sb.AppendLine($"                {bitIndexVar} += {call};");
            }
            else
            {
                string call = reuseExisting
                    ? $"_poly.{deserializeMethod}Into(bytes, {offsetExpr}, context, reuseOnly)"
                    : $"_poly.{deserializeMethod}(bytes, {offsetExpr}, context)";
                sb.AppendLine($"                {call};");
            }
            sb.AppendLine($"                {memberAccess} = _poly;");
            sb.AppendLine("                break;");
            sb.AppendLine("            }");
        }
        sb.AppendLine("            default:");
        sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"No polymorphic type mapping found for discriminator value '{{(int)({discriminatorExpr})}}'\");");
        sb.AppendLine("        }");

        // v0.12.0: when the polymorphic field has a SECONDARY [BitFieldRelated(ByteLength)]
        // binding, verify that the concrete poly type's deserialize consumed exactly the byte
        // budget the carrier declared. Mirrors the T4 nested-byte-length post-condition check —
        // wrong consumption means the wire/spec are out of sync and subsequent fields would land
        // at the wrong offset.
        //
        // Requires bitIndexVar to track post-deserialize bit offset (the caller passes one when
        // usesRuntimeBitLength is true, which is always true for polymorphic + secondary because
        // EmitMethod treats poly+secondary as dynamic — see EmitPolymorphicSerialize counterpart).
        if (field.SecondaryRelatedMemberName != null && field.SecondaryRelationKind == 1 && bitIndexVar != null)
        {
            var name = field.MemberName;
            // Codex review round-2 P1: reinterpret the carrier's bit pattern as the UNSIGNED twin
            // of its declared type before widening to long, otherwise an 8-bit signed carrier
            // holding wire value 0xC8 sign-extends to -56 and the (now-negative) verify trips on
            // any payload above the signed max (127 / 32767 / etc.). Same UnsignedTwinOf pipeline
            // [BitLengthFieldString] uses — keep them in sync. Falls back to plain `long` cast when
            // SecondaryRelatedFieldTypeName isn't cached (BITS053 secondary should make that
            // unreachable, but stay defensive so we never emit broken code).
            string carrierExpr = ReadFieldExpr(emittedWireLocals, field.SecondaryRelatedMemberName);
            string byteLenExpr = field.SecondaryRelatedFieldTypeName != null
                ? $"(long)({UnsignedTwinOf(field.SecondaryRelatedFieldTypeName)})({carrierExpr})"
                : $"(long)({carrierExpr})";
            sb.AppendLine($"        long _polyWire_{name} = {byteLenExpr};");

            // Codex review round-2 P2: apply the SECONDARY binding's deserialize converter (wire →
            // domain bytes) before computing the declared bit budget. Without this the polymorphic
            // byte-length verify uses the raw wire value, but the serializer's secondary backfill
            // routes through the same converter — so a length converter (offset / scale) makes the
            // two sides disagree by exactly the transform. Mirror that here.
            string bytesExpr = field.SecondaryValueConverterTypeFullName != null && field.SecondaryValueConverterHasDeserialize
                ? (field.SecondaryValueConverterDeserializeHasContext
                    ? $"global::System.Convert.ToInt64({field.SecondaryValueConverterTypeFullName}.OnDeserializeConvert((object)_polyWire_{name}, context))"
                    : $"global::System.Convert.ToInt64({field.SecondaryValueConverterTypeFullName}.OnDeserializeConvert((object)_polyWire_{name}))")
                : $"_polyWire_{name}";
            sb.AppendLine($"        long _polyDeclaredBits_{name} = ({bytesExpr}) * 8;");
            sb.AppendLine($"        if (({bitIndexVar} - ({offsetExpr})) != _polyDeclaredBits_{name})");
            sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Polymorphic '{name}' deserialized {{({bitIndexVar} - ({offsetExpr}))}} bits but the byte-length field '{field.SecondaryRelatedMemberName}' declared {{_polyDeclaredBits_{name}}} bits. The wire payload size does not match the declared byte budget.\");");
        }
    }

    private static void EmitFixedStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        int byteLen = field.FixedStringByteLength;
        var name = field.MemberName;

        sb.AppendLine("        {");
        sb.AppendLine($"            if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"Fixed string '{name}' requires a byte-aligned absolute bit offset on Deserialize, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");
        sb.AppendLine($"            if (({offsetExpr}) + {byteLen * 8} > bytes.Length * 8) throw new global::BitSerializer.BitSerializationNeedMoreDataException(\"Fixed string '{name}' is shorter than its {byteLen}-byte field.\");");
        sb.AppendLine($"            global::System.ReadOnlySpan<byte> _strBytes_{name} = bytes.Slice(({offsetExpr}) / 8, {byteLen});");
        sb.AppendLine($"            int _strEnd_{name} = {byteLen};");
        sb.AppendLine($"            while (_strEnd_{name} > 0 && _strBytes_{name}[_strEnd_{name} - 1] == {field.FixedStringPadding}) _strEnd_{name}--;");
        sb.AppendLine($"            {memberAccess} = global::BitSerializer.BitStringHelper.Decode(_strBytes_{name}.Slice(0, _strEnd_{name}), {GetBitStringEncodingExpression(field.StringEncodingName)});");
        sb.AppendLine("        }");
    }

    private static void EmitTerminatedStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        var name = field.MemberName;

        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Terminated string '{name}' requires a byte-aligned absolute bit offset on Deserialize, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");
        sb.AppendLine($"        int _strByteOffset_{name} = ({offsetExpr}) / 8;");
        sb.AppendLine($"        if (_strByteOffset_{name} > bytes.Length) throw new global::BitSerializer.BitSerializationNeedMoreDataException(\"Terminated string '{name}' starts beyond the available data.\");");
        sb.AppendLine($"        int _strTerminator_{name} = bytes.Slice(_strByteOffset_{name}).IndexOf((byte)0);");
        sb.AppendLine($"        if (_strTerminator_{name} < 0) throw new global::BitSerializer.BitSerializationNeedMoreDataException(\"Terminated string '{name}' has no terminator.\");");
        sb.AppendLine($"        {memberAccess} = global::BitSerializer.BitStringHelper.Decode(bytes.Slice(_strByteOffset_{name}, _strTerminator_{name}), {GetBitStringEncodingExpression(field.StringEncodingName)});");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + (_strTerminator_{name} + 1) * 8;");
    }

    /// <summary>
    /// Reads LengthBits-bit byte count, then that many encoded bytes, decodes via Encoding.
    /// Validates the prefixed byte count fits in the remaining buffer BEFORE allocating
    /// (review round-4 P1) — without this, a 32-bit prefix on malformed input would
    /// allocate up to ~2GB, and bit reads past the span return 0 silently.
    /// </summary>
    private static void EmitLengthPrefixStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        var name = field.MemberName;
        int lengthBits = field.LengthPrefixBits;
        string lenType = lengthBits == 8 ? "byte" : lengthBits == 16 ? "ushort" : "uint";

        // Mirror of the serializer guard (review round-11 P1): catch types nested at sub-byte
        // bitOffset where BITS038/BITS039 static checks couldn't see the parent context.
        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-prefix string '{name}' requires a byte-aligned absolute bit offset on Deserialize, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");

        // Read prefix into a 64-bit local so 32-bit values with high bit set don't go negative,
        // and so the buffer-size arithmetic below doesn't overflow.
        sb.AppendLine($"        if (({offsetExpr}) + {lengthBits} > bytes.Length * 8) throw new global::BitSerializer.BitSerializationNeedMoreDataException(\"Length-prefix string '{name}' is missing its {lengthBits}-bit prefix.\");");
        sb.AppendLine($"        long _strLenRaw_{name} = (long){helper}.ValueLength<{lenType}>(bytes, {offsetExpr}, {lengthBits});");
        // Compute remaining bytes after the length prefix. (offsetExpr + lengthBits) is bit-aligned
        // for byte-aligned LengthBits ∈ {8,16,32}, but use bit-level arithmetic for safety.
        sb.AppendLine($"        long _strRemBits_{name} = (long)bytes.Length * 8 - ({offsetExpr} + {lengthBits});");
        sb.AppendLine($"        if (_strLenRaw_{name} * 8 > _strRemBits_{name})");
        sb.AppendLine($"            throw new global::BitSerializer.BitSerializationNeedMoreDataException($\"Length-prefix string '{name}' declares {{_strLenRaw_{name}}} bytes, but only {{System.Math.Max(0, _strRemBits_{name} / 8)}} bytes remain in the buffer after the {lengthBits}-bit length prefix.\");");
        // Review round-9 P1: enforce MaxBytes on deserialize too. Serializer caps the encoded payload
        // at MaxBytes, so any wire value larger than MaxBytes violates the declared field contract —
        // accepting it would force allocations bigger than this model can ever produce. Reject before
        // allocating.
        if (field.LengthPrefixMaxBytes > 0)
        {
            sb.AppendLine($"        if (_strLenRaw_{name} > {field.LengthPrefixMaxBytes})");
            sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-prefix string '{name}' declares {{_strLenRaw_{name}}} bytes, exceeding the declared MaxBytes = {field.LengthPrefixMaxBytes} cap.\");");
        }
        sb.AppendLine($"        int _strLen_{name} = (int)_strLenRaw_{name};");
        sb.AppendLine($"        {memberAccess} = global::BitSerializer.BitStringHelper.Decode(bytes.Slice((({offsetExpr}) + {lengthBits}) / 8, _strLen_{name}), {GetBitStringEncodingExpression(field.StringEncodingName)});");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {lengthBits} + _strLen_{name} * 8;");
    }

    /// <summary>
    /// T4: deserialize a nested-type field whose byte budget comes from a related length field.
    /// Reads the length, optionally routes it through a deserialize converter (wire → bytes),
    /// validates against the remaining buffer, then calls the regular nested deserialize and
    /// verifies the actual bits consumed match the declared byte count exactly.
    /// </summary>
    private static void EmitNestedByteLengthRead(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr, string methodName, string nestedCallExpr, HashSet<string>? emittedWireLocals = null, bool reuseExisting = false)
    {
        EmitNestedByteLengthSetup(sb, field, offsetExpr, emittedWireLocals);
        // Codex review round-7 P2 symmetry: serializer's null-guarded path writes 0 bytes. Mirror
        // here — when the declared budget is 0, set the property to null and advance the cursor by
        // 0, skipping the nested deserialize call (which would consume 1+ bits for any non-empty
        // nested type and trip the consumed-vs-budget verify below).
        //
        // Codex review round-7 P1 (PR #5 follow-up): the `memberAccess = null;` only compiles for
        // reference carriers (CS0037/CS0403 against struct or unconstrained type parameter). For
        // value-type carriers, drop the null short-circuit — non-null structs always emit non-zero
        // size, so a 0 budget there indicates malformed wire data; let the unconditional nested
        // call run and trip the consumed-vs-budget verify, which is the correct diagnostic.
        sb.AppendLine($"        int _ncons_{field.MemberName} = 0;");
        if (field.NestedIsReferenceType)
        {
            sb.AppendLine($"        if (_nbudgetBytes_{field.MemberName} == 0) {{ {memberAccess} = null; }}");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            if (reuseExisting)
                sb.AppendLine($"            if ({memberAccess} == null) throw new global::BitSerializer.BitSerializationCapacityException(\"{field.MemberName}\", 1);");
            sb.AppendLine($"            _ncons_{field.MemberName} = {nestedCallExpr};");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine($"        _ncons_{field.MemberName} = {nestedCallExpr};");
        }
        EmitNestedByteLengthVerify(sb, field, bitIndexVar, offsetExpr);
    }

    /// <summary>Manual IBitSerializable variant of <see cref="EmitNestedByteLengthRead"/>.</summary>
    private static void EmitNestedByteLengthReadInterface(StringBuilder sb, BitFieldModel field, string bitIndexVar, string offsetExpr, string methodName, string interfaceLocal, string ctxArg, HashSet<string>? emittedWireLocals = null, bool reuseExisting = false)
    {
        EmitNestedByteLengthSetup(sb, field, offsetExpr, emittedWireLocals);
        // Round-7 P2 symmetry: same null + 0-budget short-circuit as the non-interface variant.
        // `interfaceLocal` was already assigned to a fresh instance by the caller; we leave that
        // local alone (the property assignment is done by the caller after this returns).
        //
        // No NestedIsReferenceType branch needed here: the interface path operates on the locally
        // boxed `interfaceLocal`, which the caller always assigns regardless of underlying type.
        // Value-type manual IBitSerializable carriers still get their pre-created default instance
        // when budget==0; the caller's `memberAccess = (T)interfaceLocal;` unboxes that safely.
        sb.AppendLine($"        int _ncons_{field.MemberName} = 0;");
        sb.AppendLine($"        if (_nbudgetBytes_{field.MemberName} > 0)");
        string call = reuseExisting
            ? $"{interfaceLocal}.{methodName}Into(bytes, {offsetExpr}, {ctxArg}, reuseOnly)"
            : $"{interfaceLocal}.{methodName}(bytes, {offsetExpr}, {ctxArg})";
        sb.AppendLine($"            _ncons_{field.MemberName} = {call};");
        EmitNestedByteLengthVerify(sb, field, bitIndexVar, offsetExpr);
    }

    private static void EmitNestedByteLengthSetup(StringBuilder sb, BitFieldModel field, string offsetExpr, HashSet<string>? emittedWireLocals = null)
    {
        var name = field.MemberName;

        // Mirror of the list ByteLength path: read the wire length, optionally convert wire → bytes,
        // bounds-check against the remaining buffer before invoking the nested deserializer.
        sb.AppendLine($"        long _nwireRaw_{name} = global::System.Convert.ToInt64({ReadFieldExpr(emittedWireLocals, field.RelatedMemberName!)});");
        string bytesExpr = field.ValueConverterTypeFullName != null && field.ValueConverterHasDeserialize
            ? (field.ValueConverterDeserializeHasContext
                ? $"global::System.Convert.ToInt64({field.ValueConverterTypeFullName}.OnDeserializeConvert((object)_nwireRaw_{name}, context))"
                : $"global::System.Convert.ToInt64({field.ValueConverterTypeFullName}.OnDeserializeConvert((object)_nwireRaw_{name}))")
            : $"_nwireRaw_{name}";
        sb.AppendLine($"        long _nbudgetBytes_{name} = {bytesExpr};");
        sb.AppendLine($"        long _nbudgetRemBits_{name} = (long)bytes.Length * 8 - ({offsetExpr});");
        sb.AppendLine($"        if (_nbudgetBytes_{name} < 0 || _nbudgetBytes_{name} * 8 > _nbudgetRemBits_{name})");
        sb.AppendLine($"            throw new global::BitSerializer.BitSerializationNeedMoreDataException($\"Nested '{name}' declares {{_nbudgetBytes_{name}}} bytes (from field '{field.RelatedMemberName}'), but only {{_nbudgetRemBits_{name} / 8}} bytes remain in the buffer.\");");
    }

    private static void EmitNestedByteLengthVerify(StringBuilder sb, BitFieldModel field, string bitIndexVar, string offsetExpr)
    {
        var name = field.MemberName;
        sb.AppendLine($"        if ((long)_ncons_{name} != _nbudgetBytes_{name} * 8)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Nested '{name}' consumed {{_ncons_{name}}} bits but the length field '{field.RelatedMemberName}' declared {{_nbudgetBytes_{name} * 8}} bits. The nested type's serialized layout must match the declared byte budget exactly.\");");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + _ncons_{name};");
    }

    /// <summary>
    /// Reads a [BitLengthFieldString] payload. The byte count was already deserialized into
    /// this.&lt;LengthFieldMemberName&gt; by an earlier field (BITS047 guarantees declaration order).
    /// Mirror of EmitLengthPrefixStringDeserialize minus the inline-prefix read and the prefix-bits
    /// arithmetic; uses _lfsRem to bounds-check the declared length against remaining buffer.
    /// </summary>
    private static void EmitLengthFieldStringDeserialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr, HashSet<string>? emittedWireLocals = null)
    {
        var encoding = GetEncodingExpression(field.StringEncodingName);
        var name = field.MemberName;

        // Mirror of EmitLengthPrefixStringDeserialize round-11 P1: refuse to read a byte stream
        // across a sub-byte absolute offset.
        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-field string '{name}' requires a byte-aligned absolute bit offset on Deserialize, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");

        // Read length from the peer field via cached `_wire_<name>` so a derived-getter + empty-setter
        // length carrier (Issue: BinarySerialization "派生 getter + 空 setter" 惯例) doesn't make the
        // budget evaluate to whatever the getter returns after an unrelated zero-init.
        //
        // Codex review P2: re-interpret the carrier's bit pattern as the UNSIGNED twin of its type
        // before widening to long. The serializer back-fills by bit-width, so a 200-byte string on
        // an `sbyte NameLength` carrier writes bit pattern 0xC8 — read back through `sbyte` it
        // shows as -56, and the `< 0` negativity guard below would reject our own valid output.
        // Casting `(byte)(sbyte)0xC8 → byte 200 → long 200` recovers the unsigned interpretation.
        // Unsigned carriers (byte / ushort / uint) round-trip identically through the same cast.
        string unsignedTwin = UnsignedTwinOf(field.LengthFieldTypeName);
        sb.AppendLine($"        long _lfsLenRaw_{name} = (long)({unsignedTwin})({ReadFieldExpr(emittedWireLocals, field.LengthFieldMemberName!)});");
        sb.AppendLine($"        long _lfsRemBits_{name} = (long)bytes.Length * 8 - ({offsetExpr});");
        sb.AppendLine($"        if (_lfsLenRaw_{name} * 8 > _lfsRemBits_{name})");
        sb.AppendLine($"            throw new global::BitSerializer.BitSerializationNeedMoreDataException($\"Length-field string '{name}' declares {{_lfsLenRaw_{name}}} bytes (from field '{field.LengthFieldMemberName}'), but only {{System.Math.Max(0, _lfsRemBits_{name} / 8)}} bytes remain in the buffer.\");");

        // Symmetric MaxBytes enforcement: the serializer caps the encoded payload at MaxBytes; any
        // wire value > MaxBytes is a violation, reject before allocating.
        if (field.LengthFieldMaxBytes > 0)
        {
            sb.AppendLine($"        if (_lfsLenRaw_{name} > {field.LengthFieldMaxBytes})");
            sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-field string '{name}' declares {{_lfsLenRaw_{name}}} bytes, exceeding the declared MaxBytes = {field.LengthFieldMaxBytes} cap.\");");
        }

        sb.AppendLine($"        int _lfsLen_{name} = (int)_lfsLenRaw_{name};");
        sb.AppendLine($"        {memberAccess} = global::BitSerializer.BitStringHelper.Decode(bytes.Slice(({offsetExpr}) / 8, _lfsLen_{name}), {GetBitStringEncodingExpression(field.StringEncodingName)});");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + _lfsLen_{name} * 8;");
    }

    private static string GetEncodingExpression(string encodingName)
    {
        return encodingName == "UTF8"
            ? "global::System.Text.Encoding.UTF8"
            : "global::System.Text.Encoding.ASCII";
    }

    private static string GetBitStringEncodingExpression(string encodingName)
        => encodingName == "UTF8"
            ? "global::BitSerializer.BitStringEncoding.UTF8"
            : "global::BitSerializer.BitStringEncoding.ASCII";

    private static void EmitDeserializeConverter(StringBuilder sb, BitFieldModel field, string memberAccess)
    {
        if (field.ValueConverterTypeFullName == null || !field.ValueConverterHasDeserialize) return;
        // RelationKind=ByteLength on a list turns the converter into a length converter, which
        // is applied on the related wire field (not the collection value). Skip the list-value
        // post-convert step in that mode.
        if (field.IsList && field.RelationKind == 1) return;
        var convertCall = field.ValueConverterIsStronglyTyped
            ? field.ValueConverterIsContextTyped
                ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({memberAccess}, ({field.ValueConverterContextTypeFullName})context!)"
                : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert({memberAccess})"
            : field.ValueConverterDeserializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnDeserializeConvert((object){memberAccess}, context)"
                : $"{field.ValueConverterTypeFullName}.OnDeserializeConvert((object){memberAccess})";
        sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){convertCall};");
    }

    /// <summary>
    /// Mirror of SerializerEmitter.BuildEndBitExpr — keep both in sync.
    /// </summary>
    private static string BuildEndBitExpr(TypeModel model, string? runtimeOffsetVar, int runtimeStaticEnd)
    {
        if (runtimeOffsetVar is null)
        {
            return $"(bitOffset + {model.TotalBitLength})";
        }
        int trailingBits = model.TotalBitLength - runtimeStaticEnd;
        return trailingBits > 0 ? $"({runtimeOffsetVar} + {trailingBits})" : runtimeOffsetVar;
    }

    /// <summary>
    /// Mirror of SerializerEmitter.ResolveFieldHelper — keep both in sync.
    /// 0=Inherit (outer), 1=Big (MSB), 2=Little (LSB).
    /// </summary>
    private static string ResolveFieldHelper(string outerHelper, int fieldEndian)
    {
        return fieldEndian switch
        {
            1 => "global::BitSerializer.BitHelperMSB",
            2 => "global::BitSerializer.BitHelperLSB",
            _ => outerHelper,
        };
    }

    private static bool UsesRuntimeBitLength(BitFieldModel field)
    {
        return field.IsTypeParameter
               || field.IsPotentiallyDynamic
               || field.IsTerminatedString
               || field.IsLengthPrefixString
               || field.IsLengthFieldString
               || (field.RelationKind == 1 && field.RelatedMemberName != null
                   && (field.IsNestedType || field.IsTypeParameter))
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

        if (field.IsLengthPrefixString)
        {
            return field.BitStartIndex;
        }

        if (field.IsLengthFieldString)
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
