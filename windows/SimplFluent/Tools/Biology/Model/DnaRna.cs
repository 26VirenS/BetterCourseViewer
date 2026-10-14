namespace Simpl.Tools;

// The RNA page's model (spec §4.2 RNA, §6.11): an mRNA's parts as a labelled strip (the teaching gene's mature mRNA,
// §6.12, with its cap and tail); yeast tRNA-Phe's cloverleaf laid out by §4.2's rules — straight ladders 1.0 apart along
// a stem and 1.6 across it, the acceptor stem up, the D arm left, the anticodon arm down and the T arm right, each loop a
// circle through its closing pair at 1.0 a base — with the anticodon G34 A35 A36 reading the codon 5′-UUC-3′ antiparallel;
// its 3D L-shape as a tube through the 76 P atoms of PDB 1EHZ coloured by arm, base pairs as rungs, and the 72 Å from
// the anticodon to the 3′ end; and the ribosomes' make-up.

/// <summary>A part of the tRNA: its key, its name, the positions it holds and its colour (§4.2: acceptor violet, D green,
/// anticodon blue, variable grey, T orange).</summary>
public sealed record DnaTrnaArm(string Key, string Name, string Positions, BioHexPair Colour, string About);

/// <summary>A part of the mRNA strip: its name, its letters (5′→3′), what it is, and its kind (cap, utr, cds, tail).</summary>
public sealed record DnaMrnaRegion(string Name, string Letters, string About, string Kind);

public static class DnaTrna
{
    public static readonly DnaTrnaArm Acceptor = new("acceptor", "Acceptor stem", "1–7 / 66–72, then A73 and C74 C75 A76", BioPalette.Strand1,
        "Where the amino acid is carried: the enzyme that charges the tRNA attaches phenylalanine to A76’s 3′ (or 2′) OH, at the end of the single-stranded CCA.");
    public static readonly DnaTrnaArm D = new("d", "D arm", "10–13 / 22–25, loop 14–21", BioPalette.Adenine,
        "Named for dihydrouridine (D), a modified base in its loop. It helps fold the tRNA into its L shape.");
    public static readonly DnaTrnaArm Anticodon = new("anticodon", "Anticodon arm", "27–31 / 39–43, loop 32–38", BioPalette.Cytosine,
        "Its loop carries the anticodon G34 A35 A36, which pairs antiparallel with the codon 5′-UUC-3′ (phenylalanine) on the mRNA. G34 can also wobble-pair with U, so UUU is read too.");
    public static readonly DnaTrnaArm Variable = new("variable", "Variable loop", "44–48", BioPalette.Coil,
        "Its length varies from one tRNA to another; here it is five nucleotides.");
    public static readonly DnaTrnaArm T = new("t", "T arm (TΨC)", "49–53 / 61–65, loop 54–60", BioPalette.Uracil,
        "Named for the T Ψ C in its loop (ribothymidine, pseudouridine, cytidine). Its loop packs against the D loop at the L’s corner.");
    public static readonly DnaTrnaArm Joins = new("joins", "Joins", "8–9, 26", BioPalette.Coil, "Short links between the arms.");

    public static readonly DnaTrnaArm[] Arms = { Acceptor, D, Anticodon, Variable, T, Joins };

    /// <summary>The arm a nucleotide (1–76) belongs to.</summary>
    public static DnaTrnaArm ArmOf(int i) => i switch
    {
        <= 7 => Acceptor,
        <= 9 => Joins,
        <= 25 => D,
        26 => Joins,
        <= 43 => Anticodon,
        <= 48 => Variable,
        <= 65 => T,
        _ => Acceptor,
    };

    /// <summary>The partner of a paired nucleotide (§6.11's 21 pairs), if it has one.</summary>
    public static int? Partner(int i)
    {
        foreach (var (a, b) in BioData.Trna.Pairs)
        {
            if (a == i) return b;
            if (b == i) return a;
        }
        return null;
    }

    /// <summary>A G·U pair (wobble) rather than Watson–Crick.</summary>
    public static bool IsWobble(int i, int j)
    {
        var s = BioData.TrnaSequence;
        if (i < 1 || j < 1 || i > s.Length || j > s.Length) return false;
        var p = $"{s[i - 1]}{s[j - 1]}";
        return p is "GU" or "UG";
    }

    /// <summary>The modification a nucleotide carries in the real molecule ("m²G", "Ψ"…), if any.</summary>
    public static string? Modification(int i) => BioData.Trna.Modified.FirstOrDefault(m => m.Position == i).Label;

