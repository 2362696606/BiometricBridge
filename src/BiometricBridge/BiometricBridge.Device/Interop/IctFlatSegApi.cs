using System.Runtime.InteropServices;
// ReSharper disable InconsistentNaming

namespace BiometricBridge.Device.Interop;

/// <summary>
/// <c>ICTFlatSegDll.dll</c> 的互操作声明，负责多指图像分割。
/// </summary>
/// <remarks>
/// <para>
/// 头文件声明的两个函数与 DLL 导出表完全一致，且均为未修饰的 C 名，
/// 故无需写 <c>EntryPoint</c>。
/// </para>
/// <para>
/// 本库只处理已采集到的图像数据，不接触设备句柄。
/// </para>
/// </remarks>
public static class IctFlatSegApi
{
    /// <summary>
    /// 本机库名。
    /// </summary>
    private const string Library = "ICTFlatSegDll.dll";

    /// <summary>
    /// 把含多指的图像分割成单指图像。
    /// </summary>
    /// <param name="image">
    /// 输入的多指原始灰度图数据。
    /// </param>
    /// <param name="imgWidth">
    /// 输入图像宽度。
    /// </param>
    /// <param name="imgHeight">
    /// 输入图像高度。
    /// </param>
    /// <param name="fingerNum">
    /// 输入图像中包含的手指数量（期望值）。
    /// </param>
    /// <param name="leftOrRightHand">
    /// 传出左右手判定：1 左手，2 右手，0 判定失败。
    /// </param>
    /// <param name="segFingerNum">
    /// 传出实际分割出的指纹个数。
    /// </param>
    /// <param name="fingerInfo">
    /// 传出每个单指的信息，每根占 4 个 <see cref="int"/>（中心点 X、中心点 Y、
    /// 方向、类型），故容量须为 <paramref name="fingerNum"/> × 4。
    /// </param>
    /// <param name="dstImage">
    /// 接收分割结果的缓冲区，<b>由调用方分配</b>，本函数不分配内存。
    /// </param>
    /// <returns>
    /// 0 表示成功；-1 分配内存失败；-999 <paramref name="dstImage"/> 为空。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b><paramref name="dstImage"/> 的容量存在争议，分配前必须实测确认。</b>
    /// 头文件注明为 <c>segFingerNum * 640 * 640</c> 字节；而参考实现按单指
    /// 300×400 分配（4 指共 480000 字节）。两者相差约 3.4 倍 ——
    /// 若头注释属实，按 300×400 分配会写越界。
    /// </para>
    /// <para>
    /// 参考实现的调用参数为 <c>fingerNum = 4</c>，
    /// 并在图像宽度 ≥ 800 时才调用本函数。
    /// </para>
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ICTFlatFingerSegmentDLL(byte[] image, int imgWidth, int imgHeight,
        int fingerNum, ref int leftOrRightHand, ref int segFingerNum, int[] fingerInfo,
        byte[] dstImage);

    /// <summary>
    /// 对单指图像做一次分割，输出有效区域。
    /// </summary>
    /// <param name="image">
    /// 输入的单指原始灰度图数据。
    /// </param>
    /// <param name="imgWidth">
    /// 输入图像宽度。
    /// </param>
    /// <param name="imgHeight">
    /// 输入图像高度。
    /// </param>
    /// <param name="dstImage">
    /// 接收分割结果的缓冲区，<b>由调用方分配</b>，本函数不分配内存。
    /// </param>
    /// <param name="dstWidth">
    /// 传出分割后图像的宽度。
    /// </param>
    /// <param name="dstHeight">
    /// 传出分割后图像的高度。
    /// </param>
    /// <param name="fingerAreaPer">
    /// 传出指纹面积占整幅图像的百分比。
    /// </param>
    /// <returns>
    /// 0 表示成功；-999 表示分割到的指纹数不等于 1。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ICTFlatSingleFingerSegmentDLL(byte[] image, int imgWidth, int imgHeight,
        byte[] dstImage, ref int dstWidth, ref int dstHeight, ref int fingerAreaPer);
}
