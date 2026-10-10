using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 报文里的分数：线格式是<b>数值形态的字符串</b>。
/// </summary>
/// <remarks>
/// <para>
/// CTK 的采集响应 schema 把 <c>requestedScore</c>/<c>qualityScore</c> 定义为 <c>string</c>，
/// 并用数值模式约束内容 —— 写成 JSON 数值会被 SchemaValidator 判 Failed（SBI1010 实测如此）。
/// 请求与响应两侧同型，都走这个类型。
/// </para>
/// <para>
/// 该线格式由类型承载：取值只能经 <see cref="FromScore"/>（设备给出的数值）或
/// <see cref="FromRequested"/>（请求里那串）进来，序列化一律落成字符串，
/// 不会退化成 JSON 数值。
/// </para>
/// </remarks>
[JsonConverter(typeof(SbiScoreJsonConverter))]
public readonly record struct SbiScore
{
    /// <summary>
    /// 请求方没给最低分数时报的值
    /// </summary>
    /// <remarks>
    /// <para>
    /// 响应 schema 把该字段列在 required 里，请求方未指定时也得报一个：取 0，即"未设门槛"
    /// （模式允许 0）。
    /// </para>
    /// <para>
    /// 注意这与设备侧的实际行为有差别：请求未指定时设备用的是自己的默认阈值。那个默认值在设备层，
    /// 本层取不到，故这里只能报 0。
    /// </para>
    /// </remarks>
    public static readonly SbiScore Unspecified = new("0");

    private SbiScore(string text) => Text = text;

    /// <summary>线格式文本。</summary>
    public string Text { get; }

    /// <summary>
    /// 由设备给出的分数转成线格式
    /// </summary>
    /// <param name="score">
    /// 设备给出的分数
    /// </param>
    /// <returns>
    /// 线格式分数
    /// </returns>
    /// <remarks>
    /// 不做四舍五入：设备报多少就写多少，本层不替它作主。
    /// </remarks>
    public static SbiScore FromScore(double score)
        => new(score.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// 由请求里的分数转成线格式
    /// </summary>
    /// <param name="requestedScore">
    /// 请求里的分数（字符串）
    /// </param>
    /// <returns>
    /// 线格式分数；缺失或非法时为 <see cref="Unspecified"/>
    /// </returns>
    /// <remarks>
    /// 经数值再格式化，把请求方的写法归一（<c>"60.0"</c> 与 <c>"60"</c> 同报 <c>"60"</c>）。
    /// </remarks>
    public static SbiScore FromRequested(string? requestedScore)
        => double.TryParse(requestedScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? FromScore(score)
            : Unspecified;
}

/// <summary>
/// <see cref="SbiScore"/> 的 JSON 转换器
/// </summary>
internal sealed class SbiScoreJsonConverter : JsonConverter<SbiScore>
{
    /// <inheritdoc/>
    public override SbiScore Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => SbiScore.FromRequested(reader.GetString());

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, SbiScore value, JsonSerializerOptions options)
    {
        // 结构体的 default 没走构造函数：这时按"未指定"写，不让一份响应当场序列化失败。
        writer.WriteStringValue(value.Text ?? SbiScore.Unspecified.Text);
    }
}
