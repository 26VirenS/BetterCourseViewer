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
/// and takes no clicks — it is a look, not the page. Only a page that came is drawn, on white (a page's own ground):
/// while it comes, a small card in the theme's colours waits in its place with a spinner, and where it cannot come the
/// card says so — and that the student can sign in all the same (the Mac's 200 by 160) — never a bright empty square
/// in a dark window. The square's room is kept while the page may come, so nothing under it moves; once it cannot, the
/// room closes up to the card. The school's other names (the row's "also: …") are said under its name.
/// </summary>
public sealed class SchoolConfirm : Grid
{
    public const double Side = 320;
    private readonly Border square;
    private readonly Grid inside = new() { Background = Brushes.White };
    /// <summary>The card in the square's place while the page comes, or where it could not.</summary>
    private readonly Border waiting;
    private WebView2CompositionControl? web;
    private bool settled;
    /// <summary>The square's room: kept while the page may still come, let go to the card's own height once it cannot.</summary>
    private readonly Grid stage;

    public SchoolConfirm(SchoolSearch.School school, Action yes, Action no)
    {
        square = new Border
        {
            Width = Side, Height = Side, CornerRadius = new CornerRadius(8), Child = inside, ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, Effect = K.Shadow(36, 10, 0.18), Opacity = 0,
        };
        square.Stroke(B.CardStroke);
        inside.Clip = new RectangleGeometry(new Rect(0, 0, Side - 2, Side - 2), 7, 7);
        waiting = new Border
        {
            Width = 200, Height = 160, CornerRadius = new CornerRadius(8), Child = K.Spinner(24),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        waiting.Bg(B.Card).Stroke(B.CardStroke);
        stage = new Grid { Width = Side, Height = Side, HorizontalAlignment = HorizontalAlignment.Center, Children = { square, waiting } };

        var name = K.Wrapped(school.Name, Ty.BodyLarge, B.Text, 2).Center();
        name.TextAlignment = TextAlignment.Center;
        var words = K.V(6,
            K.Text("Is this your school?", Ty.Subtitle).Center(),
            name,
            K.Text(school.Domain, Ty.Body, B.Text2).Center());
        // (the other names it goes by, as its row in the search said them — the one the student knows may be among them:
        //  "also State Center Community College District")
        if (school.Also is { Count: > 0 } also)
        {
            var others = K.Wrapped("also " + string.Join(", ", also.Take(2)) + (also.Count > 2 ? $" +{also.Count - 2}" : ""), Ty.Caption, B.Text2, 2).Center();
            others.TextAlignment = TextAlignment.Center;
            words.Children.Add(others);
        }
        // (the main choice first, as a Windows 11 dialog's buttons go — the order every pair of the first run keeps)
        var yesB = K.Button("Yes, sign in", yes, accent: true);
        yesB.MinWidth = 150;
        yesB.IsDefault = true;
        var noB = K.Button("No, search again", no);
        noB.MinWidth = 150;
        var buttons = K.H(12, yesB, noB);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var column = K.V(20, stage, words, buttons);
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
            inside.Children.Add(web);
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

    /// <summary>The page came: the square shows it, the waiting card goes (once: a page that comes after the preview was
    /// given up stays given up).</summary>
    private void Shown()
    {
        if (web == null || settled) return;
        settled = true;
        waiting.Visibility = Visibility.Collapsed;
        square.Opacity = 1;
        Motion.FadeIn(square, Motion.Medium);
    }

    /// <summary>No preview (offline, a page that would not come, the pictures): the waiting card says so, and that
    /// signing in still works; the square stays away.</summary>
    private void Failed()
    {
        settled = true;
        square.Visibility = Visibility.Collapsed;
        var said = K.Text("Couldn’t load a preview.", Ty.Caption).Center();
        said.FontWeight = FontWeights.SemiBold;
        var none = K.V(4, K.Icon(SymbolRegular.Globe24, 24, B.Text2).Margin(0, 0, 0, 4), said, K.Text("You can still sign in.", Ty.Caption, B.Text2).Center());
        none.HorizontalAlignment = HorizontalAlignment.Center;
        none.VerticalAlignment = VerticalAlignment.Center;
        waiting.Child = none;
        System.Windows.Automation.AutomationProperties.SetName(waiting, "Couldn’t load a preview of this school’s sign-in page. You can still sign in.");
        Motion.FadeIn(none, Motion.Normal);
        // (a final answer: the square's room let go, so the card sits as close to the question as the rest of the column)
        stage.Height = waiting.Height;
    }

    /// <summary>The square let go of (No, search again; the school opened).</summary>
    public void Stop()
    {
        try { web?.Dispose(); } catch { }
        web = null;
    }
}
