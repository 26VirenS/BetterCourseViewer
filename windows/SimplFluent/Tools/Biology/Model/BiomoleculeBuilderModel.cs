using System.Globalization;

namespace Simpl.Tools;

// Biomolecule Builder's own model (spec §4.4, §6.4), the Mac's BiomoleculeBuilderModel.swift: what the tool works out and
// keeps without a window — the peptide builder (BiomolPeptide.cs), the cartoons and the structure levels
// (BiomolProteins.cs), the monomers' forms and parts, the strand, sugar and lipid builders (BiomolMolecules.cs), and the
// scenes the view shows (BiomolScenes.cs) — in a static class, so it outlives the view being built again (the tools'
// rule: a view subscribes to Changed on Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit tests reach it;
// its bio-check lines (BiomolChecks) run with the others in BioSelfCheck.Run.

/// <summary>Biomolecule Builder's state, kept across its views: the shared view choices and each page's own.</summary>
public static class BiomoleculeBuilderModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    // ---- the shared controls (every page) ----

    public static BioAtomStyle Style { get; set; } = BioAtomStyle.BallStick;
    /// <summary>"pH 7 form" (default on: zwitterions and ionised groups, as cells have them).</summary>
    public static bool Ph7 { get; set; } = true;
    public static BiomolColourBy ColourBy { get; set; } = BiomolColourBy.Element;
    public static bool Labels { get; set; } = true;
    /// <summary>"Show H" as chosen on each page (none chosen: on for a monomer, off for a polymer over 200 atoms).</summary>
    public static Dictionary<string, bool> HydrogensChosen { get; } = new();

    /// <summary>Whether a page shows its H atoms: as chosen there, else on up to 200 atoms (§4.4).</summary>
    public static bool Hydrogens(string page, int atoms) => HydrogensChosen.TryGetValue(page, out var h) ? h : atoms <= 200;

    /// <summary>The look a scene is drawn in.</summary>
    public static BiomolLook Look(bool hydrogens, bool dark) => new(Style, hydrogens, Ph7, ColourBy, Labels, dark);

    // ---- Amino acids ----

    /// <summary>The amino acid shown (one-letter code).</summary>
    public static char AminoAcid { get; set; } = 'K';
    /// <summary>The mirror image (the D-form, not used in proteins).</summary>
    public static bool Mirrored { get; set; }

    // ---- Proteins ----

    /// <summary>"build" (the peptide builder) or "levels" (the four levels of structure).</summary>
    public static string ProteinView { get; set; } = "build";
    public static string Sequence { get; set; } = BiomolPeptideBuilder.DefaultSequence;
    /// <summary>Each residue's structure as letters (C, H, E, P, G, L; §4.4: coil by default).</summary>
    public static string Structure { get; set; } = "CCHHHHHHHHHHHHCEEEE";
    public static bool ShowHBonds { get; set; } = true;
    public static BiomolRibbon Ribbon { get; set; } = BiomolRibbon.Off;
    /// <summary>The β-sheet demo shown instead of the built peptide.</summary>
    public static bool SheetDemo { get; set; }
    /// <summary>"primary", "secondary", "tertiary" or "quaternary".</summary>
    public static string Level { get; set; } = "tertiary";
    public static BiomolCentroids Centroids { get; set; } = BiomolCentroids.All;
    /// <summary>Ubiquitin unfolded into a random coil (the Denature button).</summary>
    public static bool Denatured { get; set; }

    public static readonly string[] Levels = { "primary", "secondary", "tertiary", "quaternary" };

    public static string LevelTitle(string level) => level switch
    {
        "primary" => "Primary",
        "secondary" => "Secondary",
        "quaternary" => "Quaternary",
        _ => "Tertiary",
    };

    /// <summary>The structures for the sequence (padded with coil, cut to its length).</summary>
    public static BiomolSS[] StructureFor(string sequence) => BiomolSSes.Parse(Structure, sequence.Length);

    /// <summary>The sequence and structures changed together (a residue added or taken off keeps the others' structures;
    /// a new residue takes the structure of the one before it).</summary>
    public static void SetSequence(string sequence)
    {
        var old = StructureFor(Sequence);
        var ss = new BiomolSS[sequence.Length];
        for (var i = 0; i < ss.Length; i++) ss[i] = i < old.Length ? old[i] : (i > 0 ? (i - 1 < old.Length ? old[i - 1] : ss[i - 1]) : BiomolSS.Coil);
        Sequence = sequence;
        Structure = BiomolSSes.Text(ss);
    }

    private static readonly object peptideLock = new();
    private static readonly List<(string Key, BiomolPeptide Peptide)> peptides = new();

    /// <summary>The peptide for a sequence, structures and form, built once and kept (the last four), from any thread.</summary>
    public static BiomolPeptide Peptide(string sequence, IReadOnlyList<BiomolSS> ss, bool ph7)
    {
        var key = $"{sequence}|{BiomolSSes.Text(ss)}|{ph7}";
        lock (peptideLock)
        {
            var hit = peptides.FindIndex(p => p.Key == key);
            if (hit >= 0) return peptides[hit].Peptide;
        }
        var made = BiomolPeptideBuilder.Build(sequence, ss, ph7);
        lock (peptideLock)
        {
            if (peptides.FindIndex(p => p.Key == key) is var i and >= 0) return peptides[i].Peptide;
            peptides.Add((key, made));
            if (peptides.Count > 4) peptides.RemoveAt(0);
        }
        return made;
    }

    /// <summary>The peptide as the page has it now.</summary>
    public static BiomolPeptide CurrentPeptide => Peptide(Sequence, StructureFor(Sequence), Ph7);

    // ---- Nucleic acids ----

    /// <summary>"nucleotides" or "strand".</summary>
    public static string NucleicView { get; set; } = "nucleotides";
    public static string Nucleotide { get; set; } = "DA";
    public static string Strand { get; set; } = BiomolStrand.DefaultSequence;
    public static bool Complement { get; set; }

    // ---- Carbohydrates ----

    /// <summary>"mono", "di" or "poly".</summary>
    public static string CarbView { get; set; } = "mono";
    public static string Monosaccharide { get; set; } = "GLC";
    /// <summary>α- and β-glucose side by side (when either is chosen).</summary>
    public static bool CompareAnomers { get; set; }
    public static string Disaccharide { get; set; } = "MAL";
    /// <summary>The disaccharide split by water (hydrolysed).</summary>
    public static bool Split { get; set; }
    public static BiomolPolysaccharide Polysaccharide { get; set; } = BiomolPolysaccharide.Amylose;

    // ---- Lipids ----

    /// <summary>A lipid's code, or "STE+OLA" for stearic and oleic acids side by side.</summary>
    public static string Lipid { get; set; } = "OLA";

    public static readonly string[] LipidChoices = { "GOL", "PLM", "STE", "OLA", "STE+OLA", "TGL", "POV", "CLR" };

    public static string LipidTitle(string code) => code == "STE+OLA" ? "Saturated vs unsaturated" : BiomolLipid.ShortName(code);

    // ---- the results a peptide's card shows (§4.4 Proteins item 1) ----

    /// <summary>Residues, peptide bonds, waters released, mass (Σ residue masses + 18.02), net charge at pH 7 (K and R +1,
    /// D and E −1, His 0, the two ends +1 and −1), GRAVY and counts by class.</summary>
    public static (int Residues, int Bonds, int Waters, double Mass, int Charge, double Gravy, Dictionary<BioAAClass, int> Classes) Results(string sequence)
    {
        var n = sequence.Length;
        var classes = BioAAClasses.All.ToDictionary(c => c, c => sequence.Count(x => BioAminoAcids.ByOne.TryGetValue(x, out var a) && a.Group == c));
        return (n, Math.Max(n - 1, 0), Math.Max(n - 1, 0), n == 0 ? 0 : BioAminoAcids.PeptideMass(sequence), BioAminoAcids.NetCharge(sequence),
            BioAminoAcids.Gravy(sequence), classes);
    }
}

