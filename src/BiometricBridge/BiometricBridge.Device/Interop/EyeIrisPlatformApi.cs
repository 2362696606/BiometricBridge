using System.Runtime.InteropServices;
using System.Text;
// ReSharper disable MemberCanBePrivate.Global

namespace BiometricBridge.Device.Interop;

/// <summary>
/// <c>eye_iris_platform.dll</c> 的互操作声明。
/// </summary>
/// <remarks>
/// <para>
/// 声明对照 SDK 的头文件 <c>include/eye_iris_platform_sdk.h</c>，函数名与原生导出名
/// <b>逐字保持</b>，便于和头文件逐条核对（与本目录的 <see cref="IctScanApi"/> 一致）。
/// </para>
/// <para>
/// <b>返回值约定与 <see cref="IctScanApi"/> 相反：0 表示成功</b>，非零为错误码，
/// 见“错误码”区。头文件在 Windows 下把调用约定定为 <c>__stdcall</c>，故全部用
/// <see cref="CallingConvention.StdCall"/>。
/// </para>
/// <para>
/// 本文件是对厂商托管封装 <c>EyeIrisPlatformAx.dll</c> 的重写，与之的三点差异：
/// </para>
/// <list type="number">
/// <item>
/// 补上了它漏声明的 <see cref="biospi_device_info"/>（SBI 的 DEVICE_INFO 要用）和
/// <see cref="biospi_set_beeper"/>。
/// </item>
/// <item>
/// 对 C 的 <c>long</c> 一律用 <see cref="int"/>。<c>long</c> 在 Windows 上是 4 字节，
/// 厂商封装有的地方写成了 8 字节 <see cref="long"/>，在 x64 上恰因寄存器传参而侥幸无害，
/// 但在 x86 的 stdcall 栈帧上会让后续参数整体错位。详见
/// <see cref="biospi_getshortcode_length"/> 与 <see cref="image_to_jpeg"/> 的备注。
/// </item>
/// <item>
/// 回调里的图像/模板指针按头文件原样声明为 <see cref="IntPtr"/>，而非厂商封装的
/// <c>ref byte</c>。<c>ref byte</c> 逼得调用方去开 <c>UnmanagedMemoryStream</c> 取数据，
/// 这里改成调用方直接 <see cref="Marshal.Copy(IntPtr, byte[], int, int)"/>。
/// </item>
/// </list>
/// <para>
/// 仅声明 <c>eye_iris_platform.dll</c> 自身的导出；它与 <c>eye_iris_core.dll</c>、
/// <c>MNN.dll</c>、<c>libopenblas.dll</c> 等有原生依赖，部署时须整包带上。
/// </para>
/// </remarks>
public static class EyeIrisPlatformApi
{
    /// <summary>
    /// 本机库名。
    /// </summary>
    private const string Library = "eye_iris_platform.dll";

    #region 常量 —— 图像与参数默认值

    /// <summary>
    /// 单眼虹膜图像宽度（像素）。
    /// </summary>
    internal const int IrisImageWidth = 640;

    /// <summary>
    /// 单眼虹膜图像高度（像素）。
    /// </summary>
    internal const int IrisImageHeight = 480;

    /// <summary>
    /// 单眼虹膜图像字节数，即 <see cref="IrisImageWidth"/> × <see cref="IrisImageHeight"/>。
    /// </summary>
    internal const int IrisImageSize = IrisImageWidth * IrisImageHeight;

    /// <summary>
    /// 通用采集质量门限默认值。
    /// </summary>
    internal const int ParamsQualityCommon = 76;

    /// <summary>
    /// 注册采集质量门限默认值。
    /// </summary>
    internal const int ParamsQualityEnroll = 80;

    /// <summary>
    /// 识别采集质量门限默认值。
    /// </summary>
    internal const int ParamsQualityIdentify = 60;

    /// <summary>
    /// 注册时的瞳孔曝光默认值。
    /// </summary>
    internal const int ParamsEyeExpoEnroll = 70;

    /// <summary>
    /// 识别时的瞳孔曝光默认值。
    /// </summary>
    internal const int ParamsEyeExpoIdentify = 40;

    /// <summary>
    /// JPEG 质量档位：95。
    /// </summary>
    internal const int JpegQuality95 = 95;

    /// <summary>
    /// JPEG 质量档位：90。
    /// </summary>
    internal const int JpegQuality90 = 90;

    /// <summary>
    /// JPEG 质量档位：80。
    /// </summary>
    internal const int JpegQuality80 = 80;

    /// <summary>
    /// JPEG 质量档位：70。
    /// </summary>
    internal const int JpegQuality70 = 70;

    /// <summary>
    /// JPEG 质量档位：60。
    /// </summary>
    internal const int JpegQuality60 = 60;

    #endregion

    #region 常量 —— 错误码

    /// <summary>
    /// 成功。
    /// </summary>
    internal const int NoError = 0x00000000;

    /// <summary>
    /// 参数非法。
    /// </summary>
    internal const int ErrorParameter = unchecked((int)0x80010002);

    /// <summary>
    /// 图像不可用。
    /// </summary>
    internal const int ErrorBadImage = unchecked((int)0x80010003);

    /// <summary>
    /// 授权/license 无效。
    /// </summary>
    internal const int ErrorLicense = unchecked((int)0x80010004);

    // 以下 0x80010005~0x80010027 是一组 ISO/IEC 19794-6 质量判据，头文件里没有，
    // 但 DLL 的常量表里有、且错误码段连续，故判定为真实的原生返回码。
    // 做合规/质量判定时可直接用，不必自己重算这些指标。

    /// <summary>
    /// 虹膜在图像中太小。
    /// </summary>
    internal const int ErrorSmallIrisSize = unchecked((int)0x80010005);

    /// <summary>
    /// 对焦不良。
    /// </summary>
    internal const int ErrorPoorFocus = unchecked((int)0x80010006);

    /// <summary>
    /// 可见虹膜面积不足。
    /// </summary>
    internal const int ErrorSmallVisibleIris = unchecked((int)0x80010007);

    /// <summary>
    /// 纹理过弱。
    /// </summary>
    internal const int ErrorLowTexture = unchecked((int)0x80010008);

    /// <summary>
    /// 瞳孔过大。
    /// </summary>
    internal const int ErrorBigPupil = unchecked((int)0x80010009);

    /// <summary>
    /// 生成虹膜编码失败（算法侧）。
    /// </summary>
    internal const int ErrorCreateIrisCode = unchecked((int)0x80010010);

    /// <summary>
    /// 瞳孔边界质量差。
    /// </summary>
    internal const int ErrorPoorPupilBoundary = unchecked((int)0x80010011);

    /// <summary>
    /// 外虹膜缘（limbus）弱。
    /// </summary>
    internal const int ErrorWeakLimbus = unchecked((int)0x80010012);

