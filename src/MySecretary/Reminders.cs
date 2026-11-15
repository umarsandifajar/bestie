using System.Globalization;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using static MySecretary.Fx;
namespace MySecretary;

// ---------------- Reminders & Alarm ----------------
// Alarm      : pop-up lonceng + bunyi berulang sampai ditunda (Snooze) atau dimatikan.
// Reminder   : pop-up lonceng + satu bunyi bel lembut (seperti pengingat adzan), hilang sendiri setelah 30 detik; tetap bisa ditunda.
// Jadwal diperiksa tiap detik oleh SecretaryWindow (RemindEngine.Tick), jadi tetap jalan walau panel bestie tertutup.

public class ReminderItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public bool Alarm { get; set; } = true;       // true = alarm (bunyi berulang), false = pengingat (sekali)
    public int Hour { get; set; } = 7;
    public int Minute { get; set; } = 0;
    public int Days { get; set; } = 0;            // bitmask hari: bit0 = Senin ... bit6 = Minggu. 0 = sekali saja
    public string Date { get; set; } = "";        // yyyy-MM-dd, dipakai bila Days == 0
    public int Snooze { get; set; } = 5;          // menit
    public bool Enabled { get; set; } = true;
}

static class RemindEngine
{
    public static event Action? Changed;
    public static readonly string[] DayShort = { "Sen", "Sel", "Rab", "Kam", "Jum", "Sab", "Min" };
    static readonly Dictionary<string, DateTime> snoozed = new();     // id -> kapan berbunyi lagi
    static readonly Dictionary<string, string> fired = new();         // id -> menit terakhir berbunyi (cegah dobel)

    static int Bit(DayOfWeek d) => d == DayOfWeek.Sunday ? 6 : (int)d - 1;

    // Tanggal "sekali" berikutnya untuk jam:menit (hari ini bila belum lewat, kalau sudah lewat besok)
    public static string NextDate(int h, int m)
    {
        var due = DateTime.Today.AddHours(h).AddMinutes(m);
        return (due.AddMinutes(1) <= DateTime.Now ? DateTime.Today.AddDays(1) : DateTime.Today).ToString("yyyy-MM-dd");
    }

    public static string Repeat(ReminderItem r)
    {
        if (r.Days == 0)
        {
            if (r.Date == DateTime.Today.ToString("yyyy-MM-dd")) return "Hari ini";
            if (r.Date == DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")) return "Besok";
            return DateTime.TryParse(r.Date, out var d) ? d.ToString("d MMM yyyy", new CultureInfo("id-ID")) : "Sekali";
        }
        if (r.Days == 127) return "Tiap hari";
        if (r.Days == 31) return "Hari kerja";
        if (r.Days == 96) return "Akhir pekan";
        return string.Join(" ", Enumerable.Range(0, 7).Where(i => (r.Days & (1 << i)) != 0).Select(i => DayShort[i]));
    }

    public static void Snooze(ReminderItem r) { snoozed[r.Id] = DateTime.Now.AddMinutes(Math.Max(1, r.Snooze)); NotchHub.Ping(); }
    public static void Cancel(string id) { snoozed.Remove(id); }
    public static bool IsSnoozed(string id) => snoozed.ContainsKey(id);   // dipakai ikon lonceng di notch: yang ditunda masih dianggap aktif

    public static void Tick(Config cfg)
    {
        if (cfg.Reminders.Count == 0 && snoozed.Count == 0) return;
        var now = DateTime.Now; bool dirty = false, snoozeFired = false;

        // yang ditunda dan waktunya sudah habis
        foreach (var kv in snoozed.ToList())
        {
            if (now < kv.Value) continue;
            snoozed.Remove(kv.Key); snoozeFired = true;
            var s = cfg.Reminders.FirstOrDefault(x => x.Id == kv.Key);
            if (s != null) ReminderTip.Notify(s, true);
        }

        // jadwal biasa
        var today = now.ToString("yyyy-MM-dd"); var key = now.ToString("yyyyMMddHHmm");
        foreach (var r in cfg.Reminders.ToList())
        {
            if (!r.Enabled || now.Hour != r.Hour || now.Minute != r.Minute) continue;
            if (fired.TryGetValue(r.Id, out var f) && f == key) continue;
            bool onToday = r.Days == 0 ? r.Date == today : (r.Days & (1 << Bit(now.DayOfWeek))) != 0;
            if (!onToday) continue;
            fired[r.Id] = key;
            ReminderTip.Notify(r, false);
            if (r.Days == 0) { r.Enabled = false; dirty = true; }   // sekali saja: matikan setelah berbunyi
        }
        if (dirty) { cfg.Save(); Changed?.Invoke(); }
        if (dirty || snoozeFired) NotchHub.Ping();   // ikon lonceng di notch ikut diperbarui
    }
}

// ---------- Bunyi alarm: tiga denting bel naik (disintesis, tanpa file audio) ----------
static class RingTone
{
    static byte[]? wav;
    static SoundPlayer? sp;

