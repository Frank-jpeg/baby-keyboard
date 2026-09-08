namespace BabyKeyboard.Core;

/// <summary>Offline pentatonic chimes with a soft attack and bounded, quiet mixing.</summary>
public sealed class SoftToneMixer
{
    public const int SampleRate = 48000;
    private static readonly int[] Notes = [60, 62, 64, 67, 69, 72, 74, 76];
    private readonly Voice[] voices = new Voice[4];
    private long position;
    private long lastTrigger = -SampleRate;
    private int nextVoice;
    private struct Voice { public long Start; public double Frequency; public bool Active; }

    public bool Trigger(int key)
    {
        if (position - lastTrigger < SampleRate * 0.12) return false;
        int note = Notes[(key & int.MaxValue) % Notes.Length];
        voices[nextVoice] = new Voice
        {
            Start = position,
            Frequency = 440 * Math.Pow(2, (note - 69) / 12.0),
            Active = true
        };
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
                if (!voice.Active) continue;
                double t = (position - voice.Start) / (double)SampleRate;
                if (t >= 0.28) { voice.Active = false; continue; }
                double attack = Math.Min(1, t / 0.012);
                double tail = Math.Clamp((0.28 - t) / 0.04, 0, 1);
                double envelope = attack * tail * Math.Exp(-t * 15);
                double phase = Math.Tau * voice.Frequency * t;
                sample += 0.095 * envelope * (Math.Sin(phase) + 0.12 * Math.Sin(phase * 2));
            }
            samples[i] = (short)(Math.Clamp(sample, -0.40, 0.40) * short.MaxValue);
        }
    }
}
