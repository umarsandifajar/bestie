using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static MySecretary.Fx;
namespace MySecretary;

// Ikon status kecil di notch (di atas volume): To Do (ungu), lonceng alarm (kuning), lonceng pengingat (cyan).
// Count > 1 ditampilkan sebagai angka kecil di pojok ikon. Glyph = path data 24x24 dari Icons.
sealed record NotchBadge(string Key, string Glyph, Color Tint, int Count);

// Pemberitahuan ringan "ada yang berubah" (tugas, alarm, pengingat) supaya ikon di notch ikut diperbarui.
static class NotchHub
{
    public static event Action? Changed;
    public static void Ping() => Changed?.Invoke();
}

// Bentuk yang menempel di tepi kiri layar: sisi kiri rata dengan tepi, bertemu tepi lewat lengkungan cekung
// (seolah nyangkut di frame), sisi kanan membulat. Dipakai jam (IslandView) dan panel (EdgeShape).
static class Edge
{
    public const double F = 10, R = 14;

    // Aksen tipis cyan -> violet di sepanjang tepi bentuk (garis halus + pendar sangat tipis). a = 0..1 (intensitas).
    public static void Accent(DrawingContext dc, Geometry g, double a, double angle)
    {
        if (a < .01) return;
        LinearGradientBrush B(int al) => new(Pal.Alpha(Pal.Cyan, (byte)(al * Math.Min(a, 1))), Pal.Alpha(Pal.Violet, (byte)(al * Math.Min(a, 1))), angle);
        dc.DrawGeometry(null, new Pen(B(0x1E), 4) { LineJoin = PenLineJoin.Round }, g);
        dc.DrawGeometry(null, new Pen(B(0xD2), 1.2) { LineJoin = PenLineJoin.Round }, g);
    }
    public static StreamGeometry Geo(double w, double h)   // w = lebar tubuh, h = tinggi tubuh (belum termasuk lengkungan atas/bawah)
    {
        double f = F, r = R;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.QuadraticBezierTo(new Point(0, f), new Point(f, f), true, true);                                   // lengkung cekung atas
            c.LineTo(new Point(w - r, f), true, true);
            c.ArcTo(new Point(w, f + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(w, f + h - r), true, true);
            c.ArcTo(new Point(w - r, f + h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(f, f + h), true, true);
            c.QuadraticBezierTo(new Point(0, f + h), new Point(0, 2 * f + h), true, true);                      // lengkung cekung bawah
            c.LineTo(new Point(0, 0), false, false);                                                             // sisi tepi layar: tanpa garis
        }
        g.Freeze(); return g;
    }
}

// Latar panel: jendela setinggi tubuh + 2*F, jadi lengkungan atas/bawah ada di ujung jendela.
sealed class EdgeShape : FrameworkElement
{
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(double), typeof(EdgeShape),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Accent { get => (double)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(ActualWidth - 1, 40), h = Math.Max(ActualHeight - 2 * Edge.F, 20);
        var geo = Edge.Geo(w, h);
        dc.DrawGeometry(Pal.Br(Pal.Bg), new Pen(Pal.Br(Pal.Stroke), 1), geo);
        Edge.Accent(dc, geo, Accent, 45);
    }
}

// Jam di tepi kiri: jam (cyan, tebal) di baris 1, menit (putih, tipis) di baris 2.
// Saat kursor menyentuh, pulau "ditarik" keluar dengan membal halus lalu memanjang ke bawah: muncul slider volume (gradien cyan-violet,
// ikon speaker di dasarnya = mute/unmute audio) dan tombol mute mikrofon. Tombol pengaturan (gear) menyembul dari dasar notch seperti tetesan.
// Saat ada audio dari komputer, visualizer putih kecil muncul di atas angka jam. Saat Pomodoro aktif, isinya jadi menit : detik sisa.
sealed class IslandView : FrameworkElement
{
    public const double RestW = 28, HoverW = 50, F = Edge.F, BodyH = 78, Grow = 8, R = Edge.R;
    // tata letak versi terbuka (px)
    public const double PadTop = 11, PadBot = 11, Gap = 9, SliderH = 84, SliderW = 22, MicD = 28, GearRad = 17, GearGap = 8, MaxBody = 280, WinPad = 4, BadgeH = 17, BadgeGap = 3;   // MaxBody: muat notch terbuka + 3 ikon status
    public const double WinH = WinPad + F + MaxBody + F + GearGap + 2 * GearRad + 14;   // tinggi jendela: cukup untuk notch terbuka + gear
    public const double CyBody = WinPad + F + MaxBody / 2;                                // titik tengah notch di dalam jendela (tetap)
    static readonly FontFamily Fam = new("Segoe UI Variable Display, Segoe UI");
    static readonly Color Red = Color.FromRgb(0xF8, 0x71, 0x71);

