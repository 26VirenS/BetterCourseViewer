using System.Globalization;

namespace Simpl.Tools;

// DNA & RNA's bio-check lines (spec §10.1, run with the foundation's by BioSelfCheck.Run, --bio-check and the unit
// tests): the double helix's cartoon (right-handed, antiparallel, the purine's and pyrimidine's boxes, 2 and 3 H-bond
// dashes), unzipping, the grooves (§6.7's arcs and widths), the Sequence page's facts for the default strand (counts,
// GC, Chargaff, mass, melting temperature, a seeded random strand), the nucleotides' parts and numbers, the 2D base
// pairs' H-bonds (§6.5), replication's steps and keyframes, the cloverleaf's layout rules (§4.2), the tRNA model's
// 72 Å and the mRNA strip. Expected values were worked out apart from this code (by script from the specification).

public static class DnaSelfCheck
{
    private static string F(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

    public static void Run(BioCheckReport r)
    {
        Helix(r);
        SequenceFacts(r);
        Nucleotides(r);
        Replication(r);
        Rna(r);
    }

    // ---- the double helix ----

    public static void Helix(BioCheckReport r)
    {
        var seq = DNAToolModel.DefaultSequence;
        var d = BioBDNA.Build(seq);
        var n = seq.Length;
        // (strand 1's backbone runs 5′→3′ up, strand 2's down, each turning the same way as it rises: right-handed)
        var up = DnaHelix.BackbonePath(d, 1);
        var down = DnaHelix.BackbonePath(d, 2);
        bool Rises(List<BioVec> p) => p.Zip(p.Skip(1)).All(x => x.Second.Y > x.First.Y);
        bool Falls(List<BioVec> p) => p.Zip(p.Skip(1)).All(x => x.Second.Y < x.First.Y);
        r.Check("dna.antiparallel", $"up{up.Count},down{down.Count}", up.Count == n + 1 && down.Count == n + 1 && Rises(up) && Falls(down));
        var ps = up.Take(n).ToList();
        var turns = ps.Zip(ps.Skip(1), (a, b) => BioV.Cross(new BioVec(a.X, 0, a.Z), new BioVec(b.X, 0, b.Z)).Y).ToList();
        var twist = ps.Zip(ps.Skip(1), (a, b) => BioV.Angle(new BioVec(a.X, 0, a.Z), new BioVec(b.X, 0, b.Z))).Average();
        r.Check("dna.cartoon.right-handed", F(twist, "0.0000") + "°", turns.All(t => t > 0) && Math.Abs(twist - 360.0 / 10.5) < 1e-6);
        r.Near("dna.pitch", 10.5 * BioBDNA.Rise, 35.7, 1e-9, 2);

        // (the boxes: from each C1′ toward the other, purine 58% and pyrimidine 42% of the C1′–C1′ distance less the gap)
        var boxes = DnaHelix.Boxes(d);
        var bad = new List<string>();
        foreach (var k in Enumerable.Range(0, n))
        {
            var one = boxes.First(b => b.Pair == k && b.Strand == 1);
            var two = boxes.First(b => b.Pair == k && b.Strand == 2);
            var span = BioV.Distance(one.Start, two.Start) - DnaHelix.Gap;
            var purine = one.IsPurine ? one : two;
            var pyrimidine = one.IsPurine ? two : one;
            var gap = BioV.Distance(one.Start + one.Along * one.Length, two.Start + two.Along * two.Length);
            if (Math.Abs(purine.Length - 0.58 * span) > 1e-9 || Math.Abs(pyrimidine.Length - 0.42 * span) > 1e-9 || Math.Abs(gap - DnaHelix.Gap) > 1e-9 || purine.IsPurine == pyrimidine.IsPurine)
                bad.Add($"{k}");
        }
        var b0 = boxes.First(b => b.Pair == 0 && b.Strand == 1);
        r.Check("dna.cartoon.boxes", bad.Count == 0 ? $"A{F(b0.Length, "0.000")},C1'-C1'{F(BioV.Distance(b0.Start, boxes.First(b => b.Pair == 0 && b.Strand == 2).Start), "0.000")}" : string.Join(",", bad),
            bad.Count == 0 && boxes.Count == 2 * n && Math.Abs(b0.Length - 0.58 * (10.862 - 1)) < 0.002);
        var dashes = Enumerable.Range(0, n).Sum(k => DnaHelix.CartoonHBonds(boxes.First(b => b.Pair == k && b.Strand == 1), boxes.First(b => b.Pair == k && b.Strand == 2)).Count);
        var at = DnaHelix.CartoonHBonds(boxes.First(b => b.Pair == 0 && b.Strand == 1), boxes.First(b => b.Pair == 0 && b.Strand == 2)).Count;
        var gc = DnaHelix.CartoonHBonds(boxes.First(b => b.Pair == 2 && b.Strand == 1), boxes.First(b => b.Pair == 2 && b.Strand == 2)).Count;
        r.Check("dna.cartoon.H-bonds", $"AT{at},GC{gc},all{dashes}", at == 2 && gc == 3 && dashes == 50);
        // (the H-bonds as built: every one 2.79–3.00 Å, §6.5)
        var lengths = Enumerable.Range(0, n).SelectMany(k => DnaHelix.PairBonds(d, k)).Select(x => x.Distance).ToList();
        r.Check("dna.H-bond-lengths", $"{lengths.Count},{F(lengths.Min(), "0.00")}-{F(lengths.Max(), "0.00")}", lengths.Count == 50 && lengths.All(x => x is >= 2.785 and <= 3.005));

        // (the scene: a box, a letter and a backbone piece for every nucleotide; the overlays' parts and labels)
        var scene = DnaHelix.Build(new DnaHelixOptions(seq, DnaStyle.Cartoon, Ends: true, Grooves: true, Measure: true));
        var ids = scene.Content.Items.Select(i => i.Id ?? "").ToHashSet();
        var missing = Enumerable.Range(0, n).SelectMany(k => new[] { $"bp:{k}:1:base", $"bp:{k}:2:base", $"bp:{k}:1:bb", $"bp:{k}:2:bb", $"bp:{k}:hb" }).Where(x => !ids.Contains(x)).ToList();
        var overlays = new[] { "arrow:1", "arrow:2", "groove:minor", "groove:major", "measure:width", "measure:rise", "measure:pitch" }.Where(x => !ids.Contains(x)).ToList();
        var labelIds = scene.Content.Labels.Select(l => l.Id).ToList();
        var triangles = scene.Content.Items.Sum(i => i.Mesh.TriangleCount);
        r.Check("dna.scene.cartoon", $"{scene.Content.Items.Count}items,{scene.Letters.Count}letters,{scene.Content.Labels.Count}labels,{triangles}tri",
            missing.Count == 0 && overlays.Count == 0 && scene.Letters.Count(l => l.Text.Length == 1) == 2 * n && labelIds.Count <= BioLabelLayout.MaxLabels
            && new[] { "end:1:5", "end:1:3", "end:2:5", "end:2:3", "groove:minor", "groove:major" }.All(labelIds.Contains) && triangles < 300_000);
        var atoms = DnaHelix.Build(new DnaHelixOptions(DnaBig, DnaStyle.Atoms));
        var spaceTri = DnaHelix.Build(new DnaHelixOptions(DnaBig, DnaStyle.SpaceFill)).Content.Items.Sum(i => i.Mesh.TriangleCount);
        var atomTri = atoms.Content.Items.Sum(i => i.Mesh.TriangleCount);
        r.Check("dna.scene.atoms-40bp", $"{atomTri}tri,{spaceTri}tri", atomTri < 300_000 && spaceTri < 300_000 && atoms.Duplex.Atoms.Count > 1600);
        // (the ends: strand 1's 5′ at the bottom, strand 2's 5′ at the top)
        var e15 = scene.Content.Labels.First(l => l.Id == "end:1:5").Anchor.Y;
        var e25 = scene.Content.Labels.First(l => l.Id == "end:2:5").Anchor.Y;
        r.Check("dna.ends", $"1:5′@{F(e15, "0.0")},2:5′@{F(e25, "0.0")}", e15 < 1 && e25 > (n - 1) * BioBDNA.Rise);

        // (unzipping half of 20 bp: pairs 0–9 apart by 8 Å more, 10–19 still shut)
        var open = DnaHelix.Opened(d, 0.5);
        double Span(BioDuplex x, int k) => BioV.Distance(x.Pos(1, k, "C1'") ?? BioVec.Zero, x.Pos(2, k, "C1'") ?? BioVec.Zero);
        var rest = Span(d, 0);
        var opened = Enumerable.Range(0, n).Count(k => Span(open, k) > rest + 1e-6);
        r.Check("dna.unzip", $"{opened}open,{F(Span(open, 0) - rest, "0.000")}Å", opened == 10 && Math.Abs(Span(open, 0) - rest - DnaHelix.Separation) < 1e-9
            && Math.Abs(Span(open, 10) - rest) < 1e-9 && DnaHelix.Opening(0, 20, 0) == 0 && DnaHelix.Opening(19, 20, 1) == 1);

        // (the grooves: about 148° and 212° of arc; about 6.6 and 11.6 Å wide, P–P less 5.8 Å)
        r.Near("dna.grooves.minor-arc", DnaHelix.MinorArc, 148.454, 0.01, 2);
        var (minor, major) = DnaHelix.GrooveWidths();
        r.Check("dna.grooves.widths", $"{F(minor, "0.00")},{F(major, "0.00")}", Math.Abs(minor - 6.6) < 0.1 && Math.Abs(major - 11.6) < 0.1);
    }

    /// <summary>A 40 bp strand (the most the helix draws), for the triangle budget.</summary>
    private const string DnaBig = "ATGCGTACGTTAGCCATGCAATGCGTACGTTAGCCATGCA";

    // ---- the Sequence page ----

    public static void SequenceFacts(BioCheckReport r)
    {
        var s = DNAToolModel.DefaultSequence;
        var k = BioSeq.Counts(s);
        r.Check("dna.seq.counts", $"A{k.A}T{k.T}G{k.G}C{k.C}", k.A == 5 && k.T == 5 && k.G == 5 && k.C == 5 && Math.Abs(BioSeq.GcContent(s) - 0.5) < 1e-12);
        r.Same("dna.seq.reverse-complement", BioSeq.ReverseComplement(s) + "|" + BioSeq.Complement(s), "TGCATGGCTAACGTACGCAT|TACGCATGCAATCGGTACGT");
        r.Near("dna.seq.mass", BioSeq.Mass(s), 6196.87, 0.005, 2);
        r.Near("dna.seq.Tm", BioSeq.MeltingTemperature(s), 51.78, 1e-9, 2);
        r.Near("dna.seq.Chargaff", BioSeq.Chargaff("AAAAGC").PurinePercent, 50, 1e-9, 1);
        r.Same("dna.seq.random", DNAToolModel.RandomStrand(42), "GACCATATCGACGGGAACAG");
        r.Same("dna.seq.helix", DNAToolModel.HelixOf("ACGTN" + new string('A', 50)), "ACGT" + new string('A', 35));
    }

    // ---- the nucleotides ----

    public static void Nucleotides(BioCheckReport r)
    {
        var wrong = new List<string>();
        foreach (var code in DnaNucleotides.Codes)
        {
            if (BioData.Molecule(code) is not { } m) { wrong.Add(code); continue; }
            var parts = DnaNucleotides.Parts(m);
            var b = DnaNucleotides.Base(code);
            HashSet<string> Of(DnaPart p) => parts.Where(x => x.Value == p).Select(x => x.Key).ToHashSet();
            var sugar = Of(DnaPart.Sugar).Where(x => m.Atom(x)?.Element != "H").ToHashSet();
            var phosphate = Of(DnaPart.Phosphate);
            var bases = Of(DnaPart.Base);
            var wantSugar = new HashSet<string> { "C1'", "C2'", "C3'", "C4'", "C5'", "O4'", "O3'" };
            if (DnaNucleotides.IsRibose(code)) wantSugar.Add("O2'");
            var ok = parts.Count == m.Atoms.Count && sugar.SetEquals(wantSugar) && phosphate.Contains("O5'") && phosphate.All(x => x.StartsWith('P') || x.StartsWith('O') || x.StartsWith("HO"))
                     && bases.Contains(DnaNucleotides.GlycosidicN(b)) && m.Atom("O2'") != null == DnaNucleotides.IsRibose(code)
                     && (b != 'T' || bases.Contains("C7")) && (b != 'U' || m.Atom("C7") == null);
            if (!ok) wrong.Add(code);
        }
        r.Check("dna.nt.parts", wrong.Count == 0 ? "9" : string.Join(",", wrong), wrong.Count == 0);
        var da = BioData.Molecule("DA")!.Atoms.Select(a => DnaNucleotides.Number(a.Name, 'A')).Where(x => x != null).ToList();
        var dc = BioData.Molecule("DC")!.Atoms.Select(a => DnaNucleotides.Number(a.Name, 'C')).Where(x => x != null).ToList();
        r.Check("dna.nt.numbers", $"A{da.Count},C{dc.Count}", da.Count == 14 && dc.Count == 11 && new[] { "1′", "2′", "3′", "4′", "5′" }.All(da.Contains) && da.Contains("9") && !dc.Contains("7"));
        var (scene, numbers) = DnaNucleotides.Build(new DnaNucleotideOptions("DT", Numbers: true));
        var labels = scene.Labels.Select(l => l.Text).ToList();
        r.Check("dna.nt.scene", $"{scene.Labels.Count}labels,{numbers.Count}numbers",
            labels.Contains("Phosphate group") && labels.Contains("Deoxyribose (no O on 2′)") && labels.Any(t => t.StartsWith("Glycosidic bond (C1′–N1)"))
            && labels.Any(t => t.StartsWith("5-methyl")) && numbers.Count == 11 && scene.Items.Any(i => i.Id == "part:glycosidic"));

        // (the 2D pairs: §6.5's heavy-atom H-bond distances from §6.7's projected pairs)
        var atPair = DnaNucleotides.Pair2D("AT");
        var gcPair = DnaNucleotides.Pair2D("GC");
        var au = DnaNucleotides.Pair2D("AU");
        var at = atPair.HBonds.Select(h => h.Distance).ToArray();
        var gc = gcPair.HBonds.Select(h => h.Distance).ToArray();
        r.Check("dna.pairs.H-bonds", string.Join(",", at.Concat(gc).Select(x => F(x, "0.00"))),
            at.Length == 2 && gc.Length == 3 && Near(at[0], 2.84) && Near(at[1], 2.91) && Near(gc[0], 2.87) && Near(gc[1], 2.79) && Near(gc[2], 3.00));
        var h5 = au.Atoms.FindIndex(a => a.Base == 'U' && a.Name == "H5");
        var c5 = au.Atoms.FindIndex(a => a.Base == 'U' && a.Name == "C5");
        var c5h5 = h5 >= 0 && c5 >= 0 ? Math.Sqrt(Math.Pow(au.Atoms[h5].X - au.Atoms[c5].X, 2) + Math.Pow(au.Atoms[h5].Y - au.Atoms[c5].Y, 2)) : 0;
        r.Check("dna.pairs.AU", $"{au.HBonds.Count}H-bonds,C5-H5{F(c5h5, "0.00")}", au.HBonds.Count == 2 && Math.Abs(c5h5 - 1.08) < 1e-9 && au.Atoms.All(a => a.Name != "C7")
            && au.Rings.Count == 3 && atPair.Rings.Count(x => x.Base == 'A') == 2 && atPair.Rings.Count(x => x.Base == 'T') == 1 && au.Bonds.Count > 0);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) <= 0.015;

    // ---- replication ----

    public static void Replication(BioCheckReport r)
    {
        var steps = DnaReplication.Steps;
        r.Check("dna.rep.steps", $"{steps.Length}", steps.Length == 9 && DnaReplication.Frames.Length == 9 && DnaReplication.Enzymes.Length == 9
            && steps.All(s => s.Title.Length <= 40 && s.Group is >= 1 and <= 5) && steps.Select((s, i) => s.Index == i).All(x => x));
        // (each step names the enzyme at work)
        var named = new[] { "elicase", "rimase", "polymerase III", "polymerase III", "Okazaki", "polymerase I ", "ligase" };
        var missing = Enumerable.Range(1, 7).Where(i => !(steps[i].Title + " " + steps[i].Text).Contains(named[i - 1], StringComparison.Ordinal)).ToList();
        r.Check("dna.rep.enzymes", missing.Count == 0 ? "7" : string.Join(",", missing), missing.Count == 0);
        // (primers are RNA: U, never T; three Okazaki fragments, each from its own primer)
        var primed = DnaReplication.Frames[2];
        var lead = new string(Enumerable.Range(0, 4).Select(i => DnaReplication.NewLetter(true, i, DnaReplication.Leading(i, primed)?.Kind == DnaPieceKind.Rna)).ToArray());
        var lag = new string(Enumerable.Range(8, 4).Select(i => DnaReplication.NewLetter(false, i, DnaReplication.Lagging(i, primed)?.Kind == DnaPieceKind.Rna)).ToArray());
        var six = DnaReplication.Frames[5];
        var fragments = Enumerable.Range(0, 3).Count(j => DnaReplication.Lagging(DnaReplication.PrimerStart(j), six)?.Kind == DnaPieceKind.Rna
                                                     && DnaReplication.Lagging(DnaReplication.PrimerStart(j) - 1, six)?.Kind == DnaPieceKind.Dna);
        r.Check("dna.rep.primers", $"{lead},{lag},{fragments}fragments", lead == "AUGC" && lag == "CAAU" && fragments == 3);
        // (5′→3′ both ways: the leading strand's 3′ end moves right with the fork, the lagging fragment's left, away from it)
        var f = DnaReplication.Frames;
        r.Check("dna.rep.directions", $"lead{f[2].LeadDna}-{f[3].LeadDna}-{f[4].LeadDna},lag{f[2].LagDna[0]}-{f[3].LagDna[0]}-{f[4].LagDna[0]}",
            f[2].LeadDna < f[3].LeadDna && f[3].LeadDna < f[4].LeadDna && f[2].LagDna[0] > f[3].LagDna[0] && f[3].LagDna[0] > f[4].LagDna[0]
            && f[4].LeadDna <= f[4].Fork && f[3].LeadDna <= f[3].Fork);
        // (the end: both new strands whole DNA, primers gone and nicks sealed; each daughter one old strand and one new)
        var last = f[^1];
        var whole = Enumerable.Range(0, DnaReplication.Length).All(i => DnaReplication.Leading(i, last) is { Kind: DnaPieceKind.Dna, Amount: 1 }
                                                                    && DnaReplication.Lagging(i, last) is { Kind: DnaPieceKind.Dna, Amount: 1 });
        var paired = Enumerable.Range(0, DnaReplication.Length).All(i => BioSeq.Pair(DnaReplication.Top[i], false) == DnaReplication.NewLetter(true, i, false)
                                                                     && BioSeq.Pair(DnaReplication.Bottom[i], false) == DnaReplication.NewLetter(false, i, false));
        r.Check("dna.rep.semiconservative", whole && paired ? "2×(old+new)" : "not-whole", whole && paired && last.Sealed == 1 && last.Replaced == 1 && last.Apart == 1);
        var mid = DnaReplication.At(new BioStepPhase(3, 4, 0.5, false));
        r.Check("dna.rep.mix", F(mid.Fork, "0.0") + "," + F(mid.LeadDna, "0.0"), Math.Abs(mid.Fork - 18.5) < 1e-9 && Math.Abs(mid.LeadDna - 16) < 1e-9);
        // (a new strand only where the parent strands have fully parted: every keyframe, and half way between each two)
        var cramped = new List<string>();
        for (var s = 0; s < f.Length; s++)
            foreach (var t in new[] { 0.0, 0.5 })
            {
                if (s == f.Length - 1 && t > 0) continue;
                var frame = t == 0 ? f[s] : DnaForkFrame.Mix(f[s], f[s + 1], t);
                for (var i = 0; i < DnaReplication.Length; i++)
                    if ((DnaReplication.Leading(i, frame) != null || DnaReplication.Lagging(i, frame) != null) && DnaReplication.Open(i + 0.5, frame.Fork) < 0.999)
                        cramped.Add($"{s}{(t > 0 ? "½" : "")}@{i}");
            }
        r.Check("dna.rep.open", cramped.Count == 0 ? "all" : string.Join(",", cramped.Take(4)), cramped.Count == 0);
    }

    // ---- RNA ----

    public static void Rna(BioCheckReport r)
    {
        var (p, codon, centres) = DnaTrna.Cloverleaf();
        double D((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        var pairs = BioData.Trna.Pairs;
        var across = pairs.Select(x => D(p[x.I], p[x.J])).ToList();
        // (along each stem's strands, 1.0 between successive bases)
        var stems = new (int From, int To)[] { (1, 7), (66, 72), (10, 13), (22, 25), (27, 31), (39, 43), (49, 53), (61, 65) };
        var along = stems.SelectMany(s => Enumerable.Range(s.From, s.To - s.From).Select(i => D(p[i], p[i + 1]))).ToList();
        r.Check("trna.stems", $"{pairs.Count}pairs,{F(across.Min(), "0.00")}-{F(across.Max(), "0.00")},{F(along.Min(), "0.00")}-{F(along.Max(), "0.00")}",
            p.Count == 76 && pairs.Count == 21 && across.All(x => Math.Abs(x - DnaTrna.Across) < 1e-9) && along.All(x => Math.Abs(x - DnaTrna.Along) < 1e-9));
        // (each loop a circle through its closing pair, radius (m + 1)/(2π) × 1.05)
        var loops = new (string Name, int Close, int First, int M)[] { ("d", 13, 14, 8), ("anticodon", 31, 32, 7), ("t", 53, 54, 7) };
        var loopOk = loops.All(l =>
        {
            var c = centres[l.Name];
            var rr = DnaTrna.LoopRadius(l.M);
            return Enumerable.Range(l.First, l.M).Append(l.Close).All(i => Math.Abs(D(p[i], c) - rr) < 1e-9);
        });
        r.Check("trna.loops", string.Join(",", loops.Select(l => F(DnaTrna.LoopRadius(l.M), "0.000"))), loopOk);
        // (the arms' directions: acceptor up, D left, anticodon down, T right; the 3′ end the highest; no two bases closer than 0.9)
        double MeanX(int a, int b) => Enumerable.Range(a, b - a + 1).Average(i => p[i].X);
        double MeanY(int a, int b) => Enumerable.Range(a, b - a + 1).Average(i => p[i].Y);
        var directions = MeanY(1, 7) > 2 && MeanX(10, 25) < -2 && MeanY(27, 43) < -2 && MeanX(49, 65) > 2 && p[76].Y == p.Values.Max(v => v.Y) && p[76].X > 0
                         && new[] { 34, 35, 36 }.All(i => p[i].Y < -8) && p[34].X < p[35].X && p[35].X < p[36].X;
        var tooClose = 0;
        for (var i = 1; i <= 76; i++)
            for (var j = i + 2; j <= 76; j++)
                if (D(p[i], p[j]) < 0.9) tooClose++;
        r.Check("trna.layout", $"{tooClose}close", directions && tooClose == 0);
        // (the codon under the anticodon pairs antiparallel: C·G34, U·A35, U·A36, so it reads 5′-UUC-3′ right to left: Phe)
        var seq = BioData.TrnaSequence;
        var pairsOk = DnaTrna.Codon.All(c => BioSeq.Pair(c.Base, rna: true) == seq[c.Pairs - 1] || (c.Base == 'C' && seq[c.Pairs - 1] == 'G'));
        var read = new string(DnaTrna.Codon.Reverse().Select(c => c.Base).ToArray());
        r.Check("trna.codon", $"{read}={BioCode.AminoAcid(read)}", pairsOk && read == "UUC" && BioCode.AminoAcid(read) == 'F' && seq[33..36] == "GAA"
            && codon.Length == 3 && codon.All(c => c.Y < p[35].Y));
        // (the L turned to stand as textbooks draw it — by a rotation only: every distance and the hand kept)
        var raw = BioData.Trna.Nucleotides.Select(x => x.P).ToList();
        var turned = DnaTrna.Oriented();
        double Volume(List<BioVec> q) => BioV.Dot(q[9] - q[0], BioV.Cross(q[33] - q[0], q[75] - q[0]));
        var kept = Enumerable.Range(0, 76).All(i => Math.Abs(BioV.Distance(turned[i], turned[(i * 7 + 3) % 76]) - BioV.Distance(raw[i], raw[(i * 7 + 3) % 76])) < 1e-9);
        var elbowY = (turned[17].Y + turned[18].Y + turned[54].Y + turned[55].Y) / 4;
        var elbowX = (turned[17].X + turned[18].X + turned[54].X + turned[55].X) / 4;
        r.Check("trna.L", $"anticodon-y{F(turned[34].Y - elbowY, "0")},3'end-x{F(turned[75].X - elbowX, "0")}",
            kept && Math.Sign(Volume(raw)) == Math.Sign(Volume(turned)) && turned[34].Y < elbowY - 30 && turned[75].X > elbowX + 30);
        var (_, _, dist) = DnaTrna.Reach();
        var model = DnaTrna.Model(false);
        var arms = model.Items.Select(i => i.Id).Distinct().ToList();
        r.Check("trna.3D", $"{F(dist, "0.0")}Å,{arms.Count}parts", Math.Abs(dist - 72) < 1 && new[] { "arm:acceptor", "arm:d", "arm:anticodon", "arm:variable", "arm:t", "ruler" }.All(arms.Contains)
            && model.Labels.Count <= BioLabelLayout.MaxLabels);
        var regions = DnaMrna.Regions();
        var cds = regions.First(x => x.Kind == "cds").Letters;
        var protein = new string(DnaMrna.Codons().Select(c => c.AminoAcid).ToArray());
        r.Check("mrna.strip", $"{regions[1].Letters.Length}+{cds.Length}+{regions[3].Letters.Length},{protein}",
            regions[1].Letters == "ACUCAGCGCCACC" && cds.StartsWith("AUG", StringComparison.Ordinal) && cds.EndsWith("UAA", StringComparison.Ordinal) && cds.Length == 30
            && regions[3].Letters.Contains("AAUAAA") && protein == BioGenes.Teaching.Protein + "*" && !string.Concat(regions.Select(x => x.Letters)).Contains('T'));
    }
}
