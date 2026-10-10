using BiometricBridge.Host.Iso;
using ImageMagick;

namespace BiometricBridge.Host.Http.Iso;

/// <summary>
/// 基于 Magick.NET 的 <see cref="IJpeg2000Encoder"/>：单通道灰度图 → 无损 JPEG2000（JP2 容器）
/// </summary>
/// <remarks>
/// <para>
/// 放在 <c>Host.Http</c> 而非 <c>Host</c>：Magick.NET 随包分发 win/linux/osx 原生库，而
/// <c>Host</c> 与它脚下的 Android/Browser 头的边界是"不引平台原生包" —— 与本项目当初把 Kestrel
/// 放进 <c>Host.Http</c> 同一理由。组合根负责把本实现注入业务层。
/// </para>
/// <para>
/// <b>一处 quality 都不设</b>：Magick.NET 的 JP2 写出默认即为无损（可逆 5-3 变换 + 无量化，
/// 且像素能原样还原）。反过来，显式设 quality 是个陷阱 —— <c>image.Quality = 0</c> 会产出一条
/// <b>标记上仍称可逆、质量层却被截断</b>的码流，而 CTK 判"是否无损"恰恰只看那一个变换字节，
/// 于是有损的码流能一路骗过校验。故这里不碰 quality，并由单测把"可逆 + 像素完全回环"两点钉住。
/// </para>
/// <para>
/// 同理<b>不做运行期码流自检</b>：值得检的是"像素真的无损"（要完整解码，代价高），而廉价的标记检查
/// 已被上面那个例子证伪 —— 标记为真、像素为假。做了只会给出虚假的安心。
/// </para>
/// </remarks>
public sealed class MagickJpeg2000Encoder : IJpeg2000Encoder
{
    /// <summary>
    /// 单通道灰度图的像素映射串
    /// </summary>
    /// <remarks>
    /// ImageMagick 的映射串里 <c>I</c> 表示"强度"单通道，经它导入的图是 1 分量灰度
    /// （JP2 里 <c>Csiz=1</c>、颜色空间枚举 = 17 灰度）—— 这正是 CTK 要求的 GRAY[8 bit]。
    /// </remarks>
    private const string IntensityPixelMapping = "I";

    /// <inheritdoc/>
    public byte[] Encode(byte[] gray8, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gray8);

        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        if (gray8.Length != (long)width * height)
        {
            // 宽高与像素数不一致，说明调用方手里的几何信息与图像数据并非同源（例如拿错了帧的尺寸）。
            // 此时编出来的图会错位或被截断，且不失一般地看起来"编码成功"。
            throw new ArgumentException(
                $"像素数据长度 {gray8.Length} 与 {width}×{height} 不符。", nameof(gray8));
        }

        try
        {
            var readSettings = new PixelReadSettings((uint)width, (uint)height, StorageType.Char, IntensityPixelMapping);

            // 格式属性在 using 建立之后再单独赋值，不写成对象初始化器：初始化器抛出时 using 尚未接管，
            // 那个已构造出来的图反而不会被处置。
            using var image = new MagickImage(gray8, readSettings);
            image.Format = MagickFormat.Jp2;

            using var buffer = new MemoryStream();
            image.Write(buffer);
            return buffer.ToArray();
        }
        catch (Exception exception)
        {
            // 不静默降级为有损 JPEG —— 那会让 bioValue 声称无损而实为有损，肉眼与记录头都看不出来。
            throw new Jpeg2000EncodingException(
                $"JPEG2000 编码失败（{width}×{height}）：{exception.Message}", exception);
        }
    }
}
