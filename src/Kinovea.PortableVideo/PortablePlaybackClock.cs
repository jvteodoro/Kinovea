namespace Kinovea.PortableVideo;

/// <summary>
/// Maps a monotonic wall clock to media timestamps in a stream's rational
/// timebase. It does not schedule UI work or decode frames.
/// </summary>
public sealed class PortablePlaybackClock
{
    private readonly TimeProvider timeProvider;
    private int timeBaseNumerator;
    private int timeBaseDenominator;
    private long mediaAnchor;
    private long wallAnchor;
    private TimeSpan pausedElapsed;

    public PortablePlaybackClock(int timeBaseNumerator, int timeBaseDenominator, TimeProvider? timeProvider = null)
    {
        SetTimeBase(timeBaseNumerator, timeBaseDenominator);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsPlaying { get; private set; }

    public long TargetTimestamp
    {
        get
        {
            if (!IsPlaying)
                return SaturatingAdd(mediaAnchor, ToMediaTicks(pausedElapsed));
            return SaturatingAdd(mediaAnchor, ToMediaTicks(pausedElapsed + timeProvider.GetElapsedTime(wallAnchor)));
        }
    }

    public void SetTimeBase(int numerator, int denominator)
    {
        if (numerator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        timeBaseNumerator = numerator;
        timeBaseDenominator = denominator;
    }

    public void Play(long timestamp)
    {
        mediaAnchor = timestamp;
        pausedElapsed = TimeSpan.Zero;
        wallAnchor = timeProvider.GetTimestamp();
        IsPlaying = true;
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        pausedElapsed += timeProvider.GetElapsedTime(wallAnchor);
        IsPlaying = false;
    }

    public void Resume()
    {
        if (IsPlaying) return;
        wallAnchor = timeProvider.GetTimestamp();
        IsPlaying = true;
    }

    public void Stop(long timestamp = 0)
    {
        IsPlaying = false;
        mediaAnchor = timestamp;
        pausedElapsed = TimeSpan.Zero;
    }

    private long ToMediaTicks(TimeSpan elapsed)
    {
        decimal mediaTicks = (decimal)elapsed.Ticks * timeBaseDenominator /
            (TimeSpan.TicksPerSecond * (decimal)timeBaseNumerator);
        if (mediaTicks >= long.MaxValue) return long.MaxValue;
        return (long)decimal.Floor(mediaTicks);
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
}
