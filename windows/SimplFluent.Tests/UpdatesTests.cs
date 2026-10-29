using System.Text.Json;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Which published version a copy updates to (Support/UpdatesChoice.cs): the usual releases for everyone, betas only
/// with Get beta updates on, a usual release newer than a beta taking over from it, a Critical Update reaching only
/// the copies that may take it, a copy on a beta staying on it once betas are turned off, and an earlier version put
/// back staying (the version it went back from waiting for Update now). The releases are the tests' own, made as an
/// update played through makes them (Updates.TestReleases); nothing is asked of GitHub.
/// </summary>
public class UpdatesTests
{
    private const string Page = "https://github.com/26VirenS/BetterCourseViewer/releases";

    /// <summary>A version as GitHub's releases give it: a usual release, or a beta under its own tag.</summary>
    private static Updates.Release Stable(int run, bool critical = false) => Make($"windows-fluent-v{run:000}", run, false, critical);
    private static Updates.Release Beta(int run, bool critical = false) => Make($"windows-fluent-beta-v{run:000}", run, true, critical);

    private static Updates.Release Make(string tag, int run, bool beta, bool critical) =>
        new(tag, run, $"v{run:000}", beta, DateTime.UtcNow, 50 << 20, $"{Page}/tag/{tag}",
            "Release notes." + (critical ? "\n" + Updates.CriticalMark : ""),
            new Updates.Asset($"{Page}/download/{tag}/{Updates.SetupName}", 60 << 20, null));

    [Fact]
    public void AStableCopyIsNeverOfferedABeta()
    {
        // (0.3.1's copy, betas off: the newer betas are passed over for the newest usual release newer than it)
        var published = new[] { Beta(110), Beta(108), Stable(106), Stable(104), Stable(100) };
        var c = Updates.Choose(published, 104, betas: false);
        Assert.Equal("windows-fluent-v106", c.Found?.Tag);
        Assert.False(c.Found!.Beta);
        Assert.False(c.Critical);
        // (with only betas newer: up to date — the old "every 0.x copy takes betas" is gone)
        Assert.Null(Updates.Choose(new[] { Beta(110), Stable(100) }, 100, betas: false).Found);
    }

    [Fact]
    public void ABetaCopyTakesTheNewestOfAll()
    {
        var published = new[] { Beta(110), Stable(106), Stable(100) };
        Assert.Equal("windows-fluent-beta-v110", Updates.Choose(published, 100, betas: true).Found?.Tag);
        // (the newest is a usual release: that one)
        Assert.Equal("windows-fluent-v106", Updates.Choose(new[] { Stable(106), Beta(105) }, 100, betas: true).Found?.Tag);
        // (nothing newer than this copy: up to date)
        Assert.Null(Updates.Choose(published, 110, betas: true).Found);
    }

    [Fact]
    public void AStableReleaseNewerThanABetaTakesOver()
    {
        // (on beta v105: the usual release v107 is newer, and it is the update, betas on or off)
        var published = new[] { Stable(107), Beta(106), Beta(105), Stable(100) };
        Assert.Equal("windows-fluent-v107", Updates.Choose(published, 106, betas: true).Found?.Tag);
        Assert.Equal("windows-fluent-v107", Updates.Choose(published, 105, betas: false).Found?.Tag);
        // (a beta promoted — the same run published again as a usual release: the usual release, and nothing for the
        //  copy already on that run)
        var promoted = new[] { Stable(105), Beta(105), Stable(100) };
        Assert.Equal("windows-fluent-v105", Updates.Choose(promoted, 100, betas: true).Found?.Tag);
        Assert.Equal("windows-fluent-v105", Updates.Choose(promoted, 100, betas: false).Found?.Tag);
        Assert.Null(Updates.Choose(promoted, 105, betas: true).Found);
    }

    [Fact]
    public void TurnedOffOnABetaStaysOnIt()
    {
        // (on beta v105, betas turned off: never back to v100, nor on to beta v108 — until a usual release newer comes)
        var published = new List<Updates.Release> { Beta(108), Beta(105), Stable(103), Stable(100) };
        var c = Updates.Choose(published, 105, betas: false);
        Assert.Null(c.Found);
        Assert.False(c.Critical);
        published.Insert(0, Stable(109));
        Assert.Equal("windows-fluent-v109", Updates.Choose(published, 105, betas: false).Found?.Tag);
    }

