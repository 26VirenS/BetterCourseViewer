using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The biology toolkit's shared models (Tools/Biology/Model), against the specification's test vectors — the same
/// vectors, numbers and tolerances as the Mac's self-check (BioSelfCheck.swift): the genetic code and the amino acids,
/// the molecule blocks, B-DNA, SplitMix64, chi-square, every §5.3.2 cross, the genes, the proteins and tRNA, the meshes,
/// the label layout and the step-through. Plain .NET: no window is opened.
/// </summary>
public class BiologyTests
{
    [Fact]
    public void TheSelfCheckIsAllOK()
    {
        var r = BioSelfCheck.Run();
        var failed = r.Lines.Where(l => l.EndsWith(" FAIL", StringComparison.Ordinal)).ToList();
        Assert.True(failed.Count == 0, "failed: " + string.Join("\n", failed));
        Assert.True(r.Passed >= 111, $"only {r.Passed} checks ran");
        Assert.All(r.Lines, l => Assert.Matches(@"^bio-check \S+ \S+ (OK|FAIL)$", l));
    }

    // ---- the genetic code (§6.1) and the amino acids (§6.2) ----

    [Fact]
    public void TheGeneticCode()
    {
        Assert.Equal(64, BioCode.Entries.Count);
        Assert.Equal(64, BioCode.Entries.Select(e => e.Codon).Distinct().Count());
        Assert.Equal('M', BioCode.AminoAcid("AUG"));
        Assert.Equal('W', BioCode.AminoAcid("UGG"));
        Assert.Equal('M', BioCode.AminoAcid("ATG"));
        Assert.Equal('X', BioCode.AminoAcid("ANG"));
        Assert.Null(BioCode.AminoAcid("AU"));
        Assert.Equal(new[] { "UAA", "UAG", "UGA" }, BioCode.Codons('*'));
        foreach (var (aa, n) in new[] { ('L', 6), ('S', 6), ('R', 6), ('A', 4), ('G', 4), ('P', 4), ('T', 4), ('V', 4), ('I', 3), ('F', 2), ('Y', 2), ('C', 2), ('H', 2), ('Q', 2), ('N', 2), ('K', 2), ('D', 2), ('E', 2), ('M', 1), ('W', 1) })
            Assert.Equal(n, BioCode.Codons(aa).Count);
        Assert.Equal("UAC", BioCode.Anticodon3To5("AUG"));
        Assert.Equal("CAU", BioCode.Anticodon5To3("AUG"));
        Assert.True(BioCode.IsStop("UGA") && BioCode.IsStart("ATG"));
    }

    [Fact]
    public void TheTwentyAminoAcids()
    {
        var all = BioAminoAcids.All;
        Assert.Equal(20, all.Count);
        Assert.Equal(new[] { 9, 6, 2, 3 }, BioAAClasses.All.Select(c => all.Count(a => a.Group == c)));
        Assert.All(all, a => Assert.Equal(a.CodonCount, BioCode.Codons(a.One).Count));
        Assert.All(all, a => Assert.InRange(a.Mass - 18.02 - a.ResidueMass, -0.015, 0.015));
        Assert.Equal('K', BioAminoAcids.Find("lys")?.One);
        Assert.Equal("Trp", BioAminoAcids.Find("W")?.Three);
        Assert.Equal('E', BioAminoAcids.Find("glutamic acid")?.One);
        Assert.False(BioAminoAcids.ByOne['G'].IsChiral);
    }

    [Fact]
    public void TheResidueTemplatesAreLAminoAcidsInTheirFrame()
    {
        var ts = BioData.Residues;
        Assert.Equal(20, ts.Count);
        foreach (var t in ts)
        {
            var ca = t.Pos("CA")!.Value;
            var c = t.Pos("C")!.Value;
            var n = t.Pos("N")!.Value;
            Assert.True(BioV.Length(ca) < 1e-3 && Math.Abs(c.Y) < 1e-3 && Math.Abs(c.Z) < 1e-3 && c.X > 0 && Math.Abs(n.Z) < 1e-3 && n.Y > 0, t.Code + " frame");
            if (t.Code != "GLY") Assert.True(t.Pos("CB")!.Value.Z < 0, t.Code + " CB");
            Assert.Empty(t.Model(ph7: true).OddBonds());
            Assert.Empty(t.Model(ph7: false).OddBonds());
            foreach (var chi in t.Chis)
            {
                var d = Math.Abs(t.ChiValue(chi.Index)!.Value - chi.Degrees) % 360;
                Assert.True(Math.Min(d, 360 - d) <= 0.5, $"{t.Code} χ{chi.Index}");
            }
        }
        var asp = BioData.Residue("D")!;
        Assert.Equal(-1, asp.Ph7!.Charges["OD2"]);
        Assert.Contains("HD2", asp.Ph7.Remove);
        Assert.Equal(1, BioData.Residue("Lys")!.Ph7!.Charges["NZ"]);
        Assert.Equal(32, BioData.Residue("R")!.Alternatives.Count);
        Assert.Equal(new[] { -65.0, -75.0 }, BioData.Residue("N")!.BetaRotamer!.Chis);
        // (χ1 turned to 60° and back: every atom where it was)
        var leu = BioData.Residue("L")!;
        var v0 = leu.ChiValue(1)!.Value;
        var turned = leu.SettingChi(1, 60);
        Assert.Equal(60, turned.ChiValue(1)!.Value, 2);
        var back = turned.SettingChi(1, v0);
        Assert.True(leu.Atoms.Zip(back.Atoms).Max(p => BioV.Distance(p.First.Pos, p.Second.Pos)) < 1e-6);
        // (the D image: CB on the other side)
        Assert.True(BioData.Residue("A")!.Model().Mirrored().Atoms.First(a => a.Name == "CB").Pos.Z > 0);
    }

