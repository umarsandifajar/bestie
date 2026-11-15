using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
namespace MySecretary;

static class UI
{
    public static Button Btn(string t, Action a, bool primary = false)
    {
        var b = new Button { Content = t, Tag = primary ? "primary" : null, Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => a(); return b;
    }
    public static TextBlock Label(string t, bool dim = false, double size = 13)
    {
        var l = new TextBlock { Text = t, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        if (dim) l.SetResourceReference(TextBlock.ForegroundProperty, "Dim"); return l;
    }
    public static StackPanel Row(params UIElement[] c)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var x in c) p.Children.Add(x); return p;
    }
    public static Border Segmented(string[] names, int sel, Action<int> on)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal }; var g = Guid.NewGuid().ToString();
        for (int i = 0; i < names.Length; i++)
        {
            int k = i; var r = new RadioButton { Content = names[i], GroupName = g, IsChecked = i == sel };
            r.SetResourceReference(FrameworkElement.StyleProperty, "Seg"); r.Checked += (_, _) => on(k); sp.Children.Add(r);
        }
        var bd = new Border { Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        bd.SetResourceReference(Border.BackgroundProperty, "Fill"); return bd;
    }
    // Toggle gaya iOS: trek abu saat mati, gradien cyan -> violet saat nyala, kenop putih membal halus.
    public static FrameworkElement Toggle(string text, bool on, Action<bool> changed)
    {
        var knobT = new TranslateTransform(on ? 21 : 3, 0);
        var grad = new Border { CornerRadius = new CornerRadius(11), Background = Pal.CV(0), Opacity = on ? 1 : 0, IsHitTestVisible = false };
        var track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        var baseT = new Border { CornerRadius = new CornerRadius(11), IsHitTestVisible = false };
        baseT.SetResourceReference(Border.BackgroundProperty, "Track");
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = knobT, IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = .35 }
        };
        var g = new Grid(); g.Children.Add(baseT); g.Children.Add(grad); g.Children.Add(knob); track.Child = g;
        var lbl = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = true, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        DockPanel.SetDock(track, Dock.Left); row.Children.Add(track); row.Children.Add(lbl);
        row.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            Fx.To(knobT, TranslateTransform.XProperty, on ? 21 : 3, 480, Fx.EzBounce);
            Fx.To(grad, UIElement.OpacityProperty, on ? 1 : 0, 220);
            changed(on);
        };
        return row;
    }

    // Logo mini "bestie" (bes = warna teks tema, tie = gradien cyan-violet), sama dengan logo di header panel menu.
    // Letakkan di sudut kanan bawah kotak; tidak menangkap klik. Gradien ikut diperbarui saat tema diganti.
    public static FrameworkElement Logo(double size = 10)
    {
        var bes = new TextBlock { Text = "bes", FontFamily = Fx.Jakarta, FontSize = size, FontWeight = FontWeights.Bold };
        bes.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var tie = new TextBlock { Text = "tie", FontFamily = Fx.Jakarta, FontSize = size, FontWeight = FontWeights.Bold, Foreground = Pal.CV(0, true) };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        sp.Children.Add(bes); sp.Children.Add(tie);
        Action recolor = () => tie.Foreground = Pal.CV(0, true);
        sp.Loaded += (_, _) => Pal.Changed += recolor;
        sp.Unloaded += (_, _) => Pal.Changed -= recolor;
        return sp;
    }

    public static string? Folder() { var d = new OpenFolderDialog(); return d.ShowDialog() == true ? d.FolderName : null; }
    public static string Size(long b) { string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = b; int i = 0; while (v >= 1024 && i < 4) { v /= 1024; i++; } return $"{v:0.#} {u[i]}"; }
    public static void Reveal(string p) { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{p}\""); } catch { } }
    public static DataTemplate Tpl(string body) => (DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" + body + "</DataTemplate>");
}

// Jendela panel gaya Windows 11: sudut membulat, tombol tutup di kanan (merah saat disentuh), mengikuti tema.
public abstract class AppPanel : Window
{
    protected readonly Grid Main = new() { Margin = new Thickness(20, 0, 20, 20) };
    protected AppPanel(string title, double w, double h, bool topmost = false)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = topmost; Width = w + 24; Height = h + 24; WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var ttl = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0), IsHitTestVisible = false };

        var x = new TextBlock { Text = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        x.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var close = new Border
        {
            Width = 36, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Tutup", Child = x
        };
        bool down = false;
        close.MouseEnter += (_, _) => { close.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)); x.Foreground = Brushes.White; };
        close.MouseLeave += (_, _) => { down = false; close.Background = Brushes.Transparent; x.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); };
        // PENTING: tandai Handled supaya tidak sampai ke bar judul. Di sana DragMove() menelan MouseUp, itulah yang bikin X "macet".
        close.MouseLeftButtonDown += (_, e) => { down = true; e.Handled = true; };
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (down) { down = false; Close(); } };

        var bar = new Grid { Height = 40, Background = Brushes.Transparent };
        bar.Children.Add(ttl); bar.Children.Add(close);
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };

        var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(bar); Grid.SetRow(Main, 1); g.Children.Add(Main);
        var logo = UI.Logo(10); logo.Margin = new Thickness(0, 0, 16, 5); Grid.SetRow(logo, 1); g.Children.Add(logo);   // logo mini di sudut kanan bawah (area margin bawah 20 px)
        var root = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = .4 }, Child = g
        };
        root.SetResourceReference(Border.BackgroundProperty, "Win");
        root.SetResourceReference(Border.BorderBrushProperty, "Line");
        root.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");   // warna teks default untuk seluruh isi jendela
        Content = root;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
