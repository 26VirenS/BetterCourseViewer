using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Equation Balancer's reader and solver (Tools/Chemistry/Model/ChemFormula.cs, ChemBalancer.cs, ChemMolarMass.cs,
/// ChemNum.cs): formulas with brackets, hydrates and charges read into their atoms; the equations of the tools' spec
/// balanced to their known coefficients; the ones that cannot be, said why; molar masses to the hundredth; numbers
/// written without float noise.
/// </summary>
public class BalancerTests
{
    [Theory]
    [InlineData("H2O", "H2,O1", 0)]
    [InlineData("Ca(OH)2", "Ca1,O2,H2", 0)]
    [InlineData("CuSO4·5H2O", "Cu1,S1,O9,H10", 0)]
    [InlineData("CuSO4*5H2O", "Cu1,S1,O9,H10", 0)]
    [InlineData("CuSO4.5H2O", "Cu1,S1,O9,H10", 0)]
    [InlineData("Fe^3+", "Fe1", 3)]
    [InlineData("Fe3+", "Fe1", 3)]
    [InlineData("SO4^2-", "S1,O4", -2)]
    [InlineData("MnO4-", "Mn1,O4", -1)]
    [InlineData("NH4+", "N1,H4", 1)]
    [InlineData("Cu2+", "Cu1", 2)]
    [InlineData("Na^+", "Na1", 1)]
    [InlineData("PO4^{3-}", "P1,O4", -3)]
    [InlineData("H+", "H1", 1)]
    [InlineData("Al2(SO4)3", "Al2,S3,O12", 0)]
    [InlineData("K4[Fe(CN)6]", "K4,Fe1,C6,N6", 0)]
    [InlineData("2H2O(l)", "H2,O1", 0)]
    [InlineData("NaCl (aq)", "Na1,Cl1", 0)]
    public void AFormulaIsReadIntoItsAtomsAndCharge(string typed, string atoms, int charge)
    {
        var f = ChemFormula.Parse(typed, out var bad);
        Assert.True(f != null, $"could not read {typed}: {bad}");
        Assert.Equal(atoms, string.Join(",", f!.Atoms.Select(a => a.Symbol + a.Count)));
        Assert.Equal(charge, f.Charge);
    }

    [Fact]
    public void TheCoefficientAndThePhaseAreSetAside()
    {
        var f = ChemFormula.Parse("2H2O(l)")!;
        Assert.Equal(2, f.Coefficient);
        Assert.Equal("l", f.Phase);
        Assert.Equal("H2O", f.Text);
        Assert.Equal("aq", ChemFormula.Parse("NaCl (aq)")!.Phase);
    }

    [Theory]
    [InlineData("XyZ", "Xy")]
    [InlineData("Ca(OH", "(OH")]
    [InlineData("hello", "hello")]
    [InlineData("", "")]
    [InlineData("3+", "3+")]
    public void WhatCannotBeReadIsNamed(string typed, string bad)
    {
        Assert.Null(ChemFormula.Parse(typed, out var reason));
        Assert.Equal(bad, reason);
    }

    [Fact]
    public void AFormulaIsTypesetWithSubscriptsAndItsCharge()
    {
        Assert.Equal("H₂O", ChemFormula.Parse("H2O")!.Pretty);
        Assert.Equal("Ca(OH)₂", ChemFormula.Parse("Ca(OH)2")!.Pretty);
        Assert.Equal("SO₄²⁻", ChemFormula.Parse("SO4^2-")!.Pretty);
        Assert.Equal("Fe³⁺", ChemFormula.Parse("Fe3+")!.Pretty);
        Assert.Equal("H⁺", ChemFormula.Parse("H+")!.Pretty);
        Assert.Equal("MnO₄⁻", ChemFormula.Parse("MnO4-")!.Pretty);
        Assert.Equal("NH₄⁺", ChemFormula.Parse("NH4+")!.Pretty);
        Assert.Equal("CuSO₄·5H₂O", ChemFormula.Parse("CuSO4*5H2O")!.Pretty);
        var runs = ChemFormula.Parse("Ca(OH)2")!.Runs;
        Assert.Equal(2, runs.Count);
        Assert.True(runs[1].Sub);
        Assert.Equal("2", runs[1].Text);
    }

