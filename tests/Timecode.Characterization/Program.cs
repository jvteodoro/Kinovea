using System.Globalization;
using Kinovea.Services;

var assertions = new Assertions();

// MillisecondsToTimecode: zero, signs, duration fields, precision and rounding/carry.
assertions.Equal("0.00", TimeHelper.MillisecondsToTimecode(0, 2), "zero centiseconds");
assertions.Equal("- 0.50", TimeHelper.MillisecondsToTimecode(-500, 2), "negative subsecond");
assertions.Equal("1.24", TimeHelper.MillisecondsToTimecode(1245, 2), "midpoint rounds to even");
assertions.Equal("1.26", TimeHelper.MillisecondsToTimecode(1255, 2), "odd midpoint rounds to even");
assertions.Equal("1:00.00", TimeHelper.MillisecondsToTimecode(59999, 2), "seconds carry to minutes");
assertions.Equal("1:00:00.00", TimeHelper.MillisecondsToTimecode(3599999, 2), "minutes carry to hours");
assertions.Equal("10:00:00.00", TimeHelper.MillisecondsToTimecode(36000000, 2), "large duration");
assertions.Equal("0.999", TimeHelper.MillisecondsToTimecode(999.9, 3), "fraction truncates without carry above two digits");
assertions.Equal("1:01:01.234", TimeHelper.MillisecondsToTimecode(3661234, 3), "hour minute second fields");
assertions.Equal("- 1:01:01.234", TimeHelper.MillisecondsToTimecode(-3661234, 3), "negative large duration");

// All declared formats, including fallback enum members, and symbol behavior.
var basic = (framerate: 30d, frames: 42, milliseconds: 1234.6, timestamps: 1234L,
    durationTimestamps: 10000d, totalFrames: 1000d);
assertions.Equal("1.23", Format(TimecodeFormat.ClassicTime), "classic time");
assertions.Equal("42", Format(TimecodeFormat.Frames), "frames");
assertions.Equal("1235", Format(TimecodeFormat.Milliseconds), "milliseconds rounded");
assertions.Equal("1234600 µs", Format(TimecodeFormat.Microseconds, true), "microseconds with symbol");
assertions.Equal("3:42", Format(TimecodeFormat.TenThousandthOfHours), "ten-thousandth hours");
assertions.Equal("2:05", Format(TimecodeFormat.HundredthOfMinutes), "hundredth minutes");
assertions.Equal("1.23 (42)", Format(TimecodeFormat.TimeAndFrames), "time and frames");
assertions.Equal("0.123", Format(TimecodeFormat.Normalized), "normalized precision");
assertions.Equal("1234", Format(TimecodeFormat.Timestamps), "timestamps");
assertions.Equal("1.23", Format(TimecodeFormat.Unknown), "unknown falls back to classic time");
assertions.Equal("1.23", Format(TimecodeFormat.NumberOfTimeCodeFormats), "sentinel falls back to classic time");
assertions.Equal("1235 ms", Format(TimecodeFormat.Milliseconds, true), "milliseconds with symbol");
assertions.Equal("1234600", Format(TimecodeFormat.Microseconds), "microseconds without symbol");

// Frame-rate precision boundaries follow ceil(log10(framerate)).
assertions.Equal("1.23", FormatWithRate(10, 1234.56), "10 fps precision");
assertions.Equal("1.23", FormatWithRate(30, 1234.56), "30 fps precision");
assertions.Equal("1.23", FormatWithRate(100, 1234.56), "100 fps precision");
assertions.Equal("1.234", FormatWithRate(1000, 1234.56), "1000 fps precision");
assertions.Equal("1.2345", FormatWithRate(10000, 1234.56), "10000 fps precision");

// Current culture is observable for decimal output.
var previousCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    assertions.Equal("1.23", TimeHelper.MillisecondsToTimecode(1234, 2), "classic time uses a literal decimal point");
    assertions.Equal("0,123", Format(TimecodeFormat.Normalized), "French normalized decimal separator");
}
finally
{
    CultureInfo.CurrentCulture = previousCulture;
}

// Normalization and timestamp conversion boundaries.
assertions.Equal("1.000", FormatNormalized(10000, 10000, 1000), "normalized endpoint");
assertions.Equal("-0.001", FormatNormalized(-10, 10000, 1000), "negative normalized value");
assertions.Equal("Infinity", FormatNormalized(1, 0, 1000), "zero duration produces infinity");
assertions.Throws<ArgumentOutOfRangeException>(() => FormatNormalized(0, 1, 0), "zero total frames creates negative precision");
assertions.Equal("-1", FormatTimestamp(long.MaxValue), "timestamp narrows to signed 32-bit");
assertions.Equal("0", FormatTimestamp(1L << 32), "timestamp narrowing wraps at 32 bits");
assertions.Equal(int.MaxValue.ToString(CultureInfo.InvariantCulture), FormatTimestamp(int.MaxValue), "timestamp int max");
assertions.Equal(int.MinValue.ToString(CultureInfo.InvariantCulture), FormatTimestamp(int.MinValue), "timestamp int min");
assertions.Equal(17L, TimeHelper.IdentityTimestampMapper(17), "identity timestamp mapper");
foreach (var format in Enum.GetValues<TimecodeFormat>())
{
    var expectedType = format is TimecodeFormat.Frames or TimecodeFormat.Milliseconds or
        TimecodeFormat.Microseconds or TimecodeFormat.TenThousandthOfHours or
        TimecodeFormat.HundredthOfMinutes or TimecodeFormat.Timestamps
        ? TimecodeType.Number
        : TimecodeType.String;
    assertions.Equal(expectedType, TimeHelper.GetTimecodeType(format), $"type mapping: {format}");
}

Console.WriteLine($"PASS: {assertions.Count} timecode characterization assertions");

string Format(TimecodeFormat format, bool symbol = false) => TimeHelper.GetTimestring(
    basic.framerate, basic.frames, basic.milliseconds, basic.timestamps,
    basic.durationTimestamps, basic.totalFrames, format, symbol);

string FormatWithRate(double framerate, double milliseconds) => TimeHelper.GetTimestring(
    framerate, basic.frames, milliseconds, basic.timestamps,
    basic.durationTimestamps, basic.totalFrames, TimecodeFormat.ClassicTime, false);

string FormatNormalized(long timestamps, double duration, double totalFrames) => TimeHelper.GetTimestring(
    30, 0, 0, timestamps, duration, totalFrames, TimecodeFormat.Normalized, false);

string FormatTimestamp(long timestamps) => TimeHelper.GetTimestring(
    30, 0, 0, timestamps, 0, 0, TimecodeFormat.Timestamps, false);

sealed class Assertions
{
    public int Count { get; private set; }

    public void Equal<T>(T expected, T actual, string name)
    {
        Count++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected <{expected}>, got <{actual}>");
    }

    public void Throws<TException>(Action action, string name) where TException : Exception
    {
        Count++;
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
    }
}
