namespace Simpl.Tools;

// Cell Division's drawing as values (spec §4.6 Drawing rules, §5.2.4), the Mac's CellDivisionGeometry.swift: everything
// in "cell units" — the animal cell a circle of radius 1.0 and the plant cell a rounded square 1.9 × 1.9, centred at the
// origin, y up — so the view only maps units to points. A keyframe (DivisionFrame) holds every part's state at the end of
// a step; between two steps the view draws DivisionFrame.Mixed (positions, turns, condensation and opacities eased
// together), and each chromatid is drawn from its state by DivGeometry.Shape: a wavy thread at condensation 0, a capsule
// 0.055 wide at 1, with its sister 0.012 from it, pinched together at the centromere. Cells are outlines sampled round a
// centre, so one cell can pinch and part into two (or four) by easing the samples. Plain .NET, no WPF.

/// <summary>A point in cell units (y up).</summary>
public readonly record struct DivPoint(double X, double Y)
{
    public static readonly DivPoint Zero = new(0, 0);
    public static DivPoint operator +(DivPoint a, DivPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static DivPoint operator -(DivPoint a, DivPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static DivPoint operator *(DivPoint a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Angle => Math.Atan2(Y, X);

    /// <summary>The unit vector at an angle (radians, from +x, counterclockwise).</summary>
    public static DivPoint Dir(double a) => new(Math.Cos(a), Math.Sin(a));

    public DivPoint Mixed(DivPoint o, double t) => new(X + (o.X - X) * t, Y + (o.Y - Y) * t);
    public double Distance(DivPoint o) => (this - o).Length;
}

/// <summary>A box in cell units.</summary>
public readonly record struct DivRect(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly DivRect Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public DivPoint Centre => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public DivRect Adding(DivPoint p) => new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
    public DivRect Union(DivRect o) => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    public DivRect Mixed(DivRect o, double t) =>
        new(DivMath.Mix(MinX, o.MinX, t), DivMath.Mix(MinY, o.MinY, t), DivMath.Mix(MaxX, o.MaxX, t), DivMath.Mix(MaxY, o.MaxY, t));
}

public static class DivMath
{
    public static double Mix(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Between two angles the short way round (radians).</summary>
    public static double MixAngle(double a, double b, double t) => a + Turn(a, b) * t;

    /// <summary>The signed turn from one angle to another, −π…π.</summary>
    public static double Turn(double a, double b)
    {
        var d = (b - a) % (2 * Math.PI);
        if (d > Math.PI) d -= 2 * Math.PI;
        else if (d < -Math.PI) d += 2 * Math.PI;
        return d;
    }

    public static double Clamp01(double x) => Math.Min(Math.Max(x, 0), 1);

    public static double Smoothstep(double e0, double e1, double x)
    {
        var t = Clamp01((x - e0) / (e1 - e0));
        return t * t * (3 - 2 * t);
    }

    public static double Radians(double degrees) => degrees * Math.PI / 180;
}

// ---- chromosomes ----

/// <summary>A homologous pair of the model organism (spec §4.6): the long pair (7 units, centromere 40% along), the short
/// pair (4 units, 50%), and with 2n = 6 a third pair of 5 units (its centromere, which the spec leaves open, at 35%).</summary>
/// <param name="Centromere">Where the centromere is, as a fraction of the length from the short arm's end.</param>
public sealed record DivPairInfo(double Units, double Centromere, string Name);

/// <summary>A chromatid's place in the model: its pair (0 long, 1 short, 2 the third), its homolog (0 from the mother, 1
/// from the father) and which sister it is (0, 1; sister 1 is made in S phase).</summary>
public readonly record struct DivChromatidID(int Pair, int Homolog, int Sister) : IComparable<DivChromatidID>
{
    public int Index => (Pair * 2 + Homolog) * 2 + Sister;
    public int Chromosome => Pair * 2 + Homolog;

    public int CompareTo(DivChromatidID other) => Index.CompareTo(other.Index);

    public static DivChromatidID At(int index) => new(index / 4, (index / 2) % 2, index % 2);
}

/// <summary>A chromatid at a keyframe: how it is drawn and, for the counts, what it is.</summary>
public record struct DivChromatidKey
{
    public DivChromatidID Id;
    /// <summary>Its centromere.</summary>
    public DivPoint C;
    /// <summary>The short (p) arm's direction and the long (q) arm's, radians: opposite when it is straight, a V in anaphase.</summary>
    public double A1;
    public double A2;
    /// <summary>0 an interphase thread … 1 fully condensed.</summary>
    public double K;
    /// <summary>Across its chromosome's axis (left of the way from the short arm's end to the long arm's): ± for sisters
    /// side by side, 0 for a chromatid on its own.</summary>
    public double Offset;
    /// <summary>Beyond the chiasma, how far across it lies on the other homolog's side (crossing over in prophase I and
    /// metaphase I, where non-sister chromatids cross).</summary>
    public double Cross;
    /// <summary>Beyond the chiasma, coloured as the other parent: 0…1 (a recombinant chromatid).</summary>
    public double Recombined;
    public double Alpha;
    /// <summary>How much of it is drawn, from the short arm's end (1: all; its copy grows along it in S phase).</summary>
    public double Grow;
    /// <summary>The spindle pole its kinetochore faces (−1: none), and its kinetochore microtubules' opacity.</summary>
    public int Pole;
    public double Fibre;
    /// <summary>The nucleus it is gathered into as a new nucleus forms (−1: none).</summary>
    public int Nucleus;
    /// <summary>Its thread's wiggle (sisters share their chromosome's).</summary>
    public double Seed;
    /// <summary>For the counts: whether it exists yet (sister 1 is made in S), whether it is still joined to its sister,
    /// the cell it is in and the part of that cell (a pole or a nucleus; −1: the cell as a whole).</summary>
    public bool Exists;
    public bool Joined;
    public int Cell;
    public int Part;

    public DivChromatidKey(DivChromatidID id)
    {
        Id = id;
        C = DivPoint.Zero;
        A1 = Math.PI;
        A2 = 0;
        K = 0;
        Offset = 0;
        Cross = 0;
        Recombined = 0;
        Alpha = 1;
        Grow = 1;
        Pole = -1;
        Fibre = 0;
        Nucleus = -1;
        Seed = 0;
        Exists = true;
        Joined = false;
        Cell = 0;
        Part = -1;
    }
}

/// <summary>A chromatid as drawn: its line from the short arm's end through the centromere to the long arm's end.</summary>
public sealed class DivChromatidShape
{
    public DivPoint[] Points = Array.Empty<DivPoint>();
    /// <summary>The centromere's point.</summary>
    public int Centromere;
    /// <summary>The first point beyond the chiasma, where a recombinant chromatid's swapped part starts.</summary>
    public int? DistalStart;
    public double Width;
    /// <summary>Where its kinetochore microtubules attach: the centromere, on the side facing its pole.</summary>
    public DivPoint Kinetochore;
}

public static class DivGeometry
{
    /// <summary>The model's three pairs.</summary>
    public static readonly DivPairInfo[] Pairs =
    {
        new(7, 0.4, "long"),
        new(4, 0.5, "short"),
        new(5, 0.35, "middle"),
    };

    /// <summary>A unit of chromosome condensed: the long pair's 7 are 0.42, the short pair's 4 are 0.24.</summary>
    public const double Unit = 0.06;
    /// <summary>A unit as an interphase thread, measured along it: the long pair's 7 are 0.9.</summary>
    public const double ThreadUnit = 0.9 / 7;
    /// <summary>A thread's span end to end, as a part of its length along itself (its wiggles take the rest).</summary>
    public const double ThreadSpan = 0.5;
    /// <summary>A chromatid's width condensed, and as a thread.</summary>
    public const double Width = 0.055;
    public const double ThreadWidth = 0.016;
    /// <summary>Between sister chromatids.</summary>
    public const double Gap = 0.012;
    /// <summary>The thread's wave, end to end.</summary>
    public const double Wave = 0.075;
    /// <summary>Points along a chromatid, every so far.</summary>
    public const double Step = 0.008;

    public static double ChromatidWidth(double k) => DivMath.Mix(ThreadWidth, Width, DivMath.Clamp01(k));

    /// <summary>A sister's offset from its chromosome's axis: half a chromatid's width and half the gap.</summary>
    public static double SisterOffset(double k) => (ChromatidWidth(k) + Gap) / 2;

    /// <summary>The chromosome's span end to end: its thread's at 0, its condensed length at 1.</summary>
    public static double Span(DivPairInfo pair, double k) =>
        DivMath.Mix(pair.Units * ThreadUnit * ThreadSpan, pair.Units * Unit, DivMath.Clamp01(k));

    /// <summary>The thread's wiggle at condensation 0: set so the long pair's thread is 0.9 long along itself (the spec's
    /// interphase length), found once.</summary>
    public static readonly double Wiggle = FindWiggle();

    private static double FindWiggle()
    {
        double lo = 0, hi = 0.08;
        for (var i = 0; i < 40; i++)
        {
            var mid = (lo + hi) / 2;
            if (ThreadLength(Pairs[0], mid) < 0.9) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    /// <summary>The long-way length of a thread drawn with a given wiggle (for Wiggle).</summary>
    public static double ThreadLength(DivPairInfo pair, double amplitude)
    {
        var key = new DivChromatidKey(new DivChromatidID(0, 0, 0)) { K = 0 };
        return Length(Shape(key, pair, 0.5, amplitude).Points);
    }

    public static double Length(IReadOnlyList<DivPoint> points)
    {
        double total = 0;
        for (var i = 1; i < points.Count; i++) total += points[i - 1].Distance(points[i]);
        return total;
    }

    /// <summary>The chromatid drawn from its state. <paramref name="chiasma"/>: where along the long arm (a fraction of it,
    /// from the centromere) a crossing over swaps its part.</summary>
    public static DivChromatidShape Shape(DivChromatidKey c, DivPairInfo pair, double chiasma, double? amplitude = null)
    {
        var k = DivMath.Clamp01(c.K);
        var length = Span(pair, k);
        double l1 = length * pair.Centromere, l2 = length - l1;
        var amp = (amplitude ?? Wiggle) * Math.Pow(1 - k, 1.3);
        var w = ChromatidWidth(k);
        DivPoint d1 = DivPoint.Dir(c.A1), d2 = DivPoint.Dir(c.A2);
        // (left of the way along it: from the short arm's end in to the centromere, then out along the long arm)
        DivPoint n1 = new(d1.Y, -d1.X), n2 = new(-d2.Y, d2.X);
        var n0 = n1 + n2;
        n0 = n0.Length < 1e-6 ? n2 : n0 * (1 / n0.Length);
        var sc = chiasma * l2;
        double Across(double s, bool isLong)
        {
            var taper = Math.Min(1, s / 0.03);
            var wig = amp * Math.Sin(2 * Math.PI * s / Wave + c.Seed) * taper;
            // (sisters held together at the centromere: the X's waist; and a little apart toward their ends, so a
            //  condensed chromosome reads as an X)
            var pinch = 1 - 0.55 * k * Math.Exp(-(s / 0.035) * (s / 0.035)) + 0.3 * k * k * Math.Pow(s / Math.Max(isLong ? l2 : l1, 0.01), 2);
            var o = c.Offset * pinch;
            if (isLong && c.Cross != 0) o += c.Cross * DivMath.Smoothstep(sc - 0.02, sc + 0.02, s);
            return wig + o;
        }
        var pts = new List<DivPoint>();
        var m1 = Math.Max(3, (int)Math.Ceiling(l1 / Step));
        for (var i = m1; i > 0; i--)
        {
            var s = l1 * i / m1;
            pts.Add(c.C + d1 * s + n1 * Across(s, false));
        }
        var centre = pts.Count;
        var o0 = Across(0, false);
        pts.Add(c.C + n0 * o0);
        var m2 = Math.Max(3, (int)Math.Ceiling(l2 / Step));
        int? distal = null;
        for (var i = 1; i <= m2; i++)
        {
            var s = l2 * i / m2;
            if (distal == null && c.Recombined > 0 && s >= sc) distal = pts.Count;
            pts.Add(c.C + d2 * s + n2 * Across(s, true));
        }
        double side = c.Offset > 0 ? 1 : (c.Offset < 0 ? -1 : 0);
        var kinetochore = c.C + n0 * (o0 + side * w * 0.5);
        return new DivChromatidShape { Points = pts.ToArray(), Centromere = centre, DistalStart = distal, Width = w, Kinetochore = kinetochore };
    }
}

// ---- cells ----

public enum DivShapeKind { Disk, Pinched, Box }

/// <summary>A shape a cell (or part of one) fills: a disk (centre, radius); the animal cell pinched by its cleavage furrow at
/// y = 0 (a circle of radius A whose width there is 2·B); or a plant cell, a rounded box (centre, half width A, half height
/// B, corner radius).</summary>
public readonly record struct DivShape(DivShapeKind Kind, DivPoint C, double A, double B = 0, double Corner = 0)
{
    public static DivShape Disk(DivPoint c, double r) => new(DivShapeKind.Disk, c, r);
    public static DivShape Pinched(double r, double waist) => new(DivShapeKind.Pinched, DivPoint.Zero, r, waist);
    public static DivShape Box(DivPoint c, double halfWidth, double halfHeight, double corner) => new(DivShapeKind.Box, c, halfWidth, halfHeight, corner);

    public bool Inside(DivPoint p)
    {
        switch (Kind)
        {
            case DivShapeKind.Disk:
                return p.Distance(C) <= A;
            case DivShapeKind.Pinched:
            {
                double r = A, waist = B;
                if (Math.Abs(p.Y) > r) return false;
                var g = 1 - (1 - waist / r) * Math.Exp(-(p.Y / 0.3) * (p.Y / 0.3));
                return Math.Abs(p.X) <= Math.Sqrt(r * r - p.Y * p.Y) * g;
            }
            default:
            {
                double hw = A, hh = B;
                double dx = Math.Abs(p.X - C.X) - (hw - Corner), dy = Math.Abs(p.Y - C.Y) - (hh - Corner);
                if (dx <= 0 || dy <= 0) return Math.Abs(p.X - C.X) <= hw && Math.Abs(p.Y - C.Y) <= hh;
                return Math.Sqrt(dx * dx + dy * dy) <= Corner;
            }
        }
    }

    public DivRect Bounds => Kind switch
    {
        DivShapeKind.Disk => new DivRect(C.X - A, C.Y - A, C.X + A, C.Y + A),
        DivShapeKind.Pinched => new DivRect(-A, -A, A, A),
        _ => new DivRect(C.X - A, C.Y - B, C.X + A, C.Y + B),
    };
}

/// <summary>A cell, or the part of one that becomes a daughter: a shape kept to one side of x = 0 and/or y = 0 if asked.
/// The cut is a new wall (or membrane) once the cells have parted (WallX, WallY), else just where two parts of one cell
/// meet (not drawn).</summary>
public readonly record struct DivRegion(DivShape Shape, int KeepX = 0, int KeepY = 0, bool WallX = false, bool WallY = false)
{
    public bool Inside(DivPoint p) =>
        Shape.Inside(p) && (KeepX == 0 || KeepX * p.X >= 0) && (KeepY == 0 || KeepY * p.Y >= 0);

    /// <summary>Its centre of area (found on a grid).</summary>
    public DivPoint Centroid
    {
        get
        {
            var b = Shape.Bounds;
            const int n = 56;
            double sx = 0, sy = 0, count = 0;
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    var p = new DivPoint(b.MinX + b.Width * (i + 0.5) / n, b.MinY + b.Height * (j + 0.5) / n);
                    if (!Inside(p)) continue;
                    sx += p.X;
                    sy += p.Y;
                    count++;
                }
            return count > 0 ? new DivPoint(sx / count, sy / count) : b.Centre;
        }
    }
}

/// <summary>A cell's outline at a keyframe: its distance from a centre in each of <see cref="Samples"/> directions (from
/// straight down, counterclockwise), and whether the outline there is membrane (or wall), 1, or only where it meets
/// another part of the same cell, 0 (not drawn). Easing the distances and the centre between keyframes pinches one cell
/// into two.</summary>
public sealed class DivCellKey
{
    public const int Samples = 144;

    public DivPoint Centre;
    public double[] Radii;
    public double[] Membrane;

    public static double Angle(int i) => -Math.PI / 2 + 2 * Math.PI * i / Samples;

    public DivCellKey(DivPoint centre, double[] radii, double[] membrane)
    {
        Centre = centre;
        Radii = radii;
        Membrane = membrane;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DivRegion, DivCellKey> Made = new();

    /// <summary>The outline of a region, worked out once and kept (a few kinds of cell, used again whenever an option
    /// changes; nothing changes an outline once made — a mix makes a new one).</summary>
    public static DivCellKey For(DivRegion region) => Made.GetOrAdd(region, r => new DivCellKey(r));

    /// <summary>The outline of a region, round its centre of area.</summary>
    public DivCellKey(DivRegion region)
    {
        var g = region.Centroid;
        Radii = new double[Samples];
        Membrane = new double[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var d = DivPoint.Dir(Angle(i));
            var t = 0.0;
            const double dt = 0.02;
            while (t < 4 && region.Inside(g + d * (t + dt))) t += dt;
            double lo = t, hi = t + dt;
            for (var k = 0; k < 22; k++)
            {
                var mid = (lo + hi) / 2;
                if (region.Inside(g + d * mid)) lo = mid; else hi = mid;
            }
            var q = g + d * (lo + 0.003);
            bool wall;
            if (!region.Shape.Inside(q)) wall = true;
            else if (region.KeepX != 0 && region.KeepX * q.X < 0) wall = region.WallX;
            else wall = region.WallY;
            // (a cut not drawn reaches a little past the line, so the parts' fills meet without a seam)
            Radii[i] = lo + (wall ? 0 : 0.004);
            Membrane[i] = wall ? 1 : 0;
        }
        Centre = g;
    }

    public DivPoint Point(int i) => Centre + DivPoint.Dir(Angle(i)) * Radii[i];

    public DivRect Bounds
    {
        get
        {
            var b = DivRect.Empty;
            for (var i = 0; i < Radii.Length; i++) b = b.Adding(Point(i));
            return b;
        }
    }

    public DivCellKey Mixed(DivCellKey o, double t)
    {
        var radii = new double[Radii.Length];
        var membrane = new double[Membrane.Length];
        for (var i = 0; i < radii.Length; i++)
        {
            radii[i] = DivMath.Mix(Radii[i], o.Radii[i], t);
            membrane[i] = DivMath.Mix(Membrane[i], o.Membrane[i], t);
        }
        return new DivCellKey(Centre.Mixed(o.Centre, t), radii, membrane);
    }

    public bool Same(DivCellKey o) => Centre == o.Centre && Radii.SequenceEqual(o.Radii) && Membrane.SequenceEqual(o.Membrane);
}

/// <summary>A nucleus: its envelope (solid, or dashed as it breaks down or forms), its nucleolus, and how firmly it gathers
/// its chromosomes inside it (a new nucleus pulls its decondensing chromosomes in).</summary>
public readonly record struct DivNucleusKey(DivPoint C, double R = 0.55, double Alpha = 0, double Dashed = 0, double Nucleolus = 0,
    double NucleolusAlpha = 0, double Confine = 0)
{
    public DivNucleusKey Mixed(DivNucleusKey o, double t) => new(C.Mixed(o.C, t), DivMath.Mix(R, o.R, t), DivMath.Mix(Alpha, o.Alpha, t),
        DivMath.Mix(Dashed, o.Dashed, t), DivMath.Mix(Nucleolus, o.Nucleolus, t), DivMath.Mix(NucleolusAlpha, o.NucleolusAlpha, t),
        DivMath.Mix(Confine, o.Confine, t));

    /// <summary>The nucleolus sits a little off the nucleus's centre.</summary>
    public DivPoint NucleolusCentre => C + new DivPoint(0.28, -0.22) * R;
}

/// <summary>A spindle pole: where it is, the point it swings round as it moves (two keyframes with the same pivot move it
/// round it, as centrosomes move round the nucleus), and how much of a centrosome (animal cells) and its aster are drawn.</summary>
public readonly record struct DivPoleKey(DivPoint P, DivPoint Pivot, double Centrosome = 0, double Aster = 0)
{
    public DivPoleKey Mixed(DivPoleKey o, double t)
    {
        DivPoint q;
        if (Pivot == o.Pivot)
        {
            DivPoint a = P - Pivot, b = o.P - Pivot;
            var r = DivMath.Mix(a.Length, b.Length, t);
            var ang = DivMath.MixAngle(a.Angle, b.Angle, t);
            q = Pivot + DivPoint.Dir(ang) * r;
        }
        else q = P.Mixed(o.P, t);
        return new DivPoleKey(q, Pivot.Mixed(o.Pivot, t), DivMath.Mix(Centrosome, o.Centrosome, t), DivMath.Mix(Aster, o.Aster, t));
    }
}

/// <summary>A spindle between two poles: its polar microtubules (overlapping in the middle) and its metaphase plate (an
/// imaginary plane, drawn dashed across the middle).</summary>
public readonly record struct DivSpindleKey(int A, int B, double Polar = 0, double Plate = 0, double PlateLength = 1.5)
{
    public DivSpindleKey Mixed(DivSpindleKey o, double t) =>
        new(A, B, DivMath.Mix(Polar, o.Polar, t), DivMath.Mix(Plate, o.Plate, t), DivMath.Mix(PlateLength, o.PlateLength, t));
}

/// <summary>A line where a cell divides: a plant's cell plate (Golgi vesicles fusing from the middle outward, Half its
/// half-length so far), or an animal's cleavage furrow (its actin–myosin ring, drawn at the waist ± Half).</summary>
public readonly record struct DivLineKey(DivPoint C, bool Horizontal = true, double Half = 0, double Alpha = 0)
{
    public DivLineKey Mixed(DivLineKey o, double t) => new(C.Mixed(o.C, t), Horizontal, DivMath.Mix(Half, o.Half, t), DivMath.Mix(Alpha, o.Alpha, t));

    public DivPoint Point(double f) => Horizontal ? C + new DivPoint(f * Half, 0) : C + new DivPoint(0, f * Half);
}

/// <summary>Everything drawn at the end of a step, in cell units.</summary>
public sealed class DivisionFrame
{
    public bool Plant;
    public DivCellKey[] Cells = Array.Empty<DivCellKey>();
    public DivNucleusKey[] Nuclei = Array.Empty<DivNucleusKey>();
    public DivPoleKey[] Poles = Array.Empty<DivPoleKey>();
    public DivSpindleKey[] Spindles = Array.Empty<DivSpindleKey>();
    public DivLineKey[] CellPlates = Array.Empty<DivLineKey>();
    public DivLineKey[] Furrows = Array.Empty<DivLineKey>();
    public DivChromatidKey[] Chromatids = Array.Empty<DivChromatidKey>();
    /// <summary>What the view should keep in sight (the cells).</summary>
    public DivRect Bounds;

    /// <summary>Part way to another keyframe (the same parts, eased together).</summary>
    public DivisionFrame Mixed(DivisionFrame o, double t)
    {
        if (t <= 0) return this;
        if (t >= 1) return o;
        static TOut[] Zip<TIn, TOut>(TIn[] a, TIn[] b, Func<TIn, TIn, TOut> f)
        {
            var n = Math.Min(a.Length, b.Length);
            var r = new TOut[n];
            for (var i = 0; i < n; i++) r[i] = f(a[i], b[i]);
            return r;
        }
        return new DivisionFrame
        {
            Plant = Plant,
            Cells = Zip(Cells, o.Cells, (a, b) => a.Mixed(b, t)),
            Nuclei = Zip(Nuclei, o.Nuclei, (a, b) => a.Mixed(b, t)),
            Poles = Zip(Poles, o.Poles, (a, b) => a.Mixed(b, t)),
            Spindles = Zip(Spindles, o.Spindles, (a, b) => a.Mixed(b, t)),
            CellPlates = Zip(CellPlates, o.CellPlates, (a, b) => a.Mixed(b, t)),
            Furrows = Zip(Furrows, o.Furrows, (a, b) => a.Mixed(b, t)),
            Chromatids = Zip(Chromatids, o.Chromatids, (a, b) =>
            {
                var m = a;
                m.C = a.C.Mixed(b.C, t);
                m.A1 = DivMath.MixAngle(a.A1, b.A1, t);
                m.A2 = DivMath.MixAngle(a.A2, b.A2, t);
                m.K = DivMath.Mix(a.K, b.K, t);
                m.Offset = DivMath.Mix(a.Offset, b.Offset, t);
                m.Cross = DivMath.Mix(a.Cross, b.Cross, t);
                m.Recombined = DivMath.Mix(a.Recombined, b.Recombined, t);
                m.Alpha = DivMath.Mix(a.Alpha, b.Alpha, t);
                m.Grow = DivMath.Mix(a.Grow, b.Grow, t);
                // (a fibre to the same pole fades between its opacities; one changing pole fades out, then in)
                m.Fibre = a.Pole == b.Pole ? DivMath.Mix(a.Fibre, b.Fibre, t) : (t < 0.5 ? a.Fibre * (1 - 2 * t) : b.Fibre * (2 * t - 1));
                m.Pole = t < 0.5 ? a.Pole : b.Pole;
                m.Nucleus = t < 0.5 ? a.Nucleus : b.Nucleus;
                return m;
            }),
            Bounds = Bounds.Mixed(o.Bounds, t),
        };
    }

    /// <summary>The same frame, part for part (the self-check's: a mix at 0 and at 1 is its ends).</summary>
    public bool Same(DivisionFrame o) =>
        Plant == o.Plant && Cells.Length == o.Cells.Length && Cells.Zip(o.Cells).All(x => x.First.Same(x.Second))
        && Nuclei.SequenceEqual(o.Nuclei) && Poles.SequenceEqual(o.Poles) && Spindles.SequenceEqual(o.Spindles)
        && CellPlates.SequenceEqual(o.CellPlates) && Furrows.SequenceEqual(o.Furrows) && Chromatids.SequenceEqual(o.Chromatids)
        && Bounds == o.Bounds;

    /// <summary>Each chromatid drawn, gathered into its nucleus as that nucleus forms (Confine): the chromatids of a
    /// nucleus are drawn closer to its centre, together, until they fit inside it.</summary>
    public DivChromatidShape[] Shapes(double chiasma)
    {
        var shapes = Chromatids.Select(c => DivGeometry.Shape(c, DivGeometry.Pairs[c.Id.Pair], chiasma)).ToArray();
        for (var j = 0; j < Nuclei.Length; j++)
        {
            var n = Nuclei[j];
            if (n.Confine <= 0.001) continue;
            var members = Enumerable.Range(0, Chromatids.Length).Where(i => Chromatids[i].Nucleus == j && Chromatids[i].Alpha > 0.01).ToList();
            if (members.Count == 0) continue;
            var far = 0.0;
            foreach (var i in members) foreach (var p in shapes[i].Points) far = Math.Max(far, p.Distance(n.C));
            if (far <= 0) continue;
            var fit = Math.Min(1, 0.84 * n.R / far);
            var s = DivMath.Mix(1, fit, DivMath.Clamp01(n.Confine));
            foreach (var i in members)
            {
                shapes[i].Points = shapes[i].Points.Select(p => n.C + (p - n.C) * s).ToArray();
                shapes[i].Kinetochore = n.C + (shapes[i].Kinetochore - n.C) * s;
            }
        }
        return shapes;
    }
}
