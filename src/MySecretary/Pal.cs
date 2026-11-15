using System.Windows;
using System.Windows.Media;
namespace MySecretary;

// Palet tema: Black (default) atau Terang. Dipakai kode C# langsung (Pal.Ink, dst)
// dan didorong ke resource aplikasi supaya Theme.xaml ikut berubah (DynamicResource).
static class Pal
{
    public static bool Light;
    public static event Action? Changed;

    public static Color Bg, Raised, Hover, Stroke, Ink, Dim, Accent, AccentInk, Win, Island, Sel, Track, Cyan, Violet, CyanSoft, VioletSoft;

    static Color Rgb(uint v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    public static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    public static Brush Br(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // Gradien cyan -> violet (angle 0 = kiri ke kanan, 45 = diagonal). soft = versi pastel untuk teks.
    public static LinearGradientBrush CV(double angle = 0, bool soft = false)
    {
        var b = new LinearGradientBrush(soft ? CyanSoft : Cyan, soft ? VioletSoft : Violet, angle); b.Freeze(); return b;
    }

    public static void Set(bool light, bool notify = true)
    {
        Light = light;
        if (!light)
        {
            Bg = Rgb(0x0C0C0E); Raised = Rgb(0x18181B); Hover = Rgb(0x222226); Stroke = Color.FromArgb(0x24, 255, 255, 255);
            Ink = Rgb(0xF3F3F5); Dim = Rgb(0x8E8E96); Accent = Rgb(0xF3F3F5); AccentInk = Rgb(0x0B0B0C);
            Win = Rgb(0x121215); Island = Rgb(0x000000); Sel = Rgb(0x2A2A30); Track = Rgb(0x2A2A30); Cyan = Rgb(0x22D3EE); Violet = Rgb(0xA78BFA); CyanSoft = Rgb(0x67E8F9); VioletSoft = Rgb(0xC4B5FD);
        }
        else
        {
            Bg = Rgb(0xF7F7F9); Raised = Rgb(0xFFFFFF); Hover = Rgb(0xEBEBEF); Stroke = Color.FromArgb(0x1C, 0, 0, 0);
            Ink = Rgb(0x17171A); Dim = Rgb(0x6E6E78); Accent = Rgb(0x17171A); AccentInk = Rgb(0xFFFFFF);
            Win = Rgb(0xF3F3F6); Island = Rgb(0xFFFFFF); Sel = Rgb(0xE4E4E9); Track = Rgb(0xD9D9DF); Cyan = Rgb(0x0891B2); Violet = Rgb(0x7C3AED); CyanSoft = Rgb(0x06B6D4); VioletSoft = Rgb(0x8B5CF6);
        }
        Push();
        if (notify) Changed?.Invoke();
    }

    static void Push()
    {
        var r = Application.Current?.Resources; if (r == null) return;
        r["Ink"] = Br(Ink); r["Dim"] = Br(Dim); r["Fill"] = Br(Raised); r["FillHover"] = Br(Hover); r["Line"] = Br(Stroke);
        r["Accent"] = Br(Accent); r["AccentInk"] = Br(AccentInk); r["Win"] = Br(Win); r["Pop"] = Br(Bg); r["Sel"] = Br(Sel); r["Track"] = Br(Track); r["Cyan"] = Br(Cyan); r["Violet"] = Br(Violet);
    }
}
