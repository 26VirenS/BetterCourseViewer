using Simpl.SignIn;
using Xunit;

namespace Simpl.Tests;

/// <summary>The school search's rows (SignIn/SchoolSearch.cs): one a site, under the name that matched best.</summary>
public class SchoolSearchTests
{
    private static SchoolSearch.School S(string name, string domain) => new(name, domain);

    [Fact]
    public void ASiteListedTwiceIsOneRow()
    {
        var rows = SchoolSearch.BySite(new[]
        {
            (6, S("State Center SCCCD", "scccd.instructure.com")),
            (6, S("State Center Community College District", "scccd.instructure.com")),
            (4, S("State College of Florida", "scf.instructure.com")),
        }).ToList();
        Assert.Equal(2, rows.Count);
        // (as good a match: the shorter name leads, the other said under it)
        Assert.Equal("State Center SCCCD", rows[0].Name);
        Assert.Equal("scccd.instructure.com · also: State Center Community College District", rows[0].Sub);
        Assert.Equal("scf.instructure.com", rows[1].Sub);
    }

    [Fact]
    public void TheBestMatchNamesTheRowAndItsSiteKeepsItsPlace()
    {
        var rows = SchoolSearch.BySite(new[]
        {
            (2, S("State College Area School District - Students & Teachers", "scasd.instructure.com")),
            (8, S("State College Area School District - Parents", "SCASD.instructure.com")),
            (4, S("Other", "other.instructure.com")),
        }).ToList();
        Assert.Equal(new[] { "State College Area School District - Parents", "Other" }, rows.Select(r => r.Name));
        Assert.Equal(new[] { "State College Area School District - Students & Teachers" }, rows[0].Also);
    }

    [Fact]
    public void MoreThanTwoOtherNamesAreCounted()
    {
        var row = S("A", "a.edu") with { Also = new[] { "B", "C", "D", "E" } };
        Assert.Equal("a.edu · also: B, C +2", row.Sub);
    }

    [Fact]
    public void SchoolsFoundLaterJoinTheirSite()
    {
        var rows = new List<SchoolSearch.School> { S("Lakeside University", "lakeside.instructure.com") };
        var joined = SchoolSearch.Join(rows, new[]
        {
            S("Lakeside", "lakeside.instructure.com"),
            S("Lakeside University", "lakeside.instructure.com"),
            S("Lakeside Online", "online.lakeside.edu"),
            S("Lakeside Online Campus", "online.lakeside.edu"),
        }, first: true);
        Assert.Equal(2, joined.Count);
        // (a new site first, its two names as one row; the site already listed gains the new name, not its own again)
        Assert.Equal("Lakeside Online", joined[0].Name);
        Assert.Equal(new[] { "Lakeside Online Campus" }, joined[0].Also);
        Assert.Equal(new[] { "Lakeside" }, joined[1].Also);
        Assert.Equal("lakeside.instructure.com", joined[1].Id);
    }
}
