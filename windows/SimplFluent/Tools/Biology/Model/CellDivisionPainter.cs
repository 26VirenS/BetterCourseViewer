namespace Simpl.Tools;

// Cell Division's drawing as a list (spec §4.6 Drawing rules), the Mac's CellDivisionPainter.swift: a frame turned into
// what to draw, back to front — the cells' cytoplasm, the plant's walls, the membranes, the nuclei (a double envelope,
// dashed while it breaks down or forms) and nucleoli, the furrow's ring and the cell plate, the metaphase plate (dashed:
// an imaginary plane), the spindle (polar and kinetochore microtubules, broad poles in a plant), the centrosomes with
// their centrioles and asters, and the chromatids (outlined, the mother's red and the father's blue, a recombinant part
// in the other parent's colour, a dot at each chromosome's centromere). Everything is in cell units except line widths
// given in points; the view (CellDivisionCanvas.cs) maps units to points, inks to colours, and strokes. Labels' anchors
// and the pointer's hits are found here too. Plain .NET, no WPF.

/// <summary>What a part is drawn in (the view gives each its colour in light and dark).</summary>
public enum DivInk { Cytoplasm, Membrane, Wall, Nucleoplasm, Envelope, Nucleolus, Furrow, CellPlate, Plate, Microtubule, Aster, Centrosome, Centriole, Maternal, Paternal, Edge, Centromere }

/// <summary>A run of a chromatid in one colour (or part way from one to another, as crossing over recolours it).</summary>
public readonly record struct DivRun(int From, int To, DivInk Ink, DivInk Other, double Mix);

/// <summary>A chromatid to draw: its line, its width in cell units, its colour runs, and how condensed it is (its sheen
/// shows as it condenses).</summary>
public sealed record DivChromatidPaint(int Index, DivPoint[] Points, double Width, DivRun[] Runs, double Alpha, double K);

/// <summary>Something to draw.</summary>
public abstract record DivPrimitive;
/// <summary>Closed shapes filled as one (where they overlap, filled once).</summary>
public sealed record DivFill(List<DivPoint[]> Shapes, DivInk Ink, double Alpha) : DivPrimitive;
/// <summary>Lines Width points wide, dashed if asked (stroked as one, so where they meet they are not drawn twice).</summary>
public sealed record DivLine(List<DivPoint[]> Lines, DivInk Ink, double Width, double Alpha, bool Dashed) : DivPrimitive;
/// <summary>Lines Width cell units wide, round ends.</summary>
public sealed record DivBand(List<DivPoint[]> Lines, DivInk Ink, double Width, double Alpha) : DivPrimitive;
/// <summary>A disc, its radius in cell units.</summary>
public sealed record DivDisc(DivPoint C, double R, DivInk Ink, double Alpha) : DivPrimitive;
public sealed record DivChromatidPrimitive(DivChromatidPaint Paint) : DivPrimitive;

public static class DivPainter
{
    public static DivInk Parent(int homolog) => homolog == 0 ? DivInk.Maternal : DivInk.Paternal;