    [Fact]
    public void NucleotidesSugarsAndLipids()
    {
        Assert.Equal(new[] { "DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP" }, BioData.Nucleotides.Select(m => m.Code));
        Assert.Equal(new[] { "GLC", "BGC", "GAL", "FRU", "BDR", "MAL", "CBI", "SUC", "LAT" }, BioData.Sugars.Select(m => m.Code));
        Assert.Equal(new[] { "GOL", "PLM", "STE", "OLA", "TGL", "POV", "CLR" }, BioData.Lipids.Select(m => m.Code));
        foreach (var m in BioData.Nucleotides.Concat(BioData.Sugars).Concat(BioData.Lipids))
        {
            Assert.NotNull(m.Mass);
            Assert.Empty(m.Model(ph7: false).OddBonds());
            var bonded = m.Bonds.SelectMany(b => new[] { b.A, b.B }).ToHashSet();
            Assert.All(m.Atoms, a => Assert.Contains(a.Name, bonded));
        }
        var da = BioData.Molecule("DA")!;
        Assert.Equal("C10 H14 N5 O6 P", da.Formula);
        Assert.Equal(331.222, da.Mass!.Value, 3);
        Assert.Equal(new[] { "HOG2", "HOG3", "HOB2", "HOA2" }, BioData.Molecule("ATP")!.Ph7!.Remove);
    }

    // ---- B-DNA (§6.7) ----

    [Fact]
    public void BDnaMatchesItsTestVectors()
    {
        var ac = BioBDNA.Build("AC");
        Assert.Equal(10, BioData.BdnaTests.Count);
        foreach (var t in BioData.BdnaTests) Assert.True(BioV.Distance(ac.Pos(t.Strand, t.Pair, t.Name)!.Value, t.Pos) <= 0.002, $"{t.Strand}/{t.Pair}/{t.Name}");
        var d = BioBDNA.Build("ACGTACGT");
        var links = new List<double>();
        for (var k = 0; k < 7; k++)
        {
            links.Add(BioV.Distance(d.Pos(1, k, "O3'")!.Value, d.Pos(1, k + 1, "P")!.Value));
            links.Add(BioV.Distance(d.Pos(2, k + 1, "O3'")!.Value, d.Pos(2, k, "P")!.Value));
        }
        Assert.All(links, l => Assert.InRange(l, 1.578, 1.582));
        Assert.Equal(20, d.HBonds.Count);
        Assert.Equal(2.91, BioV.Distance(BioBDNA.Build("A").Pos(1, 0, "N1")!.Value, BioBDNA.Build("A").Pos(2, 0, "N3")!.Value), 1);
        Assert.InRange(BioV.Distance(BioBDNA.Build("G").Pos(1, 0, "N1")!.Value, BioBDNA.Build("G").Pos(2, 0, "N3")!.Value), 2.86, 2.88);
        Assert.Empty(d.Model.OddBonds(1.1, 1.7));
        // (right-handed: each P up strand 1 turns the same way)
        var ps = d.Phosphates(1);
        Assert.All(ps.Zip(ps.Skip(1)), p => Assert.True(BioV.Cross(new BioVec(p.First.X, 0, p.First.Z), new BioVec(p.Second.X, 0, p.Second.Z)).Y > 0));
        Assert.Equal(30, BioData.BasePairs2D["AT"].Count);
        Assert.Equal(29, BioData.BasePairs2D["GC"].Count);
        Assert.Equal("strand 2 · T 8", d.Model.Atoms.First(a => a.Tag.StartsWith("strand 2", StringComparison.Ordinal)).Tag);
    }

    // ---- SplitMix64 (§5.6.3) and chi-square (§5.6.4) ----

    [Fact]
    public void SplitMix64IsTheMacsBitForBit()
    {
        var g = new BioRandom(0);
        Assert.Equal(new[] { 0xe220a8397b1dcdafUL, 0x6e789e6aa1b965f4UL, 0x06c45d188009454fUL }, new[] { g.Next(), g.Next(), g.Next() });
        g = new BioRandom(42);
        Assert.Equal(new[] { 0xbdd732262feb6e95UL, 0x28efe333b266f103UL, 0x47526757130f9f52UL }, new[] { g.Next(), g.Next(), g.Next() });
        g = new BioRandom(42);
        Assert.Equal(0.7415648787718233, g.Uniform());
        Assert.Equal(0.1599103928769201, g.Uniform());
        Assert.Equal(0.27860113025513866, g.Uniform());
        // (the helpers' draws, in the documented order: a unit vector is z, then φ)
        var a = new BioRandom(7);
        var z = 2 * a.Uniform() - 1;
        var v = new BioRandom(7).UnitVector();
        Assert.Equal(z, v.Z);
        Assert.Equal(1, BioV.Length(v), 12);
    }

    [Theory]
    [InlineData(3.84, 1, 0.050044)]
    [InlineData(3.841459, 1, 0.050000)]
    [InlineData(5.99, 2, 0.050037)]
    [InlineData(7.81, 3, 0.050106)]
    [InlineData(6.63, 1, 0.010028)]
    [InlineData(0.6, 1, 0.438578)]
    [InlineData(2.0, 3, 0.572407)]
    [InlineData(11.07, 5, 0.050010)]
    public void ChiSquarePValues(double chi2, int df, double p) => Assert.InRange(BioChiSquare.PValue(chi2, df), p - 1e-6, p + 1e-6);

