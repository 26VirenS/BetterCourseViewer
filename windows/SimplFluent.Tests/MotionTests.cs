using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Kinematics &amp; Projectiles' maths (Tools/Physics/Model/MotionModel.cs, PhysMath.cs): the five quantities solved from
/// any three, each case and its equations; a projectile's flight; the angles that hit a target; and the numbers' words.
/// </summary>
public class MotionTests
{
    private const double Tol = 0.005;

    [Fact]
    public void ConstantAccelerationFromRest()
    {
        // (v0 = 0, a = 2, t = 5 → Δx = 25, v = 10)
        var (r, error) = KinematicsSolver.Solve(null, 0, null, 2, 5);
        Assert.Null(error);
        Assert.NotNull(r);
        Assert.Equal(25, r!.X, 9);
        Assert.Equal(10, r.V, 9);
        Assert.Equal(new[] { "v", "x" }, r.Solved.Select(s => s.Key));
        Assert.Equal(KinematicsSolver.Eq1, r.Solved[0].Equation);
        Assert.Equal(KinematicsSolver.Eq2, r.Solved[1].Equation);
    }

    [Fact]
    public void FreeFallFromTwentyMetres()
    {
        // (Δx = −20 with the g preset: 2.019 s, 19.81 m/s down)
        var (r, error) = KinematicsSolver.Solve(-20, 0, null, -KinematicsSolver.G, null);
        Assert.Null(error);
        Assert.Equal(2.019, r!.T, Tol);
        Assert.Equal(19.81, Math.Abs(r.V), Tol);
        Assert.True(r.V < 0);
        // (down taken as positive gives the same)
        var (d, _) = KinematicsSolver.Solve(20, 0, null, KinematicsSolver.G, null);
        Assert.Equal(2.019, d!.T, Tol);
        Assert.Equal(19.81, d.V, Tol);
    }

    [Theory]
    [InlineData(true, true, true, false, false)]   // x, u, v
    [InlineData(true, true, false, true, false)]   // x, u, a
    [InlineData(true, true, false, false, true)]   // x, u, t
    [InlineData(true, false, true, true, false)]   // x, v, a
    [InlineData(true, false, true, false, true)]   // x, v, t
    [InlineData(true, false, false, true, true)]   // x, a, t
    [InlineData(false, true, true, true, false)]   // u, v, a
    [InlineData(false, true, true, false, true)]   // u, v, t
    [InlineData(false, true, false, true, true)]   // u, a, t
    [InlineData(false, false, true, true, true)]   // v, a, t
    public void EveryThreeGiveBackTheSameMotion(bool x, bool u, bool v, bool a, bool t)
    {
        // (a motion: v0 = 3, a = 1.5, t = 4 → v = 9, Δx = 24; any three of its five give the other two)
        var m = new Kinematics(24, 3, 9, 1.5, 4);
        var (r, error) = KinematicsSolver.Solve(x ? m.X : null, u ? m.U : null, v ? m.V : null, a ? m.A : null, t ? m.T : null);
        Assert.Null(error);
        Assert.Equal(m.X, r!.X, 9);
        Assert.Equal(m.U, r.U, 9);
        Assert.Equal(m.V, r.V, 9);
        Assert.Equal(m.A, r.A, 9);
        Assert.Equal(m.T, r.T, 9);
        Assert.Equal(2, r.Solved.Count);
    }

    [Fact]
    public void TooFewOrTooManyAreSaidPlainly()
    {
        Assert.Contains("three", KinematicsSolver.Solve(null, 0, null, 2, null).Error!);
        Assert.Contains("two blank", KinematicsSolver.Solve(25, 0, 10, 2, 5).Error!);
        // (no motion fits)
        Assert.NotNull(KinematicsSolver.Solve(null, 0, 10, 0, null).Error);
        Assert.NotNull(KinematicsSolver.Solve(10, null, null, 2, 0).Error);
        Assert.NotNull(KinematicsSolver.Solve(-100, 0, null, 2, null).Error);
    }

