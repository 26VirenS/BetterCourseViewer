using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Simpl.Engine;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// (0.3.29, the Mac's SolverTool.swift) The Calculator, as Mathway's: a problem typed (or built on the keypad) and sent;
// the ways it can be worked offered; the one picked worked out — its answer exact and as a decimal, its steps, its graph.
// The page and its maths are the extension's (extension/tools/solver: the solver, its keypad, KaTeX to write the maths),
// shown in a web view with the app's look — light or dark, its accent — and the pin's own narrow version, which carries
// what was typed in it to the whole calculator.

/// <summary>The solver's page in a web view (the pin's: WebView2's composition control, the one kind a see-through
/// flyout shows); made as it is shown, let go when it goes.</summary>
internal sealed class SolverView : Grid
{
    private const string Host = "simpl-solver.local";
    private readonly ToolContext ctx;
    private readonly bool compact;
    private readonly string? problem, option;
    private FrameworkElement? web;
    private CoreWebView2? core;
    private string typed = "";

    public SolverView(ToolContext c, bool compact)
    {
        ctx = c;
        this.compact = compact;
        // (the pin's problem carried over, else the screenshot suite's: --solver "x^2 - x - 1 = 0" --solver-option solve:x,
        //  or the old calculator's --calc-expr)
        if (!compact)
        {
            problem = c.TakeInfo("problem") ?? Launch.Take("", "solver") ?? Launch.Take("", "calc-expr")?.TrimEnd('=');
            option = c.TakeInfo("option") ?? Launch.Take("", "solver-option");
        }
        else
        {
            problem = Launch.Take("", "calc-expr")?.TrimEnd('=');
            c.Carry = () => typed.Length > 0 ? new Dictionary<string, string> { ["problem"] = typed } : null;
            Height = 460;
        }
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
        if (!string.IsNullOrWhiteSpace(option)) q.Add("option=" + Uri.EscapeDataString(option));
        return $"https://{Host}/tools/solver/index.html?" + string.Join("&", q);
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
        var text = o["text"]?.GetValue<string>() ?? "";
        switch (o["kind"]?.GetValue<string>())
        {
            case "text": typed = text; break;
            case "copy": try { Clipboard.SetText(text); } catch { } break;
            case "openFull":
                var info = new Dictionary<string, string> { ["problem"] = text };
                if (o["option"]?.GetValue<string>() is { Length: > 0 } opt) info["option"] = opt;
                ctx.OpenFull(info, null);
                break;
            case "ready": if (Shot.Asked) Log.Write("calculator: the solver is ready"); break;
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
