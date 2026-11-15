using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace MySecretary;

sealed class ColorPanel : AppPanel
{
    readonly Border sw = new() { Width = 72, Height = 72, CornerRadius = new CornerRadius(16), Background = Brushes.Transparent, BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), BorderThickness = new Thickness(1) };
    readonly TextBox hex = Ro(), rgb = Ro(), hsl = Ro();
    readonly WrapPanel hist = new();
    static TextBox Ro() => new() { IsReadOnly = true, Width = 220, Margin = new Thickness(0, 0, 8, 0) };

    public ColorPanel() : base("Color Picker", 380, 340)
    {
        sw.SetResourceReference(Border.BorderBrushProperty, "Line");
        var s = new StackPanel(); Main.Children.Add(s);
        var side = new StackPanel { Margin = new Thickness(16, 4, 0, 0) };
        side.Children.Add(UI.Label("Klik tombol, lalu klik warna mana saja di layar.", true, 12)); side.Children.Add(UI.Btn("Ambil warna", Pick, true));
        s.Children.Add(UI.Row(sw, side));
        foreach (var (name, box) in new[] { ("HEX", hex), ("RGB", rgb), ("HSL", hsl) })
        {
            var l = UI.Label(name, true); l.Width = 42; l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0);
            var b = box; s.Children.Add(UI.Row(l, b, UI.Btn("Salin", () => Copy(b.Text))));
        }
        s.Children.Add(UI.Label("Riwayat", true, 12)); s.Children.Add(hist);
    }
    static void Copy(string t) { if (t == "") return; try { Clipboard.SetText(t); Toast.Show("Disalin: " + t); } catch { } }
    void Pick()
    {
        var o = new PickOverlay(); o.Picked += Set; Hide();
        o.Closed += (_, _) => { Show(); Activate(); };
        o.Show(); o.Activate();
    }
    void Set(Color c)
    {
        Apply(c); Copy(hex.Text);
        var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 6), Background = new SolidColorBrush(c), Cursor = Cursors.Hand, ToolTip = hex.Text };
        b.MouseLeftButtonUp += (_, _) => { Apply(c); Copy(hex.Text); };
        hist.Children.Insert(0, b); if (hist.Children.Count > 14) hist.Children.RemoveAt(14);
    }
    void Apply(Color c)
    {
        sw.Background = new SolidColorBrush(c); hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}"; rgb.Text = $"rgb({c.R}, {c.G}, {c.B})"; hsl.Text = Hsl(c);
    }
    static string Hsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2, h = 0, s = 0;
        if (mx != mn)
        {
            var d = mx - mn; s = l > .5 ? d / (2 - mx - mn) : d / (mx + mn);
            h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4; h *= 60;
        }
        return $"hsl({h:0}, {s * 100:0}%, {l * 100:0}%)";
    }
}

// Overlay layar penuh nyaris transparan: baca warna piksel di bawah kursor.
sealed class PickOverlay : Window
{
    public event Action<Color>? Picked;
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    static Color Read()
    {
        GetCursorPos(out var p); var dc = GetDC(IntPtr.Zero); var v = GetPixel(dc, p.X, p.Y); ReleaseDC(IntPtr.Zero, dc);
        return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
    }
    public PickOverlay()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        var cv = new Canvas(); Content = cv;
        var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5) };
        var tx = new TextBlock { Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var tip = new Border { Background = new SolidColorBrush(Color.FromArgb(0xE6, 28, 28, 34)), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 12, 6), IsHitTestVisible = false, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, tx } } };
        cv.Children.Add(tip);
        MouseMove += (_, e) =>
        {
            var c = Read(); dot.Background = new SolidColorBrush(c); tx.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var p = e.GetPosition(cv); Canvas.SetLeft(tip, p.X + 20); Canvas.SetTop(tip, p.Y + 20);
        };
        MouseLeftButtonDown += (_, _) => { var c = Read(); Close(); Picked?.Invoke(c); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => Focus();
    }
}
