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
}
