using System.Globalization;
using System.Net.Http;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static MySecretary.Fx;
namespace MySecretary;

// ---------- Hitung waktu sholat (offline, metode Kemenag RI: Subuh 20°, Isya 18°, Ashar mazhab Syafi'i, ihtiyat +2 menit) ----------
static class PrayerCalc
{
    public static readonly string[] Names = { "Subuh", "Dzuhur", "Ashar", "Maghrib", "Isya" };

    static double Rad(double d) => d * Math.PI / 180;
    static double Deg(double r) => r * 180 / Math.PI;
    static double Fix(double v, double m) { v %= m; return v < 0 ? v + m : v; }

    // Lima waktu (Subuh, Dzuhur, Ashar, Maghrib, Isya) untuk tanggal `date` di koordinat lat/lon, dalam zona waktu komputer.
    public static DateTime[] Day(DateTime date, double lat, double lon)
    {
        double tz = TimeZoneInfo.Local.GetUtcOffset(date.Date.AddHours(12)).TotalHours;
        int y = date.Year, m = date.Month, d = date.Day;
        if (m <= 2) { y--; m += 12; }
        int a = y / 100, b = 2 - a + a / 4;
        double jd = Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
        double D = jd - 2451545.0;
        double g = Rad(Fix(357.529 + 0.98560028 * D, 360));
        double q = Fix(280.459 + 0.98564736 * D, 360);
        double L = Rad(Fix(q + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g), 360));
        double e = Rad(23.439 - 0.00000036 * D);
        double ra = Fix(Deg(Math.Atan2(Math.Cos(e) * Math.Sin(L), Math.Cos(L))) / 15, 24);
        double dec = Math.Asin(Math.Sin(e) * Math.Sin(L));
        double eqt = Fix(q / 15 - ra + 12, 24) - 12;
        double noon = 12 + tz - lon / 15 - eqt;
        double lr = Rad(lat);

        double Ha(double ang)
        {
            double c = (-Math.Sin(Rad(ang)) - Math.Sin(lr) * Math.Sin(dec)) / (Math.Cos(lr) * Math.Cos(dec));
            return Deg(Math.Acos(Math.Clamp(c, -1, 1))) / 15;
        }
        double aa = Deg(Math.Atan(1 / (1 + Math.Tan(Math.Abs(lr - dec)))));
        double ca = (Math.Sin(Rad(aa)) - Math.Sin(lr) * Math.Sin(dec)) / (Math.Cos(lr) * Math.Cos(dec));
        double asr = noon + Deg(Math.Acos(Math.Clamp(ca, -1, 1))) / 15;

        double[] h = { noon - Ha(20), noon, asr, noon + Ha(0.833), noon + Ha(18) };
        var r = new DateTime[5];
        for (int i = 0; i < 5; i++) r[i] = date.Date.AddMinutes(Math.Ceiling(h[i] * 60 + 2));   // ihtiyat +2 menit, dibulatkan ke atas
        return r;
    }
}

// ---------- Lokasi: mengikuti lokasi saat online (IP), disimpan di config supaya tetap jalan saat offline ----------
static class Geo
{
    static readonly HttpClient http = MakeClient();
    static HttpClient MakeClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(7) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("MySecretary/1.0"); return c; }
    public static event Action? Changed;

    public static async Task Refresh(Config cfg)
    {
        foreach (var url in new[] { "https://ipwho.is/", "https://ipapi.co/json/" })
        {
            try
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
                var r = doc.RootElement;
                if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) continue;
                if (!r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo)) continue;
                if (la.ValueKind != JsonValueKind.Number || lo.ValueKind != JsonValueKind.Number) continue;
                double lat = la.GetDouble(), lon = lo.GetDouble();
                string city = r.TryGetProperty("city", out var c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? "") : "";
                if (city == "") city = cfg.PrayerCity;
                if (Math.Abs(lat - cfg.PrayerLat) > .01 || Math.Abs(lon - cfg.PrayerLon) > .01 || city != cfg.PrayerCity)
                {
                    cfg.PrayerLat = lat; cfg.PrayerLon = lon; cfg.PrayerCity = city; cfg.Save();
                    Changed?.Invoke();
                }
                return;
            }
            catch { }
        }
    }
}

// ---------- Bunyi bel lembut "tung" (disintesis, tanpa file audio) ----------
static class Chime
{
    static byte[]? wav;
    static SoundPlayer? sp;

    public static void Play()
    {
        try
        {
            wav ??= Build();
            sp?.Stop();
            sp = new SoundPlayer(new MemoryStream(wav));
            sp.Play();
        }
        catch { SystemSounds.Asterisk.Play(); }
    }

