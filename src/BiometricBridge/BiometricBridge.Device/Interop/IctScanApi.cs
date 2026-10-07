using System.Runtime.InteropServices;

namespace BiometricBridge.Device.Interop;

/// <summary>
/// <c>ictScanAPI.dll</c> 的互操作声明。
/// </summary>
/// <remarks>
/// 只声明该 DLL 导出表里真实存在的符号；头文件与这份 DLL 并非同一版本，
/// 头文件里多出的函数在此不声明。
/// <para>
/// 返回值统一约定：<b>非零表示成功，0 表示失败</b>，
/// 失败原因见 <see cref="ictScanGetLastErrNo"/>。
/// </para>
/// </remarks>
public static class IctScanApi
{
    /// <summary>
    /// 本机库名。
    /// </summary>
    private const string Library = "ictScanAPI.dll";

    #region 库信息

    /// <summary>
    /// 取库版本号。
    /// </summary>
    /// <returns>
    /// 指向库内部静态字符串的指针，调用方不得释放，
    /// 用 <see cref="Marshal.PtrToStringAnsi(nint)"/> 读。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ictScanGetLibVersion();

    #endregion

    #region 设备枚举与开关

    /// <summary>
    /// 枚举已接入的 USB 设备。
    /// </summary>
    /// <param name="deviceName">
    /// 接收设备名的槽位数组，每个槽位都要指向一块已分配的缓冲区。
    /// </param>
    /// <param name="deviceNumMax">
    /// 槽位数。
    /// </param>
    /// <param name="pDeviceNum">
    /// 传出实际设备数。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>槽位必须给真实缓冲区，传 <see langword="null"/> 会直接崩溃</b>：
    /// 库不看 <paramref name="deviceNumMax"/>，只要发现设备就往槽位里写。
    /// </para>
    /// <para>
    /// 取回的是设备的产品串（如 <c>ICT_FAP50</c>），不是 USB 实例路径，
    /// <b>里面没有序列号</b>。
    /// </para>
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanEnumUsbDevices([In, Out] IntPtr[] deviceName, int deviceNumMax, ref int pDeviceNum);

    /// <summary>
    /// 打开设备。
    /// </summary>
    /// <param name="pContext">
    /// Android 的 USB 上下文；Windows 上传 <see cref="IntPtr.Zero"/>。
    /// </param>
    /// <returns>
    /// 设备句柄；打开失败时为 <see cref="IntPtr.Zero"/>。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr ictScanInitDeviceWithIoContext(IntPtr pContext);

    /// <summary>
    /// 关闭设备并释放资源。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ictScanDeinitDevice(IntPtr ictHandle);

    #endregion

    #region 设备信息

    /// <summary>
    /// 读取设备信息。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pDeviceInfo">
    /// 接收设备信息。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetDeviceInfo(IntPtr ictHandle, ref IctScanDeviceInfo pDeviceInfo);

    #endregion

    #region 采集

    /// <summary>
    /// 取原始灰度图（对应头文件的 <c>IMG_RAW</c>）。本实现只用这一种格式。
    /// </summary>
    internal const int ImageFormatRaw = 0;

    /// <summary>
    /// 启动一次采集，阻塞到质量达标或超时为止。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="nMinQuality">
    /// 达到该质量即停止采集；传 0 表示不设质量要求。
    /// </param>
    /// <param name="nCurQuality">
    /// 接收本次达到的质量分。
    /// </param>
    /// <param name="nTimeout">
    /// 采集超时（毫秒）。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanStartCaptureImage(IntPtr ictHandle, int nMinQuality, ref int nCurQuality, int nTimeout);

    /// <summary>
    /// 停止采集。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanStopCapture(IntPtr ictHandle);

    /// <summary>
    /// 取最近一次采集到的图像。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pImage">
    /// 接收图像数据的缓冲区，由调用方分配。
    /// </param>
    /// <param name="nImageLen">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <param name="pImageFormat">
    /// 期望的图像格式，见 <see cref="ImageFormatRaw"/>。
    /// </param>
    /// <param name="compressionRatio">
    /// 压缩比。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    /// <remarks>
    /// 传出的长度偏小，装不下整幅图。缓冲区按传感器尺寸开，别照它报的长度开。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetLastImage(IntPtr ictHandle, byte[] pImage, ref int nImageLen, int pImageFormat, int compressionRatio);

    /// <summary>
    /// 取最近一次采集到的矫正图，固定 256×360。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pImage">
    /// 接收图像数据的缓冲区，由调用方分配，按 256×360 开。
    /// </param>
    /// <param name="nImageLen">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <param name="pImageFormat">
    /// 期望的图像格式，见 <see cref="ImageFormatRaw"/>。
    /// </param>
    /// <param name="compressionRatio">
    /// 压缩比。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetLastRectifiedImage(IntPtr ictHandle, byte[] pImage, ref int nImageLen, int pImageFormat, int compressionRatio);

