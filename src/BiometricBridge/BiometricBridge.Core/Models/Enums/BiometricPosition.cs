namespace BiometricBridge.Core.Models.Enums;

/// <summary>
/// 采集位置。设备层使用自有命名，不采用 SBI/ISO 的字符串字面量，
/// 与协议的映射由上层适配器负责
/// </summary>
public enum BiometricPosition
{
    /// <summary>
    /// 位置未确定。请求中表示由设备自行选择可用位置；
    /// 结果中表示设备无法判断采集到的是哪个位置
    /// </summary>
    Unknown,

    /// <summary>
    /// 左手拇指（仅指纹）
    /// </summary>
    LeftThumb,

    /// <summary>
    /// 左手食指（仅指纹）
    /// </summary>
    LeftIndexFinger,

    /// <summary>
    /// 左手中指（仅指纹）
    /// </summary>
    LeftMiddleFinger,

    /// <summary>
    /// 左手无名指（仅指纹）
    /// </summary>
    LeftRingFinger,

    /// <summary>
    /// 左手小指（仅指纹）
    /// </summary>
    LeftLittleFinger,

    /// <summary>
    /// 右手拇指（仅指纹）
    /// </summary>
    RightThumb,

    /// <summary>
    /// 右手食指（仅指纹）
    /// </summary>
    RightIndexFinger,

    /// <summary>
    /// 右手中指（仅指纹）
    /// </summary>
    RightMiddleFinger,

    /// <summary>
    /// 右手无名指（仅指纹）
    /// </summary>
    RightRingFinger,

    /// <summary>
    /// 右手小指（仅指纹）
    /// </summary>
    RightLittleFinger,

    /// <summary>
    /// 左眼（仅虹膜）
    /// </summary>
    LeftIris,

    /// <summary>
    /// 右眼（仅虹膜）
    /// </summary>
    RightIris,
}