    public static void Alarm()
    {
        try
        {
            wav ??= Build();
            sp?.Stop();
            sp = new SoundPlayer(new MemoryStream(wav));
            sp.Play();
        }
        catch { SystemSounds.Exclamation.Play(); }
    }
    public static void Stop() { try { sp?.Stop(); } catch { } }

    static byte[] Build()
    {
        const int sr = 44100; const double dur = 2.8;
        int n = (int)(sr * dur);
        double[] t0 = { 0, .34, .68 }, f0 = { 659.25, 783.99, 987.77 };                  // E5 - G5 - B5
        double[][] parts = { new[] { 1.0, 1.0, 1.0 }, new[] { 2.0, .28, .55 }, new[] { 2.76, .2, .36 }, new[] { 4.07, .07, .22 } };   // rasio, amplitudo, peluruhan (detik)
        var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
        bw.Write(new[] { 'R', 'I', 'F', 'F' }); bw.Write(36 + n * 2); bw.Write(new[] { 'W', 'A', 'V', 'E' });
        bw.Write(new[] { 'f', 'm', 't', ' ' }); bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write(new[] { 'd', 'a', 't', 'a' }); bw.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sr, s = 0;
            for (int h = 0; h < t0.Length; h++)
            {
                double dt = t - t0[h]; if (dt < 0) continue;
                double att = Math.Min(1, dt / .006);
                foreach (var p in parts) s += p[1] * Math.Sin(2 * Math.PI * f0[h] * p[0] * dt) * Math.Exp(-dt / p[2]) * att;
            }
            double rel = Math.Min(1, (dur - t) / .15);
            bw.Write((short)(Math.Clamp(s * rel * .34 / 1.6, -1, 1) * 32767));
        }
        bw.Flush(); return ms.ToArray();
    }
}

// ---------- Pop-up alarm / pengingat (gaya sama dengan pop-up adzan: kartu hitam, panah ke jam, meluncur dari kiri) ----------
sealed class ReminderTip : Window
{
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

    static readonly List<ReminderTip> Live = new();
    static readonly CultureInfo Id = new("id-ID");

    readonly TranslateTransform slide = new(-26, 0);
    readonly RotateTransform swing = new(0);
    readonly DispatcherTimer life = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer rattle = new() { Interval = TimeSpan.FromMilliseconds(1300) };
    readonly DispatcherTimer ring = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly ReminderItem item; readonly bool isAlarm, test;
    int rings; bool closing;

    public static void Notify(ReminderItem r, bool again = false, bool test = false)
    {
        if (!test) foreach (var old in Live.Where(x => x.item.Id == r.Id && !x.test).ToList()) old.Dismiss();
        new ReminderTip(r, again, test).Present();
    }