    /// <summary>
    /// 取图像质量分。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pBuffer">
    /// 图像数据；传 <see langword="null"/> 则用最近一次采集到的图。
    /// </param>
    /// <param name="nQualityScore">
    /// 传出质量分。
    /// </param>
    /// <param name="nNIFQScore">
    /// 传出 NIFQ 质量分。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetImageQualityScore(IntPtr ictHandle, byte[]? pBuffer, ref int nQualityScore, ref int nNIFQScore);

    #endregion

    #region 模板与比对

    /// <summary>
    /// 取模板的最大长度（ANSI 与 ISO 中的较大者）。
    /// </summary>
    /// <returns>
    /// 模板最大字节数。这是长度本身，不是成功标志。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetMaxTemplateSize();

    /// <summary>
    /// 取最近一次采集图像的模板。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pOutTemplate">
    /// 接收模板的缓冲区，由调用方分配，
    /// 长度按 <see cref="ictScanGetMaxTemplateSize"/> 取。
    /// </param>
    /// <param name="pnOutTemplateSize">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <param name="pTplFormat">
    /// 模板格式：0 = ISO，1 = ANSI。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetTemplate(IntPtr ictHandle, byte[] pOutTemplate, ref int pnOutTemplateSize, int pTplFormat);

    /// <summary>
    /// 取加密的模板（PID 块）。仅 L1 模块可用。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pOutEncryptedTemplate">
    /// 接收加密模板的缓冲区，由调用方分配。
    /// </param>
    /// <param name="pnOutEncryptedTemplateSize">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <param name="pTplFormat">
    /// 模板格式：0 = ISO，1 = ANSI。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetTemplateEncrypted(IntPtr ictHandle, byte[] pOutEncryptedTemplate, ref int pnOutEncryptedTemplateSize, int pTplFormat);

