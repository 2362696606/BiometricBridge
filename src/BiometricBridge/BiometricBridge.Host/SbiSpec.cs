namespace BiometricBridge.Host;

/// <summary>
/// SBI 规范里本服务用到的固定值
/// </summary>
/// <remarks>
/// <para>
/// 依据：<b>SBI Request/Response Specification v0.9.5</b>（项目 CLAUDE.md 指定的权威源）。
/// <c>docs.mosip.io</c> 上的 SBISpec 页<b>不分版本</b>、混入了更新版规范的内容（其 <c>/device</c>、
/// <c>/info</c> 用 <c>serialNo</c> 且含 <c>jwk</c>，0.9.5 里都没有），不可用于字段判定。
/// </para>
/// <para>
/// 错误码与描述不在这里 —— 它们归 <see cref="Common.SbiErrorHelper"/>（取值须逐字对齐 CTK schema）。
/// </para>
/// </remarks>
public static class SbiSpec
{
    /// <summary>
    /// 本服务生成响应所用的规范版本
    /// </summary>
    /// <remarks>
    /// 写进响应的 <c>specVersion</c>：采集响应里是单个字符串，<c>/discover</c>、<c>/info</c> 里是
    /// 仅含本值一个元素的数组。同时用作 <c>serviceVersion</c> —— 后一条跟随参考实现
    /// （Com.ICT.SbiRegisterBridge）的取值，那是过 CTK 的实证。
    /// </remarks>
    public const string Version = "0.9.5";

    /// <summary>
    /// 未注册设备的 <c>purpose</c> 取值：空串
    /// </summary>
    /// <remarks>
    /// 规范原文："For devices that are not registered the purpose is empty."
    /// <para>
    /// 这条不是可选润色：CTK 以 JSON Schema 的 <c>not: {"enum": ["Auth","Registration"]}</c> 校验该字段，
    /// 未注册却报 <c>Registration</c> 会被判 Failed。
    /// </para>
    /// </remarks>
    public const string EmptyPurpose = "";

    /// <summary>
    /// 环境（<c>env</c>）取值
    /// </summary>
    /// <remarks>
    /// schema 限定为 Staging / Developer / Pre-Production / Production 四者之一。
    /// </remarks>
    public const string DefaultEnv = "Staging";
}
