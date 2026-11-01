using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;

namespace Simpl.Kit;

/// <summary>
/// What a web tool shows where its page could not load — the Calculator's GeoGebra, the graphing calculator's Desmos, a
/// tool's window, Macmillan or ALEKS in their popup: Simpl's own, never Chromium's error page ("File not found",
/// ERR_…) — a symbol, "&lt;Tool&gt; couldn’t load", why in a line the student can act on, Try again and Open in browser.
/// Its page's navigation that failed, its page's process that stopped and its page saying it is offline all come here.
/// </summary>
public static class WebFailure
{
    /// <summary>The line for a page whose process stopped (it crashed, or stopped answering).</summary>
    public const string Stopped = "Its page stopped working. Try again to open it afresh.";

    /// <summary>The failure: <paramref name="title"/> ("Calculator couldn’t load"), <paramref name="why"/> under it, Try
    /// again (the accent's) and, where there is one, Open in browser — smaller in a pin's flyout (<paramref name="compact"/>).</summary>
    public static FrameworkElement View(string title, string why, Action retry, Action? browser, bool offline = false, bool compact = false)
    {
        var again = K.Button("Try again", retry, accent: true, icon: SymbolRegular.ArrowClockwise24);
        var buttons = K.H(8, again, browser != null ? K.Button("Open in browser", browser, icon: SymbolRegular.Globe24) : null);
        buttons.HorizontalAlignment = HorizontalAlignment.Center;
        var head = K.Text(title, compact ? Ty.BodyStrong : Ty.Subtitle);
        head.TextWrapping = TextWrapping.Wrap;
        head.TextTrimming = TextTrimming.None;
        head.Center();
        var v = K.V(compact ? 8 : 12,
            K.Icon(offline ? SymbolRegular.WifiWarning24 : SymbolRegular.ErrorCircle24, compact ? 32 : 40, B.Text3).Align(HorizontalAlignment.Center),
            head,
            K.Wrapped(why, Ty.Body, B.Text2).Center(),
            buttons.Margin(0, compact ? 2 : 4, 0, 0));
        v.HorizontalAlignment = HorizontalAlignment.Center;
        v.VerticalAlignment = VerticalAlignment.Center;
        v.MaxWidth = compact ? 300 : 420;
        v.Margin = new Thickness(compact ? 12 : 24);
        System.Windows.Automation.AutomationProperties.SetName(v, $"{title}. {why}");
        var g = new Grid { Children = { v } };
        g.Bg(B.Layer);
        Motion.FadeIn(v, Motion.Normal);
        return g;
    }

    /// <summary>Why a navigation failed, in a line the student can act on.</summary>
    public static string Why(CoreWebView2WebErrorStatus s) => s switch
    {
        CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.HostNameNotResolved or CoreWebView2WebErrorStatus.CannotConnect
            or CoreWebView2WebErrorStatus.ServerUnreachable or CoreWebView2WebErrorStatus.Timeout or CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset => "Check your internet connection, then try again.",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or CoreWebView2WebErrorStatus.CertificateExpired
            or CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or CoreWebView2WebErrorStatus.CertificateRevoked
            or CoreWebView2WebErrorStatus.CertificateIsInvalid => "The site’s security certificate isn’t valid, so Simpl didn’t open its page.",
        CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired or CoreWebView2WebErrorStatus.ValidProxyAuthenticationRequired
            => "The site wants you to sign in first. Open it in your browser to sign in.",
        _ => "Its page didn’t open. Try again, or open it in your browser.",
    };

    /// <summary>Whether a navigation failed so that the web view would show its own error page — not one the site sent
    /// (a 404 of its own, which says more), not one cancelled (a link sent to the browser, a download).</summary>
    public static bool ShowsOwnPage(CoreWebView2NavigationCompletedEventArgs e) =>
        !e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled && e.HttpStatusCode == 0;

    /// <summary>Whether a process failure took the page itself with it (not a frame's, nor the GPU's, which comes back by
    /// itself).</summary>
    public static bool TookPage(CoreWebView2ProcessFailedKind k) =>
        k is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive;
}
