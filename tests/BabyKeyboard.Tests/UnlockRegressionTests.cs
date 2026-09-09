using System.Runtime.InteropServices;
using BabyKeyboard.App;
using BabyKeyboard.Core;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private static void RunUnlockCoreRegressions()
    {
        Test("Make-only Pause, Break and text packets never leave held keys behind", () =>
        {
            foreach (int vk in new[] { 0x13, 0x03, 0xE7 })
            {
                var state = new UnlockController([vk]);
                for (int i = 0; i < 40; i++) state.KeyDown(new(vk, 0x45), i * 1000);
                Check(state.HeldCount == 0);
                state.KeyDown(Escape, 50_000); state.Tick(53_000);
                Check(state.Phase == UnlockPhase.AwaitingRelease);
                state.KeyUp(Escape, 53_100); Check(state.Phase == UnlockPhase.Complete);
            }
        });
        Test("Pause during an Esc hold cancels that hold without poisoning later attempts", () =>
        {
            var state = new UnlockController(); state.KeyDown(Escape, 0);
            state.KeyDown(new(0x13, 0x45), 1500); state.Tick(5000);
            Check(state.Phase == UnlockPhase.Playing && state.HeldCount == 1);
            state.KeyUp(Escape, 5100); state.KeyDown(Escape, 5200); state.KeyUp(Escape, 8200);
            Check(state.Phase == UnlockPhase.Complete);
        });
        Test("Overrun markers and synthetic extended Shift cannot block Esc", () =>
        {
            var state = new UnlockController();
            foreach (var key in new[] { new PhysicalKey(0xFF, 0xFF), new(0, 0), new(65, 0xFF), new(0xA0, 0x2A, true) })
                Check(!state.KeyDown(key, 0));
            Check(state.HeldCount == 0); state.KeyDown(Escape, 1000); state.KeyUp(Escape, 4000);
            Check(state.Phase == UnlockPhase.Complete);
        });
        Test("An ignored synthetic Shift release does not clear a real inherited Shift", () =>
        {
            var state = new UnlockController([0xA0]);
            state.KeyUp(new(0xA0, 0x2A, true), 0); Check(state.HeldCount == 1);
            state.KeyUp(new(0x10, 0x2A), 10); Check(state.HeldCount == 0);
        });
        Test("Missing or changed release scan codes recover the matching key", () =>
        {
            foreach (int scan in new[] { 0, 0x20, 0x21E })
            {
                var state = new UnlockController(); state.KeyDown(A, 0); state.KeyUp(new(65, scan), 100);
                Check(state.HeldCount == 0, $"Scan {scan:X} left a key held");
                state.KeyDown(Escape, 200); state.KeyUp(new(27, 0), 3200);
                Check(state.Phase == UnlockPhase.Complete);
            }
        });
        Test("A wrong release scan code does not clear a different genuinely held key", () =>
        {
            var state = new UnlockController(); var d = new PhysicalKey(68, 0x20);
            state.KeyDown(A, 0); state.KeyDown(d, 1); state.KeyUp(new(65, 0x20), 2);
            Check(state.HeldCount == 1);
            Check(!state.KeyDown(d, 3), "The D key was incorrectly released");
            state.KeyUp(d, 4); Check(state.HeldCount == 0);
        });
        Test("Driver prefixes, modifier aliases and repeated downs keep one held state", () =>
        {
            var state = new UnlockController();
            Check(state.KeyDown(new(0xA2, 0x21D), 0));
            Check(!state.KeyDown(new(0x11, 0), 1));
            state.KeyUp(new(0x11, 0x1D), 2); Check(state.HeldCount == 0);
            Check(state.KeyDown(A, 3)); Check(!state.KeyDown(new(65), 4));
            state.KeyUp(new(65), 5); Check(state.HeldCount == 0);
        });
        Test("Enter release recovery distinguishes the main key from the numpad key", () =>
        {
            var state = new UnlockController(); state.KeyDown(Enter, 0); state.KeyDown(NumEnter, 1);
            state.KeyUp(new(13, 0, true), 2); Check(state.HeldCount == 1);
            Check(!state.KeyDown(Enter, 3));
            state.KeyUp(new(13), 4); Check(state.HeldCount == 0);
        });
        Test("Simulated hours of mixed typing still allow a fresh three-second Esc exit", () =>
        {
            var state = new UnlockController();
            for (int i = 0; i < 100_000; i++)
            {
                double now = i * 200.0;
                var key = new PhysicalKey(65 + i % 26, 0x10 + i % 26);
                state.KeyDown(key, now); state.KeyDown(new(key.VirtualKey), now + 1);
                state.KeyUp(i % 2 == 0 ? key : new(key.VirtualKey), now + 30);
                state.KeyDown(new(0x13, 0x45), now + 40); // No Pause key-up.
                state.KeyDown(new(0xFF, 0xFF), now + 50); // Keyboard overflow artifact.
                state.ButtonDown(PointerButton.Left, now + 60); state.ButtonUp(PointerButton.Left, now + 70);
                Check(state.HeldCount == 0);
            }
            state.KeyDown(Escape, 20_000_000); state.Tick(20_003_000);
            Check(state.Phase == UnlockPhase.AwaitingRelease);
            state.KeyUp(Escape, 20_003_100); Check(state.Phase == UnlockPhase.Complete);
        });
        Test("New mouse transitions cannot corrupt a completed unlock state", () =>
        {
            var state = new UnlockController(); state.KeyDown(Escape, 0); state.KeyUp(Escape, 3000);
            Check(!state.ButtonDown(PointerButton.Left, 3001)); Check(state.HeldCount == 0);
        });
    }

    private static void RunUnlockRegressions(string appPath)
    {
        nint previous = Native.GetForegroundWindow();
        Native.GetCursorPos(out var cursor); Native.GetClipCursor(out var baseline);
        bool unrestricted = baseline == Native.VirtualBounds;
        using var fixture = new SafetyFixture(); using var observer = new HookObserver();
        try { RunNativeUnlockRegressions(System.IO.Path.GetFullPath(appPath), fixture, observer, baseline); }
        finally
        {
            fixture.Focus(); ReleaseInjected(); Native.RestoreClip(baseline, unrestricted);
            SetCursorPos(cursor.X, cursor.Y); if (previous != 0) Native.SetForegroundWindow(previous);
        }
    }

    private static void RunNativeUnlockRegressions(string appPath, SafetyFixture fixture, HookObserver observer, Native.Rect baseline)
    {
        Test("Native Esc exits after a make-only Pause and overflow event", () =>
        {
            using var child = new ProtectedRun(appPath, fixture, 12);
            fixture.Focus(); observer.Reset();
            Down(0x13); Down(0xFF); Thread.Sleep(100);
            WaitForHeldCount(child, 0);
            Down(27); Thread.Sleep(3200);
            Check(ReadTestState(child, 3) == (int)UnlockPhase.AwaitingRelease, "Esc did not start its exit timer");
            Up(27); child.WaitForExit(); Check(child.Process.ExitCode == 0);
            Check(observer.KeyboardEvents == 0); CheckRestored(baseline);
        });
        Test("Native release recovery handles mismatched scan codes without leaking input", () =>
        {
            using var child = new ProtectedRun(appPath, fixture, 12);
            fixture.Focus(); observer.Reset();
            Down(65); WaitForHeldCount(child, 1);
            SendKeyVariant(65, 0x20, true); InjectedDown.Remove((65, false));
            WaitForHeldCount(child, 0);
            Down(27); Thread.Sleep(3200); Up(27); child.WaitForExit();
            Check(child.Process.ExitCode == 0 && observer.KeyboardEvents == 0); CheckRestored(baseline);
        });
        Test("Native real held keys still prevent an Esc exit", () =>
        {
            using var child = new ProtectedRun(appPath, fixture, 15);
            fixture.Focus(); observer.Reset(); Down(65); Down(27); Thread.Sleep(3200);
            Check(ReadTestState(child, 3) == (int)UnlockPhase.Playing && ReadTestState(child, 2) == 2);
            Up(65); Thread.Sleep(150); Check(ReadTestState(child, 3) == (int)UnlockPhase.Playing);
            Up(27); Down(27); Thread.Sleep(3200); Up(27); child.WaitForExit();
            Check(child.Process.ExitCode == 0 && observer.KeyboardEvents == 0); CheckRestored(baseline);
        });
        Test("Native Esc exits after thousands of mixed input transitions", () =>
        {
            using var child = new ProtectedRun(appPath, fixture, 45);
            fixture.Focus(); observer.Reset();
            for (int i = 0; i < 1500; i++)
            {
                int vk = 65 + i % 26;
                Down(vk);
                if (i % 3 == 0) { SendKeyVariant(vk, 0x70 + i % 8, true); InjectedDown.Remove((vk, false)); }
                else Up(vk);
                if (i % 25 == 0) Down(0x13);
                if (i % 50 == 0) { Mouse(0x2); Mouse(0x4); Tap(0x61 + i % 8); }
            }
            WaitForHeldCount(child, 0);
            Down(27); Thread.Sleep(3200); Up(27); child.WaitForExit();
            Check(child.Process.ExitCode == 0 && observer.KeyboardEvents == 0 && observer.MouseEvents == 0);
            CheckRestored(baseline);
        });
    }

    private static void SendKeyVariant(int vk, int scan, bool up)
    {
        var input = new OsInput { Type = 1, Union = new InputUnion { Key = new KeyInput
            { Vk = (ushort)vk, Scan = (ushort)scan, Flags = up ? 2u : 0u, Extra = Tag } } };
        Check(SendInput(1, [input], Marshal.SizeOf<OsInput>()) == 1, "Variant input injection failed");
    }

    private static void WaitForHeldCount(ProtectedRun child, int expected)
    {
        // SendInput, the low-level hook and the read-only window query run on different queues.
        long actual = -1;
        for (int i = 0; i < 50; i++)
        {
            actual = ReadTestState(child, 2);
            if (actual == expected) return;
            Thread.Sleep(30);
        }
        throw new InvalidOperationException($"Held input count stayed at {actual}, expected {expected}.");
    }
}