    static byte[] Build(double dur = 2.6, double f0 = 370, double vol = .42, double decay = 1)
    {
        const int sr = 44100;
        int n = (int)(sr * dur);
        // bagian-bagian bel: rasio frekuensi, amplitudo, waktu peluruhan (detik)
        double[][] p = { new[] { 1.0, 1.0, 1.4 }, new[] { 2.0, .30, .8 }, new[] { 2.76, .22, .5 }, new[] { 4.07, .08, .28 }, new[] { 5.4, .04, .18 } };
        var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
        bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + n * 2); bw.Write(new[] { 'W', 'A', 'V', 'E' });
        bw.Write(new[] { 'f', 'm', 't', ' ' }); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sr, s = 0;
            foreach (var q in p) s += q[1] * Math.Sin(2 * Math.PI * f0 * q[0] * t) * Math.Exp(-t / (q[2] * decay));
            double att = Math.Min(1, t / .008), rel = Math.Min(1, (dur - t) / .15);
            bw.Write((short)(s * att * rel * vol * 32767 / 1.65));
        }
        bw.Flush(); return ms.ToArray();
    }
}

// ---------- Pop-up pengingat adzan (gaya sama dengan balon "Sedang diputar") ----------
sealed class AdzanTip : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    static AdzanTip? cur;
    readonly TranslateTransform slide = new(-26, 0);
    readonly RotateTransform shake = new(0);
    readonly DispatcherTimer life = new() { Interval = TimeSpan.FromSeconds(12) };
    readonly DispatcherTimer rattle = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    bool closing;

    public static void Notify(string name, DateTime at, string city)
    {
        cur?.Dismiss();
        var w = new AdzanTip(name, at, city); cur = w; w.Open();
    }

    AdzanTip(string name, DateTime at, string city)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Opacity = 0; Cursor = Cursors.Hand;
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); };

        var yellow = Color.FromRgb(0xFA, 0xCC, 0x15);
        var toa = Icons.Glyph(Icons.Toa, 21, Pal.Br(yellow), 1.7);
        if (toa is FrameworkElement te) { te.HorizontalAlignment = HorizontalAlignment.Center; te.VerticalAlignment = VerticalAlignment.Center; }
        toa.RenderTransformOrigin = new Point(.5, .5); toa.RenderTransform = shake;
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x2A, yellow.R, yellow.G, yellow.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, yellow.R, yellow.G, yellow.B)), BorderThickness = new Thickness(1), Child = toa
        };

        var cap = new TextBlock { Text = "PENGINGAT SHOLAT", FontSize = 8.5, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(Pal.Dim), Margin = new Thickness(0, 0, 0, 2) };
        var title = new TextBlock { Text = $"Waktu Adzan {name} telah tiba", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(Pal.Ink), MaxWidth = 250, TextWrapping = TextWrapping.Wrap };
        var sub = new TextBlock { Text = $"{at:HH:mm} · {city}", FontSize = 11, Foreground = Pal.Br(Pal.Dim), Margin = new Thickness(0, 1, 0, 0) };
        var txt = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(cap); txt.Children.Add(title); txt.Children.Add(sub);
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(tile); row.Children.Add(txt);

        var card = new Border
        {
            Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 9, 16, 9), BorderThickness = new Thickness(1),
            Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Stroke), Child = row
        };
        var arrow = new Border
        {
            Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2.5, 0, 0, 0),
            Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Stroke), BorderThickness = new Thickness(1, 0, 0, 1),
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform(45)
        };
        var root = new Grid { Margin = new Thickness(14), RenderTransform = slide, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = .5 } };
        root.Children.Add(card); root.Children.Add(arrow);
        Content = root;

        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = root.DesiredSize.Width; Height = root.DesiredSize.Height;
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + IslandView.HoverW + 10 - 14; Top = wa.Top + (wa.Height - Height) / 2;

        MouseLeftButtonUp += (_, _) => Dismiss();
        life.Tick += (_, _) => Dismiss();
        rattle.Tick += (_, _) => Rattle();
        Closed += (_, _) => { life.Stop(); rattle.Stop(); if (ReferenceEquals(cur, this)) cur = null; };
    }

    void Open()
    {
        Show();
        To(this, OpacityProperty, 1, 220); To(slide, TranslateTransform.XProperty, 0, 620, EzBounce);
        Rattle(); rattle.Start(); life.Start();
    }

    // Toa kuning bergetar (goyang kiri-kanan, meredam), diulang tiap ~1,3 detik
    void Rattle()
    {
        var k = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(650) };
        double[] a = { -16, 14, -12, 10, -6, 4, 0 };
        for (int i = 0; i < a.Length; i++) k.KeyFrames.Add(new LinearDoubleKeyFrame(a[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80 * (i + 1)))));
        shake.BeginAnimation(RotateTransform.AngleProperty, k);
    }

    public void Dismiss()
    {
        if (closing) return; closing = true; life.Stop(); rattle.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = EzIn };
        a.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, a);
        To(slide, TranslateTransform.XProperty, -14, 190, EzIn);
    }
}

