# Migration Matrix

## API usage mapping

| BinarySerializer | BitSerializer |
| --- | --- |
| `new BinarySerializer()` | No runtime serializer instance. Use static entrypoints. |
| `Serialize(Stream, object)` / `SerializeAsync(...)` | `BitSerializerMSB.Serialize(obj)` or `BitSerializerLSB.Serialize(obj)` then write bytes to stream yourself. |
| `Deserialize<T>(Stream)` / `DeserializeAsync<T>(...)` | Read bytes from stream first, then `BitSerializerMSB.Deserialize<T>(bytes)` or `BitSerializerLSB.Deserialize<T>(bytes)`. |
| `Deserialize(Stream, Type)` | `BitSerializerMSB.Deserialize(bytes, type)` / `BitSerializerLSB.Deserialize(bytes, type)` |
| `SizeOf(obj)` | `Serialize(obj).Length` (or custom bit-length logic from `GetTotalBitLength()`). |
| `serializer.Endianness = Endianness.Big/Little` | Choose `BitSerializerMSB` or `BitSerializerLSB` per protocol message. |

## Attribute mapping

| BinarySerializer | BitSerializer | Notes |
| --- | --- | --- |
| `[Ignore]` / `[IgnoreMember]` | `[BitIgnore]` | IgnoreMember by name has no direct equivalent. |
| `[FieldOrder]` | No direct attribute | Keep member declaration order equal to wire order. |
| `[FieldBitLength(n)]` | `[BitField(n)]` | Direct mapping for explicit bit width. |
| `[FieldCount(nameof(X))]` | `[BitField] + [BitFieldRelated(nameof(X))]` | Count field remains a normal `[BitField]` member. |
| `[FieldCount(const)]` | `[BitField] + [BitFieldCount(const)]` | Direct fixed-count mapping. |
| `[Subtype(...)]` + `[SubtypeDefault]` | `[BitFieldRelated] + [BitPoly(...)]` | Keep explicit discriminator field; default subtype is manual fallback logic. |
| `IBinarySerializable` | `IBitSerializable` | Rewrite methods to LSB/MSB + bit offset model. |
| `IValueConverter` in bindings | `IBitFieldValueConverter` | Use static conversion methods (optionally context-aware). |

## Partially mappable features

| BinarySerializer feature | BitSerializer approach |
| --- | --- |
| `[FieldLength(nameof(X))]` for strings | Use `[BitFixedString(n)]` for fixed byte length, or `[BitTerminatedString]` for terminated string model. |
| `[FieldLength(nameof(X))]` for collections/objects | Prefer count-driven model (`[BitFieldRelated]`) or implement custom `IBitSerializable` for exact byte-length-bound payloads. |
| `[FieldEncoding]` | Use `Encoding` parameter on `[BitFixedString]` / `[BitTerminatedString]`. |
| Checksum/CRC value attributes | Use lifecycle hooks (`BeforeSerialize`/`AfterSerialize`/`AfterDeserialize`) and explicit checksum fields. |

## Unsupported (no direct equivalent)

These require manual protocol logic, usually custom `IBitSerializable` blocks:
- `[FieldBitOrder]` mixed per-field behavior.
- `[FieldAlignment]`.
- `[FieldOffset]`.
- `[SerializeWhen]` / `[SerializeWhenNot]`.
- `[SerializeUntil]` / `[ItemSerializeUntil]`.
- `[ItemLength]` (variable per-item byte budget).
- `[SubtypeFactory]` / `[ItemSubtypeFactory]`.
- Deferred stream section behavior (`Streamlet`-style handling).

## High-risk migration checklist

- Ensure every wire member has explicit BitSerializer intent (`[BitField]`, string attribute, or `[BitIgnore]`).
- Verify declaration order after removing `FieldOrder`.
- Validate MSB/LSB byte outputs against golden protocol samples.
- Re-test dynamic list and polymorphic payload boundaries.
- Add regression tests before replacing old serializers in production paths.
