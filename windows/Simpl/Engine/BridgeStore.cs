using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// The extension's <c>storage.local</c>: one JSON file (%LOCALAPPDATA%\Simpl\storage.json), mirrored in memory — the
/// Mac's BridgeStore. Values are whatever the scripts store (settings, preferences, cached Canvas answers). Used on the
/// UI thread; the file write is coalesced and done off it.
/// </summary>
public sealed class BridgeStore
{
    private JsonObject data = new();
    private readonly string file = AppPaths.Storage;
    private readonly DispatcherTimer saveTimer;
    private bool erased;

    public BridgeStore()
    {
        try { if (File.Exists(file)) data = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new JsonObject(); } catch { data = new JsonObject(); }
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
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

    /// <summary>Everything gone and the file deleted; nothing written after it (the app is about to restart).</summary>
    public void Erase()
    {
        erased = true;
        saveTimer.Stop();
        data = new JsonObject();
        try { File.Delete(file); } catch { }
    }

    private void ScheduleSave()
    {
        if (erased) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>Written now (the app is closing).</summary>
    public void SaveNow()
    {
        if (erased) return;
        var text = data.ToJsonString();
        try
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, file, true);
        }
        catch { /* the next change tries again */ }
    }
}
