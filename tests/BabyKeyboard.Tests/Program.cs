using BabyKeyboard.Core;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private static int passed, failed;
    private static readonly PhysicalKey Escape = new(27, 1), Enter = new(13, 28), NumEnter = new(13, 28, true), A = new(65, 30);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--audio-check")
        {
            RunAudioCheck();
            Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
            return failed == 0 ? 0 : 1;
        }
        if (args.FirstOrDefault() == "--export-sounds")
        {
            ExportSounds(args[1]);
            return 0;
        }
        if (args.FirstOrDefault() == "--unlock-regression")
        {
            RunUnlockCoreRegressions();
            RunUnlockRegressions(args[1]);
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
        Test("Short Esc cancels without accumulating", () =>
        {
            var s = new UnlockController();
            for (int i = 0; i < 100; i++) { s.KeyDown(Escape, i * 100); s.KeyUp(Escape, i * 100 + 80); }
            Check(s.Phase == UnlockPhase.Playing && s.HeldCount == 0);
        });
        Test("Auto-repeat never shortens the three-second hold", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0);
            for (int i = 1; i < 3000; i++) Check(!s.KeyDown(Escape, i));
            Check(s.Phase == UnlockPhase.Holding);
            s.Tick(3000); Check(s.Phase == UnlockPhase.AwaitingRelease);
        });
        Test("Unlock only completes on release after full duration", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 1000);
            s.Tick(3999); Check(s.Phase == UnlockPhase.Holding);
            s.Tick(4000); Check(s.Phase == UnlockPhase.AwaitingRelease && s.HeldCount == 1);
            s.KeyUp(Escape, 4500); Check(s.Phase == UnlockPhase.Complete && s.HeldCount == 0);
        });
        Test("Release at the threshold works without a timer tick", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0); s.KeyUp(Escape, 3000);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Other keys cancel; releasing them does not restart a held Esc", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0); s.KeyDown(A, 1500);
            s.KeyUp(A, 1600); s.KeyDown(Escape, 1700); s.Tick(6000);
            Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(Escape, 6100); s.KeyDown(Escape, 6200); s.KeyUp(Escape, 9200);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Esc started with another key down never arms", () =>
        {
            var s = new UnlockController(); s.KeyDown(A, 0); s.KeyDown(Escape, 20);
            s.KeyUp(A, 40); s.Tick(9000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Mouse buttons cancel a hold", () =>
        {
            foreach (var button in Enum.GetValues<PointerButton>())
            {
                var s = new UnlockController(); s.KeyDown(Escape, 0); s.ButtonDown(button, 1500);
                s.ButtonUp(button, 1510); s.Tick(6000); Check(s.Phase == UnlockPhase.Playing);
            }
        });
        Test("A held mouse button prevents starting the timer", () =>
        {
            var s = new UnlockController(); s.ButtonDown(PointerButton.Left, 0); s.KeyDown(Escape, 100);
            s.ButtonUp(PointerButton.Left, 110); s.Tick(5000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Main and numpad Enter are ordinary keys even after a long hold", () =>
        {
            foreach (var key in new[] { Enter, NumEnter })
            {
                var s = new UnlockController(); Check(s.KeyDown(key, 0));
                s.Tick(5000); Check(s.Phase == UnlockPhase.Playing);
                s.KeyUp(key, 5500); Check(s.HeldCount == 0 && s.Phase == UnlockPhase.Playing);
            }
        });
        Test("Enter cancels an Esc hold", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0); s.KeyDown(Enter, 500);
            s.Tick(4000); Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Esc held before startup must first be released", () =>
        {
            var s = new UnlockController([27]); s.KeyDown(Escape, 0); s.Tick(6000);
            Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(Escape, 6100); s.KeyDown(Escape, 6200); s.KeyUp(Escape, 9200);
            Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Initially held keyboard and mouse inputs block unlock", () =>
        {
            var s = new UnlockController([65], [PointerButton.Right]);
            s.KeyDown(Escape, 0); s.Tick(4000); Check(s.Phase == UnlockPhase.Playing);
            s.KeyUp(A, 4100); s.ButtonUp(PointerButton.Right, 4200); s.Tick(9000);
            Check(s.Phase == UnlockPhase.Playing);
        });
        Test("Completed hold drains every new input before exit", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0); s.Tick(3000);
            s.KeyDown(A, 3001); s.ButtonDown(PointerButton.Left, 3002); s.KeyUp(Escape, 3010);
            Check(s.Phase == UnlockPhase.AwaitingRelease);
            s.KeyUp(A, 3020); Check(s.Phase == UnlockPhase.AwaitingRelease);
            s.ButtonUp(PointerButton.Left, 3030); Check(s.Phase == UnlockPhase.Complete);
        });
        Test("100,000 random transitions preserve release invariants", () =>
        {
            var s = new UnlockController(); var rng = new Random(91);
            var keys = new[] { Escape, Enter, NumEnter, A, new PhysicalKey(66, 48), new PhysicalKey(91, 91, true) };
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
        Test("Numpad 1 to 8 select the eight instruments directly", () =>
        {
            Check(SoundPresets.All.Count == 9);
            for (int i = 0; i < 8; i++)
            {
                Check(SoundPresets.FromNumpad(new(0x61 + i)) == (SoundPreset)i);
                Check(SoundPresets.Get((SoundPreset)i).Number == i + 1);
            }
            Check(SoundPresets.Get(SoundPreset.Piano).Number == 2);
            Check(SoundPresets.FromNumpad(new(0x69)) == SoundPreset.Mix);
            Check(SoundPresets.Get(SoundPreset.Mix).Number == 9);
            Check(SoundPresets.FromNumpad(new(0x60)) is null);
        });
        Test("Numpad selection works with Num Lock off without changing dedicated arrows", () =>
        {
            int[] scans = [0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48];
            int[] navigation = [0x23, 0x28, 0x22, 0x25, 0x0C, 0x27, 0x24, 0x26];
            for (int i = 0; i < 8; i++)
            {
                Check(SoundPresets.FromNumpad(new(navigation[i], scans[i])) == (SoundPreset)i);
                Check(SoundPresets.FromNumpad(new(navigation[i], scans[i], true)) is null);
                Check(SoundPresets.FromNumpad(new(navigation[i])) is null);
                Check(SoundPresets.FromNumpad(new(0x31 + i, 2 + i)) is null);
            }
            Check(SoundPresets.FromNumpad(new(0x29, 0x49)) == SoundPreset.Mix);
        });
        Test("Numpad identity survives Num Lock / Shift VK changes and ignores repeats", () =>
        {
            var s = new UnlockController();
            Check(s.KeyDown(new(0x61, 0x4F), 0));
            Check(!s.KeyDown(new(0x23, 0x4F), 50));
            s.KeyUp(new(0x23, 0x4F), 100); Check(s.HeldCount == 0);
            Check(s.KeyDown(new(0x23, 0x4F), 150));
            s.KeyUp(new(0x61, 0x4F), 200); Check(s.HeldCount == 0);
            s.KeyDown(Escape, 300); s.KeyUp(Escape, 3300); Check(s.Phase == UnlockPhase.Complete);
        });
        Test("Numpad selection key cancels a pending Esc exit", () =>
        {
            var s = new UnlockController(); s.KeyDown(Escape, 0);
            var digit = new PhysicalKey(0x62, 0x50);
            Check(s.KeyDown(digit, 1000)); s.KeyUp(digit, 1200);
            s.Tick(8000); Check(s.Phase == UnlockPhase.Playing);
        });
        RunUnlockCoreRegressions();
        RunSoundTests();
    }

    static partial void RunIntegration(string appPath);
}
