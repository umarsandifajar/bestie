using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using SkiaSharp;
namespace MySecretary;

// ---------------- Batch Watermark ----------------
// Tempel watermark teks atau logo ke banyak gambar (JPG/PNG/WebP/BMP) dan PDF sekaligus.
// Posisi: 9 titik atau "Ubin" (berulang diagonal). Opasitas, ukuran, rotasi bisa diatur; pratinjau langsung.
// Hasil selalu disimpan sebagai file BARU (nama-wm), file asli tidak disentuh.
// Gambar: SkiaSharp. PDF: PDFsharp (watermark digambar di atas tiap halaman sebagai gambar PNG transparan).

// Gaya "chip" bersama (dipakai Watermark & Reminders): aktif = gradien cyan-violet tipis + garis gradien.
static class Chips
{
    public static void Style(Border b, bool on)
    {
        if (on) { b.Background = new LinearGradientBrush(Pal.Alpha(Pal.Cyan, 0x2A), Pal.Alpha(Pal.Violet, 0x2A), 0); b.BorderBrush = Pal.CV(0); }
        else { b.SetResourceReference(Border.BackgroundProperty, "Fill"); b.SetResourceReference(Border.BorderBrushProperty, "Line"); }
    }
}

sealed class WmSpec
{
    public bool Logo;
    public string Text = "";
    public SKBitmap? LogoBmp;
    public uint Rgb;
    public double Opacity = .4;      // 0..1
    public double SizePct = 30;      // % lebar gambar/halaman
    public int Pos = 9;              // 0..8 = grid 3x3, 9 = ubin
    public double Angle = -30;       // derajat
}

sealed record WmJob(string[] Files, string OutDir, WmSpec Spec, int Fmt, int Q);

static class WmEngine
{
    public static readonly uint[] Palette = { 0xFFFFFF, 0x111111, 0x22D3EE, 0xA78BFA, 0x8E8E96, 0xFACC15 };
    public static readonly string[] PaletteNames = { "Putih", "Hitam", "Cyan", "Violet", "Abu-abu", "Kuning" };

