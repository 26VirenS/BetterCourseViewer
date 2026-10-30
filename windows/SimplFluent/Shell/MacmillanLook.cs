using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Simpl.Engine;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// (0.3.92) Simpl's look for Macmillan Learning (extension/tools/macmillan/look.js, the Mac app's too): Macmillan's own
/// page restyled as an Alta question is in the apps — a glass rail of questions with the one open marked, a glass bar
/// of its tools, the question's number large over its sheet, and the main button (Submit Answer, Next Question) with the
/// verdict beside it at the sheet's foot. Styles only: nothing of the page is taken out, and every question is answered
/// as on Macmillan's own page (the old skin, a second page drawn from Macmillan's, was taken out in 0.3.58). Drawn in
/// every frame of Macmillan's (its player is one) and nowhere else; on until the title bar's Simpl Look turns it off
/// (kept). A picture run's --macmillan-look off starts with it off.
/// </summary>
internal sealed class MacmillanLook
{
    private const string Key = "macmillanLook";
    /// <summary>The look's script, from the extension the app carries (read once).</summary>
    private static string? source;

    private readonly CoreWebView2 core;
    private string? scriptId;

    public bool On { get; private set; } = Launch.Get("macmillan-look") is { } v ? v != "off" : Prefs.Bool(Key, true);

    /// <summary>A page of Macmillan's (its Integration Hub, its assessment player), or the mock's stand-in for one.</summary>
    public static bool Applies(Uri? u) =>
        u != null && (u.Host.Equals("macmillanlearning.com", StringComparison.OrdinalIgnoreCase)
            || u.Host.EndsWith(".macmillanlearning.com", StringComparison.OrdinalIgnoreCase)
            || (u.Host is "localhost" or "127.0.0.1" && u.AbsolutePath.StartsWith("/mock-macmillan/", StringComparison.Ordinal)));

    private MacmillanLook(CoreWebView2 core) => this.core = core;

    /// <summary>The look put into the web view's pages and frames from their start (before Macmillan's page first shows),
    /// and what it finds heard, for the log.</summary>
    public static async Task<MacmillanLook> Attach(CoreWebView2 core)
    {
        var look = new MacmillanLook(core);
        core.WebMessageReceived += (_, e) => Heard(e);
        await look.Put();
        return look;
    }

    private async Task Put()
    {
        source ??= Read();
        if (source == null) return;
        try { scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync($"window.__simplMMOn = {(On ? "true" : "false")};\n{source}"); }
        catch (Exception e) { Log.Write("macmillan look: the script could not be added: " + e.Message); }
    }

    private static string? Read()
    {
        try { return File.ReadAllText(Path.Combine(ScriptBundle.ExtensionDir, "tools", "macmillan", "look.js")); }
        catch (Exception e) { Log.Write("macmillan look: the app has no copy of the look's script: " + e.Message); return null; }
    }

    /// <summary>Turned on or off (kept): the page showing follows at once (the page passes it on to its frames), and the
    /// pages loaded from now on start as it is — the script put in again with the new setting.</summary>
    public async Task Set(bool on)
    {
        On = on;
        Prefs.Set(Key, on);
        Log.Write($"macmillan look: {(on ? "on" : "off")}");
        try { await core.ExecuteScriptAsync($"window.__simplMMSet && window.__simplMMSet({(on ? "true" : "false")})"); } catch { /* the web view gone */ }
        if (scriptId != null) { try { core.RemoveScriptToExecuteOnDocumentCreated(scriptId); } catch { /* the web view gone */ } scriptId = null; }
        await Put();
    }

    /// <summary>What the script found on the player (said by the top page for its frames), once a page.</summary>
    private static void Heard(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var said = JsonDocument.Parse(e.WebMessageAsJson);
            if (said.RootElement.ValueKind != JsonValueKind.Object || !said.RootElement.TryGetProperty("simplLook", out var o)) return;
            string S(string k) => o.TryGetProperty(k, out var v) ? v.ToString() : "?";
            var here = S("here");
            Log.Write($"macmillan look: {S("said")} in {S("host")}, {S("rows")} questions, {(here is "" or "?" ? "none" : here)} marked");
        }
        catch { /* (not the look's: the recorder's, or the page's own) */ }
    }
}
