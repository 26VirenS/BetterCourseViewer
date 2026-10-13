using Simpl.Tools;
using Xunit;

namespace Simpl.Tests;

/// <summary>
/// The Biomolecule Builder's models (Tools/Biology/Model/Biomol*.cs), against the specification's test vectors (§6.4)
/// and its rules (§4.4, §7): the peptide builder's geometry, the β-sheet, proline, the forms at pH 7, the clash relief,
/// the results a peptide's card shows, the amino acids' parts and chirality, nucleotides and a strand, sugars and their
/// glycosidic bonds, lipids, the protein structure levels, the scenes and motions, and the hand-off to the chemistry
/// builder. Plain .NET: no window is opened.
/// </summary>
public class BiomoleculeBuilderTests
{
    private static BiomolSS[] All(BiomolSS s, int n) => Enumerable.Repeat(s, n).ToArray();

    [Fact]
    public void ItsSelfCheckLinesAreAllOK()
    {
        var r = new BioCheckReport();
        BioSelfCheck.BiomolPeptideChecks(r);
        BioSelfCheck.BiomolMonomerChecks(r);
        BioSelfCheck.BiomolSugarLipidChecks(r);
        BioSelfCheck.BiomolSceneChecks(r);
        Assert.True(r.Failed == 0, string.Join("\n", r.Lines.Where(l => l.EndsWith(" FAIL", StringComparison.Ordinal))));
        Assert.True(r.Passed >= 38, $"only {r.Passed} checks ran");
    }

    // ---- the peptide builder (§6.4) ----

    [Fact]
    public void AlaFourAsAHelixMatchesTheTestVector()
    {
        var p = BiomolPeptideBuilder.Build("AAAA", All(BiomolSS.Helix, 4));
        // (spot checks from §6.4's table, Å)
        void At(int i, string name, double x, double y, double z)
        {
            var k = p.AtomIndex(i, name);
            Assert.NotNull(k);
            Assert.True(BioV.Distance(p.Atoms[k!.Value].Pos, new BioVec(x, y, z)) <= 0.002, $"{i} {name} at {p.Atoms[k.Value].Pos}");
        }
        At(0, "N", 0, 0, 0);
        At(0, "C", 2.009, 1.422, 0);
        At(0, "CB", 1.946, -0.736, -1.249);
        At(1, "H", 0.727, 1.932, -1.480);
        At(3, "OXT", 4.473, 3.391, 2.950);
        At(3, "CB", 2.844, 1.131, 4.128);
        // (the peptide bond: C–N 1.329 Å; the backbone's lengths and angles as Engh & Huber)
        var b = p.Backbones;
        Assert.Equal(1.329, BioV.Distance(p.Atoms[b[0].C].Pos, p.Atoms[b[1].N].Pos), 3);
        Assert.Equal(1.458, BioV.Distance(p.Atoms[b[2].N].Pos, p.Atoms[b[2].CA].Pos), 3);
        Assert.Equal(111.2, BioV.Angle(p.Atoms[b[2].N].Pos, p.Atoms[b[2].CA].Pos, p.Atoms[b[2].C].Pos), 1);
        // (ω = 180°: the peptide bond is planar, trans)
        Assert.Equal(180, Math.Abs(BioV.Dihedral(p.Atoms[b[1].CA].Pos, p.Atoms[b[1].C].Pos, p.Atoms[b[2].N].Pos, p.Atoms[b[2].CA].Pos)), 1);
    }

