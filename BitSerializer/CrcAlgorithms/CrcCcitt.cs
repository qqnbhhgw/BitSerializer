namespace BitSerializer.CrcAlgorithms;

/// <summary>
/// CRC-16/CCITT-FALSE：多项式 0x1021，无位反射，无输入/输出异或。
/// 常见于 XMODEM / 车载 UART 协议（ATP 使用该算法，初始值 0）。
///
/// v0.12.2 性能修复 (STP Perf Issue: BitSerializer-Perf-CrcCcitt-LookupTable.md):
/// 原 bit-by-bit 实现每字节做 8 次 if-shift, CPU 分支预测命中率 ~50%, 1KB 输入约 4-8μs.
/// 改 256 项 ushort 查表后 ≈ 0.5-1ns/byte, DMI Serialize 路径从 2.55× 回归回到 v0.10 baseline.
/// 表项由 type initializer 一次构造, 所有实例共享 (512 字节 ≈ 一条 cache line × 8, 零运行时构造开销).
/// </summary>
public sealed class CrcCcitt : IBitCrcAlgorithm
{
    /// <summary>
    /// 256 项查表 — table[i] = 把 byte i 当作 MSB-aligned 16-bit 量, 经过 8 次 0x1021 多项式
    /// 移位后剩下的 CRC. 即 table[i] = CrcCcitt({i, 0}).Result with initial 0.
    /// Update 通过 (crc << 8) ^ table[(crc >> 8) ^ b] 一次性吃掉一个 byte.
    /// </summary>
    private static readonly ushort[] Table = BuildTable();

    private ushort _crc;

    public int BitWidth => 16;

    public void Reset(ulong initialValue) => _crc = (ushort)initialValue;

    public void Update(ReadOnlySpan<byte> data)
    {
        ushort crc = _crc;
        foreach (byte b in data)
        {
            crc = (ushort)((crc << 8) ^ Table[(byte)((crc >> 8) ^ b)]);
        }
        _crc = crc;
    }

    public ulong Result => _crc;

    private static ushort[] BuildTable()
    {
        // 复用原 bit-by-bit 算法构造表项 — 两路实现的等价性由
        // BitSerializerCrcLookupTableEquivalenceTests 用 1024 长度 fuzz 验证.
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)(i << 8);
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
            table[i] = crc;
        }
        return table;
    }
}
