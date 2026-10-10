namespace BiometricBridge.Device;

/// <summary>
/// 分割器判出的手别。
/// </summary>
/// <remarks>
/// 取值即原生出参的取值（<c>1</c> 左、<c>2</c> 右、<c>0</c> 判不出），另立一套编号只会多一层换算。
/// </remarks>
public enum FingerHand
{
    /// <summary>
    /// 判不出左右手
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 左手
    /// </summary>
    Left = 1,

    /// <summary>
    /// 右手
    /// </summary>
    Right = 2,
}