    /// <summary>A frame as what to draw, and its chromatids' shapes (for labels and the pointer). <paramref name="fade"/>
    /// multiplies every opacity (a crossfade with animations off draws two frames).</summary>
    public static (List<DivPrimitive> Primitives, DivChromatidShape[] Shapes) Paint(DivisionFrame f, double chiasma, double fade = 1)
    {
        var list = new List<DivPrimitive>();
        var shapes = f.Shapes(chiasma);

        // (the cells: one fill, then their walls and membranes where the outline is membrane)
        list.Add(new DivFill(f.Cells.Select(Outline).ToList(), DivInk.Cytoplasm, fade));
        // (runs of one opacity stroked together)
        var runs = f.Cells.SelectMany(MembraneRuns).GroupBy(r => r.Alpha).OrderBy(g => g.Key).ToList();
        if (f.Plant)
            foreach (var g in runs) list.Add(new DivBand(g.Select(r => r.Points).ToList(), DivInk.Wall, 0.034, g.Key * fade));
        foreach (var g in runs) list.Add(new DivLine(g.Select(r => r.Points).ToList(), DivInk.Membrane, f.Plant ? 1.2 : 1.7, g.Key * fade, false));

        // (nuclei: nucleoplasm, nucleolus, then the envelope's two membranes)
        foreach (var n in f.Nuclei)
        {
            if (n.Alpha <= 0.01 && n.NucleolusAlpha <= 0.01) continue;
            if (n.Alpha > 0.01) list.Add(new DivDisc(n.C, n.R, DivInk.Nucleoplasm, n.Alpha * fade));
            if (n.NucleolusAlpha > 0.01 && n.Nucleolus > 0.005) list.Add(new DivDisc(n.NucleolusCentre, n.Nucleolus, DivInk.Nucleolus, n.NucleolusAlpha * fade));
            if (n.Alpha <= 0.01) continue;
            var dashed = n.Dashed > 0.5;
            list.Add(new DivLine(new() { Circle(n.C, n.R), Circle(n.C, n.R - 0.022) }, DivInk.Envelope, 1.2, n.Alpha * (dashed ? 0.8 : 1) * fade, dashed));
        }

        // (the furrow's actin–myosin ring, seen edge on: a dot at each side of the waist)
        foreach (var fu in f.Furrows)
        {
            if (fu.Alpha <= 0.01) continue;
            foreach (var s in new[] { -1.0, 1.0 }) list.Add(new DivDisc(fu.Point(s) + new DivPoint(s * 0.02, 0), 0.03, DivInk.Furrow, fu.Alpha * fade));
        }
        // (the cell plate: vesicles fusing along a line, from the middle out)
        foreach (var cp in f.CellPlates)
        {
            if (cp.Alpha <= 0.01 || cp.Half <= 0.01) continue;
            list.Add(new DivLine(new() { new[] { cp.Point(-1), cp.Point(1) } }, DivInk.CellPlate, 2.4, cp.Alpha * fade, false));
            var count = Math.Max(1, (int)(cp.Half / 0.05));
            for (var i = -count; i <= count; i++) list.Add(new DivDisc(cp.Point((double)i / count), 0.017, DivInk.CellPlate, cp.Alpha * 0.9 * fade));
        }

        // (the spindles: the metaphase plate, the polar microtubules overlapping in the middle)
        foreach (var s in f.Spindles)
        {
            DivPoint a = f.Poles[s.A].P, b = f.Poles[s.B].P;
            var axis = b - a;
            if (axis.Length <= 0.05) continue;
            var u = axis * (1 / axis.Length);
            var v = new DivPoint(-u.Y, u.X);
            var mid = a.Mixed(b, 0.5);
            if (s.Plate > 0.01)
                list.Add(new DivLine(new() { new[] { mid - v * (s.PlateLength / 2), mid + v * (s.PlateLength / 2) } }, DivInk.Plate, 1.3, s.Plate * 0.85 * fade, true));
            if (s.Polar > 0.01)
            {
                var fibres = new List<DivPoint[]>();
                foreach (var (from, to, flip) in new[] { (a, b, 1.0), (b, a, -1.0) })
                    for (var j = -2; j <= 2; j++)
                    {
                        var spread = j * 0.075 + (flip > 0 ? 0 : 0.035);
                        var end = from.Mixed(to, 0.6) + v * spread;
                        fibres.Add(Bowed(PoleStart(from, end, v, f.Plant), end, v, spread * 0.5));
                    }
                list.Add(new DivLine(fibres, DivInk.Microtubule, 1, s.Polar * 0.5 * fade, false));
            }
        }
        // (kinetochore microtubules: from a pole to each chromatid's kinetochore)
        for (var i = 0; i < f.Chromatids.Length; i++)
        {
            var c = f.Chromatids[i];
            if (c.Pole < 0 || c.Fibre <= 0.01 || c.Alpha <= 0.01 || c.Pole >= f.Poles.Length) continue;
            DivPoint p = f.Poles[c.Pole].P, k = shapes[i].Kinetochore;
            var d = k - p;
            if (d.Length <= 0.02) continue;
            var v = new DivPoint(-d.Y, d.X) * (1 / d.Length);
            var broad = Across(c.Pole, f) ?? v;
            var fibres = new[] { -1.0, 1.0 }.Select(s =>
            {
                var end = k + v * (s * 0.011);
                return new[] { PoleStart(p, end, broad, f.Plant), end };
            }).ToList();
            list.Add(new DivLine(fibres, DivInk.Microtubule, 1.1, c.Fibre * c.Alpha * 0.85 * fade, false));
        }
        // (centrosomes: the pericentriolar cloud, two centrioles at right angles, and the aster's rays)
        foreach (var p in f.Poles)
        {
            if (p.Aster > 0.01)
            {
                var rays = Enumerable.Range(0, 12).Select(j =>
                {
                    var d = DivPoint.Dir(j * Math.PI / 6 + 0.2);
                    return new[] { p.P + d * 0.055, p.P + d * 0.17 };
                }).ToList();
                list.Add(new DivLine(rays, DivInk.Aster, 0.9, p.Aster * 0.45 * fade, false));
            }
            if (p.Centrosome <= 0.01) continue;
            list.Add(new DivDisc(p.P, 0.055, DivInk.Centrosome, p.Centrosome * 0.35 * fade));
            list.Add(new DivBand(new()
            {
                new[] { p.P + new DivPoint(-0.032, 0.014), p.P + new DivPoint(0.012, 0.014) },
                new[] { p.P + new DivPoint(0.026, -0.03), p.P + new DivPoint(0.026, 0.006) },
            }, DivInk.Centriole, 0.019, p.Centrosome * fade));
        }

        // (the chromatids, then a dot at each chromosome's centromere)
        for (var i = 0; i < f.Chromatids.Length; i++)
        {
            var c = f.Chromatids[i];
            if (c.Alpha <= 0.01) continue;
            var pts = shapes[i].Points;
            var distal = shapes[i].DistalStart;
            if (c.Grow < 0.999)
            {
                pts = Trimmed(pts, c.Grow);
                if (distal is { } dd && dd >= pts.Length) distal = null;
            }
            if (pts.Length < 2) continue;
            DivInk own = Parent(c.Id.Homolog), other = Parent(1 - c.Id.Homolog);
            var colourRuns = new[] { new DivRun(0, pts.Length - 1, own, own, 0) };
            if (distal is { } d && c.Recombined > 0.001 && d > 0 && d < pts.Length)
                colourRuns = new[] { new DivRun(0, d, own, own, 0), new DivRun(d - 1, pts.Length - 1, own, other, c.Recombined) };
            list.Add(new DivChromatidPrimitive(new DivChromatidPaint(i, pts, shapes[i].Width, colourRuns, c.Alpha * fade, c.K)));
        }
        for (var i = 0; i < f.Chromatids.Length; i++)
        {
            var c = f.Chromatids[i];
            if (c.Alpha <= 0.01 || c.K <= 0.25) continue;
            // (one dot per chromosome: joined sisters share theirs, on their axis)
            if (c.Joined && c.Id.Sister == 1) continue;
            var at = c.Joined ? c.C : shapes[i].Points[shapes[i].Centromere];
            var r = 0.017 + 0.008 * c.K;
            list.Add(new DivDisc(at, r, DivInk.Centromere, c.Alpha * DivMath.Smoothstep(0.25, 0.6, c.K) * fade));
        }
        return (list, shapes);
    }