    [Fact]
    public void AStableCriticalUpdateReachesEveryone()
    {
        var published = new[] { Beta(108), Stable(106), Stable(104, critical: true), Stable(100) };
        // (a usual copy: the newest usual release goes in, whatever the settings — it comes after a Critical Update)
        var stable = Updates.Choose(published, 100, betas: false);
        Assert.Equal("windows-fluent-v106", stable.Found?.Tag);
        Assert.True(stable.Critical);
        // (a beta copy: its newest, a beta, goes in the same way)
        var beta = Updates.Choose(published, 100, betas: true);
        Assert.Equal("windows-fluent-beta-v108", beta.Found?.Tag);
        Assert.True(beta.Critical);
        // (one already past the Critical Update: an ordinary update)
        Assert.False(Updates.Choose(published, 104, betas: false).Critical);
    }

    [Fact]
    public void ABetasCriticalUpdateReachesOnlyBetaCopies()
    {
        var published = new[] { Beta(108, critical: true), Stable(104), Stable(100) };
        var stable = Updates.Choose(published, 100, betas: false);
        Assert.Equal("windows-fluent-v104", stable.Found?.Tag);
        Assert.False(stable.Critical);
        Assert.False(Updates.Choose(published, 104, betas: false).Critical);
        Assert.Null(Updates.Choose(published, 104, betas: false).Found);
        var beta = Updates.Choose(published, 100, betas: true);
        Assert.Equal("windows-fluent-beta-v108", beta.Found?.Tag);
        Assert.True(beta.Critical);
    }

    [Fact]
    public void TagsAreReadForTheirRunAndChannel()
    {
        Assert.Equal((100, false), Updates.Read("windows-fluent-v100", "Simpl for Windows — Windows 11 look, preview (v100)"));
        Assert.Equal((105, true), Updates.Read("windows-fluent-beta-v105", "Simpl for Windows 0.3.1 beta (v105)"));
        // (a beta under the usual tag, as before betas had their own: its title says so)
        Assert.Equal((104, true), Updates.Read("windows-fluent-v104", "Simpl for Windows 0.3.1 Beta (v104)"));
        Assert.Null(Updates.Read("windows-app-v017", "Simpl for Windows — preview (v017)"));
        Assert.Null(Updates.Read("mac-v1.3.78", "Simpl for Mac 1.3.78 Beta"));
        Assert.Null(Updates.Read("v2.99.33", "Simpl Courses 2.99.33"));
        Assert.Null(Updates.Read("windows-fluent-beta-v", ""));
    }

    [Fact]
    public void AnEarlierVersionPutBackStays()
    {
        // (0.3.7: put back from v132 to v130 — v132 and v131 wait for Update now; v133 goes in as every update does)
        Assert.True(Updates.StaysBack(Stable(132), critical: false, stayingFrom: 132));
        Assert.True(Updates.StaysBack(Stable(131), critical: false, stayingFrom: 132));
        Assert.False(Updates.StaysBack(Stable(133), critical: false, stayingFrom: 132));
        // (a Critical Update goes in all the same; with nothing put back, or nothing found, nothing waits)
        Assert.False(Updates.StaysBack(Stable(132, critical: true), critical: true, stayingFrom: 132));
        Assert.False(Updates.StaysBack(Stable(132), critical: false, stayingFrom: null));
        Assert.False(Updates.StaysBack(null, critical: false, stayingFrom: 132));
        // (as a look finds it on v130: the version it went back from is the update, and it waits)
        var published = new List<Updates.Release> { Stable(132), Stable(131), Stable(130), Stable(128) };
        var c = Updates.Choose(published, 130, betas: false);
        Assert.Equal("windows-fluent-v132", c.Found?.Tag);
        Assert.True(Updates.StaysBack(c.Found, c.Critical, 132));
        // (a newer one published: it is the update, and it goes in by itself)
        published.Insert(0, Stable(133));
        c = Updates.Choose(published, 130, betas: false);
        Assert.Equal("windows-fluent-v133", c.Found?.Tag);
        Assert.False(Updates.StaysBack(c.Found, c.Critical, 132));
        // (v132 a Critical Update: it goes back in, whatever the stay)
        c = Updates.Choose(new[] { Stable(132, critical: true), Stable(130) }, 130, betas: false);
        Assert.False(Updates.StaysBack(c.Found, c.Critical, 132));
    }

    [Fact]
    public void ACopyFromBeforeTheBetaChannelNeverSeesABeta()
    {
        // (0.3.0 — windows-fluent-v100 — lists only the tags this matches, and takes any 0.x one it lists: a beta's own
        //  tag never matches, so no beta reaches it, whatever the beta's title)
        const string ZeroThreeZero = @"^windows-fluent-v(\d+)$";
        Assert.Matches(ZeroThreeZero, "windows-fluent-v105");
        Assert.DoesNotMatch(ZeroThreeZero, "windows-fluent-beta-v105");
    }

