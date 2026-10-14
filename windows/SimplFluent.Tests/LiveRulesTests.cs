using System.Text.Json.Nodes;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>The live check's rules (Support/LiveRules.cs): when the screen on show is checked, what it asks the engine,
/// how the answer is read, how long a failure waits, and which rows of a list changed.</summary>
public class LiveRulesTests
{
    private const long S = 1000;
    private static readonly Func<bool> Free = () => false;

    // ---- when ----

    [Fact]
    public void AScreenIsCheckedOnceWhatItShowsIsAMinuteOld()
    {
        var c = new LiveClock();
        const long at = 1_000_000;
        Assert.Equal(LiveStep.Wait, c.Step(at + 59 * S, 60, at, away: false, offline: false, Free));
        Assert.Equal(LiveStep.Check, c.Step(at + 60 * S, 60, at, away: false, offline: false, Free));
        // (what was never the school's answer — only kept from last time — at once)
        Assert.Equal(LiveStep.Check, c.Step(at, 60, 0, away: false, offline: false, Free));
    }

    [Fact]
    public void ACoursePageEveryTenMinutes()
    {
        Assert.Equal(60, LiveRules.Interval(coursePage: false));
        Assert.Equal(600, LiveRules.Interval(coursePage: true));
        // (the screenshot suite's --live-every 5: 5 s, a course page 50 s)
        Assert.Equal(50, LiveRules.Interval(coursePage: true, every: 5));
        var c = new LiveClock();
        const long at = 1_000_000;
        Assert.Equal(LiveStep.Wait, c.Step(at + 300 * S, 600, at, false, false, Free));
        Assert.Equal(LiveStep.Check, c.Step(at + 600 * S, 600, at, false, false, Free));
    }

    [Fact]
    public void ACallIsCheckedWithItsScreensAgeAndHandedInCountsWithACoursePagesAge()
    {
        Assert.Equal(60, LiveRules.AgeFor("today", 60));
        Assert.Equal(600, LiveRules.AgeFor("modules", 600));
        Assert.Equal(600, LiveRules.AgeFor("coursesProgress", 60));
        Assert.Equal(600, LiveRules.AgeFor("externalTools", 60));
        Assert.Equal(50, LiveRules.AgeFor("coursesProgress", 5, every: 5));
        Assert.Equal(5, LiveRules.AgeFor("dashList", 5, every: 5));
    }

    [Fact]
    public void NothingWhileTheWindowIsAwayOrOffline()
    {
        var c = new LiveClock();
        const long at = 1_000_000;
        var asked = false;
        bool Held() { asked = true; return false; }
        Assert.Equal(LiveStep.Paused, c.Step(at + 120 * S, 60, at, away: true, offline: false, Held));
        Assert.Equal(LiveStep.Paused, c.Step(at + 120 * S, 60, at, away: false, offline: true, Held));
        // (a screen with nothing to check)
        Assert.Equal(LiveStep.Paused, c.Step(at + 120 * S, 0, at, away: false, offline: false, Held));
        Assert.False(asked);
    }

    [Fact]
    public void BackAfterMoreThanAMinuteAwayAtOnceWhateverTheInterval()
    {
        var c = new LiveClock();
        const long at = 1_000_000;
        // a course page (ten minutes) checked a moment ago; minimized for 90 s
        Assert.Equal(LiveStep.Paused, c.Step(at + 10 * S, 600, at, away: true, offline: false, Free));
        Assert.Equal(LiveStep.Check, c.Step(at + 100 * S, 600, at + 5 * S, away: false, offline: false, Free));
        c.Began();
        c.Ended(at + 101 * S, ok: true, 600);
        // (once: the next waits for its interval)
        Assert.Equal(LiveStep.Wait, c.Step(at + 102 * S, 600, at + 101 * S, false, false, Free));
    }

    [Fact]
    public void BackAfterLessThanAMinuteTheScheduleGoesOn()
    {
        var c = new LiveClock();
        const long at = 1_000_000;
        Assert.Equal(LiveStep.Paused, c.Step(at + 10 * S, 60, at, away: true, offline: false, Free));
        Assert.Equal(LiveStep.Wait, c.Step(at + 40 * S, 60, at, away: false, offline: false, Free));
        Assert.Equal(LiveStep.Check, c.Step(at + 60 * S, 60, at, away: false, offline: false, Free));
    }

