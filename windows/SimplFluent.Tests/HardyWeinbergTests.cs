using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Hardy–Weinberg's model (Tools/Biology/Model/HardyWeinbergModel.cs) against the specification's §5.4 test vectors: the
/// calculator from every kind of value, the HWE chi-square test (df = 1), and the simulation in §5.4.3's order, its drift
/// drawn from SplitMix64 so a seed gives the Mac's populations. Plain .NET: no window is opened.
/// </summary>
public class HardyWeinbergTests
{
    [Fact]
    public void QSquaredOfSixteenHundredths()
    {
        var c = HardyWeinberg.Calculate(HWGiven.Q2, 0.16).Calc!;
        Assert.Equal(0.4, c.Q, 12);
        Assert.Equal(0.6, c.P, 12);
        Assert.Equal(0.48, c.TwoPQ, 12);
        Assert.Contains(c.Steps, s => s.Math == "q = √q² = √0.16 = 0.40");
        Assert.Contains(c.Steps, s => s.Math.StartsWith("2pq × 1,000 = 480 ", StringComparison.Ordinal));
        foreach (var (g, v) in new[] { (HWGiven.P, 0.6), (HWGiven.Q, 0.4), (HWGiven.P2, 0.36), (HWGiven.TwoPQ, 0.48), (HWGiven.RecessivePercent, 16.0), (HWGiven.DominantPercent, 84.0) })
            Assert.Equal(0.6, HardyWeinberg.Calculate(g, v).Calc!.P, 12);
        Assert.Equal("q² must be between 0 and 1.", HardyWeinberg.Calculate(HWGiven.Q2, 1.2).Error);
        Assert.NotNull(HardyWeinberg.Calculate(HWGiven.TwoPQ, 0.6).Error);
    }

    [Fact]
    public void NumbersAreWrittenAsTheMacWritesThem()
    {
        Assert.Equal("1.00", HardyWeinberg.Num(1));
        Assert.Equal("0.4225", HardyWeinberg.Num(0.4225));
        Assert.Equal("2.50 × 10⁻⁵", HardyWeinberg.Num(0.000025));
        Assert.Equal("20", HardyWeinberg.Carriers(19.8));
        // (rounded half-up on the decimal digits, as every bio tool rounds on both apps: a tie goes up, and 3.425 — 3.42499…
        //  in binary — is still 3.43; nothing rounds to "-0.00")
        Assert.Equal("3", HWFormat.Fixed(2.5, 0));
        Assert.Equal("0.13", HWFormat.Fixed(0.125, 2));
        Assert.Equal("3.43", HWFormat.Fixed(3.425, 2));
        Assert.Equal("-0.50", HWFormat.Fixed(-0.5, 2));
        Assert.Equal("0.00", HWFormat.Fixed(-0.001, 2));
        Assert.Equal("0.123457", HWFormat.G(0.1234565));
        Assert.Equal(new[] { 42, 46, 12 }, HardyWeinberg.People(new[] { 0.4225, 0.455, 0.1225 }));
        Assert.Equal(1.0 / 2500, HWInput.Number("1/2500"));
        Assert.Equal(0.16, HWInput.Number("16%"));
    }

    [Fact]
    public void TheEquilibriumTest()
    {
        var t = HardyWeinberg.Test(new HWCounts(50, 30, 20)).Test!;
        Assert.Equal(0.65, t.P, 12);
        Assert.Equal(new[] { 42.25, 45.5, 12.25 }, t.Expected.Select(e => Math.Round(e, 9)));
        Assert.InRange(t.Result.Chi2, 11.60485, 11.60495);
        Assert.InRange(t.Result.P, 0.000655, 0.000665);
        Assert.Equal(1, t.Result.Df);
        Assert.True(t.Result.Reject);
        Assert.Contains("fewer heterozygotes", t.Detail);
        Assert.Equal(5.99, HardyWeinberg.Test(new HWCounts(50, 30, 20), 2).Test!.Result.Critical);
        Assert.Null(HardyWeinberg.Test(new HWCounts(10, 0, 0)).Test);
    }

    [Fact]
    public void TheSimulationsVectors()
    {
        var lethal = HWSimulation.Run(new HWSimParams { Size = null, P0 = 0.5, Waa = 0, Generations = 3 })[0].Generations.Skip(1).Select(g => 1 - g.P).ToList();
        Assert.Equal(3, lethal.Count);
        Assert.Equal(1.0 / 3, lethal[0], 6);
        Assert.Equal(0.25, lethal[1], 6);
        Assert.Equal(0.2, lethal[2], 6);
        static HWGeneration Once(HWSimParams s) => HWSimulation.Run(s with { Size = null, Generations = 1 })[0].Generations[^1];
        Assert.Equal(0.89991, Once(new HWSimParams { P0 = 0.9, U = 1e-4 }).P, 12);
        Assert.Equal(0.26, Once(new HWSimParams { P0 = 0.2, M = 0.1, Pm = 0.8 }).P, 12);
        var f = Once(new HWSimParams { P0 = 0.6, F = 0.25 });
        Assert.Equal(0.42, f.AA, 12);
        Assert.Equal(0.36, f.Aa, 12);
        Assert.Equal(0.22, f.aa, 12);
    }

    [Fact]
    public void DriftFromSeedSevenIsTheMacs()
    {
        var pops = HWSimulation.Run(new HWSimParams { Size = 10, P0 = 0.5, Seed = 7, Replicates = 2, Generations = 5 });
        Assert.Equal(new[] { 10, 10, 12, 13, 12, 10 }, pops[0].Generations.Select(g => g.K!.Value));
        // (replicate 1 draws from seed 8)
        var eight = HWSimulation.Run(new HWSimParams { Size = 10, P0 = 0.5, Seed = 8, Replicates = 1, Generations = 5 })[0];
        Assert.Equal(eight.Generations, pops[1].Generations);
        Assert.Equal(1, HWSimulation.Run(new HWSimParams { Size = 50, WAA = 0, WAa = 0, Waa = 0, Generations = 10 })[0].ExtinctAt);
    }

    [Fact]
    public void TheFiveConditions()
    {
        Assert.All(HWSimulation.Assumptions(new HWSimParams { Size = null }), a => Assert.True(a.Holds));
        Assert.Equal(new[] { "size" }, HWSimulation.Assumptions(new HWSimParams { Size = 100 }).Where(a => !a.Holds).Select(a => a.Id));
        Assert.Contains("allele frequencies stay the same", HWSimulation.Verdict(new HWSimParams { Size = null, F = 0.3 }));
        Assert.Equal(HWSelection.RecessiveLethal, HWSelections.Matching(new HWSimParams { Waa = 0 }));
        var s = new HWSimParams { Size = 250, Seed = 9, U = 0.001 };
        Assert.Equal(s, HWSimParams.Decode(s.Encoded));
    }
}
