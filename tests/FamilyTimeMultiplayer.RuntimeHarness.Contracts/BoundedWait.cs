using System;

namespace FamilyTimeMultiplayer.RuntimeHarness.Contracts;

public sealed class BoundedWait
{
    private double? _startedAtSeconds;

    public BoundedWait(double timeoutSeconds)
    {
        if (!double.IsFinite(timeoutSeconds) || timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be a finite positive duration.");
        }
        TimeoutSeconds = timeoutSeconds;
    }

    public double TimeoutSeconds { get; }
    public string LastObservation { get; private set; } = "not observed";

    public bool IsTimedOutAt(double nowSeconds, string lastObservation)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }
        _startedAtSeconds ??= nowSeconds;
        if (nowSeconds < _startedAtSeconds.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds), "Clock cannot move backwards during a bounded wait.");
        }
        return IsTimedOut(nowSeconds - _startedAtSeconds.Value, lastObservation);
    }

    public bool IsTimedOut(double elapsedSeconds, string lastObservation)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        LastObservation = lastObservation ?? string.Empty;
        return elapsedSeconds >= TimeoutSeconds;
    }
}
