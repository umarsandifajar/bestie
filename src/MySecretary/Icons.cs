using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Shapes = System.Windows.Shapes;
namespace MySecretary;

// Ikon minimalis monokrom (garis tipis).
static class Icons
{
    // Glyph: garis tipis, grid 24x24
    public const string Pomo = "M4.5,13.5 A7.5,7.5 0 1 1 19.5,13.5 A7.5,7.5 0 1 1 4.5,13.5 Z M12,13.5 L12,9 M9.5,3 L14.5,3";
    public const string Todo = "M4.5,12 A7.5,7.5 0 1 1 19.5,12 A7.5,7.5 0 1 1 4.5,12 Z M8.5,9.5 L15.5,9.5 M8.5,12.5 L15.5,12.5 M8.5,15.5 L12.5,15.5";
    public const string Zip = "M4,4 L9,9 M9,5 L9,9 L5,9 M20,4 L15,9 M15,5 L15,9 L19,9 M4,20 L9,15 M5,15 L9,15 L9,19 M20,20 L15,15 M19,15 L15,15 L15,19";
    public const string Conv = "M4,8 L17,8 M14,5 L17,8 L14,11 M20,16 L7,16 M10,13 L7,16 L10,19";
    public const string Stash = "M3,14 L3,19 A1,1 0 0 0 4,20 L20,20 A1,1 0 0 0 21,19 L21,14 M3,14 L7,14 L8.5,16.5 L15.5,16.5 L17,14 L21,14 M12,4 L12,12 M9,9 L12,12 L15,9";
    public const string Organize = "M4,5 H10 V11 H4 Z M14,5 H20 V9 H14 Z M14,13 H20 V19 H14 Z M4,15 H10 V19 H4 Z";
    public const string Rename = "M9,5 H15 M12,5 V19 M9,19 H15 M5,8 L3,12 L5,16 M19,8 L21,12 L19,16";
    public const string SizeChk = "M12,4 A8,8 0 1 0 20,12 L12,12 Z M14.5,3.5 A8,8 0 0 1 20.5,9.5 L14.5,9.5 Z";
    public const string Clip = "M9,4 H15 V6.5 H9 Z M7,5.5 H6.5 A1.5,1.5 0 0 0 5,7 V19.5 A1.5,1.5 0 0 0 6.5,21 H17.5 A1.5,1.5 0 0 0 19,19.5 V7 A1.5,1.5 0 0 0 17.5,5.5 H17 M8.5,12 H15.5 M8.5,16 H13";
    public const string TextFmt = "M4,6 H20 M4,10.5 H15 M4,15 H20 M4,19.5 H12";
    public const string Note = "M5,4 H19 V14 L14,20 H5 Z M14,20 V14 H19 M8,9 H16 M8,13 H12";
    public const string Timer = "M7,4 H17 M7,20 H17 M8,4 V7.5 L12,12 L16,7.5 V4 M8,20 V16.5 L12,12 L16,16.5 V20";
    public const string Pick = "M14,6 L18,10 M16,4 L20,8 L17.5,10.5 L13.5,6.5 Z M13.5,7.5 L6,15 L5,19 L9,18 L16.5,10.5";
    public const string Pdf = "M7,3 H14 L19,8 V20 A1,1 0 0 1 18,21 H7 A1,1 0 0 1 6,20 V4 A1,1 0 0 1 7,3 Z M14,3 V8 H19 M9,13 H16 M9,17 H14";
    public const string Mark = "M12,3.5 C12,3.5 6,10 6,14.5 A6,6 0 0 0 18,14.5 C18,10 12,3.5 12,3.5 Z";
    public const string App = "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z";
    public const string Folder = "M3,7 A2,2 0 0 1 5,5 L9,5 L11,7 L19,7 A2,2 0 0 1 21,9 L21,17 A2,2 0 0 1 19,19 L5,19 A2,2 0 0 1 3,17 Z";
    public const string Link = "M10,14 A4,4 0 0 0 15.7,14 L18.7,11 A4,4 0 0 0 13,5.3 L11.5,6.8 M14,10 A4,4 0 0 0 8.3,10 L5.3,13 A4,4 0 0 0 11,18.7 L12.5,17.2";

