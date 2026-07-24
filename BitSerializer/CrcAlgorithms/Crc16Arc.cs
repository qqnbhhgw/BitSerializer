namespace BitSerializer.CrcAlgorithms;

/// <summary>
/// CRC-16/ARC（也称 IBM CRC-16 / LHA）：多项式 0x8005（反射 0xA001），
/// 位反射输入/输出，初始值 0，无输出异或。
///
/// v0.12.2 性能修复 (sibling change to CrcCcitt LookupTable): 同 CrcCcitt 思路换 256 项查表,
/// reflected poly 用 right-shift 形式 (table[i] xor (crc >> 8)). 反射版本 table 项跟 CrcCcitt
/// 不同, 但等价性测试方式一致.
/// </summary>
public sealed class Crc16Arc : IBitCrcAlgorithm, IBitCrcAlgorithm<Crc16Arc>
{
    /// <summary>
    /// 256 项查表 — table[i] = 把 byte i 经 8 次反射 0xA001 多项式 right-shift 后的 CRC,
    /// 即 table[i] = Crc16Arc({i}).Result with initial 0.
    /// Update 通过 table[(crc ^ b) & 0xFF] ^ (crc >> 8) 一次性吃掉一个 byte.
    /// </summary>
    private static readonly ushort[] Table = BuildTable();

    private ushort _crc;

    public int BitWidth => 16;

    public static int AlgorithmBitWidth => 16;

    public static ulong Compute(ReadOnlySpan<byte> data, ulong initialValue)
    {
        ushort crc = (ushort)initialValue;
        foreach (byte value in data)
            crc = (ushort)(Table[(byte)((crc ^ value) & 0xFF)] ^ (crc >> 8));
        return crc;
    }

    public void Reset(ulong initialValue) => _crc = (ushort)initialValue;

    public void Update(ReadOnlySpan<byte> data)
    {
        ushort crc = _crc;
        foreach (byte b in data)
        {
            crc = (ushort)(Table[(byte)((crc ^ b) & 0xFF)] ^ (crc >> 8));
        }
        _crc = crc;
    }

    public ulong Result => _crc;

    private static ushort[] BuildTable()
    {
        // 复用原 bit-by-bit 算法构造表项 — 等价性由 fuzz 测试验证.
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort crc = (ushort)i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 0x0001) != 0
                    ? (ushort)((crc >> 1) ^ 0xA001)
                    : (ushort)(crc >> 1);
            }
            table[i] = crc;
        }
        return table;
    }
}
