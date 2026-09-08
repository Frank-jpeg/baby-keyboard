using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BabyKeyboard.App;

internal sealed record AppOptions(bool Preview = false, int TimeoutSeconds = 0, bool AudioFailure = false,
    string? ReadyEvent = null, int UiHangAfterMs = 0, int InputHangAfterMs = 0, bool HooksFailure = false)
{
    internal bool Test => TimeoutSeconds > 0;
    internal static AppOptions Parse(string[] args)
    {
        bool preview = false, audioFailure = false, hooksFailure = false;
        int timeout = 0, uiHang = 0, inputHang = 0;
        string? readyEvent = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("命令行参数不完整。");
            switch (args[i])
            {
                case "--preview": preview = true; break;
                case "--smoke-test": timeout = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--test-audio-failure": audioFailure = true; break;
                case "--test-hooks-failure": hooksFailure = true; break;
                case "--ready-event": readyEvent = Value(); break;
                case "--test-ui-hang-after-ms": uiHang = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--test-input-hang-after-ms": inputHang = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException("无法识别的启动参数。");
            }
        }
        if (timeout != 0 && (timeout < 5 || timeout > 45)) throw new ArgumentException("测试时间应在 5 到 45 秒内。");
        if ((uiHang != 0 || inputHang != 0 || hooksFailure || readyEvent is not null) && timeout == 0)
            throw new ArgumentException("故障注入只允许用于带超时的测试模式。");
        return new(preview, timeout, audioFailure, readyEvent, uiHang, inputHang, hooksFailure);
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Native.SetErrorMode(0x0001 | 0x0002); // No modal driver/OS crash prompts while input is protected.
        if (args.FirstOrDefault() == "--guardian") return Guardian.Run(args);
        try
        {
            if (args.FirstOrDefault() == "--render-preview")
            {
                if (args.Length < 2) throw new ArgumentException("需要指定预览图片路径。");
                RenderPreview(args[1], args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 1);
                return 0;
            }
            var options = AppOptions.Parse(args);
            using var mutex = new Mutex(true, @"Local\BabyKeyboard-v1", out bool first);
            if (!first) return 0;
            try { return new AppRuntime(options).Run(); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            if (!args.Contains("--smoke-test"))
                MessageBox.Show("宝宝键盘未能启动，电脑输入已恢复。\n\n" + ex.Message,
                    "宝宝键盘", MessageBoxButton.OK, MessageBoxImage.Information);
            Console.Error.WriteLine("Startup failed: " + ex.Message);
            return 1;
        }
    }

    private static void RenderPreview(string path, double scale)
    {
        if (scale < .5 || scale > 2) throw new ArgumentException("预览缩放超出范围。");
        var app = new Application();
        var view = new SceneView { Width = 1440, Height = 900, Preview = true };
        view.Demo();
        view.Measure(new Size(1440, 900));
        view.Arrange(new Rect(0, 0, 1440, 900));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(1440 * scale), (int)(900 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path);
        encoder.Save(output);
        app.Shutdown();
    }
}