    [Fact]
    public void ASideSplitsAtThePlusBetweenSpeciesNeverAtAnIonsOwn()
    {
        Assert.Equal(new[] { "H2", "O2" }, ChemBalancer.Species("H2 + O2"));
        Assert.Equal(new[] { "H2", "O2" }, ChemBalancer.Species("H2+O2"));
        Assert.Equal(new[] { "Cu", "Ag+" }, ChemBalancer.Species("Cu + Ag+"));
        Assert.Equal(new[] { "MnO4-", "Fe2+", "H+" }, ChemBalancer.Species("MnO4- + Fe2+ + H+"));
        Assert.Equal(new[] { "2H2O" }, ChemBalancer.Species("2 H2O"));
        Assert.Equal(new[] { "H+", "OH-" }, ChemBalancer.Species("H+ + OH-"));
    }

    [Theory]
    [InlineData("H2 + O2 -> H2O", "2,1,2")]
    [InlineData("Fe + O2 -> Fe2O3", "4,3,2")]
    [InlineData("C3H8 + O2 -> CO2 + H2O", "1,5,3,4")]
    [InlineData("Al + HCl -> AlCl3 + H2", "2,6,2,3")]
    [InlineData("KMnO4 + HCl -> KCl + MnCl2 + H2O + Cl2", "2,16,2,2,8,5")]
    [InlineData("Ca(OH)2 + H3PO4 -> Ca3(PO4)2 + H2O", "3,2,1,6")]
    [InlineData("CuSO4·5H2O -> CuSO4 + H2O", "1,1,5")]
    [InlineData("Cu + Ag+ -> Cu2+ + Ag", "1,2,1,2")]
    [InlineData("MnO4- + Fe2+ + H+ -> Mn2+ + Fe3+ + H2O", "1,5,8,1,5,4")]
    [InlineData("2H2 + O2 = 2H2O", "2,1,2")]
    [InlineData("H2(g) + O2(g) → H2O(l)", "2,1,2")]
    [InlineData("Fe + O2 ⟶ Fe2O3", "4,3,2")]
    public void TheSpecsEquationsBalanceToTheirCoefficients(string equation, string coefficients)
    {
        var r = ChemBalancer.Balance(equation);
        Assert.True(r.Ok, $"{equation}: {r.Outcome} {r.Message}");
        Assert.Equal(coefficients, string.Join(",", r.Coefficients));
        Assert.All(r.Rows, row => Assert.True(row.Equal, $"{row.Name}: {row.Left} vs {row.Right}"));
        Assert.Equal(r.LeftMass, r.RightMass, 6);
    }

    [Fact]
    public void TheBalancedEquationReadsAsPlainText()
    {
        var r = ChemBalancer.Balance("H2 + O2 -> H2O");
        Assert.Equal("2H2 + O2 → 2H2O", r.PlainText);
        Assert.Equal("2H₂ + O₂ → 2H₂O", r.PrettyText);
        // (the count table: hydrogen and oxygen equal each side, no charge row)
        Assert.Equal(new[] { "H", "O" }, r.Rows.Select(x => x.Name));
        Assert.Equal(4, r.Rows[0].Left);
        Assert.Equal(4, r.Rows[0].Right);
        var ionic = ChemBalancer.Balance("Cu + Ag+ -> Cu2+ + Ag");
        Assert.Equal("Charge", ionic.Rows[^1].Name);
        Assert.Equal(2, ionic.Rows[^1].Left);
        Assert.Equal(2, ionic.Rows[^1].Right);
    }

    [Fact]
    public void TwoReactionsWrittenAsOneAreSaidToBeMoreThanOneWay()
    {
        var r = ChemBalancer.Balance("H2 + O2 -> H2O2 + H2O");
        Assert.Equal(BalanceOutcome.Ambiguous, r.Outcome);
        Assert.StartsWith("More than one way", r.Message);
    }

    [Fact]
    public void AnElementOnOneSideOnlyCannotBalanceAndIsNamed()
    {
        var r = ChemBalancer.Balance("Na + Cl2 -> NaBr");
        Assert.Equal(BalanceOutcome.Impossible, r.Outcome);
        Assert.Equal(new[] { "Cl", "Br" }, r.OneSided);
        Assert.Contains("Chlorine (Cl)", r.Message);
        Assert.Contains("Bromine (Br)", r.Message);
        Assert.StartsWith("This can’t be balanced", r.Message);
    }

