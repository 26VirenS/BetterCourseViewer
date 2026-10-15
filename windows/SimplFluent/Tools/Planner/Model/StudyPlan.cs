using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Simpl.Tools.Planner;

// The Study Planner's sums (0.3.11), free of any window (no System.Windows, Simpl.Kit or Simpl.Support here, as the
// biology models are): the work due, weighted by its points and its kind, shared out as study blocks over the days
// before each is due — never past it — earliest due first, each day's hours respected, blocks already done kept where
// they were and taken off what is left. Tested in SimplFluent.Tests/PlannerTests.cs.

/// <summary>What a piece of work is to the planner: an exam weighs three times an assignment, a quiz one and a half, a discussion half.</summary>
internal enum PlanKind { Exam, Quiz, Assignment, Discussion }

/// <summary>When in the day the student would rather study: the first block starts at 8, 13 or 18.</summary>
internal enum PlanTime { Morning, Afternoon, Evening }

/// <summary>A piece of work due (as the engine's calendar has it): its id, title, course code, kind, points and due moment (local).</summary>
internal sealed record PlanItem(string Id, string Title, string Course, PlanKind Kind, double? Points, DateTime Due);

/// <summary>
/// How the student studies: hours each weekday (Monday first), the block length (25, 45, 60 or 90 minutes), when in
/// the day, how many days before the due date to be finished, and which courses are left out (by code).
/// </summary>
internal sealed record PlanSettings
{
    public double[] Hours { get; init; } = Defaults();
    public int BlockMinutes { get; init; } = 45;
    public PlanTime Time { get; init; } = PlanTime.Afternoon;
    public int FinishDaysBefore { get; init; } = 1;
    public IReadOnlySet<string> Excluded { get; init; } = new HashSet<string>();

    public static readonly int[] Blocks = { 25, 45, 60, 90 };

    /// <summary>Two hours on a weekday, three at the weekend.</summary>
    public static double[] Defaults() => new[] { 2.0, 2, 2, 2, 2, 3, 3 };

    /// <summary>The hours for a day: Monday is 0 (the array's order), as the settings' grid has it.</summary>
    public double HoursOn(DayOfWeek day) => Hours[((int)day + 6) % 7];

    public bool Includes(PlanItem it) => !Excluded.Contains(it.Course);
}

/// <summary>A block of study: which work, when it starts, how long, and whether it is done.</summary>
internal sealed record PlanBlock(string ItemId, DateTime Start, int Minutes, bool Done)
{
    public DateTime End => Start.AddMinutes(Minutes);

    /// <summary>What a done block is kept under: its work and its start.</summary>
    public string Key => $"{ItemId}|{Start.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}";

    public static PlanBlock? FromKey(string key, int minutes)
    {
        var at = key.LastIndexOf('|');
        if (at <= 0) return null;
        return DateTime.TryParseExact(key[(at + 1)..], "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            ? new PlanBlock(key[..at], start, minutes, true) : null;
    }
}

/// <summary>How a piece of work stands: the minutes it needs, done, planned, and short (no room before it is due).</summary>
internal sealed record PlanProgress(PlanItem Item, int NeedMinutes, int DoneMinutes, int PlannedMinutes)
{
    public int ShortMinutes => Math.Max(0, NeedMinutes - DoneMinutes - PlannedMinutes);
    public double Fraction => NeedMinutes <= 0 ? 1 : Math.Clamp((double)DoneMinutes / NeedMinutes, 0, 1);
}

/// <summary>A plan: its blocks in order (done ones among them), how each piece of work stands, and the day it was made for.</summary>
internal sealed record StudyPlanResult(IReadOnlyList<PlanBlock> Blocks, IReadOnlyList<PlanProgress> Progress, DateTime Today)
{
    public IEnumerable<PlanBlock> On(DateTime day) => Blocks.Where(b => b.Start.Date == day.Date);
}

/// <summary>The planner itself.</summary>
internal static class StudyPlan
{
    /// <summary>How far ahead the work is read (the engine's calendar call, as the Calendar reads it).</summary>
    public const int HorizonDays = 42;

    /// <summary>The rest between two blocks, minutes.</summary>
    public const int Break = 10;

    public static double KindFactor(PlanKind k) => k switch { PlanKind.Exam => 3, PlanKind.Quiz => 1.5, PlanKind.Discussion => 0.5, _ => 1 };

    public static string KindWord(PlanKind k) => k switch { PlanKind.Exam => "Exam", PlanKind.Quiz => "Quiz", PlanKind.Discussion => "Discussion", _ => "Assignment" };

