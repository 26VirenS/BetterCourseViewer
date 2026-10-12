using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// What the screens were last told, kept on disk for the school signed in to (the Mac's AnswerCache): a screen opens
/// on what it showed last time at once, and the live answer replaces it when it is in. Only reads are kept; signing
/// out clears it all.
/// </summary>
public sealed class AnswerCache
{
    public static readonly HashSet<string> Reads = new()
    {
        "snapshot", "courses", "allCourses", "groups", "home", "today", "todaySheet", "todo", "calendar", "calView", "grades",
        "courseGrades", "coursesProgress", "notifications", "inbox", "conversation", "assignment", "topic",
        "announcements", "discussions", "assignments", "quizzes", "modules", "pages", "page", "files", "people",
        "syllabus", "dashCourses", "dashList", "dashActivity", "dashSkyline", "externalTools",
    };

    private readonly string dir;
    private readonly Dictionary<string, string> memory = new();
    private readonly List<string> order = new();
    private bool stopped;

    /// <summary>The cache of one school: <paramref name="key"/> is its host (and port).</summary>
    public AnswerCache(string key)
    {
        var safe = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        dir = Path.Combine(AppPaths.Answers, safe);
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(dir);
                var cutoff = DateTime.UtcNow.AddDays(-30); // (what has not been read in a month, let go)
                foreach (var f in Directory.EnumerateFiles(dir, "*.json"))
                    if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f);
            }
            catch { }
        });
    }

    /// <summary>The file a read is kept in: its name and its arguments, in a stable order.</summary>
    public static string? FileName(string name, JsonObject args)
    {
        if (!Reads.Contains(name)) return null;
        var sorted = Sort(args).ToJsonString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name + "|" + sorted));
        return Convert.ToHexString(hash).ToLowerInvariant() + ".json";
    }

    private static JsonNode? Sort(JsonNode? n) => n switch
    {
        JsonObject o => new JsonObject(o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, Sort(kv.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        _ => n?.DeepClone(),
    };

    public string? Text(string name, JsonObject args)
    {
        var f = FileName(name, args);
        if (f == null) return null;
        if (memory.TryGetValue(f, out var t)) return t;
        try
        {
            var text = File.ReadAllText(Path.Combine(dir, f));
            Remember(f, text);
            return text;
        }
        catch { return null; }
    }

    public void Keep(string text, string name, JsonObject args)
    {
        if (stopped) return;
        var f = FileName(name, args);
        if (f == null) return;
        Remember(f, text);
        var path = Path.Combine(dir, f);
        Task.Run(() => { try { Directory.CreateDirectory(dir); File.WriteAllText(path, text); } catch { } });
    }

    private void Remember(string f, string text)
    {
        if (!memory.ContainsKey(f)) order.Add(f);
        memory[f] = text;
        while (order.Count > 48) { memory.Remove(order[0]); order.RemoveAt(0); }
    }

    /// <summary>Nothing kept from here on (Reset Everything: the app is about to restart, erased).</summary>
    public void Stop() { stopped = true; memory.Clear(); order.Clear(); }

    /// <summary>Everything kept, gone (signing out).</summary>
    public void Clear()
    {
        memory.Clear();
        order.Clear();
        Task.Run(() => { try { Directory.Delete(dir, true); } catch { } });
    }
}