    /// <summary>What a nucleotide is for, in a phrase.</summary>
    public static string Role(int i) => i switch
    {
        34 => "the anticodon’s first base (the wobble position)",
        35 or 36 => "part of the anticodon",
        76 => "the 3′ end, where the amino acid attaches",
        >= 74 and <= 75 => "part of the 3′ CCA end",
        73 => "the discriminator base",
        1 => "the 5′ end",
        >= 44 and <= 48 => "in the variable loop",
        >= 14 and <= 21 => "in the D loop",
        >= 32 and <= 38 => "in the anticodon loop",
        >= 54 and <= 60 => "in the TΨC loop",
        _ => Partner(i) != null ? $"in the {ArmOf(i).Name.ToLowerInvariant()}" : "a link between arms",
    };

    // ---- the cloverleaf (§4.2 layout rules) ----

    /// <summary>The spacing along a stem, across a pair, and the loops' slack (§4.2).</summary>
    public const double Along = 1.0, Across = 1.6, LoopSlack = 1.05;

    /// <summary>A loop of <paramref name="m"/> unpaired bases: a circle through its closing pair with 1.0 between bases, so
    /// its radius is (m + 1)/(2π) × 1.05.</summary>
    public static double LoopRadius(int m) => (m + 1) / (2 * Math.PI) * LoopSlack;

    /// <summary>The codon's three bases under the anticodon (left to right under G34, A35, A36: C, U, U — the codon
    /// 5′-UUC-3′ written right to left, antiparallel to the anticodon).</summary>
    public static readonly (char Base, int Pairs)[] Codon = { ('C', 34), ('U', 35), ('U', 36) };

    /// <summary>Where each nucleotide (1–76) goes on the cloverleaf (units; y up), and where the codon's three bases go.</summary>
    public static (Dictionary<int, (double X, double Y)> Bases, (double X, double Y)[] Codon, Dictionary<string, (double X, double Y)> Centres) Cloverleaf()
    {
        var p = new Dictionary<int, (double X, double Y)>();
        double h = Across / 2;
        // acceptor stem, up: 1–7 down its left, 66–72 up its right; then 73–76 on up at the right (the 3′ CCA end)
        const double a0 = 2.6;
        for (var i = 1; i <= 7; i++) p[i] = (-h, a0 + (7 - i) * Along);
        for (var j = 66; j <= 72; j++) p[j] = (h, a0 + (j - 66) * Along);
        for (var i = 73; i <= 76; i++) p[i] = (h, a0 + 6 + (i - 72) * Along);
        // D stem, left: 10–13 along its top, 22–25 along its bottom
        const double dx = 2.6;
        for (var i = 10; i <= 13; i++) p[i] = (-dx - (i - 10) * Along, h);
        for (var j = 22; j <= 25; j++) p[j] = (-dx - (25 - j) * Along, -h);
        // anticodon stem, down: 27–31 down its left, 39–43 up its right
        const double ay = 2.4;
        for (var i = 27; i <= 31; i++) p[i] = (-h, -ay - (i - 27) * Along);
        for (var j = 39; j <= 43; j++) p[j] = (h, -ay - (43 - j) * Along);
        // T stem, right: 49–53 along its bottom, 61–65 along its top
        const double tx = 3.6;
        for (var i = 49; i <= 53; i++) p[i] = (tx + (i - 49) * Along, -h);
        for (var j = 61; j <= 65; j++) p[j] = (tx + (65 - j) * Along, h);
        var centres = new Dictionary<string, (double X, double Y)>();
        void Loop(int a, int b, int first, int m, (double X, double Y) outward, string name)
        {
            var (ax, ay2) = p[a];
            var (bx, by) = p[b];
            var chord = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay2) * (by - ay2));
            var r = LoopRadius(m);
            var ol = Math.Sqrt(outward.X * outward.X + outward.Y * outward.Y);
            double ox = outward.X / ol, oy = outward.Y / ol;
            var off = Math.Sqrt(Math.Max(r * r - chord * chord / 4, 0));
            double cx = (ax + bx) / 2 + ox * off, cy = (ay2 + by) / 2 + oy * off;
            centres[name] = (cx, cy);
            double angA = Math.Atan2(ay2 - cy, ax - cx), angB = Math.Atan2(by - cy, bx - cx), angO = Math.Atan2(oy, ox);
            static double Mod(double v) => ((v % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI);
            var ccw = Mod(angB - angA);
            var sweep = Mod(angO - angA) < ccw ? ccw : -Mod(angA - angB);
            for (var t = 1; t <= m; t++)
            {
                var ang = angA + sweep * t / (m + 1);
                p[first + t - 1] = (cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
            }
        }
        Loop(13, 22, 14, 8, (-1, 0), "d");
        Loop(31, 39, 32, 7, (0, -1), "anticodon");
        Loop(53, 61, 54, 7, (1, 0), "t");
        // the variable loop, on the lower right between the anticodon and T stems (1.0 apart, clear of both)
        var variable = new (double, double)[] { (1.75, -2.75), (2.6, -3.3), (3.6, -3.4), (4.3, -2.7), (4.2, -1.7) };
        for (var k = 0; k < 5; k++) p[44 + k] = variable[k];
        // the joins: 8 and 9 from the acceptor stem to the D stem, 26 from the D stem to the anticodon stem
        p[8] = (p[7].X + (p[10].X - p[7].X) / 3, p[7].Y + (p[10].Y - p[7].Y) / 3);
        p[9] = (p[7].X + (p[10].X - p[7].X) * 2 / 3, p[7].Y + (p[10].Y - p[7].Y) * 2 / 3);
        p[26] = ((p[25].X + p[27].X) / 2, (p[25].Y + p[27].Y) / 2);
        // the codon's bases a pair's width under the anticodon, 1.0 apart
        var low = new[] { 34, 35, 36 }.Min(i => p[i].Y) - Across;
        var codon = new[] { (p[35].X - Along, low), (p[35].X, low), (p[35].X + Along, low) };
        return (p, codon, centres);
    }

