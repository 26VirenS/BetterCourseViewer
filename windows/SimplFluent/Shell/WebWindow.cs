using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.Shell;

/// <summary>
/// A page of the school's own in a window of its own, signed in as the app is (the engine's web data): what a tool, a
/// quiz or "Open in Canvas" opens until its own screen is on Windows (the Mac's ToolWindow, in its plainest form) — a
/// Windows 11 window, Mica round the page, the page's title in the title bar.
/// </summary>
public sealed class WebWindow : FluentWindow
{
    private readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.Transparent };
    private readonly TitleBar bar;

    public WebWindow(Uri url, string title, Window? owner)
    {
        Title = string.IsNullOrEmpty(title) ? "Simpl" : title;
        Owner = owner;
        WindowBackdropType = Appearance.Backdrop;
        ExtendsContentIntoTitleBar = true;
        FontFamily = K.TextFace;
        SetResourceReference(ForegroundProperty, B.Text);
        if (Shot.Asked || Fx.Opaque) SetResourceReference(BackgroundProperty, B.Window);
        Placement.Restore(this, "web", 1100, 760, 480, 360);
        if (owner != null) WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bar = new TitleBar { Title = Title, Icon = new ImageIcon { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Simpl-256.png")), Width = 16, Height = 16 } };
        root.Children.Add(bar);
        Grid.SetRow(web, 1);
        root.Children.Add(web);
        Content = root;
        SizeChanged += (_, _) => Placement.Save(this, "web");
        Loaded += async (_, _) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebData, new CoreWebView2EnvironmentOptions(Engine.Engine.BrowserArguments));
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                web.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    if (string.IsNullOrEmpty(web.CoreWebView2.DocumentTitle)) return;
                    Title = web.CoreWebView2.DocumentTitle;
                    bar.Title = Title;
                };
                web.CoreWebView2.DownloadStarting += (_, e) => { e.Handled = true; FilePreview.Downloading(e); };
                web.CoreWebView2.Navigate(url.ToString());
            }
            catch (Exception e) { Dialogs.Fail("The page could not open", e.Message, owner); Close(); }
        };
    }

    /// <summary>Where a launch leads on the school's site.</summary>
    public static Uri? Address(Uri baseUrl, ToolLaunch t)
    {
        var a = t.Args;
        string? path = null;
        if (a.TryGetValue("page", out var page)) return Uri.TryCreate(baseUrl, page, out var u) ? u : null;
        if (a.TryGetValue("course", out var c))
        {
            if (a.TryGetValue("tool", out var tool)) path = $"/courses/{c}/external_tools/{tool}";
            else if (a.TryGetValue("assignment", out var id)) path = $"/courses/{c}/assignments/{id}";
            else if (a.TryGetValue("moduleItem", out var item)) path = $"/courses/{c}/modules/items/{item}";
            else if (a.TryGetValue("url", out var raw)) path = $"/courses/{c}/external_tools/retrieve?url={Uri.EscapeDataString(raw)}";
        }
        return path != null ? new Uri(baseUrl, path) : null;
    }
}
