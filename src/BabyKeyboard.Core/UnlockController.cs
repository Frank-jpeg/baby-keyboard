namespace BabyKeyboard.Core;

public readonly record struct PhysicalKey(int VirtualKey, int ScanCode = 0, bool Extended = false)
{
    public bool IsEscape => VirtualKey == 0x1B;

    // Num Lock / Shift can change a numpad key's VK between down and up.
    // Scan codes keep that physical key's identity stable.
    public (int Code, bool Extended) Identity => (ScanCode == 0 ? VirtualKey + 0x10000 : ScanCode, Extended);
}

public enum UnlockPhase { Playing, Holding, AwaitingRelease, Complete }
public enum PointerButton { Left, Right, Middle, Back, Forward }

/// <summary>Physical transitions and a monotonic clock only. Key repeat never advances the clock.</summary>
public sealed class UnlockController
{
    public const double HoldMilliseconds = 3000;
    private readonly HashSet<(int Code, bool Extended)> keys = [];
    private readonly HashSet<int> inheritedKeys;
    private readonly HashSet<PointerButton> buttons;
    private double startedAt;

    public UnlockController(IEnumerable<int>? initiallyHeldKeys = null,
        IEnumerable<PointerButton>? initiallyHeldButtons = null)
    {
        inheritedKeys = initiallyHeldKeys?.ToHashSet() ?? [];
        buttons = initiallyHeldButtons?.ToHashSet() ?? [];
    }

    public UnlockPhase Phase { get; private set; }
    public int HeldCount => keys.Count + inheritedKeys.Count + buttons.Count;
    public bool IsInheritedKey(int vk) => inheritedKeys.Contains(vk);

    public bool KeyDown(PhysicalKey key, double now)
    {
        Tick(now);
        if (Phase == UnlockPhase.Complete || inheritedKeys.Contains(key.VirtualKey) || !keys.Add(key.Identity))
            return false;
        if (Phase == UnlockPhase.AwaitingRelease) return false;
        if (Phase == UnlockPhase.Holding) Phase = UnlockPhase.Playing;
        else if (key.IsEscape && HeldCount == 1)
        {
            startedAt = now;
            Phase = UnlockPhase.Holding;
        }
        return true;
    }

    public void KeyUp(PhysicalKey key, double now)
    {
        Tick(now);
        inheritedKeys.Remove(key.VirtualKey);
        bool removed = keys.Remove(key.Identity);
        if (removed && key.IsEscape && Phase == UnlockPhase.Holding)
            Phase = UnlockPhase.Playing;
        CompleteIfReleased();
    }

    public bool ButtonDown(PointerButton button, double now)
    {
        Tick(now);
        bool fresh = buttons.Add(button);
        if (Phase == UnlockPhase.Holding) Phase = UnlockPhase.Playing;
        return fresh && Phase == UnlockPhase.Playing;
    }

    public void ButtonUp(PointerButton button, double now)
    {
        Tick(now);
        buttons.Remove(button);
        CompleteIfReleased();
    }

    public void Tick(double now)
    {
        if (Phase == UnlockPhase.Holding && now - startedAt >= HoldMilliseconds)
            Phase = UnlockPhase.AwaitingRelease;
        CompleteIfReleased();
    }

    public double Progress(double now) => Phase switch
    {
        UnlockPhase.Holding => Math.Clamp((now - startedAt) / HoldMilliseconds, 0, 1),
        UnlockPhase.AwaitingRelease or UnlockPhase.Complete => 1,
        _ => 0
    };

    private void CompleteIfReleased()
    {
        if (Phase == UnlockPhase.AwaitingRelease && HeldCount == 0)
            Phase = UnlockPhase.Complete;
    }
}