    /// <summary>A cloverleaf base's drawn radius (units): its loops have about 0.84 between bases (§4.2's radius rule, the
    /// closing pair 1.6 apart), so a base is drawn a little narrower than that.</summary>
    public const double BaseRadius = 0.37;

    /// <summary>
    /// Where a small label beside a cloverleaf base goes (units): away from its neighbours along the chain and its partner
    /// (out of its ladder, or out of its loop), turned as little as needed to keep <paramref name="clearance"/> from every
    /// other base, the codon's bases and the codon's rungs.
    /// </summary>
    public static (double X, double Y) Beside(int i, Dictionary<int, (double X, double Y)> bases, (double X, double Y)[] codon, double distance, double clearance)
    {
        var p = bases[i];
        var near = new List<(double X, double Y)>();
        if (bases.TryGetValue(i - 1, out var before)) near.Add(before);
        if (bases.TryGetValue(i + 1, out var after)) near.Add(after);
        if (Partner(i) is { } j && bases.TryGetValue(j, out var partner)) near.Add(partner);
        double ax = near.Count > 0 ? p.X - near.Average(q => q.X) : 0, ay = near.Count > 0 ? p.Y - near.Average(q => q.Y) : 1;
        var len = Math.Sqrt(ax * ax + ay * ay);
        if (len < 1e-6) { ax = 0; ay = 1; len = 1; }
        ax /= len;
        ay /= len;
        var others = bases.Where(kv => kv.Key != i).Select(kv => kv.Value).Concat(codon).ToList();
        for (var k = 0; k < Codon.Length && k < codon.Length; k++)
            if (bases.TryGetValue(Codon[k].Pairs, out var top)) others.Add(((top.X + codon[k].X) / 2, (top.Y + codon[k].Y) / 2));
        static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        (double X, double Y) best = (p.X + ax * distance, p.Y + ay * distance);
        var bestClear = -1.0;
        foreach (var turn in new[] { 0, 30, -30, 60, -60, 90, -90, 120, -120, 150, -150, 180 })
        {
            var t = turn * Math.PI / 180;
            double dx = ax * Math.Cos(t) - ay * Math.Sin(t), dy = ax * Math.Sin(t) + ay * Math.Cos(t);
            var q = (p.X + dx * distance, p.Y + dy * distance);
            var clear = others.Min(o => Dist(o, q));
            if (clear >= clearance) return q;
            if (clear > bestClear) { bestClear = clear; best = q; }
        }
        return best;
    }

    /// <summary>The number labels' bases (every tenth) and where a number and a badge (a modification) sit beside a base.</summary>
    public static readonly int[] Numbered = { 10, 20, 30, 40, 50, 60, 70 };
    public const double NumberDistance = BaseRadius + 0.42, NumberClearance = BaseRadius + 0.3;
    public const double BadgeDistance = BaseRadius + 0.55, BadgeClearance = BaseRadius + 0.5;

    // ---- the 3D L-shape (§4.2 Should) ----

    /// <summary>The tube's radius, and how far toward each other a pair's rung reaches from its P atoms (35%).</summary>
    public const double TubeRadius = 1.5, RungPull = 0.35;