    [Fact]
    public void ChiSquareTableAndTests()
    {
        Assert.Equal(10, BioChiSquare.Table.Count);
        for (var df = 1; df <= 10; df++)
            for (var i = 0; i < 4; i++)
                Assert.InRange(BioChiSquare.CriticalComputed(df, BioChiSquare.TableP[i]) - BioChiSquare.Table[df][i], -0.0051, 0.0051);
        Assert.Equal(7.81, BioChiSquare.Critical(3));
        var hwe = BioChiSquare.Test(new double[] { 50, 30, 20 }, new[] { 42.25, 45.5, 12.25 }, 1);
        Assert.Equal(11.6049, hwe.Chi2, 4);
        Assert.InRange(hwe.P, 0.00066 - 5e-6, 0.00066 + 5e-6);
        Assert.True(hwe.Reject);
        Assert.Contains("we reject the null hypothesis", hwe.Conclusion);
        var fit = BioChiSquare.Test(new double[] { 315, 108, 101, 32 }, BioChiSquare.Expected(new double[] { 9, 3, 3, 1 }, 556));
        Assert.Equal(3, fit.Df);
        Assert.False(fit.Reject);
        Assert.Equal("χ² = 0.47 < 7.81 (df = 3, p = 0.05), so we fail to reject the null hypothesis.", fit.Conclusion);
    }

    // ---- rationals (§5.6.1) and crosses (§5.3.2, §5.6.2) ----

    [Fact]
    public void FractionsStayExact()
    {
        Assert.Equal("3/32", new BioFrac(6, 64).Text);
        Assert.Equal("-1/2", new BioFrac(2, -4).Text);
        Assert.Equal("9.4%", new BioFrac(3, 32).PercentText);
        Assert.Equal("50.0%", new BioFrac(1, 2).PercentText);
        Assert.Equal(new BioFrac(3, 4), new BioFrac(1, 2) + new BioFrac(1, 4));
        Assert.Equal(new[] { 1, 2, 1 }, BioRatio.Reduce(new[] { 2, 4, 2 }));
        Assert.Equal(new[] { 1, 2, 1 }, BioRatio.Whole(new[] { new BioFrac(1, 4), new BioFrac(1, 2), new BioFrac(1, 4) }));
        Assert.Equal("1 : 2 : 1", BioRatio.Text(new[] { 1, 2, 1 }));
    }

    [Theory]
    [InlineData("Aa × Aa", "1 AA : 2 Aa : 1 aa", "3 A_ : 1 aa")]
    [InlineData("Aa × aa", "1 Aa : 1 aa", "1 A_ : 1 aa")]
    [InlineData("AA × aa", "1 Aa", "1 A_")]
    [InlineData("C^R C^W × C^R C^W", "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", "1 red : 2 pink : 1 white")]
    [InlineData("AaBb × AaBb", "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb", "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb")]
    [InlineData("AaBb x aabb", "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", "1 A_B_ : 1 A_bb : 1 aaB_ : 1 aabb")]
    [InlineData("I^A i × I^B i", "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", "1 AB : 1 A : 1 B : 1 O")]
    [InlineData("I^A I^B × I^A I^B", "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", "1 A : 2 AB : 1 B")]
    public void CrossesAsTheSpecificationHasThem(string text, string genotypes, string phenotypes)
    {
        var (cross, error) = BioCross.Parse(text);
        Assert.Null(error);
        var res = cross!.Result();
        Assert.Equal(genotypes, res.GenotypicRatioText);
        Assert.Equal(phenotypes, res.PhenotypicRatioText);
    }

    [Fact]
    public void TheTrihybridCrossAndTheProductRule()
    {
        var tri = BioCross.Parse("AaBbCc × AaBbCc").Cross!;
        var res = tri.Result();
        Assert.Equal(27, res.Genotypes.Count);
        Assert.Equal(new[] { 27, 9, 9, 9, 3, 3, 3, 1 }, res.PhenotypicRatio);
        Assert.Equal(new[] { "A_B_C_", "A_B_cc", "A_bbC_", "aaB_C_", "A_bbcc", "aaB_cc", "aabbC_", "aabbcc" }, res.Phenotypes.Select(p => p.Text));
        Assert.Equal(8, tri.Gametes1.Count);
        Assert.All(tri.Gametes1, g => Assert.Equal(new BioFrac(1, 8), g.P));
        Assert.Equal(64, res.SquareSize);
        var p = tri.Probability(new[] { BioTarget.HomRecessive, BioTarget.Dominant, BioTarget.Heterozygous });
        Assert.Equal(new[] { new BioFrac(1, 4), new BioFrac(3, 4), new BioFrac(1, 2) }, p.Factors);
        Assert.Equal(new BioFrac(3, 32), p.Product);
        Assert.Equal("9.4%", p.Product.PercentText);
    }

    [Fact]
    public void XLinkedCrossesSplitDaughtersAndSons()
    {
        var c = BioCross.Parse("X^A X^a × X^a Y").Cross!;
        Assert.True(c.HasXLinked);
        var d = c.Result(BioSex.Female);
        var s = c.Result(BioSex.Male);
        Assert.Equal("1 X^A X^a : 1 X^a X^a", d.GenotypicRatioText);
        Assert.Equal(new[] { 1, 1 }, d.PhenotypicRatio);
        Assert.Equal("unaffected", d.Phenotypes[0].Text);
        Assert.Equal(new BioFrac(1, 2), d.Phenotypes[0].Carriers);
        Assert.Equal("1 X^A Y : 1 X^a Y", s.GenotypicRatioText);
        Assert.Equal("1 unaffected : 1 affected", s.PhenotypicRatioText);
        var n = BioCross.Parse("X^A X^a × X^A Y").Cross!;
        Assert.Equal("1 X^A X^A : 1 X^A X^a", n.Result(BioSex.Female).GenotypicRatioText);
        Assert.Equal(new[] { 1 }, n.Result(BioSex.Female).PhenotypicRatio);
        Assert.Equal(new BioFrac(1, 2), n.Result(BioSex.Female).Phenotypes[0].Carriers);
        Assert.Equal("1 unaffected : 1 affected", n.Result(BioSex.Male).PhenotypicRatioText);
    }