    private static readonly Regex ExamWords = new(@"\b(exam|midterm|final|test)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PointsWords = new(@"(\d+(?:\.\d+)?)\s*pts?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A piece of work's kind from the engine's word ("Quiz", "Assignment", "Discussion") and its title: an
    /// exam, midterm, final or test by name is an exam whatever Canvas calls it.</summary>
    public static PlanKind KindOf(string? kind, string title)
    {
        if (ExamWords.IsMatch(title)) return PlanKind.Exam;
        return (kind ?? "").Trim().ToLowerInvariant() switch
        {
            "quiz" => PlanKind.Quiz,
            "discussion" => PlanKind.Discussion,
            _ => PlanKind.Assignment,
        };
    }

    /// <summary>The points in a row's line ("F26-MATH 021 20 · Quiz · 16 pts"), or none.</summary>
    public static double? PointsIn(string? line)
    {
        var m = line == null ? null : PointsWords.Match(line);
        return m is { Success: true } && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>The hour the first block of a day starts.</summary>
    public static int StartHour(PlanTime t) => t switch { PlanTime.Morning => 8, PlanTime.Evening => 18, _ => 13 };

    /// <summary>
    /// The plan: the work due after <paramref name="now"/> in the courses kept, each weighted by its points (equal when
    /// none has any; the average of the others' for one without) times its kind, given its share of the hours between now
    /// and the last deadline, in whole blocks — never fewer than one. Each day is filled earliest deadline first, then
    /// soonest due, then heaviest: first what keeps every piece on pace for its deadline (its blocks left over the days left), then whatever
    /// room is left, in the same order. A block never ends past its work's due moment, nor sits before now. Blocks done
    /// (<paramref name="done"/>) stay where they are and come off what their work needs. The same input gives the same plan.
    /// </summary>
    public static StudyPlanResult Make(IReadOnlyList<PlanItem> items, PlanSettings s, DateTime now, IReadOnlyList<PlanBlock>? done = null)
    {
        var today = now.Date;
        var block = Math.Max(5, s.BlockMinutes);
        var kept = done?.Where(b => b.Done).OrderBy(b => b.Start).ThenBy(b => b.ItemId, StringComparer.Ordinal).ToList() ?? new List<PlanBlock>();
        var active = items.Where(it => it.Due > now && s.Includes(it)).OrderBy(it => it.Due).ThenBy(it => it.Id, StringComparer.Ordinal).ToList();
        if (active.Count == 0) return new StudyPlanResult(kept, Array.Empty<PlanProgress>(), today);

        // each piece's deadline day: the due day less the days to finish early, never before today
        var deadline = active.ToDictionary(it => it.Id, it => Later(today, it.Due.Date.AddDays(-Math.Max(0, s.FinishDaysBefore))));
        var last = deadline.Values.Max();
        var days = (int)Math.Min(90, (last - today).TotalDays) + 1;

        // the hours there are, in blocks a day, less the blocks already done that day
        var capacity = new int[days];
        var total = 0;
        for (var d = 0; d < days; d++)
        {
            var day = today.AddDays(d);
            var minutes = Math.Max(0, s.HoursOn(day.DayOfWeek)) * 60;
            var doneToday = kept.Where(b => b.Start.Date == day).Sum(b => b.Minutes);
            capacity[d] = Math.Max(0, (int)Math.Floor((minutes - doneToday) / (double)block));
            total += capacity[d];
        }

        // each piece's weight, and its share of the whole in blocks
        var anyPoints = active.Any(it => it.Points is > 0);
        var average = anyPoints ? active.Where(it => it.Points is > 0).Average(it => it.Points!.Value) : 1;
        var weight = active.ToDictionary(it => it.Id, it => (anyPoints ? (it.Points is > 0 ? it.Points.Value : average) : 1) * KindFactor(it.Kind));
        var sum = weight.Values.Sum();
        var doneBlocks = kept.Sum(b => b.Minutes) / (double)block;
        var budget = total + doneBlocks;
        var need = active.ToDictionary(it => it.Id, it => Math.Max(1, (int)Math.Round(budget * weight[it.Id] / sum, MidpointRounding.AwayFromZero)));
        var doneMinutes = active.ToDictionary(it => it.Id, it => kept.Where(b => b.ItemId == it.Id).Sum(b => b.Minutes));
        var remaining = active.ToDictionary(it => it.Id, it => Math.Max(0, (int)Math.Ceiling((need[it.Id] * block - doneMinutes[it.Id]) / (double)block)));

        var placed = new List<PlanBlock>();
        var byId = active.ToDictionary(it => it.Id);
        for (var d = 0; d < days; d++)
        {
            var day = today.AddDays(d);
            var slots = capacity[d];
            if (slots == 0) continue;
            var cursor = day.AddHours(StartHour(s.Time));
            if (d == 0 && cursor < now) cursor = Quarter(now);
            // (the deadline is a day, so a piece due tonight and one due tomorrow night share it when finishing a day
            // early: the one due sooner goes first, or the other would take the whole day and leave it short)
            var candidates = active.Where(it => deadline[it.Id] >= day && remaining[it.Id] > 0)
                .OrderBy(it => deadline[it.Id]).ThenBy(it => it.Due).ThenByDescending(it => weight[it.Id]).ThenBy(it => it.Id, StringComparer.Ordinal).ToList();
            bool Place(PlanItem it)
            {
                var start = Free(cursor, block, kept);
                if (start.AddMinutes(block) > it.Due) return false;
                placed.Add(new PlanBlock(it.Id, start, block, false));
                cursor = start.AddMinutes(block + Break);
                slots--;
                remaining[it.Id]--;
                return true;
            }
            // what keeps each on pace: its blocks left over the days left to its deadline
            foreach (var it in candidates)
            {
                var daysLeft = Math.Max(1, (int)(deadline[it.Id] - day).TotalDays + 1);
                var pace = (int)Math.Ceiling(remaining[it.Id] / (double)daysLeft);
                for (var k = 0; k < pace && slots > 0 && remaining[it.Id] > 0; k++) if (!Place(it)) break;
            }
            // the room left, to the same order, a block each round
            var moved = true;
            while (slots > 0 && moved)
            {
                moved = false;
                foreach (var it in candidates)
                {
                    if (slots == 0) break;
                    if (remaining[it.Id] > 0 && Place(it)) moved = true;
                }
            }
        }

        var blocks = kept.Concat(placed).OrderBy(b => b.Start).ThenBy(b => b.ItemId, StringComparer.Ordinal).ToList();
        var progress = active.Select(it => new PlanProgress(it, need[it.Id] * block, doneMinutes[it.Id], placed.Where(b => b.ItemId == it.Id).Sum(b => b.Minutes))).ToList();
        return new StudyPlanResult(blocks, progress, today);
    }

    private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

    /// <summary>The next quarter hour on or after a moment.</summary>
    public static DateTime Quarter(DateTime t)
    {
        var m = (int)Math.Ceiling(t.TimeOfDay.TotalMinutes / 15.0) * 15;
        return t.Date.AddMinutes(m);
    }

    /// <summary>The first start on or after the cursor whose block lies over no block already done.</summary>
    private static DateTime Free(DateTime cursor, int block, List<PlanBlock> kept)
    {
        var start = cursor;
        for (var guard = 0; guard < 200; guard++)
        {
            var clash = kept.FirstOrDefault(b => b.Start < start.AddMinutes(block) && b.End > start);
            if (clash == null) return start;
            start = clash.End.AddMinutes(Break);
        }
        return start;
    }

    /// <summary>"45 min · PHYS 008 · Quiz 3": a block's words.</summary>
    public static string Words(PlanBlock b, PlanItem? it) => it == null ? $"{b.Minutes} min" : $"{b.Minutes} min · {it.Course} · {it.Title}";

    /// <summary>The plan as a calendar file (RFC 5545), a block an event, times as this PC's own: Add to Calendar.</summary>
    public static string Ics(IEnumerable<PlanBlock> blocks, IReadOnlyDictionary<string, PlanItem> items, DateTime now)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Simpl//Study Planner//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\n");
        var stamp = now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var n = 0;
        foreach (var b in blocks)
        {
            items.TryGetValue(b.ItemId, out var it);
            var title = it == null ? "Study" : $"Study: {it.Course} · {it.Title}";
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append("UID:simpl-plan-").Append(Escape(b.Key)).Append('-').Append(++n).Append("@simpl\r\n");
            sb.Append("DTSTAMP:").Append(stamp).Append("\r\n");
            sb.Append("DTSTART:").Append(b.Start.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("DTEND:").Append(b.End.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("SUMMARY:").Append(Escape(title)).Append("\r\n");
            if (it != null) sb.Append("DESCRIPTION:").Append(Escape($"{KindWord(it.Kind)} due {it.Due.ToString("ddd, MMM d 'at' h:mm tt", CultureInfo.InvariantCulture)}{(b.Done ? " · done" : "")}")).Append("\r\n");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>"2 h", "1 h 30 min", "45 min".</summary>
    public static string Duration(int minutes)
    {
        if (minutes <= 0) return "0 min";
        var h = minutes / 60;
        var m = minutes % 60;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>"2,2,2,2,2,3,3" as hours, Monday first; the defaults for anything else.</summary>
    public static double[] ParseHours(string? s)
    {
        var parts = (s ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 7) return PlanSettings.Defaults();
        var hours = new double[7];
        for (var i = 0; i < 7; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v)) return PlanSettings.Defaults();
            hours[i] = Math.Clamp(v, 0, 16);
        }
        return hours;
    }

    public static string HoursText(double[] hours) => string.Join(",", hours.Select(h => h.ToString("0.##", CultureInfo.InvariantCulture)));
}
