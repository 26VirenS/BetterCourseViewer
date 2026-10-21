using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Simpl.Support;

/// <summary>
/// (0.3.51, the Mac's 1.3.146) Simpl's crash log, for the developer tools (Developer ▸ Crashes). Kept so that a run that
/// ended badly says why the next time Simpl opens:
/// - an exception nothing caught, written down the moment it comes (the app going down with it), and one the window's
///   own catch let pass (Simpl kept going: kept too, as "kept going");
/// - a mark that this run is going: still there at the next launch, the run before ended without quitting (a crash in
///   WebView2 or the GPU's drivers, a force quit from Task Manager, the PC turned off) — kept as a crash with what
///   simpl.log said last, and Windows' own report of it (the Application log's Application Error and .NET Runtime
///   events for Simpl.exe) where Windows wrote one.
/// Quit as asked (Quit, Restart to Update, Reset Everything, signing out of Windows): the mark is taken away.
/// </summary>
public static class CrashLog
{
    public sealed record Entry(string Path, DateTime At, string Title, string Version, bool Fatal);

    public static string Folder => System.IO.Path.Combine(AppPaths.Root, "Crashes");
    private static string Running => System.IO.Path.Combine(Folder, "running.json");
    private const int Keep = 30;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly object Gate = new();
    private static bool fatalWritten;

