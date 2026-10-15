namespace Simpl.Tools;

// Kinematics & Projectiles' maths (tools), the Mac's MotionModel line for line: the five quantities of motion in a
// straight line with constant acceleration, any three given, the other two solved and the equation each came from
// named; and a projectile's flight — its time, range, highest point and impact — with the two angles that hit a target.
// Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>The five kinematic quantities (SI): Δx in m, v₀ and v in m/s, a in m/s², t in s.</summary>
public sealed record Kinematics(double X, double U, double V, double A, double T)
{
    /// <summary>Which of the five the solver found (the keys of <see cref="KinematicsSolver.Keys"/>), and the equation each came from.</summary>
    public IReadOnlyList<(string Key, string Equation)> Solved { get; init; } = Array.Empty<(string, string)>();

    public double Of(string key) => key switch { "x" => X, "u" => U, "v" => V, "a" => A, _ => T };

    /// <summary>The position at a moment of the motion (from Δx = v₀t + ½at²).</summary>
    public double XAt(double t) => U * t + 0.5 * A * t * t;

    /// <summary>The velocity at a moment of the motion (from v = v₀ + at).</summary>
    public double VAt(double t) => U + A * t;
}

/// <summary>
/// Motion in a straight line with constant acceleration: the five quantities, any three given, the other two found —
/// from v = v₀ + at, Δx = v₀t + ½at², v² = v₀² + 2aΔx and Δx = ½(v₀ + v)t, the one each answer comes from named.
/// </summary>
public static class KinematicsSolver
{
    public const string Eq1 = "v = v₀ + at", Eq2 = "Δx = v₀t + ½at²", Eq3 = "v² = v₀² + 2aΔx", Eq4 = "Δx = ½(v₀ + v)t";

    /// <summary>The quantities' keys in the fields' order: Δx, v₀, v, a, t.</summary>
    public static readonly string[] Keys = { "x", "u", "v", "a", "t" };

    /// <summary>Earth's gravity, the g preset (down is negative).</summary>
    public const double G = 9.81;

    public static string Name(string key) => key switch { "x" => "Δx", "u" => "v₀", "v" => "v", "a" => "a", _ => "t" };
    public static string Unit(string key) => key switch { "x" => "m", "u" or "v" => "m/s", "a" => "m/s²", _ => "s" };

