namespace BabyKeyboard.Core;

/// <summary>Nine offline pentatonic instruments, including a mix of the first eight. Prepared on the audio thread; bounded voices and quiet output.</summary>
public sealed class SoftToneMixer
{
    public const int SampleRate = 48000;
    private static readonly int[] Notes = [60, 62, 64, 67, 69, 72, 74, 76];
    private static readonly Lazy<float[][][]> Bank = new(CreateBank);
    private const int ReleaseSamples = SampleRate / 25;
    private readonly Voice[] voices = new Voice[8];
    private readonly float[][][] bank;
    private long position;
    private long lastTrigger = -SampleRate;
    private int nextVoice;
    private struct Voice { public float[]? Wave; public int Offset, FadeLeft; }
    public SoundPreset Preset { get; private set; }

    public SoftToneMixer(SoundPreset preset = SoundPreset.Crystal)
    {
        SoundPresets.Get(preset);
        Preset = preset;
        bank = Bank.Value;
    }

    public void ChangePreset(SoundPreset preset)
    {
        SoundPresets.Get(preset);
        if (Preset == preset) return;
        Preset = preset;
        lastTrigger = position - SampleRate;
        for (int i = 0; i < voices.Length; i++)
            if (voices[i].Wave is not null && voices[i].FadeLeft == 0) voices[i].FadeLeft = ReleaseSamples;
    }

    public bool Trigger(int key)
    {
        if (position - lastTrigger < SampleRate * 0.12) return false;
        int note = (key & int.MaxValue) % Notes.Length;
        for (int i = 0; i < voices.Length; i++)
        {
            int slot = (nextVoice + i) % voices.Length;
            if (voices[slot].Wave is null) { nextVoice = slot; break; }
        }
        voices[nextVoice] = new Voice { Wave = bank[(int)Preset][note] };
        nextVoice = (nextVoice + 1) % voices.Length;
        lastTrigger = position;
        return true;
    }

    public void Fill(Span<short> samples)
    {
        for (int i = 0; i < samples.Length; i++, position++)
        {
            double sample = 0;
            for (int v = 0; v < voices.Length; v++)
            {
                ref Voice voice = ref voices[v];
                if (voice.Wave is not { } wave) continue;
                double gain = voice.FadeLeft == 0 ? 1 : voice.FadeLeft / (double)ReleaseSamples;
                sample += wave[voice.Offset++] * gain;
                if ((voice.FadeLeft > 0 && --voice.FadeLeft == 0) || voice.Offset >= wave.Length) voice.Wave = null;
            }
            sample = .88 * sample / (1 + 1.25 * Math.Abs(sample));
            samples[i] = (short)(Math.Clamp(sample, -.30, .30) * short.MaxValue);
        }
    }

    private static float[][][] CreateBank()
    {
        var result = new float[SoundPresets.All.Count][][];
        foreach (var info in SoundPresets.All.Where(info => info.Preset != SoundPreset.Mix))
        {
            var instrument = new float[Notes.Length][];
            for (int i = 0; i < Notes.Length; i++)
                instrument[i] = CreateNote(info.Preset, 440 * Math.Pow(2, (Notes[i] - 69) / 12.0));
            result[(int)info.Preset] = instrument;
        }
        int mixIndex = (int)SoundPreset.Mix;
        result[mixIndex] = new float[Notes.Length][];
        for (int note = 0; note < Notes.Length; note++)
        {
            int length = result.Take(8).Max(instrument => instrument[note].Length);
            var mixed = new float[length];
            for (int i = 0; i < length; i++)
            {
                double value = 0;
                for (int preset = 0; preset < 8; preset++)
                {
                    var wave = result[preset][note];
                    if (i < wave.Length) value += wave[i] / 8.0;
                }
                mixed[i] = (float)value;
            }
            result[mixIndex][note] = mixed;
        }
        return result;
    }

