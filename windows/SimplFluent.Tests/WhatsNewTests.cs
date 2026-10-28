using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// What an update brings (Support/WhatsNew.cs), as the developer tools' update played through shows it: What's New with
/// every version after the one here before and up to this one, newest first; none seen before, this version's own
/// alone; and To Do &amp; Calendar's intro only after an update from a version before the one it came with.
/// </summary>
public class WhatsNewTests
{
    [Fact]
    public void AnUpdateBringsEveryVersionSinceTheOneBefore()
    {
        var since = WhatsNew.Since("0.3.79", "0.3.82");
        Assert.Equal(new[] { "0.3.82", "0.3.81", "0.3.80" }, since.Select(r => r.Version));
        Assert.Empty(WhatsNew.Since("0.3.82", "0.3.82"));
        // (newer notes than the copy itself are never shown: a version further on is not this one's)
        Assert.DoesNotContain(WhatsNew.Since("0.3.79", "0.3.81"), r => r.Version == "0.3.82");
    }

    [Fact]
    public void NoneSeenBeforeIsThisVersionsOwnAlone()
    {
        var since = WhatsNew.Since(null, "0.3.81");
        Assert.Equal("0.3.81", Assert.Single(since).Version);
    }

    [Fact]
    public void TheIntroComesOnlyFromBeforeItsVersion()
    {
        Assert.True(MergedIntro.ComesAfter("0.3.79"));
        Assert.True(MergedIntro.ComesAfter("0.2.0"));
        Assert.False(MergedIntro.ComesAfter(MergedIntro.Since));
        Assert.False(MergedIntro.ComesAfter("0.3.81"));
    }

    [Fact]
    public void TheNotesAreNewestFirst()
    {
        // (the versions an update can be played through from are the ones after this one in the list)
        for (var i = 1; i < WhatsNew.Releases.Count; i++)
            Assert.True(WhatsNew.IsNewer(WhatsNew.Releases[i - 1].Version, WhatsNew.Releases[i].Version), $"{WhatsNew.Releases[i - 1].Version} before {WhatsNew.Releases[i].Version}");
    }
}
