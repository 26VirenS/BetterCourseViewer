using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Molar Mass &amp; Stoichiometry's sums (Tools/Chemistry/Model/ChemStoich.cs, on ChemMolarMass and ChemBalancer): an
/// amount converted every way, a reaction's limiting reagent, yields, what is left and the percent yield, and an
/// empirical formula with its molecular one — the tools' spec's test vectors.
/// </summary>
public class MolesTests
{
    [Fact]
    public void TenGramsOfWaterEveryWay()
    {
        var r = ChemStoich.Convert(ChemFormula.Parse("H2O")!, 10, AmountKind.Grams);
        Assert.InRange(r.Moles, 0.5550, 0.5552);
        Assert.InRange(r.Particles, 3.342e23, 3.344e23);
        Assert.InRange(r.Litres, 12.43, 12.44);
        Assert.Equal("0.5551", ChemNum.Sig(r.Moles));
        Assert.Equal("3.343e23", ChemNum.Sig(r.Particles));
        Assert.Equal("12.43", ChemNum.Sig(r.Litres));
        // (and back from each of the others)
        Assert.InRange(ChemStoich.Convert(ChemFormula.Parse("H2O")!, r.Moles, AmountKind.Moles).Grams, 9.999, 10.001);
        Assert.InRange(ChemStoich.Convert(ChemFormula.Parse("H2O")!, r.Particles, AmountKind.Particles).Grams, 9.999, 10.001);
        Assert.InRange(ChemStoich.Convert(ChemFormula.Parse("H2O")!, r.Litres, AmountKind.LitresStp).Grams, 9.999, 10.001);
    }

    [Fact]
    public void TheLimitingReagentTheYieldsAndWhatIsLeft()
    {
        var balanced = ChemBalancer.Balance("2H2 + O2 -> 2H2O");
        var r = ChemStoich.React(balanced, new[] { new GivenAmount(4, true), new GivenAmount(16, true) }, actualProduct: 0, actualGrams: 15);
        Assert.Null(r.Message);
        Assert.Equal("O2", r.Limiting?.Text);
        Assert.InRange(r.Extent, 0.4999, 0.5001);
        var h2 = r.Rows[0];
        var o2 = r.Rows[1];
        var water = r.Rows[2];
        Assert.False(h2.Limiting);
        Assert.True(o2.Limiting);
        Assert.InRange(h2.GivenMoles!.Value, 1.983, 1.985);
        Assert.InRange(h2.UsedOrMadeMoles, 0.9999, 1.0001);
        Assert.InRange(h2.LeftMoles!.Value, 0.983, 0.985);
        Assert.InRange(h2.LeftGrams!.Value, 1.98, 1.99);
        Assert.InRange(o2.GivenMoles!.Value, 0.4999, 0.5001);
        Assert.InRange(o2.LeftMoles!.Value, 0, 1e-9);
        // (16 g of O2 is 0.50003 mol, so the yield is a hair over 1 mol: 18.02 g, shown as the spec's 18.015 to four figures)
        Assert.InRange(water.UsedOrMadeMoles, 0.9999, 1.0001);
        Assert.InRange(water.UsedOrMadeGrams, 18.01, 18.02);
        Assert.Equal("18.02", ChemNum.Sig(water.UsedOrMadeGrams));
        Assert.InRange(r.PercentYield!.Value, 83.25, 83.27);
        Assert.Equal("83.3", ChemNum.Sig(r.PercentYield.Value, 3));
    }

    [Fact]
    public void AReactantWithNothingGivenIsInExcess()
    {
        var balanced = ChemBalancer.Balance("2H2 + O2 -> 2H2O");
        var r = ChemStoich.React(balanced, new[] { new GivenAmount(null, true), new GivenAmount(1, false) });
        Assert.Equal("O2", r.Limiting?.Text);
        Assert.Null(r.Rows[0].GivenMoles);
        Assert.Null(r.Rows[0].LeftMoles);
        Assert.InRange(r.Rows[2].UsedOrMadeMoles, 1.999, 2.001);
        var none = ChemStoich.React(balanced, new[] { new GivenAmount(null, true), new GivenAmount(null, true) });
        Assert.Null(none.Limiting);
        Assert.Equal("Give the amount of at least one reactant.", none.Message);
    }

    [Fact]
    public void TheEmpiricalFormulaOfGlucoseAndItsMolecularOne()
    {
        var r = ChemStoich.Empirical(new[] { ("C", 40.0), ("H", 6.71), ("O", 53.3) }, 180, out var bad);
        Assert.True(r != null, bad);
        Assert.Equal("CH2O", r!.Formula);
        Assert.Equal(1, r.Multiplier);
        Assert.False(r.Approximate);
        Assert.InRange(r.FormulaMass, 30.02, 30.03);
        Assert.Equal("C6H12O6", r.Molecular);
        Assert.Equal(6, r.MolecularMultiplier);
        Assert.Equal(new[] { 1, 2, 1 }, r.Rows.Select(x => x.Whole));
        Assert.Null(ChemStoich.Empirical(new[] { ("C", 40.0), ("H", 6.71), ("O", 53.3) }, null, out _)!.Molecular);
    }

    [Fact]
    public void RatiosThatNeedAMultiplierGetOne()
    {
        // (iron(III) oxide: Fe 69.94 %, O 30.06 % — 1 : 1.5, so times 2)
        var r = ChemStoich.Empirical(new[] { ("fe", 69.94), ("o", 30.06) }, null, out _)!;
        Assert.Equal("Fe2O3", r.Formula);
        Assert.Equal(2, r.Multiplier);
        Assert.False(r.Approximate);
        Assert.Null(ChemStoich.Empirical(new[] { ("Xx", 1.0) }, null, out var bad));
        Assert.Equal("“Xx” is not an element", bad);
        Assert.Null(ChemStoich.Empirical(new[] { ("C", 0.0) }, null, out bad));
        Assert.Contains("above zero", bad);
    }
}