    private static float[] CreateNote(SoundPreset preset, double frequency)
    {
        double duration = preset switch
        {
            SoundPreset.Crystal => .48, SoundPreset.Piano => .82, SoundPreset.Xylophone => .38,
            SoundPreset.MusicBox => .74, SoundPreset.WaterDrop => .34, SoundPreset.Bubble => .30,
            SoundPreset.Arcade => .34, _ => .88
        };
        var samples = new float[(int)(SampleRate * duration)];
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)SampleRate;
            double phase = Math.Tau * frequency * t;
            double value = preset switch
            {
                SoundPreset.Crystal => Math.Sin(phase) * Math.Exp(-t * 8) +
                    .29 * Math.Sin(phase * 2.756) * Math.Exp(-t * 15) + .09 * Math.Sin(phase * 5.404) * Math.Exp(-t * 25),
                SoundPreset.Piano => Piano(phase, t),
                SoundPreset.Xylophone => Math.Sin(phase) * Math.Exp(-t * 13) +
                    .34 * Math.Sin(phase * 4.01) * Math.Exp(-t * 33) + .07 * Math.Sin(phase * 9.03) * Math.Exp(-t * 48),
                SoundPreset.MusicBox => Math.Sin(phase * 2) * Math.Exp(-t * 6) +
                    .18 * Math.Sin(phase * 4.008) * Math.Exp(-t * 14) + .055 * Math.Sin(phase * 6.01) * Math.Exp(-t * 22),
                SoundPreset.WaterDrop => Math.Sin(Math.Tau * frequency * (t + .035 * (1 - Math.Exp(-t / .025)))) * Math.Exp(-t * 17),
                SoundPreset.Bubble => Math.Sin(Math.Tau * frequency * (.52 * t + .019 * (1 - Math.Exp(-t / .019)))) *
                    Math.Exp(-t * 20) * (1 + .1 * Math.Sin(Math.Tau * 22 * t)),
                SoundPreset.Arcade => Arcade(frequency, t),
                _ => Sparkle(frequency, t)
            };
            double attack = Math.Sin(Math.Min(1, t / .006) * Math.PI / 2);
            double tail = Math.Sin(Math.Clamp((duration - t - 1.0 / SampleRate) / .07, 0, 1) * Math.PI / 2);
            samples[i] = (float)(value * attack * attack * tail * tail);
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }
        double level = preset is SoundPreset.MusicBox or SoundPreset.Arcade ? .112 : .13;
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(samples[i] * level / peak);
        return samples;
    }

    private static double Piano(double phase, double t) =>
        (.66 * Math.Sin(phase) + .34 * Math.Sin(phase * 1.0018)) * Math.Exp(-t * 4.7) +
        .36 * Math.Sin(phase * 2.002) * Math.Exp(-t * 7.8) +
        .17 * Math.Sin(phase * 3.006) * Math.Exp(-t * 11) +
        .075 * Math.Sin(phase * 4.013) * Math.Exp(-t * 15) +
        .025 * Math.Sin(phase * 6.03) * Math.Exp(-t * 22) +
        .009 * Math.Sin(phase * 12.71) * Math.Sin(phase * 17.23) * Math.Exp(-t * 100);

    private static double Arcade(double frequency, double t)
    {
        // Integrate the pitch steps to preserve phase. Band-limited harmonics avoid harsh square-wave edges.
        double cycle = frequency * (Math.Min(t, .075) + 1.25 * Math.Clamp(t - .075, 0, .075) + 1.5 * Math.Max(0, t - .15));
        double phase = Math.Tau * cycle;
        return (Math.Sin(phase) + .23 * Math.Sin(phase * 3) + .075 * Math.Sin(phase * 5)) * Math.Exp(-t * 10);
    }

    private static double Sparkle(double frequency, double t)
    {
        double result = 0;
        for (int i = 0; i < 3; i++)
        {
            double time = t - i * .10;
            if (time <= 0) continue;
            double attack = Math.Sin(Math.Min(1, time / .035) * Math.PI / 2);
            double phase = Math.Tau * frequency * (1 + i * .5) * time;
            result += Math.Pow(.6, i) * attack * attack * Math.Exp(-time * 5.7) *
                (Math.Sin(phase) + .12 * Math.Sin(phase * 2.001));
        }
        return result;
    }
}
