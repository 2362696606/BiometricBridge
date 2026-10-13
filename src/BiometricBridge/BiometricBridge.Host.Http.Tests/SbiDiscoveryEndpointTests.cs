using System.Net;
using System.Net.Sockets;
using System.Text;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Host;
using BiometricBridge.Host.Http.Controllers;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BiometricBridge.Host.Http.Tests;

/// <summary>
/// <c>/device</c> 的请求体绑定：声明了什么媒体类型都得能拿到设备清单
/// </summary>
/// <remarks>
/// <para>
/// 守的是真机上栽过的那一跤：客户端发发现请求时声明
/// <c>Content-Type: content-type; charset=UTF-8</c>（连合法的 <c>type/subtype</c> 都不是），
/// MVC 默认会在<b>模型绑定阶段</b>回 415，动作方法一行不跑，客户端因此拿不到设备 —— 而规范的发现请求
/// 根本没有 <c>Content-Type</c> 这一项（只列了 <c>HOST</c> 与 <c>EXT</c>）。
/// </para>
/// <para>
/// <b>为什么要起真宿主</b>：这条行为是 MVC 与格式化器<b>装配</b>出来的结果，不是某个类的行为 ——
/// 用假 MVC 或直接调控制器都验证不到"装配漏了"这件事。故这里经由 <see cref="SbiHttpSetup"/>
/// 装真 Kestrel、走真 HTTP，与宿主启动时是同一份装配。
/// </para>
/// </remarks>
[TestSubject(typeof(DeviceController))]
public sealed class SbiDiscoveryEndpointTests
{
    /// <summary>共享的长生命周期 <see cref="HttpClient"/>（避免短生命周期实例导致的套接字耗尽问题）。</summary>
    private static readonly HttpClient Http = new();

    /// <summary>发现请求体：Iris，正常大小写。</summary>
    private const string IrisRequest = """{"type":"Iris"}""";

    /// <summary>发现请求体：Iris 全大写（CTK 的 SBI1196 负向用例）。</summary>
    private const string UpperCaseIrisRequest = """{"type":"IRIS"}""";