    [Fact]
    public void OneCheckAtATimeAndAHeldOneRunsWhenTheHoldEnds()
    {
        var c = new LiveClock();
        const long at = 1_000_000;
        Assert.Equal(LiveStep.Check, c.Step(at + 60 * S, 60, at, false, false, Free));
        c.Began();
        Assert.Equal(LiveStep.Busy, c.Step(at + 120 * S, 60, at, false, false, Free));
        c.Ended(at + 121 * S, ok: true, 60);
        var typing = true;
        Assert.Equal(LiveStep.Held, c.Step(at + 200 * S, 60, at + 121 * S, false, false, () => typing));
        typing = false;
        Assert.Equal(LiveStep.Check, c.Step(at + 201 * S, 60, at + 121 * S, false, false, () => typing));
    }

    [Fact]
    public void AFailureBacksOffOneTwoFourFiveMinutesAndASuccessStartsOver()
    {
        Assert.Equal(60, LiveRules.Backoff(1, 60));
        Assert.Equal(120, LiveRules.Backoff(2, 60));
        Assert.Equal(240, LiveRules.Backoff(3, 60));
        Assert.Equal(300, LiveRules.Backoff(4, 60));
        Assert.Equal(300, LiveRules.Backoff(40, 60));
        // (never sooner than the screen's own interval: a course page)
        Assert.Equal(600, LiveRules.Backoff(2, 600));
        var c = new LiveClock();
        long now = 1_000_000;
        foreach (var wait in new[] { 60, 120, 240, 300, 300 })
        {
            c.Began();
            c.Ended(now, ok: false, 60);
            Assert.Equal(LiveStep.Wait, c.Step(now + wait * S - 1, 60, 0, false, false, Free));
            Assert.Equal(LiveStep.Check, c.Step(now + wait * S, 60, 0, false, false, Free));
            now += wait * S;
        }
        c.Began();
        c.Ended(now, ok: true, 60);
        Assert.Equal(0, c.Failures);
        Assert.Equal(LiveStep.Check, c.Step(now, 60, 0, false, false, Free));
    }

    [Fact]
    public void TypingIsAKeyInTheLastFiveSeconds()
    {
        Assert.True(LiveRules.Typing(10_000, 6_000));
        Assert.False(LiveRules.Typing(11_001, 6_000));
        Assert.False(LiveRules.Typing(10_000, 0));
    }

    // ---- what is asked ----

    [Fact]
    public void ACheckAsksWithAgeAndHashAndNeverWritesAPreference()
    {
        var screen = new JsonObject { ["hideDone"] = true, ["kept"] = false };
        var a = LiveRules.CheckArgs("dashList", screen, 60, "24s-abc");
        Assert.False(a.ContainsKey("hideDone"));
        Assert.Equal(60, a["maxAge"]!.GetValue<double>());
        Assert.Equal("24s-abc", a["known"]!.GetValue<string>());
        Assert.False(a["kept"]!.GetValue<bool>());
        // (the screen's own arguments are left as they were)
        Assert.True(screen.ContainsKey("hideDone"));
        var todo = LiveRules.CheckArgs("todo", new JsonObject { ["group"] = "course", ["showDone"] = true }, 5, null);
        Assert.DoesNotContain(todo, kv => kv.Key is "group" or "showDone");
        Assert.Equal("", todo["known"]!.GetValue<string>());
        // (a call whose arguments only read keeps them: a course's)
        Assert.Equal("courses/101", LiveRules.CheckArgs("modules", new JsonObject { ["ctx"] = "courses/101" }, 600, "h")["ctx"]!.GetValue<string>());
    }

    // ---- what is answered ----

