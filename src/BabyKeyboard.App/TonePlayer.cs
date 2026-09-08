using System.Runtime.InteropServices;
using System.Threading.Channels;
using BabyKeyboard.Core;

namespace BabyKeyboard.App;

/// <summary>Small in-memory PCM player. No files, downloads, volume changes, or external audio packages.</summary>
internal sealed class TonePlayer : IDisposable
{
    private readonly record struct Note(int Key, SoundPreset Sound);
    private readonly Channel<Note> notes = Channel.CreateBounded<Note>(new BoundedChannelOptions(16)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly Thread? thread;
    private volatile bool stopping, available;
    private int selectedSound;
    internal bool Available => available;

    internal TonePlayer(bool simulateFailure = false)
    {
        if (simulateFailure) return;
        thread = new Thread(Run) { IsBackground = true, Name = "Soft instruments" };
        thread.Start();
    }

    internal void Play(int key)
    {
        if (available) notes.Writer.TryWrite(new(key, (SoundPreset)Volatile.Read(ref selectedSound)));
    }

    internal void SelectPreset(SoundPreset sound)
    {
        SoundPresets.Get(sound);
        Interlocked.Exchange(ref selectedSound, (int)sound);
        Play(64); // Confirm a selection with a note from the chosen instrument.
    }

    private void Run()
    {
        nint device = 0;
        List<(nint Header, nint Data)> blocks = [];
        uint size = (uint)Marshal.SizeOf<WaveHeader>();
        try
        {
            var format = new WaveFormat
            {
                Tag = 1, Channels = 1, Rate = SoftToneMixer.SampleRate,
                BytesPerSecond = SoftToneMixer.SampleRate * 2, Align = 2, Bits = 16
            };
            if (waveOutOpen(out device, uint.MaxValue, ref format, 0, 0, 0) != 0) return;
            var mixer = new SoftToneMixer();
            short[] samples = new short[512];
            int flagsOffset = Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags)).ToInt32();
            for (int i = 0; i < 3; i++)
            {
                nint data = Marshal.AllocHGlobal(samples.Length * 2);
                nint header = Marshal.AllocHGlobal((int)size);
                blocks.Add((header, data));
                Marshal.StructureToPtr(new WaveHeader { Data = data, Length = (uint)samples.Length * 2 }, header, false);
                if (waveOutPrepareHeader(device, header, size) != 0) return;
                mixer.Fill(samples);
                Marshal.Copy(samples, 0, data, samples.Length);
                if (waveOutWrite(device, header, size) != 0) return;
            }
            available = true;
            while (!stopping)
            {
                foreach (var block in blocks)
                {
                    if ((Marshal.ReadInt32(block.Header, flagsOffset) & 1) == 0) continue;
                    var sound = (SoundPreset)Volatile.Read(ref selectedSound);
                    if (mixer.Preset != sound)
                    {
                        mixer.ChangePreset(sound);
                        mixer.Trigger(64);
                    }
                    while (notes.Reader.TryRead(out var note))
                        if (note.Sound == sound) mixer.Trigger(note.Key);
                    mixer.Fill(samples);
                    Marshal.Copy(samples, 0, block.Data, samples.Length);
                    if (waveOutWrite(device, block.Header, size) != 0) return;
                }
                Thread.Sleep(4);
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or OutOfMemoryException) { }
        finally
        {
            available = false;
            if (device != 0)
            {
                waveOutReset(device);
                foreach (var block in blocks)
                {
                    // A driver that refuses to release a block must retain its memory until process exit.
                    if (waveOutUnprepareHeader(device, block.Header, size) == 0)
                    {
                        Marshal.FreeHGlobal(block.Header);
                        Marshal.FreeHGlobal(block.Data);
                    }
                }
                waveOutClose(device);
            }
        }
    }

    public void Dispose()
    {
        stopping = true;
        thread?.Join(1200);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Tag, Channels; public uint Rate, BytesPerSecond; public ushort Align, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data; public uint Length, Recorded; public nuint User;
        public uint Flags, Loops; public nint Next; public nuint Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint device, uint id, ref WaveFormat format, nuint callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint device);
}
