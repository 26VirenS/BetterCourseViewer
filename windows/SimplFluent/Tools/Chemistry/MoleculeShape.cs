using System.Windows.Media.Media3D;

namespace Simpl.Tools;

// The 3D builder's shapes, laid out by VSEPR instead of found by forces (the Mac's MoleculeShape.swift, its VSEPR,
// 1.3.74). Round each atom its electron groups — its bonds, its lone pairs and an odd electron — take the directions
// their number gives: two in a line, three round a triangle, four a tetrahedron, five a trigonal bipyramid (lone pairs
// round its middle, where there is more room), six an octahedron (lone pairs opposite each other). Each lone pair
// squeezes the bonds a little closer (ammonia 107°, water 104.5°) and an odd electron, which takes less room than a bond,
// lets them open (NO₂ 134°). The molecule is grown from its first atom outwards: each atom's groups are set round the bond
// it was reached by and turned about that bond to stand staggered from its neighbour's, a bond across from a lone pair on
// the next atom preferred (so hydrogen peroxide is skewed, as it is, and not flat). Nothing is left to settle, so a
// symmetrical molecule comes out exactly symmetrical and its bond dipoles cancel. (The forces this replaces pushed each
// atom's groups apart with nothing pushing back on the atom they were round: carbon dioxide settled bent at about 90°
// and polar, water at 120°, methane from the Lewis maker square.) A molecule with a ring cannot be grown that way: it is
// eased into shape from where it is (as drawn), its bonds held at their lengths and the angles at each atom at the ones
// its groups give, and its lone pairs set in the room its bonds leave.

internal static class Vsepr
{
    /// <summary>How far each lone pair bends the bonds round its atom away from itself; an odd electron, smaller than a
    /// bond, lets them open instead.</summary>
    public const double LonePairSqueeze = 0.047, OddElectronSqueeze = -0.135;

    /// <summary>How much a bond across from a lone pair on the atom next to it is preferred, turning about the bond
    /// between them.</summary>
    public const double AcrossLonePair = 0.15;

    private static Vector3D V(double x, double y, double z) => new(x, y, z);

    private static Vector3D Unit(Vector3D v) => v / v.Length;

    /// <summary>
    /// The directions for n electron groups, the ones lone pairs take first listed first: a line; a triangle; a
    /// tetrahedron, two of its corners up out of the page and two down in it; a trigonal bipyramid's middle, then its
    /// ends; an octahedron's front and back, then its square; a pentagonal bipyramid's ends, then its middle; a square
    /// antiprism.
    /// </summary>
    public static List<Vector3D> Ideal(int n)
    {
        switch (n)
        {
            case <= 1: return new() { V(1, 0, 0) };
            case 2: return new() { V(1, 0, 0), V(-1, 0, 0) };
            case 3: return Enumerable.Range(0, 3).Select(k => { var t = Math.PI / 2 + k * 2 * Math.PI / 3; return V(Math.Cos(t), Math.Sin(t), 0); }).ToList();
            case 4:
            {
                double c = Math.Sqrt(1.0 / 3), s = Math.Sqrt(2.0 / 3);
                return new() { V(0, c, s), V(0, c, -s), V(s, -c, 0), V(-s, -c, 0) };
            }
            case 5:
                return new[] { 2.0, 4, 0 }.Select(k => { var t = k * Math.PI / 3; return V(0, Math.Cos(t), Math.Sin(t)); })
                    .Concat(new[] { V(1, 0, 0), V(-1, 0, 0) }).ToList();
            case 6: return new() { V(0, 0, 1), V(0, 0, -1), V(1, 0, 0), V(-1, 0, 0), V(0, 1, 0), V(0, -1, 0) };
            case 7:
                return new[] { V(0, 0, 1), V(0, 0, -1) }
                    .Concat(Enumerable.Range(0, 5).Select(k => { var t = Math.PI / 2 + k * 2 * Math.PI / 5; return V(Math.Cos(t), Math.Sin(t), 0); })).ToList();
            case 8:
                return Enumerable.Range(0, 8).Select(k =>
                {
                    var t = k * Math.PI / 2 + (k < 4 ? 0 : Math.PI / 4);
                    var z = k < 4 ? 0.55 : -0.55;
                    var r = Math.Sqrt(1 - z * z);
                    return V(r * Math.Cos(t), r * Math.Sin(t), z);
                }).ToList();
            default:
                // (more than any atom here has: spread evenly round a sphere)
                return Enumerable.Range(0, n).Select(k =>
                {
                    var z = 1 - 2 * (k + 0.5) / n;
                    var r = Math.Sqrt(1 - z * z);
                    var t = k * Math.PI * (3 - Math.Sqrt(5));
                    return V(r * Math.Cos(t), r * Math.Sin(t), z);
                }).ToList();
        }
    }