    [Fact]
    public void TheHelixIsRightHandedWithItsTextbookMeasures()
    {
        var p = BiomolPeptideBuilder.Build(new string('A', 20), All(BiomolSS.Helix, 20));
        var (perTurn, rise, radius) = BiomolPeptideBuilder.HelixParameters(p.CaTrace);
        Assert.InRange(perTurn, 3.61, 3.63);
        Assert.InRange(rise, 1.55, 1.57);
        Assert.InRange(radius, 2.27, 2.29);
        // (every helix H-bond is backbone C=O(i) to N–H(i+4), never a side chain)
        Assert.Equal(16, p.HBonds.Count);
        Assert.All(p.HBonds, h => Assert.Equal(4, h.Donor - h.Acceptor));
        Assert.All(p.HBonds, h => Assert.Contains(p.Atoms[h.O].Name, new[] { "O" }));
        // (φ and ψ read back from the atoms with the IUPAC sign)
        var b = p.Backbones;
        Assert.Equal(-57, BioV.Dihedral(p.Atoms[b[4].C].Pos, p.Atoms[b[5].N].Pos, p.Atoms[b[5].CA].Pos, p.Atoms[b[5].C].Pos), 1);
        Assert.Equal(-47, BioV.Dihedral(p.Atoms[b[5].N].Pos, p.Atoms[b[5].CA].Pos, p.Atoms[b[5].C].Pos, p.Atoms[b[6].N].Pos), 1);
        // (a left-handed helix turns the other way)
        var left = BiomolPeptideBuilder.Build(new string('G', 20), All(BiomolSS.LeftHelix, 20));
        Assert.True(BiomolPeptideBuilder.HelixParameters(left.CaTrace).Rise < 0);
    }

    [Fact]
    public void TheSheetDemoMatchesItsVectorsAndBonds()
    {
        var s = BiomolSheet.Build("GGGGGGG");
        // (§6.4's CA atoms, to 0.002 Å)
        Assert.InRange(BioV.Distance(s.Strands[1].CaTrace[0], new BioVec(16.074, 13.823, -2.443)), 0, 0.002);
        Assert.InRange(BioV.Distance(s.Strands[1].CaTrace[6], new BioVec(-1.508, 2.754, -3.470)), 0, 0.002);
        Assert.InRange(BioV.Distance(s.Strands[0].CaTrace[6], new BioVec(19.024, 11.090, 1.057)), 0, 0.002);
        var h12 = s.HBonds.Where(h => (h.Donor, h.Acceptor) is (0, 1) or (1, 0)).ToList();
        Assert.Equal(8, h12.Count);
        Assert.All(h12, h => Assert.InRange(h.Bond.Distance, 2.89, 2.91));
        Assert.Equal(6, s.HBonds.Count - h12.Count);
        // (a strand rises about 3.47 Å a residue)
        var st = BiomolPeptideBuilder.Build(new string('A', 12), All(BiomolSS.Strand, 12)).CaTrace;
        Assert.InRange(BioV.Distance(st[0], st[10]) / 10, 3.45, 3.49);
    }

    [Fact]
    public void ProlineBreaksAHelixFourInAndHasNoAmideH()
    {
        var p = BiomolPeptideBuilder.Build("AAAAPAAAAA", All(BiomolSS.Helix, 10));
        Assert.Equal(new[] { 4 }, p.ProlineBreaks);
        Assert.Equal("HHHCCHHHHH", BiomolSSes.Text(p.Built));
        Assert.Equal("HHHHHHHHHH", BiomolSSes.Text(p.Asked));
        Assert.Null(p.AtomIndex(4, "H"));
        Assert.Null(p.Backbones[4].H);
        Assert.Empty(BiomolPeptideBuilder.Build("AAPAAA", All(BiomolSS.Helix, 6)).ProlineBreaks);
        // (in a 3₁₀ helix too; not in a strand, where its φ is held at −75°)
        Assert.Equal(new[] { 4 }, BiomolPeptideBuilder.Build("AAAAPAA", All(BiomolSS.Helix310, 7)).ProlineBreaks);
        var strand = BiomolPeptideBuilder.Build("AAAPAAA", All(BiomolSS.Strand, 7));
        Assert.Empty(strand.ProlineBreaks);
        Assert.Equal(-75, strand.Phi[3]);
    }

