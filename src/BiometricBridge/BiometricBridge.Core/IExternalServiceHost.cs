namespace BiometricBridge.Core;

/// <summary>
/// 对外服务的宿主。只约定"把对外服务跑起来、停下来"这一件事，不涉及服务的具体方式
/// </summary>
/// <remarks>
/// <para>
/// 对外服务的方式有多种（HTTP、gRPC、命名管道……），实现可整体替换，上层只依赖本接口。
/// 故这里刻意只放启动与停止：运行状态、监听地址、鉴权都是实现自己的事，不上浮到契约。
/// </para>
/// <para>
/// 两个方法都必须可重复调用：已启动再启、未启动就停，都应安静地什么都不做。
/// </para>
/// </remarks>
public interface IExternalServiceHost
{
    /// <summary>
    /// 启动对外服务
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 启动完成的任务
    /// </returns>
    /// <exception cref="IOException">
    /// 监听地址已被占用等，起不来。失败时服务保持未启动状态
    /// </exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止对外服务
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 停止完成的任务
    /// </returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
