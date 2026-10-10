using CoreEnums = BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Host;

/// <summary>
/// SBI 的 <c>bioSubType</c> 字面量 与 <c>deviceSubId</c> 分组 的唯一映射表
/// </summary>
/// <remarks>
/// <para>
/// 规范给定的字面量与分组只在本文件出现一次：请求侧解析、响应侧生成、分组校验与展开都经这里。
/// 三处各写一份 switch 必然漂移（此前正是如此）。
/// </para>
/// <para>
/// <b>取值逐字对齐规范</b>（<c>IndexFinger</c> 是一个词、<c>F</c> 大写），大小写敏感：与发现端点的
/// 类型比对同一态度 —— 宽容处理只会让客户端的笔误静默变成"采了别的部位"。
/// </para>
/// <para>
/// 分组语义（规范）：指纹／虹膜的 <c>deviceSubId</c> <c>1</c>=左（左手四指／左眼）、
/// <c>2</c>=右、<c>3</c>=双（双拇指／双眼）、<c>0</c>=未知（指纹联采不可用 0）。
/// 注意<b>左右四指不含拇指</b>：双拇指自成第 3 组。
/// </para>
/// </remarks>
internal static class SbiBioSubTypes
{
    /// <summary>
    /// "任意／不明"的字面量。请求里它表示"不点名"，响应里表示"判不出部位"。
    /// </summary>
    public const string UnknownLiteral = "UNKNOWN";

    /// <summary>
    /// 位置 → 线格式字面量
    /// </summary>
    /// <remarks>
    /// 只收具名部位：<see cref="BiometricPosition.Unknown"/> 不入表，它由调用方按"判不出"处理。
    /// </remarks>
    private static readonly Dictionary<CoreEnums.BiometricPosition, string> LiteralByPosition = new()
    {
        [CoreEnums.BiometricPosition.LeftThumb] = "Left Thumb",
        [CoreEnums.BiometricPosition.LeftIndexFinger] = "Left IndexFinger",
        [CoreEnums.BiometricPosition.LeftMiddleFinger] = "Left MiddleFinger",
        [CoreEnums.BiometricPosition.LeftRingFinger] = "Left RingFinger",
        [CoreEnums.BiometricPosition.LeftLittleFinger] = "Left LittleFinger",
        [CoreEnums.BiometricPosition.RightThumb] = "Right Thumb",
        [CoreEnums.BiometricPosition.RightIndexFinger] = "Right IndexFinger",
        [CoreEnums.BiometricPosition.RightMiddleFinger] = "Right MiddleFinger",
        [CoreEnums.BiometricPosition.RightRingFinger] = "Right RingFinger",
        [CoreEnums.BiometricPosition.RightLittleFinger] = "Right LittleFinger",
        [CoreEnums.BiometricPosition.LeftIris] = "Left",
        [CoreEnums.BiometricPosition.RightIris] = "Right",
    };

    /// <summary>
    /// 分组的成员部位（各模态）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"哪一侧是哪几枚"的唯一出处：正向用于展开与校验，反向（由一组部位推分组）由它遍历得出，
    /// 免得再写一份反向表。
    /// </para>
    /// <para>
    /// <see cref="CoreEnums.CaptureGroup.Any"/> 列出该模态的全部具名部位，即"不约束"：
    /// 它的成员关系恒成立，展开只用于"有哪些部位"这类遍历，<b>不代表设备必须按此顺序或数量返回</b>。
    /// 左右四指的展开按规范给设备定的顺序（食指 → 小指）。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<(CoreEnums.BiometricModality Modality, CoreEnums.CaptureGroup Group),
        CoreEnums.BiometricPosition[]> MembersByGroup = new()
        {
            [(CoreEnums.BiometricModality.Finger, CoreEnums.CaptureGroup.Left)] =
            [
                CoreEnums.BiometricPosition.LeftIndexFinger,
                CoreEnums.BiometricPosition.LeftMiddleFinger,
                CoreEnums.BiometricPosition.LeftRingFinger,
                CoreEnums.BiometricPosition.LeftLittleFinger,
            ],
            [(CoreEnums.BiometricModality.Finger, CoreEnums.CaptureGroup.Right)] =
            [
                CoreEnums.BiometricPosition.RightIndexFinger,
                CoreEnums.BiometricPosition.RightMiddleFinger,
                CoreEnums.BiometricPosition.RightRingFinger,
                CoreEnums.BiometricPosition.RightLittleFinger,
            ],

            // 双拇指：两只手的拇指各一枚，跨左右手。
            [(CoreEnums.BiometricModality.Finger, CoreEnums.CaptureGroup.Both)] =
            [
                CoreEnums.BiometricPosition.LeftThumb,
                CoreEnums.BiometricPosition.RightThumb,
            ],
            [(CoreEnums.BiometricModality.Finger, CoreEnums.CaptureGroup.Any)] =
            [
                CoreEnums.BiometricPosition.LeftIndexFinger,
                CoreEnums.BiometricPosition.LeftMiddleFinger,
                CoreEnums.BiometricPosition.LeftRingFinger,
                CoreEnums.BiometricPosition.LeftLittleFinger,
                CoreEnums.BiometricPosition.LeftThumb,
                CoreEnums.BiometricPosition.RightIndexFinger,
                CoreEnums.BiometricPosition.RightMiddleFinger,
                CoreEnums.BiometricPosition.RightRingFinger,
                CoreEnums.BiometricPosition.RightLittleFinger,
                CoreEnums.BiometricPosition.RightThumb,
            ],

            [(CoreEnums.BiometricModality.Iris, CoreEnums.CaptureGroup.Left)] =
            [
                CoreEnums.BiometricPosition.LeftIris,
            ],
            [(CoreEnums.BiometricModality.Iris, CoreEnums.CaptureGroup.Right)] =
            [
                CoreEnums.BiometricPosition.RightIris,
            ],
            [(CoreEnums.BiometricModality.Iris, CoreEnums.CaptureGroup.Both)] =
            [
                CoreEnums.BiometricPosition.LeftIris,
                CoreEnums.BiometricPosition.RightIris,
            ],
            [(CoreEnums.BiometricModality.Iris, CoreEnums.CaptureGroup.Any)] =
            [
                CoreEnums.BiometricPosition.LeftIris,
                CoreEnums.BiometricPosition.RightIris,
            ],

            // 人脸没有具名部位（规范不使用 bioSubType），也就没有分组可言。
            [(CoreEnums.BiometricModality.Face, CoreEnums.CaptureGroup.Any)] = [],
        };

