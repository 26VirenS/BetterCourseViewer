using System.Text.Json;
using System.Text.Json.Nodes;

namespace Simpl.Support;

/// <summary>
/// The rules of a live check, as both apps keep them (README, Live checks): the screen on show reads
/// its calls again every minute — a course's pages every ten — with the engine's maxAge, compares each answer with the
/// one it shows, and changes nothing when they are the same. Kept apart from WPF so the tests can hold the app to them:
/// how often a screen checks, what it asks the engine, how it reads the answer, how long it waits after a failure, and
/// which rows of a list changed. <see cref="LiveClock"/> says when a check runs.
/// </summary>
public static class LiveRules
{
    /// <summary>How often the screen on show checks, in seconds, and the maxAge it asks with (the screenshot suite may
    /// ask for less: --live-every).</summary>
    public const double Every = 60;

    /// <summary>A course's pages (its home, modules, pages, files, assignments, discussions, announcements, people, its
    /// grades) check this many times less often — every ten minutes: the engine keeps their data ten minutes at most.</summary>
    public const int CourseTimes = 10;

    /// <summary>A failed check is tried again after one interval, then two, four, and five at most (60 s → 2 → 4 → 5 min).</summary>
    public const double MostBackoff = 5;

    /// <summary>A key pressed this lately (ms) is the student typing: no check meanwhile.</summary>
    public const long TypingMs = 5000;

    /// <summary>The arguments of a call that write a preference rather than read (the engine contract, §3): a check
    /// never passes them — dashList's hideDone, todo's grouping and its done rows. What they chose is kept by the engine,
    /// which answers the check as it answered the screen.</summary>
    private static readonly Dictionary<string, string[]> Writes = new(StringComparer.Ordinal)
    {
        ["dashList"] = new[] { "hideDone" },
        ["todo"] = new[] { "group", "showDone" },
    };

    /// <summary>A call whose answer is made of each course's own work (its handed-in counts): checked at a course page's
    /// age wherever it shows, so the Dashboard's cards and the Courses page do not read every course's work each minute.</summary>
    private static readonly HashSet<string> CourseMade = new(StringComparer.Ordinal) { "coursesProgress" };

    /// <summary>How often a screen checks, in seconds: <paramref name="every"/>, or ten times that for a course's page.</summary>
    public static double Interval(bool coursePage, double every = Every) => coursePage ? every * CourseTimes : every;

    /// <summary>The maxAge a call is checked with on a screen that checks every <paramref name="interval"/> seconds: the
    /// interval itself (so a change made since the last check is never hidden by the engine's memo), a course page's for
    /// what is made of each course's work.</summary>
    public static double AgeFor(string call, double interval, double every = Every) =>
        CourseMade.Contains(call) ? Math.Max(interval, every * CourseTimes) : interval;

    /// <summary>How long to wait after the <paramref name="failures"/>th failure in a row, in seconds: one base interval,
    /// then two, four, five at most — never sooner than the screen's own interval.</summary>
    public static double Backoff(int failures, double interval, double every = Every)
    {
        if (failures <= 0) return interval;
        var times = Math.Min(Math.Pow(2, Math.Min(failures, 8) - 1), MostBackoff);
        return Math.Max(interval, every * times);
    }

    /// <summary>The arguments a check asks with: the screen's own (a copy), less any that write a preference, with the
    /// maxAge (seconds) and the hash of the answer on screen ('' when it is not known yet).</summary>
    public static JsonObject CheckArgs(string call, JsonObject screen, double maxAge, string? known)
    {
        var a = (JsonObject)screen.DeepClone();
        if (Writes.TryGetValue(call, out var drop)) foreach (var k in drop) a.Remove(k);
        a.Remove("maxAge");
        a.Remove("known");
        a["maxAge"] = maxAge;
        a["known"] = known ?? "";
        return a;
    }

    /// <summary>The key pressed lately (ms, Environment.TickCount64): typing.</summary>
    public static bool Typing(long now, long lastKey) => lastKey > 0 && now - lastKey < TypingMs;