    /// <summary>An atom's groups: its bonds' directions first, then its lone pairs', then its odd electron's.</summary>
    public static List<Vector3D> Groups(int b, int l, int s)
    {
        if (b + l + s <= 0) return new();
        var u = Ideal(b + l + s);
        var lone = u.Take(l).ToList();
        var odd = u.Skip(l).Take(s).ToList();
        var push = new Vector3D();
        foreach (var x in lone) push += x * LonePairSqueeze;
        foreach (var x in odd) push += x * OddElectronSqueeze;
        return u.Skip(l + s).Select(x => Unit(x - push)).Concat(lone).Concat(odd).ToList();
    }

    /// <summary>v turned about the unit axis k by an angle.</summary>
    public static Vector3D Turn(Vector3D v, Vector3D k, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return v * c + Vector3D.CrossProduct(k, v) * s + k * (Vector3D.DotProduct(k, v) * (1 - c));
    }

    /// <summary>The turn (a unit axis and an angle) that takes the unit vector a to b.</summary>
    public static (Vector3D Axis, double Angle) Turning(Vector3D a, Vector3D b)
    {
        var axis = Vector3D.CrossProduct(a, b);
        var d = Math.Clamp(Vector3D.DotProduct(a, b), -1, 1);
        if (axis.Length > 1e-9) return (Unit(axis), Math.Acos(d));
        if (d > 0) return (V(1, 0, 0), 0);
        return (Unit(Vector3D.CrossProduct(a, Math.Abs(a.X) < 0.9 ? V(1, 0, 0) : V(0, 1, 0))), Math.PI);
    }

    /// <summary>v less its part along the unit axis k.</summary>
    private static Vector3D Flat(Vector3D v, Vector3D k) => v - k * Vector3D.DotProduct(v, k);