    [Fact]
    public void TheFormsAtPH7AndNeutral()
    {
        var ph7 = BiomolPeptideBuilder.Build("KAD", All(BiomolSS.Coil, 3), ph7: true);
        var neutral = BiomolPeptideBuilder.Build("KAD", All(BiomolSS.Coil, 3), ph7: false);
        // (N-terminus –NH₃⁺ and C-terminus –COO⁻ at pH 7; –NH₂ and –COOH neutral)
        Assert.NotNull(ph7.AtomIndex(0, "H3"));
        Assert.Null(ph7.AtomIndex(2, "HXT"));
        Assert.Null(neutral.AtomIndex(0, "H3"));
        Assert.NotNull(neutral.AtomIndex(2, "HXT"));
        // (Lys NZ three H at pH 7, two neutral; Asp's OD2 without its H at pH 7)
        Assert.Equal(3, ph7.Atoms.Count(a => a.Residue == 0 && a.Name.StartsWith("HZ", StringComparison.Ordinal)));
        Assert.Equal(2, neutral.Atoms.Count(a => a.Residue == 0 && a.Name.StartsWith("HZ", StringComparison.Ordinal)));
        Assert.Null(ph7.AtomIndex(2, "HD2"));
        Assert.NotNull(neutral.AtomIndex(2, "HD2"));
        // (the charges marked at pH 7: the N-terminus and Lys +, the C-terminus and Asp −; none in the neutral form)
        var marks = BiomolScenes.PeptideChargeMarks(ph7, new BiomolLook(), BiomolChem.Plain);
        Assert.Equal(2, marks.Count(m => m.Text == "+"));
        Assert.Equal(2, marks.Count(m => m.Text == "−"));
        Assert.Empty(BiomolScenes.PeptideChargeMarks(neutral, new BiomolLook() with { Ph7 = false }, BiomolChem.Plain));
        // (every bond a sensible length in both)
        Assert.Empty(ph7.Model().Model.OddBonds());
        Assert.Empty(neutral.Model().Model.OddBonds());
    }

    [Fact]
    public void ClashReliefLeavesNoHeavyAtomsTouching()
    {
        // (§6.4: on random helices the worst contact was 2.69 Å; strands 3.04 Å)
        var rng = new BioRandom(7);
        const string letters = "ACDEFGHIKLMNQRSTVWY";
        for (var k = 0; k < 12; k++)
        {
            var s = new string(Enumerable.Range(0, 16).Select(_ => letters[rng.Int(letters.Length)]).ToArray());
            Assert.True(BioSelfCheck.BiomolWorstContact(BiomolPeptideBuilder.Build(s, All(BiomolSS.Helix, s.Length))) >= 2.6, s);
            Assert.True(BioSelfCheck.BiomolWorstContact(BiomolPeptideBuilder.Build(s, All(BiomolSS.Strand, s.Length))) >= 2.6, s);
        }
        // (Asp and Asn in a strand take their β rotamer)
        var dn = BiomolPeptideBuilder.Build("ADNA", All(BiomolSS.Strand, 4), relieve: false);
        Assert.Equal("m-20", dn.Rotamers[1]);
        Assert.Equal("m-80", dn.Rotamers[2]);
    }

    [Fact]
    public void TheCardsResultsForTheDefaultPeptide()
    {
        var r = BiomoleculeBuilderModel.Results(BiomolPeptideBuilder.DefaultSequence);
        Assert.Equal(19, r.Residues);
        Assert.Equal(18, r.Bonds);
        Assert.Equal(18, r.Waters);
        // (Σ residue masses 2265.68 + 18.02)
        Assert.Equal(2283.70, r.Mass, 2);
        Assert.Equal(4, r.Charge);
        Assert.Equal(-0.3, r.Gravy, 6);
        Assert.Equal(8, r.Classes[BioAAClass.Nonpolar]);
        Assert.Equal(6, r.Classes[BioAAClass.Polar]);
        Assert.Equal(0, r.Classes[BioAAClass.Acidic]);
        Assert.Equal(5, r.Classes[BioAAClass.Basic]);
    }

    [Fact]
    public void AResidueAddedTakesTheStructureBeforeIt()
    {
        var seq = BiomoleculeBuilderModel.Sequence;
        var ss = BiomoleculeBuilderModel.Structure;
        try
        {
            BiomoleculeBuilderModel.Sequence = "AAA";
            BiomoleculeBuilderModel.Structure = "CHE";
            BiomoleculeBuilderModel.SetSequence("AAAK");
            Assert.Equal("CHEE", BiomoleculeBuilderModel.Structure);
            BiomoleculeBuilderModel.SetSequence("AA");
            Assert.Equal("CH", BiomoleculeBuilderModel.Structure);
            Assert.Same(BiomoleculeBuilderModel.CurrentPeptide, BiomoleculeBuilderModel.CurrentPeptide);
        }
        finally
        {
            BiomoleculeBuilderModel.Sequence = seq;
            BiomoleculeBuilderModel.Structure = ss;
        }
    }

