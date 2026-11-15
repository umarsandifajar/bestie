using System.Diagnostics;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using static MySecretary.Fx;
namespace MySecretary;

sealed record Entry(string Key, string Name, Func<double, UIElement> Icon, Action Run, bool Tool, bool KeepOpen = false);

// Panel "bestie": tersembunyi, meluncur keluar dari tepi kiri layar. Dibuka lewat klik jam kecil (IslandWindow)
// atau kursor menempel lama di tepi kiri.
public sealed class SecretaryWindow : Window
{
    const double PanelW = 264, F = Edge.F, Slack = 14, FishH = 236, FishPanelH = 396;   // Slack: ruang transparan di sekeliling panel supaya efek membal tidak terpotong
    const string PomoKey = "tool:Pomodoro";
    static readonly string[] TabNames = { "Semua", "Pin", "Hide" };

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    static double Dpi { get { var w = SystemParameters.PrimaryScreenWidth; return w > 0 ? GetSystemMetrics(0) / w : 1; } }

    readonly Config cfg;
    readonly Grid host = new();                      // isi jendela (transparan, sedikit lebih besar dari panel)
    readonly Grid anim = new();                      // seluruh panel; inilah yang meluncur dan membal
    readonly TranslateTransform animT = new(-420, 0);
    readonly ScaleTransform bounce = new(1, 1);
    Grid card = new();
    ScrollViewer scroll = new(); StackPanel list = new();     // scroll + list: hanya untuk tab Prayer & pesan kosong
    FisheyeList fish = new(); Grid body = new(); FitBox fit = new();   // fit: tab Prayer (muat-pas, tanpa scroll)               // daftar item bergaya fisheye
    readonly List<(SolidColorBrush bg, SolidColorBrush fg)> chipB = new();
    List<Entry> current = new();
    TextBlock? pomoLabel;
    int tab, gen, dwell, idwell, cool, leave, menus; bool shown;
    EdgeShape edgeBg = new();
    readonly DispatcherTimer poll = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(40) };

    bool pomoOn; DateTime pomoEnd; string pomoText = ""; readonly DispatcherTimer pomoT = new() { Interval = TimeSpan.FromMilliseconds(500) };
    SettingsWindow? settings; IslandWindow? island;
    PrayerView? prayerView; Border? prayerChip;
    readonly DispatcherTimer prayerT = new() { Interval = TimeSpan.FromSeconds(1) }; string lastAdzan = ""; int geoTick;
    readonly Dictionary<string, Window> tools = new();

    public SecretaryWindow(Config c)
    {
        cfg = c;
        Title = "bestie";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = PanelW + 1 + Slack; Height = 560;
        anim.Width = PanelW + 1; anim.HorizontalAlignment = HorizontalAlignment.Left; anim.VerticalAlignment = VerticalAlignment.Center;
        anim.RenderTransformOrigin = new Point(0, .5);
        anim.RenderTransform = new TransformGroup { Children = { bounce, animT } };
        anim.Opacity = 0; host.Children.Add(anim); Content = host;
        anim.MouseEnter += (_, _) => Bounce(true); anim.MouseLeave += (_, _) => Bounce(false);
        BuildUi();
        poll.Tick += Poll; pomoT.Tick += (_, _) => PomoTick();
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (shown && !Busy && !IsActive) Dismiss(); }));
        PreviewKeyDown += OnKey;
        Pal.Changed += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Rethemed));
    }

    // Dipanggil sekali saat aplikasi mulai: buat HWND (tanpa menampilkan), sembunyikan dari Alt+Tab, pasang pendengar clipboard, tampilkan jam di tepi kiri.
    public void Start()
    {
        var h = new WindowInteropHelper(this).EnsureHandle();
        SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80);   // WS_EX_TOOLWINDOW
        HwndSource.FromHwnd(h)?.AddHook(ClipStore.Hook); ClipStore.Start(h);
        island = new IslandWindow { Enabled = true }; island.Clicked += Reveal; island.SettingsClicked += OpenSettings; island.BadgeClicked += OnBadge; island.Set(true);
        NotchHub.Changed += RefreshBadges; RefreshBadges();   // ikon To Do / alarm / pengingat di notch
        poll.Start();
        prayerT.Tick += (_, _) => AdzanCheck(); prayerT.Start();
        _ = Geo.Refresh(cfg);
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (_, e) => { if (e.IsAvailable) Dispatcher.BeginInvoke(new Action(() => { _ = Geo.Refresh(cfg); })); };
    }

    void ApplyIsland() { if (island == null) return; island.Enabled = true; island.Set(!shown); }
    void Rethemed() { BuildUi(); if (shown) Refresh(false, true); RefreshBadges(); }

    // ---------- ikon status di notch (di atas volume) ----------
    // To Do (ungu): muncul selama ada tugas yang belum Done dan kotaknya sedang tertutup (atau tidak "selalu di atas", supaya tidak hilang di balik jendela lain).
    // Lonceng kuning = ada alarm aktif, lonceng cyan = ada pengingat aktif. Klik ikon = buka kotak / panelnya.
    void RefreshBadges()
    {
        if (island == null) return;
        var l = new List<NotchBadge>();
        int todo = cfg.Todos.Count(x => x.Status != TodoSt.Done);
        if (todo > 0 && (!TodoWindow.IsOpen || !cfg.TodoTop)) l.Add(new NotchBadge("todo", Icons.Todo, Pal.Violet, todo));
        int al = cfg.Reminders.Count(r => r.Alarm && (r.Enabled || RemindEngine.IsSnoozed(r.Id)));
        int rm = cfg.Reminders.Count(r => !r.Alarm && (r.Enabled || RemindEngine.IsSnoozed(r.Id)));
        if (al > 0) l.Add(new NotchBadge("alarm", Icons.Bell, Pal.Light ? Color.FromRgb(0xCA, 0x8A, 0x04) : Color.FromRgb(0xFA, 0xCC, 0x15), al));
        if (rm > 0) l.Add(new NotchBadge("remind", Icons.Bell, Pal.Cyan, rm));
        island.SetBadges(l);
    }

    void OnBadge(string key)
    {
        if (key == "todo") OpenTodo();
        else OpenTool("remind", () => new RemindersPanel(cfg));
    }

    void OpenTodo()
    {
        if (tools.TryGetValue("todo", out var w)) { w.Topmost = true; w.Activate(); w.Topmost = cfg.TodoTop; return; }   // sudah terbuka: bawa ke depan
        OpenTool("todo", () => new TodoWindow(cfg));
    }

    // ---------- UI ----------
    void BuildUi()
    {
        anim.Children.Clear(); chipB.Clear();
        edgeBg = new EdgeShape(); anim.Children.Add(edgeBg);                                   // latar: menempel di tepi layar, lengkung cekung di atas & bawah
        card = new Grid { Margin = new Thickness(0, F, 0, F) };               // isi panel
        card.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Pal.Br(Pal.Ink));
        anim.Children.Add(card);

        var g = card;
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- header: logo "bestie" (bes = putih, tie = gradien cyan-violet) + tombol Donate
        var head = new Grid { Margin = new Thickness(18, 14, 14, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        logo.Children.Add(new TextBlock { Text = "bes", FontFamily = Fx.Jakarta, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Pal.Br(Pal.Ink) });
        logo.Children.Add(new TextBlock { Text = "tie", FontFamily = Fx.Jakarta, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Pal.CV(0, true) });
        head.Children.Add(logo);
        var don = DonateBtn(); Grid.SetColumn(don, 1); head.Children.Add(don);
        g.Children.Add(head);

        // --- tab: Semua · Pin · Hide
        var chips = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, -5, 2) };
        for (int i = 0; i < TabNames.Length; i++) chips.Children.Add(Chip(TabNames[i], i));
        chips.Children.Add(PrayerBtn());
        Grid.SetRow(chips, 1); g.Children.Add(chips);

        // --- daftar: fisheye (item aktif di tengah, sisanya mengecil + memudar). scroll hanya dipakai tab Prayer / pesan kosong.
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, Visibility = Visibility.Collapsed };
        scroll.OpacityMask = new LinearGradientBrush(new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), .025),
            new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), .95), new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1)
        }, 90);
        list = new StackPanel { Margin = new Thickness(0, 6, 0, 8) }; scroll.Content = list;
        fish = new FisheyeList { Height = FishH, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        body = new Grid(); body.Children.Add(scroll); body.Children.Add(fish);
        fit = new FitBox { Visibility = Visibility.Collapsed }; body.Children.Add(fit);
        Grid.SetRow(body, 2); g.Children.Add(body);

        // --- footer: hanya pengaturan (kiri) dan + (kanan)
        Border gear = null!, plus = null!;
        var gearG = Icons.Glyph(Icons.Gear, 15, Pal.Br(Pal.Ink), 1.4); var plusG = Icons.Glyph(Icons.Plus, 14, Pal.Br(Pal.Ink), 1.5);
        gear = Pill(gearG, 30, 28, 8, () => GearMenu(gear));
        plus = Pill(plusG, 30, 28, 8, () => PlusMenu(plus));
        Icons.Tint(gear, gearG, Pal.Ink); Icons.Tint(plus, plusG, Pal.Ink);
        plus.HorizontalAlignment = HorizontalAlignment.Right;
        var foot = new Grid { Margin = new Thickness(14, 2, 14, 6) };
        foot.Children.Add(gear); foot.Children.Add(plus);
        Grid.SetRow(foot, 3); g.Children.Add(foot);

        // --- kredit kecil, satu baris
        var credit = new TextBlock
        {
            Text = "Made with love by Fajar Umarsandi", FontFamily = Fx.Jakarta, FontSize = 9, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.NoWrap,
            Foreground = Pal.Br(Pal.Alpha(Pal.Ink, 0x80)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10), IsHitTestVisible = false
        };
        Grid.SetRow(credit, 4); g.Children.Add(credit);
    }

    // Tombol Donate: kuning lembut, hover membal halus, buka sandiana.my.id
    Border DonateBtn()
    {
        var bg = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A)); var ink = Color.FromRgb(0x42, 0x2C, 0x06);
        var st = new ScaleTransform(1, 1);
        var heart = Icons.Glyph(Icons.Heart, 11, Pal.Br(ink), 1.8, true);
        ((FrameworkElement)heart).VerticalAlignment = VerticalAlignment.Center;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(heart);
        sp.Children.Add(new TextBlock { Text = "Donate", FontFamily = Fx.Jakarta, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Pal.Br(ink), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
        var b = new Border
        {
            Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(11, 0, 12, 0), Background = bg, Child = sp, Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = st, ToolTip = "sandiana.my.id", VerticalAlignment = VerticalAlignment.Center
        };
        bool down = false;
        b.MouseEnter += (_, _) => { ToC(bg, Color.FromRgb(0xFC, 0xD3, 0x4D), 160); Pop(st, 1.07, 560); };
        b.MouseLeave += (_, _) => { down = false; ToC(bg, Color.FromRgb(0xFD, 0xE6, 0x8A), 220); Scale(st, 1, 180); };
        b.MouseLeftButtonDown += (_, _) => { down = true; Scale(st, .95, 80); };
        b.MouseLeftButtonUp += (_, _) =>
        {
            Scale(st, 1.04, 160); if (!down) return; down = false;
            try { Process.Start(new ProcessStartInfo("https://sandiana.my.id") { UseShellExecute = true }); }
            catch (Exception ex) { Toast.Show("Gagal membuka link: " + ex.Message); }
        };
        return b;
    }

    // Island panel membal halus saat kursor masuk / keluar
    void Bounce(bool on)
    {
        if (on && !shown) return;
        if (on) Pop(bounce, 1.018, 650);
        else { To(bounce, ScaleTransform.ScaleXProperty, 1, 320); To(bounce, ScaleTransform.ScaleYProperty, 1, 320); }
    }

    // Tombol footer: hover halus (tanpa glow).
    Border Pill(UIElement child, double w, double h, double radius, Action click)
    {
        var bg = new SolidColorBrush(Pal.Raised); var st = new ScaleTransform(1, 1);
        if (child is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        var b = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = bg, BorderBrush = Pal.Br(Pal.Stroke), BorderThickness = new Thickness(1),
            Child = child, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(.5, .5), RenderTransform = st, HorizontalAlignment = HorizontalAlignment.Left
        };
        bool down = false;
        b.MouseEnter += (_, _) => ToC(bg, Pal.Hover, 140);
        b.MouseLeave += (_, _) => { down = false; ToC(bg, Pal.Raised, 200); Scale(st, 1, 140); };
        b.MouseLeftButtonDown += (_, _) => { down = true; Scale(st, .95, 80); };
        b.MouseLeftButtonUp += (_, _) => { Scale(st, 1, 160); if (!down) return; down = false; click(); };
        return b;
    }

    Border Chip(string label, int idx)
    {
        bool on = idx == tab;
        var bg = new SolidColorBrush(on ? Pal.Ink : Pal.Raised); var fg = new SolidColorBrush(on ? Pal.Bg : Pal.Dim);
        chipB.Add((bg, fg));
        var b = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 5, 0), Background = bg,
            Cursor = Cursors.Hand, Child = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = fg }
        };
        b.MouseEnter += (_, _) => { if (idx != tab) { ToC(bg, Pal.Hover, 140); ToC(fg, Pal.Ink, 140); } };
        b.MouseLeave += (_, _) => { if (idx != tab) { ToC(bg, Pal.Raised, 200); ToC(fg, Pal.Dim, 200); } };
        b.MouseLeftButtonUp += (_, _) => SetTab(idx);
        return b;
    }

    // Tombol "Prayer": tab keempat (gradien cyan -> violet). Isinya jadwal sholat tampil di daftar panel.
    Border PrayerBtn()
    {
        var st = new ScaleTransform(1, 1);
        var b = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 5, 0), Background = Pal.CV(0), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = tab == 3 ? Pal.Br(Pal.Ink) : Brushes.Transparent,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = st,
            Child = new TextBlock { Text = "Prayer", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Pal.Br(Pal.AccentInk) }
        };
        prayerChip = b;
        bool down = false;
        b.MouseEnter += (_, _) => Pop(st, 1.07, 560);
        b.MouseLeave += (_, _) => { down = false; Scale(st, 1, 180); };
        b.MouseLeftButtonDown += (_, _) => { down = true; Scale(st, .95, 80); };
        b.MouseLeftButtonUp += (_, _) => { Scale(st, 1.03, 160); if (!down) return; down = false; SetTab(3); };
        return b;
    }

    // Tiap detik: bunyikan bel + pop-up saat waktu adzan tiba (hanya dalam 60 detik pertama, tidak diulang)
    void AdzanCheck()
    {
        if (++geoTick % 1800 == 0) _ = Geo.Refresh(cfg);   // perbarui lokasi tiap 30 menit
        try { RemindEngine.Tick(cfg); } catch { }          // alarm & pengingat (jalan terus walau panel tertutup)
        if (!cfg.Adzan) return;
        var now = DateTime.Now; var ts = PrayerCalc.Day(now.Date, cfg.PrayerLat, cfg.PrayerLon);
        for (int i = 0; i < 5; i++)
        {
            var d = (now - ts[i]).TotalSeconds;
            if (d < 0 || d > 60) continue;
            var key = now.ToString("yyyyMMdd") + i;
            if (key == lastAdzan) return;
            lastAdzan = key;
            Chime.Play(); AdzanTip.Notify(PrayerCalc.Names[i], ts[i], cfg.PrayerCity);
            return;
        }
    }

    void SetTab(int i)
    {
        if (i == tab) return; tab = i;
        for (int k = 0; k < chipB.Count; k++) { bool on = k == tab; ToC(chipB[k].bg, on ? Pal.Ink : Pal.Raised, 220); ToC(chipB[k].fg, on ? Pal.Bg : Pal.Dim, 220); }
        if (prayerChip != null) prayerChip.BorderBrush = tab == 3 ? Pal.Br(Pal.Ink) : Brushes.Transparent;
        Refresh(true, false);
        if (shown) To(anim, FrameworkElement.HeightProperty, TargetH(), 380, EzOut);   // panel memanjang untuk tab Prayer, memendek untuk daftar fisheye
    }

    // Tinggi panel: daftar fisheye pendek; tab Prayer butuh ruang penuh.
    double TargetH()
    {
        var wa = SystemParameters.WorkArea;
        double full = Math.Min(520, wa.Height - 2 * F - 2 * Slack - 8) + 2 * F;
        return tab == 3 ? full : Math.Min(full, FishPanelH);
    }

    // ---------- daftar item ----------
    bool IsFav(string k) => cfg.Favs.Contains(k);
    bool IsHidden(string k) => cfg.Hidden.Contains(k);
    void ToggleHide(string k) { if (!cfg.Hidden.Remove(k)) cfg.Hidden.Add(k); cfg.Save(); }
    void ToggleFav(string k) { if (!cfg.Favs.Remove(k)) cfg.Favs.Add(k); cfg.Save(); }
    void Remember(string k) { cfg.Recent.Remove(k); cfg.Recent.Insert(0, k); while (cfg.Recent.Count > 8) cfg.Recent.RemoveAt(cfg.Recent.Count - 1); cfg.Save(); }

    static Entry Mk(string name, string glyph, Action run, bool keep = false) => new("tool:" + name, name, s => Icons.Tile(glyph, s), run, true, keep);
    List<Entry> AllEntries()
    {
        var l = new List<Entry>
        {
            Mk("Pomodoro", Icons.Pomo, TogglePomo, true),
            Mk("Folder Organizer", Icons.Organize, () => OpenTool("org", () => new FolderOrganizerPanel())),
            Mk("Bulk File Renamer", Icons.Rename, Soon),
            Mk("File Size Checker", Icons.SizeChk, () => OpenTool("size", () => new SizePanel())),
            Mk("Clipboard History", Icons.Clip, () => OpenTool("clip", () => new ClipPanel())),
            Mk("Text Formatter", Icons.TextFmt, () => OpenTool("text", () => new TextPanel())),
            Mk("Notes", Icons.Note, () => OpenTool("note", () => new NotePanel())),
            Mk("To Do / My Task", Icons.Todo, OpenTodo),
            Mk("Floating Timer", Icons.Timer, () => OpenTool("timer", () => new TimerPanel(cfg))),
            Mk("Reminders & Alarm", Icons.Bell, () => OpenTool("remind", () => new RemindersPanel(cfg))),
            Mk("Color Picker", Icons.Pick, () => OpenTool("color", () => new ColorPanel())),
            Mk("Doc Converter", Icons.Pdf, () => OpenTool("pdf", () => new DocConverterPanel())),
            Mk("Batch Watermark", Icons.Mark, () => OpenTool("wm", () => new WatermarkPanel(cfg))),
        };
        foreach (var p in cfg.Pins) { var pp = p; l.Add(new Entry("pin:" + pp.Name + "|" + pp.Path, pp.Name, s => Icons.ForPin(pp, s), () => OpenPin(pp), false)); }
        return l;
    }
    static void Soon() => Toast.Show("Segera hadir di batch berikutnya");
    static void OpenPin(Pin p)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(p.Path)) { UseShellExecute = true }); }
        catch (Exception ex) { Toast.Show("Gagal membuka " + p.Name + ": " + ex.Message); }
    }
    void OpenTool(string key, Func<Window> make)
    {
        if (tools.TryGetValue(key, out var w)) { w.Activate(); return; }
        var nw = make(); tools[key] = nw; nw.Closed += (_, _) => tools.Remove(key); nw.Show(); nw.Activate();
    }

    void Refresh(bool animate, bool keepPos)
    {
        prayerView?.Stop(); prayerView = null;
        fit.Child = null; fit.Visibility = Visibility.Collapsed;
        if (tab == 3)
        {
            // Prayer: tanpa ScrollViewer. FitBox mengecilkan isi secara proporsional supaya semuanya muat di panel.
            fish.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Collapsed;
            list.Children.Clear(); pomoLabel = null;
            var pv = new PrayerView(cfg); prayerView = pv; fit.Child = pv; fit.Visibility = Visibility.Visible; pv.Start();
            fit.BeginAnimation(OpacityProperty, null);
            if (animate) { fit.Opacity = 0; var tr = new TranslateTransform(0, 12); fit.RenderTransform = tr; To(fit, OpacityProperty, 1, 340, EzOut, 110); To(tr, TranslateTransform.YProperty, 0, 420, EzOut, 110); }
            else { fit.Opacity = 1; fit.RenderTransform = Transform.Identity; }
            return;
        }
        string? keepKey = keepPos && fish.Count > 0 && fish.Index < current.Count ? current[fish.Index].Key : null;   // fokus tetap di item yang sama
        int oldIdx = fish.Index;
        var all = AllEntries(); IEnumerable<Entry> src;
        var vis = all.Where(e => !IsHidden(e.Key));
        if (tab == 1) src = vis.Where(e => IsFav(e.Key));
        else if (tab == 2) src = all.Where(e => IsHidden(e.Key));
        else src = vis.OrderBy(e => IsFav(e.Key) ? 0 : 1);   // yang disematkan di atas
        current = src.ToList();
        pomoLabel = null;
        if (current.Count == 0)
        {
            fish.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible;
            list.Children.Clear();
            var msg = tab == 1 ? "Belum ada yang disematkan.\nTekan ikon pin di sebelah kanan item." : tab == 2 ? "Tidak ada yang disembunyikan.\nTekan ikon mata di sebelah item." : "Kosong";
            list.Children.Add(new TextBlock { Text = msg, Foreground = Pal.Br(Pal.Dim), FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(20, 40, 20, 0) });
            return;
        }
        int start = 0;
        if (keepKey != null) { int k = current.FindIndex(e => e.Key == keepKey); start = k >= 0 ? k : Math.Min(oldIdx, current.Count - 1); }
        else if (keepPos) start = Math.Min(oldIdx, current.Count - 1);
        scroll.Visibility = Visibility.Collapsed; fish.Visibility = Visibility.Visible;
        fish.SetRows(current.Select(Row), start, animate);
        fish.BeginAnimation(OpacityProperty, null);
        if (animate) { fish.Opacity = 0; To(fish, OpacityProperty, 1, 320, EzOut, 90); } else fish.Opacity = 1;
    }

    FishRow Row(Entry e)
    {
        bool fav = IsFav(e.Key), hid = IsHidden(e.Key);
        var root = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var press = new ScaleTransform(1, 1);
        var g = new Grid { RenderTransformOrigin = new Point(.5, .5), RenderTransform = press }; root.Child = g;

        // sorotan item aktif (di tengah): latar halus polos (tanpa garis warna). Opasitasnya mengikuti kedekatan dengan tengah.
        var hl = new Border
        {
            Opacity = 0, IsHitTestVisible = false, CornerRadius = new CornerRadius(12), Background = Pal.Br(Pal.Hover)
        };
        g.Children.Add(hl);

        var c = new Grid { Margin = new Thickness(9, 0, 6, 0) };
        c.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); c.ColumnDefinitions.Add(new ColumnDefinition()); c.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Grid { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
        var iconEl = e.Icon(28); icon.Children.Add(iconEl);
        Icons.Tint(root, iconEl, Pal.Ink);                                   // hover: ikon jadi gradien cyan-violet
        var name = new TextBlock
        {
            Text = e.Name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 1);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 2);
        if (e.Key == PomoKey)
        {
            pomoLabel = new TextBlock { Foreground = Pal.Br(Pal.Cyan), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Text = pomoOn ? pomoText : "" };
            right.Children.Add(pomoLabel);
        }

        // tombol kecil di kanan item: hide (mata) dan pin. Opasitas luar diatur fisheye (hanya jelas di item aktif), opasitas ikon diatur hover.
        Border Mini(string glyph, string tip, bool filled, double rest, Action act)
        {
            var gl = Icons.Glyph(glyph, 13, Pal.Br(Pal.Dim), 1.5, filled);
            var b = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = gl, Opacity = 0 };
            if (gl is FrameworkElement f) { f.HorizontalAlignment = HorizontalAlignment.Center; f.VerticalAlignment = VerticalAlignment.Center; f.Opacity = rest; }
            Icons.Tint(b, gl, Pal.Dim);
            b.MouseLeftButtonDown += (_, ev) => ev.Handled = true;
            b.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; act(); };
            b.MouseEnter += (_, _) => To(gl, OpacityProperty, 1, 120);
            b.MouseLeave += (_, _) => To(gl, OpacityProperty, rest, 160);
            return b;
        }
        var hide = Mini(hid ? Icons.Eye : Icons.EyeOff, hid ? "Tampilkan lagi" : "Sembunyikan", false, .55, () => { ToggleHide(e.Key); Refresh(false, true); });
        var pin = Mini(Icons.PinG, fav ? "Lepas sematan" : "Sematkan", fav, fav ? .95 : .55, () => { ToggleFav(e.Key); Refresh(false, true); });
        right.Children.Add(hide); right.Children.Add(pin);
        c.Children.Add(icon); c.Children.Add(name); c.Children.Add(right); g.Children.Add(c);

        bool down = false;
        root.MouseLeave += (_, _) => { down = false; Scale(press, 1, 140); };
        root.MouseLeftButtonDown += (_, _) => { down = true; Scale(press, .985, 80); };
        root.MouseLeftButtonUp += (_, _) =>
        {
            Scale(press, 1, 160); if (!down) return; down = false;
            int idx = current.IndexOf(e);
            if (idx < 0) return;
            if (idx == fish.Index) Launch(e); else fish.FocusTo(idx);   // klik item samping: bawa ke tengah dulu; klik item tengah: jalankan
        };

        return new FishRow(root, ad =>
        {
            double cl = Math.Clamp(1 - ad * 1.7, 0, 1);
            hl.Opacity = cl;
            hide.Opacity = cl; pin.Opacity = fav ? Math.Max(cl, .9) : cl;
            bool act = ad < .45; hide.IsHitTestVisible = act; pin.IsHitTestVisible = act;
        });
    }

    void Launch(Entry e)
    {
        Remember(e.Key);
        if (e.KeepOpen) { e.Run(); return; }
        Dismiss();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { try { e.Run(); } catch (Exception ex) { Toast.Show("Gagal: " + ex.Message); } }));
    }

    // ---------- muncul / sembunyi ----------
    void Reveal()
    {
        if (shown) return;
        shown = true; gen++; dwell = 0; leave = 0;
        island?.Set(false);
        var wa = SystemParameters.WorkArea;
        double full = Math.Min(520, wa.Height - 2 * F - 2 * Slack - 8) + 2 * F;   // jendela setinggi maksimum; panel di dalamnya memanjang/memendek sesuai tab
        anim.BeginAnimation(FrameworkElement.HeightProperty, null); anim.Height = TargetH();
        Width = PanelW + 1 + Slack; Height = full + 2 * Slack; Left = wa.Left; Top = wa.Top + (wa.Height - Height) / 2;
        bounce.BeginAnimation(ScaleTransform.ScaleXProperty, null); bounce.BeginAnimation(ScaleTransform.ScaleYProperty, null); bounce.ScaleX = bounce.ScaleY = 1; edgeBg.BeginAnimation(EdgeShape.AccentProperty, null); edgeBg.Accent = 0;
        Refresh(true, false);
        Show();
        To(animT, TranslateTransform.XProperty, 0, 420, EzOut);
        To(anim, OpacityProperty, 1, 240);
    }

    void Dismiss()
    {
        if (!shown) return;
        shown = false; cool = 25; var gg = ++gen;
        var a = new DoubleAnimation(-(PanelW + 12), TimeSpan.FromMilliseconds(280)) { EasingFunction = EzIn };
        a.Completed += (_, _) => { if (gg == gen && !shown) { base.Hide(); prayerView?.Stop(); island?.Set(true); } };
        animT.BeginAnimation(TranslateTransform.XProperty, a);
        To(anim, OpacityProperty, 0, 260, EzIn);
    }

    bool Busy => menus > 0 || PrayerView.PopupOpen > 0;

    void Poll(object? s, EventArgs e)
    {
        if (!GetCursorPos(out var p)) return;
        double k = Dpi, x = p.X / k, y = p.Y / k;
        if (!shown)
        {
            // jam di tepi kiri: kursor menyentuh -> ditarik keluar; klik -> buka panel
            bool onIsland = island != null && island.Over(x, y);
            island?.SetHover(onIsland);
            if (cool > 0) { cool--; idwell = 0; }
            else idwell = island != null && island.OverClock(x, y) && cfg.OpenMode == "hover" ? idwell + 1 : 0;   // mode hover: tahan kursor ~0,45 dtk di angka jam (bukan di slider/mic)
            if (idwell >= 11) { idwell = 0; Reveal(); return; }
            bool atEdge = cfg.Edge && !onIsland && x >= -1 && x <= 1.5 && y >= 0 && y <= SystemParameters.PrimaryScreenHeight;
            bool pressed = (GetAsyncKeyState(0x01) & 0x8000) != 0;   // jangan ganggu saat seret jendela ke tepi
            dwell = atEdge && !pressed ? dwell + 1 : 0;
            if (dwell >= 3) Reveal();
            return;
        }
        double L = Left, T = Top;
        double ph = Math.Max(anim.ActualHeight, 100), pt = T + (Height - ph) / 2;   // panel berada di tengah jendela
        bool inside = (x >= L - 2 && x <= L + PanelW + 14 && y >= pt + F - 14 && y <= pt + ph - F + 14);
        bool sliver = x >= -1 && x <= 1.5;   // masih menempel di tepi
        if (inside || sliver || Busy) leave = 0;
        else { leave++; if (leave * 40 >= 450) Dismiss(); }
    }

    void OnKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        else if (e.Key == Key.Down && tab != 3) { fish.Step(1); e.Handled = true; }
        else if (e.Key == Key.Up && tab != 3) { fish.Step(-1); e.Handled = true; }
        else if (e.Key == Key.Enter && tab != 3 && fish.Count > 0 && fish.Index < current.Count) { Launch(current[fish.Index]); e.Handled = true; }
    }

    // ---------- footer: menu ----------
    ContextMenu NewMenu(UIElement target)
    {
        var m = new ContextMenu { Placement = PlacementMode.Top, PlacementTarget = target };
        m.Opened += (_, _) => menus++; m.Closed += (_, _) => menus--;
        return m;
    }
    static MenuItem Mi(string t, Action a) { var m = new MenuItem { Header = t }; m.Click += (_, _) => a(); return m; }

    void GearMenu(UIElement target)
    {
        var m = NewMenu(target);
        m.Items.Add(Mi("Pengaturan…", OpenSettings));
        m.Items.Add(Mi("Keluar", () => Application.Current.Shutdown()));
        m.IsOpen = true;
    }
    void PlusMenu(UIElement target)
    {
        var m = NewMenu(target);
        m.Items.Add(Mi("Tambah aplikasi…", AddApp));
        m.Items.Add(Mi("Tambah folder…", AddFolder));
        m.Items.Add(Mi("Tambah link dari clipboard", AddLink));
        m.IsOpen = true;
    }
    void AddApp()
    {
        Dismiss();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var d = new OpenFileDialog { Title = "Pilih aplikasi atau file", Filter = "Semua file|*.*" };
            if (d.ShowDialog() != true) return;
            cfg.Pins.Add(new Pin { Name = System.IO.Path.GetFileNameWithoutExtension(d.FileName), Path = d.FileName, Kind = "app" });
            cfg.Save(); Toast.Show("Ditambahkan ke bestie");
        }));
    }
    void AddFolder()
    {
        Dismiss();
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var f = UI.Folder(); if (f == null) return;
            var nm = System.IO.Path.GetFileName(f.TrimEnd('\\', '/')); if (string.IsNullOrEmpty(nm)) nm = f;
            cfg.Pins.Add(new Pin { Name = nm, Path = f, Kind = "folder" });
            cfg.Save(); Toast.Show("Folder ditambahkan");
        }));
    }
    void AddLink()
    {
        string t = ""; try { t = Clipboard.GetText().Trim(); } catch { }
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) { Toast.Show("Salin dulu sebuah link (http…), lalu tekan lagi"); return; }
        cfg.Pins.Add(new Pin { Name = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host, Path = t, Kind = "url" });
        cfg.Save(); Refresh(false, true); Toast.Show("Link ditambahkan");
    }
    void OpenSettings()
    {
        Dismiss();
        if (settings == null) { settings = new SettingsWindow(cfg, ApplyIsland); settings.Closed += (_, _) => settings = null; settings.Show(); }
        else settings.Activate();
    }

    // ---------- pomodoro ----------
    void TogglePomo()
    {
        if (pomoOn) { StopPomo(); Toast.Show("Pomodoro dihentikan"); return; }
        var m = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = card };
        m.Opened += (_, _) => menus++; m.Closed += (_, _) => menus--;
        foreach (var min in cfg.PresetList())
        {
            int mm = min; var mi = new MenuItem { Header = $"Fokus {mm} menit", IsChecked = mm == cfg.PomodoroMin };
            mi.Click += (_, _) => { StartPomo(mm); Dismiss(); }; m.Items.Add(mi);
        }
        m.IsOpen = true;
    }
    void StartPomo(int min)
    {
        cfg.PomodoroMin = min; cfg.Save();
        pomoOn = true; pomoEnd = DateTime.Now.AddMinutes(min); pomoT.Start(); PomoTick();
        Toast.Show($"Fokus {min} menit dimulai");
    }
    void PomoTick()
    {
        var rem = (pomoEnd - DateTime.Now).TotalSeconds;
        if (rem <= 0) { StopPomo(); SystemSounds.Exclamation.Play(); Toast.Show("Sesi fokus selesai, istirahat dulu"); return; }
        pomoText = $"{(int)rem / 60}:{(int)rem % 60:00}";
        island?.SetTimer(((int)rem / 60).ToString("00"), ((int)rem % 60).ToString("00"));
        if (pomoLabel != null) pomoLabel.Text = pomoText;
    }
    void StopPomo() { pomoOn = false; pomoT.Stop(); pomoText = ""; if (pomoLabel != null) pomoLabel.Text = ""; island?.SetTimer(null, null); }
}
