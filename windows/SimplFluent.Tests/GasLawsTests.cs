using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Gas Laws' sums (Tools/Chemistry/Model/ChemGas.cs): PV = nRT with any one blank, in any unit; the combined law with
/// one of its six blank or a pair held constant; the pressure and temperature units — the tools' spec's test vectors.
/// </summary>
public class GasLawsTests
{
    private static GasValue G(double? v, string unit) => new(v, unit);

    [Fact]
    public void OneMoleAtStpFillsTwentyTwoPointFourLitres()
    {
        // (the spec's 0.9996 came from a rounder R; with R = 0.082057 it is 0.9994 — about 1.00 mol either way)
        var r = ChemGas.Ideal(G(1, "atm"), G(22.4, "L"), G(null, "mol"), G(273.15, "K"), null);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(GasVar.N, r.Solved);
        Assert.InRange(r.Value, 0.9990, 1.0000);
        Assert.Equal("0.9994", ChemNum.Sig(r.Value, 4));
        Assert.Equal("1", ChemNum.Sig(r.Value, 2));
        Assert.Equal("mol", r.Unit);
    }

    [Fact]
    public void TwoMolesAtThreeHundredKelvinInTenLitres()
    {
        var r = ChemGas.Ideal(G(null, "atm"), G(10, "L"), G(2, "mol"), G(300, "K"), null);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(GasVar.P, r.Solved);
        Assert.InRange(r.Value, 4.92, 4.93);
        Assert.Equal("4.92", ChemNum.Sig(r.Value, 3));
        // (the same in kPa and in °C)
        var kpa = ChemGas.Ideal(G(null, "kPa"), G(10000, "mL"), G(2, "mol"), G(26.85, "°C"), null);
        Assert.InRange(kpa.Value, 498.7, 499.0);
        var t = ChemGas.Ideal(G(4.92342, "atm"), G(10, "L"), G(2, "mol"), G(null, "°C"), null);
        Assert.Equal(GasVar.T, t.Solved);
        Assert.InRange(t.Value, 26.84, 26.86);
        var v = ChemGas.Ideal(G(4.92342, "atm"), G(null, "L"), G(2, "mol"), G(300, "K"), null);
        Assert.InRange(v.Value, 9.999, 10.001);
    }

    [Fact]
    public void GramsNeedTheGasFormula()
    {
        var none = ChemGas.Ideal(G(1, "atm"), G(22.4, "L"), G(null, "g"), G(273.15, "K"), null);
        Assert.False(none.Ok);
        Assert.Equal("Give the gas’s formula to use grams.", none.Message);
        var co2 = ChemGas.Ideal(G(1, "atm"), G(22.4, "L"), G(null, "g"), G(273.15, "K"), ChemFormula.Parse("CO2"));
        Assert.True(co2.Ok);
        Assert.InRange(co2.Value, 43.97, 44.0);
        Assert.Equal("g", co2.Unit);
        var moles = ChemGas.Ideal(G(null, "atm"), G(10, "L"), G(88.018, "g"), G(300, "K"), ChemFormula.Parse("CO2"));
        Assert.InRange(moles.Value, 4.92, 4.93);
    }

    [Fact]
    public void ExactlyOneBlankIsWanted()
    {
        Assert.Equal("Fill in all but one.", ChemGas.Ideal(G(1, "atm"), G(null, "L"), G(null, "mol"), G(273.15, "K"), null).Message);
        Assert.Equal("Leave exactly one blank to solve it.", ChemGas.Ideal(G(1, "atm"), G(22.4, "L"), G(1, "mol"), G(273.15, "K"), null).Message);
        Assert.Contains("absolute zero", ChemGas.Ideal(G(1, "atm"), G(22.4, "L"), G(null, "mol"), G(-300, "°C"), null).Message);
        Assert.Equal("Volume must be above zero.", ChemGas.Ideal(G(1, "atm"), G(0, "L"), G(null, "mol"), G(300, "K"), null).Message);
    }

