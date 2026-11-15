using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static MySecretary.Fx;
namespace MySecretary;

// Satu baris fisheye: elemen + callback yang dipanggil tiap posisi berubah (ad = jarak baris dari tengah, 0 = aktif).
sealed record FishRow(FrameworkElement El, Action<double> Apply);

// Daftar bergaya fisheye: item aktif di tengah (ukuran penuh), item di atas/bawahnya mengecil dan memudar bertahap.
// Tanpa scroll bar: roda mouse / panah atas-bawah menggeser fokus, klik item samping membawanya ke tengah.
sealed class FisheyeList : Canvas
{
    public const double RowH = 44;

    // Tabel per jarak 0,1,2,... dari tengah: posisi Y (px dari tengah), skala, dan opasitas. Diinterpolasi halus di antaranya.
    static readonly double[] Ys = { 0, 50, 88, 116, 136, 150 };
    static readonly double[] Sc = { 1, .86, .72, .60, .50, .44 };
    static readonly double[] Op = { 1, .60, .28, .08, 0, 0 };
    static readonly IEasingFunction Ez = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .9 };

    public static readonly DependencyProperty PosProperty = DependencyProperty.Register(nameof(Pos), typeof(double), typeof(FisheyeList),
        new PropertyMetadata(0.0, (d, _) => ((FisheyeList)d).Place()));
    public double Pos { get => (double)GetValue(PosProperty); set => SetValue(PosProperty, value); }

    readonly List<FishRow> rows = new();
    readonly List<(ScaleTransform s, TranslateTransform t)> xf = new();
    int target; double wheel;

    public int Index => target;
    public int Count => rows.Count;

    public FisheyeList()
    {
        ClipToBounds = true; Background = Brushes.Transparent; Focusable = false;
        SizeChanged += (_, _) => Place();
        MouseWheel += (_, e) =>
        {
            wheel += e.Delta; int n = (int)(wheel / 120);
            if (n != 0) { wheel -= n * 120; Step(-n); }
            e.Handled = true;
        };
    }

    public void SetRows(IEnumerable<FishRow> items, int index, bool animate)
    {
        BeginAnimation(PosProperty, null);
        Children.Clear(); rows.Clear(); xf.Clear();
        foreach (var r in items)
        {
            var s = new ScaleTransform(1, 1); var t = new TranslateTransform();
            r.El.Height = RowH; r.El.RenderTransformOrigin = new Point(.5, .5);
            r.El.RenderTransform = new TransformGroup { Children = { s, t } };
            Children.Add(r.El); rows.Add(r); xf.Add((s, t));
        }
        target = rows.Count == 0 ? 0 : Math.Clamp(index, 0, rows.Count - 1);
        Pos = animate && rows.Count > 0 ? target - 1.6 : target;
        if (animate && rows.Count > 0) To(this, PosProperty, target, 520, EzOut);
        Place();
    }

    public void FocusTo(int i)
    {
        if (rows.Count == 0) return;
        i = Math.Clamp(i, 0, rows.Count - 1);
        if (i == target) return;
        target = i;
        BeginAnimation(PosProperty, new DoubleAnimation(i, TimeSpan.FromMilliseconds(380)) { EasingFunction = Ez });
    }
    public void Step(int n) => FocusTo(target + n);

    static double Interp(double[] tb, double ad)
    {
        if (ad >= tb.Length - 1) return tb[tb.Length - 1];
        int i = (int)ad; double f = ad - i;
        return tb[i] + (tb[i + 1] - tb[i]) * f;
    }

    void Place()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;
        double cy = h / 2, rw = Math.Max(40, w - 16), pos = Pos;
        for (int i = 0; i < rows.Count; i++)
        {
            double d = i - pos, ad = Math.Abs(d);
            var el = rows[i].El; var (s, t) = xf[i];
            double op = Interp(Op, ad);
            if (op < .01) { if (el.Visibility != Visibility.Hidden) el.Visibility = Visibility.Hidden; continue; }
            if (el.Visibility != Visibility.Visible) el.Visibility = Visibility.Visible;
            if (double.IsNaN(el.Width) || Math.Abs(el.Width - rw) > .5) el.Width = rw;
            SetLeft(el, (w - rw) / 2); SetTop(el, cy - RowH / 2);
            double sc = Interp(Sc, ad);
            s.ScaleX = sc; s.ScaleY = sc; t.Y = Math.Sign(d) * Interp(Ys, ad);
            el.Opacity = op;
            Panel.SetZIndex(el, 100 - (int)(ad * 10));
            rows[i].Apply(ad);
        }
    }
}