/// <summary>What the sequence fields take (§3.7's cleaning): a peptide's one-letter codes (the 20, up to 40) and a strand's
/// DNA letters (A, C, G, T, up to 12), with the inline message when something is left out.</summary>
public static class BiomolInput
{
    public static (string Sequence, string? Message) Protein(string text)
    {
        var input = new BioSequenceInput(text, BioSeqKind.Protein, BiomolPeptideBuilder.MaxResidues);
        var bad = input.Invalid.Select(i => (i.Letter, i.Positions)).ToList();
        foreach (var c in new[] { 'X', '*' })
        {
            var at = input.Letters.Select((x, k) => (x, k)).Where(p => p.x == c).Select(p => p.k + 1).ToList();
            if (at.Count > 0) bad.Add((c, at));
        }
        var seq = new string(input.Letters.Where(x => BioAminoAcids.ByOne.ContainsKey(x)).ToArray());
        if (bad.Count > 0)
            return (seq, "Use the 20 amino-acid letters. Left out: " + string.Join("; ", bad.Select(b => $"“{b.Letter}” at {string.Join(", ", b.Positions.Take(6))}")) + ".");
        if (input.Truncated) return (seq, $"Only the first {BiomolPeptideBuilder.MaxResidues} residues are built.");
        return (seq, null);
    }

    public static (string Sequence, string? Message) Dna(string text)
    {
        var input = new BioSequenceInput(text, BioSeqKind.Dna, BiomolStrand.MaxLength);
        var seq = new string(input.Letters.Where(x => "ACGT".Contains(x)).ToArray());
        if (input.Suggestion == BioSeqKind.Rna) return (seq, "This strand is DNA: use T, not U (RNA's U is left out).");
        var bad = input.Invalid.Select(i => (i.Letter, i.Positions)).ToList();
        var ns = input.Letters.Select((x, k) => (x, k)).Where(p => p.x == 'N').Select(p => p.k + 1).ToList();
        if (ns.Count > 0) bad.Add(('N', ns));
        if (bad.Count > 0)
            return (seq, "A strand is built from A, C, G and T. Left out: " + string.Join("; ", bad.Select(b => $"“{b.Letter}” at {string.Join(", ", b.Positions.Take(6))}")) + ".");
        if (input.Truncated) return (seq, $"Only the first {BiomolStrand.MaxLength} nucleotides are built.");
        return (seq, null);
    }
}

/// <summary>How the builder writes things: formulas with subscripts, signed charges, the atom a picked id names.</summary>
public static class BiomolText
{
    /// <summary>A formula with subscript digits: "C9H11NO2" or "C10 H14 N5 O6 P" → "C₉H₁₁NO₂", "C₁₀H₁₄N₅O₆P".</summary>
    public static string Formula(string f) => new(f.Where(c => c != ' ').Select(c => c is >= '0' and <= '9' ? (char)('₀' + (c - '0')) : c).ToArray());

    /// <summary>A signed whole number: "+1", "0", "−2".</summary>
    public static string Signed(int n) => n > 0 ? $"+{n}" : (n < 0 ? $"−{-n}" : "0");

