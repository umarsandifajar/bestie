using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
namespace MySecretary;

// ---------------- Dokumen -> PDF ----------------
// CSV  : dirender sendiri jadi tabel (PDFsharp), kolom & tinggi baris menyesuaikan isi, header diulang tiap halaman.
// DOCX : urutan percobaan  1) Microsoft Word (COM)  2) LibreOffice (headless)  3) pembaca .docx bawaan (teks, judul, tabel; tanpa gambar).
// DOC  : butuh Microsoft Word atau LibreOffice (format biner lama tidak bisa dibaca sendiri).
static class DocPdf
{
    // Mengembalikan nama mesin yang dipakai (untuk ditampilkan di status). Melempar exception dengan pesan yang bisa dibaca bila gagal.
    public static string Convert(string src, string dst, int orient)
    {
        var ext = Path.GetExtension(src).ToLowerInvariant();
        if (ext == ".csv") { CsvToPdf(src, dst, orient); return "CSV"; }

        if (TryWord(src, dst)) return "Word";
        if (TryLibre(src, dst)) return "LibreOffice";
        if (ext == ".docx") { DocxToPdf(src, dst); return "bawaan"; }
        throw new InvalidOperationException("File .doc butuh Microsoft Word atau LibreOffice terpasang di komputer ini.");
    }

    // ===================== Word / LibreOffice =====================
    static bool TryWord(string src, string dst)
    {
        Type? t = null;
        try { t = Type.GetTypeFromProgID("Word.Application"); } catch { }
        if (t == null) return false;
        return OnSta(() =>
        {
            object? appObj = null, docObj = null;
            try
            {
                appObj = Activator.CreateInstance(t);
                dynamic app = appObj!;
                app.Visible = false; app.DisplayAlerts = 0;
                docObj = app.Documents.Open(src, false, true, false);   // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
                dynamic doc = docObj!;
                doc.ExportAsFixedFormat(dst, 17);                       // 17 = wdExportFormatPDF
                return File.Exists(dst);
            }
            catch { try { if (File.Exists(dst)) File.Delete(dst); } catch { } return false; }
            finally
            {
                try { if (docObj != null) { dynamic d = docObj; d.Close(false); } } catch { }
                try { if (appObj != null) { dynamic a = appObj; a.Quit(false); } } catch { }
                try { if (docObj != null) Marshal.FinalReleaseComObject(docObj); } catch { }
                try { if (appObj != null) Marshal.FinalReleaseComObject(appObj); } catch { }
            }
        }, 120000, false);
    }

    static T OnSta<T>(Func<T> f, int ms, T onTimeout)
    {
        T r = onTimeout;
        var th = new Thread(() => { try { r = f(); } catch { } }) { IsBackground = true };
        th.SetApartmentState(ApartmentState.STA); th.Start();
        return th.Join(ms) ? r : onTimeout;
    }

    static bool TryLibre(string src, string dst)
    {
        string? exe = null;
        foreach (var p in new[] { @"C:\Program Files\LibreOffice\program\soffice.exe", @"C:\Program Files (x86)\LibreOffice\program\soffice.exe" })
            if (File.Exists(p)) { exe = p; break; }
        if (exe == null) return false;
        var tmp = Path.Combine(Path.GetTempPath(), "bestie_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--headless"); psi.ArgumentList.Add("--convert-to"); psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir"); psi.ArgumentList.Add(tmp); psi.ArgumentList.Add(src);
            using var pr = Process.Start(psi);
            if (pr == null) return false;
            if (!pr.WaitForExit(120000)) { try { pr.Kill(true); } catch { } return false; }
            var o = Path.Combine(tmp, Path.GetFileNameWithoutExtension(src) + ".pdf");
            if (!File.Exists(o)) return false;
            File.Move(o, dst, true); return true;
        }
        catch { return false; }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // ===================== penata halaman PDF =====================
    static class Fonts
    {
        static readonly Dictionary<(double, bool, bool), XFont> cache = new();
        public static XFont Get(double size, bool bold = false, bool italic = false)
        {
            lock (cache)
            {
                var k = (size, bold, italic);
                if (!cache.TryGetValue(k, out var f))
                {
                    var st = bold && italic ? XFontStyleEx.BoldItalic : bold ? XFontStyleEx.Bold : italic ? XFontStyleEx.Italic : XFontStyleEx.Regular;
                    f = new XFont("Arial", size, st); cache[k] = f;
                }
                return f;
            }
        }
    }

