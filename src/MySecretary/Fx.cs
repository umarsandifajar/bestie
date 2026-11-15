using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace MySecretary;

// Helper animasi bersama
static class Fx
{
    public static readonly IEasingFunction EzOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction EzIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    // Membal halus (satu kali, cepat meredam) untuk efek island/panel saat kena kursor
    public static readonly IEasingFunction EzBounce = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 };

    public static void To(IAnimatable t, DependencyProperty p, double v, int ms, IEasingFunction? ez = null, int delay = 0)
        => t.BeginAnimation(p, new DoubleAnimation(v, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ez ?? EzOut, BeginTime = TimeSpan.FromMilliseconds(delay) });

    public static void ToC(SolidColorBrush b, Color c, int ms)
        => b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(ms)) { EasingFunction = EzOut });

    public static void Pop(ScaleTransform s, double v, int ms = 600) { To(s, ScaleTransform.ScaleXProperty, v, ms, EzBounce); To(s, ScaleTransform.ScaleYProperty, v, ms, EzBounce); }
    public static readonly FontFamily Jakarta = new(new Uri("pack://application:,,,/MySecretary;component/"), "./Fonts/#Plus Jakarta Sans, Plus Jakarta Sans, Segoe UI Variable Display, Segoe UI");

    public static void Scale(ScaleTransform s, double v, int ms) { To(s, ScaleTransform.ScaleXProperty, v, ms); To(s, ScaleTransform.ScaleYProperty, v, ms); }
}
