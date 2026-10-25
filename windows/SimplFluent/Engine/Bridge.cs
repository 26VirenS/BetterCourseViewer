using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>Who hears what the page says about the app's own chrome (shell.state, shell.open, shell.tab).</summary>
public interface IShellListener
{
    void ShellMessage(string op, JsonObject body);
}

/// <summary>
/// The native half of bridge.js (the Mac's Bridge): extension storage, "sign out", "open settings", a question or a
/// short list the page asks as the app's own dialog and menu, a library injected on demand, and the shell's messages.
/// One instance serves every web view the app opens, so a setting changed in one reaches the others as
/// storage.onChanged does in a browser.
/// </summary>
public sealed class Bridge
{
    public static readonly Bridge Shared = new();

    private readonly BridgeStore store = new();
    private readonly List<WeakReference<Microsoft.Web.WebView2.Wpf.WebView2>> views = new();
    private bool savesAtExit;

    /// <summary>The app's own chrome: what the page says about it goes there.</summary>
    public IShellListener? Shell { get; set; }

    public event Action? SignOutRequested;
    public event Action? OpenSettingsRequested;
    public event Action<JsonObject>? SettingsChanged;

    /// <summary>Keys written before the first page (--demo: as a student who has used the app).</summary>
    public void Seed(JsonObject items) => store.Set(items);

    public void EraseStorage() => store.Erase();

    public void SaveNow() => store.SaveNow();

    /// <summary>The storage written as the app quits (Application.Exit), so a change in its last moments is kept — set
    /// once, by the first engine. After Reset Everything's erase, nothing is.</summary>
    public void SaveAtExit()
    {
        if (savesAtExit || Application.Current is not { } app) return;
        savesAtExit = true;
        app.Exit += (_, _) => SaveNow();
    }

    public JsonNode? Value(string key) => store.Value(key);

    /// <summary>The extension's saved settings (the appearance, among others), for the app's own chrome.</summary>
    public JsonObject Settings => store.Value("settings") as JsonObject ?? new JsonObject();

    public void Register(Microsoft.Web.WebView2.Wpf.WebView2 view) => views.Add(new WeakReference<Microsoft.Web.WebView2.Wpf.WebView2>(view));

    /// <summary>A web view let go of (before it is disposed): no longer told of storage changes.</summary>
    public void Unregister(Microsoft.Web.WebView2.Wpf.WebView2 view) =>
        views.RemoveAll(w => !w.TryGetTarget(out var v) || ReferenceEquals(v, view));

    /// <summary>
    /// What the app's settings always are, whatever writes them (the Mac's followingPhone): light or dark follows
    /// Windows ("system"), and Simpl's look is always on — off, the app's own screens would be gone.
    /// </summary>
    public static JsonObject FollowingSystem(JsonObject items)
    {
        if (items["settings"] is not JsonObject s || s["appearance"] is not JsonObject a) return items;
        var mode = a["darkMode"]?.ToString();
        var skin = a["skin"] is JsonValue sv && sv.TryGetValue<bool>(out var sk) ? sk : (bool?)null;
        var until = a["offUntil"] is JsonValue uv && uv.TryGetValue<double>(out var u) ? u : 0;
        if (!((mode != null && mode != "system") || skin == false || until != 0)) return items;
        if (mode != null) a["darkMode"] = "system";
        if (skin != null) a["skin"] = true;
        if (a.ContainsKey("offUntil")) a["offUntil"] = 0;
        return items;
    }

    /// <summary>At launch: a saved look or appearance from before brought into line.</summary>
    public void FollowSystemAppearance()
    {
        if (store.Value("settings") is not JsonObject s) return;
        var copy = new JsonObject { ["settings"] = s.DeepClone() };
        var fixedItems = FollowingSystem(copy);
        if (fixedItems["settings"]!.ToJsonString() != s.ToJsonString()) store.Set(fixedItems);
    }

