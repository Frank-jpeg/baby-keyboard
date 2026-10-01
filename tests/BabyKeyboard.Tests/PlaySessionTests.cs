using BabyKeyboard.Core;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private static void RunPlaySessionTests()
    {
        Test("Session warns at 30 seconds and rests exactly at deadline", () =>
        {
            var s = new PlaySession(300);
            s.Update(269.99); Check(s.Phase == PlayPhase.Playing);
            s.Update(270); Check(s.Phase == PlayPhase.Warning);
            s.Update(299.99); Check(s.Phase == PlayPhase.Warning);
            s.Update(300); Check(s.Phase == PlayPhase.Resting && s.RemainingSeconds == 0);
        });
        Test("Rest never resumes from elapsed time or stale updates", () =>
        {
            var s = new PlaySession(60);
            s.Update(10000); s.Update(0);
            Check(s.Phase == PlayPhase.Resting && s.RemainingSeconds == 0);
        });
        Test("Invalid session duration is rejected", () =>
        {
            foreach (double value in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
            {
                bool rejected = false;
                try { _ = new PlaySession(value); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected);
            }
        });
        Test("Session counts idle time and warning survives stale updates", () =>
        {
            var s = new PlaySession(60);
            s.Update(40); s.Update(10); Check(s.RemainingSeconds == 20);
            s.Update(61); Check(s.Phase == PlayPhase.Resting);
        });
    }
}