    [Fact]
    public void TheSequenceFieldsCleanTheirInput()
    {
        Assert.Equal(("MKTAY", (string?)null), BiomolInput.Protein("mkt ay"));
        Assert.Contains("“B” at 6", BiomolInput.Protein("MKTAYB").Message);
        Assert.Equal(40, BiomolInput.Protein(new string('G', 50)).Sequence.Length);
        Assert.Equal("ATGC", BiomolInput.Dna("5'-atgc-3'").Sequence);
        Assert.Contains("use T, not U", BiomolInput.Dna("AUGC").Message);
        Assert.Equal(12, BiomolInput.Dna(new string('A', 20)).Sequence.Length);
    }

    // ---- amino acids ----

    [Fact]
    public void AminoAcidsHaveTheirPartsAndHand()
    {
        foreach (var aa in BioAminoAcids.All)
        {
            var m = BiomolAminoAcid.Model(aa.One, ph7: true);
            var t = BiomolAminoAcid.Template(aa.One)!;
            // (L: CB below the frame's xy-plane; the mirror above it)
            if (aa.IsChiral)
            {
                Assert.True(m.Atoms.First(a => a.Name == "CB").Pos.Z < 0, aa.Name);
                Assert.True(BiomolAminoAcid.Model(aa.One, true, mirrored: true).Atoms.First(a => a.Name == "CB").Pos.Z > 0, aa.Name);
            }
            Assert.Equal(BiomolParts.Amino, BiomolAminoAcid.Part("N", t));
            Assert.Equal(BiomolParts.Alpha, BiomolAminoAcid.Part("CA", t));
            Assert.Equal(BiomolParts.Carboxyl, BiomolAminoAcid.Part("OXT", t));
            // (the zwitterion's net charge is the side chain's)
            Assert.Equal(aa.Charge, BiomolAminoAcid.Charges(aa.One, ph7: true).Values.Sum());
            Assert.Empty(BiomolAminoAcid.Charges(aa.One, ph7: false));
            Assert.Equal(4, BiomolScenes.AminoAcid(aa.One, new BiomolLook(), false, BiomolChem.Plain).Labels.Count);
        }
        Assert.Null(BiomolAminoAcid.CornClockwise(BiomolAminoAcid.Model('G', true)));
        // (Pro's R group closes on its N; that N is in the amino group)
        var pro = BiomolAminoAcid.Template('P')!;
        Assert.Equal(BiomolParts.RGroup, BiomolAminoAcid.Part("CD", pro));
        Assert.Contains(BiomolAminoAcid.Sketch('P').Atoms, a => a.Name == "N");
    }

    // ---- nucleic acids ----

    [Fact]
    public void AStrandHasItsPhosphodiesterBonds()
    {
        var d = BiomolStrand.Build("ATGCGT", complement: false);
        var links = BiomolStrand.Linkages(d, 1);
        Assert.Equal(5, links.Count);
        foreach (var l in links)
        {
            Assert.Equal("C3'", d.Atoms[l[0]].Name);
            Assert.Equal("O3'", d.Atoms[l[1]].Name);
            Assert.Equal("P", d.Atoms[l[2]].Name);
            Assert.Equal(1.580, BioV.Distance(d.Atoms[l[1]].Pos, d.Atoms[l[2]].Pos), 2);
            Assert.Equal(d.Atoms[l[0]].Pair + 1, d.Atoms[l[2]].Pair);
        }
        Assert.Empty(d.HBonds);
        Assert.Equal(15, BiomolStrand.Build("ATGCGT", complement: true).HBonds.Count);
        Assert.Equal(3, BiomolScenes.StrandAdded(d, new BiomolLook(), BiomolChem.Plain).Groups.Count);
        Assert.Equal(3, BiomolScenes.StrandRemoved(d, new BiomolLook(), BiomolChem.Plain).Groups.Count);
        var hidden = BiomolScenes.Strand(d, new BiomolLook(), new BiomolScenes.StrandOptions(5), BiomolChem.Plain);
        Assert.DoesNotContain(hidden.Items, i => (i.Id ?? "").StartsWith("nt:1:5:", StringComparison.Ordinal));
    }

