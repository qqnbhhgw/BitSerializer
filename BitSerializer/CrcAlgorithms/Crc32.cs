namespace BitSerializer.CrcAlgorithms;

/// <summary>
/// CRC-32/ISO-HDLC（IEEE 802.3 / zlib / PKZIP）：多项式 0xEDB88320（反射 0x04C11DB7），
/// 位反射输入/输出，初始值 0xFFFFFFFF，输出异或 0xFFFFFFFF。
/// 对 ASCII "123456789" 结果为 0xCBF43926。
/// 当使用默认 InitialValue = 0 时，Reset 会将其翻转为标准 0xFFFFFFFF。
/// 若需自定义初始值，传入已翻转的值（即与 0xFFFFFFFF 异或后的值）。
/// </summary>
public sealed class Crc32 : IBitCrcAlgorithm
{
    private static readonly uint[] Table = BuildTable();

    private uint _crc;

    public int BitWidth => 32;

    public void Reset(ulong initialValue)
    {
        // 约定：InitialValue=0 (BitCrc 默认) 使用标准 0xFFFFFFFF
        _crc = initialValue == 0 ? 0xFFFFFFFFu : (uint)initialValue;
    }

    public void Update(ReadOnlySpan<byte> data)
    {
        uint crc = _crc;
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        _crc = crc;
    }

    public ulong Result => _crc ^ 0xFFFFFFFFu;

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }
}