    static Geometry Geo(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
    static readonly Geometry GSpk1 = Geo(Icons.SpkBody + " " + Icons.SpkW1),
        GSpk2 = Geo(Icons.SpkBody + " " + Icons.SpkW1 + " " + Icons.SpkW2), GSpkM = Geo(Icons.SpkBody + " " + Icons.SpkX),
        GMic = Geo(Icons.MicOn), GMicOff = Geo(Icons.MicOff), GGear = Geo(Icons.Gear);

    public static readonly DependencyProperty ExtentProperty = DependencyProperty.Register(nameof(Extent), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(RestW, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Extent { get => (double)GetValue(ExtentProperty); set => SetValue(ExtentProperty, value); }

    // 0..1: intensitas aksen cyan-violet di tepi saat kursor menyentuh
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Accent { get => (double)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    // 0..1: seberapa tampak visualizer (animasi muncul/hilang, isi pulau bergeser halus)
    public static readonly DependencyProperty VizShowProperty = DependencyProperty.Register(nameof(VizShow), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double VizShow { get => (double)GetValue(VizShowProperty); set => SetValue(VizShowProperty, value); }

    // 0..1: notch memanjang ke bawah (slider volume + mic muncul)
    public static readonly DependencyProperty OpenProperty = DependencyProperty.Register(nameof(Open), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Open { get => (double)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }

    // 0..1(+): tombol gear menyembul dari dasar notch (boleh sedikit >1 saat membal)
    public static readonly DependencyProperty GearProperty = DependencyProperty.Register(nameof(Gear), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Gear { get => (double)GetValue(GearProperty); set => SetValue(GearProperty, value); }

    // 0..1: gear sedang disentuh kursor (membesar + ikon berputar + gradien)
    public static readonly DependencyProperty GearHotProperty = DependencyProperty.Register(nameof(GearHot), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double GearHot { get => (double)GetValue(GearHotProperty); set => SetValue(GearHotProperty, value); }

    // tinggi slot ikon status (px): 0 bila tidak ada, dianimasikan saat ikon muncul / hilang
    public static readonly DependencyProperty BadgeSlotProperty = DependencyProperty.Register(nameof(BadgeSlot), typeof(double), typeof(IslandView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double BadgeSlot { get => (double)GetValue(BadgeSlotProperty); set => SetValue(BadgeSlotProperty, value); }

    public List<NotchBadge> Badges = new();
    readonly List<Rect> badgeRects = new();
    int badgeHot = -1;
    static readonly Dictionary<string, Geometry> gcache = new();
    static Geometry BG(string d) { if (!gcache.TryGetValue(d, out var g)) { g = Geo(d); gcache[d] = g; } return g; }
    public event Action<string>? BadgeClick;

    public string Hour = "--", Min = "--", Day = "";
    public Rect VizRect;                       // area visualizer (koordinat view), untuk deteksi hover balon info
    public Rect ClockR = Rect.Empty, SliderRect = Rect.Empty, MicRect = Rect.Empty, GearRect = Rect.Empty;   // area interaktif (koordinat view)
    public double Vol; public bool SpkMuted, MicMuted, HasMic = true;
    readonly double[] bars = new double[4];
    double cH1, cH2, cH3;

    // aksi dari kursor
    public event Action? ClockClick, SpkClick, MicClick, GearClick;
    public event Action<double>? VolumeSet;     // 0..1 (geser / klik di slider)
    public event Action<double>? VolumeStep;    // +/- (roda mouse)

    int press; bool moved, micHot, gearHot; Point downPos;   // press: 1 slider, 2 mic, 3 gear, 4 badan notch

    public bool Settling { get { foreach (var b in bars) if (b > .02) return true; return false; } }
    public bool Dragging => press == 1 && IsMouseCaptured;

    // Dipanggil dari AudioMonitor tiap beberapa puluh ms: gerakkan batang visualizer menuju level terbaru.
    public void Step(float level, bool playing)
    {
        double tm = Environment.TickCount64 / 1000.0;
        double lv = Math.Min(1, Math.Pow(Math.Max(level, 0), .55) * 1.7);
        for (int i = 0; i < bars.Length; i++)
        {
            double wob = .35 + .65 * Math.Abs(Math.Sin(tm * (5.5 + i * 1.9) + i * 1.7));
            double target = playing ? lv * wob : 0;
            bars[i] += (target - bars[i]) * .42;
        }
        InvalidateVisual();
    }

    FormattedText Ft(string s, double size, FontWeight wt, Color c)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Fam, FontStyles.Normal, wt, FontStretches.Normal), size, Pal.Br(c), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // tinggi blok jam pada ukuran penuh (diukur sekali)
    void Metrics()
    {
        if (cH1 > 0) return;
        cH1 = Ft("00", 17, FontWeights.Bold, Pal.Cyan).Height; cH2 = Ft("00", 17, FontWeights.Thin, Pal.Ink).Height; cH3 = Ft("Sen", 9.5, FontWeights.Medium, Pal.Ink).Height;
    }
    double ClockFull => cH1 - 1 + cH2 + cH3 + 1;

    // tinggi badan notch saat terbuka penuh (belum termasuk lengkung atas/bawah)
    public double ExpandedH() { Metrics(); return PadTop + 12 * VizShow + ClockFull + BadgeSlot + Gap + SliderH + Gap + MicD + PadBot; }

    static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    // Ikon garis tipis (grid 24x24) digambar langsung: pusat (cx,cy), ukuran px, rot derajat.
    static void DrawGlyph(DrawingContext dc, Geometry g, double cx, double cy, double size, Brush b, double thick, double rot = 0)
    {
        double sc = size / 24;
        dc.PushTransform(new TranslateTransform(cx, cy));
        if (rot != 0) dc.PushTransform(new RotateTransform(rot));
        dc.PushTransform(new ScaleTransform(sc, sc));
        dc.PushTransform(new TranslateTransform(-12, -12));
        dc.DrawGeometry(null, new Pen(b, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g);
        dc.Pop(); dc.Pop(); if (rot != 0) dc.Pop(); dc.Pop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        Metrics();
        double w = Math.Max(Extent, 26), f = F;
        double t = Math.Clamp((w - RestW) / (HoverW - RestW), 0, 1.25), tc = Math.Min(t, 1);
        double eo = Math.Clamp(Open, 0, 1), slot = 12 * VizShow, bs = Math.Max(0, BadgeSlot);
        double expH = PadTop + slot + ClockFull + bs + Gap + SliderH + Gap + MicD + PadBot;
        double restH = BodyH + bs;                                          // saat diam, badan sedikit lebih tinggi bila ada ikon status
        double h = restH + Grow * t + eo * (expH - restH - Grow);        // tumbuh sedikit saat ditarik, lalu memanjang saat terbuka
        double oy = CyBody - f - h / 2;                                    // titik tengah notch selalu tetap
        dc.PushTransform(new TranslateTransform(0, oy));
        var body = Edge.Geo(w, h);
        dc.DrawGeometry(Pal.Br(Pal.Island), new Pen(Pal.Br(Pal.Stroke), 1), body);
        Edge.Accent(dc, body, Accent, 90);
        DrawGear(dc, w, h, f, oy);

        double sz = 13 + 4 * tc;                                         // teks ikut membesar saat ditarik
        var h1 = Ft(Hour, sz, FontWeights.Bold, Pal.Cyan);               // angka jam: cyan tebal, tanpa glow
        var h2 = Ft(Min, sz, FontWeights.Thin, Pal.Ink);
        var h3 = Ft(Day, 9.5, FontWeights.Medium, Pal.Alpha(Pal.Dim, (byte)(255 * tc)));
        double gap = -1;
        double total = slot + h1.Height + gap + h2.Height + (h3.Height + 1) * tc;
        double yRest = f + (h - total - bs) / 2, yTop = f + PadTop;
        double y = yRest + (yTop - yRest) * eo, cx = w / 2;              // saat terbuka, jam naik ke atas notch
        double y0 = y;
        ClockR = new Rect(0, oy + y, w, total);

        // visualizer kecil (putih) di atas angka jam
        const double bw = 2.2, gp = 2.2, maxH = 9;
        double tw = bars.Length * bw + (bars.Length - 1) * gp, x0 = cx - tw / 2, baseY = y + 10;
        VizRect = new Rect(cx - tw / 2 - 2, oy + y - 1, tw + 4, 13);
        if (VizShow > .01)
        {
            var br = Pal.Br(Pal.Alpha(Pal.Ink, (byte)(235 * Math.Min(VizShow, 1))));
            for (int i = 0; i < bars.Length; i++)
            {
                double bh = 2 + (maxH - 2) * Math.Clamp(bars[i], 0, 1);
                dc.DrawRoundedRectangle(br, null, new Rect(x0 + i * (bw + gp), baseY - bh, bw, bh), 1.1, 1.1);
            }
        }
        y += slot;

        dc.DrawText(h1, new Point(cx - h1.Width / 2, y)); y += h1.Height + gap;
        dc.DrawText(h2, new Point(cx - h2.Width / 2, y)); y += h2.Height + 1;
        if (tc > .02) dc.DrawText(h3, new Point(cx - h3.Width / 2, y));

        DrawBadges(dc, cx, y0 + total, bs, oy);                          // ikon status tepat di bawah jam = di atas volume saat terbuka
        DrawControls(dc, cx, f + PadTop + slot + ClockFull + bs + Gap, eo, oy);
        dc.Pop();
    }

    // Ikon status (To Do / alarm / pengingat) bertumpuk ke bawah dari `top`. Tiap ikon: lingkaran tipis berwarna + glyph; angka kecil bila lebih dari satu.
    void DrawBadges(DrawingContext dc, double cx, double top, double bs, double oy)
    {
        badgeRects.Clear();
        int n = Badges.Count;
        if (n == 0 || bs < 3) return;
        double a = Math.Clamp(bs / (n * BadgeH + BadgeGap), 0, 1);       // muncul halus saat slot tumbuh
        double y = top + BadgeGap;
        dc.PushOpacity(a);
        for (int i = 0; i < n; i++)
        {
            var bd = Badges[i]; double cy = y + BadgeH / 2; bool hot = i == badgeHot;
            dc.DrawEllipse(Pal.Br(Pal.Alpha(bd.Tint, (byte)(hot ? 0x58 : 0x26))), null, new Point(cx, cy), 8, 8);
            DrawGlyph(dc, BG(bd.Glyph), cx, cy, 12.5, Pal.Br(bd.Tint), 1.5);
            if (bd.Count > 1)
            {
                var ft = Ft(bd.Count > 9 ? "9+" : bd.Count.ToString(CultureInfo.InvariantCulture), 7, FontWeights.Bold, Pal.AccentInk);
                double bx = cx + 6.5, by = cy - 5.8;
                dc.DrawEllipse(Pal.Br(bd.Tint), null, new Point(bx, by), 4.6, 4.6);
                dc.DrawText(ft, new Point(bx - ft.Width / 2, by - ft.Height / 2));
            }
            badgeRects.Add(new Rect(cx - 9, oy + cy - 8.5, 18, 17));
            y += BadgeH;
        }
        dc.Pop();
    }

    // Slider volume (gradien cyan-violet, ikon speaker di dasar) + tombol mic. sTop = atas slider (koordinat badan).
    void DrawControls(DrawingContext dc, double cx, double sTop, double eo, double oy)
    {
        SliderRect = Rect.Empty; MicRect = Rect.Empty;
        double ctl = Math.Clamp((eo - .4) / .6, 0, 1);
        if (ctl < .01) return;

        // --- slider
        double sw = SliderW, sh = SliderH, sx = cx - sw / 2, sy = sTop;
        var track = new Rect(sx, sy, sw, sh);
        SliderRect = new Rect(sx - 5, oy + sy, sw + 10, sh);
        double lv = Math.Clamp(Vol, 0, 1), fh = sh * lv;
        var fillRect = new Rect(sx, sy + sh - fh, sw, fh);
        double pop = .85 + .15 * ctl;
        dc.PushOpacity(ctl);
        dc.PushTransform(new ScaleTransform(pop, pop, cx, sy + sh / 2));
        dc.DrawRoundedRectangle(Pal.Br(Pal.Track), null, track, sw / 2, sw / 2);
        if (fh > .5)
        {
            var grad = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Pal.Cyan, 0), new GradientStop(Pal.Violet, 1) },
                new Point(0, sy + sh), new Point(0, sy)) { MappingMode = BrushMappingMode.Absolute };   // cyan di bawah -> violet di atas
            dc.PushClip(new RectangleGeometry(fillRect));
            if (SpkMuted) dc.PushOpacity(.3);
            dc.DrawRoundedRectangle(grad, null, track, sw / 2, sw / 2);
            if (SpkMuted) dc.Pop();
            dc.Pop();
        }
        double iy = sy + sh - 16;
        var spk = SpkMuted || lv < .01 ? GSpkM : lv < .5 ? GSpk1 : GSpk2;
        if (SpkMuted) DrawGlyph(dc, spk, cx, iy, 15, Pal.Br(Red), 1.7);
        else
        {
            // ikon dua warna: gelap di atas isian gradien, terang di atas bagian kosong
            dc.PushClip(new RectangleGeometry(fillRect)); DrawGlyph(dc, spk, cx, iy, 15, Pal.Br(Pal.AccentInk), 1.7); dc.Pop();
            dc.PushClip(new RectangleGeometry(new Rect(sx, sy, sw, sh - fh))); DrawGlyph(dc, spk, cx, iy, 15, Pal.Br(Pal.Ink), 1.7); dc.Pop();
        }
        dc.Pop(); dc.Pop();

        // --- tombol mic
        double my = sTop + sh + Gap + MicD / 2;
        MicRect = new Rect(cx - MicD / 2, oy + my - MicD / 2, MicD, MicD);
        double ma = Math.Clamp((eo - .55) / .45, 0, 1) * (HasMic ? 1 : .35);
        bool mm = MicMuted;
        dc.PushOpacity(ma);
        var bg = mm ? Pal.Alpha(Red, 0x30) : micHot ? Pal.Hover : Pal.Raised;
        dc.DrawEllipse(Pal.Br(bg), new Pen(Pal.Br(mm ? Pal.Alpha(Red, 0x70) : Pal.Stroke), 1), new Point(cx, my), MicD / 2, MicD / 2);
        DrawGlyph(dc, mm ? GMicOff : GMic, cx, my, 15, Pal.Br(mm ? Red : Pal.Ink), 1.6);
        dc.Pop();
    }

    // Gear: bola kecil yang menyembul dari dasar notch. Awalnya tersambung leher gooey, lalu leher menipis dan putus.
    void DrawGear(DrawingContext dc, double w, double h, double f, double oy)
    {
        double g = Gear;
        if (g < .01) { GearRect = Rect.Empty; return; }
        double gc = Math.Clamp(g, 0, 1), cx = w / 2;
        double r = GearRad * (.45 + .55 * gc) * (1 + .08 * GearHot);
        double yStart = f + h - 8, yEnd = f + h + GearGap + GearRad;
        double cy = yStart + (yEnd - yStart) * g;                        // g > 1 sesaat = membal sedikit melewati titik akhir
        Geometry shape = new EllipseGeometry(new Point(cx, cy), r, r);

        double ns = 1 - Smooth((gc - .55) / .35);                       // kekuatan leher
        double yb = f + h - 2, wn = r * .75 * ns, u = wn * .55;
        double yc = cy - Math.Sqrt(Math.Max(r * r - u * u, 0)) + 1;
        if (wn > .6 && yc > yb + .5)
        {
            var neck = new StreamGeometry();
            using (var c = neck.Open())
            {
                double mid = (yb + yc) / 2;
                c.BeginFigure(new Point(cx - wn, yb), true, true);
                c.QuadraticBezierTo(new Point(cx - wn * .3, mid), new Point(cx - u, yc), true, true);
                c.LineTo(new Point(cx + u, yc), true, true);
                c.QuadraticBezierTo(new Point(cx + wn * .3, mid), new Point(cx + wn, yb), true, true);
            }
            neck.Freeze();
            shape = Geometry.Combine(shape, neck, GeometryCombineMode.Union, null);
        }

        double det = Math.Clamp((gc - .6) / .4, 0, 1);                   // 0 = masih menyatu dengan notch, 1 = terpisah penuh
        var edge = Pal.Br(Pal.Alpha(Pal.Stroke, (byte)(Pal.Stroke.A * det)));
        dc.DrawGeometry(Pal.Br(Pal.Island), new Pen(edge, 1), shape);
        Edge.Accent(dc, shape, Accent * det, 90);
        GearRect = new Rect(cx - r, oy + cy - r, 2 * r, 2 * r);

        double ia = Math.Clamp((gc - .55) / .45, 0, 1), hot = Math.Clamp(GearHot, 0, 1);
        if (hot > .01) dc.DrawEllipse(Pal.Br(Pal.Alpha(Pal.Ink, (byte)(26 * hot))), null, new Point(cx, cy), r * .92, r * .92);
        if (ia > .02)
        {
            double rot = -120 * (1 - gc) + 50 * GearHot;                 // berputar masuk saat muncul, berputar lagi saat disentuh
            double isz = 17 * (r / GearRad);
            dc.PushOpacity(ia);
            DrawGlyph(dc, GGear, cx, cy, isz, Pal.Br(Pal.Ink), 1.5, rot);
            if (hot > .01)
            {
                var grad = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Pal.Cyan, 0), new GradientStop(Pal.Violet, 1) },
                    new Point(0, 0), new Point(24, 24)) { MappingMode = BrushMappingMode.Absolute };
                dc.PushOpacity(hot);
                DrawGlyph(dc, GGear, cx, cy, isz, grad, 1.5, rot);
                dc.Pop();
            }
            dc.Pop();
        }
    }

    // ---------- interaksi ----------
    static bool InCircle(Point p, Rect r) { double dx = p.X - (r.X + r.Width / 2), dy = p.Y - (r.Y + r.Height / 2), rr = r.Width / 2 + 3; return dx * dx + dy * dy <= rr * rr; }

    int Hit(Point p)
    {
        for (int i = 0; i < badgeRects.Count; i++) if (badgeRects[i].Contains(p)) return 10 + i;   // 10+ = ikon status ke-i
        if (Gear > .6 && !GearRect.IsEmpty && InCircle(p, GearRect)) return 3;
        if (Open > .6 && !SliderRect.IsEmpty && SliderRect.Contains(p)) return 1;
        if (Open > .6 && !MicRect.IsEmpty && HasMic && InCircle(p, MicRect)) return 2;
        return 4;
    }
    double VolAt(double y) => Math.Clamp((SliderRect.Bottom - y) / SliderRect.Height, 0, 1);

    void SetGearHot(bool on)
    {
        if (on == gearHot) return; gearHot = on;
        To(this, GearHotProperty, on ? 1 : 0, on ? 420 : 260, on ? EzBounce : EzOut);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        press = Hit(p); downPos = p; moved = false;
        if (press == 1) CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (press == 1 && IsMouseCaptured)
        {
            if (!moved && Math.Abs(p.Y - downPos.Y) > 3) moved = true;
            if (moved) VolumeSet?.Invoke(VolAt(p.Y));
        }
        else
        {
            int k = Hit(p);
            SetGearHot(k == 3);
            if ((k == 2) != micHot) { micHot = k == 2; InvalidateVisual(); }
            int bh = k >= 10 ? k - 10 : -1;
            if (bh != badgeHot) { badgeHot = bh; InvalidateVisual(); }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this); int k = press; press = 0;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (k == 1)
        {
            if (!moved)
            {
                if (p.Y >= SliderRect.Bottom - 30) SpkClick?.Invoke();    // klik ikon speaker = mute / unmute
                else VolumeSet?.Invoke(VolAt(p.Y));                        // klik di slider = lompat ke level itu
            }
        }
        else if (k == 2 && Hit(p) == 2) MicClick?.Invoke();
        else if (k == 3 && Hit(p) == 3) GearClick?.Invoke();
        else if (k == 4 && Hit(p) == 4) ClockClick?.Invoke();
        else if (k >= 10 && Hit(p) == k) { int bi = k - 10; if (bi < Badges.Count) BadgeClick?.Invoke(Badges[bi].Key); }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!IsMouseCaptured) { SetGearHot(false); if (micHot) { micHot = false; InvalidateVisual(); } if (badgeHot != -1) { badgeHot = -1; InvalidateVisual(); } }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Open > .6) { VolumeStep?.Invoke(e.Delta > 0 ? .04 : -.04); e.Handled = true; }
        base.OnMouseWheel(e);
    }
}

sealed class IslandWindow : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    static readonly IEasingFunction EzGear = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 1.2 };
    static readonly IEasingFunction EzQuint = new QuinticEase { EasingMode = EasingMode.EaseOut };

