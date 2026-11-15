using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp;
namespace MySecretary;

// ---------------- Folder Organizer ----------------
// Rapikan file di satu folder ke sub-folder: per jenis file, dan/atau per tahun / bulan / tanggal (toggle, dengan pratinjau sebelum jalan).
// Setelah rapikan, hasilnya ditampilkan: file pindah ke folder mana saja. Folder bisa diwarnai seperti stabilo:
// ikon folder diganti ikon berwarna lewat desktop.ini + file .ico tersembunyi di dalam folder itu (bisa diurungkan, tidak menyentuh folder yang sudah punya desktop.ini sendiri).
sealed record FItem(string Full, string Cat, DateTime Mod, DateTime Cre);
sealed record FMove(string Src, string Dst, string Rel);

sealed class FolderOrganizerPanel : AppPanel
{
    // ---------- kategori ----------
    static readonly Dictionary<string, string> Map = Build();
    static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>();
        void A(string cat, string exts) { foreach (var e in exts.Split(' ')) d["." + e] = cat; }
        A("Gambar", "jpg jpeg png gif bmp webp heic svg tif tiff ico raw psd");
        A("Dokumen", "pdf doc docx xls xlsx ppt pptx txt md rtf csv odt epub");
        A("Video", "mp4 mkv mov avi wmv webm flv m4v");
        A("Audio", "mp3 wav flac aac m4a ogg wma");
        A("Arsip", "zip rar 7z tar gz iso");
        A("Aplikasi", "exe msi apk bat");
        A("Kode", "cs js ts py java cpp c h html css json xml sql sh ps1");
        return d;
    }
    static readonly string[] Months = { "Januari", "Februari", "Maret", "April", "Mei", "Juni", "Juli", "Agustus", "September", "Oktober", "November", "Desember" };

    // ---------- warna stabilo ----------
    sealed record Hue(string Name, uint Rgb);
    static readonly Hue[] Palette =
    {
        new("Kuning", 0xFACC15), new("Hijau", 0x4ADE80), new("Pink", 0xF472B6), new("Oranye", 0xFB923C),
        new("Biru", 0x38BDF8), new("Ungu", 0xA78BFA), new("Merah", 0xF87171), new("Abu", 0x94A3B8)
    };
    static readonly Dictionary<string, int> DefaultHue = new()
    {
        ["Gambar"] = 2, ["Dokumen"] = 4, ["Video"] = 5, ["Audio"] = 3, ["Arsip"] = 0, ["Aplikasi"] = 6, ["Kode"] = 1, ["Lainnya"] = 7
    };
    const string IcoName = ".bestie-color.ico";
    static int DefaultOf(string key) => DefaultHue.TryGetValue(key, out var h) ? h : key.Sum(ch => (int)ch) % Palette.Length;
    static Color HueColor(int i) { var v = Palette[i].Rgb; return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v); }

    // ---------- state ----------
    readonly TextBox path = new() { Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Width = 360, Margin = new Thickness(0, 0, 8, 0) };
    readonly StackPanel listHost = new();
    readonly TextBlock status = UI.Label("Pilih folder lalu Pindai.", true);
    readonly List<FItem> scanned = new();
    readonly List<FMove> moved = new();
    readonly HashSet<string> created = new(StringComparer.OrdinalIgnoreCase);        // folder yang dibuat oleh rapikan (untuk urungkan)
    readonly Dictionary<string, HashSet<string>> groupDirs = new();                    // folder teratas -> semua folder tujuan di bawahnya
    readonly Dictionary<string, int> hue = new();                                      // folder teratas -> indeks warna (-1 = tanpa warna)
    readonly Button run, undo;
    string baseDir = "";
    bool byType = true, byYear, byMonth, byDay, paint = true, resultMode; int dateSrc;

    public FolderOrganizerPanel() : base("Folder Organizer", 580, 650)
    {
        var s = new StackPanel(); Main.Children.Add(s);
        s.Children.Add(UI.Label("Rapikan file di satu folder ke sub-folder berdasarkan jenis dan/atau tanggal. Shortcut dan file tersembunyi tidak disentuh.", true));
        s.Children.Add(UI.Row(path, UI.Btn("Pilih…", () => { var f = UI.Folder(); if (f != null) { path.Text = f; Scan(); } })));

        var tg = new UniformGrid { Columns = 2, Margin = new Thickness(0, 2, 0, 0) };
        tg.Children.Add(UI.Toggle("Pisah per jenis file", byType, v => { byType = v; Preview(); }));
        tg.Children.Add(UI.Toggle("Warnai folder (stabilo)", paint, v => { paint = v; Preview(); }));
        tg.Children.Add(UI.Toggle("Pisah per tahun", byYear, v => { byYear = v; Preview(); }));
        tg.Children.Add(UI.Toggle("Pisah per bulan", byMonth, v => { byMonth = v; Preview(); }));
        tg.Children.Add(UI.Toggle("Pisah per tanggal", byDay, v => { byDay = v; Preview(); }));
        s.Children.Add(tg);

        s.Children.Add(UI.Label("Tanggal yang dipakai untuk pengelompokan", true, 12));
        s.Children.Add(UI.Segmented(new[] { "Terakhir diubah", "Tanggal dibuat" }, 0, i => { dateSrc = i; Preview(); }));

        s.Children.Add(new ScrollViewer { Height = 215, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listHost, Margin = new Thickness(0, 0, 0, 10) });
        run = UI.Btn("Rapikan sekarang", Run, true); run.IsEnabled = false;
        undo = UI.Btn("Urungkan", Undo); undo.IsEnabled = false;
        s.Children.Add(UI.Row(UI.Btn("Pindai", Scan), run, undo));
        s.Children.Add(status);
        listHost.Children.Add(Note("Pilih folder, atur pengelompokan, lalu klik Pindai."));
    }

    // ---------- nama folder tujuan ----------
    string Rel(FItem it)
    {
        var d = dateSrc == 0 ? it.Mod : it.Cre;
        var parts = new List<string>();
        if (byType) parts.Add(it.Cat);
        if (byYear) parts.Add(d.Year.ToString());
        if (byMonth) parts.Add(byYear ? $"{d.Month:00} {Months[d.Month - 1]}" : $"{d.Year}-{d.Month:00} {Months[d.Month - 1]}");
        if (byDay) parts.Add(byMonth ? $"{d.Day:00}" : byYear ? $"{d.Month:00}-{d.Day:00}" : $"{d.Year}-{d.Month:00}-{d.Day:00}");
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }
    static string TopOf(string rel) => rel.Split(Path.DirectorySeparatorChar)[0];
    static string Pretty(string rel) => rel.Replace(Path.DirectorySeparatorChar.ToString(), "  ›  ");
    bool AnyGroup => byType || byYear || byMonth || byDay;

    // ---------- pindai & pratinjau ----------
    void Scan()
    {
        resultMode = false;
        var dir = path.Text.Trim();
        if (!Directory.Exists(dir)) { status.Text = "Folder tidak ditemukan."; return; }
        scanned.Clear();
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var fi = new FileInfo(f); var ext = fi.Extension.ToLowerInvariant();
                if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || ext is ".lnk" or ".url" or ".ini") continue;
                scanned.Add(new FItem(f, Map.TryGetValue(ext, out var c) ? c : "Lainnya", fi.LastWriteTime, fi.CreationTime));
            }
        }
        catch (Exception ex) { status.Text = "Gagal memindai: " + ex.Message; return; }
        baseDir = dir;
        Preview();
    }

    void Preview()
    {
        if (resultMode) return;
        listHost.Children.Clear();
        if (scanned.Count == 0)
        {
            run.IsEnabled = false;
            listHost.Children.Add(Note(baseDir == "" ? "Pilih folder, atur pengelompokan, lalu klik Pindai." : "Tidak ada file untuk dirapikan."));
            if (baseDir != "") status.Text = "Tidak ada file untuk dirapikan.";
            return;
        }
        if (!AnyGroup)
        {
            run.IsEnabled = false; status.Text = "Aktifkan minimal satu pengelompokan (jenis, tahun, bulan, atau tanggal).";
            listHost.Children.Add(Note("Belum ada pengelompokan yang aktif."));
            return;
        }
        run.IsEnabled = true;
        listHost.Children.Add(Cap("Pratinjau — file akan dipindah ke folder ini:"));
        var groups = scanned.GroupBy(it => Rel(it)).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var g in groups) listHost.Children.Add(DirRow(Pretty(g.Key), $"{g.Count()} file", paint ? DefaultOf(TopOf(g.Key)) : -1, null, null, true));
        status.Text = $"{scanned.Count} file siap dirapikan ke {groups.Count} folder.";
    }

    // ---------- rapikan ----------
    string EnsureDir(string rel)
    {
        var cur = baseDir;
        foreach (var seg in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Path.Combine(cur, seg);
            if (!Directory.Exists(cur)) { Directory.CreateDirectory(cur); created.Add(cur); }
        }
        return cur;
    }

    void Run()
    {
        if (scanned.Count == 0 || !AnyGroup) return;
        moved.Clear(); created.Clear(); groupDirs.Clear(); hue.Clear(); int fail = 0;
        foreach (var it in scanned)
        {
            try
            {
                var rel = Rel(it);
                var dir = EnsureDir(rel);
                var to = Unique(Path.Combine(dir, Path.GetFileName(it.Full)));
                File.Move(it.Full, to); moved.Add(new FMove(it.Full, to, rel));
            }
            catch { fail++; }
        }

        // semua folder tujuan (dari folder teratas sampai daun), dikelompokkan per folder teratas
        foreach (var rel in moved.Select(m => m.Rel).Distinct())
        {
            var key = TopOf(rel); var cur = baseDir;
            if (!groupDirs.TryGetValue(key, out var hs)) { hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase); groupDirs[key] = hs; }
            foreach (var seg in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) { cur = Path.Combine(cur, seg); hs.Add(cur); }
        }
        foreach (var kv in groupDirs)
        {
            int idx = paint ? DefaultOf(kv.Key) : -1; hue[kv.Key] = idx;
            if (idx >= 0) foreach (var d in kv.Value) Paint(d, idx);
        }
        if (paint) Refresh();

        resultMode = true; scanned.Clear(); run.IsEnabled = false; undo.IsEnabled = moved.Count > 0;
        ShowResult();
        status.Text = $"{moved.Count} file dirapikan ke {moved.Select(m => m.Rel).Distinct().Count()} folder" + (fail > 0 ? $", {fail} gagal" : "") + ".";
    }

    // ---------- hasil ----------
    void ShowResult()
    {
        listHost.Children.Clear();
        listHost.Children.Add(Cap("Dirapikan di: " + baseDir));
        listHost.Children.Add(Cap("Klik titik warna di kanan nama folder untuk mengganti warnanya. Klik dua kali baris untuk membuka foldernya."));
        foreach (var g in moved.GroupBy(m => TopOf(m.Rel)).OrderByDescending(grp => grp.Count()))
        {
            var key = g.Key; int hi = hue.TryGetValue(key, out var h) ? h : -1;
            var head = new DockPanel { Margin = new Thickness(0, 10, 8, 2), LastChildFill = true };
            var sw = Swatches(key, hi); DockPanel.SetDock(sw, Dock.Right); head.Children.Add(sw);
            var dot = Dot(hi, 12); dot.Margin = new Thickness(0, 0, 8, 0); dot.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(dot, Dock.Left); head.Children.Add(dot);
            head.Children.Add(new TextBlock { Text = $"{key}  ·  {g.Count()} file", FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            listHost.Children.Add(head);
            foreach (var d in g.GroupBy(m => m.Rel).OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var names = d.Select(m => Path.GetFileName(m.Dst)).ToList();
                var sub = string.Join(", ", names.Take(3)) + (names.Count > 3 ? $" +{names.Count - 3} lainnya" : "");
                listHost.Children.Add(DirRow(Pretty(d.Key), $"{d.Count()} file", -1, sub, Path.Combine(baseDir, d.Key), false));
            }
        }
    }

    FrameworkElement Swatches(string key, int selected)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Palette.Length; i++)
        {
            int k = i;
            var e = Dot(i, 14); e.Margin = new Thickness(2, 0, 2, 0); e.Cursor = Cursors.Hand; e.ToolTip = Palette[i].Name;
            e.StrokeThickness = 2;
            if (i == selected) e.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink"); else e.Stroke = Brushes.Transparent;
            e.MouseLeftButtonUp += (_, _) => Recolor(key, k);
            sp.Children.Add(e);
        }
        var none = new TextBlock { Text = "✕", FontSize = 11, Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Tanpa warna", VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        none.SetResourceReference(TextBlock.ForegroundProperty, selected < 0 ? "Ink" : "Dim");
        none.MouseLeftButtonUp += (_, _) => Recolor(key, -1);
        sp.Children.Add(none);
        return sp;
    }

    void Recolor(string key, int idx)
    {
        hue[key] = idx;
        if (groupDirs.TryGetValue(key, out var dirs)) foreach (var d in dirs) { if (idx >= 0) Paint(d, idx); else Unpaint(d); }
        Refresh(); ShowResult();
    }

    // ---------- urungkan ----------
    void Undo()
    {
        int n = 0;
        foreach (var m in Enumerable.Reverse(moved))
        {
            try { if (File.Exists(m.Dst)) { File.Move(m.Dst, Unique(m.Src)); n++; } } catch { }
        }
        foreach (var d in groupDirs.Values.SelectMany(x => x)) Unpaint(d);
        foreach (var d in created.OrderByDescending(x => x.Length))
        {
            try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
        Refresh();
        moved.Clear(); created.Clear(); groupDirs.Clear(); hue.Clear(); undo.IsEnabled = false;
        Scan();
        status.Text = $"{n} file dikembalikan. " + status.Text;
    }

    // ---------- komponen tampilan ----------
    static System.Windows.Shapes.Ellipse Dot(int hueIdx, double size)
    {
        var e = new System.Windows.Shapes.Ellipse { Width = size, Height = size };
        if (hueIdx >= 0) e.Fill = new SolidColorBrush(HueColor(hueIdx)); else e.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Track");
        return e;
    }

    static TextBlock Cap(string t) { var l = UI.Label(t, true, 12); l.Margin = new Thickness(2, 2, 8, 2); return l; }
    static TextBlock Note(string t) { var l = UI.Label(t, true); l.Margin = new Thickness(4, 14, 4, 0); l.TextAlignment = TextAlignment.Center; return l; }

    FrameworkElement DirRow(string title, string right, int hueIdx, string? sub, string? openDir, bool showDot)
    {
        var dp = new DockPanel { Margin = new Thickness(2, 3, 8, 3), Background = Brushes.Transparent, LastChildFill = true };
        if (showDot) { var dot = Dot(hueIdx, 10); dot.Margin = new Thickness(0, 0, 8, 0); dot.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(dot, Dock.Left); dp.Children.Add(dot); }
        else dp.Margin = new Thickness(18, 3, 8, 3);
        var cnt = new TextBlock { Text = right, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        cnt.SetResourceReference(TextBlock.ForegroundProperty, "Dim"); DockPanel.SetDock(cnt, Dock.Right); dp.Children.Add(cnt);
        var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tx.Children.Add(new TextBlock { Text = title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
        if (sub != null)
        {
            var st = new TextBlock { Text = sub, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            st.SetResourceReference(TextBlock.ForegroundProperty, "Dim"); tx.Children.Add(st);
        }
        dp.Children.Add(tx);
        if (openDir != null)
        {
            dp.Cursor = Cursors.Hand; dp.ToolTip = openDir;
            dp.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) OpenDir(openDir); };
        }
        return dp;
    }

    static void OpenDir(string dir)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
    }

    static string Unique(string p)
    {
        if (!File.Exists(p)) return p;
        var d = Path.GetDirectoryName(p)!; var n = Path.GetFileNameWithoutExtension(p); var e = Path.GetExtension(p); int i = 2; string q;
        do q = Path.Combine(d, $"{n} ({i++}){e}"); while (File.Exists(q));
        return q;
    }

    // ---------- warna folder (ikon folder berwarna lewat desktop.ini) ----------
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    static void Refresh() { try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { } }   // SHCNE_ASSOCCHANGED: Explorer memuat ulang ikon

    static bool Paint(string dir, int idx)
    {
        try
        {
            var ini = Path.Combine(dir, "desktop.ini"); var ico = Path.Combine(dir, IcoName);
            if (File.Exists(ini) && !File.Exists(ico)) return false;            // folder ini sudah punya pengaturan sendiri: jangan ditimpa
            foreach (var f in new[] { ini, ico }) if (File.Exists(f)) File.SetAttributes(f, FileAttributes.Normal);
            File.WriteAllBytes(ico, IcoFor(idx));
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + IcoName + ",0\r\nIconFile=" + IcoName + "\r\nIconIndex=0\r\n", Encoding.Unicode);
            File.SetAttributes(ico, FileAttributes.Hidden | FileAttributes.System);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var di = new DirectoryInfo(dir); di.Attributes |= FileAttributes.ReadOnly;   // syarat Explorer membaca desktop.ini
            return true;
        }
        catch { return false; }
    }

    static void Unpaint(string dir)
    {
        try
        {
            var ico = Path.Combine(dir, IcoName); var ini = Path.Combine(dir, "desktop.ini");
            if (!File.Exists(ico)) return;                                       // bukan warna buatan kita
            foreach (var f in new[] { ini, ico }) if (File.Exists(f)) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            var di = new DirectoryInfo(dir); di.Attributes &= ~FileAttributes.ReadOnly;
        }
        catch { }
    }

    static readonly Dictionary<int, byte[]> icoCache = new();
    static byte[] IcoFor(int idx)
    {
        lock (icoCache)
        {
            if (icoCache.TryGetValue(idx, out var b)) return b;
            int[] sizes = { 256, 48, 32, 16 };
            var pngs = sizes.Select(s => FolderPng(s, Palette[idx].Rgb)).ToList();
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte sz = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(sz); w.Write(sz); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32); w.Write(pngs[i].Length); w.Write(off);
                off += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
            w.Flush();
            return icoCache[idx] = ms.ToArray();
        }
    }

    static SKColor Shade(SKColor c, float f) => new SKColor((byte)Math.Min(255f, c.Red * f), (byte)Math.Min(255f, c.Green * f), (byte)Math.Min(255f, c.Blue * f));

    // Ikon folder datar bersudut halus: badan dengan gradasi (seperti stabilo), tab dan punggung lebih gelap.
    static byte[] FolderPng(int s, uint rgb)
    {
        using var bmp = new SKBitmap(s, s);
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        float k = s / 256f;
        var baseC = new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        using var back = new SKPaint { IsAntialias = true, Color = Shade(baseC, .72f) };
        c.DrawRoundRect(new SKRect(20 * k, 46 * k, 236 * k, 212 * k), 20 * k, 20 * k, back);
        c.DrawRoundRect(new SKRect(20 * k, 30 * k, 124 * k, 92 * k), 14 * k, 14 * k, back);
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 70 * k), new SKPoint(0, 228 * k), new[] { Shade(baseC, 1.15f), baseC }, null, SKShaderTileMode.Clamp);
        using var front = new SKPaint { IsAntialias = true, Shader = shader };
        c.DrawRoundRect(new SKRect(12 * k, 72 * k, 244 * k, 226 * k), 20 * k, 20 * k, front);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
