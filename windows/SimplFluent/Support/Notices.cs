using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;
using Simpl.Models;

namespace Simpl.Support;

/// <summary>
/// Simpl's own notifications, as Windows shows them (the Mac's Reminders and Activity): a reminder before work still to
/// hand in is due — the day before and the hour before, or as set in Settings ▸ Notifications — and an alert when
/// something new is posted in a course (an announcement, a grade, a comment, a message, a discussion, as chosen),
/// looked for about every three minutes. Both run while Simpl runs, which it does with its window closed (the
/// notification area); nothing is sent anywhere. A click on one opens what it is about.
/// </summary>
public static class Notices
{
    private sealed class ReminderItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Course { get; set; }
        public string Kind { get; set; } = "";
        public string Due { get; set; } = "";
        public string? Url { get; set; }
    }

    private sealed class RemindersData
    {
        public List<ReminderItem> Items { get; set; } = new();
    }

    private static Engine.Engine? engine;
    private static Shell.MainWindow? window;
    private static readonly List<DispatcherTimer> due = new();
    private static DispatcherTimer? watch, settle;
    private static int lastData = -1;

    /// <summary>The app's screens up: reminders set from what is due now, and the look for new activity begun.</summary>
    public static void Start(Engine.Engine e, Shell.MainWindow w)
    {
        if (Launch.Get("shot") != null || Launch.Flag("demo")) return;
        if (engine == e) return;
        engine = e;
        window = w;
        e.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName != nameof(Engine.Engine.DataVersion) || e.DataVersion == lastData) return;
            lastData = e.DataVersion;
            // (a tick, a hand-in: set again once things are still)
            settle?.Stop();
            settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            settle.Tick += (_, _) => { settle?.Stop(); _ = Reschedule(); };
            settle.Start();
        };
        _ = Reschedule();
        watch = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        watch.Tick += (_, _) => _ = Look(notify: true);
        watch.Start();
        _ = Look(notify: false); // (what the student sees now is not news)
    }

    /// <summary>Signed out, another school: no reminder nor alert of that account's left.</summary>
    public static void Stop()
    {
        foreach (var t in due) t.Stop();
        due.Clear();
        watch?.Stop();
        watch = null;
        engine = null;
        Prefs.Remove("activity:seen");
    }

    /// <summary>Settings ▸ Notifications changed: the reminders set again, and what is there now noted as seen.</summary>
    public static void SettingsChanged()
    {
        _ = Reschedule();
        _ = Look(notify: false);
    }

    // ---- reminders ----

    /// <summary>How long before a due time, as Settings ▸ Notifications says it: the minutes before for each alert.</summary>
    private static int[] Lead => (Prefs.String("reminders:lead") ?? "1 day and 1 hour before") switch
    {
        "1 hour before" => new[] { 60 },
        "3 hours before" => new[] { 180 },
        "1 day before" => new[] { 1440 },
        "2 days before" => new[] { 2880 },
        _ => new[] { 1440, 60 },
    };

    /// <summary>The reminders set again from what the school says now: the old ones taken away, the new ones set.</summary>
    public static async Task Reschedule()
    {
        foreach (var t in due) t.Stop();
        due.Clear();
        if (engine == null || !Prefs.Bool("reminders:on")) return;
        RemindersData data;
        try { data = await engine.Call<RemindersData>("reminders"); }
        catch (Exception e) { Log.Write("reminders: " + e.Message); return; }
        var now = DateTimeOffset.Now;
        foreach (var item in data.Items)
        {
            if (!DateTimeOffset.TryParse(item.Due, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) continue;
            foreach (var m in Lead)
            {
                var fire = at.AddMinutes(-m);
                var wait = fire - now;
                if (wait < TimeSpan.FromMinutes(1) || wait > TimeSpan.FromDays(22)) continue;
                var t = new DispatcherTimer { Interval = wait };
                var it = item;
                var minutes = m;
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    due.Remove(t);
                    Remind(it, at, minutes);
                };
                t.Start();
                due.Add(t);
            }
        }
        Log.Write($"reminders: {due.Count} set");
    }

    private static void Remind(ReminderItem item, DateTimeOffset at, int minutes)
    {
        var time = at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
        var when = minutes switch
        {
            1440 => $"due tomorrow at {time}",
            2880 => $"due in 2 days, at {time}",
            60 => $"due in 1 hour, at {time}",
            _ => $"due in {minutes / 60} hours, at {time}",
        };
        var text = item.Kind == "My task" ? char.ToUpperInvariant(when[0]) + when[1..] : $"{item.Kind} {when}";
        if (!string.IsNullOrEmpty(item.Course)) text = $"{item.Course} · {text}";
        var e = engine;
        window?.Notify(item.Title, text, item.Url is { Length: > 0 } u && e != null ? () => e.OpenWeb(u, item.Title) : null);
    }

    // ---- new activity ----

    private static readonly string[] Standard = { "announcements", "grades", "comments", "inbox" };

    private static string? KindOf(DashActivityRow r) => r.Type switch
    {
        "Announcement" => "announcements",
        "Submission" => (r.Kind ?? "").Contains("omment", StringComparison.Ordinal) ? "comments" : "grades",
        "Conversation" or "Message" => "inbox",
        "DiscussionTopic" => "discussions",
        _ => null,
    };

    /// <summary>The school's recent activity read: anything new since the last look, of a kind chosen, said (four at
    /// most, then how many more); everything read noted as seen.</summary>
    private static async Task Look(bool notify)
    {
        if (engine == null || !Prefs.Bool("activity:on") || engine.OnBrightspace) return;
        DashActivityData data;
        try { data = await engine.Call<DashActivityData>("dashActivity"); }
        catch (Exception e) { Log.Write("activity: " + e.Message); return; }
        var seen = Seen();
        var kinds = (Prefs.String("activity:kinds") ?? string.Join(",", Standard)).Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var fresh = data.Rows.Where(r => r.Unread == true && !seen.Contains(r.Id) && KindOf(r) is { } k && kinds.Contains(k)).ToList();
        foreach (var r in data.Rows) seen.Add(r.Id);
        Prefs.Set("activity:seen", JsonSerializer.Serialize(seen.TakeLast(500).ToList()));
        Prefs.Set("activity:last", DateTime.UtcNow.ToString("o"));
        if (!notify || fresh.Count == 0) return;
        var e0 = engine;
        foreach (var r in fresh.Take(4))
        {
            var sub = string.Join(" · ", new[] { r.Kind, r.Course }.Where(s => !string.IsNullOrEmpty(s)));
            var row = r;
            window?.Notify(r.Title, string.IsNullOrEmpty(r.Preview) ? sub : $"{sub}\n{r.Preview}", row.Url is { Length: > 0 } u ? () => e0.OpenWeb(u, row.Title) : null);
            await Task.Delay(1500); // (one after another, each read)
        }
        if (fresh.Count > 4) window?.Notify($"{fresh.Count - 4} more new in your courses", "Open Simpl to see them.", () => e0.Go(Engine.Place.Dashboard));
    }

    private static HashSet<string> Seen()
    {
        try { return JsonSerializer.Deserialize<List<string>>(Prefs.String("activity:seen") ?? "[]")?.ToHashSet() ?? new(); }
        catch { return new(); }
    }
}