    /// <summary>An element's name for an atom card.</summary>
    public static string Element(string symbol) => symbol switch
    {
        "H" => "Hydrogen", "C" => "Carbon", "N" => "Nitrogen", "O" => "Oxygen", "P" => "Phosphorus", "S" => "Sulfur", "Fe" => "Iron", _ => symbol,
    };

    /// <summary>The atom an id names ("…:atom:31" → 31; "atom:31" → 31).</summary>
    public static int? AtomIndex(string? id)
    {
        if (id == null) return null;
        var k = id.LastIndexOf(":atom:", StringComparison.Ordinal);
        if (k >= 0) return int.TryParse(id[(k + 6)..], out var n) ? n : null;
        return id.StartsWith("atom:", StringComparison.Ordinal) && int.TryParse(id[5..], out var m) ? m : null;
    }
}

/// <summary>The app's motion curve for the builder's short motions: a strong ease-out, cubic-bezier(0.23, 1, 0.32, 1).</summary>
public static class BiomolEase
{
    public static double StrongOut(double t) => CubicBezier(0.23, 1, 0.32, 1, t);

    /// <summary>y at x = t on the cubic Bézier from (0, 0) to (1, 1) with control points (x1, y1) and (x2, y2) (Newton's
    /// method on x, then bisection if it stalls).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        var x = Math.Clamp(t, 0, 1);
        static double Bez(double s, double a, double b) => 3 * (1 - s) * (1 - s) * s * a + 3 * (1 - s) * s * s * b + s * s * s;
        static double Slope(double s, double a, double b) => 3 * (1 - s) * (1 - s) * a + 6 * (1 - s) * s * (b - a) + 3 * s * s * (1 - b);
        var s = x;
        for (var i = 0; i < 8; i++)
        {
            var dx = Bez(s, x1, x2) - x;
            var d = Slope(s, x1, x2);
            if (Math.Abs(dx) < 1e-7) return Bez(s, y1, y2);
            if (Math.Abs(d) < 1e-6) break;
            s -= dx / d;
        }
        double lo = 0, hi = 1;
        s = x;
        for (var i = 0; i < 40; i++)
        {
            var v = Bez(s, x1, x2);
            if (Math.Abs(v - x) < 1e-7) break;
            if (v < x) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Bez(s, y1, y2);
    }
}

public static partial class BioSelfCheck
{
    /// <summary>Biomolecule Builder's own bio-check lines, against §6.4's test vectors and §10.1 items 3–4 (the Mac's
    /// biomolChecks, line for line).</summary>
    static partial void BiomolChecks(BioCheckReport r)
    {
        BiomolPeptideChecks(r);
        BiomolMonomerChecks(r);
        BiomolSugarLipidChecks(r);
        BiomolSceneChecks(r);
    }

    // ---- the peptide builder (§6.4, §10.1 items 3 and 4) ----

