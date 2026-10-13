using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Simpl.Support;

/// <summary>
/// (The screenshot suite's runs: --shot.) The page's own errors written down — a script that threw and nobody caught
/// it, a promise rejected that nobody handled, a console.error — as the web view's DevTools protocol reports them, in
/// page-errors.log beside simpl.log, so CI finds them in the logs it reads after the pictures (scripts/dev/scan-logs.py)
/// rather than nobody ever seeing them. A file of its own: a page's words are not Simpl's, and simpl.log is held to
/// naming no username (the pictures' encryption check). Nothing of this runs for a student.
/// </summary>
public static class PageErrors
{
    /// <summary>At most this many lines a run: a page that throws in a loop says so, and does not fill the log.</summary>
    private const int Most = 60;
    private static int written;
    private static readonly object Gate = new();

    public static async void Watch(CoreWebView2 core)
    {
        if (!Shot.Asked) return;
        try
        {
            core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived += (_, e) => Write("uncaught " + Thrown(e.ParameterObjectAsJson));
            core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled").DevToolsProtocolEventReceived += (_, e) =>
            {
                if (ConsoleError(e.ParameterObjectAsJson) is { } text) Write("console.error " + text);
            };
            await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");
        }
        catch (Exception e) { Log.Write("page errors not watched: " + e.Message); }
    }

    private static void Write(string line)
    {
        if (++written > Most) return;
        var text = "page: " + (line.Length > 500 ? line[..500] + "…" : line) + (written == Most ? " (and no more of them are written)" : "");
        try
        {
            lock (Gate) File.AppendAllText(Path.Combine(AppPaths.Root, "page-errors.log"), $"{DateTime.Now:HH:mm:ss.fff} {text}\n");
        }
        catch { /* nowhere to write: the picture is still taken */ }
    }

    /// <summary>"TypeError: x is not a function (http://…/app.js:12)": the exception's first line and where it was thrown.</summary>
    private static string Thrown(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var d = doc.RootElement.GetProperty("exceptionDetails");
            var text = d.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var desc) ? desc.GetString() : null;
            text ??= d.TryGetProperty("text", out var t) ? t.GetString() : null;
            var first = (text ?? "an exception").Split('\n')[0].Trim();
            var url = d.TryGetProperty("url", out var u) ? u.GetString() : null;
            var line = d.TryGetProperty("lineNumber", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() + 1 : 0;
            return string.IsNullOrEmpty(url) ? first : $"{first} ({url}:{line})";
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return "an exception (unreadable)"; }
    }

    /// <summary>A console.error's words (each argument's value, or its description), or null for any other console call.</summary>
    private static string? ConsoleError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() is not ("error" or "assert")) return null;
            var parts = new List<string>();
            if (root.TryGetProperty("args", out var args))
                foreach (var a in args.EnumerateArray())
                {
                    var s = a.TryGetProperty("value", out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                        : a.TryGetProperty("description", out var dd) ? dd.GetString()?.Split('\n')[0] : null;
                    if (!string.IsNullOrEmpty(s)) parts.Add(s);
                }
            return parts.Count > 0 ? string.Join(' ', parts) : "(no words)";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }
}
