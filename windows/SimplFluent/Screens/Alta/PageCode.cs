using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Simpl.Support;

namespace Simpl.Screens;

/// <summary>
/// (0.3.27) Save Page Code for a popup that does not hear its page as Alta's does (Macmillan's): the frames in it kept as
/// they come, and Alta's page-code script (AltaHook.Collect) run in the page and in each of them, its answers heard on
/// their way back — the page, every frame's HTML (with any web component's own tree), and the scripts and style sheets
/// they load, in one text file in Downloads, addresses without their queries (a launch's tokens ride in them).
/// </summary>
internal sealed class PageCode
{
    private readonly CoreWebView2 core;
    private readonly string vendor;
    private readonly List<CoreWebView2Frame> frames = new();
    private readonly Dictionary<string, TaskCompletionSource<JsonNode?>> asked = new();

    public PageCode(CoreWebView2 core, string vendor)
    {
        this.core = core;
        this.vendor = vendor;
        core.WebMessageReceived += (_, e) => Heard(e);
        core.FrameCreated += (_, e) => Watch(e.Frame);
    }

    private void Watch(CoreWebView2Frame f)
    {
        try
        {
            frames.Add(f);
            if (frames.Count > 30) frames.RemoveAt(0);
            f.WebMessageReceived += (_, e) => Heard(e);
            f.FrameCreated += (_, e) => Watch(e.Frame);
            f.Destroyed += (_, _) => frames.Remove(f);
        }
        catch { /* an older runtime: the frame's code is not had */ }
    }

    private void Heard(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? text;
        try { text = e.TryGetWebMessageAsString(); } catch { return; }
        if (text == null || !text.Contains("\"code\"")) return;
        try
        {
            if (JsonNode.Parse(text) is JsonObject o && o["kind"]?.GetValue<string>() == "code" && o["token"]?.GetValue<string>() is { } token && asked.Remove(token, out var t))
                t.TrySetResult(o["result"]?.DeepClone());
        }
        catch { }
    }

    private static string? Str(JsonNode? n)
    {
        try { return n?.GetValue<string>(); } catch { return n?.ToString(); }
    }

    /// <summary>The file written (its path), or null: nothing could be read, or it could not be written.</summary>
    public async Task<string?> Save()
    {
        var line = new string('=', 100);
        var out_ = new StringBuilder($"Simpl for Windows {Updates.Version}: {vendor} page code, saved {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}\n");
        var seen = new HashSet<string>();
        var count = 0;
        var targets = new List<CoreWebView2Frame?> { null };
        targets.AddRange(frames.ToList());
        foreach (var f in targets)
        {
            if (await Collect(f) is not JsonObject r) continue;
            var url = Str(r["url"]) ?? "?";
            var html = Str(r["html"]) ?? "";
            if (!seen.Add(url + "#" + html.Length)) continue;
            count++;
            out_.Append($"\n\n{line}\n");
            out_.Append($"PAGE {count}{(f == null ? " (the popup's page)" : " (a frame in it)")}: {url}\n");
            out_.Append($"Title: {Str(r["title"]) ?? ""}\n{line}\n");
            out_.Append("\n---- HTML ----\n").Append(html).Append('\n');
            foreach (var sh in (r["shadows"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                out_.Append($"\n---- WEB COMPONENT'S OWN TREE: {Str(sh["host"]) ?? ""} ----\n").Append(Str(sh["html"]) ?? "").Append('\n');
            foreach (var file in (r["files"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                out_.Append($"\n---- {(Str(file["kind"]) ?? "file").ToUpperInvariant()}: {Str(file["url"]) ?? ""} ----\n");
                if (Str(file["text"]) is { } t) out_.Append(t).Append('\n');
                else out_.Append($"(could not be read: {Str(file["error"]) ?? "unknown"})\n");
            }
        }
        if (count == 0) return null;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd 'at' HH.mm.ss", CultureInfo.InvariantCulture);
        try
        {
            var dir = AltaSession.Downloads();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"Simpl {vendor} Code {stamp}.txt");
            await File.WriteAllTextAsync(path, out_.ToString());
            return path;
        }
        catch { return null; }
    }

    private async Task<JsonNode?> Collect(CoreWebView2Frame? frame)
    {
        var token = Guid.NewGuid().ToString("N");
        var done = new TaskCompletionSource<JsonNode?>();
        asked[token] = done;
        var js = "(async function () {\n" + AltaHook.Collect + "\n})().then(function (r) { window.chrome.webview.postMessage(JSON.stringify({ kind: 'code', token: '" + token
                 + "', result: r })); }, function () { window.chrome.webview.postMessage(JSON.stringify({ kind: 'code', token: '" + token + "', result: null })); }); null";
        try
        {
            if (frame == null) await core.ExecuteScriptAsync(js);
            else if (frame.IsDestroyed() == 0) await frame.ExecuteScriptAsync(js);
            else return null;
            var finished = await Task.WhenAny(done.Task, Task.Delay(40_000));
            return finished == done.Task ? done.Task.Result : null;
        }
        catch { return null; }
        finally { asked.Remove(token); }
    }
}
