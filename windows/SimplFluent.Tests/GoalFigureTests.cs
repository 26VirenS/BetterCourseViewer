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
}