    /// <summary>
    /// The two quantities left blank, found from the three given — or why they can't be: fewer or more than three given,
    /// or numbers no motion fits (a = 0 with v ≠ v₀; t = 0 with Δx ≠ 0; v² less than nothing).
    /// </summary>
    public static (Kinematics? Result, string? Error) Solve(double? x, double? u, double? v, double? a, double? t)
    {
        var given = new[] { x, u, v, a, t }.Count(q => q != null);
        if (given < 3) return (null, "Fill in any three of the five — the other two are solved.");
        if (given > 3) return (null, "Leave two blank — those are the ones solved.");
        const double eps = 1e-12;
        try
        {
            // (each case named by what is missing)
            if (x == null && t == null)
            {
                // u, v, a
                double A = a!.Value, U = u!.Value, V = v!.Value;
                if (Math.Abs(A) < eps) return (null, Math.Abs(V - U) < eps ? "With a = 0 and v = v₀, any time and distance fit — give t or Δx." : "With a = 0 the velocity can't change from v₀ to v.");
                var T = (V - U) / A;
                var X = (V * V - U * U) / (2 * A);
                return (new Kinematics(X, U, V, A, T) { Solved = new[] { ("t", Eq1), ("x", Eq3) } }, null);
            }
            if (x == null && a == null)
            {
                // u, v, t
                double U = u!.Value, V = v!.Value, T = t!.Value;
                if (Math.Abs(T) < eps) return (null, "With t = 0 nothing has moved yet — give a time.");
                var A = (V - U) / T;
                var X = 0.5 * (U + V) * T;
                return (new Kinematics(X, U, V, A, T) { Solved = new[] { ("a", Eq1), ("x", Eq4) } }, null);
            }
            if (x == null && v == null)
            {
                // u, a, t
                double U = u!.Value, A = a!.Value, T = t!.Value;
                return (new Kinematics(U * T + 0.5 * A * T * T, U, U + A * T, A, T) { Solved = new[] { ("v", Eq1), ("x", Eq2) } }, null);
            }
            if (x == null && u == null)
            {
                // v, a, t
                double V = v!.Value, A = a!.Value, T = t!.Value;
                var U = V - A * T;
                return (new Kinematics(U * T + 0.5 * A * T * T, U, V, A, T) { Solved = new[] { ("u", Eq1), ("x", Eq2) } }, null);
            }
            if (t == null && a == null)
            {
                // u, v, x
                double U = u!.Value, V = v!.Value, X = x!.Value;
                if (Math.Abs(U + V) < eps) return (null, Math.Abs(X) < eps ? "With Δx = 0 and v = −v₀, any time fits — give t." : "With v = −v₀ the average velocity is 0, so Δx can't be anything but 0.");
                var T = 2 * X / (U + V);
                var A = Math.Abs(X) < eps ? 0 : (V * V - U * U) / (2 * X);
                if (Math.Abs(X) < eps && Math.Abs(V - U) > eps) return (null, "With Δx = 0 the velocity can't change from v₀ to v.");
                return (new Kinematics(X, U, V, A, T) { Solved = new[] { ("t", Eq4), ("a", Eq3) } }, null);
            }
            if (t == null && v == null)
            {
                // u, a, x: v² = u² + 2ax; the root whose time is not negative (the first time the position is reached)
                double U = u!.Value, A = a!.Value, X = x!.Value;
                var vv = U * U + 2 * A * X;
                if (vv < -eps) return (null, "No motion fits: v² = v₀² + 2aΔx comes out less than zero — check the signs.");
                var root = Math.Sqrt(Math.Max(0, vv));
                double V, T;
                if (Math.Abs(A) < eps)
                {
                    if (Math.Abs(U) < eps) return (null, "With v₀ = 0 and a = 0 nothing moves — give a velocity or an acceleration.");
                    V = U;
                    T = X / U;
                }
                else
                {
                    var t1 = (root - U) / A;
                    var t2 = (-root - U) / A;
                    // (the earlier non-negative time; failing that, the later)
                    var pick1 = t1 >= -eps && (t2 < -eps || t1 <= t2);
                    V = pick1 ? root : -root;
                    T = pick1 ? t1 : t2;
                }
                return (new Kinematics(X, U, V, A, T) { Solved = new[] { ("v", Eq3), ("t", Eq1) } }, null);
            }
            if (t == null && u == null)
            {
                // v, a, x: u² = v² − 2ax
                double V = v!.Value, A = a!.Value, X = x!.Value;
                var uu = V * V - 2 * A * X;
                if (uu < -eps) return (null, "No motion fits: v₀² = v² − 2aΔx comes out less than zero — check the signs.");
                var root = Math.Sqrt(Math.Max(0, uu));
                double U, T;
                if (Math.Abs(A) < eps)
                {
                    if (Math.Abs(V) < eps) return (null, "With v = 0 and a = 0 nothing moves — give a velocity or an acceleration.");
                    U = V;
                    T = X / V;
                }
                else
                {
                    var t1 = (V - root) / A;
                    var t2 = (V + root) / A;
                    var pick1 = t1 >= -eps && (t2 < -eps || t1 <= t2);
                    U = pick1 ? root : -root;
                    T = pick1 ? t1 : t2;
                }
                return (new Kinematics(X, U, V, A, T) { Solved = new[] { ("u", Eq3), ("t", Eq1) } }, null);
            }
            if (u == null && a == null)
            {
                // v, x, t
                double V = v!.Value, X = x!.Value, T = t!.Value;
                if (Math.Abs(T) < eps) return (null, "With t = 0 nothing has moved yet — give a time.");
                var U = 2 * X / T - V;
                return (new Kinematics(X, U, V, (V - U) / T, T) { Solved = new[] { ("u", Eq4), ("a", Eq1) } }, null);
            }
            if (v == null && a == null)
            {
                // u, x, t
                double U = u!.Value, X = x!.Value, T = t!.Value;
                if (Math.Abs(T) < eps) return (null, "With t = 0 nothing has moved yet — give a time.");
                var A = 2 * (X - U * T) / (T * T);
                return (new Kinematics(X, U, U + A * T, A, T) { Solved = new[] { ("a", Eq2), ("v", Eq1) } }, null);
            }
            // u, v missing: a, x, t
            {
                double A = a!.Value, X = x!.Value, T = t!.Value;
                if (Math.Abs(T) < eps) return (null, "With t = 0 nothing has moved yet — give a time.");
                var U = (X - 0.5 * A * T * T) / T;
                return (new Kinematics(X, U, U + A * T, A, T) { Solved = new[] { ("u", Eq2), ("v", Eq1) } }, null);
            }
        }
        catch (Exception)
        {
            return (null, "These numbers don't describe a motion — check them.");
        }
    }
}

