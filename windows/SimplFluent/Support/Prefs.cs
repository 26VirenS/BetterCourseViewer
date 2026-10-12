using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simpl.Support;

/// <summary>
/// Where the app keeps its files: %LOCALAPPDATA%\Simpl (or, with --profile, a folder of its own — the screenshot suite
/// runs several copies at once, each apart).
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Make(Launch.Get("profile") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Simpl"));
    public static string WebData => Make(Path.Combine(Root, "WebView2"));
    public static string Answers => Make(Path.Combine(Root, "Answers"));
    public static string Storage => Path.Combine(Root, "storage.json");
    public static string PrefsFile => Path.Combine(Root, "prefs.json");
    /// <summary>The folder the app runs from: the extension and the bridge are beside it.</summary>
    public static string AppDir => AppContext.BaseDirectory;

    private static string Make(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* read-only: the calls that write say so */ }
        return dir;
    }
}

/// <summary>The app's own preferences (the Mac's UserDefaults): a small JSON file, written as it changes.</summary>
public static class Prefs
{
    private static readonly object Gate = new();
    private static JsonObject? data;
    private static bool erased;

    private static JsonObject Data
    {
        get
        {
            if (data != null) return data;
            try { data = JsonNode.Parse(File.ReadAllText(AppPaths.PrefsFile)) as JsonObject; } catch { data = null; }
            return data ??= new JsonObject();
        }
    }

    public static string? String(string key) { lock (Gate) return Data[key]?.GetValue<string>(); }

    public static bool Bool(string key, bool fallback = false)
    {
        lock (Gate) { try { return Data[key]?.GetValue<bool>() ?? fallback; } catch { return fallback; } }
    }

    public static double Double(string key, double fallback = 0)
    {
        lock (Gate) { try { return Data[key]?.GetValue<double>() ?? fallback; } catch { return fallback; } }
    }

    public static void Set(string key, object? value)
    {
        lock (Gate)
        {
            if (value == null) Data.Remove(key);
            else Data[key] = JsonSerializer.SerializeToNode(value);
            Save();
        }
    }

    public static void Remove(string key) => Set(key, null);

    /// <summary>Every key and its value, for the Developer window.</summary>
    public static IReadOnlyList<(string Key, string Value)> All()
    {
        lock (Gate) return Data.Select(kv => (kv.Key, kv.Value?.ToJsonString() ?? "null")).OrderBy(k => k.Key).ToList();
    }

    /// <summary>
    /// The file deleted, and nothing written after it by this copy (Reset Everything restarts it, Uninstall quits it):
    /// what is changed on the way out — the window's place, saved as it closes — would write every preference back.
    /// </summary>
    public static void Erase()
    {
        lock (Gate)
        {
            erased = true;
            try { File.Delete(AppPaths.PrefsFile); } catch { }
        }
    }

    private static void Save()
    {
        if (erased) return;
        try
        {
            var tmp = AppPaths.PrefsFile + ".tmp";
            File.WriteAllText(tmp, Data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, AppPaths.PrefsFile, true);
        }
        catch { /* the next write tries again */ }
    }
}
