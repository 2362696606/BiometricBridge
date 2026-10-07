using System.Runtime.InteropServices;

namespace BiometricBridge.Device.Interop;

/// <summary>
/// 对应原生库的 <c>ICTSCAN_DEVICE_INFO</c> 结构。
/// </summary>
/// <remarks>
/// <para>
/// 原生头文件里该结构被包在 <c>#pragma pack(push, 1)</c> 中，但那个块的守卫是
/// <c>#if defined(__WIN32__)</c>，而 MSVC 并不定义该宏（它定义的是 <c>_WIN32</c>），
/// 故实际编译出来的结构按默认对齐。所幸四个 <c>int</c> 加四个 <c>char</c> 共 20 字节，
/// 两种对齐下布局相同，不存在歧义。
/// </para>
/// <para>
/// 固件版本在原生结构里是 <c>char firmwareVerion[4]</c>（原头文件拼写如此）。此处拆成四个
/// <see cref="byte"/> 字段而非用 <c>ByValArray</c>，是为了避开数组字段在互操作时的
/// 初始化要求，行为完全等价。
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct IctScanDeviceInfo
{
    /// <summary>
    /// 传感器宽度（像素）。
    /// </summary>
    internal int Width;

    /// <summary>
    /// 传感器高度（像素）。
    /// </summary>
    internal int Height;

    /// <summary>
    /// 处理宽度（像素）。
    /// </summary>
    internal int ProcWidth;

    /// <summary>
    /// 处理高度（像素）。
    /// </summary>
    internal int ProcHeight;

    /// <summary>
    /// 固件版本第 1 字节。
    /// </summary>
    internal byte Firmware0;

    /// <summary>
    /// 固件版本第 2 字节。
    /// </summary>
    internal byte Firmware1;

    /// <summary>
    /// 固件版本第 3 字节。
    /// </summary>
    internal byte Firmware2;

    /// <summary>
    /// 固件版本第 4 字节。
    /// </summary>
    internal byte Firmware3;
}