// ---------- Busur waktu sholat: lintasan matahari dari Subuh sampai Isya, titik tiap waktu, matahari bercahaya ----------
sealed class ArcView : FrameworkElement
{
    static readonly FontFamily Fam = new("Segoe UI Variable Text, Segoe UI");
    static readonly double[] Hs = { 0, 1.0, .8, .3, 0 };      // tinggi relatif tiap titik (Dzuhur paling tinggi)

    public DateTime[] Times = new DateTime[5];
    public DateTime Now = DateTime.Now;
    public bool IsToday = true;
    public int NextIdx = -1;
    public double Phase;

    FormattedText Ft(string s, double size, FontWeight wt, Color c)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Fam, FontStyles.Normal, wt, FontStretches.Normal), size, Pal.Br(c), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    static double Hgt(double t)   // Catmull-Rom melalui 5 titik, t = 0..4
    {
        t = Math.Clamp(t, 0, 4);
        int i = Math.Min((int)Math.Floor(t), 3); double u = t - i;
        double p0 = Hs[Math.Max(i - 1, 0)], p1 = Hs[i], p2 = Hs[i + 1], p3 = Hs[Math.Min(i + 2, 4)];
        return Math.Max(0, .5 * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u));
    }
    static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static LinearGradientBrush Hor(Color a, Color b, double x0, double x1)
    {
        var br = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(x0, 0), EndPoint = new Point(x1, 0) };
        br.GradientStops.Add(new GradientStop(a, 0)); br.GradientStops.Add(new GradientStop(b, 1)); return br;
    }
    static LinearGradientBrush Ver(Color a, Color b)
    {
        var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        br.GradientStops.Add(new GradientStop(a, 0)); br.GradientStops.Add(new GradientStop(b, 1)); return br;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w < 60) return;
        double x0 = 22, x1 = w - 22, baseY = h - 35, arcH = baseY - 12, span = x1 - x0;
        double Yx(double x) => baseY - arcH * Hgt((x - x0) / span * 4) / 1.04;

        var line = new StreamGeometry(); var area = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(new Point(x0, Yx(x0)), false, false);
            for (int k = 1; k <= 90; k++) { double x = x0 + span * k / 90; c.LineTo(new Point(x, Yx(x)), true, true); }
        }
        using (var c = area.Open())
        {
            c.BeginFigure(new Point(x0, baseY), true, true);
            for (int k = 0; k <= 90; k++) { double x = x0 + span * k / 90; c.LineTo(new Point(x, Yx(x)), true, true); }
            c.LineTo(new Point(x1, baseY), true, true);
        }
        line.Freeze(); area.Freeze();

        // posisi matahari: menyusuri titik-titik menurut jam sekarang
        double sunX = x0;
        if (Now >= Times[4]) sunX = x1;
        else if (Now > Times[0])
            for (int i = 0; i < 4; i++)
                if (Now < Times[i + 1])
                {
                    double f = (Now - Times[i]).TotalSeconds / Math.Max(1, (Times[i + 1] - Times[i]).TotalSeconds);
                    sunX = x0 + (i + f) * span / 4; break;
                }

        // garis dasar + busur redup (bagian yang belum dilewati)
        dc.DrawLine(new Pen(Pal.Br(Pal.Alpha(Pal.Dim, 0x40)), 1), new Point(4, baseY), new Point(w - 4, baseY));
        dc.DrawGeometry(Ver(Pal.Alpha(Pal.Dim, 0x22), Pal.Alpha(Pal.Dim, 0x00)), null, area);
        dc.DrawGeometry(null, new Pen(Pal.Br(Pal.Alpha(Pal.Dim, 0x70)), 1.4) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0), DashCap = PenLineCap.Round }, line);

        // bagian yang sudah dilewati: gradien cyan -> violet menyala
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, sunX), h)));
        dc.DrawGeometry(Ver(Pal.Alpha(Pal.Violet, 0x58), Pal.Alpha(Pal.Cyan, 0x00)), null, area);
        dc.DrawGeometry(null, new Pen(Hor(Pal.Alpha(Pal.Cyan, 0x2A), Pal.Alpha(Pal.Violet, 0x2A), x0, x1), 7) { LineJoin = PenLineJoin.Round }, line);
        dc.DrawGeometry(null, new Pen(Hor(Pal.Cyan, Pal.Violet, x0, x1), 2.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
        dc.Pop();

        // titik tiap waktu + label
        for (int i = 0; i < 5; i++)
        {
            double x = x0 + i * span / 4, y = Yx(x);
            bool passed = Now >= Times[i], next = i == NextIdx;
            var col = Mix(Pal.Cyan, Pal.Violet, i / 4.0);
            if (next)
            {
                double pr = 6.5 + 3 * (.5 + .5 * Math.Sin(Phase));
                dc.DrawEllipse(null, new Pen(Pal.Br(Pal.Alpha(col, (byte)(150 - 90 * (pr - 6.5) / 3))), 1.2), new Point(x, y), pr, pr);
                dc.DrawEllipse(Pal.Br(Pal.Bg), new Pen(Pal.Br(col), 2), new Point(x, y), 4.2, 4.2);
            }
            else if (passed) dc.DrawEllipse(Pal.Br(col), null, new Point(x, y), 3.5, 3.5);
            else dc.DrawEllipse(Pal.Br(Pal.Bg), new Pen(Pal.Br(Pal.Alpha(Pal.Dim, 0xB0)), 1.3), new Point(x, y), 3.2, 3.2);

            var nm = Ft(PrayerCalc.Names[i], 10.5, next ? FontWeights.SemiBold : FontWeights.Medium, next ? Pal.Ink : (passed ? Pal.Alpha(Pal.Ink, 0xC0) : Pal.Dim));
            var tm = Ft(Times[i].ToString("HH:mm"), 10, FontWeights.Normal, next ? col : Pal.Dim);
            dc.DrawText(nm, new Point(x - nm.Width / 2, baseY + 9));
            dc.DrawText(tm, new Point(x - tm.Width / 2, baseY + 9 + nm.Height + 1));
        }

        // matahari bercahaya (hanya untuk hari ini)
        if (IsToday)
        {
            var c = new Point(sunX, Yx(sunX));
            double pr = 15 + 2.5 * Math.Sin(Phase);
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0xFD, 0xE6, 0x8A), 0));
            glow.GradientStops.Add(new GradientStop(Pal.Alpha(Pal.Cyan, 0x44), .45));
            glow.GradientStops.Add(new GradientStop(Pal.Alpha(Pal.Violet, 0x00), 1));
            dc.DrawEllipse(glow, null, c, pr, pr);
            dc.DrawEllipse(Pal.Br(Color.FromRgb(0xFF, 0xF4, 0xC8)), null, c, 4.3, 4.3);
        }
    }
}

