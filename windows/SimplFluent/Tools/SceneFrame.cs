using System.Windows.Media.Media3D;

namespace Simpl.Tools;

/// <summary>
/// How a 3D view frames its model — the same in every one of them: the biology toolkit's (BioSceneView, its labels kept
/// off the model), the element's atom, orbitals and electron cloud (Atom3DView) and the molecule builder's
/// (MoleculeSceneView). The model, with its labels, fills about <see cref="Fill"/> of the view where it meets the view's
/// edges first — its height in a view wider than it, its width in a narrow one — on first show, on Reset view and
/// whenever the view's size changes (a tool's popup sized by its edges, or with the window), a zoom of the student's own
/// kept as the same share of it. The lens is measured across the view's smaller side, so a tall narrow view does not cut
/// a model's sides off. (A tool's popup kept beside the pane once showed a double helix 40% of its view's height, small
/// in the middle with its labels round it: the owner's rule since is 80–85%.)
/// </summary>
internal static class SceneFrame
{
    /// <summary>The share of the view the model fills where it meets the view first.</summary>
    public const double Fill = 0.85;

    /// <summary>The least share a labelled model is left with for its labels' sake (the biology views): the camera stands
    /// back from <see cref="Fill"/> while its labels do not fit round the model, but no further than this, and those that
    /// still do not are folded into "+N more". (A cell's sixteen labels in columns beside it held the cell to half its
    /// view, small in the middle with its labels round it: the owner's complaint.)</summary>
    public const double Least = 0.75;

    /// <summary>WPF's field of view (measured across the view's width) for a lens of <paramref name="degrees"/> across the
    /// view's smaller side.</summary>
    public static double FieldOfView(double degrees, double width, double height) =>
        width > 0 && height > 0 && width > height ? 2 * Math.Atan(Math.Tan(degrees * Math.PI / 360) * width / height) * 180 / Math.PI : degrees;

    /// <summary>The tangents of half the lens across the view's width and its height, the lens of <paramref name="degrees"/>
    /// across its smaller side.</summary>
    public static (double X, double Y) HalfTangents(double degrees, double width, double height)
    {
        var t = Math.Tan(degrees * Math.PI / 360);
        if (width <= 0 || height <= 0) return (t, t);
        return width >= height ? (t * width / height, t) : (t, t * height / width);
    }

    /// <summary>How far from its centre the camera stands for a ball of <paramref name="radius"/> about it to fill
    /// <paramref name="fill"/> of the view's smaller side, however it is turned (an atom turned by a drag, its rings at any
    /// slant): its edge, seen at the angle whose tangent is radius / √(d² − radius²), there.</summary>
    public static double Ball(double radius, double degrees, double fill = Fill)
    {
        var k = fill * Math.Tan(degrees * Math.PI / 360);
        return Math.Max(radius, 1e-3) * Math.Sqrt(1 + 1 / (k * k));
    }

    /// <summary>
    /// How far from the centre the camera stands, looking along −z, for <paramref name="points"/> (from the centre: a
    /// model's atoms' and lobes' outermost points) to fill <paramref name="fill"/> of the view where they meet its edges
    /// first, as they stand — or, <paramref name="upright"/>, at any turn about the view's up (a molecule turning on its
    /// own, an atom's orbitals revolving): a point r from that axis then shows at most r / √(d² − r²) across, the edge of
    /// the round it turns on, and its height at the nearest it comes, d − r.
    /// </summary>
    public static double Points(IEnumerable<Vector3D> points, double degrees, double width, double height, bool upright, double fill = Fill)
    {
        var (tx, ty) = HalfTangents(degrees, width, height);
        double ax = fill * tx, ay = fill * ty, d = 0, reach = 0;
        foreach (var p in points)
        {
            if (upright)
            {
                var r = Math.Sqrt(p.X * p.X + p.Z * p.Z);
                d = Math.Max(d, Math.Max(r * Math.Sqrt(1 + 1 / (ax * ax)), r + Math.Abs(p.Y) / ay));
                reach = Math.Max(reach, Math.Sqrt(r * r + p.Y * p.Y));
            }
            else
            {
                d = Math.Max(d, p.Z + Math.Max(Math.Abs(p.X) / ax, Math.Abs(p.Y) / ay));
                reach = Math.Max(reach, p.Length);
            }
        }
        // (never inside the model)
        return Math.Max(d, reach * 1.05);
    }

    /// <summary>
    /// <see cref="Points"/> as they stand, the camera moved across and up to look at their middle: where it can stand
    /// nearest with every point inside the view, each axis on its own (<see cref="Middle"/>). A model off its own centre
    /// — a molecule's atoms about their mean, an amino acid in its ball — framed from the centre filled 85% of the view on
    /// one side and 65% on the other. Returns the distance and where the camera looks, from the centre.
    /// </summary>
    public static (double Distance, Vector3D Aim) PointsCentred(IReadOnlyCollection<Vector3D> points, double degrees, double width, double height, double fill = Fill)
    {
        if (points.Count == 0) return (0, new Vector3D());
        var (tx, ty) = HalfTangents(degrees, width, height);
        double ax = fill * tx, ay = fill * ty;
        var aim = new Vector3D(Middle(points.Select(p => (p.Z, p.X)), 1 / ax), Middle(points.Select(p => (p.Z, p.Y)), 1 / ay), 0);
        double d = 0, reach = 0;
        foreach (var p in points)
        {
            d = Math.Max(d, p.Z + Math.Max(Math.Abs(p.X - aim.X) / ax, Math.Abs(p.Y - aim.Y) / ay));
            reach = Math.Max(reach, (p - aim).Length);
        }
        // (never inside the model)
        return (Math.Max(d, reach * 1.05), aim);
    }

    /// <summary>Along one of the view's axes, where the camera looks so it can stand nearest with every point inside the
    /// view: the least, over aims o, of the most of a + k·|x − o| (a point's depth term a, its place x), found by thirds.</summary>
    public static double Middle(IEnumerable<(double A, double X)> points, double k)
    {
        var list = points.ToList();
        if (list.Count == 0) return 0;
        double lo = list.Min(p => p.X), hi = list.Max(p => p.X);
        double Most(double o) => list.Max(p => p.A + k * Math.Abs(p.X - o));
        for (var i = 0; i < 48 && hi - lo > 1e-9; i++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (Most(m1) <= Most(m2)) hi = m2; else lo = m1;
        }
        return (lo + hi) / 2;
    }
}