    sealed class Pager : IDisposable
    {
        public readonly PdfDocument Doc = new();
        public XGraphics? G;
        public readonly double W, H, ML, MR, MT, MB;
        public double Y;
        int n;
        public Pager(double w, double h, double ml, double mr, double mt, double mb) { W = w; H = h; ML = ml; MR = mr; MT = mt; MB = mb; }
        public double Width => W - ML - MR;
        public double Bottom => H - MB;
        public void NewPage()
        {
            G?.Dispose();
            var page = Doc.AddPage(); page.Width = XUnit.FromPoint(W); page.Height = XUnit.FromPoint(H);
            G = XGraphics.FromPdfPage(page); Y = MT; n++;
            G.DrawString(n.ToString(), Fonts.Get(7.5), new XSolidBrush(XColor.FromArgb(140, 140, 150)), W / 2, H - 24, XStringFormats.TopCenter);
        }
        public void Ensure(double h) { if (G == null || Y + h > Bottom) NewPage(); }
        public void Save(string path) { G?.Dispose(); G = null; Doc.Save(path); }
        public void Dispose() { G?.Dispose(); G = null; Doc.Dispose(); }
    }

    static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\n') sb.Append(ch);
            else if (ch == '\t' || ch == '\u00A0') sb.Append(' ');
            else if (ch >= ' ' && ch != '\u007F') sb.Append(ch);
        }
        return sb.ToString();
    }

    // Bungkus teks per kata (kata yang lebih panjang dari lebar kolom dipotong per huruf). Lebih dari maxLines -> dipotong dengan "…".
    static List<string> Wrap(XGraphics g, string text, XFont f, double maxW, int maxLines)
    {
        var lines = new List<string>();
        foreach (var para in Clean(text).Split('\n'))
        {
            var cur = "";
            foreach (var word in para.Split(' '))
            {
                var w = word;
                var test = cur.Length == 0 ? w : cur + " " + w;
                if (g.MeasureString(test, f).Width <= maxW) { cur = test; continue; }
                if (cur.Length > 0) { lines.Add(cur); cur = ""; }
                while (w.Length > 1 && g.MeasureString(w, f).Width > maxW)
                {
                    int k = w.Length - 1;
                    while (k > 1 && g.MeasureString(w.Substring(0, k), f).Width > maxW) k--;
                    lines.Add(w.Substring(0, k)); w = w.Substring(k);
                }
                cur = w;
            }
            lines.Add(cur);
        }
        if (lines.Count > maxLines) { lines.RemoveRange(maxLines, lines.Count - maxLines); lines[maxLines - 1] = lines[maxLines - 1].TrimEnd() + "…"; }
        return lines;
    }

    // ===================== CSV =====================
    static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch { return Encoding.Latin1.GetString(bytes); }   // CSV dari Excel lama (Windows-1252)
    }

    static List<string[]> ParseCsv(string text)
    {
        // pembatas dipilih dari baris pertama: , ; tab |
        var first = text.Split('\n')[0];
        char delim = ',', best = ','; int bestN = -1;
        foreach (var c in new[] { ',', ';', '\t', '|' }) { int n = first.Count(x => x == c); if (n > bestN) { bestN = n; best = c; } }
        delim = best;

        var rows = new List<string[]>(); var row = new List<string>(); var sb = new StringBuilder(); bool q = false, any = false;
        void EndCell() { row.Add(sb.ToString()); sb.Clear(); }
        void EndRow() { EndCell(); if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray()); row = new List<string>(); any = false; }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i]; any = true;
            if (q)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                else sb.Append(c);
            }
            else if (c == '"') q = true;
            else if (c == delim) EndCell();
            else if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (c == '\n') EndRow();
            else sb.Append(c);
        }
        if (any) EndRow();
        return rows;
    }

    static double[] FitCols(double[] nat, double avail, double min)
    {
        var w = (double[])nat.Clone();
        for (int it = 0; it < 8; it++)
        {
            if (w.Sum() <= avail + .01) break;
            double fixedSum = 0, flex = 0;
            foreach (var x in w) { if (x <= min + .01) fixedSum += x; else flex += x; }
            if (flex <= 0) break;
            double k = (avail - fixedSum) / flex;
            for (int i = 0; i < w.Length; i++) if (w[i] > min + .01) w[i] = Math.Max(min, w[i] * k);
        }
        double s = w.Sum(); if (s > avail) { double k = avail / s; for (int i = 0; i < w.Length; i++) w[i] *= k; }
        return w;
    }

    static void CsvToPdf(string src, string dst, int orient)
    {
        var rows = ParseCsv(ReadText(src));
        if (rows.Count == 0) throw new InvalidDataException("File CSV kosong.");
        if (rows.Count > 50000) throw new InvalidDataException("CSV terlalu besar (maksimal 50.000 baris).");
        int cols = rows.Max(r => r.Length);

        bool land = orient == 2 || (orient == 0 && cols > 5);
        double pw = 595.28, ph = 841.89; if (land) (pw, ph) = (ph, pw);
        using var P = new Pager(pw, ph, 36, 36, 40, 44);
        P.NewPage();
        double fs = cols > 14 ? 7 : cols > 8 ? 8 : 9;
        var fN = Fonts.Get(fs); var fB = Fonts.Get(fs, true);
        const double padH = 4, padV = 3; double lineH = fs * 1.3;
        var g = P.G!;

        // lebar kolom alami (dari isi, 300 baris pertama), maks 200pt
        var nat = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            double m = 28;
            for (int r = 0; r < Math.Min(rows.Count, 300); r++)
            {
                if (c >= rows[r].Length) continue;
                var one = Clean(rows[r][c]).Replace('\n', ' ');
                if (one.Length > 120) one = one.Substring(0, 120);
                m = Math.Max(m, g.MeasureString(one, r == 0 ? fB : fN).Width + 2 * padH + 2);
            }
            nat[c] = Math.Min(m, 200);
        }

        double avail = P.Width; var wd = new double[cols]; var groups = new List<List<int>>();
        if (nat.Sum() <= avail) { wd = nat; groups.Add(Enumerable.Range(0, cols).ToList()); }
        else if (cols * 44 <= avail) { wd = FitCols(nat, avail, 44); groups.Add(Enumerable.Range(0, cols).ToList()); }
        else
        {
            // terlalu banyak kolom: bagi jadi beberapa kelompok kolom (tiap kelompok memenuhi lebar halaman)
            var cur = new List<int>(); double sum = 0;
            for (int c = 0; c < cols; c++)
            {
                wd[c] = Math.Min(nat[c], Math.Min(150, avail));
                if (cur.Count > 0 && sum + wd[c] > avail) { groups.Add(cur); cur = new List<int>(); sum = 0; }
                cur.Add(c); sum += wd[c];
            }
            if (cur.Count > 0) groups.Add(cur);
        }

        var grid = new XPen(XColor.FromArgb(205, 208, 215), 0.5);
        var head = new XSolidBrush(XColor.FromArgb(226, 230, 238)); var zebra = new XSolidBrush(XColor.FromArgb(246, 247, 250));
        var ink = new XSolidBrush(XColor.FromArgb(25, 25, 30));
        bool titled = false; bool firstGroup = true;

        foreach (var grp in groups)
        {
            if (!firstGroup) P.NewPage();
            firstGroup = false; g = P.G!;
            if (!titled)
            {
                g.DrawString(Path.GetFileName(src), Fonts.Get(11, true), ink, P.ML, P.Y, XStringFormats.TopLeft);
                P.Y += 22; titled = true;
            }
            var gr = grp;
            void DrawRow(string[] row, bool header, bool shade)
            {
                var font = header ? fB : fN; g = P.G!;
                var cells = new List<List<string>>(); int mx = 1;
                foreach (var c in gr) { var l = Wrap(g, c < row.Length ? row[c] : "", font, wd[c] - 2 * padH, header ? 3 : 8); cells.Add(l); mx = Math.Max(mx, l.Count); }
                double h = mx * lineH + 2 * padV;
                if (P.Y + h > P.Bottom) { P.NewPage(); g = P.G!; if (!header) DrawRow(rows[0], true, false); }
                double x = P.ML, y = P.Y;
                for (int k = 0; k < gr.Count; k++)
                {
                    int c = gr[k];
                    if (header) g.DrawRectangle(grid, head, x, y, wd[c], h);
                    else if (shade) g.DrawRectangle(grid, zebra, x, y, wd[c], h);
                    else g.DrawRectangle(grid, x, y, wd[c], h);
                    for (int li = 0; li < cells[k].Count; li++) g.DrawString(cells[k][li], font, ink, x + padH, y + padV + li * lineH, XStringFormats.TopLeft);
                    x += wd[c];
                }
                P.Y += h;
            }
            DrawRow(rows[0], true, false);
            for (int r = 1; r < rows.Count; r++) DrawRow(rows[r], false, r % 2 == 0);
        }
        P.Save(dst);
    }

    // ===================== DOCX (pembaca bawaan) =====================
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    sealed record Tok(string T, XFont F, XBrush B, bool U, bool Sp, bool Nl);

    static double Tw(XElement? el, string attr, double def)
    {
        var v = (string?)el?.Attribute(W + attr);
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0 ? t / 20.0 : def;
    }
    static bool On(XElement? el) => el != null && ((string?)el.Attribute(W + "val")) is not ("0" or "false" or "none");

    static void DocxToPdf(string src, string dst)
    {
        XDocument xdoc;
        using (var zip = ZipFile.OpenRead(src))
        {
            var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Bukan file .docx yang valid.");
            using var stream = entry.Open(); xdoc = XDocument.Load(stream);
        }
        var body = xdoc.Root?.Element(W + "body") ?? throw new InvalidDataException("Isi dokumen tidak ditemukan.");

        double pw = 595.28, ph = 841.89, ml = 64, mr = 64, mt = 60, mb = 60;
        var sp = body.Elements(W + "sectPr").LastOrDefault() ?? body.Descendants(W + "sectPr").LastOrDefault();
        if (sp != null)
        {
            var sz = sp.Element(W + "pgSz"); var mg = sp.Element(W + "pgMar");
            pw = Tw(sz, "w", pw); ph = Tw(sz, "h", ph);
            if (pw < 200 || ph < 200 || pw > 2000 || ph > 2000) { pw = 595.28; ph = 841.89; }
            ml = Math.Min(Tw(mg, "left", ml), pw / 3); mr = Math.Min(Tw(mg, "right", mr), pw / 3);
            mt = Math.Min(Tw(mg, "top", mt), ph / 4); mb = Math.Min(Tw(mg, "bottom", mb), ph / 4);
        }

        using var P = new Pager(pw, ph, ml, mr, mt, mb);
        P.NewPage();
        var black = new XSolidBrush(XColor.FromArgb(0, 0, 0));
        var line = new XPen(XColor.FromArgb(120, 120, 128), 0.6);

        string ParaText(XElement p)
        {
            var sb = new StringBuilder();
            foreach (var n in p.Descendants())
            {
                if (n.Name == W + "t") sb.Append(n.Value);
                else if (n.Name == W + "tab") sb.Append(' ');
                else if (n.Name == W + "br" || n.Name == W + "cr") sb.Append('\n');
            }
            return sb.ToString();
        }

        void Para(XElement p)
        {
            var ppr = p.Element(W + "pPr");
            var style = ((string?)ppr?.Element(W + "pStyle")?.Attribute(W + "val") ?? "").ToLowerInvariant().Replace(" ", "");
            double baseSize = 11; bool hb = false;
            if (style == "title" || style == "judul") { baseSize = 24; hb = true; }
            else if (style.StartsWith("heading") || style.StartsWith("judul"))
            {
                char c = style[style.Length - 1]; hb = true;
                baseSize = c == '1' ? 18 : c == '2' ? 15 : c == '3' ? 13 : 12;
            }
            var jc = (string?)ppr?.Element(W + "jc")?.Attribute(W + "val") ?? "left";
            double indent = Tw(ppr?.Element(W + "ind"), "left", 0);
            double before = Tw(ppr?.Element(W + "spacing"), "before", 0), after = Tw(ppr?.Element(W + "spacing"), "after", 6);
            bool bullet = ppr?.Element(W + "numPr") != null;

            var toks = new List<Tok>();
            if (bullet) toks.Add(new Tok("•", Fonts.Get(baseSize), black, false, true, false));
            foreach (var r in p.Descendants(W + "r"))
            {
                var rpr = r.Element(W + "rPr");
                bool b = hb || On(rpr?.Element(W + "b")), it = On(rpr?.Element(W + "i")), u = On(rpr?.Element(W + "u"));
                double size = baseSize;
                if (double.TryParse((string?)rpr?.Element(W + "sz")?.Attribute(W + "val"), NumberStyles.Float, CultureInfo.InvariantCulture, out var hp) && hp > 0) size = Math.Clamp(hp / 2, 6, 72);
                XBrush br = black;
                var col = (string?)rpr?.Element(W + "color")?.Attribute(W + "val");
                if (col != null && Regex.IsMatch(col, "^[0-9A-Fa-f]{6}$"))
                    br = new XSolidBrush(XColor.FromArgb(System.Convert.ToInt32(col.Substring(0, 2), 16), System.Convert.ToInt32(col.Substring(2, 2), 16), System.Convert.ToInt32(col.Substring(4, 2), 16)));
                var f = Fonts.Get(size, b, it);
                foreach (var ch in r.Elements())
                {
                    if (ch.Name == W + "t")
                    {
                        var text = Clean(ch.Value).Replace('\n', ' ');
                        if (text.Length == 0) continue;
                        if (char.IsWhiteSpace(text[0]) && toks.Count > 0) toks[toks.Count - 1] = toks[toks.Count - 1] with { Sp = true };
                        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < words.Length; i++)
                            toks.Add(new Tok(words[i], f, br, u, i < words.Length - 1 || char.IsWhiteSpace(text[text.Length - 1]), false));
                    }
                    else if (ch.Name == W + "tab" && toks.Count > 0) toks[toks.Count - 1] = toks[toks.Count - 1] with { Sp = true };
                    else if (ch.Name == W + "br" || ch.Name == W + "cr") toks.Add(new Tok("", f, br, false, false, true));
                }
            }

            double x0 = P.ML + indent, avail = Math.Max(40, P.Width - indent);
            var lines = new List<(List<Tok> t, double w, double h)>();
            var cur = new List<Tok>(); double cw = 0, ch2 = 0;
            void Flush() { lines.Add((cur, cw, ch2 == 0 ? baseSize * 1.25 : ch2)); cur = new List<Tok>(); cw = 0; ch2 = 0; }
            foreach (var t in toks)
            {
                if (t.Nl) { Flush(); continue; }
                double w = P.G!.MeasureString(t.T, t.F).Width;
                if (cur.Count > 0 && cw + w > avail) Flush();
                cur.Add(t); cw += w + (t.Sp ? t.F.Size * 0.28 : 0); ch2 = Math.Max(ch2, t.F.Size * 1.28);
            }
            Flush();

            if (P.Y > P.MT + 0.1) P.Y += before;
            foreach (var (lt, lw, lh) in lines)
            {
                P.Ensure(lh);
                double x = x0;
                if (jc == "center") x += (avail - lw) / 2; else if (jc == "right" || jc == "end") x += avail - lw;
                foreach (var t in lt)
                {
                    double w = P.G!.MeasureString(t.T, t.F).Width;
                    P.G.DrawString(t.T, t.F, t.B, x, P.Y, XStringFormats.TopLeft);
                    if (t.U) P.G.DrawLine(new XPen(XColor.FromArgb(0, 0, 0), 0.5), x, P.Y + t.F.Size * 1.1, x + w, P.Y + t.F.Size * 1.1);
                    x += w + (t.Sp ? t.F.Size * 0.28 : 0);
                }
                P.Y += lh;
            }
            P.Y += after;
        }

        void Table(XElement tbl)
        {
            var grid = tbl.Element(W + "tblGrid")?.Elements(W + "gridCol").Select(gc => Tw(gc, "w", 0)).Where(v => v > 0).ToList() ?? new List<double>();
            var trs = tbl.Elements(W + "tr").ToList();
            int ncols = grid.Count > 0 ? grid.Count : Math.Max(1, trs.Select(tr => tr.Elements(W + "tc").Count()).DefaultIfEmpty(1).Max());
            double avail = P.Width;
            var widths = new double[ncols];
            if (grid.Count > 0) { double sum = grid.Sum(), k = sum > avail ? avail / sum : 1; for (int i = 0; i < ncols; i++) widths[i] = grid[i] * k; }
            else for (int i = 0; i < ncols; i++) widths[i] = avail / ncols;

            var font = Fonts.Get(10); const double pad = 4, lh = 13;
            int maxL = Math.Max(1, (int)((P.H - P.MT - P.MB - 2 * pad) / lh));
            foreach (var tr in trs)
            {
                var cells = new List<(List<string> lines, double w)>(); int ci = 0; int mx = 1;
                foreach (var tc in tr.Elements(W + "tc"))
                {
                    int span = int.TryParse((string?)tc.Element(W + "tcPr")?.Element(W + "gridSpan")?.Attribute(W + "val"), out var sN) && sN > 0 ? sN : 1;
                    double cw = 0; for (int k = 0; k < span; k++) cw += ci + k < ncols ? widths[ci + k] : widths[ncols - 1];
                    ci += span;
                    var text = string.Join("\n", tc.Descendants(W + "p").Select(ParaText));
                    var l = Wrap(P.G!, text, font, Math.Max(10, cw - 2 * pad), maxL);
                    cells.Add((l, cw)); mx = Math.Max(mx, l.Count);
                }
                if (cells.Count == 0) continue;
                double h = mx * lh + 2 * pad;
                P.Ensure(h);
                double x = P.ML;
                foreach (var (l, cw) in cells)
                {
                    P.G!.DrawRectangle(line, x, P.Y, cw, h);
                    for (int i = 0; i < l.Count; i++) P.G.DrawString(l[i], font, black, x + pad, P.Y + pad + i * lh, XStringFormats.TopLeft);
                    x += cw;
                }
                P.Y += h;
            }
            P.Y += 8;
        }

        void Blocks(XElement parent)
        {
            foreach (var el in parent.Elements())
            {
                if (el.Name == W + "p") Para(el);
                else if (el.Name == W + "tbl") Table(el);
                else if (el.Name == W + "sdt") { var c = el.Element(W + "sdtContent"); if (c != null) Blocks(c); }
            }
        }
        Blocks(body);
        P.Save(dst);
    }
}
