using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Windows.Media.Control;
using static MySecretary.Fx;
namespace MySecretary;

// ---------- Core Audio (COM) : peak meter perangkat output default ----------
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    void GetPeakValue(out float peak);
}

// Memantau apakah ada audio keluar dari komputer (peak meter) dan lagu/judul yang sedang diputar (Windows media session).
sealed class AudioMonitor
{
    public bool Playing { get; private set; }
    public string Title { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public ImageSource? Cover { get; private set; }        // cover lagu (null = tidak ada)
    public bool Paused { get; private set; }               // sesi media sedang dijeda
    public TimeSpan Duration { get; private set; }         // 0 = tidak diketahui
    public TimeSpan Position                               // posisi lagu sekarang (diinterpolasi antar pembacaan)
    {
        get
        {
            var p = basePos + (Paused ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Environment.TickCount64 - baseAt));
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p < TimeSpan.Zero ? TimeSpan.Zero : p;
        }
    }
    public event Action? Changed;                 // status main/berhenti atau judul berubah
    public event Action<float, bool>? Frame;      // level terbaru + status, tiap tick

    readonly DispatcherTimer t = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
    IAudioMeterInformation? meter;
    GlobalSystemMediaTransportControlsSessionManager? mgr;
    long reacquire, metaAt, lastLoud, hold, baseAt; int loud, quick, coverTry; float level; bool metaBusy;
    TimeSpan basePos; string lastApp = "";
    GlobalSystemMediaTransportControlsSession? cur;        // sesi yang sedang dikendalikan (play/pause/next/prev)

    public AudioMonitor() { t.Tick += (_, _) => OnTick(); }
    public void Start() => t.Start();

