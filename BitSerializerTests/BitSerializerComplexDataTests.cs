using BitSerializer;
using Shouldly;

namespace BitSerializerTests;

/// <summary>
///  830位应答器报文
/// </summary>
[BitSerialize]
public partial class Balise830
{
    /// <summary>
    /// 信息传送的方向（0=车对地，1=地对车），本规范中统一为1。
    /// </summary>
    [BitField(1)]
    public QUpdown QUpdown { get; set; }

    /// <summary>
    /// 语言/代码版本编号（0010000=V1.0）
    /// </summary>
    [BitField(7)]
    public int MVersion { get; set; }

    /// <summary>
    /// 信息传输媒介（0=应答器）
    /// </summary>
    [BitField(1)]
    public int Media { get; set; }

    /// <summary>
    /// 本应答器在应答器组中的位置（000=1，111=8），本规范中不设置应答器组，本变量采用000。
    /// </summary>
    [BitField(3)]
    public int NPig { get; set; }

    /// <summary>
    /// 应答器组中所包含的应答器数量（000=1，111=8），本规范中不设置应答器组，本变量采用000。
    /// </summary>
    [BitField(3)]
    public int NTotal { get; set; }

    /// <summary>
    /// 本应答器信息与前/后应答器信息的关系（00=不同，01=与后一个相同，10=与前一个相同），本规范采用00。
    /// </summary>
    [BitField(2)]
    public int MDup { get; set; }

    /// <summary>
    /// 报文计数器（0～255）。
    /// </summary>
    [BitField(8)]
    public byte MCount { get; set; }
}

public enum QUpdown
{
    Down = 0b0,
    Up   = 0b1
}

/// <summary>
/// 互联互通应答器报文
/// </summary>
[BitSerialize]
public partial class HlhtBalise830 : Balise830
{
    /// <summary>
    /// 线路编号
    /// </summary>
    [BitField(10)]
    public uint LineId { get; set; }

    /// <summary>
    /// 应答器（组）编号
    /// </summary>
    [BitField(14)]
    public ushort BaliseId { get; set; }

    /// <summary>
    /// 应答器（组）的链接关系 （0=不被链接，1=被链接），本规范采用0。
    /// </summary>
    [BitField(1)]
    public int QLink { get; set; }

    /// <summary>
    /// ETCS 包Id
    /// </summary>
    [BitField(8)]
    public int ETCSPkgId { get; set; }

    /// <summary>
    /// 互联互通 ETCS 包Id
    /// </summary>
    [BitField]
    [BitFieldRelated(nameof(ETCSPkgId))]
    [BitPoly(44, typeof(HlhtETCS44))]
    public HlhtETCS44 HlhtETCS44 { get; set; }
}

[BitSerialize]
public partial class ETCS
{
}

[BitSerialize]
public partial class ETCS44Base : ETCS
{
    [BitField(2)] public QDir QDir { get; set; }
    [BitField(13)] public int LPackage { get; set; }
}

public enum QDir
{
    Reverse  = 0b00,
    Normal   = 0b01,
    Both     = 0b10,
    Reserved = 0b11
}

[BitSerialize]
public partial class HlhtETCS44 : ETCS44Base
{
    [BitField(9)] public int NIDXUser { get; set; }

    [BitField]
    [BitFieldRelated(nameof(NIDXUser))]
    [BitPoly(202, typeof(CTCS202))]
    [BitPoly(203, typeof(CTCS203))]
    public CTCS CTCSPkg { get; set; }
}

[BitSerialize]
public partial class CTCS
{
}

[BitSerialize]
public partial class CTCS202 : CTCS
{
    /// <summary>
    /// 地图版本信息（本应答器所辖范围内的地图版本）
    /// </summary>
    [BitField]
    public ushort EMapVersion { get; set; }
}

[BitSerialize]
public partial class CTCS203 : CTCS
{
    /// <summary>
    /// 主应答器时为该处信号机显示状态。
    /// 填充应答器时为所填充进路始端信号机的状态。
    /// </summary>
    [BitField]
    public CTCS203Aspect Aspect { get; set; }

    /// <summary>
    /// 该应答器预告信号状态。该位仅对于主应答器有效，该位表示该主应答器兼具预告应答器功能时，对应的沿进路方向第二架信号机显示状态。
    /// 对于不兼具预告功能的主应答器，该位域全部填0。
    /// 填充应答器该位无效（填充应答器全部填0），应用不应采用。
    /// </summary>
    [BitField]
    public CTCS203Aspect PreAspect { get; set; }

    /// <summary>
    /// 联锁与LEU通信状态。
    ///  0：联锁与LEU通信正常；
    ///  1：联锁与LEU通信中断。
    /// 当C_LEU_BALISE为1（即LEU与应答器通信中断）时，该位无效。
    /// 如系统采用点灯电路的方式驱动LEU，点灯电路故障，按照LEU与联锁通信中断处理。
    /// </summary>
    [BitField(1)]
    public int CiLeu { get; set; } = 1;

    /// <summary>
    /// LEU与应答器通信状态。
    /// 0：LEU与应答器通信正常；
    /// 1：LEU与应答器通信中断。
    /// </summary>
    [BitField(1)]
    public int CiLeuBalise { get; set; } = 1;

