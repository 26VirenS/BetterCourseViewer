using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Simpl.Kit;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;

namespace Simpl.Screens;

/// <summary>
/// (Mac 1.3.70) A question's graph, large, in a window of its own over the popup (the Mac's AltaGraphWindow): moved and
/// sized as the student likes, its colours dark or as Alta draws them (the same choice as the question's, Light or Dark
/// in its title bar while Alta's page is dark), and put away when the question is answered or gone, or the popup closed.
/// </summary>
internal sealed class AltaGraphWindow : FluentWindow
{
    private static AltaGraphWindow? shared;
    private readonly Image image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly Grid ground = new();
    private readonly TitleBar bar;
    private readonly ContentControl trailing = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private AltaSession? session;
    private bool light, quiet;

    private AltaGraphWindow(string title, Window? owner)
    {
        Title = title;
        Owner = owner;
        ShowInTaskbar = false;
        ShowActivated = false; // (in front of the popup, the page keeping the keyboard)
        WindowBackdropType = Support.Appearance.Backdrop;
        if (Support.Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        MinWidth = 280;
        MinHeight = 220;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bar = new TitleBar { Title = title, ShowMinimize = false, ShowMaximize = false, TrailingContent = trailing };
        root.Children.Add(bar);
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.Margin = new Thickness(14);
        ground.Children.Add(image);
        root.Children.Add(ground.Row(1));
        Content = root;
        Closed += (_, _) =>
        {
            if (shared == this) shared = null;
            if (!quiet) session?.PicWindowClosed();
        };
    }

    /// <summary>The graph's picture shown (the window opened, or the one open given the new picture).</summary>
    public static void Show(BitmapSource picture, Size natural, string title, AltaSession session, Window? owner)
    {
        if (shared == null)
        {
            var w = new AltaGraphWindow(title, owner);
            // (as large as it is drawn, within six tenths of the screen)
            var fit = SystemParameters.WorkArea.Size;
            var k = Math.Min(1, Math.Min(fit.Width * 0.6 / Math.Max(natural.Width, 1), fit.Height * 0.6 / Math.Max(natural.Height, 1)));
            w.Width = Math.Max(320, natural.Width * k + 32);
            w.Height = Math.Max(260, natural.Height * k + 80);
            shared = w;
        }
        var win = shared;
        win.session = session;
        win.light = session.PicsLight;
        win.Title = title;
        win.bar.Title = title;
        win.image.Source = picture;
        win.Paint();
        if (!win.IsVisible) win.Show();
    }

    /// <summary>Light or Dark chosen (here, or by the popup's own button).</summary>
    public static void Light(bool on)
    {
        if (shared == null) return;
        shared.light = on;
        shared.Paint();
    }

    /// <summary>Put away by the popup (the question gone, the popup closed): not told back to it.</summary>
    public static void CloseIt()
    {
        if (shared is not { } w) return;
        shared = null;
        w.quiet = true;
        w.Close();
    }

    /// <summary>Dark by default when Alta's page is (the question's own colours turned over: 0.3.5, as the student chooses
    /// for the page, no longer as the app is); Light brings back Alta's own on white.</summary>
    private void Paint()
    {
        var dark = session?.Dark ?? Support.Appearance.Dark;
        ground.Background = K.Brush(dark && !light ? Color.FromRgb(0x1c, 0x1c, 0x1c) : Colors.White);
        if (!dark || session == null) { trailing.Content = null; return; }
        var s = session;
        var b = K.Button(light ? "Dark" : "Light", () => s.SetPicsLight(!light), icon: light ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24);
        b.MinWidth = 0;
        b.Margin = new Thickness(0, 0, 8, 0);
        b.ToolTip = light ? "Show the graph in dark colours" : "Show the graph in its own light colours";
        trailing.Content = b;
    }
}
