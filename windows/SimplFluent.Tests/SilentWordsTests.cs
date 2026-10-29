using Simpl.Models;
using Simpl.Screens;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// (0.3.84) Silent mode's words (Screens/DashboardSilent.cs SilentWords), as the web's and the Mac's: how soon the next
/// piece of work is, counting down within the day, then its day and time and its course; a row's day and time.
/// </summary>
public class SilentWordsTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0);

    private static DashSilentRow Row(DateTime when, string? verb = "Due", string? course = "BIO 101") => new()
    {
        Id = "1", Title = "Lab report", At = new DateTimeOffset(when).ToUnixTimeMilliseconds(), Verb = verb, Course = course,
    };

    [Fact]
    public void WithinTheHourItCountsMinutes()
    {
        var (lead, after) = SilentWords.Next(Row(Now.AddMinutes(25)), Now);
        Assert.Equal("Due in 25 minutes", lead);
        Assert.Equal(new[] { "Today at 9:25 AM", "BIO 101" }, after);
        Assert.Equal("Due in 1 minute", SilentWords.Next(Row(Now.AddSeconds(20)), Now).Lead);
    }

    [Fact]
    public void WithinTheDayItCountsHoursAndMinutes()
    {
        Assert.Equal("Due in 4 h 40 min", SilentWords.Next(Row(Now.AddHours(4).AddMinutes(40)), Now).Lead);
        Assert.Equal("Starts in 2 h", SilentWords.Next(Row(Now.AddHours(2), verb: "Starts"), Now).Lead);
        // (a task of one's own: just "In…")
        Assert.Equal("In 3 h", SilentWords.Next(Row(Now.AddHours(3), verb: ""), Now).Lead);
    }

    [Fact]
    public void FurtherOnItSaysTheDay()
    {
        var (lead, after) = SilentWords.Next(Row(new DateTime(2026, 10, 30, 23, 59, 0)), Now);
        Assert.Equal("Due tomorrow at 11:59 PM", lead);
        Assert.Equal(new[] { "BIO 101" }, after);
        Assert.Equal("Due Monday at 11:59 PM", SilentWords.Next(Row(new DateTime(2026, 11, 2, 23, 59, 0)), Now).Lead);
        Assert.Equal("November 12 at 8:00 AM", SilentWords.Next(Row(new DateTime(2026, 11, 12, 8, 0, 0), verb: null, course: null), Now).Lead);
    }

    [Fact]
    public void ARowSaysItsDayBriefly()
    {
        Assert.Equal("Tomorrow, 9:00 AM", SilentWords.Row(Row(new DateTime(2026, 10, 30, 9, 0, 0)), Now));
        Assert.Equal("Sat, 11:59 PM", SilentWords.Row(Row(new DateTime(2026, 10, 31, 23, 59, 0)), Now));
        Assert.Equal("Nov 12, 8:00 AM", SilentWords.Row(Row(new DateTime(2026, 11, 12, 8, 0, 0)), Now));
    }
}
