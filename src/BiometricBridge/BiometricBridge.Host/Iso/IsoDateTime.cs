using System.Buffers.Binary;

namespace BiometricBridge.Host.Iso;

/// <summary>
/// ISO/IEC 19794-1 的采集日期时间编码（9 字节）
/// </summary>
/// <remarks>
/// <para>
/// 结构（全部按大端）：年 <c>uint16</c>、月、日、时、分、秒各 1 字节、毫秒 <c>uint16</c>，取值一律 UTC。
/// </para>
/// <para>
/// 各家 ISO 19794 记录的表示头都用这同一段（<c>capture date and time</c>，9 字节），故集中一处。
/// </para>
/// </remarks>
internal static class IsoDateTime
{
    /// <summary>
    /// 编码后的字节数
    /// </summary>
    public const int Size = 9;

    /// <summary>
    /// 把时刻写入目标缓冲区
    /// </summary>
    /// <param name="destination">
    /// 目标缓冲区，长度至少 <see cref="Size"/>
    /// </param>
    /// <param name="timestamp">
    /// 采集时刻
    /// </param>
    public static void Write(Span<byte> destination, DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();

        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], (ushort)utc.Year);
        destination[2] = (byte)utc.Month;
        destination[3] = (byte)utc.Day;
        destination[4] = (byte)utc.Hour;
        destination[5] = (byte)utc.Minute;
        destination[6] = (byte)utc.Second;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(7, 2), (ushort)utc.Millisecond);
    }
}
