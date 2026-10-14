using System.Globalization;

namespace Simpl.Tools;

// The biology toolkit's self-check (spec §10.1), the Mac's BioSelfCheck.swift check for check — the same names, the same
// test vectors, the same tolerances: the shared data and algorithms recomputed and compared with the specification's
// test vectors, a line each — `bio-check <name> <value> <OK|FAIL>` — written to the log with --bio-check (BioTools.cs),
// and run whole by the unit tests (windows/SimplFluent.Tests/BiologyTests.cs), which fail on any FAIL. Each tool adds its
// own checks in its own model file (BioSelfCheck.CellChecks … EnergyChecks, partial methods), so the tools' builders
// never edit this one.

/// <summary>The self-check's lines and counts.</summary>
public sealed class BioCheckReport
{
    private readonly List<string> lines = new();

    public IReadOnlyList<string> Lines => lines;
    public int Passed { get; private set; }
    public int Failed { get; private set; }

    /// <summary>A check: its name (no spaces), what was found (spaces become _), and whether it is right.</summary>
    public void Check(string name, string value, bool ok)
    {
        var v = value.Length == 0 ? "-" : value.Replace(' ', '_');
        lines.Add($"bio-check {name} {v} {(ok ? "OK" : "FAIL")}");
        if (ok) Passed++; else Failed++;
    }

    /// <summary>A number within <paramref name="tolerance"/> of what it should be.</summary>
    public void Near(string name, double value, double expected, double tolerance, int digits = 6) =>
        Check(name, value.ToString("F" + digits, CultureInfo.InvariantCulture), Math.Abs(value - expected) <= tolerance);

    /// <summary>Two texts the same.</summary>
    public void Same(string name, string value, string expected) => Check(name, value, value == expected);

    /// <summary>"bio-check summary 110/110 OK".</summary>
    public string Summary => $"bio-check summary {Passed}/{Passed + Failed} {(Failed == 0 ? "OK" : "FAIL")}";
}

public static partial class BioSelfCheck
{
    private static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Every check, the foundation's then the tools'.</summary>
    public static BioCheckReport Run()
    {
        var r = new BioCheckReport();
        GeneticCode(r);
        AminoAcids(r);
        Molecules(r);
        Bdna(r);
        Randomness(r);
        ChiSquare(r);
        Crosses(r);
        Sequences(r);
        Structures(r);
        Geometry(r);
        Steps(r);
        // (the tools' own, each in its model file under Biology/Model)
        CellChecks(r);
        DnaChecks(r);
        GeneChecks(r);
        BiomolChecks(r);
        CrossChecks(r);
        DivideChecks(r);
        PopgenChecks(r);
        BiolabChecks(r);
        EnzymeChecks(r);
        EnergyChecks(r);
        return r;
    }

    // (each tool's checks: declared here, written in its own model file)
    static partial void CellChecks(BioCheckReport r);
    static partial void DnaChecks(BioCheckReport r);
    static partial void GeneChecks(BioCheckReport r);
    static partial void BiomolChecks(BioCheckReport r);
    static partial void CrossChecks(BioCheckReport r);
    static partial void DivideChecks(BioCheckReport r);
    static partial void PopgenChecks(BioCheckReport r);
    static partial void BiolabChecks(BioCheckReport r);
    static partial void EnzymeChecks(BioCheckReport r);
    static partial void EnergyChecks(BioCheckReport r);

    // ---- 1. the genetic code ----

    public static void GeneticCode(BioCheckReport r)
    {
        var e = BioCode.Entries;
        r.Check("code.count", $"{e.Count}", e.Count == 64 && e.Select(x => x.Codon).Distinct().Count() == 64);
        r.Check("code.AUG", $"{BioCode.AminoAcid("AUG") ?? '?'}", BioCode.AminoAcid("AUG") == 'M');
        r.Check("code.UGG", $"{BioCode.AminoAcid("UGG") ?? '?'}", BioCode.AminoAcid("UGG") == 'W');
        var stops = new[] { "UAA", "UAG", "UGA" }.Select(c => BioCode.AminoAcid(c) ?? '?').ToArray();
        r.Check("code.stops", new string(stops), new string(stops) == "***" && BioCode.Codons('*').SequenceEqual(new[] { "UAA", "UAG", "UGA" }));
        // (§6.1's counts: Leu, Ser, Arg 6; Ala, Gly, Pro, Thr, Val 4; Ile 3; stop 3; Phe … Glu 2; Met, Trp 1)
        var want = new Dictionary<char, int>
        {
            ['L'] = 6, ['S'] = 6, ['R'] = 6, ['A'] = 4, ['G'] = 4, ['P'] = 4, ['T'] = 4, ['V'] = 4, ['I'] = 3, ['*'] = 3,
            ['F'] = 2, ['Y'] = 2, ['C'] = 2, ['H'] = 2, ['Q'] = 2, ['N'] = 2, ['K'] = 2, ['D'] = 2, ['E'] = 2, ['M'] = 1, ['W'] = 1,
        };
        var wrong = want.Keys.OrderBy(c => c).Where(c => BioCode.Codons(c).Count != want[c]).ToArray();
        r.Check("code.counts", wrong.Length == 0 ? "all" : new string(wrong), wrong.Length == 0);
        // (the NCBI string, index 16·b1 + 4·b2 + b3 over T C A G, says the same for every codon)
        var ncbi = BioText.Lines(BioDataText.NcbiText).Select(l => l.Split('=').Last().Trim()).ToList();
        var agree = ncbi.Count == 4 && ncbi[0].Length == 64;
        if (agree)
            for (var i = 0; i < 64; i++)
                if (BioCode.AminoAcid(new string(new[] { ncbi[1][i], ncbi[2][i], ncbi[3][i] })) != ncbi[0][i]) agree = false;
        r.Check("code.ncbi", agree ? "64" : "differs", agree);
        r.Same("code.anticodon", BioCode.Anticodon3To5("AUG") + "/" + BioCode.Anticodon5To3("AUG"), "UAC/CAU");
        r.Check("code.DNA-letters", $"{BioCode.AminoAcid("ATG") ?? '?'}{BioCode.AminoAcid("NNN") ?? '?'}", BioCode.AminoAcid("ATG") == 'M' && BioCode.AminoAcid("ANG") == 'X');
    }

