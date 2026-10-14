using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// Gene Expression's models (Tools/Biology/Model/GeneExpression*.cs) against the specification's test vectors (§4.3,
/// §6.1, §6.12): the genes as the step-through builds them, transcription a nucleotide a step and translation a codon a
/// step ending in MAFSDWQKG, the codon table's grid, wheel and lookup, the six reading frames and their ORFs, and the
/// mutation classifier (§6.12's table, the sickle-cell mutation). Plain .NET: no window is opened.
/// </summary>
public class GeneExpressionTests
{
    [Fact]
    public void ItsSelfCheckLinesAreAllOK()
    {
        var r = new BioCheckReport();
        BioSelfCheck.GeneGeneChecks(r);
        BioSelfCheck.GeneProcessChecks(r);
        BioSelfCheck.GeneCodonChecks(r);
        BioSelfCheck.GeneFrameChecks(r);
        BioSelfCheck.GeneMutationChecks(r);
        var failed = r.Lines.Where(l => l.EndsWith(" FAIL", StringComparison.Ordinal)).ToList();
        Assert.True(failed.Count == 0, "failed: " + string.Join("\n", failed));
        Assert.Equal(40, r.Passed);
        Assert.Contains(BioSelfCheck.Run().Lines, l => l.StartsWith("bio-check gx.mutation.sickle ", StringComparison.Ordinal));
    }

    // ---- the genes (§6.12) ----

    [Fact]
    public void TheTeachingGeneIsTranscribedSplicedAndTranslated()
    {
        var t = BioGenes.Teaching;
        var g = GXGene.Teaching(GXCell.Eukaryote);
        Assert.Equal(t.Template, g.Template);
        Assert.Equal(t.PreMRNA, g.PreMRNA);
        Assert.Equal(t.MatureMRNA, g.Mature);
        Assert.Equal(13, g.CdsStart);
        Assert.Equal("MAFSDWQKG", g.Protein);
        Assert.Equal("UAA", g.Codons[^1].Codon);
        Assert.Equal(t.Cds, g.CdsDNA);
        Assert.Equal("GCAATAAAGCC", g.DownstreamDNA);
        Assert.Equal((3, "AGC", 2), g.SplitCodon);
        // (the RNA matches the coding strand with U for T, never the template)
        Assert.Equal(BioSeq.Transcribe(t.Coding[31..]), g.PreMRNA);
        Assert.NotEqual(BioSeq.Transcribe(t.Template[31..]), g.PreMRNA);
    }

    [Fact]
    public void AProkaryoteHasNoIntronAndMakesTheSameProtein()
    {
        var p = GXGene.Teaching(GXCell.Prokaryote);
        Assert.Empty(p.Introns);
        Assert.Equal(85, p.Coding.Length);
        Assert.Equal(BioGenes.Teaching.MatureMRNA, p.Mature);
        Assert.Equal("MAFSDWQKG", p.Protein);
    }

    [Fact]
    public void BetaGlobinTranslatesToItsProtein()
    {
        var h = GXGene.Hbb(GXCell.Eukaryote);
        Assert.Equal(BioGenes.HbbProtein, h.Protein);
        Assert.Equal(147, h.Protein.Length);
        Assert.True(h.HasStop);
    }

    [Fact]
    public void ASequenceOfOnesOwnStartsAtItsFirstAug()
    {
        Assert.Equal("MK", GXGene.Own("ccATGAAATAGgg", GXCell.Eukaryote)?.Protein);
        Assert.Equal("", GXGene.Own("CCCGGG", GXCell.Eukaryote)?.Protein);
        Assert.Null(GXGene.Own("", GXCell.Eukaryote));
        var open = GXGene.Own("AUGGCC", GXCell.Eukaryote);
        Assert.Equal("MA", open?.Protein);
        Assert.False(open?.HasStop);
    }

    // ---- the step-through ----

