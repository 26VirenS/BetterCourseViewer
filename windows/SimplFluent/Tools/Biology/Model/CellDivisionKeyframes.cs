namespace Simpl.Tools;

// Cell Division's keyframes (spec §5.2.4, in cell units, the spindle along y), the Mac's CellDivisionKeyframes.swift:
// where every chromatid, cell, nucleus, pole and fibre is at the end of each step of mitosis and of meiosis, for the model
// organism (2n = 4, or 2n = 6), in an animal or a plant cell, with crossing over, independent assortment and
// nondisjunction as set. The rules are the specification's — interphase threads at seeded places in a nucleus of radius
// 0.55, the centrosomes from (0.62, 0.62) round to the poles at (0, ±0.82), chromosomes on the plate with their
// centromeres on y = 0 and their chromatids along it (φ = 0), anaphase chromatids at (xᵢ·0.6, ±0.62) bent into a V 55°
// either side, telophase at (xᵢ·0.5, ±0.62) in new nuclei of radius 0.32, homologs at ±0.06 in metaphase I, two cells at
// (0, ±0.9) of radius 0.72, meiosis II's spindles across each cell with poles at (±0.55, ±0.9), four cells at (±0.62,
// ±0.9) of radius 0.48 — with two choices of ours where the rules leave room or would overlap:
// - On a plate, xᵢ = (i − (k − 1)/2)·0.22 (0.16 in meiosis II) is the least spacing: chromosomes that lie along the plate
//   are longer than that (0.42 and 0.24), so they are spread to clear each other (by 0.08), as far as the cell allows;
//   each one's long arm points outward.
// - In prophase I the homologs pair so that the two non-sister chromatids that meet are 0.05 apart (centre to centre),
//   the tetrad's four chromatids all seen (as in metaphase I, where homologs at ±0.06 put them 0.053 apart).
// Plant cells divide inside their box: the daughters are its halves (and quarters), centred at (0, ±0.475).

/// <summary>How a step's counts are worded (the specification's tables word each step its own way).</summary>
public enum DivCountStyle
{
    /// <summary>One cell: "4", "8", "2n", "4c".</summary>
    Cell,
    /// <summary>Prophase I: "4 (as 2 tetrads)".</summary>
    Tetrads,
    /// <summary>Mitosis's anaphase: "8", "4n in one cell (two diploid sets moving apart)".</summary>
    Anaphase,
    /// <summary>Mitosis's telophase: "8 (in two nuclei)", "2n per nucleus", "4c total".</summary>
    TwoNuclei,
    /// <summary>After a division: "4 per cell".</summary>
    PerCell,
    /// <summary>Telophase I: "2 per cell", "n (haploid)".</summary>
    HaploidCells,
    /// <summary>Prophase II, metaphase II: per cell, said plainly ("2", "n").</summary>
    PerCellPlain,
    /// <summary>Anaphase I: "4 (2 to each pole)", "2n → two sets of n".</summary>
    AnaphaseI,
    /// <summary>Anaphase II: "4 (2 to each pole)", "n per pole" (per cell).</summary>
    AnaphaseII,
}

public enum DivAnchorKind { Point, Centromere, Arm, ShortArm, Nucleus, Nucleolus, Pole, Cell, Fibre, Polar, Plate, CellPlate, Furrow }

/// <summary>Where a label's leader line points, worked out from the frame as drawn: a point; a chromatid's centromere (by its
/// index, I); part way (T) along a chromatid's long arm (0 at the centromere, 1 at its end) or short arm; a nucleus's
/// envelope at an angle (T, radians); a nucleolus; a pole; a cell's outline at a sample (J); part way from a pole (0) to a
/// chromatid's kinetochore (1); a spindle's polar microtubules part way from its first pole; a spindle's metaphase plate
/// part way along it (−1…1); a cell plate or a furrow part way along it (−1…1).</summary>
public readonly record struct DivAnchor(DivAnchorKind Kind, int I = 0, double T = 0, int J = 0, DivPoint P = default)
{
    public static DivAnchor At(DivPoint p) => new(DivAnchorKind.Point, P: p);
    public static DivAnchor Centromere(int i) => new(DivAnchorKind.Centromere, i);
    public static DivAnchor Arm(int i, double t) => new(DivAnchorKind.Arm, i, t);
    public static DivAnchor ShortArm(int i, double t) => new(DivAnchorKind.ShortArm, i, t);
    public static DivAnchor Nucleus(int j, double angle) => new(DivAnchorKind.Nucleus, j, angle);
    public static DivAnchor Nucleolus(int j) => new(DivAnchorKind.Nucleolus, j);
    public static DivAnchor Pole(int j) => new(DivAnchorKind.Pole, j);
    public static DivAnchor Cell(int j, int sample) => new(DivAnchorKind.Cell, j, J: sample);
    public static DivAnchor Fibre(int i, double t) => new(DivAnchorKind.Fibre, i, t);
    public static DivAnchor Polar(int s, double t) => new(DivAnchorKind.Polar, s, t);
    public static DivAnchor Plate(int s, double t) => new(DivAnchorKind.Plate, s, t);
    public static DivAnchor CellPlate(int j, double t) => new(DivAnchorKind.CellPlate, j, t);
    public static DivAnchor Furrow(int j, double t) => new(DivAnchorKind.Furrow, j, t);
}

/// <summary>A label on the drawing: what it names (Part, for its card), its words, and where it points.</summary>
public sealed record DivLabelDef(string Part, string Text, DivAnchor Anchor)
{
    public string Id => Part + ":" + Text;
}

/// <summary>A badge in a cell after a division ("2n", "n", "n+1").</summary>
public sealed record DivBadgeDef(int Cell, DivPoint At);

/// <summary>One step's keyframe, its labels and how its counts are worded.</summary>
public sealed class DivisionKeyframe
{
    public required DivisionFrame Frame;
    public required List<DivLabelDef> Labels;
    public List<DivBadgeDef> Badges = new();
    public required DivCountStyle Style;
    /// <summary>Homologs paired as tetrads (prophase I, metaphase I).</summary>
    public bool Paired;
}

/// <summary>The steps of each process, in order (the Mac's raw values: "g1", "prophase1" …).</summary>
public enum DivStageKey { G1, S, G2, Prophase, Prometaphase, Metaphase, Anaphase, Telophase, Cytokinesis, Prophase1, Metaphase1, Anaphase1, Telophase1, Prophase2, Metaphase2, Anaphase2, Telophase2 }

public static class DivStages
{
    public static readonly DivStageKey[] Mitosis =
        { DivStageKey.G1, DivStageKey.S, DivStageKey.G2, DivStageKey.Prophase, DivStageKey.Prometaphase, DivStageKey.Metaphase, DivStageKey.Anaphase, DivStageKey.Telophase, DivStageKey.Cytokinesis };

    public static readonly DivStageKey[] Meiosis =
        { DivStageKey.G1, DivStageKey.S, DivStageKey.G2, DivStageKey.Prophase1, DivStageKey.Metaphase1, DivStageKey.Anaphase1, DivStageKey.Telophase1, DivStageKey.Prophase2, DivStageKey.Metaphase2, DivStageKey.Anaphase2, DivStageKey.Telophase2 };

    /// <summary>The Mac's name for a step ("g1", "prophase1"): the self-check's lines and a launch's step.</summary>
    public static string Raw(this DivStageKey k)
    {
        var s = k.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }
}

/// <summary>Meiosis II's rule for a chromosome's chromatid: its cell, its place in the plate, its pair, which sister, and its
/// place (along the plate, relative to the cell's centre, and its long arm's direction).</summary>
public delegate void DivPlace(int cell, int j, int pair, int sister, (double Y, double A2) place, ref DivChromatidKey key);

public sealed class DivisionBuilder
{
    public readonly DivisionProcess Process;
    private readonly DivisionOptions o;

