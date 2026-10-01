using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace BabyKeyboard.App;

internal sealed class SessionSetupWindow : Window
{
    private readonly TextBox minutes;
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    internal int Minutes { get; private set; } = 5;

    internal SessionSetupWindow()
    {
        Title = "宝宝键盘 · 家长设置";
        Width = 450; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "这次玩多久？", FontSize = 25 });
        panel.Children.Add(new TextBlock { Text = "到点停止声音和特效，保持键鼠保护。", Margin = new Thickness(0, 12, 0, 16) });
        minutes = new TextBox { Text = "5", FontSize = 20, MaxLength = 2 };
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int value in new[] { 3, 5, 10 })
        {
            var button = new Button { Content = $"{value} 分钟", Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(0, 0, 10, 12) };
            button.Click += (_, _) => minutes.Text = value.ToString(CultureInfo.InvariantCulture);
            presets.Children.Add(button);
        }
        panel.Children.Add(presets);
        panel.Children.Add(new TextBlock { Text = "自定义分钟数（1～60）", Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(minutes);
        panel.Children.Add(error);
        panel.Children.Add(new TextBlock { Text = "最后 30 秒会提醒。休息后不会自动继续。\n家长长按 Esc 退出，重新打开即可开始下一轮。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18) });
        var start = new Button { Content = "开始玩耍", Padding = new Thickness(12), IsDefault = true };
        start.Click += (_, _) =>
        {
            if (!int.TryParse(minutes.Text, out int value) || value is < 1 or > 60)
            { error.Text = "请输入 1～60 的整数。"; return; }
            Minutes = value;
            DialogResult = true;
        };
        panel.Children.Add(start);
    }
}
