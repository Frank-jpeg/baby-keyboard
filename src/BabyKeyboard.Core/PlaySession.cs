namespace BabyKeyboard.Core;

public enum PlayPhase { Playing, Warning, Resting }

/// <summary>Elapsed time comes from a monotonic clock, never from keyboard activity.</summary>
public sealed class PlaySession
{
    private readonly double duration;
    private double elapsed;
    public PlaySession(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        duration = seconds;
    }
    public double RemainingSeconds => Math.Max(0, duration - elapsed);
    public PlayPhase Phase => RemainingSeconds <= 0 ? PlayPhase.Resting :
        RemainingSeconds <= 30 ? PlayPhase.Warning : PlayPhase.Playing;
    public void Update(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        elapsed = Math.Max(elapsed, elapsedSeconds);
    }
}