    public static void BiomolPeptideChecks(BioCheckReport r)
    {
        // (Ala₄ as an α-helix against the test vector: every listed atom within 0.002 Å)
        var ala4 = BiomolPeptideBuilder.Build("AAAA", Enumerable.Repeat(BiomolSS.Helix, 4).ToArray(), ph7: true);
        double worst = 0;
        var missing = new List<string>();
        var listed = 0;
        foreach (var line in BioText.Lines(BioData.PeptideTestText))
        {
            var f = BioText.Words(line);
            if (f.Length != 6 || f[1] != "ALA" || BioText.Int(f[0]) is not { } i || BioText.Number(f[3]) is not { } x || BioText.Number(f[4]) is not { } y || BioText.Number(f[5]) is not { } z) continue;
            listed++;
            if (ala4.AtomIndex(i, f[2]) is not { } k) { missing.Add($"{i}{f[2]}"); continue; }
            worst = Math.Max(worst, BioV.Distance(ala4.Atoms[k].Pos, new BioVec(x, y, z)));
        }
        r.Check("biomol.Ala4-helix", $"{listed},worst{F(worst, "0.0000")}", listed == 25 && missing.Count == 0 && worst <= 0.002);

        // (poly-Ala₂₀: 3.62 residues a turn, 1.56 Å a residue, right-handed; O(i)···N(i+4) 3.10 Å for every i)
        var ala20 = BiomolPeptideBuilder.Build(new string('A', 20), Enumerable.Repeat(BiomolSS.Helix, 20).ToArray());
        var hp = BiomolPeptideBuilder.HelixParameters(ala20.CaTrace);
        r.Near("biomol.helix-per-turn", hp.PerTurn, 3.62, 0.01, 3);
        r.Near("biomol.helix-rise", hp.Rise, 1.56, 0.01, 3);
        r.Near("biomol.helix-radius", hp.Radius, 2.28, 0.01, 3);
        var on = Enumerable.Range(0, 16).Select(i => BioV.Distance(ala20.Atoms[ala20.Backbones[i].O].Pos, ala20.Atoms[ala20.Backbones[i + 4].N].Pos)).ToList();
        var onOff = on.Select(d => Math.Abs(d - 3.10)).DefaultIfEmpty(1).Max();
        r.Check("biomol.helix-O(i)N(i+4)", $"{on.Count},{F(on.FirstOrDefault(), "0.000")}", on.Count == 16 && onOff <= 0.01);
        var hb = ala20.HBonds;
        r.Check("biomol.helix-H-bonds", $"{hb.Count}", hb.Count == 16 && hb.All(h => h.Donor - h.Acceptor == 4));
        var trace = ala20.CaTrace;
        var caca = trace.Zip(trace.Skip(1)).Select(p => BioV.Distance(p.First, p.Second)).ToList();
        r.Near("biomol.CA-CA", caca.Sum() / caca.Count, 3.80, 0.01, 3);
        // (a β-strand rises 3.47 Å a residue)
        var strand = BiomolPeptideBuilder.Build(new string('A', 12), Enumerable.Repeat(BiomolSS.Strand, 12).ToArray());
        var st = strand.CaTrace;
        r.Near("biomol.strand-rise", BioV.Distance(st[0], st[10]) / 10, 3.47, 0.02, 3);

        // (the β-sheet: S2's CA atoms against the test vector; 8 S1–S2 H-bonds of 2.90 Å, 6 between S2 and S3)
        var sheet = BiomolSheet.Build("GGGGGGG");
        double s2worst = 0;
        var s2listed = 0;
        var section = 0;
        foreach (var line in BioText.Lines(BioData.PeptideTestText))
        {
            if (line.StartsWith("Gly7", StringComparison.Ordinal)) { section = 1; continue; }
            if (line.StartsWith("S2", StringComparison.Ordinal)) { section = 2; continue; }
            var f = BioText.Words(line);
            if (section != 2 || f.Length != 5 || f[1] != "CA" || BioText.Int(f[0]) is not { } i || BioText.Number(f[2]) is not { } x || BioText.Number(f[3]) is not { } y || BioText.Number(f[4]) is not { } z) continue;
            s2listed++;
            s2worst = Math.Max(s2worst, BioV.Distance(sheet.Strands[1].CaTrace[i], new BioVec(x, y, z)));
        }
        r.Check("biomol.sheet-S2", $"{s2listed},worst{F(s2worst, "0.0000")}", s2listed == 7 && s2worst <= 0.002);
        var h12 = sheet.HBonds.Where(h => (h.Donor, h.Acceptor) is (0, 1) or (1, 0)).Select(h => h.Bond.Distance).ToList();
        var h23 = sheet.HBonds.Where(h => (h.Donor, h.Acceptor) is (1, 2) or (2, 1)).Select(h => h.Bond.Distance).ToList();
        r.Check("biomol.sheet-H-bonds", $"{h12.Count}+{h23.Count},{F(h12.DefaultIfEmpty(0).Min(), "0.000")}-{F(h12.DefaultIfEmpty(0).Max(), "0.000")}",
            h12.Count == 8 && h12.All(d => Math.Abs(d - 2.90) <= 0.01) && h23.Count == 6);

        // (the default peptide as a helix: the clash relief leaves no heavy atoms of different residues closer than 2.6 Å —
        // but for backbone neighbours and the backbone N···O pairs — and every bond 0.9–1.9 Å)
        var seq = BiomolPeptideBuilder.DefaultSequence;
        var helix = BiomolPeptideBuilder.Build(seq, Enumerable.Repeat(BiomolSS.Helix, seq.Length).ToArray());
        r.Check("biomol.default-bonds", $"{helix.Atoms.Count}atoms", helix.Model().Model.OddBonds().Count == 0 && helix.Atoms.Count > 300);
        var contact = BiomolWorstContact(helix);
        r.Check("biomol.clash-helix", F(contact, "0.00"), contact >= 2.6);
        var beta = BiomolPeptideBuilder.Build(seq, Enumerable.Repeat(BiomolSS.Strand, seq.Length).ToArray());
        var contactB = BiomolWorstContact(beta);
        r.Check("biomol.clash-strand", F(contactB, "0.00"), contactB >= 2.6);
        // (proline: four into a helix it breaks it; the first three it does not)
        var pro = BiomolPeptideBuilder.Build("AAAAPAAAAA", Enumerable.Repeat(BiomolSS.Helix, 10).ToArray());
        var early = BiomolPeptideBuilder.Build("AAPAAA", Enumerable.Repeat(BiomolSS.Helix, 6).ToArray());
        r.Check("biomol.proline-rule", $"[{string.Join(", ", pro.ProlineBreaks)}]" + BiomolSSes.Text(pro.Built),
            pro.ProlineBreaks.SequenceEqual(new[] { 4 }) && pro.Built[3] == BiomolSS.Coil && pro.Built[4] == BiomolSS.Coil && pro.Built[5] == BiomolSS.Helix
            && early.ProlineBreaks.Count == 0 && pro.AtomIndex(4, "H") == null);
        // (a Pro in a strand keeps its ring's φ: its CD no nearer the C before it than the bonded 1–3 distance)
        var proStrand = BiomolPeptideBuilder.Build("AAAPAAA", Enumerable.Repeat(BiomolSS.Strand, 7).ToArray());
        var cd = proStrand.AtomIndex(3, "CD") is { } cdi ? BioV.Distance(proStrand.Atoms[cdi].Pos, proStrand.Atoms[proStrand.Backbones[2].C].Pos) : 0;
        r.Check("biomol.proline-strand", $"phi{F(proStrand.Phi[3], "0")},C-CD{F(cd, "0.00")}", proStrand.Phi[3] == -75 && cd > 2.2);
        // (the forms: Ala₃ at pH 7 has H3 on its N-terminus and no HXT; the neutral one HXT and no H3; Lys's NZ keeps three
        // H at pH 7 and two in the neutral form)
        var coil3 = new[] { BiomolSS.Coil, BiomolSS.Coil, BiomolSS.Coil };
        var k7 = BiomolPeptideBuilder.Build("KAK", coil3, ph7: true);
        var k0 = BiomolPeptideBuilder.Build("KAK", coil3, ph7: false);
        int hz7 = k7.Atoms.Count(a => a.Name.StartsWith("HZ", StringComparison.Ordinal)), hz0 = k0.Atoms.Count(a => a.Name.StartsWith("HZ", StringComparison.Ordinal));
        var hxt = k0.AtomIndex(2, "HXT") is { } hi ? BioV.Distance(k0.Atoms[hi].Pos, k0.Atoms[k0.AtomIndex(2, "OXT") ?? 0].Pos) : 0;
        r.Check("biomol.forms", $"HZ{hz7}/{hz0},OH{F(hxt, "0.00")}",
            k7.AtomIndex(0, "H3") != null && k7.AtomIndex(2, "HXT") == null && k0.AtomIndex(0, "H3") == null && hz7 == 6 && hz0 == 4 && Math.Abs(hxt - 0.97) < 0.02);
    }

