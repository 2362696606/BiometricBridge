using BiometricBridge.Host;
using Microsoft.AspNetCore.Mvc;

namespace BiometricBridge.Host.Http.Controllers;

/// <summary>
/// 健康检查。外部调用方用它确认桥接器的对外服务是否在线
/// </summary>
/// <remarks>
/// 路由写死而不按类名推导（<c>[controller]</c>）：这是对外的契约，拼写不该随重命名而漂移。
/// </remarks>
[ApiController]
[Route("api/health")]
public sealed class HealthController(BridgeStatusService statusService) : ControllerBase
{
    /// <summary>
    /// 取桥接器运行状态
    /// </summary>
    /// <returns>
    /// 桥接器运行状态
    /// </returns>
    [HttpGet]
    public ActionResult<BridgeStatus> Get() => statusService.GetStatus();
}
