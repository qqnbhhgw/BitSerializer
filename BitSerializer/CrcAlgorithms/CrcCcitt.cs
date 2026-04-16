namespace BitSerializer.CrcAlgorithms;

/// <summary>
/// CRC-16/CCITT-FALSE：多项式 0x1021，无位反射，无输入/输出异或。
/// 常见于 XMODEM / 车载 UART 协议（ATP 使用该算法，初始值 0）。
/// </summary>
public sealed class CrcCcitt : IBitCrcAlgorithm
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
