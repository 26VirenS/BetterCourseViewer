using System.Numerics;

namespace Simpl.Tools;

// The biology toolkit's procedural geometry (spec §3.4), the Mac's BioMesh.swift: triangle meshes with smooth normals
// for everything its 3D scenes draw — spheres and icospheres, cylinders, capsules, tubes and ribbons along a path
// (Catmull–Rom through the points, rotation-minimising frames), tori, spheroids, superellipsoids, rounded boxes, lenses,
// seeded blobs and spherical-shell patches — and the cutaway done in geometry, not with clip planes (Viewport3D has
// none, and both apps cut the same way): a star-shaped solid or shell made with a wedge left out (the octant facing the
// camera, or a half) and caps where it was cut, showing the wall's thickness. Plain arithmetic: BioScene.cs turns a mesh
// into a frozen MeshGeometry3D, many meshes of one material merged into one with a map back to the object each vertex
// and triangle is from. Winding is counter-clockwise seen from the side the normals face.

/// <summary>A triangle mesh (float positions and normals, as the GPU takes them; int indices, as WPF takes them).</summary>
public sealed class BioMesh
{
    public List<Vector3> Positions { get; private set; } = new();
    public List<Vector3> Normals { get; private set; } = new();
    public List<int> Indices { get; private set; } = new();

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;
    public bool IsEmpty => Indices.Count == 0;

    public static Vector3 F(BioVec v) => new((float)v.X, (float)v.Y, (float)v.Z);
    public static BioVec D(Vector3 v) => new(v.X, v.Y, v.Z);

    public int Vertex(BioVec p, BioVec n)
    {
        Positions.Add(F(p));
        Normals.Add(F(BioV.Unit(n)));
        return Positions.Count - 1;
    }

    public void Triangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    /// <summary>A flat triangle facing <paramref name="n"/> (its winding chosen so), with that normal at each corner.</summary>
    public void FlatTriangle(BioVec a, BioVec b, BioVec c, BioVec n)
    {
        var ccw = BioV.Dot(BioV.Cross(b - a, c - a), n) >= 0;
        int i = Vertex(a, n), j = Vertex(ccw ? b : c, n), k = Vertex(ccw ? c : b, n);
        Triangle(i, j, k);
    }

    /// <summary>A flat quad a–b–c–d (in order round it) facing <paramref name="n"/>.</summary>
    public void FlatQuad(BioVec a, BioVec b, BioVec c, BioVec d, BioVec n)
    {
        FlatTriangle(a, b, c, n);
        FlatTriangle(a, c, d, n);
    }

    /// <summary>Another mesh's triangles added to this one.</summary>
    public void Append(BioMesh m)
    {
        var start = Positions.Count;
        Positions.AddRange(m.Positions);
        Normals.AddRange(m.Normals);
        foreach (var i in m.Indices) Indices.Add(i + start);
    }

    /// <summary>A copy (its lists its own).</summary>
    public BioMesh Copy() => new() { Positions = Positions.ToList(), Normals = Normals.ToList(), Indices = Indices.ToList() };

    /// <summary>The mesh placed by a frame (positions through it; normals by its cofactor, so a scaled frame shades right; a
    /// mirroring frame keeps the winding outward).</summary>
    public BioMesh Transformed(BioFrame f)
    {
        var out_ = new BioMesh();
        out_.Positions.Capacity = Positions.Count;
        out_.Normals.Capacity = Normals.Count;
        foreach (var p in Positions) out_.Positions.Add(F(f.Apply(D(p))));
        foreach (var n in Normals) out_.Normals.Add(F(f.ApplyNormal(D(n))));
        if (f.Determinant < 0)
        {
            out_.Indices.Capacity = Indices.Count;
            for (var t = 0; t + 2 < Indices.Count; t += 3) { out_.Indices.Add(Indices[t]); out_.Indices.Add(Indices[t + 2]); out_.Indices.Add(Indices[t + 1]); }
        }
        else out_.Indices = Indices.ToList();
        return out_;
    }

    public BioMesh Translated(BioVec v) => Transformed(new BioFrame(v));
    public BioMesh Scaled(double s) => Transformed(new BioFrame(BioVec.Zero, new BioVec(s, 0, 0), new BioVec(0, s, 0), new BioVec(0, 0, s)));

    /// <summary>The same surface seen from the other side: normals turned round, winding reversed (an inner wall; an
    /// outline's inverted hull).</summary>
    public BioMesh Flipped()
    {
        var out_ = new BioMesh { Positions = Positions.ToList(), Normals = Normals.Select(n => -n).ToList() };
        out_.Indices.Capacity = Indices.Count;
        for (var t = 0; t + 2 < Indices.Count; t += 3) { out_.Indices.Add(Indices[t]); out_.Indices.Add(Indices[t + 2]); out_.Indices.Add(Indices[t + 1]); }
        return out_;
    }

    /// <summary>The bounding sphere of its vertices.</summary>
    public (BioVec Centre, double Radius) Bounds => BioV.BoundingSphere(Positions.Select(D).ToList());
}

