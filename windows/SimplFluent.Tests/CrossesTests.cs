using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Genetics Crosses' model (Tools/Biology/Model/CrossesModel.cs, PedigreeModel.cs) against the specification's §5.3.2
/// reference results, through the Cross page's own path (a typed cross, the pickers, the examples, the test cross), and
/// the Probability, Chi-square and Pedigree pages' working. Plain .NET: no window is opened.
/// </summary>
public class CrossesTests
{
    private static CrossSetup Setup(string text) => CrossSetup.Parse(text).Setup ?? throw new InvalidOperationException(CrossSetup.Parse(text).Error);

    [Theory]
    [InlineData("Aa × Aa", "1 AA : 2 Aa : 1 aa", "3 A_ : 1 aa")]
    [InlineData("Aa × aa", "1 Aa : 1 aa", "1 A_ : 1 aa")]
    [InlineData("AA × aa", "1 Aa", "1 A_")]
    [InlineData("C^R C^W × C^R C^W", "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", "1 red : 2 pink : 1 white")]
    [InlineData("AaBb × AaBb", "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb", "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb")]
    [InlineData("AaBb × aabb", "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", "1 A_B_ : 1 A_bb : 1 aaB_ : 1 aabb")]
    [InlineData("I^A i × I^B i", "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", "1 AB : 1 A : 1 B : 1 O")]
    [InlineData("I^A I^B × I^A I^B", "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", "1 A : 2 AB : 1 B")]
    public void TheReferenceCrosses(string text, string genotypes, string phenotypes)
    {
        var s = Setup(text);
        var r = s.Cross.Result();
        Assert.Equal(genotypes, r.GenotypicRatioText);
        Assert.Equal(phenotypes, r.PhenotypicRatioText);
        Assert.Equal(text, s.Text);
    }

    [Fact]
    public void ATrihybridHasTwentySevenGenotypesAndEightPhenotypes()
    {
        var r = Setup("AaBbCc × AaBbCc").Cross.Result();
        Assert.Equal(27, r.Genotypes.Count);
        Assert.Equal(new[] { 27, 9, 9, 9, 3, 3, 3, 1 }, r.PhenotypicRatio);
        var forked = new CrossForkedLine(Setup("AaBbCc × AaBbCc").Cross, false);
        Assert.Equal(new[] { 27L, 9, 9, 3, 9, 3, 3, 1 }, forked.Leaves.Select(l => l.P.Num));
        Assert.Equal(BioFrac.One, forked.Total);
    }

    [Fact]
    public void XLinkedCrossesSplitDaughtersAndSons()
    {
        // (typed father first: the mother is put first)
        var s = Setup("X^a Y × X^A X^a");
        Assert.Equal("X^A X^a × X^a Y", s.Text);
        var d = s.Cross.Result(BioSex.Female);
        var m = s.Cross.Result(BioSex.Male);
        Assert.Equal("1 X^A X^a : 1 X^a X^a", d.GenotypicRatioText);
        Assert.Equal("1 unaffected : 1 affected", d.PhenotypicRatioText);
        Assert.Equal(new BioFrac(1, 2), d.Phenotypes[0].Carriers);
        Assert.Equal("1 X^A Y : 1 X^a Y", m.GenotypicRatioText);
        var n = Setup("X^A X^a × X^A Y").Cross;
        Assert.Equal("1 X^A X^A : 1 X^A X^a", n.Result(BioSex.Female).GenotypicRatioText);
        Assert.Single(n.Result(BioSex.Female).Phenotypes);
        Assert.Equal(new BioFrac(1, 2), n.Result(BioSex.Male).Phenotypes.First(p => p.Text == "affected").P);
    }

