using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace MySecretary;

static class Toast
{
    static Window? cur;
    public static void Show(string text)
    {
        cur?.Close();
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
            ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, IsHitTestVisible = false,
            Content = new Border
            {
                Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Stroke), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(8),
                Child = new TextBlock { Text = text, Foreground = Pal.Br(Pal.Ink), FontSize = 12.5, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") }
            }
        };
        w.Loaded += (_, _) => { var wa = SystemParameters.WorkArea; w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2; w.Top = wa.Bottom - w.ActualHeight - 16; };
        cur = w; w.Show();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        t.Tick += (_, _) => { t.Stop(); w.Close(); };
        t.Start();
    }
}
