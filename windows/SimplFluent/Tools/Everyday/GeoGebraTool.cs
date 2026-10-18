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
// extension's (extension/tools/geogebra), which loads GeoGebra from geogebra.org into a web view — so it needs the
// internet, and says so with Try again without it.

/// <summary>GeoGebra's page in a web view (the pin's: WebView2's composition control, the one kind a see-through flyout
/// shows); made as it is shown, let go when it goes.</summary>
internal sealed class GeoGebraView : Grid
{
    private const string Host = "simpl-solver.local";
    private readonly ToolContext ctx;
    private readonly bool compact;
    private readonly string? problem;
    private FrameworkElement? web;
    private CoreWebView2? core;

    public GeoGebraView(ToolContext c, bool compact)
    {
        ctx = c;
        this.compact = compact;
        // (the screenshot suite's: --geogebra "f(x) = x^2 - 2x - 3", a command GeoGebra runs as it opens)
        if (!compact) problem = Launch.Take("", "geogebra");
        else Height = 460;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private async void Start()
    {
        if (web != null) return;
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
            core.Navigate(Address());
        }
        catch (Exception e) { Log.Write("calculator: " + e.Message); }
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
        return $"https://{Host}/tools/geogebra/index.html?" + string.Join("&", q);
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
            case "offline": Log.Write("calculator: GeoGebra could not be loaded"); break;
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
