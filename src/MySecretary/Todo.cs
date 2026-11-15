using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Run = System.Windows.Documents.Run;
using Shapes = System.Windows.Shapes;
using static MySecretary.Fx;
namespace MySecretary;

// ---------------- To Do / My Task ----------------
// Kotak melayang bergaya timeline vertikal: titik status di kiri (Done = terisi + centang, On Process = cincin + jam, Hold = abu + jeda),
// garis penghubung ke tugas berikutnya, tanggal kecil, judul tebal, keterangan abu-abu.
// Klik titik status -> pilih Done / On Process / Hold (atau Hapus). Kotak bisa digeser (tarik bar atas), di-resize (tarik tepi / sudut),
// dan bisa diatur selalu di atas (ikon pin). Setelah kotak ditutup, ikon To Do muncul di notch (di atas volume) selama masih ada tugas yang belum Done.

public class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public string Status { get; set; } = TodoSt.Process;     // done | process | hold
    public DateTime Created { get; set; } = DateTime.Now;
}

static class TodoSt
{
    public const string Done = "done", Process = "process", Hold = "hold";
    public static string Label(string s) => s == Done ? "Done" : s == Hold ? "Hold" : "On Process";
}

sealed class TodoWindow : Window
{
    public static bool IsOpen { get; private set; }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }

    static readonly CultureInfo Id = new("id-ID");
    static readonly Color Red = Color.FromRgb(0xF8, 0x71, 0x71);

    readonly Config cfg;
    readonly Border headHost = new();
    readonly StackPanel listHost = new();
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    readonly TextBox titleBox = new() { MaxLength = 120 };
    readonly TextBox noteBox = new() { MaxLength = 300 };
    readonly Button addBtn, cancelBtn;
    readonly DispatcherTimer geomT = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Action onTheme;
    TextBlock? summary;
    string? editId;

    public TodoWindow(Config c)
    {
        cfg = c; IsOpen = true;
        Title = "To Do";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = cfg.TodoTop; MinWidth = 300; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyGeom();

        addBtn = UI.Btn("Tambah", Save, true); addBtn.Margin = new Thickness(8, 0, 0, 0);
        cancelBtn = UI.Btn("Batal", ResetForm); cancelBtn.Margin = new Thickness(8, 0, 0, 0); cancelBtn.Visibility = Visibility.Collapsed;

        Content = BuildRoot();
        BuildHeader();
        RenderList();

        onTheme = () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rethemed));
        Pal.Changed += onTheme;
        geomT.Tick += (_, _) => { geomT.Stop(); StoreGeom(); cfg.Save(); };
        SizeChanged += (_, _) => KickGeom();
        LocationChanged += (_, _) => KickGeom();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && editId != null) { ResetForm(); e.Handled = true; } };
        Closed += OnClosed;

        Opacity = 0;
        Loaded += (_, _) => To(this, OpacityProperty, 1, 200);
        NotchHub.Ping();
    }

    void OnClosed(object? s, EventArgs e)
    {
        geomT.Stop(); StoreGeom(); cfg.Save();
        Pal.Changed -= onTheme;
        IsOpen = false;
        NotchHub.Ping();   // kotak ditutup: ikon To Do muncul di notch
    }

    // ---------- posisi & ukuran ----------
    void ApplyGeom()
    {
        var wa = SystemParameters.WorkArea;
        double w = Math.Clamp(cfg.TodoW, MinWidth, Math.Max(MinWidth, wa.Width));
        double h = Math.Clamp(cfg.TodoH, MinHeight, Math.Max(MinHeight, wa.Height));
        double x = wa.Right - w - 24, y = wa.Top + 60;
        if (cfg.TodoPlaced)
        {
            double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop, vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
            // pakai posisi tersimpan hanya bila masih terlihat di layar (monitor bisa saja sudah dicabut)
            if (cfg.TodoX + w > vl + 80 && cfg.TodoX < vl + vw - 80 && cfg.TodoY > vt - 10 && cfg.TodoY < vt + vh - 60) { x = cfg.TodoX; y = cfg.TodoY; }
        }
        Width = w; Height = h; Left = x; Top = y;
    }
    void StoreGeom() { cfg.TodoX = Left; cfg.TodoY = Top; cfg.TodoW = Width; cfg.TodoH = Height; cfg.TodoPlaced = true; }
    void KickGeom() { geomT.Stop(); geomT.Start(); }

    Point Cur()
    {
        GetCursorPos(out var p);
        var d = VisualTreeHelper.GetDpi(this);
        return new Point(p.X / d.DpiScaleX, p.Y / d.DpiScaleY);
    }

    void DoResize(Rect r0, Point p0, bool l, bool r, bool t, bool b)
    {
        var p = Cur(); double dx = p.X - p0.X, dy = p.Y - p0.Y;
        double nl = r0.Left, nt = r0.Top, nw = r0.Width, nh = r0.Height;
        if (r) nw = Math.Max(MinWidth, r0.Width + dx);
        if (b) nh = Math.Max(MinHeight, r0.Height + dy);
        if (l) { nw = Math.Max(MinWidth, r0.Width - dx); nl = r0.Right - nw; }
        if (t) { nh = Math.Max(MinHeight, r0.Height - dy); nt = r0.Bottom - nh; }
        Left = nl; Top = nt; Width = nw; Height = nh;
    }

    // Pegangan resize tak kasatmata di tepi & sudut kartu (posisi kursor dibaca dari layar supaya stabil saat tepi kiri/atas ikut bergeser)
    void AddHandles(Grid g)
    {
        const double E = 5, C = 12;
        void H(HorizontalAlignment ha, VerticalAlignment va, double w, double h, Cursor cur, bool l, bool r, bool t, bool b)
        {
            var el = new Border { Background = Brushes.Transparent, HorizontalAlignment = ha, VerticalAlignment = va, Cursor = cur };
            if (!double.IsNaN(w)) el.Width = w;
            if (!double.IsNaN(h)) el.Height = h;
            Point p0 = default; Rect r0 = default;
            el.MouseLeftButtonDown += (_, e) => { el.CaptureMouse(); p0 = Cur(); r0 = new Rect(Left, Top, Width, Height); e.Handled = true; };
            el.MouseMove += (_, _) => { if (el.IsMouseCaptured) DoResize(r0, p0, l, r, t, b); };
            el.MouseLeftButtonUp += (_, e) => { el.ReleaseMouseCapture(); e.Handled = true; };
            g.Children.Add(el);
        }
        H(HorizontalAlignment.Left, VerticalAlignment.Stretch, E, double.NaN, Cursors.SizeWE, true, false, false, false);
        H(HorizontalAlignment.Right, VerticalAlignment.Stretch, E, double.NaN, Cursors.SizeWE, false, true, false, false);
        H(HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, E, Cursors.SizeNS, false, false, true, false);
        H(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, E, Cursors.SizeNS, false, false, false, true);
        H(HorizontalAlignment.Left, VerticalAlignment.Top, C, C, Cursors.SizeNWSE, true, false, true, false);
        H(HorizontalAlignment.Right, VerticalAlignment.Top, C, C, Cursors.SizeNESW, false, true, true, false);
        H(HorizontalAlignment.Left, VerticalAlignment.Bottom, C, C, Cursors.SizeNESW, true, false, false, true);
        H(HorizontalAlignment.Right, VerticalAlignment.Bottom, C, C, Cursors.SizeNWSE, false, true, false, true);
    }

    // ---------- kerangka ----------
    FrameworkElement BuildRoot()
    {
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(headHost, 0); body.Children.Add(headHost);
        scroll.Content = listHost; scroll.Margin = new Thickness(0, 0, 5, 0); listHost.Margin = new Thickness(0, 6, 0, 10);
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        var comp = BuildComposer(); Grid.SetRow(comp, 2); body.Children.Add(comp);

        var inner = new Grid(); inner.Children.Add(body);
        var logo = UI.Logo(10); logo.Margin = new Thickness(0, 0, 16, 6); inner.Children.Add(logo);   // logo mini di sudut kanan bawah
        AddHandles(inner);
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = .4 }
        };
        card.SetResourceReference(Border.BackgroundProperty, "Win");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Ink");   // warna teks default seluruh isi kotak
        return new Grid { Margin = new Thickness(12), Children = { card } };
    }

    void BuildHeader()
    {
        var g = new Grid { Height = 48, Background = Brushes.Transparent };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ic = Icons.Glyph(Icons.Todo, 20, Pal.CV(45, true), 1.6);
        if (ic is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        var ttl = new TextBlock { Text = "To Do / My Task", FontFamily = Fx.Jakarta, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Pal.Br(Pal.Ink) };
        summary = new TextBlock { FontSize = 11, Foreground = Pal.Br(Pal.Dim), Margin = new Thickness(0, 1, 0, 0) };
        var txt = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(ttl); txt.Children.Add(summary);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        left.Children.Add(ic); left.Children.Add(txt);
        g.Children.Add(left);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        btns.Children.Add(IconBtn(Icons.Trash, "Hapus semua tugas yang sudah Done", ClearDone, false));
        btns.Children.Add(IconBtn(Icons.PinG, cfg.TodoTop ? "Selalu di atas: aktif (klik untuk mematikan)" : "Selalu di atas: mati (klik untuk mengaktifkan)", TogglePin, cfg.TodoTop));
        btns.Children.Add(CloseBtn());
        Grid.SetColumn(btns, 1); g.Children.Add(btns);

        // geser kotak dengan menarik bar atas
        g.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
        headHost.Child = g;
        UpdateSummary();
    }

    void UpdateSummary()
    {
        if (summary == null) return;
        int active = cfg.Todos.Count(x => x.Status != TodoSt.Done), done = cfg.Todos.Count - active;
        summary.Text = cfg.Todos.Count == 0 ? "Belum ada tugas" : $"{active} aktif · {done} selesai";
    }

    Border IconBtn(string glyph, string tip, Action click, bool on)
    {
        var gl = Icons.Glyph(glyph, 15, on ? Pal.CV(45) : Pal.Br(Pal.Dim), 1.5, on);
        if (gl is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        var b = new Border
        {
            Width = 30, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 2, 0),
            Cursor = Cursors.Hand, ToolTip = tip, Child = gl
        };
        bool down = false;
        b.MouseEnter += (_, _) => b.Background = Pal.Br(Pal.Hover);
        b.MouseLeave += (_, _) => { down = false; b.Background = Brushes.Transparent; };
        // PENTING: tandai Handled supaya tidak sampai ke bar judul (DragMove menelan MouseUp)
        b.MouseLeftButtonDown += (_, e) => { down = true; e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (down) { down = false; click(); } };
        return b;
    }

    Border CloseBtn()
    {
        var x = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Foreground = Pal.Br(Pal.Ink)
        };
        var b = new Border
        {
            Width = 36, Height = 28, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = "Tutup · tetap bisa dibuka dari ikon di notch", Child = x
        };
        bool down = false;
        b.MouseEnter += (_, _) => { b.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)); x.Foreground = Brushes.White; };
        b.MouseLeave += (_, _) => { down = false; b.Background = Brushes.Transparent; x.Foreground = Pal.Br(Pal.Ink); };
        b.MouseLeftButtonDown += (_, e) => { down = true; e.Handled = true; };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (down) { down = false; Close(); } };
        return b;
    }

    void TogglePin()
    {
        cfg.TodoTop = !cfg.TodoTop; Topmost = cfg.TodoTop; cfg.Save();
        BuildHeader(); NotchHub.Ping();
        Toast.Show(cfg.TodoTop ? "To Do: selalu di atas aktif" : "To Do: selalu di atas dimatikan");
    }

    void Rethemed() { BuildHeader(); RenderList(); }

    // ---------- form tambah / ubah ----------
    static Grid Field(TextBox tb, string hint)
    {
        var h = new TextBlock { Text = hint, FontSize = 12.5, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Opacity = .85 };
        h.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
        void Sync() => h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => Sync();
        Sync();
        var g = new Grid(); g.Children.Add(tb); g.Children.Add(h);
        return g;
    }

    FrameworkElement BuildComposer()
    {
        var sp = new StackPanel { Margin = new Thickness(14, 10, 14, 26) };   // bawah lebih lega untuk logo mini
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var f1 = Field(titleBox, "Tugas baru…");
        Grid.SetColumn(addBtn, 1); Grid.SetColumn(cancelBtn, 2);
        row.Children.Add(f1); row.Children.Add(addBtn); row.Children.Add(cancelBtn);
        sp.Children.Add(row);
        sp.Children.Add(Field(noteBox, "Keterangan (opsional)"));
        titleBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };
        noteBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };
        var top = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = sp };
        top.SetResourceReference(Border.BorderBrushProperty, "Line");
        return top;
    }

    void ResetForm()
    {
        editId = null; titleBox.Clear(); noteBox.Clear();
        addBtn.Content = "Tambah"; cancelBtn.Visibility = Visibility.Collapsed;
        RenderList();
    }

    void Edit(TodoItem t)
    {
        editId = t.Id; titleBox.Text = t.Title; noteBox.Text = t.Note;
        addBtn.Content = "Simpan"; cancelBtn.Visibility = Visibility.Visible;
        RenderList();
        titleBox.Focus(); titleBox.SelectAll();
    }

    void Save()
    {
        var tt = titleBox.Text.Trim();
        if (tt == "") { titleBox.Focus(); return; }
        var nn = noteBox.Text.Trim();
        bool added = false;
        if (editId != null && cfg.Todos.FirstOrDefault(x => x.Id == editId) is { } ex) { ex.Title = tt; ex.Note = nn; }
        else { cfg.Todos.Add(new TodoItem { Title = tt, Note = nn }); added = true; }
        cfg.Save(); ResetForm(); NotchHub.Ping();
        if (added) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => scroll.ScrollToEnd()));
        titleBox.Focus();
    }

    // ---------- aksi ----------
    void SetStatus(TodoItem t, string st)
    {
        if (t.Status == st) return;
        t.Status = st; cfg.Save(); RenderList(); NotchHub.Ping();
    }

    void Delete(TodoItem t)
    {
        cfg.Todos.Remove(t); cfg.Save();
        if (editId == t.Id) ResetForm(); else RenderList();
        NotchHub.Ping();
    }

    void ClearDone()
    {
        int n = cfg.Todos.RemoveAll(x => x.Status == TodoSt.Done);
        if (n == 0) { Toast.Show("Belum ada tugas yang selesai"); return; }
        cfg.Save();
        if (editId != null && !cfg.Todos.Any(x => x.Id == editId)) ResetForm(); else RenderList();
        NotchHub.Ping();
        Toast.Show($"{n} tugas selesai dihapus");
    }

    // Menu pilihan status di titik: Done / On Process / Hold + Hapus
    void ShowStatusMenu(FrameworkElement anchor, TodoItem t)
    {
        var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        void Add(string st, string label, string glyph, Color tint)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Icons.Glyph(glyph, 15, Pal.Br(tint), 1.6));
            sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Pal.Br(Pal.Ink) });
            var mi = new MenuItem { Header = sp, IsChecked = t.Status == st };
            mi.Click += (_, _) => SetStatus(t, st);
            m.Items.Add(mi);
        }
        Add(TodoSt.Done, "Done", Icons.CheckFull, Pal.Violet);
        Add(TodoSt.Process, "On Process", Icons.ClockFull, Pal.Cyan);
        Add(TodoSt.Hold, "Hold", Icons.PauseFull, Pal.Dim);

        var dsp = new StackPanel { Orientation = Orientation.Horizontal };
        dsp.Children.Add(Icons.Glyph(Icons.Trash, 15, Pal.Br(Red), 1.6));
        dsp.Children.Add(new TextBlock { Text = "Hapus", Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Pal.Br(Red) });
        var del = new MenuItem { Header = dsp };
        del.Click += (_, _) => Delete(t);
        m.Items.Add(del);
        m.IsOpen = true;
    }

    // ---------- tampilan timeline ----------
    static Color StatusColor(string st) => st == TodoSt.Done ? Pal.Violet : st == TodoSt.Hold ? Pal.Dim : Pal.Cyan;

    static Brush LineBrush(string st)
    {
        if (st == TodoSt.Done) return Pal.CV(90);                       // jalur yang sudah dilewati: gradien penuh
        if (st == TodoSt.Process) return Pal.Br(Pal.Alpha(Pal.Cyan, 0x66));   // sedang jalan: lebih redup
        return Pal.Br(Pal.Track);                                       // belum: abu-abu
    }

    static UIElement Ctr(UIElement e)
    {
        if (e is FrameworkElement f) { f.HorizontalAlignment = HorizontalAlignment.Center; f.VerticalAlignment = VerticalAlignment.Center; }
        return e;
    }

    // Titik status 24 px: Done = terisi gradien + centang, On Process = cincin gradien + jam, Hold = cincin abu + jeda
    static FrameworkElement StatusDot(string st)
    {
        var g = new Grid { Width = 24, Height = 24 };
        if (st == TodoSt.Done)
        {
            g.Children.Add(new Shapes.Ellipse { Fill = Pal.CV(45) });
            g.Children.Add(Ctr(Icons.Glyph(Icons.Check, 13, Pal.Br(Pal.AccentInk), 2.2)));
        }
        else if (st == TodoSt.Process)
        {
            g.Children.Add(new Shapes.Ellipse { Stroke = Pal.CV(45), StrokeThickness = 1.8, Fill = Pal.Br(Pal.Alpha(Pal.Cyan, 0x1C)) });
            g.Children.Add(Ctr(Icons.Glyph(Icons.ClockHands, 13, Pal.Br(Pal.Cyan), 1.8)));
        }
        else
        {
            g.Children.Add(new Shapes.Ellipse { Stroke = Pal.Br(Pal.Alpha(Pal.Dim, 0xA0)), StrokeThickness = 1.6, Fill = Pal.Br(Pal.Alpha(Pal.Dim, 0x14)) });
            g.Children.Add(Ctr(Icons.Glyph(Icons.PauseHands, 13, Pal.Br(Pal.Dim), 1.8)));
        }
        return g;
    }

    void RenderList()
    {
        listHost.Children.Clear();
        UpdateSummary();
        if (cfg.Todos.Count == 0)
        {
            listHost.Children.Add(new TextBlock
            {
                Text = "Belum ada tugas.\nKetik di bawah lalu tekan Enter.", FontSize = 12.5, TextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 50, 20, 0), Foreground = Pal.Br(Pal.Dim)
            });
            return;
        }
        for (int i = 0; i < cfg.Todos.Count; i++) listHost.Children.Add(BuildRow(cfg.Todos[i], i == cfg.Todos.Count - 1));
    }

    FrameworkElement BuildRow(TodoItem t, bool last)
    {
        bool done = t.Status == TodoSt.Done, editing = t.Id == editId;
        var row = new Grid { Margin = new Thickness(14, 0, 8, 0), Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // jalur: garis penghubung ke tugas berikutnya + titik status (klik = ubah status)
        var rail = new Grid();
        if (!last)
            rail.Children.Add(new Border
            {
                Width = 2, CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 28, 0, -3), Background = LineBrush(t.Status)
            });
        var dot = StatusDot(t.Status);
        var pressed = new ScaleTransform(1, 1);
        dot.RenderTransformOrigin = new Point(.5, .5); dot.RenderTransform = pressed;
        var hit = new Border
        {
            Width = 30, Height = 30, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Ubah status",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = dot
        };
        hit.MouseEnter += (_, _) => Pop(pressed, 1.12, 500);
        hit.MouseLeave += (_, _) => Scale(pressed, 1, 160);
        hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowStatusMenu(hit, t); };
        rail.Children.Add(hit);
        row.Children.Add(rail);

        // isi: tanggal kecil + status, judul tebal, keterangan abu-abu
        var date = new TextBlock { FontSize = 11 };
        date.Inlines.Add(new Run(t.Created.ToString("d MMM yyyy, HH:mm", Id)) { Foreground = Pal.Br(Pal.Dim) });
        date.Inlines.Add(new Run("  ·  " + TodoSt.Label(t.Status)) { Foreground = Pal.Br(StatusColor(t.Status)), FontWeight = FontWeights.SemiBold });
        var title = new TextBlock
        {
            Text = t.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0), Foreground = Pal.Br(Pal.Ink)
        };
        if (done) { title.TextDecorations = TextDecorations.Strikethrough; title.Opacity = .5; }
        var col = new StackPanel(); col.Children.Add(date); col.Children.Add(title);
        if (!string.IsNullOrWhiteSpace(t.Note))
            col.Children.Add(new TextBlock
            {
                Text = t.Note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                Foreground = Pal.Br(Pal.Dim), Opacity = done ? .6 : 1
            });
        var content = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 4, 8, 12), Margin = new Thickness(0, 0, 0, 4),
            Background = editing ? Pal.Br(Pal.Hover) : Brushes.Transparent, Cursor = Cursors.Hand, Child = col
        };
        content.MouseLeftButtonUp += (_, e) => { e.Handled = true; Edit(t); };
        Grid.SetColumn(content, 1); row.Children.Add(content);

        // tombol hapus: muncul saat kursor di baris
        var xg = Icons.Glyph("M6,6 L18,18 M18,6 L6,18", 10, Pal.Br(Pal.Dim), 2);
        Ctr(xg);
        var del = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Hapus",
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 4, 0, 0), Child = xg, Opacity = 0, IsHitTestVisible = false
        };
        del.MouseEnter += (_, _) => del.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        del.MouseLeave += (_, _) => del.Background = Brushes.Transparent;
        del.MouseLeftButtonDown += (_, e) => e.Handled = true;
        del.MouseLeftButtonUp += (_, e) => { e.Handled = true; Delete(t); };
        Grid.SetColumn(del, 2); row.Children.Add(del);

        row.MouseEnter += (_, _) => { if (!editing) content.Background = Pal.Br(Pal.Hover); del.IsHitTestVisible = true; del.Opacity = 1; };
        row.MouseLeave += (_, _) => { if (!editing) content.Background = Brushes.Transparent; del.IsHitTestVisible = false; del.Opacity = 0; };
        return row;
    }
}