    /// <summary>The anticodon's middle (the mean of P34–P36) and the 3′ end (P76), and the distance between (§6.11: 72 Å).</summary>
    public static (BioVec Anticodon, BioVec End, double Distance) Reach()
    {
        var n = BioData.Trna.Nucleotides;
        if (n.Count < 76) return (BioVec.Zero, BioVec.Zero, 0);
        var a = (n[33].P + n[34].P + n[35].P) / 3;
        return (a, n[75].P, BioV.Distance(a, n[75].P));
    }

    /// <summary>The L-shape: a tube through the 76 P atoms coloured by arm, the 21 base pairs as rungs pulled 35% in from
    /// their P atoms, the anticodon's and the 3′ end's nucleotides marked, the arms labelled, and a ruler from the
    /// anticodon to the 3′ end.</summary>
    /// <summary>
    /// The 76 P atoms turned (a rotation only, so the molecule keeps its hand) to stand as textbooks draw the L: the
    /// anticodon arm hanging down from the elbow (where the D and T loops meet) and the acceptor arm reaching right to the
    /// 3′ end; centred.
    /// </summary>
    public static List<BioVec> Oriented()
    {
        var nts = BioData.Trna.Nucleotides;
        var pts = nts.Select(x => x.P).ToList();
        if (pts.Count < 76) return pts;
        var (anti, end, _) = Reach();
        var elbow = (pts[17] + pts[18] + pts[54] + pts[55]) / 4;
        var y = BioV.Unit(elbow - anti);
        var along = end - elbow;
        var x = BioV.Unit(along - y * BioV.Dot(along, y));
        var z = BioV.Cross(x, y);
        var centre = pts.Aggregate(BioVec.Zero, (s, p) => s + p) / pts.Count;
        return pts.Select(p =>
        {
            var d = p - centre;
            return new BioVec(BioV.Dot(d, x), BioV.Dot(d, y), BioV.Dot(d, z));
        }).ToList();
    }

    public static BioSceneContent Model(bool dark)
    {
        var nts = BioData.Trna.Nucleotides;
        var items = new List<BioSceneItem>();
        var labels = new List<BioLabel3D>();
        if (nts.Count < 76) return new BioSceneContent();
        var pos = Oriented();
        const int samples = 6;
        var spline = BioPath.CatmullRom(pos, samples);
        // (a run of nucleotides of one arm: from halfway to the one before to halfway to the one after; the runs meet seamlessly)
        var runs = new List<(int Start, int End)>();
        var runStart = 1;
        for (var i = 2; i <= 77; i++)
        {
            if (i <= 76 && ArmOf(i).Key == ArmOf(runStart).Key) continue;
            runs.Add((runStart, i - 1));
            runStart = i;
        }
        var cuts = new List<int> { 0 };
        foreach (var (start, _) in runs.Skip(1)) cuts.Add((start - 1) * samples - samples / 2);
        cuts.Add(spline.Count - 1);
        var pieces = DnaHelix.TubePieces(spline, TubeRadius, cuts);
        for (var k = 0; k < runs.Count && k < pieces.Count; k++)
        {
            var arm = ArmOf(runs[k].Start);
            items.Add(new BioSceneItem($"arm:{arm.Key}", pieces[k], new BioMaterial(arm.Colour, dark, roughness: 0.4)));
        }
        foreach (var (a, b) in BioData.Trna.Pairs)
        {
            BioVec pa = pos[a - 1], pb = pos[b - 1];
            var arm = ArmOf(a);
            var rung = BioShapes.Cylinder(pa + (pb - pa) * RungPull, pb + (pa - pb) * RungPull, 0.55, 10);
            items.Add(new BioSceneItem($"arm:{arm.Key}", rung, new BioMaterial(arm.Colour, dark, roughness: 0.5)));
        }
        // (the anticodon's three nucleotides and A76, as balls in their bases' colours)
        foreach (var i in new[] { 34, 35, 36, 76 })
            items.Add(new BioSceneItem($"arm:{ArmOf(i).Key}", BioShapes.Sphere(2.1, 14, 28).Translated(pos[i - 1]),
                new BioMaterial(BioPalette.Base(nts[i - 1].Base), dark, roughness: 0.4)));
        var anti = (pos[33] + pos[34] + pos[35]) / 3;
        var end = pos[75];
        var dist = BioV.Distance(anti, end);
        // (the ruler: anticodon to 3′ end, with ticks)
        var ruler = BioShapes.Cylinder(anti, end, 0.18, 10);
        var dir = BioV.Unit(end - anti);
        var side = BioV.Unit(BioV.Cross(dir, new BioVec(0, 0, 1)));
        if (BioV.Length(side) < 0.1) side = BioV.Unit(BioV.Cross(dir, new BioVec(0, 1, 0)));
        ruler.Append(BioShapes.Cylinder(anti - side * 1.6, anti + side * 1.6, 0.18, 10));
        ruler.Append(BioShapes.Cylinder(end - side * 1.6, end + side * 1.6, 0.18, 10));
        items.Add(new BioSceneItem("ruler", ruler, new BioMaterial(dark ? "#d1d1d6" : "#48484a", Roughness: 0.6)));
        BioVec Mean(int from, int to) => Enumerable.Range(from, to - from + 1).Aggregate(BioVec.Zero, (s, k) => s + pos[k - 1]) / (to - from + 1);
        labels.Add(new BioLabel3D("arm:anticodon", "Anticodon (G34 A35 A36)", anti, Area: 6));
        labels.Add(new BioLabel3D("arm:acceptor", "Amino acid attaches here (3′ CCA)", end, Area: 6));
        labels.Add(new BioLabel3D("arm:acceptor", "Acceptor stem", Mean(1, 7), Area: 4));
        labels.Add(new BioLabel3D("arm:d", "D arm", Mean(14, 21), Area: 4));
        labels.Add(new BioLabel3D("arm:t", "T arm (TΨC)", Mean(54, 60), Area: 4));
        labels.Add(new BioLabel3D("arm:variable", "Variable loop", Mean(44, 48), Area: 2));
        labels.Add(new BioLabel3D("ruler", $"≈7 nm ({dist:0} Å) from anticodon to 3′ end", (anti + end) / 2, Area: 5));
        return new BioSceneContent { Items = items, Labels = labels };
    }
}