    // Glyph untuk "chrome" panel (bukan ubin)
    public const string Gear = "M5,12 A7,7 0 1 0 19,12 A7,7 0 1 0 5,12 Z M9.2,12 A2.8,2.8 0 1 0 14.8,12 A2.8,2.8 0 1 0 9.2,12 Z M19,12 L21.5,12 M16.95,16.95 L18.72,18.72 M12,19 L12,21.5 M7.05,16.95 L5.28,18.72 M5,12 L2.5,12 M7.05,7.05 L5.28,5.28 M12,5 L12,2.5 M16.95,7.05 L18.72,5.28";
    public const string PinG = "M9.5,3.5 H14.5 M10.5,3.5 L10,10 L7.5,13 H16.5 L14,10 L13.5,3.5 M12,13 V20.5";
    public const string Search = "M4.5,10.5 A6,6 0 1 0 16.5,10.5 A6,6 0 1 0 4.5,10.5 Z M15,15 L20,20";
    public const string Eye = "M2,12 C5,6.5 8.5,5 12,5 C15.5,5 19,6.5 22,12 C19,17.5 15.5,19 12,19 C8.5,19 5,17.5 2,12 Z M9.5,12 A2.5,2.5 0 1 0 14.5,12 A2.5,2.5 0 1 0 9.5,12 Z";
    public const string EyeOff = "M2,12 C5,6.5 8.5,5 12,5 C15.5,5 19,6.5 22,12 C19,17.5 15.5,19 12,19 C8.5,19 5,17.5 2,12 Z M9.5,12 A2.5,2.5 0 1 0 14.5,12 A2.5,2.5 0 1 0 9.5,12 Z M4,4 L20,20";
    public const string Plus = "M12,5 V19 M5,12 H19";
    public const string Trash = "M5,7 H19 M9.5,4 H14.5 M7,7 L8,20 H16 L17,7 M10.5,11 V16 M13.5,11 V16";
    public const string Check = "M7,12.5 L10.5,16 L17,8.5";
    public const string ClockHands = "M12,7.5 V12 L15.2,14.2";
    public const string ClockFull = "M3.5,12 A8.5,8.5 0 1 0 20.5,12 A8.5,8.5 0 1 0 3.5,12 Z M12,7.5 V12 L15.2,14.2";
    public const string PauseHands = "M9.8,8.5 V15.5 M14.2,8.5 V15.5";
    public const string PauseFull = "M3.5,12 A8.5,8.5 0 1 0 20.5,12 A8.5,8.5 0 1 0 3.5,12 Z M9.8,9 V15 M14.2,9 V15";
    public const string CheckFull = "M3.5,12 A8.5,8.5 0 1 0 20.5,12 A8.5,8.5 0 1 0 3.5,12 Z M8,12.3 L10.8,15 L16,9.2";
    public const string Heart = "M12,20 C12,20 4,14.5 4,9.5 A4,4 0 0 1 12,8 A4,4 0 0 1 20,9.5 C20,14.5 12,20 12,20 Z";
    public const string Toa = "M3.5,10 V14 H7 L14,18.5 V5.5 L7,10 Z M7,14 L8.5,19.5 M17,9 A4,4 0 0 1 17,15 M19.5,6.5 A8,8 0 0 1 19.5,17.5";
    public const string Bell = "M6,10 A6,6 0 0 1 18,10 V15 L19.5,17 H4.5 L6,15 Z M10,19.5 A2,2 0 0 0 14,19.5 M12,3.5 V4.2";
    public const string Music = "M9,18 V6 L19,4 V16 M9,18 A2.5,2.5 0 1 1 4,18 A2.5,2.5 0 1 1 9,18 Z M19,16 A2.5,2.5 0 1 1 14,16 A2.5,2.5 0 1 1 19,16 Z";

    // Bagian ikon volume / mikrofon untuk side notch (digambar langsung di IslandView)
    public const string SpkBody = "M4,9.5 H7.5 L12.5,5.5 V18.5 L7.5,14.5 H4 Z";
    public const string SpkW1 = "M15.5,9.5 A3.5,3.5 0 0 1 15.5,14.5";
    public const string SpkW2 = "M18,7 A7,7 0 0 1 18,17";
    public const string SpkX = "M16,9.5 L21,14.5 M21,9.5 L16,14.5";
    public const string MicOn = "M12,3.5 A3,3 0 0 1 15,6.5 V11.5 A3,3 0 0 1 9,11.5 V6.5 A3,3 0 0 1 12,3.5 Z M5.5,11 A6.5,6.5 0 0 0 18.5,11 M12,17.5 V20.5 M8.5,20.5 H15.5";
    public const string MicOff = MicOn + " M4,4 L20,20";