    static SKTypeface? tf;
    static SKTypeface Face => tf ??= SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    // Bentuk "stempel" watermark (teks atau logo), sudah diputar dan sudah diberi opasitas. baseW = lebar dasar (px) sebelum rotasi.
    public static SKBitmap? Stamp(WmSpec s, double baseW)
    {
        baseW = Math.Max(8, baseW);
        SKBitmap raw;
        if (s.Logo)
        {
            if (s.LogoBmp == null) return null;
            double sc = baseW / s.LogoBmp.Width;
            int w = Math.Max(1, (int)Math.Round(s.LogoBmp.Width * sc)), h = Math.Max(1, (int)Math.Round(s.LogoBmp.Height * sc));
            raw = s.LogoBmp.Resize(new SKImageInfo(w, h), SKFilterQuality.High) ?? s.LogoBmp.Copy();
        }
        else
        {
            var text = s.Text.Trim(); if (text == "") return null;
            var col = new SKColor((byte)(s.Rgb >> 16), (byte)(s.Rgb >> 8), (byte)s.Rgb);
            using var p = new SKPaint { IsAntialias = true, Color = col, Typeface = Face, TextSize = 100 };
            float w100 = Math.Max(1, p.MeasureText(text));
            float size = (float)Math.Min(100 * baseW / w100, baseW * .6);
            size = Math.Max(size, 6);
            p.TextSize = size;
            p.GetFontMetrics(out var fm);
            float tw = p.MeasureText(text), th = fm.Descent - fm.Ascent, pad = size * .12f;
            raw = new SKBitmap(Math.Max(1, (int)Math.Ceiling(tw + 2 * pad)), Math.Max(1, (int)Math.Ceiling(th + 2 * pad)));
            using var c = new SKCanvas(raw);
            c.Clear(SKColors.Transparent);
            c.DrawText(text, pad, pad - fm.Ascent, p);
        }

        using (raw)
        {
            double rad = s.Angle * Math.PI / 180, cs = Math.Abs(Math.Cos(rad)), sn = Math.Abs(Math.Sin(rad));
            int bw = Math.Max(1, (int)Math.Ceiling(raw.Width * cs + raw.Height * sn)), bh = Math.Max(1, (int)Math.Ceiling(raw.Width * sn + raw.Height * cs));
            var r = new SKBitmap(bw, bh);
            using var cv = new SKCanvas(r);
            cv.Clear(SKColors.Transparent);
            cv.Translate(bw / 2f, bh / 2f);
            cv.RotateDegrees((float)s.Angle);
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Math.Clamp(s.Opacity, 0, 1))) };
            cv.DrawBitmap(raw, -raw.Width / 2f, -raw.Height / 2f, paint);
            return r;
        }
    }

    // Titik kiri-atas tempat stempel digambar (area W x H, stempel sw x sh)
    public static List<(float x, float y)> Spots(double W, double H, double sw, double sh, int pos)
    {
        var l = new List<(float x, float y)>();
        if (pos >= 9)
        {
            double gx = Math.Max(24, sw * 1.55), gy = Math.Max(24, sh * 1.9); int row = 0;
            for (double y = gy * .2; y < H && l.Count < 300; y += gy, row++)
                for (double x = (row % 2 == 1 ? gx * .5 : 0) - gx * .15; x < W && l.Count < 300; x += gx) l.Add(((float)x, (float)y));
            return l;
        }
        double m = Math.Min(W, H) * .03; int c = pos % 3, r = pos / 3;
        double px = c == 0 ? m : c == 1 ? (W - sw) / 2 : W - sw - m;
        double py = r == 0 ? m : r == 1 ? (H - sh) / 2 : H - sh - m;
        l.Add(((float)px, (float)py));
        return l;
    }

    // Gambar watermark ke kanvas bitmap W x H
    public static void Apply(SKCanvas c, int W, int H, WmSpec s)
    {
        using var st = Stamp(s, W * s.SizePct / 100);
        if (st == null) return;
        foreach (var (x, y) in Spots(W, H, st.Width, st.Height, s.Pos)) c.DrawBitmap(st, x, y);
    }

    public static BitmapSource ToWpf(SKBitmap b)
    {
        using var img = SKImage.FromBitmap(b);
        using var d = img.Encode(SKEncodedImageFormat.Png, 100);
        var bi = new BitmapImage();
        bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = new MemoryStream(d.ToArray()); bi.EndInit(); bi.Freeze();
        return bi;
    }
}

sealed class WatermarkPanel : AppPanel
{
    static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    readonly Config cfg;
    readonly List<string> files = new();
    readonly StackPanel listHost = new();
    readonly TextBox textBox = new() { MaxLength = 120, Margin = new Thickness(0, 0, 0, 10) };
    readonly TextBox logoBox = new() { IsReadOnly = false, Margin = new Thickness(0, 0, 8, 10) };
    readonly StackPanel textPanel = new(), logoPanel = new();
    readonly Image prevImg = new() { Stretch = Stretch.Uniform, Margin = new Thickness(6) };
    readonly TextBlock prevCap = UI.Label("", true, 11);
    readonly TextBlock outLbl = UI.Label("", true, 12);
    readonly TextBlock status = UI.Label("", true);
    readonly ProgressBar bar = new() { Margin = new Thickness(0, 0, 0, 8), Maximum = 1, Value = 0 };
    readonly DispatcherTimer deb = new() { Interval = TimeSpan.FromMilliseconds(140) };
    readonly Button run, reveal;

    string outDir = ""; string? lastOut; bool busy;
    bool logo; int color, pos, fmt, q;
    double opacity, sizePct, angle;
    SKBitmap? baseBmp; string? baseKey;
    SKBitmap? logoBmp; string? logoKey;