    /// <summary>
    /// 水平方向边距不足。
    /// </summary>
    internal const int ErrorLessHorzMargin = unchecked((int)0x80010013);

    /// <summary>
    /// 垂直方向边距不足。
    /// </summary>
    internal const int ErrorLessVertMargin = unchecked((int)0x80010014);

    /// <summary>
    /// 视线偏离（斜视）。
    /// </summary>
    internal const int ErrorGazePresent = unchecked((int)0x80010015);

    /// <summary>
    /// 头部旋转。
    /// </summary>
    internal const int ErrorHeadRotationPresent = unchecked((int)0x80010016);

    /// <summary>
    /// 放大倍率异常。
    /// </summary>
    internal const int ErrorMagnificationPresent = unchecked((int)0x80010017);

    /// <summary>
    /// 隔行扫描伪影。
    /// </summary>
    internal const int ErrorInterlacePresent = unchecked((int)0x80010018);

    /// <summary>
    /// 非虹膜（像是眼睛以外的内容）。
    /// </summary>
    internal const int ErrorEyeness = unchecked((int)0x80010019);

    /// <summary>
    /// 方向性对焦不良。
    /// </summary>
    internal const int ErrorPoorDirectionalFocus = unchecked((int)0x80010020);

    /// <summary>
    /// 灰度标准差（GLSV）不足。
    /// </summary>
    internal const int ErrorPoorGlsv = unchecked((int)0x80010021);

    /// <summary>
    /// 信噪比（SNR）不足。
    /// </summary>
    internal const int ErrorPoorSnr = unchecked((int)0x80010022);

    /// <summary>
    /// 瞳孔过小。
    /// </summary>
    internal const int ErrorSmallPupil = unchecked((int)0x80010023);

    /// <summary>
    /// 疑似硬性隐形眼镜。
    /// </summary>
    internal const int ErrorHardContactLens = unchecked((int)0x80010024);

    /// <summary>
    /// 瞳孔形状异常。
    /// </summary>
    internal const int ErrorPupilShape = unchecked((int)0x80010025);

    /// <summary>
    /// 视线无法计算。
    /// </summary>
    internal const int ErrorGazeCannotCompute = unchecked((int)0x80010026);

    /// <summary>
    /// 疑似隐形眼镜。
    /// </summary>
    internal const int ErrorContactLens = unchecked((int)0x80010027);

    /// <summary>
    /// 内存不足。
    /// </summary>
    internal const int ErrorLowMem = unchecked((int)0x80010028);

    /// <summary>
    /// 注册超时。
    /// </summary>
    internal const int ErrorEnrollTimeout = unchecked((int)0x80010100);

    /// <summary>
    /// 注册时命中重复。
    /// </summary>
    internal const int ErrorEnrollDuplicate = unchecked((int)0x80010101);

    /// <summary>
    /// 注册结束。
    /// </summary>
    internal const int ErrorEnrollEnd = unchecked((int)0x80010102);

    /// <summary>
    /// 注册失败。
    /// </summary>
    internal const int ErrorEnrollFailed = unchecked((int)0x80010103);

    /// <summary>
    /// 注册成功。
    /// </summary>
    internal const int EnrollSucceed = unchecked((int)0x80010104);

    /// <summary>
    /// 识别超时。
    /// </summary>
    internal const int ErrorIdentifyTimeout = unchecked((int)0x80010110);

    /// <summary>
    /// 识别结束。
    /// </summary>
    internal const int ErrorIdentifyEnd = unchecked((int)0x80010111);

    /// <summary>
    /// 识别失败（未命中）。
    /// </summary>
    internal const int ErrorIdentifyFailed = unchecked((int)0x80010112);

    /// <summary>
    /// 识别成功。
    /// </summary>
    internal const int IdentifySucceed = unchecked((int)0x80010113);

    /// <summary>
    /// 识别超时（另一码）。
    /// </summary>
    internal const int IdentifyTimeout = unchecked((int)0x80010114);

    /// <summary>
    /// 用户离开。
    /// </summary>
    internal const int IdentifyLeave = unchecked((int)0x80010115);

    /// <summary>
    /// 采集超时。
    /// </summary>
    internal const int ErrorCaptureTimeout = unchecked((int)0x80010120);

    /// <summary>
    /// 采集结束。
    /// </summary>
    internal const int ErrorCaptureEnd = unchecked((int)0x80010121);

    /// <summary>
    /// 采集失败。
    /// </summary>
    internal const int ErrorCaptureFailed = unchecked((int)0x80010122);

    /// <summary>
    /// 采集成功。
    /// </summary>
    internal const int CaptureSucceed = unchecked((int)0x80010123);

    /// <summary>
    /// 识别采集超时。
    /// </summary>
    internal const int ErrorIdentifyCaptureTimeout = unchecked((int)0x80010200);

    /// <summary>
    /// 识别采集结束。
    /// </summary>
    internal const int ErrorIdentifyCaptureEnd = unchecked((int)0x80010201);

    /// <summary>
    /// 识别采集失败。
    /// </summary>
    internal const int ErrorIdentifyCaptureFailed = unchecked((int)0x80010202);

    /// <summary>
    /// 识别采集成功。
    /// </summary>
    internal const int IdentifyCaptureSucceed = unchecked((int)0x80010203);

    /// <summary>
    /// 压缩采集超时。
    /// </summary>
    internal const int ErrorCompressedCaptureTimeout = unchecked((int)0x80010300);

    /// <summary>
    /// 压缩采集结束。
    /// </summary>
    internal const int ErrorCompressedCaptureEnd = unchecked((int)0x80010301);

    /// <summary>
    /// 压缩采集失败。
    /// </summary>
    internal const int ErrorCompressedCaptureFailed = unchecked((int)0x80010302);

    /// <summary>
    /// 压缩采集成功。
    /// </summary>
    internal const int CompressedCaptureSucceed = unchecked((int)0x80010303);

    /// <summary>
    /// 底库中无匹配。
    /// </summary>
    internal const int NoMatchFound = unchecked((int)0x80011001);

    /// <summary>
    /// 该机型不支持此功能。
    /// </summary>
    internal const int NotSupported = unchecked((int)0x80011002);

    /// <summary>
    /// 该 API 不被支持。
    /// </summary>
    internal const int ApiNotSupported = unchecked((int)0x80011013);

    /// <summary>
    /// 未知错误。
    /// </summary>
    internal const int ErrorUnknown = unchecked((int)0x80011FFF);

    /// <summary>
    /// 子类型非法。
    /// </summary>
    internal const int InvalidSubtype = unchecked((int)0x80012012);

    /// <summary>
    /// 虹膜编码参数为空。
    /// </summary>
    internal const int IrisCodeParamNull = unchecked((int)0x80012013);

    /// <summary>
    /// 长虹膜编码参数为空。
    /// </summary>
    internal const int LongIrisCodeParamNull = unchecked((int)0x80012014);