    [Fact]
    public void TheCombinedLawSolvesTheSecondVolume()
    {
        var r = ChemGas.Combined(new[] { G(1, "atm"), G(2, "L"), G(300, "K"), G(2, "atm"), G(null, "L"), G(600, "K") }, null);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(4, r.Solved);
        Assert.InRange(r.Value, 1.999, 2.001);
        Assert.Equal("L", r.Unit);
        // (each of the other five, from the same six)
        Assert.InRange(ChemGas.Combined(new[] { G(null, "atm"), G(2, "L"), G(300, "K"), G(2, "atm"), G(2, "L"), G(600, "K") }, null).Value, 0.999, 1.001);
        Assert.InRange(ChemGas.Combined(new[] { G(1, "atm"), G(null, "L"), G(300, "K"), G(2, "atm"), G(2, "L"), G(600, "K") }, null).Value, 1.999, 2.001);
        Assert.InRange(ChemGas.Combined(new[] { G(1, "atm"), G(2, "L"), G(null, "K"), G(2, "atm"), G(2, "L"), G(600, "K") }, null).Value, 299.9, 300.1);
        Assert.InRange(ChemGas.Combined(new[] { G(1, "atm"), G(2, "L"), G(300, "K"), G(null, "atm"), G(2, "L"), G(600, "K") }, null).Value, 1.999, 2.001);
        Assert.InRange(ChemGas.Combined(new[] { G(1, "atm"), G(2, "L"), G(300, "K"), G(2, "atm"), G(2, "L"), G(null, "K") }, null).Value, 599.9, 600.1);
    }

    [Fact]
    public void APairHeldConstantIsLeftOut()
    {
        // (Boyle: 1 atm × 2 L at 2 atm → 1 L, the temperatures blank and not asked for)
        var boyle = ChemGas.Combined(new[] { G(1, "atm"), G(2, "L"), G(null, "K"), G(2, "atm"), G(null, "L"), G(null, "K") }, "T");
        Assert.True(boyle.Ok, boyle.Message);
        Assert.Equal(4, boyle.Solved);
        Assert.InRange(boyle.Value, 0.999, 1.001);
        // (Charles: 2 L at 300 K → 4 L at 600 K)
        var charles = ChemGas.Combined(new[] { G(null, "atm"), G(2, "L"), G(300, "K"), G(null, "atm"), G(null, "L"), G(600, "K") }, "P");
        Assert.InRange(charles.Value, 3.999, 4.001);
        // (Gay-Lussac: 1 atm at 300 K → 2 atm at 600 K)
        var gay = ChemGas.Combined(new[] { G(1, "atm"), G(null, "L"), G(300, "K"), G(null, "atm"), G(null, "L"), G(600, "K") }, "V");
        Assert.InRange(gay.Value, 1.999, 2.001);
        Assert.Equal("Fill in all but one.", ChemGas.Combined(new[] { G(1, "atm"), G(null, "L"), G(null, "K"), G(2, "atm"), G(null, "L"), G(null, "K") }, "T").Message);
    }

    [Fact]
    public void OneAtmosphereInEveryUnit()
    {
        Assert.InRange(ChemGasUnits.FromAtm(1, "kPa"), 101.324, 101.326);
        Assert.InRange(ChemGasUnits.FromAtm(1, "mmHg"), 759.99, 760.01);
        Assert.InRange(ChemGasUnits.FromAtm(1, "psi"), 14.695, 14.697);
        Assert.InRange(ChemGasUnits.FromAtm(1, "bar"), 1.0132, 1.0133);
        Assert.InRange(ChemGasUnits.ToAtm(101.325, "kPa"), 0.9999, 1.0001);
        Assert.InRange(ChemGasUnits.ToAtm(760, "mmHg"), 0.9999, 1.0001);
        Assert.InRange(ChemGasUnits.ToAtm(14.696, "psi"), 0.9999, 1.0001);
        Assert.Equal(273.15, ChemGasUnits.ToKelvin(0, "°C"), 6);
        Assert.Equal(273.15, ChemGasUnits.ToKelvin(32, "°F"), 6);
        Assert.Equal(212, ChemGasUnits.FromKelvin(373.15, "°F"), 6);
        Assert.Equal(1000, ChemGasUnits.ToLitres(1, "m³"), 6);
        Assert.Equal(0.25, ChemGasUnits.ToLitres(250, "mL"), 6);
    }
}