    /// <summary>
    /// The turn about their middles that best lays the points a on the points b (Horn's method: the turn's quaternion is
    /// the leading eigenvector of a 4 × 4 matrix of their products, found by squaring the matrix until one direction is
    /// left in it).
    /// </summary>
    public static Func<Vector3D, Vector3D> BestTurn(IReadOnlyList<Vector3D> a, IReadOnlyList<Vector3D> b)
    {
        var count = Math.Min(a.Count, b.Count);
        double n = Math.Max(count, 1);
        Vector3D ca = new(), cb = new();
        for (var i = 0; i < a.Count; i++) ca += a[i];
        for (var i = 0; i < b.Count; i++) cb += b[i];
        ca /= n;
        cb /= n;
        var s = new double[3, 3];
        for (var k = 0; k < count; k++)
        {
            var p = a[k] - ca;
            var q = b[k] - cb;
            double[] pv = { p.X, p.Y, p.Z }, qv = { q.X, q.Y, q.Z };
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++) s[i, j] += pv[i] * qv[j];
        }
        double xx = s[0, 0], xy = s[0, 1], xz = s[0, 2], yx = s[1, 0], yy = s[1, 1], yz = s[1, 2], zx = s[2, 0], zy = s[2, 1], zz = s[2, 2];
        var m = new double[4, 4]
        {
            { xx + yy + zz, yz - zy, zx - xz, xy - yx },
            { yz - zy, xx - yy - zz, xy + yx, zx + xz },
            { zx - xz, xy + yx, yy - xx - zz, yz + zy },
            { xy - yx, zx + xz, yz + zy, zz - xx - yy },
        };
        var qq = Leading(m);
        if (qq.Length != 4) return v => v - ca + cb;
        double w = qq[0], qx = qq[1], qy = qq[2], qz = qq[3];
        var r0 = V(1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - w * qz), 2 * (qx * qz + w * qy));
        var r1 = V(2 * (qx * qy + w * qz), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - w * qx));
        var r2 = V(2 * (qx * qz - w * qy), 2 * (qy * qz + w * qx), 1 - 2 * (qx * qx + qy * qy));
        return v =>
        {
            var d = v - ca;
            return V(Vector3D.DotProduct(r0, d), Vector3D.DotProduct(r1, d), Vector3D.DotProduct(r2, d)) + cb;
        };
    }

    /// <summary>
    /// The leading eigenvector (unit) of a symmetric matrix: shifted so that every eigenvalue is positive and the largest
    /// stays the largest, then squared until one direction is left in it, whose longest column is taken.
    /// </summary>
    public static double[] Leading(double[,] a)
    {
        var n = a.GetLength(0);
        var m = (double[,])a.Clone();
        var shift = 1e-9;
        foreach (var x in a) shift += Math.Abs(x);
        for (var i = 0; i < n; i++) m[i, i] += shift;
        for (var step = 0; step < 40; step++)
        {
            var sq = new double[n, n];
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    for (var k = 0; k < n; k++) sq[i, j] += m[i, k] * m[k, j];
            var top = 1e-300;
            foreach (var x in sq) top = Math.Max(top, Math.Abs(x));
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++) m[i, j] = sq[i, j] / top;
        }
        var best = Array.Empty<double>();
        var len = 0.0;
        for (var j = 0; j < n; j++)
        {
            var column = Enumerable.Range(0, n).Select(i => m[i, j]).ToArray();
            var l = Math.Sqrt(column.Sum(x => x * x));
            if (l > len) (best, len) = (column, l);
        }
        return len > 1e-12 ? best.Select(x => x / len).ToArray() : Array.Empty<double>();
    }

    /// <summary>
    /// A piece turned about its middle to face the viewer: spread most from side to side, then up and down, least towards
    /// the viewer (its principal axes), so a chain lies across the view and a flat molecule faces it.
    /// </summary>
    public static void Face(List<int> part, Vector3D[] pos, List<Vector3D>[] frame)
    {
        var c = new Vector3D();
        foreach (var i in part) c += pos[i];
        c /= Math.Max(part.Count, 1);
        var spread = new double[3, 3];
        foreach (var i in part)
        {
            var d = pos[i] - c;
            double[] dv = { d.X, d.Y, d.Z };
            for (var x = 0; x < 3; x++)
                for (var y = 0; y < 3; y++) spread[x, y] += dv[x] * dv[y];
        }
        var e = Leading(spread);
        if (e.Length != 3) return;
        var a = V(e[0], e[1], e[2]);
        // (the most along it taken away, the next most is the second axis)
        var along = 0.0;
        for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++) along += e[x] * spread[x, y] * e[y];
        for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++) spread[x, y] -= along * e[x] * e[y];
        var f = Leading(spread);
        var b = f.Length == 3 ? Flat(V(f[0], f[1], f[2]), a) : new Vector3D();
        if (b.Length < 1e-6) b = Vector3D.CrossProduct(a, Math.Abs(a.X) < 0.9 ? V(1, 0, 0) : V(0, 1, 0));
        b = Unit(b);
        var k = Vector3D.CrossProduct(a, b);
        foreach (var i in part)
        {
            var d = pos[i] - c;
            pos[i] = c + V(Vector3D.DotProduct(d, a), Vector3D.DotProduct(d, b), Vector3D.DotProduct(d, k));
            frame[i] = frame[i].Select(x => V(Vector3D.DotProduct(x, a), Vector3D.DotProduct(x, b), Vector3D.DotProduct(x, k))).ToList();
        }
    }

    /// <summary>
    /// Where each atom goes and which way its lone pairs point. Atoms are given by their lone pairs and odd electrons,
    /// bonds by their two atoms (indices) and length, and <paramref name="start"/> is where the atoms are now. With
    /// <paramref name="keep"/>, each piece is turned and moved to lie as close to the start as it can — a molecule being
    /// built stays where it is, one from the Lewis builder as it was drawn; without, each is laid out afresh round its
    /// first atom. A piece with a ring is eased into shape from the start either way.
    /// </summary>
    public static (Vector3D[] Positions, List<Vector3D>[] LonePairs) Layout(int[] lone, int[] odd, IReadOnlyList<(int A, int B, double Length)> bonds, Vector3D[] start, bool keep)
    {
        var n = lone.Length;
        var nb = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        var length = new Dictionary<int, double>();
        foreach (var e in bonds)
        {
            if (e.A == e.B || e.A >= n || e.B >= n || nb[e.A].Contains(e.B)) continue;
            nb[e.A].Add(e.B);
            nb[e.B].Add(e.A);
            length[e.A * n + e.B] = e.Length;
            length[e.B * n + e.A] = e.Length;
        }
        var given = Enumerable.Range(0, n).Select(i => Groups(nb[i].Count, lone[i], odd[i])).ToArray();
        var pos = (Vector3D[])start.Clone();
        var frame = given.Select(g => g.ToList()).ToArray();
        var seen = new bool[n];
        var pieces = new List<List<int>>();
        for (var root = 0; root < n; root++)
        {
            if (seen[root]) continue;
            var part = new List<int> { root };
            seen[root] = true;
            for (var k = 0; k < part.Count; k++)
                foreach (var y in nb[part[k]])
                    if (!seen[y]) { seen[y] = true; part.Add(y); }
            if (part.Sum(i => nb[i].Count) / 2 >= part.Count)
            {
                Ease(part, nb, length, given, start, pos, frame);
            }
            else
            {
                Grow(root, nb, length, given, pos, frame);
                if (keep)
                {
                    var fit = BestTurn(part.Select(i => pos[i]).ToList(), part.Select(i => start[i]).ToList());
                    var o = fit(new Vector3D());
                    foreach (var i in part)
                    {
                        pos[i] = fit(pos[i]);
                        frame[i] = frame[i].Select(x => fit(x) - o).ToList();
                    }
                }
            }
            // (laid out afresh, a molecule of more than one centre — a chain, a ring — is turned to face the viewer; one
            // centre's own directions already do)
            if (!keep && part.Count(i => nb[i].Count >= 2) >= 2) Face(part, pos, frame);
            // (never on top of a piece already placed: moved along until it is clear)
            for (var tries = 0; tries < 400; tries++)
            {
                var close = double.MaxValue;
                foreach (var j in pieces.SelectMany(p => p))
                    foreach (var i in part) close = Math.Min(close, (pos[i] - pos[j]).Length);
                if (close >= 2.2) break;
                foreach (var i in part) pos[i] += V(0.25, 0, 0);
            }
            pieces.Add(part);
        }
        var pairs = Enumerable.Range(0, n).Select(i => frame[i].Skip(nb[i].Count).Take(lone[i]).ToList()).ToArray();
        return (pos, pairs);
    }

    /// <summary>
    /// A piece without a ring, grown out from its first atom: each atom's groups set round the bond back to the atom it
    /// was reached from, then turned about that bond to stand staggered from that atom's groups — the turn whose groups'
    /// tips are furthest from the neighbour's, a bond across from a lone pair (either way round) counted in its favour.
    /// </summary>
    private static void Grow(int root, List<int>[] nb, Dictionary<int, double> length, List<Vector3D>[] given, Vector3D[] pos, List<Vector3D>[] frame)
    {
        var n = nb.Length;
        var slot = new Dictionary<int, int>();
        pos[root] = new Vector3D();
        frame[root] = given[root].ToList();
        for (var k = 0; k < nb[root].Count; k++) slot[root * n + nb[root][k]] = k;
        var placed = new HashSet<int> { root };
        var queue = new List<int> { root };
        for (var q = 0; q < queue.Count; q++)
        {
            var c = queue[q];
            foreach (var y in nb[c])
            {
                if (!placed.Add(y)) continue;
                queue.Add(y);
                if (!slot.TryGetValue(c * n + y, out var sc) || !length.TryGetValue(c * n + y, out var len)) continue;
                var u = frame[c][sc];
                pos[y] = pos[c] + u * len;
                var back = -u;
                var mine = given[y];
                var bonded = nb[y].Count;
                var theirs = Enumerable.Range(0, frame[c].Count).Where(d => d != sc).ToList();
                (double Score, List<Vector3D> Dirs, int Back)? best = null;
                // (any of its bonds may point back when its groups are not all alike: five or more)
                foreach (var kb in mine.Count > 4 ? Enumerable.Range(0, bonded) : new[] { 0 })
                {
                    var t = Turning(mine[kb], back);
                    var set = mine.Select(x => Turn(x, t.Axis, t.Angle)).ToList();
                    var others = Enumerable.Range(0, set.Count).Where(e => e != kb).ToList();
                    // (the turns that put one of its groups straight across from one of the neighbour's)
                    var turns = new List<double>();
                    foreach (var e in others)
                    {
                        var fe = Flat(set[e], back);
                        if (fe.Length <= 1e-6) continue;
                        foreach (var d in theirs)
                        {
                            var fd = Flat(-frame[c][d], back);
                            if (fd.Length <= 1e-6) continue;
                            Vector3D a = Unit(fe), b = Unit(fd);
                            turns.Add(Math.Atan2(Vector3D.DotProduct(Vector3D.CrossProduct(a, b), back), Vector3D.DotProduct(a, b)));
                        }
                    }
                    if (turns.Count == 0) turns.Add(0);
                    foreach (var phi in turns)
                    {
                        var dirs = set.Select(x => Turn(x, back, phi)).ToList();
                        var score = 0.0;
                        foreach (var e in others)
                        {
                            foreach (var d in theirs)
                            {
                                var tip = u * len + dirs[e];
                                score += 1 / Math.Max((tip - frame[c][d]).Length, 1e-6);
                                if ((e >= bonded) != (d >= nb[c].Count))
                                {
                                    Vector3D fe = Flat(dirs[e], back), fd = Flat(frame[c][d], back);
                                    if (fe.Length > 1e-6 && fd.Length > 1e-6)
                                        score -= AcrossLonePair * Math.Max(0, -Vector3D.DotProduct(Unit(fe), Unit(fd)));
                                }
                            }
                        }
                        if (best == null || score < best.Value.Score - 1e-9) best = (score, dirs, kb);
                    }
                }
                if (best is not { } chosen) continue;
                frame[y] = chosen.Dirs;
                slot[y * n + c] = chosen.Back;
                var free = Enumerable.Range(0, bonded).Where(k => k != chosen.Back).ToList();
                var rest = nb[y].Where(z => z != c).ToList();
                for (var k = 0; k < rest.Count && k < free.Count; k++) slot[y * n + rest[k]] = free[k];
            }
        }
    }

    /// <summary>
    /// A piece with a ring, eased into shape from where it is: its bonds held at their lengths, the angle between each two
    /// bonds of an atom at the nearest of the angles its groups make, and atoms neither bonded nor sharing a neighbour kept
    /// apart; then each atom's groups turned to fit its bonds as they are, for its lone pairs.
    /// </summary>
    private static void Ease(List<int> part, List<int>[] nb, Dictionary<int, double> length, List<Vector3D>[] given, Vector3D[] start, Vector3D[] pos, List<Vector3D>[] frame)
    {
        var n = nb.Length;
        for (var k = 0; k < part.Count; k++)
        {
            var i = part[k];
            pos[i] = start[i];
            // (two atoms in the same place moved a little apart)
            if (part.Take(k).Any(j => (pos[j] - pos[i]).Length < 1e-3))
                pos[i] += V(Math.Sin(i * 1.7), Math.Cos(i * 2.3), Math.Sin(i * 3.1)) * 0.3;
        }
        var angles = Enumerable.Range(0, n).Select(i =>
        {
            var b = nb[i].Count;
            var outs = new List<double>();
            for (var x = 0; x < b; x++)
                for (var y = x + 1; y < b; y++) outs.Add(Math.Acos(Math.Clamp(Vector3D.DotProduct(given[i][x], given[i][y]), -1, 1)));
            return outs;
        }).ToArray();
        // (the pairs bonded or with a neighbour in common: their distance is the bonds' and angles' to keep)
        var near = new HashSet<int>();
        foreach (var i in part)
            foreach (var j in nb[i])
            {
                near.Add(i * n + j);
                foreach (var k in nb[j]) near.Add(i * n + k);
            }
        var f = new Vector3D[n];
        for (var step = 0; step < 1500; step++)
        {
            Array.Clear(f);
            foreach (var i in part)
            {
                foreach (var j in nb[i])
                {
                    if (i >= j) continue;
                    var d = pos[j] - pos[i];
                    var l = Math.Max(d.Length, 1e-6);
                    var want = length.TryGetValue(i * n + j, out var w) ? w : 1.5;
                    var push = d * ((l - want) * 0.5 / l);
                    f[i] += push;
                    f[j] -= push;
                }
                var ns = nb[i];
                for (var x = 0; x < ns.Count; x++)
                {
                    for (var y = x + 1; y < ns.Count; y++)
                    {
                        int a = ns[x], b = ns[y];
                        var now = Math.Acos(Math.Clamp(Vector3D.DotProduct(Unit(pos[a] - pos[i]), Unit(pos[b] - pos[i])), -1, 1));
                        var want = angles[i].Count == 0 ? now : angles[i].MinBy(t => Math.Abs(t - now));
                        var la = length.TryGetValue(i * n + a, out var wa) ? wa : 1.5;
                        var lb = length.TryGetValue(i * n + b, out var wb) ? wb : 1.5;
                        var target = Math.Sqrt(la * la + lb * lb - 2 * la * lb * Math.Cos(want));
                        var d = pos[b] - pos[a];
                        var l = Math.Max(d.Length, 1e-6);
                        var push = d * ((l - target) * 0.3 / l);
                        f[a] += push;
                        f[b] -= push;
                    }
                }
                foreach (var j in part)
                {
                    if (i >= j || near.Contains(i * n + j)) continue;
                    var d = pos[j] - pos[i];
                    var l = Math.Max(d.Length, 1e-6);
                    if (l < 2.4)
                    {
                        var push = d * ((2.4 - l) * 0.2 / l);
                        f[i] -= push;
                        f[j] += push;
                    }
                }
            }
            var rate = step < 1000 ? 0.4 : 0.2;
            foreach (var i in part)
            {
                var m = f[i] * rate;
                if (m.Length > 0.2) m = Unit(m) * 0.2;
                pos[i] += m;
            }
        }
        foreach (var i in part)
        {
            var b = nb[i].Count;
            if (b == 0) continue;
            var from = new List<Vector3D> { new() };
            from.AddRange(given[i].Take(b));
            var to = new List<Vector3D> { new() };
            to.AddRange(nb[i].Select(j => Unit(pos[j] - pos[i])));
            var fit = BestTurn(from, to);
            var o = fit(new Vector3D());
            frame[i] = given[i].Select(x => fit(x) - o).ToList();
        }
    }
}
