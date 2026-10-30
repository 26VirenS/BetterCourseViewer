using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Simpl.Engine;
using Simpl.Support;

namespace Simpl.Shell;

/// <summary>
/// (0.3.92) Simpl's look over a tool's own page, restyled as an Alta question is in the apps — styles only, from the
/// extension the app carries (the Mac app's too): nothing of the page is taken out, and every question is answered as on
/// the tool's own page (the old Macmillan skin, a second page drawn from Macmillan's, was taken out in 0.3.58).
/// Macmillan Learning (extension/tools/macmillan/look.js): a glass rail of questions with the one open marked, a glass
/// bar of its tools, the question's number large over its sheet, and Submit Answer / Next Question with the verdict at the
/// sheet's foot. (0.3.93) ALEKS (extension/tools/aleks/look.js): a light glass bar with the topic and its progress, the
/// question on a sheet, its tools in a glass column beside it, and the actions (Explanation, Check, …) on a glass strip
/// under the answer. Each script is put into every page and frame from their start and draws only on its tool's pages;
/// each look is on until the title bar's Simpl Look turns it off there (kept, one setting per tool). A picture run's
/// --macmillan-look off / --aleks-look off starts with it off. (0.3.94) In the Macmillan and ALEKS popup
/// (Screens/Look/LookPopup.cs), attached with <c>rail</c>: each script is told its page has the popup's rail beside it,
/// and what the page tells that rail as it changes is handed on (<see cref="Rail"/>).
/// </summary>
internal sealed class PageLook
{
    public sealed record Kind(string Name, string Title, string Key, string Dir, string Flag, string Setter, string Domain, string Mock, string Arg);

    public static readonly Kind Macmillan = new("macmillan", "Macmillan", "macmillanLook", "macmillan", "__simplMMOn", "__simplMMSet", "macmillanlearning.com", "/mock-macmillan/", "macmillan-look");
    public static readonly Kind Aleks = new("aleks", "ALEKS", "aleksLook", "aleks", "__simplAKOn", "__simplAKSet", "aleks.com", "/mock-aleks/", "aleks-look");
    private static readonly Kind[] All = { Macmillan, Aleks };

    /// <summary>(0.3.94) A look by its name (the popup's launch says which): Macmillan's for a name it does not know.</summary>
    public static Kind Named(string name) => All.FirstOrDefault(k => k.Name == name) ?? Macmillan;

    /// <summary>The looks' scripts, from the extension the app carries (each read once).</summary>
    private static readonly Dictionary<string, string?> Sources = new();

    private readonly CoreWebView2 core;
    private readonly bool rail;
    /// <summary>(0.3.95) The popup's own front end over the page ("card": the tool's bar out of sight in the page, its
    /// buttons the popup's), or "" for none.</summary>
    private readonly string front;
    private readonly Dictionary<string, string> scriptIds = new();
    private readonly Dictionary<string, bool> on = new();

    /// <summary>(0.3.94) What the page tells the popup's rail, each time it changes (its <c>rail</c> names the look).</summary>
    public event Action<JsonElement>? Rail;

    private PageLook(CoreWebView2 core, bool rail, string front)
    {
        this.core = core;
        this.rail = rail;
        this.front = front;
    }

    public bool On(Kind k) => on.TryGetValue(k.Name, out var v) ? v : Start(k);

    private static bool Start(Kind k) => Launch.Get(k.Arg) is { } v ? v != "off" : Prefs.Bool(k.Key, true);

    /// <summary>The look for a page of a tool's (any of its hosts), or the mock's stand-in for one; null for any other.</summary>
    public static Kind? For(Uri? u) => u == null ? null : All.FirstOrDefault(k =>
        u.Host.Equals(k.Domain, StringComparison.OrdinalIgnoreCase)
        || u.Host.EndsWith("." + k.Domain, StringComparison.OrdinalIgnoreCase)
        || (u.Host is "localhost" or "127.0.0.1" && u.AbsolutePath.StartsWith(k.Mock, StringComparison.Ordinal)));

    /// <summary>Every look put into the web view's pages and frames from their start (before the tool's page first shows),
    /// and what they find heard, for the log.</summary>
    public static async Task<PageLook> Attach(CoreWebView2 core, bool rail = false, string front = "")
    {
        var look = new PageLook(core, rail, front);
        core.WebMessageReceived += (_, e) => look.Heard(e);
        foreach (var k in All)
        {
            look.on[k.Name] = Start(k);
            await look.Put(k);
        }
        return look;
    }

    private async Task Put(Kind k)
    {
        if (!Sources.TryGetValue(k.Dir, out var source)) Sources[k.Dir] = source = Read(k);
        if (source == null) return;
        var head = $"window.{k.Flag} = {(On(k) ? "true" : "false")};" + (rail || front.Length > 0 ? " window.__simplLookRail = true;" : "")
            + (front.Length > 0 ? $" window.__simplLookFront = '{front}';" : "");
        try { scriptIds[k.Name] = await core.AddScriptToExecuteOnDocumentCreatedAsync($"{head}\n{source}"); }
        catch (Exception e) { Log.Write($"{k.Name} look: the script could not be added: " + e.Message); }
    }

    private static string? Read(Kind k)
    {
        try { return File.ReadAllText(Path.Combine(ScriptBundle.ExtensionDir, "tools", k.Dir, "look.js")); }
        catch (Exception e) { Log.Write($"{k.Name} look: the app has no copy of the look's script: " + e.Message); return null; }
    }

    /// <summary>Turned on or off (kept): the page showing follows at once (the page passes it on to its frames), and the
    /// pages loaded from now on start as it is — the script put in again with the new setting.</summary>
    public async Task Set(Kind k, bool value)
    {
        on[k.Name] = value;
        Prefs.Set(k.Key, value);
        Log.Write($"{k.Name} look: {(value ? "on" : "off")}");
        try { await core.ExecuteScriptAsync($"window.{k.Setter} && window.{k.Setter}({(value ? "true" : "false")})"); } catch { /* the web view gone */ }
        if (scriptIds.Remove(k.Name, out var id)) { try { core.RemoveScriptToExecuteOnDocumentCreated(id); } catch { /* the web view gone */ } }
        await Put(k);
    }

    /// <summary>What a look found on its page (said by the top page for its frames), once a page: Macmillan's its
    /// questions and the one marked, ALEKS's what it found of the page (its "look" names it). (0.3.94) What it tells the
    /// popup's rail (its "rail" names it) is handed on, not logged.</summary>
    private void Heard(CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var said = JsonDocument.Parse(e.WebMessageAsJson);
            if (said.RootElement.ValueKind != JsonValueKind.Object || !said.RootElement.TryGetProperty("simplLook", out var o)) return;
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty("rail", out _))
            {
                Rail?.Invoke(o.Clone());
                return;
            }
            string S(string k) => o.TryGetProperty(k, out var v) ? v.ToString() : "?";
            if (o.TryGetProperty("look", out var name))
            {
                Log.Write($"{name} look: {S("said")} in {S("host")}, {S("detail")}");
                return;
            }
            var here = S("here");
            Log.Write($"macmillan look: {S("said")} in {S("host")}, {S("rows")} questions, {(here is "" or "?" ? "none" : here)} marked");
        }
        catch { /* (not a look's: the recorder's, or the page's own) */ }
    }
}
