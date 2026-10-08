using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 补帧装饰器：保证预览投给接收端的帧率不低于设定下限。
/// </summary>
/// <remarks>
/// <para>
/// 设备只负责把帧送出来，不保证送得多快；这道下限由本装饰器补上 —— 上游帧来得稀疏时，
/// 把缓存里那最后一帧按节拍再投一次。于是<b>调用方拿到手的就是满足刷新要求的流</b>，
/// 既不必自己接一个缓存，也不必知道"补帧"这回事。
/// </para>
/// <para>
/// 补不补取决于<b>运行期帧有没有按时到</b>，不取决于设备类型：推得快的设备（如虹膜）定时器
/// 几乎不触发，自动退化为直通；推得慢或会中途停顿的设备才真正补上。故各设备无需为此写任何代码。
/// </para>
/// <para>
/// 注册在最外层（order 大于日志装饰器，见 <c>App.RegisterTypes</c>）：这样日志装饰器数到的
/// 是设备真正投出的帧数，补出来的帧不混入其中。
/// </para>
/// </remarks>
public sealed class PacingDeviceDecorator : IBiometricDeviceDecorator
{
    #region 常量

    /// <summary>
    /// 预览刷新率的下限（帧/秒）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 规范只要求不低于 3fps，这里取 10fps。<b>决定观感的是"交付节拍"，不是设备出帧率</b>：
    /// 设备慢于一拍时本装饰器重发最后一帧，交付快一拍，新帧从产生到露面的等待就短一截 ——
    /// 3fps 下最多等 333ms，10fps 下最多 100ms。
    /// </para>
    /// <para>
    /// 取 10fps 是对齐参考实现：它把取帧与发帧解耦之后，发帧侧走的正是 100ms 的节拍。
    /// </para>
    /// <para>
    /// 与具体设备无关，故整条装饰链共用这一个值。它落在设备侧而非界面侧是有意的取舍：
    /// 界面因此不必再接一个缓存。
    /// </para>
    /// </remarks>
    public const int MinimumFrameRate = 10;

    /// <summary>
    /// 定时器粒度余量（毫秒）。
    /// </summary>
    /// <remarks>
    /// 系统定时器<b>只会晚不会早</b>，且按系统时钟粒度触发（Windows 默认约 15.6ms）。
    /// 实测按名义 333ms 排程，相邻补帧实际落在 ~341ms，折合 2.93fps —— 反而跌破了当时定的 3fps 下限。
    /// 故名义间隔取小一点，把这点粒度吃进去。留大了会无谓地提高推送频率，取 20ms 有一档余量。
    /// </remarks>
    private const double TimerGranularityMarginMs = 20;

    /// <summary>
    /// 补帧间隔：刷新率下限的倒数扣掉定时器粒度余量。
    /// </summary>
    public static readonly TimeSpan FrameInterval =
        TimeSpan.FromMilliseconds(1000d / MinimumFrameRate - TimerGranularityMarginMs);

    #endregion

    /// <summary>
    /// 构造装饰器。
    /// </summary>
    /// <param name="innerDevice">
    /// 被装饰的设备。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="innerDevice"/> 为 null。
    /// </exception>
    public PacingDeviceDecorator(IBiometricDevice innerDevice)
    {
        ArgumentNullException.ThrowIfNull(innerDevice);
        InnerDevice = innerDevice;
    }

    /// <inheritdoc/>
    public IBiometricDevice InnerDevice { get; }

    /// <inheritdoc/>
    public bool IsConnected => InnerDevice.IsConnected;

