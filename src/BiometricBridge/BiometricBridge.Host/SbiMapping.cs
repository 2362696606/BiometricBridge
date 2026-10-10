using System.Globalization;
using BiometricBridge.Host.Common;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using CoreEnums = BiometricBridge.Core.Models.Enums;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// 设备抽象层（<c>BiometricBridge.Core.Models</c>）→ 报文层（<c>BiometricBridge.Host.Dto</c>）的映射
/// </summary>
/// <remarks>
/// <para>
/// 两个端点（<c>/device</c>、<c>/info</c>）共用同一套映射：同一台设备在两处必须给出同样的状态与
/// 同样的身份取值，各写一份必然漂移。
/// </para>
/// <para>
/// 类型名用 <c>CoreModels</c> / <c>CoreEnums</c> 别名限定，是为了让这处"两层之间的接缝"一眼可见 ——
/// 报文类型不引用设备抽象层的同名类型，两边只在这里对接。
/// </para>
/// </remarks>
internal static class SbiMapping
{
    /// <summary>
    /// 管理器状态映射成协议状态
    /// </summary>
    /// <param name="snapshot">
    /// 设备快照
    /// </param>
    /// <returns>
    /// 协议状态
    /// </returns>
    /// <remarks>
    /// 管理器状态为"就绪"时以设备<b>实际连接状态</b>为准：没连上就不算就绪。管理器的"就绪"是默认值，
    /// 一个从未连过的设备也会是它，直接转发等于告诉客户端"来采集吧"，而那次采集必然失败。
    /// 其余状态（忙 / 未就绪 / 未注册）是人为或设备侧给出的真实状态，一律照搬。
    /// </remarks>
    public static DeviceStatus MapStatus(CoreModels.DeviceSnapshot snapshot)
    {
        var status = snapshot.DeviceStatus == CoreEnums.BiometricDeviceStatus.Ready && !snapshot.IsConnected
            ? CoreEnums.BiometricDeviceStatus.NotReady
            : snapshot.DeviceStatus;

        return status switch
        {
            CoreEnums.BiometricDeviceStatus.Ready => DeviceStatus.Ready,
            CoreEnums.BiometricDeviceStatus.Busy => DeviceStatus.Busy,
            CoreEnums.BiometricDeviceStatus.NotReady => DeviceStatus.NotReady,
            CoreEnums.BiometricDeviceStatus.NotRegistered => DeviceStatus.NotRegistered,
            _ => DeviceStatus.NotReady,
        };
    }

    /// <summary>
    /// 协议状态 → 错误对象
    /// </summary>
    /// <param name="status">
    /// 协议状态
    /// </param>
    /// <returns>
    /// 错误对象
    /// </returns>
    /// <remarks>
    /// 成功也返回对象（取表示成功的码）：两个端点的错误块都是结构的一部分，不是可选的。
    /// </remarks>
    public static SbiError ErrorFor(DeviceStatus status) => status switch
    {
        DeviceStatus.Ready => SbiErrorHelper.Success,
        DeviceStatus.Busy => SbiErrorHelper.DeviceBusy,
        DeviceStatus.NotReady => SbiErrorHelper.DeviceNotReady,
        _ => SbiErrorHelper.DeviceNotRegistered,
    };

    /// <summary>
    /// 设备子 ID 映射
    /// </summary>
    /// <param name="deviceSubIds">
    /// 设备抽象层的子 ID
    /// </param>
    /// <returns>
    /// 协议里的子 ID
    /// </returns>
    /// <remarks>
    /// <b>线格式是字符串数组，不是整数数组</b>。v0.9.5 的 schema 文档把该字段写作 <c>array of integer</c>，
    /// 但 CTK 实际用的 schema 期望字符串、且取值须落在 <c>["0","1","2","3"]</c> 内 ——
    /// 2026-10-09 实测发整数被判 <c>integer found, string expected</c>。以 CTK 的为准。
    /// </remarks>
    public static string[] MapDeviceSubId(IReadOnlyList<int> deviceSubIds)
        => [.. deviceSubIds.Select(static id => id.ToString(CultureInfo.InvariantCulture))];

    /// <summary>
    /// 认证等级映射
    /// </summary>
    /// <param name="level">
    /// 设备抽象层的认证等级
    /// </param>
    /// <returns>
    /// 协议里的认证等级
    /// </returns>
    public static CertificationLevel MapCertification(CoreEnums.CertificationLevel level) => level switch
    {
        CoreEnums.CertificationLevel.L1 => CertificationLevel.L1,
        _ => CertificationLevel.L0,
    };