/// <summary>Where a mesh goes: an origin and three axes (each may be scaled; a negative determinant mirrors).</summary>
public readonly struct BioFrame
{
    public BioVec Origin { get; }
    public BioVec X { get; }
    public BioVec Y { get; }
    public BioVec Z { get; }

    public BioFrame(BioVec origin) : this(origin, new BioVec(1, 0, 0), new BioVec(0, 1, 0), new BioVec(0, 0, 1)) { }

    public BioFrame(BioVec origin, BioVec x, BioVec y, BioVec z)
    {
        Origin = origin;
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly BioFrame Identity = new(BioVec.Zero);

    /// <summary>A frame at <paramref name="origin"/> whose y axis points along <paramref name="dir"/> (things made along y —
    /// cylinders, capsules — stood that way).</summary>
    public static BioFrame Along(BioVec dir, BioVec origin)
    {
        var y = BioV.Unit(dir);
        var reference = Math.Abs(y.Y) < 0.9 ? new BioVec(0, 1, 0) : new BioVec(1, 0, 0);
        var x = BioV.Unit(BioV.Cross(reference, y));
        var z = BioV.Cross(x, y);
        return new BioFrame(origin, x, y, z);
    }

    /// <summary>A turn of <paramref name="degrees"/> about <paramref name="axis"/> (right-handed), at <paramref name="origin"/>, scaled by <paramref name="scale"/>.</summary>
    public static BioFrame Rotation(BioVec axis, double degrees, BioVec? origin = null, double scale = 1) =>
        new(origin ?? BioVec.Zero, BioV.Rotate(new BioVec(1, 0, 0), axis, degrees) * scale, BioV.Rotate(new BioVec(0, 1, 0), axis, degrees) * scale,
            BioV.Rotate(new BioVec(0, 0, 1), axis, degrees) * scale);

    public BioVec Apply(BioVec p) => Origin + p.X * X + p.Y * Y + p.Z * Z;
    /// <summary>A direction (no origin).</summary>
    public BioVec ApplyVector(BioVec v) => v.X * X + v.Y * Y + v.Z * Z;

    /// <summary>A normal: by the cofactor matrix (det · M⁻ᵀ), so it stays at right angles to a scaled surface.</summary>
    public BioVec ApplyNormal(BioVec n)
    {
        double s = Determinant < 0 ? -1 : 1;
        return BioV.Unit((n.X * BioV.Cross(Y, Z) + n.Y * BioV.Cross(Z, X) + n.Z * BioV.Cross(X, Y)) * s);
    }

    public double Determinant => BioV.Dot(X, BioV.Cross(Y, Z));

    /// <summary>This frame after another: other.Apply(this.Apply(p)).</summary>
    public BioFrame Then(BioFrame other) => new(other.Apply(Origin), other.ApplyVector(X), other.ApplyVector(Y), other.ApplyVector(Z));
}

/// <summary>The cutaway (spec §3.4 item 3): what is left out of a star-shaped solid or shell, in its own frame.</summary>
public enum BioCut
{
    /// <summary>The octant x &gt; 0, y &gt; 0, z &gt; 0 (the front upper right, toward the camera).</summary>
    Octant,
    /// <summary>The half z &gt; 0 (a slice; a mitochondrion cut lengthwise).</summary>
    Half,
}

public static class BioCuts
{
    /// <summary>Whether a point (in the shape's frame, about its centre) is in the part left out.</summary>
    public static bool Removes(this BioCut c, BioVec p) => c == BioCut.Octant ? p.X > 0 && p.Y > 0 && p.Z > 0 : p.Z > 0;
}

/// <summary>A radius function: the distance from the origin to a star-shaped surface along a unit direction.</summary>
public delegate double BioRadius(BioVec direction);

public static class BioShapes
{
    /// <summary>The direction at polar angle θ from +y and azimuth φ from +z toward +x.</summary>
    public static BioVec Direction(double theta, double phi) => new(Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta), Math.Sin(theta) * Math.Cos(phi));

    /// <summary>
    /// A star-shaped surface about the origin: the point at distance radius(d) along each direction d, on a grid of
    /// <paramref name="rings"/> × <paramref name="segments"/> (rings even and segments a multiple of 4 when cut, so the cut
    /// runs along grid lines). With <paramref name="inner"/>, a shell: its inner wall at inner(d), facing in. With
    /// <paramref name="cut"/>, the wedge is left out and the cut faces capped — between the walls for a shell, to the
    /// centre for a solid.
    /// </summary>
    public static BioMesh Radial(BioRadius radius, int rings = 24, int segments = 48, BioCut? cut = null, BioRadius? inner = null)
    {
        var nr = Math.Max(2, cut == null ? rings : rings + rings % 2);
        var ns = Math.Max(3, cut == null ? segments : (segments + 3) / 4 * 4);
        static BioVec Point(BioRadius r, double t, double p) => Direction(t, p) * r(Direction(t, p));
        static BioVec Normal(BioRadius r, double t, double p)
        {
            const double e = 1e-4;
            var dt = Point(r, t + e, p) - Point(r, t - e, p);
            var dp = Point(r, t, p + e) - Point(r, t, p - e);
            var n = BioV.Cross(dt, dp);
            var pos = Point(r, t, p);
            if (BioV.Length(n) < 1e-12) return BioV.Unit(pos.Y >= 0 ? new BioVec(0, 1, 0) : new BioVec(0, -1, 0));
            return BioV.Dot(n, pos) >= 0 ? n : -n;
        }
        BioMesh Surface(BioRadius r, bool inward)
        {
            var m = new BioMesh();
            for (var i = 0; i <= nr; i++)
            {
                var t = Math.PI * i / nr;
                for (var j = 0; j < ns; j++)
                {
                    var p = 2 * Math.PI * j / ns;
                    var tt = i == 0 ? 1e-6 : (i == nr ? Math.PI - 1e-6 : t);
                    m.Vertex(Point(r, t, p), Normal(r, tt, p));
                }
            }
            for (var i = 0; i < nr; i++)
            {
                for (var j = 0; j < ns; j++)
                {
                    double tm = Math.PI * (i + 0.5) / nr, pm = 2 * Math.PI * (j + 0.5) / ns;
                    if (cut is { } c && c.Removes(Direction(tm, pm))) continue;
                    int a = i * ns + j, d = i * ns + (j + 1) % ns;
                    int b = (i + 1) * ns + j, cc = (i + 1) * ns + (j + 1) % ns;
                    // (a pole's row is one point: its quads are triangles)
                    if (i < nr - 1) m.Triangle(a, b, cc);
                    if (i > 0) m.Triangle(a, cc, d);
                }
            }
            return inward ? m.Flipped() : m;
        }
        var mesh = Surface(radius, false);
        if (inner != null) mesh.Append(Surface(inner, true));
        if (cut is not { } cutKind) return mesh;
        // (the caps: along each edge of the cut, from the outer wall to the inner, or to the centre)
        var edges = new List<(List<(double T, double P)> Points, BioVec Facing)>();
        int half = nr / 2, quarter = ns / 4;
        if (cutKind == BioCut.Octant)
        {
            edges.Add((Enumerable.Range(0, half + 1).Select(k => (Math.PI * k / nr, 0.0)).ToList(), new BioVec(1, 0, 0)));
            edges.Add((Enumerable.Range(0, half + 1).Select(k => (Math.PI * k / nr, Math.PI / 2)).ToList(), new BioVec(0, 0, 1)));
            edges.Add((Enumerable.Range(0, quarter + 1).Select(k => (Math.PI / 2, 2 * Math.PI * k / ns)).ToList(), new BioVec(0, 1, 0)));
        }
        else
        {
            edges.Add((Enumerable.Range(0, nr + 1).Select(k => (Math.PI * k / nr, Math.PI / 2)).ToList(), new BioVec(0, 0, 1)));
            edges.Add((Enumerable.Range(0, nr + 1).Select(k => (Math.PI * k / nr, 3 * Math.PI / 2)).ToList(), new BioVec(0, 0, 1)));
        }
        foreach (var (points, facing) in edges)
        {
            for (var k = 0; k < points.Count - 1; k++)
            {
                var (t0, p0) = points[k];
                var (t1, p1) = points[k + 1];
                BioVec o0 = Point(radius, t0, p0), o1 = Point(radius, t1, p1);
                if (inner != null) mesh.FlatQuad(o0, o1, Point(inner, t1, p1), Point(inner, t0, p0), facing);
                else mesh.FlatTriangle(o0, o1, BioVec.Zero, facing);
            }
        }
        return mesh;
    }

    // ---- radius functions (star-shaped about the origin) ----

    /// <summary>A sphere's.</summary>
    public static BioRadius SphereRadius(double r) => _ => r;

    /// <summary>An ellipsoid's, semi-axes a, b, c along x, y, z.</summary>
    public static BioRadius EllipsoidRadius(double a, double b, double c) => d => 1 / Math.Sqrt(d.X * d.X / (a * a) + d.Y * d.Y / (b * b) + d.Z * d.Z / (c * c));

    /// <summary>A capsule's along y: radius r, its cylinder <paramref name="length"/> long (so length + 2r overall).</summary>
    public static BioRadius CapsuleRadius(double r, double length)
    {
        var h = length / 2;
        return d =>
        {
            var side = Math.Sqrt(d.X * d.X + d.Z * d.Z);
            if (side > 1e-9)
            {
                var t = r / side;
                if (Math.Abs(t * d.Y) <= h) return t;
            }
            var dc = Math.Abs(d.Y) * h;
            return dc + Math.Sqrt(dc * dc - h * h + r * r);
        };
    }

    /// <summary>A superellipsoid's: semi-axes a, b, c; e1 shapes it north–south, e2 east–west (1 and 1 is an ellipsoid;
    /// near 0 a box).</summary>
    public static BioRadius SuperellipsoidRadius(double a, double b, double c, double e1, double e2) => d =>
    {
        var xz = Math.Pow(Math.Pow(Math.Abs(d.X / a), 2 / e2) + Math.Pow(Math.Abs(d.Z / c), 2 / e2), e2 / e1);
        var f = xz + Math.Pow(Math.Abs(d.Y / b), 2 / e1);
        return f > 0 ? Math.Pow(f, -e1 / 2) : 0;
    };

    /// <summary>A rounded box's: half extents and corner radius (by its signed distance; <paramref name="level"/> &lt; 0 is a
    /// wall that far in).</summary>
    public static BioRadius RoundedBoxRadius(BioVec half, double corner, double level = 0)
    {
        var rc = Math.Min(corner, Math.Min(half.X, Math.Min(half.Y, half.Z)));
        var inner = new BioVec(half.X - rc, half.Y - rc, half.Z - rc);
        double Sdf(BioVec p)
        {
            var q = new BioVec(Math.Abs(p.X) - inner.X, Math.Abs(p.Y) - inner.Y, Math.Abs(p.Z) - inner.Z);
            var outside = BioV.Length(new BioVec(Math.Max(q.X, 0), Math.Max(q.Y, 0), Math.Max(q.Z, 0)));
            return outside + Math.Min(Math.Max(q.X, Math.Max(q.Y, q.Z)), 0) - rc;
        }
        return ByDistance(Sdf, BioV.Length(half) * 1.5, level);
    }

    /// <summary>A lens's (two spherical caps): <paramref name="diameter"/> across in x and z, <paramref name="thickness"/> in y.</summary>
    public static BioRadius LensRadius(double diameter, double thickness, double level = 0)
    {
        var c = (diameter * diameter - thickness * thickness) / (4 * thickness);
        var rho = c + thickness / 2;
        double Sdf(BioVec p) => Math.Max(BioV.Length(p - new BioVec(0, -c, 0)) - rho, BioV.Length(p - new BioVec(0, c, 0)) - rho);
        return ByDistance(Sdf, diameter, level);
    }

    /// <summary>The radius along each direction where a signed distance reaches <paramref name="level"/>, found by bisection (48 halvings).</summary>
    public static BioRadius ByDistance(Func<BioVec, double> sdf, double reach, double level = 0) => d =>
    {
        double lo = 0, hi = reach;
        for (var k = 0; k < 48; k++)
        {
            var mid = (lo + hi) / 2;
            if (sdf(d * mid) < level) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    };

    /// <summary>A seeded blob's radius function (§3.4): a base shape's radius times 1 + Σ aₖ·sin(fₖ·(u·dₖ) + φₖ).</summary>
    public static BioRadius Blob(BioRadius baseRadius, BioBlob blob) => d => baseRadius(d) * blob.Factor(d);

    // ---- solids ----

    /// <summary>A UV sphere.</summary>
    public static BioMesh Sphere(double radius, int rings = 16, int segments = 32) => Radial(SphereRadius(radius), rings, segments);

    /// <summary>A spheroid (or any ellipsoid), semi-axes along x, y, z.</summary>
    public static BioMesh Spheroid(double a, double b, double c, int rings = 20, int segments = 40, BioCut? cut = null) => Radial(EllipsoidRadius(a, b, c), rings, segments, cut);

    /// <summary>A capsule along y: radius r, cylinder <paramref name="length"/> (24 × 12 by default).</summary>
    public static BioMesh Capsule(double radius, double length, int rings = 12, int segments = 24, BioCut? cut = null) => Radial(CapsuleRadius(radius, length), rings, segments, cut);

    public static BioMesh RoundedBox(BioVec half, double corner, int rings = 24, int segments = 48, BioCut? cut = null) => Radial(RoundedBoxRadius(half, corner), rings, segments, cut);

    public static BioMesh Lens(double diameter, double thickness, int rings = 16, int segments = 32, BioCut? cut = null) => Radial(LensRadius(diameter, thickness), rings, segments, cut);

    public static BioMesh Superellipsoid(double a, double b, double c, double e1, double e2, int rings = 20, int segments = 40) =>
        Radial(SuperellipsoidRadius(a, b, c, e1, e2), rings, segments);

    /// <summary>An icosphere: an icosahedron's faces split <paramref name="subdivisions"/> times (1: 80 triangles; 2: 320),
    /// for the many small spheres (ribosomes, vesicles, atoms in a big model).</summary>
    public static BioMesh Icosphere(double radius, int subdivisions = 1)
    {
        var t = (1 + Math.Sqrt(5.0)) / 2;
        var v = new List<BioVec>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        }.Select(BioV.Unit).ToList();
        var f = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (var s = 0; s < Math.Max(0, subdivisions); s++)
        {
            var mid = new Dictionary<long, int>();
            int Middle(int a, int b)
            {
                var key = (long)Math.Min(a, b) * 100_000 + Math.Max(a, b);
                if (mid.TryGetValue(key, out var m)) return m;
                v.Add(BioV.Unit((v[a] + v[b]) / 2));
                mid[key] = v.Count - 1;
                return v.Count - 1;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in f)
            {
                int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }
            f = next;
        }
        var mesh = new BioMesh();
        foreach (var p in v) mesh.Vertex(p * radius, p);
        foreach (var (a, b, c) in f)
        {
            // (wound to face out)
            var n = BioV.Cross(v[b] - v[a], v[c] - v[a]);
            if (BioV.Dot(n, v[a]) >= 0) mesh.Triangle(a, b, c); else mesh.Triangle(a, c, b);
        }
        return mesh;
    }

    /// <summary>A cylinder from a to b (a bond, a centriole's tube), its ends capped if asked.</summary>
    public static BioMesh Cylinder(BioVec from, BioVec to, double radius, int segments = 16, bool caps = true)
    {
        var len = BioV.Distance(from, to);
        if (len <= 1e-9) return new BioMesh();
        var m = new BioMesh();
        var n = Math.Max(3, segments);
        for (var k = 0; k <= 1; k++)
            for (var j = 0; j < n; j++)
            {
                var p = 2 * Math.PI * j / n;
                var d = new BioVec(Math.Sin(p), 0, Math.Cos(p));
                m.Vertex(new BioVec(d.X * radius, k * len, d.Z * radius), d);
            }
        for (var j = 0; j < n; j++)
        {
            int a0 = j, a1 = (j + 1) % n, b0 = n + j, b1 = n + (j + 1) % n;
            m.Triangle(a0, a1, b1);
            m.Triangle(a0, b1, b0);
        }
        if (caps)
            for (var k = 0; k <= 1; k++)
            {
                var y = k * len;
                var facing = new BioVec(0, k == 0 ? -1 : 1, 0);
                for (var j = 0; j < n; j++)
                {
                    double p0 = 2 * Math.PI * j / n, p1 = 2 * Math.PI * (j + 1) / n;
                    m.FlatTriangle(new BioVec(0, y, 0), new BioVec(Math.Sin(p0) * radius, y, Math.Cos(p0) * radius), new BioVec(Math.Sin(p1) * radius, y, Math.Cos(p1) * radius), facing);
                }
            }
        return m.Transformed(BioFrame.Along(to - from, from));
    }

    /// <summary>A torus about the y axis: the ring's radius and the tube's.</summary>
    public static BioMesh Torus(double ring, double pipe, int ringSegments = 32, int pipeSegments = 12)
    {
        var m = new BioMesh();
        int nu = Math.Max(3, ringSegments), nv = Math.Max(3, pipeSegments);
        for (var i = 0; i < nu; i++)
        {
            var u = 2 * Math.PI * i / nu;
            var c = new BioVec(Math.Sin(u) * ring, 0, Math.Cos(u) * ring);
            var outward = new BioVec(Math.Sin(u), 0, Math.Cos(u));
            for (var j = 0; j < nv; j++)
            {
                var v = 2 * Math.PI * j / nv;
                var n = outward * Math.Cos(v) + new BioVec(0, 1, 0) * Math.Sin(v);
                m.Vertex(c + n * pipe, n);
            }
        }
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                int a = i * nv + j, b = (i + 1) % nu * nv + j;
                int c = (i + 1) % nu * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                m.Triangle(a, b, c);
                m.Triangle(a, c, d);
            }
        return m;
    }

    /// <summary>
    /// A curved patch of a spherical shell about the origin (the rough ER's sheets, §5.1.5): polar angles θ from +y,
    /// azimuths φ from +z toward +x, its mid-surface at radius(θ, φ), <paramref name="thickness"/> thick, its two walls
    /// joined at the rim; the thickness fades to a fifth over the last <paramref name="rimFade"/> of each range (a rounded rim).
    /// </summary>
    public static BioMesh ShellPatch((double Lo, double Hi) theta, (double Lo, double Hi) phi, double thickness, Func<double, double, double> radius,
        double rimFade = 0.05, int rows = 16, int columns = 32)
    {
        int nr = Math.Max(1, rows), nc = Math.Max(1, columns);
        double Fade(double u)
        {
            var e = Math.Min(u, 1 - u);
            return rimFade > 0 && e < rimFade ? 0.2 + 0.8 * (e / rimFade) : 1;
        }
        BioVec At(int i, int j, double side)
        {
            double u = (double)i / nr, v = (double)j / nc;
            var t = theta.Lo + (theta.Hi - theta.Lo) * u;
            var p = phi.Lo + (phi.Hi - phi.Lo) * v;
            var r = radius(t, p) + side * thickness / 2 * Fade(u) * Fade(v);
            return Direction(t, p) * r;
        }
        var m = new BioMesh();
        foreach (var (side, outward) in new[] { (1.0, true), (-1.0, false) })
        {
            var grid = new BioMesh();
            for (var i = 0; i <= nr; i++) for (var j = 0; j <= nc; j++) grid.Vertex(At(i, j, side), BioVec.Zero);
            for (var i = 0; i < nr; i++)
                for (var j = 0; j < nc; j++)
                {
                    int a = i * (nc + 1) + j, d = i * (nc + 1) + j + 1;
                    int b = (i + 1) * (nc + 1) + j, c = (i + 1) * (nc + 1) + j + 1;
                    grid.Triangle(a, b, c);
                    grid.Triangle(a, c, d);
                }
            grid = SmoothNormals(grid);
            // (the grid as built faces out — away from the origin; the inner wall turned to face in)
            if (BioV.Dot(BioMesh.D(grid.Normals[0]), At(0, 0, side)) < 0) grid = grid.Flipped();
            m.Append(outward ? grid : grid.Flipped());
        }
        // (the rim: the two walls joined along the four edges, facing out of the patch)
        var centre = At(nr / 2, nc / 2, 0);
        void Rim(IEnumerable<(int I, int J)> ptsIn)
        {
            var pts = ptsIn.ToList();
            for (var k = 0; k < pts.Count - 1; k++)
            {
                var (i0, j0) = pts[k];
                var (i1, j1) = pts[k + 1];
                BioVec o0 = At(i0, j0, 1), o1 = At(i1, j1, 1), n0 = At(i0, j0, -1), n1 = At(i1, j1, -1);
                var midPoint = (o0 + o1 + n0 + n1) / 4;
                BioVec along = o1 - o0, across = o0 - n0;
                var facing = BioV.Cross(along, across);
                if (BioV.Dot(facing, midPoint - centre) < 0) facing = -facing;
                m.FlatQuad(o0, o1, n1, n0, facing);
            }
        }
        Rim(Enumerable.Range(0, nc + 1).Select(j => (0, j)));
        Rim(Enumerable.Range(0, nc + 1).Select(j => (nr, j)));
        Rim(Enumerable.Range(0, nr + 1).Select(i => (i, 0)));
        Rim(Enumerable.Range(0, nr + 1).Select(i => (i, nc)));
        return m;
    }

    /// <summary>Normals made smooth again from the triangles round each vertex (area-weighted), for meshes whose normals were
    /// not worked out as they were made.</summary>
    public static BioMesh SmoothNormals(BioMesh mesh)
    {
        var acc = new BioVec[mesh.Positions.Count];
        BioVec V(int i) => BioMesh.D(mesh.Positions[i]);
        for (var t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var n = BioV.Cross(V(b) - V(a), V(c) - V(a));
            acc[a] += n;
            acc[b] += n;
            acc[c] += n;
        }
        var out_ = mesh.Copy();
        for (var i = 0; i < acc.Length; i++) out_.Normals[i] = BioMesh.F(BioV.Unit(acc[i]));
        return out_;
    }
}

