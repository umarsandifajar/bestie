using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using WinPdf = Windows.Data.Pdf;
namespace MySecretary;

// ---------------- Doc Converter ----------------
// Konverter dokumen: gambar (jpg/png/webp) -> PDF, DOC / DOCX / CSV -> PDF, gabung PDF, pisah PDF, kompres gambar & PDF.
// Dokumen -> PDF dikerjakan DocPdf.cs (Word / LibreOffice bila ada, kalau tidak pembaca .docx bawaan; CSV dirender sebagai tabel).
// Gambar: SkiaSharp (baca jpg/png/webp/bmp, EXIF diputar otomatis). PDF: PDFsharp (gabung / pisah / buat).
// Kompres PDF: tiap halaman dirender ulang (Windows.Data.Pdf) lalu disimpan sebagai gambar JPEG -> ukuran kecil, tetapi teks tidak bisa diseleksi lagi.
sealed record PageJob(int Mode, string[] Files, string OutDir, bool A4, int ImgQ, bool Ranged, string Ranges, int Level, int Fmt, int Orient);

sealed class DocConverterPanel : AppPanel
{
    const int MImg = 0, MDoc = 1, MMerge = 2, MSplit = 3, MComp = 4;   // urutan tab
    static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
    static readonly string[] DocExt = { ".doc", ".docx", ".csv" };
    static readonly string[] Tabs = { "Gambar → PDF", "Dokumen → PDF", "Gabung PDF", "Pisah PDF", "Kompres" };
    static readonly string[] Hints =
    {
        "Ubah satu atau beberapa gambar (JPG, PNG, WebP) menjadi satu file PDF. Urutan halaman mengikuti urutan daftar.",
        "Ubah dokumen DOC, DOCX, dan CSV menjadi PDF. Tiap file jadi satu PDF dengan nama yang sama. DOC/DOCX memakai Microsoft Word atau LibreOffice bila terpasang (hasil paling mirip aslinya); tanpa keduanya, DOCX tetap bisa tapi hanya teks, judul, dan tabel. DOC lama butuh Word atau LibreOffice.",
        "Gabungkan beberapa PDF menjadi satu. Atur urutannya dengan tombol ↑ ↓.",
        "Pisahkan satu PDF: tiap halaman jadi file sendiri, atau potong per rentang halaman.",
        "Perkecil ukuran gambar (JPG, PNG, WebP) dan PDF. Kompres PDF merender ulang halaman jadi gambar, jadi teks tidak bisa diseleksi lagi."
    };

    readonly List<string>[] lists = { new(), new(), new(), new(), new() };
    readonly StackPanel listHost = new();
    readonly StackPanel opts = new();
    readonly TextBlock hint = UI.Label("", true);
    readonly TextBlock outLbl = UI.Label("", true, 12);
    readonly TextBlock status = UI.Label("", true);
    readonly ProgressBar bar = new() { Margin = new Thickness(0, 0, 0, 8), Maximum = 1, Value = 0 };
    readonly TextBox rangeBox = new() { Text = "1-3, 5", Width = 200, Margin = new Thickness(0, 0, 0, 10) };
    readonly Button run, reveal;
    string outDir = ""; string? lastOut; bool busy;
    int mode, imgQ, level = 1, fmt, orient; bool a4, ranged;

    public DocConverterPanel() : base("Doc Converter", 580, 660)
    {
        AllowDrop = true; Drop += OnDrop;
        var s = new StackPanel(); Main.Children.Add(s);

        s.Children.Add(UI.Segmented(Tabs, 0, i => { mode = i; ModeChanged(); }));
        s.Children.Add(hint);
        s.Children.Add(UI.Row(UI.Btn("Tambah file…", Add), UI.Btn("Kosongkan", () => { lists[mode].Clear(); Render(); })));

        var sv = new ScrollViewer { Height = 176, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = listHost, Margin = new Thickness(0, 0, 0, 10) };
        s.Children.Add(sv);
        s.Children.Add(opts);

        s.Children.Add(outLbl);
        s.Children.Add(UI.Row(UI.Btn("Folder output…", () => { var f = UI.Folder(); if (f != null) { outDir = f; OutText(); } }), UI.Btn("Reset", () => { outDir = ""; OutText(); })));

        run = UI.Btn("Proses", Go, true); reveal = UI.Btn("Buka hasil", () => { if (lastOut != null) UI.Reveal(lastOut); }); reveal.IsEnabled = false;
        s.Children.Add(UI.Row(run, reveal));
        s.Children.Add(bar);
        s.Children.Add(status);
        ModeChanged();
    }

