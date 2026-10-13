using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Simpl.Support;

namespace Simpl.Engine;

/// <summary>
/// What the screens were last told, kept on disk for the school signed in to (the Mac's AnswerCache): a screen opens
/// on what it showed last time at once, and the live answer replaces it when it is in. Only reads are kept; signing
/// out clears it all. Each answer is sealed by the vault, in a file whose name — and its school's folder's — is the
/// vault's HMAC of what it holds, so neither the school nor what was read can be told from the names.
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

    private readonly string school;
    private readonly string dir;
    private readonly Dictionary<string, string> memory = new();
    private readonly List<string> order = new();
    private bool stopped;

    /// <summary>The cache of one school: <paramref name="key"/> is its host (and port).</summary>
    public AnswerCache(string key)
    {
        school = key;
        dir = Path.Combine(AppPaths.Answers, Vault.FileName("answers-school:" + key));
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(dir);
                var cutoff = DateTime.UtcNow.AddDays(-30); // (what has not been read in a month, let go)
                foreach (var f in Directory.EnumerateFiles(dir, "*.bin"))
                    if (File.GetLastWriteTimeUtc(f) < cutoff) File.Delete(f);
                // (what an earlier Simpl kept in plain text — a folder per school, a .json per answer — only a cache:
                // deleted, not sealed)
                foreach (var f in Directory.EnumerateFiles(AppPaths.Answers, "*.json", SearchOption.AllDirectories)) File.Delete(f);
                foreach (var d in Directory.EnumerateDirectories(AppPaths.Answers))
                    if (!string.Equals(d, dir, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            }
            catch { }
        });
    }

    /// <summary>The name a read is kept under: its name and its arguments, in a stable order, hashed.</summary>
    public static string? FileName(string name, JsonObject args)
    {
        if (!Reads.Contains(name)) return null;
        var sorted = Sort(args).ToJsonString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name + "|" + sorted));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonNode? Sort(JsonNode? n) => n switch
    {
        JsonObject o => new JsonObject(o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => KeyValuePair.Create(kv.Key, Sort(kv.Value)))),
        JsonArray a => new JsonArray(a.Select(Sort).ToArray()),
        _ => n?.DeepClone(),
    };

    /// <summary>What a kept answer is sealed for: this school's, this read's — so no file opens as another.</summary>
    private string Purpose(string f) => "answers:" + school + "/" + f;

    /// <summary>Its file: named by the vault, from its purpose.</summary>
    private string PathOf(string f) => Path.Combine(dir, Vault.FileName(Purpose(f)) + ".bin");

    public string? Text(string name, JsonObject args)
    {
        var f = FileName(name, args);
        if (f == null) return null;
        if (memory.TryGetValue(f, out var t)) return t;
        try
        {
            var read = Vault.Read(PathOf(f), Purpose(f), cache: true);
            try
            {
                if (read.State != VaultState.Sealed) return null;
                var text = read.Text!;
                Remember(f, text);
                return text;
            }
            finally { read.Zero(); }
        }
        catch { return null; }
    }

    public void Keep(string text, string name, JsonObject args)
    {
        if (stopped) return;
        var f = FileName(name, args);
        if (f == null) return;
        Remember(f, text);
        Task.Run(() => { try { Directory.CreateDirectory(dir); Vault.Write(PathOf(f), text, Purpose(f)); } catch { } });
    }

    private void Remember(string f, string text)
    {
        if (!memory.ContainsKey(f)) order.Add(f);
        memory[f] = text;
        while (order.Count > 48) { memory.Remove(order[0]); order.RemoveAt(0); }
    }

    /// <summary>Nothing kept from here on (Reset Everything and Uninstall's erase: Simpl is about to restart or quit).</summary>
    public void Stop() { stopped = true; memory.Clear(); order.Clear(); }

    /// <summary>Everything kept, gone (signing out).</summary>
    public void Clear()
    {
        memory.Clear();
        order.Clear();
        Task.Run(() => { try { Directory.Delete(dir, true); } catch { } });
    }
}
