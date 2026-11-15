using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace MySecretary;

public sealed class SettingsWindow : AppPanel
{
    readonly Config cfg; readonly Action? onChange; string kind = "app";
    public SettingsWindow(Config c, Action? onChange = null) : base("Pengaturan · bestie", 540, 600)
    {
        cfg = c; this.onChange = onChange;
        var fav = new StackPanel(); var gen = new StackPanel { Visibility = Visibility.Collapsed };
        var s = new StackPanel(); Main.Children.Add(s);
        s.Children.Add(UI.Segmented(new[] { "Library", "Umum" }, 0, i => { fav.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed; gen.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed; }));
        s.Children.Add(fav); s.Children.Add(gen);
        Fav(fav); Gen(gen); Closed += (_, _) => cfg.Save();
    }

    void Fav(StackPanel p)
    {
        var list = new ListBox { ItemsSource = cfg.Pins, Height = 170, Margin = new Thickness(0, 0, 0, 8),
            ItemTemplate = UI.Tpl("<StackPanel><TextBlock Text='{Binding Name}' FontWeight='SemiBold'/><TextBlock Text='{Binding Path}' Foreground='#9A9AA3' FontSize='11' TextTrimming='CharacterEllipsis'/></StackPanel>") };
        p.Children.Add(list);
        p.Children.Add(UI.Row(
            UI.Btn("▲", () => { int i = list.SelectedIndex; if (i > 0) { cfg.Pins.Move(i, i - 1); list.SelectedIndex = i - 1; } }),
            UI.Btn("▼", () => { int i = list.SelectedIndex; if (i >= 0 && i < cfg.Pins.Count - 1) { cfg.Pins.Move(i, i + 1); list.SelectedIndex = i + 1; } }),
            UI.Btn("Hapus", () => { if (list.SelectedItem is Pin x) cfg.Pins.Remove(x); })));
        var name = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        var path = new TextBox { Width = 340, Margin = new Thickness(0, 0, 8, 0) };
        var icon = new TextBox { Width = 340, Margin = new Thickness(0, 0, 8, 0) };
        p.Children.Add(UI.Label("Nama", true, 12)); p.Children.Add(name);
        p.Children.Add(UI.Label("Jenis", true, 12));
        p.Children.Add(UI.Segmented(new[] { "Aplikasi", "Folder", "Link" }, 0, i => kind = new[] { "app", "folder", "url" }[i]));
        p.Children.Add(UI.Label("Lokasi (file, folder, atau URL)", true, 12));
        p.Children.Add(UI.Row(path, UI.Btn("Pilih…", () =>
        {
            if (kind == "folder") { var f = UI.Folder(); if (f != null) path.Text = f; }
            else { var d = new OpenFileDialog(); if (d.ShowDialog() == true) { path.Text = d.FileName; if (name.Text == "") name.Text = System.IO.Path.GetFileNameWithoutExtension(d.FileName); } }
        })));
        p.Children.Add(UI.Label("Ikon sendiri (opsional, png/ico/jpg)", true, 12));
        p.Children.Add(UI.Row(icon, UI.Btn("Pilih…", () => { var d = new OpenFileDialog { Filter = "Gambar|*.png;*.ico;*.jpg;*.jpeg" }; if (d.ShowDialog() == true) icon.Text = d.FileName; })));
        p.Children.Add(UI.Btn("Tambah ke Library", () =>
        {
            if (name.Text.Trim() == "" || path.Text.Trim() == "") { Toast.Show("Isi nama dan lokasi dulu"); return; }
            cfg.Pins.Add(new Pin { Name = name.Text.Trim(), Path = path.Text.Trim(), Kind = kind, Icon = icon.Text.Trim() == "" ? null : icon.Text.Trim() });
            name.Clear(); path.Clear(); icon.Clear();
        }, true));
    }

    void Gen(StackPanel p)
    {
        p.Children.Add(UI.Label("Tema", true, 12));
        p.Children.Add(UI.Segmented(new[] { "Black", "Terang" }, cfg.Light ? 1 : 0, i =>
        {
            bool l = i == 1; if (l == cfg.Light) return;
            cfg.Light = l; cfg.Save(); Pal.Set(l);
        }));
        p.Children.Add(UI.Label("Buka panel dari jam dengan", true, 12));
        p.Children.Add(UI.Segmented(new[] { "Klik jam", "Hover jam" }, cfg.OpenMode == "hover" ? 1 : 0, i => { cfg.OpenMode = i == 1 ? "hover" : "click"; cfg.Save(); }));
        p.Children.Add(UI.Toggle("Buka panel saat kursor menyentuh tepi kiri layar (di luar jam)", cfg.Edge, v => { cfg.Edge = v; cfg.Save(); }));
        p.Children.Add(UI.Toggle("Pengingat waktu adzan (pop-up + bunyi bel lembut)", cfg.Adzan, v => { cfg.Adzan = v; cfg.Save(); }));
        p.Children.Add(UI.Row(UI.Btn("Tes pengingat adzan", () => { Chime.Play(); AdzanTip.Notify("Maghrib", DateTime.Now, cfg.PrayerCity); })));
        p.Children.Add(UI.Label($"Jadwal sholat: {cfg.PrayerCity} (metode Kemenag RI), lokasi mengikuti koneksi internet.", true, 12));
        var pre = new TextBox { Text = cfg.Presets, Width = 300 }; pre.TextChanged += (_, _) => cfg.Presets = pre.Text;
        p.Children.Add(UI.Label("Pilihan waktu Pomodoro (menit, pisahkan koma)")); p.Children.Add(UI.Row(pre));
        var tpre = new TextBox { Text = cfg.TimerPresets, Width = 300 }; tpre.TextChanged += (_, _) => cfg.TimerPresets = tpre.Text;
        p.Children.Add(UI.Label("Pilihan durasi Timer yang ditampilkan (menit, pisahkan koma)")); p.Children.Add(UI.Row(tpre));
        var tdef = new TextBox { Text = cfg.TimerMin.ToString(), Width = 80 };
        tdef.TextChanged += (_, _) => { if (int.TryParse(tdef.Text.Trim(), out var m) && m >= 1 && m <= 600) cfg.TimerMin = m; };
        p.Children.Add(UI.Label("Durasi awal Timer saat dibuka (menit)")); p.Children.Add(UI.Row(tdef));
        p.Children.Add(UI.Label("Pilihan durasi berlaku saat Timer dibuka lagi.", true, 12));
        p.Children.Add(UI.Label("Arahkan kursor ke jam di tepi kiri untuk menariknya keluar, klik untuk membuka bestie.", true, 12));
    }
}
