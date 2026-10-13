using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// The extension's <c>storage.local</c>: one JSON file (%LOCALAPPDATA%\Simpl\storage.json, sealed by the vault), mirrored
/// in memory — the Mac's BridgeStore. Values are whatever the scripts store (settings, preferences, cached Canvas
/// answers). Used on the UI thread; the file is read and opened as the app starts (<see cref="Preload"/>, once the vault
/// is open), and each write is coalesced, put into words on the UI thread, sealed and written off it, one after another.
/// </summary>
public sealed class BridgeStore
{
    private const string Purpose = "storage";
    private static Task<(JsonObject Data, bool Legacy)>? preloaded;

    private JsonObject data;
    private readonly string file = AppPaths.Storage;
    private readonly DispatcherTimer saveTimer;
    /// <summary>The writer: each save written after the one before it, off the UI thread.</summary>
    private Task writing = Task.CompletedTask;
    private readonly object gate = new();
    private bool erased;

    /// <summary>storage.json read on a worker as the app starts (Program.Main, just after the vault began opening: this
    /// waits for it), for the store to take once it is made.</summary>
    public static void Preload() => preloaded ??= Task.Run(async () =>
    {
        await Vault.Ready;
        return Load();
    });

    /// <summary>What storage.json holds, and whether it was plain text an earlier Simpl kept (to be written sealed).</summary>
    private static (JsonObject Data, bool Legacy) Load()
    {
        var read = Vault.Read(AppPaths.Storage, Purpose);
        try { if (read.Plain != null) return (JsonNode.Parse(read.Plain) as JsonObject ?? new JsonObject(), read.Legacy); } catch { }
        finally { read.Zero(); }
        return (new JsonObject(), false);
    }

    public BridgeStore()
    {
        // (read as the app started — in by now, as a rule; else read here)
        var (loaded, legacy) = preloaded?.GetAwaiter().GetResult() ?? Load();
        data = loaded;
        preloaded = null;
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSoon(); };
        // (kept in plain text by an earlier Simpl: written sealed in a moment, over it)
        if (legacy) ScheduleSave();
    }

    /// <summary>Chrome's shapes: nothing/null = everything, a key, a list of keys, or {key: default}.</summary>
    public JsonObject Get(JsonNode? keys)
    {
        var o = new JsonObject();
        switch (keys)
        {
            case null:
                foreach (var kv in data) o[kv.Key] = kv.Value?.DeepClone();
                break;
            case JsonValue v when v.TryGetValue<string>(out var key):
                if (data.TryGetPropertyValue(key, out var val)) o[key] = val?.DeepClone();
                break;
            case JsonArray list:
                foreach (var k in list)
                {
                    var name = k?.ToString();
                    if (name != null && data.TryGetPropertyValue(name, out var val2)) o[name] = val2?.DeepClone();
                }
                break;
            case JsonObject defaults:
                foreach (var kv in defaults) o[kv.Key] = (data.TryGetPropertyValue(kv.Key, out var have) ? have : kv.Value)?.DeepClone();
                break;
        }
        return o;
    }

    public JsonNode? Value(string key) => data.TryGetPropertyValue(key, out var v) ? v : null;

    /// <summary>The changes in storage.onChanged's shape: {key: {oldValue?, newValue?}}.</summary>
    public JsonObject Set(JsonObject items)
    {
        var changes = new JsonObject();
        foreach (var kv in items)
        {
            var change = new JsonObject { ["newValue"] = kv.Value?.DeepClone() };
            if (data.TryGetPropertyValue(kv.Key, out var old)) change["oldValue"] = old?.DeepClone();
            changes[kv.Key] = change;
            data[kv.Key] = kv.Value?.DeepClone();
        }
        ScheduleSave();
        return changes;
    }

    public JsonObject Remove(IEnumerable<string> keys)
    {
        var changes = new JsonObject();
        foreach (var k in keys)
        {
            if (data.TryGetPropertyValue(k, out var old)) { changes[k] = new JsonObject { ["oldValue"] = old?.DeepClone() }; data.Remove(k); }
        }
        ScheduleSave();
        return changes;
    }

    public JsonObject Clear()
    {
        var changes = new JsonObject();
        foreach (var kv in data) changes[kv.Key] = new JsonObject { ["oldValue"] = kv.Value?.DeepClone() };
        data = new JsonObject();
        ScheduleSave();
        return changes;
    }

    /// <summary>Everything gone and the file deleted; nothing written after it (the app is about to restart) — a write
    /// under way finishes first, and is deleted with the rest.</summary>
    public void Erase()
    {
        saveTimer.Stop();
        lock (gate)
        {
            erased = true;
            data = new JsonObject();
            try { File.Delete(file); } catch { }
        }
    }

    private void ScheduleSave()
    {
        if (erased) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>The store as it is now, to the writer: put into words here (its values are the UI thread's), written off it.</summary>
    private void SaveSoon()
    {
        if (erased) return;
        var text = data.ToJsonString();
        writing = writing.ContinueWith(_ => Write(text), TaskScheduler.Default);
    }

    /// <summary>Written now (the app is closing), once the writer has written what it has.</summary>
    public void SaveNow()
    {
        if (erased) return;
        saveTimer.Stop();
        var text = data.ToJsonString();
        try { writing.Wait(); } catch { }
        Write(text);
    }

    private void Write(string text)
    {
        lock (gate)
        {
            if (erased) return; // (erased meanwhile: nothing written after it)
            try { Vault.Write(file, text, Purpose); }
            catch { /* the next change tries again */ }
        }
    }
}
