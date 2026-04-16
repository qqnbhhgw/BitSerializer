---
name: binaryserializer-to-bitserializer-migration
description: Migrate C# code from jefffhaynes/BinarySerializer to Jlzeng.BitSerializer. Use when a repository contains BinarySerializer usage (BinarySerializer class, IBinarySerializable, FieldOrder/FieldLength/FieldBitLength/FieldCount/Subtype/SerializeWhen/FieldEndianness attributes) and needs equivalent BitSerializer models ([BitSerialize], [BitField], BitSerializerMSB/BitSerializerLSB) plus explicit fallback patterns for unsupported features.
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

For polymorphism:
- Keep discriminator field as a normal `[BitField(...)]`.
- Mark payload with `[BitField] + [BitFieldRelated(nameof(Discriminator))] + [BitPoly(...)]`.
- Provide explicit max bit length on payload field when needed.

## 4. Apply attribute-by-attribute migration

Use [references/migration-matrix.md](references/migration-matrix.md) as the source of truth.

Do not force one-to-one rewrites when semantics differ. Prefer:
- Count-prefixed collections instead of sentinel termination.
- Global MSB/LSB mode choice instead of per-field bit order/endianness.
- `IBitFieldValueConverter` and lifecycle hooks for value transforms and checks.
- Manual `IBitSerializable` for offsets, alignment, deferred stream sections, or conditional wire layout.

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