    // ---- 2. the amino acids and their templates ----

    public static void AminoAcids(BioCheckReport r)
    {
        var all = BioAminoAcids.All;
        r.Check("aa.count", $"{all.Count}", all.Count == 20 && all.Select(a => a.One).Distinct().Count() == 20);
        var codonsWrong = all.Where(a => a.CodonCount != BioCode.Codons(a.One).Count).Select(a => a.Three).ToList();
        r.Check("aa.codons", codonsWrong.Count == 0 ? "all" : string.Join(",", codonsWrong), codonsWrong.Count == 0);
        var massWrong = all.Where(a => Math.Abs(a.Mass - 18.02 - a.ResidueMass) > 0.015).Select(a => a.Three).ToList();
        r.Check("aa.residue-mass", massWrong.Count == 0 ? "all" : string.Join(",", massWrong), massWrong.Count == 0);
        var groups = BioAAClasses.All.Select(c => all.Count(a => a.Group == c)).ToArray();
        r.Check("aa.groups", string.Join(",", groups), groups.SequenceEqual(new[] { 9, 6, 2, 3 }));
        r.Check("aa.find", BioAminoAcids.Find("lys")?.Name ?? "-", BioAminoAcids.Find("lys")?.One == 'K' && BioAminoAcids.Find("W")?.Three == "Trp"
            && BioAminoAcids.Find("glutamic acid")?.One == 'E');

        var ts = BioData.Residues;
        r.Check("aa.templates", $"{ts.Count}", ts.Count == 20 && ts.Select(t => t.Code).Distinct().Count() == 20 && all.All(a => BioData.Residue($"{a.One}") != null));
        // (the frame: CA at the origin, C on +x, N in the xy-plane with y > 0; every L-amino acid's CB at z < 0)
        List<string> badFrame = new(), badCB = new(), badBond = new(), badChi = new();
        foreach (var t in ts)
        {
            if (t.Pos("CA") is not { } ca || t.Pos("C") is not { } c || t.Pos("N") is not { } n) { badFrame.Add(t.Code); continue; }
            if (BioV.Length(ca) > 1e-3 || Math.Abs(c.Y) > 1e-3 || Math.Abs(c.Z) > 1e-3 || c.X <= 0 || Math.Abs(n.Z) > 1e-3 || n.Y <= 0) badFrame.Add(t.Code);
            if (t.Code != "GLY" && (t.Pos("CB")?.Z ?? 1) >= 0) badCB.Add(t.Code);
            badBond.AddRange(t.Model(ph7: true, hydrogens: true).OddBonds().Select(b => $"{t.Code}:{b}"));
            badBond.AddRange(t.Model(ph7: false, hydrogens: true).OddBonds().Select(b => $"{t.Code}:{b}"));
            foreach (var chi in t.Chis)
            {
                if (t.ChiValue(chi.Index) is not { } v) { badChi.Add($"{t.Code}χ{chi.Index}"); continue; }
                var d = Math.Abs(v - chi.Degrees) % 360;
                if (d > 180) d = 360 - d;
                if (d > 0.5) badChi.Add($"{t.Code}χ{chi.Index}={F(v, "0.0")}");
            }
        }
        r.Check("aa.frame", badFrame.Count == 0 ? "all" : string.Join(",", badFrame), badFrame.Count == 0);
        r.Check("aa.L-chirality", badCB.Count == 0 ? "CB.z<0" : string.Join(",", badCB), badCB.Count == 0);
        r.Check("aa.bond-lengths", badBond.Count == 0 ? "0.9-1.9" : string.Join(",", badBond.Take(4)), badBond.Count == 0);
        r.Check("aa.chi", badChi.Count == 0 ? "within0.5" : string.Join(",", badChi), badChi.Count == 0);
        // (the pH 7 forms: Asp −1 at OD2, Lys +1 at NZ; turning χ1 and back leaves the template as it was)
        BioMolecule? asp = BioData.Residue("D"), lys = BioData.Residue("K");
        int Charge(BioMolecule? m, string atom) => m?.Ph7?.Charges.GetValueOrDefault(atom) ?? 0;
        r.Check("aa.pH7", $"Asp{Charge(asp, "OD2")},Lys{Charge(lys, "NZ")}",
            Charge(asp, "OD2") == -1 && asp?.Ph7?.Remove.Contains("HD2") == true && Charge(lys, "NZ") == 1);
        if (BioData.Residue("L") is { } leu && leu.ChiValue(1) is { } v0)
        {
            var turned = leu.SettingChi(1, 60);
            var back = turned.SettingChi(1, v0);
            var drift = leu.Atoms.Zip(back.Atoms, (a, b) => BioV.Distance(a.Pos, b.Pos)).DefaultIfEmpty(1).Max();
            r.Check("aa.set-chi", $"{F(turned.ChiValue(1) ?? 0, "0.0")},{F(drift, "0.0000")}", Math.Abs((turned.ChiValue(1) ?? 0) - 60) < 0.01 && drift < 1e-6);
        }
        else r.Check("aa.set-chi", "no-Leu", false);
        var alts = ts.FirstOrDefault(t => t.Code == "ARG")?.Alternatives.Count ?? 0;
        r.Check("aa.rotamers", $"ARG{alts}", alts == 32 && ts.FirstOrDefault(t => t.Code == "ASN")?.BetaRotamer?.Chis.SequenceEqual(new[] { -65.0, -75.0 }) == true);
    }