    /// <summary>
    /// 图像尺寸超出范围。
    /// </summary>
    internal const int ImageSizeNotInRange = unchecked((int)0x80012015);

    /// <summary>
    /// 图像格式非法。
    /// </summary>
    internal const int InvalidImageType = unchecked((int)0x80012016);

    /// <summary>
    /// 汉明距离阈值超出范围。
    /// </summary>
    internal const int HdThresholdNotInRange = unchecked((int)0x80012017);

    /// <summary>
    /// 虹膜编码列表长度非法。
    /// </summary>
    internal const int InvalidIrisCodeListSize = unchecked((int)0x80012018);

    /// <summary>
    /// 比对模式非法。
    /// </summary>
    internal const int InvalidMatchingMode = unchecked((int)0x80012019);

    /// <summary>
    /// 命中下标超出范围。
    /// </summary>
    internal const int MatchIndexNotInRange = unchecked((int)0x8001201A);

    /// <summary>
    /// 命中下标无效。
    /// </summary>
    internal const int MatchIndexInvalid = unchecked((int)0x8001201B);

    /// <summary>
    /// 图像张数超出范围。
    /// </summary>
    internal const int ImageCountNotInRange = unchecked((int)0x8001201C);

    /// <summary>
    /// 图像下标参数为空。
    /// </summary>
    internal const int ImageIndexParamNull = unchecked((int)0x8001201D);

    #endregion

    #region 常量 —— 回调类型与设备事件

    /// <summary>
    /// 回调类型：注册。
    /// </summary>
    internal const int CallbackEnroll = 1;

    /// <summary>
    /// 回调类型：实时预览图。
    /// </summary>
    internal const int CallbackLiveImage = 2;

    /// <summary>
    /// 回调类型：设备状态。
    /// </summary>
    internal const int CallbackStatus = 3;

    /// <summary>
    /// 回调类型：识别。
    /// </summary>
    internal const int CallbackIdentify = 4;

    /// <summary>
    /// 回调类型：采集。
    /// </summary>
    internal const int CallbackCapture = 5;

    /// <summary>
    /// 回调类型：识别采集。
    /// </summary>
    internal const int CallbackIdentifyCapture = 6;

    /// <summary>
    /// 回调类型：压缩采集。
    /// </summary>
    internal const int CallbackCompressedCapture = 7;

    /// <summary>
    /// 设备事件：机身倾斜。
    /// </summary>
    internal const int DeviceEventTilt = 1;

    /// <summary>
    /// 设备事件：意外拔出。
    /// </summary>
    internal const int DeviceEventSurpriseRemove = 2;

    /// <summary>
    /// 设备事件：关闭。
    /// </summary>
    internal const int DeviceEventClose = 3;

    /// <summary>
    /// 设备事件：事务超时。
    /// </summary>
    internal const int DeviceEventTxnTimeout = 4;

    /// <summary>
    /// 设备事件：事务错误。
    /// </summary>
    internal const int DeviceEventTxnError = 5;

    /// <summary>
    /// 设备事件：设备插入。
    /// </summary>
    internal const int DeviceEventArrival = 6;

    /// <summary>
    /// 设备事件：在线状态变化。
    /// </summary>
    internal const int DeviceEventPresent = 7;

    /// <summary>
    /// 设备事件：人脸距离变化，取值见 <see cref="UserRange"/>。
    /// </summary>
    internal const int DeviceEventDistance = 8;

    /// <summary>
    /// 设备事件：距离区间变化，取值见 <see cref="UserRange"/>。
    /// </summary>
    internal const int DeviceEventRange = 9;

    /// <summary>
    /// 设备事件：光照。
    /// </summary>
    internal const int DeviceEventLight = 10;

    /// <summary>
    /// 设备事件：注册校验提示。
    /// </summary>
    internal const int DeviceEventEnrollVerification = 11;

    /// <summary>
    /// 设备事件：提示睁眼。
    /// </summary>
    internal const int DeviceEventOpenEyes = 12;

    /// <summary>
    /// 倾斜状态：正常。
    /// </summary>
    internal const int DeviceTiltOff = 0;

    /// <summary>
    /// 倾斜状态：颠倒。
    /// </summary>
    internal const int DeviceTiltOn = 1;

    /// <summary>
    /// 设备错误码：打开失败。
    /// </summary>
    internal const int DeviceErrOpen = 102;

    /// <summary>
    /// 设备错误码：状态非法。
    /// </summary>
    internal const int DeviceErrInvalidState = 104;

    /// <summary>
    /// 事务错误细分：图像。
    /// </summary>
    internal const int DeviceErrTxnImage = 1;

    /// <summary>
    /// 事务错误细分：照明。
    /// </summary>
    internal const int DeviceErrTxnIllumination = 2;

    /// <summary>
    /// 事务错误细分：快门。
    /// </summary>
    internal const int DeviceErrTxnShutter = 3;

    /// <summary>
    /// 事务错误细分：被中止。
    /// </summary>
    internal const int DeviceErrTxnAbort = 4;

    #endregion

    #region 常量 —— 眼睛、版本、连接与命令类型

    /// <summary>
    /// 眼睛选择：自动。
    /// </summary>
    internal const int EyeAuto = 0;

    /// <summary>
    /// 眼睛选择：左眼。
    /// </summary>
    internal const int EyeLeft = 1;

    /// <summary>
    /// 眼睛选择：右眼。
    /// </summary>
    internal const int EyeRight = 2;

    /// <summary>
    /// 眼睛选择：双眼。
    /// </summary>
    internal const int EyeBoth = 3;

    /// <summary>
    /// 眼睛选择：任意一只眼。
    /// </summary>
    internal const int EyeEither = 4;

    /// <summary>
    /// 版本类型：设备。
    /// </summary>
    internal const int VersionDevice = 0;

    /// <summary>
    /// 版本类型：固件。
    /// </summary>
    internal const int VersionFirmware = 1;

    /// <summary>
    /// 版本类型：驱动。
    /// </summary>
    internal const int VersionDriver = 2;

    /// <summary>
    /// 版本类型：算法。
    /// </summary>
    internal const int VersionAlgorithm = 3;

    /// <summary>
    /// 版本类型：平台。
    /// </summary>
    internal const int VersionPlatform = 4;

    /// <summary>
    /// <see cref="biospi_get_version"/> 输出缓冲区的推荐容量，与头文件的
    /// <c>BioSPI_VERSION_LENGTH</c> 一致。
    /// </summary>
    internal const int VersionBufferLength = 260;

    /// <summary>
    /// 连接类型：普通应用程连接。
    /// </summary>
    internal const int AttachNormal = 0;

    /// <summary>
    /// 连接类型：服务程序连接。
    /// </summary>
    internal const int AttachService = 1;

    /// <summary>
    /// 连接类型：自动普通连接。
    /// </summary>
    internal const int AttachNormalAuto = 2;

