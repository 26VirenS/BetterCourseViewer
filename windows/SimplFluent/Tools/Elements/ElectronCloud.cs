namespace Simpl.Tools;

// The electron cloud model of an atom, for the element page's Cloud view (the Mac's ElectronCloud.swift, the same numbers
// to the last bit): each occupied subshell's electrons as a cloud of dots, each dot a place one of them could be found —
// a hydrogen-like orbital (its radial nodes kept) sized by Slater's rules, its electrons in its real orbitals by Hund's
// rule, the dots drawn from |ψ|² by a seeded generator so both apps draw the same cloud. No WPF here: it builds and runs
// in a plain console project (the spec's test vectors).

/// <summary>The electron cloud model: Slater's effective charges, the radial tables, the sampler and the generator.</summary>
public static class ElectronCloud
{
    public const double RhoMax = 80.0;
    public const int GridSteps = 4096;
    public const int PointsCap = 40000, PointsBase = 12000, PointsPerElectronOfZ = 1500, MinPerSubshell = 600;
    /// <summary>The Bohr radius in picometres.</summary>
    public const double BohrPm = 52.9177210544;

    /// <summary>Slater's effective principal quantum number n* for n = 1…7 (index 0 unused; 7 is our extrapolation).</summary>
    private static readonly double[] NStar = { 0.0, 1.0, 2.0, 3.0, 3.7, 4.0, 4.2, 4.3 };

    /// <summary>A subshell as the cloud draws it.</summary>
    public sealed class Sub
    {
        public required int N { get; init; }
        public required int L { get; init; }
        public required int Count { get; init; }
        /// <summary>Slater's effective nuclear charge.</summary>
        public required double Zeff { get; init; }
        /// <summary>The hydrogen-like exponent: the orbital is hydrogen's with Z = Zeta.</summary>
        public required double Zeta { get; init; }
        /// <summary>Dots per electron.</summary>
        public required int PerElectron { get; init; }
        /// <summary>Electrons in each real orbital, by Hund's rule (the apps' PTSubshell.filling).</summary>
        public required int[] Filling { get; init; }
        /// <summary>Bohr radii per unit of rho (rho = 2·Zeta·r / n).</summary>
        public double Scale => N / (2.0 * Zeta);
        /// <summary>Mean distance from the nucleus, in Bohr radii (exact for the hydrogen-like orbital).</summary>
        public double MeanRadius => (3 * N * N - L * (L + 1)) / (2.0 * Zeta);
        /// <summary>The radius inside which a fraction q of its electron density lies, in Bohr radii.</summary>
        public double Radius(double q) => InverseCdf(N, L, q) * Scale;
        public string Name => $"{N}{"spdf"[Math.Clamp(L, 0, 3)]}";
    }

    /// <summary>An atom's cloud: its subshells, and every dot (in Bohr radii) with the subshell and orbital it came from.</summary>
    public sealed class Cloud
    {
        public required int Z { get; init; }
        public required List<Sub> Subs { get; init; }
        public required double[] X { get; init; }
        public required double[] Y { get; init; }
        public required double[] Zs { get; init; }
        public required byte[] SubIndex { get; init; }
        public required byte[] Orbital { get; init; }
        /// <summary>The radius the whole atom is framed by: its largest subshell 95% radius (Bohr radii).</summary>
        public double FrameAll => Subs.Count == 0 ? 1 : Subs.Max(s => s.Radius(0.95));
        public int Count => X.Length;
    }

    // ---- Slater's rules ----

    /// <summary>Slater's group order: (1s)(2s,2p)(3s,3p)(3d)(4s,4p)(4d)(4f)(5s,5p)(5d)(5f)(6s,6p)(6d)(7s,7p).</summary>
    private static int Group(int n, int l) => n * 4 + (l <= 1 ? 0 : l - 1);

    /// <summary>Z − S by Slater's rules, S summed in whole hundredths so every platform gets the same double.</summary>
    public static double SlaterZeff(int z, IReadOnlyList<(int N, int L, int Count)> subs, int n, int l)
    {
        var me = Group(n, l);
        var s100 = 0;
        foreach (var (n2, l2, c) in subs)
        {
            var g = Group(n2, l2);
            if (g == me) s100 += (c - (n2 == n && l2 == l ? 1 : 0)) * (n == 1 ? 30 : 35);
            else if (l <= 1)
            {
                if (n2 == n - 1) s100 += c * 85;
                else if (n2 <= n - 2) s100 += c * 100;
            }
            else if (g < me) s100 += c * 100;
        }
        return (100 * z - s100) / 100.0;
    }