    /// <inheritdoc/>
    public string? SerialNo => InnerDevice.SerialNo;

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return InnerDevice.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return InnerDevice.DisconnectAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        return InnerDevice.CaptureAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 把接收端包一层补帧再转发，流结束时收掉那层 —— 定时器随之停摆，不会在预览结束后继续投帧。
    /// </remarks>
    public async Task RunPreviewAsync(PreviewFrameSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        using var relay = new PacingRelay(sink);
        await InnerDevice.RunPreviewAsync(relay.OnFrame, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return InnerDevice.DisposeAsync();
    }

    /// <summary>
    /// 补帧中继：上游推来的帧原样转发并缓存最后一帧；超过 <see cref="FrameInterval"/> 没有新帧，
    /// 就把缓存那帧再投一次。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上游推多快就发多快 —— 一律立即转发，<b>不节流</b>。只有上游慢下来时才由定时器补帧，
    /// 于是只要投过至少一帧，相邻两次投递的间隔<b>恒不超过 <see cref="FrameInterval"/></b>。
    /// </para>
    /// <para>
    /// 只留最新一帧、不排队：预览要的是"现在"，积压旧帧只会让画面落后于现实。
    /// </para>
    /// <para>
    /// 之所以是个具名类而不是像数帧那样就地一个闭包：它扛着定时器与最后一帧两样状态，
    /// 塞进闭包只会让捕获关系变糊。
    /// </para>
    /// </remarks>
    private sealed class PacingRelay : IDisposable
    {
        /// <summary>
        /// 保护 <see cref="_frame"/>、<see cref="_disposed"/> 与定时器的重排，使"取帧 / 判定投递 /
        /// 重排下一次"成为一步原子操作。锁内只做这几件事，实际投递在锁外 —— 下游接收端
        /// 可能会切线程或做别的事，不该持着本锁。
        /// </summary>
        private readonly object _gate = new();

        /// <summary>
        /// 补帧定时器。以"永不触发"构造，首帧到达才起。
        /// </summary>
        /// <remarks>
        /// <b>一次性重排，而非固定周期。</b>每次投递都把下一次触发重排到 <c>interval</c> 之后，
        /// 于是"触发"本身就意味着"距上次投递已满 interval"，无需再比较时间戳。
        /// 固定周期做不到这点：真帧恰好落在一次 tick 之后时，两帧会被拉到接近 2×interval 而跌破下限；
        /// 且周期短于回调耗时时 <see cref="Timer"/> 会并发回调，重排式的一次性定时器不会。
        /// </remarks>
        private readonly Timer _timer;

        /// <summary>
        /// 下游接收端。
        /// </summary>
        private readonly PreviewFrameSink _downstream;

        /// <summary>
        /// 缓存的最新一帧；null 表示当前无帧（尚未投过，或已被释放）。
        /// </summary>
        private PreviewFrame? _frame;

        /// <summary>
        /// 是否已释放。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 构造中继。
        /// </summary>
        /// <param name="downstream">
        /// 下游接收端。
        /// </param>
        internal PacingRelay(PreviewFrameSink downstream)
        {
            _downstream = downstream;

            // 以"永不触发"构造：没有帧就没有什么可补的，起表交给第一帧。
            _timer = new Timer(OnTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// 收下一帧：转发给下游，并缓存住以备补帧。
        /// </summary>
        /// <param name="frame">
        /// 预览帧。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="frame"/> 为 null。
        /// </exception>
        /// <remarks>
        /// 已释放时退化为空操作，不抛：释放与收尾期间上游可能还有在飞的调用。
        /// </remarks>
        internal void OnFrame(PreviewFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            PreviewFrame toEmit;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _frame = frame;

                // 与"设帧"同锁完成，故排定的截止点必然对应最新一次投递：
                // 并发的 OnFrame / 定时器回调之间不会各自排出一个互相推远的截止点。
                _timer.Change(FrameInterval, Timeout.InfiniteTimeSpan);

                toEmit = frame;
            }

            Emit(toEmit);
        }

        /// <summary>
        /// 停表并释放。可重复调用。
        /// </summary>
        /// <remarks>
        /// 先置 <see cref="_disposed"/> 再释放定时器，两步都在锁内：<see cref="Timer.Dispose()"/> 不会等待
        /// 在飞的回调，若放行回调去 <c>Change</c> 已释放的定时器，异常会落在的线程池线程上把进程带走。
        /// 在飞的回调要么在锁内先看到标志而收手，要么先于本方法完成全部动作。
        /// </remarks>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _frame = null;
                _timer.Dispose();
            }
        }

        /// <summary>
        /// 把帧投给下游。
        /// </summary>
        /// <param name="frame">
        /// 要投递的帧。
        /// </param>
        private void Emit(PreviewFrame frame)
        {
            // 锁外投：下游可能会切到界面线程，不该持着本锁。
            // 重排已在锁内完成，故下游再慢也拖不动下一次重排。
            _downstream(frame);
        }

        /// <summary>
        /// 补帧：定时器到点意味着"距上次投递已满 <see cref="FrameInterval"/> 且期间没有新帧"。
        /// </summary>
        /// <param name="state">
        /// 未使用。
        /// </param>
        /// <remarks>
        /// 本回调由线程池线程调用。有无新帧无需再判定：任何一次投递都会重排定时器，
        /// 于是它能触发就说明期间确实没有新的投递。
        /// </remarks>
        private void OnTick(object? state)
        {
            PreviewFrame toEmit;
            lock (_gate)
            {
                // 无帧（未被 Dispose 停表却仍有在飞回调）或已释放都不补：直接收手，不再重排。
                if (_disposed || _frame is null)
                {
                    return;
                }

                _timer.Change(FrameInterval, Timeout.InfiniteTimeSpan);
                toEmit = _frame;
            }

            Emit(toEmit);
        }
    }
}