    /// <summary>
    /// 连接类型：自动服务连接。
    /// </summary>
    internal const int AttachServiceAuto = 3;

    /// <summary>
    /// 连接类型：支持睡眠的普通连接。
    /// </summary>
    internal const int AttachNormalSleep = 4;

    /// <summary>
    /// 连接类型：支持睡眠的服务连接。
    /// </summary>
    internal const int AttachServiceSleep = 5;

    /// <summary>
    /// 连接类型：NET 算法引擎。
    /// </summary>
    internal const int AttachNormalNet = 8;

    /// <summary>
    /// 连接类型：NET 特殊。
    /// </summary>
    internal const int AttachNormalNetSpec = 16;

    /// <summary>
    /// 命令类型：同步。
    /// </summary>
    internal const int CmdTypeSync = 0;

    /// <summary>
    /// 命令类型：异步，结果走回调。
    /// </summary>
    internal const int CmdTypeAsync = 1;

    /// <summary>
    /// 断开类型：普通。
    /// </summary>
    internal const int NormalDetach = 0;

    /// <summary>
    /// 断开类型：设备被移除。
    /// </summary>
    /// <remarks>
    /// 头文件此值写作 <c>0x80000000</c>，而厂商托管封装 <c>EyeIrisPlatformAx</c> 里写作
    /// <c>1</c>，两者不一致。此处按头文件（原生契约）取 <c>0x80000000</c>；
    /// 若实测 <see cref="biospi_detach"/> 行为不符，回来核对这一条。
    /// </remarks>
    internal const int DeviceRemoveDetach = unchecked((int)0x80000000);

    /// <summary>
    /// 采集触发方式：自动。
    /// </summary>
    internal const int CaptureTypeAuto = 0;

    /// <summary>
    /// 采集触发方式：手动。
    /// </summary>
    internal const int CaptureTypeManual = 1;

    /// <summary>
    /// 采集优先级：速度优先。
    /// </summary>
    internal const int PrioritySpeed = 0;

    /// <summary>
    /// 采集优先级：质量优先。
    /// </summary>
    internal const int PriorityQuality = 1;

    /// <summary>
    /// 采集优先级：注册速度优先。
    /// </summary>
    internal const int PriorityEnrollSpeed = 2;

    #endregion

    #region 设备连接

    /// <summary>
    /// 连接 SDK 与设备。
    /// </summary>
    /// <param name="attachType">
    /// 连接类型，见 <see cref="AttachNormal"/> 等。
    /// </param>
    /// <param name="serviceHandle">
    /// 服务模式用的句柄；普通模式传 <see cref="IntPtr.Zero"/>。
    /// </param>
    /// <param name="serviceName">
    /// 服务模式用的名称；普通模式传空串或 <see langword="null"/>。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功；失败后须先 <see cref="biospi_detach"/> 再重试。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int biospi_attach(uint attachType, IntPtr serviceHandle, string? serviceName);

    /// <summary>
    /// 设置系统参数。
    /// </summary>
    /// <param name="config">
    /// 按值传入的参数结构。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_config(BioSpiConfig config);

    /// <summary>
    /// 断开 SDK 与设备。退出应用前必须调用。
    /// </summary>
    /// <param name="reserved">
    /// 断开类型，见 <see cref="NormalDetach"/> / <see cref="DeviceRemoveDetach"/>。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_detach(uint reserved);

    /// <summary>
    /// 取设备信息。头文件里的 <c>biospi_device_info</c>；厂商托管封装漏了这一个。
    /// </summary>
    /// <param name="deviceInfo">
    /// 接收设备信息。结构含数组字段，调用前须先给各数组字段赋好实例。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 这是 SBI 的 DEVICE_INFO 操作要的数据来源——厂商代码、型号、设备编号都在里面。
    /// 是否需要在 <see cref="biospi_attach"/> 之后、其他操作之前调用，头文件未说明，实测确认。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_device_info(ref BioSpiDevice deviceInfo);

    /// <summary>
    /// 取硬件或软件版本号。
    /// </summary>
    /// <param name="type">
    /// 版本类型，见 <see cref="VersionDevice"/> 等。
    /// </param>
    /// <param name="version">
    /// 接收版本字符串，容量按 <see cref="VersionBufferLength"/> 开。
    /// </param>
    /// <param name="length">
    /// <paramref name="version"/> 的容量。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 头文件的 C++ 默认容量是 50，而 <c>BioSPI_VERSION_LENGTH</c> 是 260。
    /// 别传 50 —— 版本串里有超出的可能，传小了会被截断或写穿。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int biospi_get_version(int type, StringBuilder version, int length);

    /// <summary>
    /// 取虹膜短编码长度。
    /// </summary>
    /// <param name="length">
    /// 传出长度。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 头文件声明为 <c>long *</c>。Windows 的 C <c>long</c> 是 4 字节，故此处是
    /// <c>int</c> 而非 <c>long</c>（厂商托管封装在这个函数上用的正是 Int32）。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_getshortcode_length(ref int length);

    /// <summary>
    /// 取虹膜长编码长度。
    /// </summary>
    /// <param name="length">
    /// 传出长度。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_getlongcode_length(ref int length);

    /// <summary>
    /// 设置曝光。
    /// </summary>
    /// <param name="value">
    /// <c>&lt;= 0</c> 为自动曝光；<c>1..16</c> 为 16 级手动曝光，1 最小。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_set_exposure(int value);

    /// <summary>
    /// 控制指示灯闪烁。
    /// </summary>
    /// <param name="value">
    /// 位掩码：bit0 = 红光闪，bit1 = 绿灯闪，bit2 = 蓝光闪。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_set_led_flicker(int value);

    /// <summary>
    /// 控制红外补光灯工作方式。
    /// </summary>
    /// <param name="value">
    /// 1 = 常亮，0 = 常灭。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_set_ir_led_mode(int value);

    /// <summary>
    /// 控制蜂鸣器。头文件里的 <c>biospi_set_beeper</c>；厂商托管封装漏了这一个。
    /// </summary>
    /// <param name="beepType">
    /// 0 = 蜂鸣一声。其余取值头文件未定义，标注为“保留待扩展”。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_set_beeper(int beepType);

    /// <summary>
    /// 停止当前的注册、采集或识别。
    /// </summary>
    /// <param name="reserved">
    /// 保留，传 0。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_cancel(int reserved);

    #endregion

    #region 注册、识别与采集