    ReminderTip(ReminderItem r, bool again, bool test)
    {
        item = r; isAlarm = r.Alarm; this.test = test;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize; Opacity = 0;
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80 | 0x08000000); };

        var tint = isAlarm ? Color.FromRgb(0xFA, 0xCC, 0x15) : Pal.Cyan;
        var bell = Icons.Glyph(Icons.Bell, 21, Pal.Br(tint), 1.7);
        if (bell is FrameworkElement be) { be.HorizontalAlignment = HorizontalAlignment.Center; be.VerticalAlignment = VerticalAlignment.Center; }
        bell.RenderTransformOrigin = new Point(.5, .12); bell.RenderTransform = swing;
        var tile = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x2A, tint.R, tint.G, tint.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, tint.R, tint.G, tint.B)), BorderThickness = new Thickness(1), Child = bell
        };

        var cap = new TextBlock
        {
            Text = (isAlarm ? "ALARM" : "PENGINGAT") + (again ? " · DITUNDA" : ""), FontSize = 8.5, FontWeight = FontWeights.SemiBold,
            Foreground = Pal.Br(Pal.Dim), Margin = new Thickness(0, 0, 0, 2)
        };
        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(r.Title) ? (isAlarm ? "Alarm" : "Pengingat") : r.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = Pal.Br(Pal.Ink), MaxWidth = 230, TextWrapping = TextWrapping.Wrap
        };
        var sub = new TextBlock { Text = $"{r.Hour:00}:{r.Minute:00} · {DateTime.Now.ToString("ddd, d MMM", Id)}", FontSize = 11, Foreground = Pal.Br(Pal.Dim), Margin = new Thickness(0, 1, 0, 0) };
        var txt = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(cap); txt.Children.Add(title); txt.Children.Add(sub);
        var top = new StackPanel { Orientation = Orientation.Horizontal }; top.Children.Add(tile); top.Children.Add(txt);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(51, 11, 0, 0) };
        btns.Children.Add(Pill($"Tunda {Math.Max(1, r.Snooze)} mnt", true, SnoozeNow));
        btns.Children.Add(Pill(isAlarm ? "Matikan" : "Oke", false, Dismiss));

        var col = new StackPanel(); col.Children.Add(top); col.Children.Add(btns);
        var card = new Border
        {
            Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 9, 16, 11), BorderThickness = new Thickness(1),
            Background = Pal.Br(Pal.Bg), BorderBrush = Pal.Br(Pal.Stroke), Child = col
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

        life.Tick += (_, _) => Dismiss();
        rattle.Tick += (_, _) => Rattle();
        ring.Tick += (_, _) => { if (++rings >= 12) ring.Stop(); else RingTone.Alarm(); };   // alarm berbunyi ~1 menit, pop-up tetap sampai ditutup
        Closed += (_, _) => { life.Stop(); rattle.Stop(); ring.Stop(); Live.Remove(this); };
    }

    // Tombol pil: utama = gradien cyan-violet, biasa = kartu gelap bergaris
    static Border Pill(string text, bool primary, Action a)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = primary ? Pal.Br(Color.FromRgb(0x0B, 0x0B, 0x0C)) : Pal.Br(Pal.Ink)
        };
        var b = new Border
        {
            Height = 28, MinWidth = 76, CornerRadius = new CornerRadius(9), Padding = new Thickness(13, 0, 13, 0), Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = tb,
            Background = primary ? (Brush)Pal.CV(0) : Pal.Br(Pal.Raised), BorderBrush = primary ? Brushes.Transparent : Pal.Br(Pal.Stroke)
        };
        var sc = new ScaleTransform(1, 1); b.RenderTransformOrigin = new Point(.5, .5); b.RenderTransform = sc;
        bool down = false;
        b.MouseEnter += (_, _) => b.Opacity = .86;
        b.MouseLeave += (_, _) => { down = false; b.Opacity = 1; Scale(sc, 1, 120); };
        b.MouseLeftButtonDown += (_, _) => { down = true; Scale(sc, .95, 70); };
        b.MouseLeftButtonUp += (_, _) => { Scale(sc, 1, 140); if (!down) return; down = false; a(); };
        return b;
    }

    void Present()
    {
        Live.Add(this); Relayout();
        Show();
        To(this, OpacityProperty, 1, 220); To(slide, TranslateTransform.XProperty, 0, 620, EzBounce);
        Rattle(); rattle.Start();
        if (isAlarm) { RingTone.Alarm(); ring.Start(); }
        else { Chime.Play(); life.Start(); }
    }

    // Susun pop-up (bila ada beberapa) ke bawah, di tengah tinggi layar, di sebelah jam
    static void Relayout()
    {
        if (Live.Count == 0) return;
        var wa = SystemParameters.WorkArea;
        double visible = Live.Sum(w => w.Height - 28) + 8 * (Live.Count - 1);
        double y = wa.Top + Math.Max(12, (wa.Height - visible) / 2) - 14;
        foreach (var t in Live) { t.Left = wa.Left + IslandView.HoverW + 10 - 14; t.Top = y; y += t.Height - 20; }
    }

    // Lonceng berayun dari titik gantung di atas (meredam), diulang tiap ~1,3 detik
    void Rattle()
    {
        var k = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(700) };
        double[] a = { -18, 16, -13, 10, -6, 3, 0 };
        for (int i = 0; i < a.Length; i++) k.KeyFrames.Add(new LinearDoubleKeyFrame(a[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85 * (i + 1)))));
        swing.BeginAnimation(RotateTransform.AngleProperty, k);
    }

    void SnoozeNow()
    {
        if (!test)
        {
            RemindEngine.Snooze(item);
            Toast.Show($"Ditunda {Math.Max(1, item.Snooze)} menit · {DateTime.Now.AddMinutes(Math.Max(1, item.Snooze)):HH:mm}");
        }
        Dismiss();
    }

    public void Dismiss()
    {
        if (closing) return; closing = true; life.Stop(); rattle.Stop(); ring.Stop();
        Live.Remove(this); Relayout();
        if (!Live.Any(x => x.isAlarm && !x.closing)) RingTone.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = EzIn };
        a.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, a);
        To(slide, TranslateTransform.XProperty, -14, 190, EzIn);
    }
}

