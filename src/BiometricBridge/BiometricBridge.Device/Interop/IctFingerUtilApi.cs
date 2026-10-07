using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace BiometricBridge.Device.Interop;

/// <summary>
/// <c>ICTFingerUtil.dll</c> 的互操作声明。
/// </summary>
/// <remarks>
/// <para>
/// 头文件声明的四个函数与 DLL 导出表完全一致，且均为未修饰的 C 名，
/// 故无需写 <c>EntryPoint</c>。
/// </para>
/// <para>
/// 从 import 库的符号前缀 <c>__imp_</c>（而非 32 位下的 <c>__imp__</c>）可判定
/// 该库为 x64 构建。
/// </para>
/// </remarks>
public static class IctFingerUtilApi
{
    /// <summary>
    /// 本机库名。
    /// </summary>
    private const string Library = "ICTFingerUtil.dll";

    /// <summary>
    /// 检测灰度图四边是否接触到指纹内容，用于判断手指是否超出采集区。
    /// </summary>
    /// <param name="grayImage">
    /// 灰度图数据，长度须为 width × height。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="retTop">
    /// 传出上边界是否有内容：1 有，0 无。
    /// </param>
    /// <param name="retBottom">
    /// 传出下边界是否有内容：1 有，0 无。
    /// </param>
    /// <param name="retLeft">
    /// 传出左边界是否有内容：1 有，0 无。
    /// </param>
    /// <param name="retRight">
    /// 传出右边界是否有内容：1 有，0 无。
    /// </param>
    /// <returns>
    /// 0 表示成功，-1 表示入参非法。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ict_finger_border_det(byte[] grayImage, int width, int height,
        ref int retTop, ref int retBottom, ref int retLeft, ref int retRight);

    /// <summary>
    /// 按块统计灰度图的归一化信息。
    /// </summary>
    /// <param name="img">
    /// 输入灰度图数据。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="blockSize">
    /// 分块窗口尺寸。
    /// </param>
    /// <param name="stride">
    /// 分块滑动步长。
    /// </param>
    /// <param name="blockCounts">
    /// 传出采样到的有效块数。
    /// </param>
    /// <param name="blockSums">
    /// 传出归一化得分汇总。
    /// </param>
    /// <param name="lowBlockCounts">
    /// 传出低于 <paramref name="lowBlockThresh"/> 的块数。
    /// </param>
    /// <param name="lowBlockThresh">
    /// 低块判定阈值。
    /// </param>
    /// <returns>
    /// 0 表示成功，-1 表示图像或分块几何参数非法。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int get_block_norm_sum(byte[] img, int width, int height, int blockSize,
        int stride, ref int blockCounts, ref float blockSums, ref int lowBlockCounts,
        float lowBlockThresh);

    /// <summary>
    /// 清理分割后图像。
    /// </summary>
    /// <param name="cleanImage">
    /// 待清理的图像数据，原地修改。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <returns>
    /// 0 表示成功，-1 表示入参非法。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ict_clean_segimage(byte[] cleanImage, int width, int height);

    /// <summary>
    /// 计算单指图像质量分（NFIQ 2.0）。
    /// </summary>
    /// <param name="grayImage">
    /// 单指灰度图数据，长度须为 width × height。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="blockSize">
    /// 分块窗口尺寸。
    /// </param>
    /// <param name="stride">
    /// 分块滑动步长。
    /// </param>
    /// <param name="lowBlockThresh">
    /// 低块判定阈值。
    /// </param>
    /// <param name="lowBlockRatio">
    /// 低块占比阈值。
    /// </param>
    /// <param name="qualityScore">
    /// 传出质量分。
    /// </param>
    /// <returns>
    /// 0 表示成功，-1 表示入参非法。
    /// </returns>
    /// <remarks>
    /// 参考实现取 <c>blockSize = 8</c>、<c>stride = 4</c>、
    /// <c>lowBlockThresh = 10.0f</c>、<c>lowBlockRatio = 0.1f</c>。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ict_get_finger_quality(byte[] grayImage, int width, int height,
        int blockSize, int stride, float lowBlockThresh, float lowBlockRatio,
        ref int qualityScore);
}
