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
/// vault's HMAC of what it holds, so neither the school nor what was read can be told from the names. The answers read
/// lately are kept in memory too (as their text, a few megabytes at most), with when the page gave them: a screen let go
/// and opened again, or read ahead, shows them at once — and one given moments ago, nothing changed since, is not asked
/// for again (<see cref="Fresh"/>).
/// </summary>
public sealed class AnswerCache
{
    public static readonly HashSet<string> Reads = new()
    {
        "snapshot", "courses", "allCourses", "groups", "home", "today", "todaySheet", "todo", "calendar", "calView", "grades",
        "courseGrades", "coursesProgress", "notifications", "inbox", "conversation", "assignment", "topic",
        "announcements", "discussions", "assignments", "quizzes", "modules", "pages", "page", "files", "people",
        "syllabus", "dashCourses", "dashList", "dashActivity", "dashSkyline", "externalTools", "moreFromCanvas",
    };

    /// <summary>An answer the page gave this long ago, nothing changed since, is as good as asking again.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(90);
    /// <summary>The most kept in memory: so many answers, so many characters in all.</summary>
    private const int MostAnswers = 160, MostChars = 3_000_000;

    /// <summary>An answer in memory: its text, and — when the page gave it while Simpl ran — when, and under which
    /// DataVersion (Environment.TickCount64; 0 for one read back from disk).</summary>
    private sealed record Kept(string Text, long At, int Version);

    private readonly string school;
    private readonly string dir;
    private readonly Dictionary<string, Kept> memory = new();
    /// <summary>The least lately used first.</summary>
    private readonly LinkedList<string> order = new();
    private readonly Dictionary<string, LinkedListNode<string>> nodes = new();
    private long chars;
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
        if (memory.TryGetValue(f, out var k)) { Touch(f); return k.Text; }
        try
        {
            var read = Vault.Read(PathOf(f), Purpose(f), cache: true);
            try
            {
                if (read.State != VaultState.Sealed) return null;
                var text = read.Text!;
                Remember(f, new Kept(text, 0, -1));
                return text;
            }
            finally { read.Zero(); }
        }
        catch { return null; }
    }

    /// <summary>When the page gave this read's answer (Environment.TickCount64), if that was less than
    /// <see cref="FreshFor"/> ago and nothing has changed since (<paramref name="version"/> is the engine's DataVersion).</summary>
    public long? Fresh(string name, JsonObject args, int version)
    {
        var f = FileName(name, args);
        if (f == null || !memory.TryGetValue(f, out var k) || k.At == 0 || k.Version != version) return null;
        return Environment.TickCount64 - k.At < FreshFor.TotalMilliseconds ? k.At : null;
    }

    /// <summary>A read's answer, as the page gave it under DataVersion <paramref name="version"/>: in memory, and sealed on disk.</summary>
    public void Keep(string text, string name, JsonObject args, int version)
    {
        if (stopped) return;
        var f = FileName(name, args);
        if (f == null) return;
        Remember(f, new Kept(text, Environment.TickCount64, version));
        Task.Run(() => { try { Directory.CreateDirectory(dir); Vault.Write(PathOf(f), text, Purpose(f)); } catch { } });
    }

    private void Remember(string f, Kept k)
    {
        if (memory.TryGetValue(f, out var was)) chars -= was.Text.Length;
        memory[f] = k;
        chars += k.Text.Length;
        Touch(f);
        while (order.Count > 1 && (order.Count > MostAnswers || chars > MostChars))
        {
            var oldest = order.First!.Value;
            order.RemoveFirst();
            nodes.Remove(oldest);
            if (memory.Remove(oldest, out var gone)) chars -= gone.Text.Length;
        }
    }

    /// <summary>Used now: the last to be let go.</summary>
    private void Touch(string f)
    {
        if (nodes.TryGetValue(f, out var n)) order.Remove(n);
        nodes[f] = order.AddLast(f);
    }

    private void Forget()
    {
        memory.Clear();
        order.Clear();
        nodes.Clear();
        chars = 0;
    }

    /// <summary>Nothing kept from here on (Reset Everything and Uninstall's erase: Simpl is about to restart or quit).</summary>
    public void Stop() { stopped = true; Forget(); }

    /// <summary>Everything kept, gone (signing out).</summary>
    public void Clear()
    {
        Forget();
        Task.Run(() => { try { Directory.Delete(dir, true); } catch { } });
    }
}
