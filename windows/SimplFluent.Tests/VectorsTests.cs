using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Vectors' maths (Tools/Physics/Model/VectorModel.cs): magnitudes and angles, sums, dot and cross products, the angle
/// between two, unit vectors and projections, and the launch's list read and written — the shared worked examples.
/// </summary>
public class VectorsTests
{
    private const double Tol = 0.005;

    [Fact]
    public void ThreeFourFive()
    {
        var v = new Vec(3, 4);
        Assert.Equal(5, v.Magnitude, 1e-12);
        Assert.Equal(53.13, v.AngleDeg, Tol);
        Assert.Equal("(3, 4)", v.Text(false));
        Assert.Equal("(3, 4, 0)", v.Text(true));
    }

    [Fact]
    public void TheSumOfTwo()
    {
        // ((3,4) + (1,2) = (4,6), 7.211 long)
        var r = VectorMath.Sum(new[] { new Vec(3, 4), new Vec(1, 2) });
        Assert.Equal(new Vec(4, 6), r);
        Assert.Equal(7.211, r.Magnitude, Tol);
        Assert.Equal(new Vec(4, 6), new Vec(3, 4) + new Vec(1, 2));
        Assert.Equal(Vec.Zero, VectorMath.Sum(Array.Empty<Vec>()));
    }

    [Fact]
    public void DotAndCross()
    {
        Assert.Equal(32, VectorMath.Dot(new Vec(1, 2, 3), new Vec(4, 5, 6)), 1e-12);
        Assert.Equal(new Vec(0, 0, 1), VectorMath.Cross(new Vec(1, 0, 0), new Vec(0, 1, 0)));
        Assert.Equal(new Vec(0, 0, -1), VectorMath.Cross(new Vec(0, 1, 0), new Vec(1, 0, 0)));
        // (in 2D the cross product is its z-component: (3,4)×(1,2) = 3·2 − 4·1 = 2)
        Assert.Equal(2, VectorMath.Cross(new Vec(3, 4), new Vec(1, 2)).Z, 1e-12);
    }

    [Fact]
    public void TheAngleBetween()
    {
        Assert.Equal(90, VectorMath.AngleBetween(new Vec(1, 0), new Vec(0, 1))!.Value, 1e-9);
        Assert.Equal(0, VectorMath.AngleBetween(new Vec(2, 0), new Vec(5, 0))!.Value, 1e-9);
        Assert.Equal(180, VectorMath.AngleBetween(new Vec(1, 1), new Vec(-2, -2))!.Value, 1e-9);
        Assert.Null(VectorMath.AngleBetween(Vec.Zero, new Vec(1, 0)));
    }

    [Fact]
    public void MagnitudeAndAngleToComponents()
    {
        // (10 at 30° → (8.660, 5))
        var v = VectorMath.Polar(10, 30);
        Assert.Equal(8.660, v.X, Tol);
        Assert.Equal(5, v.Y, 1e-9);
        Assert.Equal(10, v.Magnitude, 1e-9);
        Assert.Equal(30, v.AngleDeg, 1e-9);
        // (angles go all the way round from +x, counter-clockwise)
        Assert.Equal(270, new Vec(0, -1).AngleDeg, 1e-9);
        Assert.Equal(180, new Vec(-3, 0).AngleDeg, 1e-9);
        Assert.Equal(0, new Vec(1, 0).AngleDeg, 1e-9);
    }

    [Fact]
    public void UnitVectorsAndProjections()
    {
        Assert.Equal(new Vec(0.6, 0.8), VectorMath.Unit(new Vec(3, 4)));
        Assert.Equal(Vec.Zero, VectorMath.Unit(Vec.Zero));
        // (the projection of (3,4) onto (1,0) is (3,0); onto (1,1) it is (3.5,3.5))
        Assert.Equal(new Vec(3, 0), VectorMath.Projection(new Vec(3, 4), new Vec(1, 0)));
        var p = VectorMath.Projection(new Vec(3, 4), new Vec(1, 1))!.Value;
        Assert.Equal(3.5, p.X, 1e-9);
        Assert.Equal(3.5, p.Y, 1e-9);
        Assert.Equal(3, VectorMath.ScalarProjection(new Vec(3, 4), new Vec(2, 0))!.Value, 1e-9);
        Assert.Null(VectorMath.Projection(new Vec(3, 4), Vec.Zero));
    }

    [Fact]
    public void TheListReadAndWritten()
    {
        var vs = VectorMath.Parse("3,4;1,2");
        Assert.Equal(new[] { new Vec(3, 4), new Vec(1, 2) }, vs);
        Assert.Equal("3,4;1,2", VectorMath.Write(vs, false));
        Assert.Equal("3,4,0;1,2,0", VectorMath.Write(vs, true));
        Assert.Equal(new Vec(1, 2, 3), VectorMath.Parse("1,2,3")[0]);
        // (at most six; a part that is not a number is 0 there; nothing from nothing)
        Assert.Equal(6, VectorMath.Parse("1,1;2,2;3,3;4,4;5,5;6,6;7,7").Count);
        Assert.Equal(new Vec(0, 2), VectorMath.Parse("x,2")[0]);
        Assert.Empty(VectorMath.Parse(""));
        Assert.Empty(VectorMath.Parse(null));
        Assert.Equal("C", VectorMath.Name(2));
    }
}