    [Fact]
    public void ALaunchAtFortyFiveDegrees()
    {
        // (v0 = 20 m/s at 45°, h = 0, g = 9.81 → range 40.77 m, max height 10.19 m, flight 2.883 s)
        var f = Projectile.Solve(20, 45, 0);
        Assert.NotNull(f);
        Assert.Equal(40.77, f!.Range, Tol);
        Assert.Equal(10.19, f.MaxHeight, Tol);
        Assert.Equal(2.883, f.Time, Tol);
        Assert.Equal(20, f.ImpactSpeed, Tol);
        Assert.Equal(-45, f.ImpactAngle, Tol);
        Assert.Equal(f.Time / 2, f.ApexTime, 1e-9);
        Assert.Equal(f.Range / 2, f.ApexX, 1e-9);
    }

    [Fact]
    public void DroppedFromTwentyMetres()
    {
        // (free fall from 20 m → 2.019 s, 19.81 m/s)
        var f = Projectile.Solve(0, 0, 20);
        Assert.Equal(2.019, f!.Time, Tol);
        Assert.Equal(19.81, f.ImpactSpeed, Tol);
        Assert.Equal(0, f.Range, 1e-9);
        Assert.Equal(20, f.MaxHeight, 1e-9);
        Assert.Equal(-90, f.ImpactAngle, 1e-6);
        // (the path ends on the ground at the landing)
        var (x, y) = Projectile.At(0, 0, 20, f.Time);
        Assert.Equal(0, x, 1e-9);
        Assert.Equal(0, y, 1e-6);
    }

    [Fact]
    public void TheTwoAnglesThatHitATarget()
    {
        // (from the ground, the two angles add to 90°; the range at 45° is reached by 45° alone)
        var a = Projectile.TargetAngles(20, 20, 0);
        Assert.Equal(2, a.Length);
        Assert.Equal(90, a[0] + a[1], 1e-6);
        Assert.True(a[0] < a[1]);
        foreach (var angle in a) Assert.Equal(20, Projectile.Solve(20, angle, 0)!.Range, 1e-6);
        var edge = Projectile.TargetAngles(20, 40.7747, 0);
        Assert.NotEmpty(edge);
        Assert.Equal(45, edge[0], 0.5);
        Assert.Empty(Projectile.TargetAngles(20, 60, 0));
        // (from a height, both angles still land on it)
        foreach (var angle in Projectile.TargetAngles(15, 10, 5)) Assert.Equal(10, Projectile.Solve(15, angle, 5)!.Range, 1e-6);
    }

    [Theory]
    [InlineData(40.7747, "40.77")]
    [InlineData(10.1937, "10.19")]
    [InlineData(2.8832, "2.883")]
    [InlineData(0.03, "0.03")]
    [InlineData(25, "25")]
    [InlineData(-9.81, "−9.81")]
    [InlineData(66.6667, "66.67")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(0, "0")]
    [InlineData(12345678901, "1.235e+10")]
    public void NumbersAreWrittenToFourFigures(double v, string text) => Assert.Equal(text, PhysMath.Text(v));

    [Theory]
    [InlineData("20", 20)]
    [InlineData("−9.81", -9.81)]
    [InlineData("45°", 45)]
    [InlineData("1,000", 1000)]
    [InlineData("20 m/s", 20)]
    [InlineData("2e3", 2000)]
    public void NumbersAreReadAsTyped(string s, double v) => Assert.Equal(v, PhysMath.Number(s)!.Value, 1e-9);

    [Fact]
    public void WordsAreNotNumbers()
    {
        Assert.Null(PhysMath.Number(""));
        Assert.Null(PhysMath.Number("   "));
        Assert.Null(PhysMath.Number("abc"));
        Assert.Null(PhysMath.Number(null));
        Assert.Equal("40.77 m", PhysMath.WithUnit(40.7747, "m"));
        Assert.Equal("53.13°", PhysMath.WithUnit(53.1301, "°"));
        Assert.Equal("-9.81 m/s2", PhysMath.Ascii("−9.81 m/s2"));
    }
}