    /// <summary>The hydrogen-like exponent whose mean radius is Slater's orbital's: Zeff·(3n² − l(l+1)) / (n*(2n*+1)).</summary>
    public static double Zeta(double zeff, int n, int l)
    {
        var ns = NStar[n];
        return zeff * (double)(3 * n * n - l * (l + 1)) / (ns * (2.0 * ns + 1.0));
    }

    // ---- the radial distribution, tabulated in rho ----

    /// <summary>The generalised Laguerre polynomial L_k^(a)(x), by its three-term recurrence.</summary>
    public static double Laguerre(int k, int a, double x)
    {
        if (k == 0) return 1.0;
        double l0 = 1.0, l1 = 1.0 + a - x;
        for (var i = 1; i < k; i++)
        {
            var next = ((2 * i + 1 + a - x) * l1 - (i + a) * l0) / (i + 1);
            l0 = l1;
            l1 = next;
        }
        return l1;
    }

    /// <summary>P(rho) ∝ rho^(2l+2) · L_(n−l−1)^(2l+1)(rho)² · e^(−rho): the radial probability density, unnormalised.</summary>
    public static double Density(int n, int l, double rho)
    {
        var lg = Laguerre(n - l - 1, 2 * l + 1, rho);
        var pw = 1.0;
        for (var i = 0; i < 2 * l + 2; i++) pw *= rho;
        return pw * lg * lg * Math.Exp(-rho);
    }

    private static readonly Dictionary<int, double[]> Tables = new();

    /// <summary>The cumulative distribution on rho = 0, h, 2h … 80 (h = 80/4096), trapezoids, normalised to end at 1.</summary>
    public static double[] Table(int n, int l)
    {
        lock (Tables)
        {
            if (Tables.TryGetValue(n * 8 + l, out var have)) return have;
            const double h = RhoMax / GridSteps;
            var p = new double[GridSteps + 1];
            for (var i = 0; i <= GridSteps; i++) p[i] = Density(n, l, i * h);
            var c = new double[GridSteps + 1];
            for (var i = 1; i <= GridSteps; i++) c[i] = c[i - 1] + 0.5 * (p[i - 1] + p[i]) * h;
            var total = c[GridSteps];
            for (var i = 0; i <= GridSteps; i++) c[i] /= total;
            c[GridSteps] = 1.0;
            Tables[n * 8 + l] = c;
            return c;
        }
    }

