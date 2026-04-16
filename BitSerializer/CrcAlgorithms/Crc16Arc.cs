namespace BitSerializer.CrcAlgorithms;

/// <summary>
/// CRC-16/ARC（也称 IBM CRC-16 / LHA）：多项式 0x8005（反射 0xA001），
/// 位反射输入/输出，初始值 0，无输出异或。
/// </summary>
public sealed class Crc16Arc : IBitCrcAlgorithm
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
