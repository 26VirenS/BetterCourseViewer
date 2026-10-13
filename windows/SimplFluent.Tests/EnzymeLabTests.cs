using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Enzyme Lab's model (Tools/Biology/Model/EnzymeLabModel.cs) against the specification's §5.7: Michaelis–Menten and
/// its two inhibitors, the temperature presets' peaks, irreversible denaturation, the pH bell, the energy diagram, and
/// the simulator's settings linking the three graphs. Plain .NET: no window is opened.
/// </summary>
public class EnzymeLabTests
{
    [Fact]
    public void MichaelisMentenAndItsInhibitors()
    {
        Assert.Equal(50, EnzymeLabModel.Rate(2, 100, 2), 12);
        Assert.Equal(0, EnzymeLabModel.Rate(0, 100, 2));
        // competitive: Km up, Vmax the same; overcome by enough substrate
        var comp = EnzymeLabModel.Apparent(100, 2, EnzymeInhibitor.Competitive, 2, 1);
        Assert.Equal(6, comp.Km, 12);
        Assert.Equal(100, comp.Vmax, 12);
        Assert.True(EnzymeLabModel.Rate(1e5, comp.Vmax, comp.Km) > 99.9);
        // noncompetitive: Vmax down, Km the same; not overcome by more substrate
        var non = EnzymeLabModel.Apparent(100, 2, EnzymeInhibitor.Noncompetitive, 2, 1);
        Assert.Equal(2, non.Km, 12);
        Assert.Equal(100.0 / 3, non.Vmax, 12);
        Assert.True(EnzymeLabModel.Rate(1e5, non.Vmax, non.Km) < 33.34);
        Assert.Equal((100.0, 2.0), EnzymeLabModel.Apparent(100, 2, EnzymeInhibitor.None, 2, 1));
    }

    [Fact]
    public void TemperatureCurvesPeakWhereTheSpecificationSays()
    {
        foreach (var p in EnzymeLabModel.TemperaturePresets)
        {
            var peak = EnzymeLabModel.PeakTemperature(p);
            Assert.InRange(peak, p.StatedPeak - 0.1, p.StatedPeak + 0.1);
            Assert.Equal(1, EnzymeLabModel.TemperatureFactor(peak, p), 12);
            // a numerical maximum agrees with the formula's
            var best = Enumerable.Range(0, 100001).Select(i => i / 1000.0).MaxBy(t => EnzymeLabModel.TemperatureFactor(t, p));
            Assert.InRange(best, peak - 0.002, peak + 0.002);
            Assert.Equal(p.Tm + 2 * p.W, EnzymeLabModel.DenaturesAbove(p), 12);
        }
        Assert.Equal(new[] { "human", "taq", "fish" }, EnzymeLabModel.TemperaturePresets.Select(p => p.Id));
    }

    [Fact]
    public void ThePhBell()
    {
        Assert.Equal(1, EnzymeLabModel.PhFactor(6.8, 6.8), 12);
        Assert.Equal(Math.Exp(-1), EnzymeLabModel.PhFactor(5.3, 6.8), 12);
        Assert.Equal(EnzymeLabModel.PhFactor(5, 7), EnzymeLabModel.PhFactor(9, 7), 12);
        Assert.Equal(new[] { 2.0, 6.8, 7.0, 8.0 }, EnzymeLabModel.PhPresets.Select(p => p.Optimum));
    }

    [Fact]
    public void TheEnergyDiagramKeepsDeltaG()
    {
        foreach (var (profile, ea) in new[] { (EnzymeLabModel.Uncatalysed, 75.0), (EnzymeLabModel.Catalysed, 40.0) })
        {
            Assert.Equal(0, profile[0].G, 9);
            Assert.Equal(-30, profile[^1].G, 9);
            Assert.Equal(ea, profile.Max(p => p.G), 9);
            Assert.Equal(101, profile.Count);
        }
        Assert.Equal(EnzymeLabModel.EaCatalysed, EnzymeLabModel.Energy(0.5, EnzymeLabModel.EaCatalysed), 12);
    }