    /// <summary>
    /// 注册虹膜模板。
    /// </summary>
    /// <param name="cmdType">
    /// <see cref="CmdTypeAsync"/> 时结果走回调；<see cref="CmdTypeSync"/> 时用下面的出参。
    /// </param>
    /// <param name="enrollParams">
    /// 注册参数，按值传入。
    /// </param>
    /// <param name="leftImageQuality">
    /// 左眼图像质量；同步模式用。
    /// </param>
    /// <param name="leftTemplate">
    /// 接收左眼模板的缓冲区，容量按 <see cref="biospi_getshortcode_length"/> 的长度 × 2；
    /// 传 <see langword="null"/> 表示不需要。同步模式用。
    /// </param>
    /// <param name="leftImage">
    /// 接收左眼图像（1 幅 VGA）的缓冲区，容量 <see cref="IrisImageSize"/> × 2；
    /// 传 <see langword="null"/> 表示不需要。同步模式用。
    /// </param>
    /// <param name="rightImageQuality">
    /// 右眼图像质量；同步模式用。
    /// </param>
    /// <param name="rightTemplate">
    /// 接收右眼模板的缓冲区，规则同 <paramref name="leftTemplate"/>。
    /// </param>
    /// <param name="rightImage">
    /// 接收右眼图像的缓冲区，规则同 <paramref name="leftImage"/>。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 异步模式下所有出参都被忽略，但仍要传入（<c>ref</c> 参数不能省略），传局部变量即可。
    /// </para>
    /// <para>
    /// 左右眼的图像缓冲各接 2 幅 VGA 图（注册会连采两张），故容量是
    /// <see cref="IrisImageSize"/> 的 2 倍；模板缓冲则是每组编码长度的 2 倍。
    /// </para>
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_enroll(int cmdType, BioSpiParams enrollParams,
        ref int leftImageQuality, byte[]? leftTemplate, byte[]? leftImage,
        ref int rightImageQuality, byte[]? rightTemplate, byte[]? rightImage);