    [Fact]
    public void TheStepThroughGoesANucleotideThenACodonAPress()
    {
        var g = GXGene.Teaching(GXCell.Eukaryote);
        var p = new GXProcess(g);
        Assert.Equal(99, p.Steps.Count);
        var tx = p.StepsOf(GXStage.TxElong)!.Value;
        Assert.Equal(81, tx.Hi - tx.Lo + 1);
        for (var i = tx.Lo; i <= tx.Hi; i++)
        {
            var k = i - tx.Lo + 1;
            Assert.Equal(k, p.RnaAt(i).Length);
            // (each nucleotide pairs with the template base under it: A→U, T→A, G→C, C→G)
            Assert.Equal(BioSeq.Pair(g.Template[g.TxStart + k - 1], rna: true), p.RnaAt(i)[^1]);
        }
        var tl = p.StepsOf(GXStage.TlElong)!.Value;
        Assert.Equal(8, tl.Hi - tl.Lo + 1);
        for (var i = tl.Lo; i <= tl.Hi; i++) Assert.Equal(g.Protein[..(i - tl.Lo + 2)], p.ChainAt(i));
        Assert.Contains("MAFSDWQKG", p.Steps[^1].Text);
        Assert.All(p.Steps, s => Assert.InRange(s.Title.Length, 1, 40));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, p.Steps.Select(s => s.Group ?? 0).Distinct());
        // (termination is by a release factor, a protein, not a tRNA)
        var end = p.StepsOf(GXStage.TlEnd)!.Value.Lo;
        Assert.Contains("release factor", p.Steps[end].Text);
        Assert.Contains("UAA", p.Steps[end].Text);
    }

    [Fact]
    public void AProkaryoteSkipsRnaProcessing()
    {
        var p = new GXProcess(GXGene.Teaching(GXCell.Prokaryote));
        Assert.DoesNotContain(p.Frames, f => f.Stage.Group() == 3);
        Assert.Contains(p.Steps, s => s.Notices.Contains("coupled"));
        Assert.Contains("Shine–Dalgarno", p.Steps[p.StepsOf(GXStage.TlInit)!.Value.Lo].Text);
    }

    [Fact]
    public void TheControllerDrivesTheSteps()
    {
        var now = 0.0;
        var p = new GXProcess(GXGene.Teaching(GXCell.Eukaryote));
        var c = new BioStepController(p.Steps, clock: () => now);
        Assert.True(c.JumpToGroup(4));
        Assert.Equal(GXStage.TlInit, p.Frames[c.Index].Stage);
        c.Next();
        now = 0.3;
        var phase = c.Phase();
        var mid = GXScene.Make(p, phase.From, phase.To, phase.T);
        // (half way: the tRNA is in, the peptide bond forming, the ribosome not yet moved)
        Assert.Equal(0, mid.Ribosome);
        Assert.Contains(mid.Trnas, x => x.Codon == 1);
    }

    // ---- the codon table (§6.1) ----

    [Fact]
    public void TheCodonTableAndItsLookup()
    {
        var rows = GXCodonTable.GridRows;
        Assert.Equal(16, rows.Count);
        Assert.Equal(new[] { "UUU", "UCU", "UAU", "UGU" }, rows[0]);
        Assert.Equal(new[] { "GUG", "GCG", "GAG", "GGG" }, rows[15]);
        Assert.Equal(BioCode.Entries.Select(e => e.Codon), GXCodonTable.Sectors(2).Select(s => s.Prefix));
        Assert.Equal("AUG", GXCodonTable.Hit(0.67, 199));
        Assert.Equal(GXQuery.OfCodon("AUG"), GXCodonTable.Parse("atg"));
        Assert.Equal(GXQuery.OfAminoAcid('K'), GXCodonTable.Parse("lysine"));
        Assert.Equal(GXQueryKind.Stop, GXCodonTable.Parse("stop").Kind);
        Assert.Equal(GXQueryKind.Invalid, GXCodonTable.Parse("AUT").Kind);
        Assert.Equal(6, GXCodonTable.Matches(GXCodonTable.Parse("Leu")).Count);
        Assert.Equal(64, GXCodonTable.WheelLabels.Sum(l => l.Codons.Count));
    }

    // ---- reading frames ----

    [Fact]
    public void TheReadingFramesFindTheTeachingGenesOrf()
    {
        var rows = GXReadingFrames.Rows(BioGenes.Teaching.MatureMRNA, reverse: false);
        Assert.Equal("LSATMAFSDWQKG*AIK", new string(rows[1].Cells.Select(c => c.Aa).ToArray()));
        var orfs = BioSeq.Orfs(BioSeq.ToDna(BioGenes.Teaching.MatureMRNA), minCodons: 10, reverseStrand: true);
        Assert.Equal(2, orfs[0].Frame);
        Assert.Equal(14, orfs[0].Start);
        Assert.Equal("MAFSDWQKG", orfs[0].Protein);
        Assert.Equal(6, GXReadingFrames.Rows("ATGGCCTAA", reverse: true).Count);
    }

    // ---- mutations (§6.12) ----

    [Theory]
    [InlineData("c.9C>T", GXMutationKind.Silent, "MAFSDWQKG")]
    [InlineData("c.5C>T", GXMutationKind.Missense, "MVFSDWQKG")]
    [InlineData("c.19C>T", GXMutationKind.Nonsense, "MAFSDW")]
    [InlineData("c.6_7insA", GXMutationKind.Frameshift, "MAIQRLAERL")]
    [InlineData("c.7del", GXMutationKind.Frameshift, "MASAIGRKA")]
    [InlineData("c.13_15del", GXMutationKind.InFrameDeletion, "MAFSWQKG")]
    [InlineData("c.3G>A", GXMutationKind.StartLoss, "")]
    [InlineData("c.28T>C", GXMutationKind.StopLoss, "MAFSDWQKG")]
    [InlineData("c.6_7insGGC", GXMutationKind.InFrameInsertion, "MAGFSDWQKG")]
    public void TheTeachingGenesMutationsAreClassified(string hgvs, GXMutationKind kind, string proteinStart)
    {
        var cds = BioGenes.Teaching.Cds;
        var (edit, error) = GXEdit.Parse(hgvs, cds);
        Assert.Null(error);
        var res = GXMutations.Classify(cds, edit!, GXMutations.TeachingSource.Downstream);
        Assert.Equal(kind, res.Kind);
        Assert.Equal(hgvs, res.Hgvs);
        Assert.StartsWith(proteinStart, res.Mutant.Protein, StringComparison.Ordinal);
        // (only insertions and deletions that are not a multiple of 3 are frameshifts)
        Assert.Equal(kind == GXMutationKind.Frameshift, edit!.LengthChange % 3 != 0);
    }

    [Fact]
    public void TheSickleCellMutationIsMissenseGluToVal()
    {
        var s = GXMutations.Classify(BioGenes.HbbCDS, GXMutations.SickleEdit, matureNumbering: true);
        Assert.Equal(GXMutationKind.Missense, s.Kind);
        Assert.Equal("Glu7Val", s.Change);
        Assert.Equal('E', s.From?.One);
        Assert.Equal('V', s.To?.One);
        Assert.False(s.Conservative);
        Assert.Contains(s.Lines, l => l.Contains("Glu6Val"));
        Assert.Contains(s.Lines, l => l.Contains("negative (−1) → uncharged (0)"));
        Assert.StartsWith("MVHLTPVEKS", s.Mutant.Protein, StringComparison.Ordinal);
        Assert.Equal("GTG", s.Mutant.Codons[6]);
    }

    // ---- what another tool hands over ----

    [Fact]
    public void WhatAnotherToolHandsOverIsTaken()
    {
        var info = new Dictionary<string, string> { ["sequence"] = "ccATGAAATAGgg" };
        string? Take(string k) => info.Remove(k, out var v) ? v : null;
        Assert.Equal("express", GeneExpressionModel.TakeHandOff(Take));
        Assert.Equal(GXSource.Own, GeneExpressionModel.Source);
        Assert.Equal("MK", GeneExpressionModel.Process?.Gene.Protein);
        Assert.Equal(0, GeneExpressionModel.Steps.Index);
        Assert.Equal(GeneExpressionModel.Process?.Steps.Count, GeneExpressionModel.Steps.Steps.Count);
        info["codon"] = "ugg";
        Assert.Equal("codons", GeneExpressionModel.TakeHandOff(Take));
        Assert.Equal("UGG", GeneExpressionModel.Picked);
        info["mutation"] = "sickle";
        Assert.Equal("mutations", GeneExpressionModel.TakeHandOff(Take));
        Assert.Equal(GXMutationGene.Hbb, GeneExpressionModel.MutationGene);
        Assert.Equal(GXMutationKind.Missense, GeneExpressionModel.Result?.Kind);
        Assert.Null(GeneExpressionModel.TakeHandOff(Take));
    }

    [Fact]
    public void ATypedChangeIsCheckedAgainstTheSequence()
    {
        var cds = BioGenes.Teaching.Cds;
        Assert.NotNull(GXEdit.Parse("c.9A>T", cds).Error);
        Assert.Equal(new GXEdit.Insert(3, "G"), GXEdit.Parse("c.4dup", cds).Edit);
        Assert.Equal(new GXEdit.Delete(12, 14), GXEdit.Parse("13_15delGAT", cds).Edit);
        Assert.Null(GXEdit.Parse("c.6_8insA", cds).Edit);
        Assert.Equal((null, null), GXEdit.Parse("  ", cds));
    }
}
