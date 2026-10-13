using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Kit;
using Simpl.Support;
using Wpf.Ui.Controls;

namespace Simpl.SignIn;

/// <summary>
/// The school chosen, before it is opened (the Mac's SchoolConfirm): a small square of its sign-in page, live, and "Is
/// this your school?". Yes opens it and the sign-in goes on as always; No goes back to the search as it was left. The
/// page in the square keeps nothing (InPrivate: no cookies, no storage), is drawn zoomed out as on a laptop's screen,
/// and takes no clicks — it is a look, not the page.
/// </summary>
public sealed class SchoolConfirm : Grid
{
    public const double Side = 320;
    private readonly Border square;
    private readonly Grid inside = new() { Background = Brushes.White };
    private readonly FrameworkElement loading = K.Spinner(28);
    private WebView2CompositionControl? web;
    private readonly Border webHolder = new() { Opacity = 0 };
    private bool settled;

    public SchoolConfirm(SchoolSearch.School school, Action yes, Action no)
    {
        inside.Children.Add(loading);
        square = new Border
        {
            Width = Side, Height = Side, CornerRadius = new CornerRadius(8), Child = inside, ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, Effect = K.Shadow(36, 10, 0.18),
        };
        square.Stroke(B.CardStroke);
        inside.Clip = new RectangleGeometry(new Rect(0, 0, Side - 2, Side - 2), 7, 7);

        var name = K.Wrapped(school.Name, Ty.BodyLarge, B.Text, 2).Center();
        name.TextAlignment = TextAlignment.Center;
        var words = K.V(6,
            K.Text("Is this your school?", Ty.Subtitle).Center(),
            name,
            K.Text(school.Domain, Ty.Body, B.Text2).Center());
        var noB = K.Button("No, Search Again", no);
        noB.MinWidth = 150;
        var yesB = K.Button("Yes, Sign In", yes, accent: true);
        yesB.MinWidth = 150;
        yesB.IsDefault = true;
        var buttons = K.H(12, noB, yesB);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var column = K.V(20, square, words, buttons);
        column.Width = 520;
        column.HorizontalAlignment = HorizontalAlignment.Center;
        column.VerticalAlignment = VerticalAlignment.Center;
        column.Margin = new Thickness(0, 0, 0, 36);
        Children.Add(column);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { no(); e.Handled = true; }
            else if (e.Key == Key.Enter) { yes(); e.Handled = true; }
        };
        Loaded += (_, _) => { Focusable = true; Focus(); Load(school.Domain); };
    }

    private async void Load(string host)
    {
        if (Shot.Asked && Launch.Get("picker-preview") != "yes") { Failed(); return; } // (the suite's pictures: no outside site)
        try
        {
            web = new WebView2CompositionControl
            {
                IsHitTestVisible = false, Focusable = false,
                Width = Side, Height = Side,
                CreationProperties = new CoreWebView2CreationProperties { IsInPrivateModeEnabled = true, UserDataFolder = System.IO.Path.Combine(AppPaths.Root, "Preview") },
            };
            webHolder.Child = web;
            inside.Children.Insert(0, webHolder);
            await web.EnsureCoreWebView2Async();
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsScriptEnabled = true;
            web.ZoomFactor = 0.3; // (about 1,070 of the page's points across the square, as on a laptop)
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess || e.HttpStatusCode > 0) Shown();
                else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled && !settled) Failed();
            };
            core.Navigate($"https://{host}/");
            // (a page that never finishes — a sign-in that keeps redirecting — shows what it has after a while)
            await Task.Delay(10_000);
            if (!settled) { if (core.Source.StartsWith("http")) Shown(); else Failed(); }
        }
        catch (Exception e)
        {
            Log.Write("preview: " + e.Message);
            Failed();
        }
    }

    private void Shown()
    {
        if (web == null) return;
        settled = true;
        loading.Visibility = Visibility.Collapsed;
        webHolder.Opacity = 1;
        Motion.FadeIn(webHolder, Motion.Medium);
    }

    private void Failed()
    {
        settled = true;
        loading.Visibility = Visibility.Collapsed;
        webHolder.Visibility = Visibility.Collapsed;
        var none = K.V(8, K.Icon(SymbolRegular.Globe24, 28, Color.FromArgb(115, 0, 0, 0)),
            new TextBlockLike("No preview of this page").Element);
        none.HorizontalAlignment = HorizontalAlignment.Center;
        none.VerticalAlignment = VerticalAlignment.Center;
        inside.Children.Add(none);
    }

    /// <summary>The square let go of (No, Search Again; the school opened).</summary>
    public void Stop()
    {
        try { web?.Dispose(); } catch { }
        web = null;
    }

    /// <summary>Words on the square's white, whatever the theme.</summary>
    private sealed class TextBlockLike
    {
        public System.Windows.Controls.TextBlock Element { get; }
        public TextBlockLike(string s)
        {
            Element = K.Text(s, Ty.Body);
            Element.Foreground = K.Brush(Color.FromArgb(115, 0, 0, 0));
            Element.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }
}
