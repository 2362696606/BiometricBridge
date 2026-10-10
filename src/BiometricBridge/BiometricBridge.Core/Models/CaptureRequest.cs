using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

/// <summary>
/// 采集请求：只承载"采集动作"本身的参数
/// </summary>
/// <remarks>
/// <para>
/// 采集动作有两个维度，与 SBI 报文的 <c>bio[]</c> 一一对应：
/// <see cref="Positions"/> 是逐枚具名部位（对应 <c>bioSubType</c>），
/// <see cref="Group"/> 是分组（对应 <c>deviceSubId</c>：哪一侧、哪一组），
/// <see cref="Count"/> 是枚数。事务与协议上下文（事务号、哈希链、环境、用途）留在报文层，
/// 不是采集动作。
/// </para>
/// <para>
/// <b>请求侧的"不指定"由空集合表达，不用 <see cref="BiometricPosition.Unknown"/></b> ——
/// 后者只表示结果侧"设备判不出采到的是哪一枚"。
/// </para>
/// </remarks>
public sealed record CaptureRequest
{
    /// <summary>
    /// 要采集的模态
    /// </summary>
    public required BiometricModality Modality { get; init; }

    /// <summary>
    /// 要采集的具名部位，有序
    /// </summary>
    /// <remarks>
    /// <para>
    /// 空表示"不点名"：此时由 <see cref="Group"/> 决定哪一侧、<see cref="Count"/> 决定几枚
    /// （0 表示由设备自行决定）。
    /// </para>
    /// <para>
    /// 取值须与 <see cref="Modality"/> 匹配（指纹位置不可用于虹膜，反之亦然），也须与
    /// <see cref="Group"/> 自洽。人脸恒为空。
    /// </para>
    /// </remarks>
    public IReadOnlyList<BiometricPosition> Positions { get; init; } = [];

    /// <summary>
    /// 采集分组（哪一侧、哪一组）
    /// </summary>
    /// <remarks>
    /// 指纹的整组联采与虹膜的双眼都靠它表达；<see cref="CaptureGroup.Any"/> 表示不指定。
    /// </remarks>
    public CaptureGroup Group { get; init; } = CaptureGroup.Any;

    /// <summary>
    /// 要采集的枚数，0 表示不指定
    /// </summary>
    /// <remarks>
    /// <see cref="Positions"/> 点名时应与其余长一致；不点名时才由它说话。
    /// </remarks>
    public int Count { get; init; }

    /// <summary>
    /// 超时（毫秒）。超过此时长仍未达质量分，返回本次采集中质量最佳的帧
    /// </summary>
    public int Timeout { get; init; }

    /// <summary>
    /// 期望质量分，达到即自动采集；null 表示使用设备默认阈值
    /// </summary>
    public double? RequestedScore { get; init; }
}