    // Ikon minimalis: glyph garis tipis (monokrom mengikuti tema) di dalam sel kecil bersudut halus.
    public static UIElement Glyph(string d, double size, Brush brush, double thick = 1.5, bool fill = false)
    {
        var p = new Shapes.Path
        {
            Data = Geometry.Parse(d), Stroke = brush, StrokeThickness = thick, Fill = fill ? brush : null,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        var cv = new Canvas { Width = 24, Height = 24 }; cv.Children.Add(p);
        return new Viewbox { Width = size, Height = size, Child = cv, IsHitTestVisible = false };
    }

    // Saat kursor menyentuh `trigger`, goresan ikon berubah halus dari warna diam menjadi gradien cyan -> violet.
    // Mapping absolut (koordinat 24x24) supaya garis lurus tanpa tinggi/lebar tetap kebagian gradien.
    public static void Tint(UIElement trigger, UIElement glyph, Color rest)
    {
        var paths = new List<Shapes.Path>(); Collect(glyph, paths);
        if (paths.Count == 0) return;
        var g = new LinearGradientBrush(new GradientStopCollection { new GradientStop(rest, 0), new GradientStop(rest, 1) }, new Point(0, 0), new Point(24, 24)) { MappingMode = BrushMappingMode.Absolute };
        foreach (var p in paths) { p.Stroke = g; if (p.Fill != null) p.Fill = g; }
        void Go(Color a, Color b, int ms)
        {
            g.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(a, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.EzOut });
            g.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(b, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Fx.EzOut });
        }
        trigger.MouseEnter += (_, _) => Go(Pal.Cyan, Pal.Violet, 180);
        trigger.MouseLeave += (_, _) => Go(rest, rest, 260);
    }
    static void Collect(DependencyObject d, List<Shapes.Path> l)
    {
        if (d is Shapes.Path p) l.Add(p);
        foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject o) Collect(o, l);
    }

    static UIElement Ink(string glyph, double s) => Glyph(glyph, s * .56, Pal.Br(Pal.Ink), 1.4);

    // Sel ikon alat (ukuran s px)
    public static UIElement Tile(string glyph, double s) => Cell(s, Ink(glyph, s));

    public static UIElement ForPin(Pin p, double s)
    {
        ImageSource? img = null;
        if (!string.IsNullOrWhiteSpace(p.Icon) && File.Exists(p.Icon)) img = LoadImage(p.Icon!);
        else if (p.Kind == "app") img = ExeIcon(p.Path);
        if (img != null)
        {
            var im = new Image { Source = img, Width = s * .62, Height = s * .62, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            return Cell(s, im);
        }
        if (p.Kind == "folder") return Cell(s, Ink(Folder, s));
        if (p.Kind == "url") return Cell(s, Ink(Link, s));
        return Cell(s, Ink(App, s));
    }

    static UIElement Cell(double s, UIElement? inner)
    {
        var b = new Border
        {
            Width = s, Height = s, CornerRadius = new CornerRadius(8), IsHitTestVisible = false,
            Background = Pal.Br(Pal.Raised), BorderBrush = Pal.Br(Pal.Stroke), BorderThickness = new Thickness(1)
        };
        if (inner != null)
        {
            if (inner is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
            b.Child = inner;
        }
        return b;
    }

    static ImageSource? LoadImage(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path); bi.DecodePixelWidth = 96; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit();
            bi.Freeze(); return bi;
        }
        catch { return null; }
    }

    // Ikon asli aplikasi (dari file .exe/.lnk dll). Untuk nama pendek seperti "notepad.exe" dicari dulu di System32 dan PATH.
    static ImageSource? ExeIcon(string path)
    {
        try
        {
            var p = Resolve(Environment.ExpandEnvironmentVariables(path));
            if (p == null) return null;
            using var ic = System.Drawing.Icon.ExtractAssociatedIcon(p);
            if (ic == null) return null;
            var bs = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze(); return bs;
        }
        catch { return null; }
    }
    static string? Resolve(string p)
    {
        if (File.Exists(p)) return p;
        if (p.Contains('\\') || p.Contains('/')) return null;
        var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs) { try { var f = System.IO.Path.Combine(d.Trim(), p); if (File.Exists(f)) return f; } catch { } }
        return null;
    }
}