    /// <summary>The closest approach of heavy atoms in different residues, but for backbone atoms of neighbouring residues
    /// and the backbone's own N···O pairs (the script's measure).</summary>
    public static double BiomolWorstContact(BiomolPeptide p)
    {
        var heavy = p.Atoms.Where(a => a.Element != "H").ToList();
        var bb = new HashSet<string> { "N", "CA", "C", "O" };
        var w = 9.0;
        for (var x = 0; x < heavy.Count; x++)
            for (var y = x + 1; y < heavy.Count; y++)
            {
                BiomolPeptide.Atom a = heavy[x], b = heavy[y];
                var di = Math.Abs(a.Residue - b.Residue);
                if (di == 0) continue;
                if (di == 1 && bb.Contains(a.Name) && bb.Contains(b.Name)) continue;
                if ((a.Name is "N" or "O") && (b.Name is "N" or "O")) continue;
                w = Math.Min(w, BioV.Distance(a.Pos, b.Pos));
            }
        return w;
    }

    // ---- amino acids and nucleotides ----

    public static void BiomolMonomerChecks(BioCheckReport r)
    {
        // (each free amino acid's neutral form has §6.2's formula; its pH 7 form the same less an H for Asp and Glu, plus one
        // for Lys and Arg, its net charge 0, −1 or +1 as §6.2 says)
        var formulaWrong = new List<string>();
        var chargeWrong = new List<string>();
        foreach (var aa in BioAminoAcids.All)
        {
            if (BiomolAminoAcid.Template(aa.One) is not { } t) { formulaWrong.Add(aa.Three); continue; }
            var want = BiomolFormulaCounts(aa.Formula);
            var have = new Dictionary<string, int>();
            foreach (var a in BiomolAminoAcid.Atoms(t, ph7: false)) have[a.Element] = have.GetValueOrDefault(a.Element) + 1;
            if (have.Count != want.Count || have.Any(kv => want.GetValueOrDefault(kv.Key) != kv.Value)) formulaWrong.Add(aa.Three);
            var h7 = BiomolAminoAcid.Atoms(t, ph7: true).Count(a => a.Element == "H");
            var shift = aa.One is 'D' or 'E' ? -1 : (aa.One is 'K' or 'R' ? 1 : 0);
            var net = BiomolAminoAcid.Charges(aa.One, ph7: true).Values.Sum();
            if (h7 != want.GetValueOrDefault("H") + shift || net != aa.Charge) chargeWrong.Add($"{aa.Three}{net}");
        }
        r.Check("biomol.aa-formulas", formulaWrong.Count == 0 ? "20" : string.Join(",", formulaWrong), formulaWrong.Count == 0);
        r.Check("biomol.aa-pH7-charge", chargeWrong.Count == 0 ? "20" : string.Join(",", chargeWrong), chargeWrong.Count == 0);
        // (CORN: every L-amino acid's CO → R → N runs clockwise seen from its Hα; the mirror image counter-clockwise)
        var cornWrong = new List<string>();
        foreach (var aa in BioAminoAcids.All.Where(a => a.IsChiral))
        {
            var l = BiomolAminoAcid.CornClockwise(BiomolAminoAcid.Model(aa.One, ph7: true));
            var d = BiomolAminoAcid.CornClockwise(BiomolAminoAcid.Model(aa.One, ph7: true, mirrored: true));
            if (l != true || d != false) cornWrong.Add(aa.Three);
        }
        var gly = BiomolAminoAcid.CornClockwise(BiomolAminoAcid.Model('G', ph7: true));
        r.Check("biomol.CORN", cornWrong.Count == 0 ? "19L" : string.Join(",", cornWrong), cornWrong.Count == 0 && gly == null);
        // (the parts: every amino acid has all four; glycine's R group is one H; proline's N is in its amino group)
        var partsWrong = new List<string>();
        foreach (var aa in BioAminoAcids.All)
        {
            if (BiomolAminoAcid.Template(aa.One) is not { } t) continue;
            var ps = t.Atoms.Select(a => BiomolAminoAcid.Part(a.Name, t).Id).ToHashSet();
            if (!ps.SetEquals(new[] { "amino", "alpha", "carboxyl", "r" })) partsWrong.Add(aa.Three);
        }
        if (BiomolAminoAcid.Template('G') is { } g)
        {
            var rg = g.Atoms.Count(a => BiomolAminoAcid.Part(a.Name, g) == BiomolParts.RGroup);
            if (rg != 1) partsWrong.Add($"GLY-R{rg}");
        }
        if (BiomolAminoAcid.Template('P') is { } pt && BiomolAminoAcid.Part("N", pt) != BiomolParts.Amino) partsWrong.Add("PRO-N");
        r.Check("biomol.aa-parts", partsWrong.Count == 0 ? "20" : string.Join(",", partsWrong), partsWrong.Count == 0);
        // (the R-group sketches: one per amino acid, bonds about one unit long, the R group above CA)
        var sketchWrong = new List<string>();
        foreach (var aa in BioAminoAcids.All)
        {
            var s = BiomolAminoAcid.Sketch(aa.One);
            var lengths = s.Bonds.Select(b => Math.Sqrt(Math.Pow(s.Atoms[b.A].X - s.Atoms[b.B].X, 2) + Math.Pow(s.Atoms[b.A].Y - s.Atoms[b.B].Y, 2))).ToList();
            var up = s.Atoms.Where(a => !a.Attachment).Sum(a => a.Y);
            if (s.Atoms.Count < 2 || lengths.Any(x => x < 0.35 || x > 1.3) || up <= 0) sketchWrong.Add(aa.Three);
        }
        r.Check("biomol.aa-sketches", sketchWrong.Count == 0 ? "20" : string.Join(",", sketchWrong), sketchWrong.Count == 0);
        // (nucleotides: each has a phosphate, a sugar and a base; deoxy ones have no O2′; the strand's linkages)
        var ntWrong = new List<string>();
        foreach (var code in BiomolNucleotide.Codes)
        {
            if (BioData.Molecule(code) is not { } m) { ntWrong.Add(code); continue; }
            var ps = m.Atoms.Select(a => BiomolNucleotide.Part(a.Name).Id).ToHashSet();
            var hasO2 = m.Atom("O2'") != null;
            if (!ps.SetEquals(new[] { "phosphate", "sugar", "base" }) || hasO2 == BiomolNucleotide.IsDeoxy(code)) ntWrong.Add(code);
        }
        r.Check("biomol.nucleotide-parts", ntWrong.Count == 0 ? "9" : string.Join(",", ntWrong), ntWrong.Count == 0);
        var dx = BiomolStrand.Build("ATGCGT", complement: true);
        var links = BiomolStrand.Linkages(dx, 1).Concat(BiomolStrand.Linkages(dx, 2)).ToList();
        var op = links.Select(l => BioV.Distance(dx.Atoms[l[1]].Pos, dx.Atoms[l[2]].Pos)).ToList();
        r.Check("biomol.strand", $"{links.Count}links,{dx.HBonds.Count}H-bonds,O3'P{F(op.FirstOrDefault(), "0.000")}",
            links.Count == 10 && op.All(x => Math.Abs(x - 1.58) < 0.005) && dx.HBonds.Count == 2 + 2 + 3 + 3 + 3 + 2);
    }