public static class DnaMrna
{
    /// <summary>The teaching gene's mature mRNA (§6.12, made up for this tool) as a strip: the 5′ cap, the 5′ UTR, the
    /// coding sequence from AUG to the stop, the 3′ UTR (with the poly-A signal) and the poly-A tail.</summary>
    public static List<DnaMrnaRegion> Regions()
    {
        var g = BioGenes.Teaching;
        var mature = g.MatureMRNA;
        var cds = BioSeq.Transcribe(g.Cds);
        var start = mature.IndexOf(cds, StringComparison.Ordinal);
        if (start < 0) start = mature.IndexOf("AUG", StringComparison.Ordinal);
        var utr5 = start > 0 ? mature[..start] : "";
        var utr3 = start >= 0 && start + cds.Length <= mature.Length ? mature[(start + cds.Length)..] : "";
        return new List<DnaMrnaRegion>
        {
            new("5′ cap", "m⁷G", "A methylated G joined to the first nucleotide 5′ to 5′. It protects the mRNA and helps the ribosome find it.", "cap"),
            new("5′ UTR", utr5, "Untranslated: read by the ribosome as it scans for the start codon, but not made into protein.", "utr"),
            new("Coding sequence", cds, "From the start codon AUG to the stop codon: read three bases at a time into the protein.", "cds"),
            new("3′ UTR", utr3, "Untranslated. It holds the poly-A signal AAUAAA, which marks where the tail is added.", "utr"),
            new("Poly-A tail", new string('A', 18), "A long run of A nucleotides added after transcription (not copied from the DNA). It protects the mRNA’s 3′ end.", "tail"),
        };
    }

    /// <summary>The coding sequence's codons with their amino acids (one letter; * for the stop).</summary>
    public static List<(string Codon, char AminoAcid)> Codons()
    {
        var cds = BioSeq.Transcribe(BioGenes.Teaching.Cds);
        return BioSeq.Codons(cds).Select(c => (c, BioCode.AminoAcid(c) ?? '?')).ToList();
    }
}

/// <summary>The ribosomes' make-up (§4.2 rRNA and ribosomes).</summary>
public static class DnaRibosomes
{
    public sealed record Subunit(string Name, string Rna, string Proteins);
    public sealed record Ribosome(string Name, string Where, Subunit Small, Subunit Large);

    public static readonly Ribosome Bacterial = new("70S", "Bacteria (and, 70S-type, mitochondria and chloroplasts)",
        new Subunit("30S", "16S rRNA", "about 21 proteins"), new Subunit("50S", "23S + 5S rRNA", "about 33 proteins"));
    public static readonly Ribosome Eukaryotic = new("80S", "The cytosol of eukaryotic cells",
        new Subunit("40S", "18S rRNA", ""), new Subunit("60S", "28S + 5.8S + 5S rRNA", ""));
}
