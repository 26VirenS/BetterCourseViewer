using System.Globalization;
using System.Numerics;

namespace Simpl.Tools;

// The Cell Explorer's organelles as meshes (spec §5.1.5's recipes, exact geometry), the Mac's CellExplorerGeometry.swift
// recipe for recipe: the cut shells (their outer and inner walls and the cut faces, each shaded on its own), the
// nucleus's envelope with its pores, a Golgi stack's curved cisternae, the mitochondrion with its cristae (whole, or cut
// lengthwise), the chloroplast with its grana and lamellae, lysosomes and peroxisomes, the centrioles' nine triplets, and
// the small things the cell is full of. Each organelle is made once in its own frame (its long or thin axis along y) and
// placed by CellExplorerBuild.cs. Lengths in µm. Plain .NET.

public static class CEGeo
{
    public static double Smoothstep(double e0, double e1, double x)
    {
        var t = Math.Min(Math.Max((x - e0) / (e1 - e0), 0), 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Whether a point is in the cutaway's wedge about <paramref name="c"/>: the octant x, y, z &gt; 0 (§3.4 item 3).</summary>
    public static bool InWedge(BioVec p, BioVec c = default) => p.X > c.X && p.Y > c.Y && p.Z > c.Z;

    /// <summary>How far a point is from the wedge about <paramref name="c"/> (0 inside it).</summary>
    public static double WedgeDistance(BioVec p, BioVec c = default)
    {
        var d = p - c;
        if (d.X > 0 && d.Y > 0 && d.Z > 0) return 0;
        return BioV.Length(new BioVec(Math.Min(d.X, 0), Math.Min(d.Y, 0), Math.Min(d.Z, 0)));
    }

    /// <summary>The triangles whose centroid <paramref name="keep"/> accepts, the vertices no triangle uses dropped.</summary>
    public static BioMesh Filtered(BioMesh m, Func<BioVec, bool> keep)
    {
        var out_ = new BioMesh();
        var map = new int[m.Positions.Count];
        Array.Fill(map, -1);
        for (var t = 0; t + 2 < m.Indices.Count; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            var centroid = (BioMesh.D(m.Positions[a]) + BioMesh.D(m.Positions[b]) + BioMesh.D(m.Positions[c])) / 3;
            if (!keep(centroid)) continue;
            foreach (var i in new[] { a, b, c })
            {
                if (map[i] < 0)
                {
                    map[i] = out_.Positions.Count;
                    out_.Positions.Add(m.Positions[i]);
                    out_.Normals.Add(m.Normals[i]);
                }
                out_.Indices.Add(map[i]);
            }
        }
        return out_;
    }

    /// <summary>A mesh moved (its positions only: quicker than a frame, for the explode slider); itself when not moved.</summary>
    public static BioMesh Moved(BioMesh m, BioVec d)
    {
        if (d.X == 0 && d.Y == 0 && d.Z == 0) return m;
        var f = BioMesh.F(d);
        var out_ = m.Copy();
        for (var i = 0; i < out_.Positions.Count; i++) out_.Positions[i] += f;
        return out_;
    }

    /// <summary>A shell made by BioShapes.Radial (centred on the origin) split into its outer wall, its inner wall and its
    /// cut faces, by which way each triangle faces: out, in, or across.</summary>
    public static (BioMesh Outer, BioMesh Inner, BioMesh Caps) SplitShell(BioMesh m)
    {
        var outer = new Builder(m);
        var inner = new Builder(m);
        var caps = new Builder(m);
        for (var t = 0; t + 2 < m.Indices.Count; t += 3)
        {
            int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
            var centre = (BioMesh.D(m.Positions[a]) + BioMesh.D(m.Positions[b]) + BioMesh.D(m.Positions[c])) / 3;
            var n = BioMesh.D(m.Normals[a]) + BioMesh.D(m.Normals[b]) + BioMesh.D(m.Normals[c]);
            var facing = BioV.Dot(BioV.Unit(n), BioV.Unit(centre));
            (facing > 0.1 ? outer : facing < -0.1 ? inner : caps).Add(a, b, c);
        }
        return (outer.Mesh, inner.Mesh, caps.Mesh);
    }

    /// <summary>A mesh of some of another's triangles, each vertex copied once.</summary>
    private sealed class Builder
    {
        private readonly BioMesh from;
        private readonly Dictionary<int, int> map = new();
        public BioMesh Mesh { get; } = new();

        public Builder(BioMesh from) => this.from = from;

        public void Add(params int[] ids)
        {
            foreach (var i in ids)
            {
                if (!map.TryGetValue(i, out var j))
                {
                    j = Mesh.Positions.Count;
                    Mesh.Positions.Add(from.Positions[i]);
                    Mesh.Normals.Add(from.Normals[i]);
                    map[i] = j;
                }
                Mesh.Indices.Add(j);
            }
        }
    }

    /// <summary>A unit vector at right angles to <paramref name="d"/>.</summary>
    public static BioVec Perpendicular(BioVec d) => BioV.Unit(BioV.Cross(d, Math.Abs(d.Y) < 0.9 ? new BioVec(0, 1, 0) : new BioVec(1, 0, 0)));

    /// <summary>A direction within <paramref name="degrees"/> of <paramref name="d"/>, uniform over that cone: two draws (cos of
    /// the angle from d, then the azimuth about it).</summary>
    public static BioVec Cone(BioVec d, double degrees, ref BioRandom rng)
    {
        var cmax = Math.Cos(degrees * Math.PI / 180);
        var c = 1 - rng.Uniform() * (1 - cmax);
        var phi = 2 * Math.PI * rng.Uniform();
        var s = Math.Sqrt(Math.Max(0, 1 - c * c));
        var e1 = Perpendicular(d);
        var e2 = BioV.Cross(BioV.Unit(d), e1);
        return BioV.Unit(BioV.Unit(d) * c + (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * s);
    }

    /// <summary><paramref name="d"/> turned by exactly <paramref name="degrees"/> toward an azimuth drawn about it (one draw).</summary>
    public static BioVec Turned(BioVec d, double degrees, ref BioRandom rng)
    {
        var phi = 2 * Math.PI * rng.Uniform();
        var e1 = Perpendicular(d);
        var e2 = BioV.Cross(BioV.Unit(d), e1);
        var a = degrees * Math.PI / 180;
        return BioV.Unit(BioV.Unit(d) * Math.Cos(a) + (e1 * Math.Cos(phi) + e2 * Math.Sin(phi)) * Math.Sin(a));
    }

    /// <summary>A frame turned by a random rotation (BioRandom.Rotation's), at a point.</summary>
    public static BioFrame Frame((BioVec Axis, double Degrees) r, BioVec p) => BioFrame.Rotation(r.Axis, r.Degrees, p);

    /// <summary>A frame at <paramref name="p"/> with y along <paramref name="y"/> and z as near <paramref name="towardZ"/> as y
    /// allows (a cut organelle's cut half facing it).</summary>
    public static BioFrame Frame(BioVec y, BioVec towardZ, BioVec p)
    {
        var yy = BioV.Unit(y);
        var zz = towardZ - yy * BioV.Dot(towardZ, yy);
        zz = BioV.Length(zz) > 1e-6 ? BioV.Unit(zz) : Perpendicular(yy);
        return new BioFrame(p, BioV.Cross(yy, zz), yy, zz);
    }

    /// <summary>"#rrggbb" mixed toward another by t.</summary>
    public static string Mix(string a, string b, double t)
    {
        var x = BioPalette.Rgb(a);
        var y = BioPalette.Rgb(b);
        static string Byte(double v) => Math.Min(Math.Max((int)Math.Round(v * 255, MidpointRounding.AwayFromZero), 0), 255).ToString("x2", CultureInfo.InvariantCulture);
        return "#" + Byte(x.R + (y.R - x.R) * t) + Byte(x.G + (y.G - x.G) * t) + Byte(x.B + (y.B - x.B) * t);
    }

    /// <summary>A lighter tint of a colour pair (a cut face, §5.1.5 "Membranes and walls").</summary>
    public static BioHexPair Lighter(BioHexPair p, double t = 0.45) => new(Mix(p.Light, "#ffffff", t), Mix(p.Dark, "#ffffff", t * 0.7));

    /// <summary>A darker shade (the plant wall's outer face, a lysosome's contents).</summary>
    public static BioHexPair Darker(BioHexPair p, double t = 0.2) => new(Mix(p.Light, "#000000", t), Mix(p.Dark, "#000000", t * 0.8));

    /// <summary>A rounded box's signed distance (half extents, corner radius).</summary>
    public static double RoundedBoxSdf(BioVec p, BioVec half, double corner)
    {
        var rc = Math.Min(corner, Math.Min(half.X, Math.Min(half.Y, half.Z)));
        var q = new BioVec(Math.Abs(p.X) - (half.X - rc), Math.Abs(p.Y) - (half.Y - rc), Math.Abs(p.Z) - (half.Z - rc));
        var outside = BioV.Length(new BioVec(Math.Max(q.X, 0), Math.Max(q.Y, 0), Math.Max(q.Z, 0)));
        return outside + Math.Min(Math.Max(q.X, Math.Max(q.Y, q.Z)), 0) - rc;
    }

    /// <summary>A capsule along x (its axis from −half to +half), radius r: signed distance.</summary>
    public static double CapsuleXSdf(BioVec p, double half, double r)
    {
        var x = Math.Min(Math.Max(p.X, -half), half);
        return BioV.Length(p - new BioVec(x, 0, 0)) - r;
    }

    /// <summary>A capsule along x's radius function (BioShapes' capsule stands along y).</summary>
    public static BioRadius CapsuleXRadius(double r, double length)
    {
        var f = BioShapes.CapsuleRadius(r, length);
        return d => f(new BioVec(d.Y, d.X, d.Z));
    }

    /// <summary>The outward unit normal of a surface given as a signed distance, numerically.</summary>
    public static BioVec Gradient(Func<BioVec, double> f, BioVec p, double h = 1e-4)
    {
        var g = new BioVec(f(p + new BioVec(h, 0, 0)) - f(p - new BioVec(h, 0, 0)),
            f(p + new BioVec(0, h, 0)) - f(p - new BioVec(0, h, 0)),
            f(p + new BioVec(0, 0, h)) - f(p - new BioVec(0, 0, h)));
        return BioV.Unit(g);
    }

    /// <summary>Points along a cubic Bézier.</summary>
    public static List<BioVec> Bezier(BioVec p0, BioVec p1, BioVec p2, BioVec p3, int points)
    {
        var out_ = new List<BioVec>(points);
        for (var i = 0; i < points; i++)
        {
            double t = (double)i / Math.Max(points - 1, 1), u = 1 - t;
            out_.Add(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
        }
        return out_;
    }

    /// <summary>The area and enclosed volume of a closed mesh (the divergence theorem).</summary>
    public static (double Area, double Volume) AreaVolume(BioMesh m)
    {
        double a = 0, v = 0;
        for (var t = 0; t + 2 < m.Indices.Count; t += 3)
        {
            BioVec p0 = BioMesh.D(m.Positions[m.Indices[t]]), p1 = BioMesh.D(m.Positions[m.Indices[t + 1]]), p2 = BioMesh.D(m.Positions[m.Indices[t + 2]]);
            a += BioV.Length(BioV.Cross(p1 - p0, p2 - p0)) / 2;
            v += BioV.Dot(p0, BioV.Cross(p1, p2)) / 6;
        }
        return (a, Math.Abs(v));
    }

    /// <summary>A box (8 corners, flat faces): a peroxisome's crystalline core, a cholesterol rod.</summary>
    public static BioMesh Box(BioVec h)
    {
        var m = new BioMesh();
        BioVec C(double x, double y, double z) => new(x * h.X, y * h.Y, z * h.Z);
        m.FlatQuad(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), new BioVec(1, 0, 0));
        m.FlatQuad(C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), new BioVec(-1, 0, 0));
        m.FlatQuad(C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), new BioVec(0, 1, 0));
        m.FlatQuad(C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), new BioVec(0, -1, 0));
        m.FlatQuad(C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), new BioVec(0, 0, 1));
        m.FlatQuad(C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), new BioVec(0, 0, -1));
        return m;
    }

    /// <summary>A flat strip from a to b (a stroma lamella): <paramref name="width"/> across (along <paramref name="side"/>),
    /// <paramref name="thickness"/> thick.</summary>
    public static BioMesh Slab(BioVec a, BioVec b, BioVec side, double width, double thickness)
    {
        var along = BioV.Unit(b - a);
        var s = BioV.Unit(side - along * BioV.Dot(side, along)) * (width / 2);
        var up = BioV.Unit(BioV.Cross(along, s)) * (thickness / 2);
        var m = new BioMesh();
        var corners = new List<BioVec>();
        foreach (var end in new[] { a, b })
        {
            corners.Add(end + s + up);
            corners.Add(end - s + up);
            corners.Add(end - s - up);
            corners.Add(end + s - up);
        }
        var mid = (a + b) / 2;
        foreach (var (i, j, k, l) in new[] { (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (0, 3, 2, 1), (4, 5, 6, 7) })
        {
            var face = (corners[i] + corners[j] + corners[k] + corners[l]) / 4;
            m.FlatQuad(corners[i], corners[j], corners[k], corners[l], face - mid);
        }
        return m;
    }

    /// <summary>An octahedron (8 triangles): a ribosome drawn at its true size, where there are thousands (§4.1 item 7).</summary>
    public static BioMesh Octahedron(double r)
    {
        var m = new BioMesh();
        var v = new[] { new BioVec(1, 0, 0), new BioVec(-1, 0, 0), new BioVec(0, 1, 0), new BioVec(0, -1, 0), new BioVec(0, 0, 1), new BioVec(0, 0, -1) };
        foreach (var p in v) m.Vertex(p * r, p);
        foreach (var (a, b, c) in new[] { (0, 2, 4), (4, 2, 1), (1, 2, 5), (5, 2, 0), (4, 3, 0), (1, 3, 4), (5, 3, 1), (0, 3, 5) }) m.Triangle(a, b, c);
        return m;
    }

    /// <summary>A tetrahedron (4 triangles, its corners on a sphere of radius r): a dot smaller than a pixel, by the thousand.</summary>
    public static BioMesh Tetrahedron(double r)
    {
        var m = new BioMesh();
        var v = new[] { new BioVec(1, 1, 1), new BioVec(1, -1, -1), new BioVec(-1, 1, -1), new BioVec(-1, -1, 1) }.Select(BioV.Unit).ToArray();
        foreach (var p in v) m.Vertex(p * r, p);
        foreach (var (a, b, c) in new[] { (0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2) })
        {
            // (wound to face out)
            var n = BioV.Cross(v[b] - v[a], v[c] - v[a]);
            if (BioV.Dot(n, v[a] + v[b] + v[c]) >= 0) m.Triangle(a, b, c); else m.Triangle(a, c, b);
        }
        return m;
    }
}

