namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备信息请求（<c>MOSIPDINFO /info</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本类型没有任何属性</b>：SBI 对该请求没有定义参数，报文体的内容不被使用。
/// </para>
/// <para>
/// 仍然建出这个类型，是为了让端点有明确的入参类型，而不是在处理者上挂一个无名的 <c>object</c>；
/// 将来若规范给这个请求加了参数，加在这里即可。
/// </para>
/// </remarks>
public sealed record DeviceInfoRequest;