    void Release() { if (meter != null) { try { Marshal.ReleaseComObject(meter); } catch { } meter = null; } }
    void Acquire()
    {
        Release();
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            en.GetDefaultAudioEndpoint(0, 1, out var dev);            // eRender, eMultimedia
            var iid = typeof(IAudioMeterInformation).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out var o);        // CLSCTX_ALL
            meter = (IAudioMeterInformation)o;
            Marshal.ReleaseComObject(dev); Marshal.ReleaseComObject(en);
        }
        catch { meter = null; }
    }

    void OnTick()
    {
        long now = Environment.TickCount64;
        if (meter == null || now >= reacquire) { Acquire(); reacquire = now + 8000; }
        float pk = 0;
        try { meter?.GetPeakValue(out pk); } catch { Release(); reacquire = now + 2000; }
        level = pk > level ? pk : level * .85f + pk * .15f;               // naik cepat, turun halus

        if (pk > 0.004f) { loud++; lastLoud = now; } else loud = 0;
        bool was = Playing;
        if (!Playing && loud >= 3) Playing = true;
        else if (Playing && now - lastLoud > 2000 && now > hold) Playing = false;

        if (Playing != was)
        {
            t.Interval = TimeSpan.FromMilliseconds(Playing ? 40 : 150);
            if (Playing) metaAt = 0; else { Title = ""; Artist = ""; Cover = null; Paused = false; Duration = TimeSpan.Zero; cur = null; }
            Changed?.Invoke();
        }
        if (Playing && now >= metaAt) { metaAt = now + (quick > 0 ? 500 : 2000); if (quick > 0) quick--; Meta(); }
        Frame?.Invoke(level, Playing);
    }

    static string AppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return "";
        var n = aumid.Split('!').Last().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        if (n.Contains('.')) n = n.Split('.').Last();
        return n.Length == 0 ? "" : char.ToUpperInvariant(n[0]) + n.Substring(1);
    }

    static GlobalSystemMediaTransportControlsSessionPlaybackStatus Status(GlobalSystemMediaTransportControlsSession s)
        => s.GetPlaybackInfo().PlaybackStatus;

    // Sesi yang dipakai: yang sedang Playing; kalau baru saja dijeda lewat balon, tetap sesi aplikasi yang sama.
    GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (mgr == null) return null;
        var all = mgr.GetSessions();
        foreach (var s in all) if (Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return s;
        if (Environment.TickCount64 < hold)
            foreach (var s in all) if (s.SourceAppUserModelId == lastApp) return s;
        return null;
    }

    static async Task<ImageSource?> LoadCover(Windows.Storage.Streams.IRandomAccessStreamReference? r)
    {
        if (r == null) return null;
        try
        {
            using var rs = await r.OpenReadAsync();
            using var st = rs.AsStream();
            var ms = new MemoryStream(); await st.CopyToAsync(ms); ms.Position = 0;
            if (ms.Length == 0) return null;
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 420; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    async void Meta()
    {
        if (metaBusy) return; metaBusy = true;
        string ti = "", ar = ""; bool paused = false, got = false;
        TimeSpan pos = TimeSpan.Zero, dur = TimeSpan.Zero; ImageSource? img = null; bool coverRead = false;
        try
        {
            mgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var pick = PickSession();
            cur = pick;
            if (pick != null)
            {
                got = true; lastApp = pick.SourceAppUserModelId ?? "";
                var p = await pick.TryGetMediaPropertiesAsync();
                ti = p.Title ?? ""; ar = p.Artist ?? "";
                if (ar == "") ar = AppName(pick.SourceAppUserModelId);
                paused = Status(pick) != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var tl = pick.GetTimelineProperties();
                dur = tl.EndTime - tl.StartTime; pos = tl.Position - tl.StartTime;
                if (!paused)
                {
                    double extra = (DateTimeOffset.Now - tl.LastUpdatedTime).TotalSeconds;
                    if (extra > 0 && extra < 6 * 3600) pos += TimeSpan.FromSeconds(extra);
                }
                if (ti != Title || (Cover == null && coverTry < 3)) { coverTry++; coverRead = true; img = await LoadCover(p.Thumbnail); }
            }
        }
        catch { }
        metaBusy = false;
        if (!Playing) return;
        if (!got || ti == "") { ti = "Audio sedang diputar"; ar = ""; }

        bool changed = ti != Title || ar != Artist || (got && paused != Paused);
        if (ti != Title) { coverTry = 0; if (!coverRead) img = null; Cover = img; changed = true; }
        else if (coverRead && img != null) { Cover = img; changed = true; }
        Title = ti; Artist = ar;
        if (got) { Paused = paused; Duration = dur > TimeSpan.Zero ? dur : TimeSpan.Zero; basePos = pos; baseAt = Environment.TickCount64; }
        if (changed) Changed?.Invoke();
    }

    // ---- kendali media (dipakai balon "sedang diputar") ----
    public async void PlayPause()
    {
        var s = cur ?? mgr?.GetCurrentSession(); if (s == null) return;
        bool toPause = !Paused;
        var p = Position; Paused = toPause; basePos = p; baseAt = Environment.TickCount64;      // ubah tampilan lebih dulu agar terasa responsif
        if (toPause) hold = Environment.TickCount64 + 60000;                                      // jaga balon tetap ada ~1 menit saat dijeda
        Changed?.Invoke();
        try { await s.TryTogglePlayPauseAsync(); } catch { }
        metaAt = Environment.TickCount64 + 400; quick = 2;
    }

    public async void Skip(bool next)
    {
        var s = cur ?? mgr?.GetCurrentSession(); if (s == null) return;
        try { if (next) await s.TrySkipNextAsync(); else await s.TrySkipPreviousAsync(); } catch { }
        basePos = TimeSpan.Zero; baseAt = Environment.TickCount64;
        metaAt = Environment.TickCount64 + 450; quick = 3;
    }
}

// Garis timeline bergelombang yang bergerak mengikuti level audio (visualizer). Bagian yang sudah diputar = gradien cyan-violet,
// sisanya redup; kenop kecil di posisi lagu.
sealed class WaveBar : FrameworkElement
{
    public double Progress, Level, Phase; public bool Active = true;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w < 24) return;
        double mid = h / 2, x0 = 6, x1 = w - 6, span = x1 - x0;
        double amp = Active ? 1.2 + Math.Min(1, Level * 2.4) * 4.6 : .5;
        double Y(double x)
        {
            double u = Math.Clamp((x - x0) / span, 0, 1), env = .35 + .65 * Math.Sin(Math.PI * u);
            return mid + amp * env * (Math.Sin(x * .17 + Phase) * .7 + Math.Sin(x * .33 - Phase * 1.4) * .3);
        }
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0, Y(x0)), false, false);
            for (double x = x0 + 2; x <= x1; x += 2) c.LineTo(new Point(x, Y(x)), true, true);
        }
        g.Freeze();
        double px = x0 + span * Math.Clamp(Progress, 0, 1);

        var grad = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x0, 0), EndPoint = new Point(x1, 0) };
        grad.GradientStops.Add(new GradientStop(Pal.Cyan, 0)); grad.GradientStops.Add(new GradientStop(Pal.Violet, 1));
        var dim = new Pen(Pal.Br(Color.FromArgb(0x48, 255, 255, 255)), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var lit = new Pen(grad, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

        dc.PushClip(new RectangleGeometry(new Rect(px, 0, Math.Max(0, w - px), h)));
        dc.DrawGeometry(null, dim, g); dc.Pop();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, px, h)));
        dc.DrawGeometry(null, lit, g); dc.Pop();

        var k = new Point(px, Y(px));
        var glow = new RadialGradientBrush();
        glow.GradientStops.Add(new GradientStop(Pal.Alpha(Pal.CyanSoft, 0x90), 0)); glow.GradientStops.Add(new GradientStop(Pal.Alpha(Pal.VioletSoft, 0x00), 1));
        dc.DrawEllipse(glow, null, k, 9, 9);
        dc.DrawEllipse(Pal.Br(Colors.White), null, k, 4, 4);
    }
}