    /// <summary>Where a cell's outline is membrane: runs of it, each with one opacity (as cells part, the line between them
    /// appears).</summary>
    public static List<(DivPoint[] Points, double Alpha)> MembraneRuns(DivCellKey c)
    {
        const int n = DivCellKey.Samples;
        double Level(int i) => Math.Round(Math.Min(c.Membrane[i % n], c.Membrane[(i + 1) % n]) * 20, MidpointRounding.AwayFromZero) / 20;
        // (start where the level changes, so a run is never cut at sample 0)
        var start = Enumerable.Range(0, n).FirstOrDefault(i => Level(i) != Level((i + n - 1) % n), -1);
        if (start < 0) start = 0;
        var outs = new List<(DivPoint[] Points, double Alpha)>();
        var current = new List<DivPoint>();
        var alpha = Level(start);
        for (var step = 0; step < n; step++)
        {
            var i = (start + step) % n;
            var a = Level(i);
            if (a != alpha)
            {
                if (alpha > 0.02 && current.Count > 1) outs.Add((current.ToArray(), alpha));
                current = new List<DivPoint>();
                alpha = a;
            }
            if (current.Count == 0) current.Add(c.Point(i));
            current.Add(c.Point((i + 1) % n));
        }
        if (alpha > 0.02 && current.Count > 1) outs.Add((current.ToArray(), alpha));
        // (a run that stops where its cell meets another part of the same cell reaches on to the line between them, so the
        //  outline has no gap where the two parts' runs meet)
        if (!(outs.Count > 1 || (outs.Count > 0 ? outs[0].Points.Length : 0) <= n)) return outs;
        return outs.Select(run =>
        {
            var pts = run.Points.ToArray();
            if (pts.Length > 2)
            {
                pts[0] = Reach(pts[0], pts[0] - pts[1]);
                pts[^1] = Reach(pts[^1], pts[^1] - pts[^2]);
            }
            return (pts, run.Alpha);
        }).ToList();
    }