    [Fact]
    public void ALethalHomozygoteIsBornTwoToOne()
    {
        var c = BioCross.Parse("A^Y a × A^Y a").Cross!;
        c.Genes[0].Pattern = BioInheritance.LethalDominantHomozygote;
        c.Genes[0].TraitNames = new() { "yellow", "non-yellow" };
        var res = c.Result();
        Assert.Equal("1 A^Y A^Y : 2 A^Y a : 1 aa", res.GenotypicRatioText);
        Assert.True(res.Genotypes[0].Dies);
        Assert.Equal(new[] { 2, 1 }, res.BornGenotypicRatio);
        Assert.Equal("2 yellow : 1 non-yellow", res.PhenotypicRatioText);
        Assert.Equal(new BioFrac(3, 4), res.BornFraction);
    }

    [Fact]
    public void ACrossThatCannotBeReadSaysSo()
    {
        var bad = BioCross.Parse("Aa");
        Assert.Null(bad.Cross);
        Assert.NotNull(bad.Error);
        Assert.Equal(new[] { 0, 1 }, BioCross.Parse("aA × Aa").Cross!.Parent1[0]);
        Assert.NotNull(BioCross.Parse("A × Aa").Error);
    }

    // ---- sequences (§3.7) and genes (§6.12) ----

    [Fact]
    public void TheTeachingGene()
    {
        var g = BioGenes.Teaching;
        Assert.Equal(112, g.Coding.Length);
        Assert.Equal(new[] { 1, 32, 45, 56, 83, 102 }, g.Parts.Select(p => p.Start));
        Assert.Equal("TATAAAA", g.Coding[4..11]);
        Assert.Equal(g.Template, BioSeq.TemplateStrand(g.Coding));
        Assert.Equal(g.PreMRNA, BioSeq.Transcribe(g.Coding[31..112]));
        Assert.Equal(g.MatureMRNA, BioSeq.Transcribe(g.Coding[31..55] + g.Coding[82..112]));
        Assert.Equal(g.Cds, g.Coding[44..55] + g.Coding[82..101]);
        Assert.Equal("MAFSDWQKG*", BioSeq.Translate(g.Cds));
        Assert.Equal("MAFR*", BioSeq.ProteinFromFirstStart(g.PreMRNA) + "*");
        var orf = BioSeq.Orfs(g.MatureMRNA, minCodons: 3, reverseStrand: false)[0];
        Assert.Equal((2, 14, "MAFSDWQKG", true), (orf.Frame, orf.Start, orf.Protein, orf.HasStop));
        Assert.Equal("+2", orf.FrameLabel);
        Assert.Equal(6, g.Mutations.Count);
    }

    [Fact]
    public void HumanBetaGlobinAndTheSickleCellMutation()
    {
        Assert.Equal(444, BioGenes.HbbCDS.Length);
        Assert.Equal(BioGenes.HbbProtein + "*", BioSeq.Translate(BioGenes.HbbCDS));
        var hbb = BioGenes.HbbCDS.ToCharArray();
        Assert.Equal('A', hbb[19]);
        hbb[19] = 'T';
        Assert.StartsWith("MVHLTPVEKS", BioSeq.Translate(new string(hbb)));
        Assert.Equal("GTG", BioSeq.Codons(new string(hbb))[6]);
    }

    [Fact]
    public void SequencesAreCleanedAndWorked()
    {
        var a = new BioSequenceInput(">x\n atg c12gt", BioSeqKind.Dna);
        Assert.Equal("ATGCGT", a.Sequence);
        var rna = new BioSequenceInput("AUGC", BioSeqKind.Dna);
        Assert.Equal(BioSeqKind.Rna, rna.Suggestion);
        Assert.Equal("This looks like RNA. Switch?", rna.Message);
        var bad = new BioSequenceInput("ATGXQ", BioSeqKind.Dna);
        Assert.Null(bad.Sequence);
        Assert.Equal(new[] { 'X', 'Q' }, bad.Invalid.Select(i => i.Letter));
        Assert.Equal(new[] { 4 }, bad.Invalid[0].Positions);
        Assert.Equal("DNA uses only A, C, G, T and N. Not allowed: “X” at 4; “Q” at 5.", bad.Message);
        var capped = new BioSequenceInput("5′-ACGTACGT-3′", BioSeqKind.Dna, cap: 4);
        Assert.Equal("ACGT", capped.Sequence);
        Assert.True(capped.Truncated);
        Assert.Equal("NGCAT", BioSeq.ReverseComplement("ATGCN"));
        Assert.Equal("TACG", BioSeq.Complement("ATGC"));
        Assert.Equal(new[] { "MA*", "WP", "GL", "LGH", "*A", "RP" },
            Enumerable.Range(0, 3).Select(f => BioSeq.Translate("ATGGCCTAA", f)).Concat(Enumerable.Range(0, 3).Select(f => BioSeq.Translate("ATGGCCTAA", f, reverse: true))));
        Assert.Equal(331.22 + 307.20 + 347.22 + 322.21 - 3 * 18.02, BioSeq.Mass("ACGT"), 9);
        Assert.Equal(36, BioSeq.MeltingTemperature("ATGCATGCATGC"), 9);
        Assert.Equal(50, BioSeq.Chargaff("AAAGC").PurinePercent, 9);
        Assert.Equal(0.5, BioSeq.GcContent("ATGC"), 9);
        var lines = BioSeq.Lines(new string('A', 75));
        Assert.Equal(new[] { 1, 61 }, lines.Select(l => l.Start));
        Assert.Equal(new[] { "AAAAAAAAAA", "AAAAA" }, lines[1].Groups);
    }

    // ---- proteins and tRNA (§6.10, §6.11) ----

