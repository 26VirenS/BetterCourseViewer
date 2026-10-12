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

/// <summary>
/// The app's own preferences (the Mac's UserDefaults): a small JSON file. A change is in memory at once, for every
/// reader; the file is written a moment after the changes stop (a window dragged changes its place dozens of times a
/// second), off the UI thread, and as the app ends if anything is still to be written.
/// </summary>
public static class Prefs
{
    // Gate guards the values and is held only briefly, never over the disk; WriteGate guards the file and is taken
    // first (a write, Erase) — so readers never wait on a write, and no write lands after Erase.
    private static readonly object Gate = new();
    private static readonly object WriteGate = new();
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);
    private const long LongestWait = 2000; // ms
    private static JsonObject? data;
    private static bool erased, dirty, armed;
    private static long armedAt;
    private static Timer? timer;

    // (Quit, Restart to Update, Reset Everything's restart and the last-resort Environment.Exit all end here)
    static Prefs() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();

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

    /// <summary>Seen by every reader at once; the file follows a moment later (nothing to write if nothing changed).</summary>
    public static void Set(string key, object? value)
    {
        var node = value == null ? null : JsonSerializer.SerializeToNode(value);
        lock (Gate)
        {
            if (value == null)
            {
                if (!Data.Remove(key)) return;
            }
            else
            {
                if (Data.TryGetPropertyValue(key, out var old) && JsonNode.DeepEquals(old, node)) return;
                Data[key] = node;
            }
            Changed();
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
        // (a write already under way finishes first; any after it finds the store erased and writes nothing)
        lock (WriteGate)
        {
            lock (Gate)
            {
                erased = true;
                dirty = armed = false;
                timer?.Dispose();
                timer = null;
            }
            try { File.Delete(AppPaths.PrefsFile); } catch { }
            try { File.Delete(AppPaths.PrefsFile + ".tmp"); } catch { }
        }
    }

    /// <summary>What is still to be written, written now (as the app ends; otherwise the timer does it).</summary>
    public static void Flush()
    {
        lock (WriteGate)
        {
            string json;
            lock (Gate)
            {
                armed = false;
                if (!dirty || erased) return;
                dirty = false;
                json = Data.ToJsonString(Indented);
            }
            // (WriteGate held from here to the move: Erase waits for it, so erased cannot turn true in between)
            try
            {
                var tmp = AppPaths.PrefsFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, AppPaths.PrefsFile, true);
            }
            catch
            {
                lock (Gate) dirty = true; // (the next change, or the app's end, tries again)
            }
        }
    }

    /// <summary>(Under Gate.) The file written once the changes have been quiet for a moment.</summary>
    private static void Changed()
    {
        if (erased) return;
        dirty = true;
        var now = Environment.TickCount64;
        if (!armed) { armed = true; armedAt = now; }
        // (put off while changes keep coming — a window dragged — but not past a couple of seconds: the timer, armed
        // all along, then goes off as it is)
        else if (now - armedAt >= LongestWait) return;
        timer ??= new Timer(_ => Flush());
        timer.Change(Quiet, Timeout.InfiniteTimeSpan);
    }
}