    readonly IslandView view = new() { Opacity = 0 };
    readonly TranslateTransform slide = new(-60, 0);
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer volT = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly CultureInfo id = new("id-ID");
    readonly AudioMonitor audio = new();
    readonly VolumeCtl vol = new();
    readonly NowTip tip;
    readonly DispatcherTimer tipT = new() { Interval = TimeSpan.FromMilliseconds(120) };   // menjaga balon tetap ada selama kursor di visualizer / di balon
    bool present, hover, timer, vizOn, tipOn, vizHot; int gen, leaveTicks, tipGone;

    public bool Enabled { get; set; } = true;
    public event Action? Clicked;            // klik jam -> buka panel
    public event Action? SettingsClicked;    // klik gear -> buka pengaturan
    public event Action<string>? BadgeClicked;   // klik ikon status di notch: "todo" | "alarm" | "remind"

    public IslandWindow()
    {
        tip = new NowTip(audio);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Focusable = false;
        Width = IslandView.HoverW + 22; Height = IslandView.WinH;
        view.RenderTransform = slide; view.Cursor = Cursors.Hand; Content = view;
        view.ClockClick += () => Clicked?.Invoke();
        view.GearClick += () => SettingsClicked?.Invoke();
        view.BadgeClick += k => BadgeClicked?.Invoke(k);
        view.VolumeSet += v => { vol.SetVolume((float)v); Sync(); };
        view.VolumeStep += d => { vol.Poll(); vol.SetVolume((float)(vol.Volume + d)); Sync(); };
        view.SpkClick += () => { vol.ToggleSpk(); Sync(); };
        view.MicClick += () => { vol.ToggleMic(); Sync(); };
        view.MouseMove += (_, e) =>
        {
            var r = view.VizRect; r.Inflate(6, 5);
            vizHot = view.VizShow > .5 && r.Contains(e.GetPosition(view));
            if (vizHot) { tipGone = 0; SetTip(true); }
        };
        view.MouseLeave += (_, _) => vizHot = false;
        tipT.Tick += (_, _) =>
        {
            if (!tipOn) { tipT.Stop(); return; }
            if (vizHot || tip.IsMouseOver) { tipGone = 0; return; }    // kursor masih di visualizer atau di balon
            if (++tipGone >= 3) SetTip(false);                          // jeda ~0,35 dtk supaya kursor sempat pindah ke balon
        };
        // tidak mencuri fokus + tersembunyi dari Alt+Tab
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); };
        clock.Tick += (_, _) => Tick(); Tick();
        volT.Tick += (_, _) => { if (!view.Dragging && vol.Poll()) Sync(); };
        Pal.Changed += () => view.InvalidateVisual();
        audio.Changed += OnAudio;
        audio.Frame += (lv, playing) => { if (present && (playing || view.Settling)) view.Step(lv, playing); };
        audio.Start();
    }

    // Salin status volume/mute ke tampilan
    void Sync()
    {
        view.Vol = vol.Volume; view.SpkMuted = vol.SpkMuted; view.MicMuted = vol.MicMuted; view.HasMic = vol.HasMic;
        view.InvalidateVisual();
    }

    void OnAudio()
    {
        bool on = audio.Playing;
        if (on != vizOn)
        {
            vizOn = on;
            To(view, IslandView.VizShowProperty, on ? 1 : 0, on ? 420 : 380, EzOut);
            if (!on) SetTip(false);
        }
        if (tipOn) tip.Sync();
    }

    // Balon lagu (cover + prev/play/pause/next): muncul di kanan visualizer saat kursor diarahkan ke visualizer, tetap ada selama kursor di balon.
    void SetTip(bool on)
    {
        on &= present && audio.Playing;
        if (on == tipOn) return;
        tipOn = on;
        if (on) { tipGone = 0; tip.ShowAt(Left + IslandView.HoverW + 10, Top + view.VizRect.Y + view.VizRect.Height / 2); tipT.Start(); }
        else { tip.Dismiss(); tipT.Stop(); }
    }

    // Ganti ikon status di notch (To Do / alarm / pengingat). Notch memanjang halus bila ada ikon, kembali pendek bila kosong.
    public void SetBadges(List<NotchBadge> list)
    {
        view.Badges = list;
        double slot = list.Count == 0 ? 0 : list.Count * IslandView.BadgeH + IslandView.BadgeGap;
        To(view, IslandView.BadgeSlotProperty, slot, 300, EzOut);
        view.InvalidateVisual();
    }

    // Pomodoro aktif: tampilkan menit/detik sisa menggantikan jam. null = kembali ke jam.
    public void SetTimer(string? a, string? b)
    {
        if (a == null) { if (!timer) return; timer = false; view.Hour = "--"; Tick(); return; }
        if (timer && view.Hour == a && view.Min == b) return;
        timer = true; view.Hour = a; view.Min = b ?? ""; view.Day = "Fokus"; view.InvalidateVisual();
    }

    void Tick()
    {
        if (timer) return;
        var n = DateTime.Now;
        var h = n.ToString("HH", CultureInfo.InvariantCulture); var m = n.ToString("mm", CultureInfo.InvariantCulture);
        if (h == view.Hour && m == view.Min) return;
        view.Hour = h; view.Min = m; view.Day = id.DateTimeFormat.AbbreviatedDayNames[(int)n.DayOfWeek];
        view.InvalidateVisual();
    }

    // Titik tengah notch = tengah layar (jendela lebih tinggi karena notch bisa memanjang + gear)
    void Place() { var wa = SystemParameters.WorkArea; Left = wa.Left; Top = wa.Top + wa.Height / 2 - IslandView.CyBody; }

    // Kembalikan semua animasi tampilan ke keadaan diam (notch kecil, tanpa gear)
    void ResetVisual()
    {
        foreach (var dp in new[] { IslandView.ExtentProperty, IslandView.AccentProperty, IslandView.OpenProperty, IslandView.GearProperty, IslandView.GearHotProperty })
            view.BeginAnimation(dp, null);
        view.Extent = IslandView.RestW; view.Accent = 0; view.Open = 0; view.Gear = 0; view.GearHot = 0;
        hover = false; leaveTicks = 0; volT.Stop();
    }

    // Tampilkan / sembunyikan pulau (meluncur dari/ke tepi kiri)
    public void Set(bool visible)
    {
        visible &= Enabled;
        if (visible == present) return;
        present = visible; var g = ++gen;
        if (visible)
        {
            ResetVisual(); Place(); Tick(); clock.Start(); Show();
            To(slide, TranslateTransform.XProperty, 0, 380, EzOut); To(view, OpacityProperty, 1, 240);
        }
        else
        {
            hover = false; volT.Stop();
            SetTip(false);
            clock.Stop();
            var a = new DoubleAnimation(-60, TimeSpan.FromMilliseconds(220)) { EasingFunction = EzIn };
            a.Completed += (_, _) => { if (g == gen && !present) Hide(); };
            slide.BeginAnimation(TranslateTransform.XProperty, a);
            To(view, OpacityProperty, 0, 200, EzIn);
        }
    }

    // Kursor menyentuh pulau? Saat belum terbuka: hanya badan kecil. Saat terbuka: seluruh notch yang memanjang + area gear di bawahnya
    // (zona sedikit lebih lebar supaya tidak berkedip).
    public bool Over(double x, double y)
    {
        if (!present) return false;
        double cy = Top + IslandView.CyBody;
        if (!hover)
        {
            double half = (IslandView.BodyH + view.BadgeSlot) / 2 + 6;
            return x >= Left - 2 && x <= Left + IslandView.RestW + 2 && y >= cy - half && y <= cy + half;
        }
        double eh = view.ExpandedH();
        double top = cy - eh / 2 - 6, bot = cy + eh / 2 + IslandView.GearGap + 2 * IslandView.GearRad + 6;
        return x >= Left - 2 && x <= Left + IslandView.HoverW + 12 && y >= top && y <= bot;
    }

    // Kursor di area jam? (dipakai mode "Hover jam": menahan kursor di jam membuka panel, bukan di slider/mic)
    public bool OverClock(double x, double y)
    {
        if (!present) return false;
        if (!hover) return Over(x, y);
        var r = view.ClockR;
        return !r.IsEmpty && x >= Left - 2 && x <= Left + IslandView.HoverW + 12 && y >= Top + r.Top - 4 && y <= Top + r.Bottom + 4;
    }

    public void SetHover(bool h)
    {
        if (!present) return;
        if (view.Dragging) h = true;                        // sedang menyeret slider: jangan menutup
        if (h) leaveTicks = 0;
        else if (hover && ++leaveTicks < 6) return;         // jeda ~0,25 dtk sebelum menutup, supaya tidak berkedip
        if (h == hover) return;
        hover = h;
        To(view, IslandView.AccentProperty, h ? 1 : 0, h ? 320 : 450);
        if (h)
        {
            vol.Acquire(); Sync(); volT.Start();
            view.BeginAnimation(IslandView.ExtentProperty, new DoubleAnimation(IslandView.HoverW, TimeSpan.FromMilliseconds(760))
                { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 7 } });   // "ditarik" dengan membal yang halus
            To(view, IslandView.OpenProperty, 1, 640, EzQuint, 60);                                  // memanjang ke bawah: slider + mic
            To(view, IslandView.GearProperty, 1, 760, EzGear, 220);                                  // gear menyembul dari dasar notch
        }
        else
        {
            volT.Stop();
            view.BeginAnimation(IslandView.ExtentProperty, new DoubleAnimation(IslandView.RestW, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .35 } });
            To(view, IslandView.GearProperty, 0, 300, EzIn);                                         // gear ditarik masuk dulu
            To(view, IslandView.OpenProperty, 0, 420, EzOut, 90);
            To(view, IslandView.GearHotProperty, 0, 200, EzOut);
        }
    }
}
