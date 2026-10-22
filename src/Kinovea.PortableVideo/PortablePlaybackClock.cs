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
    public double SpeedFactor { get; private set; } = 1.0;

    public long TargetTimestamp
    {
        get
        {
            if (!IsPlaying)
                return SaturatingAdd(mediaAnchor, ToMediaTicks(pausedElapsed, SpeedFactor));
            return SaturatingAdd(mediaAnchor, ToMediaTicks(pausedElapsed + timeProvider.GetElapsedTime(wallAnchor), SpeedFactor));
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

    /// <summary>Changes playback rate without moving the current media position.</summary>
    public void SetSpeedFactor(double speedFactor)
    {
        if (!double.IsFinite(speedFactor) || speedFactor < 0.002 || speedFactor > 10.0)
            throw new ArgumentOutOfRangeException(nameof(speedFactor), "Playback speed must be between 0.002x and 10x.");

        long currentTimestamp = TargetTimestamp;
        mediaAnchor = currentTimestamp;
        pausedElapsed = TimeSpan.Zero;
        wallAnchor = timeProvider.GetTimestamp();
        SpeedFactor = speedFactor;
    }

    private long ToMediaTicks(TimeSpan elapsed, double speedFactor)
    {
        decimal mediaTicks = (decimal)elapsed.Ticks * timeBaseDenominator * (decimal)speedFactor /
            (TimeSpan.TicksPerSecond * (decimal)timeBaseNumerator);
        if (mediaTicks >= long.MaxValue) return long.MaxValue;
        return (long)decimal.Floor(mediaTicks);
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;
}
