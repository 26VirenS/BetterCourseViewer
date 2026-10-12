using System.Windows;

namespace GlassShowcase;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var w = new ShowcaseWindow();
        app.MainWindow = w;
        // (CI: --shot file.png takes a picture of the window after a moment, then quits)
        var shot = Array.IndexOf(args, "--shot") is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (shot != null)
        {
            w.Loaded += async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(6));
                Snapshot.Take(w, shot);
                // (what drew it: the glass shader, or its fallback where WPF draws in software)
                System.IO.File.WriteAllText(shot + ".txt", $"shader={Simpl.Components.Glass.GlassEffect.Available} renderTier={System.Windows.Media.RenderCapability.Tier >> 16}");
                app.Shutdown();
            };
        }
        app.Run(w);
    }
}
