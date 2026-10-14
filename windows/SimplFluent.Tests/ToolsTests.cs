using Simpl.Engine;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The tools' place in the window and their words: a tool opened whole is a popup over the window, never a step of a
/// place (Engine/Navigator.cs, Tools/ToolHost.cs), and the unit converter writes an answer with its unit
/// (Tools/Everyday/UnitConverter.cs).
/// </summary>
public class ToolsTests
{
    public ToolsTests() => _ = Profile.Folder;

    [Fact]
    public void AToolIsNoStepOfAPlace()
    {
        // (a tool opens in its popup over where you are: a route of one, kept by an earlier version, belongs to no place)
        Assert.Null(Engine.Engine.PlaceOf(new Route(RouteKind.Tool, "units")));
    }

    [Fact]
    public void AScreenBelongsWhereItsCourseOrItsRootPlaceIs()
    {
        Assert.True(Navigator.Within(Place.Tools, Place.Tools));
        Assert.False(Navigator.Within(Place.Dashboard, Place.Tools));
        Assert.True(Navigator.Within(Place.Home("courses/1"), Place.Section("courses/1", "assignments")));
        Assert.False(Navigator.Within(Place.Home("courses/2"), Place.Section("courses/1", "assignments")));
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