    /// <summary>The rho below which a fraction u of the subshell's density lies (u in [0, 1)).</summary>
    public static double InverseCdf(int n, int l, double u)
    {
        var c = Table(n, l);
        int lo = 0, hi = GridSteps;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (c[mid] <= u) lo = mid; else hi = mid;
        }
        var d = c[lo + 1] - c[lo];
        var t = d > 0 ? (u - c[lo]) / d : 0.0;
        return (lo + t) * (RhoMax / GridSteps);
    }

    // ---- the angular distribution: each real orbital's polynomial on the unit sphere, and its largest square ----

    /// <summary>The real orbital's angular polynomial at a unit direction (the apps' order: px py pz; dxy dxz dyz dx²−y² dz²;
    /// fz³ fxz² fyz² fxyz fz(x²−y²) fx(x²−3y²) fy(3x²−y²)).</summary>
    public static double Poly(int l, int i, double x, double y, double z) => l switch
    {
        1 => i == 0 ? x : i == 1 ? y : z,
        2 => i switch
        {
            0 => x * y,
            1 => x * z,
            2 => y * z,
            3 => x * x - y * y,
            _ => 3.0 * z * z - 1.0,
        },
        3 => i switch
        {
            0 => z * (5.0 * z * z - 3.0),
            1 => x * (5.0 * z * z - 1.0),
            2 => y * (5.0 * z * z - 1.0),
            3 => x * y * z,
            4 => z * (x * x - y * y),
            5 => x * (x * x - 3.0 * y * y),
            _ => y * (3.0 * x * x - y * y),
        },
        _ => 1.0,
    };

    /// <summary>The largest value of Poly² on the unit sphere (the rejection bound).</summary>
    public static double Bound(int l, int i) => l switch
    {
        1 => 1.0,
        2 => i == 3 ? 1.0 : i == 4 ? 4.0 : 0.25,
        3 => i switch
        {
            0 => 4.0,
            1 or 2 => 6400.0 / 3375.0,
            3 => 1.0 / 27.0,
            4 => 4.0 / 27.0,
            _ => 1.0,
        },
        _ => 1.0,
    };

    // ---- the generator: SplitMix64 ----

    public struct Rng
    {
        public ulong State;
        public Rng(ulong seed) { State = seed; }

        public ulong NextU64()
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                var z = State;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>A double in [0, 1): the top 53 bits.</summary>
        public double Next() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);
    }

    /// <summary>A direction uniform on the sphere: a point uniform in the cube, kept if inside the ball, made unit.</summary>
    public static (double X, double Y, double Z) Direction(ref Rng rng)
    {
        while (true)
        {
            var x = 2.0 * rng.Next() - 1.0;
            var y = 2.0 * rng.Next() - 1.0;
            var z = 2.0 * rng.Next() - 1.0;
            var s = x * x + y * y + z * z;
            if (s > 1e-12 && s <= 1.0)
            {
                var inv = 1.0 / Math.Sqrt(s);
                return (x * inv, y * inv, z * inv);
            }
        }
    }

    /// <summary>One dot of an orbital, in Bohr radii: its distance from the radial table, its direction from |Y|².</summary>
    public static (double X, double Y, double Z) Sample(ref Rng rng, Sub s, int orbital)
    {
        var r = InverseCdf(s.N, s.L, rng.Next()) * s.Scale;
        while (true)
        {
            var (x, y, z) = Direction(ref rng);
            if (s.L > 0)
            {
                var p = Poly(s.L, orbital, x, y, z);
                var w = rng.Next();
                if (!(w * Bound(s.L, orbital) <= p * p)) continue;
            }
            return (r * x, r * y, r * z);
        }
    }

    // ---- an atom ----

    /// <summary>Electrons in each orbital by Hund's rule: one in each before any takes two (the apps' filling).</summary>
    public static int[] Filling(int l, int count)
    {
        var k = 2 * l + 1;
        var f = new int[k];
        for (var i = 0; i < k; i++) f[i] = (count > i ? 1 : 0) + (count > k + i ? 1 : 0);
        return f;
    }

    /// <summary>The subshells with their charges and dot counts (subs in the apps' order: n, then l).</summary>
    public static List<Sub> Plan(int z, IReadOnlyList<(int N, int L, int Count)> subs)
    {
        var total = Math.Min(PointsCap, PointsBase + PointsPerElectronOfZ * z);
        var perElectron = total / Math.Max(z, 1);
        var plan = new List<Sub>();
        foreach (var (n, l, c) in subs)
        {
            if (c <= 0 || n < 1 || n > 7 || l < 0 || l > 3) continue;
            var zeff = SlaterZeff(z, subs, n, l);
            plan.Add(new Sub
            {
                N = n, L = l, Count = c, Zeff = zeff, Zeta = Zeta(zeff, n, l),
                PerElectron = Math.Max(perElectron, (MinPerSubshell + c - 1) / c), Filling = Filling(l, c),
            });
        }
        return plan;
    }

    /// <summary>The atom's cloud: the generator seeded with its atomic number, drawn subshell by subshell, orbital by
    /// orbital.</summary>
    public static Cloud Build(int z, IReadOnlyList<(int N, int L, int Count)> subs)
    {
        var plan = Plan(z, subs);
        var count = 0;
        foreach (var s in plan) foreach (var f in s.Filling) count += f * s.PerElectron;
        double[] xs = new double[count], ys = new double[count], zs = new double[count];
        byte[] sub = new byte[count], orb = new byte[count];
        var rng = new Rng((ulong)z);
        var k = 0;
        for (var si = 0; si < plan.Count; si++)
        {
            var s = plan[si];
            for (var i = 0; i < s.Filling.Length; i++)
                for (var j = 0; j < s.Filling[i] * s.PerElectron; j++)
                {
                    var (x, y, zz) = Sample(ref rng, s, i);
                    xs[k] = x; ys[k] = y; zs[k] = zz; sub[k] = (byte)si; orb[k] = (byte)i;
                    k++;
                }
        }
        return new Cloud { Z = z, Subs = plan, X = xs, Y = ys, Zs = zs, SubIndex = sub, Orbital = orb };
    }
}
