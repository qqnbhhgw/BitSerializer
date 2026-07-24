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
    internal static void EmitAutoBackfill(StringBuilder sb, TypeModel model)
    {
        var fields = model.Fields;
        // v0.12.1: cross-inheritance carriers resolve through InheritedFields stubs so backfill
        // can emit the correct cast/overflow checks against the actual ancestor property's type
        // and bit width. The wire assignment `this.<carrier> = ...` resolves to the inherited
        // property via C# semantics at runtime, mirroring how user code would write to it.
        BitFieldModel? FindCarrier(string name) =>
            fields.Find(f => f.MemberName == name)
            ?? model.InheritedFields.Find(f => f.MemberName == name);
        foreach (var field in fields)
        {
            // [BitLengthFieldString]: backfill the byte count into a peer length field. Mirrors the
            // list ByteLength backfill but the "collection bit length" is the string's encoded byte
            // count (with UTF-8 boundary rollback when MaxBytes truncates inside a multi-byte char).
            if (field.IsLengthFieldString && field.LengthFieldMemberName != null)
            {
                var lenField = FindCarrier(field.LengthFieldMemberName);
                // v0.12.1: inherited carrier — base.Serialize already wrote the wire slot before
                // this method runs, so a property-level backfill here would set `this.<carrier>`
                // but the wire bytes are already out the door. Skip silently and rely on the user
                // (or the BinarySerialization-double-decorated peer) to set the carrier *before*
                // Serialize is called. Same rationale applies to the [BitFieldRelated] branch
                // below.
                if (lenField != null && !lenField.IsInherited)
                    EmitLengthFieldStringBackfill(sb, field, lenField);
                continue;
            }

            if (field.RelatedMemberName == null)
                continue;

            var relatedField = FindCarrier(field.RelatedMemberName);
            if (relatedField == null)
                continue;
            if (relatedField.IsInherited)
                continue;

            // T4: ByteLength on a nested [BitSerialize] / IBitSerializable / type-parameter carrier
            // — compute the nested type's serialized byte count and backfill the related length
            // field. Bytes are written by the regular nested-emit path; only the backfill differs.
            if ((field.IsNestedType || field.IsTypeParameter) && field.RelationKind == 1)
            {
                EmitNestedByteLengthBackfill(sb, field, relatedField);
                continue;
            }

            if (field.IsList && !field.FixedCount.HasValue)
            {
                if (field.RelationKind == 1)
                {
                    EmitByteLengthBackfill(sb, field, relatedField);
                }
                else
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

                // v0.12.0: polymorphic field can also carry a SECONDARY ByteLength binding (BITS057
                // gates: poly only). Backfill the byte-length carrier from the runtime poly object's
                // GetTotalBitLength(). Null payload writes 0 bytes (same convention as nested
                // ByteLength backfill in round-7 P2). The discriminator backfill above already ran
                // — both carriers will hold consistent values before the primitive write loop.
                if (field.SecondaryRelatedMemberName != null && field.SecondaryRelationKind == 1)
                {
                    var byteLenField = FindCarrier(field.SecondaryRelatedMemberName);
                    // v0.12.1: skip backfill when the secondary carrier lives on a base class —
                    // base.Serialize already wrote the wire slot, so a property-level write here
                    // would be too late. User must populate the carrier before Serialize.
                    if (byteLenField != null && !byteLenField.IsInherited)
                    {
                        var name = field.MemberName;
                        sb.AppendLine($"        int _polyBits_{name} = 0;");
                        sb.AppendLine($"        if (this.{name} != null)");
                        sb.AppendLine($"            _polyBits_{name} = ((global::BitSerializer.IBitSerializable)this.{name}).GetTotalBitLength();");
                        sb.AppendLine($"        if ((_polyBits_{name} & 7) != 0)");
                        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Polymorphic '{name}' serializes to {{_polyBits_{name}}} bits which is not byte-aligned; the secondary [BitFieldRelated(ByteLength)] binding requires the runtime poly type's size to be a whole number of bytes.\");");
                        sb.AppendLine($"        int _polyBytes_{name} = _polyBits_{name} / 8;");

                        // Codex review round-2 P2: route the domain byte count through the SECONDARY
                        // binding's ValueConverter (when declared) before writing the wire value. The
                        // converter is keyed off Secondary* model fields, not the primary set —
                        // primary carries the discriminator-side converter (typically absent), and a
                        // length converter (offset / scale / shift) on the ByteLength binding is the
                        // common protocol pattern this multi-binding path was designed for.
                        string wireRaw = field.SecondaryValueConverterTypeFullName != null && field.SecondaryValueConverterHasSerialize
                            ? (field.SecondaryValueConverterSerializeHasContext
                                ? $"{field.SecondaryValueConverterTypeFullName}.OnSerializeConvert((object)_polyBytes_{name}, context)"
                                : $"{field.SecondaryValueConverterTypeFullName}.OnSerializeConvert((object)_polyBytes_{name})")
                            : $"_polyBytes_{name}";
                        string wireValue = field.SecondaryValueConverterTypeFullName != null && field.SecondaryValueConverterHasSerialize
                            ? $"global::System.Convert.ToInt64({wireRaw})"
                            : $"(long){wireRaw}";
                        sb.AppendLine($"        long _polyWire_{name} = {wireValue};");
                        // Codex review round-6 P2: 32-bit carriers also need bounds checking. Without
                        // it, an int carrier could accept _polyWire_ values outside [int.MinValue,
                        // int.MaxValue] and the explicit cast below would wrap silently in unchecked
                        // context, writing a corrupted length while leaving deserialize to fail with
                        // a confusing byte-budget mismatch. Pick the right max from the carrier type
                        // name (int = signed 32-bit, uint = unsigned 32-bit). 64-bit carriers can
                        // physically hold any byte count produced by Convert.ToInt64 so skip there.
                        if (byteLenField.BitLength < 32)
                        {
                            long maxValue = (1L << byteLenField.BitLength) - 1;
                            sb.AppendLine($"        if (_polyWire_{name} < 0 || _polyWire_{name} > {maxValue})");
                            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Polymorphic '{name}' produced wire length {{_polyWire_{name}}} which cannot fit in the {byteLenField.BitLength}-bit byte-length field '{byteLenField.MemberName}'.\");");
                        }
                        else if (byteLenField.BitLength == 32)
                        {
                            string carrierMax = byteLenField.MemberTypeName == "int"
                                ? "(long)int.MaxValue"
                                : "(long)uint.MaxValue";
                            sb.AppendLine($"        if (_polyWire_{name} < 0 || _polyWire_{name} > {carrierMax})");
                            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Polymorphic '{name}' produced wire length {{_polyWire_{name}}} which cannot fit in the {byteLenField.MemberName} carrier ({byteLenField.MemberTypeName}).\");");
                        }
                        sb.AppendLine($"        this.{byteLenField.MemberName} = ({byteLenField.MemberTypeName})_polyWire_{name};");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Emits the byte-count backfill for a [BitLengthFieldString] field. The byte count goes into
    /// the referenced length field; the actual string bytes are written later by EmitLengthFieldStringSerialize.
    /// Mirrors EmitByteLengthBackfill structurally but the "collection" is a string encoded via Encoding.
    /// Caches the computed length on a local (_lfsLen_<field>) so the serializer body can reuse it
    /// without re-encoding.
    /// </summary>
    private static void EmitLengthFieldStringBackfill(StringBuilder sb, BitFieldModel field, BitFieldModel lenField)
    {
        var name = field.MemberName;
        long lengthMax = field.LengthFieldBitWidth == 32 ? uint.MaxValue : (1L << field.LengthFieldBitWidth) - 1;

        sb.AppendLine($"        int _lfsLen_{name} = global::BitSerializer.BitStringHelper.GetByteCount(this.{name}, {GetBitStringEncodingExpression(field.StringEncodingName)}, {field.LengthFieldMaxBytes});");

        // Overflow against the length field's bit width (skip 32-bit: a managed string can never
        // overflow uint range).
        if (field.LengthFieldBitWidth < 32)
        {
            sb.AppendLine($"        if (_lfsLen_{name} > {lengthMax})");
            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"String '{name}' encoded byte length {{_lfsLen_{name}}} exceeds the maximum ({lengthMax}) representable by the {field.LengthFieldBitWidth}-bit length field '{lenField.MemberName}'.\");");
        }

        // Backfill.
        sb.AppendLine($"        this.{lenField.MemberName} = ({lenField.MemberTypeName})_lfsLen_{name};");
    }

    /// <summary>
    /// T4: emits the byte-count backfill for a nested-type field tagged with RelationKind=ByteLength.
    /// Mirrors the list ByteLength path but the "byte count" comes from the nested type's
    /// GetTotalBitLength() (or compile-time BitLength for fixed-size nested types) instead of a
    /// per-element multiply. Null nested values are treated as zero bytes.
    /// </summary>
    private static void EmitNestedByteLengthBackfill(StringBuilder sb, BitFieldModel field, BitFieldModel relatedField)
    {
        var name = field.MemberName;

        // Compute total bits. Static-length nested types use the compile-time BitLength so we
        // don't pay for an interface call. Dynamic / type-parameter / manual carriers go through
        // IBitSerializable.GetTotalBitLength.
        bool isStatic = !field.IsPotentiallyDynamic
                        && !field.IsTypeParameter
                        && field.BitLength > 0;

        // Codex review round-7 P1 (PR #5 follow-up): the null-guard only compiles for reference
        // carriers. Struct / unconstrained-type-parameter carriers can never be null, so emit the
        // bit-length compute unconditionally for those — value types always have content, so the
        // "treat null as 0 bytes" affordance doesn't apply (and would produce CS0019 if guarded).
        sb.AppendLine($"        int _nbits_{name} = 0;");
        if (field.NestedIsReferenceType)
        {
            sb.AppendLine($"        if (this.{name} != null)");
            sb.AppendLine("        {");
            if (isStatic)
            {
                sb.AppendLine($"            _nbits_{name} = {field.BitLength};");
            }
            else
            {
                sb.AppendLine($"            _nbits_{name} = ((global::BitSerializer.IBitSerializable)this.{name}).GetTotalBitLength();");
            }
            sb.AppendLine("        }");
        }
        else
        {
            if (isStatic)
            {
                sb.AppendLine($"        _nbits_{name} = {field.BitLength};");
            }
            else
            {
                sb.AppendLine($"        _nbits_{name} = ((global::BitSerializer.IBitSerializable)this.{name}).GetTotalBitLength();");
            }
        }

        // Runtime byte-alignment guard. Mirror of the list path — BITS051 catches the static case
        // at compile time but dynamic nested types must be checked at runtime.
        sb.AppendLine($"        if ((_nbits_{name} & 7) != 0)");
        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Nested '{name}' serializes to {{_nbits_{name}}} bits which is not byte-aligned; RelationKind=ByteLength requires the nested type's runtime size to be a whole number of bytes.\");");
        sb.AppendLine($"        int _nbytes_{name} = _nbits_{name} / 8;");

        // Optional converter: domainBytes -> wireValue (e.g. wire stores byte_count + 4).
        string wireRaw = field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize
            ? (field.ValueConverterSerializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object)_nbytes_{name}, context)"
                : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object)_nbytes_{name})")
            : $"_nbytes_{name}";

        string wireValue = field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize
            ? $"global::System.Convert.ToInt64({wireRaw})"
            : $"(long){wireRaw}";
        sb.AppendLine($"        long _nwire_{name} = {wireValue};");
        if (relatedField.BitLength < 32)
        {
            long maxValue = (1L << relatedField.BitLength) - 1;
            sb.AppendLine($"        if (_nwire_{name} < 0 || _nwire_{name} > {maxValue})");
            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Nested '{name}' produced wire length {{_nwire_{name}}} which cannot fit in the {relatedField.BitLength}-bit field '{relatedField.MemberName}'.\");");
        }
        sb.AppendLine($"        this.{relatedField.MemberName} = ({relatedField.MemberTypeName})_nwire_{name};");
    }

    /// <summary>
    /// Emits the byte-length backfill for a list field tagged with RelationKind=ByteLength
    /// (Enhancement B). Computes the collection's total serialized byte count, optionally
    /// routes the result through a length converter (Enhancement A), then writes it to the
    /// related wire field. Null collections are treated as zero bytes.
    /// </summary>
    private static void EmitByteLengthBackfill(StringBuilder sb, BitFieldModel field, BitFieldModel relatedField)
    {
        var name = field.MemberName;
        var lengthProp = field.IsArray ? "Length" : "Count";

        // Compute total bit length of the collection. Static-stride elements multiply count by
        // element bit width; elements with runtime-variable size sum GetTotalBitLength() per element.
        // Manual IBitSerializable elements always use runtime sums in the serializer's dynamic-list
        // loop (even when an explicit bit length was declared on the field), so the prediction here
        // must do the same — otherwise the backfilled length drifts from the bytes actually written.
        bool elementIsDynamic = field.ListElementHasDynamicLength
                                || field.ListElementIsManualBitSerializable;

        sb.AppendLine($"        int _collBits_{name} = 0;");
        sb.AppendLine($"        if (this.{name} != null)");
        sb.AppendLine("        {");
        if (elementIsDynamic)
        {
            sb.AppendLine($"            for (int _i = 0; _i < this.{name}.{lengthProp}; _i++)");
            sb.AppendLine($"                _collBits_{name} += ((global::BitSerializer.IBitSerializable)this.{name}[_i]).GetTotalBitLength();");
        }
        else
        {
            sb.AppendLine($"            _collBits_{name} = this.{name}.{lengthProp} * {field.ListElementBitLength};");
        }
        sb.AppendLine("        }");

        // Byte alignment check.
        sb.AppendLine($"        if ((_collBits_{name} & 7) != 0)");
        sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Collection '{name}' serializes to {{_collBits_{name}}} bits which is not byte-aligned; RelationKind=ByteLength requires a byte-aligned total.\");");
        sb.AppendLine($"        int _collBytes_{name} = _collBits_{name} / 8;");

        // Optional converter: domainBytes -> wireValue.
        string wireRaw = field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize
            ? (field.ValueConverterSerializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object)_collBytes_{name}, context)"
                : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object)_collBytes_{name})")
            : $"_collBytes_{name}";

        // Overflow check against the related field's bit width (only when < 32 bits).
        string wireValue = field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize
            ? $"global::System.Convert.ToInt64({wireRaw})"
            : $"(long){wireRaw}";
        sb.AppendLine($"        long _wire_{name} = {wireValue};");
        if (relatedField.BitLength < 32)
        {
            long maxValue = (1L << relatedField.BitLength) - 1;
            sb.AppendLine($"        if (_wire_{name} < 0 || _wire_{name} > {maxValue})");
            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"Collection '{name}' produced wire length {{_wire_{name}}} which cannot fit in the {relatedField.BitLength}-bit field '{relatedField.MemberName}'.\");");
        }
        sb.AppendLine($"        this.{relatedField.MemberName} = ({relatedField.MemberTypeName})_wire_{name};");
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

        // Apply converters that can change an auto-backfilled length before computing that length.
        foreach (var f in model.Fields)
        {
            bool convertsListValue = f.IsList && f.RelationKind != 1;
            if ((convertsListValue || f.IsLengthFieldString)
                && f.ValueConverterTypeFullName != null && f.ValueConverterHasSerialize)
            {
                var ma = $"this.{f.MemberName}";
                var convertCall = BuildSerializeConverterCall(f, ma);
                sb.AppendLine($"        {ma} = ({f.MemberTypeFullName}){convertCall};");
            }
        }

        EmitAutoBackfill(sb, model);

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

            // If this field is itself a CRC result slot, capture its runtime bit offset so the CRC
            // computation block at the end of the method can back-fill the computed value to the actual
            // runtime position (review round-7 P1). Static derivation from runtimeOffsetVar /
            // BitStartIndex breaks down when dynamic content sits both before and after the CRC.
            if (field.IsCrcResult)
                sb.AppendLine($"        int _crcFieldBitOff_{field.MemberName} = {offsetExpr};");

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
            else if (field.IsLengthPrefixString)
            {
                EmitSerializeConverter(sb, field, memberAccess);
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitLengthPrefixStringSerialize(sb, field, helper, memberAccess, fieldEndVar, offsetExpr);
            }
            else if (field.IsLengthFieldString)
            {
                fieldEndVar = $"_bitIndex_{field.MemberName}";
                EmitLengthFieldStringSerialize(sb, field, helper, fieldEndVar, offsetExpr);
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
                // Codex review round-7 P2: when this is a RelationKind=ByteLength carrier,
                // EmitNestedByteLengthBackfill already writes a 0 byte budget for null payloads —
                // but the previous code then unconditionally called `{memberAccess}.Serialize*(...)`
                // here, producing a guaranteed NRE for optional/null nested payloads. Wrap the
                // entire nested-serialize block in a null-check ONLY for the ByteLength path so
                // null + 0-bytes-budget is a valid round-trippable wire shape; keep the original
                // strict NRE-on-null behavior for non-ByteLength carriers because there's no way
                // to recover the offset when the slot is fixed-size.
                //
                // Codex review round-7 P1 (PR #5 follow-up): the null-guard itself only compiles
                // when memberAccess is a reference type — `!= null` against a struct or against an
                // unconstrained / struct-constrained type parameter is CS0019. Skip the guard for
                // those cases; value-type carriers can never be null, so the body is always safe
                // to execute and the 0-byte-budget edge case won't be hit (a struct always has
                // content, GetTotalBitLength()>0 always emits non-zero size).
                bool isByteLengthCarrier = field.RelationKind == 1 && field.RelatedMemberName != null;
                bool emitNullGuard = isByteLengthCarrier && field.NestedIsReferenceType;

                // Declare fieldEndVar at outer scope when needed (downstream CRC tracking + offset
                // pipeline reads it), default it to offsetExpr so the null branch's "advance 0 bits"
                // is observable.
                if (usesRuntimeBitLength)
                {
                    fieldEndVar = $"_bitIndex_{field.MemberName}";
                    sb.AppendLine($"        int {fieldEndVar} = {offsetExpr};");
                }

                string indent = "        ";
                if (emitNullGuard)
                {
                    sb.AppendLine($"        if ({memberAccess} != null)");
                    sb.AppendLine("        {");
                    indent = "            ";
                }

                if (field.NestedHasOwnContext)
                {
                    sb.AppendLine($"{indent}int _nestedBitOff_{field.MemberName} = {offsetExpr};");
                    sb.AppendLine($"{indent}var _nestedCtx_{field.MemberName} = ({ibsExpr}).SerializeContext();");
                    sb.AppendLine($"{indent}({ibsExpr}).BeforeSerialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                    ctxArg = $"_nestedCtx_{field.MemberName}";
                }
                string callExpr = field.IsManualBitSerializable
                    ? $"({ibsExpr}).{methodName}(bytes, {offsetExpr}, {ctxArg})"
                    : $"{memberAccess}.{methodName}(bytes, {offsetExpr}, {ctxArg})";

                if (usesRuntimeBitLength)
                {
                    sb.AppendLine($"{indent}{fieldEndVar} = {offsetExpr} + {callExpr};");
                }
                else
                {
                    sb.AppendLine($"{indent}{callExpr};");
                }
                if (field.NestedHasOwnContext)
                {
                    sb.AppendLine($"{indent}({ibsExpr}).AfterSerialize(_nestedCtx_{field.MemberName}, bytes.Slice(_nestedBitOff_{field.MemberName} / 8));");
                }

                if (emitNullGuard)
                    sb.AppendLine("        }");
            }
            else if (field.IsNumericOrEnum)
            {
                // Primitive converter is handled inside EmitPrimitiveSerialize.
                // [BitField(Endian = ...)] can override the outer method's bit order for this field.
                var fieldHelper = ResolveFieldHelper(helper, field.Endian);
                EmitPrimitiveSerialize(sb, field, fieldHelper, memberAccess, offsetExpr);
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
            // Use the runtime offset captured when the CRC field was serialized — handles all layouts
            // (CRC at head, in middle, at tail; with leading or trailing dynamic fields).
            string crcOffsetExpr = $"_crcFieldBitOff_{crcField.MemberName}";
            sb.AppendLine("        {");
            if (crc.IsWholeBuffer)
            {
                // WholeBuffer mode: CRC covers (bitOffset/8 + SkipHead) .. (endBit/8 - SkipTail).
                // The CRC field's own slot is expected to live inside the SkipTail region so the CRC does
                // not read its uninitialized self back.
                //
                // Two byte-alignment guards (review P1): division-by-8 silently truncates partial bytes,
                // so we reject (a) non-byte-aligned bitOffset (e.g. caller nested this type after a 1-bit
                // field) and (b) non-byte-aligned end bit (e.g. dynamic content produced an odd bit count).
                // Without these guards, Slice() would include neighbor bytes from outside the type, or
                // drop the trailing partial byte, producing silently wrong CRC values.
                string endBitExpr = BuildEndBitExpr(model, runtimeOffsetVar, runtimeStaticEnd);
                sb.AppendLine($"            if ((bitOffset & 7) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer requires byte-aligned bitOffset; got {{bitOffset}} (bitOffset & 7 = {{bitOffset & 7}}).\");");
                sb.AppendLine($"            int _crcEndBit_{crc.TargetFieldName}_wb = {endBitExpr};");
                sb.AppendLine($"            if ((_crcEndBit_{crc.TargetFieldName}_wb & 7) != 0)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer requires the type's serialized total bit length to be byte-aligned; got {{_crcEndBit_{crc.TargetFieldName}_wb - bitOffset}} bits ({{(_crcEndBit_{crc.TargetFieldName}_wb - bitOffset) & 7}} bits past the last byte boundary).\");");
                sb.AppendLine($"            int _crcStart = (bitOffset / 8) + {crc.SkipHeadBytes};");
                sb.AppendLine($"            int _crcEnd   = (_crcEndBit_{crc.TargetFieldName}_wb / 8) - {crc.SkipTailBytes};");
                // Reject empty CRC range too (review P2). _crcEnd == _crcStart means SkipHead+SkipTail ==
                // totalBytes — Update() over an empty span returns the algorithm's initial value, which is
                // a silent misconfiguration. _crcEnd < _crcStart is the negative case (SkipTail too large).
                sb.AppendLine($"            if (_crcEnd <= _crcStart)");
                sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"CRC '{crcField.MemberName}' WholeBuffer range is empty or negative: SkipHeadBytes ({crc.SkipHeadBytes}) + SkipTailBytes ({crc.SkipTailBytes}) ≥ total written bytes ({{(_crcEndBit_{crc.TargetFieldName}_wb - bitOffset) / 8}}).\");");
            }
            else if (crc.HasDynamicInclude)
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
            sb.AppendLine($"            {castType} _crcVal = ({castType})_crcResult;");
            // CRC fields honor [BitField(Endian = ...)] just like any other primitive — the CRC block
            // runs after the field's initial write, so we must use the same helper as ResolveFieldHelper
            // would have chosen above, otherwise the rewrite silently undoes the per-field byte order.
            var crcHelper = ResolveFieldHelper(helper, crcField.Endian);
            sb.AppendLine($"            {crcHelper}.SetValueLength<{castType}>(bytes, {crcOffsetExpr}, {crc.CrcFieldBitLength}, _crcVal);");
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

    /// <summary>
    /// Builds an expression evaluating to the bit-offset one-past-the-end of this type's serialized
    /// payload (in absolute bits, i.e. relative to the buffer start, NOT relative to bitOffset).
    /// Mirrors the return-value calculation at the end of EmitMethod. WholeBuffer CRC mode uses this
    /// so it can byte-align-check before dividing by 8 (review P1).
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

    public static string EmitDelegationMethod(TypeModel model, string bitOrder)
    {
        var methodName = $"Serialize{bitOrder}";
        var newKeyword = model.HasBitSerializableBaseType ? "new " : "";
        return $"    public {newKeyword}int {methodName}(global::System.Span<byte> bytes, int bitOffset) => {methodName}(bytes, bitOffset, null);\n";
    }

    private static void EmitPrimitiveSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string offsetExpr)
    {
        var typeName = field.IsEnum ? field.EnumUnderlyingTypeName! : field.MemberTypeName;

        // [BitFieldValue(constant)]: write the constant directly and force-set the property so the
        // in-memory object reflects the wire bytes. BITS044 already rejects co-occurrence with
        // [BitCrc] / [BitFieldRelated] / [BitFieldCount] / converter, so we can skip those branches.
        if (field.HasConstantValue)
        {
            string literal = $"unchecked(({typeName}){field.ConstantValue}L)";
            EmitPrimitiveWrite(sb, field, helper, typeName, offsetExpr, literal);
            // Update the property so subsequent reads of the in-memory object see the pinned value.
            string assignedLiteral = field.IsEnum
                ? $"({field.MemberTypeFullName})unchecked(({typeName}){field.ConstantValue}L)"
                : $"unchecked(({field.MemberTypeFullName}){field.ConstantValue}L)";
            sb.AppendLine($"        {memberAccess} = {assignedLiteral};");
            return;
        }

        if (field.ValueConverterTypeFullName != null && field.ValueConverterHasSerialize)
        {
            var convertCall = field.ValueConverterIsStronglyTyped
                ? field.ValueConverterIsContextTyped
                    ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert({memberAccess}, ({field.ValueConverterContextTypeFullName})context!)"
                    : $"{field.ValueConverterTypeFullName}.OnSerializeConvert({memberAccess})"
                : field.ValueConverterSerializeHasContext
                    ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess}, context)"
                    : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){memberAccess})";
            var wireTypeName = field.ValueConverterWireTypeFullName ?? typeName;
            EmitPrimitiveWrite(sb, field, helper, wireTypeName, offsetExpr, $"unchecked(({wireTypeName}){convertCall})");
        }
        else if (field.IsEnum)
        {
            EmitPrimitiveWrite(sb, field, helper, typeName, offsetExpr, $"unchecked(({typeName}){memberAccess})");
        }
        else
        {
            EmitPrimitiveWrite(sb, field, helper, typeName, offsetExpr, memberAccess);
        }
    }

    private static void EmitPrimitiveWrite(StringBuilder sb, BitFieldModel field, string helper, string typeName, string offsetExpr, string valueExpr)
    {
        if ((field.BitStartIndex & 7) != 0 || (field.BitLength != 8 && field.BitLength != 16 && field.BitLength != 32 && field.BitLength != 64))
        {
            sb.AppendLine($"        {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, {valueExpr});");
            return;
        }

        string byteOffset = $"({offsetExpr}) / 8";
        sb.AppendLine($"        if ((({offsetExpr}) & 7) == 0)");
        sb.AppendLine("        {");
        if (field.BitLength == 8)
        {
            sb.AppendLine($"            bytes[{byteOffset}] = unchecked((byte)({valueExpr}));");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine($"            {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, {valueExpr});");
            return;
        }

        string endian = helper.Contains("LSB") ? "LittleEndian" : "BigEndian";
        string unsignedType = field.BitLength == 16 ? "ushort" : field.BitLength == 32 ? "uint" : "ulong";
        string methodType = field.BitLength == 16 ? "UInt16" : field.BitLength == 32 ? "UInt32" : "UInt64";
        sb.AppendLine($"            global::System.Buffers.Binary.BinaryPrimitives.Write{methodType}{endian}(bytes.Slice({byteOffset}), unchecked(({unsignedType})({valueExpr})));");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine($"            {helper}.SetValueLength<{typeName}>(bytes, {offsetExpr}, {field.BitLength}, {valueExpr});");
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
                    // Iterate over the actual collection length, not the (possibly converted) wire field.
                    var lenProp = field.IsArray ? "Length" : "Count";
                    countExpr = $"_listCount_{field.MemberName}";
                    sb.AppendLine($"        int {countExpr} = {memberAccess}?.{lenProp} ?? 0;");
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
            // Iterate over the actual collection length, not the (possibly converted) wire field.
            var _dynLenProp = field.IsArray ? "Length" : "Count";
            sb.AppendLine($"        int {bitIndexVar} = {offsetExpr};");
            sb.AppendLine($"        int _listCount_{field.MemberName} = {memberAccess}?.{_dynLenProp} ?? 0;");
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
        int byteLen = field.FixedStringByteLength;
        var name = field.MemberName;

        sb.AppendLine("        {");
        sb.AppendLine($"            if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"                throw new global::System.IO.InvalidDataException($\"Fixed string '{name}' requires a byte-aligned absolute bit offset, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");
        sb.AppendLine($"            int _strByteOffset_{name} = ({offsetExpr}) / 8;");
        sb.AppendLine($"            int _strLen_{name} = global::BitSerializer.BitStringHelper.Encode({memberAccess}, {GetBitStringEncodingExpression(field.StringEncodingName)}, bytes.Slice(_strByteOffset_{name}, {byteLen}), {byteLen});");
        sb.AppendLine($"            for (int _si = _strLen_{name}; _si < {byteLen}; _si++)");
        sb.AppendLine($"                {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _si * 8, 8, {field.FixedStringPadding});");
        sb.AppendLine("        }");
    }

    private static void EmitTerminatedStringSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var name = field.MemberName;

        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Terminated string '{name}' requires a byte-aligned absolute bit offset, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary.\");");
        sb.AppendLine($"        int _strByteOffset_{name} = ({offsetExpr}) / 8;");
        sb.AppendLine($"        int _strWriteLen_{name} = global::BitSerializer.BitStringHelper.GetByteCount({memberAccess}, {GetBitStringEncodingExpression(field.StringEncodingName)}, stopAtNull: true);");
        sb.AppendLine($"        global::BitSerializer.BitStringHelper.Encode({memberAccess}, {GetBitStringEncodingExpression(field.StringEncodingName)}, bytes.Slice(_strByteOffset_{name}, _strWriteLen_{name}), stopAtNull: true);");
        sb.AppendLine($"        {helper}.SetValueLength<byte>(bytes, {offsetExpr} + _strWriteLen_{name} * 8, 8, 0);");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + (_strWriteLen_{name} + 1) * 8;");
    }

    /// <summary>
    /// Emits length-prefix string serialization: write LengthBits-bit byte count, then encoded bytes.
    /// UTF-8 truncation to MaxBytes mirrors the BitFixedString path (split-safe multi-byte boundary).
    /// All locals are suffixed with the field name to avoid clashes — matches the BitTerminatedString
    /// convention so the bitIndex var stays visible to subsequent fields.
    /// </summary>
    private static void EmitLengthPrefixStringSerialize(StringBuilder sb, BitFieldModel field, string helper, string memberAccess, string bitIndexVar, string offsetExpr)
    {
        var name = field.MemberName;
        int lengthBits = field.LengthPrefixBits;
        int maxBytes = field.LengthPrefixMaxBytes;
        long lengthMax = lengthBits == 32 ? uint.MaxValue : (1L << lengthBits) - 1;

        // Review round-11 P1: BITS038/BITS039 only validate alignment within the declaring type's
        // static layout. If the type itself is serialized at a non-byte bitOffset (e.g. nested inside
        // a parent that wrote a 1-bit flag first), the LPS field's absolute offset still lands on a
        // sub-byte boundary. Runtime guard: refuse to emit the byte-stream across a non-byte boundary.
        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-prefix string '{name}' requires a byte-aligned absolute bit offset, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary (the containing type was nested at a non-byte bitOffset).\");");

        sb.AppendLine($"        int _strLen_{name} = global::BitSerializer.BitStringHelper.GetByteCount({memberAccess}, {GetBitStringEncodingExpression(field.StringEncodingName)}, {maxBytes});");

        // Bounds check against LengthBits capacity (skip for 32-bit: int.MaxValue < uint.MaxValue
        // so a managed string can never overflow a 32-bit length prefix — comparison would warn).
        if (lengthBits < 32)
        {
            sb.AppendLine($"        if (_strLen_{name} > {lengthMax})");
            sb.AppendLine($"            throw new global::System.InvalidOperationException($\"String '{name}' encoded byte length {{_strLen_{name}}} exceeds the maximum ({lengthMax}) representable by the {lengthBits}-bit length prefix.\");");
        }

        // Write length prefix and bytes.
        string lenType = lengthBits == 8 ? "byte" : lengthBits == 16 ? "ushort" : "uint";
        sb.AppendLine($"        {helper}.SetValueLength<{lenType}>(bytes, {offsetExpr}, {lengthBits}, ({lenType})_strLen_{name});");
        sb.AppendLine($"        global::BitSerializer.BitStringHelper.Encode({memberAccess}, {GetBitStringEncodingExpression(field.StringEncodingName)}, bytes.Slice((({offsetExpr}) + {lengthBits}) / 8, _strLen_{name}), {maxBytes});");
        sb.AppendLine($"        int {bitIndexVar} = {offsetExpr} + {lengthBits} + _strLen_{name} * 8;");
    }

    /// <summary>
    /// Emits the bytes-only side of a [BitLengthFieldString] field. The encoded byte count and
    /// the cached payload (_lfsBytes_<name>, _lfsLen_<name>) come from EmitLengthFieldStringBackfill,
    /// which ran in EmitAutoBackfill before any field serialization started — so we just spool the
    /// bytes here without re-encoding. Mirror of EmitLengthPrefixStringSerialize minus the prefix
    /// write (the prefix lives in a peer field that gets its normal primitive emit).
    /// </summary>
    private static void EmitLengthFieldStringSerialize(StringBuilder sb, BitFieldModel field, string helper, string bitIndexVar, string offsetExpr)
    {
        var name = field.MemberName;

        // Mirror of EmitLengthPrefixStringSerialize round-11 P1 runtime guard: the string body is a
        // byte stream, so a sub-byte absolute offset (caused by a parent type writing this one at a
        // non-byte bitOffset) would corrupt wire compatibility regardless of what BITS048 checked.
        sb.AppendLine($"        if ((({offsetExpr}) & 7) != 0)");
        sb.AppendLine($"            throw new global::System.IO.InvalidDataException($\"Length-field string '{name}' requires a byte-aligned absolute bit offset, but got {{({offsetExpr}) & 7}} extra bits past the byte boundary (the containing type was nested at a non-byte bitOffset).\");");

        sb.AppendLine($"        global::BitSerializer.BitStringHelper.Encode(this.{name}, {GetBitStringEncodingExpression(field.StringEncodingName)}, bytes.Slice(({offsetExpr}) / 8, _lfsLen_{name}), {field.LengthFieldMaxBytes});");
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

    private static void EmitSerializeConverter(StringBuilder sb, BitFieldModel field, string memberAccess)
    {
        if (field.ValueConverterTypeFullName == null || !field.ValueConverterHasSerialize) return;
        var convertCall = BuildSerializeConverterCall(field, memberAccess);
        sb.AppendLine($"        {memberAccess} = ({field.MemberTypeFullName}){convertCall};");
    }

    private static string BuildSerializeConverterCall(BitFieldModel field, string valueExpression)
        => field.ValueConverterIsStronglyTyped
            ? field.ValueConverterIsContextTyped
                ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert({valueExpression}, ({field.ValueConverterContextTypeFullName})context!)"
                : $"{field.ValueConverterTypeFullName}.OnSerializeConvert({valueExpression})"
            : field.ValueConverterSerializeHasContext
                ? $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){valueExpression}, context)"
                : $"{field.ValueConverterTypeFullName}.OnSerializeConvert((object){valueExpression})";

    /// <summary>
    /// Maps a field's [BitField(Endian = ...)] override (0=Inherit, 1=Big, 2=Little) to the helper class.
    /// Inherit keeps the outer method's helper; Big forces MSB; Little forces LSB. BITS028 already
    /// guarantees this is only invoked for byte-aligned, byte-multiple-width scalar fields where the
    /// MSB↔LSB swap actually means a byte-order flip.
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