/// <summary>
/// A seeded blob's ripples (§3.4): <c>terms</c> sines of the direction, their total amplitude <c>amplitude</c> (≤ 0.12).
/// Drawn from SplitMix64 in this order, term by term: dₖ (<see cref="BioRandom.UnitVector"/>, two draws), fₖ = 1.5…3.5
/// (one), φₖ = 0…2π (one), a weight 0.5…1 (one); the weights are then scaled to add up to the amplitude.
/// </summary>
public sealed class BioBlob
{
    public IReadOnlyList<(BioVec D, double F, double Phase, double A)> Terms { get; }

    public BioBlob(ulong seed, double amplitude, int terms = 4)
    {
        var rng = new BioRandom(seed);
        var raw = new List<(BioVec, double, double, double)>();
        for (var k = 0; k < Math.Max(1, terms); k++)
        {
            var d = rng.UnitVector();
            var f = rng.Uniform(1.5, 3.5);
            var p = rng.Uniform(0, 2 * Math.PI);
            var w = rng.Uniform(0.5, 1);
            raw.Add((d, f, p, w));
        }
        var total = raw.Sum(r => r.Item4);
        Terms = raw.Select(r => (r.Item1, r.Item2, r.Item3, total > 0 ? r.Item4 / total * amplitude : 0)).ToList();
    }

