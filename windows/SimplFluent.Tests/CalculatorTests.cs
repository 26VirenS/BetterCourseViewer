using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>The calculator's sums (Tools/Everyday/Calculator.cs), typed in as the keyboard and --calc-expr type them.</summary>
public class CalculatorTests
{
    public CalculatorTests() => _ = Profile.Folder;

    private static double? Sum(string line)
    {
        var e = new CalcEngine();
        e.Feed(line);
        return e.Value;
    }

    [Theory]
    [InlineData("2^10=", 1024)]
    [InlineData("12×4=", 48)]
    [InlineData("12*4", 48)]
    [InlineData("1+2×3=", 7)]
    [InlineData("(1+2)×3=", 9)]
    [InlineData("10÷4=", 2.5)]
    [InlineData("2^3^2=", 512)]
    [InlineData("sin(30)+sqrt(16)=", 4.5)]
    [InlineData("cos(60)=", 0.5)]
    [InlineData("5!=", 120)]
    [InlineData("-3+5=", 2)]
    public void WorksOut(string line, double value)
    {
        var v = Sum(line);
        Assert.NotNull(v);
        Assert.Equal(value, v!.Value, 9);
    }

    [Fact]
    public void AnOpenBracketIsClosedForIt()
    {
        var e = new CalcEngine();
        e.Feed("2×(3+4");
        var r = e.Evaluate();
        Assert.Equal(CalcOutcome.Ok, r.Kind);
        Assert.Equal(14, r.Value, 9);
        Assert.EndsWith(")", r.Closed);
    }

    [Fact]
    public void AnOperatorStillWantingANumberSaysNothingYet()
    {
        var e = new CalcEngine();
        e.Feed("2+");
        Assert.NotEqual(CalcOutcome.Ok, e.Evaluate().Kind);
        Assert.Equal(CalcOutcome.Empty, new CalcEngine().Evaluate().Kind);
    }

    [Theory]
    [InlineData("12", 12.0)]
    [InlineData("0.5", 0.5)]
    [InlineData(".5", 0.5)]
    [InlineData("3.", 3.0)]
    [InlineData("2E3", 2000.0)]
    [InlineData("2E−3", 0.002)]
    public void NumbersAsTyped(string typed, double value) => Assert.Equal(value, CalcEngine.Number(typed)!.Value, 12);

    [Theory]
    [InlineData("2E")]
    [InlineData("2E−")]
    public void ANumberStillBeingTypedIsNotOneYet(string typed) => Assert.Null(CalcEngine.Number(typed));
}
