using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Solutions &amp; pH's sums (Tools/Chemistry/Model/ChemSolution.cs): pH, pOH, [H+] and [OH-] from any one; strong and
/// weak acids and bases from their concentration (the weak by the exact quadratic); molarity and dilution; a titration's
/// equivalence point, its pH at the points that matter and its curve; a buffer — the tools' spec's test vectors.
/// </summary>
public class SolutionsTests
{
    [Fact]
    public void AMillimolarAcidIsPhThree()
    {
        var s = ChemPh.From(PhGiven.H, 1e-3)!;
        Assert.Equal(3.00, s.PH, 2);
        Assert.Equal(11.00, s.POH, 2);
        Assert.InRange(s.OH, 0.99e-11, 1.01e-11);
        Assert.Equal("1e-11", ChemNum.Sig(s.OH));
        // (and from each of the others)
        Assert.InRange(ChemPh.From(PhGiven.PH, 3)!.H, 0.999e-3, 1.001e-3);
        Assert.Equal(3.00, ChemPh.From(PhGiven.POH, 11)!.PH, 2);
        Assert.Equal(3.00, ChemPh.From(PhGiven.OH, 1e-11)!.PH, 2);
        Assert.Null(ChemPh.From(PhGiven.H, 0));
        Assert.Null(ChemPh.From(PhGiven.OH, -1));
    }

    [Fact]
    public void AceticAcidByTheExactQuadratic()
    {
        var r = ChemPh.OfSolution(AcidBaseKind.WeakAcid, 0.10, 1.8e-5)!;
        Assert.InRange(r.Set.PH, 2.871, 2.881);
        Assert.Equal("2.88", ChemNum.Fixed(r.Set.PH, 2));
        Assert.InRange(r.PercentIonised!.Value, 1.32, 1.34);
        // (the 5 % approximation would say 2.87: the quadratic is asked for)
        Assert.InRange(r.Ionised!.Value, 1.332e-3, 1.334e-3);
        var ammonia = ChemPh.OfSolution(AcidBaseKind.WeakBase, 0.10, 1.8e-5)!;
        Assert.InRange(ammonia.Set.PH, 11.12, 11.13);
        Assert.Null(ChemPh.OfSolution(AcidBaseKind.WeakAcid, 0.10, null));
    }

    [Fact]
    public void StrongAcidsAndBasesAreWhollyIonised()
    {
        Assert.Equal(1.00, ChemPh.OfSolution(AcidBaseKind.StrongAcid, 0.1, null)!.Set.PH, 2);
        Assert.Equal(12.70, ChemPh.OfSolution(AcidBaseKind.StrongBase, 0.05, null)!.Set.PH, 2);
        // (a very dilute acid is still an acid: water's own ions keep it under 7)
        Assert.InRange(ChemPh.OfSolution(AcidBaseKind.StrongAcid, 1e-8, null)!.Set.PH, 6.95, 7.0);
        Assert.Null(ChemPh.OfSolution(AcidBaseKind.StrongAcid, 0, null));
    }

    [Fact]
    public void DilutingTwoMolarToHalfMolarNeedsFourTimesTheVolume()
    {
        var (solved, value) = ChemDilution.Solve(2, 50, 0.5, null, out var message)!.Value;
        Assert.Null(message);
        Assert.Equal(3, solved);
        Assert.Equal(200, value, 6);
        Assert.Equal(50, ChemDilution.Solve(2, null, 0.5, 200, out _)!.Value.Value, 6);
        Assert.Equal(0.5, ChemDilution.Solve(2, 50, null, 200, out _)!.Value.Value, 6);
        Assert.Equal(2, ChemDilution.Solve(null, 50, 0.5, 200, out _)!.Value.Value, 6);
        Assert.Null(ChemDilution.Solve(2, null, null, 200, out message));
        Assert.Equal("Fill in all but one.", message);
        // (stock weaker than the target: said)
        ChemDilution.Solve(0.5, null, 2, 200, out message);
        Assert.Contains("weaker", message);
    }