    // ---------- daftar file ----------
    bool Accept(string p)
    {
        var e = Path.GetExtension(p).ToLowerInvariant();
        return mode switch { MImg => ImgExt.Contains(e), MDoc => DocExt.Contains(e), MMerge or MSplit => e == ".pdf", _ => e == ".pdf" || ImgExt.Contains(e) };
    }

    void AddFiles(IEnumerable<string> paths)
    {
        var l = lists[mode]; int bad = 0;
        foreach (var p in paths)
        {
            if (!File.Exists(p) || !Accept(p)) { bad++; continue; }
            if (mode == MSplit) l.Clear();                              // pisah PDF: hanya satu file
            if (!l.Contains(p, StringComparer.OrdinalIgnoreCase)) l.Add(p);
            if (mode == MSplit) break;
        }
        Render();
        if (bad > 0) status.Text = $"{bad} file dilewati (jenis tidak sesuai untuk mode ini).";
    }

    void Add()
    {
        var f = mode switch
        {
            MImg => "Gambar|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
            MDoc => "Dokumen|*.doc;*.docx;*.csv",
            MMerge or MSplit => "PDF|*.pdf",
            _ => "Gambar & PDF|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.pdf"
        };
        var d = new OpenFileDialog { Multiselect = mode != MSplit, Filter = f };
        if (d.ShowDialog() == true) AddFiles(d.FileNames);
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] fs) return;
        AddFiles(fs.SelectMany(p => Directory.Exists(p) ? Directory.EnumerateFiles(p) : new[] { p }).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
    }

    Button Mini(string glyph, Action a)
    {
        var b = new Button { Content = glyph, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(3, 0, 0, 0), FontSize = 11, MinWidth = 26 };
        b.Click += (_, _) => a(); return b;
    }

    void Render()
    {
        listHost.Children.Clear();
        var l = lists[mode];
        if (l.Count == 0)
        {
            var t = UI.Label("Tarik & lepas file ke jendela ini, atau klik Tambah file…", true);
            t.Margin = new Thickness(4, 14, 4, 0); t.TextAlignment = TextAlignment.Center; listHost.Children.Add(t); return;
        }
        for (int i = 0; i < l.Count; i++)
        {
            int k = i; var p = l[k];
            var g = new Grid { Margin = new Thickness(2, 3, 2, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int c = 0; c < 3; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = UI.Label((k + 1).ToString(), true, 12); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(0);
            var name = new TextBlock { Text = Path.GetFileName(p), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
            var size = UI.Label(UI.Size(new FileInfo(p).Length), true, 11); size.Margin = new Thickness(0);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }; tx.Children.Add(name); tx.Children.Add(size);
            Grid.SetColumn(tx, 1);
            var up = Mini("↑", () => { if (k > 0) { (l[k - 1], l[k]) = (l[k], l[k - 1]); Render(); } }); Grid.SetColumn(up, 2);
            var dn = Mini("↓", () => { if (k < l.Count - 1) { (l[k + 1], l[k]) = (l[k], l[k + 1]); Render(); } }); Grid.SetColumn(dn, 3);
            var rm = Mini("✕", () => { l.RemoveAt(k); Render(); }); Grid.SetColumn(rm, 4);
            g.Children.Add(n); g.Children.Add(tx); g.Children.Add(up); g.Children.Add(dn); g.Children.Add(rm);
            listHost.Children.Add(g);
        }
    }

    // ---------- opsi per mode ----------
    void ModeChanged()
    {
        hint.Text = Hints[mode];
        opts.Children.Clear();
        switch (mode)
        {
            case MImg:
                opts.Children.Add(UI.Label("Ukuran halaman", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Sesuai gambar", "A4" }, a4 ? 1 : 0, i => a4 = i == 1));
                opts.Children.Add(UI.Label("Kualitas gambar", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Tinggi", "Hemat ukuran" }, imgQ, i => imgQ = i));
                break;
            case MDoc:
                opts.Children.Add(UI.Label("Orientasi halaman untuk CSV", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Otomatis", "Potret", "Lanskap" }, orient, i => orient = i));
                break;
            case MSplit:
                opts.Children.Add(UI.Label("Cara memisah", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Tiap halaman", "Per rentang" }, ranged ? 1 : 0, i => { ranged = i == 1; rangeBox.IsEnabled = ranged; }));
                rangeBox.IsEnabled = ranged;
                opts.Children.Add(UI.Label("Rentang (pisahkan koma, tiap rentang jadi satu file), mis. 1-3, 5, 8-", true, 12));
                if (rangeBox.Parent is Panel pp) pp.Children.Remove(rangeBox);
                rangeBox.HorizontalAlignment = HorizontalAlignment.Left;
                opts.Children.Add(rangeBox);
                break;
            case MComp:
                opts.Children.Add(UI.Label("Tingkat kompres", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Ringan", "Sedang", "Kuat" }, level, i => level = i));
                opts.Children.Add(UI.Label("Format hasil untuk gambar", true, 12));
                opts.Children.Add(UI.Segmented(new[] { "Sama", "JPG", "WebP" }, fmt, i => fmt = i));
                break;
        }
        OutText(); Render();
    }

    void OutText() => outLbl.Text = "Simpan ke: " + (outDir == "" ? "folder yang sama dengan file sumber" : outDir);

    // ---------- proses ----------
    async void Go()
    {
        if (busy) return;
        var l = lists[mode];
        if (l.Count == 0) { status.Text = "Tambahkan file dulu."; return; }
        if (mode == MMerge && l.Count < 2) { status.Text = "Pilih minimal 2 PDF untuk digabung."; return; }
        var dir = outDir != "" ? outDir : Path.GetDirectoryName(l[0])!;
        var job = new PageJob(mode, l.ToArray(), dir, a4, imgQ, ranged, rangeBox.Text, level, fmt, orient);
        busy = true; run.IsEnabled = false; reveal.IsEnabled = false; bar.Maximum = Math.Max(1, l.Count); bar.Value = 0; status.Text = "Memproses…";
        var prog = new Progress<(int n, string m)>(t => { bar.Value = Math.Min(bar.Maximum, t.n); status.Text = t.m; });
        try
        {
            var (msg, last) = await Task.Run(() => Work(job, prog));
            status.Text = msg; lastOut = last; reveal.IsEnabled = last != null;
        }
        catch (Exception ex) { status.Text = "Gagal: " + ex.Message; }
        finally { busy = false; run.IsEnabled = true; bar.Value = bar.Maximum; }
    }

    static string Bad(List<string> bad) => bad.Count == 0 ? "" : $" · {bad.Count} gagal ({string.Join(", ", bad.Take(3))}{(bad.Count > 3 ? "…" : "")})";

    static async Task<(string, string?)> Work(PageJob j, IProgress<(int, string)> pr)
    {
        Directory.CreateDirectory(j.OutDir);
        var bad = new List<string>(); string? last = null;
        switch (j.Mode)
        {
            case MImg: // gambar -> PDF
            {
                var baseName = j.Files.Length == 1 ? Path.GetFileNameWithoutExtension(j.Files[0]) : "gambar-gabungan";
                var outP = Unique(Path.Combine(j.OutDir, baseName + ".pdf"));
                int q = j.ImgQ == 0 ? 92 : 78, ok = 0;
                var keep = new List<MemoryStream>();
                using (var doc = new PdfDocument())
                {
                    for (int i = 0; i < j.Files.Length; i++)
                    {
                        var f = j.Files[i]; pr.Report((i, $"Memproses {Path.GetFileName(f)} ({i + 1}/{j.Files.Length})"));
                        try
                        {
                            using var raw = LoadBmp(f); using var flat = Flatten(raw);
                            var ms = new MemoryStream(Enc(flat, SKEncodedImageFormat.Jpeg, q)); keep.Add(ms);
                            using var xi = XImage.FromStream(ms);
                            double iw = flat.Width, ih = flat.Height, pw, ph, dx = 0, dy = 0, dw, dh;
                            if (j.A4)
                            {
                                pw = 595.28; ph = 841.89; if (iw > ih) (pw, ph) = (ph, pw);
                                double m = 24, sc = Math.Min((pw - 2 * m) / iw, (ph - 2 * m) / ih);
                                dw = iw * sc; dh = ih * sc; dx = (pw - dw) / 2; dy = (ph - dh) / 2;
                            }
                            else
                            {
                                double sc = 72.0 / 150, big = Math.Max(iw, ih) * sc; if (big > 1190) sc *= 1190 / big;
                                pw = dw = iw * sc; ph = dh = ih * sc;
                            }
                            var page = doc.AddPage(); page.Width = XUnit.FromPoint(pw); page.Height = XUnit.FromPoint(ph);
                            using (var gfx = XGraphics.FromPdfPage(page)) gfx.DrawImage(xi, dx, dy, dw, dh);
                            ok++;
                        }
                        catch { bad.Add(Path.GetFileName(f)); }
                    }
                    if (ok == 0) throw new InvalidOperationException("Tidak ada gambar yang bisa dibaca.");
                    doc.Save(outP);
                }
                foreach (var m in keep) m.Dispose();
                last = outP;
                return ($"Selesai: {ok} gambar → {Path.GetFileName(outP)} ({UI.Size(new FileInfo(outP).Length)})" + Bad(bad), last);
            }
            case MDoc: // DOC / DOCX / CSV -> PDF
            {
                int ok = 0; var engines = new List<string>(); string? firstErr = null;
                for (int i = 0; i < j.Files.Length; i++)
                {
                    var f = j.Files[i]; pr.Report((i, $"Mengonversi {Path.GetFileName(f)} ({i + 1}/{j.Files.Length})"));
                    var dst = Unique(Path.Combine(j.OutDir, Path.GetFileNameWithoutExtension(f) + ".pdf"));
                    try
                    {
                        var eng = DocPdf.Convert(f, dst, j.Orient);
                        if (!engines.Contains(eng)) engines.Add(eng);
                        ok++; last = dst;
                    }
                    catch (Exception ex)
                    {
                        bad.Add(Path.GetFileName(f)); firstErr ??= ex.Message;
                        try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                    }
                }
                if (ok == 0) throw new InvalidOperationException(firstErr ?? "Tidak ada dokumen yang berhasil dikonversi.");
                return ($"Selesai: {ok} dokumen → PDF ({string.Join(", ", engines)})" + Bad(bad) + (bad.Count > 0 && firstErr != null ? " · " + firstErr : ""), last);
            }
            case MMerge: // gabung PDF
            {
                var outP = Unique(Path.Combine(j.OutDir, "gabungan.pdf")); int pages = 0;
                var ins = new List<PdfDocument>();
                try
                {
                    using var outDoc = new PdfDocument();
                    for (int i = 0; i < j.Files.Length; i++)
                    {
                        var f = j.Files[i]; pr.Report((i, $"Menggabung {Path.GetFileName(f)} ({i + 1}/{j.Files.Length})"));
                        try
                        {
                            var inDoc = PdfReader.Open(f, PdfDocumentOpenMode.Import); ins.Add(inDoc);
                            for (int p = 0; p < inDoc.PageCount; p++) { outDoc.AddPage(inDoc.Pages[p]); pages++; }
                        }
                        catch { bad.Add(Path.GetFileName(f)); }
                    }
                    if (pages == 0) throw new InvalidOperationException("Tidak ada halaman yang bisa dibaca (PDF terkunci password?).");
                    outDoc.Save(outP);
                }
                finally { foreach (var d in ins) d.Dispose(); }
                last = outP;
                return ($"Selesai: {pages} halaman → {Path.GetFileName(outP)} ({UI.Size(new FileInfo(outP).Length)})" + Bad(bad), last);
            }
            case MSplit: // pisah PDF
            {
                var src = j.Files[0]; var name = Path.GetFileNameWithoutExtension(src);
                using var inDoc = PdfReader.Open(src, PdfDocumentOpenMode.Import);
                int n = inDoc.PageCount;
                var ranges = j.Ranged ? ParseRanges(j.Ranges, n) : Enumerable.Range(1, n).Select(i => (i, i)).ToList();
                var sub = UniqueDir(Path.Combine(j.OutDir, name + "_pisah")); Directory.CreateDirectory(sub);
                int k = 0;
                foreach (var (a, b) in ranges)
                {
                    pr.Report((k, $"Menyimpan halaman {a}{(a == b ? "" : "–" + b)} ({k + 1}/{ranges.Count})"));
                    using var od = new PdfDocument();
                    for (int p = a; p <= b; p++) od.AddPage(inDoc.Pages[p - 1]);
                    od.Save(Path.Combine(sub, a == b ? $"{name}_hal{a}.pdf" : $"{name}_hal{a}-{b}.pdf")); k++;
                }
                last = sub;
                return ($"Selesai: {k} file dari {n} halaman → folder {Path.GetFileName(sub)}", last);
            }
            default: // kompres
            {
                long totOld = 0, totNew = 0; int ok = 0, same = 0;
                int[] qImg = { 85, 72, 55 }, maxDim = { 3000, 2200, 1600 }, dpi = { 150, 110, 80 }, qPdf = { 80, 65, 50 };
                for (int i = 0; i < j.Files.Length; i++)
                {
                    var f = j.Files[i]; var ext = Path.GetExtension(f).ToLowerInvariant();
                    pr.Report((i, $"Mengompres {Path.GetFileName(f)} ({i + 1}/{j.Files.Length})"));
                    string? dst = null;
                    try
                    {
                        long old = new FileInfo(f).Length;
                        if (ext == ".pdf")
                        {
                            dst = Unique(Path.Combine(j.OutDir, Path.GetFileNameWithoutExtension(f) + "-kompres.pdf"));
                            await CompressPdf(f, dst, dpi[j.Level], qPdf[j.Level]);
                        }
                        else
                        {
                            var fm = j.Fmt == 1 ? SKEncodedImageFormat.Jpeg : j.Fmt == 2 ? SKEncodedImageFormat.Webp
                                : ext is ".jpg" or ".jpeg" ? SKEncodedImageFormat.Jpeg : ext == ".webp" ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png;
                            var oe = fm == SKEncodedImageFormat.Jpeg ? ".jpg" : fm == SKEncodedImageFormat.Webp ? ".webp" : ".png";
                            dst = Unique(Path.Combine(j.OutDir, Path.GetFileNameWithoutExtension(f) + "-kompres" + oe));
                            using var raw = LoadBmp(f);
                            SKBitmap cur = raw; SKBitmap? rs = null;
                            int big = Math.Max(raw.Width, raw.Height);
                            if (big > maxDim[j.Level]) { double sc = (double)maxDim[j.Level] / big; rs = raw.Resize(new SKImageInfo((int)Math.Max(1, raw.Width * sc), (int)Math.Max(1, raw.Height * sc)), SKFilterQuality.High); if (rs != null) cur = rs; }
                            using var flat = fm == SKEncodedImageFormat.Png ? null : Flatten(cur);
                            File.WriteAllBytes(dst, Enc(flat ?? cur, fm, fm == SKEncodedImageFormat.Png ? 100 : qImg[j.Level]));
                            rs?.Dispose();
                        }
                        long nw = new FileInfo(dst).Length;
                        if (nw >= old) { File.Delete(dst); dst = null; same++; totOld += old; totNew += old; }
                        else { totOld += old; totNew += nw; ok++; last = dst; }
                    }
                    catch { bad.Add(Path.GetFileName(f)); if (dst != null) { try { File.Delete(dst); } catch { } } }
                }
                if (ok == 0 && same == 0) throw new InvalidOperationException("Tidak ada file yang berhasil dikompres" + Bad(bad));
                int pct = totOld > 0 ? (int)Math.Round(100.0 * (totOld - totNew) / totOld) : 0;
                var msg = $"Selesai: {UI.Size(totOld)} → {UI.Size(totNew)} (hemat {pct}%)";
                if (same > 0) msg += $" · {same} file sudah optimal (tidak dibuat ulang)";
                return (msg + Bad(bad), last);
            }
        }
    }

    // ---------- PDF: render ulang tiap halaman jadi JPEG ----------
    static async Task CompressPdf(string src, string dst, int dpi, int q)
    {
        var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(src);
        var doc = await WinPdf.PdfDocument.LoadFromFileAsync(sf);
        var keep = new List<MemoryStream>();
        try
        {
            using var od = new PdfDocument();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                using var pg = doc.GetPage(i);
                double wDip = pg.Size.Width, hDip = pg.Size.Height;
                var opt = new WinPdf.PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(wDip / 96.0 * dpi)), DestinationHeight = (uint)Math.Max(1, Math.Round(hDip / 96.0 * dpi)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
                };
                using var rs = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await pg.RenderToStreamAsync(rs, opt);
                rs.Seek(0);
                var pngMs = new MemoryStream(); await rs.AsStreamForRead().CopyToAsync(pngMs); pngMs.Position = 0;
                using var bmp = SKBitmap.Decode(pngMs.ToArray()); using var flat = Flatten(bmp);
                var jm = new MemoryStream(Enc(flat, SKEncodedImageFormat.Jpeg, q)); keep.Add(jm);
                using var xi = XImage.FromStream(jm);
                var page = od.AddPage(); page.Width = XUnit.FromPoint(wDip * .75); page.Height = XUnit.FromPoint(hDip * .75);
                using var gfx = XGraphics.FromPdfPage(page); gfx.DrawImage(xi, 0, 0, wDip * .75, hDip * .75);
            }
            od.Save(dst);
        }
        finally { foreach (var m in keep) m.Dispose(); }
    }

    // ---------- gambar (SkiaSharp) ----------
    internal static SKBitmap LoadBmp(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Format gambar tidak dikenali");
        var bmp = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Gambar tidak bisa dibaca");
        return Orient(bmp, (int)codec.EncodedOrigin);
    }

    static SKMatrix M(float sx, float kx, float tx, float ky, float sy, float ty)
        => new SKMatrix { ScaleX = sx, SkewX = kx, TransX = tx, SkewY = ky, ScaleY = sy, TransY = ty, Persp0 = 0, Persp1 = 0, Persp2 = 1 };

    // Terapkan orientasi EXIF (foto HP sering tersimpan miring)
    static SKBitmap Orient(SKBitmap b, int o)
    {
        if (o <= 1 || o > 8) return b;
        float W = b.Width, H = b.Height; bool swap = o >= 5;
        var r = new SKBitmap(swap ? b.Height : b.Width, swap ? b.Width : b.Height);
        using (var c = new SKCanvas(r))
        {
            c.SetMatrix(o switch
            {
                2 => M(-1, 0, W, 0, 1, 0),
                3 => M(-1, 0, W, 0, -1, H),
                4 => M(1, 0, 0, 0, -1, H),
                5 => M(0, 1, 0, 1, 0, 0),
                6 => M(0, -1, H, 1, 0, 0),
                7 => M(0, -1, H, -1, 0, W),
                _ => M(0, 1, 0, -1, 0, W)
            });
            c.DrawBitmap(b, 0, 0);
        }
        b.Dispose(); return r;
    }

    // Letakkan di atas latar putih (transparansi PNG/WebP tidak didukung JPEG)
    internal static SKBitmap Flatten(SKBitmap b)
    {
        var r = new SKBitmap(b.Width, b.Height);
        using var c = new SKCanvas(r); c.Clear(SKColors.White); c.DrawBitmap(b, 0, 0);
        return r;
    }

    internal static byte[] Enc(SKBitmap b, SKEncodedImageFormat f, int q)
    {
        using var img = SKImage.FromBitmap(b);
        using var d = img.Encode(f, q) ?? throw new InvalidOperationException("Gagal menyimpan gambar");
        return d.ToArray();
    }

    // ---------- util ----------
    static List<(int, int)> ParseRanges(string t, int n)
    {
        var r = new List<(int, int)>();
        foreach (var part in t.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = part.Split('-', 2, StringSplitOptions.TrimEntries); int a, b;
            if (p.Length == 1) { if (!int.TryParse(p[0], out a)) throw new FormatException($"Rentang tidak valid: {part}"); b = a; }
            else
            {
                a = p[0] == "" ? 1 : int.TryParse(p[0], out var x) ? x : throw new FormatException($"Rentang tidak valid: {part}");
                b = p[1] == "" ? n : int.TryParse(p[1], out var y) ? y : throw new FormatException($"Rentang tidak valid: {part}");
            }
            if (a > b) (a, b) = (b, a);
            if (b < 1 || a > n) throw new FormatException($"Halaman {part} di luar jangkauan (PDF ini {n} halaman)");
            r.Add((Math.Max(1, a), Math.Min(n, b)));
        }
        if (r.Count == 0) throw new FormatException("Isi rentang halaman dulu, mis. 1-3, 5");
        return r;
    }

    internal static string Unique(string p)
    {
        if (!File.Exists(p)) return p;
        var d = Path.GetDirectoryName(p)!; var n = Path.GetFileNameWithoutExtension(p); var e = Path.GetExtension(p); int i = 2; string q;
        do q = Path.Combine(d, $"{n} ({i++}){e}"); while (File.Exists(q));
        return q;
    }
    static string UniqueDir(string p)
    {
        if (!Directory.Exists(p)) return p;
        int i = 2; string q; do q = $"{p} ({i++})"; while (Directory.Exists(q));
        return q;
    }
}