    private bool Meiosis => Process == DivisionProcess.Meiosis;
    private int N => o.Pairs;
    /// <summary>The daughters' centres after the first division, (0, ±D); meiosis II's four cells at (±Q, ±D).</summary>
    private double D => o.Plant ? 0.475 : 0.9;
    private double Q => o.Plant ? 0.475 : 0.62;
    /// <summary>Room along a plate across the whole cell, and across a daughter cell in meiosis II.</summary>
    private double Room => o.Plant ? 1.76 : 1.84;
    private double Room2 => o.Plant ? 0.84 : 1.3;
    /// <summary>Pole slots: mitosis 0 top, 1 bottom; meiosis 0 top (meiosis II: top cell's left), 1 top cell's right, 2
    /// bottom (bottom cell's left), 3 bottom cell's right.</summary>
    private const int Top = 0;
    private int Bottom => Meiosis ? 2 : 1;

    /// <summary>Each chromosome's place in interphase: its centromere and its long arm's direction (seeded, inside the
    /// nucleus, apart from the others), the same every launch and on every platform.</summary>
    private readonly List<(DivPoint C, double Phi)> layout;
    /// <summary>Prophase I: a seeded turn for each tetrad (0…2π).</summary>
    private readonly double[] tetradAngles;
    /// <summary>Prophase II: a seeded turn for each chromosome (0…2π).</summary>
    private readonly double[] prophase2;

    public DivisionBuilder(DivisionProcess process, DivisionOptions options)
    {
        Process = process;
        o = options;
        layout = InterphaseLayout(o.Pairs);
        var rng = new BioRandom(0xD1_7E7A_D000UL + (ulong)o.Pairs);
        tetradAngles = Enumerable.Range(0, o.Pairs).Select(_ => 2 * Math.PI * rng.Uniform()).ToArray();
        var r2 = new BioRandom(0xD1_0002_0000UL + (ulong)o.Pairs);
        prophase2 = Enumerable.Range(0, 2 * o.Pairs).Select(_ => 2 * Math.PI * r2.Uniform()).ToArray();
    }

    /// <summary>Seeded places for the 2n interphase threads: centromeres within 0.34 of the centre, both ends within 0.47
    /// (the nucleus is 0.55), centromeres at least 0.14 apart.</summary>
    public static List<(DivPoint C, double Phi)> InterphaseLayout(int pairs)
    {
        var rng = new BioRandom(0xC311_D1F1_0000UL + (ulong)pairs);
        var placed = new List<(DivPoint C, double Phi)>();
        for (var u = 0; u < 2 * pairs; u++)
        {
            var pair = DivGeometry.Pairs[u / 2];
            var span = DivGeometry.Span(pair, 0);
            double l1 = span * pair.Centromere, l2 = span - l1;
            (DivPoint, double)? chosen = null;
            for (var attempt = 0; attempt < 500; attempt++)
            {
                double r = 0.34 * Math.Sqrt(rng.Uniform()), a = 2 * Math.PI * rng.Uniform(), phi = 2 * Math.PI * rng.Uniform();
                var c = new DivPoint(r * Math.Cos(a), r * Math.Sin(a));
                DivPoint e1 = c + DivPoint.Dir(phi + Math.PI) * l1, e2 = c + DivPoint.Dir(phi) * l2;
                if (e1.Length > 0.47 || e2.Length > 0.47 || placed.Any(p => p.C.Distance(c) < 0.14)) continue;
                chosen = (c, phi);
                break;
            }
            placed.Add(chosen ?? (DivPoint.Zero, 0));
        }
        return placed;
    }

    // ---- steps ----

    public DivStageKey[] Stages => Meiosis ? DivStages.Meiosis : DivStages.Mitosis;

    public List<DivisionKeyframe> Keyframes() => Stages.Select(Keyframe).ToList();

    public DivisionKeyframe Keyframe(DivStageKey s) => s switch
    {
        DivStageKey.G1 or DivStageKey.S or DivStageKey.G2 => Interphase(s),
        DivStageKey.Prophase => ProphaseFrame(),
        DivStageKey.Prometaphase => Prometaphase(),
        DivStageKey.Metaphase => Metaphase(),
        DivStageKey.Anaphase => Anaphase(),
        DivStageKey.Telophase => Telophase(),
        DivStageKey.Cytokinesis => Cytokinesis(),
        DivStageKey.Prophase1 => Prophase1(),
        DivStageKey.Metaphase1 => Metaphase1(),
        DivStageKey.Anaphase1 => Anaphase1(),
        DivStageKey.Telophase1 => Telophase1(),
        DivStageKey.Prophase2 => Prophase2Frame(),
        DivStageKey.Metaphase2 => Metaphase2(),
        DivStageKey.Anaphase2 => Anaphase2(),
        _ => Telophase2(),
    };

    // ---- parts ----

    /// <summary>The chromatids, in index order, unplaced.</summary>
    private DivChromatidKey[] Blank() =>
        Enumerable.Range(0, 4 * N).Select(i =>
        {
            var k = new DivChromatidKey(DivChromatidID.At(i));
            k.Seed = k.Id.Chromosome * 1.7 + 0.3;
            return k;
        }).ToArray();

    private static int Index(int pair, int homolog, int sister) => (pair * 2 + homolog) * 2 + sister;

    private static readonly (int X, int Y)[] Quarters = { (-1, 1), (1, 1), (-1, -1), (1, -1) };

    /// <summary>The whole cell (before it divides), as its parts that become daughters.</summary>
    private DivCellKey[] WholeCells(double? pinch = null)
    {
        DivShape shape;
        if (o.Plant) shape = DivShape.Box(DivPoint.Zero, 0.95, 0.95, 0.14);
        else if (pinch is { } w) shape = DivShape.Pinched(1, w);
        else shape = DivShape.Disk(DivPoint.Zero, 1);
        // (meiosis: an animal cell's halves each traced by two slots, each drawing its own side, until meiosis II parts
        //  them; a plant cell, which does not move as it divides, in quarters whose new walls appear)
        if (Meiosis)
            return Quarters.Select(q => o.Plant
                ? DivCellKey.For(new DivRegion(shape, q.X, q.Y))
                : DivCellKey.For(new DivRegion(shape, KeepY: q.Y), q.X)).ToArray();
        return new[] { 1, -1 }.Select(s => DivCellKey.For(new DivRegion(shape, KeepY: s))).ToArray();
    }

    /// <summary>Two daughter cells (top, bottom): mitosis's after cytokinesis, meiosis's after telophase I.</summary>
    private DivCellKey[] TwoCells()
    {
        var box = DivShape.Box(DivPoint.Zero, 0.95, 0.95, 0.14);
        if (Meiosis)
            return Quarters.Select(s => o.Plant
                ? DivCellKey.For(new DivRegion(box, s.X, s.Y, WallY: true))
                : DivCellKey.For(new DivRegion(DivShape.Disk(new DivPoint(0, s.Y * D), 0.72)), s.X)).ToArray();
        return new[] { 1, -1 }.Select(s => o.Plant
            ? DivCellKey.For(new DivRegion(box, KeepY: s, WallY: true))
            : DivCellKey.For(new DivRegion(DivShape.Disk(new DivPoint(0, s * D), 0.72)))).ToArray();
    }

    /// <summary>Meiosis's four cells after telophase II.</summary>
    private DivCellKey[] FourCells() =>
        Quarters.Select(s => o.Plant
            ? DivCellKey.For(new DivRegion(DivShape.Box(DivPoint.Zero, 0.95, 0.95, 0.14), s.X, s.Y, WallX: true, WallY: true))
            : DivCellKey.For(new DivRegion(DivShape.Disk(new DivPoint(s.X * Q, s.Y * D), 0.48)))).ToArray();

    private int Slots => Meiosis ? 4 : 2;