    // ---- nucleotides, sugars, lipids ----

    public static void Molecules(BioCheckReport r)
    {
        var groups = new (string Name, IReadOnlyList<BioMolecule> Ms, string[] Codes)[]
        {
            ("nucleotides", BioData.Nucleotides, new[] { "DA", "DC", "DG", "DT", "A", "C", "G", "U", "ATP" }),
            ("sugars", BioData.Sugars, new[] { "GLC", "BGC", "GAL", "FRU", "BDR", "MAL", "CBI", "SUC", "LAT" }),
            ("lipids", BioData.Lipids, new[] { "GOL", "PLM", "STE", "OLA", "TGL", "POV", "CLR" }),
        };
        foreach (var (name, ms, codes) in groups)
        {
            var odd = ms.SelectMany(m => m.Model(ph7: false).OddBonds().Select(b => $"{m.Code}:{b}")).ToList();
            var unbonded = ms.Where(m =>
            {
                var bonded = m.Bonds.SelectMany(b => new[] { b.A, b.B }).ToHashSet();
                return m.Atoms.Any(a => !bonded.Contains(a.Name));
            }).Select(m => m.Code).ToList();
            r.Check($"mol.{name}", $"{ms.Count}", ms.Select(m => m.Code).SequenceEqual(codes) && ms.All(m => m.Mass != null));
            r.Check($"mol.{name}.bonds", odd.Count == 0 && unbonded.Count == 0 ? "0.9-1.9" : string.Join(",", odd.Concat(unbonded).Take(4)), odd.Count == 0 && unbonded.Count == 0);
        }
        // (the formula's atoms are the block's atoms: dAMP C10 H14 N5 O6 P)
        var formulaWrong = new List<string>();
        foreach (var m in BioData.Nucleotides.Concat(BioData.Sugars).Concat(BioData.Lipids))
        {
            if (m.Formula is not { } f) { formulaWrong.Add(m.Code); continue; }
            var want = new Dictionary<string, int>();
            foreach (var part in BioText.Words(f))
            {
                var el = new string(part.TakeWhile(char.IsLetter).ToArray());
                var n = BioText.Int(new string(part.SkipWhile(char.IsLetter).ToArray())) ?? 1;
                want[el] = want.GetValueOrDefault(el) + n;
            }
            var have = new Dictionary<string, int>();
            foreach (var a in m.Atoms) have[a.Element] = have.GetValueOrDefault(a.Element) + 1;
            if (want.Count != have.Count || want.Any(kv => have.GetValueOrDefault(kv.Key) != kv.Value)) formulaWrong.Add(m.Code);
        }
        r.Check("mol.formulas", formulaWrong.Count == 0 ? "all" : string.Join(",", formulaWrong), formulaWrong.Count == 0);
        var atp = BioData.Molecule("ATP");
        r.Check("mol.ATP.pH7", $"{atp?.Ph7?.Remove.Count ?? 0}", atp?.Ph7?.Remove.SequenceEqual(new[] { "HOG2", "HOG3", "HOB2", "HOA2" }) == true);
    }

    // ---- 5. B-DNA ----

    public static void Bdna(BioCheckReport r)
    {
        var ac = BioBDNA.Build("AC");
        var worst = 0.0;
        var missing = new List<string>();
        foreach (var t in BioData.BdnaTests)
        {
            if (ac.Pos(t.Strand, t.Pair, t.Name) is not { } p) { missing.Add(t.Name); continue; }
            worst = Math.Max(worst, BioV.Distance(p, t.Pos));
        }
        r.Check("dna.test-vectors", $"{BioData.BdnaTests.Count},worst{F(worst, "0.0000")}", BioData.BdnaTests.Count == 10 && missing.Count == 0 && worst <= 0.002);
        var d = BioBDNA.Build("ACGTACGT");
        var links = new List<double>();
        for (var k = 0; k < 7; k++)
        {
            if (d.Pos(1, k, "O3'") is { } o && d.Pos(1, k + 1, "P") is { } p) links.Add(BioV.Distance(o, p));
            if (d.Pos(2, k + 1, "O3'") is { } o2 && d.Pos(2, k, "P") is { } p2) links.Add(BioV.Distance(o2, p2));
        }
        var off = links.Count == 0 ? 1 : links.Max(x => Math.Abs(x - 1.580));
        r.Check("dna.O3'-P", $"{links.Count},{F(links.FirstOrDefault(), "0.0000")}", links.Count == 14 && off <= 0.002);
        var bonded = d.Bonds.SelectMany(b => new[] { b.A, b.B }).ToHashSet();
        r.Check("dna.bonds", $"{d.Atoms.Count}atoms,{d.Bonds.Count}bonds", bonded.Count == d.Atoms.Count && d.Model.OddBonds(1.1, 1.7).Count == 0);
        static double Hb(string s, string x, string y)
        {
            var dd = BioBDNA.Build(s);
            return dd.Pos(1, 0, x) is { } a && dd.Pos(2, 0, y) is { } b ? BioV.Distance(a, b) : 0;
        }
        r.Near("dna.AT.N1-N3", Hb("A", "N1", "N3"), 2.91, 0.01, 3);
        r.Near("dna.GC.N1-N3", Hb("G", "N1", "N3"), 2.87, 0.01, 3);
        r.Check("dna.H-bonds", $"{d.HBonds.Count}", d.HBonds.Count == 4 * 2 + 4 * 3);
        // (right-handed: going up strand 1, each P turns the same way θ grows — X = r·sin θ, Z = r·cos θ)
        var p0 = d.Phosphates(1);
        var turns = p0.Zip(p0.Skip(1), (a, b) => BioV.Cross(new BioVec(a.X, 0, a.Z), new BioVec(b.X, 0, b.Z)).Y).ToList();
        r.Check("dna.right-handed", $"{turns.Count}", turns.Count > 0 && turns.All(t => t > 0));
        var pairs = BioData.BasePairs2D;
        int at = pairs.GetValueOrDefault("AT")?.Count ?? 0, gc = pairs.GetValueOrDefault("GC")?.Count ?? 0;
        r.Check("dna.pairs-2D", $"AT{at},GC{gc}", at == 30 && gc == 29);
    }

