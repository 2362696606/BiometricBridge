using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BiometricBridge.Core;
using BiometricBridge.Host;
using BiometricBridge.Host.Common;
using BiometricBridge.Host.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host.Http.Controllers;

/// <summary>
/// SBI 预览推流。注册客户端（如 MOSIP 注册客户端 / CTK）用它看实时画面（辅助采集）
/// </summary>
/// <remarks>
/// <para>
/// 路由写死为规范的 <c>/stream</c>；跨域与预检由 <see cref="SbiCorsMiddleware"/> 统一处理，本类不管。
/// 推流只对注册用途设备提供（"stream APIs are available only for the registration environment"），
/// 本服务的设备都是注册用途，故不做额外判定。
/// </para>
/// <para>
/// <b>成功路径不是 JSON，而是 MJPEG 的帧序列</b>：响应头一旦按流的方式发出，就没有改回 JSON 报文的位置，
/// 故所有校验（设备在不在、连没连）都必须在写响应头<b>之前</b>完成 —— 见 <see cref="StreamResponse"/>。
/// </para>
/// <para>
/// 帧的编码（灰度 → JPEG）归传输层：设备给的是原始灰度图，而线格式要求 M-JPEG，这层差异在此抹平。
/// </para>
/// </remarks>
[ApiController]
[Route("stream")]
public sealed class StreamController(
    PreviewStreamService stream,
    IDevicePreview preview,
    ILogger<StreamController> logger) : ControllerBase
{
    #region 常量

    /// <summary>
    /// multipart 的分界串
    /// </summary>
    /// <remarks>
    /// 报文里写成 <c>--{Boundary}</c>；响应头的 <c>Content-Type</c> 只写不带前缀的这一串。
    /// </remarks>
    private const string Boundary = "mosip-sbi-mjpeg-boundary";

    /// <summary>
    /// 请求未给超时时用的默认值（毫秒）：规范定为 5 分钟
    /// </summary>
    private const int DefaultTimeoutMs = 300_000;

    /// <summary>
    /// JPEG 质量（0–100）
    /// </summary>
    private const int JpegQuality = 80;

    /// <summary>
    /// 帧间隔分隔符
    /// </summary>
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    #endregion

    /// <summary>
    /// 推流
    /// </summary>
    /// <param name="cancellationToken">
    /// 请求中止令牌（由框架注入 <c>RequestAborted</c>）
    /// </param>
    /// <returns>
    /// 成功时为空的动作结果（帧已直接写入响应体）；失败时为 JSON 错误报文
    /// </returns>
    /// <remarks>
    /// <b>不用 <c>[FromBody]</c> 绑定</b>：推流请求体是可缺省的，而 CTK 实测发 <c>/stream</c> 时不带
    /// <c>application/json</c>（乃至无体），走 <c>[FromBody]</c> 会被框架判 <b>415</b>。故手工宽容解析
    /// —— 空体、非 JSON、缺 Content-Type 一律当缺省参数，见 <see cref="ReadRequestAsync"/>。
    /// </remarks>
    [HttpPost]
    [AcceptVerbs("STREAM")]
    public async Task<IActionResult> Stream(CancellationToken cancellationToken)
    {
        // 规范要求推流响应不可缓存。
        Response.Headers.CacheControl = "no-store";

        var request = await ReadRequestAsync(cancellationToken).ConfigureAwait(false);

        // —— 校验一律在写响应头之前走完（见类型说明）——
        var device = stream.ResolveDevice(request?.DeviceId, request?.DeviceSubId);
        if (device is null || !device.IsConnected)
        {
            return Ok(new StreamResponse { Error = SbiErrorHelper.NoDeviceConnected });
        }

        // 只留最新一帧、丢旧帧：推流要的是"现在"，堆放旧帧只会让画面越来越滞后。
        var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        try
        {
            await preview.StartPreviewAsync(device.DeviceId, frame => Publish(frames, frame), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 还没写过任何响应内容，仍能按 JSON 报错。
            logger.LogWarning(exception, "启动推流失败：设备 {DeviceId}。", device.DeviceId);
            return Ok(new StreamResponse { Error = SbiErrorHelper.DeviceNotReady });
        }

        var timeout = request?.Timeout is { } requested and > 0 ? requested : DefaultTimeoutMs;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);

        try
        {
            Response.ContentType = $"multipart/x-mixed-replace; boundary={Boundary}";
            await Response.StartAsync(lifetime.Token).ConfigureAwait(false);

            await foreach (var jpeg in frames.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                await WritePartAsync(jpeg, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端断开或到时：都是正常的收尾路径。
        }
        finally
        {
            frames.Writer.TryComplete();

            // 预览期间设备被占用，必须收掉，否则设备一直停在预览态，接不了连接/断开/采集。
            await preview.StopPreviewAsync(device.DeviceId).ConfigureAwait(false);
        }

        return new EmptyResult();
    }

    /// <summary>
    /// 宽容地读推流请求体
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 解析出的请求；空体、非 JSON 或解析失败时为 null（调用方按缺省参数处理）
    /// </returns>
    /// <remarks>
    /// 不看 <c>Content-Type</c>，直接按 JSON 试解；解不出就退回 null。注册采集请求体是可选参数，
    /// 为此把一个 415 抛给客户端，不如"读不出就当没给"。
    /// </remarks>
    private async Task<StreamRequest?> ReadRequestAsync(CancellationToken cancellationToken)
    {
        // 有长度且为 0：确实没有体。长度为 null（分块传输）时仍试读一把，读不出自然回 null。
        if (Request.ContentLength == 0)
        {
            return null;
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<StreamRequest>(Request.Body, SbiJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 收下一帧、编码并投进队列
    /// </summary>
    /// <param name="frames">
    /// 帧队列
    /// </param>
    /// <param name="frame">
    /// 预览帧
    /// </param>
    /// <remarks>
    /// 本方法跑在设备线程的栈帧里（见 <see cref="PreviewFrameSink"/>），<b>不得抛异常</b>，
    /// 故编码失败就地吞掉并记一条，绝不让异常穿过托管边界。
    /// </remarks>
    private void Publish(ChannelWriter<byte[]> frames, CoreModels.PreviewFrame frame)
    {
        try
        {
            frames.TryWrite(EncodeJpeg(frame));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "预览帧编码失败，丢弃该帧。");
        }
    }

    /// <summary>
    /// 把一个 MJPEG 分片写入响应体
    /// </summary>
    /// <param name="jpeg">
    /// 已编码的 JPEG 帧
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    private async Task WritePartAsync(byte[] jpeg, CancellationToken cancellationToken)
    {
        var header = Encoding.ASCII.GetBytes(
            $"--{Boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");

        var body = Response.Body;
        await body.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await body.WriteAsync(jpeg, cancellationToken).ConfigureAwait(false);
        await body.WriteAsync(Crlf, cancellationToken).ConfigureAwait(false);
        await body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把 8 位灰度帧编成 JPEG
    /// </summary>
    /// <param name="frame">
    /// 预览帧
    /// </param>
    /// <returns>
    /// JPEG 字节
    /// </returns>
    /// <remarks>
    /// 显式把灰度展开成 <c>Bgra8888</c> 再编码，而不是拿灰度源直接编：Skia 的 JPEG 编码器对灰度色彩类型的
    /// 支持各版本/后端不一，直编可能返回空数据。三通道取值相同，故 <c>Bgra</c> 与 <c>Rgba</c> 的字节序在这个
    /// 场景下无所谓。
    /// </remarks>
    private static byte[] EncodeJpeg(CoreModels.PreviewFrame frame)
    {
        var pixelCount = Math.Min(frame.Data.Length, frame.Width * frame.Height);
        var bgra = new byte[frame.Width * frame.Height * 4];
        var gray = frame.Data;
        for (var i = 0; i < pixelCount; i++)
        {
            var value = gray[i];
            var offset = i * 4;
            bgra[offset] = value;
            bgra[offset + 1] = value;
            bgra[offset + 2] = value;
            bgra[offset + 3] = 0xFF;
        }

        using var bitmap = new SKBitmap(new SKImageInfo(
            frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(bgra, 0, bitmap.GetPixels(), bgra.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
            ?? throw new InvalidOperationException("JPEG 编码返回空数据。");

        return encoded.ToArray();
    }
}
