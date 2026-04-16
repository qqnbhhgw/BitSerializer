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
| `[FieldCount(const)]` + short-data zero-pad | `[BitField] + [BitFieldCount(const, PadIfShort = true)]` | Serialize always writes N; deserialize pads tail with default when stream is short. Primitive-element arrays/lists only. |
| Trailing "read-to-end" array | `[BitField] + [BitFieldConsumeRemaining]` | Reads `(bytes.Length*8 - offset) / elemBits` elements. Must be last field; element type must be numeric/enum. |
| `[FieldCrc16(nameof(X), ...)]` | `[BitCrc(typeof(CrcCcitt))]` on result field + `[BitCrcInclude(nameof(Result))]` on participants | Source-gen computes and backfills CRC. Built-in algorithms in `BitSerializer.CrcAlgorithms`: `CrcCcitt`, `Crc16Arc`, `Crc32`. |
| `[FieldCrc32(nameof(X), ...)]` | Same as above with `typeof(Crc32)` | Include range must be contiguous + byte-aligned. |
| Custom checksum algorithm | Implement `IBitCrcAlgorithm` + `[BitCrc(typeof(MyAlgo))]` | Requires public parameterless constructor. |
| `[Subtype(...)]` + `[SubtypeDefault]` | `[BitFieldRelated] + [BitPoly(...)]` | Keep explicit discriminator field; default subtype is manual fallback logic. |
| `IBinarySerializable` | `IBitSerializable` | Rewrite methods to LSB/MSB + bit offset model. |
| `IValueConverter` in bindings | `IBitFieldValueConverter` | Use static conversion methods (optionally context-aware). |

## Partially mappable features

| BinarySerializer feature | BitSerializer approach |
| --- | --- |
| `[FieldLength(nameof(X))]` for strings | Use `[BitFixedString(n)]` for fixed byte length, or `[BitTerminatedString]` for terminated string model. |
| `[FieldLength(nameof(X))]` for collections/objects | Prefer count-driven model (`[BitFieldRelated]`). For byte-length-bound payloads that may arrive short, use `[BitFieldCount(n, PadIfShort = true)]`. For exact-byte payloads with nested types, implement custom `IBitSerializable`. |
| `[FieldEncoding]` | Use `Encoding` parameter on `[BitFixedString]` / `[BitTerminatedString]`. |
| Non-CRC checksums / hash-like fields | Use lifecycle hooks (`BeforeSerialize`/`AfterSerialize`/`AfterDeserialize`) and explicit checksum fields. Declarative `[BitCrc]` covers standard polynomial CRCs only. |

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

## Worked example: frame with CRC + fixed padded buffer

BinarySerializer:
```csharp
public class Frame
{
    [FieldOrder(0)] public byte Start { get; set; } = 0x7E;
    [FieldOrder(1), FieldCrc16(nameof(Crc))] public byte DestAddr { get; set; }
    [FieldOrder(2), FieldCrc16(nameof(Crc))] public byte SrcAddr { get; set; }
    [FieldOrder(3), FieldCrc16(nameof(Crc)), FieldCount(276)] public byte[] Payload { get; set; }
    [FieldOrder(4)] public ushort Crc { get; set; }
    [FieldOrder(5)] public byte End { get; set; } = 0xCF;
}
```

BitSerializer:
```csharp
using BitSerializer;
using BitSerializer.CrcAlgorithms;

[BitSerialize]
public partial class Frame
{
    [BitField(8)] public byte Start { get; set; } = 0x7E;

    [BitField(8), BitCrcInclude(nameof(Crc))] public byte DestAddr { get; set; }
    [BitField(8), BitCrcInclude(nameof(Crc))] public byte SrcAddr { get; set; }

    [BitField, BitFieldCount(276, PadIfShort = true), BitCrcInclude(nameof(Crc))]
    public byte[] Payload { get; set; } = System.Array.Empty<byte>();

    [BitField(16), BitCrc(typeof(CrcCcitt))] public ushort Crc { get; set; }
    [BitField(8)] public byte End { get; set; } = 0xCF;
}
```

Key shifts:
- `FieldCrc16` split into result attribute on the CRC field + participation attribute on each covered field.
- `FieldCount(276)` + manual zero-padding in outer `IBitSerializable` collapses to `BitFieldCount(276, PadIfShort = true)`.
- No `AfterSerialize` hook, no hand-coded byte offsets, no `IBitSerializable` implementation.

## High-risk migration checklist

- Ensure every wire member has explicit BitSerializer intent (`[BitField]`, string attribute, or `[BitIgnore]`).
- Verify declaration order after removing `FieldOrder`.
- Validate MSB/LSB byte outputs against golden protocol samples.
- Re-test dynamic list and polymorphic payload boundaries.
- Add regression tests before replacing old serializers in production paths.