    /// <summary>1 + Σ aₖ·sin(fₖ·(u·dₖ) + φₖ) for a unit direction u.</summary>
    public double Factor(BioVec u) => 1 + Terms.Sum(t => t.A * Math.Sin(t.F * BioV.Dot(u, t.D) + t.Phase));
}

// ---- along a path ----

public static class BioPath
{
    /// <summary>Points on a Catmull–Rom spline through the points (uniform; the ends' tangents from their neighbours),
    /// <paramref name="samples"/> to each span, the last point included; closed, it comes back to the first.</summary>
    public static List<BioVec> CatmullRom(IReadOnlyList<BioVec> points, int samples = 6, bool closed = false)
    {
        var n = points.Count;
        if (n < 2) return points.ToList();
        BioVec P(int i)
        {
            if (closed) return points[(i % n + n) % n];
            if (i < 0) return points[0] * 2 - points[1];
            if (i >= n) return points[n - 1] * 2 - points[n - 2];
            return points[i];
        }
        var out_ = new List<BioVec>();
        var spans = closed ? n : n - 1;
        var s = Math.Max(1, samples);
        for (var i = 0; i < spans; i++)
        {
            BioVec p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
            for (var k = 0; k < s; k++)
            {
                double t = (double)k / s, t2 = t * t, t3 = t2 * t;
                var a = p1 * 2;
                var b = (p2 - p0) * t;
                var c = (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2;
                var d = (p1 * 3 - p0 - p2 * 3 + p3) * t3;
                out_.Add((a + b + c + d) * 0.5);
            }
        }
        out_.Add(closed ? points[0] : points[n - 1]);
        return out_;
    }

    /// <summary>Tangents of a polyline (central differences).</summary>
    public static List<BioVec> Tangents(IReadOnlyList<BioVec> pts, bool closed = false)
    {
        var n = pts.Count;
        return Enumerable.Range(0, n).Select(i =>
        {
            var a = closed ? pts[(i - 1 + n) % n] : pts[Math.Max(i - 1, 0)];
            var b = closed ? pts[(i + 1) % n] : pts[Math.Min(i + 1, n - 1)];
            var t = BioV.Unit(b - a);
            return BioV.Length(t) > 0 ? t : new BioVec(0, 1, 0);
        }).ToList();
    }

    /// <summary>Rotation-minimising frames along a polyline (Wang et al.'s double reflection): each point's tangent and a
    /// normal that turns as little as it can. Closed, the twist left at the end is shared out along it.</summary>
    public static List<(BioVec T, BioVec R)> Frames(IReadOnlyList<BioVec> pts, bool closed = false, BioVec? start = null)
    {
        var n = pts.Count;
        if (n < 2) return pts.Select(_ => (new BioVec(0, 1, 0), new BioVec(1, 0, 0))).ToList();
        var ts = Tangents(pts, closed);
        var r0 = start ?? BioV.Cross(ts[0], Math.Abs(ts[0].Y) < 0.9 ? new BioVec(0, 1, 0) : new BioVec(1, 0, 0));
        r0 = BioV.Unit(r0 - ts[0] * BioV.Dot(r0, ts[0]));
        // (closed, the frame is carried on round to the first point again, to see how far it has twisted)
        var steps = closed ? n : n - 1;
        var out_ = new List<(BioVec T, BioVec R)> { (ts[0], r0) };
        for (var i = 0; i < steps; i++)
        {
            BioVec p0 = pts[i], p1 = pts[(i + 1) % n], t1 = ts[(i + 1) % n];
            var v1 = p1 - p0;
            var c1 = BioV.Dot(v1, v1);
            var (ti, ri) = out_[i];
            if (c1 <= 1e-18) { out_.Add((t1, ri)); continue; }
            var rl = ri - v1 * (2 / c1 * BioV.Dot(v1, ri));
            var tl = ti - v1 * (2 / c1 * BioV.Dot(v1, ti));
            var v2 = t1 - tl;
            var c2 = BioV.Dot(v2, v2);
            var r = c2 > 1e-18 ? rl - v2 * (2 / c2 * BioV.Dot(v2, rl)) : rl;
            out_.Add((t1, BioV.Unit(r)));
        }
        if (closed)
        {
            // (the twist that would be left where the loop meets itself, shared out along it)
            var rn = out_[n].R;
            var angle = Math.Atan2(BioV.Dot(BioV.Cross(rn, r0), ts[0]), BioV.Dot(rn, r0)) * 180 / Math.PI;
            out_ = Enumerable.Range(0, n).Select(k => (out_[k].T, BioV.Rotate(out_[k].R, out_[k].T, angle * k / n))).ToList();
        }
        return out_;
    }

    /// <summary>A tube along a path (§3.4): a circle of <paramref name="sides"/> swept along a Catmull–Rom spline through the
    /// points (or the points themselves, <paramref name="smooth"/> off), radius <paramref name="radius"/> (or per input
    /// point, <paramref name="radii"/>), its ends capped if open.</summary>
    public static BioMesh Tube(IReadOnlyList<BioVec> points, double radius, IReadOnlyList<double>? radii = null, int sides = 10, int samples = 6,
        bool closed = false, bool smooth = true, bool caps = true)
    {
        if (points.Count < 2) return new BioMesh();
        var path = smooth ? CatmullRom(points, samples, closed) : (closed ? points.Append(points[0]).ToList() : points.ToList());
        var fr = Frames(closed ? path.Take(path.Count - 1).ToList() : path, closed);
        var frs = closed ? fr.Append(fr[0]).ToList() : fr;
        double Rad(int k)
        {
            if (radii == null || radii.Count != points.Count) return radius;
            var u = (double)k / Math.Max(path.Count - 1, 1) * (closed ? points.Count : points.Count - 1);
            var i = Math.Min((int)u, radii.Count - 1);
            var j = closed ? (i + 1) % radii.Count : Math.Min(i + 1, radii.Count - 1);
            var f = u - i;
            return radii[i] * (1 - f) + radii[j] * f;
        }
        var m = new BioMesh();
        var ns = Math.Max(3, sides);
        for (var k = 0; k < path.Count; k++)
        {
            var (t, r) = frs[k];
            var s = BioV.Cross(t, r);
            for (var j = 0; j < ns; j++)
            {
                var a = 2 * Math.PI * j / ns;
                var n = r * Math.Cos(a) + s * Math.Sin(a);
                m.Vertex(path[k] + n * Rad(k), n);
            }
        }
        for (var k = 0; k < path.Count - 1; k++)
            for (var j = 0; j < ns; j++)
            {
                int a = k * ns + j, b = (k + 1) * ns + j;
                int c = (k + 1) * ns + (j + 1) % ns, d = k * ns + (j + 1) % ns;
                // (r → s is counter-clockwise about t, so a–d–c faces out)
                m.Triangle(a, d, c);
                m.Triangle(a, c, b);
            }
        if (caps && !closed)
            foreach (var (k, sign) in new[] { (0, -1.0), (path.Count - 1, 1.0) })
            {
                var (t, r) = frs[k];
                var s = BioV.Cross(t, r);
                for (var j = 0; j < ns; j++)
                {
                    double a0 = 2 * Math.PI * j / ns, a1 = 2 * Math.PI * (j + 1) / ns;
                    m.FlatTriangle(path[k], path[k] + (r * Math.Cos(a0) + s * Math.Sin(a0)) * Rad(k), path[k] + (r * Math.Cos(a1) + s * Math.Sin(a1)) * Rad(k), t * sign);
                }
            }
        return m;
    }

    /// <summary>
    /// A ribbon along a path (§3.4, the protein cartoon's helices and strands): a flat band <paramref name="width"/> wide and
    /// <paramref name="thickness"/> thick (an ellipse across, if <paramref name="elliptical"/>), its broad face turned by
    /// <paramref name="guides"/> (one side direction per input point, e.g. from CA toward the helix axis) or by
    /// rotation-minimising frames; <paramref name="arrow"/> makes the band widen to its width at that input point and
    /// narrow to a point at the end (a β-strand's arrowhead).
    /// </summary>
    public static BioMesh Ribbon(IReadOnlyList<BioVec> points, double width, double thickness, IReadOnlyList<BioVec>? guides = null, int samples = 6,
        (int From, double Width)? arrow = null, bool elliptical = false)
    {
        if (points.Count < 2) return new BioMesh();
        var path = CatmullRom(points, samples);
        var ts = Tangents(path);
        var rmf = Frames(path);
        var spanSamples = Math.Max(1, samples);
        BioVec Side(int k)
        {
            var u = rmf[k].R;
            if (guides != null && guides.Count == points.Count)
            {
                var i = Math.Min(k / spanSamples, guides.Count - 1);
                var j = Math.Min(i + 1, guides.Count - 1);
                var f = (double)(k % spanSamples) / spanSamples;
                u = guides[i] * (1 - f) + guides[j] * f;
            }
            var t = ts[k];
            var o = u - t * BioV.Dot(u, t);
            return BioV.Length(o) > 1e-9 ? BioV.Unit(o) : rmf[k].R;
        }
        double HalfWidth(int k)
        {
            if (arrow is not { } ar) return width / 2;
            var startK = ar.From * spanSamples;
            if (k < startK) return width / 2;
            double span = Math.Max(path.Count - 1 - startK, 1);
            return ar.Width / 2 * (1 - (k - startK) / span);
        }
        var m = new BioMesh();
        if (elliptical)
        {
            const int ns = 12;
            for (var k = 0; k < path.Count; k++)
            {
                var u = Side(k);
                var v = BioV.Cross(ts[k], u);
                double hw = HalfWidth(k), ht = thickness / 2;
                for (var j = 0; j < ns; j++)
                {
                    var a = 2 * Math.PI * j / ns;
                    var p = path[k] + u * (Math.Cos(a) * hw) + v * (Math.Sin(a) * ht);
                    var n = u * (Math.Cos(a) / Math.Max(hw, 1e-6)) + v * (Math.Sin(a) / Math.Max(ht, 1e-6));
                    m.Vertex(p, n);
                }
            }
            for (var k = 0; k < path.Count - 1; k++)
                for (var j = 0; j < ns; j++)
                {
                    int a = k * ns + j, b = (k + 1) * ns + j;
                    int c = (k + 1) * ns + (j + 1) % ns, d = k * ns + (j + 1) % ns;
                    m.Triangle(a, d, c);
                    m.Triangle(a, c, b);
                }
            return m;
        }
        // (a flat band: four faces, each with its own normals, and the two ends closed)
        var corners = new List<BioVec[]>();
        for (var k = 0; k < path.Count; k++)
        {
            var u = Side(k);
            var v = BioV.Cross(ts[k], u);
            double hw = HalfWidth(k), ht = thickness / 2;
            corners.Add(new[] { path[k] + u * hw + v * ht, path[k] - u * hw + v * ht, path[k] - u * hw - v * ht, path[k] + u * hw - v * ht });
        }
        for (var face = 0; face < 4; face++)
        {
            int i0 = face, i1 = (face + 1) % 4;
            for (var k = 0; k < path.Count - 1; k++)
            {
                BioVec a = corners[k][i0], b = corners[k][i1], c = corners[k + 1][i1], d = corners[k + 1][i0];
                var mid = (a + b + c + d) / 4;
                m.FlatQuad(a, b, c, d, mid - (path[k] + path[k + 1]) / 2);
            }
        }
        BioVec[] first = corners[0], last = corners[path.Count - 1];
        m.FlatQuad(first[0], first[1], first[2], first[3], -ts[0]);
        m.FlatQuad(last[0], last[1], last[2], last[3], ts[path.Count - 1]);
        return m;
    }
}

// ---- many objects in one geometry ----

/// <summary>Meshes of one material merged into one, with the range of triangles and vertices each object's mesh took (so a
/// hit names its object: by triangle, as the Mac's SceneKit reports it, or by vertex, as WPF's
/// RayMeshGeometry3DHitTestResult.VertexIndex1 does): the batching of §3.4.</summary>
public sealed class BioMeshBatch
{
    public BioMesh Mesh { get; } = new();
    private readonly List<(string Id, int First, int Count, int FirstVertex, int VertexCount)> ranges = new();

    /// <summary>Each object's first triangle and triangle count (and first vertex and vertex count), in order.</summary>
    public IReadOnlyList<(string Id, int First, int Count, int FirstVertex, int VertexCount)> Ranges => ranges;

    public void Add(BioMesh m, string id)
    {
        if (m.IsEmpty) return;
        ranges.Add((id, Mesh.TriangleCount, m.TriangleCount, Mesh.VertexCount, m.VertexCount));
        Mesh.Append(m);
    }

    /// <summary>The object a triangle (by index in the merged mesh) belongs to.</summary>
    public string? ObjectAtTriangle(int t)
    {
        int lo = 0, hi = ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var r = ranges[mid];
            if (t < r.First) hi = mid - 1; else if (t >= r.First + r.Count) lo = mid + 1; else return r.Id;
        }
        return null;
    }

    /// <summary>The object a vertex (by index in the merged mesh) belongs to.</summary>
    public string? ObjectAtVertex(int v)
    {
        int lo = 0, hi = ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var r = ranges[mid];
            if (v < r.FirstVertex) hi = mid - 1; else if (v >= r.FirstVertex + r.VertexCount) lo = mid + 1; else return r.Id;
        }
        return null;
    }
}
