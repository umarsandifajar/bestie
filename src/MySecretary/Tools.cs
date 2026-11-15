using System.ComponentModel;
using System.Globalization;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Run = System.Windows.Documents.Run;
namespace MySecretary;

// ---------------- File Size Checker ----------------
sealed record SizeRow(string Name, string SizeText, double Pct, string FullPath);
sealed class SizePanel : AppPanel
{
    readonly TextBox path = new() { Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Width = 300, Margin = new Thickness(0, 0, 8, 0) };
    readonly ListBox list = new() { Height = 270 };
    readonly TextBlock status = UI.Label("Pilih folder lalu Pindai. Klik dua kali untuk membuka lokasinya.", true);
    int mode;
    public SizePanel() : base("File Size Checker", 540, 480)
    {
        var s = new StackPanel(); Main.Children.Add(s);
        s.Children.Add(UI.Row(path, UI.Btn("Pilih…", () => { var f = UI.Folder(); if (f != null) path.Text = f; }), UI.Btn("Pindai", Scan, true)));
        s.Children.Add(UI.Segmented(new[] { "Isi folder", "File terbesar" }, 0, i => mode = i));
        list.ItemTemplate = UI.Tpl("<Grid Margin='0,2'><Grid.RowDefinitions><RowDefinition/><RowDefinition/></Grid.RowDefinitions><DockPanel><TextBlock DockPanel.Dock='Right' Text='{Binding SizeText}' Foreground='{DynamicResource Dim}'/><TextBlock Text='{Binding Name}' TextTrimming='CharacterEllipsis' Margin='0,0,10,0'/></DockPanel><ProgressBar Grid.Row='1' Maximum='100' Value='{Binding Pct,Mode=OneWay}' Margin='0,4,0,0'/></Grid>");
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is SizeRow r) UI.Reveal(r.FullPath); };
        s.Children.Add(list); s.Children.Add(status);
    }
    async void Scan()
    {
        var dir = path.Text; if (!Directory.Exists(dir)) { status.Text = "Folder tidak ditemukan."; return; }
        status.Text = "Memindai…"; list.ItemsSource = null; bool largest = mode == 1;
        var (rows, total, count) = await Task.Run(() => Compute(dir, largest));
        list.ItemsSource = rows; status.Text = $"Total {UI.Size(total)} · {count} file";
    }
    static (List<SizeRow>, long, int) Compute(string dir, bool largest)
    {
        var deep = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        long total = 0; int count = 0; var items = new List<(string p, long s)>();
        if (largest)
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", deep)) { try { var l = new FileInfo(f).Length; total += l; count++; items.Add((f, l)); } catch { } }
        }
        else
        {
            foreach (var e in Directory.EnumerateFileSystemEntries(dir, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                long sz = 0;
                try
                {
                    if (Directory.Exists(e)) { foreach (var f in Directory.EnumerateFiles(e, "*", deep)) { try { sz += new FileInfo(f).Length; count++; } catch { } } }
                    else { sz = new FileInfo(e).Length; count++; }
                }
                catch { }
                total += sz; items.Add((e, sz));
            }
        }
        var top = items.OrderByDescending(i => i.s).Take(40).ToList(); double max = top.Count > 0 ? Math.Max(1, top[0].s) : 1;
        return (top.Select(i => new SizeRow(largest ? Path.GetFileName(i.p) + "   —   " + Path.GetDirectoryName(i.p) : Path.GetFileName(i.p), UI.Size(i.s), i.s / max * 100, i.p)).ToList(), total, count);
    }
}