// Balon "sedang diputar": kartu kecil di kanan island (ekor menunjuk ke visualizer). Bisa diklik: prev, play/pause, next.
// Kotak cover di kiri (judul, artis, garis timeline di kanannya), latar hitam polos.
sealed class NowTip : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    static readonly Color White = Colors.White, Soft = Color.FromArgb(0xB8, 255, 255, 255), Faint = Color.FromArgb(0x30, 255, 255, 255);
    static readonly Brush Black = Pal.Br(Color.FromRgb(0x08, 0x08, 0x0A)), Plate = Pal.Br(Color.FromRgb(0x1C, 0x1C, 0x20));
    const string PrevD = "M6.5,5 V19 M18.5,5.5 L9,12 L18.5,18.5 Z", NextD = "M17.5,5 V19 M5.5,5.5 L15,12 L5.5,18.5 Z";
    const string PlayD = "M9.5,6.5 L18,12 L9.5,17.5 Z", PauseD = "M8.5,6 V18 M15.5,6 V18";

    readonly AudioMonitor audio;
    readonly TextBlock title, artist, tCur, tEnd;
    readonly Border card, coverBox, playBtn;
    readonly UIElement noCover;
    readonly WaveBar wave = new() { Height = 18, Margin = new Thickness(0, 5, 0, 0) };
    readonly Grid root;
    readonly UIElement icPlay, icPause;
    readonly TranslateTransform slide = new(-10, 0);
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(120) };
    bool shown; int gen; ImageSource? coverShown;

    public NowTip(AudioMonitor a)
    {
        audio = a;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false; Opacity = 0;
        // tidak mencuri fokus + tersembunyi dari Alt+Tab; tidak tembus-klik (tombol bisa ditekan)
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); };

        // --- kotak cover (kiri)
        noCover = Icons.Glyph(Icons.Music, 22, Pal.Br(Soft), 1.5);
        if (noCover is FrameworkElement nc) { nc.HorizontalAlignment = HorizontalAlignment.Center; nc.VerticalAlignment = VerticalAlignment.Center; }
        coverBox = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Top, Background = Plate, Child = noCover, Margin = new Thickness(0, 0, 12, 0) };

        // --- teks + garis timeline (kanan cover)
        title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(White), TextTrimming = TextTrimming.CharacterEllipsis };
        artist = new TextBlock { FontSize = 11, Foreground = Pal.Br(Soft), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        tCur = new TextBlock { FontSize = 9, Foreground = Pal.Br(Soft), Text = "0:00" };
        tEnd = new TextBlock { FontSize = 9, Foreground = Pal.Br(Soft), Text = "--:--", HorizontalAlignment = HorizontalAlignment.Right };
        var times = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        times.Children.Add(tCur); times.Children.Add(tEnd);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(title); right.Children.Add(artist); right.Children.Add(wave); right.Children.Add(times);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1); head.Children.Add(coverBox); head.Children.Add(right);

        // --- tombol: prev · play/pause · next (putih polos)
        icPlay = Icons.Glyph(PlayD, 14, Pal.Br(White), 1.4, true);
        icPause = Icons.Glyph(PauseD, 14, Pal.Br(White), 2.4);
        playBtn = Ctl(icPause, 26, () => audio.PlayPause());
        var prev = Ctl(Icons.Glyph(PrevD, 13, Pal.Br(White), 1.4, true), 26, () => audio.Skip(false));
        var next = Ctl(Icons.Glyph(NextD, 13, Pal.Br(White), 1.4, true), 26, () => audio.Skip(true));
        prev.Margin = new Thickness(0, 0, 10, 0); next.Margin = new Thickness(10, 0, 0, 0);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        ctl.Children.Add(prev); ctl.Children.Add(playBtn); ctl.Children.Add(next);

        var col = new StackPanel { Margin = new Thickness(12, 12, 14, 8) };
        col.Children.Add(head); col.Children.Add(ctl);

        card = new Border
        {
            Width = 300, Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1),
            Background = Black, BorderBrush = Pal.Br(Faint), Child = col
        };
        var arrow = new Border   // belah ketupat kecil: dua sisi kirinya membentuk ekor "<"
        {
            Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2.5, 0, 0, 0),
            Background = Black, BorderBrush = Pal.Br(Faint), BorderThickness = new Thickness(1, 0, 0, 1),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45)
        };
        root = new Grid { Margin = new Thickness(14), RenderTransform = slide, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = .5 } };
        root.Children.Add(card); root.Children.Add(arrow);
        Content = root;
        tick.Tick += (_, _) => Tick();
        audio.Frame += (lv, _) => { if (!shown) return; wave.Level = lv; wave.Phase += .2; wave.Active = !audio.Paused; wave.InvalidateVisual(); };   // gelombang bergerak ~25 fps
    }

    // Tombol bulat putih polos; sorot putih tipis saat disentuh.
    static Border Ctl(UIElement glyph, double size, Action click)
    {
        var bg = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)); var st = new ScaleTransform(1, 1);
        if (glyph is FrameworkElement g) { g.HorizontalAlignment = HorizontalAlignment.Center; g.VerticalAlignment = VerticalAlignment.Center; }
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Cursor = Cursors.Hand, Child = glyph,
            Background = bg, RenderTransformOrigin = new Point(.5, .5), RenderTransform = st
        };
        bool down = false;
        b.MouseEnter += (_, _) => { Pop(st, 1.1, 520); ToC(bg, Color.FromArgb(0x26, 255, 255, 255), 140); };
        b.MouseLeave += (_, _) => { down = false; Scale(st, 1, 180); ToC(bg, Color.FromArgb(0, 255, 255, 255), 200); };
        b.MouseLeftButtonDown += (_, _) => { down = true; Scale(st, .88, 80); };
        b.MouseLeftButtonUp += (_, _) => { Scale(st, 1.06, 200); if (!down) return; down = false; click(); };
        return b;
    }

    static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // Posisi lagu (dipanggil ~8x/detik selama balon tampil)
    void Tick()
    {
        var d = audio.Duration; var p = audio.Position;
        tCur.Text = Fmt(p);
        if (d <= TimeSpan.Zero) { tEnd.Text = "--:--"; wave.Progress = 0; }
        else { tEnd.Text = Fmt(d); wave.Progress = Math.Clamp(p.TotalSeconds / d.TotalSeconds, 0, 1); }
        wave.InvalidateVisual();
    }

    // Salin judul, artis, cover, status jeda dari AudioMonitor ke tampilan
    public void Sync()
    {
        title.Text = audio.Title; artist.Text = audio.Artist; artist.Visibility = audio.Artist == "" ? Visibility.Collapsed : Visibility.Visible;
        var want = audio.Paused ? icPlay : icPause;
        if (!ReferenceEquals(playBtn.Child, want)) playBtn.Child = want;
        if (!ReferenceEquals(coverShown, audio.Cover))
        {
            coverShown = audio.Cover;
            if (coverShown != null) { coverBox.Background = new ImageBrush(coverShown) { Stretch = Stretch.UniformToFill }; coverBox.Child = null; }
            else { coverBox.Background = Plate; coverBox.Child = noCover; }
        }
        Tick();
    }

    // x = ujung ekor balon, cy = tengah vertikal
    public void ShowAt(double x, double cy)
    {
        shown = true; gen++; Sync();
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ds = root.DesiredSize; Width = ds.Width; Height = ds.Height;
        Left = x - 14; Top = cy - ds.Height / 2;
        if (!IsVisible) { Opacity = 0; slide.X = -10; Show(); }
        tick.Start();
        To(this, OpacityProperty, 1, 200); To(slide, TranslateTransform.XProperty, 0, 560, EzBounce);
    }

    public void Dismiss()
    {
        if (!shown) return;
        shown = false; var g = ++gen;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = EzIn };
        a.Completed += (_, _) => { if (g == gen && !shown) { Hide(); tick.Stop(); } };
        BeginAnimation(OpacityProperty, a);
        To(slide, TranslateTransform.XProperty, -8, 160, EzIn);
    }
}
