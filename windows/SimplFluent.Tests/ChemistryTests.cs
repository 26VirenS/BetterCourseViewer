using System.Windows;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>The elements' table and the Lewis maker's chemistry (Tools/Chemistry): counts, formulas, shapes, electrons.</summary>
public class ChemistryTests
{
    public ChemistryTests() => _ = Profile.Folder;

    [Fact]
    public void EveryElementOnceInOrder()
    {
        var all = ChemElements.All;
        Assert.Equal(118, all.Count);
        Assert.Equal(Enumerable.Range(1, 118), all.Select(e => e.Number));
        Assert.Equal(118, all.Select(e => e.Symbol).Distinct().Count());
        Assert.Equal("Iron", ChemElements.Of("Fe")?.Name);
        Assert.Equal(26, ChemElements.Of("Fe")?.Number);
        Assert.Null(ChemElements.Of("Xx"));
        // (each period's last element is a noble gas, in group 18)
        foreach (var n in new[] { 2, 10, 18, 36, 54, 86, 118 }) Assert.Equal(18, all[n - 1].Group);
    }

    [Fact]
    public void MassesRiseThroughTheTableBarAFewKnownPairs()
    {
        // (argon and potassium, cobalt and nickel, tellurium and iodine are the famous inversions; nothing else falls)
        var inversions = ChemElements.All.Zip(ChemElements.All.Skip(1)).Where(p => p.Second.Mass > 0 && p.First.Mass > p.Second.Mass)
            .Select(p => p.First.Symbol + ">" + p.Second.Symbol).ToHashSet();
        foreach (var known in new[] { "Ar>K", "Co>Ni", "Te>I" }) Assert.Contains(known, inversions);
        Assert.True(inversions.Count <= 8, "masses out of order: " + string.Join(", ", inversions));
    }

    [Theory]
    [InlineData("H", 1)]
    [InlineData("He", 2)]
    [InlineData("C", 4)]
    [InlineData("N", 5)]
    [InlineData("O", 6)]
    [InlineData("Cl", 7)]
    [InlineData("Na", 1)]
    [InlineData("Mg", 2)]
    [InlineData("Al", 3)]
    public void ValenceElectrons(string symbol, int valence) => Assert.Equal(valence, LChem.Valence(symbol));

    [Fact]
    public void FormulasInTheConventionalOrder()
    {
        Assert.Equal("CH₄", LChem.Formula(new[] { "H", "C", "H", "H", "H" }, 0));
        Assert.Equal("H₂O", LChem.Formula(new[] { "O", "H", "H" }, 0));
        Assert.Equal("NH₃", LChem.Formula(new[] { "H", "N", "H", "H" }, 0));
        Assert.Equal("NH₄⁺", LChem.Formula(new[] { "N", "H", "H", "H", "H" }, 1));
        Assert.Equal("CO₃²⁻", LChem.Formula(new[] { "O", "C", "O", "O" }, -2));
        Assert.Equal("", LChem.ChargeText(0));
        Assert.Equal("⁻", LChem.ChargeText(-1));
        Assert.Equal("³⁺", LChem.ChargeText(3));
    }

    [Theory]
    [InlineData(2, 0, "Linear", "180°")]
    [InlineData(2, 2, "Bent", "about 104.5°")]
    [InlineData(3, 0, "Trigonal planar", "120°")]
    [InlineData(3, 1, "Trigonal pyramidal", "about 107°")]
    [InlineData(4, 0, "Tetrahedral", "109.5°")]
    [InlineData(6, 0, "Octahedral", "90°")]
    public void ShapesByVsepr(int bonded, int lone, string name, string angle) => Assert.Equal((name, angle), LChem.Shape(bonded, lone));

    /// <summary>A central atom with atoms round it, as the quick way builds one.</summary>
    private static (LGraph Graph, int Centre, int[] Around) Star(string centre, string[] around, int charge = 0)
    {
        var g = new LGraph { Charge = charge };
        var c = g.Add(centre, new Point(0, 0));
        var ids = around.Select((s, k) => g.Add(s, new Point(60 * Math.Cos(k), 60 * Math.Sin(k)))).ToArray();
        foreach (var id in ids) g.Connect(c, id);
        return (g, c, ids);
    }

    [Fact]
    public void WaterHasTwoLonePairsOnItsOxygen()
    {
        var (g, o, hs) = Star("O", new[] { "H", "H" });
        var r = LewisSolver.Solve(g, LewisSolver.Mode.Formal);
        Assert.Equal(8, r.TotalElectrons);
        var s = Assert.IsType<LStructure>(r.Major);
        Assert.Equal(4, s.LoneOf(o));
        Assert.Equal(8, s.Electrons(g, o));
        foreach (var h in hs) Assert.Equal(2, s.Electrons(g, h));
        Assert.All(s.Formal.Values, f => Assert.Equal(0, f));
        Assert.Equal((2, 2), LGeometry.Domains(g, s, o));
    }

    [Fact]
    public void CarbonDioxideHasTwoDoubleBonds()
    {
        var (g, c, os) = Star("C", new[] { "O", "O" });
        var r = LewisSolver.Solve(g, LewisSolver.Mode.Formal);
        Assert.Equal(16, r.TotalElectrons);
        var s = Assert.IsType<LStructure>(r.Major);
        foreach (var b in g.Bonds) Assert.Equal(2, s.Order(b.Id));
        Assert.All(s.Formal.Values, f => Assert.Equal(0, f));
        foreach (var id in os.Append(c)) Assert.Equal(8, s.Electrons(g, id));
        Assert.Equal((2, 0), LGeometry.Domains(g, s, c));
    }

    [Fact]
    public void NitrateHasThreeEquivalentResonanceStructures()
    {
        var (g, n, os) = Star("N", new[] { "O", "O", "O" }, charge: -1);
        var r = LewisSolver.Solve(g, LewisSolver.Mode.Formal);
        Assert.Equal(24, r.TotalElectrons);
        Assert.Equal(3, r.Equivalent);
        // (each N–O bond one and a third on average: one double bond shared among three)
        foreach (var b in g.Bonds) Assert.Equal(4.0 / 3, r.AverageOrder[b.Id], 6);
        var s = Assert.IsType<LStructure>(r.Major);
        Assert.Equal(-1, s.Formal.Values.Sum());
        Assert.Equal(8, s.Electrons(g, n));
    }

    [Fact]
    public void AmmoniumCarriesItsCharge()
    {
        var (g, n, _) = Star("N", new[] { "H", "H", "H", "H" }, charge: 1);
        var r = LewisSolver.Solve(g, LewisSolver.Mode.Formal);
        Assert.Equal(8, r.TotalElectrons);
        var s = Assert.IsType<LStructure>(r.Major);
        Assert.Equal(0, s.LoneOf(n));
        Assert.Equal(1, s.Formal.Values.Sum());
        Assert.Equal((4, 0), LGeometry.Domains(g, s, n));
    }

    [Fact]
    public void ATransitionMetalIsSaidNotDrawn()
    {
        var (g, _, _) = Star("Fe", new[] { "Cl", "Cl", "Cl" });
        var r = LewisSolver.Solve(g, LewisSolver.Mode.Formal);
        Assert.False(r.Drawable);
        Assert.NotEmpty(r.Notes);
    }

    [Fact]
    public void EveryPresetIsFoundByItsName()
    {
        Assert.NotEmpty(LewisPresets.All);
        foreach (var p in LewisPresets.All) Assert.Same(p, LewisPresets.Named(p.Name));
    }
}