    /// <summary>"C2H5NO2" counted: C 2, H 5, N 1, O 2.</summary>
    private static Dictionary<string, int> BiomolFormulaCounts(string formula)
    {
        var want = new Dictionary<string, int>();
        string el = "", digits = "";
        void Flush()
        {
            if (el.Length > 0) want[el] = want.GetValueOrDefault(el) + (int.TryParse(digits, out var n) ? n : 1);
            el = "";
            digits = "";
        }
        foreach (var ch in formula)
        {
            if (char.IsUpper(ch)) { Flush(); el = ch.ToString(); }
            else if (char.IsLower(ch)) el += ch;
            else if (char.IsDigit(ch)) digits += ch;
        }
        Flush();
        return want;
    }

    // ---- sugars and lipids ----

    public static void BiomolSugarLipidChecks(BioCheckReport r)
    {
        // (α-glucose's C1 OH is trans to C6, β-glucose's, galactose's, fructose's and ribose's cis; the rings' sizes)
        var anomers = new List<string>();
        foreach (var code in BiomolSugar.Monosaccharides)
        {
            if (BioData.Molecule(code) is not { } m || BiomolSugar.RingOf(m) is not { } ring) { anomers.Add($"{code}?"); continue; }
            anomers.Add($"{code}{(BiomolSugar.IsAlpha(m, ring) ? "α" : "β")}{ring.Atoms.Count}");
        }
        r.Same("biomol.anomers", string.Join(",", anomers), "GLCα6,BGCβ6,GALβ6,FRUβ5,BDRβ5");
        // (each disaccharide's glycosidic O, and its linkage's α or β read from the atoms)
        var gl = new List<string>();
        foreach (var code in BiomolSugar.Disaccharides)
        {
            if (BioData.Molecule(code) is not { } m || BiomolSugar.GlycosidicOf(m) is not { } g || BiomolSugar.RingOf(m) is not { } ring) { gl.Add($"{code}?"); continue; }
            var text = $"{code}:{g.O}:{g.Anomeric}-{g.Other}:{(BiomolSugar.IsAlpha(m, ring) ? "α" : "β")}";
            if (code == "SUC" && BiomolSugar.RingOf(m, primed: true) is { } fr) text += BiomolSugar.IsAlpha(m, fr) ? "α" : "β";
            gl.Add(text);
        }
        r.Same("biomol.glycosidic", string.Join(",", gl), "MAL:O1:C1-C4':α,CBI:O4':C1-C4':β,SUC:O1:C1-C2':αβ,LAT:O1:C1-C4':β");
        // (hydrolysis: two monosaccharides' atoms, C6H12O6 each; the sugars 4 Å further apart; the new O 1.43 Å from C1)
        var splitWrong = new List<string>();
        foreach (var code in BiomolSugar.Disaccharides)
        {
            if (BioData.Molecule(code) is not { } m || BiomolSugar.GlycosidicOf(m) is not { } g || BiomolSugar.Split(m) is not { } s) { splitWrong.Add(code); continue; }
            var counts = new Dictionary<string, int>();
            foreach (var a in s.Model.Atoms) counts[a.Element] = counts.GetValueOrDefault(a.Element) + 1;
            var c1 = s.Model.Atoms.FirstOrDefault(a => a.Name == g.Anomeric)?.Pos ?? BioVec.Zero;
            var og = s.Model.Atoms.FirstOrDefault(a => a.Name == g.O)?.Pos ?? BioVec.Zero;
            var ow = s.Model.Atoms.FirstOrDefault(a => a.Name == "O (water)")?.Pos ?? BioVec.Zero;
            var before = BioV.Distance(m.Pos(g.Anomeric) ?? c1, m.Pos(g.O) ?? og);
            var countsOK = counts.Count == 3 && counts.GetValueOrDefault("C") == 12 && counts.GetValueOrDefault("H") == 24 && counts.GetValueOrDefault("O") == 12;
            if (!countsOK || Math.Abs(BioV.Distance(c1, og) - before - 4) > 1e-6 || Math.Abs(BioV.Distance(c1, ow) - 1.43) > 1e-6 || s.Model.OddBonds().Count > 0) splitWrong.Add(code);
        }
        r.Check("biomol.hydrolysis", splitWrong.Count == 0 ? "4" : string.Join(",", splitWrong), splitWrong.Count == 0);
        // (lipids: oleic acid bends at its cis double bond, stearic acid does not; tristearin's 3 ester bonds and POPC's 2
        // are bonds; cholesterol has four rings)
        var ola = BioData.Molecule("OLA") is { } om ? BiomolLipid.KinkAngle(om) ?? 0 : 0;
        var ste = BioData.Molecule("STE") is { } sm ? BiomolLipid.KinkAngle(sm) ?? 99 : 99;
        r.Check("biomol.oleic-kink", $"{F(ola, "0")}/{F(ste, "0")}", ola > 25 && ste < 10);
        var esterWrong = new List<string>();
        foreach (var code in new[] { "TGL", "POV", "OLA" })
        {
            if (BioData.Molecule(code) is not { } m) continue;
            foreach (var (a, b) in BiomolLipid.EsterBonds(code).Concat(BiomolLipid.CisBonds(code)))
                if (!m.Bonds.Any(x => (x.A == a && x.B == b) || (x.A == b && x.B == a))) esterWrong.Add($"{code}{a}-{b}");
        }
        var clr = BioData.Molecule("CLR");
        var heavy = clr?.Atoms.Where(a => a.Element != "H").Select(a => a.Name).ToHashSet() ?? new HashSet<string>();
        var heavyBonds = clr?.Bonds.Count(b => heavy.Contains(b.A) && heavy.Contains(b.B)) ?? 0;
        var rings = heavyBonds - heavy.Count + 1;
        r.Check("biomol.lipid-bonds", esterWrong.Count == 0 ? $"esters,rings{rings}" : string.Join(",", esterWrong), esterWrong.Count == 0 && rings == 4);
        var headTail = new List<string>();
        foreach (var code in BiomolLipid.Codes)
        {
            if (BioData.Molecule(code) is not { } m) continue;
            var d = m.Atoms.Select(a => BiomolLipid.Detail(a.Name, m)).ToList();
            var philic = d.Count(BiomolLipid.IsHydrophilic);
            var phobic = d.Count - philic;
            if (code == "GOL" ? phobic != 0 : (philic == 0 || phobic == 0)) headTail.Add(code);
        }
        r.Check("biomol.lipid-parts", headTail.Count == 0 ? "7" : string.Join(",", headTail), headTail.Count == 0);
        // (the polysaccharide diagrams: their glyphs, links and branches)
        var p = BiomolPolysaccharides.All.Select(k =>
        {
            var l = k.LayoutOf();
            return $"{l.Glyphs.Count}/{l.Links.Count(x => x.Kind == BiomolPolysaccharides.LinkKind.Branch)}/{l.Links.Count(x => x.Kind == BiomolPolysaccharides.LinkKind.HBond)}";
        });
        r.Same("biomol.polysaccharides", string.Join(",", p), "12/0/0,17/1/0,23/3/0,24/0/8,8/0/0");
    }

