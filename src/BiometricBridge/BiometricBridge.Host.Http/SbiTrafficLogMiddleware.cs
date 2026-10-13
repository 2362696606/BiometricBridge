using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BiometricBridge.Host.Http;

/// <summary>
/// SBI 报文日志：每一次对外通信完整记一条，供事后溯源
/// </summary>
/// <remarks>
/// <para>
/// 一条里带上"谁调的、请求了什么、回了什么"：调用方（<c>Origin</c>／<c>EXT</c>／<c>User-Agent</c>，以及来源地址
/// 与框架的 <c>TraceIdentifier</c>）、请求行（动词、路径、媒体类型、字节数）、请求体、响应状态、响应体与耗时。
/// </para>
/// <para>
/// <b>为什么记在中间件而不是控制器里</b>：这样连"没走到控制器"的通信也留痕 —— 真机上那场 415 正是死在
/// 模型绑定阶段（动作方法一行没跑），若只在控制器里记，最需要的那一条恰恰没有。故它挂在最外层，
/// 包住跨域中间件与路由，预检也一并留痕。
/// </para>
/// <para>
/// <b>两侧的报文都不整体缓冲</b>：请求体先开框架缓冲再读、读完拨回开头，模型绑定照旧拿得到它；响应体走
/// "边写边留"（<see cref="CappedTeeStream"/>），写透到真实响应流的同时留一份 —— 推流是无限长的 M-JPEG，
/// 整体缓冲既会把它全吃进内存，也会让日志等到流结束才出现。
/// </para>
/// <para>
/// <b>报文体的保留有上限</b>（<see cref="BodyLimitBytes"/>）：采集回执里是图像与 ISO 记录，单条可达数百 KB，
/// 而日志同时进文件 sink 与界面日志视图（视图内存里留 2000 条）。超限的部分不进日志，但<b>总长照记</b>，
/// 故"这一条到底多大"始终可查。
/// </para>
/// <para>
/// 未捕获异常穿过本中间件时，记下的状态码还是异常抛出前的值（框架随后才把它变成 500），
/// 故另记一个 <c>异常</c> 字段来消歧 —— 有它即表示"这条没走完"。
/// </para>
/// </remarks>
public sealed class SbiTrafficLogMiddleware(RequestDelegate next, ILogger<SbiTrafficLogMiddleware> logger)
{
    /// <summary>
    /// 单侧报文体最多记的字节数
    /// </summary>
    /// <remarks>
    /// 64 KB 足够覆盖发现、信息与采集<b>请求</b>的全部内容，以及采集<b>回执</b>里错误码、分数、长度这些
    /// "用来判断对错"的部分；被截断的只有图像载荷本身。
    /// </remarks>
    public const int BodyLimitBytes = 64 * 1024;

    /// <summary>
    /// 读报文时的块大小
    /// </summary>
    private const int ChunkBytes = 8 * 1024;

    /// <summary>
    /// 处理请求
    /// </summary>
    /// <param name="context">
    /// 请求上下文
    /// </param>
    /// <returns>
    /// 表示处理完成的任务
    /// </returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        // 请求体要能被读两遍（这里一遍、模型绑定一遍）：先开框架缓冲，读完拨回开头。
        request.EnableBuffering();