    [Fact]
    public void ALethalHomozygoteIsBornTwoToOne()
    {
        var s = Setup("A^Y a × A^Y a");
        s.SetPattern(BioInheritance.LethalDominantHomozygote, 0);
        var r = s.Cross.Result();
        Assert.Equal("1 A^Y A^Y : 2 A^Y a : 1 aa", r.GenotypicRatioText);
        Assert.True(r.Genotypes[0].Dies);
        Assert.Equal(new[] { 2, 1 }, r.BornGenotypicRatio);
        Assert.Equal(new BioFrac(3, 4), r.BornFraction);
        var yellow = CrossSetup.Standard;
        Assert.True(yellow.ApplyExample(CrossExamples.Find("yellow")!, 0));
        Assert.Equal("2 yellow : 1 non-yellow", yellow.Cross.Result().PhenotypicRatioText);
        // (a lethal homozygote is never offered as a parent)
        Assert.Equal(2, yellow.Genotypes(0, 1).Count);
    }

    [Fact]
    public void ThePickersOfferOnlyWhatAParentCanHave()
    {
        var s = CrossSetup.Standard;
        s.AddGene();
        s.SetPattern(BioInheritance.XRecessive, 1);
        Assert.Equal("Aa X^B X^b × Aa X^B Y", s.Text);
        Assert.Equal(new[] { "X^B X^B", "X^B X^b", "X^b X^b" }, s.Genotypes(1, 1).Select(g => s.Genes[1].Text(g)));
        Assert.Equal(new[] { "X^B Y", "X^b Y" }, s.Genotypes(1, 2).Select(g => s.Genes[1].Text(g)));
        Assert.False(s.Allowed(BioInheritance.XDominant, 0));
        var abo = CrossSetup.Standard;
        abo.SetPattern(BioInheritance.Abo, 0);
        Assert.Equal("I^A i × I^B i", abo.Text);
        var test = Setup("AaBb × AaBb");
        test.TestCross();
        Assert.Equal("AaBb × aabb", test.Text);
        Assert.Equal(new[] { 1, 1, 1, 1 }, test.Cross.Result().PhenotypicRatio);
        Assert.Equal(s, CrossSetup.Decode(s.Encoded));
    }

    [Fact]
    public void TypedMistakesAreSaid()
    {
        Assert.NotNull(CrossSetup.Parse("X^A X^a × X^A X^a").Error);
        Assert.NotNull(CrossSetup.Parse("AaBbCcDd × AaBbCcDd").Error);
        Assert.NotNull(CrossSetup.Parse("C^R C^W × C^B C^B").Error);
        Assert.NotNull(CrossSetup.Parse("Aa").Error);
        Assert.Equal(new[] { "C^R", "C^W" }, Setup("C^R C^R × C^R C^R").Genes[0].Alleles);
    }

    [Fact]
    public void TheSquareIsInCanonicalOrder()
    {
        var sq = new CrossPunnett(Setup("AaBb × AaBb").Cross);
        Assert.Equal(new[] { "AB", "Ab", "aB", "ab" }, sq.Rows.Select(r => r.Text));
        Assert.Equal("AABB", sq.Cells[0][0].Text);
        Assert.Equal("aabb", sq.Cells[3][3].Text);
        Assert.Equal(3, sq.Cells[3][3].Tint);
        var x = new CrossPunnett(Setup("X^A X^a × X^a Y").Cross);
        Assert.Equal(BioSex.Male, x.Cells[0][1].Sex);
        Assert.True(x.Cells[0][0].Carrier);
    }

    [Fact]
    public void TheProductAndSumRules()
    {
        var c = Setup("AaBbCc × AaBbCc").Cross;
        CrossTarget Chip(int k, string label) => CrossProbability.Targets(c.Genes[k]).First(t => t.Label == label);
        var p = CrossProbability.Product(c, new[] { Chip(0, "aa"), Chip(1, "B_"), Chip(2, "Cc") });
        Assert.Equal(new[] { new BioFrac(1, 4), new BioFrac(3, 4), new BioFrac(1, 2) }, p.Factors);
        Assert.Equal(new BioFrac(3, 32), p.Product);
        Assert.Equal("9.4%", p.Product.PercentText);
        Assert.Equal("aaB_Cc", CrossProbability.TargetText(c, new[] { Chip(0, "aa"), Chip(1, "B_"), Chip(2, "Cc") }));
        Assert.Equal(new BioFrac(1, 2), CrossProbability.Sum(c, 0, new[] { Chip(0, "AA"), Chip(0, "aa") }).Total);
        Assert.Equal(new BioFrac(37, 64), CrossProbability.AtLeastOne(new BioFrac(1, 4), 3).Exact);
        Assert.Equal(new BioFrac(9, 64), CrossProbability.Exactly(2, 3, new BioFrac(1, 4)).Exact);
        Assert.Null(CrossProbability.AtLeastOne(new BioFrac(3, 128), 20).Exact);
    }

