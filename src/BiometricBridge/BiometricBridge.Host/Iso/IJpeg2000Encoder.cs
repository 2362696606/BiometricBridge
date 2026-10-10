namespace BiometricBridge.Host.Iso;

/// <summary>
/// 原始灰度图 → JPEG2000 码流
/// </summary>
/// <remarks>
/// <para>
/// 虹膜的 ISO/IEC 19794-6 记录要求图像格式为 <c>MONO_JPEG2000</c>，且注册用途还要求无损
/// —— 详见 <see cref="Iso19794IrisImage"/>。设备只给原始灰度图，中间这一步得有人做。
/// </para>
/// <para>
/// <b>抽象留在这里，实现另立平台项目</b>：可用的编码器都要带平台原生库，而本层（以及它脚下的
/// Android/Browser 头）的边界是"不引平台原生包"。组合根负责把实现注进来；换实现不动本层。
/// </para>
/// </remarks>
public interface IJpeg2000Encoder
{
    /// <summary>
    /// 把一张单通道灰度图编成 JPEG2000 码流
    /// </summary>
    /// <param name="gray8">
    /// 灰度像素，逐行排列、每像素 1 字节，长度须等于 <paramref name="width"/> × <paramref name="height"/>。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <returns>
    /// JPEG2000 码流。
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="gray8"/> 为 null。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 宽或高小于 1。
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 像素数与宽高不符 —— 说明调用方手里的几何信息与图像数据并非同源。
    /// </exception>
    /// <exception cref="Jpeg2000EncodingException">
    /// 编码器自身失败。
    /// </exception>
    /// <remarks>
    /// <b>实现必须产出无损码流</b>：CTK 除了看记录头里声明的压缩类型，还会真的解码一次并核对
    /// "解码器是否判为无损"，有损码流过不了。
    /// </remarks>
    byte[] Encode(byte[] gray8, int width, int height);
}
