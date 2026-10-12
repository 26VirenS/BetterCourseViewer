using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// A page of the school's own in a window of its own, signed in as the app is (the engine's web data): what a tool, a
/// quiz or "Open in Canvas" opens until its own screen is on Windows (the Mac's ToolWindow, for now its plainest form).
/// </summary>
public sealed class WebWindow : Window
{
    private readonly WebView2 web = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 28, 28, 30) };

    public WebWindow(Uri url, string title, Window? owner)
    {
        Title = string.IsNullOrEmpty(title) ? "Simpl" : title;
        Owner = owner;
        Width = 1100;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Content = web;
        Loaded += async (_, _) =>
        {
            try
            {
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebData, options);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.DocumentTitleChanged += (_, _) => { if (!string.IsNullOrEmpty(web.CoreWebView2.DocumentTitle)) Title = web.CoreWebView2.DocumentTitle; };
                web.CoreWebView2.Navigate(url.ToString());
            }
            catch (Exception e) { Dialogs.Fail("The page could not open", e.Message); Close(); }
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
        }
        return path != null ? new Uri(baseUrl, path) : null;
    }
}