// ---- organelles in their own frames (§5.1.5) ----

/// <summary>A piece's colour: the part's own (Body), a cut face's lighter tint (Cap), a darker shade (Dark), another colour
/// the part names (a nucleus's nucleoplasm, a chloroplast's grana or stroma; "matrix" a lighter tint), or one of its own.</summary>
public readonly record struct CERole(string Kind, string? Name = null, BioHexPair? Own = null)
{
    public static readonly CERole Body = new("body");
    public static readonly CERole Cap = new("cap");
    public static readonly CERole Dark = new("dark");
    public static CERole Named(string name) => new("named", name);
    public static CERole Colour(BioHexPair c) => new("colour", null, c);
}

/// <summary>One organelle's look: its pieces, each a mesh and how it is shaded (filled in with the part's colours when
/// placed). A piece's Alpha null is the part's own (opaque, or §5.1's translucency where it has one).</summary>
public sealed class CEShape
{
    public sealed record Piece(BioMesh Mesh, CERole Role, double? Alpha, double Roughness, int Order);

    public List<Piece> Pieces { get; } = new();

    public void Add(BioMesh m, CERole? role = null, double? alpha = null, double roughness = 0.5, int order = 0)
    {
        if (m.IsEmpty) return;
        Pieces.Add(new Piece(m, role ?? CERole.Body, alpha, roughness, order));
    }

