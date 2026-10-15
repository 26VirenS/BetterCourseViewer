using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Statistics tool's maths (Tools/Everyday/Model/Statistics.cs) on the shared spec's vectors — a sample described,
/// the line through points, the normal and t distributions (erf, the incomplete beta), the t-tests — the same numbers
/// as the Mac's.
/// </summary>
public class StatisticsTests
{
    private static readonly double[] Sample = { 2, 4, 4, 4, 5, 5, 7, 9 };

    [Fact]
    public void ASampleDescribed()
    {
        var d = StatsMath.Describe(Sample)!;
        Assert.Equal(8, d.N);
        Assert.Equal(5, d.Mean, 9);
        Assert.Equal(4.5, d.Median, 9);
        Assert.Equal(new[] { 4.0 }, d.Modes);
        Assert.Equal(2, d.PopSd, 9);
        Assert.Equal(2.138, d.SampleSd, 3);
        Assert.Equal(4, d.Q1, 9);
        Assert.Equal(6, d.Q3, 9);
        Assert.Equal(2, d.Iqr, 9);
        Assert.Equal(2, d.Min);
        Assert.Equal(9, d.Max);
        Assert.Equal(7, d.Range);
        Assert.Equal(40, d.Sum);
        Assert.Equal(232, d.SumSquares);
        Assert.Equal(4, d.PopVar, 9);
        Assert.Equal(2.138 / Math.Sqrt(8), d.Se, 3);
        Assert.Empty(d.Outliers);
    }

    [Fact]
    public void QuartilesLeaveTheMedianOutWhenNIsOdd()
    {
        // (Moore and McCabe: 1..7 has Q1 2 and Q3 6)
        var (q1, q3) = StatsMath.Quartiles(new double[] { 1, 2, 3, 4, 5, 6, 7 });
        Assert.Equal(2, q1);
        Assert.Equal(6, q3);
        Assert.Equal((5.0, 5.0), StatsMath.Quartiles(new double[] { 5 }));
    }

    [Fact]
    public void ModesOutliersAndBins()
    {
        Assert.Empty(StatsMath.Modes(new double[] { 1, 2, 3 }));
        Assert.Equal(new[] { 2.0, 3.0 }, StatsMath.Modes(new double[] { 1, 2, 2, 3, 3 }));
        var d = StatsMath.Describe(new double[] { 10, 11, 12, 13, 14, 40 })!;
        Assert.Equal(new[] { 40.0 }, d.Outliers);
        var bins = StatsMath.Bins(StatsMath.Describe(Sample)!);
        Assert.Equal(4, bins.Counts.Length);               // ⌈log₂ 8 + 1⌉
        Assert.Equal(8, bins.Counts.Sum());
        Assert.Equal(2, bins.Start);
        Assert.Equal(1.75, bins.Width, 9);
        Assert.Equal(new[] { 1, 5, 1, 1 }, bins.Counts);
    }

