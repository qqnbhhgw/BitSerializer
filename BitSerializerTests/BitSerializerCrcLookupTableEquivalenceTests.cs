using BitSerializer;
using BitSerializer.CrcAlgorithms;
using Shouldly;

namespace BitSerializerTests;

/// <summary>
/// v0.12.2 perf: <see cref="CrcCcitt"/> 和 <see cref="Crc16Arc"/> 从 bit-by-bit 改 256 项查表,
/// 验证 wire byte equality.
///
/// 三层覆盖:
/// 1. 公认 well-known test vectors — "123456789" 标准串, 锁住算法语义不漂.
/// 2. 1024 长度 fuzz — 内部保留参考 bit-by-bit 实现, 两路结果逐字节对等.
/// 3. 边界长度 (0 / 1 / 15 / 16 / 17 / 255 / 256 / 1023) — 跨 byte 边界 + table 索引边界.
///
/// 原 Issue: D:\CARS\stp\Stp.Peripheral.Dmi.Protocol\BitSerializer-Perf-CrcCcitt-LookupTable.md
/// STP DMI Serialize 路径 v0.10 → v0.12.1 回归 1.8-2.55×, 表化后回归基线.
/// </summary>
public class BitSerializerCrcLookupTableEquivalenceTests
{
    // ============ well-known vectors ============

    /// <summary>
    /// CRC-16/CCITT-FALSE: "123456789" (ASCII) with initial 0xFFFF → 0x29B1 (IETF/Wikipedia).
    /// 项目 InitialValue 默认 0, 这里显式 Reset(0xFFFF) 验算法本身.
    /// </summary>
    [Fact]
    public void CrcCcitt_StandardVector_123456789()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        var crc = new CrcCcitt();
        crc.Reset(0xFFFF);
        crc.Update(data);
        crc.Result.ShouldBe(0x29B1UL);
    }

    /// <summary>
    /// CRC-16/CCITT-FALSE: "123456789" with initial 0 (XMODEM-like) → 0x31C3.
    /// 这是 ATP 协议常用的初始值.
    /// </summary>
    [Fact]
    public void CrcCcitt_XmodemStyle_Initial0()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        var crc = new CrcCcitt();
        crc.Reset(0);
        crc.Update(data);
        crc.Result.ShouldBe(0x31C3UL);
    }

    /// <summary>
    /// CRC-16/ARC: "123456789" → 0xBB3D (IETF/Wikipedia).
    /// </summary>
    [Fact]
    public void Crc16Arc_StandardVector_123456789()
    {
        var data = System.Text.Encoding.ASCII.GetBytes("123456789");
        var crc = new Crc16Arc();
        crc.Reset(0);
        crc.Update(data);
        crc.Result.ShouldBe(0xBB3DUL);
    }

    // ============ fuzz equivalence vs bit-by-bit reference ============

    [Fact]
    public void CrcCcitt_TableImpl_Equals_BitByBit_OverFuzzInputs()
    {
        var rng = new System.Random(42);
        for (int len = 0; len <= 1024; len++)
        {
            var data = new byte[len];
            rng.NextBytes(data);

            var reference = new CrcCcittBitByBitReference();
            reference.Reset(0);
            reference.Update(data);

            var optimized = new CrcCcitt();
            optimized.Reset(0);
            optimized.Update(data);

            optimized.Result.ShouldBe(
                reference.Result,
                $"CrcCcitt mismatch at len={len}: optimized=0x{optimized.Result:X4}, reference=0x{reference.Result:X4}");
        }
    }

    [Fact]
    public void Crc16Arc_TableImpl_Equals_BitByBit_OverFuzzInputs()
    {
        var rng = new System.Random(42);
        for (int len = 0; len <= 1024; len++)
        {
            var data = new byte[len];
            rng.NextBytes(data);

            var reference = new Crc16ArcBitByBitReference();
            reference.Reset(0);
            reference.Update(data);

            var optimized = new Crc16Arc();
            optimized.Reset(0);
            optimized.Update(data);

            optimized.Result.ShouldBe(
                reference.Result,
                $"Crc16Arc mismatch at len={len}: optimized=0x{optimized.Result:X4}, reference=0x{reference.Result:X4}");
        }
    }

    // ============ boundary lengths + multiple Update calls (streaming) ============

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(1023)]
    public void CrcCcitt_BoundaryLength_SingleVsSplitUpdate_Equal(int len)
    {
        var rng = new System.Random(len);
        var data = new byte[len];
        rng.NextBytes(data);

        var single = new CrcCcitt();
        single.Reset(0);
        single.Update(data);

        // Split halfway to verify Update is genuinely streaming and not memcpy-once
        var split = new CrcCcitt();
        split.Reset(0);
        split.Update(data.AsSpan(0, len / 2));
        split.Update(data.AsSpan(len / 2));

        split.Result.ShouldBe(single.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(1023)]
    public void Crc16Arc_BoundaryLength_SingleVsSplitUpdate_Equal(int len)
    {
        var rng = new System.Random(len);
        var data = new byte[len];
        rng.NextBytes(data);

        var single = new Crc16Arc();
        single.Reset(0);
        single.Update(data);

        var split = new Crc16Arc();
        split.Reset(0);
        split.Update(data.AsSpan(0, len / 2));
        split.Update(data.AsSpan(len / 2));

        split.Result.ShouldBe(single.Result);
    }

    // ============ test-only bit-by-bit reference impls ============
    // 保留 v0.12.1 之前的实现做对照, 防止后续算法重构悄悄改写 wire 行为.

    private sealed class CrcCcittBitByBitReference : IBitCrcAlgorithm
    {
        private ushort _crc;
        public int BitWidth => 16;
        public void Reset(ulong initialValue) => _crc = (ushort)initialValue;
        public void Update(ReadOnlySpan<byte> data)
        {
            ushort crc = _crc;
            foreach (byte b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
                }
            }
            _crc = crc;
        }
        public ulong Result => _crc;
    }

    private sealed class Crc16ArcBitByBitReference : IBitCrcAlgorithm
    {
        private ushort _crc;
        public int BitWidth => 16;
        public void Reset(ulong initialValue) => _crc = (ushort)initialValue;
        public void Update(ReadOnlySpan<byte> data)
        {
            ushort crc = _crc;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    crc = (crc & 0x0001) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
                }
            }
            _crc = crc;
        }
        public ulong Result => _crc;
    }
}