    /// <summary>Placed by a frame.</summary>
    public CEShape Placed(BioFrame f)
    {
        var s = new CEShape();
        s.Pieces.AddRange(Pieces.Select(p => p with { Mesh = p.Mesh.Transformed(f) }));
        return s;
    }

    public int Triangles => Pieces.Sum(p => p.Mesh.TriangleCount);
}

public static class CERecipes
{
    // ---- shells ----

    /// <summary>A shell (its outer wall at <paramref name="outer"/>, its inner at <paramref name="inner"/>), cut by the
    /// cutaway's octant if asked, as three pieces: outer wall, inner wall, cut faces (§5.1.5: the cut face a lighter tint).</summary>
    public static (BioMesh Outer, BioMesh Inner, BioMesh Caps) Shell(BioRadius outer, BioRadius inner, bool cut, int rings, int segments) =>
        CEGeo.SplitShell(BioShapes.Radial(outer, rings, segments, cut ? BioCut.Octant : null, inner));

    /// <summary>
    /// A box-like star-shaped surface (a rounded box, the plant's wall) meshed on a box's six faces rather than a sphere's
    /// latitudes and longitudes, so its edges and corners run along grid lines instead of across them: each face of the
    /// box <paramref name="h"/> (the shape's own proportions) is an n × n grid, every grid point sent along its direction to
    /// <paramref name="radius"/>. Cut, the octant x, y, z &gt; 0 is left out (its boundaries are grid lines: n even).
    /// </summary>
    public static BioMesh BoxSurface(BioVec h, int n, bool cut, BioRadius radius, bool inward = false)
    {
        var faces = new (BioVec N, BioVec U, BioVec V)[]
        {
            (new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), (new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
            (new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)), (new(0, -1, 0), new(1, 0, 0), new(0, 0, 1)),
            (new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)), (new(0, 0, -1), new(0, 1, 0), new(1, 0, 0)),
        };
        var m = Math.Max(2, n + n % 2);
        var mesh = new BioMesh();
        foreach (var f in faces)
        {
            BioVec Q(double s, double t)
            {
                var p = f.N + f.U * s + f.V * t;
                return new BioVec(p.X * h.X, p.Y * h.Y, p.Z * h.Z);
            }
            BioVec Point(double s, double t)
            {
                var d = BioV.Unit(Q(s, t));
                return d * radius(d);
            }
            var b = mesh.Positions.Count;
            const double e = 1e-4;
            for (var j = 0; j <= m; j++)
                for (var i = 0; i <= m; i++)
                {
                    double s = -1 + 2.0 * i / m, t = -1 + 2.0 * j / m;
                    var p = Point(s, t);
                    var nrm = BioV.Cross(Point(s + e, t) - Point(s - e, t), Point(s, t + e) - Point(s, t - e));
                    if (BioV.Dot(nrm, p) < 0) nrm = -nrm;
                    mesh.Vertex(p, inward ? -nrm : nrm);
                }
            for (var j = 0; j < m; j++)
                for (var i = 0; i < m; i++)
                {
                    double s = -1 + 2 * (i + 0.5) / m, t = -1 + 2 * (j + 0.5) / m;
                    if (cut && CEGeo.InWedge(Q(s, t))) continue;
                    int a = b + j * (m + 1) + i, c1 = a + 1, c = a + m + 1 + 1, d = a + m + 1;
                    if (inward)
                    {
                        mesh.Triangle(a, c, c1);
                        mesh.Triangle(a, d, c);
                    }
                    else
                    {
                        mesh.Triangle(a, c1, c);
                        mesh.Triangle(a, c, d);
                    }
                }
        }
        return mesh;
    }

    /// <summary>The cut faces of a box-meshed shell (BoxSurface's), between its outer and inner walls, through the very grid
    /// points its walls' cut edges have: on the plane x = 0 from +y to +z, on y = 0 from +z to +x, on z = 0 from +x to +y.</summary>
    public static BioMesh BoxCaps(BioVec h, int n, BioRadius outer, BioRadius inner)
    {
        var m = Math.Max(2, n + n % 2) / 2;
        var mesh = new BioMesh();
        // (each plane's edge: along one face from its axis to the box's edge, then down the next face to the other axis)
        var edges = new (BioVec Facing, Func<double, BioVec> A, Func<double, BioVec> B)[]
        {
            (new BioVec(1, 0, 0), k => new BioVec(0, 1, k), k => new BioVec(0, k, 1)),
            (new BioVec(0, 1, 0), k => new BioVec(k, 0, 1), k => new BioVec(1, 0, k)),
            (new BioVec(0, 0, 1), k => new BioVec(1, k, 0), k => new BioVec(k, 1, 0)),
        };
        foreach (var e in edges)
        {
            var dirs = new List<BioVec>();
            for (var i = 0; i <= m; i++) dirs.Add(e.A((double)i / m));
            for (var i = m - 1; i >= 0; i--) dirs.Add(e.B((double)i / m));
            var ds = dirs.Select(d => BioV.Unit(new BioVec(d.X * h.X, d.Y * h.Y, d.Z * h.Z))).ToList();
            for (var k = 0; k < ds.Count - 1; k++)
            {
                BioVec d0 = ds[k], d1 = ds[k + 1];
                mesh.FlatQuad(d0 * outer(d0), d1 * outer(d1), d1 * inner(d1), d0 * inner(d0), e.Facing);
            }
        }
        return mesh;
    }

    /// <summary>The cut faces of a cell's inside, between <paramref name="inner"/> (the vacuole, or nothing: the centre) and
    /// <paramref name="outer"/> (the membrane's inner wall), on the three planes of the octant cut: the cytoplasm's "tinted
    /// cap" (§5.1.1).</summary>
    public static BioMesh CutFaces(BioRadius outer, BioRadius? inner, int steps = 24)
    {
        var m = new BioMesh();
        var half = Math.PI / 2;
        // (each face: an edge of the cut swept from the plane's one axis to its other)
        var faces = new (Func<double, BioVec> Dir, BioVec Facing)[]
        {
            (a => new BioVec(0, Math.Cos(a), Math.Sin(a)), new BioVec(1, 0, 0)),
            (a => new BioVec(Math.Cos(a), Math.Sin(a), 0), new BioVec(0, 0, 1)),
            (a => new BioVec(Math.Sin(a), 0, Math.Cos(a)), new BioVec(0, 1, 0)),
        };
        foreach (var f in faces)
            for (var k = 0; k < steps; k++)
            {
                double a0 = half * k / steps, a1 = half * (k + 1) / steps;
                BioVec d0 = f.Dir(a0), d1 = f.Dir(a1);
                BioVec o0 = d0 * outer(d0), o1 = d1 * outer(d1);
                if (inner != null) m.FlatQuad(o0, o1, d1 * inner(d1), d0 * inner(d0), f.Facing);
                else m.FlatTriangle(BioVec.Zero, o0, o1, f.Facing);
            }
        return m;
    }

    // ---- nucleus ----

    /// <summary>§5.1.5 Nucleus: the pores' directions, M points on the Fibonacci lattice.</summary>
    public static List<BioVec> PoreDirections(int m = 110)
    {
        var out_ = new List<BioVec>(m);
        for (var i = 0; i < m; i++)
        {
            var y = 1 - (i + 0.5) * 2 / m;
            var rho = Math.Sqrt(Math.Max(0, 1 - y * y));
            var phi = i * 2.39996323;
            out_.Add(new BioVec(rho * Math.Cos(phi), y, rho * Math.Sin(phi)));
        }
        return out_;
    }

    /// <summary>The nuclear envelope about the origin: two membranes (outer at R, inner 0.05 µm in, each drawn 0.012 µm
    /// thick) with a hole of radius 0.06 µm through both at each pore (triangles whose centroid is that near a pore's axis
    /// left out), cut if asked; and the nucleoplasm inside, translucent.</summary>
    public static CEShape Envelope(double r, IReadOnlyList<BioVec> pores, bool cut)
    {
        var s = new CEShape();
        BioMesh Holed(BioMesh m) => CEGeo.Filtered(m, c =>
        {
            foreach (var p in pores)
            {
                var along = BioV.Dot(c, p);
                if (along <= 0) continue;
                if (BioV.Length(c - p * along) < 0.06) return false;
            }
            return true;
        });
        foreach (var (o, i) in new[] { (r, r - 0.012), (r - 0.05, r - 0.062) })
        {
            var sh = Shell(BioShapes.SphereRadius(o), BioShapes.SphereRadius(i), cut, 40, 80);
            s.Add(Holed(sh.Outer), roughness: 0.45);
            s.Add(Holed(sh.Inner), roughness: 0.45);
            s.Add(sh.Caps, CERole.Cap, alpha: 1, roughness: 0.5);
        }
        var plasm = CEGeo.SplitShell(BioShapes.Radial(BioShapes.SphereRadius(r - 0.07), 20, 40, cut ? BioCut.Octant : null));
        s.Add(plasm.Outer, CERole.Named("nucleoplasm"), alpha: 0.3, roughness: 0.3, order: 1);
        s.Add(plasm.Caps, CERole.Named("nucleoplasm"), alpha: 0.42, roughness: 0.3, order: 1);
        return s;
    }

    /// <summary>A nuclear pore's ring (torus R 0.07, r 0.02), its axis along y.</summary>
    public static readonly BioMesh PoreRing = BioShapes.Torus(0.07, 0.02, 10, 4);

    // ---- Golgi ----

    /// <summary>A Golgi stack about the origin, its axis along +y (cis face at −y, toward the nucleus): <paramref name="count"/>
    /// cisternae (§5.1.5: a rounded-rectangle footprint long × short for the middle one, scaled by 1 − 0.08·|j − mid|, 0.07 µm
    /// thick with rims swollen ×1.8 in the outer 15%, bent into a bowl by d²/(2·2.2 µm), <paramref name="spacing"/> apart).</summary>
    public static CEShape Golgi(int count, double longSide, double shortSide, double thickness, double spacing, int rings = 8, int segments = 36)
    {
        var s = new CEShape();
        var mid = (count - 1) / 2.0;
        for (var j = 0; j < count; j++)
        {
            var scale = 1 - 0.08 * Math.Abs(j - mid);
            s.Add(Cisterna(longSide / 2 * scale, shortSide / 2 * scale, (j - mid) * spacing, thickness / 2, rings, segments), roughness: 0.45);
        }
        return s;
    }

    /// <summary>One cisterna: a superellipse footprint (exponent 4: a rounded rectangle) of semi-axes a (x) and b (z), lifted
    /// into a bowl, its two faces meeting at a rounded rim.</summary>
    public static BioMesh Cisterna(double a, double b, double y0, double halfThickness, int rings = 8, int segments = 36)
    {
        (double, double) Foot(double r, double psi)
        {
            double c = Math.Cos(psi), s = Math.Sin(psi);
            return (a * r * Math.Sign(c) * Math.Sqrt(Math.Abs(c)), b * r * Math.Sign(s) * Math.Sqrt(Math.Abs(s)));
        }
        double Half(double r)
        {
            var swell = 1 + 0.8 * CEGeo.Smoothstep(0.78, 0.88, r);
            var edge = r <= 0.9 ? 1 : Math.Sqrt(Math.Max(0, 1 - Math.Pow((r - 0.9) / 0.1, 2)));
            return halfThickness * swell * edge;
        }
        var m = new BioMesh();
        foreach (var side in new[] { 1.0, -1.0 })
        {
            var g = new BioMesh();
            for (var i = 0; i <= rings; i++)
            {
                var r = (double)i / rings;
                for (var j = 0; j < segments; j++)
                {
                    var (x, z) = Foot(r, 2 * Math.PI * j / segments);
                    var d2 = x * x + z * z;
                    g.Vertex(new BioVec(x, y0 + d2 / (2 * 2.2) + side * Half(r), z), new BioVec(0, side, 0));
                }
            }
            for (var i = 0; i < rings; i++)
                for (var j = 0; j < segments; j++)
                {
                    int p = i * segments + j, q = i * segments + (j + 1) % segments;
                    int r = (i + 1) * segments + j, t = (i + 1) * segments + (j + 1) % segments;
                    g.Triangle(p, r, t);
                    g.Triangle(p, t, q);
                }
            g = BioShapes.SmoothNormals(g);
            // (the top faces +y, the bottom −y)
            var probe = g.Normals[segments * (rings / 2)];
            if (probe.Y * side < 0) g = g.Flipped();
            m.Append(g);
        }
        return m;
    }

    // ---- mitochondrion ----

    /// <summary>
    /// §5.1.5 Mitochondrion, its long axis along y: the outer membrane (capsule r 0.45, cylinder 1.3: 2.2 µm long)
    /// translucent, the inner (r 0.40) opaque. Whole, the cristae are hidden inside the opaque inner membrane and are left
    /// out; cut lengthwise (the half z &gt; 0 removed), both membranes are shells with their cut faces, the inner's inside is
    /// the matrix's tint, and the nine cristae show. <paramref name="fine"/>: meshed finer, for Look inside.
    /// </summary>
    public static CEShape Mitochondrion(bool half, bool fine = false) => !half ? MitoWhole.Value : fine ? MitoHalfFine.Value : MitoHalf.Value;

    private static readonly Lazy<CEShape> MitoWhole = new(() =>
    {
        var s = new CEShape();
        const double l = 1.3;
        s.Add(BioShapes.Capsule(0.45, l, 10, 20), alpha: 0.45, roughness: 0.3, order: 3);
        s.Add(BioShapes.Capsule(0.40, l, 10, 20), roughness: 0.55);
        return s;
    });

    private static readonly Lazy<CEShape> MitoHalf = new(() => MakeMitoHalf(12, 24));
    private static readonly Lazy<CEShape> MitoHalfFine = new(() => MakeMitoHalf(24, 48));

    private static CEShape MakeMitoHalf(int rings, int segments)
    {
        var s = new CEShape();
        const double l = 1.3;
        var o = CEGeo.SplitShell(BioShapes.Radial(BioShapes.CapsuleRadius(0.45, l), rings, segments, BioCut.Half, BioShapes.CapsuleRadius(0.435, l)));
        s.Add(o.Outer, alpha: 0.45, roughness: 0.3, order: 3);
        s.Add(o.Inner, alpha: 0.45, roughness: 0.3, order: 3);
        s.Add(o.Caps, CERole.Cap, alpha: 1);
        var i = CEGeo.SplitShell(BioShapes.Radial(BioShapes.CapsuleRadius(0.40, l), rings, segments, BioCut.Half, BioShapes.CapsuleRadius(0.38, l)));
        s.Add(i.Outer, roughness: 0.55);
        s.Add(i.Inner, CERole.Named("matrix"), roughness: 0.6);
        s.Add(i.Caps, CERole.Cap);
        var cristae = new BioMesh();
        for (var k = 0; k < 9; k++)
        {
            var y = -0.55 * l + 1.1 * l * (k + 1) / 10;
            cristae.Append(Crista(y, k % 2 == 1, true));
        }
        s.Add(cristae, CERole.Dark, roughness: 0.55);
        return s;
    }

    /// <summary>A crista at height y: a flat sheet across the inner membrane (radius 0.39, into the membrane) from its
    /// attached side to 75% of the inner diameter, 0.05 µm thick, its free edge rounded; only z ≤ 0 when cut.</summary>
    public static BioMesh Crista(double y, bool mirrored, bool half)
    {
        const double r = 0.39;
        const double free = -0.40 + 0.75 * 0.80;
        var a0 = Math.Acos(free / r);
        double start = half ? Math.PI : a0, end = 2 * Math.PI - a0;
        var outline = new List<(double, double)>();
        const int n = 14;
        for (var k = 0; k <= n; k++)
        {
            var a = start + (end - start) * k / n;
            outline.Add((r * Math.Cos(a), r * Math.Sin(a)));
        }
        // (the free edge, along x = free back up to z = 0 or the circle's top; then, cut, along z = 0)
        if (half) outline.Add((free, 0));
        var sign = mirrored ? -1.0 : 1.0;
        var pts = outline.Select(o => new BioVec(sign * -o.Item1, 0, o.Item2)).ToList();
        const double ht = 0.025;
        var m = new BioMesh();
        var centre = pts.Aggregate(BioVec.Zero, (acc, p) => acc + p) / pts.Count;
        for (var k = 0; k < pts.Count; k++)
        {
            BioVec a = pts[k], b = pts[(k + 1) % pts.Count];
            m.FlatTriangle(centre + new BioVec(0, y + ht, 0), a + new BioVec(0, y + ht, 0), b + new BioVec(0, y + ht, 0), new BioVec(0, 1, 0));
            m.FlatTriangle(centre + new BioVec(0, y - ht, 0), a + new BioVec(0, y - ht, 0), b + new BioVec(0, y - ht, 0), new BioVec(0, -1, 0));
            var out_ = BioV.Cross(b - a, new BioVec(0, 1, 0));
            var facing = BioV.Dot(out_, (a + b) / 2 - centre) >= 0 ? out_ : -out_;
            m.FlatQuad(a + new BioVec(0, y + ht, 0), b + new BioVec(0, y + ht, 0), b + new BioVec(0, y - ht, 0), a + new BioVec(0, y - ht, 0), facing);
        }
        // (the free edge rounded: a rod along it)
        var zTop = half ? 0.0 : Math.Sqrt(r * r - free * free);
        var zBottom = -Math.Sqrt(r * r - free * free);
        m.Append(BioShapes.Cylinder(new BioVec(sign * -free, y, zBottom), new BioVec(sign * -free, y, zTop), ht, 6, caps: false));
        return m;
    }

    // ---- chloroplast ----

    /// <summary>The 14 grana's centres (§5.1.5): the hexagonal lattice (spacing 1.0 µm) points nearest the lens's centre,
    /// inside 80% of its radius, on its mid-plane (x, z).</summary>
    public static readonly IReadOnlyList<BioVec> GranaCentres = MakeGranaCentres();

    private static List<BioVec> MakeGranaCentres()
    {
        var pts = new List<BioVec>();
        for (var i = -4; i <= 4; i++)
            for (var j = -4; j <= 4; j++)
            {
                var p = new BioVec(i + 0.5 * j, 0, j * Math.Sqrt(3.0) / 2);
                if (BioV.Length(p) <= 2.0 + 1e-9) pts.Add(p);
            }
        pts.Sort((a, b) =>
        {
            double la = BioV.Length(a), lb = BioV.Length(b);
            if (Math.Abs(la - lb) > 1e-9) return la.CompareTo(lb);
            return Math.Atan2(a.Z, a.X).CompareTo(Math.Atan2(b.Z, b.X));
        });
        return pts.Take(14).ToList();
    }

    private static readonly Lazy<CEShape> ChloroplastWhole = new(() => MakeChloroplast(false, false, 0.02));

    /// <summary>
    /// §5.1.5 Chloroplast, its thin axis along y: the envelope (two lenses: 5.0 × 2.4 µm, the inner 0.96 of it), translucent
    /// green; the stroma's tint; 14 grana, each 10 thylakoid discs (r 0.25, 0.035 thick, 0.05 apart) — at a cell's distance
    /// a granum is drawn as one stack the discs' size, <paramref name="detail"/> draws every disc; stroma lamellae joining
    /// neighbouring grana at alternating heights (0.12 µm wide, 0.02 thick); two starch grains; and, in detail, 70S
    /// ribosomes and a loop of DNA. Cut lengthwise (z &gt; 0 removed), the envelope is two shells and only the grana on the
    /// kept side are drawn. <paramref name="fine"/>: meshed finer, for Look inside.
    /// </summary>
    public static CEShape Chloroplast(bool detail, bool half, double ribosomeRadius, bool fine = false) =>
        !detail && !half ? ChloroplastWhole.Value : MakeChloroplast(detail, half, ribosomeRadius, fine);

    private static CEShape MakeChloroplast(bool detail, bool half, double ribosomeRadius, bool fine = false)
    {
        var s = new CEShape();
        var (rings, segments) = fine ? (28, 56) : (16, 32);
        BioRadius Lens(double scale) => BioShapes.LensRadius(5.0 * scale, 2.4 * scale);
        if (half)
        {
            var o = CEGeo.SplitShell(BioShapes.Radial(Lens(1), rings, segments, BioCut.Half, Lens(0.985)));
            s.Add(o.Outer, alpha: 0.35, roughness: 0.3, order: 3);
            s.Add(o.Caps, CERole.Cap, alpha: 1);
            var i = CEGeo.SplitShell(BioShapes.Radial(Lens(0.96), rings, segments, BioCut.Half, Lens(0.945)));
            s.Add(i.Outer, alpha: 0.4, roughness: 0.3, order: 2);
            s.Add(i.Inner, CERole.Named("stroma"), alpha: 0.55, roughness: 0.4, order: 2);
            s.Add(i.Caps, CERole.Cap, alpha: 1);
        }
        else
        {
            s.Add(BioShapes.Lens(5.0, 2.4, 10, 20), alpha: 0.35, roughness: 0.3, order: 3);
            s.Add(BioShapes.Lens(4.8, 2.304, 8, 16), CERole.Named("stroma"), alpha: 0.45, roughness: 0.4, order: 2);
        }
        var kept = GranaCentres.Where(c => !half || c.Z <= 0.3).ToList();
        var grana = new BioMesh();
        foreach (var c in kept)
        {
            if (detail)
                for (var k = 0; k < 10; k++)
                {
                    var y = -0.225 + 0.05 * k;
                    grana.Append(BioShapes.Cylinder(c + new BioVec(0, y - 0.0175, 0), c + new BioVec(0, y + 0.0175, 0), 0.25, 12));
                }
            else grana.Append(BioShapes.Cylinder(c + new BioVec(0, -0.2425, 0), c + new BioVec(0, 0.2425, 0), 0.25, 8));
        }
        s.Add(grana, CERole.Named("grana"), roughness: 0.5);
        // (lamellae: each pair of neighbours once, alternating heights)
        var lamellae = new BioMesh();
        var n = 0;
        for (var i = 0; i < kept.Count; i++)
            for (var j = i + 1; j < kept.Count; j++)
            {
                if (Math.Abs(BioV.Distance(kept[i], kept[j]) - 1.0) >= 0.05) continue;
                var y = n % 2 == 0 ? 0.1 : -0.1;
                BioVec a = kept[i], b = kept[j];
                var dir = BioV.Unit(b - a);
                lamellae.Append(CEGeo.Slab(a + dir * 0.22 + new BioVec(0, y, 0), b - dir * 0.22 + new BioVec(0, y, 0), BioV.Cross(dir, new BioVec(0, 1, 0)), 0.12, 0.02));
                n++;
            }
        s.Add(lamellae, CERole.Named("grana"), roughness: 0.5);
        // (starch grains: white ellipsoids 0.5 × 0.3 µm, between the grana)
        var starch = new BioMesh();
        foreach (var p in new[] { new BioVec(1.5, 0, -0.85), new BioVec(-1.5, 0, 0.85) })
            if (!half || p.Z <= 0) starch.Append(BioShapes.Spheroid(0.25, 0.15, 0.25, detail ? 8 : 6, detail ? 16 : 12).Translated(p));
        s.Add(starch, CERole.Colour(new BioHexPair("#f4f1e6", "#e9e4d2")), roughness: 0.6);
        if (detail)
        {
            var dots = new BioMesh();
            var rng = new BioRandom(0xC41A);
            var dot = BioShapes.Icosphere(ribosomeRadius, 0);
            var count = 0;
            while (count < 40)
            {
                var p = new BioVec(rng.Uniform(-1.8, 1.8), rng.Uniform(-0.5, 0.5), rng.Uniform(-1.8, 1.8));
                if (half && p.Z > 0) continue;
                if (BioV.Length(new BioVec(p.X, 0, p.Z)) > 2.0) continue;
                if (GranaCentres.Any(g => BioV.Length(new BioVec(p.X - g.X, 0, p.Z - g.Z)) < 0.32)) continue;
                dots.Append(dot.Translated(p));
                count++;
            }
            s.Add(dots, CERole.Colour(new BioHexPair("#2c3e8f", "#7b8fe8")), roughness: 0.5);
            var loop = Enumerable.Range(0, 16).Select(i =>
            {
                var t = 2 * Math.PI * i / 16;
                return new BioVec(-0.2 + 0.18 * Math.Cos(t), 0.03 * Math.Sin(3 * t), -0.6 + 0.18 * Math.Sin(t));
            }).ToList();
            s.Add(BioPath.Tube(loop, 0.012, sides: 6, samples: 2, closed: true), CERole.Colour(new BioHexPair("#9b6bd1", "#c39cf5")), roughness: 0.5);
        }
        return s;
    }

    // ---- small organelles ----

    /// <summary>§5.1.5 Lysosome: r 0.3, its single membrane translucent, its contents a darker sphere r 0.22.</summary>
    public static readonly CEShape Lysosome = MakeLysosome();

    private static CEShape MakeLysosome()
    {
        var s = new CEShape();
        s.Add(BioShapes.Sphere(0.3, 10, 20), alpha: 0.4, roughness: 0.3, order: 2);
        s.Add(BioShapes.Sphere(0.22, 8, 16), CERole.Dark, roughness: 0.55);
        return s;
    }

    /// <summary>§5.1.5 Peroxisome: r 0.25, translucent, with a cube (edge 0.18) for the crystalline core.</summary>
    public static readonly CEShape Peroxisome = MakePeroxisome();

    private static CEShape MakePeroxisome()
    {
        var s = new CEShape();
        s.Add(BioShapes.Sphere(0.25, 10, 20), alpha: 0.4, roughness: 0.3, order: 2);
        s.Add(CEGeo.Box(new BioVec(0.09, 0.09, 0.09)), CERole.Dark, roughness: 0.4);
        return s;
    }

    /// <summary>A centriole along y (§5.1.5 Centrosome): 9 triplets of tubes (r 0.012, 0.45 long) at radii 0.07, 0.085 and
    /// 0.1, each triplet's blade turned 30° from the radius (the pinwheel).</summary>
    public static readonly BioMesh Centriole = MakeCentriole();

    private static BioMesh MakeCentriole()
    {
        var m = new BioMesh();
        const double mid = 0.085;
        for (var k = 0; k < 9; k++)
        {
            var a = k * 40 * Math.PI / 180;
            var b = new BioVec(Math.Cos(a), 0, Math.Sin(a)) * mid;
            var w = new BioVec(Math.Cos(a + Math.PI / 6), 0, Math.Sin(a + Math.PI / 6));
            var bw = BioV.Dot(b, w);
            foreach (var rho in new[] { 0.07, 0.085, 0.1 })
            {
                // (the point of the blade at that radius: |b + s·w| = ρ)
                var disc = Math.Max(0, bw * bw - mid * mid + rho * rho);
                var s = rho >= mid ? -bw + Math.Sqrt(disc) : -bw - Math.Sqrt(disc);
                var p = b + w * (Math.Abs(rho - mid) < 1e-9 ? 0 : s);
                m.Append(BioShapes.Cylinder(p + new BioVec(0, -0.225, 0), p + new BioVec(0, 0.225, 0), 0.012, 6));
            }
        }
        return m;
    }
}