    [Fact]
    public void NumbersAreReadHoweverTheyAreSeparated()
    {
        var n = StatsMath.Numbers("1, 2;3\t4\n5\r\n−6 seven 8.5", out var skipped);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, -6, 8.5 }, n);
        Assert.Equal(1, skipped);
        Assert.Empty(StatsMath.Numbers("  "));
        Assert.Null(StatsMath.Describe(Array.Empty<double>()));
    }

    [Fact]
    public void TheLineThroughPoints()
    {
        var r = StatsMath.Linear(new double[] { 1, 2, 3, 4, 5 }, new double[] { 2, 4, 5, 4, 5 })!;
        Assert.Equal(0.6, r.Slope, 9);
        Assert.Equal(2.2, r.Intercept, 9);
        Assert.Equal(0.7746, r.R, 4);
        Assert.Equal(0.6, r.R2, 9);
        Assert.Equal(5.8, r.Predict(6), 9);
        Assert.Null(StatsMath.Linear(new double[] { 1 }, new double[] { 2 }));
        Assert.Null(StatsMath.Linear(new double[] { 3, 3, 3 }, new double[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData(0.5, 0.5204998778130465)]
    [InlineData(1, 0.8427007929497149)]
    [InlineData(2, 0.9953222650189527)]
    [InlineData(2.6, 0.9997639655834707)]
    [InlineData(3, 0.9999779095030014)]
    [InlineData(-1, -0.8427007929497149)]
    public void ErfIsCloserThanAMillionth(double x, double erf) => Assert.Equal(erf, StatsMath.Erf(x), 12);

    [Fact]
    public void TheNormalDistribution()
    {
        Assert.Equal(0.8413, StatsMath.NormalCdf(115, 100, 15), 4);
        Assert.Equal(1, (115 - 100) / 15.0, 9);
        Assert.Equal(1.5417257900280018e-8, StatsMath.Erfc(4), 1e-20);
        Assert.Equal(1.959963984540054, StatsMath.NormalInverse(0.975), 10);
        Assert.Equal(-3.090232306167813, StatsMath.NormalInverse(0.001), 10);
        Assert.Equal(0, StatsMath.NormalInverse(0.5), 12);
        // (the inverse undoes the CDF)
        foreach (var z in new[] { -2.5, -0.3, 0.7, 3.1 }) Assert.Equal(z, StatsMath.NormalInverse(StatsMath.NormalCdf(z)), 9);
    }

    [Fact]
    public void TheTDistributionThroughTheIncompleteBeta()
    {
        Assert.Equal(0.5248, StatsMath.IncompleteBeta(2, 3, 0.4), 10);
        Assert.Equal(Math.Log(24), StatsMath.LnGamma(5), 9);
        Assert.Equal(0.050, StatsMath.TTwoTailed(2.228, 10), 3);
        Assert.Equal(2.2281388519863, StatsMath.TCritical(0.05, 10), 9);
        Assert.Equal(0.9030481598787676, StatsMath.TCdf(1.5, 5), 9);
        Assert.Equal(1 - 0.9030481598787676, StatsMath.TCdf(-1.5, 5), 9);
        Assert.Equal(1.5, StatsMath.TQuantile(StatsMath.TCdf(1.5, 5), 5), 9);
        Assert.Equal(StatsMath.NormalPdf(0), StatsMath.TPdf(0, 1e7), 5);
    }

    [Fact]
    public void TheOneSampleTTest()
    {
        var r = StatsMath.OneSample(Sample, 4)!;
        Assert.Equal(1.323, r.T, 3);
        Assert.Equal(7, r.Df);
        Assert.Equal(0.227, r.P, 3);
        Assert.Equal(5, r.Estimate, 9);
        Assert.Equal(3.2125, r.CiLow, 0.001);
        Assert.Equal(6.7875, r.CiHigh, 0.001);
        // (the same test from its summary figures)
        var s = StatsMath.OneSample(5, 2.138089935299395, 8, 4)!;
        Assert.Equal(r.T, s.T, 9);
        Assert.Null(StatsMath.OneSample(new double[] { 3, 3, 3 }, 2));
    }

    [Fact]
    public void WelchAndPaired()
    {
        double[] a = { 20, 22, 19, 24, 25 }, b = { 18, 17, 21, 19, 16 };
        var w = StatsMath.Welch(a, b)!;
        Assert.Equal(2.6605, w.T, 4);
        Assert.Equal(7.4394, w.Df, 4);
        Assert.Equal(0.0307, w.P, 4);
        Assert.Equal(3.8, w.Estimate, 9);
        var p = StatsMath.Paired(a, b)!;
        Assert.Equal(2.0793, p.T, 4);
        Assert.Equal(4, p.Df);
        Assert.Equal(0.1061, p.P, 4);
        Assert.Null(StatsMath.Paired(a, new double[] { 1, 2 }));
    }

    [Theory]
    [InlineData(0.30000000000000004, "0.3")]
    [InlineData(2.138089935299395, "2.13809")]
    [InlineData(-2, "−2")]
    [InlineData(0.00001234, "1.234e−5")]
    [InlineData(123456789012, "1.23457e+11")]
    [InlineData(0, "0")]
    public void NumbersShowWithoutNoise(double v, string text) => Assert.Equal(text, StatsMath.Sig(v));

    [Fact]
    public void PValuesShowToFourPlaces()
    {
        Assert.Equal("0.0500", StatsMath.PText(0.05004));
        Assert.Equal("< 0.0001", StatsMath.PText(0.00001));
    }
}