// ---------------- Text Formatter ----------------
sealed class TextPanel : AppPanel
{
    static string[] Ln(string s) => Regex.Split(s, "\r?\n");
    static string Jn(IEnumerable<string> l) => string.Join("\r\n", l);
    static readonly (string, Func<string, string>)[] Ops =
    {
        ("UPPERCASE", s => s.ToUpperInvariant()),
        ("lowercase", s => s.ToLowerInvariant()),
        ("Title Case", s => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLower())),
        ("Rapikan spasi", s => Jn(Ln(s).Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim()))),
        ("Hapus baris kosong", s => Jn(Ln(s).Where(l => !string.IsNullOrWhiteSpace(l)))),
        ("Hapus duplikat", s => Jn(Ln(s).Distinct())),
        ("Urutkan A–Z", s => Jn(Ln(s).OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase))),
        ("Balik urutan", s => Jn(Ln(s).AsEnumerable().Reverse())),
        ("slug-kebab", s => Regex.Replace(s.ToLowerInvariant().Trim(), "[^a-z0-9]+", "-").Trim('-')),
        ("camelCase", s => { var w = Regex.Split(s.Trim(), "[^A-Za-z0-9]+").Where(x => x != "").ToArray(); return w.Length == 0 ? "" : w[0].ToLowerInvariant() + string.Concat(w.Skip(1).Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1).ToLowerInvariant())); }),
        ("JSON rapi", s => { try { return JsonSerializer.Serialize(JsonDocument.Parse(s).RootElement, new JsonSerializerOptions { WriteIndented = true }); } catch { Toast.Show("Bukan JSON yang valid"); return s; } }),
    };
    readonly TextBox box = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190, VerticalContentAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 10) };
    public TextPanel() : base("Text Formatter", 520, 440)
    {
        var s = new StackPanel(); Main.Children.Add(s); s.Children.Add(box);
        var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) }; s.Children.Add(w);
        foreach (var (n, f) in Ops) { var b = UI.Btn(n, () => box.Text = f(box.Text)); b.Margin = new Thickness(0, 0, 6, 6); w.Children.Add(b); }
        s.Children.Add(UI.Row(UI.Btn("Salin hasil", () => { try { Clipboard.SetText(box.Text); Toast.Show("Teks disalin"); } catch { } }, true), UI.Btn("Kosongkan", () => box.Clear())));
        var info = UI.Label("0 karakter", true, 12); s.Children.Add(info);
        box.TextChanged += (_, _) => info.Text = $"{box.Text.Length} karakter · {Regex.Matches(box.Text, @"\S+").Count} kata · {box.LineCount} baris";
    }
}

// ---------------- Notes (dulu: Catatan cepat) ----------------
sealed class NotePanel : AppPanel
{
    static string NoteFile => Path.Combine(Config.Dir, "note.txt");
    public NotePanel() : base("Notes", 300, 340, true)
    {
        var wa = SystemParameters.WorkArea; WindowStartupLocation = WindowStartupLocation.Manual; Left = wa.Right - Width - 16; Top = wa.Top + 60;
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Top };
        try { if (File.Exists(NoteFile)) box.Text = File.ReadAllText(NoteFile); } catch { }
        void Save() { try { Directory.CreateDirectory(Config.Dir); File.WriteAllText(NoteFile, box.Text); } catch { } }
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; t.Tick += (_, _) => { t.Stop(); Save(); };
        box.TextChanged += (_, _) => { t.Stop(); t.Start(); }; Closed += (_, _) => Save();
        Main.Children.Add(box);
    }
}

// ---------------- Floating Timer ----------------
// Menit besar tebal cyan, detik lebih kecil, dan garis timer cyan -> violet yang menyala dan memendek sesuai sisa waktu.
sealed class TimerPanel : AppPanel
{
    readonly Run mm = new() { FontSize = 58, FontWeight = FontWeights.Bold };
    readonly Run sep = new("\u2009:\u2009") { FontSize = 24 };
    readonly Run sc = new() { FontSize = 26, FontWeight = FontWeights.SemiBold };
    readonly ScaleTransform barScale = new(1, 1);
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly List<(int min, Border b, TextBlock tx)> chips = new();
    readonly Button go; readonly Config cfg;
    int total; double rem; DateTime end; bool running;