    [Fact]
    public void ChiSquareStatesTheNullHypothesisAndNeverAccepts()
    {
        var rows = ChiSquareWork.Rows(Setup("AaBb × AaBb").Cross.Result());
        var counts = new[] { "315", "108", "101", "32" };
        for (var i = 0; i < 4; i++) rows[i].Observed = counts[i];
        var w = ChiSquareWork.Make(rows).Work!;
        Assert.Equal(new[] { 312.75, 104.25, 104.25, 34.75 }, w.Expected);
        Assert.InRange(w.Result.Chi2, 0.469, 0.471);
        Assert.Equal(3, w.Result.Df);
        Assert.Equal(7.81, w.Result.Critical);
        Assert.False(w.Result.Reject);
        Assert.Contains("The data fit a 9:3:3:1 ratio.", w.NullHypothesis);
        Assert.Contains("fail to reject the null hypothesis", w.Result.Conclusion);
        Assert.DoesNotContain("accept the null", (w.Result.Conclusion + w.ConclusionDetail + w.Meaning).ToLowerInvariant());
        var bad = rows.Select(r => r.Copy()).ToList();
        bad[0].Observed = "3.5";
        bad[1].Ratio = "0";
        Assert.Equal(2, ChiSquareWork.Make(bad).Problems.Count);
    }

    [Fact]
    public void ThePedigreeCheckerRulesModesOut()
    {
        PedigreeVerdict V(string example, PedigreeMode m) => PedigreeChecker.Verdict(Pedigree.Examples.First(e => e.Id == example).Pedigree, m);
        Assert.True(V("ar", PedigreeMode.AutosomalRecessive).Consistent);
        Assert.False(V("ar", PedigreeMode.AutosomalDominant).Consistent);
        Assert.False(V("ar", PedigreeMode.XRecessive).Consistent);
        Assert.True(V("ad", PedigreeMode.AutosomalDominant).Consistent);
        Assert.False(V("ad", PedigreeMode.AutosomalRecessive).Consistent);
        Assert.True(V("xr", PedigreeMode.XRecessive).Consistent);
        Assert.Contains("affected son", V("xr", PedigreeMode.XDominant).Reasons[0]);
        Assert.True(V("xd", PedigreeMode.XDominant).Consistent);
        Assert.False(V("xd", PedigreeMode.XRecessive).Consistent);
        // (a carrier in a dominant mode rules it out by herself)
        var c = Pedigree.Start;
        c.Update(1, x => x.Carrier = true);
        c.AddChild(0, true, true);
        Assert.Equal(new[] { 1 }, PedigreeChecker.Verdict(c, PedigreeMode.AutosomalDominant).Culprits);
        Assert.True(PedigreeChecker.Verdict(c, PedigreeMode.XRecessive).Consistent);
    }

    [Fact]
    public void APedigreeIsEditedAndLaidOut()
    {
        var p = Pedigree.Start;
        var kid = p.AddChild(0, true)!.Value;
        Assert.NotNull(p.AddPartner(kid));
        var grandchild = p.AddChild(kid, false, true)!.Value;
        Assert.Null(p.AddChild(grandchild, true));
        p.Remove(kid);
        Assert.Equal(2, p.People.Count);
        Assert.False(p.CanRemove(0));
        Assert.Equal(p, Pedigree.Decode(p.Encoded));
        var labels = Pedigree.Examples.First(e => e.Id == "xr").Pedigree.Layout().Select(s => s.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.Contains("III-3", labels);
    }
}
