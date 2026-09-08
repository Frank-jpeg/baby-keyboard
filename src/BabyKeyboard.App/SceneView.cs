using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BabyKeyboard.Core;

namespace BabyKeyboard.App;

internal sealed class SceneView : FrameworkElement
{
    private static readonly Color[] Colors =
    [Color.FromRgb(108, 240, 220), Color.FromRgb(171, 153, 255), Color.FromRgb(255, 187, 114),
     Color.FromRgb(244, 146, 197), Color.FromRgb(124, 203, 255)];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Burst> bursts = [];
    private readonly Random random = new(73);
    private readonly Dictionary<string, Geometry> glyphs = [];
    private readonly Dictionary<(string, double, string), FormattedText> labels = [];
    private DrawingGroup? backdrop;
    private Size backdropSize;
    private int sequence;
    private bool hasPlayed;
    private double? manualTime;
    private SoundPreset selectedSound;
    private double soundSelectedAt = double.NegativeInfinity;
    internal bool Preview { get; set; }
    internal bool SoundAvailable { get; set; } = true;
    internal InputSnapshot Input { get; set; } = new(UnlockPhase.Playing, 0, 0);
    private double Time => manualTime ?? clock.Elapsed.TotalSeconds;
    private sealed record Burst(string Label, double X, double Y, int Color, double Birth, double Tilt, bool Click);

    internal SceneView()
    {
        ClipToBounds = true;
        Focusable = false;
        IsHitTestVisible = true;
    }

    internal void AddKey(int key)
    {
        var slots = new (double X, double Y)[] { (.50, .48), (.27, .40), (.72, .43), (.66, .64), (.33, .63), (.49, .29) };
        var slot = slots[sequence % slots.Length];
        Add(new(KeyLabels.For(key), slot.X + (random.NextDouble() - .5) * .05,
            slot.Y + (random.NextDouble() - .5) * .04, sequence % Colors.Length,
            Time, (random.NextDouble() - .5) * 15, false));
        sequence++;
    }

    internal void SelectSound(SoundPreset sound, bool animate = true)
    {
        selectedSound = sound;
        if (animate) soundSelectedAt = Time;
        InvalidateVisual();
    }

    internal void AddClick(Point position)
    {
        Add(new("", Math.Clamp(position.X / Math.Max(ActualWidth, 1), .03, .97),
            Math.Clamp(position.Y / Math.Max(ActualHeight, 1), .12, .83), sequence++ % Colors.Length,
            Time, 0, true));
    }

    private void Add(Burst burst)
    {
        hasPlayed = true;
        if (bursts.Count == 32) bursts.RemoveAt(0);
        bursts.Add(burst);
        InvalidateVisual();
    }

    internal void Advance()
    {
        double now = Time;
        bursts.RemoveAll(b => now - b.Birth > 1.9);
        InvalidateVisual();
    }

    internal void Demo()
    {
        manualTime = 2;
        hasPlayed = true;
        bursts.Clear();
        bursts.Add(new("空格", .26, .56, 1, 1.33, -7, false));
        bursts.Add(new("↑", .73, .42, 2, 1.49, 8, false));
        bursts.Add(new("A", .50, .46, 0, 1.72, -5, false));
        bursts.Add(new("", .77, .65, 3, 1.66, 0, true));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (backdrop is null || backdropSize != RenderSize)
        {
            backdropSize = RenderSize;
            backdrop = MakeBackdrop(w, h);
        }
        dc.DrawDrawing(backdrop);
        double uiScale = Math.Clamp(Math.Min(w / 1440, h / 900), .65, 1.6);
        DrawHeader(dc, w, uiScale);
        if (!hasPlayed || bursts.Count == 0) DrawInvitation(dc, w, h, uiScale);
        foreach (var burst in bursts) DrawBurst(dc, burst, w, h, uiScale);
        DrawFooter(dc, w, h, uiScale);
    }

