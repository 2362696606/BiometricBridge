using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 预览帧接收端。设备把采集过程中的实时图投给它。
/// </summary>
/// <remarks>
/// <para>
/// <b>由设备线程调用</b>：可能并发，且停止预览后仍可能再收到一帧（在飞的回调）。
/// 实现须自行处理线程 —— 界面侧用 <c>Dispatcher.UIThread.Post</c> 切回去。
/// </para>
/// <para>
/// <b>实现不得抛异常。</b>本方法跑在原生回调的栈帧里，异常会穿过托管边界回到原生代码。
/// </para>
/// <para>
/// 做成委托而不是单方法接口：装饰链上想观察帧的那几层靠闭包就地组装即可
/// （数帧无非 <c>frame =&gt; { count++; inner(frame); }</c>）。C# 没有匿名内部类，
/// 而接口在这里会逼每个观察者都生出一个人工类型 —— 那是纯脚手架。
/// </para>
/// <para>
/// 之所以是"起预览时必须交出的接收端"而不是设备上的事件：装饰链（串行化 + 日志 + 补帧）转发事件
/// 得各自重抛一遍，而"启动预览时必须交出一个接收端"让"预览在跑却没人接帧"在结构上不可能发生。
/// </para>
/// </remarks>
/// <param name="frame">
/// 预览帧。其 <see cref="PreviewFrame.Data"/> 由调用方拥有，可长期持有。
/// </param>
public delegate void PreviewFrameSink(PreviewFrame frame);