/// <summary>A projectile's flight: the time in the air, the range, the highest point, where and when it is reached,
/// and the speed and angle at impact (the angle below the horizontal, negative).</summary>
public sealed record Flight(double Time, double Range, double MaxHeight, double ApexTime, double ApexX, double ImpactSpeed, double ImpactAngle);

/// <summary>
/// A projectile launched at a speed and an angle from a height, under gravity g, air ignored: its flight, its position at
/// any moment, and the two angles that land it on a target at a distance.
/// </summary>
public static class Projectile
{
    public const double G = 9.81;

    /// <summary>The flight, or null when it can't be one (no speed, g ≤ 0, or launched from below the ground straight down).</summary>
    public static Flight? Solve(double v0, double angleDeg, double h, double g = G)
    {
        if (!(g > 0) || v0 < 0 || h < 0) return null;
        var th = angleDeg * PhysMath.DegToRad;
        double vx = v0 * Math.Cos(th), vy = v0 * Math.Sin(th);
        var disc = vy * vy + 2 * g * h;
        if (disc < 0) return null;
        var time = (vy + Math.Sqrt(disc)) / g;
        if (!(time > 0) && h == 0 && v0 == 0) time = 0;
        var range = vx * time;
        var apexTime = vy > 0 ? vy / g : 0;
        var maxHeight = vy > 0 ? h + vy * vy / (2 * g) : h;
        var vyEnd = vy - g * time;
        var impactSpeed = Math.Sqrt(vx * vx + vyEnd * vyEnd);
        var impactAngle = Math.Atan2(vyEnd, vx) * PhysMath.RadToDeg;
        return new Flight(time, range, maxHeight, apexTime, vx * apexTime, impactSpeed, impactAngle);
    }

    /// <summary>Where the projectile is after <paramref name="t"/> seconds (x from the launch point, y above the ground).</summary>
    public static (double X, double Y) At(double v0, double angleDeg, double h, double t, double g = G)
    {
        var th = angleDeg * PhysMath.DegToRad;
        return (v0 * Math.Cos(th) * t, h + v0 * Math.Sin(th) * t - 0.5 * g * t * t);
    }

    /// <summary>
    /// The angles that land a projectile launched at <paramref name="v0"/> from height <paramref name="h"/> on the ground
    /// at distance <paramref name="d"/>: two (the low and the high, the same once when the target is at the very edge of
    /// reach), or none when it is out of reach. From d·tanθ − g·d²(1 + tan²θ)/(2v₀²) + h = 0, a quadratic in tanθ.
    /// </summary>
    public static double[] TargetAngles(double v0, double d, double h, double g = G)
    {
        if (!(v0 > 0) || !(d > 0) || !(g > 0)) return Array.Empty<double>();
        var k = g * d * d / (2 * v0 * v0);
        // −k·T² + d·T + (h − k) = 0
        var disc = d * d - 4 * (-k) * (h - k);
        if (disc < -1e-9) return Array.Empty<double>();
        disc = Math.Max(0, disc);
        var root = Math.Sqrt(disc);
        var t1 = (-d + root) / (2 * -k);
        var t2 = (-d - root) / (2 * -k);
        var a1 = Math.Atan(t1) * PhysMath.RadToDeg;
        var a2 = Math.Atan(t2) * PhysMath.RadToDeg;
        var low = Math.Min(a1, a2);
        var high = Math.Max(a1, a2);
        return Math.Abs(high - low) < 1e-6 ? new[] { low } : new[] { low, high };
    }
}