    private DrawingGroup MakeBackdrop(double w, double h)
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            var background = new LinearGradientBrush(Color.FromRgb(9, 16, 32), Color.FromRgb(10, 12, 26), 65);
            dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
            Glow(dc, new(w * .28, h * .40), w * .39, h * .65, Color.FromArgb(30, 50, 140, 146));
            Glow(dc, new(w * .82, h * .51), w * .39, h * .64, Color.FromArgb(23, 112, 79, 166));
            var stars = new Random(308);
            for (int i = 0; i < 115; i++)
            {
                double radius = i % 7 == 0 ? 1.4 : .8;
                var brush = new SolidColorBrush(Color.FromArgb((byte)stars.Next(25, 72), 182, 210, 233));
                dc.DrawEllipse(brush, null, new(stars.NextDouble() * w, stars.NextDouble() * h), radius, radius);
            }
            var orbitPen = new Pen(new SolidColorBrush(Color.FromArgb(12, 145, 183, 210)), 1);
            dc.PushTransform(new RotateTransform(-22, w * .51, h * .49));
            dc.DrawEllipse(null, orbitPen, new(w * .51, h * .49), w * .35, h * .28);
            dc.DrawEllipse(null, orbitPen, new(w * .51, h * .49), w * .43, h * .36);
            dc.Pop();
        }
        group.Freeze();
        return group;
    }

    private void DrawHeader(DrawingContext dc, double w, double scale)
    {
        double pad = 44 * scale, top = 32 * scale;
        var icon = new Rect(pad, top, 40 * scale, 40 * scale);
        dc.DrawRoundedRectangle(Brush("#163331"), new Pen(Brush("#467E79"), 1), icon, 12 * scale, 12 * scale);
        Text(dc, "B", 22 * scale, "#AAFAE7", new(icon.X + 12 * scale, icon.Y + 5 * scale));
        Text(dc, "宝宝键盘", 20 * scale, "#ECF4F9", new(pad + 55 * scale, top + 3 * scale));
        Text(dc, "小小按键，大大惊喜", 11 * scale, "#798997", new(pad + 55 * scale, top + 30 * scale));
        string status = Preview ? "画面预览" : "键鼠保护中";
        double chipWidth = 136 * scale;
        var chip = new Rect(w - pad - chipWidth, top + 4 * scale, chipWidth, 34 * scale);
        dc.DrawRoundedRectangle(Brush("#13282B"), new Pen(Brush("#264743"), 1), chip, 17 * scale, 17 * scale);
        dc.DrawEllipse(Brush("#84E9C6"), null, new(chip.X + 18 * scale, chip.Y + 17 * scale), 3 * scale, 3 * scale);
        Text(dc, status, 12 * scale, "#B8D9D2", new(chip.X + 31 * scale, chip.Y + 8 * scale));
    }

    private void DrawInvitation(DrawingContext dc, double w, double h, double scale)
    {
        double opacity = hasPlayed ? .55 : 1;
        dc.PushOpacity(opacity);
        CenterText(dc, hasPlayed ? "再敲一下？" : "敲一下，亮起来", 43 * scale, "#E0EDF4", new(w / 2, h * .405));
        CenterText(dc, "每一个按键，都有自己的光。", 16 * scale, "#8296A6", new(w / 2, h * .405 + 72 * scale));
        double tile = 45 * scale, gap = 12 * scale;
        string[] names = ["A", "B", "C", "✦"];
        for (int i = 0; i < names.Length; i++)
        {
            var r = new Rect(w / 2 - (tile * 4 + gap * 3) / 2 + i * (tile + gap), h * .405 + 127 * scale, tile, tile);
            dc.DrawRoundedRectangle(Brush("#14212F"), new Pen(Brush("#344552"), 1), r, 12 * scale, 12 * scale);
            CenterText(dc, names[i], 18 * scale, "#89BDB8", new(r.X + r.Width / 2, r.Y + 10 * scale));
        }
        dc.Pop();
    }

    private void DrawBurst(DrawingContext dc, Burst burst, double w, double h, double scale)
    {
        double age = Math.Max(0, Time - burst.Birth);
        if (age > 1.9) return;
        double fade = 1 - Math.Clamp((age - .7) / 1.2, 0, 1);
        var color = Colors[burst.Color];
        var center = new Point(w * burst.X, h * burst.Y - age * 18 * scale);
        dc.PushOpacity(fade);
        Glow(dc, center, (burst.Click ? 80 : 175) * scale, (burst.Click ? 80 : 175) * scale,
            Color.FromArgb(44, color.R, color.G, color.B));

        double ringRadius = (burst.Click ? 12 : 75) * scale + age * 103 * scale;
        dc.PushOpacity(Math.Max(0, 1 - age / 1.3) * .65);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(color), 1.2 * scale), center, ringRadius, ringRadius);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(color), .6 * scale), center, ringRadius + 9 * scale, ringRadius + 9 * scale);
        dc.Pop();

        for (int p = 0; p < 12; p++)
        {
            double angle = p * Math.Tau / 12 + burst.Tilt;
            double radius = ((burst.Click ? 12 : 80) + age * (50 + (p % 4) * 22)) * scale;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            dc.PushOpacity(Math.Max(0, 1 - age / 1.6) * (p % 3 == 0 ? 1 : .65));
            var brush = new SolidColorBrush(color);
            if (p % 3 == 0)
            {
                double s = 4 * scale;
                dc.DrawLine(new Pen(brush, 1.2 * scale), new(point.X - s, point.Y), new(point.X + s, point.Y));
                dc.DrawLine(new Pen(brush, 1.2 * scale), new(point.X, point.Y - s), new(point.X, point.Y + s));
            }
            else dc.DrawEllipse(brush, null, point, 1.7 * scale, 1.7 * scale);
            dc.Pop();
        }

        if (!burst.Click)
        {
            double pop = age < .22 ? .72 + .32 * Math.Sin(age / .22 * Math.PI / 2) : 1 + .04 * Math.Exp(-(age - .22) * 10);
            dc.PushTransform(new TranslateTransform(center.X, center.Y));
            dc.PushTransform(new RotateTransform(burst.Tilt));
            dc.PushTransform(new ScaleTransform(scale * pop, scale * pop));
            Geometry glyph = Glyph(burst.Label);
            var bounds = glyph.Bounds;
            double tileWidth = Math.Max(158, bounds.Width + 58);
            var tile = new Rect(-tileWidth / 2, -85, tileWidth, 170);
            var tileFill = new LinearGradientBrush(Color.FromArgb(235, 24, 39, 53), Color.FromArgb(241, 15, 23, 40), 90);
            dc.DrawRoundedRectangle(tileFill, new Pen(new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)), 1.2), tile, 29, 29);
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(20, color.R, color.G, color.B)), 5), tile, 29, 29);
            dc.PushTransform(new TranslateTransform(-bounds.Left - bounds.Width / 2, -bounds.Top - bounds.Height / 2));
            dc.DrawGeometry(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromArgb(14, color.R, color.G, color.B)), 17), glyph);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B)), 5), glyph);
            dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(235, 255, 253), color, 90), null, glyph);
            dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
        }
        dc.Pop();
    }

    private void DrawFooter(DrawingContext dc, double w, double h, double scale)
    {
        scale = Math.Min(scale, Math.Max(.2, (w - 32) / 1056));
        double left = (w - 1056 * scale) / 2;
        double top = h - 152 * scale;
        var soundPanel = new Rect(left, top, 684 * scale, 108 * scale);
        bool justSelected = Time - soundSelectedAt < 1.1;
        dc.DrawRoundedRectangle(Brush("#121E2C"), new Pen(Brush(justSelected ? "#83C8BA" : "#30404E"), 1), soundPanel, 19 * scale, 19 * scale);
        var selected = SoundPresets.Get(selectedSound);
        Text(dc, selected.Name, 18 * scale, "#C9F6E8", new(left + 20 * scale, top + 11 * scale));
        Text(dc, selected.Description, 10 * scale, "#8099A6", new(left + 21 * scale, top + 39 * scale));
        Text(dc, "小键盘 1～8 切换音色", 11 * scale, "#9CADBA", new(soundPanel.Right - 159 * scale, top + 18 * scale));
        foreach (var sound in SoundPresets.All)
        {
            bool chosen = sound.Preset == selectedSound;
            var chip = new Rect(left + (18 + (sound.Number - 1) * 82) * scale, top + 64 * scale, 74 * scale, 29 * scale);
            dc.DrawRoundedRectangle(Brush(chosen ? "#214139" : "#192737"),
                new Pen(Brush(chosen ? "#75BCA9" : "#2E4152"), .8), chip, 8 * scale, 8 * scale);
            var number = new Rect(chip.X + 5 * scale, chip.Y + 5 * scale, 19 * scale, 19 * scale);
            dc.DrawRoundedRectangle(Brush(chosen ? "#88DBBD" : "#2D4254"), null, number, 5 * scale, 5 * scale);
            CenterText(dc, sound.Number.ToString(CultureInfo.InvariantCulture), 11 * scale, chosen ? "#142D28" : "#A9BCCB",
                new(number.X + number.Width / 2, number.Y + 1 * scale));
            Text(dc, sound.ShortName, 11 * scale, chosen ? "#D7F8E9" : "#9DB0BF", new(chip.X + 29 * scale, chip.Y + 6 * scale));
        }

        double width = 354 * scale;
        var pill = new Rect(soundPanel.Right + 18 * scale, top, width, 108 * scale);
        bool active = Input.Phase is UnlockPhase.Holding or UnlockPhase.AwaitingRelease or UnlockPhase.Complete;
        dc.DrawRoundedRectangle(Brush(active ? "#163431" : "#121E2C"), new Pen(Brush(active ? "#6CA99C" : "#30404E"), 1), pill, 19 * scale, 19 * scale);
        var key = new Rect(pill.X + 18 * scale, pill.Y + 31 * scale, 51 * scale, 38 * scale);
        dc.DrawRoundedRectangle(Brush("#223644"), new Pen(Brush("#637987"), .8), key, 9 * scale, 9 * scale);
        CenterText(dc, "Esc", 17 * scale, "#BCDAD9", new(key.X + key.Width / 2, key.Y + 6 * scale));
        string headline = Input.Phase switch
        {
            UnlockPhase.Holding => "继续按住 Esc…",
            UnlockPhase.AwaitingRelease or UnlockPhase.Complete => "松手退出",
            _ => "长按 Esc 3 秒退出"
        };
        string detail = Input.Phase switch
        {
            UnlockPhase.Holding => $"还有 {Math.Max(0, 3 * (1 - Input.Progress)):0.0} 秒 · 提前松开会取消",
            UnlockPhase.AwaitingRelease or UnlockPhase.Complete => "松开所有按键和鼠标按钮",
            _ => "按满后松手，即可返回桌面"
        };
        Text(dc, headline, 16 * scale, active ? "#C7FBE7" : "#D0DEE6", new(pill.X + 84 * scale, pill.Y + 26 * scale));
        Text(dc, detail, 11 * scale, "#849CA7", new(pill.X + 84 * scale, pill.Y + 58 * scale));
        if (active)
        {
            var line = new Rect(pill.X + 18 * scale, pill.Bottom - 8 * scale, (width - 36 * scale) * Input.Progress, 2 * scale);
            dc.DrawRoundedRectangle(Brush("#9BF3CC"), null, line, scale, scale);
        }
        double pad = 46 * scale;
        for (int i = 0; i < 4; i++)
        {
            double barHeight = (SoundAvailable ? new[] { 5, 12, 8, 16 }[i] : 3) * scale;
            dc.DrawRoundedRectangle(Brush(SoundAvailable ? "#79A39E" : "#60717E"), null,
                new Rect(pad + i * 5 * scale, h - 19 * scale - barHeight, 2 * scale, barHeight), scale, scale);
        }
        Text(dc, SoundAvailable ? "柔和音效 · 8 种小惊喜" : "静音运行 · 仍可选择音色", 11 * scale, "#82949F", new(pad + 29 * scale, h - 34 * scale));
        string right = Preview ? "预览不会拦截键鼠" : "放心敲，慢慢玩。";
        Text(dc, right, 11 * scale, "#627786", new(w - pad - 116 * scale, h - 34 * scale));
    }

    private Geometry Glyph(string label)
    {
        if (glyphs.TryGetValue(label, out var cached)) return cached;
        double size = label.Length switch { 1 => 112, 2 => 76, 3 or 4 => 63, _ => 45 };
        var text = new FormattedText(label, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), size, Brushes.White, 1);
        var geometry = text.BuildGeometry(new Point());
        geometry.Freeze();
        glyphs[label] = geometry;
        return geometry;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void Text(DrawingContext dc, string label, double size, string color, Point position)
    {
        var key = (label, size, color);
        if (!labels.TryGetValue(key, out var text))
        {
            if (labels.Count > 256) labels.Clear();
            text = new FormattedText(label, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            labels[key] = text;
        }
        dc.DrawText(text, position);
    }

    private void CenterText(DrawingContext dc, string label, double size, string color, Point position)
    {
        var text = new FormattedText(label, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new(position.X - text.Width / 2, position.Y));
    }

    private static void Glow(DrawingContext dc, Point center, double rx, double ry, Color color)
    {
        var brush = new RadialGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B));
        dc.DrawEllipse(brush, null, center, rx, ry);
    }
}