// ---------- Pembungkus "muat-pas": bila tinggi tidak cukup, isinya dikecilkan proporsional (tanpa scroll bar) ----------
sealed class FitBox : Decorator
{
    readonly ScaleTransform st = new(1, 1);
    double s = 1;
    public double MinScale { get; set; } = .55;

    protected override Size MeasureOverride(Size avail)
    {
        if (Child == null) return new Size(0, 0);
        bool fw = !double.IsInfinity(avail.Width), fh = !double.IsInfinity(avail.Height);
        Child.Measure(new Size(avail.Width, double.PositiveInfinity));
        double h = Child.DesiredSize.Height;
        s = 1;
        if (fh && h > avail.Height && h > 0) s = Math.Max(MinScale, avail.Height / h);
        if (s < 1 && fw) Child.Measure(new Size(avail.Width / s, double.PositiveInfinity));   // lebar dilebarkan agar setelah mengecil tetap memenuhi panel
        double rw = fw ? avail.Width : Child.DesiredSize.Width * s;
        double rh = Child.DesiredSize.Height * s;
        return new Size(rw, fh ? Math.Min(rh, avail.Height) : rh);
    }

    protected override Size ArrangeOverride(Size fin)
    {
        if (Child == null) return fin;
        st.ScaleX = st.ScaleY = s;
        Child.RenderTransformOrigin = new Point(0, 0); Child.RenderTransform = st;
        Child.Arrange(new Rect(0, 0, fin.Width / s, Child.DesiredSize.Height));
        return fin;
    }
}

// ---------- Tab Prayer: tampil di dalam panel (sama seperti tab Semua / Pin / Hide) ----------
// Kartu "Next" di atas, busur waktu (animasi) di bawah, terpisah.
sealed class PrayerView : StackPanel
{
    double barW = 208;   // lebar bar progres (mengikuti lebar kartu)
    static readonly CultureInfo En = new("en-US");

    readonly Config cfg;
    readonly TextBlock tCity, tDate, tClock, tNextLbl, tName, tTime, tIn, tDay;
    readonly Border fill;
    readonly ArcView arc = new() { Height = 106 };
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer anim = new() { Interval = TimeSpan.FromMilliseconds(33) };
    int dayOff; bool running;