    /// <summary>
    /// 识别虹膜（1:N，比对内部模板池）。
    /// </summary>
    /// <param name="cmdType">
    /// <see cref="CmdTypeSync"/> 或 <see cref="CmdTypeAsync"/>。
    /// </param>
    /// <param name="identifyParams">
    /// 识别参数，按值传入。
    /// </param>
    /// <param name="leftIndex">
    /// 左眼命中的模板序号；<c>-1</c> 表示未命中。同步模式用。
    /// </param>
    /// <param name="rightIndex">
    /// 右眼命中的模板序号；<c>-1</c> 表示未命中。同步模式用。
    /// </param>
    /// <param name="leftImage">
    /// 接收左眼图像的缓冲区，容量 <see cref="IrisImageSize"/>。
    /// </param>
    /// <param name="rightImage">
    /// 接收右眼图像的缓冲区，容量 <see cref="IrisImageSize"/>。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 返回的下标是“<see cref="biospi_preset_template"/> 灌入顺序中的第几个”，不含用户信息，
    /// 调用方须自己维护序号到用户的映射。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_identify(int cmdType, BioSpiParams identifyParams,
        ref int leftIndex, ref int rightIndex, byte[]? leftImage, byte[]? rightImage);

    /// <summary>
    /// 只采集虹膜图像，不做比对。
    /// </summary>
    /// <param name="cmdType">
    /// <see cref="CmdTypeSync"/> 或 <see cref="CmdTypeAsync"/>。
    /// </param>
    /// <param name="params">
    /// 采集参数，按值传入。
    /// </param>
    /// <param name="leftQuality">
    /// 左眼图像质量。同步模式用。
    /// </param>
    /// <param name="rightQuality">
    /// 右眼图像质量。同步模式用。
    /// </param>
    /// <param name="leftImage">
    /// 接收左眼图像的缓冲区，容量 <see cref="IrisImageSize"/>。
    /// </param>
    /// <param name="rightImage">
    /// 接收右眼图像的缓冲区，容量 <see cref="IrisImageSize"/>。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_capture(int cmdType, BioSpiParams @params,
        ref int leftQuality, ref int rightQuality, byte[]? leftImage, byte[]? rightImage);

    /// <summary>
    /// 采集虹膜模板，不做比对。
    /// </summary>
    /// <param name="cmdType">
    /// <see cref="CmdTypeSync"/> 或 <see cref="CmdTypeAsync"/>。
    /// </param>
    /// <param name="params">
    /// 采集参数，按值传入。
    /// </param>
    /// <param name="leftImageQuality">
    /// 左眼图像质量。同步模式用。
    /// </param>
    /// <param name="leftTemplate">
    /// 接收左眼模板的缓冲区。
    /// </param>
    /// <param name="leftImage">
    /// 接收左眼图像的缓冲区。传 <see langword="null"/> 表示不需要。
    /// </param>
    /// <param name="rightImageQuality">
    /// 右眼图像质量。同步模式用。
    /// </param>
    /// <param name="rightTemplate">
    /// 接收右眼模板的缓冲区。
    /// </param>
    /// <param name="rightImage">
    /// 接收右眼图像的缓冲区。传 <see langword="null"/> 表示不需要。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_identify_capture(int cmdType, BioSpiParams @params,
        ref int leftImageQuality, byte[]? leftTemplate, byte[]? leftImage,
        ref int rightImageQuality, byte[]? rightTemplate, byte[]? rightImage);

    /// <summary>
    /// 连续采集虹膜模板，期间不停止采集。
    /// </summary>
    /// <param name="cmdType">
    /// 仅支持 <see cref="CmdTypeAsync"/>，结果全部走回调。
    /// </param>
    /// <param name="params">
    /// 采集参数，按值传入。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_identify_capture_no_halt(int cmdType, BioSpiParams @params);

    /// <summary>
    /// 采集压缩后的虹膜图像。仅支持异步，结果走
    /// <see cref="CallbackCompressedCapture"/> 回调。
    /// </summary>
    /// <param name="params">
    /// 采集参数，按值传入。
    /// </param>
    /// <param name="compressedQuality">
    /// 压缩质量档位，见 <see cref="JpegQuality80"/> 等。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 要压缩图优先走这个接口，别去调 <see cref="image_to_jpeg"/> —— 后者的内存归属有问题，
    /// 见其备注。这里图像经回调给出，无需调用方释放。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_compressed_capture(BioSpiParams @params, int compressedQuality);

    /// <summary>
    /// 强制立即采集一次。异步模式下可随时调用，图像经回调给出。
    /// </summary>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_forced_capture();

    #endregion

    #region 模板池与比对

    /// <summary>
    /// 重载模板池，覆盖原有全部内容。
    /// </summary>
    /// <param name="eyes">
    /// 眼睛标志：<see cref="EyeLeft"/> 或 <see cref="EyeRight"/>。
    /// </param>
    /// <param name="templateSize">
    /// 模板个数。
    /// </param>
    /// <param name="templateArray">
    /// 模板数据，<paramref name="templateSize"/> 组首尾相接。
    /// 个数为 0 时也要传一个非空数组（用于清空模板池）。
    /// </param>
    /// <param name="templateId">
    /// 每组模板对应的用户 ID，长度与 <paramref name="templateSize"/> 相同；
    /// 传 <see langword="null"/> 表示不设 ID。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 这里灌入的顺序，就是 <see cref="biospi_identify"/> 返回下标的依据，两者必须一致。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_preset_template(uint eyes, uint templateSize,
        byte[] templateArray, uint[]? templateId);

    /// <summary>
    /// 向模板池追加模板。
    /// </summary>
    /// <param name="eyes">
    /// 眼睛标志。
    /// </param>
    /// <param name="templateSize">
    /// 追加的模板个数。
    /// </param>
    /// <param name="templateArray">
    /// 模板数据。
    /// </param>
    /// <param name="templateId">
    /// 传出新增模板的起始序号，长度与 <paramref name="templateSize"/> 相同。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_add_template(uint eyes, uint templateSize,
        byte[] templateArray, uint[] templateId);

    /// <summary>
    /// 更新模板池中已有模板。
    /// </summary>
    /// <param name="eyes">
    /// 眼睛标志。
    /// </param>
    /// <param name="templateSize">
    /// 更新的模板个数。
    /// </param>
    /// <param name="templateId">
    /// 要更新的模板序号，长度与 <paramref name="templateSize"/> 相同。
    /// </param>
    /// <param name="templateArray">
    /// 新模板数据。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_update_template(uint eyes, uint templateSize,
        uint[] templateId, byte[] templateArray);

    /// <summary>
    /// 在调用方给定的模板集合里做比对。
    /// </summary>
    /// <param name="templateSize">
    /// 待比对集合的模板个数。
    /// </param>
    /// <param name="storedTemplateArray">
    /// 待比对的模板集合，首尾相接。
    /// </param>
    /// <param name="matchTemplate">
    /// 要匹配的单个模板。
    /// </param>
    /// <param name="index">
    /// 传出命中位置（自 0 起）；未命中时为负。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// <b>与 <see cref="biospi_identify"/> 的区别：这里不碰内部模板池，集合由调用方传入。</b>
    /// 正因如此它才是无状态形态——MOSIP 那边 BioSDK 的 <c>match(probe, gallery)</c>
    /// 就是这个语义。注意集合是“模板数组”，不是数据库。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int biospi_match(uint templateSize,
        byte[] storedTemplateArray, byte[] matchTemplate, ref int index);

    #endregion

    #region 图像与格式转换

    /// <summary>
    /// JPEG 解码为 VGA 灰度图。
    /// </summary>
    /// <param name="jpegData">
    /// JPEG 数据。
    /// </param>
    /// <param name="jpegDataSize">
    /// JPEG 数据长度。头文件声明为 C <c>long</c>，Windows 上是 4 字节，故用 <see cref="int"/>。
    /// </param>
    /// <param name="vgaImage">
    /// 接收 VGA 图像的缓冲区，由调用方分配。
    /// </param>
    /// <param name="width">
    /// 传出图像宽度。
    /// </param>
    /// <param name="height">
    /// 传出图像高度。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern int jpeg_to_image(byte[] jpegData, int jpegDataSize,
        byte[] vgaImage, ref int width, ref int height);

    /// <summary>
    /// VGA 灰度图编码为 JPEG。
    /// </summary>
    /// <param name="vgaImage">
    /// VGA 图像。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="compressedQuality">
    /// 压缩质量 0~100，越大越清晰。
    /// </param>
    /// <param name="jpegDataSize">
    /// 传出 JPEG 数据长度。
    /// </param>
    /// <returns>
    /// 指向库内部分配的 JPEG 数据的指针。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>返回值的内存归属有问题，用前先向厂商确认。</b>头文件写“内存由外部释放”，
    /// 但库并未导出对应的释放函数——它内部用 C++ 堆分配，从托管侧直接
    /// <c>FreeHGlobal</c> 跨 CRT 释放是不安全的。
    /// </para>
    /// <para>
    /// 厂商托管封装把它声明成 <c>byte[]</c> 返回，那样 CLR 会复制数据、却不会释放原生块，
    /// <b>每次调用泄漏一块</b>。此处按原样返回 <see cref="IntPtr"/>，把归属问题显式暴露出来，
    /// 而不是埋进声明里。
    /// </para>
    /// <para>
    /// 要 JPEG 建议改用 <see cref="biospi_compressed_capture"/>，那条路没有归属问题。
    /// </para>
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern IntPtr image_to_jpeg(byte[] vgaImage, int width, int height,
        int compressedQuality, ref int jpegDataSize);

    /// <summary>
    /// VGA 灰度图编码为 JPEG2000。
    /// </summary>
    /// <param name="vgaImage">
    /// VGA 图像。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="compressedRatio">
    /// 压缩比 1~100，越小越清晰，1 为最高质量。
    /// </param>
    /// <param name="jp2DataSize">
    /// 传出 JPEG2000 数据长度。
    /// </param>
    /// <returns>
    /// 指向库内部分配的 JPEG2000 数据的指针。
    /// </returns>
    /// <remarks>
    /// 返回值的归属问题同 <see cref="image_to_jpeg"/>，先向厂商确认释放方式。
    /// 这是产出 ISO/IEC 19794-6 允许的 <c>MONO_JPEG2000</c> 格式的通路。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
    internal static extern IntPtr image_to_jp2(byte[] vgaImage, int width, int height,
        int compressedRatio, ref int jp2DataSize);

    /// <summary>
    /// 从图像文件生成虹膜模板。
    /// </summary>
    /// <param name="imageFile">
    /// 输入图像文件的路径（ANSI 字符串）。
    /// </param>
    /// <param name="data">
    /// 接收模板数据的缓冲区，由调用方分配，容量按
    /// <see cref="biospi_getshortcode_length"/> 取。
    /// </param>
    /// <param name="dataLen">
    /// 传出模板数据长度。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 入参是<b>文件路径</b>而不是图像缓冲区，库自己去读盘。要处理内存里的图像，
    /// 得先落盘，或者改用采集路径拿模板。
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    internal static extern int image_to_template(string imageFile, byte[] data, ref uint dataLen);

    #endregion

    #region 回调注册

    /// <summary>
    /// 注册状态回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackStatus"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    /// <remarks>
    /// 以下 7 个方法都指向同一个原生导出 <c>biospi_set_callback</c>，<paramref name="eventType"/>
    /// 即它的第一个参数。拆成 7 个是为了让委托类型强类型化。
    /// <para>
    /// <b>委托实例必须由调用方持有到不再需要为止</b>：只传进去而不保存引用，GC 一旦回收，
    /// 原生侧留下的就是野函数指针，随后设备事件触发即崩溃。设备类里用字段存住它们。
    /// </para>
    /// </remarks>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetStatusCallback(int eventType, FireOnStatusNotify callback, IntPtr context);

    /// <summary>
    /// 注册实时预览图回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackLiveImage"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetLiveImageCallback(int eventType, FireOnLiveImage callback, IntPtr context);

    /// <summary>
    /// 注册注册结果回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackEnroll"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetEnrollCallback(int eventType, FireOnEnrollNotify callback, IntPtr context);

    /// <summary>
    /// 注册识别结果回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackIdentify"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetIdentifyCallback(int eventType, FireOnIdentifyNotify callback, IntPtr context);

    /// <summary>
    /// 注册采集结果回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackCapture"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetCaptureCallback(int eventType, FireOnCaptureNotify callback, IntPtr context);

    /// <summary>
    /// 注册识别采集结果回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackIdentifyCapture"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetIdentifyCaptureCallback(int eventType, FireOnIdentifyCaptureNotify callback, IntPtr context);

    /// <summary>
    /// 注册压缩采集结果回调。
    /// </summary>
    /// <param name="eventType">
    /// 固定传 <see cref="CallbackCompressedCapture"/>。
    /// </param>
    /// <param name="callback">
    /// 回调委托。
    /// </param>
    /// <param name="context">
    /// 原样回传给回调的上下文指针。
    /// </param>
    /// <returns>
    /// <see cref="NoError"/> 表示成功。
    /// </returns>
    [DllImport(Library, CallingConvention = CallingConvention.StdCall, EntryPoint = "biospi_set_callback")]
    internal static extern int SetCompressedCaptureCallback(int eventType, FireOnCompressedCaptureNotify callback, IntPtr context);

    #endregion

    #region 回调委托

    /// <summary>
    /// 状态回调。
    /// </summary>
    /// <param name="type">
    /// 状态类型，见 <see cref="DeviceEventTilt"/> 等 <c>DeviceEvent*</c> 常量。
    /// </param>
    /// <param name="value">
    /// 状态值，含义随 <paramref name="type"/> 而定。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    internal delegate void FireOnStatusNotify(int type, int value, IntPtr context);

    /// <summary>
    /// 实时预览图回调。用于把采集到的画面投到界面上。
    /// </summary>
    /// <param name="eye">
    /// 眼别。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="liveImage">
    /// 灰度图像数据，长度 <paramref name="width"/> × <paramref name="height"/>。
    /// </param>
    /// <param name="imageSize">
    /// 图像数据字节数。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    /// <remarks>
    /// <paramref name="liveImage"/> 只在回调期间有效，要留存须当场
    /// <see cref="Marshal.Copy(IntPtr, byte[], int, int)"/> 拷走。
    /// <b>本回调由设备线程调用，不是 UI 线程</b>，碰控件前须自行切回。
    /// </remarks>
    internal delegate void FireOnLiveImage(int eye, int width, int height, IntPtr liveImage, int imageSize, IntPtr context);

    /// <summary>
    /// 注册结果回调。
    /// </summary>
    /// <param name="result">
    /// <c>&gt;0</c> 为成功，其值是按位或的 <see cref="EyeLeft"/> / <see cref="EyeRight"/>；
    /// <c>&lt;0</c> 为错误码。
    /// </param>
    /// <param name="leftIndex">
    /// 命中重复时左眼所在的模板序号；仅当 <paramref name="result"/> 为
    /// <see cref="ErrorEnrollDuplicate"/> 时有意义。
    /// </param>
    /// <param name="rightIndex">
    /// 命中重复时右眼所在的模板序号。
    /// </param>
    /// <param name="leftImageQuality">
    /// 左眼图像质量；<c>&lt;= 50</c> 视为该眼注册失败。
    /// </param>
    /// <param name="rightImageQuality">
    /// 右眼图像质量；<c>&lt;= 50</c> 视为该眼注册失败。
    /// </param>
    /// <param name="leftTemplate">
    /// 左眼模板数据。
    /// </param>
    /// <param name="rightTemplate">
    /// 右眼模板数据。
    /// </param>
    /// <param name="leftImage">
    /// 左眼图像，2 幅 VGA 首尾相接。
    /// </param>
    /// <param name="rightImage">
    /// 右眼图像，2 幅 VGA 首尾相接。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    /// <remarks>
    /// 所有指针只在回调期间有效，须当场拷走。本回调由设备线程调用。
    /// </remarks>
    internal delegate void FireOnEnrollNotify(int result, int leftIndex, int rightIndex,
        uint leftImageQuality, uint rightImageQuality,
        IntPtr leftTemplate, IntPtr rightTemplate, IntPtr leftImage, IntPtr rightImage, IntPtr context);

    /// <summary>
    /// 识别结果回调。
    /// </summary>
    /// <param name="result">
    /// <c>&gt;0</c> 为成功，其值是按位或的 <see cref="EyeLeft"/> / <see cref="EyeRight"/>；
    /// <c>&lt;0</c> 为错误码。
    /// </param>
    /// <param name="leftIndex">
    /// 左眼命中的模板序号；<c>-1</c> 为未命中。
    /// </param>
    /// <param name="rightIndex">
    /// 右眼命中的模板序号；<c>-1</c> 为未命中。
    /// </param>
    /// <param name="leftImageQuality">
    /// 左眼图像质量。
    /// </param>
    /// <param name="rightImageQuality">
    /// 右眼图像质量。
    /// </param>
    /// <param name="leftImage">
    /// 左眼图像。
    /// </param>
    /// <param name="rightImage">
    /// 右眼图像。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    /// <remarks>
    /// <paramref name="leftIndex"/> / <paramref name="rightIndex"/> 是模板池序号，
    /// 须由调用方映射回用户。所有指针只在回调期间有效。
    /// </remarks>
    internal delegate void FireOnIdentifyNotify(int result, int leftIndex, int rightIndex,
        uint leftImageQuality, uint rightImageQuality,
        IntPtr leftImage, IntPtr rightImage, IntPtr context);

    /// <summary>
    /// 采集结果回调。
    /// </summary>
    /// <param name="result">
    /// <c>&gt;0</c> 为成功，其值是按位或的 <see cref="EyeLeft"/> / <see cref="EyeRight"/>；
    /// <c>&lt;0</c> 为错误码。
    /// </param>
    /// <param name="leftQuality">
    /// 左眼图像质量。
    /// </param>
    /// <param name="rightQuality">
    /// 右眼图像质量。
    /// </param>
    /// <param name="leftImage">
    /// 左眼图像，1 幅 VGA。
    /// </param>
    /// <param name="rightImage">
    /// 右眼图像，1 幅 VGA。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    internal delegate void FireOnCaptureNotify(int result, uint leftQuality, uint rightQuality,
        IntPtr leftImage, IntPtr rightImage, IntPtr context);

    /// <summary>
    /// 识别采集结果回调。
    /// </summary>
    /// <param name="result">
    /// <c>&gt;0</c> 为成功，其值是按位或的 <see cref="EyeLeft"/> / <see cref="EyeRight"/>；
    /// <c>&lt;0</c> 为错误码。
    /// </param>
    /// <param name="leftQuality">
    /// 左眼图像质量。
    /// </param>
    /// <param name="rightQuality">
    /// 右眼图像质量。
    /// </param>
    /// <param name="leftTemplate">
    /// 左眼模板数据。
    /// </param>
    /// <param name="rightTemplate">
    /// 右眼模板数据。
    /// </param>
    /// <param name="leftImage">
    /// 左眼图像，1 幅 VGA。
    /// </param>
    /// <param name="rightImage">
    /// 右眼图像，1 幅 VGA。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    internal delegate void FireOnIdentifyCaptureNotify(int result, uint leftQuality, uint rightQuality,
        IntPtr leftTemplate, IntPtr rightTemplate, IntPtr leftImage, IntPtr rightImage, IntPtr context);

    /// <summary>
    /// 压缩采集结果回调。
    /// </summary>
    /// <param name="result">
    /// <c>&gt;0</c> 为成功，其值是按位或的 <see cref="EyeLeft"/> / <see cref="EyeRight"/>；
    /// <c>&lt;0</c> 为错误码。
    /// </param>
    /// <param name="leftSize">
    /// 左眼压缩图字节数。
    /// </param>
    /// <param name="rightSize">
    /// 右眼压缩图字节数。
    /// </param>
    /// <param name="leftQuality">
    /// 左眼图像质量。
    /// </param>
    /// <param name="rightQuality">
    /// 右眼图像质量。
    /// </param>
    /// <param name="leftCompressed">
    /// 左眼压缩图数据。
    /// </param>
    /// <param name="rightCompressed">
    /// 右眼压缩图数据。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文。
    /// </param>
    internal delegate void FireOnCompressedCaptureNotify(int result, int leftSize, int rightSize,
        uint leftQuality, uint rightQuality,
        IntPtr leftCompressed, IntPtr rightCompressed, IntPtr context);

    #endregion

    /// <summary>
    /// 用户距离区间，对应原生 <c>enum USER_RANGE</c>。
    /// </summary>
    /// <remarks>
    /// 经 <see cref="FireOnStatusNotify"/> 在 <see cref="DeviceEventDistance"/> 或
    /// <see cref="DeviceEventRange"/> 事件下以 <c>value</c> 给出，用来提示用户站远/靠近。
    /// </remarks>
    internal enum UserRange
    {
        /// <summary>
        /// 太远。
        /// </summary>
        TooFar = 0,

        /// <summary>
        /// 偏远，提示用户靠近。
        /// </summary>
        Far = 1,

        /// <summary>
        /// 距离合适。
        /// </summary>
        Ok = 2,

        /// <summary>
        /// 偏近，提示用户远离。
        /// </summary>
        Near = 3
    }

    /// <summary>
    /// 对应原生 <c>BioSPI_PARAMS</c>，采集/注册/识别参数。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按值传给原生函数，<b>字段布局即 ABI，不能增删改序</b>。原生 7 个 <c>int</c>（含
    /// <c>long type</c> / <c>long priority</c> —— Windows 的 C <c>long</c> 也是 4 字节），
    /// 共 28 字节，与厂商托管封装 2.9.1.0 逐字段核对一致。
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BioSpiParams
    {
        /// <summary>
        /// 眼睛选择，见 <see cref="EyeAuto"/> 等。
        /// </summary>
        internal int Eyes;

        /// <summary>
        /// 质量门限。
        /// </summary>
        internal int Quality;

        /// <summary>
        /// 瞳孔曝光值。
        /// </summary>
        internal int EyeExpo;

        /// <summary>
        /// 超时（秒）；0 表示不限时。
        /// </summary>
        internal int TimeOut;

        /// <summary>
        /// 采集触发方式，见 <see cref="CaptureTypeAuto"/>；仅手动采集模式有效。
        /// </summary>
        internal int Type;

        /// <summary>
        /// 优先级，见 <see cref="PrioritySpeed"/>；仅手动采集模式有效。
        /// </summary>
        internal int Priority;

        /// <summary>
        /// 指定注册采集虹膜的区域标志，8 位为一组，默认 <c>0x00000303</c>
        /// （两组相同，表示左右眼用同一设定）。
        /// </summary>
        internal int Size;
    }

    /// <summary>
    /// 对应原生 <c>BioSPI_CONFIG</c>，系统参数。
    /// </summary>
    /// <remarks>
    /// 按值传给 <see cref="biospi_config"/>，5 个 <c>int</c> 共 20 字节。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BioSpiConfig
    {
        /// <summary>
        /// 识别阈值档位 1~10，默认 5。
        /// </summary>
        internal int IrisThresholdLevel;

        /// <summary>
        /// 活体开关：0 关、1 开，默认关。
        /// </summary>
        internal int EnableAntiSpoof;

        /// <summary>
        /// 一次识别的最长时间（秒），默认 5。
        /// </summary>
        internal int DurationOnce;

        /// <summary>
        /// 图像增强开关，默认关（0）。
        /// </summary>
        internal int ImageEnhanceEnable;

        /// <summary>
        /// 灰度跳变抑制开关：0 不抑制、1 抑制，默认不抑制。
        /// </summary>
        internal int DisableGreyscale;
    }

    /// <summary>
    /// 对应原生 <c>BioSPI_DEVICE</c>，设备标识信息。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由 <see cref="biospi_device_info"/> 填充。5 个 <c>char[260]</c> 加 2 个 <c>int[260]</c>，
    /// 共 3380 字节。
    /// </para>
    /// <para>
    /// 头文件里前两组是 <c>char</c> 数组（ANSI 字符串），后两组是 <c>int</c> 数组，
    /// 顺序不能调换。因为是 <c>ref</c> 传入的结构，<b>调用前须先给全部数组字段赋好实例</b>，
    /// 否则封送会失败——这正是不把数组拆成逐个字段的原因（260 个字段不现实）。
    /// </para>
    /// <para>
    /// 字符串字段的编码取 ANSI。厂商未说明是否为 UTF-8；若读到乱码，先怀疑这里。
    /// </para>
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct BioSpiDevice
    {
        /// <summary>
        /// 设备厂商代码。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal byte[] VendorCode;

        /// <summary>
        /// 设备型号代码。头文件给出的取值：01 移动虹膜识别设备、02 虹膜识别一体机、
        /// 03 身份验证终端、04 手持机、05 远距离虹膜采集识别设备、06 虹膜门禁、
        /// 07 虹膜闸机、08 人脸+虹膜设备、99 其他。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal byte[] DeviceType;

        /// <summary>
        /// 设备编号（序列号）。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal byte[] DeviceNum;

        /// <summary>
        /// 设备型号名称。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal byte[] DeviceModel;

        /// <summary>
        /// 保留信息 1。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal byte[] ReservedInfo1;

        /// <summary>
        /// 保留信息 2。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal int[] ReservedInfo2;

        /// <summary>
        /// 保留信息 3。
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = VersionBufferLength)]
        internal int[] ReservedInfo3;
    }
}