    /// <summary>
    /// A list's rows compared by their ids (stable identities: a planner item's, a course's, an activity's): for each row
    /// of the new list, whether it stays as it is (<see cref="RowChange.Kept"/>: its id and its look the same — its
    /// element untouched), is drawn again in its place (<see cref="RowChange.Changed"/>) or is new; and the ids that are
    /// gone. A look is anything that says how a row draws (its answer's JSON). A second row with an id already seen is
    /// told apart by its place among those of that id.
    /// </summary>
    public static RowDiff Rows(IReadOnlyList<(string Id, string Look)> before, IReadOnlyList<(string Id, string Look)> after)
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, look) in Unique(before)) old[id] = look;
        var rows = new List<(string Id, RowChange Change)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, look) in Unique(after))
        {
            seen.Add(id);
            rows.Add((id, !old.TryGetValue(id, out var was) ? RowChange.Added : was == look ? RowChange.Kept : RowChange.Changed));
        }
        var gone = old.Keys.Where(k => !seen.Contains(k)).ToList();
        // (gone in the order they were in)
        gone.Sort((x, y) => IndexOf(before, x).CompareTo(IndexOf(before, y)));
        return new RowDiff(rows, gone);
    }

    /// <summary>A list's ids made unique: the second "a" is "a#2".</summary>
    public static IEnumerable<(string Id, string Look)> Unique(IReadOnlyList<(string Id, string Look)> rows)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, look) in rows)
        {
            var n = count.TryGetValue(id, out var c) ? c + 1 : 1;
            count[id] = n;
            yield return (n == 1 ? id : $"{id}#{n}", look);
        }
    }

    private static int IndexOf(IReadOnlyList<(string Id, string Look)> rows, string id)
    {
        var i = 0;
        foreach (var (u, _) in Unique(rows)) { if (u == id) return i; i++; }
        return -1;
    }
}

/// <summary>How a row of a list fares in a check: as it was, drawn again in its place, or new.</summary>
public enum RowChange { Kept, Changed, Added }

/// <summary>A list compared (<see cref="LiveRules.Rows"/>): the new list's rows in order, each with how it fares, and the
/// ids that are gone.</summary>
public sealed record RowDiff(List<(string Id, RowChange Change)> Rows, List<string> Gone)
{
    public int Added => Rows.Count(r => r.Change == RowChange.Added);
    public int Changed => Rows.Count(r => r.Change == RowChange.Changed);
    public int Removed => Gone.Count;
    /// <summary>Nothing to do: every row kept, in the same order.</summary>
    public bool Same(IReadOnlyList<(string Id, string Look)> before) =>
        Removed == 0 && Rows.All(r => r.Change == RowChange.Kept) && LiveRules.Unique(before).Select(b => b.Id).SequenceEqual(Rows.Select(r => r.Id));
    /// <summary>What it did, for the log: "+1", "-1 ~2", "moved".</summary>
    public string Said
    {
        get
        {
            var parts = new List<string>();
            if (Added > 0) parts.Add($"+{Added}");
            if (Removed > 0) parts.Add($"-{Removed}");
            if (Changed > 0) parts.Add($"~{Changed}");
            return parts.Count == 0 ? "moved" : string.Join(" ", parts);
        }
    }
}

/// <summary>What the engine answered a check (the engine contract, §1): the same as on screen, something new — its JSON
/// as the call gives it without `known` — or a failure (the screen stays as it is).</summary>
public enum LiveAnswerKind { Same, New, Failed }