    [Fact]
    public void MolarityFromAnyTwo()
    {
        var r = ChemMolarity.Solve(null, 58.44, ChemFormula.Parse("NaCl"), 1, null, 1.0, out var message)!;
        Assert.Null(message);
        Assert.Equal("molarity", r.Solved);
        Assert.InRange(r.Molarity, 0.9999, 1.0001);
        Assert.InRange(r.MassPercent!.Value, 5.84, 5.85);
        Assert.InRange(r.Ppm!.Value, 58430, 58450);
        Assert.Equal(0.25, ChemMolarity.Solve(0.5, null, null, 2, null, 1, out _)!.Molarity, 6);
        Assert.Equal(2, ChemMolarity.Solve(0.5, null, null, null, 0.25, 1, out _)!.VolumeL, 6);
        Assert.Equal(0.5, ChemMolarity.Solve(null, null, null, 2, 0.25, 1, out _)!.Moles, 6);
        Assert.Null(ChemMolarity.Solve(null, 10, null, 1, null, 1, out message));
        Assert.Equal("Give the solute’s formula to use grams.", message);
    }

    [Fact]
    public void HydrochloricAcidAgainstSodiumHydroxide()
    {
        var r = ChemTitration.Run(TitrationKind.StrongAcidStrongBase, 0.1, 25, 0.1, null, out var message)!;
        Assert.Null(message);
        Assert.Equal(25, r.EquivalenceMl, 6);
        Assert.Equal(7.00, r.EquivalencePh, 2);
        Assert.Equal(1.00, r.StartPh, 2);
        Assert.InRange(r.AfterPh, 12.5, 12.55);
        Assert.Equal(ChemTitration.CurvePoints, r.Curve.Count);
        Assert.Equal(0, r.Curve[0].Ml, 6);
        Assert.Equal(50, r.Curve[^1].Ml, 6);
        // (the curve only rises)
        for (var i = 1; i < r.Curve.Count; i++) Assert.True(r.Curve[i].Ph >= r.Curve[i - 1].Ph - 1e-9);
    }

    [Fact]
    public void AceticAcidAgainstSodiumHydroxide()
    {
        var r = ChemTitration.Run(TitrationKind.WeakAcidStrongBase, 0.1, 25, 0.1, 1.8e-5, out _)!;
        Assert.Equal(25, r.EquivalenceMl, 6);
        Assert.InRange(r.HalfPh, 4.73, 4.75);
        Assert.InRange(r.PK!.Value, 4.74, 4.75);
        Assert.InRange(r.EquivalencePh, 8.71, 8.73);
        Assert.InRange(r.StartPh, 2.87, 2.88);
        Assert.Null(ChemTitration.Run(TitrationKind.WeakAcidStrongBase, 0.1, 25, 0.1, null, out var message));
        Assert.Equal("Give the acid’s Ka.", message);
        // (ammonia against hydrochloric acid: the mirror image, equivalence below 7)
        var ammonia = ChemTitration.Run(TitrationKind.WeakBaseStrongAcid, 0.1, 25, 0.1, 1.8e-5, out _)!;
        Assert.InRange(ammonia.StartPh, 11.12, 11.13);
        Assert.InRange(ammonia.HalfPh, 9.25, 9.27);
        Assert.InRange(ammonia.EquivalencePh, 5.27, 5.29);
        for (var i = 1; i < ammonia.Curve.Count; i++) Assert.True(ammonia.Curve[i].Ph <= ammonia.Curve[i - 1].Ph + 1e-9);
    }

    [Fact]
    public void ABufferByHendersonHasselbalch()
    {
        Assert.Equal(4.74, ChemPh.Buffer(4.74, 0.1, 0.1)!.Value, 6);
        Assert.InRange(ChemPh.Buffer(4.74, 0.2, 0.1)!.Value, 5.04, 5.05);
        Assert.Null(ChemPh.Buffer(4.74, 0, 0.1));
    }
}
