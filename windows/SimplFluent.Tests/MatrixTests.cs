using System.Numerics;
using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Matrix Calculator's maths (Tools/Everyday/Model/Matrix.cs) on the shared spec's vectors — exact fractions, the
/// determinant, the inverse, the reduced row echelon form and rank, A·x = b, powers, and eigenvalues by the shifted QR
/// iteration — the same numbers as the Mac's.
/// </summary>
public class MatrixTests
{
    private static readonly Mat A = Mat.Of(new long[] { 1, 2 }, new long[] { 3, 4 });
    private static readonly Mat Singular = Mat.Of(new long[] { 1, 2, 3 }, new long[] { 4, 5, 6 }, new long[] { 7, 8, 9 });

    [Fact]
    public void FractionsStayExact()
    {
        var half = new Rational(1, 2);
        var third = new Rational(1, 3);
        Assert.Equal(new Rational(5, 6), half + third);
        Assert.Equal(new Rational(1, 6), half - third);
        Assert.Equal(new Rational(1, 6), half * third);
        Assert.Equal(new Rational(3, 2), half / third);
        Assert.Equal(new Rational(-3, 4), new Rational(6, -8));
        Assert.Equal("−3/4", new Rational(6, -8).Text());
        Assert.Equal("-3/4", new Rational(6, -8).Ascii());
        Assert.Equal("0", new Rational(0, 5).Text());
        Assert.True(new Rational(4, 2).IsInteger);
        Assert.Equal(-1.0 / 3, new Rational(-1, 3).ToDouble(), 15);
    }

    [Theory]
    [InlineData("3", "3", true)]
    [InlineData("-3", "−3", true)]
    [InlineData("3/4", "3/4", true)]
    [InlineData("6/-8", "−3/4", true)]
    [InlineData("0.25", "1/4", false)]
    [InlineData("1e-3", "1/1000", false)]
    [InlineData("1,5", "3/2", false)]
    public void ACellIsReadAsItIsTyped(string typed, string text, bool exact)
    {
        var r = Rational.Parse(typed, out var wasExact);
        Assert.NotNull(r);
        Assert.Equal(text, r!.Value.Text());
        Assert.Equal(exact, wasExact);
    }

    [Fact]
    public void ACellThatIsNoNumberIsNamed()
    {
        Assert.Null(Rational.Parse("x", out _));
        Assert.Null(Rational.Parse("1/0", out _));
        Assert.Null(MatrixParse.Parse("1 2; 3 x", out _, out var error));
        Assert.Equal("Can't read “x” — a cell is a number, a fraction like 3/4 or a decimal.", error);
        Assert.Null(MatrixParse.Parse("1 2; 3", out _, out error));
        Assert.Equal("Every row needs the same number of cells.", error);
        Assert.Null(MatrixParse.Parse("  ", out _, out error));
        Assert.Null(error);
    }

    [Fact]
    public void AMatrixIsReadFromRowsHoweverTheyAreSeparated()
    {
        var m = MatrixParse.Parse("1\t2\r\n3\t4", out var exact, out _)!;
        Assert.True(m.SameAs(A));
        Assert.True(exact);
        var d = MatrixParse.Parse("1/2 0.25; -3, 1", out exact, out _)!;
        Assert.False(exact);
        Assert.Equal("1/2 1/4; -3 1", d.Ascii());
    }

    [Fact]
    public void TheSpecsTwoByTwo()
    {
        Assert.Equal(new Rational(-2), A.Determinant());
        var inv = A.Inverse()!;
        Assert.Equal("-2 1; 3/2 -1/2", inv.Ascii());
        Assert.Equal("-2\t1\n1.5\t-0.5", inv.Tabbed(false));
        Assert.True(A.Multiply(inv).SameAs(Mat.Identity(2)));
        Assert.Equal(new Rational(5), A.Trace());
        Assert.Equal(2, A.Rank());
        Assert.Equal("1 3; 2 4", A.Transpose().Ascii());
    }

    [Fact]
    public void SumsProductsAndPowers()
    {
        Assert.Equal("2 4; 6 8", A.Add(A).Ascii());
        Assert.Equal("0 0; 0 0", A.Subtract(A).Ascii());
        Assert.Equal("7 10; 15 22", A.Multiply(A).Ascii());
        Assert.Equal("1/2 1; 3/2 2", A.Scale(new Rational(1, 2)).Ascii());
        Assert.Equal("37 54; 81 118", A.Power(3).Ascii());
        Assert.Equal("1 0; 0 1", A.Power(0).Ascii());
        Assert.True(A.Power(-1).SameAs(A.Inverse()!));
        var wrong = Assert.Throws<MatrixError>(() => A.Add(Singular));
        Assert.Equal("A is 2×2 and B is 3×3: they have to be the same size to add or subtract.", wrong.Message);
        Assert.Throws<MatrixError>(() => A.Multiply(Singular));
        Assert.Throws<MatrixError>(() => Mat.Of(new long[] { 1, 2, 3 }).Determinant());
        Assert.Throws<MatrixError>(() => Singular.Power(-1));
    }