    /// <summary>
    /// 具名部位的字面量 → 位置，按模态分表
    /// </summary>
    /// <remarks>
    /// 由 <see cref="LiteralByPosition"/> 派生，不手写第二份字面量。
    /// </remarks>
    private static readonly Dictionary<CoreEnums.BiometricModality, Dictionary<string, CoreEnums.BiometricPosition>>
        PositionByLiteral = BuildPositionByLiteral();

    /// <summary>
    /// 具体分组（不含"不指定"）
    /// </summary>
    /// <remarks>
    /// 反推分组时按它遍历；顺序只影响判定过程的先后，不影响结果。
    /// </remarks>
    private static readonly CoreEnums.CaptureGroup[] SpecificGroups =
    [
        CoreEnums.CaptureGroup.Left,
        CoreEnums.CaptureGroup.Right,
        CoreEnums.CaptureGroup.Both,
    ];

    /// <summary>
    /// 全部具名部位（不含 <see cref="BiometricPosition.Unknown"/>）
    /// </summary>
    /// <remarks>
    /// 供遍历与一致性测试用：新增部位时，本表、分组表、ISO 编码表须同时补齐。
    /// </remarks>
    public static IReadOnlyCollection<CoreEnums.BiometricPosition> NamedPositions => LiteralByPosition.Keys;

    /// <summary>
    /// 响应侧：位置 → 线格式字面量
    /// </summary>
    /// <param name="position">
    /// 采集结果的位置
    /// </param>
    /// <returns>
    /// 规范字面量；位置判不出（<see langword="null"/> 或 <see cref="BiometricPosition.Unknown"/>）
    /// 时为 <see cref="UnknownLiteral"/>
    /// </returns>
    public static string LiteralOf(CoreEnums.BiometricPosition? position)
        => position is { } named && LiteralByPosition.TryGetValue(named, out var literal)
            ? literal
            : UnknownLiteral;

    /// <summary>
    /// 字面量是不是"任意／不明"
    /// </summary>
    /// <param name="literal">
    /// 线格式字面量
    /// </param>
    /// <returns>
    /// 是 <see cref="UnknownLiteral"/> 返回 true
    /// </returns>
    /// <remarks>
    /// 与"非法字面量"分开判：前者是合法取值（请求里表示不点名），后者该报错。
    /// </remarks>
    public static bool IsUnknownLiteral(string? literal)
        => string.Equals(literal, UnknownLiteral, StringComparison.Ordinal);

    /// <summary>
    /// 请求侧：线格式字面量 → 位置
    /// </summary>
    /// <param name="literal">
    /// 线格式字面量
    /// </param>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="position">
    /// 解析出的位置
    /// </param>
    /// <returns>
    /// 解析出具名部位返回 true；为空、为 <c>UNKNOWN</c>、或与模态不符返回 false
    /// </returns>
    public static bool TryParseLiteral(
        string? literal,
        CoreEnums.BiometricModality modality,
        out CoreEnums.BiometricPosition position)
    {
        position = CoreEnums.BiometricPosition.Unknown;

        return !string.IsNullOrEmpty(literal)
            && PositionByLiteral.TryGetValue(modality, out var byLiteral)
            && byLiteral.TryGetValue(literal, out position);
    }

    /// <summary>
    /// 协议子 ID → 分组
    /// </summary>
    /// <param name="deviceSubId">
    /// 协议里的 <c>deviceSubId</c>
    /// </param>
    /// <param name="group">
    /// 对应的分组
    /// </param>
    /// <returns>
    /// 取值合法返回 true
    /// </returns>
    public static bool TryGroupOfDeviceSubId(int deviceSubId, out CoreEnums.CaptureGroup group)
    {
        group = deviceSubId switch
        {
            0 => CoreEnums.CaptureGroup.Any,
            1 => CoreEnums.CaptureGroup.Left,
            2 => CoreEnums.CaptureGroup.Right,
            3 => CoreEnums.CaptureGroup.Both,
            _ => CoreEnums.CaptureGroup.Any,
        };

        return deviceSubId is >= 0 and <= 3;
    }

