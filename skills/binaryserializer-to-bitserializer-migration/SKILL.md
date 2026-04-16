---
name: binaryserializer-to-bitserializer-migration
description: Migrate C# code from jefffhaynes/BinarySerializer to Jlzeng.BitSerializer. Use when a repository contains BinarySerializer usage (BinarySerializer class, IBinarySerializable, FieldOrder/FieldLength/FieldBitLength/FieldCount/FieldCrc16/FieldCrc32/Subtype/SerializeWhen/FieldEndianness attributes) and needs equivalent BitSerializer models ([BitSerialize], [BitField], [BitCrc]/[BitCrcInclude], [BitFieldCount(PadIfShort=true)], [BitFieldConsumeRemaining], BitSerializerMSB/BitSerializerLSB) plus explicit fallback patterns for unsupported features.
---

# BinarySerializer -> BitSerializer Migration

Perform migration in this order.

## 1. Inventory old usage first

Run `scripts/find-binaryserializer-usage.ps1` against the target repository.

Classify findings into:
- Directly mappable attributes and APIs.
- Partially mappable features (needs model reshape).
- Unsupported features (must use manual `IBitSerializable` blocks or protocol redesign).

If unsupported features appear, read [references/migration-matrix.md](references/migration-matrix.md) before editing code.

## 2. Replace package and runtime entry API

Replace package reference:
- Remove `BinarySerializer`.
- Add `Jlzeng.BitSerializer`.

Replace runtime usage pattern:
- Replace `new BinarySerializer()` pipeline with static serializer entrypoints.
- Use `BitSerializerMSB` for MSB-first wire format.
- Use `BitSerializerLSB` for LSB-first wire format.

Use this mapping:
- `serializer.Serialize(stream, obj)` -> `var bytes = BitSerializerMSB.Serialize(obj); stream.Write(bytes);`
- `serializer.Deserialize<T>(stream)` -> read bytes first, then `BitSerializerMSB.Deserialize<T>(bytes)`
- `serializer.SizeOf(obj)` -> `BitSerializerMSB.Serialize(obj).Length` (or pre-allocated span flow)

## 3. Rebuild model declarations for source generation

For each serialized model:
- Add `[BitSerialize]`.
- Declare type as `partial`.
- Convert serializable members to `[BitField(...)]` or string attributes (`[BitFixedString]` / `[BitTerminatedString]`).
- Add `[BitIgnore]` for members not on the wire.
- Keep members in exact wire order because `FieldOrder` is not used in this library.

For collections:
- Prefer `[BitFieldRelated(nameof(CountField))]` for dynamic count.
- Use `[BitFieldCount(n)]` for fixed count.
- Use `[BitFieldCount(n, PadIfShort = true)]` when the wire slot is fixed-size but the payload may be shorter (e.g. BTM 276-byte buffers with variable real data). Serialize writes N elements (padding with default); deserialize pads missing tail when the stream is short.
- Use `[BitFieldConsumeRemaining]` for a trailing primitive-element array/list that should read until end-of-buffer. Must be the last field; element type must be numeric or enum.

For polymorphism:
- Keep discriminator field as a normal `[BitField(...)]`.
- Mark payload with `[BitField] + [BitFieldRelated(nameof(Discriminator))] + [BitPoly(...)]`.
- Provide explicit max bit length on payload field when needed.

For CRC / checksum fields (replaces `[FieldCrc16]` / `[FieldCrc32]`):
- Mark the result field with `[BitCrc(typeof(CrcCcitt))]` (or `Crc16Arc` / `Crc32`, all in `BitSerializer.CrcAlgorithms`).
- Mark each participating field with `[BitCrcInclude(nameof(TargetCrcField))]`.
- Source generator computes and writes the CRC after all fields are serialized, and backfills the property.
- Optional `InitialValue = ...` and `ValidateOnDeserialize = true` for both directions.
- Include range must be contiguous and byte-aligned at both ends; the CRC field itself must be byte-aligned. Violations surface as `BITS015`–`BITS018`.
- For algorithms not in the box, implement `IBitCrcAlgorithm` (`BitWidth`, `Reset(initialValue)`, `Update(ReadOnlySpan<byte>)`, `Result`). Algorithm type needs a public parameterless constructor.
- Nested CRCs (outer frame wrapping inner CRC'd content) work out of the box: inner computes first during its own serialize call, outer reads the finalized inner bytes.

## 4. Apply attribute-by-attribute migration

Use [references/migration-matrix.md](references/migration-matrix.md) as the source of truth.

Do not force one-to-one rewrites when semantics differ. Prefer:
- Count-prefixed collections instead of sentinel termination.
- Global MSB/LSB mode choice instead of per-field bit order/endianness.
- `[BitCrc]` + `[BitCrcInclude]` for CRC fields (no manual offsets, no `AfterSerialize` hook).
- `[BitFieldCount(n, PadIfShort = true)]` / `[BitFieldConsumeRemaining]` for zero-padded or trailing variable-length buffers (no manual zero-fill in outer frame).
- `IBitFieldValueConverter` and lifecycle hooks for value transforms and non-CRC side effects.
- Manual `IBitSerializable` only for offsets, alignment, deferred stream sections, or conditional wire layout that can't be expressed declaratively.

## 5. Recreate custom serializers and converters

Migrate `IBinarySerializable` implementations to `IBitSerializable`:
- Implement `SerializeLSB`, `SerializeMSB`, `DeserializeLSB`, `DeserializeMSB`, `GetTotalBitLength`.
- Add context-aware overload usage when needed (`SerializeContext`, `DeserializeContext`).

Migrate `IValueConverter` usages to `IBitFieldValueConverter` static methods:
- `OnSerializeConvert(...)`
- `OnDeserializeConvert(...)`
- Optional context overloads with `object? context`

## 6. Validate behavior with protocol fixtures

Validate in this order:
1. Build and resolve generator diagnostics.
2. Round-trip tests on representative packets for both directions.
3. Golden byte sequence comparisons against existing protocol fixtures.
4. Boundary tests for dynamic lengths, polymorphism, and non-byte-aligned fields.

When migration changes semantics for unsupported BinarySerializer attributes, preserve old behavior with dedicated regression tests that assert exact bytes.

## 7. Audit residual manual fallbacks

Run `scripts/audit-bitserializer-migration.ps1` against the migrated repository. It scans for patterns that can likely be rewritten using the declarative API but are still manually coded:

- Manual CRC inside `AfterSerialize` / `BeforeSerialize` → `[BitCrc]` + `[BitCrcInclude]`
- `UartCrc16` / `CrcCcitt` calls with hardcoded byte offsets → declarative CRC
- `BinaryPrimitives.Write*Endian` inside lifecycle hooks (classic CRC-writeback) → declarative CRC
- Manual zero-padding of short byte arrays → `[BitFieldCount(N, PadIfShort = true)]`
- Read-to-end byte loops → `[BitFieldConsumeRemaining]`
- Residual `IBitSerializable` implementations that may now be expressible declaratively
- Leftover `using BinarySerialization;` / `[FieldCrc16]` / `[FieldCrc32]`

Exit code is non-zero if any match is found, so the script is CI-friendly.