    /// <summary>At launch, once this copy is the one running: the run before looked at, this one marked.</summary>
    public static void Install()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(Running))
            {
                var before = Read(Running);
                if (!fatalWritten) RecordPrevious(before);
            }
            File.WriteAllText(Running, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["version"] = WhatsNew.Current,
                ["started"] = DateTime.Now.ToString("o", Inv),
                ["pid"] = Environment.ProcessId.ToString(Inv),
            }));
            // (any way out that is not a crash: Application.Shutdown, Environment.Exit — never an unhandled exception's)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Quitting();
        }
        catch (Exception e) { Log.Write("crash log: could not be set up: " + e.Message); }
    }

    public static void Quitting()
    {
        try { File.Delete(Running); } catch { }
    }

    /// <summary>An exception: <paramref name="fatal"/> when the app goes down with it (the mark then taken away, this
    /// file saying it all); else the window's catch let it pass and Simpl kept going.</summary>
    public static void Exception(object? what, bool fatal, string where)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var e = what as Exception;
                var title = e != null ? $"{e.GetType().Name}: {FirstLine(e.Message)}" : FirstLine(what?.ToString() ?? "Unknown");
                var text = new StringBuilder();
                text.AppendLine($"Title: {(fatal ? "" : "Kept going after ")}{title}");
                text.AppendLine($"Version: {WhatsNew.Current}");
                text.AppendLine($"Kind: {(fatal ? "crash" : "caught")}");
                text.AppendLine($"When: {DateTime.Now.ToString("F", CultureInfo.CurrentCulture)}");
                text.AppendLine($"Where: {where}");
                text.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, {Environment.ProcessorCount} processors");
                text.AppendLine();
                text.AppendLine("— What stopped it —");
                text.AppendLine(what?.ToString() ?? "(nothing said)");
                text.AppendLine();
                AppendLog(text);
                Write(text.ToString(), fatal ? "crash" : "caught");
                if (fatal)
                {
                    fatalWritten = true;
                    try { File.Delete(Running); } catch { }
                }
            }
        }
        catch { /* going down: nothing more to be done */ }
    }

    // ---- kept ----

    private static void RecordPrevious(Dictionary<string, string> before)
    {
        var version = before.GetValueOrDefault("version") ?? "?";
        var started = DateTime.TryParse(before.GetValueOrDefault("started"), Inv, DateTimeStyles.RoundtripKind, out var s) ? s : DateTime.Now.AddDays(-1);
        var windows = WindowsReports(started);
        var title = windows.Count > 0 ? windows[0].Title : "Ended without quitting (a crash, a force quit, or the PC turned off)";
        var text = new StringBuilder();
        text.AppendLine($"Title: {title}");
        text.AppendLine($"Version: {version}");
        text.AppendLine("Kind: crash");
        text.AppendLine($"Started: {started.ToString("F", CultureInfo.CurrentCulture)}");
        text.AppendLine($"Windows {Environment.OSVersion.Version}");
        text.AppendLine();
        foreach (var w in windows)
        {
            text.AppendLine($"— Windows' report ({w.At.ToString("G", CultureInfo.CurrentCulture)}) —");
            text.AppendLine(w.Text);
            text.AppendLine();
        }
        AppendLog(text);
        Write(text.ToString(), "crash");
    }

    /// <summary>simpl.log's last 300 lines: what Simpl was doing as it stopped.</summary>
    private static void AppendLog(StringBuilder text)
    {
        var lines = Tail(System.IO.Path.Combine(AppPaths.Root, "simpl.log"), 300);
        text.AppendLine($"— The last {lines.Count} lines of simpl.log —");
        foreach (var l in lines) text.AppendLine(l);
    }

    private static void Write(string text, string kind)
    {
        var name = $"{kind}-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff", Inv)}.txt";
        File.WriteAllText(System.IO.Path.Combine(Folder, name), text);
        foreach (var old in All().Skip(Keep)) Delete(old);
    }

    /// <summary>Windows' own reports of Simpl.exe stopping since <paramref name="since"/> (Application Error, .NET
    /// Runtime, Application Hang), newest first; none where the log cannot be read.</summary>
    private static List<(DateTime At, string Title, string Text)> WindowsReports(DateTime since)
    {
        var found = new List<(DateTime, string, string)>();
        try
        {
            var ms = (long)Math.Max(60_000, (DateTime.Now - since).TotalMilliseconds + 60_000);
            var query = new EventLogQuery("Application", PathType.LogName,
                $"*[System[(EventID=1000 or EventID=1002 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= {ms}]]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            var exe = System.IO.Path.GetFileName(Environment.ProcessPath ?? "Simpl.exe");
            for (var rec = reader.ReadEvent(); rec != null && found.Count < 4; rec = reader.ReadEvent())
            {
                using (rec)
                {
                    string message;
                    try { message = rec.FormatDescription() ?? ""; } catch { message = ""; }
                    if (!message.Contains(exe, StringComparison.OrdinalIgnoreCase)) continue;
                    var kind = rec.Id switch { 1002 => "Stopped responding", 1026 => ".NET Runtime error", _ => "Application Error" };
                    var detail = message.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Exception Info:", StringComparison.OrdinalIgnoreCase)
                        || l.StartsWith("Exception code:", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Faulting module name:", StringComparison.OrdinalIgnoreCase));
                    found.Add((rec.TimeCreated ?? DateTime.Now, detail is { Length: > 0 } ? $"{kind} — {detail}" : kind, message.Trim()));
                }
            }
        }
        catch (Exception e) { Log.Write("crash log: Windows' Application log could not be read: " + e.Message); }
        return found;
    }

    /// <summary>Every crash kept, newest first.</summary>
    public static List<Entry> All()
    {
        try
        {
            if (!Directory.Exists(Folder)) return new();
            return Directory.GetFiles(Folder, "*.txt")
                .Where(p => System.IO.Path.GetFileName(p) is var n && (n.StartsWith("crash-") || n.StartsWith("caught-")))
                .Select(p =>
                {
                    var head = File.ReadLines(p).Take(6).ToList();
                    string? Field(string name) => head.FirstOrDefault(l => l.StartsWith(name + ": "))?[(name.Length + 2)..];
                    return new Entry(p, File.GetLastWriteTime(p), Field("Title") ?? "Crash", Field("Version") ?? "?", Field("Kind") != "caught");
                })
                .OrderByDescending(e => e.At)
                .ToList();
        }
        catch { return new(); }
    }

    public static string Text(Entry e)
    {
        try { return File.ReadAllText(e.Path); } catch { return "This file could not be read."; }
    }

    public static void Delete(Entry e)
    {
        try { File.Delete(e.Path); } catch { }
    }

    public static void DeleteAll()
    {
        foreach (var e in All()) Delete(e);
    }

    /// <summary>The developer tools' test: Simpl stopped on purpose, as a crash would, so the next launch shows it.</summary>
    public static void CrashNow()
    {
        Log.Write("crash log: stopping on purpose (Developer ▸ Crashes ▸ Crash now)");
        new Thread(() => throw new InvalidOperationException("Crash now, from the developer tools")) { IsBackground = true }.Start();
    }

    // ---- pieces ----

    private static Dictionary<string, string> Read(string path)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    private static string FirstLine(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private static List<string> Tail(string path, int count)
    {
        try
        {
            if (!File.Exists(path)) return new();
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const long most = 1024 * 1024;
            var from = Math.Max(0, f.Length - most);
            f.Seek(from, SeekOrigin.Begin);
            using var r = new StreamReader(f, Encoding.UTF8);
            var lines = r.ReadToEnd().Split('\n').Skip(from > 0 ? 1 : 0).Where(l => l.Length > 0).ToList();
            return lines.Count > count ? lines.GetRange(lines.Count - count, count) : lines;
        }
        catch { return new(); }
    }
}
