namespace Kinovea.PortableDesktop.Presentation;

/// <summary>Maps two media clocks onto a shared timeline measured in seconds.</summary>
/// <remarks>
/// Each media item's local start maps to its actual source time in seconds.
/// This preserves an absolute time offset between sources while allowing each
/// player to keep its native timestamp units and timebase.
/// </remarks>
public sealed class DualPlayerTimeline
{
    private readonly MediaClock first;
    private readonly MediaClock second;

    public DualPlayerTimeline(MediaClock first, MediaClock second)
    {
        first.Validate();
        second.Validate();
        this.first = first;
        this.second = second;
        StartSeconds = Math.Min(first.StartSeconds, second.StartSeconds);
        EndSeconds = Math.Max(first.EndSeconds, second.EndSeconds);
    }

    public double StartSeconds { get; }
    public double EndSeconds { get; }

    public double ToCommonSeconds(int player, long localTimestamp)
    {
        MediaClock clock = Get(player);
        return localTimestamp * clock.TimeBaseNumerator / clock.TimeBaseDenominator;
    }

    public long ToLocalTimestamp(int player, double commonSeconds)
    {
        if (!double.IsFinite(commonSeconds))
            throw new ArgumentOutOfRangeException(nameof(commonSeconds));
        MediaClock clock = Get(player);
        double local = commonSeconds * clock.TimeBaseDenominator / clock.TimeBaseNumerator;
        if (local <= clock.StartTimestamp) return clock.StartTimestamp;
        if (local >= clock.EndTimestamp) return clock.EndTimestamp;
        return checked((long)Math.Round(local, MidpointRounding.AwayFromZero));
    }

    public double ToFraction(double commonSeconds) => EndSeconds <= StartSeconds
        ? 0
        : Math.Clamp((commonSeconds - StartSeconds) / (EndSeconds - StartSeconds), 0, 1);

    public double FromFraction(double fraction)
    {
        if (!double.IsFinite(fraction))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        return StartSeconds + Math.Clamp(fraction, 0, 1) * (EndSeconds - StartSeconds);
    }

    private MediaClock Get(int player) => player switch
    {
        0 => first,
        1 => second,
        _ => throw new ArgumentOutOfRangeException(nameof(player))
    };
}

public readonly record struct MediaClock(
    long StartTimestamp,
    long EndTimestamp,
    int TimeBaseNumerator,
    int TimeBaseDenominator)
{
    public double StartSeconds => StartTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;
    public double EndSeconds => EndTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;

    public void Validate()
    {
        if (EndTimestamp < StartTimestamp)
            throw new ArgumentOutOfRangeException(nameof(EndTimestamp), "Media timestamps must form an ordered range.");
        if (TimeBaseNumerator <= 0 || TimeBaseDenominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(TimeBaseNumerator), "Media timebase values must be positive.");
    }
}