    /// <summary>The interphase nucleus (the other slots hidden in it).</summary>
    private DivNucleusKey[] OneNucleus(double alpha = 1, double dashed = 0, double nucleolus = 0.12, double nucleolusAlpha = 1, double confine = 1) =>
        Enumerable.Range(0, Slots).Select(i => new DivNucleusKey(DivPoint.Zero, 0.55, i == 0 ? alpha : 0, dashed, nucleolus,
            i == 0 ? nucleolusAlpha : 0, i == 0 ? confine : 0)).ToArray();

    /// <summary>Nuclei at given places (each slot its own), all with the same look.</summary>
    private static DivNucleusKey[] NucleiAt(DivPoint[] centres, double r, double alpha, double dashed = 0, double nucleolus = 0, double nucleolusAlpha = 0, double confine = 0) =>
        centres.Select(c => new DivNucleusKey(c, r, alpha, dashed, nucleolus, nucleolusAlpha, confine)).ToArray();

    /// <summary>Poles: animal cells draw a centrosome at each (a plant's poles have none).</summary>
    private DivPoleKey PoleAt(DivPoint p, DivPoint? pivot = null, double centrosome = 1, double aster = 0) =>
        new(p, pivot ?? DivPoint.Zero, o.Plant ? 0 : centrosome, o.Plant ? 0 : aster);

    /// <summary>The poles before meiosis II: the top and bottom slots placed, their partners hidden with them.</summary>
    private DivPoleKey[] Poles(DivPoleKey top, DivPoleKey bottom)
    {
        if (!Meiosis) return new[] { top, bottom };
        return new[] { top, top with { Centrosome = 0, Aster = 0 }, bottom, bottom with { Centrosome = 0, Aster = 0 } };
    }

    private DivSpindleKey[] Spindles(double polar = 0, double plate = 0, double polar2 = 0, double plate2 = 0)
    {
        if (Meiosis)
        {
            var l2 = o.Plant ? 0.78 : 1.1;
            return new[] { new DivSpindleKey(0, 2, polar, plate, 1.8), new DivSpindleKey(0, 1, polar2, plate2, l2), new DivSpindleKey(2, 3, polar2, plate2, l2) };
        }
        return new[] { new DivSpindleKey(0, 1, polar, plate, 1.8) };
    }

    /// <summary>Plant cell plates: the first division's along y = 0; meiosis II's along x = 0 in each cell.</summary>
    private DivLineKey[] CellPlates(double first = 0, double firstAlpha = 0, double second = 0, double secondAlpha = 0)
    {
        if (!o.Plant) return Array.Empty<DivLineKey>();
        var list = new List<DivLineKey> { new(DivPoint.Zero, true, first, firstAlpha) };
        if (Meiosis)
        {
            list.Add(new DivLineKey(new DivPoint(0, D), false, second, secondAlpha));
            list.Add(new DivLineKey(new DivPoint(0, -D), false, second, secondAlpha));
        }
        return list.ToArray();
    }

    /// <summary>The animal cleavage furrow's ring (mitosis's telophase).</summary>
    private DivLineKey[] Furrows(double half = 0, double alpha = 0)
    {
        if (o.Plant || Meiosis) return Array.Empty<DivLineKey>();
        return new[] { new DivLineKey(DivPoint.Zero, true, half, alpha) };
    }

    private DivisionFrame Frame(DivCellKey[] cells, DivNucleusKey[] nuclei, DivPoleKey[] poles, DivSpindleKey[] spindles,
        DivLineKey[] cellPlates, DivLineKey[] furrows, DivChromatidKey[] chromatids)
    {
        var b = cells.Aggregate(DivRect.Empty, (acc, c) => acc.Union(c.Bounds));
        return new DivisionFrame { Plant = o.Plant, Cells = cells, Nuclei = nuclei, Poles = poles, Spindles = spindles, CellPlates = cellPlates, Furrows = furrows, Chromatids = chromatids, Bounds = b };
    }

    // ---- plates ----

    /// <summary>Places along a plate, centred on 0, for chromosomes reaching <c>Back</c> and <c>Forward</c> along it from
    /// their centromeres: the specification's spacing at least, spread to clear each other by <paramref name="gap"/> as far
    /// as <paramref name="room"/> allows (closer, down to the specification's spacing, when the room runs short).</summary>
    public static double[] Plate(IReadOnlyList<(double Back, double Forward)> extents, double spacing, double room, double gap = 0.08)
    {
        if (extents.Count == 0) return Array.Empty<double>();
        var k = extents.Count;
        var extra = new List<double>();
        for (var i = 0; i < k - 1; i++) extra.Add(Math.Max(0, extents[i].Forward + extents[i + 1].Back + gap - spacing));
        var fixedRoom = extents[0].Back + extents[k - 1].Forward + (k - 1) * spacing;
        var wanted = extra.Sum();
        var lambda = wanted > 0 ? Math.Min(1, Math.Max(0, (room - fixedRoom) / wanted)) : 1;
        var xs = new List<double> { 0 };
        for (var i = 0; i < k - 1; i++) xs.Add(xs[i] + spacing + extra[i] * lambda);
        double lo = xs[0] - extents[0].Back, hi = xs[k - 1] + extents[k - 1].Forward;
        var shift = -(lo + hi) / 2;
        return xs.Select(x => x + shift).ToArray();
    }

    /// <summary>A chromosome's reach either side of its centromere, condensed, when its long arm points forward or back.</summary>
    public static (double Back, double Forward) Extent(int pair, bool longForward)
    {
        var info = DivGeometry.Pairs[pair];
        var l = DivGeometry.Span(info, 1);
        double l1 = l * info.Centromere, l2 = l - l1;
        return longForward ? (l1, l2) : (l2, l1);
    }

    /// <summary>Mitosis's metaphase plate: the chromosomes in order (long, …, long; mother's and father's alternating), each
    /// one's centromere x and its long arm's direction (outward).</summary>
    private Dictionary<int, (double X, double A2)> MitosisPlate
    {
        get
        {
            int[] order = N == 3 ? new[] { 0, 5, 2, 3, 4, 1 } : new[] { 0, 3, 2, 1 }; // (chromosome = pair·2 + homolog)
            var k = order.Length;
            var outward = Enumerable.Range(0, k).Select(i => i >= k / 2).ToArray();
            var ext = Enumerable.Range(0, k).Select(i => Extent(order[i] / 2, outward[i])).ToList();
            var xs = Plate(ext, 0.22, Room);
            var map = new Dictionary<int, (double, double)>();
            for (var i = 0; i < k; i++) map[order[i]] = (xs[i], outward[i] ? 0 : Math.PI);
            return map;
        }
    }

    /// <summary>Metaphase I's plate: a tetrad per pair, in pair order, long arms outward.</summary>
    private Dictionary<int, (double X, double A2)> MeiosisPlate
    {
        get
        {
            var k = N;
            var outward = Enumerable.Range(0, k).Select(i => i >= k / 2).ToArray();
            var ext = Enumerable.Range(0, k).Select(i => Extent(i, outward[i])).ToList();
            var xs = Plate(ext, 0.22, Room);
            var map = new Dictionary<int, (double, double)>();
            for (var i = 0; i < k; i++) map[i] = (xs[i], outward[i] ? 0 : Math.PI);
            return map;
        }
    }

    /// <summary>The homolog of a pair that faces the top pole in metaphase I (the mother's, unless the pair is flipped).</summary>
    private int Upper(int p) => p < o.Flipped.Length && o.Flipped[p] ? 1 : 0;

