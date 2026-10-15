using System.Globalization;

namespace Simpl.Tools;

// Vectors' maths (tools), the Mac's VectorModel line for line: a vector by its components or by its magnitude and angle,
// magnitudes and directions, the sum, dot and cross products, the angle between two, unit vectors and projections; and
// the launch's list ("3,4;1,2") read and written. Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>A vector in 2D or 3D (z is 0 in 2D).</summary>
public readonly record struct Vec(double X, double Y, double Z = 0)
{
    public static readonly Vec Zero = new(0, 0, 0);

    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec operator -(Vec a) => new(-a.X, -a.Y, -a.Z);
    public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec operator *(double s, Vec a) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>|v|.</summary>
    public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>The direction in the plane, from +x counter-clockwise, 0 up to 360°.</summary>
    public double AngleDeg
    {
        get
        {
            var a = Math.Atan2(Y, X) * PhysMath.RadToDeg;
            if (a < 0) a += 360;
            return Math.Abs(a - 360) < 1e-9 ? 0 : a;
        }
    }

    /// <summary>"(3, 4)" in 2D, "(1, 2, 3)" in 3D, each part to four figures.</summary>
    public string Text(bool threeD) => threeD ? $"({PhysMath.Text(X)}, {PhysMath.Text(Y)}, {PhysMath.Text(Z)})" : $"({PhysMath.Text(X)}, {PhysMath.Text(Y)})";

    /// <summary>As the launch writes it: "3,4" or "1,2,3", ASCII.</summary>
    public string Plain(bool threeD) => threeD
        ? string.Create(CultureInfo.InvariantCulture, $"{X:0.######},{Y:0.######},{Z:0.######}")
        : string.Create(CultureInfo.InvariantCulture, $"{X:0.######},{Y:0.######}");
}

/// <summary>Vector arithmetic, by name.</summary>
public static class VectorMath
{
    /// <summary>A 2D vector from its magnitude and its angle from +x, counter-clockwise, in degrees.</summary>
    public static Vec Polar(double magnitude, double angleDeg)
    {
        var th = angleDeg * PhysMath.DegToRad;
        return new Vec(magnitude * Math.Cos(th), magnitude * Math.Sin(th), 0);
    }

    public static Vec Sum(IEnumerable<Vec> vs)
    {
        var s = Vec.Zero;
        foreach (var v in vs) s += v;
        return s;
    }

    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec Cross(Vec a, Vec b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>The angle between two vectors in degrees (0 to 180); null when either is zero. From atan2(|a×b|, a·b),
    /// which keeps its precision where acos of the cosine loses it, at 0° and 180°.</summary>
    public static double? AngleBetween(Vec a, Vec b)
    {
        double la = a.Magnitude, lb = b.Magnitude;
        if (la < 1e-12 || lb < 1e-12) return null;
        return Math.Atan2(Cross(a, b).Magnitude, Dot(a, b)) * PhysMath.RadToDeg;
    }

    /// <summary>The vector made one long (a zero vector stays zero).</summary>
    public static Vec Unit(Vec a)
    {
        var l = a.Magnitude;
        return l < 1e-12 ? Vec.Zero : new Vec(a.X / l, a.Y / l, a.Z / l);
    }

    /// <summary>The projection of a onto b: (a·b̂)b̂; null when b is zero.</summary>
    public static Vec? Projection(Vec a, Vec b)
    {
        var l = b.Magnitude;
        if (l < 1e-12) return null;
        return b * (Dot(a, b) / (l * l));
    }

    /// <summary>The scalar projection of a onto b, a·b̂; null when b is zero.</summary>
    public static double? ScalarProjection(Vec a, Vec b)
    {
        var l = b.Magnitude;
        return l < 1e-12 ? null : Dot(a, b) / l;
    }

    /// <summary>The launch's list: vectors separated by ";", components by "," ("3,4;1,2"; "1,2,3;4,5,6"). A part that
    /// isn't a number makes the vector 0 there; an empty list gives none.</summary>
    public static List<Vec> Parse(string? list)
    {
        var vs = new List<Vec>();
        if (string.IsNullOrWhiteSpace(list)) return vs;
        foreach (var part in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var c = part.Split(',', StringSplitOptions.TrimEntries).Select(s => PhysMath.Number(s) ?? 0).ToList();
            if (c.Count == 0) continue;
            vs.Add(new Vec(c[0], c.Count > 1 ? c[1] : 0, c.Count > 2 ? c[2] : 0));
            if (vs.Count == 6) break;
        }
        return vs;
    }

    /// <summary>The list as the launch writes it ("3,4;1,2").</summary>
    public static string Write(IEnumerable<Vec> vs, bool threeD) => string.Join(";", vs.Select(v => v.Plain(threeD)));

    /// <summary>The names the vectors go by: A, B, C …</summary>
    public static string Name(int i) => ((char)('A' + i)).ToString();
}
