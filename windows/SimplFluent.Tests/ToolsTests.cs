using Simpl.Engine;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The tools' place in the window and their words: a tool opened on the Tools page is a step of the Tools place
/// (Engine/Navigator.cs, Tools/ToolHost.cs), and the unit converter writes an answer with its unit
/// (Tools/Everyday/UnitConverter.cs).
/// </summary>
public class ToolsTests
{
    public ToolsTests() => _ = Profile.Folder;

    [Fact]
    public void AToolOpenedFromElsewhereIsTheToolsPlaceWithTheToolOnIt()
    {
        var nav = new Navigator();
        nav.Go(Place.Dashboard);
        nav.Push(Route.Tool("calc"), Place.Tools);
        Assert.Equal(Place.Tools, nav.Current.Place);
        Assert.Equal(new[] { Route.Tool("calc") }, nav.Current.Path);
        // (one step: Back goes to where it was opened from)
        nav.GoBack();
        Assert.Equal(Place.Dashboard, nav.Current.Place);
        Assert.Empty(nav.Current.Path);
    }

    [Fact]
    public void AToolOpenedFromTheTilesIsAStepDeeper()
    {
        var nav = new Navigator();
        nav.Go(Place.Tools);
        nav.Push(Route.Tool("ptable"), Place.Tools);
        Assert.Equal(Place.Tools, nav.Current.Place);
        Assert.Single(nav.Current.Path);
        nav.GoBack();
        Assert.Equal(Place.Tools, nav.Current.Place);
        Assert.Empty(nav.Current.Path);
    }

    [Fact]
    public void AScreenBelongsWhereItsCourseOrItsRootPlaceIs()
    {
        Assert.True(Navigator.Within(Place.Tools, Place.Tools));
        Assert.False(Navigator.Within(Place.Dashboard, Place.Tools));
        Assert.True(Navigator.Within(Place.Home("courses/1"), Place.Section("courses/1", "assignments")));
        Assert.False(Navigator.Within(Place.Home("courses/2"), Place.Section("courses/1", "assignments")));
        Assert.Equal(Place.Tools, Engine.Engine.PlaceOf(Route.Tool("units")));
    }

    [Theory]
    [InlineData(212, "°F", "212 °F")]
    [InlineData(3.28, "ft", "3.28 ft")]
    [InlineData(90, "°", "90°")]
    [InlineData(30, "′", "30′")]
    [InlineData(-40, "°C", "−40 °C")]
    public void AnAnswerHasItsUnit(double v, string symbol, string text) =>
        Assert.Equal(text, UnitMath.WithUnit(v, new UnitChoice("unit", symbol, 1)));
}
