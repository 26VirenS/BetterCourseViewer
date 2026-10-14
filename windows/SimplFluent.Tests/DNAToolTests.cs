using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// DNA &amp; RNA's model (Tools/Biology/Model: DNAToolModel, DnaHelix, DnaNucleotides, DnaReplication, DnaRna) against the
/// specification (§4.2, §6.5–§6.7, §6.11): the double helix's handedness, strands and cartoon, unzipping and the grooves;
/// the Sequence page's facts; the nucleotides' parts; replication's steps; the cloverleaf's layout and the tRNA's L. Its
/// bio-check lines (DnaChecks) must all be OK. Plain .NET: no window is opened.
/// </summary>
public class DNAToolTests
{
    [Fact]
    public void ItsBioCheckLinesAreAllOK()
    {
        var r = new BioCheckReport();
        DnaSelfCheck.Run(r);
        var failed = r.Lines.Where(l => l.EndsWith(" FAIL", StringComparison.Ordinal)).ToList();
        Assert.True(failed.Count == 0, "failed: " + string.Join("\n", failed));
        Assert.True(r.Passed >= 25, $"only {r.Passed} checks ran");
        Assert.All(r.Lines, l => Assert.Matches(@"^bio-check \S+ \S+ (OK|FAIL)$", l));
    }

    // ---- the double helix (§4.2, §6.7) ----

    [Fact]
    public void TheHelixIsRightHandedAndAntiparallel()
    {
        var d = BioBDNA.Build("ATGCGTACGTTAGCCATGCA");
        var up = DnaHelix.BackbonePath(d, 1);
        var down = DnaHelix.BackbonePath(d, 2);
        // (strand 1 5′→3′ up the axis, strand 2 5′→3′ down it)
        Assert.True(up.Zip(up.Skip(1)).All(p => p.Second.Y > p.First.Y));
        Assert.True(down.Zip(down.Skip(1)).All(p => p.Second.Y < p.First.Y));
        // (rising, each P turns counter-clockwise seen from above: right-handed; §6.7's test vector)
        var p0 = up[0];
        var p1 = up[1];
        Assert.True(BioV.Cross(new BioVec(p0.X, 0, p0.Z), new BioVec(p1.X, 0, p1.Z)).Y > 0);
        Assert.Equal(5.019, p1.X, 3);
        Assert.Equal(3.400, p1.Y, 3);
        Assert.Equal(7.362, p1.Z, 3);
        Assert.Equal(35.7, 10.5 * BioBDNA.Rise, 9);
    }

    [Fact]
    public void ThePurineBoxIsLongerAndThePairsHaveTwoOrThreeDashes()
    {
        var d = BioBDNA.Build("AG");
        var boxes = DnaHelix.Boxes(d);
        Assert.Equal(4, boxes.Count);
        var a = boxes.Single(b => b.Pair == 0 && b.Strand == 1);
        var t = boxes.Single(b => b.Pair == 0 && b.Strand == 2);
        Assert.Equal('T', t.Base);
        var span = BioV.Distance(a.Start, t.Start) - DnaHelix.Gap;
        Assert.Equal(0.58 * span, a.Length, 9);
        Assert.Equal(0.42 * span, t.Length, 9);
        Assert.Equal(10.86, BioV.Distance(a.Start, t.Start), 2);
        Assert.Equal(2, DnaHelix.CartoonHBonds(a, t).Count);
        var g = boxes.Single(b => b.Pair == 1 && b.Strand == 1);
        var c = boxes.Single(b => b.Pair == 1 && b.Strand == 2);
        Assert.Equal(3, DnaHelix.CartoonHBonds(g, c).Count);
        Assert.Equal(new[] { 2.87, 2.79, 3.00 }, DnaHelix.PairBonds(d, 1).Select(x => Math.Round(x.Distance, 2)));
    }

    [Fact]
    public void UnzippingOpensFromTheBottomEnd()
    {
        var d = BioBDNA.Build("ATGCGTACGT");
        Assert.Same(d, DnaHelix.Opened(d, 0));
        var half = DnaHelix.Opened(d, 0.5);
        double Span(BioDuplex x, int k) => BioV.Distance(x.Pos(1, k, "C1'")!.Value, x.Pos(2, k, "C1'")!.Value);
        Assert.Equal(Span(d, 0) + DnaHelix.Separation, Span(half, 0), 9);
        Assert.Equal(Span(d, 4) + DnaHelix.Separation, Span(half, 4), 9);
        Assert.Equal(Span(d, 5), Span(half, 5), 9);
        Assert.Equal(0.5, DnaHelix.Opening(2, 10, 0.25), 9);
        // (the H-bond dashes fade as a pair opens: none left on a pair fully apart)
        var scene = DnaHelix.Build(new DnaHelixOptions("ATGCGTACGT", Unzip: 0.5));
        Assert.DoesNotContain(scene.Content.Items, i => i.Id == "bp:0:hb");
        Assert.Contains(scene.Content.Items, i => i.Id == "bp:9:hb" && !i.Material.IsTranslucent);
    }