    /// <summary>
    /// 模态映射
    /// </summary>
    /// <param name="modality">
    /// 设备抽象层的模态
    /// </param>
    /// <returns>
    /// 协议里的生物特征类型
    /// </returns>
    public static BiometricType MapBiometricType(CoreEnums.BiometricModality modality) => modality switch
    {
        CoreEnums.BiometricModality.Iris => BiometricType.Iris,
        CoreEnums.BiometricModality.Face => BiometricType.Face,
        _ => BiometricType.Finger,
    };

    /// <summary>
    /// 设备子类型映射
    /// </summary>
    /// <param name="subType">
    /// 设备抽象层的子类型
    /// </param>
    /// <returns>
    /// 协议里的子类型
    /// </returns>
    /// <remarks>
    /// 设备抽象层的 <see cref="CoreEnums.DeviceSubType.Finger"/>（窄幅传感器上的单指）必须落到俗称的
    /// <c>Single</c>，<b>不能照抄成 <c>Finger</c></b>：CTK 的 <c>DiscoverResponseSchema</c> 在
    /// <c>type=Finger</c> 时只接受 <c>Slap</c>/<c>Single</c>/<c>Touchless</c>，写 <c>Finger</c> 直接判失败
    /// （<c>Finger</c> 是 <c>type</c> 的取值，两者不是一回事）。
    /// </remarks>
    public static DeviceSubType MapDeviceSubType(CoreEnums.DeviceSubType subType) => subType switch
    {
        CoreEnums.DeviceSubType.Slap => DeviceSubType.Slap,
        CoreEnums.DeviceSubType.Touchless => DeviceSubType.Touchless,
        CoreEnums.DeviceSubType.Double => DeviceSubType.Double,
        CoreEnums.DeviceSubType.FullFace => DeviceSubType.FullFace,
        _ => DeviceSubType.Single,
    };

    /// <summary>
    /// 采集位置映射成协议里的生物特征子类型
    /// </summary>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="position">
    /// 设备抽象层的采集位置
    /// </param>
    /// <returns>
    /// 协议里的子类型字面量；人脸为空串，取不到具体部位时为 <c>UNKNOWN</c>
    /// </returns>
    /// <remarks>
    /// <b>取值逐字对齐规范</b>（注意 <c>IndexFinger</c> 是一个词、<c>F</c> 大写）；字面量表在
    /// <see cref="SbiBioSubTypes"/>，本方法是它的一层委托。具名部位必得精确字面量，
    /// 只有"判不出部位"（<see langword="null"/> 或 <see cref="CoreEnums.BiometricPosition.Unknown"/>）
    /// 才落 <c>UNKNOWN</c>。人脸没有子类型，取空串。
    /// </remarks>
    public static string MapBioSubType(
        CoreEnums.BiometricModality modality,
        CoreEnums.BiometricPosition? position)
        => modality == CoreEnums.BiometricModality.Face
            ? string.Empty
            : SbiBioSubTypes.LiteralOf(position);

    /// <summary>
    /// 请求里的子类型字面量映射成设备抽象层的采集位置
    /// </summary>
    /// <param name="bioSubType">
    /// 请求里的子类型（单个），取值见规范
    /// </param>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <param name="position">
    /// 解析出的位置；解析失败时为 <see cref="CoreEnums.BiometricPosition.Unknown"/>
    /// </param>
    /// <returns>
    /// 解析出具体部位返回 true；为空、为 <c>UNKNOWN</c>、或与模态不符返回 false
    /// </returns>
    /// <remarks>
    /// 大小写严格：与发现端点的类型比对同一态度 —— 取值是规范给定的字面量，宽容处理只会让
    /// 客户端的笔误静默变成"采了别的部位"。字面量表在 <see cref="SbiBioSubTypes"/>，本方法是它的
    /// 一层委托；<c>UNKNOWN</c> 是<b>合法取值</b>（表示不点名）但解不出具名部位，故同样返回 false
    /// —— 要区分它与非法字面量，用 <see cref="SbiBioSubTypes.IsUnknownLiteral"/>。
    /// </remarks>
    public static bool TryMapPosition(
        string? bioSubType,
        CoreEnums.BiometricModality modality,
        out CoreEnums.BiometricPosition position)
        => SbiBioSubTypes.TryParseLiteral(bioSubType, modality, out position);
}
