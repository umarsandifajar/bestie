using System.Collections.ObjectModel;
using System.Text.Json;
namespace MySecretary;

public class Pin
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "app";   // app | folder | url
    public string? Icon { get; set; }           // optional png/ico/jpg
}

public class Config
{
    public int PomodoroMin { get; set; } = 25;
    public bool Light { get; set; } = false;    // tema: false = Black, true = Terang
    public bool Island { get; set; } = true;    // jam kecil menempel di tepi kiri layar
    public bool Edge { get; set; } = false;     // buka panel saat kursor menyentuh tepi kiri layar (default: mati)
    public string Presets { get; set; } = "15,25,45,60";          // pilihan menit Pomodoro
    public string TimerPresets { get; set; } = "5,10,15,30";    // pilihan menit pop-up Timer
    public string OpenMode { get; set; } = "click";                // cara buka panel dari jam: click | hover
    public int TimerMin { get; set; } = 5;                         // durasi awal pop-up Timer (menit)
    public int DzikirIdx { get; set; } = 0;                        // pilihan dzikir terakhir (Dzikir Counter)
    public int DzikirTarget { get; set; } = 33;                    // jumlah dzikir terakhir
    public bool Adzan { get; set; } = true;                        // pengingat waktu adzan (pop-up + bel)
    public double PrayerLat { get; set; } = -6.2088;               // lokasi jadwal sholat (diperbarui otomatis saat online)
    public double PrayerLon { get; set; } = 106.8456;
    public string PrayerCity { get; set; } = "Jakarta";

    // Reminders & Alarm
    public List<ReminderItem> Reminders { get; set; } = new();

    // To Do / My Task
    public List<TodoItem> Todos { get; set; } = new();
    public bool TodoTop { get; set; } = true;       // kotak To Do selalu di atas jendela lain
    public bool TodoPlaced { get; set; } = false;   // true setelah posisi / ukuran kotak pernah disimpan
    public double TodoX { get; set; } = 0;
    public double TodoY { get; set; } = 0;
    public double TodoW { get; set; } = 360;
    public double TodoH { get; set; } = 500;

    // Batch Watermark: pengaturan terakhir
    public string WmText { get; set; } = "© bestie";
    public string WmLogo { get; set; } = "";
    public bool WmUseLogo { get; set; } = false;
    public double WmOpacity { get; set; } = 40;     // %
    public double WmSize { get; set; } = 22;        // % lebar
    public double WmAngle { get; set; } = -30;      // derajat
    public int WmPos { get; set; } = 9;             // 0..8 grid, 9 = ubin
    public int WmColor { get; set; } = 0;

    public List<string> Favs { get; set; } = new() { "tool:Pomodoro", "tool:Clipboard History" };   // item yang disematkan
    public List<string> Hidden { get; set; } = new();                                                // item yang disembunyikan (tab Hide)
    public List<string> Recent { get; set; } = new();                                                // item terakhir dipakai (terbaru di depan)
    public ObservableCollection<Pin> Pins { get; set; } = new();

    // Daftar menit dari string "15,25,45,60"
    static List<int> Parse(string? src, int[] fallback)
    {
        var r = new List<int>();
        foreach (var part in (src ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out var m) && m > 0 && m <= 600 && !r.Contains(m)) r.Add(m);
        if (r.Count == 0) r.AddRange(fallback);
        return r;
    }
    public List<int> PresetList() => Parse(Presets, new[] { 15, 25, 45, 60 });
    public List<int> TimerList() => Parse(TimerPresets, new[] { 5, 10, 15, 30 });

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Dir => System.IO.Path.Combine(AppData, "MySecretary");
    static string FilePath => System.IO.Path.Combine(Dir, "config.json");
    static string OldDir => System.IO.Path.Combine(AppData, "RadialLauncher");   // versi lama, dimigrasi otomatis

    public static Config Load()
    {
        try
        {
            var f = System.IO.File.Exists(FilePath) ? FilePath : System.IO.Path.Combine(OldDir, "config.json");
            if (System.IO.File.Exists(f))
            {
                var c = JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(f));
                if (c != null)
                {
                    if (f != FilePath)
                    {
                        c.Save();
                        try { var n = System.IO.Path.Combine(OldDir, "note.txt"); if (System.IO.File.Exists(n)) System.IO.File.Copy(n, System.IO.Path.Combine(Dir, "note.txt"), true); } catch { }
                    }
                    if (c.TimerPresets == "5,10,15,30,60") c.TimerPresets = "5,15,30,60";   // default lama -> 4 opsi
                    c.RenameKeys();
                    return c;
                }
            }
        }
        catch { }
        var d = new Config();   // instalasi baru: hanya menu bawaan bestie, Library kosong (pengguna menambah sendiri di Pengaturan)
        return d;
    }

    // Menu yang diganti nama: pin / hide / recent lama ikut pindah ke nama baru. Menu yang dihapus dibuang dari daftar.
    void RenameKeys()
    {
        var ren = new Dictionary<string, string> { ["tool:Desktop Organizer"] = "tool:Folder Organizer", ["tool:Image & PDF Pages"] = "tool:Doc Converter", ["tool:Catatan cepat"] = "tool:Notes" };
        foreach (var l in new[] { Favs, Hidden, Recent })
        {
            for (int i = 0; i < l.Count; i++) if (ren.TryGetValue(l[i], out var n)) l[i] = n;
            l.Remove("tool:Image Resizer");
            var seen = new HashSet<string>(); l.RemoveAll(x => !seen.Add(x));
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