    [Fact]
    public void TheGroovesAreAboutAQuarterAndThreeQuartersWide()
    {
        Assert.Equal(148.45, DnaHelix.MinorArc, 2);
        Assert.Equal(211.55, DnaHelix.MajorArc, 2);
        var (minor, major) = DnaHelix.GrooveWidths();
        Assert.InRange(minor, 6.5, 6.7);
        Assert.InRange(major, 11.5, 11.7);
    }

    [Fact]
    public void TheAtomStylesStayUnderTheTriangleBudget()
    {
        var forty = string.Concat(Enumerable.Repeat("ATGCGTACGT", 4));
        foreach (var style in new[] { DnaStyle.Atoms, DnaStyle.SpaceFill, DnaStyle.Cartoon })
        {
            var s = DnaHelix.Build(new DnaHelixOptions(forty, style, Ends: true, Grooves: true, Measure: true));
            Assert.InRange(s.Content.Items.Sum(i => i.Mesh.TriangleCount), 1, 299_999);
            Assert.True(s.Content.Labels.Count <= BioLabelLayout.MaxLabels);
        }
    }

    // ---- the Sequence page and the model's state ----

    [Fact]
    public void TheStrandKeepsItsLastGoodSequence()
    {
        DNAToolModel.Quietly(() => DNAToolModel.SetSequence(DNAToolModel.DefaultSequence));
        DNAToolModel.Quietly(() => DNAToolModel.SetText("ATGXXC"));
        Assert.Equal(DNAToolModel.DefaultSequence, DNAToolModel.Sequence);
        Assert.NotNull(DNAToolModel.Input.Message);
        DNAToolModel.Quietly(() => DNAToolModel.SetText("5′-ggatcc-3′"));
        Assert.Equal("GGATCC", DNAToolModel.Sequence);
        Assert.Equal("GGATCC", DNAToolModel.HelixSequence);
        DNAToolModel.Quietly(() => DNAToolModel.SetSequence(DNAToolModel.DefaultSequence));
        Assert.Equal(20, DNAToolModel.RandomStrand(7).Length);
        Assert.Equal(DNAToolModel.RandomStrand(7), DNAToolModel.RandomStrand(7));
        Assert.Equal(5, DNAToolModel.SelectedPair("bp:5:1:base"));
        Assert.Equal(2, DNAToolModel.SelectedStrand("bp:5:2"));
        Assert.Null(DNAToolModel.SelectedPair("groove:major"));
    }

    // ---- the nucleotides (§6.5, §6.6) ----

    [Fact]
    public void EveryNucleotideAtomHasItsPart()
    {
        foreach (var code in DnaNucleotides.Codes)
        {
            var m = BioData.Molecule(code)!;
            var parts = DnaNucleotides.Parts(m);
            Assert.Equal(m.Atoms.Count, parts.Count);
            Assert.Equal(DnaPart.Phosphate, parts["O5'"]);
            Assert.Equal(DnaPart.Sugar, parts["C1'"]);
            Assert.Equal(DnaPart.Base, parts[DnaNucleotides.GlycosidicN(DnaNucleotides.Base(code))]);
            Assert.Equal(DnaNucleotides.IsRibose(code), m.Atom("O2'") != null);
        }
        Assert.Equal("3′", DnaNucleotides.Number("C3'", 'A'));
        Assert.Equal("9", DnaNucleotides.Number("N9", 'G'));
        Assert.Null(DnaNucleotides.Number("N7", 'C'));
        Assert.Null(DnaNucleotides.Number("O3'", 'A'));
    }

    // ---- replication ----

    [Fact]
    public void ReplicationIsSemiconservative()
    {
        Assert.Equal(9, DnaReplication.Steps.Length);
        var last = DnaReplication.Frames[^1];
        for (var i = 0; i < DnaReplication.Length; i++)
        {
            Assert.Equal(DnaPieceKind.Dna, DnaReplication.Leading(i, last)?.Kind);
            Assert.Equal(DnaPieceKind.Dna, DnaReplication.Lagging(i, last)?.Kind);
        }
        // (primers are RNA: U where DNA would have T)
        Assert.Equal('U', DnaReplication.NewLetter(true, 1, rna: true));
        Assert.Equal('T', DnaReplication.NewLetter(true, 1, rna: false));
        Assert.Null(DnaReplication.Leading(10, DnaReplication.Frames[0]));
    }

    // ---- RNA (§6.11) ----

    [Fact]
    public void TheCloverleafFollowsTheLayoutRules()
    {
        var (p, codon, _) = DnaTrna.Cloverleaf();
        Assert.Equal(76, p.Count);
        Assert.Equal(DnaTrna.Across, p[72].X - p[1].X, 9);
        Assert.Equal(p[1].Y, p[72].Y, 9);
        Assert.True(p[76].Y > p[1].Y);
        Assert.True(p[13].X < p[10].X);
        Assert.True(p[31].Y < p[27].Y);
        Assert.True(p[53].X > p[49].X);
        Assert.Equal(3, codon.Length);
        Assert.Equal("acceptor", DnaTrna.ArmOf(76).Key);
        Assert.Equal("anticodon", DnaTrna.ArmOf(35).Key);
        Assert.Equal(69, DnaTrna.Partner(4));
        Assert.True(DnaTrna.IsWobble(4, 69));
        Assert.Equal("Ψ", DnaTrna.Modification(39));
        Assert.InRange(DnaTrna.Reach().Distance, 71, 73);
    }
}
