using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 设备采集：在指定设备上执行一次采集
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IDeviceInventory"/> 并列的第二道窄门：发现/信息端点只需"看清单"，而采集端点
/// 要"让某台设备采一次"。二者都落在设备抽象层，使宿主层与设备管理者两边只认得接口 ——
/// 宿主不必知道设备从哪儿来，换一套设备管理实现时接口不动。
/// </para>
/// <para>
/// 设备实例本身不流出管理器（见 <see cref="IDeviceInventory"/>），故这里以
/// <paramref name="deviceId"/> 指代设备，与 <see cref="DeviceSnapshot.DeviceId"/> 同一个 id。
/// </para>
/// <para>
/// <b>不做并发仲裁</b>：底层设备的串行化由设备侧的装饰链负责（见 <c>SerializingDeviceDecorator</c>），
/// 本接口只把请求转下去。
/// </para>
/// </remarks>
public interface IDeviceCapture
{
    /// <summary>
    /// 在指定设备上采集生物特征
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id，取自 <see cref="DeviceSnapshot.DeviceId"/>
    /// </param>
    /// <param name="request">
    /// 采集请求
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 采集结果集合。一次调用可能返回多条（如四指联采、双目同时）
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接
    /// </exception>
    Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        Guid deviceId,
        CaptureRequest request,
        CancellationToken cancellationToken = default);
}
