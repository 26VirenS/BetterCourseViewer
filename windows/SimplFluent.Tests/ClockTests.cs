using System.Globalization;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// (The UI review's W22) One clock for every time (Support/Clock.cs): the reader's own — "11:59 PM" on a 12-hour clock,
/// "23:59" on a 24-hour one — and a publisher's or Canvas's times, written their own way, written the app's.
/// </summary>
public class ClockTests
{
    private static T In<T>(string culture, Func<T> f)
    {
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try { return f(); } finally { CultureInfo.CurrentCulture = was; }
    }

    [Fact]
    public void ATimeIsTheReadersClock()
    {
        var t = new DateTime(2026, 11, 3, 23, 59, 0);
        Assert.Equal("11:59 PM", In("en-US", () => Clock.Time(t)));
        Assert.Equal("23:59", In("en-GB", () => Clock.Time(t)));
    }

    [Fact]
    public void AnHourAloneNamesNoon()
    {
        Assert.Equal("Noon", In("en-US", () => Clock.Hour(12)));
        Assert.Equal("12 AM", In("en-US", () => Clock.Hour(0)));
        Assert.Equal("8 AM", In("en-US", () => Clock.Hour(8)));
        Assert.Equal("13:00", In("en-GB", () => Clock.Hour(13)));
    }

    [Theory]
    [InlineData("Due Nov 3, 11:59 pm", "Due Nov 3, 11:59 PM")]
    [InlineData("6 times open · 11:15am – 12:30pm", "6 times open · 11:15 AM – 12:30 PM")]
    [InlineData("Opens 9:00 A.M. Monday", "Opens 9:00 AM Monday")]
    [InlineData("10:30a", "10:30 AM")]
    [InlineData("Due 11:59 PM", "Due 11:59 PM")]
    [InlineData("at 3:15 a student asked", "at 3:15 a student asked")]
    [InlineData("Chapter 3:45 and 14:30", "Chapter 3:45 and 14:30")]
    public void APublishersTimesAreWrittenTheAppsWay(string from, string to) => Assert.Equal(to, In("en-US", () => Clock.Tidy(from)));

    [Fact]
    public void OnATwentyFourHourClockTheyAreTooAndNothingElseIsTouched()
    {
        Assert.Equal("Due Nov 3, 23:59", In("en-GB", () => Clock.Tidy("Due Nov 3, 11:59 pm")));
        Assert.Equal("", Clock.Tidy(null));
    }
}