    [Fact]
    public void TheSettingsLinkTheGraphsAndDenaturingLasts()
    {
        EnzymeLabModel.Reset();
        try
        {
            // the Mac's starting settings
            Assert.Equal((20.0, 100.0, 5.0, 10.0, 5.0, 37.0, 7.0), (EnzymeLabModel.Substrate, EnzymeLabModel.Vmax, EnzymeLabModel.Km, EnzymeLabModel.InhibitorConcentration, EnzymeLabModel.Ki, EnzymeLabModel.Temperature, EnzymeLabModel.Ph));
            Assert.Equal(("human", "amylase"), (EnzymeLabModel.TemperatureId, EnzymeLabModel.PhId));

            // linked: the rate against [S] uses Vmax × f(T) × f(pH), and the curves pass through the points each graph marks
            var rate = EnzymeLabModel.RateNow;
            Assert.Equal(EnzymeLabModel.Rate(20, 100 * EnzymeLabModel.TemperatureNow * EnzymeLabModel.PhNow, 5), rate, 12);
            var onS = EnzymeLabModel.RateCurve().MinBy(p => Math.Abs(p.X - EnzymeLabModel.Substrate));
            Assert.Equal(rate, onS.Y, 9);
            var onT = EnzymeLabModel.TemperatureCurve(EnzymeLabModel.Preset).MinBy(p => Math.Abs(p.X - EnzymeLabModel.Temperature));
            Assert.Equal(EnzymeLabModel.TemperatureNow, onT.Y, 9);
            var onPh = EnzymeLabModel.PhCurve(EnzymeLabModel.PhEnzyme).MinBy(p => Math.Abs(p.X - EnzymeLabModel.Ph));
            Assert.Equal(EnzymeLabModel.PhNow, onPh.Y, 9);
            Assert.Equal(EnzymeSite.Bound, EnzymeLabModel.Site);

            // a competitive inhibitor lowers the rate at low [S]; enough substrate overcomes it
            EnzymeLabModel.SetInhibitor(EnzymeInhibitor.Competitive);
            Assert.True(EnzymeLabModel.RateNow < rate);
            Assert.Equal(EnzymeSite.Competitive, EnzymeLabModel.Site);
            EnzymeLabModel.SetInhibitor(EnzymeInhibitor.Noncompetitive);
            Assert.Equal(EnzymeSite.Noncompetitive, EnzymeLabModel.Site);
            EnzymeLabModel.SetInhibitor(EnzymeInhibitor.None);

            // heated past Tm + 2w: denatured, and cooling down leaves 10%
            var fresh = EnzymeLabModel.RateNow;
            EnzymeLabModel.SetTemperature(45);
            Assert.False(EnzymeLabModel.Denatured);
            EnzymeLabModel.SetTemperature(47);
            Assert.True(EnzymeLabModel.Denatured);
            Assert.Equal(EnzymeSite.Denatured, EnzymeLabModel.Site);
            EnzymeLabModel.SetTemperature(37);
            Assert.Equal(fresh * EnzymeLabModel.AfterDenaturing, EnzymeLabModel.RateNow, 9);
            Assert.Equal(EnzymeLabModel.TemperatureCurve(EnzymeLabModel.Preset, cooled: true)[37].Y, EnzymeLabModel.TemperatureNow, 9);
            EnzymeLabModel.Fresh();
            Assert.False(EnzymeLabModel.Denatured);
            Assert.Equal(fresh, EnzymeLabModel.RateNow, 9);

            // a fresh enzyme while it is still too hot: at its optimum instead
            EnzymeLabModel.SetTemperature(60);
            EnzymeLabModel.Fresh();
            Assert.False(EnzymeLabModel.Denatured);
            Assert.Equal(37, EnzymeLabModel.Temperature);

            // another enzyme's curve: a fresh sample, which unfolds at once if the temperature is already past it
            EnzymeLabModel.SetTemperature(60);
            EnzymeLabModel.SetTemperaturePreset("taq");
            Assert.False(EnzymeLabModel.Denatured);
            Assert.Equal(60, EnzymeLabModel.Temperature);
            EnzymeLabModel.SetTemperaturePreset("fish");
            Assert.True(EnzymeLabModel.Denatured);
            EnzymeLabModel.SetTemperaturePreset("nonsense");
            Assert.Equal("human", EnzymeLabModel.TemperatureId);

            // a pH preset changes the curve, not the pH set
            EnzymeLabModel.SetPhPreset("pepsin");
            Assert.Equal(7.0, EnzymeLabModel.Ph);
            Assert.True(EnzymeLabModel.PhStrain > 0.5);
            Assert.NotNull(EnzymeLabModel.StrainNote);
            EnzymeLabModel.SetPh(2);
            Assert.Equal(1, EnzymeLabModel.PhNow, 12);

            // the settings stay in their ranges
            EnzymeLabModel.SetSubstrate(500);
            EnzymeLabModel.SetKm(0);
            EnzymeLabModel.SetPh(-3);
            Assert.Equal((100.0, 0.5, 0.0), (EnzymeLabModel.Substrate, EnzymeLabModel.Km, EnzymeLabModel.Ph));
            Assert.Equal(5, EnzymeLabModel.MixUps.Count);
            Assert.All(EnzymeLabModel.MixUps, m => Assert.False(string.IsNullOrWhiteSpace(m.Text)));
        }
        finally { EnzymeLabModel.Reset(); }
    }
}