    [Fact]
    public void GitHubsListIsReadForBothTags()
    {
        // (as GitHub gives it: the Mac's and the extension's beside, a draft, and run 105 published as a beta and then
        //  promoted)
        const string json = """
        [
          { "tag_name": "v2.99.33", "name": "Simpl Courses 2.99.33", "draft": false, "assets": [] },
          { "tag_name": "windows-fluent-v105", "name": "Simpl for Windows — Windows 11 look, preview (v105)", "draft": false, "prerelease": true,
            "html_url": "https://github.com/x/y/releases/tag/windows-fluent-v105", "body": "Notes.", "published_at": "2026-10-20T10:00:00Z",
            "assets": [ { "name": "Simpl-Setup.exe", "size": 62914560, "browser_download_url": "https://github.com/x/y/releases/download/windows-fluent-v105/Simpl-Setup.exe", "digest": "sha256:ABCD" },
                        { "name": "Simpl-Windows-x64.zip", "size": 52428800, "browser_download_url": "https://github.com/x/y/z.zip" } ] },
          { "tag_name": "windows-fluent-beta-v107", "name": "Simpl for Windows 0.3.2 beta (v107)", "draft": false, "prerelease": true,
            "html_url": "https://github.com/x/y/releases/tag/windows-fluent-beta-v107", "body": "Notes.\n<!-- simpl-critical -->", "assets": [] },
          { "tag_name": "windows-fluent-v110", "name": "a draft", "draft": true, "assets": [] },
          { "tag_name": "windows-fluent-beta-v105", "name": "Simpl for Windows 0.3.1 beta (v105)", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "mac-v1.3.78", "name": "Simpl for Mac 1.3.78 Beta", "draft": false, "assets": [] },
          { "tag_name": "windows-fluent-v100", "name": "Simpl for Windows — Windows 11 look, preview (v100)", "draft": false, "assets": [] }
        ]
        """;
        using var doc = JsonDocument.Parse(json);
        var list = Updates.Parse(doc.RootElement);
        Assert.Equal(new[] { "windows-fluent-beta-v107", "windows-fluent-v105", "windows-fluent-v100" }, list.Select(r => r.Tag));
        Assert.Equal(new[] { true, false, false }, list.Select(r => r.Beta));
        Assert.True(list[0].Critical);
        var promoted = list[1];
        Assert.Equal("v105", promoted.Version);
        Assert.Equal(52428800, promoted.Size);
        Assert.Equal(62914560, promoted.Setup?.Size);
        Assert.Equal("ABCD", promoted.Setup?.Sha256);
        // (and chosen from: a usual copy on v100 takes v105; a beta copy, the critical beta v107)
        Assert.Equal("windows-fluent-v105", Updates.Choose(list, 100, betas: false).Found?.Tag);
        var beta = Updates.Choose(list, 100, betas: true);
        Assert.Equal("windows-fluent-beta-v107", beta.Found?.Tag);
        Assert.True(beta.Critical);
    }

    [Fact]
    public void TheSitesFeedNamesTheCriticalUpdateACopyMayTake()
    {
        // (as scripts/publish-site.sh writes /app/windows-latest.json: a usual Critical Update v127, a beta one v130)
        using var both = JsonDocument.Parse("""{ "newest": 129, "newestBeta": 130, "critical": 127, "criticalBeta": 130 }""");
        Assert.Equal(127, Updates.CriticalRun(both.RootElement, betas: false));
        Assert.Equal(130, Updates.CriticalRun(both.RootElement, betas: true));
        // (none named — null, left out — or a feed that is not one: none)
        using var none = JsonDocument.Parse("""{ "newest": 129, "newestBeta": 130, "critical": null, "criticalBeta": null }""");
        Assert.Equal(0, Updates.CriticalRun(none.RootElement, betas: true));
        using var bare = JsonDocument.Parse("""{ "newest": 129 }""");
        Assert.Equal(0, Updates.CriticalRun(bare.RootElement, betas: false));
        using var odd = JsonDocument.Parse("""[ "critical", 127 ]""");
        Assert.Equal(0, Updates.CriticalRun(odd.RootElement, betas: true));
        // (only a usual Critical Update: a beta copy takes it too)
        using var usual = JsonDocument.Parse("""{ "critical": 128, "criticalBeta": 128 }""");
        Assert.Equal(128, Updates.CriticalRun(usual.RootElement, betas: true));
        // (0.3.89: the newest of any kind the half-minute look hands on — a usual copy the newest usual release, a beta
        //  copy the newest of all; none named, or no feed, none)
        Assert.Equal(129, Updates.NewestRun(both.RootElement, betas: false));
        Assert.Equal(130, Updates.NewestRun(both.RootElement, betas: true));
        Assert.Equal(129, Updates.NewestRun(bare.RootElement, betas: true));
        Assert.Equal(0, Updates.NewestRun(usual.RootElement, betas: true));
        Assert.Equal(0, Updates.NewestRun(odd.RootElement, betas: false));
    }
}
