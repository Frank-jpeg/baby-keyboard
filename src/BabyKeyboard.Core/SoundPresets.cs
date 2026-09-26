namespace BabyKeyboard.Core;

public enum SoundPreset { Crystal, Piano, Xylophone, MusicBox, WaterDrop, Bubble, Arcade, Starlight, Mix }

public sealed record SoundPresetInfo(SoundPreset Preset, string Name, string ShortName, string Description)
{
    public int Number => (int)Preset + 1;
}

public static class SoundPresets
{
    public static IReadOnlyList<SoundPresetInfo> All { get; } = Array.AsReadOnly<SoundPresetInfo>(
    [
        new(SoundPreset.Crystal, "清脆水晶", "水晶", "像小小的水晶，轻轻碰响"),
        new(SoundPreset.Piano, "轻柔钢琴", "钢琴", "指尖落下，一颗温柔的音符"),
        new(SoundPreset.Xylophone, "童趣木琴", "木琴", "叩叩咚咚，小木块也会唱歌"),
        new(SoundPreset.MusicBox, "叮咚八音盒", "八音盒", "打开一只装着童话的音乐盒"),
        new(SoundPreset.WaterDrop, "灵动水滴", "水滴", "滴答，一滴水落进小池塘"),
        new(SoundPreset.Bubble, "弹跳泡泡", "泡泡", "啵！一颗泡泡轻轻弹起来"),
        new(SoundPreset.Arcade, "复古游戏", "游戏", "叮咚，收集一颗小星星"),
        new(SoundPreset.Starlight, "星空魔法", "星空", "星星一闪一闪，洒下小小魔法"),
        new(SoundPreset.Mix, "八色混合", "混合", "把 1～8 种声音混在一起")
    ]);

    public static SoundPresetInfo Get(SoundPreset preset) =>
        (uint)preset < All.Count ? All[(int)preset] : throw new ArgumentOutOfRangeException(nameof(preset));

    /// <summary>Physical numpad 1–9, with either Num Lock state. Dedicated navigation keys are extended.</summary>
    public static SoundPreset? FromNumpad(PhysicalKey key)
    {
        if (key.Extended) return null;
        if (key.VirtualKey is >= 0x61 and <= 0x69) return (SoundPreset)(key.VirtualKey - 0x61);
        int number = (key.ScanCode, key.VirtualKey) switch
        {
            (0x4F, 0x23) => 1, (0x50, 0x28) => 2, (0x51, 0x22) => 3,
            (0x4B, 0x25) => 4, (0x4C, 0x0C) => 5, (0x4D, 0x27) => 6,
            (0x47, 0x24) => 7, (0x48, 0x26) => 8, (0x49, 0x29) => 9,
            _ => 0
        };
        return number == 0 ? null : (SoundPreset)(number - 1);
    }
}