    // ---- 6. SplitMix64 and chi-square ----

    public static void Randomness(BioCheckReport r)
    {
        static ulong[] Words(ulong seed)
        {
            var g = new BioRandom(seed);
            return new[] { g.Next(), g.Next(), g.Next() };
        }
        ulong[] w0 = Words(0), w42 = Words(42);
        r.Check("rng.seed0", string.Join(",", w0.Select(w => w.ToString("x"))), w0.SequenceEqual(new[] { 0xe220a8397b1dcdafUL, 0x6e789e6aa1b965f4UL, 0x06c45d188009454fUL }));
        r.Check("rng.seed42", string.Join(",", w42.Select(w => w.ToString("x"))), w42.SequenceEqual(new[] { 0xbdd732262feb6e95UL, 0x28efe333b266f103UL, 0x47526757130f9f52UL }));
        var g = new BioRandom(42);
        var u = new[] { g.Uniform(), g.Uniform(), g.Uniform() };
        var want = new[] { 0.7415648787718233, 0.1599103928769201, 0.27860113025513866 };
        r.Check("rng.uniform42", string.Join(",", u.Select(x => F(x, "F16"))), u.Zip(want).All(p => Math.Abs(p.First - p.Second) < 1e-15));
    }

    public static void ChiSquare(BioCheckReport r)
    {
        var vectors = new (double X, int Df, double P)[]
        {
            (3.84, 1, 0.050044), (3.841459, 1, 0.050000), (5.99, 2, 0.050037), (7.81, 3, 0.050106),
            (6.63, 1, 0.010028), (0.6, 1, 0.438578), (2.0, 3, 0.572407), (11.07, 5, 0.050010),
        };
        foreach (var (x, df, p) in vectors) r.Near($"chi2.p({F(x, "0.0#####")},{df})", BioChiSquare.PValue(x, df), p, 1e-6);
        // (the table's p = 0.10 … 0.001 rows, recomputed by bisection, agree to its two decimals)
        var off = new List<string>();
        for (var df = 1; df <= 10; df++)
            for (var i = 0; i < BioChiSquare.TableP.Length; i++)
            {
                var p = BioChiSquare.TableP[i];
                var c = BioChiSquare.CriticalComputed(df, p);
                var t = BioChiSquare.Table.TryGetValue(df, out var row) ? row[i] : -1;
                if (Math.Abs(c - t) > 0.0051) off.Add($"{df}@{F(p, "0.0##")}={F(c, "0.000")}");
            }
        r.Check("chi2.critical", off.Count == 0 ? "40" : string.Join(",", off.Take(3)), off.Count == 0 && BioChiSquare.Table.Count == 10);
        // (the Hardy–Weinberg vector of §5.4.2: 50, 30, 20 against p = 0.65, df 1)
        var hwe = BioChiSquare.Test(new double[] { 50, 30, 20 }, new[] { 42.25, 45.5, 12.25 }, 1);
        r.Check("chi2.HWE", $"{F(hwe.Chi2, "0.0000")},{F(hwe.P, "0.00000")}", Math.Abs(hwe.Chi2 - 11.6049) < 1e-4 && Math.Abs(hwe.P - 0.00066) < 5e-6 && hwe.Reject);
        var fit = BioChiSquare.Test(new double[] { 315, 108, 101, 32 }, BioChiSquare.Expected(new double[] { 9, 3, 3, 1 }, 556));
        r.Check("chi2.conclusion", F(fit.Chi2, "0.000"), fit.Df == 3 && !fit.Reject && fit.Conclusion.Contains("fail to reject"));
    }

    // ---- 7. crosses ----

    public static void Crosses(BioCheckReport r)
    {
        static BioCross? Cross(string text, Action<BioCross>? adjust = null)
        {
            if (BioCross.Parse(text).Cross is not { } c) return null;
            adjust?.Invoke(c);
            return c;
        }
        void Row(string name, string text, string geno, int[] pheno, string? phenoText = null)
        {
            if (Cross(text) is not { } c) { r.Check($"cross.{name}", "unparsed", false); return; }
            var res = c.Result();
            var ok = res.GenotypicRatioText == geno && res.PhenotypicRatio.SequenceEqual(pheno) && (phenoText == null || res.PhenotypicRatioText == phenoText);
            r.Check($"cross.{name}", res.GenotypicRatioText + "|" + res.PhenotypicRatioText, ok);
        }
        Row("Aa×Aa", "Aa × Aa", "1 AA : 2 Aa : 1 aa", new[] { 3, 1 }, "3 A_ : 1 aa");
        Row("Aa×aa", "Aa × aa", "1 Aa : 1 aa", new[] { 1, 1 });
        Row("AA×aa", "AA × aa", "1 Aa", new[] { 1 }, "1 A_");
        Row("incomplete", "C^R C^W × C^R C^W", "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", new[] { 1, 2, 1 }, "1 red : 2 pink : 1 white");
        Row("AaBb×AaBb", "AaBb × AaBb", "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb",
            new[] { 9, 3, 3, 1 }, "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb");
        Row("AaBb×aabb", "AaBb x aabb", "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", new[] { 1, 1, 1, 1 });
        if (Cross("AaBbCc × AaBbCc") is { } tri)
        {
            var res = tri.Result();
            r.Check("cross.AaBbCc", $"{res.Genotypes.Count}|" + string.Join(":", res.PhenotypicRatio),
                res.Genotypes.Count == 27 && res.PhenotypicRatio.SequenceEqual(new[] { 27, 9, 9, 9, 3, 3, 3, 1 })
                && res.Phenotypes.Select(p => p.Text).SequenceEqual(new[] { "A_B_C_", "A_B_cc", "A_bbC_", "aaB_C_", "A_bbcc", "aaB_cc", "aabbC_", "aabbcc" }));
            var g1 = tri.Gametes1;
            r.Check("cross.gametes", $"{g1.Count}", g1.Count == 8 && g1.All(g => g.P == new BioFrac(1, 8)));
            var p = tri.Probability(new[] { BioTarget.HomRecessive, BioTarget.Dominant, BioTarget.Heterozygous });
            r.Check("cross.product-rule", string.Join("×", p.Factors.Select(f => f.Text)) + "=" + p.Product.Text,
                p.Factors.SequenceEqual(new[] { new BioFrac(1, 4), new BioFrac(3, 4), new BioFrac(1, 2) }) && p.Product == new BioFrac(3, 32) && p.Product.PercentText == "9.4%");
        }
        else r.Check("cross.AaBbCc", "unparsed", false);
        Row("ABO-AO×BO", "I^A i × I^B i", "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", new[] { 1, 1, 1, 1 }, "1 AB : 1 A : 1 B : 1 O");
        Row("ABO-AB×AB", "I^A I^B × I^A I^B", "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", new[] { 1, 2, 1 }, "1 A : 2 AB : 1 B");
        void XLinked(string name, string text, string daughters, int[] dPheno, BioFrac dCarriers, string sons, string sPheno)
        {
            if (Cross(text) is not { } c) { r.Check($"cross.{name}", "unparsed", false); return; }
            BioCrossResult d = c.Result(BioSex.Female), s = c.Result(BioSex.Male);
            var carriers = d.Phenotypes.FirstOrDefault()?.Carriers ?? BioFrac.Zero;
            r.Check($"cross.{name}", d.GenotypicRatioText + "|" + s.GenotypicRatioText + "|" + s.PhenotypicRatioText,
                d.GenotypicRatioText == daughters && d.PhenotypicRatio.SequenceEqual(dPheno) && carriers == dCarriers
                && s.GenotypicRatioText == sons && s.PhenotypicRatioText == sPheno && d.Phenotypes.FirstOrDefault()?.Text == "unaffected");
        }
        XLinked("X-carrier×affected", "X^A X^a × X^a Y", "1 X^A X^a : 1 X^a X^a", new[] { 1, 1 }, new BioFrac(1, 2), "1 X^A Y : 1 X^a Y", "1 unaffected : 1 affected");
        XLinked("X-carrier×normal", "X^A X^a × X^A Y", "1 X^A X^A : 1 X^A X^a", new[] { 1 }, new BioFrac(1, 2), "1 X^A Y : 1 X^a Y", "1 unaffected : 1 affected");
        if (Cross("A^Y a × A^Y a", c => { c.Genes[0].Pattern = BioInheritance.LethalDominantHomozygote; c.Genes[0].TraitNames = new() { "yellow", "non-yellow" }; }) is { } lethal)
        {
            var res = lethal.Result();
            r.Check("cross.lethal", res.GenotypicRatioText + "|" + string.Join(":", res.BornGenotypicRatio) + "|" + res.PhenotypicRatioText,
                res.GenotypicRatioText == "1 A^Y A^Y : 2 A^Y a : 1 aa" && res.Genotypes.FirstOrDefault()?.Dies == true
                && res.BornGenotypicRatio.SequenceEqual(new[] { 2, 1 }) && res.PhenotypicRatioText == "2 yellow : 1 non-yellow" && res.BornFraction == new BioFrac(3, 4));
        }
        else r.Check("cross.lethal", "unparsed", false);
        var bad = BioCross.Parse("Aa");
        var flipped = BioCross.Parse("aA × Aa").Cross?.Parent1;
        r.Check("cross.parse-error", bad.Error == null ? "none" : "said", bad.Cross == null && bad.Error != null && flipped is { Length: 1 } && flipped[0].SequenceEqual(new[] { 0, 1 }));
        r.Same("frac.text", new BioFrac(6, 64).Text + "," + new BioFrac(2, -4).Text + "," + BioRatio.Text(BioRatio.Reduce(new[] { 2, 4, 2 })), "3/32,-1/2,1 : 2 : 1");
    }

    // ---- 10. sequences and genes ----

    public static void Sequences(BioCheckReport r)
    {
        var g = BioGenes.Teaching;
        var c = g.Coding;
        string Span(int a, int b) => a >= 1 && b <= c.Length && a <= b ? c[(a - 1)..b] : "";
        r.Check("gene.length", $"{c.Length}", c.Length == 112 && g.Parts.LastOrDefault()?.End == 112 && g.Parts.Select(p => p.Start).SequenceEqual(new[] { 1, 32, 45, 56, 83, 102 }));
        r.Same("gene.marks", string.Join(",", Span(5, 11), Span(45, 47), Span(99, 101), Span(104, 109)), "TATAAAA,ATG,TAA,AATAAA");
        r.Check("gene.template", $"{g.Template.Length}", g.Template == BioSeq.TemplateStrand(g.Coding));
        var pre = BioSeq.Transcribe(Span(32, 112));
        r.Check("gene.pre-mRNA", $"{pre.Length}nt", pre == g.PreMRNA);
        var mature = BioSeq.Transcribe(Span(32, 55) + Span(83, 112));
        r.Check("gene.mature-mRNA", $"{mature.Length}nt", mature == g.MatureMRNA);
        var cds = Span(45, 55) + Span(83, 101);
        r.Check("gene.CDS", cds, cds == g.Cds);
        r.Same("gene.protein", BioSeq.Translate(g.Cds), g.Protein + "*");
        r.Same("gene.unspliced", (BioSeq.ProteinFromFirstStart(g.PreMRNA) ?? "") + "*", g.UnsplicedProtein);
        var orfs = BioSeq.Orfs(g.MatureMRNA, minCodons: 3, reverseStrand: false);
        var first = orfs.FirstOrDefault();
        r.Check("gene.ORF", first is { } o ? $"{o.FrameLabel}@{o.Start}-{o.End}" : "none",
            first?.Frame == 2 && first.Start == 14 && first.Protein == g.Protein && first.HasStop);
        // (the mutation table's mutant sequences translate to the proteins it lists)
        var wrong = new List<string>();
        foreach (var m in g.Mutations)
        {
            var protein = BioSeq.Translate(m.Cds.Replace("**", ""));
            var core = new string(protein.TakeWhile(ch => ch != '*').ToArray());
            if (m.Protein.Contains('…'))
            {
                // ("MAIQRLAERL… (no stop before the end)": its start, and no stop)
                var listed = m.Protein.Split('…')[0];
                if (!core.StartsWith(listed, StringComparison.Ordinal) || protein.Contains('*')) wrong.Add(m.Name);
            }
            else
            {
                // ("MAFSDWQKG" or "MAFSDW*": up to the stop, which is there)
                var listed = m.Protein.EndsWith('*') ? m.Protein[..^1] : m.Protein;
                if (core != listed || !protein.Contains('*')) wrong.Add(m.Name);
            }
        }
        r.Check("gene.mutations", wrong.Count == 0 ? $"{g.Mutations.Count}" : string.Join(",", wrong), wrong.Count == 0 && g.Mutations.Count == 6);
        // (HBB: its CDS translates to NP_000509.1; c.20A>T makes codon 7 GTG, Glu → Val)
        r.Check("gene.HBB", $"{BioGenes.HbbCDS.Length}nt", BioSeq.Translate(BioGenes.HbbCDS) == BioGenes.HbbProtein + "*");
        var hbb = BioGenes.HbbCDS.ToCharArray();
        var was = hbb[19];
        hbb[19] = 'T';
        var mutant = BioSeq.Translate(new string(hbb));
        r.Check("gene.sickle", mutant[..Math.Min(10, mutant.Length)], was == 'A' && mutant.StartsWith(BioGenes.Sickle.ProteinStart, StringComparison.Ordinal)
            && BioSeq.Codons(new string(hbb))[6] == "GTG");
        // (cleaning: a FASTA header, digits and spaces dropped; U in DNA offered as RNA; bad letters placed)
        var a = new BioSequenceInput(">x\n atg c12gt", BioSeqKind.Dna);
        var b = new BioSequenceInput("AUGC", BioSeqKind.Dna);
        var badInput = new BioSequenceInput("ATGXQ", BioSeqKind.Dna);
        r.Check("seq.clean", a.Letters + "," + (b.Suggestion?.Key() ?? "-") + "," + string.Concat(badInput.Invalid.Select(i => $"{i.Letter}[{string.Join(", ", i.Positions)}]")),
            a.Sequence == "ATGCGT" && b.Suggestion == BioSeqKind.Rna && badInput.Invalid.Count == 2 && badInput.Invalid[0].Positions.SequenceEqual(new[] { 4 }) && badInput.Sequence == null);
        r.Same("seq.reverse-complement", BioSeq.ReverseComplement("ATGCN") + "," + BioSeq.Complement("ATGC"), "NGCAT,TACG");
        var frames = Enumerable.Range(0, 3).Select(f => BioSeq.Translate("ATGGCCTAA", f)).Concat(Enumerable.Range(0, 3).Select(f => BioSeq.Translate("ATGGCCTAA", f, reverse: true)));
        r.Same("seq.frames", string.Join(",", frames), "MA*,WP,GL,LGH,*A,RP");
        r.Near("seq.mass", BioSeq.Mass("ACGT"), 331.22 + 307.20 + 347.22 + 322.21 - 3 * 18.02, 1e-9, 2);
        r.Near("seq.Tm", BioSeq.MeltingTemperature("ATGCATGCATGC"), 36, 1e-9, 1);
        r.Near("seq.Chargaff", BioSeq.Chargaff("AAAGC").PurinePercent, 50, 1e-9, 1);
        var lines = BioSeq.Lines(new string('A', 75));
        r.Check("seq.groups", string.Join(",", lines.Select(l => $"{l.Start}:{l.Groups.Count}")),
            lines.Count == 2 && lines[1].Start == 61 && lines[1].Groups.SequenceEqual(new[] { "AAAAAAAAAA", "AAAAA" }));
    }

    // ---- proteins and tRNA ----

    public static void Structures(BioCheckReport r)
    {
        var u = BioData.Ubiquitin;
        r.Check("ubiquitin", $"{u.Count}", u.Count == 76 && u.Select(x => x.I).SequenceEqual(Enumerable.Range(1, 76)) && u.All(x => x.Centroid != null));
        var h = BioData.Hemoglobin;
        var chains = new[] { 'A', 'B', 'C', 'D' }.Select(c => h.Residues.Count(x => x.Chain == c)).ToArray();
        r.Check("hemoglobin", string.Join(",", chains) + $",hemes{h.Hemes.Count}", chains.SequenceEqual(new[] { 141, 146, 141, 146 }) && h.Hemes.Count == 4 && h.Hemes.All(x => x.N.Length == 4));
        var beta = new string(h.Residues.Where(x => x.Chain == 'B').Select(x => x.Aa).ToArray());
        r.Check("hemoglobin.beta=HBB", beta[..Math.Min(10, beta.Length)], beta == BioGenes.HbbProtein[1..]);
        var caca = u.Zip(u.Skip(1), (a, b) => BioV.Distance(a.Ca, b.Ca)).ToList();
        double lo = caca.Count > 0 ? caca.Min() : 0, hi = caca.Count > 0 ? caca.Max() : 9;
        r.Check("ubiquitin.CA-CA", $"{F(lo, "0.00")}-{F(hi, "0.00")}", lo > 3.6 && hi < 4.0);
        var t = BioData.Trna;
        var seq = new string(t.Nucleotides.Select(x => x.Base).ToArray());
        r.Check("tRNA", $"{t.Nucleotides.Count}", t.Nucleotides.Count == 76 && seq == BioData.TrnaSequence);
        var ok = t.Pairs.All(p =>
        {
            var pr = $"{BioData.TrnaSequence[p.I - 1]}{BioData.TrnaSequence[p.J - 1]}";
            return new[] { "AU", "UA", "GC", "CG", "GU", "UG" }.Contains(pr);
        });
        r.Check("tRNA.pairs", $"{t.Pairs.Count}", t.Pairs.Count == 21 && ok);
        r.Check("tRNA.modified", $"{t.Modified.Count}", t.Modified.Count == 14 && t.Modified.FirstOrDefault().Position == 10 && t.Modified.LastOrDefault().Label == "m¹A");
        if (t.Nucleotides.Count == 76)
        {
            var anticodon = (t.Nucleotides[33].P + t.Nucleotides[34].P + t.Nucleotides[35].P) / 3;
            r.Near("tRNA.anticodon-3'end", BioV.Distance(anticodon, t.Nucleotides[75].P), 72, 1.0, 1);
        }
    }

    // ---- the foundation's geometry and labels ----

    /// <summary>A closed mesh's volume by the divergence theorem (positive when its faces face out).</summary>
    public static double Volume(BioMesh m)
    {
        var v = 0.0;
        for (var k = 0; k + 2 < m.Indices.Count; k += 3)
        {
            BioVec a = BioMesh.D(m.Positions[m.Indices[k]]), b = BioMesh.D(m.Positions[m.Indices[k + 1]]), c = BioMesh.D(m.Positions[m.Indices[k + 2]]);
            v += BioV.Dot(a, BioV.Cross(b, c)) / 6;
        }
        return v;
    }

    public static void Geometry(BioCheckReport r)
    {
        var ball = 4.0 / 3 * Math.PI;
        var ico = BioShapes.Icosphere(1, 1);
        r.Check("mesh.icosphere", $"{ico.TriangleCount}", ico.TriangleCount == 80 && Volume(ico) > 0);
        var s = BioShapes.Sphere(1, 32, 64);
        r.Near("mesh.sphere-volume", Volume(s) / ball, 1, 0.01, 4);
        // (a cut solid keeps 7/8; a cut shell keeps 7/8 of its wall — so the caps are there and face out)
        var cut = BioShapes.Radial(BioShapes.SphereRadius(1), 32, 64, BioCut.Octant);
        r.Near("mesh.octant-cut", Volume(cut) / ball, 0.875, 0.01, 4);
        var shell = BioShapes.Radial(BioShapes.SphereRadius(1), 32, 64, BioCut.Octant, BioShapes.SphereRadius(0.9));
        r.Near("mesh.cut-shell", Volume(shell) / (ball * (1 - 0.729)), 0.875, 0.015, 4);
        var half = BioShapes.Capsule(0.45, 1.3, 24, 48, BioCut.Half);
        var capsuleV = Math.PI * 0.45 * 0.45 * 1.3 + 4.0 / 3 * Math.PI * Math.Pow(0.45, 3);
        r.Near("mesh.capsule-half", Volume(half) / capsuleV, 0.5, 0.01, 4);
        var box = BioShapes.RoundedBox(new BioVec(2, 1, 1), 0.25, 32, 64);
        var boxV = 4.0 * 2 * 2 - (8 - 4.0 / 3 * Math.PI) * Math.Pow(0.25, 3) - 0.25 * 0.25 * (4 - Math.PI) * (4 * 1.5 + 4 * 0.5 + 4 * 0.5);
        r.Near("mesh.rounded-box", Volume(box) / boxV, 1, 0.02, 4);
        var tube = BioPath.Tube(new[] { new BioVec(0, 0, 0), new BioVec(0, 1, 0), new BioVec(0, 2, 0) }, 0.1, sides: 24, samples: 4);
        r.Near("mesh.tube", Volume(tube) / (Math.PI * 0.01 * 2), 1, 0.02, 4);
        var ring = BioPath.Tube(Enumerable.Range(0, 12).Select(k => new BioVec(Math.Cos(k * Math.PI / 6), 0, Math.Sin(k * Math.PI / 6))).ToList(), 0.05, sides: 12, closed: true);
        r.Check("mesh.closed-tube", F(Volume(ring), "0.0000"), Volume(ring) > 0);
        var ribbon = BioPath.Ribbon(new[] { new BioVec(0, 0, 0), new BioVec(1, 0, 0), new BioVec(2, 0, 0), new BioVec(3, 0, 0) }, 1.6, 0.4, arrow: (2, 3.2));
        r.Check("mesh.ribbon", F(Volume(ribbon), "0.000"), Volume(ribbon) > 0);
        var patch = BioShapes.ShellPatch((0.25 * Math.PI, 0.85 * Math.PI), (0, 1.1 * Math.PI), 0.06, (_, _) => 3.55);
        r.Check("mesh.shell-patch", F(Volume(patch), "0.000"), Volume(patch) > 0);
        var torus = BioShapes.Torus(0.07, 0.02, 96, 48);
        r.Near("mesh.torus", Volume(torus) / (2 * Math.PI * Math.PI * 0.07 * 0.0004), 1, 0.01, 4);
        var cyl = BioShapes.Cylinder(new BioVec(1, 1, 1), new BioVec(1, 3, 1), 0.5, 48);
        r.Near("mesh.cylinder", Volume(cyl) / (Math.PI * 0.25 * 2), 1, 0.01, 4);
        var blob = new BioBlob(0xC311, 0.06);
        r.Near("mesh.blob-amplitude", blob.Terms.Sum(x => x.A), 0.06, 1e-12);
        if (BioData.Residue("A") is { } ala)
        {
            var m = ala.Model(ph7: true);
            var items = BioMoleculeMeshes.Items(m, BioAtomStyle.BallStick, _ => 0.3, _ => "#909090");
            var atoms = items.Count(i => i.Id != null);
            r.Check("mesh.molecule", $"{atoms}atoms,{items.Count - atoms}bonds",
                atoms == 13 && m.Atoms.Count == 13 && items.Count - atoms == m.Bonds.Count + m.Bonds.Count(b => b.Order == 2));
        }
        var batch = new BioMeshBatch();
        batch.Add(ico, "a");
        batch.Add(s, "b");
        r.Check("mesh.batch", (batch.ObjectAtTriangle(79) ?? "-") + (batch.ObjectAtTriangle(80) ?? "-"),
            batch.ObjectAtTriangle(79) == "a" && batch.ObjectAtTriangle(80) == "b" && batch.ObjectAtTriangle(batch.Mesh.TriangleCount) == null
            && batch.ObjectAtVertex(ico.VertexCount - 1) == "a" && batch.ObjectAtVertex(ico.VertexCount) == "b");
        // (labels: each side's column, at least 22 apart, inside the view — all twenty where the view is tall enough for
        //  them, seven a column in one 200 tall, the largest objects keeping theirs)
        var inputs = Enumerable.Range(0, 20).Select(k => new BioLabelInput($"o{k}", $"Part {k}", k % 2 == 0 ? 300 : 500, 380 + k, Area: k)).ToList();
        var (placed, more) = BioLabelLayout.Layout(inputs, 400, 400, 150, 800, 800);
        var (few, left) = BioLabelLayout.Layout(inputs, 400, 100, 150, 800, 200);
        var lefts = placed.Where(p => p.Left).OrderBy(p => p.Y).ToList();
        var gapsOK = lefts.Zip(lefts.Skip(1)).All(p => p.Second.Y - p.First.Y >= 22 - 1e-9);
        var inside = placed.All(p => p.Frame.X >= 16 - 1e-9 && p.Frame.X + p.Frame.Width <= 800 - 16 + 1e-9 && p.Frame.Y >= 16 - 1e-9);
        r.Check("labels.layout", $"{placed.Count}+{more},{few.Count}+{left}", placed.Count == 20 && more == 0 && few.Count == 14 && left == 6
            && few.All(p => p.Id != "o0") && gapsOK && inside && lefts.All(p => p.EdgeX == 400 - 174 && p.ElbowX == 400 - 162));
    }

    // ---- the step-through (§3.6), on a clock of its own ----

    public static void Steps(BioCheckReport r)
    {
        var now = 0.0;
        var steps = Enumerable.Range(0, 4).Select(i => new BioStep(i, $"Step {i}", "…", Group: i < 2 ? 1 : 2)).ToList();
        var c = new BioStepController(steps, clock: () => now);
        c.Next();
        now = 0.3;
        var mid = c.Phase();
        now = 0.61;
        var rest = c.Phase();
        c.Play();
        now += BioStepTiming.Dwell + BioStepTiming.Move + 0.01;
        c.Tick();
        var afterOne = c.Index;
        now += 10;
        c.Tick();
        c.Tick(now + 10);
        var ended = c.Index == 3 && !c.Playing;
        c.ReduceMotion = true;
        c.JumpToGroup(1);
        var fade = c.Phase(now + 0.1);
        r.Check("steps.controller", $"{F(mid.T, "0.000")},{afterOne},{c.Index}",
            mid.From == 0 && mid.To == 1 && Math.Abs(mid.T - BioStepTiming.Ease(0.5)) < 1e-9 && rest == BioStepPhase.At(1) && afterOne == 2 && ended
            && c.Index == 0 && fade.Crossfade && Math.Abs(fade.T - 0.5) < 1e-9 && BioStepTiming.Caption(1, steps) == "Step 2 of 4 · Step 1");
    }
}
