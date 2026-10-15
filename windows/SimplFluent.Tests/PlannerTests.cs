using Simpl.Tools.Planner;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Study Planner's sums (Tools/Planner/Model/StudyPlan.cs): a block never ends past its work's due moment; the
/// minutes a piece of work gets go with its weight (points times kind); a day's hours are respected; a block done stays
/// where it was and comes off what is left; the same input gives the same plan; the kinds and points read off the
/// engine's rows; and the calendar file.
/// </summary>
public class PlannerTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0); // (a Thursday)

    private static PlanItem Item(string id, string title, string course, PlanKind kind, double? points, DateTime due) => new(id, title, course, kind, points, due);

    private static PlanSettings Settings(double hours = 2, int block = 45, int finish = 1) =>
        new() { Hours = new[] { hours, hours, hours, hours, hours, hours, hours }, BlockMinutes = block, FinishDaysBefore = finish };

    [Fact]
    public void ABlockNeverEndsPastItsWorksDueMoment()
    {
        var items = new[]
        {
            Item("a", "Qz01", "MATH 021", PlanKind.Quiz, 10, Now.Date.AddHours(23).AddMinutes(59)),
            Item("b", "Lec06-PreQuiz", "MATH 021", PlanKind.Quiz, 17, Now.Date.AddDays(1).AddHours(10)),
            Item("c", "Midterm 1", "MATH 021", PlanKind.Exam, 100, Now.Date.AddDays(15).AddHours(10).AddMinutes(30)),
            Item("d", "Week 3 lab", "PHYS 008", PlanKind.Assignment, 20, Now.AddHours(1)),
        };
        var plan = StudyPlan.Make(items, Settings(), Now);
        Assert.NotEmpty(plan.Blocks);
        foreach (var b in plan.Blocks)
        {
            var it = items.Single(i => i.Id == b.ItemId);
            Assert.True(b.End <= it.Due, $"{it.Title}: a block ends {b.End} but it is due {it.Due}");
            Assert.True(b.Start >= Now, $"{it.Title}: a block starts {b.Start}, before now");
        }
        // (work due in an hour, at 10 am, in the afternoon's plan: no room for a block before it — said as short, never forced)
        Assert.Equal(0, plan.Progress.Single(p => p.Item.Id == "d").PlannedMinutes);
        Assert.True(plan.Progress.Single(p => p.Item.Id == "d").ShortMinutes > 0);
    }

    [Fact]
    public void TheMinutesAPieceGetsGoWithItsWeight()
    {
        // the same due date far off, plenty of hours: a 100-point exam (×3) against a 100-point assignment (×1) is 3 to 1
        var due = Now.Date.AddDays(20).AddHours(23);
        var items = new[] { Item("exam", "Midterm 1", "MATH 021", PlanKind.Exam, 100, due), Item("hw", "Problem set 4", "MATH 021", PlanKind.Assignment, 100, due) };
        var plan = StudyPlan.Make(items, Settings(hours: 3, block: 45), Now);
        var exam = plan.Progress.Single(p => p.Item.Id == "exam");
        var hw = plan.Progress.Single(p => p.Item.Id == "hw");
        Assert.True(Math.Abs(exam.NeedMinutes - 3.0 * hw.NeedMinutes) <= 45, $"exam {exam.NeedMinutes} against assignment {hw.NeedMinutes}");
        Assert.Equal(exam.NeedMinutes, exam.PlannedMinutes);
        Assert.Equal(hw.NeedMinutes, hw.PlannedMinutes);
        // points count too: 50 against 100 of the same kind is 1 to 2
        var byPoints = StudyPlan.Make(new[] { Item("x", "Essay 1", "WRIT 001", PlanKind.Assignment, 50, due), Item("y", "Essay 2", "WRIT 001", PlanKind.Assignment, 100, due) }, Settings(hours: 3), Now);
        Assert.True(Math.Abs(2.0 * byPoints.Progress[0].NeedMinutes - byPoints.Progress[1].NeedMinutes) <= 45);
        // no points anywhere: equal shares
        var equal = StudyPlan.Make(new[] { Item("x", "Reading 1", "HIST 010", PlanKind.Assignment, null, due), Item("y", "Reading 2", "HIST 010", PlanKind.Assignment, null, due) }, Settings(hours: 3), Now);
        Assert.Equal(equal.Progress[0].NeedMinutes, equal.Progress[1].NeedMinutes);
    }

    [Fact]
    public void ADaysHoursAreRespected()
    {
        var items = Enumerable.Range(0, 6).Select(i => Item($"i{i}", $"Problem set {i}", "MATH 021", PlanKind.Assignment, 20, Now.Date.AddDays(3 + i).AddHours(23))).ToList();
        var s = new PlanSettings { Hours = new[] { 1.0, 2, 0.5, 2, 1.5, 3, 3 }, BlockMinutes = 30, FinishDaysBefore = 1 };
        var plan = StudyPlan.Make(items, s, Now);
        foreach (var day in plan.Blocks.GroupBy(b => b.Start.Date))
        {
            var minutes = day.Sum(b => b.Minutes);
            Assert.True(minutes <= s.HoursOn(day.Key.DayOfWeek) * 60 + 1e-9, $"{day.Key:ddd}: {minutes} min planned, {s.HoursOn(day.Key.DayOfWeek)} h allowed");
        }
        // (the blocks of a day never lie over one another)
        foreach (var day in plan.Blocks.GroupBy(b => b.Start.Date))
        {
            var ordered = day.OrderBy(b => b.Start).ToList();
            for (var i = 1; i < ordered.Count; i++) Assert.True(ordered[i].Start >= ordered[i - 1].End);
        }
    }

    [Fact]
    public void ABlockDoneStaysWhereItWasAndComesOffWhatIsLeft()
    {
        var due = Now.Date.AddDays(10).AddHours(23);
        var items = new[] { Item("exam", "Midterm 1", "MATH 021", PlanKind.Exam, 100, due), Item("hw", "Problem set 4", "PHYS 008", PlanKind.Assignment, 30, due) };
        var first = StudyPlan.Make(items, Settings(), Now);
        var examNeed = first.Progress.Single(p => p.Item.Id == "exam").NeedMinutes;
        var done = first.Blocks.Where(b => b.ItemId == "exam").Take(2).Select(b => b with { Done = true }).ToList();
        var again = StudyPlan.Make(items, Settings(), Now, done);
        var exam = again.Progress.Single(p => p.Item.Id == "exam");
        Assert.Equal(90, exam.DoneMinutes);
        Assert.Equal(examNeed, exam.NeedMinutes);
        Assert.Equal(examNeed - 90, exam.PlannedMinutes);
        // (the done blocks are in the plan as they were, ticked; nothing new lies over them)
        foreach (var d in done) Assert.Contains(again.Blocks, b => b.Done && b.ItemId == d.ItemId && b.Start == d.Start);
        foreach (var b in again.Blocks.Where(b => !b.Done)) foreach (var d in done) Assert.True(b.Start >= d.End || b.End <= d.Start);
        // (a done block is kept by its key, and read back from it)
        var key = done[0].Key;
        var back = PlanBlock.FromKey(key, 45);
        Assert.NotNull(back);
        Assert.Equal(done[0].Start, back!.Start);
        Assert.Equal("exam", back.ItemId);
    }

    [Fact]
    public void TheSameInputGivesTheSamePlan()
    {
        var items = new[]
        {
            Item("a", "Qz01", "MATH 021", PlanKind.Quiz, 10, Now.Date.AddDays(2).AddHours(23)),
            Item("b", "Lec07-PreQuiz", "MATH 021", PlanKind.Quiz, 20, Now.Date.AddDays(4).AddHours(23)),
            Item("c", "Midterm 1", "MATH 021", PlanKind.Exam, 100, Now.Date.AddDays(15).AddHours(10)),
            Item("d", "Discussion Quiz for this week", "MATH 021", PlanKind.Discussion, 5, Now.Date.AddDays(5).AddHours(23)),
            Item("e", "Lec08-PreQuiz", "PHYS 008", PlanKind.Quiz, 20, Now.Date.AddDays(8).AddHours(23)),
        };
        var one = StudyPlan.Make(items, Settings(), Now);
        var two = StudyPlan.Make(items.Reverse().ToArray(), Settings(), Now);
        Assert.Equal(one.Blocks.Select(b => b.Key), two.Blocks.Select(b => b.Key));
        Assert.Equal(one.Progress.Select(p => (p.Item.Id, p.NeedMinutes, p.PlannedMinutes)), two.Progress.Select(p => (p.Item.Id, p.NeedMinutes, p.PlannedMinutes)));
        // (the earliest due comes first in today's plan)
        Assert.Equal("a", one.On(Now.Date).First().ItemId);
    }

    [Fact]
    public void APieceDueSoonerGoesFirstOnASharedDeadlineDay()
    {
        // finishing a day early, a discussion due tonight and an assignment due tomorrow night (10 points each) both
        // have today as their deadline, and today holds two blocks; a piece due later makes the assignment's need more
        // than a block (its weight sorted it first, and it took both of today's blocks: the discussion, which had room,
        // was said to be short). The one due tonight gets today's first block, and is not short.
        var items = new[]
        {
            Item("fri", "Composition of Functions", "MATH 021", PlanKind.Assignment, 10, Now.Date.AddDays(1).AddHours(23).AddMinutes(59)),
            Item("thu", "Dis01", "MATH 021", PlanKind.Discussion, 10, Now.Date.AddHours(23).AddMinutes(59)),
            Item("later", "Problem set 4", "MATH 021", PlanKind.Assignment, 25, Now.Date.AddDays(5).AddHours(23).AddMinutes(59)),
        };
        var plan = StudyPlan.Make(items, Settings(hours: 1.5, finish: 1), Now);
        var today = plan.On(Now.Date).ToList();
        Assert.Equal(2, today.Count);
        Assert.Equal("thu", today[0].ItemId);
        Assert.Equal("fri", today[1].ItemId);
        var thu = plan.Progress.Single(p => p.Item.Id == "thu");
        Assert.Equal(45, thu.PlannedMinutes);
        Assert.Equal(0, thu.ShortMinutes);
    }

    [Fact]
    public void WorkDoneOrPastOrInACourseLeftOutIsNotPlanned()
    {
        var items = new[]
        {
            Item("past", "Lec05-PreQuiz", "MATH 021", PlanKind.Quiz, 19, Now.AddDays(-1)),
            Item("out", "Week 3 lab", "PHYS 008", PlanKind.Assignment, 20, Now.AddDays(3)),
            Item("in", "Problem set 4", "MATH 021", PlanKind.Assignment, 20, Now.AddDays(3)),
        };
        var s = new PlanSettings { Excluded = new HashSet<string> { "PHYS 008" } };
        var plan = StudyPlan.Make(items, s, Now);
        Assert.Equal(new[] { "in" }, plan.Progress.Select(p => p.Item.Id));
        Assert.All(plan.Blocks, b => Assert.Equal("in", b.ItemId));
    }

    [Theory]
    [InlineData("Assignment", "Midterm 1", "Exam")]
    [InlineData("Assignment", "Final Exam", "Exam")]
    [InlineData("Quiz", "Qz01", "Quiz")]
    [InlineData("Quiz", "Unit test 2", "Exam")]
    [InlineData("Discussion", "Discussion Quiz for this week", "Discussion")]
    [InlineData("Assignment", "Problem set 4", "Assignment")]
    [InlineData(null, "Lab report", "Assignment")]
    public void AKindIsReadOffTheWordAndTheTitle(string? kind, string title, string expected) => Assert.Equal(expected, StudyPlan.KindWord(StudyPlan.KindOf(kind, title)));

    [Theory]
    [InlineData("F26-MATH 021 20 · Quiz · 16 pts", 16.0)]
    [InlineData("F26-PHYS 008 01 · Assignment · 1 pt", 1.0)]
    [InlineData("F26-MATH 021 20 · Assignment · 2.5 pts · COB2 140", 2.5)]
    [InlineData("F26-MATH 021 20 · Event", null)]
    public void ThePointsAreReadOffARowsLine(string line, double? points) => Assert.Equal(points, StudyPlan.PointsIn(line));

    [Fact]
    public void TheCalendarFileHasAnEventABlock()
    {
        var due = Now.Date.AddDays(5).AddHours(23);
        var items = new[] { Item("exam", "Midterm 1", "MATH 021", PlanKind.Exam, 100, due) };
        var plan = StudyPlan.Make(items, Settings(), Now);
        var ics = StudyPlan.Ics(plan.Blocks, items.ToDictionary(i => i.Id), Now);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Equal(plan.Blocks.Count, ics.Split("BEGIN:VEVENT").Length - 1);
        Assert.Contains("SUMMARY:Study: MATH 021 · Midterm 1", ics);
        Assert.Contains($"DTSTART:{plan.Blocks[0].Start:yyyyMMdd'T'HHmmss}", ics);
    }

    [Fact]
    public void TheHoursAndWordsReadAsTheyShould()
    {
        Assert.Equal(new[] { 2.0, 2, 2, 2, 2, 3, 3 }, StudyPlan.ParseHours("2,2,2,2,2,3,3"));
        Assert.Equal(PlanSettings.Defaults(), StudyPlan.ParseHours("2,2"));
        Assert.Equal("1.5,2,2,2,2,3,3", StudyPlan.HoursText(StudyPlan.ParseHours("1.5,2,2,2,2,3,3")));
        Assert.Equal(3, new PlanSettings().HoursOn(DayOfWeek.Sunday));
        Assert.Equal(2, new PlanSettings().HoursOn(DayOfWeek.Monday));
        Assert.Equal("1 h 30 min", StudyPlan.Duration(90));
        Assert.Equal("45 min", StudyPlan.Duration(45));
        Assert.Equal("2 h", StudyPlan.Duration(120));
        Assert.Equal("45 min · PHYS 008 · Quiz 3", StudyPlan.Words(new PlanBlock("q", Now, 45, false), Item("q", "Quiz 3", "PHYS 008", PlanKind.Quiz, 10, Now.AddDays(1))));
        Assert.Equal(new DateTime(2026, 10, 15, 9, 15, 0), StudyPlan.Quarter(new DateTime(2026, 10, 15, 9, 1, 0)));
    }
}
