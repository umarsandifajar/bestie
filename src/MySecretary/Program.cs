using System.Windows;
namespace MySecretary;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "MySecretary.Single", out var first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/MySecretary;component/Theme.xaml", UriKind.Relative) });
        var cfg = Config.Load();
        Pal.Set(cfg.Light, false);
        var win = new SecretaryWindow(cfg);
        app.Startup += (_, _) => { win.Start(); Toast.Show("bestie aktif · arahkan kursor ke jam di tepi kiri"); };
        app.Run();
    }
}
