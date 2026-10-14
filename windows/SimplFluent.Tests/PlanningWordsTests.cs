using System.Globalization;
using Simpl.Models;
using Simpl.Screens;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The figures and words Grades, the Calendar, the Inbox and the rubric work out for themselves (from a page a version
/// older, or for what the student changes as they go), written as the engine writes its own (2.99.33): half up on the
/// decimal value, one date format, the rubric's mark at the points given.
/// </summary>
public class PlanningWordsTests
{
    [Theory]
    [InlineData(3.425, "3.43")]
    [InlineData(4, "4.00")]
    [InlineData(2.995, "3.00")]
    [InlineData(3.424999, "3.42")]
    public void AGpaIsHalfUpToTwoPlaces(double gpa, string words) => Assert.Equal(words, GradesKit.Gpa(gpa));

    [Theory]
    [InlineData(null, 81.25, "81.3%")]
    [InlineData(null, 92.0, "92%")]
    [InlineData("", null, "–")]
    [InlineData("N/A", null, "–")]
    [InlineData("92.4%", 92.4, "92.4%")]
    [InlineData("–", null, "–")]
    public void APercentIsTheEnginesOrWrittenAsItsAre(string? text, double? pct, string words) =>
        Assert.Equal(words, GradesKit.Percent(text, pct));

    [Theory]
    [InlineData(16, "16 B")]
    [InlineData(12 * 1024 + 300, "12 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    public void AFilesSizeIsWrittenAsThePageWritesIt(long bytes, string words) => Assert.Equal(words, MessageFiles.SizeText(bytes));

    [Fact]
    public void ADayIsWrittenOneWayTheYearOnlyWhenItIsNotThisOne()
    {
        var year = DateTime.Today.Year;
        Assert.Equal("Oct 13", CalendarKit.DateWords(new DateTime(year, 10, 13)));
        Assert.Equal("Mon, Oct 13, 2025", CalendarKit.DateWords(new DateTime(2025, 10, 13), weekday: true));
    }

    [Fact]
    public void OnlyWorkHandedInIsStruckAndWhatIsOverIsGreyed()
    {
        var handedIn = new CalEvent { Kind = "Assignment", Done = true };
        Assert.True(CalendarKit.IsStruck(handedIn));
        var lectureOver = new CalEvent { Kind = "Event", Done = true };
        Assert.False(CalendarKit.IsStruck(lectureOver));
        Assert.True(CalendarKit.IsPast(lectureOver));
        // (the engine's own words win: a lecture under way is not past, work excused is struck)
        Assert.False(CalendarKit.IsPast(new CalEvent { Kind = "Event", Done = true, Past = false, Struck = false }));
        Assert.True(CalendarKit.IsStruck(new CalEvent { Kind = "Assignment", Struck = true }));
    }

    [Fact]
    public void AMonthDrawsOnlyTheWeeksItTouches()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US"); // (weeks from Sunday)
            Assert.Equal(5, CalendarScreen.WeekCount(new DateTime(2026, 10, 1)));
            Assert.Equal(4, CalendarScreen.WeekCount(new DateTime(2026, 2, 1)));
            Assert.Equal(6, CalendarScreen.WeekCount(new DateTime(2026, 8, 1)));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    /// <summary>A marker picked Partial (3 of 6) and gave 4, as Canvas lets them.</summary>
    private static RubricRow Adjusted(bool? said = null, string? markText = null) => new()
    {
        Id = "c1", Name = "Correctness", Pts = "4 / 6", Worth = 6, Score = 4, Adjusted = said, MarkText = markText,
        Ratings = new()
        {
            new RubricRating { Text = "Full marks", Value = 6 },
            new RubricRating { Text = "Partial", Value = 3, Got = true },
            new RubricRating { Text = "No marks", Value = 0 },
        },
    };

    [Fact]
    public void TheMarkIsThePointsGivenWhereTheyAreNotTheLevels()
    {
        var model = new RubricModel(new[] { Adjusted() });
        var c = model.Criteria[0];
        Assert.True(c.Adjusted);
        Assert.Equal("Your mark · 4 pts", c.MarkText);
        Assert.Equal(1, c.Mark); // (the level picked is still Partial)
        // the mark's dot at 4 on a bar from 0 (its foot) to 6 (its top)
        var y = new RubricGeometry(model).MarkY(0, 500);
        Assert.NotNull(y);
        Assert.Equal(500 - (500 - RubricRing.BarTop) * 4 / 6.0, y!.Value, 6);
        // the engine's words, when it sends them
        Assert.Equal("Your mark · 4 pts", new RubricModel(new[] { Adjusted(true, "Your mark · 4 pts") }).Criteria[0].MarkText);
    }

    [Fact]
    public void AMarkOnItsLevelIsJustYourMark()
    {
        var row = Adjusted();
        row.Score = 3;
        row.Pts = "3 / 6";
        var model = new RubricModel(new[] { row });
        Assert.False(model.Criteria[0].Adjusted);
        Assert.Equal("Your mark", model.Criteria[0].MarkText);
        Assert.Null(new RubricGeometry(model).MarkY(0, 500));
    }

    [Fact]
    public void TheLevelsStandOneUnderTheNext()
    {
        var model = new RubricModel(new[] { Adjusted() });
        var places = new RubricGeometry(model).Places(0, 500, new double[] { 40, 40, 40 });
        Assert.Equal(3, places.Count);
        Assert.Equal(places[0].Row + 48, places[1].Row, 6);
        Assert.Equal(places[1].Row + 48, places[2].Row, 6);
        Assert.True(places[1].Picked);
    }
}