    /// <summary>The V of a chromatid pulled toward a direction: its arms trail 55° either side of straight back, each arm
    /// taking the side nearer where it pointed.</summary>
    public static (double A1, double A2) VArms(double motion, double from, double spread = 55)
    {
        double c1 = motion + Math.PI + DivMath.Radians(spread), c2 = motion + Math.PI - DivMath.Radians(spread);
        return Math.Abs(DivMath.Turn(from, c1)) <= Math.Abs(DivMath.Turn(from, c2)) ? (c2, c1) : (c1, c2);
    }

    // ---- interphase (mitosis and meiosis alike) ----

    private DivisionKeyframe Interphase(DivStageKey s)
    {
        var k = s == DivStageKey.G2 ? 0.1 : 0;
        var ks = Blank();
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var l = layout[id.Chromosome];
            ks[i].C = l.C;
            ks[i].A2 = l.Phi;
            ks[i].A1 = l.Phi + Math.PI;
            ks[i].K = k;
            ks[i].Nucleus = 0;
            if (s == DivStageKey.G1)
            {
                if (id.Sister == 1)
                {
                    ks[i].Exists = false;
                    ks[i].Alpha = 0;
                    ks[i].Grow = 0;
                }
            }
            else
            {
                ks[i].Offset = (id.Sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(k);
                ks[i].Joined = true;
            }
        }
        var ps = s == DivStageKey.G2
            ? Poles(PoleAt(new DivPoint(0.70, 0.55), aster: 0.3), PoleAt(new DivPoint(0.62, 0.62), aster: 0.3))
            : Poles(PoleAt(new DivPoint(0.62, 0.62), aster: 0.25), PoleAt(new DivPoint(0.62, 0.62), centrosome: 0));
        var f = Frame(WholeCells(), OneNucleus(), ps, Spindles(), CellPlates(), Furrows(), ks);
        int l0 = Index(0, 0, 0), s0 = Index(1, 1, 0);
        var labels = new List<DivLabelDef>
        {
            new("nucleus", "Nucleus", DivAnchor.Nucleus(0, DivMath.Radians(200))),
            new("envelope", "Nuclear envelope", DivAnchor.Nucleus(0, DivMath.Radians(-35))),
            new("nucleolus", "Nucleolus", DivAnchor.Nucleolus(0)),
        };
        labels.Add(s switch
        {
            DivStageKey.G1 => new DivLabelDef("chromatin", "Chromosomes (chromatin)", DivAnchor.Arm(l0, 0.8)),
            DivStageKey.S => new DivLabelDef("replication", "DNA copied (S phase)", DivAnchor.Arm(l0, 0.8)),
            _ => new DivLabelDef("sisters", "Sister chromatids", DivAnchor.Arm(l0, 0.8)),
        });
        labels.Add(new DivLabelDef("homologs", "Homolog (other parent)", DivAnchor.Arm(Index(0, 1, 0), 0.7)));
        labels.Add(new DivLabelDef("chromatin", "Short chromosome", DivAnchor.Arm(s0, 0.9)));
        if (!o.Plant) labels.Add(new DivLabelDef("centrosome", s == DivStageKey.G2 ? "2 centrosomes + centrioles" : "Centrosome + centrioles", DivAnchor.Pole(Top)));
        labels.Add(o.Plant ? new DivLabelDef("wall", "Cell wall", DivAnchor.Cell(0, 112)) : new DivLabelDef("membrane", "Cell membrane", DivAnchor.Cell(0, 100)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Cell };
    }

    /// <summary>The chromatids condensing in place (mitosis's prophase).</summary>
    private DivChromatidKey[] Condensed(double k)
    {
        var ks = Blank();
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var l = layout[id.Chromosome];
            ks[i].C = l.C;
            ks[i].A2 = l.Phi;
            ks[i].A1 = l.Phi + Math.PI;
            ks[i].K = k;
            ks[i].Offset = (id.Sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(k);
            ks[i].Joined = true;
            ks[i].Nucleus = 0;
        }
        return ks;
    }

    // ---- mitosis ----

    private DivisionKeyframe ProphaseFrame()
    {
        var ks = Condensed(0.7);
        var ps = Poles(PoleAt(new DivPoint(0.5, 0.72), aster: 0.8), PoleAt(new DivPoint(-0.5, 0.72), aster: 0.8));
        var f = Frame(WholeCells(), OneNucleus(nucleolus: 0.07, nucleolusAlpha: 0.2), ps, Spindles(polar: 0.5), CellPlates(), Furrows(), ks);
        var l0 = Index(0, 0, 0);
        var labels = new List<DivLabelDef>
        {
            new("chromosome", "Chromosome (condensing)", DivAnchor.Arm(Index(1, 0, 0), 0.9)),
            new("sisters", "Sister chromatids", DivAnchor.Arm(l0, 0.85)),
            new("centromere", "Centromere", DivAnchor.Centromere(Index(0, 1, 0))),
            new("envelope", "Nuclear envelope", DivAnchor.Nucleus(0, DivMath.Radians(-40))),
            new("spindle", "Spindle (forming)", DivAnchor.Polar(0, 0.5)),
        };
        if (!o.Plant) labels.Add(new DivLabelDef("centrosome", "Centrosomes moving apart", DivAnchor.Pole(Bottom)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Cell };
    }

    /// <summary>Which pole each chromatid faces from metaphase on: the sister whose side is up goes to the top pole.</summary>
    private int MitosisPole(DivChromatidID id, double a2)
    {
        var up = Math.Cos(a2) > 0 ? 0 : 1;
        return id.Sister == up ? Top : Bottom;
    }

    private DivisionKeyframe Prometaphase()
    {
        var plate = MitosisPlate;
        var ks = Condensed(1);
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var l = layout[id.Chromosome];
            var target = plate[id.Chromosome].A2;
            // (on the way to the plate: half way across, the spindle's middle closer)
            ks[i].C = new DivPoint(DivMath.Mix(l.C.X, plate[id.Chromosome].X, 0.5), l.C.Y * 0.55);
            ks[i].A2 = DivMath.MixAngle(l.Phi, target, 0.5);
            ks[i].A1 = ks[i].A2 + Math.PI;
            ks[i].Pole = MitosisPole(id, target);
            ks[i].Fibre = 0.8;
            ks[i].Nucleus = -1;
        }
        var ps = Poles(PoleAt(new DivPoint(0, 0.82), aster: 1), PoleAt(new DivPoint(0, -0.82), aster: 1));
        var f = Frame(WholeCells(), OneNucleus(alpha: 0.5, dashed: 1, nucleolus: 0.04, nucleolusAlpha: 0, confine: 0), ps, Spindles(polar: 1), CellPlates(), Furrows(), ks);
        var l0 = Index(0, 0, 0);
        var labels = new List<DivLabelDef>
        {
            new("envelope", "Envelope breaking down", DivAnchor.Nucleus(0, DivMath.Radians(-30))),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(l0, 0.5)),
            new("centromere", "Centromere (kinetochore)", DivAnchor.Centromere(Index(1, 0, 0))),
            new("chromosome", "Chromosome", DivAnchor.Arm(Index(0, 1, 0), 0.9)),
            o.Plant ? new("pole", "Pole (no centrioles)", DivAnchor.Pole(Top)) : new("centrosome", "Centrosome (spindle pole)", DivAnchor.Pole(Top)),
        };
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Cell };
    }

    private DivisionKeyframe Metaphase()
    {
        var plate = MitosisPlate;
        var ks = Condensed(1);
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var p = plate[id.Chromosome];
            ks[i].C = new DivPoint(p.X, 0);
            ks[i].A2 = p.A2;
            ks[i].A1 = p.A2 + Math.PI;
            ks[i].Pole = MitosisPole(id, p.A2);
            ks[i].Fibre = 1;
            ks[i].Nucleus = -1;
        }
        var ps = Poles(PoleAt(new DivPoint(0, 0.82), aster: 1), PoleAt(new DivPoint(0, -0.82), aster: 1));
        var f = Frame(WholeCells(), OneNucleus(alpha: 0, dashed: 1, nucleolus: 0, nucleolusAlpha: 0, confine: 0), ps, Spindles(polar: 1, plate: 1), CellPlates(), Furrows(), ks);
        var l0 = Index(0, 0, 0);
        var labels = new List<DivLabelDef>
        {
            new("plate", "Metaphase plate", DivAnchor.Plate(0, -1)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(1, 1, 0), 0.45)),
            new("spindle", "Spindle", DivAnchor.Polar(0, 0.3)),
            new("sisters", "Sister chromatids", DivAnchor.Arm(l0, 0.8)),
            new("centromere", "Centromere", DivAnchor.Centromere(Index(0, 1, 0))),
            o.Plant ? new("pole", "Spindle pole", DivAnchor.Pole(Top)) : new("centrosome", "Centrosome", DivAnchor.Pole(Top)),
        };
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Cell };
    }

    /// <summary>Mitosis's anaphase (f 0.6) and telophase (0.5): each chromatid's centromere at (xᵢ·f, ±0.62), a V — 55°
    /// either side in anaphase, closing to 25° as it gathers into its new nucleus in telophase.</summary>
    private DivChromatidKey[] Separated(double f, double k, double spread = 55)
    {
        var plate = MitosisPlate;
        var ks = Blank();
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var p = plate[id.Chromosome];
            var pole = MitosisPole(id, p.A2);
            var up = pole == Top;
            var arms = VArms(up ? Math.PI / 2 : -Math.PI / 2, p.A2, spread);
            ks[i].C = new DivPoint(p.X * f, up ? 0.62 : -0.62);
            ks[i].A1 = arms.A1;
            ks[i].A2 = arms.A2;
            ks[i].K = k;
            ks[i].Pole = pole;
            ks[i].Part = up ? 0 : 1;
        }
        return ks;
    }

    private DivisionKeyframe Anaphase()
    {
        var ks = Separated(0.6, 1);
        for (var i = 0; i < ks.Length; i++) ks[i].Fibre = 1;
        var ps = Poles(PoleAt(new DivPoint(0, 0.82), aster: 1), PoleAt(new DivPoint(0, -0.82), aster: 1));
        var ns = NucleiAt(new[] { new DivPoint(0, 0.62), new DivPoint(0, -0.62) }, 0.32, 0);
        var f = Frame(WholeCells(), ns, ps, Spindles(polar: 1), CellPlates(), Furrows(), ks);
        var l0 = Index(0, 0, 0);
        var labels = new List<DivLabelDef>
        {
            new("daughterChromosome", "Daughter chromosome", DivAnchor.Arm(l0, 0.85)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(1, 0, 0), 0.5)),
            new("polar", "Polar microtubules", DivAnchor.Polar(0, 0.55)),
            o.Plant ? new("pole", "Spindle pole", DivAnchor.Pole(Top)) : new("centrosome", "Centrosome", DivAnchor.Pole(Top)),
        };
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Anaphase };
    }

    private DivisionKeyframe Telophase()
    {
        var ks = Separated(0.5, 0.4, 25);
        for (var i = 0; i < ks.Length; i++) ks[i].Nucleus = ks[i].Part;
        // (the centrosomes beside the new nuclei, which now fill the poles' places)
        var ps = Poles(PoleAt(new DivPoint(0.38, 0.82), aster: 0.4), PoleAt(new DivPoint(0.38, -0.82), aster: 0.4));
        var ns = NucleiAt(new[] { new DivPoint(0, 0.62), new DivPoint(0, -0.62) }, 0.32, 1, nucleolus: 0.05, nucleolusAlpha: 0.7, confine: 1);
        var f = Frame(WholeCells(pinch: 0.1), ns, ps, Spindles(polar: 0.35), CellPlates(first: 0.55, firstAlpha: 1), Furrows(half: 0.1, alpha: 1), ks);
        var labels = new List<DivLabelDef>
        {
            new("envelope", "Envelope re-forming", DivAnchor.Nucleus(0, DivMath.Radians(150))),
            new("chromosome", "Chromosomes decondensing", DivAnchor.Nucleus(1, DivMath.Radians(-150))),
            o.Plant ? new("cellplate", "Cell plate", DivAnchor.CellPlate(0, 0.9)) : new("furrow", "Cleavage furrow", DivAnchor.Furrow(0, 1)),
            new("nucleus", "Two nuclei", DivAnchor.Nucleus(1, DivMath.Radians(-20))),
        };
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.TwoNuclei };
    }

    private DivisionKeyframe Cytokinesis()
    {
        var ks = Blank();
        var plate = MitosisPlate;
        var nr = o.Plant ? 0.3 : 0.32;
        for (var i = 0; i < ks.Length; i++)
        {
            var id = ks[i].Id;
            var p = plate[id.Chromosome];
            var up = MitosisPole(id, p.A2) == Top;
            var centre = new DivPoint(0, up ? D : -D);
            var l = layout[id.Chromosome];
            ks[i].C = centre + l.C * (nr / 0.55);
            ks[i].A2 = l.Phi;
            ks[i].A1 = l.Phi + Math.PI;
            ks[i].K = 0;
            ks[i].Cell = up ? 0 : 1;
            ks[i].Nucleus = up ? 0 : 1;
        }
        var ps = Poles(PoleAt(new DivPoint(0.38, D + 0.38), aster: 0.25), PoleAt(new DivPoint(0.38, -D - 0.38), aster: 0.25));
        var ns = NucleiAt(new[] { new DivPoint(0, D), new DivPoint(0, -D) }, nr, 1, nucleolus: 0.08, nucleolusAlpha: 1, confine: 1);
        var f = Frame(TwoCells(), ns, ps, Spindles(), CellPlates(first: 0.95, firstAlpha: 0.55), Furrows(), ks);
        var side = o.Plant ? 0.6 : 0.52;
        var labels = new List<DivLabelDef>
        {
            new("daughter", "Daughter cell (2n)", DivAnchor.Cell(0, 110)),
            new("daughter", "Identical daughter (2n)", DivAnchor.Cell(1, 40)),
            new("nucleus", "Nucleus", DivAnchor.Nucleus(1, DivMath.Radians(-160))),
            o.Plant ? new("cellplate", "New cell wall", DivAnchor.CellPlate(0, 0.85)) : new("furrow", "Membranes sealed", DivAnchor.Cell(0, 0)),
        };
        return new DivisionKeyframe
        {
            Frame = f, Labels = labels, Style = DivCountStyle.PerCell,
            Badges = new() { new DivBadgeDef(0, new DivPoint(side, D)), new DivBadgeDef(1, new DivPoint(side, -D)) },
        };
    }

    // ---- meiosis I ----

    /// <summary>A tetrad laid out: homolog A (Upper) on one side of the axis and B on the other, h from it; sisters either
    /// side of their homolog's axis; on the long pair, with crossing over, the two chromatids that meet cross beyond the
    /// chiasma.</summary>
    private void Tetrad(DivChromatidKey[] ks, int p, DivPoint centre, double a2, double k, double h, bool crossing)
    {
        int a = Upper(p), b = 1 - a;
        // (A on the side the normal points to when its long arm points +x; the other way round when it points −x, so A is
        //  the one facing the top pole either way)
        double sign = Math.Cos(a2) >= 0 ? 1 : -1;
        var nrm = new DivPoint(-Math.Sin(a2), Math.Cos(a2));
        var s = DivGeometry.SisterOffset(k);
        var inner = 2 * (h - s);
        foreach (var homolog in new[] { a, b })
        {
            var hs = (homolog == a ? 1.0 : -1.0) * sign;
            for (var sister = 0; sister <= 1; sister++)
            {
                var i = Index(p, homolog, sister);
                ks[i].C = centre + nrm * (hs * h);
                ks[i].A2 = a2;
                ks[i].A1 = a2 + Math.PI;
                ks[i].K = k;
                ks[i].Offset = (sister == 0 ? 1 : -1) * s;
                ks[i].Joined = true;
                // (the chromatid facing the other homolog: A's on its −hs side, B's likewise toward A)
                var facing = (sister == 0 ? 1.0 : -1.0) == -hs;
                if (crossing && p == 0 && facing)
                {
                    ks[i].Cross = -hs * inner;
                    ks[i].Recombined = 1;
                }
                else ks[i].Cross = 0;
            }
        }
    }

    private bool Crossing => o.CrossingOver;

    /// <summary>Where crossing over left its mark: the recombinant chromatids keep their swapped part after prophase I.</summary>
    private void KeepRecombined(DivChromatidKey[] ks)
    {
        if (!Crossing) return;
        for (var homolog = 0; homolog <= 1; homolog++) ks[Index(0, homolog, RecombinantSister(homolog))].Recombined = 1;
    }

    private DivisionKeyframe Prophase1()
    {
        var ks = Condensed(0.7);
        var plate = MeiosisPlate;
        for (var p = 0; p < N; p++)
        {
            // (the tetrads round the nucleus's middle, apart, each lying across its spoke and turned by a seed, then the
            //  short way to match how it will lie on the plate)
            var spoke = 0.6 + 2 * Math.PI * p / N;
            var centre = DivPoint.Dir(spoke) * (N == 2 ? 0.17 : 0.22);
            var a2 = spoke + Math.PI / 2 + (tetradAngles[p] - Math.PI) * 0.15;
            var target = plate[p].A2;
            if (Math.Abs(DivMath.Turn(a2, target)) > Math.PI / 2) a2 += Math.PI;
            var s = DivGeometry.SisterOffset(0.7);
            Tetrad(ks, p, centre, a2, 0.7, 0.025 + s, Crossing);
        }
        var ps = Poles(PoleAt(new DivPoint(0.5, 0.72), aster: 0.8), PoleAt(new DivPoint(-0.5, 0.72), aster: 0.8));
        var f = Frame(WholeCells(), OneNucleus(alpha: 0.6, dashed: 1, nucleolus: 0.05, nucleolusAlpha: 0, confine: 1), ps, Spindles(polar: 0.5), CellPlates(), Furrows(), ks);
        var a = Upper(0);
        var labels = new List<DivLabelDef>
        {
            new("tetrad", "Tetrad (bivalent)", DivAnchor.Arm(Index(1, Upper(1), 0), 0.9)),
            new("homologs", "Homologs paired (synapsis)", DivAnchor.ShortArm(Index(0, 1 - a, 0), 0.9)),
            new("sisters", "Sister chromatids", DivAnchor.Arm(Index(0, a, 0), 0.95)),
            new("envelope", "Envelope breaking down", DivAnchor.Nucleus(0, DivMath.Radians(-40))),
            new("spindle", "Spindle forming", DivAnchor.Polar(0, 0.5)),
        };
        if (Crossing) labels.Add(new DivLabelDef("chiasma", "Chiasma (crossing over)", DivAnchor.Arm(Index(0, a, 1), o.Chiasma)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Tetrads, Paired = true };
    }

    private DivisionKeyframe Metaphase1()
    {
        var ks = Condensed(1);
        var plate = MeiosisPlate;
        for (var p = 0; p < N; p++)
        {
            var pl = plate[p];
            Tetrad(ks, p, new DivPoint(pl.X, 0), pl.A2, 1, 0.06, Crossing);
            var a = Upper(p);
            for (var homolog = 0; homolog <= 1; homolog++)
                for (var sister = 0; sister <= 1; sister++)
                {
                    var i = Index(p, homolog, sister);
                    // (nondisjunction in meiosis I: the short pair's homologs both face the top pole)
                    var toTop = homolog == a || (o.Nondisjunction == DivisionNondisjunction.MeiosisI && p == 1);
                    ks[i].Pole = toTop ? Top : Bottom;
                    ks[i].Fibre = 1;
                    ks[i].Nucleus = -1;
                }
        }
        var ps = Poles(PoleAt(new DivPoint(0, 0.82), aster: 1), PoleAt(new DivPoint(0, -0.82), aster: 1));
        var f = Frame(WholeCells(), OneNucleus(alpha: 0, dashed: 1, nucleolus: 0, nucleolusAlpha: 0, confine: 0), ps, Spindles(polar: 1, plate: 1), CellPlates(), Furrows(), ks);
        var a0 = Upper(0);
        var labels = new List<DivLabelDef>
        {
            new("tetrad", "Homologous pair (tetrad)", DivAnchor.Arm(Index(N - 1, Upper(N - 1), 0), 0.9)),
            new("plate", "Metaphase plate", DivAnchor.Plate(0, -1)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(0, 1 - a0, 1), 0.45)),
            o.Plant ? new("pole", "Spindle pole", DivAnchor.Pole(Top)) : new("centrosome", "Centrosome", DivAnchor.Pole(Top)),
        };
        if (Crossing) labels.Add(new DivLabelDef("chiasma", "Chiasma", DivAnchor.Arm(Index(0, a0, 1), o.Chiasma)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.Cell, Paired = true };
    }

    /// <summary>Anaphase I (f 0.6) and telophase I: each homolog, both chromatids together, at (xᵢ·f, ±y), a V; with
    /// nondisjunction in meiosis I the short pair's homologs both go up, side by side.</summary>
    private DivChromatidKey[] HomologsApart(double f, double y, double k)
    {
        var ks = Blank();
        var plate = MeiosisPlate;
        for (var p = 0; p < N; p++)
        {
            var pl = plate[p];
            var a = Upper(p);
            for (var homolog = 0; homolog <= 1; homolog++)
            {
                var ndUp = o.Nondisjunction == DivisionNondisjunction.MeiosisI && p == 1;
                var up = homolog == a || ndUp;
                var dx = ndUp ? (homolog == a ? -0.075 : 0.075) : 0;
                var arms = VArms(up ? Math.PI / 2 : -Math.PI / 2, pl.A2);
                for (var sister = 0; sister <= 1; sister++)
                {
                    var i = Index(p, homolog, sister);
                    ks[i].C = new DivPoint(pl.X * f + dx, up ? y : -y);
                    ks[i].A1 = arms.A1;
                    ks[i].A2 = arms.A2;
                    ks[i].K = k;
                    ks[i].Offset = (sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(k);
                    ks[i].Joined = true;
                    ks[i].Pole = up ? Top : Bottom;
                    ks[i].Part = up ? 0 : 1;
                }
            }
        }
        KeepRecombined(ks);
        return ks;
    }

    private DivisionKeyframe Anaphase1()
    {
        var ks = HomologsApart(0.6, 0.62, 1);
        for (var i = 0; i < ks.Length; i++) ks[i].Fibre = 1;
        var ps = Poles(PoleAt(new DivPoint(0, 0.82), aster: 1), PoleAt(new DivPoint(0, -0.82), aster: 1));
        var ns = Enumerable.Range(0, 4).Select(i => new DivNucleusKey(new DivPoint(0, i < 2 ? D : -D), o.Plant ? 0.36 : 0.4, 0, 1)).ToArray();
        var f = Frame(WholeCells(), ns, ps, Spindles(polar: 1), CellPlates(), Furrows(), ks);
        var a = Upper(0);
        var labels = new List<DivLabelDef>
        {
            new("homologs", "Homologs separating", DivAnchor.Arm(Index(0, 1 - a, 0), 0.9)),
            new("sisters", "Sisters stay together", DivAnchor.Arm(Index(N - 1, Upper(N - 1), 0), 0.9)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(0, a, 0), 0.5)),
        };
        if (Crossing) labels.Add(new DivLabelDef("recombinant", "Recombinant chromatid", DivAnchor.Arm(Index(0, a, RecombinantSister(a)), 0.95)));
        if (o.Nondisjunction == DivisionNondisjunction.MeiosisI)
            labels.Add(new DivLabelDef("nondisjunction", "Nondisjunction", DivAnchor.Arm(Index(1, 1 - Upper(1), 0), 0.9)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.AnaphaseI };
    }

    /// <summary>The sister of a homolog of the long pair that crossed over: the one facing the other homolog in the tetrad
    /// (as Tetrad lays it out, the same in prophase I and on the plate).</summary>
    private int RecombinantSister(int homolog)
    {
        var hs = (homolog == Upper(0) ? 1.0 : -1.0) * (Math.Cos(MeiosisPlate[0].A2) >= 0 ? 1 : -1);
        return -hs > 0 ? 0 : 1;
    }

    private DivisionKeyframe Telophase1()
    {
        var ks = HomologsApart(0.55, D + 0.08, 0.85);
        for (var i = 0; i < ks.Length; i++)
        {
            var up = ks[i].Part == 0;
            ks[i].Cell = up ? 0 : 2;
            ks[i].Part = -1;
            ks[i].Pole = -1;
        }
        var ps = Poles(PoleAt(new DivPoint(0, o.Plant ? D + 0.3 : D + 0.46), aster: 0.25), PoleAt(new DivPoint(0, o.Plant ? -D - 0.3 : -D - 0.46), aster: 0.25));
        var r = o.Plant ? 0.36 : 0.4;
        var ns = Enumerable.Range(0, 4).Select(i => new DivNucleusKey(new DivPoint(0, i < 2 ? D : -D), r, i % 2 == 0 ? 0.55 : 0, 1)).ToArray();
        var f = Frame(TwoCells(), ns, ps, Spindles(), CellPlates(first: 0.95, firstAlpha: 0.75), Furrows(), ks);
        var side = o.Plant ? 0.6 : 0.52;
        var labels = new List<DivLabelDef>
        {
            new("haploid", "Haploid cell (n)", DivAnchor.Cell(0, 100)),
            new("sisters", "Still two chromatids each", DivAnchor.Arm(Index(0, Upper(0), 0), 0.9)),
            new("envelope", "Nuclear envelope (brief)", DivAnchor.Nucleus(2, DivMath.Radians(-150))),
            o.Plant ? new("cellplate", "Cell plate → new wall", DivAnchor.CellPlate(0, 0.85)) : new("furrow", "Cleavage furrow (closed)", DivAnchor.Cell(1, 0)),
        };
        return new DivisionKeyframe
        {
            Frame = f, Labels = labels, Style = DivCountStyle.HaploidCells,
            Badges = new() { new DivBadgeDef(0, new DivPoint(side, D)), new DivBadgeDef(2, new DivPoint(side, -D)) },
        };
    }

    // ---- meiosis II ----

    /// <summary>The chromosomes each cell has after meiosis I, in plate order: (pair, homolog), long arm down for the first
    /// half, up for the rest.</summary>
    private List<(int Cell, List<(int Pair, int Homolog)> Chromosomes)> SecondDivisionContents()
    {
        var topCell = new List<(int, int)>();
        var bottomCell = new List<(int, int)>();
        for (var p = 0; p < N; p++)
        {
            var a = Upper(p);
            if (o.Nondisjunction == DivisionNondisjunction.MeiosisI && p == 1)
            {
                topCell.Add((p, a));
                topCell.Add((p, 1 - a));
            }
            else
            {
                topCell.Add((p, a));
                bottomCell.Add((p, 1 - a));
            }
        }
        return new() { (0, topCell), (2, bottomCell) };
    }

    /// <summary>Meiosis II's plate in a cell: each chromosome's place along it (relative to the cell's centre) and its long
    /// arm's direction (down for the first half, up for the rest).</summary>
    private List<(double Y, double A2)> SecondPlate(List<(int Pair, int Homolog)> list)
    {
        var k = list.Count;
        var outward = Enumerable.Range(0, k).Select(i => i >= k / 2).ToArray();
        var ext = Enumerable.Range(0, k).Select(i => Extent(list[i].Pair, outward[i])).ToList();
        var ys = Plate(ext, 0.16, Room2);
        return Enumerable.Range(0, k).Select(i => (ys[i], outward[i] ? Math.PI / 2 : -Math.PI / 2)).ToList();
    }

    /// <summary>The left and right pole slots of a cell (0 top, 2 bottom).</summary>
    private static (int Left, int Right) SidePoles(int cell) => cell == 0 ? (0, 1) : (2, 3);

    /// <summary>Meiosis II's chromatids placed by a rule for each chromosome.</summary>
    private DivChromatidKey[] SecondDivision(DivPlace place)
    {
        var ks = Blank();
        foreach (var (cell, list) in SecondDivisionContents())
        {
            var plate = SecondPlate(list);
            for (var j = 0; j < list.Count; j++)
            {
                var ch = list[j];
                for (var sister = 0; sister <= 1; sister++)
                {
                    var i = Index(ch.Pair, ch.Homolog, sister);
                    ks[i].Cell = cell;
                    ks[i].Joined = true;
                    place(cell, j, ch.Pair, sister, plate[j], ref ks[i]);
                }
            }
        }
        KeepRecombined(ks);
        return ks;
    }

    private DivPoleKey[] SecondPoles(double apart, double aster) =>
        Enumerable.Range(0, 4).Select(i =>
        {
            var cy = i < 2 ? D : -D;
            double x = i % 2 == 0 ? -1 : 1;
            var p = apart >= 1 ? new DivPoint(x * 0.55, cy) : new DivPoint(x * 0.32, cy + (i < 2 ? 1 : -1) * (o.Plant ? 0.25 : 0.42));
            return PoleAt(p, new DivPoint(0, cy), aster: aster);
        }).ToArray();

    private DivNucleusKey[] HiddenNuclei(double r) =>
        Enumerable.Range(0, 4).Select(i => new DivNucleusKey(new DivPoint(0, i < 2 ? D : -D), r, 0, 1)).ToArray();

    private DivisionKeyframe Prophase2Frame()
    {
        var contents = SecondDivisionContents();
        var counts = contents.ToDictionary(c => c.Cell, c => c.Chromosomes.Count);
        var ks = SecondDivision((int cell, int j, int _, int sister, (double Y, double A2) _, ref DivChromatidKey key) =>
        {
            // (round the cell's centre like the spokes of a wheel, each lying across its spoke, turned a little by a seed)
            var cy = cell == 0 ? D : -D;
            var k = Math.Max(counts.GetValueOrDefault(cell, 1), 1);
            var jitter = prophase2[Math.Min((cell == 0 ? 0 : N) + j, prophase2.Length - 1)];
            var spoke = (cell == 0 ? 0.3 : 1.9) + 2 * Math.PI * j / k;
            var r = k == 1 ? 0.04 : (o.Plant ? 0.15 : 0.21);
            key.C = new DivPoint(r * Math.Cos(spoke), cy + r * Math.Sin(spoke));
            key.A2 = spoke + Math.PI / 2 + (jitter - Math.PI) * 0.12;
            key.A1 = key.A2 + Math.PI;
            key.K = 1;
            key.Offset = (sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(1);
        });
        var f = Frame(TwoCells(), HiddenNuclei(o.Plant ? 0.36 : 0.4), SecondPoles(0, 0.8), Spindles(polar2: 0.5), CellPlates(first: 0.95, firstAlpha: 0), Furrows(), ks);
        var first = contents[0].Chromosomes[0];
        var labels = new List<DivLabelDef>
        {
            new("spindle", "Spindle (each cell)", DivAnchor.Polar(1, 0.5)),
            new("chromosome", "Still 2 chromatids", DivAnchor.Arm(Index(first.Pair, first.Homolog, 0), 0.9)),
            new("noS", "No new DNA copy", DivAnchor.Cell(2, 20)),
        };
        if (!o.Plant) labels.Add(new DivLabelDef("centrosome", "Centrosomes", DivAnchor.Pole(1)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.PerCellPlain };
    }

    private DivisionKeyframe Metaphase2()
    {
        var ks = SecondDivision((int cell, int _, int pair, int sister, (double Y, double A2) place, ref DivChromatidKey key) =>
        {
            var cy = cell == 0 ? D : -D;
            var poles = SidePoles(cell);
            key.C = new DivPoint(0, cy + place.Y);
            key.A2 = place.A2;
            key.A1 = place.A2 + Math.PI;
            key.K = 1;
            key.Offset = (sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(1);
            // (the sister on the left — the normal of an arm pointing up faces left — goes left; nondisjunction in meiosis
            //  II: the top cell's short chromosome has both kinetochores facing its left pole)
            var left = (sister == 0) == (place.A2 > 0);
            var nd = o.Nondisjunction == DivisionNondisjunction.MeiosisII && cell == 0 && pair == 1;
            key.Pole = nd || left ? poles.Left : poles.Right;
            key.Fibre = 1;
        });
        var f = Frame(TwoCells(), HiddenNuclei(0.4), SecondPoles(1, 1), Spindles(polar2: 1, plate2: 1), CellPlates(first: 0.95, firstAlpha: 0), Furrows(), ks);
        var first = SecondDivisionContents()[0].Chromosomes[0];
        var labels = new List<DivLabelDef>
        {
            new("plate", "Metaphase plate", DivAnchor.Plate(1, 0.95)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(first.Pair, first.Homolog, 0), 0.45)),
            new("sisters", "Sister chromatids", DivAnchor.Arm(Index(first.Pair, first.Homolog, 1), 0.9)),
            o.Plant ? new("pole", "Spindle pole", DivAnchor.Pole(3)) : new("centrosome", "Centrosome", DivAnchor.Pole(3)),
        };
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.PerCellPlain };
    }

    /// <summary>Anaphase II (fx 0.42 from the plate, fy 0.6) and telophase II: each chromatid at its pole's side, a V; with
    /// nondisjunction in meiosis II the top cell's short chromosome goes whole to its left pole.</summary>
    private DivChromatidKey[] SistersApart(double fx, double fy, double k, bool joinedND) =>
        SecondDivision((int cell, int _, int pair, int sister, (double Y, double A2) place, ref DivChromatidKey key) =>
        {
            var cy = cell == 0 ? D : -D;
            var nd = o.Nondisjunction == DivisionNondisjunction.MeiosisII && cell == 0 && pair == 1;
            var left = nd || ((sister == 0) == (place.A2 > 0));
            var arms = VArms(left ? Math.PI : 0, place.A2);
            key.C = new DivPoint(left ? -fx : fx, cy + place.Y * fy + (nd ? (sister == 0 ? 0.07 : -0.07) : 0));
            key.A1 = arms.A1;
            key.A2 = arms.A2;
            key.K = k;
            key.Joined = nd && joinedND;
            key.Offset = nd && joinedND ? (sister == 0 ? 1 : -1) * DivGeometry.SisterOffset(k) : 0;
            var poles = SidePoles(cell);
            key.Pole = left ? poles.Left : poles.Right;
            key.Part = left ? 0 : 1;
        });

    private DivisionKeyframe Anaphase2()
    {
        var ks = SistersApart(0.42, 0.6, 1, true);
        for (var i = 0; i < ks.Length; i++) ks[i].Fibre = 1;
        var ns = Enumerable.Range(0, 4).Select(i => new DivNucleusKey(new DivPoint(i % 2 == 0 ? -Q : Q, i < 2 ? D : -D), 0.26, 0)).ToArray();
        var f = Frame(TwoCells(), ns, SecondPoles(1, 1), Spindles(polar2: 1), CellPlates(first: 0.95, firstAlpha: 0), Furrows(), ks);
        var first = SecondDivisionContents()[0].Chromosomes[0];
        var labels = new List<DivLabelDef>
        {
            new("daughterChromosome", "Sisters separating", DivAnchor.Arm(Index(first.Pair, first.Homolog, 0), 0.9)),
            new("kinetochore", "Kinetochore microtubules", DivAnchor.Fibre(Index(first.Pair, first.Homolog, 1), 0.5)),
            new("polar", "Polar microtubules", DivAnchor.Polar(2, 0.6)),
        };
        if (o.Nondisjunction == DivisionNondisjunction.MeiosisII)
            labels.Add(new DivLabelDef("nondisjunction", "Nondisjunction", DivAnchor.Arm(Index(1, Upper(1), 0), 0.9)));
        return new DivisionKeyframe { Frame = f, Labels = labels, Style = DivCountStyle.AnaphaseII };
    }

    private DivisionKeyframe Telophase2()
    {
        var ks = SistersApart(Q, 0.4, 0.4, false);
        for (var i = 0; i < ks.Length; i++)
        {
            var cell = ks[i].Cell + (ks[i].Part == 1 ? 1 : 0);
            ks[i].Cell = cell;
            ks[i].Nucleus = cell;
            ks[i].Part = -1;
            ks[i].Pole = -1;
        }
        const double nr = 0.26;
        var centres = new[] { new DivPoint(-Q, D), new DivPoint(Q, D), new DivPoint(-Q, -D), new DivPoint(Q, -D) };
        // (a nucleus given three chromosomes by nondisjunction: side by side across it — the two copies of one pair had
        //  one place, and the three were drawn as one tangle)
        foreach (var g in Enumerable.Range(0, ks.Length).GroupBy(i => ks[i].Nucleus).Where(g => g.Key >= 0 && g.Key < 4 && g.Count() > 2))
        {
            var list = g.OrderBy(i => ks[i].C.Y).ThenBy(i => i).ToList();
            for (var j = 0; j < list.Count; j++)
                ks[list[j]].C = new DivPoint(centres[g.Key].X + (j - (list.Count - 1) / 2.0) * 0.1, centres[g.Key].Y);
        }
        var ns = NucleiAt(centres, nr, 1, nucleolus: 0.05, nucleolusAlpha: 0.8, confine: 1);
        var ps = Enumerable.Range(0, 4).Select(i => PoleAt(centres[i] + new DivPoint(i % 2 == 0 ? -0.24 : 0.24, i < 2 ? 0.3 : -0.3), new DivPoint(0, i < 2 ? D : -D), aster: 0.2)).ToArray();
        var f = Frame(FourCells(), ns, ps, Spindles(), CellPlates(first: 0.95, firstAlpha: 0, second: D, secondAlpha: 0.75), Furrows(), ks);
        var dy = o.Plant ? 0.3 : 0.32;
        var badges = Enumerable.Range(0, 4).Select(c => new DivBadgeDef(c, centres[c] + new DivPoint(0, c < 2 ? dy : -dy))).ToList();
        // (after nondisjunction not every cell is n: the label says which they are)
        var gametes = o.Nondisjunction switch
        {
            DivisionNondisjunction.MeiosisI => "Four cells: n+1 and n−1",
            DivisionNondisjunction.MeiosisII => "Four cells: n, n+1 and n−1",
            _ => "Four haploid cells (n)",
        };
        var labels = new List<DivLabelDef>
        {
            new("gamete", gametes, DivAnchor.Cell(0, 100)),
            new("nucleus", "Nucleus", DivAnchor.Nucleus(3, DivMath.Radians(-30))),
        };
        if (Crossing) labels.Add(new DivLabelDef("recombinant", "Recombinant chromosome", DivAnchor.Nucleus(0, DivMath.Radians(200))));
        return new DivisionKeyframe { Frame = f, Labels = labels, Badges = badges, Style = DivCountStyle.PerCell };
    }
}
