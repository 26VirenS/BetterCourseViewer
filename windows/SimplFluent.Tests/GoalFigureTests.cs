using Simpl.Support;
using Xunit;

namespace Simpl.Tests;

/// <summary>The term's GPA goal (Support/GoalFigure.cs): half up to the hundredth on its decimal value, two decimals
/// always, between 0 and 4 — as the engine and the Mac keep it.</summary>
public class GoalFigureTests
{
    [Theory]
    [InlineData(3.425, 3.43)] // (the double holds 3.42499…; Math.Round on it says 3.42)
    [InlineData(3.125, 3.13)] // (a tie: half up, not to even)
    [InlineData(2.004, 2.0)]
    [InlineData(4.2, 4.0)]
    [InlineData(-1, 0.0)]
    public void KeptHalfUpBetweenNoneAndFour(double typed, double kept) => Assert.Equal(kept, GoalFigure.Value(typed));

    [Theory]
    [InlineData(4, "4.00")]
    [InlineData(3.425, "3.43")]
    [InlineData(3.5, "3.50")]
    public void ShownWithTwoDecimals(double goal, string shown) => Assert.Equal(shown, GoalFigure.Text(goal));

    /// <summary>Grades' own rounding (Screens/Grades/GradesKit.cs), the same half up: and a what-if past what a decimal
    /// holds — a score of 1e30, typed into What-if assignment's sheet — is a number still, not an OverflowException.</summary>
    [Fact]
    public void GradesRoundHalfUpAndNeverOverflow()
    {
        Assert.Equal(3.43m, Simpl.Screens.GradesKit.Half(3.425, 2));
        Assert.Equal(81.3m, Simpl.Screens.GradesKit.Half(81.25, 1));
        Assert.Equal(0m, Simpl.Screens.GradesKit.Half(double.NaN, 1));
        Assert.True(Simpl.Screens.GradesKit.Half(1e30 / 100 * 100, 1) > 1e28m);
        Assert.True(Simpl.Screens.GradesKit.Half(-1e32, 1) < -1e28m);
    }
}