        string requestBody;
        try
        {
            requestBody = await CaptureAsync(request.Body, BodyLimitBytes, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 客户端在请求体读完前就断了。照常往下走（后续处理自会以同一个令牌收场），日志里如实标注。
            requestBody = "（未读完，客户端已中断）";
        }

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        var originalBody = context.Response.Body;
        var tee = new CappedTeeStream(originalBody, BodyLimitBytes);

        // 换上去而不是缓冲：见类型说明。
        context.Response.Body = tee;

        var startedAt = Stopwatch.GetTimestamp();
        Exception? failure = null;

        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            // 先换回真身再记：异常路径下也得恢复，否则框架收尾时还会往包装流里写。
            context.Response.Body = originalBody;

            logger.LogInformation(
                "SBI 通信：{Method} {Path} → {StatusCode}（{ElapsedMilliseconds} ms）" +
                "\n\t调用方：{Caller}；连接 {ConnectionId}；来源 {RemoteAddress}；追踪 {TraceIdentifier}" +
                "\n\t请求：{RequestContentType}，{RequestBytes} 字节\n\t{RequestBody}" +
                "\n\t响应：{ResponseContentType}，{ResponseBytes} 字节\n\t{ResponseBody}" +
                "\n\t异常：{Failure}",
                request.Method,
                request.Path.Value,
                context.Response.StatusCode,
                Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds),
                DescribeCaller(request),

                // 连接号与框架自己那些 Request starting/finished 行里的 ConnectionId 同源，
                // 据此可以把这条日志与框架的收发记录对起来。
                context.Connection.Id,
                context.Connection.RemoteIpAddress?.ToString() ?? "（未知）",
                context.TraceIdentifier,
                request.ContentType ?? "（无）",
                request.ContentLength ?? -1,
                requestBody,
                context.Response.ContentType ?? "（无）",
                tee.TotalBytes,
                RenderResponseBody(tee, context.Response.ContentType),
                failure is null ? "（无）" : $"{failure.GetType().Name}：{failure.Message}");
        }
    }

    /// <summary>
    /// 读一段流并留下前若干字节
    /// </summary>
    /// <param name="source">
    /// 源流
    /// </param>
    /// <param name="limit">
    /// 最多留多少字节
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 按 UTF-8 呈现的报文；超限时在末尾标出总长
    /// </returns>
    /// <remarks>
    /// 请求体一律按文本呈现：SBI 各请求体都是 JSON 报文，且客户端声明的媒体类型本身就不可信
    /// （正是它把请求判成 415 的那个），照它决定"是不是文本"没有意义。
    /// </remarks>
    private static async Task<string> CaptureAsync(Stream source, int limit, CancellationToken cancellationToken)
    {
        using var kept = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        long totalBytes = 0;

        // 整段读完（模型绑定等着它，缓冲流也要读到末尾），但只留前 limit 字节。
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;

            var room = limit - (int)kept.Length;
            if (room > 0)
            {
                kept.Write(chunk, 0, Math.Min(room, read));
            }
        }

        return RenderText(kept.GetBuffer().AsSpan(0, (int)kept.Length), totalBytes);
    }

    /// <summary>
    /// 把留存的响应字节呈现成日志里的一行
    /// </summary>
    /// <param name="tee">
    /// 边写边留的响应流
    /// </param>
    /// <param name="contentType">
    /// 响应声明的媒体类型
    /// </param>
    /// <returns>
    /// JSON／文本按原文，其余按"二进制 + 字节数"，超限时标出总长
    /// </returns>
    private static string RenderResponseBody(CappedTeeStream tee, string? contentType)
    {
        if (tee.TotalBytes == 0)
        {
            return "（空）";
        }

        // 推流推的是 JPEG 帧：塞进日志既不可读，也会把一行撑得极长，故只报字节数与媒体类型。
        if (!IsTextual(contentType))
        {
            return $"（二进制，{contentType ?? "未声明媒体类型"}，共 {tee.TotalBytes} 字节）";
        }

        return RenderText(tee.Captured(), tee.TotalBytes);
    }

    /// <summary>
    /// 按 UTF-8 呈现字节，超限时标出总长
    /// </summary>
    /// <param name="bytes">
    /// 留存的字节
    /// </param>
    /// <param name="totalBytes">
    /// 实际总字节数
    /// </param>
    /// <returns>
    /// 呈现结果
    /// </returns>
    private static string RenderText(ReadOnlySpan<byte> bytes, long totalBytes)
    {
        var text = Encoding.UTF8.GetString(bytes);

        return bytes.Length < totalBytes
            ? $"{text}…（共 {totalBytes} 字节，此处只留前 {bytes.Length} 字节）"
            : text;
    }

    /// <summary>
    /// 判断响应是不是文本类
    /// </summary>
    /// <param name="contentType">
    /// 响应声明的媒体类型
    /// </param>
    /// <returns>
    /// JSON 或 text/* 时为 true
    /// </returns>
    /// <remarks>
    /// 未声明媒体类型时按文本试：SBI 的 JSON 响应即便漏了 Content-Type 也还是 JSON。
    /// </remarks>
    private static bool IsTextual(string? contentType)
        => contentType is null
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 描述调用方：它是"这条通信是谁发的"的唯一线索
    /// </summary>
    /// <param name="request">
    /// 请求
    /// </param>
    /// <returns>
    /// 三个标识头的取值
    /// </returns>
    /// <remarks>
    /// 浏览器用 <c>Origin</c> 自报家门，非浏览器客户端（MOSIP 的各组件）用 <c>EXT</c>，
    /// 其余靠 <c>User-Agent</c>。三者都可能缺，故逐个如实标注 —— 没有的那次就是"问不出来"的记录。
    /// </remarks>
    private static string DescribeCaller(HttpRequest request)
        => $"Origin={OrNone(request.Headers.Origin.ToString())}｜" +
           $"EXT={OrNone(request.Headers["EXT"].ToString())}｜" +
           $"User-Agent={OrNone(request.Headers.UserAgent.ToString())}";

    /// <summary>
    /// 取个空值可读的呈现
    /// </summary>
    /// <param name="value">
    /// 头的取值
    /// </param>
    /// <returns>
    /// 原值；空时是"（无）"
    /// </returns>
    private static string OrNone(string value) => string.IsNullOrEmpty(value) ? "（无）" : value;

    /// <summary>
    /// 边写边留的响应流
    /// </summary>
    /// <remarks>
    /// 字节照常写透到真实响应流，同时留一份前若干字节供日志使用 —— 因此推流照旧一路推下去，
    /// 不会为了记日志而被缓冲。只为写这一侧存在：框架只往响应体里写，故其余操作一律不支持。
    /// </remarks>
    private sealed class CappedTeeStream(Stream inner, int limit) : Stream
    {
        /// <summary>
        /// 留存的字节
        /// </summary>
        private readonly MemoryStream _kept = new();

        /// <summary>
        /// 已写出的总字节数，含未留存的部分
        /// </summary>
        public long TotalBytes { get; private set; }

        /// <summary>
        /// 取留存的字节
        /// </summary>
        /// <returns>
        /// 留存的字节副本
        /// </returns>
        public byte[] Captured() => _kept.ToArray();

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// 恒抛出：只写流没有长度
        /// </exception>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// 恒抛出：只写流没有位置
        /// </exception>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Keep(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Keep(buffer.AsSpan(offset, count));
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Keep(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        /// <inheritdoc/>
        public override void Flush() => inner.Flush();

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// 恒抛出：只写流不可读
        /// </exception>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// 恒抛出：只写流不可定位
        /// </exception>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">
        /// 恒抛出：只写流没有固定长度
        /// </exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>
        /// 留存本次写入的前若干字节，其余只计数
        /// </summary>
        /// <param name="written">
        /// 本次写出的字节
        /// </param>
        private void Keep(ReadOnlySpan<byte> written)
        {
            TotalBytes += written.Length;

            var room = limit - (int)_kept.Length;
            if (room <= 0)
            {
                return;
            }

            _kept.Write(written[..Math.Min(room, written.Length)]);
        }
    }
}
