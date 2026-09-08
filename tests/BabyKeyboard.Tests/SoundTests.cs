using System.IO;
using System.Text;
using BabyKeyboard.Core;

namespace BabyKeyboard.Tests;

internal static partial class Program
{
    private static void RunSoundTests()
    {
        Test("All eight timbres have soft starts, bounded volume and silent tails at every pitch", () =>
        {
            foreach (var info in SoundPresets.All)
            for (int note = 0; note < 8; note++)
            {
                var mixer = new SoftToneMixer(info.Preset);
                short[] samples = new short[SoftToneMixer.SampleRate];
                Check(mixer.Trigger(note)); mixer.Fill(samples);
                int peak = samples.Max(s => Math.Abs((int)s));
                Check(peak is > 2000 and < 4000, $"{info.Name}: unexpected peak {peak}");
                Check(samples[0] == 0 && Math.Abs(samples[1]) <= 2, $"{info.Name}: abrupt onset");
                Check(samples.TakeLast(2000).All(s => s == 0), $"{info.Name}: tail did not return to zero");
                Check(MaxStep(samples) < 1200, $"{info.Name}: discontinuous waveform");
            }
        });
        Test("Eight timbres produce different waveforms for the same musical note", () =>
        {
            List<short[]> waves = [];
            foreach (var info in SoundPresets.All)
            {
                var mixer = new SoftToneMixer(info.Preset);
                short[] wave = new short[SoftToneMixer.SampleRate]; mixer.Trigger(0); mixer.Fill(wave);
                foreach (var previous in waves)
                {
                    double dot = 0, energyA = 0, energyB = 0;
                    for (int i = 0; i < wave.Length; i++)
                    {
                        dot += (double)wave[i] * previous[i];
                        energyA += (double)wave[i] * wave[i]; energyB += (double)previous[i] * previous[i];
                    }
                    Check(Math.Abs(dot) / Math.Sqrt(energyA * energyB) < .97, $"{info.Name}: duplicate timbre");
                }
                waves.Add(wave);
            }
        });
        Test("Sustained random playing stays quiet and continuous in all eight instruments", () =>
        {
            foreach (var info in SoundPresets.All)
            {
                var mixer = new SoftToneMixer(info.Preset); short[] block = new short[512];
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
                Check(peak is > 2000 and < 10000, $"{info.Name}: unexpected amplitude {peak}");
                Check(maxStep < 1500, $"{info.Name}: audio discontinuity {maxStep}");
                for (int i = 0; i < 100; i++) mixer.Fill(block);
                Check(block.All(s => s == 0));
            }
        });
        Test("Audio rate limiting ignores excessive simultaneous notes in every preset", () =>
        {
            foreach (var info in SoundPresets.All)
            {
                var mixer = new SoftToneMixer(info.Preset); Check(mixer.Trigger(65));
                for (int i = 0; i < 1000; i++) Check(!mixer.Trigger(i));
                short[] buffer = new short[6000]; mixer.Fill(buffer); Check(mixer.Trigger(66));
            }
        });
        Test("Rapid instrument changes fade old voices and allow immediate new feedback", () =>
        {
            var mixer = new SoftToneMixer(); short[] block = new short[512];
            int previous = 0, maxStep = 0;
            for (int i = 0; i < 500; i++)
            {
                mixer.ChangePreset((SoundPreset)(i % 8)); Check(mixer.Trigger(64));
                mixer.Fill(block);
                foreach (int sample in block)
                {
                    Check(Math.Abs(sample) < 10000);
                    maxStep = Math.Max(maxStep, Math.Abs(sample - previous)); previous = sample;
                }
            }
            Check(maxStep < 1500, $"Switching caused a discontinuity: {maxStep}");
            for (int i = 0; i < 100; i++) mixer.Fill(block);
            Check(block.All(s => s == 0));
        });
        Test("Reselecting the same instrument does not bypass audio rate limiting", () =>
        {
            var mixer = new SoftToneMixer(SoundPreset.Piano); Check(mixer.Trigger(65));
            mixer.ChangePreset(SoundPreset.Piano); Check(!mixer.Trigger(66));
        });
    }

    private static int MaxStep(short[] samples)
    {
        int result = 0;
        for (int i = 1; i < samples.Length; i++) result = Math.Max(result, Math.Abs(samples[i] - samples[i - 1]));
        return result;
    }

    private static void ExportSounds(string path)
    {
        // An offline listening sample, in numpad 1–8 order. No input recording or audio capture.
        List<short> all = [];
        foreach (var info in SoundPresets.All)
        {
            var mixer = new SoftToneMixer(info.Preset);
            foreach (int note in new[] { 0, 2, 4 })
            {
                mixer.Trigger(note); short[] part = new short[SoftToneMixer.SampleRate / 3];
                mixer.Fill(part); all.AddRange(part);
            }
            short[] tail = new short[SoftToneMixer.SampleRate]; mixer.Fill(tail); all.AddRange(tail);
            all.AddRange(new short[SoftToneMixer.SampleRate / 2]);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + all.Count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(SoftToneMixer.SampleRate); writer.Write(SoftToneMixer.SampleRate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(all.Count * 2);
        foreach (short sample in all) writer.Write(sample);
        Console.WriteLine(path);
    }
}