public readonly record struct LiveAnswer(LiveAnswerKind Kind, string? Hash, string? Value, string? Error)
{
    public static LiveAnswer Failed(string why) => new(LiveAnswerKind.Failed, null, null, why);

    /// <summary>The envelope read: { unchanged: true, hash }, { unchanged: false, hash, value }, or { error }.</summary>
    public static LiveAnswer Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return Failed("The answer could not be read.");
            if (r.TryGetProperty("error", out var e) && e.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
                return Failed(e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
            var hash = r.TryGetProperty("hash", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            if (string.IsNullOrEmpty(hash)) return Failed("The answer had no hash.");
            if (r.TryGetProperty("unchanged", out var u) && u.ValueKind == JsonValueKind.True) return new(LiveAnswerKind.Same, hash, null, null);
            if (r.TryGetProperty("value", out var v)) return new(LiveAnswerKind.New, hash, v.GetRawText(), null);
            return Failed("The answer could not be read.");
        }
        catch (JsonException) { return Failed("The answer could not be read."); }
    }
}

/// <summary>What the live check should do now (<see cref="LiveClock.Step"/>).</summary>
public enum LiveStep
{
    /// <summary>Nothing: the window is out of sight, Simpl is offline, or the screen has nothing to check.</summary>
    Paused,
    /// <summary>Not due yet (or waiting out a failure).</summary>
    Wait,
    /// <summary>Due, but the last check is still running: this one is skipped.</summary>
    Busy,
    /// <summary>Due, but something holds it (a quiz, typing, What-If, a sheet, a menu, a drag): it runs once that ends.</summary>
    Held,
    /// <summary>Check now.</summary>
    Check,
}

/// <summary>
/// When the live check runs (the rules both apps keep): while the window shows, the screen on show is checked once what
/// it shows is older than its interval — every minute, a course's page every ten. Nothing while the window is minimized,
/// hidden in the notification area or offline; back after more than a minute away, at once. One check at a time: one
/// due while the last still runs is skipped. A failure waits a minute, then two, four, five at most; a success starts
/// over. Times are milliseconds (Environment.TickCount64).
/// </summary>
public sealed class LiveClock
{
    private long awaySince = -1, retryAt;
    private bool back;

    /// <summary>The base interval, in seconds (a minute; less for the screenshot suite).</summary>
    public double Every { get; }
    public int Failures { get; private set; }
    public bool Running { get; private set; }
    /// <summary>When a failure lets the next check run (0: no wait).</summary>
    public long RetryAt => retryAt;

    public LiveClock(double every = LiveRules.Every) => Every = every > 0 ? every : LiveRules.Every;

    /// <summary>
    /// What to do at <paramref name="now"/> for the screen on show: it checks every <paramref name="interval"/> seconds
    /// (0: nothing to check), and the oldest of what it shows was the school's answer at <paramref name="oldest"/> (0:
    /// never). <paramref name="held"/> is asked only once a check is due.
    /// </summary>
    public LiveStep Step(long now, double interval, long oldest, bool away, bool offline, Func<bool> held)
    {
        if (away)
        {
            if (awaySince < 0) awaySince = now;
            return LiveStep.Paused;
        }
        if (awaySince >= 0)
        {
            // (back after more than a minute away: at once, whatever the screen's own interval)
            if (now - awaySince > Every * 1000) back = true;
            awaySince = -1;
        }
        if (offline || interval <= 0) return LiveStep.Paused;
        if (now < retryAt) return LiveStep.Wait;
        var due = back || oldest <= 0 || now - oldest >= interval * 1000;
        if (!due) return LiveStep.Wait;
        if (Running) return LiveStep.Busy;
        return held() ? LiveStep.Held : LiveStep.Check;
    }

    /// <summary>A check began.</summary>
    public void Began()
    {
        Running = true;
        back = false;
    }

    /// <summary>A check ended at <paramref name="now"/> — every call answered (<paramref name="ok"/>), or not: then the
    /// next waits out the back-off for a screen that checks every <paramref name="interval"/> seconds.</summary>
    public void Ended(long now, bool ok, double interval)
    {
        Running = false;
        if (ok)
        {
            Failures = 0;
            retryAt = 0;
            return;
        }
        Failures++;
        retryAt = now + (long)(LiveRules.Backoff(Failures, interval, Every) * 1000);
    }
}
