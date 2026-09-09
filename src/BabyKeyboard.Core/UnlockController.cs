namespace BabyKeyboard.Core;

public readonly record struct PhysicalKey(int VirtualKey, int ScanCode = 0, bool Extended = false)
{
    public bool IsEscape => VirtualKey == 0x1B;

    // Pause/Break and Unicode packets are actions, not reliable down/up pairs.
    public bool IsMomentary => VirtualKey is 0x03 or 0x13 or 0xE7;
    public bool IsIgnored => VirtualKey is <= 0 or >= 0xFF || (ScanCode & 0xFF) == 0xFF ||
        (Extended && VirtualKey is 0x10 or 0xA0 or 0xA1); // Print Screen's synthetic E0 Shift.
    public int CanonicalVirtualKey => VirtualKey switch
    {
        0x10 => (ScanCode & 0xFF) == 0x36 ? 0xA1 : 0xA0,
        0x11 => Extended ? 0xA3 : 0xA2,
        0x12 => Extended ? 0xA5 : 0xA4,
        _ => VirtualKey
    };

    // Num Lock / Shift can change a numpad key's VK between down and up.
    // Scan codes keep that physical key's identity stable.
    public (int Code, bool Extended) Identity =>
        ((ScanCode & 0xFF) == 0 ? CanonicalVirtualKey + 0x10000 : ScanCode & 0xFF, Extended);
}

public enum UnlockPhase { Playing, Holding, AwaitingRelease, Complete }
public enum PointerButton { Left, Right, Middle, Back, Forward }

/// <summary>Physical transitions and a monotonic clock only. Key repeat never advances the clock.</summary>
public sealed class UnlockController
{
    public const double HoldMilliseconds = 3000;
    private readonly Dictionary<(int Code, bool Extended), PhysicalKey> keys = [];
    private readonly HashSet<int> inheritedKeys;
    private readonly HashSet<PointerButton> buttons;
    private double startedAt;

    public UnlockController(IEnumerable<int>? initiallyHeldKeys = null,
        IEnumerable<PointerButton>? initiallyHeldButtons = null)
    {
        inheritedKeys = initiallyHeldKeys?.Where(vk =>
            !new PhysicalKey(vk).IsMomentary && !new PhysicalKey(vk).IsIgnored).ToHashSet() ?? [];
        buttons = initiallyHeldButtons?.ToHashSet() ?? [];
    }

    public UnlockPhase Phase { get; private set; }
    public int HeldCount => keys.Count + inheritedKeys.Count + buttons.Count;
    public bool IsInheritedKey(int vk) => inheritedKeys.Contains(vk);
    public bool IsInheritedKey(PhysicalKey key) => !key.IsIgnored && !key.IsMomentary &&
        (IsInheritedKey(key.VirtualKey) || IsInheritedKey(key.CanonicalVirtualKey));
    public bool EscapeHeld
    {
        get
        {
            foreach (var key in keys.Values) if (key.IsEscape) return true;
            return false;
        }
    }

    public bool KeyDown(PhysicalKey key, double now)
    {
        Tick(now);
        if (Phase == UnlockPhase.Complete || key.IsIgnored) return false;
        if (key.IsMomentary)
        {
            if (Phase == UnlockPhase.Holding) Phase = UnlockPhase.Playing;
            return Phase == UnlockPhase.Playing;
        }
        if (IsInheritedKey(key) || keys.ContainsKey(key.Identity) || HasMatchingVirtualKey(key))
            return false;
        keys.Add(key.Identity, key);
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
        if (key.IsIgnored || key.IsMomentary) { CompleteIfReleased(); return; }
        inheritedKeys.Remove(key.VirtualKey);
        inheritedKeys.Remove(key.CanonicalVirtualKey);
        bool removed = RemoveReleasedKey(key);
        if (removed && !EscapeHeld && Phase == UnlockPhase.Holding)
            Phase = UnlockPhase.Playing;
        CompleteIfReleased();
    }

    public bool ButtonDown(PointerButton button, double now)
    {
        Tick(now);
        if (Phase == UnlockPhase.Complete) return false;
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

    private bool RemoveReleasedKey(PhysicalKey released)
    {
        bool physicalMatch = keys.TryGetValue(released.Identity, out var physical);
        if (physicalMatch && physical.CanonicalVirtualKey == released.CanonicalVirtualKey)
            return keys.Remove(released.Identity);
        // Some drivers/remappers report a missing/different scan code on release.
        // Recover a single matching VK; never guess between two held Enter keys.
        (int Code, bool Extended) matching = default, sameSide = default;
        int matchCount = 0, sideCount = 0;
        foreach (var pair in keys)
        {
            if (pair.Value.CanonicalVirtualKey != released.CanonicalVirtualKey) continue;
            matching = pair.Key; matchCount++;
            if (pair.Value.Extended == released.Extended) { sameSide = pair.Key; sideCount++; }
        }
        if (sideCount == 1) return keys.Remove(sameSide);
        if (matchCount == 1) return keys.Remove(matching);
        return physicalMatch && keys.Remove(released.Identity); // Num Lock / layout changed the VK.
    }

    private bool HasMatchingVirtualKey(PhysicalKey key)
    {
        foreach (var held in keys.Values)
            if (held.CanonicalVirtualKey == key.CanonicalVirtualKey && held.Extended == key.Extended) return true;
        return false;
    }
}