    // ---- scenes ----

    public static void BiomolSceneChecks(BioCheckReport r)
    {
        var chem = BiomolChem.Plain;
        var look = new BiomolLook();
        // (every amino acid's scene: an item per atom, ids its parts', four labels)
        var aaWrong = new List<string>();
        foreach (var aa in BioAminoAcids.All)
        {
            var c = BiomolScenes.AminoAcid(aa.One, look, false, chem);
            var atoms = c.Items.Count(i => (i.Id ?? "").Contains(":atom:"));
            if (atoms != BiomolAminoAcid.Model(aa.One, ph7: true).Atoms.Count || c.Labels.Count != 4) aaWrong.Add(aa.Three);
        }
        r.Check("biomol.scene-amino", aaWrong.Count == 0 ? "20" : string.Join(",", aaWrong), aaWrong.Count == 0);
        // (the default peptide, coloured by part, with H-bonds and the ribbon over it: atoms without H, peptide bonds, its
        // helix's H-bonds, a cartoon piece per segment)
        look = look with { Hydrogens = false, ColourBy = BiomolColourBy.Part };
        var seq = BiomolPeptideBuilder.DefaultSequence;
        var pep = BiomolPeptideBuilder.Build(seq, BiomolSSes.Parse("CCHHHHHHHHHHHHCEEEE", seq.Length));
        var pc = BiomolScenes.Peptide(pep, look, new BiomolScenes.PeptideOptions(HBonds: true, Ribbon: BiomolRibbon.Over), chem);
        var patoms = pc.Items.Count(i => (i.Id ?? "").Contains(":atom:"));
        var pbonds = pc.Items.Count(i => (i.Id ?? "").StartsWith("pbond:", StringComparison.Ordinal));
        var phb = pc.Items.Count(i => (i.Id ?? "").StartsWith("hbond:", StringComparison.Ordinal));
        var cartoon = pc.Items.Count(i => (i.Id ?? "").EndsWith(":cartoon", StringComparison.Ordinal));
        r.Check("biomol.scene-peptide", $"{patoms}atoms,{pbonds}pbonds,{phb}H-bonds,{cartoon}cartoon,{pc.Labels.Count}labels",
            patoms == pep.Atoms.Count(a => a.Element != "H") && pbonds == seq.Length - 1 && phb == pep.HBonds.Count && phb >= 8 && cartoon == 4 && pc.Labels.Count <= 14);
        // (the structure levels: ubiquitin's 76 centroids, haemoglobin's 4 chains and 4 hemes; denaturing morphs mesh to
        // matching mesh)
        var t = BiomolProteins.Tertiary(false, BiomolCentroids.Core);
        var q = BiomolProteins.Quaternary(false);
        var hemes = q.Items.Count(i => (i.Id ?? "").EndsWith(":disk", StringComparison.Ordinal));
        var chains = q.Items.Select(i => i.Id).Where(i => i != null && i.StartsWith("chain:", StringComparison.Ordinal)).Select(i => i!.Split(':')[1]).ToHashSet();
        var den = BiomolProteins.DenatureMeshes(false);
        var matching = den.Parts.All(x => x.Native.VertexCount == x.Coil.VertexCount && x.Native.Indices.SequenceEqual(x.Coil.Indices));
        var coil = BiomolProteins.RandomCoil(BiomolProteins.UbiquitinCA);
        var steps = coil.Zip(coil.Skip(1)).Select(x => BioV.Distance(x.First, x.Second)).ToList();
        var centroids = t.Items.Count(i => (i.Id ?? "").StartsWith("res:", StringComparison.Ordinal));
        r.Check("biomol.scene-levels", $"{centroids}centroids,{chains.Count}chains,{hemes}hemes",
            centroids == 76 && chains.Count == 4 && hemes == 4 && matching && steps.All(s => Math.Abs(s - 3.8) < 1e-9) && coil.Count == 76);
        var sec = BiomolScenes.Secondary(look, chem);
        var secH = sec.Items.Count(i => (i.Id ?? "").Contains("hbond"));
        r.Check("biomol.scene-secondary", $"{secH}H-bonds", secH == 8 + 14);
        // (the other pages' scenes are not empty; the motions have their groups)
        look = look with { Hydrogens = true };
        var counts = new[]
        {
            BiomolScenes.Nucleotide("ATP", look, chem).Items.Count,
            BiomolScenes.Strand(BiomolStrand.Build("ATGC", complement: true), look, null, chem).Items.Count,
            BiomolScenes.Monosaccharide("GLC", look, chem).Content.Items.Count,
            BiomolScenes.Disaccharide("SUC", true, look, chem).Content.Items.Count,
            BiomolScenes.Lipid("POV", look, chem).Items.Count,
            BiomolScenes.Lipid("STE+OLA", look, chem).Items.Count,
        };
        var motions = new[]
        {
            BiomolScenes.PeptideAdded(pep, look, chem).Groups.Count,
            BiomolScenes.PeptideRemoved(pep, look, chem).Groups.Count,
            BiomolScenes.StrandAdded(BiomolStrand.Build("ATGC", complement: false), look, chem).Groups.Count,
            BiomolScenes.DisaccharideMotion("MAL", true, look, chem).Groups.Count,
        };
        r.Check("biomol.scenes", string.Join(",", counts) + "|" + string.Join(",", motions), counts.All(c => c > 20) && motions.SequenceEqual(new[] { 3, 3, 3, 7 }));
        // (the fields' cleaning and the motion curve)
        var pin = BiomolInput.Protein("mkt ay1x*b");
        var din = BiomolInput.Dna("atgun");
        r.Check("biomol.input", pin.Sequence + "|" + din.Sequence, pin.Sequence == "MKTAY" && pin.Message?.Contains("“X” at 6") == true
            && pin.Message?.Contains("“B” at 8") == true && din.Sequence == "ATG" && din.Message != null && BiomolInput.Protein(new string('A', 45)).Sequence.Length == 40);
        r.Same("biomol.text", BiomolText.Formula("C10 H14 N5 O6 P") + "," + BiomolText.Signed(-2) + "," + (BiomolText.AtomIndex("ss:1:res:3:atom:31") ?? -1), "C₁₀H₁₄N₅O₆P,−2,31");
        var ease = new[] { 0.0, 0.5, 1.0 }.Select(BiomolEase.StrongOut).ToArray();
        r.Check("biomol.ease", string.Join(",", ease.Select(e => F(e, "0.000"))), ease[0] == 0 && Math.Abs(ease[2] - 1) < 1e-6 && ease[1] > 0.85);
        var text = BiomolHandOff.Text(BiomolAminoAcid.Model('W', ph7: true), "Tryptophan");
        var lines = text.Split('\n');
        r.Check("biomol.hand-off", $"{lines.Length}lines", lines[0] == "title Tryptophan" && lines.Count(l => l.StartsWith("atom ", StringComparison.Ordinal)) == 27
            && lines.Count(l => l.StartsWith("bond ", StringComparison.Ordinal)) == 28);
    }
}
