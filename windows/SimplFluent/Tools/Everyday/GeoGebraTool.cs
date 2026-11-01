using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// (0.3.30, the Mac's GeoGebraTool.swift) The Calculator is GeoGebra's: its Calculator Suite (graphing, geometry, 3D,
// CAS, probability, the scientific calculator) in the tool, its scientific calculator in the pin. The page is the
// extension's (extension/tools/geogebra/index.html — from 0.3.30 until the UI review it was shipped one folder up, so
// every Calculator showed Chromium's "File not found"), which loads GeoGebra from geogebra.org into a web view — so it
// needs the internet. Where the page cannot load — its navigation fails, its process stops, or GeoGebra does not answer
// (the page's "offline") — Simpl's own failure shows in its place (Kit/WebFailure.cs): "Calculator couldn’t load", Try
// again, Open in browser.

/// <summary>GeoGebra's page in a web view (the pin's: WebView2's composition control, the one kind a see-through flyout
/// shows); made as it is shown, let go when it goes — or, where it could not load, Simpl's failure in its place.</summary>
internal sealed class GeoGebraView : Grid
{
    private const string Host = "simpl-solver.local";
    private readonly ToolContext ctx;
    private readonly bool compact;
    private readonly string? problem;
    /// <summary>(the screenshot suite's --web-tool-error calc, a development run's) The page asked for is one that is not
    /// there, so its failure is pictured as a student would meet it.</summary>
    private readonly bool broken = Launch.IsDevelopment && string.Equals(Launch.Get("web-tool-error"), "calc", StringComparison.OrdinalIgnoreCase);
    /// <summary>Simpl's failure, in the web view's place while it could not load.</summary>
    private readonly ContentControl failure = new() { Focusable = false, Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private FrameworkElement? web;
    private CoreWebView2? core;
    private bool failed;

    public GeoGebraView(ToolContext c, bool compact)
    {
        ctx = c;
        this.compact = compact;
        // (the screenshot suite's: --geogebra "f(x) = x^2 - 2x - 3", a command GeoGebra runs as it opens)
        if (!compact) problem = Launch.Take("", "geogebra");
        else Height = 460;
        Children.Add(failure);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private async void Start()
    {
        if (web != null) return;
        // (made afresh — shown again, or Try again: whatever failed before is tried again)
        failed = false;
        failure.Visibility = Visibility.Collapsed;
        failure.Content = null;
        Appearance.Changed -= OnLook;
        Appearance.Changed += OnLook;
        try
        {
            var env = await Engine.Engine.WebEnvironment();
            if (!IsLoaded || web != null) return;
            if (compact)
            {
                var w = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                web = w;
                Children.Add(w);
                await w.EnsureCoreWebView2Async(env);
                core = w.CoreWebView2;
            }
            else
            {
                var w = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                web = w;
                Children.Add(w);
                await w.EnsureCoreWebView2Async(env);
                core = w.CoreWebView2;
            }
            if (core == null) return;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.SetVirtualHostNameToFolderMapping(Host, ScriptBundle.ExtensionDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += OnMessage;
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u.Host == Host || u.Scheme is not ("http" or "https")) return;
                e.Cancel = true;
                Engine.Engine.OpenExternally(u);
            };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) Engine.Engine.OpenExternally(u); };
            // (its page not there, or not answering: Simpl's failure, never the web view's own error page; a link sent to
            //  the browser is a navigation cancelled, not a failure)
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) Fail($"its page, {e.WebErrorStatus}", WebFailure.Why(e.WebErrorStatus));
            };
            core.ProcessFailed += (_, e) =>
            {
                if (WebFailure.TookPage(e.ProcessFailedKind)) Fail($"its page's process, {e.ProcessFailedKind}", WebFailure.Stopped, gone: e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited);
            };
            core.Navigate(Address());
        }
        catch (Exception e)
        {
            Log.Write("calculator: " + e.Message);
            if (IsLoaded) Fail("its web view", "Simpl couldn’t start its web view. Try again, or open it in your browser.");
        }
    }

    private string ToolName => ToolsCenter.KindOf(ctx.Key)?.Name ?? "Calculator";

    /// <summary>
    /// Its page could not load: the web view out of sight (the whole tool's is a window of its own, which nothing drawn
    /// here can cover) and Simpl's failure in its place — Try again makes the web view afresh; Open in browser opens
    /// GeoGebra's own (the scientific calculator for the pin). <paramref name="gone"/>: the web view itself went with its
    /// process, and is let go at once.
    /// </summary>
    private void Fail(string what, string why, bool offline = false, bool gone = false)
    {
        if (failed) return;
        failed = true;
        Log.Write($"calculator: could not load ({what}): Simpl's own error shown in its place");
        if (gone) Stop();
        else if (web != null) web.Visibility = Visibility.Hidden;
        var site = new Uri(compact ? "https://www.geogebra.org/scientific" : "https://www.geogebra.org/calculator");
        failure.Content = WebFailure.View($"{ToolName} couldn’t load", why, Retry, () => Engine.Engine.OpenExternally(site), offline, compact);
        failure.Visibility = Visibility.Visible;
        Shot.Moved();
    }

    /// <summary>Try again: the web view made afresh, its page asked for again.</summary>
    private void Retry()
    {
        Stop();
        Start();
    }

    private string Address()
    {
        var q = new List<string>
        {
            "theme=" + (Appearance.Dark ? "dark" : "light"),
            "accent=" + Uri.EscapeDataString(Hex(Appearance.Accent)),
            "os=win",
            "clear=1",
        };
        if (compact) q.Add("compact=1");
        if (!string.IsNullOrWhiteSpace(problem)) q.Add("q=" + Uri.EscapeDataString(problem));
        return $"https://{Host}/tools/geogebra/{(broken ? "not-there.html" : "index.html")}?" + string.Join("&", q);
    }

    private static string Hex(System.Windows.Media.Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private void OnLook() => Dispatcher.BeginInvoke(() =>
    {
        try { core?.ExecuteScriptAsync($"window.simplSolverTheme && simplSolverTheme('{(Appearance.Dark ? "dark" : "light")}', '{Hex(Appearance.Accent)}')"); } catch { }
    });

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? json;
        try { json = e.TryGetWebMessageAsString(); } catch { return; }
        if (json == null) return;
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; } catch { return; }
        if (o == null || o["solver"]?.GetValue<bool>() != true) return;
        switch (o["kind"]?.GetValue<string>())
        {
            case "ready": Log.Write("calculator: GeoGebra is ready"); break;
            // (GeoGebra not answering — no internet, or geogebra.org down: Simpl's own failure over the page's note)
            case "offline": Fail("GeoGebra did not answer", "GeoGebra needs the internet. Check your connection, then try again.", offline: true); break;
        }
    }

    /// <summary>Gone from the screen: its web view let go (its processes with it), made again if it comes back.</summary>
    private void Stop()
    {
        Appearance.Changed -= OnLook;
        if (web == null) return;
        var w = web;
        web = null;
        if (core != null) core.WebMessageReceived -= OnMessage;
        core = null;
        Children.Remove(w);
        try { (w as IDisposable)?.Dispose(); } catch { }
    }
}
