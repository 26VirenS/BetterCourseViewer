using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Cell Division's model (Tools/Biology/Model/CellDivision*.cs) against spec §5.2: the counts at every step of mitosis and
/// meiosis as the tables have them, the misconceptions the tool is there for (a replicated chromosome is one chromosome,
/// ploidy counts sets, DNA copies once and only in S, meiosis I parts homologs and meiosis II sisters, homologs are there
/// in mitosis too), crossing over, independent assortment, nondisjunction, plant and animal cells, the step-through
/// between keyframes, the Compare page's pairs and the cell cycle's wheel. Plain .NET: no window is opened.
/// </summary>
public class CellDivisionTests
{
    private static DivisionSequence Mitosis(DivisionOptions? o = null) => DivisionModel.Sequence(DivisionProcess.Mitosis, o ?? new DivisionOptions());
    private static DivisionSequence Meiosis(DivisionOptions? o = null) => DivisionModel.Sequence(DivisionProcess.Meiosis, o ?? new DivisionOptions());

    [Fact]
    public void TheSelfCheckLinesAreAllOK()
    {
        var lines = BioSelfCheck.Run().Lines.Where(l => l.StartsWith("bio-check divide.", StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count >= 48, $"only {lines.Count} divide lines");
        Assert.All(lines, l => Assert.EndsWith(" OK", l));
    }

    [Fact]
    public void MitosisCountsAreTheTablesAtEveryStep()
    {
        var s = Mitosis();
        Assert.Equal(new[] { "G1", "S phase", "G2", "Prophase", "Prometaphase", "Metaphase", "Anaphase", "Telophase", "Cytokinesis" }, s.Stages.Select(x => x.Title));
        var want = new (string Chr, string Human, string Cds, string Ploidy, string Dna)[]
        {
            ("4", "46", "4", "2n", "2c"), ("4", "46", "8", "2n", "4c"), ("4", "46", "8", "2n", "4c"), ("4", "46", "8", "2n", "4c"),
            ("4", "46", "8", "2n", "4c"), ("4", "46", "8", "2n", "4c"), ("8", "92", "8", "4n in one cell (two diploid sets moving apart)", "4c"),
            ("8 (in two nuclei)", "92 (in two nuclei)", "8", "2n per nucleus", "4c total"), ("4 per cell", "46 per cell", "4 per cell", "2n", "2c"),
        };
        for (var i = 0; i < want.Length; i++)
        {
            var c = s.Counts[i];
            Assert.Equal(want[i], (c.Chromosomes, c.ChromosomesHuman, c.Chromatids, c.Ploidy, c.Dna));
        }
    }

    [Fact]
    public void MeiosisCountsAreTheTablesAtEveryStep()
    {
        var s = Meiosis();
        Assert.Equal(11, s.Stages.Count);
        var want = new (string Chr, string Cds, string Ploidy, string Dna)[]
        {
            ("4", "4", "2n", "2c"), ("4", "8", "2n", "4c"), ("4", "8", "2n", "4c"), ("4 (as 2 tetrads)", "8", "2n", "4c"), ("4", "8", "2n", "4c"),
            ("4 (2 to each pole)", "8", "2n → two sets of n", "4c"), ("2 per cell", "4 per cell", "n (haploid)", "2c"), ("2", "4", "n", "2c"),
            ("2", "4", "n", "2c"), ("4 (2 to each pole)", "4", "n per pole", "2c"), ("2 per cell", "2 per cell", "n", "1c"),
        };
        for (var i = 0; i < want.Length; i++)
        {
            var c = s.Counts[i];
            Assert.Equal(want[i], (c.Chromosomes, c.Chromatids, c.Ploidy, c.Dna));
        }
        Assert.Equal("46 (as 23 tetrads)", s.Counts[3].ChromosomesHuman);
        Assert.Contains("23 in humans", s.Stages[3].Text);
    }

    [Fact]
    public void AReplicatedChromosomeIsOneChromosome()
    {
        // (S phase doubles the DNA molecules, never the chromosomes; anaphase doubles the chromosomes, never the DNA)
        var s = Mitosis();
        Assert.Equal(s.Counts[0].Chromosomes, s.Counts[1].Chromosomes);
        Assert.Equal("4", s.Counts[0].Chromatids);
        Assert.Equal("8", s.Counts[1].Chromatids);
        Assert.Equal("4c", s.Counts[5].Dna);
        Assert.Equal("4c", s.Counts[6].Dna);
        Assert.Equal("8", s.Counts[6].Chromosomes);
        Assert.Equal("Not copied yet", s.Counts[0].Sisters);
        Assert.Equal("Yes", s.Counts[5].Sisters);
        Assert.StartsWith("No: separated", s.Counts[6].Sisters);
    }

    [Fact]
    public void PloidyCountsSetsNotChromatids()
    {
        // (after meiosis I each cell is haploid with every chromosome still two chromatids; DNA copies once, before meiosis I)
        var s = Meiosis();
        var top = new DivTally(s.Keyframes[6].Frame.Chromatids.Where(c => c.Cell == 0), 2);
        Assert.Equal("n", top.Ploidy);
        Assert.Equal(2, top.Chromosomes);
        Assert.Equal(4, top.Chromatids);
        Assert.Equal(2.0, top.C, 9);
        var before = new DivTally(s.Keyframes[7].Frame.Chromatids.Where(c => c.Cell == 0), 2);
        Assert.Equal(top.Chromatids, before.Chromatids);
        Assert.Contains("No DNA replication between meiosis I and II", s.Stages[7].Text);
        Assert.Equal("No", s.Counts[6].Homologs);
    }

    [Fact]
    public void HomologsAreThereInMitosisButOnlyPairInMeiosisI()
    {
        var m = Mitosis();
        Assert.All(m.Counts.Take(8), c => Assert.StartsWith("Yes", c.Homologs));
        Assert.Equal("Yes, but they don't pair", m.Counts[5].Homologs);
        var e = Meiosis();
        Assert.Equal("Yes, paired as tetrads", e.Counts[3].Homologs);
        Assert.Equal("Yes, paired as tetrads", e.Counts[4].Homologs);
        Assert.Equal("Yes, separating", e.Counts[5].Homologs);
        Assert.True(e.Keyframes[3].Paired && e.Keyframes[4].Paired && !m.Keyframes.Any(k => k.Paired));
    }

    [Fact]
    public void MeiosisIPartsHomologsAndMeiosisIIPartsSisters()
    {
        var s = Meiosis();
        // (anaphase I: both sisters of each homolog to one pole; the homologs to opposite poles)
        var ana1 = s.Keyframes[5].Frame.Chromatids;
        foreach (var g in ana1.GroupBy(c => c.Id.Chromosome)) Assert.Single(g.Select(c => c.Pole).Distinct());
        foreach (var g in ana1.GroupBy(c => c.Id.Pair)) Assert.Equal(2, g.Select(c => c.Pole).Distinct().Count());
        // (anaphase II: the sisters to opposite poles)
        var ana2 = s.Keyframes[9].Frame.Chromatids;
        foreach (var g in ana2.GroupBy(c => c.Id.Chromosome)) Assert.Equal(2, g.Select(c => c.Pole).Distinct().Count());
        // (mitosis's anaphase: sisters apart)
        var ana = Mitosis().Keyframes[6].Frame.Chromatids;
        foreach (var g in ana.GroupBy(c => c.Id.Chromosome)) Assert.Equal(2, g.Select(c => c.Pole).Distinct().Count());
    }

    [Fact]
    public void CrossingOverSwapsTheEndsOfTwoNonSisterChromatids()
    {
        var s = Meiosis();
        Assert.All(s.Keyframes.Take(3), k => Assert.All(k.Frame.Chromatids, c => Assert.Equal(0, c.Recombined)));
        var rec = s.Keyframes[3].Frame.Chromatids.Where(c => c.Recombined > 0.5).ToList();
        Assert.Equal(2, rec.Count);
        Assert.All(rec, c => Assert.Equal(0, c.Id.Pair));
        Assert.Equal(new[] { 0, 1 }, rec.Select(c => c.Id.Homolog).OrderBy(x => x));
        Assert.Equal(4, s.Products.Select(p => string.Join(",", p.Chromatids.Select(c => $"{c.Id.Chromosome}{(c.Recombined > 0.5 ? "r" : "")}"))).Distinct().Count());
        // (the chiasma's place moves where the swapped part starts)
        var shapeNear = DivGeometry.Shape(s.Keyframes[4].Frame.Chromatids.First(c => c.Recombined > 0.5), DivGeometry.Pairs[0], 0.2);
        var shapeFar = DivGeometry.Shape(s.Keyframes[4].Frame.Chromatids.First(c => c.Recombined > 0.5), DivGeometry.Pairs[0], 0.8);
        Assert.True(shapeNear.DistalStart < shapeFar.DistalStart);
        var off = Meiosis(new DivisionOptions { CrossingOver = false });
        Assert.All(off.Keyframes, k => Assert.All(k.Frame.Chromatids, c => Assert.Equal(0, c.Recombined)));
        Assert.DoesNotContain(off.Keyframes[3].Labels, l => l.Part == "chiasma");
    }

    [Fact]
    public void IndependentAssortmentFlipsWhichHomologGoesWhere()
    {
        var plain = Meiosis(new DivisionOptions { CrossingOver = false });
        var flipped = Meiosis(new DivisionOptions { CrossingOver = false, Flipped = new[] { false, true, false } });
        // (the short pair's mother's homolog: top cell (0) unflipped, bottom cell (2) flipped)
        int CellOf(DivisionSequence s, int pair, int homolog) => s.Keyframes[6].Frame.Chromatids.First(c => c.Id.Pair == pair && c.Id.Homolog == homolog).Cell;
        Assert.Equal(0, CellOf(plain, 1, 0));
        Assert.Equal(2, CellOf(flipped, 1, 0));
        Assert.Equal(CellOf(plain, 0, 0), CellOf(flipped, 0, 0));
        Assert.Equal("2ⁿ = 8 possible assortments; humans 2²³ ≈ 8.4 million", DivisionModel.Assortments(3));
    }

    [Fact]
    public void NondisjunctionMakesNPlusOneAndNMinusOneGametes()
    {
        var one = Meiosis(new DivisionOptions { Nondisjunction = DivisionNondisjunction.MeiosisI });
        Assert.Equal(new[] { "n+1", "n+1", "n−1", "n−1" }, one.Products.Select(p => p.Tally.Ploidy));
        Assert.Equal(new[] { 3, 3, 1, 1 }, one.Products.Select(p => p.Tally.Chromosomes));
        Assert.Equal("Top left · top right · bottom left · bottom right", one.Counts[10].Listing);
        Assert.Contains("trisomy 21", one.Stages[10].Notice[^1]);
        var two = Meiosis(new DivisionOptions { Nondisjunction = DivisionNondisjunction.MeiosisII });
        Assert.Equal(new[] { "n+1", "n−1", "n", "n" }, two.Products.Select(p => p.Tally.Ploidy));
        Assert.Equal("Partly: one chromosome's sisters did not separate", two.Counts[9].Sisters);
        // (1.25c and 0.75c, rounded half-up as every bio tool rounds)
        Assert.Equal("1.3c · 0.8c · 1c · 1c", Meiosis(new DivisionOptions { Pairs = 3, Nondisjunction = DivisionNondisjunction.MeiosisII }).Counts[10].Dna);
    }

    [Fact]
    public void PlantCellsHaveNoCentriolesAndACellPlate()
    {
        var animal = Mitosis();
        var plant = Mitosis(new DivisionOptions { Plant = true });
        Assert.All(plant.Keyframes, k => Assert.All(k.Frame.Poles, p => Assert.Equal(0, p.Centrosome)));
        Assert.Contains(animal.Keyframes[5].Frame.Poles, p => p.Centrosome > 0);
        Assert.Empty(plant.Keyframes[7].Frame.Furrows);
        Assert.True(plant.Keyframes[7].Frame.CellPlates[0].Alpha > 0);
        Assert.True(animal.Keyframes[7].Frame.Furrows[0].Alpha > 0);
        Assert.Contains("cell plate", plant.Stages[8].Text);
        Assert.Contains("cleavage furrow", animal.Stages[8].Text);
        Assert.Contains(plant.Keyframes[0].Labels, l => l.Part == "wall");
        Assert.DoesNotContain(plant.Keyframes.SelectMany(k => k.Labels), l => l.Part == "centrosome");
        // (the plant's daughters are its box's halves: still 1.9 × 1.9 in all)
        Assert.Equal(1.9, plant.Keyframes[8].Frame.Bounds.Height, 1);
    }

    [Fact]
    public void SixChromosomesCountTheSameWay()
    {
        var o = new DivisionOptions { Pairs = 3 };
        Assert.Equal(new[] { "6", "6", "6", "6", "6", "6", "12", "12 (in two nuclei)", "6 per cell" }, Mitosis(o).Counts.Select(c => c.Chromosomes));
        Assert.Equal("6 (as 3 tetrads)", Meiosis(o).Counts[3].Chromosomes);
        Assert.Equal("3 per cell", Meiosis(o).Counts[10].Chromosomes);
        Assert.Equal(12, Mitosis(o).Keyframes[0].Frame.Chromatids.Length);
    }

    [Fact]
    public void EveryLabelPointsAtSomethingShown()
    {
        foreach (var o in new[] { new DivisionOptions(), new DivisionOptions { Plant = true }, new DivisionOptions { Pairs = 3, Flipped = new[] { false, true, true } },
                     new DivisionOptions { Nondisjunction = DivisionNondisjunction.MeiosisI }, new DivisionOptions { Plant = true, CrossingOver = false, Nondisjunction = DivisionNondisjunction.MeiosisII } })
            foreach (var s in new[] { Mitosis(o), Meiosis(o) })
                foreach (var k in s.Keyframes)
                {
                    var shapes = k.Frame.Shapes(o.Chiasma);
                    var b = k.Frame.Bounds;
                    foreach (var l in k.Labels)
                    {
                        var (p, visible) = DivPainter.Anchor(l.Anchor, k.Frame, shapes);
                        Assert.True(visible, $"{s.Process} {l.Text} hidden");
                        Assert.InRange(p.X, b.MinX - 0.05, b.MaxX + 0.05);
                        Assert.InRange(p.Y, b.MinY - 0.05, b.MaxY + 0.05);
                        Assert.True(l.Text.Length <= 40, l.Text);
                    }
                    Assert.Equal(k.Labels.Count, k.Labels.Select(l => l.Id).Distinct().Count());
                }
    }

    [Fact]
    public void TheStepThroughMovesBetweenKeyframes()
    {
        var now = 0.0;
        var s = Mitosis();
        var c = new BioStepController(s.Steps, clock: () => now);
        Assert.Equal(9, c.Steps.Count);
        Assert.Equal(1, c.Steps[0].Group);
        Assert.Equal(6, c.Steps[8].Group);
        Assert.True(c.JumpToGroup(3));
        Assert.Equal(5, c.Index);
        now = 10;
        c.Next();
        now = 10.3;
        var ph = c.Phase();
        Assert.True(ph.IsMoving && ph.From == 5 && ph.To == 6);
        var mid = s.Keyframes[5].Frame.Mixed(s.Keyframes[6].Frame, ph.T);
        // (a chromatid part way from the plate to its pole)
        var y = Math.Abs(mid.Chromatids[0].C.Y);
        Assert.InRange(y, 0.01, 0.61);
        now = 11;
        Assert.False(c.IsMoving());
        Assert.Equal(BioStepPhase.At(6), c.Phase());
        // (painting a frame part way: a chromatid drawn for each that exists, a centromere dot per chromosome)
        var (prims, _) = DivPainter.Paint(mid, 0.5);
        Assert.Equal(8, prims.OfType<DivChromatidPrimitive>().Count());
        Assert.Contains(prims, p => p is DivFill);
    }

    [Fact]
    public void NewOptionsKeepTheStepAndTheLongPairIsNeverFlipped()
    {
        CellDivisionModel.Set(new DivisionOptions());
        CellDivisionModel.MeiosisSteps.Go(6, animated: false);
        CellDivisionModel.Set(CellDivisionModel.Options with { Plant = true });
        Assert.Equal(6, CellDivisionModel.MeiosisSteps.Index);
        Assert.True(CellDivisionModel.Meiosis.Options.Plant);
        CellDivisionModel.Set(new DivisionOptions { Flipped = new[] { true, true, false }, Chiasma = 2, Pairs = 9 });
        Assert.Equal(new[] { false, true, false }, CellDivisionModel.Options.Flipped);
        Assert.Equal(DivisionOptions.ChiasmaMax, CellDivisionModel.Options.Chiasma);
        Assert.Equal(3, CellDivisionModel.Options.Pairs);
        CellDivisionModel.Flip(0);
        Assert.False(CellDivisionModel.Options.Flipped[0]);
        CellDivisionModel.Flip(2);
        Assert.True(CellDivisionModel.Options.Flipped[2]);
        CellDivisionModel.Set(new DivisionOptions());
        CellDivisionModel.MeiosisSteps.Go(0, animated: false);
    }

    [Fact]
    public void TheComparePagePairsTheStages()
    {
        var m = Mitosis();
        var e = Meiosis();
        var pairs = DivisionModel.CompareSteps.Select(s => (m.Stages[s.Mitosis].Key, e.Stages[s.Meiosis].Key)).ToList();
        Assert.Contains((DivStageKey.Prophase, DivStageKey.Prophase1), pairs);
        Assert.Contains((DivStageKey.Metaphase, DivStageKey.Metaphase1), pairs);
        Assert.Contains((DivStageKey.Anaphase, DivStageKey.Anaphase1), pairs);
        Assert.Equal((DivStageKey.Cytokinesis, DivStageKey.Telophase2), pairs[^1]);
        Assert.All(DivisionModel.CompareSteps, s => Assert.All(s.Rows, r => Assert.InRange(r, 0, DivisionModel.CompareTable.Length - 1)));
        Assert.Equal("Homologous pairs (metaphase I); individual chromosomes (metaphase II)", DivisionModel.CompareTable[5][2]);
    }

    [Fact]
    public void TheCellCycleWheelIsInProportion()
    {
        var arcs = DivisionModel.CycleArcs;
        Assert.Equal(new[] { "g1", "s", "g2", "m" }, arcs.Select(a => a.Phase.Id));
        Assert.Equal(new[] { 165.0, 120, 60, 15 }, arcs.Select(a => Math.Round(a.End - a.Start, 9)));
        // (M centred at the top; the checkpoints on the G1/S and G2/M boundaries, the spindle's in M)
        Assert.Equal(360, (arcs[3].Start + arcs[3].End) / 2, 9);
        Assert.Equal(arcs[0].End, DivisionModel.Checkpoints.First(c => c.Id == "g1s").Angle, 9);
        Assert.Equal(arcs[2].End, DivisionModel.Checkpoints.First(c => c.Id == "g2m").Angle, 9);
        Assert.Equal(1, DivisionModel.CyclinG1S(11), 9);
        Assert.True(DivisionModel.CyclinM(23.4) > 0.9 && DivisionModel.CyclinM(24) < 0.1 && DivisionModel.CyclinM(5) < 0.1);
    }
}