    [Fact]
    public void ProteinsAndTransferRna()
    {
        Assert.Equal(Enumerable.Range(1, 76), BioData.Ubiquitin.Select(r => r.I));
        Assert.All(BioData.Ubiquitin.Zip(BioData.Ubiquitin.Skip(1)), p => Assert.InRange(BioV.Distance(p.First.Ca, p.Second.Ca), 3.6, 4.0));
        var h = BioData.Hemoglobin;
        Assert.Equal(new[] { 141, 146, 141, 146 }, new[] { 'A', 'B', 'C', 'D' }.Select(c => h.Residues.Count(r => r.Chain == c)));
        Assert.Equal(4, h.Hemes.Count);
        Assert.Equal(BioGenes.HbbProtein[1..], new string(h.Residues.Where(r => r.Chain == 'B').Select(r => r.Aa).ToArray()));
        var t = BioData.Trna;
        Assert.Equal(BioData.TrnaSequence, new string(t.Nucleotides.Select(n => n.Base).ToArray()));
        Assert.Equal(21, t.Pairs.Count);
        Assert.All(t.Pairs, p => Assert.Contains($"{BioData.TrnaSequence[p.I - 1]}{BioData.TrnaSequence[p.J - 1]}", new[] { "AU", "UA", "GC", "CG", "GU", "UG" }));
        Assert.Equal(14, t.Modified.Count);
        Assert.Equal(("m²G", 10), t.Modified[0]);
        Assert.Equal("m¹A", t.Modified[^1].Label);
        var anticodon = (t.Nucleotides[33].P + t.Nucleotides[34].P + t.Nucleotides[35].P) / 3;
        Assert.InRange(BioV.Distance(anticodon, t.Nucleotides[75].P), 71, 73);
    }

    // ---- the meshes and labels (§3.4, §3.5) ----

    [Fact]
    public void MeshesAreClosedAndFaceOut()
    {
        var ball = 4.0 / 3 * Math.PI;
        Assert.Equal(80, BioShapes.Icosphere(1, 1).TriangleCount);
        Assert.InRange(BioSelfCheck.Volume(BioShapes.Sphere(1, 32, 64)) / ball, 0.99, 1.01);
        Assert.InRange(BioSelfCheck.Volume(BioShapes.Radial(BioShapes.SphereRadius(1), 32, 64, BioCut.Octant)) / ball, 0.865, 0.885);
        Assert.InRange(BioSelfCheck.Volume(BioShapes.Radial(BioShapes.SphereRadius(1), 32, 64, BioCut.Octant, BioShapes.SphereRadius(0.9))) / (ball * 0.271), 0.86, 0.89);
        Assert.InRange(BioSelfCheck.Volume(BioShapes.Cylinder(new BioVec(1, 1, 1), new BioVec(1, 3, 1), 0.5, 48)) / (Math.PI * 0.5), 0.99, 1.01);
        Assert.InRange(BioSelfCheck.Volume(BioShapes.Torus(0.07, 0.02, 96, 48)) / (2 * Math.PI * Math.PI * 0.07 * 0.0004), 0.99, 1.01);
        // (a mirrored frame keeps the faces outward)
        var mirrored = BioShapes.Sphere(1).Transformed(new BioFrame(BioVec.Zero, new BioVec(-1, 0, 0), new BioVec(0, 1, 0), new BioVec(0, 0, 1)));
        Assert.True(BioSelfCheck.Volume(mirrored) > 0);
        Assert.True(BioSelfCheck.Volume(BioShapes.Sphere(1).Flipped()) < 0);
    }

    [Fact]
    public void ABatchNamesTheObjectOfAVertexOrATriangle()
    {
        var a = BioShapes.Icosphere(1, 1);
        var b = BioShapes.Sphere(1);
        var batch = new BioMeshBatch();
        batch.Add(a, "organelle:mito:0");
        batch.Add(new BioMesh(), "nothing");
        batch.Add(b, "organelle:mito:1");
        Assert.Equal("organelle:mito:0", batch.ObjectAtTriangle(79));
        Assert.Equal("organelle:mito:1", batch.ObjectAtTriangle(80));
        Assert.Null(batch.ObjectAtTriangle(batch.Mesh.TriangleCount));
        Assert.Equal("organelle:mito:1", batch.ObjectAtVertex(a.VertexCount));
        Assert.True(BioSceneContent.Matches("organelle:mito:1", "organelle:mito"));
        Assert.False(BioSceneContent.Matches("organelle:mitochondrion", "organelle:mito"));
    }

    [Fact]
    public void LabelsSitInColumnsApartAndInside()
    {
        var inputs = Enumerable.Range(0, 20).Select(k => new BioLabelInput($"o{k}", $"Part {k}", k % 2 == 0 ? 300 : 500, 380 + k, Area: k)).ToList();
        var (placed, more) = BioLabelLayout.Layout(inputs, 400, 400, 150, 800, 800);
        // (a view tall enough for them all: every label shown, none counted as "more")
        Assert.Equal(20, placed.Count);
        Assert.Equal(0, more);
        foreach (var side in new[] { true, false })
        {
            var col = placed.Where(p => p.Left == side).OrderBy(p => p.Y).ToList();
            Assert.All(col.Zip(col.Skip(1)), p => Assert.True(p.Second.Y - p.First.Y >= 22 - 1e-9));
        }
        Assert.All(placed, p => Assert.True(p.Frame.X >= 16 - 1e-9 && p.Frame.X + p.Frame.Width <= 784 + 1e-9 && p.Frame.Y >= 16 - 1e-9));
        Assert.All(placed.Where(p => p.Left), p => Assert.Equal((226.0, 238.0), (p.EdgeX, p.ElbowX)));
        // (a view 200 tall holds seven a column: the largest objects keep their labels, the rest are counted)
        var (few, left) = BioLabelLayout.Layout(inputs, 400, 100, 150, 800, 200);
        Assert.Equal(14, few.Count);
        Assert.Equal(6, left);
        Assert.DoesNotContain(few, p => p.Id == "o0");
        Assert.All(few, p => Assert.True(p.Frame.Y >= 16 - 1e-9 && p.Frame.Y + p.Frame.Height <= 184 + 1e-9));
        // (a column too long for the view is pushed up from the bottom, still 22 apart and inside)
        var crowd = Enumerable.Range(0, 12).Select(k => new BioLabelInput($"c{k}", "x", 100, 300)).ToList();
        var (packed, _) = BioLabelLayout.Layout(crowd, 400, 150, 100, 800, 300);
        Assert.True(packed.Min(p => p.Y) >= 25 - 1e-9);
    }

