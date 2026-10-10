namespace BiometricBridge.Core.Models.Enums;

/// <summary>
/// 采集分组：这一次采的是"哪一组"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="BiometricPosition"/> 是两个维度：位置是"逐枚具名部位"，分组是"哪一侧、哪一组"。
/// 指纹的左四指／右四指／双拇指、虹膜的左眼／右眼／双眼，都是它。
/// </para>
/// <para>
/// 与 SBI 的 <c>deviceSubId</c>（1 左 / 2 右 / 3 双 / 0 未知）说的是同一件事，但本枚举用设备层
/// 自有命名、不照搬协议编号，映射由上层适配器负责（见 <c>Host/SbiBioSubTypes.cs</c>）——
/// 与 <see cref="BiometricPosition"/> 对协议字面量的态度一致。
/// </para>
/// </remarks>
public enum CaptureGroup
{
    /// <summary>
    /// 不指定分组，由设备自行选择
    /// </summary>
    Any,

    /// <summary>
    /// 左侧：指纹的左四指联采、虹膜的左眼
    /// </summary>
    Left,

    /// <summary>
    /// 右侧：指纹的右四指联采、虹膜的右眼
    /// </summary>
    Right,

    /// <summary>
    /// 双侧：指纹的双拇指、虹膜的双眼
    /// </summary>
    Both,
}
