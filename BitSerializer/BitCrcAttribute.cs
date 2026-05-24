namespace BitSerializer;

/// <summary>
/// 标记 CRC 结果字段。两种模式：
/// <list type="bullet">
/// <item><b>IncludeRange 模式（默认）</b>：与 [BitCrcInclude] 配合，CRC 覆盖被显式标注的连续字段范围。
/// 要求范围内字段字节对齐、多态子类型位宽 8 倍数、无未受控动态内容（BITS017）。</item>
/// <item><b>WholeBuffer 模式</b>：设置 <see cref="WholeBuffer"/> = true，CRC 覆盖整个类型序列化后的
/// buffer，可通过 <see cref="SkipHeadBytes"/> / <see cref="SkipTailBytes"/> 跳过开头/末尾字节
/// （如帧头分隔符、CRC 字段本身、帧结束符）。该模式下无 [BitCrcInclude]，不受 BITS017 限制，
/// 适用于含动态字段、多态子类型、变长 List/byte[] 的协议帧（如 DMI 的
/// <c>UartCrc16(bytes, 1, bytes.Length - 4)</c> = SkipHeadBytes=1, SkipTailBytes=4）。</item>
/// </list>
/// 两种模式互斥，混用时报 BITS029。
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class BitCrcAttribute(Type algorithmType) : Attribute
{
    public Type AlgorithmType { get; set; } = algorithmType;

    public ulong InitialValue { get; set; } = 0;

    public bool ValidateOnDeserialize { get; set; } = false;

    /// <summary>
    /// WholeBuffer 模式开关。true 时 CRC 覆盖整个类型序列化后的 buffer（减去 SkipHead/SkipTail），
    /// 此时不允许同类型出现 [BitCrcInclude]，且不受 BITS017 多态/动态字段限制。默认 false（IncludeRange）。
    /// </summary>
    public bool WholeBuffer { get; set; } = false;

    /// <summary>
    /// WholeBuffer 模式下跳过 buffer 开头的字节数（如帧头分隔符 0x7E）。默认 0。
    /// </summary>
    public int SkipHeadBytes { get; set; } = 0;

    /// <summary>
    /// WholeBuffer 模式下跳过 buffer 末尾的字节数（必须 ≥ CRC 字段自身字节宽 + 其它末尾字段如帧结束符）。
    /// 默认 0。若 SkipTailBytes 小于 CRC 字段自身字节宽，CRC 会读到自身值，结果不稳定。
    /// </summary>
    public int SkipTailBytes { get; set; } = 0;
}
