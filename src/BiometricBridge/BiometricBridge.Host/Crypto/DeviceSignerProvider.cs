namespace BiometricBridge.Host.Crypto;

/// <summary>
/// 按设备身份提供签名器：首次需要时现场生成设备密钥、请 DP 签发设备证书，之后复用；也可显式更换
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么按需生成</b>：设备证书的 Subject 里要写设备序列号，而序列号只有设备连上之后才报得出来。
/// 启动时就建会签出一张 <c>CN</c> 为空的证书。
/// </para>
/// <para>
/// <b>为什么按身份缓存（而不是只留最近一个）</b>：一台机器上可以有多个设备，各有各的密钥与证书。
/// 只留一个槽位的话，两台设备交替请求会让"另一台"每次都重新签发一张证书 —— 证书在客户端看来
/// 随机变化，密钥轮换的语义也随之失效。
/// </para>
/// <para>
/// 构建失败<b>不缓存</b>，下次请求会重试 —— 设备还没连上时每次请求都得到一次重试机会，连上后自然就好。
/// </para>
/// </remarks>
public sealed class DeviceSignerProvider : IDisposable
{
    #region Fileds

    /// <summary>
    /// 设备证书签发器（进程级复用，不在此释放）
    /// </summary>
    private readonly IDeviceCertificateIssuer _issuer;

    /// <summary>
    /// 保护下面三个集合
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 各身份当前的密钥与签名器
    /// </summary>
    private readonly Dictionary<string, Entry> _current = [];

    /// <summary>
    /// 被轮换下来的密钥
    /// </summary>
    /// <remarks>
    /// <b>不就地释放</b>：换证书的那一刻可能还有请求正拿着旧的签名器签名，放掉它的私钥会让那些请求当场炸掉。
    /// 轮换是人工操作、次数有限，攒着到本类释放时一并收掉即可。
    /// </remarks>
    private readonly List<DeviceKeyManager> _retired = [];

    /// <summary>
    /// 是否已释放
    /// </summary>
    private bool _disposed;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="issuer">
    /// 设备证书签发器
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="issuer"/> 为 null
    /// </exception>
    public DeviceSignerProvider(IDeviceCertificateIssuer issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        _issuer = issuer;
    }

    /// <summary>
    /// 取给该设备签名的签名器
    /// </summary>
    /// <param name="serialNo">
    /// 设备序列号；空表示设备尚未报出（未连接）
    /// </param>
    /// <param name="make">
    /// 厂商
    /// </param>
    /// <param name="model">
    /// 型号
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称
    /// </param>
    /// <returns>
    /// 签名器
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 序列号为空（设备未连接）
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// 已释放
    /// </exception>
    public IJwsSigner GetSigner(string serialNo, string make, string model, string deviceProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var identity = IdentityOf(serialNo, make, model, deviceProvider);

        lock (_gate)
        {
            if (_current.TryGetValue(identity, out var cached))
            {
                return cached.Signer;
            }

            var created = CreateEntry(serialNo, make, model, deviceProvider);
            _current.Add(identity, created);
            return created.Signer;
        }
    }

    /// <summary>
    /// 更换该设备的证书：生成新设备密钥并请 DP 重新签发，替换掉原来那一套
    /// </summary>
    /// <param name="serialNo">
    /// 设备序列号；不能为空 —— 设备证书的 <c>CN</c> 就是它
    /// </param>
    /// <param name="make">
    /// 厂商
    /// </param>
    /// <param name="model">
    /// 型号
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// 序列号为空（设备未连接）
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// 已释放
    /// </exception>
    /// <remarks>
    /// 新建<b>先于</b>替换：签发失败时旧的那套原样留着，客户端不会因为一次失败的轮换而突然验不了签。
    /// </remarks>
    public void Rotate(string serialNo, string make, string model, string deviceProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var identity = IdentityOf(serialNo, make, model, deviceProvider);

        lock (_gate)
        {
            var created = CreateEntry(serialNo, make, model, deviceProvider);

            if (_current.Remove(identity, out var previous))
            {
                _retired.Add(previous.KeyManager);
            }

            _current[identity] = created;
        }
    }

    /// <summary>
    /// 释放当前与所有被轮换下来的设备密钥
    /// </summary>
    /// <remarks>
    /// 签发器（DP 私钥与 DP 证书）归组合根所有，不在此释放 —— 它要一直活在 JWS 头的 <c>x5c</c> 链里。
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

            foreach (var entry in _current.Values)
            {
                entry.KeyManager.Dispose();
            }

            _current.Clear();

            foreach (var keyManager in _retired)
            {
                keyManager.Dispose();
            }

            _retired.Clear();
        }
    }

    /// <summary>
    /// 由设备身份算出缓存键
    /// </summary>
    /// <param name="serialNo">
    /// 设备序列号
    /// </param>
    /// <param name="make">
    /// 厂商
    /// </param>
    /// <param name="model">
    /// 型号
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称
    /// </param>
    /// <returns>
    /// 缓存键
    /// </returns>
    /// <remarks>
    /// 用单元分隔符拼接：这几项都可能含空格或连字符，随便挑一个当分隔符都可能把两组不同的取值拼成同一个键。
    /// </remarks>
    private static string IdentityOf(string serialNo, string make, string model, string deviceProvider)
        => string.Join('\u001f', serialNo, make, model, deviceProvider);

    /// <summary>
    /// 生成一套新的设备密钥与签名器
    /// </summary>
    /// <param name="serialNo">
    /// 设备序列号
    /// </param>
    /// <param name="make">
    /// 厂商
    /// </param>
    /// <param name="model">
    /// 型号
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称
    /// </param>
    /// <returns>
    /// 密钥与签名器
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 序列号为空 —— 空白的 <c>CN</c> 签出来的证书没有身份意义，宁可当场失败
    /// </exception>
    private Entry CreateEntry(string serialNo, string make, string model, string deviceProvider)
    {
        if (string.IsNullOrWhiteSpace(serialNo))
        {
            throw new InvalidOperationException("设备尚未报出序列号（未连接），无法签发设备证书。");
        }

        var keyManager = DeviceKeyManager.Create(_issuer, serialNo, make, model, deviceProvider);
        return new Entry(keyManager, new JwsSigner(keyManager));
    }

    /// <summary>
    /// 一个身份对应的一套材料
    /// </summary>
    /// <param name="KeyManager">
    /// 设备密钥与证书
    /// </param>
    /// <param name="Signer">
    /// 用该密钥签名的签名器
    /// </param>
    private sealed record Entry(DeviceKeyManager KeyManager, JwsSigner Signer);
}