    [Fact]
    public void LabelsSitBesideANarrowModelAndAreKeptEven()
    {
        // (a helix-like model, narrow in a wide view, four anchors left of its middle and eleven right of it)
        var inputs = Enumerable.Range(0, 15).Select(k => new BioLabelInput($"p{k}", $"Part {k}",
            k < 4 ? 360 + 8 * k : 405 + 5 * (k - 4), 120 + 25 * k, Width: 80)).ToList();
        var box = new BioLabelBox(350, 100, 450, 500);
        var (placed, more) = BioLabelLayout.Layout(inputs, 400, 300, 200, 900, 600, box);
        Assert.Equal(15, placed.Count);
        Assert.Equal(0, more);
        // (the columns 24 out from the model's box, not from its 200-wide circle: beside the model, not at the view's edges)
        Assert.All(placed.Where(p => p.Left), p => Assert.Equal(326, p.EdgeX, 6));
        Assert.All(placed.Where(p => !p.Left), p => Assert.Equal(474, p.EdgeX, 6));
        // (kept even: the right column's anchors nearest the middle went over, until the two differ by two at most)
        var lefts = placed.Count(p => p.Left);
        Assert.True(Math.Abs(lefts - (placed.Count - lefts)) <= 2, $"{lefts} left of {placed.Count}");
        Assert.Contains(placed, p => p.Left && p.Id == "p4");
        // (but an anchor far out on one side is never sent across the model)
        var far = Enumerable.Range(0, 6).Select(k => new BioLabelInput($"f{k}", "Far", 700, 100 + 30 * k, Width: 60)).ToList();
        var (kept, _) = BioLabelLayout.Layout(far, 400, 300, 200, 900, 600);
        Assert.All(kept, p => Assert.False(p.Left));
        // (a scale bar over the view's bottom-left corner: the left column ends above it, the right one is left as it is)
        var low = Enumerable.Range(0, 8).Select(k => new BioLabelInput($"l{k}", $"Low {k}", k < 4 ? 300 : 500, 120 + 20 * k, Area: k, Width: 70)).ToList();
        var bar = new BioLabelBox(0, 150, 200, 200);
        var (clear, gone) = BioLabelLayout.Layout(low, 400, 100, 150, 800, 200, keepClear: bar);
        Assert.All(clear.Where(p => p.Left), p => Assert.True(p.Frame.Y + p.Frame.Height <= bar.Y0 + 1e-9));
        Assert.Equal(4, clear.Count(p => !p.Left));
        Assert.Equal(8, clear.Count + gone);
        // (and a left column with more than it holds above the bar gives its label nearest the middle to the right one,
        //  which has room, rather than counting it out: six left of the middle, four right, all ten shown)
        var near = Enumerable.Range(0, 10).Select(k => new BioLabelInput($"n{k}", $"Near {k}", k < 6 ? 370 + k : 500, 30 + 15 * k, Area: k, Width: 70)).ToList();
        var (over, none) = BioLabelLayout.Layout(near, 400, 100, 150, 800, 200, keepClear: bar);
        Assert.Equal((10, 0), (over.Count, none));
        Assert.Equal(5, over.Count(p => p.Left));
        Assert.All(over.Where(p => p.Left), p => Assert.True(p.Frame.Y + p.Frame.Height <= bar.Y0 + 1e-9));
    }

    [Fact]
    public void ALabelThatWouldHideItsAnchorGoesAboveOrBelowTheModel()
    {
        // (a narrow view: a long label moved in from its column lands on its own anchor)
        var inputs = new List<BioLabelInput>
        {
            new("a", "A long label on the upper left", 150, 220, Width: 220),
            new("b", "Below", 400, 340, Width: 60),
            new("c", "Short", 330, 300, Width: 60),
        };
        var box = new BioLabelBox(120, 200, 420, 360);
        var (placed, _) = BioLabelLayout.Layout(inputs, 270, 280, 150, 545, 520, box);
        var a = placed.Single(p => p.Id == "a");
        // (moved to a row just above the model, over its anchor, its leader straight up to its lower edge)
        Assert.Equal(200 - 6 - 9, a.Y, 6);
        Assert.True(a.Frame.X <= 150 && a.Frame.X + a.Frame.Width >= 150);
        Assert.Equal(new[] { (150.0, a.Y + 9) }, a.Leader);
        // (a short label beside the model stays in its column, its leader to the elbow and along)
        var c = placed.Single(p => p.Id == "c");
        Assert.Null(c.EdgeY);
        Assert.Equal(2, c.Leader.Length);
        // (without the model's box the layout is the Mac's)
        var (plain, _) = BioLabelLayout.Layout(inputs, 270, 280, 150, 545, 520);
        Assert.All(plain, p => Assert.Null(p.EdgeY));
        // (two long labels that fit side by side in the row above are packed into it, the first slid left for the second)
        var pair = new List<BioLabelInput>
        {
            new("p", "The first long label, upper left", 200, 230, Width: 240),
            new("q", "The second long label, upper right", 330, 235, Width: 250),
        };
        var (two, _) = BioLabelLayout.Layout(pair, 270, 280, 150, 545, 520, box);
        Assert.All(two, p => Assert.Equal(200 - 6 - 9, p.Y, 6));
        var (lp, lq) = (two.Single(p => p.Id == "p").Frame, two.Single(p => p.Id == "q").Frame);
        Assert.True(lp.X >= 16 && lp.X + lp.Width + 6 <= lq.X + 1e-9 && lq.X + lq.Width <= 545 - 16 + 1e-9);
        // (two that do not fit side by side: the second a row further out, its leader clear of the first's capsule)
        var wide = new List<BioLabelInput>
        {
            new("p", "A very long first label over the upper left", 150, 230, Width: 300),
            new("q", "A very long second label over the upper right", 420, 235, Width: 300),
        };
        var (rowed, _) = BioLabelLayout.Layout(wide, 270, 280, 150, 545, 520, box);
        var (inner, outer) = (rowed.Single(p => p.Id == "p"), rowed.Single(p => p.Id == "q"));
        Assert.Equal(22, inner.Y - outer.Y, 6);
        var (ex, ey) = outer.Leader[0];
        double XAt(double y) => outer.AnchorX + (ex - outer.AnchorX) * (y - outer.AnchorY) / (ey - outer.AnchorY);
        var f = inner.Frame;
        Assert.False(Math.Max(XAt(f.Y), XAt(f.Y + f.Height)) > f.X && Math.Min(XAt(f.Y), XAt(f.Y + f.Height)) < f.X + f.Width);
    }