    /// <summary>From the end of a membrane, on along it to where cells divide (x = 0 or y = 0), if that is near.</summary>
    public static DivPoint Reach(DivPoint p, DivPoint d)
    {
        if (d.Length <= 0) return p;
        var u = d * (1 / d.Length);
        var t = 0.0;
        foreach (var (pos, dir) in new[] { (p.X, u.X), (p.Y, u.Y) })
        {
            if (Math.Abs(dir) <= 1e-6) continue;
            var hit = -pos / dir;
            if (hit > 0 && hit < 0.08) t = t == 0 ? hit : Math.Min(t, hit);
        }
        return p + u * t;
    }

    /// <summary>A cell's outline for its fill: its samples, with the corner where membrane meets the line to another part of
    /// the same cell put in, so the parts' fills meet without a sliver between them.</summary>
    public static DivPoint[] Outline(DivCellKey c)
    {
        const int n = DivCellKey.Samples;
        var outs = new List<DivPoint>();
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            outs.Add(c.Point(i));
            bool here = c.Membrane[i] >= 0.5, next = c.Membrane[j] >= 0.5;
            if (here && !next) outs.Add(Reach(c.Point(i), c.Point(i) - c.Point((i + n - 1) % n)));
            else if (!here && next) outs.Add(Reach(c.Point(j), c.Point(j) - c.Point((j + 1) % n)));
        }
        return outs.ToArray();
    }

    public static DivPoint[] Circle(DivPoint c, double r, int segments = 96) =>
        Enumerable.Range(0, segments + 1).Select(i => c + DivPoint.Dir(2 * Math.PI * i / segments) * r).ToArray();

    /// <summary>A microtubule bowed out from the spindle's axis (the spindle's barrel shape).</summary>
    public static DivPoint[] Bowed(DivPoint a, DivPoint b, DivPoint v, double bow) =>
        Enumerable.Range(0, 11).Select(j =>
        {
            var t = j / 10.0;
            return a.Mixed(b, t) + v * (bow * Math.Sin(Math.PI * t));
        }).ToArray();

    /// <summary>Where a microtubule leaves its pole: the centrosome in an animal cell; spread along a broad pole in a plant
    /// (no centrosome: the plant's microtubule-organising regions).</summary>
    public static DivPoint PoleStart(DivPoint p, DivPoint end, DivPoint v, bool plant)
    {
        if (!plant) return p;
        var d = end - p;
        return p + v * Math.Max(-0.14, Math.Min(0.14, (d.X * v.X + d.Y * v.Y) * 0.35));
    }

    /// <summary>Across a pole's spindle (the spindle there that is most drawn), for a plant's broad poles.</summary>
    public static DivPoint? Across(int j, DivisionFrame f)
    {
        var candidates = f.Spindles.Where(s => (s.A == j || s.B == j) && f.Poles[s.A].P.Distance(f.Poles[s.B].P) > 0.05).ToList();
        if (candidates.Count == 0) return null;
        // (the most drawn; of equals, the first, as the Mac's max(by:) keeps)
        var best = candidates[0];
        foreach (var s in candidates.Skip(1)) if (s.Polar + s.Plate > best.Polar + best.Plate) best = s;
        var d = f.Poles[best.B].P - f.Poles[best.A].P;
        return new DivPoint(-d.Y, d.X) * (1 / d.Length);
    }

    /// <summary>The first part of a line, as a fraction of its length.</summary>
    public static DivPoint[] Trimmed(DivPoint[] pts, double f)
    {
        if (f <= 0 || pts.Length <= 1) return Array.Empty<DivPoint>();
        var total = DivGeometry.Length(pts);
        var want = total * Math.Min(f, 1);
        var outs = new List<DivPoint> { pts[0] };
        for (var i = 1; i < pts.Length; i++)
        {
            DivPoint a = pts[i - 1], b = pts[i];
            var d = a.Distance(b);
            if (d >= want)
            {
                outs.Add(a.Mixed(b, d > 0 ? want / d : 0));
                return outs.ToArray();
            }
            want -= d;
            outs.Add(b);
        }
        return outs.ToArray();
    }

    // ---- labels and the pointer ----

    /// <summary>Where a label points in a frame as drawn, and whether its part is shown.</summary>
    public static (DivPoint Point, bool Visible) Anchor(DivAnchor a, DivisionFrame f, DivChromatidShape[] shapes)
    {
        switch (a.Kind)
        {
            case DivAnchorKind.Point:
                return (a.P, true);
            case DivAnchorKind.Centromere:
                if (a.I < 0 || a.I >= shapes.Length) return (DivPoint.Zero, false);
                return (shapes[a.I].Points[shapes[a.I].Centromere], f.Chromatids[a.I].Alpha > 0.3);
            case DivAnchorKind.Arm:
            {
                if (a.I < 0 || a.I >= shapes.Length) return (DivPoint.Zero, false);
                var s = shapes[a.I];
                var j = s.Centromere + (int)Math.Round(a.T * (s.Points.Length - 1 - s.Centromere), MidpointRounding.AwayFromZero);
                return (s.Points[Math.Min(j, s.Points.Length - 1)], f.Chromatids[a.I].Alpha > 0.3);
            }
            case DivAnchorKind.ShortArm:
            {
                if (a.I < 0 || a.I >= shapes.Length) return (DivPoint.Zero, false);
                var s = shapes[a.I];
                var j = s.Centromere - (int)Math.Round(a.T * s.Centromere, MidpointRounding.AwayFromZero);
                return (s.Points[Math.Max(j, 0)], f.Chromatids[a.I].Alpha > 0.3);
            }
            case DivAnchorKind.Nucleus:
            {
                if (a.I < 0 || a.I >= f.Nuclei.Length) return (DivPoint.Zero, false);
                var n = f.Nuclei[a.I];
                return (n.C + DivPoint.Dir(a.T) * n.R, n.Alpha > 0.2);
            }
            case DivAnchorKind.Nucleolus:
            {
                if (a.I < 0 || a.I >= f.Nuclei.Length) return (DivPoint.Zero, false);
                var n = f.Nuclei[a.I];
                return (n.NucleolusCentre, n.NucleolusAlpha > 0.3 && n.Nucleolus > 0.02);
            }
            case DivAnchorKind.Pole:
                if (a.I < 0 || a.I >= f.Poles.Length) return (DivPoint.Zero, false);
                return (f.Poles[a.I].P, f.Plant || f.Poles[a.I].Centrosome > 0.3);
            case DivAnchorKind.Cell:
                if (a.I < 0 || a.I >= f.Cells.Length) return (DivPoint.Zero, false);
                return (f.Cells[a.I].Point(a.J % DivCellKey.Samples), true);
            case DivAnchorKind.Fibre:
            {
                if (a.I < 0 || a.I >= f.Chromatids.Length) return (DivPoint.Zero, false);
                var c = f.Chromatids[a.I];
                if (c.Pole < 0 || c.Pole >= f.Poles.Length) return (DivPoint.Zero, false);
                return (f.Poles[c.Pole].P.Mixed(shapes[a.I].Kinetochore, a.T), c.Fibre > 0.3 && c.Alpha > 0.3);
            }
            case DivAnchorKind.Polar:
            case DivAnchorKind.Plate:
            {
                if (a.I < 0 || a.I >= f.Spindles.Length) return (DivPoint.Zero, false);
                var sp = f.Spindles[a.I];
                DivPoint pa = f.Poles[sp.A].P, pb = f.Poles[sp.B].P;
                var d = pb - pa;
                var v = d.Length > 0 ? new DivPoint(-d.Y, d.X) * (1 / d.Length) : new DivPoint(1, 0);
                return a.Kind == DivAnchorKind.Polar
                    ? (pa.Mixed(pb, a.T) + v * 0.15, sp.Polar > 0.2)
                    : (pa.Mixed(pb, 0.5) + v * (a.T * sp.PlateLength / 2), sp.Plate > 0.3);
            }
            case DivAnchorKind.CellPlate:
                if (a.I < 0 || a.I >= f.CellPlates.Length) return (DivPoint.Zero, false);
                return (f.CellPlates[a.I].Point(a.T), f.CellPlates[a.I].Alpha > 0.2);
            default:
                if (a.I < 0 || a.I >= f.Furrows.Length) return (DivPoint.Zero, false);
                return (f.Furrows[a.I].Point(a.T), f.Furrows[a.I].Alpha > 0.2);
        }
    }

    /// <summary>The chromatid under a point (cell units), if one is within <paramref name="tolerance"/> of its line.</summary>
    public static int? Hit(DivPoint p, DivisionFrame f, DivChromatidShape[] shapes, double tolerance)
    {
        (int I, double D)? best = null;
        for (var i = 0; i < shapes.Length; i++)
        {
            if (f.Chromatids[i].Alpha <= 0.3) continue;
            var s = shapes[i];
            var d = double.PositiveInfinity;
            for (var k = 1; k < s.Points.Length; k++) d = Math.Min(d, Distance(p, s.Points[k - 1], s.Points[k]));
            if (d <= s.Width / 2 + tolerance && d < (best?.D ?? double.PositiveInfinity)) best = (i, d);
        }
        return best?.I;
    }

    /// <summary>From a point to a segment.</summary>
    public static double Distance(DivPoint p, DivPoint a, DivPoint b)
    {
        DivPoint ab = b - a, ap = p - a;
        var l2 = ab.X * ab.X + ab.Y * ab.Y;
        var t = l2 > 0 ? DivMath.Clamp01((ap.X * ab.X + ap.Y * ab.Y) / l2) : 0;
        return p.Distance(a + ab * t);
    }
}