    [Fact]
    public void TheReducedRowEchelonFormWithItsSteps()
    {
        var (r, steps, rank, pivots) = Singular.Rref();
        Assert.Equal("1 0 -1; 0 1 2; 0 0 0", r.Ascii());
        Assert.Equal(2, rank);
        Assert.Equal(new[] { 0, 1 }, pivots);
        Assert.Equal(new[] { "R2 ← R2 − 4R1", "R3 ← R3 − 7R1", "R2 ← R2 ÷ −3", "R1 ← R1 − 2R2", "R3 ← R3 + 6R2" }, steps);
        Assert.Equal(Rational.Zero, Singular.Determinant());
        Assert.Null(Singular.Inverse());
        Assert.Empty(Mat.Identity(3).Rref().Steps);
    }

    [Fact]
    public void ASystemSolved()
    {
        var (x, why) = Mat.Of(new long[] { 2, 1 }, new long[] { 1, 3 }).Solve(Mat.Of(new long[] { 3 }, new long[] { 5 }));
        Assert.Null(why);
        Assert.Equal("4/5; 7/5", x!.Ascii());
        Assert.Equal(0.8, x[0, 0].ToDouble(), 12);
        Assert.Equal(1.4, x[1, 0].ToDouble(), 12);
        Assert.Equal("No solution: the equations contradict each other.", Singular.Solve(Mat.Of(new long[] { 1 }, new long[] { 2 }, new long[] { 4 })).Why);
        Assert.Equal("Infinitely many solutions: the system has 1 free variable.", Singular.Solve(Mat.Of(new long[] { 6 }, new long[] { 15 }, new long[] { 24 })).Why);
        Assert.Throws<MatrixError>(() => A.Solve(Mat.Of(new long[] { 1 }, new long[] { 2 }, new long[] { 3 })));
    }

    private static void Same(Complex[] got, params Complex[] want)
    {
        Assert.Equal(want.Length, got.Length);
        var left = got.ToList();
        foreach (var w in want)
        {
            var i = left.FindIndex(g => Complex.Abs(g - w) < 1e-6);
            Assert.True(i >= 0, $"{Eigen.Text(w)} missing from {string.Join(", ", got.Select(Eigen.Text))}");
            left.RemoveAt(i);
        }
    }

    [Fact]
    public void EigenvaluesRealAndComplex()
    {
        Same(Mat.Of(new long[] { 2, 0 }, new long[] { 0, 3 }).Eigenvalues(), 2, 3);
        Same(Mat.Of(new long[] { 0, -1 }, new long[] { 1, 0 }).Eigenvalues(), Complex.ImaginaryOne, -Complex.ImaginaryOne);
        Same(Mat.Of(new long[] { 2, 1, 0 }, new long[] { 1, 3, 1 }, new long[] { 0, 1, 4 }).Eigenvalues(), 3 - Math.Sqrt(3), 3, 3 + Math.Sqrt(3));
        Same(Singular.Eigenvalues(), 16.116843969807043, -1.1168439698070427, 0);
        // (the companion matrix of x⁴ − 1: 1, −1, ±i)
        Same(Mat.Of(new long[] { 0, 0, 0, 1 }, new long[] { 1, 0, 0, 0 }, new long[] { 0, 1, 0, 0 }, new long[] { 0, 0, 1, 0 }).Eigenvalues(), 1, -1, Complex.ImaginaryOne, -Complex.ImaginaryOne);
        // (a 6×6 with two complex pairs)
        Same(Mat.Of(new long[] { 1, 2, 0, 0, 0, 0 }, new long[] { -2, 1, 0, 0, 0, 0 }, new long[] { 0, 0, 3, 0, 0, 0 }, new long[] { 0, 0, 0, 0, 1, 0 }, new long[] { 0, 0, 0, -1, 0, 0 }, new long[] { 0, 0, 0, 0, 0, 7 }).Eigenvalues(),
            7, 3, new Complex(1, 2), new Complex(1, -2), Complex.ImaginaryOne, -Complex.ImaginaryOne);
        Same(Mat.Of(new long[] { 5 }).Eigenvalues(), 5);
        Same(Mat.Of(new long[] { 2, 1 }, new long[] { 0, 2 }).Eigenvalues(), 2, 2);
        // (largest real part first)
        Assert.Equal(new[] { "3", "2" }, Mat.Of(new long[] { 2, 0 }, new long[] { 0, 3 }).Eigenvalues().Select(Eigen.Text));
    }

    [Theory]
    [InlineData(1.5, -2, "1.5 − 2i")]
    [InlineData(0, 1, "i")]
    [InlineData(0, -1, "−i")]
    [InlineData(-0.5, 0, "−0.5")]
    [InlineData(2, 0.5, "2 + 0.5i")]
    public void AnEigenvalueAsWords(double re, double im, string text) => Assert.Equal(text, Eigen.Text(new Complex(re, im)));
}
