using BiometricBridge.Host.Dto;
using Microsoft.AspNetCore.Mvc;

namespace BiometricBridge.Host.Http.Controllers;

/// <summary>
/// SBI 设备信息。客户端（如 MOSIP 注册客户端 / CTK）用它取设备的详细状态与身份
/// </summary>
/// <remarks>
/// <para>
/// 路由写死为规范的 <c>/info</c>；跨域与预检由 <see cref="SbiCorsMiddleware"/> 统一处理，本类不管。
/// </para>
/// <para>
/// <b>同时接受 <c>MOSIPDINFO</c>、<c>MOSIPINFO</c> 与 <c>POST</c></b>：规范页写
/// <c>MOSIPINFO</c>，而 MOSIP 各客户端（含参考实现）用的是 <c>MOSIPDINFO</c>（多一个 D）。
/// 两者的报文相同，故同一个动作都接，不分成两份实现。
/// </para>
/// </remarks>
[ApiController]
[Route("info")]
public sealed class InfoController(DeviceInfoService deviceInfo, HttpHostSettings settings) : ControllerBase
{
    /// <summary>
    /// 取设备信息
    /// </summary>
    /// <param name="request">
    /// 请求体；规范对该请求没有定义参数，内容不被使用
    /// </param>
    /// <returns>
    /// 设备信息数组
    /// </returns>
    [HttpPost]
    [AcceptVerbs("MOSIPDINFO")]
    [AcceptVerbs("MOSIPINFO")]
    public ActionResult<IReadOnlyList<DeviceInfoResponse>> GetInfo([FromBody] DeviceInfoRequest? request)
    {
        // 规范要求设备信息响应不可缓存。
        Response.Headers.CacheControl = "no-store";

        // 回呼根地址结尾带斜杠：后续请求以它为基址拼接。
        return Ok(deviceInfo.GetDeviceInfo($"{settings.BaseUrl}/"));
    }
}