    /// <summary>
    /// 分组 → 协议子 ID
    /// </summary>
    /// <param name="group">
    /// 分组
    /// </param>
    /// <returns>
    /// 规范里的 <c>deviceSubId</c> 取值
    /// </returns>
    public static int DeviceSubIdOf(CoreEnums.CaptureGroup group) => group switch
    {
        CoreEnums.CaptureGroup.Left => 1,
        CoreEnums.CaptureGroup.Right => 2,
        CoreEnums.CaptureGroup.Both => 3,
        _ => 0,
    };

    /// <summary>
    /// 某模态下某分组的成员部位
    /// </summary>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="group">
    /// 分组
    /// </param>
    /// <returns>
    /// 该组的具名部位；<see cref="CoreEnums.CaptureGroup.Any"/> 返回该模态全部具名部位；
    /// 模态与分组无此组合（如人脸带具体分组）时返回空
    /// </returns>
    public static IReadOnlyList<CoreEnums.BiometricPosition> PositionsOfGroup(
        CoreEnums.BiometricModality modality,
        CoreEnums.CaptureGroup group)
        => MembersByGroup.TryGetValue((modality, group), out var members) ? members : [];

    /// <summary>
    /// 部位是否属于该模态的该分组
    /// </summary>
    /// <param name="position">
    /// 位置
    /// </param>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="group">
    /// 分组
    /// </param>
    /// <returns>
    /// 属于返回 true；<see cref="CoreEnums.CaptureGroup.Any"/> 视为不约束，恒为 true
    /// </returns>
    public static bool IsInGroup(
        CoreEnums.BiometricPosition position,
        CoreEnums.BiometricModality modality,
        CoreEnums.CaptureGroup group)
        => PositionsOfGroup(modality, group).Contains(position);

    /// <summary>
    /// 由一组具名部位反推分组
    /// </summary>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="positions">
    /// 具名部位集合
    /// </param>
    /// <param name="group">
    /// 反推出的分组
    /// </param>
    /// <returns>
    /// 能推出一组时返回 true（空集合、跨组、含非本模态部位均 false）
    /// </returns>
    /// <remarks>
    /// <para>
    /// 取<b>最具体的包含组</b>：所有部位都落在某组里时，取成员最少的那组。这是必要的 ——
    /// "双"组的成员本就包含各单侧（双眼含左眼、双拇指含左拇指），只取"命中的组"会同时命中 Left 与 Both。
    /// </para>
    /// <para>
    /// 推出来的只是"这些部位落在哪一组"，<b>不等于"必须采满整组"</b>：请求点了一枚左眼，
    /// 推出来的 Left 只用来与 <c>deviceSubId</c> 对账，不该据此要求两眼都到齐。
    /// </para>
    /// </remarks>
    public static bool TryGroupOf(
        CoreEnums.BiometricModality modality,
        IReadOnlyList<CoreEnums.BiometricPosition> positions,
        out CoreEnums.CaptureGroup group)
    {
        group = CoreEnums.CaptureGroup.Any;

        if (positions.Count == 0)
        {
            return false;
        }

        var found = false;
        var memberCount = int.MaxValue;

        foreach (var candidate in SpecificGroups)
        {
            var members = PositionsOfGroup(modality, candidate);
            if (!positions.All(members.Contains))
            {
                continue;
            }

            if (members.Count < memberCount)
            {
                memberCount = members.Count;
                group = candidate;
                found = true;
                continue;
            }

            if (members.Count == memberCount)
            {
                // 两个候选同样具体（当前数据里不会发生）：判不了，宁可说推不出来，也不随口给一个。
                group = CoreEnums.CaptureGroup.Any;
                return false;
            }
        }

        return found;
    }

    /// <summary>
    /// 由字面量表派生"字面量 → 位置"表
    /// </summary>
    /// <returns>
    /// 按模态分好的反查表
    /// </returns>
    private static Dictionary<CoreEnums.BiometricModality, Dictionary<string, CoreEnums.BiometricPosition>>
        BuildPositionByLiteral()
    {
        var iris = new Dictionary<string, CoreEnums.BiometricPosition>(StringComparer.Ordinal);
        var finger = new Dictionary<string, CoreEnums.BiometricPosition>(StringComparer.Ordinal);

        foreach (var (position, literal) in LiteralByPosition)
        {
            var table = position is CoreEnums.BiometricPosition.LeftIris or CoreEnums.BiometricPosition.RightIris
                ? iris
                : finger;

            table[literal] = position;
        }

        return new Dictionary<CoreEnums.BiometricModality, Dictionary<string, CoreEnums.BiometricPosition>>
        {
            [CoreEnums.BiometricModality.Finger] = finger,
            [CoreEnums.BiometricModality.Iris] = iris,
        };
    }
}
