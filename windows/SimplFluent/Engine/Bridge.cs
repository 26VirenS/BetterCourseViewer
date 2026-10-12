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

    /// <summary>The app's own chrome: what the page says about it goes there.</summary>
    public IShellListener? Shell { get; set; }

    public event Action? SignOutRequested;
    public event Action? OpenSettingsRequested;
    public event Action<JsonObject>? SettingsChanged;

    /// <summary>Keys written before the first page (--demo: as a student who has used the app).</summary>
    public void Seed(JsonObject items) => store.Set(items);

    public void EraseStorage() => store.Erase();

    public void SaveNow() => store.SaveNow();

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
                Broadcast(changes);
                if (items["settings"] is JsonObject settings) SettingsChanged?.Invoke(settings);
                return null;
            }
            case "storage.remove":
                Broadcast(store.Remove((body["keys"] as JsonArray)?.Select(k => k?.ToString() ?? "") ?? Enumerable.Empty<string>()));
                return null;
            case "storage.clear":
                Broadcast(store.Clear());
                return null;
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
