using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// What an update brings (Support/WhatsNew.cs), as the developer tools' update played through shows it: What's New with
/// every version after the one here before and up to this one, newest first; none seen before, this version's own
/// alone; and To Do &amp; Calendar's intro only after an update from a version before the one it came with. It leads with
/// a few changes a student notices, and every title is in sentence case.
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
    public void ItLeadsWithAFewChangesAStudentNotices()
    {
        // (one to three, the newest version's first, what is new before what is fixed, never the developer tools')
        var since = WhatsNew.Since("0.3.103", "0.3.105");
        var leading = WhatsNew.Leading(since);
        Assert.InRange(leading.Count, 1, 3);
        Assert.Equal("Community tools", leading[0].Title);
        Assert.DoesNotContain(leading, n => n.Kind == "dev");
        Assert.True(leading.Select(n => n.Kind).SequenceEqual(leading.Select(n => n.Kind).OrderBy(k => k == "new" ? 0 : k == "improved" ? 1 : 2)) || leading.Count == 1);
        Assert.DoesNotContain(WhatsNew.Leading(WhatsNew.Since("0.3.82", "0.3.83")), n => n.Title == "Simulate an update");
        Assert.All(WhatsNew.Releases, r => Assert.InRange(WhatsNew.Leading(new[] { r }).Count, 0, 3));
    }

    [Fact]
    public void EveryTitleIsInSentenceCase()
    {
        // (Windows 11's rule, as every title in the app: "Share your theme", never "Share Your Theme")
        var bad = WhatsNew.Releases.SelectMany(r => r.Notes.Select(n => (r.Version, n.Title, Words: SentenceCase.Offenders(n.Title))))
            .Where(x => x.Words.Count > 0).Select(x => $"{x.Version} “{x.Title}”: {string.Join(", ", x.Words)}").ToList();
        Assert.True(bad.Count == 0, "Title Case in What's New: " + string.Join("; ", bad));
    }

    [Fact]
    public void TheNotesAreNewestFirst()
    {
        // (the versions an update can be played through from are the ones after this one in the list)
        for (var i = 1; i < WhatsNew.Releases.Count; i++)
            Assert.True(WhatsNew.IsNewer(WhatsNew.Releases[i - 1].Version, WhatsNew.Releases[i].Version), $"{WhatsNew.Releases[i - 1].Version} before {WhatsNew.Releases[i].Version}");
    }
}