    /// <summary>
    /// 主应答器至MA终点距离，有保护区段时为保护区段终点距离。
    /// </summary>
    [BitField(24)]
    public uint DistanceToMaEnd { get; set; }

    /// <summary>
    /// 主应答器至进路末端保护区段起点的距离，无保护区段时为0。
    /// </summary>
    [BitField(24)]
    public uint DistanceToOverlapStart { get; set; }

    /// <summary>
    /// 道岔数量，最多15个。
    /// </summary>
    [BitField(4)]
    public int MaSwitchCount { get; set; }

    /// <summary>
    /// 道岔编号 + 道岔状态
    /// </summary>
    [BitField]
    [BitFieldRelated(nameof(MaSwitchCount))]
    public List<BaliseSwitch> MaSwitches { get; set; }
}

/// <summary>
/// 应答器道岔状态
/// </summary>
public enum BaliseSwitchState
{
    Reverse = 0b01,
    Normal  = 0b10
}

[BitSerialize]
public partial record BaliseSwitch
{
    /// <summary>
    /// 道岔编号
    /// </summary>
    [BitField]
    public ushort Id { get; set; }

    /// <summary>
    /// 道岔状态
    /// </summary>
    [BitField(2)]
    public BaliseSwitchState SwitchState { get; set; }
}

/// <summary>
/// 指示状态
/// </summary>
[BitSerialize]
public partial class CTCS203Aspect
{
    /// <summary>
    /// 预留
    /// </summary>
    [BitField(2)]
    public byte Reserved { get; set; }

    /// <summary>
    /// 对向道岔位置
    /// </summary>
    [BitField]
    public FacingSwitchesPositions SwitchesPositions { get; set; }

    /// <summary>
    /// 是否绿显
    /// </summary>
    [BitField(1)]
    public byte SignalGreen { get; set; }

    /// <summary>
    /// 是否有保护区段
    /// </summary>
    [BitField(1)]
    public byte ProtectSectionValue { get; set; }

    /// <summary>
    /// 是否绿显
    /// </summary>
    /// <returns></returns>
    public bool IsGreen() => SignalGreen == 1;

    /// <summary>
    /// 是否红显
    /// </summary>
    /// <returns></returns>
    public bool IsRed() => !IsGreen() && SwitchesPositions.GetPositions().All(p => p == FacingSwitchPosition.Normal);

    /// <summary>
    /// 是否有保护区段
    /// </summary>
    /// <returns></returns>
    public bool HasProtectSection() => ProtectSectionValue == 1;
}

/// <summary>
/// 对向道岔状态
/// </summary>
public enum FacingSwitchPosition
{
    Normal  = 0b0,
    Reverse = 0b1
}

/// <summary>
/// 对向道岔位置
/// </summary>
[BitSerialize]
public partial class FacingSwitchesPositions
{
    [BitField(1)] public FacingSwitchPosition Switch15Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch14Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch13Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch12Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch11Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch10Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch9Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch8Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch7Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch6Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch5Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch4Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch3Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch2Position { get; set; }
    [BitField(1)] public FacingSwitchPosition Switch1Position { get; set; }

    /// <summary>
    /// 按照编号顺序获取道岔位置
    /// </summary>
    /// <returns></returns>
    public IEnumerable<FacingSwitchPosition> GetPositions()
    {
        return
        [
            Switch1Position,
            Switch2Position,
            Switch3Position,
            Switch4Position,
            Switch5Position,
            Switch6Position,
            Switch7Position,
            Switch8Position,
            Switch9Position,
            Switch10Position,
            Switch11Position,
            Switch12Position,
            Switch13Position,
            Switch14Position,
            Switch15Position
        ];
    }
}

public class BitSerializerComplexDataTests
{
    [Fact]
    public void ShouldRunTrip()
    {
        var hlhtCtcs203 = new CTCS203
        {
            Aspect = new CTCS203Aspect
            {
                SignalGreen = 1,
                ProtectSectionValue = 1,
                SwitchesPositions = new FacingSwitchesPositions
                {
                    Switch1Position = FacingSwitchPosition.Normal,
                    Switch2Position = FacingSwitchPosition.Normal,
                }
            },

            PreAspect = new CTCS203Aspect
            {
                SignalGreen = 1,
                ProtectSectionValue = 1,
                SwitchesPositions = new FacingSwitchesPositions
                {
                    Switch1Position = FacingSwitchPosition.Normal,
                    Switch2Position = FacingSwitchPosition.Normal,
                }
            },

            MaSwitches = [],
        };

        var hlhtEtcs44 = new HlhtETCS44
        {
            NIDXUser = 203,
            CTCSPkg = hlhtCtcs203
        };

        var hlhtBalise830 = new HlhtBalise830
        {
            LineId = 1,
            BaliseId = 1,
            QLink = 1,
            ETCSPkgId = 44,
            HlhtETCS44 = hlhtEtcs44
        };

        var bytes = BitSerializerMSB.Serialize(hlhtBalise830);
        var another = BitSerializerMSB.Deserialize<HlhtBalise830>(bytes);

        another.ShouldNotBeNull();
        another.ShouldBeEquivalentTo(hlhtBalise830);
    }
}