    [Fact]
    public void LabelsAreNotDroppedCrossedOrLedIntoAnotherLabel()
    {
        // (a right column with more than it holds gives its labels nearest the middle to the left one while that has room,
        //  however far out they lie: the cell's Vesicles was dropped beside a left column with room for two)
        var bar = new BioLabelBox(0, 150, 200, 200);
        var cell = Enumerable.Range(0, 10).Select(k => new BioLabelInput($"r{k}", $"Right {k}", k == 0 ? 480 : 560, 20 + 16 * k, Area: k, Width: 70))
            .Append(new BioLabelInput("l0", "Left", 300, 60, Width: 60)).ToList();
        var (all, dropped) = BioLabelLayout.Layout(cell, 400, 100, 150, 800, 200, keepClear: bar);
        Assert.Equal((11, 0), (all.Count, dropped));
        Assert.Contains(all, p => p.Left && p.Id == "r0");
        Assert.All(all.Where(p => p.Left), p => Assert.True(p.Frame.Y + p.Frame.Height <= bar.Y0 + 1e-9));
        // (two leaders in a column that would cross swap places: an anchor deep in the model above one near its edge)
        var deep = new List<BioLabelInput>
        {
            new("a", "Top", 390, 100, Width: 60), new("b", "Deep", 300, 102, Width: 60), new("c", "Near", 250, 104, Width: 60),
            new("d", "Right", 500, 100, Width: 60), new("e", "Right", 520, 300, Width: 60),
        };
        var (column, _) = BioLabelLayout.Layout(deep, 400, 300, 150, 800, 600);
        static bool Cross(BioLabelPlacement p, BioLabelPlacement q)
        {
            static double Turn((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            (double, double) p0 = (p.AnchorX, p.AnchorY), p1 = p.Leader[0], q0 = (q.AnchorX, q.AnchorY), q1 = q.Leader[0];
            return Turn(q0, q1, p0) * Turn(q0, q1, p1) < 0 && Turn(p0, p1, q0) * Turn(p0, p1, q1) < 0;
        }
        Assert.All(column, p => Assert.All(column.Where(q => q.Id != p.Id), q => Assert.False(Cross(p, q), $"{p.Id} crosses {q.Id}")));
        Assert.True(column.Single(p => p.Id == "c").Y < column.Single(p => p.Id == "b").Y);
        // (a label over another label's anchor goes to a row — the bacterium's "Fimbriae (pili)" over the flagella's — and
        //  an anchor straight under its own capsule is led straight to its edge, not round its end)
        var box = new BioLabelBox(300, 200, 500, 400);
        var hairs = new List<BioLabelInput> { new("fim", "Fimbriae (pili)", 310, 300, Width: 120), new("fla", "Flagella", 200, 302, Width: 80) };
        var (placed, _) = BioLabelLayout.Layout(hairs, 400, 300, 150, 800, 600, box);
        var fim = placed.Single(p => p.Id == "fim");
        var fla = placed.Single(p => p.Id == "fla");
        Assert.NotNull(fim.EdgeY);
        Assert.Equal(400 + 6 + 9, fim.Y, 6);
        Assert.Equal(new[] { (205.0, fla.Y - 9) }, fla.Leader);
        Assert.Equal(0, BioLabelLayout.Conflicts(placed, box));
        // (a model reaching the view's top: a label over it anchored in the middle half of its height goes to a row below,
        //  as there is none above; one anchored near its top is not led right across it)
        var tall = new BioLabelBox(300, 30, 500, 300);
        var (middle, _) = BioLabelLayout.Layout(new[] { new BioLabelInput("mid", "Middle label", 420, 150, Width: 200) }, 400, 165, 150, 600, 600, tall);
        Assert.True(middle[0].Row);
        Assert.Equal(300 + 6 + 9, middle[0].Y, 6);
        var (topmost, _) = BioLabelLayout.Layout(new[] { new BioLabelInput("top", "Top label", 420, 40, Width: 200) }, 400, 165, 150, 600, 600, tall);
        Assert.False(topmost[0].Row);
        // (and a label over the model counts against the camera's fit; one beside it does not)
        Assert.Equal(1, BioLabelLayout.Conflicts(new[] { new BioLabelPlacement("x", "Over", true, 340, 300, 80, 320, 300, 352) }, box));
        Assert.Equal(0, BioLabelLayout.Conflicts(new[] { new BioLabelPlacement("x", "Beside", true, 296, 300, 80, 320, 300, 308) }, box));
    }

    [Fact]
    public void AnOutlineIsTheModelsHullAndLabelsAreKeptOffIt()
    {
        // (a round cell's outline: its hull, its width at a height, and its box's empty corners)
        var round = BioOutline.Round(Enumerable.Range(0, 72).Select(k => (292 + 160 * Math.Cos(k * Math.PI / 36), 229 + 160 * Math.Sin(k * Math.PI / 36))))!;
        Assert.Equal(132, round.Box.X0, 0);
        Assert.Equal(452, round.Box.X1, 0);
        var middle = round.Across(220, 238)!.Value;
        Assert.True(middle.X0 < 134 && middle.X1 > 450);
        Assert.Null(round.Across(20, 60));
        var nearTop = round.Across(72, 80)!.Value;
        Assert.True(nearTop.X1 - nearTop.X0 < 140, $"{nearTop}");
        Assert.True(round.Overlaps(280, 220, 320, 240));
        Assert.False(round.Overlaps(136, 73, 180, 91), "a label in the box's corner lies off the round cell");
        Assert.False(round.Overlaps(452, 220, 500, 238));
        // (sixteen labels round a cell three quarters of a 584 × 458 view: in the corners its outline leaves, none on it,
        //  over another's anchor or led through another's capsule; those with no room folded into "+N more")
        var cell = BioOutline.Round(Enumerable.Range(0, 72).Select(k => (292 + 215 * Math.Cos(k * Math.PI / 36), 229 + 172 * Math.Sin(k * Math.PI / 36))))!;
        var labels = Enumerable.Range(0, 16).Select(k =>
        {
            var a = k * Math.PI / 8 + 0.2;
            return new BioLabelInput($"part:{k}", $"Part number {k}", 292 + 120 * Math.Cos(a), 229 + 100 * Math.Sin(a), Area: 16 - k, Width: 110);
        }).ToList();
        var folded = new List<string>();
        var (placed, more) = BioLabelLayout.Layout(labels, 292, 229, 180, 584, 458, cell.Box, new BioLabelBox(0, 398, 200, 458), outline: cell, folded: folded);
        Assert.Equal(16, placed.Count + more);
        Assert.Equal(folded.Count, more);
        Assert.True(placed.Count >= 12, $"only {placed.Count} placed");
        Assert.Equal(0, BioLabelLayout.Conflicts(placed, cell.Box, cell));
        Assert.All(placed, p => Assert.False(cell.Overlaps(p.Frame.X, p.Frame.Y, p.Frame.X + p.Frame.Width, p.Frame.Y + p.Frame.Height, 4), p.Text));
        Assert.All(placed, p => Assert.True(p.Frame.X >= 16 - 1e-9 && p.Frame.X + p.Frame.Width <= 568 + 1e-9 && p.Frame.Y >= 16 - 1e-9 && p.Frame.Y + p.Frame.Height <= 442 + 1e-9, p.Text));
        Assert.All(placed, p => Assert.False(p.Frame.X < 200 && p.Frame.Y + p.Frame.Height > 398, $"{p.Text} over the scale bar"));
        // (the smallest objects' labels are the ones folded)
        Assert.All(folded, id => Assert.True(int.Parse(id.Split(':')[1]) >= 8, id));
        // (without being asked to fold, the same layout leaves those in the way where they are, counted by Conflicts)
        var (all, _) = BioLabelLayout.Layout(labels, 292, 229, 180, 584, 458, cell.Box, outline: cell);
        Assert.Equal(16, all.Count);
    }

    // ---- the step-through (§3.6) ----

    [Fact]
    public void TheStepThroughMovesPlaysAndStops()
    {
        var now = 0.0;
        var steps = Enumerable.Range(0, 5).Select(i => new BioStep(i, $"Stage {i}", "…", Group: i / 2 + 1)).ToList();
        var c = new BioStepController(steps, clock: () => now);
        var said = new List<string>();
        c.Announce += said.Add;
        c.Next();
        Assert.Equal(new BioStepPhase(0, 1, BioStepTiming.Ease(0.5), false), c.Phase(0.3));
        Assert.True(c.IsMoving(0.3));
        Assert.Equal(BioStepPhase.At(1), c.Phase(0.7));
        Assert.Equal("Step 2 of 5 · Stage 1. …", said[0]);
        c.Back();
        Assert.Equal(new BioStepPhase(1, 0, 0, false), c.Phase(0));
        // (play: a step every 3 s at 1×, stopping at the last)
        c.Play();
        for (var k = 0; k < 6; k++) { now += 3.01; c.Tick(); }
        Assert.Equal(4, c.Index);
        Assert.False(c.Playing);
        // (from the last, play starts again at the first; 2× is twice as quick)
        c.Speed = 2;
        c.Play();
        Assert.Equal(0, c.Index);
        now += 1.6;
        c.Tick();
        Assert.Equal(1, c.Index);
        c.Pause();
        Assert.True(c.JumpToGroup(3));
        Assert.Equal(4, c.Index);
        Assert.False(c.JumpToGroup(9));
        // (animations off: a 200 ms crossfade, eased not)
        c.ReduceMotion = true;
        c.First();
        var fade = c.Phase(now + 0.05);
        Assert.Equal((4, 0, true), (fade.From, fade.To, fade.Crossfade));
        Assert.Equal(0.5, fade.T, 9);
        c.SetSteps(steps.Take(2).ToList(), keepIndex: true);
        Assert.Equal(0, c.Index);
        Assert.Equal(BioStepPhase.At(0), c.Phase(now));
        Assert.Equal(3, new BioStepPhase(0, 1, 0.25, false).Mix(2, 6), 9);
    }

    // ---- the colours (§3.8) ----

    [Fact]
    public void ColoursComeInPairs()
    {
        Assert.Equal("#2e9e48", BioPalette.Base('A').Light);
        Assert.Equal("#ffa133", BioPalette.Base('u').Dark);
        Assert.Equal(BioPalette.Coil, BioPalette.Base('N'));
        Assert.Equal(BioPalette.Acidic, BioPalette.AminoAcid('E'));
        Assert.Equal(BioPalette.Basic, BioPalette.AminoAcid(BioAAClass.Basic));
        Assert.Equal(BioPalette.Helix, BioPalette.Secondary('H'));
        Assert.Equal((46 / 255.0, 158 / 255.0, 72 / 255.0), BioPalette.Rgb("#2e9e48"));
    }
}
