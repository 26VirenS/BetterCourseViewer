using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>(0.3.67) Simpl Community (Support/Community.cs, Appearance.PanFrom): which photos are read, what they are,
/// and where a theme's picture is put as this app keeps it.</summary>
public class CommunityTests
{
    public CommunityTests() => _ = Profile.Folder;

    private static readonly Uri Site = new("https://simplcourses.com");
    private static readonly Uri Mock = new("http://localhost:8800");

    [Fact]
    public void OnlyTheGallerysOwnPhotosAreRead()
    {
        Assert.NotNull(Community.PhotoUrl("https://simplcourses.com/api/community/photo/d385vtz8/side", Site));
        Assert.NotNull(Community.PhotoUrl("https://www.simplcourses.com/api/community/photo/d385vtz8/today", Mock));
        Assert.NotNull(Community.PhotoUrl("http://localhost:8800/api/community/photo/d385vtz8/today", Mock));
        // (not over https, not the site, not a photo's path, another port, not an id)
        Assert.Null(Community.PhotoUrl("http://simplcourses.com/api/community/photo/d385vtz8/side", Site));
        Assert.Null(Community.PhotoUrl("https://simplcourses.com.evil.example/api/community/photo/d385vtz8/side", Site));
        Assert.Null(Community.PhotoUrl("https://simplcourses.com/api/community/photo/../themes", Site));
        Assert.Null(Community.PhotoUrl("http://localhost:9999/api/community/photo/d385vtz8/today", Mock));
        Assert.Null(Community.PhotoUrl("https://simplcourses.com/api/community/photo/D385/side", Site));
        Assert.Null(Community.PhotoUrl(null, Site));
        Assert.Null(Community.PhotoUrl("not a link", Site));
    }

    [Fact]
    public void APhotoIsKnownByItsFirstBytes()
    {
        Assert.Equal(".jpg", Community.Extension(new byte[] { 0xff, 0xd8, 0xff, 0xe0 }));
        Assert.Equal(".png", Community.Extension(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0 }));
        Assert.Equal(".webp", Community.Extension("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Null(Community.Extension("<html>"u8.ToArray()));
        Assert.Null(Community.Extension(Array.Empty<byte>()));
    }

    [Fact]
    public void WherePinnedStaysPinned()
    {
        Assert.Equal((0.0, 0.0), Appearance.PanFrom("today", 100, 100, 1, 1.5));
        Assert.Equal((0.0, 0.0), Appearance.PanFrom("side", 50, 100, 1.7, 1.5));
        Assert.Equal((0.0, 0.0), Appearance.PanFrom("graded", 100, 100, 2, 0));
    }

    [Fact]
    public void APictureMovesAsFarAsItOverflowsItsPlace()
    {
        // (a 3:2 photo on a counter, about 2.6:1: as wide as it, a third again as tall — halfway up moves it down by half of that)
        Assert.Equal((0.0, 0.367), Appearance.PanFrom("today", 0, 50, 1, 1.5));
        // (the same photo in the pane, far wider than it: a little left of its middle is as far right as it goes; zoomed,
        //  a fifth taller than the pane, four fifths down lifts it by a fifth of that)
        Assert.Equal((0.5, 0.04), Appearance.PanFrom("side", 30, 80, 1.2, 1.5));
        // (zoomed out to half, against the counter's left: half the counter to the left)
        Assert.Equal((-0.5, 0.0), Appearance.PanFrom("overdue", 0, 100, 0.5, 2.6));
    }
}