    public WatermarkPanel(Config c) : base("Batch Watermark", 600, 660)
    {
        cfg = c;
        AllowDrop = true; Drop += OnDrop;

        // nilai awal dari config (ingat pengaturan terakhir)
        textBox.Text = cfg.WmText; logoBox.Text = cfg.WmLogo; logo = cfg.WmUseLogo;
        opacity = Math.Clamp(cfg.WmOpacity, 5, 100); sizePct = Math.Clamp(cfg.WmSize, 5, 90); angle = Math.Clamp(cfg.WmAngle, -90, 90);
        pos = Math.Clamp(cfg.WmPos, 0, 9); color = Math.Clamp(cfg.WmColor, 0, WmEngine.Palette.Length - 1);

        var s = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        Main.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = s });

        s.Children.Add(UI.Label("Tempel teks atau logo ke banyak gambar dan PDF sekaligus. Hasil disimpan sebagai file baru (nama-wm), file asli tidak diubah.", true));
        s.Children.Add(UI.Segmented(new[] { "Watermark teks", "Watermark logo" }, logo ? 1 : 0, i => { logo = i == 1; ModeUi(); Preview(); }));

        // --- pratinjau
        var pv = new Border { Height = 200, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 4), Child = prevImg };
        pv.SetResourceReference(Border.BackgroundProperty, "Pop"); pv.SetResourceReference(Border.BorderBrushProperty, "Line");
        s.Children.Add(pv); s.Children.Add(prevCap);

        // --- daftar file
        s.Children.Add(UI.Row(UI.Btn("Tambah file…", Add), UI.Btn("Kosongkan", () => { files.Clear(); RenderList(); Preview(true); })));
        s.Children.Add(new ScrollViewer { Height = 92, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listHost, Margin = new Thickness(0, 0, 0, 12) });

        // --- teks
        textPanel.Children.Add(UI.Label("Teks watermark", true, 12));
        textPanel.Children.Add(textBox);
        textPanel.Children.Add(UI.Label("Warna", true, 12));
        textPanel.Children.Add(ColorChips());
        s.Children.Add(textPanel);

        // --- logo
        logoPanel.Children.Add(UI.Label("File logo (PNG transparan paling bagus)", true, 12));
        var pick = UI.Btn("Pilih…", () => { var d = new OpenFileDialog { Filter = "Gambar|*.png;*.jpg;*.jpeg;*.webp;*.bmp" }; if (d.ShowDialog() == true) logoBox.Text = d.FileName; });
        pick.Margin = new Thickness(0, 0, 0, 10);
        var lr = new DockPanel { LastChildFill = true }; DockPanel.SetDock(pick, Dock.Right); lr.Children.Add(pick); lr.Children.Add(logoBox);
        logoPanel.Children.Add(lr);
        s.Children.Add(logoPanel);

        // --- slider
        s.Children.Add(Slide("Opasitas", 5, 100, 5, opacity, "%", v => { opacity = v; Preview(); }));
        s.Children.Add(Slide("Ukuran (% lebar)", 5, 90, 1, sizePct, "%", v => { sizePct = v; Preview(); }));
        s.Children.Add(Slide("Rotasi", -90, 90, 5, angle, "°", v => { angle = v; Preview(); }));

        // --- posisi
        s.Children.Add(UI.Label("Posisi", true, 12));
        s.Children.Add(PosPicker());

        // --- keluaran
        s.Children.Add(UI.Label("Format hasil untuk gambar", true, 12));
        s.Children.Add(UI.Segmented(new[] { "Sama", "JPG", "PNG", "WebP" }, 0, i => fmt = i));
        s.Children.Add(UI.Label("Kualitas JPG / WebP", true, 12));
        s.Children.Add(UI.Segmented(new[] { "Tinggi", "Hemat ukuran" }, 0, i => q = i));
        s.Children.Add(outLbl);
        s.Children.Add(UI.Row(UI.Btn("Folder output…", () => { var f = UI.Folder(); if (f != null) { outDir = f; OutText(); } }), UI.Btn("Reset", () => { outDir = ""; OutText(); })));

        run = UI.Btn("Beri watermark", Go, true);
        reveal = UI.Btn("Buka hasil", () => { if (lastOut != null) UI.Reveal(lastOut); }); reveal.IsEnabled = false;
        s.Children.Add(UI.Row(run, reveal));
        s.Children.Add(bar); s.Children.Add(status);

        textBox.TextChanged += (_, _) => Preview();
        logoBox.TextChanged += (_, _) => Preview();
        deb.Tick += (_, _) => { deb.Stop(); DoPreview(); };
        Closed += (_, _) => { deb.Stop(); Store(); baseBmp?.Dispose(); logoBmp?.Dispose(); };

        ModeUi(); OutText(); RenderList(); Preview(true);
    }

    // ---------- kontrol ----------
    static FrameworkElement Slide(string label, double min, double max, double step, double val, string unit, Action<double> on)
    {
        var name = new TextBlock { Text = label, FontSize = 12 }; name.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
        var num = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Pal.CV(0, true) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(num, Dock.Right); head.Children.Add(num); head.Children.Add(name);

        var track = new Grid { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var rail = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        rail.SetResourceReference(Border.BackgroundProperty, "Track");
        var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Background = Pal.CV(0) };
        var kt = new TranslateTransform();
        var knob = new System.Windows.Shapes.Ellipse
        {
            Width = 16, Height = 16, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = kt, Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = .4 }
        };
        track.Children.Add(rail); track.Children.Add(fill); track.Children.Add(knob);

        double v = Math.Clamp(val, min, max);
        void Place()
        {
            double uw = Math.Max(1, track.ActualWidth - 16), x = (v - min) / (max - min) * uw;
            fill.Width = Math.Max(0, x); kt.X = x; num.Text = Math.Round(v) + unit;
        }
        void SetFrom(MouseEventArgs e)
        {
            double uw = Math.Max(1, track.ActualWidth - 16), p = Math.Clamp((e.GetPosition(track).X - 8) / uw, 0, 1);
            v = Math.Clamp(Math.Round((min + p * (max - min)) / step) * step, min, max); Place(); on(v);
        }
        track.SizeChanged += (_, _) => Place();
        track.MouseLeftButtonDown += (_, e) => { track.CaptureMouse(); SetFrom(e); };
        track.MouseMove += (_, e) => { if (track.IsMouseCaptured) SetFrom(e); };
        track.MouseLeftButtonUp += (_, _) => track.ReleaseMouseCapture();
        track.MouseWheel += (_, e) => { v = Math.Clamp(v + Math.Sign(e.Delta) * step, min, max); Place(); on(v); e.Handled = true; };
        Place();

        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        box.Children.Add(head); box.Children.Add(track);
        return box;
    }

    FrameworkElement ColorChips()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var dots = new List<Border>();
        void Paint()
        {
            for (int i = 0; i < dots.Count; i++)
            {
                if (i == color) dots[i].BorderBrush = Pal.CV(0); else dots[i].SetResourceReference(Border.BorderBrushProperty, "Line");
            }
        }
        for (int i = 0; i < WmEngine.Palette.Length; i++)
        {
            int k = i; uint v = WmEngine.Palette[k];
            var d = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 9, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)), ToolTip = WmEngine.PaletteNames[k]
            };
            d.MouseLeftButtonUp += (_, _) => { color = k; Paint(); Preview(); };
            dots.Add(d); row.Children.Add(d);
        }
        Paint();
        return row;
    }

    FrameworkElement PosPicker()
    {
        var tileTx = new TextBlock { Text = "Ubin (berulang)", FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var tile = new Border
        {
            Height = 30, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Child = tileTx
        };
        var grid = new UniformGrid { Rows = 3, Columns = 3, Width = 99, Height = 66 };
        var cells = new List<Border>();
        void Paint()
        {
            for (int i = 0; i < cells.Count; i++) Chips.Style(cells[i], pos == i);
            Chips.Style(tile, pos == 9);
            tileTx.SetResourceReference(TextBlock.ForegroundProperty, pos == 9 ? "Cyan" : "Ink");
        }
        for (int i = 0; i < 9; i++)
        {
            int k = i;
            var b = new Border { Margin = new Thickness(2), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            b.MouseLeftButtonUp += (_, _) => { pos = k; Paint(); Preview(); };
            cells.Add(b); grid.Children.Add(b);
        }
        tile.MouseLeftButtonUp += (_, _) => { pos = 9; Paint(); Preview(); };
        Paint();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(grid); row.Children.Add(tile);
        return row;
    }

    void ModeUi()
    {
        textPanel.Visibility = logo ? Visibility.Collapsed : Visibility.Visible;
        logoPanel.Visibility = logo ? Visibility.Visible : Visibility.Collapsed;
    }

    void OutText() => outLbl.Text = "Simpan ke: " + (outDir == "" ? "folder yang sama dengan file sumber" : outDir);

    void Store()
    {
        cfg.WmText = textBox.Text; cfg.WmLogo = logoBox.Text; cfg.WmUseLogo = logo;
        cfg.WmOpacity = opacity; cfg.WmSize = sizePct; cfg.WmAngle = angle; cfg.WmPos = pos; cfg.WmColor = color;
        cfg.Save();
    }

    // ---------- daftar file ----------
    void Add()
    {
        var d = new OpenFileDialog { Multiselect = true, Filter = "Gambar & PDF|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.pdf" };
        if (d.ShowDialog() == true) AddFiles(d.FileNames);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] fs) return;
        AddFiles(fs.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p) : new[] { p }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    void AddFiles(IEnumerable<string> paths)
    {
        int bad = 0;
        foreach (var p in paths)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            if (!File.Exists(p) || !(ImgExt.Contains(e) || e == ".pdf")) { bad++; continue; }
            if (!files.Contains(p, StringComparer.OrdinalIgnoreCase)) files.Add(p);
        }
        RenderList(); Preview(true);
        status.Text = bad > 0 ? $"{bad} file dilewati (hanya gambar JPG/PNG/WebP/BMP dan PDF)." : $"{files.Count} file siap diproses.";
    }

    void RenderList()
    {
        listHost.Children.Clear();
        if (files.Count == 0)
        {
            var t = UI.Label("Tarik & lepas gambar / PDF ke jendela ini, atau klik Tambah file…", true, 12);
            t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(4, 16, 4, 0); listHost.Children.Add(t); return;
        }
        for (int i = 0; i < files.Count; i++)
        {
            int k = i; var p = files[k];
            var g = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            tx.Children.Add(new TextBlock { Text = Path.GetFileName(p), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 });
            var sz = UI.Label(UI.Size(new FileInfo(p).Length), true, 11); sz.Margin = new Thickness(0); tx.Children.Add(sz);
            var rm = new Button { Content = "✕", Padding = new Thickness(7, 2, 7, 2), FontSize = 11, MinWidth = 26 };
            rm.Click += (_, _) => { files.RemoveAt(k); RenderList(); Preview(true); };
            Grid.SetColumn(rm, 1); g.Children.Add(tx); g.Children.Add(rm);
            listHost.Children.Add(g);
        }
    }

    // ---------- pratinjau ----------
    void Preview(bool reload = false)
    {
        if (reload) baseKey = null;
        deb.Stop(); deb.Start();
    }

    WmSpec Spec() => new()
    {
        Logo = logo, Text = textBox.Text, LogoBmp = LogoBitmap(), Rgb = WmEngine.Palette[color],
        Opacity = opacity / 100, SizePct = sizePct, Pos = pos, Angle = angle
    };

    SKBitmap? LogoBitmap()
    {
        if (!logo) return null;
        var p = logoBox.Text.Trim();
        if (p != logoKey)
        {
            logoBmp?.Dispose(); logoBmp = null; logoKey = p;
            try { if (File.Exists(p)) logoBmp = DocConverterPanel.LoadBmp(p); } catch { }
        }
        return logoBmp;
    }

    void DoPreview()
    {
        try
        {
            var first = files.Count > 0 ? files[0] : null;
            if (baseBmp == null || baseKey != (first ?? ""))
            {
                baseBmp?.Dispose(); baseBmp = LoadBase(first); baseKey = first ?? "";
            }
            using var comp = baseBmp!.Copy();
            using (var c = new SKCanvas(comp)) WmEngine.Apply(c, comp.Width, comp.Height, Spec());
            prevImg.Source = WmEngine.ToWpf(comp);
            prevCap.Text = first == null ? "Pratinjau contoh. Tambahkan file untuk melihat hasil pada file aslimu."
                : first.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "Pratinjau: halaman PDF contoh (hasil asli mengikuti tiap halaman)"
                : "Pratinjau: " + Path.GetFileName(first);
        }
        catch { }
    }

    static SKBitmap LoadBase(string? f)
    {
        if (f == null) return Sample();
        if (f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return Sheet();
        try
        {
            using var raw = DocConverterPanel.LoadBmp(f);
            double sc = Math.Min(1, 720.0 / Math.Max(raw.Width, raw.Height));
            SKBitmap res = sc < 1
                ? raw.Resize(new SKImageInfo(Math.Max(1, (int)(raw.Width * sc)), Math.Max(1, (int)(raw.Height * sc))), SKFilterQuality.Medium) ?? raw.Copy()
                : raw.Copy();
            var flat = DocConverterPanel.Flatten(res); res.Dispose();
            return flat;
        }
        catch { return Sample(); }
    }

    static SKBitmap Sample()
    {
        var b = new SKBitmap(640, 400);
        using var c = new SKCanvas(b);
        using var p = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(640, 400),
                new[] { new SKColor(0x1E, 0x29, 0x3B), new SKColor(0x4C, 0x1D, 0x95), new SKColor(0x0E, 0x74, 0x90) }, null, SKShaderTileMode.Clamp)
        };
        c.DrawRect(0, 0, 640, 400, p);
        return b;
    }

    static SKBitmap Sheet()
    {
        var b = new SKBitmap(420, 594);
        using var c = new SKCanvas(b); c.Clear(SKColors.White);
        using var p = new SKPaint { Color = new SKColor(0xD4, 0xD4, 0xD8), IsAntialias = true };
        for (int i = 0; i < 14; i++) c.DrawRect(40, 50 + i * 36, i % 5 == 4 ? 200 : 340, 8, p);
        return b;
    }

    // ---------- proses ----------
    async void Go()
    {
        if (busy) return;
        if (files.Count == 0) { status.Text = "Tambahkan gambar atau PDF dulu."; return; }
        var spec = Spec();
        if (logo && spec.LogoBmp == null) { status.Text = "Pilih file logo yang valid dulu."; return; }
        if (!logo && string.IsNullOrWhiteSpace(spec.Text)) { status.Text = "Isi teks watermark dulu."; return; }
        spec.LogoBmp = spec.LogoBmp?.Copy();   // salinan sendiri untuk thread latar
        Store();
        var job = new WmJob(files.ToArray(), outDir, spec, fmt, q);
        busy = true; run.IsEnabled = false; reveal.IsEnabled = false; bar.Maximum = Math.Max(1, files.Count); bar.Value = 0; status.Text = "Memproses…";
        var prog = new Progress<(int n, string m)>(t => { bar.Value = Math.Min(bar.Maximum, t.n); status.Text = t.m; });
        try
        {
            var (msg, last) = await Task.Run(() => Work(job, prog));
            status.Text = msg; lastOut = last; reveal.IsEnabled = last != null;
        }
        catch (Exception ex) { status.Text = "Gagal: " + ex.Message; }
        finally { spec.LogoBmp?.Dispose(); busy = false; run.IsEnabled = true; bar.Value = bar.Maximum; }
    }

    static (string, string?) Work(WmJob j, IProgress<(int, string)> pr)
    {
        var bad = new List<string>(); string? last = null; int ok = 0, pages = 0;
        for (int i = 0; i < j.Files.Length; i++)
        {
            var f = j.Files[i];
            pr.Report((i, $"Memberi watermark {Path.GetFileName(f)} ({i + 1}/{j.Files.Length})"));
            string? dst = null;
            try
            {
                var dir = j.OutDir != "" ? j.OutDir : Path.GetDirectoryName(f)!;
                Directory.CreateDirectory(dir);
                var ext = Path.GetExtension(f).ToLowerInvariant();
                var name = Path.GetFileNameWithoutExtension(f) + "-wm";
                if (ext == ".pdf")
                {
                    dst = DocConverterPanel.Unique(Path.Combine(dir, name + ".pdf"));
                    pages += StampPdf(f, dst, j.Spec);
                }
                else
                {
                    var fm = j.Fmt switch
                    {
                        1 => SKEncodedImageFormat.Jpeg,
                        2 => SKEncodedImageFormat.Png,
                        3 => SKEncodedImageFormat.Webp,
                        _ => ext is ".jpg" or ".jpeg" ? SKEncodedImageFormat.Jpeg : ext == ".webp" ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png
                    };
                    var oe = fm == SKEncodedImageFormat.Jpeg ? ".jpg" : fm == SKEncodedImageFormat.Webp ? ".webp" : ".png";
                    dst = DocConverterPanel.Unique(Path.Combine(dir, name + oe));
                    using var raw = DocConverterPanel.LoadBmp(f);
                    using var comp = new SKBitmap(raw.Width, raw.Height);
                    using (var c = new SKCanvas(comp))
                    {
                        c.Clear(fm == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
                        c.DrawBitmap(raw, 0, 0);
                        WmEngine.Apply(c, comp.Width, comp.Height, j.Spec);
                    }
                    File.WriteAllBytes(dst, DocConverterPanel.Enc(comp, fm, j.Q == 0 ? 92 : 78));
                }
                ok++; last = dst;
            }
            catch
            {
                bad.Add(Path.GetFileName(f));
                if (dst != null) { try { File.Delete(dst); } catch { } }
            }
        }
        if (ok == 0) throw new InvalidOperationException("Tidak ada file yang berhasil diberi watermark" + (bad.Count > 0 ? $" ({string.Join(", ", bad.Take(3))}). PDF berpassword tidak didukung." : "."));
        var msg = $"Selesai: {ok} file diberi watermark" + (pages > 0 ? $" ({pages} halaman PDF)" : "");
        if (bad.Count > 0) msg += $" · {bad.Count} gagal ({string.Join(", ", bad.Take(3))}{(bad.Count > 3 ? "…" : "")})";
        return (msg, last);
    }

    // PDF: tiap halaman ditempeli stempel PNG transparan (resolusi ~180 dpi). Mengembalikan jumlah halaman.
    static int StampPdf(string src, string dst, WmSpec s)
    {
        const double k = 2.5;   // piksel stempel per poin PDF
        var keep = new List<MemoryStream>();
        var imgs = new Dictionary<int, (XImage xi, double w, double h)>();
        try
        {
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            int n = doc.PageCount;
            for (int i = 0; i < n; i++)
            {
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                double pw = gfx.PageSize.Width, ph = gfx.PageSize.Height;
                int key = (int)Math.Round(pw);
                if (!imgs.TryGetValue(key, out var st))
                {
                    using var bmp = WmEngine.Stamp(s, pw * k * s.SizePct / 100) ?? throw new InvalidOperationException("Watermark kosong");
                    using var im = SKImage.FromBitmap(bmp);
                    using var d = im.Encode(SKEncodedImageFormat.Png, 100);
                    var ms = new MemoryStream(d.ToArray()); keep.Add(ms);
                    st = (XImage.FromStream(ms), bmp.Width / k, bmp.Height / k);
                    imgs[key] = st;
                }
                foreach (var (x, y) in WmEngine.Spots(pw, ph, st.w, st.h, s.Pos)) gfx.DrawImage(st.xi, x, y, st.w, st.h);
            }
            doc.Save(dst);
            return n;
        }
        finally
        {
            foreach (var v in imgs.Values) v.xi.Dispose();
            foreach (var m in keep) m.Dispose();
        }
    }
}