    public TimerPanel(Config c) : base("Timer", 300, 285, true)
    {
        cfg = c;
        var wa = SystemParameters.WorkArea; WindowStartupLocation = WindowStartupLocation.Manual; Left = (wa.Width - Width) / 2; Top = wa.Top + 16;
        var s = new StackPanel(); Main.Children.Add(s);

        mm.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Cyan");
        sep.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Dim");
        sc.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");
        var time = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), Margin = new Thickness(0, 0, 0, 2) };
        time.Inlines.Add(mm); time.Inlines.Add(sep); time.Inlines.Add(sc);
        s.Children.Add(time);

        // garis timer
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(6, 6, 6, 20) };
        track.SetResourceReference(Border.BackgroundProperty, "Track");
        track.Child = new Border
        {
            CornerRadius = new CornerRadius(3), Background = Pal.CV(0), RenderTransformOrigin = new Point(0, .5), RenderTransform = barScale,
            Effect = new DropShadowEffect { Color = Pal.Cyan, BlurRadius = 10, ShadowDepth = 0, Opacity = .65 }
        };
        s.Children.Add(track);

        // pilihan durasi (diatur di Pengaturan)
        var pre = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var m in cfg.TimerList()) AddChip(pre, m);
        s.Children.Add(pre);

        go = UI.Btn("Mulai", Toggle, true);
        var row = UI.Row(go, UI.Btn("Reset", () => Set(Math.Max(1, total / 60)))); row.HorizontalAlignment = HorizontalAlignment.Center; s.Children.Add(row);

        total = Math.Clamp(cfg.TimerMin, 1, 600) * 60; rem = total;
        Mark(); Render();
        t.Tick += (_, _) => Tick(); Closed += (_, _) => t.Stop();
    }

    void AddChip(WrapPanel p, int m)
    {
        var tx = new TextBlock { Text = m + "m", FontSize = 12, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
        var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(3), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = tx };
        b.MouseEnter += (_, _) => b.Opacity = .82; b.MouseLeave += (_, _) => b.Opacity = 1;
        b.MouseLeftButtonUp += (_, _) => Set(m);
        chips.Add((m, b, tx)); p.Children.Add(b);
    }

    // tandai chip yang sesuai durasi terpilih
    void Mark()
    {
        foreach (var (min, b, tx) in chips)
        {
            if (total == min * 60)
            {
                b.Background = new LinearGradientBrush(Pal.Alpha(Pal.Cyan, 0x2A), Pal.Alpha(Pal.Violet, 0x2A), 0); b.BorderBrush = Pal.CV(0);
                tx.SetResourceReference(TextBlock.ForegroundProperty, "Cyan");
            }
            else
            {
                b.SetResourceReference(Border.BackgroundProperty, "Fill"); b.SetResourceReference(Border.BorderBrushProperty, "Line");
                tx.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            }
        }
    }

    // garis timer: penuh = sisa waktu penuh. run = berjalan mulus sampai nol selama sisa waktu; ease = isi ulang halus.
    void Bar(bool run, bool ease)
    {
        double cur = barScale.ScaleX;
        barScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        double f = total > 0 ? Math.Clamp(rem / total, 0, 1) : 0;
        barScale.ScaleX = f;
        if (run && f > 0) barScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(f, 0, TimeSpan.FromSeconds(rem)));
        else if (ease) barScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(cur, f, TimeSpan.FromMilliseconds(420)) { EasingFunction = Fx.EzOut });
    }

    void Set(int min)
    {
        total = min * 60; rem = total; running = false; t.Stop(); go.Content = "Mulai";
        cfg.TimerMin = min; cfg.Save();
        Mark(); Bar(false, true); Render();
    }
    void Toggle()
    {
        if (running) { rem = Math.Max(0, (end - DateTime.Now).TotalSeconds); running = false; t.Stop(); go.Content = "Lanjut"; Bar(false, false); }
        else { if (rem <= 0) rem = total; end = DateTime.Now.AddSeconds(rem); running = true; t.Start(); go.Content = "Jeda"; Bar(true, false); }
        Render();
    }
    void Tick()
    {
        rem = (end - DateTime.Now).TotalSeconds;
        if (rem <= 0) { rem = 0; running = false; t.Stop(); go.Content = "Mulai"; Bar(false, false); SystemSounds.Asterisk.Play(); Toast.Show("Timer selesai"); }
        Render();
    }
    void Render() { int r = (int)Math.Ceiling(rem); mm.Text = (r / 60).ToString("00"); sc.Text = (r % 60).ToString("00"); }
}

// ---------------- Clipboard History ----------------
static class ClipStore
{
    public static readonly ObservableCollection<string> Items = new();
    public static bool Paused;
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr h);
    public static void Start(IntPtr h) => AddClipboardFormatListener(h);
    public static IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x031D && !Paused)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(t) && t.Length <= 20000) { Items.Remove(t); Items.Insert(0, t); while (Items.Count > 50) Items.RemoveAt(Items.Count - 1); }
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }
}
sealed class ClipPanel : AppPanel
{
    public ClipPanel() : base("Clipboard History", 460, 480)
    {
        var view = CollectionViewSource.GetDefaultView(ClipStore.Items);
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        search.TextChanged += (_, _) => { var q = search.Text.Trim(); view.Filter = q == "" ? null : o => ((string)o).Contains(q, StringComparison.OrdinalIgnoreCase); };
        var list = new ListBox { ItemsSource = view, ItemTemplate = UI.Tpl("<TextBlock Text='{Binding}' TextWrapping='Wrap' MaxHeight='38' TextTrimming='CharacterEllipsis'/>") };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is string t) { try { Clipboard.SetText(t); Toast.Show("Disalin ke clipboard"); } catch { } list.SelectedIndex = -1; } };
        var pause = UI.Btn(ClipStore.Paused ? "Lanjutkan rekam" : "Jeda rekam", () => { }); 
        pause.Click += (_, _) => { ClipStore.Paused = !ClipStore.Paused; pause.Content = ClipStore.Paused ? "Lanjutkan rekam" : "Jeda rekam"; };
        var bottom = UI.Row(pause, UI.Btn("Bersihkan", () => ClipStore.Items.Clear()));
        bottom.Margin = new Thickness(0, 10, 0, 0);
        var dock = new DockPanel(); DockPanel.SetDock(search, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(search); dock.Children.Add(bottom); dock.Children.Add(list);
        Main.Children.Add(dock);
    }
}