// ---------- Panel Reminders & Alarm ----------
sealed class RemindersPanel : AppPanel
{
    static readonly int[] SnoozeOpts = { 5, 10, 15 };
    readonly Config cfg;
    readonly StackPanel listHost = new();
    readonly TextBox title = new() { MaxLength = 80, Margin = new Thickness(0, 0, 0, 12) };
    readonly TextBlock hh = new(), mm = new();
    readonly List<Border> dayChips = new();
    readonly List<TextBlock> dayTx = new();
    readonly Button add, cancel;
    Border kindSeg = null!, snoozeSeg = null!;
    int hour, minute, days, snooze = 5; bool alarm = true; string? editId;

    public RemindersPanel(Config c) : base("Reminders & Alarm", 560, 640)
    {
        cfg = c;
        var root = new DockPanel(); Main.Children.Add(root);

        // ---- kartu form
        var f = new StackPanel();
        var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 6), Margin = new Thickness(0, 0, 0, 12), Child = f };
        card.SetResourceReference(Border.BackgroundProperty, "Fill"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
        DockPanel.SetDock(card, Dock.Top); root.Children.Add(card);

        kindSeg = UI.Segmented(new[] { "Alarm", "Reminder" }, 0, i => { alarm = i == 0; UpdateAdd(); });
        f.Children.Add(kindSeg);
        f.Children.Add(UI.Label("Judul", true, 12));
        f.Children.Add(title);

        // waktu (spinner besar) + pengaturan cepat
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var sp = Spinner(); g.Children.Add(sp);
        var right = new StackPanel { Margin = new Thickness(26, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(UI.Label("Atur cepat (dari sekarang)", true, 12));
        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (lbl, mins) in new[] { ("+5 mnt", 5), ("+15 mnt", 15), ("+30 mnt", 30), ("+1 jam", 60) })
        {
            int mn = mins; var q = Chip(lbl, 54, false); q.Margin = new Thickness(0, 0, 6, 6);
            q.MouseLeftButtonUp += (_, _) => { var t = DateTime.Now.AddMinutes(mn); hour = t.Hour; minute = t.Minute; days = 0; PaintDays(); ShowTime(); };
            quick.Children.Add(q);
        }
        right.Children.Add(quick);
        right.Children.Add(UI.Label("Tunda (snooze)", true, 12));
        snoozeSeg = UI.Segmented(new[] { "5 mnt", "10 mnt", "15 mnt" }, 0, i => snooze = SnoozeOpts[i]);
        snoozeSeg.Margin = new Thickness(0);
        right.Children.Add(snoozeSeg);
        Grid.SetColumn(right, 1); g.Children.Add(right);
        f.Children.Add(g);

        // ulangi
        f.Children.Add(UI.Label("Ulangi", true, 12));
        var dr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        for (int i = 0; i < 7; i++)
        {
            int k = i; var ch = Chip(RemindEngine.DayShort[k], 40, true); ch.Margin = new Thickness(0, 0, 6, 0);
            ch.MouseLeftButtonUp += (_, _) => { days ^= 1 << k; PaintDays(); };
            dayChips.Add(ch); dayTx.Add((TextBlock)ch.Child); dr.Children.Add(ch);
        }
        f.Children.Add(dr);
        var hint = UI.Label("Tidak ada hari dipilih = sekali saja (hari ini bila waktunya belum lewat, kalau sudah lewat besok).", true, 11);
        hint.Margin = new Thickness(0, 2, 0, 12); f.Children.Add(hint);

        add = UI.Btn("Tambah alarm", Save, true);
        cancel = UI.Btn("Batal", ResetForm); cancel.Visibility = Visibility.Collapsed;
        f.Children.Add(UI.Row(add, cancel, UI.Btn("Tes pop-up", TestTip)));

        // ---- daftar
        var head = UI.Label("Terjadwal", true, 12); head.Margin = new Thickness(2, 0, 0, 6);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listHost });

        Action onChanged = () => RenderList();
        RemindEngine.Changed += onChanged;
        Closed += (_, _) => RemindEngine.Changed -= onChanged;

        ResetForm(); RenderList();
    }

    // ---------- komponen ----------
    // Chip teks kecil (toggle hari / tombol cepat)
    static Border Chip(string text, double w, bool paint)
    {
        var tx = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tx.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var b = new Border { Width = w, Height = 28, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = tx };
        Chips.Style(b, false);
        b.MouseEnter += (_, _) => b.Opacity = .82; b.MouseLeave += (_, _) => b.Opacity = 1;
        return b;
    }

    // Spinner jam : menit. Panah atas/bawah (menit ±5), roda mouse (±1)
    FrameworkElement Spinner()
    {
        FrameworkElement Col(TextBlock num, Action<int> step, int arrowStep)
        {
            num.FontSize = 44; num.FontWeight = FontWeights.Bold; num.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            num.Foreground = Pal.CV(0, true); num.HorizontalAlignment = HorizontalAlignment.Center; num.TextAlignment = TextAlignment.Center; num.MinWidth = 64;
            Border Arrow(string d, int dir)
            {
                var gl = Icons.Glyph(d, 13, Pal.Br(Pal.Dim), 2.2);
                var b = new Border { Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = gl, ToolTip = "Klik: ±" + arrowStep + " · Roda mouse: ±1" };
                if (gl is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
                Icons.Tint(b, gl, Pal.Dim);
                b.MouseLeftButtonUp += (_, _) => step(dir * arrowStep);
                return b;
            }
            var sp = new StackPanel { Background = Brushes.Transparent };
            sp.Children.Add(Arrow("M6,15 L12,9 L18,15", 1)); sp.Children.Add(num); sp.Children.Add(Arrow("M6,9 L12,15 L18,9", -1));
            sp.MouseWheel += (_, e) => { step(Math.Sign(e.Delta)); e.Handled = true; };
            return sp;
        }
        var colon = new TextBlock { Text = ":", FontSize = 36, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 4) };
        colon.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Col(hh, d => { hour = ((hour + d) % 24 + 24) % 24; ShowTime(); }, 1));
        row.Children.Add(colon);
        row.Children.Add(Col(mm, d =>
        {
            if (Math.Abs(d) >= 5) { int s = d > 0 ? 5 : -5; minute = (((int)Math.Round(minute / 5.0) * 5 + s) % 60 + 60) % 60; }   // panah: loncat ke kelipatan 5
            else minute = ((minute + d) % 60 + 60) % 60;
            ShowTime();
        }, 5));
        return row;
    }

    void ShowTime() { hh.Text = hour.ToString("00"); mm.Text = minute.ToString("00"); }

    void PaintDays()
    {
        for (int i = 0; i < 7; i++)
        {
            bool on = (days & (1 << i)) != 0; Chips.Style(dayChips[i], on);
            dayTx[i].SetResourceReference(TextBlock.ForegroundProperty, on ? "Cyan" : "Ink");
        }
    }

    static void SetSeg(Border seg, int idx)
    {
        if (seg.Child is StackPanel sp && idx >= 0 && idx < sp.Children.Count && sp.Children[idx] is RadioButton rb) rb.IsChecked = true;
    }

    void UpdateAdd() => add.Content = editId != null ? "Simpan perubahan" : alarm ? "Tambah alarm" : "Tambah pengingat";

    // ---------- form ----------
    void ResetForm()
    {
        editId = null; title.Clear();
        var t = DateTime.Now.AddMinutes(30); hour = t.Hour; minute = t.Minute - t.Minute % 5; days = 0;
        SetSeg(kindSeg, 0); SetSeg(snoozeSeg, 0); alarm = true; snooze = 5;
        cancel.Visibility = Visibility.Collapsed;
        ShowTime(); PaintDays(); UpdateAdd();
    }

    void Edit(ReminderItem r)
    {
        editId = r.Id; title.Text = r.Title; hour = r.Hour; minute = r.Minute; days = r.Days;
        SetSeg(kindSeg, r.Alarm ? 0 : 1); SetSeg(snoozeSeg, Math.Max(0, Array.IndexOf(SnoozeOpts, r.Snooze)));
        alarm = r.Alarm; snooze = r.Snooze;
        cancel.Visibility = Visibility.Visible;
        ShowTime(); PaintDays(); UpdateAdd();
    }

    void Save()
    {
        var t = title.Text.Trim(); if (t == "") t = alarm ? "Alarm" : "Pengingat";
        ReminderItem r;
        if (editId != null && cfg.Reminders.FirstOrDefault(x => x.Id == editId) is { } ex) r = ex;
        else { r = new ReminderItem(); cfg.Reminders.Add(r); }
        r.Title = t; r.Alarm = alarm; r.Hour = hour; r.Minute = minute; r.Days = days; r.Snooze = snooze; r.Enabled = true;
        r.Date = days == 0 ? RemindEngine.NextDate(hour, minute) : "";
        RemindEngine.Cancel(r.Id);
        cfg.Save(); NotchHub.Ping();
        Toast.Show($"{(alarm ? "Alarm" : "Pengingat")} {hour:00}:{minute:00} · {RemindEngine.Repeat(r)}");
        ResetForm(); RenderList();
    }

    void TestTip() => ReminderTip.Notify(new ReminderItem { Title = title.Text.Trim() == "" ? (alarm ? "Tes alarm" : "Tes pengingat") : title.Text.Trim(), Alarm = alarm, Hour = hour, Minute = minute, Snooze = snooze }, false, true);

    // ---------- daftar ----------
    void RenderList()
    {
        listHost.Children.Clear();
        if (cfg.Reminders.Count == 0)
        {
            var e = UI.Label("Belum ada alarm atau pengingat.\nAtur waktu di atas lalu klik Tambah.", true, 12.5);
            e.TextAlignment = TextAlignment.Center; e.Margin = new Thickness(0, 28, 0, 0); listHost.Children.Add(e); return;
        }
        foreach (var r in cfg.Reminders.OrderBy(x => x.Hour * 60 + x.Minute).ToList())
        {
            var it = r;
            var tint = it.Alarm ? Color.FromRgb(0xFA, 0xCC, 0x15) : Pal.Cyan;
            var gl = Icons.Glyph(Icons.Bell, 18, Pal.Br(tint), 1.6);
            if (gl is FrameworkElement ge) { ge.HorizontalAlignment = HorizontalAlignment.Center; ge.VerticalAlignment = VerticalAlignment.Center; }
            var tile = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x24, tint.R, tint.G, tint.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, tint.R, tint.G, tint.B)), BorderThickness = new Thickness(1), Child = gl
            };

            var time = new TextBlock { Text = $"{it.Hour:00}:{it.Minute:00}", FontSize = 20, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") };
            if (it.Enabled) time.Foreground = Pal.CV(0, true); else time.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
            var name = new TextBlock { Text = it.Title, FontSize = 12.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = UI.Label($"{(it.Alarm ? "Alarm" : "Pengingat")} · {RemindEngine.Repeat(it)} · tunda {it.Snooze} mnt", true, 11); sub.Margin = new Thickness(0); sub.TextTrimming = TextTrimming.CharacterEllipsis;
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            time.Margin = new Thickness(0, 0, 10, 0); time.VerticalAlignment = VerticalAlignment.Center; name.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(time); line.Children.Add(name);
            tx.Children.Add(line); tx.Children.Add(sub);

            var tg = UI.Toggle("", it.Enabled, v =>
            {
                it.Enabled = v; RemindEngine.Cancel(it.Id);
                if (v && it.Days == 0) it.Date = RemindEngine.NextDate(it.Hour, it.Minute);
                cfg.Save(); NotchHub.Ping(); RenderList();
            });
            tg.Margin = new Thickness(0); tg.VerticalAlignment = VerticalAlignment.Center;
            if (tg is DockPanel dp) dp.Margin = new Thickness(0, 0, 4, 0);
            var tgw = new Border { Background = Brushes.Transparent, Child = tg, VerticalAlignment = VerticalAlignment.Center };
            tgw.MouseLeftButtonDown += (_, e) => e.Handled = true;
            tgw.MouseLeftButtonUp += (_, e) => e.Handled = true;   // klik toggle jangan ikut membuka mode edit

            var xg = Icons.Glyph("M6,6 L18,18 M18,6 L6,18", 11, Pal.Br(Pal.Dim), 2);
            if (xg is FrameworkElement xe) { xe.HorizontalAlignment = HorizontalAlignment.Center; xe.VerticalAlignment = VerticalAlignment.Center; }
            var del = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Hapus", Child = xg };
            del.MouseEnter += (_, _) => del.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            del.MouseLeave += (_, _) => del.Background = Brushes.Transparent;
            del.MouseLeftButtonDown += (_, e) => e.Handled = true;
            del.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true; RemindEngine.Cancel(it.Id); cfg.Reminders.Remove(it); cfg.Save(); NotchHub.Ping();
                if (editId == it.Id) ResetForm();
                RenderList();
            };

            var gr = new Grid();
            gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); gr.ColumnDefinitions.Add(new ColumnDefinition());
            gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(tx, 1); Grid.SetColumn(tgw, 2); Grid.SetColumn(del, 3);
            gr.Children.Add(tile); gr.Children.Add(tx); gr.Children.Add(tgw); gr.Children.Add(del);

            var row = new Border { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 8, 9), Margin = new Thickness(0, 0, 0, 7), Cursor = Cursors.Hand, Child = gr, Opacity = it.Enabled ? 1 : .55 };
            row.SetResourceReference(Border.BackgroundProperty, "Fill"); row.SetResourceReference(Border.BorderBrushProperty, "Line");
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "FillHover");
            row.MouseLeave += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Fill");
            row.MouseLeftButtonUp += (_, _) => Edit(it);
            listHost.Children.Add(row);
        }
    }
}