    /// <summary>
    /// 采图并生成模板，一次调用完成。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="byFingerPosition">
    /// 手指序号。保留参数，未使用。
    /// </param>
    /// <param name="pInImageBuffer">
    /// 输入图像；传 <see langword="null"/> 则新采一张，否则把这张图送 MCU 处理。
    /// </param>
    /// <param name="pnInoutQuality">
    /// 传出图像质量分。
    /// </param>
    /// <param name="pOutImageBuffer">
    /// 输出图像：<paramref name="pInImageBuffer"/> 非空时与它相同，否则是刚采到的新图。
    /// </param>
    /// <param name="pOutTemplate">
    /// 接收模板的缓冲区，由调用方分配。
    /// </param>
    /// <param name="pnOutTemplateSize">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <param name="pTplFormat">
    /// 模板格式：0 = ISO，1 = ANSI。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanCreateTemplate(IntPtr ictHandle, byte byFingerPosition, byte[]? pInImageBuffer,
        ref int pnInoutQuality, byte[] pOutImageBuffer, byte[] pOutTemplate, ref int pnOutTemplateSize, int pTplFormat);

    /// <summary>
    /// 采一张新图并与给定模板比对。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="byFingerPosition">
    /// 手指序号。保留参数，未使用。
    /// </param>
    /// <param name="pInTemplate">
    /// 要比对的模板。
    /// </param>
    /// <param name="inTemplateSize">
    /// 模板长度。
    /// </param>
    /// <param name="pnInoutQuality">
    /// 传入认证质量门限，传出本次采图的质量分。
    /// </param>
    /// <param name="pOutImageBuffer">
    /// 接收本次采集的图像。
    /// </param>
    /// <param name="pfOutResult">
    /// 传出算法给出的匹配分。
    /// </param>
    /// <param name="pTplFormat">
    /// 模板格式：0 = ISO，1 = ANSI。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanVerifyTemplate(IntPtr ictHandle, byte byFingerPosition, byte[] pInTemplate, int inTemplateSize,
        ref int pnInoutQuality, byte[] pOutImageBuffer, ref int pfOutResult, int pTplFormat);

    /// <summary>
    /// 比对两个模板，给出匹配分。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pProbeTemplate">
    /// 现场模板。
    /// </param>
    /// <param name="probeTemplateSize">
    /// 现场模板长度。
    /// </param>
    /// <param name="pGaleryTemplate">
    /// 底库模板。
    /// </param>
    /// <param name="galeryTemplateSize">
    /// 底库模板长度。
    /// </param>
    /// <param name="pfOutResult">
    /// 传出算法给出的匹配分。
    /// </param>
    /// <param name="pTplFormat">
    /// 模板格式：0 = ISO，1 = ANSI。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanMatchTemplates(IntPtr ictHandle, byte[] pProbeTemplate, int probeTemplateSize,
        byte[] pGaleryTemplate, int galeryTemplateSize, ref int pfOutResult, int pTplFormat);

    /// <summary>
    /// 把 ISO 模板转成 ANSI 模板。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="pTemplateIso">
    /// 待转换的 ISO 模板。
    /// </param>
    /// <param name="nTemplateIsoSize">
    /// ISO 模板长度。
    /// </param>
    /// <param name="pTemplateANSI">
    /// 接收 ANSI 模板的缓冲区，由调用方分配。
    /// </param>
    /// <param name="pnInOutTemplateSize">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanConvertIsoTemplateToAnsi(IntPtr ictHandle, byte[] pTemplateIso, int nTemplateIsoSize,
        byte[] pTemplateANSI, ref int pnInOutTemplateSize);

    #endregion

    #region 图像格式转换与传感器控制

    // 本区四个函数在原生源码里没落在 extern "C" 块内，只能按 C++ 装饰名解析，
    // 故 EntryPoint 写装饰名。装饰名由编译器按签名生成，厂商重编译会变；
    // 换 DLL 后若在此抛 EntryPointNotFoundException，重新 dump 一份导出表核对名字。

    /// <summary>
    /// 把原始灰度图转成 WSQ。
    /// </summary>
    /// <param name="pRawBuffer">
    /// 原始图。
    /// </param>
    /// <param name="width">
    /// 原始图宽度。
    /// </param>
    /// <param name="height">
    /// 原始图高度。
    /// </param>
    /// <param name="dpi">
    /// 原始图分辨率。
    /// </param>
    /// <param name="bitrate">
    /// 压缩码率。
    /// </param>
    /// <param name="pWsqBuffer">
    /// 接收 WSQ 数据的缓冲区，由调用方分配并释放。
    /// </param>
    /// <param name="pnWsqSize">
    /// 传入缓冲区长度，传出实际长度。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?ictScanConvertRawToWsq@@YAHPEAXHHHM0PEAH@Z")]
    internal static extern int ictScanConvertRawToWsq(byte[] pRawBuffer, int width, int height, int dpi, float bitrate,
        byte[] pWsqBuffer, ref int pnWsqSize);

    /// <summary>
    /// 把原始灰度图转成 BMP。
    /// </summary>
    /// <param name="pRawBuffer">
    /// 原始图。
    /// </param>
    /// <param name="width">
    /// 原始图宽度。
    /// </param>
    /// <param name="height">
    /// 原始图高度。
    /// </param>
    /// <param name="pBmpBuffer">
    /// 接收 BMP 数据的缓冲区，由调用方分配并释放，
    /// 长度需 <c>1078 + 图像字节数</c>。
    /// </param>
    /// <param name="bitCount">
    /// 像素位深，取 8、16 或 24。
    /// </param>
    /// <param name="bmpSize">
    /// 缓冲区长度。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanConvertRawToBmp(byte[] pRawBuffer, int width, int height, byte[] pBmpBuffer,
        int bitCount, int bmpSize);

    /// <summary>
    /// 把原始灰度图转成 BGR。
    /// </summary>
    /// <param name="pRawBuffer">
    /// 原始图。
    /// </param>
    /// <param name="width">
    /// 原始图宽度。
    /// </param>
    /// <param name="height">
    /// 原始图高度。
    /// </param>
    /// <param name="pBgrBuffer">
    /// 接收 BGR 数据的缓冲区，由调用方分配并释放。
    /// </param>
    /// <param name="bitCount">
    /// 像素位深。
    /// </param>
    /// <param name="bgrSize">
    /// 缓冲区长度。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?ictScanConvertRawToBgr@@YAHPEAXHH0HH@Z")]
    internal static extern int ictScanConvertRawToBgr(byte[] pRawBuffer, int width, int height, byte[] pBgrBuffer,
        int bitCount, int bgrSize);

    /// <summary>
    /// 设置 LED 亮度（PWM）。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="param1">
    /// 含义未知。
    /// </param>
    /// <param name="param2">
    /// 含义未知。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    /// <remarks>
    /// 参数语义不可知，要用先问厂商，别按名字猜。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?ictScanSetLedPwm@@YAHPEAXEH@Z")]
    internal static extern int ictScanSetLedPwm(IntPtr ictHandle, byte param1, int param2);

    /// <summary>
    /// 给设备下发通用指令。
    /// </summary>
    /// <param name="ictHandle">
    /// 设备句柄。
    /// </param>
    /// <param name="param1">
    /// 含义未知。
    /// </param>
    /// <param name="param2">
    /// 含义未知。
    /// </param>
    /// <returns>
    /// 非零表示成功。
    /// </returns>
    /// <remarks>
    /// 参数语义不可知，要用先问厂商。注意它不带 <c>ictScan</c> 前缀。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?ictSetGeneralCommands@@YAHPEAXHH@Z")]
    internal static extern int ictSetGeneralCommands(IntPtr ictHandle, int param1, int param2);

    #endregion

    #region 错误码

    /// <summary>
    /// 取最近一次失败的错误码。
    /// </summary>
    /// <returns>
    /// 错误码；0 表示上一次调用并未记录错误。
    /// </returns>
    /// <remarks>
    /// 库内混用 Win32 错误码（如 87 = <c>ERROR_INVALID_PARAMETER</c>）
    /// 与 <c>0x2000xxxx</c> 自定义码，本 DLL 没有把码转成文字的函数。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ictScanGetLastErrNo();

    /// <summary>
    /// 写入错误码。
    /// </summary>
    /// <param name="dwError">
    /// 要写入的错误码。
    /// </param>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void ictScanSetLastErrNo(uint dwError);

    #endregion
}
