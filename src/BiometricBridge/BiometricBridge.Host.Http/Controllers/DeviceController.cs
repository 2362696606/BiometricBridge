using BiometricBridge.Host.Dto;
using Microsoft.AspNetCore.Mvc;

namespace BiometricBridge.Host.Http.Controllers;

/// <summary>
/// SBI 设备发现。客户端（如 MOSIP 注册客户端 / CTK）用它找本机可用的设备
/// </summary>
/// <remarks>
/// <para>
/// 路由写死为规范的 <c>/device</c>（不带 <c>api/</c> 前缀）：这是对外的契约，拼写与位置都由规范定。
/// 跨域与预检由 <see cref="SbiCorsMiddleware"/> 统一处理，本类不管。
/// </para>
/// <para>
/// 本类只做传输层的事 —— 路由、动词、响应头、把端口拼成回呼地址。设备怎么发现、响应怎么拼装
/// 归 <see cref="DeviceDiscoveryService"/>，故换一种对外服务方式时本类可整体替换。
/// </para>
/// <para>
/// <b>同时接受 <c>POST</c> 与自定义方法 <c>MOSIPDISC</c></b>：两版规范对同一件事各定了一个动词 ——
/// 1.2.0 的 SBISpec 写 <c>POST</c>，而 SBI/MDS 0.9.5 的 schema 与 MOSIP 各客户端用的是
/// <c>MOSIPDISC</c>（规范里那条请求行就是 <c>MOSIPDISC http://127.0.0.1:&lt;port&gt;/device</c>）。
/// 两者报文相同，故同一个动作接两个动词，不分成两份实现。
/// </para>
/// </remarks>
[ApiController]
[Route("device")]
public sealed class DeviceController(DeviceDiscoveryService discovery, HttpHostSettings settings) : ControllerBase
{
    /// <summary>
    /// 发现设备
    /// </summary>
    /// <param name="request">
    /// 发现请求；报文体可缺省，缺省时按不限模态回应
    /// </param>
    /// <returns>
    /// 设备记录数组；类型非法时为单元素错误记录
    /// </returns>
    /// <remarks>
    /// 请求里的类型是自由字符串、由业务层按规范的四个字面量<b>严格比对</b>，故取值非法（大写、拼错、
    /// 缺失）<b>不会走 400</b>：协议为它定了"发现响应的错误态"形状，HTTP 仍是 200 ——
    /// 见 <see cref="DeviceDiscoveryService"/> 与 <see cref="DiscoveryErrorResponse"/>。
    /// </remarks>
    [HttpPost]
    [AcceptVerbs("MOSIPDISC")]
    public ActionResult<IReadOnlyList<object>> Discover([FromBody] DiscoveryRequest? request)
    {
        // 规范要求发现响应不可缓存，并给出自身地址。
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Location = $"{settings.BaseUrl}/device";

        // 回呼根地址结尾带斜杠：后续请求以它为基址拼接（规范：callbackId 取 <device_service_port>/）。
        return Ok(discovery.GetDiscovery(request?.Type, $"{settings.BaseUrl}/"));
    }
}