    [Fact]
    public void TheEnginesAnswerIsRead()
    {
        var same = LiveAnswer.Parse("{\"unchanged\":true,\"hash\":\"24s-16hdy1ebuko\"}");
        Assert.Equal(LiveAnswerKind.Same, same.Kind);
        Assert.Equal("24s-16hdy1ebuko", same.Hash);
        Assert.Null(same.Value);
        var changed = LiveAnswer.Parse("{\"unchanged\":false,\"hash\":\"9s-x\",\"value\":{\"rows\":[1,2],\"name\":\"Ünï \\\"q\\\"\"}}");
        Assert.Equal(LiveAnswerKind.New, changed.Kind);
        Assert.Equal("9s-x", changed.Hash);
        // (the value's JSON exactly as the call gives it without `known`, to compare with the text on screen)
        Assert.Equal("{\"rows\":[1,2],\"name\":\"Ünï \\\"q\\\"\"}", changed.Value);
        var failed = LiveAnswer.Parse("{\"error\":\"Your planner could not be loaded.\"}");
        Assert.Equal(LiveAnswerKind.Failed, failed.Kind);
        Assert.Equal("Your planner could not be loaded.", failed.Error);
        Assert.Equal(LiveAnswerKind.Failed, LiveAnswer.Parse("not json").Kind);
        Assert.Equal(LiveAnswerKind.Failed, LiveAnswer.Parse("{\"unchanged\":true}").Kind);
        Assert.Equal(LiveAnswerKind.Failed, LiveAnswer.Parse("[1]").Kind);
        // (an answer that is null still has a value: null)
        Assert.Equal("null", LiveAnswer.Parse("{\"unchanged\":false,\"hash\":\"4s-n\",\"value\":null}").Value);
    }

    // ---- which rows ----

    private static List<(string, string)> L(params string[] rows) => rows.Select(r => (r.Split('=')[0], r.Contains('=') ? r.Split('=')[1] : "")).ToList();

    [Fact]
    public void AnUnchangedListIsLeftAsItIs()
    {
        var before = L("a=1", "b=1", "c=1");
        var d = LiveRules.Rows(before, L("a=1", "b=1", "c=1"));
        Assert.True(d.Same(before));
        Assert.All(d.Rows, r => Assert.Equal(RowChange.Kept, r.Change));
    }

    [Fact]
    public void ARowDoneIsDrawnAgainInItsPlaceTheOthersKept()
    {
        var before = L("assignment:1009=open", "assignment:1010=open", "quiz:9011=open");
        var d = LiveRules.Rows(before, L("assignment:1009=done", "assignment:1010=open", "quiz:9011=open"));
        Assert.False(d.Same(before));
        Assert.Equal(new[] { RowChange.Changed, RowChange.Kept, RowChange.Kept }, d.Rows.Select(r => r.Change));
        Assert.Empty(d.Gone);
        Assert.Equal("~1", d.Said);
    }

    [Fact]
    public void ARowAddedAndOneGone()
    {
        var before = L("a=1", "b=1", "c=1");
        var d = LiveRules.Rows(before, L("a=1", "planner_note:note1=1", "c=1"));
        Assert.Equal(new[] { ("a", RowChange.Kept), ("planner_note:note1", RowChange.Added), ("c", RowChange.Kept) }, d.Rows);
        Assert.Equal(new[] { "b" }, d.Gone);
        Assert.Equal("+1 -1", d.Said);
    }

    [Fact]
    public void RowsMovedAreKeptAndSaidAsMoved()
    {
        var before = L("a=1", "b=1");
        var d = LiveRules.Rows(before, L("b=1", "a=1"));
        Assert.False(d.Same(before));
        Assert.Equal("moved", d.Said);
    }

    [Fact]
    public void ARepeatedIdIsToldApartByItsPlace()
    {
        var before = L("x=1", "x=2");
        var d = LiveRules.Rows(before, L("x=1", "x=3"));
        Assert.Equal(new[] { ("x", RowChange.Kept), ("x#2", RowChange.Changed) }, d.Rows);
        Assert.Empty(d.Gone);
        var gone = LiveRules.Rows(L("a=1", "b=1", "c=1"), L("b=1"));
        Assert.Equal(new[] { "a", "c" }, gone.Gone);
    }
}