    // ---- sugars and lipids ----

    [Fact]
    public void AlphaAndBetaGlucoseDifferAtC1()
    {
        var a = BioData.Molecule("GLC")!;
        var b = BioData.Molecule("BGC")!;
        var ra = BiomolSugar.RingOf(a)!;
        var rb = BiomolSugar.RingOf(b)!;
        Assert.Equal("C1", ra.Anomeric);
        Assert.Equal("C6", ra.Reference);
        Assert.True(BiomolSugar.IsAlpha(a, ra));
        Assert.False(BiomolSugar.IsAlpha(b, rb));
        // (fructose's anomeric carbon is C2)
        Assert.Equal("C2", BiomolSugar.RingOf(BioData.Molecule("FRU")!)!.Anomeric);
        // (the ring carbons numbered 1–6 on the sugar's marks)
        var marks = BiomolScenes.Monosaccharide("GLC", new BiomolLook(), BiomolChem.Plain).Marks.Select(m => m.Text).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, marks);
    }

    [Fact]
    public void HydrolysisSplitsADisaccharideIntoTwoMonosaccharides()
    {
        foreach (var code in BiomolSugar.Disaccharides)
        {
            var m = BioData.Molecule(code)!;
            var s = BiomolSugar.Split(m)!.Value;
            Assert.Equal(m.Atoms.Count + 3, s.Model.Atoms.Count);
            // (each sugar alone is C6H12O6)
            for (var r = 0; r < 2; r++)
            {
                var atoms = s.Model.Atoms.Where(a => s.Residue[a.Id] == r).ToList();
                Assert.Equal(6, atoms.Count(a => a.Element == "C"));
                Assert.Equal(12, atoms.Count(a => a.Element == "H"));
                Assert.Equal(6, atoms.Count(a => a.Element == "O"));
            }
            Assert.Equal(7, BiomolScenes.DisaccharideMotion(code, true, new BiomolLook(), BiomolChem.Plain).Groups.Count);
            Assert.Equal(6, BiomolScenes.DisaccharideMotion(code, false, new BiomolLook(), BiomolChem.Plain).Groups.Count);
            Assert.Contains(BiomolScenes.Disaccharide(code, false, new BiomolLook(), BiomolChem.Plain).Content.Items, i => i.Id == "glyco:0");
        }
    }

    [Fact]
    public void OleicAcidKinksAndFatsHaveTheirEsterBonds()
    {
        Assert.InRange(BiomolLipid.KinkAngle(BioData.Molecule("OLA")!)!.Value, 25, 90);
        Assert.InRange(BiomolLipid.KinkAngle(BioData.Molecule("STE")!)!.Value, 0, 10);
        Assert.Equal(3, BiomolLipid.EsterBonds("TGL").Length);
        var pov = BioData.Molecule("POV")!;
        // (POPC's choline and phosphate are its hydrophilic head; its tails hydrophobic)
        Assert.True(BiomolLipid.IsHydrophilic(BiomolLipid.Detail("N", pov)));
        Assert.True(BiomolLipid.IsHydrophilic(BiomolLipid.Detail("P", pov)));
        Assert.False(BiomolLipid.IsHydrophilic(BiomolLipid.Detail("C218", pov)));
        var c = BiomolScenes.Lipid("TGL", new BiomolLook(), BiomolChem.Plain);
        Assert.Equal(3, c.Items.Count(i => (i.Id ?? "").StartsWith("ester:", StringComparison.Ordinal)));
    }

    // ---- protein structure levels ----

    [Fact]
    public void TheStructureLevels()
    {
        var primary = BiomolScenes.Primary("MKTAY", new BiomolLook());
        Assert.Equal(5, primary.Content.Items.Count(i => (i.Id ?? "").StartsWith("bead:", StringComparison.Ordinal)));
        Assert.Equal(4, primary.Content.Items.Count(i => (i.Id ?? "").StartsWith("pbond:", StringComparison.Ordinal)));
        Assert.Equal("MKTAY", string.Concat(primary.Marks.Select(m => m.Text)));
        // (the beads a CA–CA step apart, the rows turning back)
        var at = BiomolScenes.BeadPlaces(12);
        Assert.All(at.Zip(at.Skip(1)).Where((_, i) => i != 9), p => Assert.Equal(3.8, BioV.Distance(p.First, p.Second), 6));
        Assert.Equal(at[9].X, at[10].X, 6);
        // (ubiquitin: its nonpolar side chains sit nearer its centre than its polar and charged ones; no Cys)
        var (np, other, npn, othern) = BiomolProteins.CoreDistances();
        Assert.True(np < other, $"{np} vs {other}");
        Assert.Equal(76, npn + othern + BioData.Ubiquitin.Count(r => r.Aa == 'G'));
        Assert.DoesNotContain(BioData.Ubiquitin, r => r.Aa == 'C');
        // (denatured: the same chain as a random coil, 3.8 Å steps)
        var den = BiomolProteins.Denatured(false);
        Assert.NotEmpty(den.Items);
        Assert.NotNull(den.Frame);
        // (haemoglobin: four chains, four hemes, β6 Glu marked on both β chains)
        var q = BiomolProteins.Quaternary(false);
        Assert.Equal(2, q.Items.Count(i => (i.Id ?? "").StartsWith("glu6:", StringComparison.Ordinal)));
        Assert.All(BioData.Hemoglobin.Residues.Where(r => r.Chain is 'B' or 'D' && r.I == 6), r => Assert.Equal('E', r.Aa));
    }

    // ---- motions and the hand-off ----

    [Fact]
    public void MotionsRunTheirCourse()
    {
        var p = BiomolPeptideBuilder.Build("AKA", All(BiomolSS.Coil, 3));
        var m = BiomolScenes.PeptideAdded(p, new BiomolLook(), BiomolChem.Plain);
        Assert.InRange(m.Length, 0.8, 1.2);
        // (the new residue slides in from 7 Å off and fades in; the water lifts off and fades out; the bond flashes)
        var (start, alpha0) = BiomolMotion.At(m.Groups[0], 0);
        var (end, alpha1) = BiomolMotion.At(m.Groups[0], m.Length);
        Assert.Equal(7, start.X, 6);
        Assert.Equal(0, BioV.Length(end), 6);
        Assert.Equal(0, alpha0, 6);
        Assert.Equal(1, alpha1, 6);
        Assert.Equal(0, BiomolMotion.At(m.Groups[1], m.Length).Alpha, 6);
        Assert.Equal(0, BiomolMotion.At(m.Groups[2], 0).Alpha, 6);
        Assert.True(BiomolMotion.At(m.Groups[2], m.Groups[2].Delay + m.Groups[2].Duration / 2).Alpha > 0.99);
        // (the water is H₂O: 0.96 Å and 104.5°)
        Assert.Equal(3, BiomolScenes.Water(BioVec.Zero, new BiomolLook(), BiomolChem.Plain).Count(i => (i.Id ?? "").StartsWith("water:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheHandOffReadsBackAsItWasWritten()
    {
        var model = BiomolAminoAcid.Model('W', ph7: true);
        var (title, atoms, bonds) = BiomolHandOff.Parse(BiomolHandOff.Text(model, "Tryptophan"));
        Assert.Equal("Tryptophan", title);
        Assert.Equal(model.Atoms.Count, atoms.Count);
        Assert.Equal(model.Bonds.Count, bonds.Count);
        Assert.All(atoms.Zip(model.Atoms), x => Assert.True(BioV.Distance(x.First.Pos, x.Second.Pos) < 0.001));
        var (_, heavy, heavyBonds) = BiomolHandOff.Parse(BiomolHandOff.Text(model, "Tryptophan", hydrogens: false));
        Assert.Equal(model.Atoms.Count(a => a.Element != "H"), heavy.Count);
        Assert.All(heavyBonds, b => Assert.True(b.A < heavy.Count && b.B < heavy.Count));
    }
}