    /// <summary>(0.3.60) The calendars linked in Calendar → Sync: their files read for the page, which cannot read another
    /// site (background.js calFile). No cookies, and nothing of Simpl's own goes with a read.</summary>
    private static readonly System.Net.Http.HttpClient Calendars = new(new System.Net.Http.HttpClientHandler { UseCookies = false, AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private const int CalendarMost = 8 * 1024 * 1024;

    /// <summary>A linked calendar's file (its private .ics address): http and https only, eight megabytes at most; its
    /// status and its text, or status 0 and why not (the Mac's Bridge.swift fetchText).</summary>
    private static async Task<JsonNode> FetchText(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return new JsonObject { ["status"] = 0, ["error"] = "That is not a web address." };
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, u);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var res = await Calendars.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            var status = (int)res.StatusCode;
            if (res.Content.Headers.ContentLength is long n && n > CalendarMost)
                return new JsonObject { ["status"] = 0, ["error"] = "That calendar is too big to read." };
            await using var stream = await res.Content.ReadAsStreamAsync();
            using var kept = new System.IO.MemoryStream();
            var buf = new byte[81920];
            int got;
            while ((got = await stream.ReadAsync(buf)) > 0)
            {
                kept.Write(buf, 0, got);
                if (kept.Length > CalendarMost) return new JsonObject { ["status"] = 0, ["error"] = "That calendar is too big to read." };
            }
            return new JsonObject { ["status"] = status, ["text"] = System.Text.Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length) };
        }
        catch (Exception e)
        {
            Log.Write("calendar read: " + e.Message);
            return new JsonObject { ["status"] = 0, ["error"] = e is TaskCanceledException ? "The calendar took too long to answer." : "The calendar could not be reached." };
        }
    }

    private const int FeedMost = 1024 * 1024;

    /// <summary>(0.3.63) Where Simpl's own calendar may be sent: simplcourses.com's place for them, or this computer's (the
    /// picture suite's stand-in).</summary>
    internal static bool FeedAddress(Uri u) =>
        u.AbsolutePath.StartsWith("/api/feed/", StringComparison.Ordinal)
        && ((u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("simplcourses.com", StringComparison.OrdinalIgnoreCase))
            || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    /// <summary>(0.3.63) Simpl's own calendar, sealed on the page, sent to simplcourses.com or taken back (background.js
    /// feedSend): the page cannot send to another site. There only, PUT or DELETE, the sealed bytes as base64, a megabyte
    /// at most, no cookies; its status, or status 0 and why not (the Mac's Bridge.swift feedSend).</summary>
    private static async Task<JsonNode> FeedSend(JsonObject body)
    {
        var address = body["url"]?.ToString() ?? "";
        var method = (body["method"]?.ToString() ?? "").ToUpperInvariant();
        var token = body["token"]?.ToString() ?? "";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var u) || !FeedAddress(u) || (method != "PUT" && method != "DELETE") || token.Length == 0)
            return new JsonObject { ["status"] = 0, ["error"] = "That is not a Simpl calendar address." };
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(method == "PUT" ? System.Net.Http.HttpMethod.Put : System.Net.Http.HttpMethod.Delete, u);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            if (method == "PUT")
            {
                byte[] sealedCopy;
                try { sealedCopy = Convert.FromBase64String(body["body"]?.ToString() ?? ""); }
                catch (FormatException) { sealedCopy = Array.Empty<byte>(); }
                if (sealedCopy.Length == 0 || sealedCopy.Length > FeedMost)
                    return new JsonObject { ["status"] = 0, ["error"] = "Your Simpl calendar is too big to send." };
                req.Content = new System.Net.Http.ByteArrayContent(sealedCopy);
                req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            }
            using var res = await Calendars.SendAsync(req);
            return new JsonObject { ["status"] = (int)res.StatusCode };
        }
        catch (Exception e)
        {
            Log.Write("calendar send: " + e.Message);
            return new JsonObject { ["status"] = 0, ["error"] = e is TaskCanceledException ? "simplcourses.com took too long to answer." : "Simpl could not reach simplcourses.com." };
        }
    }

    /// <summary>A message from bridge.js; its answer (or null), or an exception for an error.</summary>
    public async Task<JsonNode?> Handle(JsonObject body, Microsoft.Web.WebView2.Wpf.WebView2 from)
    {
        var op = body["op"]?.ToString() ?? "";
        switch (op)
        {
            case "storage.get":
                return store.Get(body["keys"]);
            case "storage.set":
            {
                var items = FollowingSystem(body["items"] as JsonObject ?? new JsonObject());
                var changes = store.Set(items);
                // (stored: telling the open pages and the app's chrome can no longer make the page's write fail)
                Told("storage.set", () => Broadcast(changes));
                if (items["settings"] is JsonObject settings) Told("settings", () => SettingsChanged?.Invoke(settings));
                return null;
            }
            case "storage.remove":
            {
                var changes = store.Remove((body["keys"] as JsonArray)?.Select(k => k?.ToString() ?? "") ?? Enumerable.Empty<string>());
                Told("storage.remove", () => Broadcast(changes));
                return null;
            }
            case "storage.clear":
            {
                var changes = store.Clear();
                Told("storage.clear", () => Broadcast(changes));
                return null;
            }
            case "signOut":
                SignOutRequested?.Invoke();
                return new JsonObject { ["ok"] = true };
            case "openOptions":
                OpenSettingsRequested?.Invoke();
                return new JsonObject { ["ok"] = true };
            case "log":
                System.Diagnostics.Debug.WriteLine("[Simpl Courses web] " + body["text"]);
                return null;
            case "ask":
                return new JsonObject { ["ok"] = await Ask(body) };
            case "menu":
                return new JsonObject { ["index"] = await Menu(body, from) };
            case "inject":
            {
                // (a library the converter draws on, from the app's own copy of the extension)
                foreach (var f in (body["files"] as JsonArray)?.Select(n => n?.ToString() ?? "") ?? Enumerable.Empty<string>())
                {
                    if (f.Contains("..")) return new JsonObject { ["ok"] = false };
                    var source = ScriptBundle.File(f);
                    if (source.Length == 0) return new JsonObject { ["ok"] = false };
                    await from.CoreWebView2.ExecuteScriptAsync(source + "\n;true");
                }
                return new JsonObject { ["ok"] = true };
            }
            case "fetchText":
                return await FetchText(body["url"]?.ToString() ?? "");
            case "feedSend":
                return await FeedSend(body);
            case "previewFile":
            {
                var url = body["url"]?.ToString() ?? "";
                if (Uri.TryCreate(url, UriKind.Absolute, out var u)) FilePreview.Open(u, body["name"]?.ToString() ?? "", from);
                return new JsonObject { ["ok"] = true };
            }
            case "navState":
            case "haptic":
                return null;
            case "shell.state":
            case "shell.page":
            case "shell.open":
            case "shell.tab":
            case "shell.focus":
                Shell?.ShellMessage(op, body);
                return null;
            default:
                return null;
        }
    }

    private static void Told(string what, Action tell)
    {
        try { tell(); } catch (Exception e) { Log.Write($"bridge {what}, after storing: {e}"); }
    }

    /// <summary>The app's own question for one the page asks (a destructive answer said as such): true for OK.</summary>
    private static Task<bool> Ask(JsonObject body)
    {
        var title = body["title"]?.ToString() ?? "";
        var note = body["note"]?.ToString() ?? "";
        var ok = body["okLabel"]?.ToString() ?? "OK";
        var cancel = body["cancelLabel"]?.ToString();
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        return Simpl.Shell.Dialogs.Ask(owner, title, note, ok, string.IsNullOrEmpty(cancel) ? null : cancel, body["danger"]?.GetValue<bool>() == true);
    }

    /// <summary>A short list the page offers (a menu, a picker), as the app's own pop-up menu: the index picked, or -1.</summary>
    private static Task<int> Menu(JsonObject body, FrameworkElement at)
    {
        var done = new TaskCompletionSource<int>();
        var menu = new ContextMenu { PlacementTarget = at };
        var title = body["title"]?.ToString();
        if (!string.IsNullOrEmpty(title)) menu.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        var i = 0;
        foreach (var item in (body["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var index = i++;
            var label = item["label"]?.ToString() ?? "";
            var sub = item["sub"]?.ToString();
            if (!string.IsNullOrEmpty(sub)) label += " · " + sub;
            var mi = new MenuItem { Header = label, IsChecked = item["active"]?.GetValue<bool>() == true };
            mi.Click += (_, _) => done.TrySetResult(index);
            menu.Items.Add(mi);
        }
        if (body["rect"] is JsonObject r)
        {
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
            menu.HorizontalOffset = r["x"]?.GetValue<double>() ?? 0;
            menu.VerticalOffset = (r["y"]?.GetValue<double>() ?? 0) + (r["height"]?.GetValue<double>() ?? 0);
        }
        menu.Closed += (_, _) => Application.Current.Dispatcher.BeginInvoke(() => done.TrySetResult(-1));
        menu.IsOpen = true;
        return done.Task;
    }

    /// <summary>storage.onChanged for every open web view, the one that wrote included (as browsers do).</summary>
    private void Broadcast(JsonObject changes)
    {
        if (changes.Count == 0) return;
        var js = $"window.BCVBridge&&BCVBridge.storageChanged({Json.Literal(changes)})";
        foreach (var w in views.ToList())
        {
            // (a page's web view let go of — a school page left, the school's preview, a popup closed — may not be
            // collected yet: touching it throws, and the save that asked must not fail for it; it is dropped)
            try
            {
                if (!w.TryGetTarget(out var v) || v.CoreWebView2 is not { } core) { views.Remove(w); continue; }
                _ = core.ExecuteScriptAsync(js).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                views.Remove(w);
            }
        }
    }
}