    public PrayerView(Config c)
    {
        cfg = c; Margin = new Thickness(16, 6, 16, 14);

        TextBlock T(double size, FontWeight wt, Brush fg, FontFamily? ff = null)
        {
            var t = new TextBlock { FontSize = size, FontWeight = wt, Foreground = fg };
            if (ff != null) t.FontFamily = ff; return t;
        }

        // --- header: kota kiri, tanggal + jam kanan
        var head = new Grid { Margin = new Thickness(2, 0, 2, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tCity = T(12.5, FontWeights.SemiBold, Pal.Br(Pal.Ink)); tCity.VerticalAlignment = VerticalAlignment.Center; tCity.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(tCity);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        tDate = T(10, FontWeights.Medium, Pal.Br(Pal.Dim)); tDate.HorizontalAlignment = HorizontalAlignment.Right;
        tClock = T(14, FontWeights.Bold, Pal.Br(Pal.Ink)); tClock.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(tDate); right.Children.Add(tClock); Grid.SetColumn(right, 1); head.Children.Add(right);

        // --- kartu "Next"
        tNextLbl = T(11, FontWeights.Medium, Pal.Br(Pal.Dim));
        tName = T(22, FontWeights.Bold, Pal.CV(0, true), Fx.Jakarta);
        tTime = T(14, FontWeights.SemiBold, Pal.Br(Pal.Ink));
        tIn = T(11, FontWeights.SemiBold, Pal.Br(Pal.Cyan)); tIn.HorizontalAlignment = HorizontalAlignment.Right; tIn.VerticalAlignment = VerticalAlignment.Bottom; tIn.Margin = new Thickness(0, 0, 0, 2);
        var tl = new Grid { Margin = new Thickness(0, 1, 0, 0) }; tl.Children.Add(tTime); tl.Children.Add(tIn);
        fill = new Border { Height = 5, Width = 0, CornerRadius = new CornerRadius(2.5), Background = Pal.CV(0), HorizontalAlignment = HorizontalAlignment.Left };
        var bar = new Grid { Height = 5, Margin = new Thickness(0, 6, 0, 0) };
        bar.SizeChanged += (_, _) => { if (Math.Abs(bar.ActualWidth - barW) > .5) { barW = bar.ActualWidth; Update(); } };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = Pal.Br(Pal.Track) }); bar.Children.Add(fill);
        var nsp = new StackPanel(); nsp.Children.Add(tNextLbl); nsp.Children.Add(tName); nsp.Children.Add(tl); nsp.Children.Add(bar);
        var next = new Border
        {
            Margin = new Thickness(0, 9, 0, 0), Padding = new Thickness(12, 7, 12, 9), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = nsp,
            Background = new LinearGradientBrush(Pal.Alpha(Pal.Cyan, 0x24), Pal.Alpha(Pal.Violet, 0x30), 45),
            BorderBrush = new LinearGradientBrush(Pal.Alpha(Pal.Cyan, 0x50), Pal.Alpha(Pal.Violet, 0x50), 45)
        };

        // --- kartu busur (animasi) di bawah, dengan navigasi hari
        var nav = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); nav.ColumnDefinitions.Add(new ColumnDefinition()); nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prev = NavBtn("‹", () => Shift(-1)); var nxt = NavBtn("›", () => Shift(1));
        tDay = T(11, FontWeights.SemiBold, Pal.Br(Pal.Dim)); tDay.HorizontalAlignment = HorizontalAlignment.Center; tDay.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tDay, 1); Grid.SetColumn(nxt, 2);
        nav.Children.Add(prev); nav.Children.Add(tDay); nav.Children.Add(nxt);
        var asp = new StackPanel(); asp.Children.Add(nav); asp.Children.Add(arc);
        var arcCard = new Border
        {
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = asp,
            Background = Pal.Br(Pal.Raised), BorderBrush = Pal.Br(Pal.Stroke)
        };

        Children.Add(head); Children.Add(next); Children.Add(arcCard); Children.Add(DzikirCard());
        Refill(); Update();
        tick.Tick += (_, _) => Update();
        anim.Tick += (_, _) => { arc.Phase += .11; arc.InvalidateVisual(); };
    }

    // ---------- Dzikir Counter: pilihan dzikir · jumlah · tombol bulat (hitung mundur, senyap; bunyi hanya saat selesai) ----------
    static readonly string[] Dz = { "Subhanallah", "Alhamdulillah", "La ilaha illallah", "Allahu Akbar", "Astaghfirullah", "Lainnya" };
    public static int PopupOpen;
    int dzSel, dzTarget; bool dzRun, dzDone, dzProg;
    TextBox dzBox = null!; TextBlock dzName = null!, dzTip = null!;
    Popup? dzPop; DropShadowEffect? dzGlow;
    readonly ScaleTransform dzNum = new(1, 1);
    long dzClosedAt;

    Border DzikirCard()
    {
        dzSel = Math.Clamp(cfg.DzikirIdx, 0, Dz.Length - 1); dzTarget = cfg.DzikirTarget > 0 ? cfg.DzikirTarget : 33;

        var title = new TextBlock { Text = "Dzikir Counter", FontFamily = Fx.Jakarta, FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Pal.CV(0, true), Margin = new Thickness(2, 0, 0, 7) };

        // --- dropdown cantik
        var faceBg = new SolidColorBrush(Pal.Raised); var faceLine = new SolidColorBrush(Pal.Stroke);
        dzName = new TextBlock { Text = Dz[dzSel], FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(Pal.Ink), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chevRot = new RotateTransform(0);
        var chev = Icons.Glyph("M6,9.5 L12,15.5 L18,9.5", 11, Pal.Br(Pal.Dim), 2.2);
        chev.RenderTransformOrigin = new Point(.5, .5); chev.RenderTransform = chevRot;
        if (chev is FrameworkElement ce) { ce.VerticalAlignment = VerticalAlignment.Center; ce.Margin = new Thickness(6, 0, 0, 0); }
        var fg = new Grid { Margin = new Thickness(10, 0, 8, 0) };
        fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fg.Children.Add(dzName); Grid.SetColumn(chev, 1); fg.Children.Add(chev);
        var face = new Border { Height = 28, CornerRadius = new CornerRadius(8), Background = faceBg, BorderBrush = faceLine, BorderThickness = new Thickness(1), Child = fg, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 6, 0) };

        var items = new StackPanel();
        var popCard = new Border { Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Stroke), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Padding = new Thickness(4), Child = items };
        var popT = new TranslateTransform(0, 8);
        var popRoot = new Grid { Margin = new Thickness(10), Opacity = 0, RenderTransform = popT, Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Opacity = .5 } };
        popRoot.Children.Add(popCard);
        var pop = new Popup { PlacementTarget = face, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, HorizontalOffset = -10, VerticalOffset = 6, Child = popRoot };
        dzPop = pop;

        void Build()
        {
            items.Children.Clear();
            for (int i = 0; i < Dz.Length; i++)
            {
                int k = i; bool on = k == dzSel;
                var hl = new SolidColorBrush(Pal.Alpha(Pal.Hover, 0));
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = on ? (Brush)Pal.CV(45) : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) };
                var fgc = new SolidColorBrush(on ? Pal.Ink : Pal.Dim);
                var tx = new TextBlock { Text = Dz[k], FontSize = 11.5, FontWeight = on ? FontWeights.SemiBold : FontWeights.Medium, Foreground = fgc, VerticalAlignment = VerticalAlignment.Center };
                var sp = new StackPanel { Orientation = Orientation.Horizontal }; sp.Children.Add(dot); sp.Children.Add(tx);
                var it = new Border { Height = 28, CornerRadius = new CornerRadius(7), Background = hl, Padding = new Thickness(9, 0, 14, 0), Cursor = Cursors.Hand, Child = sp };
                it.MouseEnter += (_, _) => { ToC(hl, Pal.Hover, 120); ToC(fgc, Pal.Ink, 120); };
                it.MouseLeave += (_, _) => { ToC(hl, Pal.Alpha(Pal.Hover, 0), 180); ToC(fgc, on ? Pal.Ink : Pal.Dim, 180); };
                it.MouseLeftButtonUp += (_, _) => { Select(k); pop.IsOpen = false; };
                items.Children.Add(it);
            }
        }
        pop.Opened += (_, _) =>
        {
            PopupOpen++;
            To(chevRot, RotateTransform.AngleProperty, 180, 240); ToC(faceLine, Pal.Alpha(Pal.Cyan, 0xB0), 160); ToC(faceBg, Pal.Hover, 160);
            popRoot.BeginAnimation(OpacityProperty, null); popRoot.Opacity = 0; popT.BeginAnimation(TranslateTransform.YProperty, null); popT.Y = 8;
            To(popRoot, OpacityProperty, 1, 170); To(popT, TranslateTransform.YProperty, 0, 240);
        };
        pop.Closed += (_, _) =>
        {
            PopupOpen = Math.Max(0, PopupOpen - 1); dzClosedAt = Environment.TickCount64;
            To(chevRot, RotateTransform.AngleProperty, 0, 240); ToC(faceLine, Pal.Stroke, 200); ToC(faceBg, Pal.Raised, 200);
        };
        face.MouseEnter += (_, _) => { if (!pop.IsOpen) ToC(faceBg, Pal.Hover, 140); };
        face.MouseLeave += (_, _) => { if (!pop.IsOpen) ToC(faceBg, Pal.Raised, 200); };
        face.MouseLeftButtonUp += (_, _) =>
        {
            if (pop.IsOpen) { pop.IsOpen = false; return; }
            if (Environment.TickCount64 - dzClosedAt < 250) return;   // klik yang barusan menutup popup, jangan dibuka lagi
            Build(); popCard.MinWidth = Math.Max(150, face.ActualWidth); pop.IsOpen = true;
        };

        // --- kotak angka cantik
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/></ControlTemplate>");
        dzBox = new TextBox
        {
            Template = tpl, Text = dzTarget.ToString(), MaxLength = 4, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(Pal.Ink), CaretBrush = Pal.Br(Pal.Cyan),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0), RenderTransformOrigin = new Point(.5, .5), RenderTransform = dzNum
        };
        var hint = new TextBlock { Text = "jumlah", FontSize = 9.5, Foreground = Pal.Br(Pal.Alpha(Pal.Dim, 0xA0)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var boxG = new Grid(); boxG.Children.Add(dzBox); boxG.Children.Add(hint);
        var boxLine = new SolidColorBrush(Pal.Stroke);
        var boxB = new Border { Width = 52, Height = 28, CornerRadius = new CornerRadius(8), Background = Pal.Br(Pal.Raised), BorderBrush = boxLine, BorderThickness = new Thickness(1), Child = boxG, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.IBeam };
        boxB.MouseLeftButtonDown += (_, _) => dzBox.Focus();
        dzBox.GotKeyboardFocus += (_, _) => { ToC(boxLine, Pal.Alpha(Pal.Cyan, 0xC0), 160); dzBox.SelectAll(); };
        dzBox.LostKeyboardFocus += (_, _) => ToC(boxLine, Pal.Stroke, 220);
        dzBox.PreviewTextInput += (_, e) => e.Handled = e.Text.Any(ch => ch < '0' || ch > '9');
        dzBox.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { DzStep(); e.Handled = true; } };
        dzBox.TextChanged += (_, _) =>
        {
            hint.Visibility = dzBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (dzProg) return;
            var f = new string(dzBox.Text.Where(ch => ch >= '0' && ch <= '9').ToArray());
            if (f != dzBox.Text) { dzProg = true; dzBox.Text = f; dzBox.CaretIndex = f.Length; dzProg = false; }
            dzRun = false; dzDone = false; dzTip.Text = "Klik buat mulai";
            if (int.TryParse(f, out var n) && n > 0) { dzTarget = n; cfg.DzikirTarget = n; cfg.Save(); }
        };

        // --- tombol bulat gradien cyan-violet yang menyala
        dzGlow = new DropShadowEffect { Color = Pal.Cyan, BlurRadius = 10, ShadowDepth = 0, Opacity = .75 };
        var st = new ScaleTransform(1, 1);
        var play = Icons.Glyph("M9.5,6.5 L18,12 L9.5,17.5 Z", 13, Pal.Br(Pal.AccentInk), 1.6, true);
        if (play is FrameworkElement pe) { pe.HorizontalAlignment = HorizontalAlignment.Center; pe.VerticalAlignment = VerticalAlignment.Center; pe.Margin = new Thickness(2, 0, 0, 0); }
        var btn = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Pal.CV(45), Cursor = Cursors.Hand, Effect = dzGlow, RenderTransformOrigin = new Point(.5, .5), RenderTransform = st, Child = play };
        dzTip = new TextBlock { Text = "Klik buat mulai", FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Pal.Br(Pal.Ink) };
        var tip = new ToolTip
        {
            Content = dzTip, Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Alpha(Pal.Cyan, 0xA0)), Padding = new Thickness(9, 4, 9, 4), HasDropShadow = false,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popupSize, targetSize, _) => new[] { new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, -popupSize.Height - 6), PopupPrimaryAxis.None) }
        };
        btn.ToolTip = tip; ToolTipService.SetInitialShowDelay(btn, 120); ToolTipService.SetShowDuration(btn, 4000);
        bool down = false;
        btn.MouseEnter += (_, _) => Pop(st, 1.13, 560);
        btn.MouseLeave += (_, _) => { down = false; Scale(st, 1, 180); };
        btn.MouseLeftButtonDown += (_, _) => { down = true; Scale(st, .88, 80); };
        btn.MouseLeftButtonUp += (_, _) => { Scale(st, 1.06, 200); if (!down) return; down = false; DzStep(); };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(boxB, 1); Grid.SetColumn(btn, 2);
        row.Children.Add(face); row.Children.Add(boxB); row.Children.Add(btn);

        var sp2 = new StackPanel(); sp2.Children.Add(title); sp2.Children.Add(row);
        return new Border
        {
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 9, 10, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = sp2,
            Background = Pal.Br(Pal.Raised), BorderBrush = Pal.Br(Pal.Stroke)
        };
    }

    void Select(int k)
    {
        dzSel = k; dzName.Text = Dz[k]; cfg.DzikirIdx = k; cfg.Save();
        dzRun = false; dzDone = false; SetBox(dzTarget); dzTip.Text = "Klik buat mulai";
    }
    void SetBox(int n) { dzProg = true; dzBox.Text = n.ToString(); dzProg = false; }

    // Tiap klik: hitung mundur satu (senyap). Sampai 0 -> bel penutup + pemberitahuan. Klik lagi setelah selesai = ulang dari awal.
    void DzStep()
    {
        int.TryParse(dzBox.Text, out var n);
        if (n <= 0)
        {
            if (dzDone && dzTarget > 0) { dzDone = false; SetBox(dzTarget); dzTip.Text = "Klik buat mulai"; return; }
            Toast.Show("Isi jumlah dzikir dulu"); return;
        }
        if (!dzRun) { dzRun = true; dzTarget = n; }
        n--; SetBox(n); dzTip.Text = "Klik untuk hitung mundur";
        dzNum.BeginAnimation(ScaleTransform.ScaleXProperty, null); dzNum.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        dzNum.ScaleX = dzNum.ScaleY = 1.2; Pop(dzNum, 1, 460);
        if (n == 0)
        {
            dzRun = false; dzDone = true; dzTip.Text = "Klik buat ulang";
            Chime.Play();   // satu-satunya bunyi: saat hitungan selesai
            Toast.Show($"{(dzSel == Dz.Length - 1 ? "Dzikir" : Dz[dzSel])} ×{dzTarget} selesai");
        }
    }

    // Tombol menyala: cahaya berdenyut dan berganti halus cyan <-> violet (hanya saat tab Prayer tampil)
    void Glow(bool on)
    {
        if (dzGlow == null) return;
        dzGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null); dzGlow.BeginAnimation(DropShadowEffect.OpacityProperty, null); dzGlow.BeginAnimation(DropShadowEffect.ColorProperty, null);
        if (!on) return;
        var ez = new SineEase { EasingMode = EasingMode.EaseInOut };
        dzGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(7, 20, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ez });
        dzGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(1300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ez });
        dzGlow.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(Pal.Cyan, Pal.Violet, TimeSpan.FromMilliseconds(2200)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ez });
    }

    Border NavBtn(string glyph, Action click)
    {
        var bg = new SolidColorBrush(Pal.Alpha(Pal.Hover, 0)); var fg = new SolidColorBrush(Pal.Dim);
        var b = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = bg, Cursor = Cursors.Hand,
            Child = new TextBlock { Text = glyph, FontSize = 17, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, 0, 0) }
        };
        b.MouseEnter += (_, _) => { ToC(bg, Pal.Hover, 140); ToC(fg, Pal.Ink, 140); };
        b.MouseLeave += (_, _) => { ToC(bg, Pal.Alpha(Pal.Hover, 0), 200); ToC(fg, Pal.Dim, 200); };
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    public void Start()
    {
        if (running) return; running = true;
        Geo.Changed += OnGeo; Refill(); Update(); tick.Start(); anim.Start(); Glow(true);
    }
    public void Stop()
    {
        if (!running) return; running = false;
        Geo.Changed -= OnGeo; tick.Stop(); anim.Stop(); Glow(false);
        if (dzPop != null) dzPop.IsOpen = false;
    }

    void OnGeo() { Refill(); Update(); }
    void Shift(int d) { dayOff = Math.Clamp(dayOff + d, -3, 7); Refill(); Update(); }

    // Muat jadwal hari yang sedang dilihat ke busur
    void Refill()
    {
        var day = DateTime.Today.AddDays(dayOff);
        arc.Times = PrayerCalc.Day(day, cfg.PrayerLat, cfg.PrayerLon); arc.IsToday = dayOff == 0;
        tCity.Text = "Prayer · " + cfg.PrayerCity;
        tDay.Text = dayOff == 0 ? "Hari ini" : dayOff == 1 ? "Besok" : dayOff == -1 ? "Kemarin" : day.ToString("ddd d MMM", En);
    }

    // Tiap detik: jam, kartu "Next", progres, posisi matahari
    void Update()
    {
        var now = DateTime.Now;
        tClock.Text = now.ToString("HH:mm", En); tDate.Text = now.ToString("ddd d MMM yyyy", En);

        var ev = new List<(DateTime t, int i)>();
        for (int d = -1; d <= 1; d++) { var ts = PrayerCalc.Day(now.Date.AddDays(d), cfg.PrayerLat, cfg.PrayerLon); for (int i = 0; i < 5; i++) ev.Add((ts[i], i)); }
        int n = ev.FindIndex(e => e.t > now); if (n < 1) n = 1;
        var nx = ev[n]; var pv = ev[n - 1];

        tNextLbl.Text = nx.t.Date > now.Date ? "Next, tomorrow" : "Next";
        tName.Text = PrayerCalc.Names[nx.i]; tTime.Text = nx.t.ToString("HH:mm", En);
        int mins = (int)Math.Ceiling((nx.t - now).TotalMinutes); if (mins < 1) mins = 1;
        tIn.Text = mins >= 60 ? $"in {mins / 60} h {mins % 60} min" : $"in {mins} min";
        double frac = Math.Clamp((now - pv.t).TotalSeconds / Math.Max(1, (nx.t - pv.t).TotalSeconds), 0, 1);
        To(fill, WidthProperty, frac * barW, 700);

        arc.Now = now; arc.NextIdx = nx.t.Date == DateTime.Today.AddDays(dayOff) ? nx.i : -1;
    }
}
