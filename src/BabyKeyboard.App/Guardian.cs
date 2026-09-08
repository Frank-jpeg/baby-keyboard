using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace BabyKeyboard.App;

internal static class SelfLaunch
{
    internal static ProcessStartInfo Info(params string[] arguments)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位程序文件。");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BabyKeyboard.dll"));
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }
}

internal sealed class GuardianClient : IDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly Process child;
    private readonly Thread pulseThread;
    private readonly CancellationTokenSource stop = new();
    private readonly Func<bool> isHealthy;
    private readonly Action failed;
    private int disposed;

    internal GuardianClient(Native.Rect originalClip, bool unrestricted, Func<bool> isHealthy,
        Action failed, int maximumSeconds = 0)
    {
        this.isHealthy = isHealthy;
        this.failed = failed;
        string name = $"BabyKeyboard-{Environment.ProcessId}-{Guid.NewGuid():N}";
        pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using Process self = Process.GetCurrentProcess();
        var info = SelfLaunch.Info("--guardian", name, Environment.ProcessId.ToString(),
            self.StartTime.ToUniversalTime().Ticks.ToString(), originalClip.Left.ToString(),
            originalClip.Top.ToString(), originalClip.Right.ToString(), originalClip.Bottom.ToString(),
            unrestricted ? "1" : "0", maximumSeconds.ToString());
        try
        {
            child = Process.Start(info) ?? throw new InvalidOperationException("无法启动保护看护进程。");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
        pulseThread = new Thread(Pulse) { IsBackground = true, Name = "Guardian heartbeat" };
        pulseThread.Start();
    }

    private void Pulse()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (isHealthy()) { pipe.WriteByte(0x48); pipe.Flush(); }
                if (stop.Token.WaitHandle.WaitOne(200)) break;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            if (!stop.IsCancellationRequested) failed();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        pulseThread.Join(1000);
        try { pipe.WriteByte(0x51); pipe.Flush(); } catch (IOException) { }
        pipe.Dispose();
        child.WaitForExit(1500);
        child.Dispose();
        stop.Dispose();
    }
}

internal static class Guardian
{
    internal static int Run(string[] args)
    {
        if (args.Length != 10) return 64;
        int parentId = int.Parse(args[2]);
        long parentStart = long.Parse(args[3]);
        var original = new Native.Rect(int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6]), int.Parse(args[7]));
        bool unrestricted = args[8] == "1";
        int maximumSeconds = int.Parse(args[9]);
        Process? parent = null;
        bool matched = false;
        try
        {
            parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != parentStart) return 65;
            matched = true;
            using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(8000);
            var clock = Stopwatch.StartNew();
            long lastBeat = clock.ElapsedMilliseconds;
            byte[] value = new byte[1];
            Task<int>? pendingRead = null;
            while (!parent.HasExited)
            {
                pendingRead ??= pipe.ReadAsync(value.AsMemory()).AsTask();
                if (pendingRead.Wait(100))
                {
                    if (pendingRead.Result == 0) break;
                    if (value[0] == 0x51) return 0;
                    if (value[0] == 0x48) lastBeat = clock.ElapsedMilliseconds;
                    pendingRead = null;
                }
                if (clock.ElapsedMilliseconds - lastBeat >= 5000 ||
                    (maximumSeconds > 0 && clock.Elapsed.TotalSeconds >= maximumSeconds)) break;
            }
            if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(2000); }
            return 70;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException
            or System.ComponentModel.Win32Exception or AggregateException or ArgumentException)
        {
            if (matched && parent is not null)
            {
                try { if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(2000); } }
                catch (Exception killError) when (killError is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            return 71;
        }
        finally
        {
            if (matched) Native.RestoreClip(original, unrestricted);
            parent?.Dispose();
        }
    }
}