    [Fact]
    public void AnUnreadableFormulaIsNamed()
    {
        var r = ChemBalancer.Balance("H2 + XyZ -> H2O");
        Assert.Equal(BalanceOutcome.Unreadable, r.Outcome);
        Assert.Equal("Xy", r.BadToken);
        Assert.Equal("Can’t read “Xy” — is it a real formula?", r.Message);
    }

    [Fact]
    public void NoArrowOrAnEmptySideIsSaid()
    {
        Assert.Equal(BalanceOutcome.Malformed, ChemBalancer.Balance("H2 + O2").Outcome);
        Assert.Equal(BalanceOutcome.Malformed, ChemBalancer.Balance("H2 + O2 ->").Outcome);
        Assert.Equal(BalanceOutcome.Empty, ChemBalancer.Balance("   ").Outcome);
    }

    [Theory]
    [InlineData("H2O", 18.015)]
    [InlineData("CO2", 44.009)]
    [InlineData("Ca(OH)2", 74.093)]
    [InlineData("C6H12O6", 180.156)]
    [InlineData("CuSO4·5H2O", 249.685)]
    [InlineData("NaCl", 58.443)]
    public void MolarMassesToTheHundredth(string formula, double expected)
    {
        var r = ChemMolarMass.Of(formula, out var bad);
        Assert.True(r != null, bad);
        Assert.InRange(r!.Total, expected - 0.01, expected + 0.01);
        Assert.InRange(r.Rows.Sum(x => x.Percent), 99.99, 100.01);
    }

    [Fact]
    public void TheBreakdownHasEachElementOnce()
    {
        var r = ChemMolarMass.Of("Ca(OH)2", out _)!;
        Assert.Equal(new[] { "Ca", "O", "H" }, r.Rows.Select(x => x.Symbol));
        Assert.Equal(new[] { 1, 2, 2 }, r.Rows.Select(x => x.Count));
        Assert.Equal("Calcium", r.Rows[0].Name);
        Assert.InRange(r.Rows[0].Percent, 54.08, 54.10);
    }

    [Fact]
    public void TheMassCheckAgreesOnBothSides()
    {
        var r = ChemBalancer.Balance("C3H8 + O2 -> CO2 + H2O");
        Assert.InRange(r.LeftMass, 204.0, 204.2);
        Assert.Equal(r.LeftMass, r.RightMass, 9);
    }

    [Theory]
    [InlineData(0.1 + 0.2, 4, "0.3")]
    [InlineData(18.015, 5, "18.015")]
    [InlineData(0.55507, 4, "0.5551")]
    [InlineData(3.343e23, 4, "3.343e23")]
    [InlineData(1e-11, 4, "1e-11")]
    [InlineData(4.9234, 3, "4.92")]
    [InlineData(0, 4, "0")]
    [InlineData(-2.5, 4, "-2.5")]
    [InlineData(1234567, 4, "1235000")]
    public void NumbersAreWrittenToTheirFiguresWithoutNoise(double v, int figures, string text) => Assert.Equal(text, ChemNum.Sig(v, figures));

    [Fact]
    public void NumbersAreReadAsTyped()
    {
        Assert.Equal(1000, ChemNum.Read("1,000"));
        Assert.Equal(-3.5, ChemNum.Read("−3.5"));
        Assert.Equal(6.02e23, ChemNum.Read("6.02e23"));
        Assert.Null(ChemNum.Read(""));
        Assert.Null(ChemNum.Read("abc"));
        Assert.Equal("7.00", ChemNum.Fixed(7.0001, 2));
        Assert.Equal("0.00", ChemNum.Fixed(-0.00001, 2));
        Assert.Equal(3, ChemNum.FiguresOf("0.0820"));
        Assert.Equal(5, ChemNum.FiguresOf("273.15"));
    }

    [Fact]
    public void FractionsStayExactAndReduced()
    {
        var half = ChemFraction.Of(2, 4);
        Assert.Equal(new ChemFraction(1, 2), half);
        Assert.Equal(new ChemFraction(5, 6), half + ChemFraction.Of(1, 3));
        Assert.Equal(new ChemFraction(1, 6), half * ChemFraction.Of(1, 3));
        Assert.Equal(new ChemFraction(3, 2), half / ChemFraction.Of(1, 3));
        Assert.Equal(new ChemFraction(-1, 2), ChemFraction.Of(1, -2));
        Assert.Throws<OverflowException>(() => ChemFraction.Of(long.MaxValue, 1) * ChemFraction.Of(2, 1));
    }
}
