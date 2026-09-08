using BabyKeyboard.Core;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private static int passed, failed;
    private static readonly PhysicalKey Enter = new(13, 28), NumEnter = new(13, 28, true), A = new(65, 30);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--audio-check")
        {
            RunAudioCheck();
            Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
        RunCoreTests();
        if (args.Length > 0 && args[0] == "--integration") RunIntegration(args[1]);
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RunCoreTests()
    {
        Test("A normal key has one transition and no unlock", () =>
        {
            var s = new UnlockController();
            Check(s.KeyDown(A, 0)); Check(!s.KeyDown(A, 100));
            s.Tick(5000); Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(A, 5001); Check(s.HeldCount == 0);
        });
        Test("Short Enter cancels without accumulating", () =>
        {
            var s = new UnlockController();
            for (int i = 0; i < 100; i++) { s.KeyDown(Enter, i * 100); s.KeyUp(Enter, i * 100 + 80); }
            Check(s.Phase == UnlockPhase.Playing && s.HeldCount == 0);
        });
        Test("Auto-repeat never shortens the three-second hold", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 0);
            for (int i = 1; i < 3000; i++) Check(!s.KeyDown(Enter, i));
            Check(s.Phase == UnlockPhase.Holding);
            s.Tick(3000); Check(s.Phase == UnlockPhase.AwaitingRelease);
        });
        Test("Unlock only completes on release after full duration", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 1000);
            s.Tick(3999); Check(s.Phase == UnlockPhase.Holding);
            s.Tick(4000); Check(s.Phase == UnlockPhase.AwaitingRelease && s.HeldCount == 1);
            s.KeyUp(Enter, 4500); Check(s.Phase == UnlockPhase.Complete && s.HeldCount == 0);
        });
        Test("Release at the threshold works without a timer tick", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 0); s.KeyUp(Enter, 3000);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Other keys cancel; releasing them does not restart a held Enter", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 0); s.KeyDown(A, 1500);
            s.KeyUp(A, 1600); s.KeyDown(Enter, 1700); s.Tick(6000);
            Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(Enter, 6100); s.KeyDown(Enter, 6200); s.KeyUp(Enter, 9200);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Enter started with another key down never arms", () =>
        {
            var s = new UnlockController(); s.KeyDown(A, 0); s.KeyDown(Enter, 20);
            s.KeyUp(A, 40); s.Tick(9000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Mouse buttons cancel a hold", () =>
        {
            foreach (var button in Enum.GetValues<PointerButton>())
            {
                var s = new UnlockController(); s.KeyDown(Enter, 0); s.ButtonDown(button, 1500);
                s.ButtonUp(button, 1510); s.Tick(6000); Check(s.Phase == UnlockPhase.Playing);
            }
        });
        Test("A held mouse button prevents starting the timer", () =>
        {
            var s = new UnlockController(); s.ButtonDown(PointerButton.Left, 0); s.KeyDown(Enter, 100);
            s.ButtonUp(PointerButton.Left, 110); s.Tick(5000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Numpad Enter is supported", () =>
        {
            var s = new UnlockController(); s.KeyDown(NumEnter, 0); s.KeyUp(NumEnter, 3500);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Main and numpad Enter together do not arm", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 0); s.KeyDown(NumEnter, 500);
            s.Tick(4000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Enter held before startup must first be released", () =>
        {
            var s = new UnlockController([13]); s.KeyDown(Enter, 0); s.Tick(6000);
            Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(Enter, 6100); s.KeyDown(Enter, 6200); s.KeyUp(Enter, 9200);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Initially held keyboard and mouse inputs block unlock", () =>
        {
            var s = new UnlockController([65], [PointerButton.Right]);
            s.KeyDown(Enter, 0); s.Tick(4000); Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(A, 4100); s.ButtonUp(PointerButton.Right, 4200); s.Tick(9000);
            Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Completed hold drains every new input before exit", () =>
        {
            var s = new UnlockController(); s.KeyDown(Enter, 0); s.Tick(3000);
            s.KeyDown(A, 3001); s.ButtonDown(PointerButton.Left, 3002); s.KeyUp(Enter, 3010);
            Check(s.Phase == UnlockPhase.AwaitingRelease);
            s.KeyUp(A, 3020); Check(s.Phase == UnlockPhase.AwaitingRelease);
            s.ButtonUp(PointerButton.Left, 3030); Check(s.Phase == UnlockPhase.Complete);
        });
        Test("100,000 random transitions preserve release invariants", () =>
        {
            var s = new UnlockController(); var rng = new Random(91);
            var keys = new[] { Enter, NumEnter, A, new PhysicalKey(66, 48), new PhysicalKey(91, 91, true) };
            for (int i = 0; i < 100_000; i++)
            {
                int choice = rng.Next(5); double now = i * 7;
                if (choice == 0) s.KeyDown(keys[rng.Next(keys.Length)], now);
                else if (choice == 1) s.KeyUp(keys[rng.Next(keys.Length)], now);
                else if (choice == 2) s.ButtonDown(PointerButton.Left, now);
                else if (choice == 3) s.ButtonUp(PointerButton.Left, now);
                else s.Tick(now);
                Check(s.Phase != UnlockPhase.Complete || s.HeldCount == 0);
                Check(s.Progress(now) is >= 0 and <= 1);
                Check(s.Phase != UnlockPhase.Holding || s.HeldCount == 1);
            }
        });
        Test("Every virtual key has a presentable label", () =>
        {
            for (int i = 0; i < 256; i++) Check(!string.IsNullOrWhiteSpace(KeyLabels.For(i)));
            Check(KeyLabels.For(65) == "A" && KeyLabels.For(32) == "空格");
        });
        Test("Audio attack is soft, mixing bounded, and the tail becomes silent", () =>
        {
            var mixer = new SoftToneMixer(); short[] block = new short[512];
            int peak = 0, maxStep = 0, previous = 0;
            for (int i = 0; i < 500; i++)
            {
                mixer.Trigger(i); mixer.Fill(block);
                foreach (int sample in block)
                {
                    peak = Math.Max(peak, Math.Abs(sample));
                    maxStep = Math.Max(maxStep, Math.Abs(sample - previous)); previous = sample;
                }
            }
            Check(peak > 200 && peak < 8000, $"Unexpected amplitude: {peak}");
            Check(maxStep < 1000, $"Audio discontinuity: {maxStep}");
            for (int i = 0; i < 40; i++) mixer.Fill(block);
            Check(block.All(s => s == 0));
        });
        Test("Audio rate limiting ignores excessive simultaneous notes", () =>
        {
            var mixer = new SoftToneMixer(); Check(mixer.Trigger(65));
            for (int i = 0; i < 1000; i++) Check(!mixer.Trigger(i));
            short[] buffer = new short[6000]; mixer.Fill(buffer); Check(mixer.Trigger(66));
        });
    }

    static partial void RunIntegration(string appPath);
}
