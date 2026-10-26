using System.Windows.Media;
using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>(0.3.74) A colour worn throughout (Support/Appearance.cs): the shades drawn from it, mixed as the web mixes
/// them, and each standing against its ground.</summary>
public class ColourWornTests
{
    private static readonly Color Dusk = Color.FromRgb(0xff, 0x37, 0x5f), Ocean = Color.FromRgb(0x40, 0xc8, 0xe0);
    private static readonly Color LightPane = Color.FromRgb(0xf3, 0xf3, 0xf3), DarkPane = Color.FromRgb(0x20, 0x20, 0x20);

    [Fact]
    public void AMixKeepsItsEnds()
    {
        Assert.Equal(Dusk, Appearance.MixOk(Dusk, Colors.White, 0));
        Assert.Equal(Colors.White, Appearance.MixOk(Dusk, Colors.White, 1));
        Assert.Equal(Colors.Black, Appearance.MixOk(Dusk, Colors.Black, 1));
    }

    [Fact]
    public void TheCountersShadesGoFromLightToDeep()
    {
        // (the web's: Due today 38% white, Tomorrow 20% white, Next 7 days the colour, then 16%, 32% and 48% black)
        double L(Color c) => Kit.K.Luminance(c);
        var shades = new[]
        {
            Appearance.MixOk(Ocean, Colors.White, 0.38), Appearance.MixOk(Ocean, Colors.White, 0.2), Ocean,
            Appearance.MixOk(Ocean, Colors.Black, 0.16), Appearance.MixOk(Ocean, Colors.Black, 0.32), Appearance.MixOk(Ocean, Colors.Black, 0.48),
        };
        for (var i = 1; i < shades.Length; i++) Assert.True(L(shades[i]) < L(shades[i - 1]), $"shade {i} is deeper than shade {i - 1}");
    }

    [Fact]
    public void ReachStandsThreeToOne()
    {
        foreach (var ground in new[] { Colors.White, LightPane, DarkPane })
        foreach (var c in new[] { Dusk, Ocean, Color.FromRgb(0xff, 0x9f, 0x0a), Color.FromRgb(0x5e, 0x5c, 0xe6) })
            Assert.True(Appearance.Contrast(Appearance.Reach(c, ground, 3), ground) >= 3, $"#{c.R:x2}{c.G:x2}{c.B:x2} on #{ground.R:x2}{ground.G:x2}{ground.B:x2}");
        // (one that already stands is left as it is)
        Assert.Equal(Color.FromRgb(0x00, 0x5a, 0x9e), Appearance.Reach(Color.FromRgb(0x00, 0x5a, 0x9e), Colors.White, 3));
    }

    [Fact]
    public void ThePanesShadesEachStandAndDiffer()
    {
        foreach (var ground in new[] { LightPane, DarkPane })
        {
            var shades = Appearance.Shades(Dusk, 9, ground);
            Assert.Equal(9, shades.Length);
            Assert.All(shades, s => Assert.True(Appearance.Contrast(s, ground) >= 3));
            Assert.True(shades.Take(5).Distinct().Count() >= 3, "the first five rows are not one flat colour");
        }
        Assert.Empty(Appearance.Shades(Dusk, 0, LightPane));
    }
}