    /// <summary>
    /// 客户端声明了一个框架认不出的媒体类型：仍须回设备清单，而不是 415
    /// </summary>
    [Fact]
    public async Task 声明非法的媒体类型也照常发现设备()
    {
        await using var host = await TestHost.StartAsync();

        using HttpResponseMessage response = await SendAsync(
            host, "MOSIPDISC", "content-type; charset=UTF-8", IrisRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 清单是空的（本测试不关心设备字段），但"进得去、回得来"正是要守的：
        // 之前这里拿到的是 415 + ProblemDetails，动作方法根本没跑。
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 客户端声明了框架认得出但不作 JSON 看的媒体类型：同样照常发现
    /// </summary>
    [Fact]
    public async Task 声明纯文本媒体类型也照常发现设备()
    {
        await using var host = await TestHost.StartAsync();

        using HttpResponseMessage response = await SendAsync(host, "MOSIPDISC", "text/plain", IrisRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// <c>application/json</c> 与不带媒体类型的老路径都不能被改动碰到
    /// </summary>
    /// <param name="contentType">
    /// 声明的媒体类型；null 表示不带该头
    /// </param>
    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task 常规媒体类型照常发现设备(string? contentType)
    {
        await using var host = await TestHost.StartAsync();

        using HttpResponseMessage response = await SendAsync(host, "MOSIPDISC", contentType, IrisRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 标准 <c>POST</c> 与自定义动词进的是同一个动作
    /// </summary>
    [Fact]
    public async Task Post动词照常发现设备()
    {
        await using var host = await TestHost.StartAsync();

        using HttpResponseMessage response = await SendAsync(host, "POST", "application/json", IrisRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 宽容绑定不等于放过协议层校验：非法取值仍走"200 + 错误态元素"
    /// </summary>
    /// <remarks>
    /// 框架那道 415 拆掉之后请求会真的走进上层 —— 若上层的判定跟着一起松掉，CTK 的 SBI1196
    /// （大写取值必须回错误态）就会失守。故这条钉住"拆闸没有拆到协议语义"。
    /// </remarks>
    [Fact]
    public async Task 非法类型仍回错误态而非四百()
    {
        await using var host = await TestHost.StartAsync();

        using HttpResponseMessage response = await SendAsync(
            host, "MOSIPDISC", "application/json", UpperCaseIrisRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "\"errorCode\":\"504\"",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 每一次通信都要留下可溯源的一条：调用方、动词路径、状态、请求体都在里面
    /// </summary>
    [Fact]
    public async Task 报文日志记下调用方与收发内容()
    {
        await using var host = await TestHost.StartAsync();

        using HttpRequestMessage request = new(new HttpMethod("MOSIPDISC"), $"{host.BaseUrl}/device")
        {
            Content = Body("application/json", IrisRequest),
        };

        // EXT 是非浏览器客户端自报家门的头（浏览器用 Origin）：溯源靠它认出"是谁"。
        request.Headers.TryAddWithoutValidation("EXT", "BiometricBridge.Tests");

        using HttpResponseMessage response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // 日志记在响应写出之后的收尾里，客户端读到响应时它可能还没落 —— 故等它出现，而不是立刻断言。
        var entry = await host.WaitForTrafficLogAsync(
            static message => message.Contains("/device", StringComparison.Ordinal));

        Assert.Contains("MOSIPDISC", entry, StringComparison.Ordinal);
        Assert.Contains("200", entry, StringComparison.Ordinal);
        Assert.Contains("EXT=BiometricBridge.Tests", entry, StringComparison.Ordinal);

        // 请求体与响应体都要留：响应体那行是空清单的 "[]"，若框架绕开了包装流直接写 Kestrel，
        // 这里就只剩一个空响应 —— 故这条同时钉住"响应体确实经过了记录链路"。
        Assert.Contains(IrisRequest, entry, StringComparison.Ordinal);
        Assert.Contains("\n\t[]", entry, StringComparison.Ordinal);
    }

    /// <summary>
    /// 发一次发现请求
    /// </summary>
    /// <param name="host">
    /// 测试宿主
    /// </param>
    /// <param name="verb">
    /// HTTP 动词
    /// </param>
    /// <param name="contentType">
    /// 声明的媒体类型；null 表示不带该头
    /// </param>
    /// <param name="body">
    /// 请求体
    /// </param>
    /// <returns>
    /// 响应
    /// </returns>
    private static async Task<HttpResponseMessage> SendAsync(
        TestHost host,
        string verb,
        string? contentType,
        string body)
    {
        using HttpRequestMessage request = new(new HttpMethod(verb), $"{host.BaseUrl}/device")
        {
            Content = Body(contentType, body),
        };

        return await Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// 造请求体
    /// </summary>
    /// <param name="contentType">
    /// 声明的媒体类型；null 表示不带该头
    /// </param>
    /// <param name="body">
    /// 报文
    /// </param>
    /// <returns>
    /// 请求体
    /// </returns>
    /// <remarks>
    /// 用 <see cref="ByteArrayContent"/> 而非 <c>StringContent</c>：后者自带
    /// <c>text/plain; charset=utf-8</c>，而且要写出<b>非法</b>的媒体类型必须绕过校验
    /// （<c>TryAddWithoutValidation</c>）。
    /// </remarks>
    private static ByteArrayContent Body(string? contentType, string body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));

        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return content;
    }

    /// <summary>
    /// 取一个空闲端口。测试不占用 SBI 约定的 4501–4600 段，免得与真在跑的服务撞上。
    /// </summary>
    /// <returns>
    /// 端口号
    /// </returns>
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    /// <summary>
    /// 测试宿主：按宿主的装配起一个真 Kestrel
    /// </summary>
    /// <remarks>
    /// 装配走 <see cref="SbiHttpSetup"/>，与 <see cref="HttpServiceHost"/> 启动时是同一份；
    /// 差别只在依赖：设备清单换成空的、报文日志的出口换成本测试的收集器。
    /// </remarks>
    private sealed class TestHost(WebApplication app, HttpHostSettings settings, CapturingLogger log) : IAsyncDisposable
    {
        /// <summary>
        /// 监听根地址
        /// </summary>
        public string BaseUrl => settings.BaseUrl;

        /// <summary>
        /// 起一个测试宿主
        /// </summary>
        /// <returns>
        /// 已开始监听的宿主
        /// </returns>
        public static async Task<TestHost> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production,
            });

            builder.Logging.ClearProviders();

            var settings = new HttpHostSettings { Port = FreePort() };
            builder.WebHost.UseUrls(settings.BaseUrl);

            var log = new CapturingLogger();

            // 空清单足够：本测试守的是"请求进得去、回得来"，设备字段的形状不在这里验。
            builder.Services.AddSingleton<IDeviceInventory, EmptyInventory>();
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton<DeviceIdentityService>();
            builder.Services.AddSingleton<DeviceDiscoveryService>();
            builder.Services.AddSingleton<ILogger<SbiTrafficLogMiddleware>>(log);
            builder.Services.AddSbiControllers();

            var app = builder.Build();
            app.UseSbiPipeline();
            await app.StartAsync(TestContext.Current.CancellationToken);

            return new TestHost(app, settings, log);
        }

        /// <summary>
        /// 等一条满足条件的报文日志出现
        /// </summary>
        /// <param name="predicate">
        /// 匹配条件
        /// </param>
        /// <returns>
        /// 匹配到的日志原文
        /// </returns>
        public async Task<string> WaitForTrafficLogAsync(Func<string, bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                var match = log.Messages.FirstOrDefault(predicate);
                if (match is not null)
                {
                    return match;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Fail($"等不到匹配的报文日志。已收到：\n{string.Join("\n", log.Messages)}");
            return string.Empty;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// 空设备清单
    /// </summary>
    private sealed class EmptyInventory : IDeviceInventory
    {
        /// <inheritdoc/>
        public IReadOnlyList<DeviceSnapshot> GetDeviceSnapshots() => [];
    }

    /// <summary>
    /// 收集日志原文：用来断言"溯源信息真的记下来了"
    /// </summary>
    private sealed class CapturingLogger : ILogger<SbiTrafficLogMiddleware>
    {
        private readonly List<string> _messages = [];

        /// <summary>
        /// 已收到的日志
        /// </summary>
        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
