namespace BiometricBridge.Host.Http;

/// <summary>
/// HTTP 宿主的监听设置。
/// </summary>
/// <remarks>
/// <para>
/// 对外主要提供 SBI 服务，故监听地址固定为本地回环 <c>127.0.0.1</c>，端口限定在 SBI 约定的一段内。
/// </para>
/// <para>
/// 端口是宿主的启动参数，本属 HTTP 自己的事，故不放进 <c>IExternalServiceHost</c> 契约 ——
/// 换一种服务方式（gRPC、命名管道……）时另配一份设置，契约不动。宿主在每次启动时读本对象，
/// 故改端口后停一次再启即生效。
/// </para>
/// <para>
/// 只是一个可变的设置载体，<b>不实现 INotifyPropertyChanged</b>：通知归界面侧（控制面板的设置
/// 视图模型），本类只管被读、被写。
/// </para>
/// </remarks>
public sealed class HttpHostSettings
{
    /// <summary>
    /// 监听地址前缀，仅回环：本机调用方可达，外部机器不可达。
    /// </summary>
    /// <remarks>
    /// 写 <c>127.0.0.1</c> 而非 <c>localhost</c>，避开双栈解析差异。
    /// </remarks>
    private const string ListenAddress = "http://127.0.0.1";

    /// <summary>
    /// 允许的端口下限。
    /// </summary>
    public const int MinPort = 4501;

    /// <summary>
    /// 允许的端口上限。
    /// </summary>
    public const int MaxPort = 4600;

    /// <summary>
    /// 默认监听端口。
    /// </summary>
    public const int DefaultPort = MinPort;

    /// <summary>
    /// 监听端口。宿主启动时读取；界面只写入 <see cref="MinPort"/>–<see cref="MaxPort"/> 之间的值。
    /// </summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// 监听根地址，形如 <c>http://127.0.0.1:4501</c>（不含结尾斜杠）。
    /// </summary>
    /// <remarks>
    /// 宿主拿它建监听，控制器拿它拼响应里的回呼地址 —— 一处定义、两边一致：改绑定地址时回呼地址自动跟着改，
    /// 不会一边听着这个地址、一边把客户端指向别处。
    /// </remarks>
    public string BaseUrl => $"{ListenAddress}:{Port}";
}
