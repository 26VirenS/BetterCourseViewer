using System.Globalization;
using System.Numerics;
using System.Text;

namespace Simpl.Tools;

// The Matrix Calculator's maths (tools; the Mac's MatrixMath): matrices of exact fractions (big integers over big
// integers, so 1/3 stays 1/3 and nothing overflows), their sums and products, the determinant, the inverse, the rank,
// the reduced row echelon form with each row operation said, the trace, A·x = b, powers, and eigenvalues by the shifted
// QR iteration on the Hessenberg form (real and complex) — with no window in it, so the tests can run it as it is.

/// <summary>An exact fraction: its numerator over a positive denominator, in lowest terms.</summary>
internal readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    public readonly BigInteger Num, Den;

    public static readonly Rational Zero = new(0, 1), One = new(1, 1);

    public Rational(BigInteger num, BigInteger den)
    {
        if (den.IsZero) throw new DivideByZeroException();
        if (den.Sign < 0) { num = -num; den = -den; }
        var g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(num), den);
        if (!g.IsOne && !g.IsZero) { num /= g; den /= g; }
        Num = num;
        Den = den;
    }

    public Rational(long n) : this(n, 1) { }

    public bool IsZero => Num.IsZero;
    public bool IsInteger => Den.IsOne;
    public int Sign => Num.Sign;

    public static Rational operator +(Rational a, Rational b) => new(a.Num * b.Den + b.Num * a.Den, a.Den * b.Den);
    public static Rational operator -(Rational a, Rational b) => new(a.Num * b.Den - b.Num * a.Den, a.Den * b.Den);
    public static Rational operator *(Rational a, Rational b) => new(a.Num * b.Num, a.Den * b.Den);
    public static Rational operator /(Rational a, Rational b) => new(a.Num * b.Den, a.Den * b.Num);
    public static Rational operator -(Rational a) => new(-a.Num, a.Den);
    public static bool operator ==(Rational a, Rational b) => a.Num == b.Num && a.Den == b.Den;
    public static bool operator !=(Rational a, Rational b) => !(a == b);

    public Rational Abs() => Num.Sign < 0 ? -this : this;
    public bool Equals(Rational other) => this == other;
    public override bool Equals(object? obj) => obj is Rational r && this == r;
    public override int GetHashCode() => HashCode.Combine(Num, Den);
    public int CompareTo(Rational other) => (Num * other.Den).CompareTo(other.Num * Den);

    public double ToDouble()
    {
        if (Den.IsOne) return (double)Num;
        // (a huge fraction: scale both down so neither overflows a double on its own)
        var shift = Math.Max(0, (int)Math.Max(Num.GetBitLength(), Den.GetBitLength()) - 1000);
        return (double)(Num >> shift) / (double)(Den >> shift);
    }

    /// <summary>"3", "−3/4" (the typographic minus).</summary>
    public string Text() => (Den.IsOne ? Num.ToString(CultureInfo.InvariantCulture) : $"{Num.ToString(CultureInfo.InvariantCulture)}/{Den.ToString(CultureInfo.InvariantCulture)}").Replace("-", "−");

    /// <summary>"3", "-3/4" (plain ASCII, for the clipboard).</summary>
    public string Ascii() => Text().Replace("−", "-");

    public override string ToString() => Text();

    /// <summary>A cell as typed: an integer, a fraction "3/4", or a decimal "0.25" (taken exactly as 1/4); <paramref name="exact"/>
    /// says whether it was an integer or a fraction, so the whole matrix knows whether its answers are exact.</summary>
    public static Rational? Parse(string s, out bool exact)
    {
        exact = false;
        var t = s.Trim().Replace("−", "-").Replace(",", ".");
        if (t.Length == 0) return null;
        var slash = t.IndexOf('/');
        if (slash > 0)
        {
            if (BigInteger.TryParse(t[..slash].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) &&
                BigInteger.TryParse(t[(slash + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var d) && !d.IsZero)
            {
                exact = true;
                return new Rational(n, d);
            }
            return null;
        }
        if (BigInteger.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole))
        {
            exact = true;
            return new Rational(whole, 1);
        }
        if (decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
        {
            // (0.25 is exactly 25/100: the decimal's digits over its power of ten)
            var bits = decimal.GetBits(dec);
            var scale = (bits[3] >> 16) & 0xFF;
            var negative = (bits[3] & unchecked((int)0x80000000)) != 0;
            var mantissa = (new BigInteger((uint)bits[2]) << 64) + (new BigInteger((uint)bits[1]) << 32) + new BigInteger((uint)bits[0]);
            return new Rational(negative ? -mantissa : mantissa, BigInteger.Pow(10, scale));
        }
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v))
        {
            // (1e-3 and the like: through a decimal when it fits)
            try { return Parse(((decimal)v).ToString(CultureInfo.InvariantCulture), out _); } catch (OverflowException) { return null; }
        }
        return null;
    }
}

/// <summary>A matrix of exact fractions.</summary>
internal sealed class Mat
{
    private readonly Rational[,] v;

    public int Rows { get; }
    public int Cols { get; }

    public Mat(int rows, int cols)
    {
        Rows = rows;
        Cols = cols;
        v = new Rational[rows, cols];
        for (var i = 0; i < rows; i++) for (var j = 0; j < cols; j++) v[i, j] = Rational.Zero;
    }

    public Mat(Rational[,] values) : this(values.GetLength(0), values.GetLength(1))
    {
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) v[i, j] = values[i, j];
    }

    /// <summary>A matrix from whole numbers, as the tests write them.</summary>
    public static Mat Of(params long[][] rows)
    {
        var m = new Mat(rows.Length, rows.Length == 0 ? 0 : rows[0].Length);
        for (var i = 0; i < m.Rows; i++) for (var j = 0; j < m.Cols; j++) m[i, j] = new Rational(rows[i][j]);
        return m;
    }

    public static Mat Identity(int n)
    {
        var m = new Mat(n, n);
        for (var i = 0; i < n; i++) m[i, i] = Rational.One;
        return m;
    }

    public Rational this[int i, int j] { get => v[i, j]; set => v[i, j] = value; }

    public bool IsSquare => Rows == Cols;

    public Mat Copy() => new(v);

    public double[,] ToDoubles()
    {
        var d = new double[Rows, Cols];
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) d[i, j] = v[i, j].ToDouble();
        return d;
    }

    public bool SameAs(Mat o)
    {
        if (o.Rows != Rows || o.Cols != Cols) return false;
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) if (v[i, j] != o[i, j]) return false;
        return true;
    }

    // ---- arithmetic ----

    public Mat Add(Mat o) => Zip(o, (a, b) => a + b);
    public Mat Subtract(Mat o) => Zip(o, (a, b) => a - b);

    private Mat Zip(Mat o, Func<Rational, Rational, Rational> f)
    {
        if (o.Rows != Rows || o.Cols != Cols) throw new MatrixError($"A is {Rows}×{Cols} and B is {o.Rows}×{o.Cols}: they have to be the same size to add or subtract.");
        var m = new Mat(Rows, Cols);
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) m[i, j] = f(v[i, j], o[i, j]);
        return m;
    }

    public Mat Multiply(Mat o)
    {
        if (Cols != o.Rows) throw new MatrixError($"A has {Cols} columns and B has {o.Rows} rows: A × B needs them equal.");
        var m = new Mat(Rows, o.Cols);
        for (var i = 0; i < Rows; i++)
            for (var j = 0; j < o.Cols; j++)
            {
                var sum = Rational.Zero;
                for (var k = 0; k < Cols; k++) sum += v[i, k] * o[k, j];
                m[i, j] = sum;
            }
        return m;
    }

    public Mat Scale(Rational k)
    {
        var m = new Mat(Rows, Cols);
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) m[i, j] = v[i, j] * k;
        return m;
    }

    public Mat Transpose()
    {
        var m = new Mat(Cols, Rows);
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) m[j, i] = v[i, j];
        return m;
    }

    public Rational Trace()
    {
        if (!IsSquare) throw new MatrixError("The trace needs a square matrix.");
        var t = Rational.Zero;
        for (var i = 0; i < Rows; i++) t += v[i, i];
        return t;
    }

    /// <summary>The determinant, by elimination (exact).</summary>
    public Rational Determinant()
    {
        if (!IsSquare) throw new MatrixError("The determinant needs a square matrix.");
        var a = Copy();
        var det = Rational.One;
        for (var c = 0; c < Rows; c++)
        {
            var p = -1;
            for (var r = c; r < Rows; r++) if (!a[r, c].IsZero) { p = r; break; }
            if (p < 0) return Rational.Zero;
            if (p != c) { a.SwapRows(p, c); det = -det; }
            det *= a[c, c];
            for (var r = c + 1; r < Rows; r++)
            {
                if (a[r, c].IsZero) continue;
                var f = a[r, c] / a[c, c];
                for (var j = c; j < Cols; j++) a[r, j] -= f * a[c, j];
            }
        }
        return det;
    }

    /// <summary>The inverse by Gauss–Jordan on [A | I], or null when A is singular.</summary>
    public Mat? Inverse()
    {
        if (!IsSquare) throw new MatrixError("The inverse needs a square matrix.");
        var aug = new Mat(Rows, Cols * 2);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < Cols; j++) aug[i, j] = v[i, j];
            aug[i, Cols + i] = Rational.One;
        }
        var (r, _, rank, _) = aug.Rref(Cols);
        if (rank < Rows) return null;
        var inv = new Mat(Rows, Cols);
        for (var i = 0; i < Rows; i++) for (var j = 0; j < Cols; j++) inv[i, j] = r[i, Cols + j];
        return inv;
    }

    public int Rank() => Rref().Rank;

    private void SwapRows(int a, int b)
    {
        for (var j = 0; j < Cols; j++) (v[a, j], v[b, j]) = (v[b, j], v[a, j]);
    }

    /// <summary>
    /// The reduced row echelon form, each row operation said as it is done ("R2 ← R2 − 4R1", "R1 ↔ R2", "R1 ← R1 ÷ 3"),
    /// with the rank and the pivot columns. Pivots are sought in the first <paramref name="pivotCols"/> columns only
    /// (the whole width when not given): an augmented matrix reduces on its left part.
    /// </summary>
    public (Mat Result, List<string> Steps, int Rank, List<int> Pivots) Rref(int? pivotCols = null)
    {
        var a = Copy();
        var steps = new List<string>();
        var pivots = new List<int>();
        var lead = 0;
        var width = pivotCols ?? Cols;
        for (var c = 0; c < width && lead < Rows; c++)
        {
            var p = -1;
            for (var r = lead; r < Rows; r++) if (!a[r, c].IsZero) { p = r; break; }
            if (p < 0) continue;
            if (p != lead) { a.SwapRows(p, lead); steps.Add($"R{lead + 1} ↔ R{p + 1}"); }
            var pivot = a[lead, c];
            if (pivot != Rational.One)
            {
                for (var j = 0; j < Cols; j++) a[lead, j] /= pivot;
                steps.Add(pivot.IsInteger ? $"R{lead + 1} ← R{lead + 1} ÷ {pivot.Text()}" : $"R{lead + 1} ← R{lead + 1} × {(Rational.One / pivot).Text()}");
            }
            for (var r = 0; r < Rows; r++)
            {
                if (r == lead || a[r, c].IsZero) continue;
                var f = a[r, c];
                for (var j = 0; j < Cols; j++) a[r, j] -= f * a[lead, j];
                steps.Add($"R{r + 1} ← R{r + 1} {(f.Sign < 0 ? "+" : "−")} {Coefficient(f.Abs())}R{lead + 1}");
            }
            pivots.Add(c);
            lead++;
        }
        return (a, steps, pivots.Count, pivots);
    }

    /// <summary>A step's multiplier: none for 1, "4" or "(3/2)".</summary>
    private static string Coefficient(Rational f) => f == Rational.One ? "" : f.IsInteger ? f.Text() : $"({f.Text()})";

    /// <summary>A·x = b for b a column (B's first column): the one solution, or why there is none or not just one.</summary>
    public (Mat? X, string? Why) Solve(Mat b)
    {
        if (b.Rows != Rows) throw new MatrixError($"A has {Rows} rows and B has {b.Rows}: for A·x = B, B needs a value for each row of A.");
        var aug = new Mat(Rows, Cols + 1);
        for (var i = 0; i < Rows; i++)
        {
            for (var j = 0; j < Cols; j++) aug[i, j] = v[i, j];
            aug[i, Cols] = b[i, 0];
        }
        var (r, _, rank, pivots) = aug.Rref(Cols);
        // (a row that reads 0 = c with c not 0: no solution)
        for (var i = rank; i < Rows; i++) if (!r[i, Cols].IsZero) return (null, "No solution: the equations contradict each other.");
        if (rank < Cols) return (null, $"Infinitely many solutions: the system has {Cols - rank} free {(Cols - rank == 1 ? "variable" : "variables")}.");
        var x = new Mat(Cols, 1);
        for (var k = 0; k < pivots.Count; k++) x[pivots[k], 0] = r[k, Cols];
        return (x, null);
    }

    /// <summary>Aⁿ for a whole n (a negative n: the inverse's power), by squaring.</summary>
    public Mat Power(int n)
    {
        if (!IsSquare) throw new MatrixError("A power needs a square matrix.");
        if (n < 0)
        {
            var inv = Inverse() ?? throw new MatrixError("A is singular: it has no inverse, so no negative power.");
            return inv.Power(-n);
        }
        var result = Identity(Rows);
        var b = Copy();
        while (n > 0)
        {
            if ((n & 1) == 1) result = result.Multiply(b);
            n >>= 1;
            if (n > 0) b = b.Multiply(b);
        }
        return result;
    }

    /// <summary>The eigenvalues (real and complex), largest real part first.</summary>
    public Complex[] Eigenvalues()
    {
        if (!IsSquare) throw new MatrixError("Eigenvalues need a square matrix.");
        return Eigen.Of(ToDoubles());
    }

    // ---- as words ----

    /// <summary>"1 2; 3 4": rows separated by semicolons, cells by spaces (fractions as 3/4).</summary>
    public string Ascii()
    {
        var b = new StringBuilder();
        for (var i = 0; i < Rows; i++)
        {
            if (i > 0) b.Append("; ");
            for (var j = 0; j < Cols; j++) { if (j > 0) b.Append(' '); b.Append(v[i, j].Ascii()); }
        }
        return b.ToString();
    }

    /// <summary>Rows on lines, cells apart by tabs (a spreadsheet takes it), as fractions or decimals.</summary>
    public string Tabbed(bool fractions)
    {
        var b = new StringBuilder();
        for (var i = 0; i < Rows; i++)
        {
            if (i > 0) b.Append('\n');
            for (var j = 0; j < Cols; j++) { if (j > 0) b.Append('\t'); b.Append(fractions ? v[i, j].Ascii() : StatsMath.Ascii(v[i, j].ToDouble())); }
        }
        return b.ToString();
    }
}

/// <summary>Why an operation cannot be done, in plain words.</summary>
internal sealed class MatrixError : Exception
{
    public MatrixError(string message) : base(message) { }
}

/// <summary>A matrix as typed or pasted, read.</summary>
internal static class MatrixParse
{
    /// <summary>
    /// Rows separated by semicolons or line breaks, cells by spaces, commas or tabs ("1 2; 3 4", a block pasted from a
    /// spreadsheet). <paramref name="exact"/>: every cell was an integer or a fraction, so answers can be shown exact.
    /// </summary>
    public static Mat? Parse(string? text, out bool exact, out string? error)
    {
        exact = true;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        var rows = new List<List<Rational>>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = new List<Rational>();
            foreach (var cell in line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var r = Rational.Parse(cell, out var cellExact);
                if (r == null) { error = $"Can't read “{cell}” — a cell is a number, a fraction like 3/4 or a decimal."; return null; }
                if (!cellExact) exact = false;
                cells.Add(r.Value);
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return null;
        var cols = rows[0].Count;
        if (rows.Any(r => r.Count != cols)) { error = "Every row needs the same number of cells."; return null; }
        var m = new Mat(rows.Count, cols);
        for (var i = 0; i < rows.Count; i++) for (var j = 0; j < cols; j++) m[i, j] = rows[i][j];
        return m;
    }
}

/// <summary>Eigenvalues of a real matrix: reduced to upper Hessenberg form by elimination with pivoting, then the
/// QR iteration with shifts (Numerical Recipes' elmhes and hqr; 1-based inside, as they are written).</summary>
internal static class Eigen
{
    public static Complex[] Of(double[,] m)
    {
        var n = m.GetLength(0);
        if (n == 0) return Array.Empty<Complex>();
        var a = new double[n + 1, n + 1];
        var scale = 0.0;
        for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) { a[i + 1, j + 1] = m[i, j]; scale = Math.Max(scale, Math.Abs(m[i, j])); }
        Hessenberg(a, n);
        for (var i = 1; i <= n; i++) for (var j = 1; j < i - 1; j++) a[i, j] = 0;
        var wr = new double[n + 1];
        var wi = new double[n + 1];
        Hqr(a, n, wr, wi);
        var eps = (scale == 0 ? 1 : scale) * 1e-12;
        var result = new Complex[n];
        for (var i = 1; i <= n; i++)
        {
            double re = Math.Abs(wr[i]) < eps ? 0 : wr[i], im = Math.Abs(wi[i]) < eps ? 0 : wi[i];
            result[i - 1] = new Complex(re, im);
        }
        return result.OrderByDescending(c => c.Real).ThenByDescending(c => c.Imaginary).ToArray();
    }

    private static void Hessenberg(double[,] a, int n)
    {
        for (var m = 2; m < n; m++)
        {
            var x = 0.0;
            var i = m;
            for (var j = m; j <= n; j++)
                if (Math.Abs(a[j, m - 1]) > Math.Abs(x)) { x = a[j, m - 1]; i = j; }
            if (i != m)
            {
                for (var j = m - 1; j <= n; j++) (a[i, j], a[m, j]) = (a[m, j], a[i, j]);
                for (var j = 1; j <= n; j++) (a[j, i], a[j, m]) = (a[j, m], a[j, i]);
            }
            if (x != 0)
                for (i = m + 1; i <= n; i++)
                {
                    var y = a[i, m - 1];
                    if (y == 0) continue;
                    y /= x;
                    a[i, m - 1] = y;
                    for (var j = m; j <= n; j++) a[i, j] -= y * a[m, j];
                    for (var j = 1; j <= n; j++) a[j, m] += y * a[j, i];
                }
        }
    }

    private static double Sign(double a, double b) => b >= 0 ? Math.Abs(a) : -Math.Abs(a);

    private static void Hqr(double[,] a, int n, double[] wr, double[] wi)
    {
        var anorm = 0.0;
        for (var i = 1; i <= n; i++) for (var j = Math.Max(i - 1, 1); j <= n; j++) anorm += Math.Abs(a[i, j]);
        var nn = n;
        var t = 0.0;
        while (nn >= 1)
        {
            var its = 0;
            int l;
            do
            {
                for (l = nn; l >= 2; l--)
                {
                    var s = Math.Abs(a[l - 1, l - 1]) + Math.Abs(a[l, l]);
                    if (s == 0) s = anorm;
                    if (Math.Abs(a[l, l - 1]) + s == s) { a[l, l - 1] = 0; break; }
                }
                var x = a[nn, nn];
                if (l == nn)
                {
                    wr[nn] = x + t;
                    wi[nn--] = 0;
                }
                else
                {
                    var y = a[nn - 1, nn - 1];
                    var w = a[nn, nn - 1] * a[nn - 1, nn];
                    if (l == nn - 1)
                    {
                        var p = 0.5 * (y - x);
                        var q = p * p + w;
                        var z = Math.Sqrt(Math.Abs(q));
                        x += t;
                        if (q >= 0)
                        {
                            z = p + Sign(z, p);
                            wr[nn - 1] = wr[nn] = x + z;
                            if (z != 0) wr[nn] = x - w / z;
                            wi[nn - 1] = wi[nn] = 0;
                        }
                        else
                        {
                            wr[nn - 1] = wr[nn] = x + p;
                            wi[nn - 1] = -(wi[nn] = z);
                        }
                        nn -= 2;
                    }
                    else
                    {
                        if (its == 60) throw new MatrixError("The eigenvalues did not settle: try the matrix in decimals with fewer figures.");
                        if (its == 10 || its == 20)
                        {
                            t += x;
                            for (var i = 1; i <= nn; i++) a[i, i] -= x;
                            var s = Math.Abs(a[nn, nn - 1]) + Math.Abs(a[nn - 1, nn - 2]);
                            y = x = 0.75 * s;
                            w = -0.4375 * s * s;
                        }
                        ++its;
                        int m;
                        double pp = 0, qq = 0, rr = 0;
                        for (m = nn - 2; m >= l; m--)
                        {
                            var z = a[m, m];
                            var r = x - z;
                            var s = y - z;
                            pp = (r * s - w) / a[m + 1, m] + a[m, m + 1];
                            qq = a[m + 1, m + 1] - z - r - s;
                            rr = a[m + 2, m + 1];
                            s = Math.Abs(pp) + Math.Abs(qq) + Math.Abs(rr);
                            pp /= s; qq /= s; rr /= s;
                            if (m == l) break;
                            var u = Math.Abs(a[m, m - 1]) * (Math.Abs(qq) + Math.Abs(rr));
                            var vv = Math.Abs(pp) * (Math.Abs(a[m - 1, m - 1]) + Math.Abs(z) + Math.Abs(a[m + 1, m + 1]));
                            if (u + vv == vv) break;
                        }
                        for (var i = m + 2; i <= nn; i++)
                        {
                            a[i, i - 2] = 0;
                            if (i != m + 2) a[i, i - 3] = 0;
                        }
                        for (var k = m; k <= nn - 1; k++)
                        {
                            double p = pp, q = qq, r = rr;
                            if (k != m)
                            {
                                p = a[k, k - 1];
                                q = a[k + 1, k - 1];
                                r = 0;
                                if (k != nn - 1) r = a[k + 2, k - 1];
                                if ((x = Math.Abs(p) + Math.Abs(q) + Math.Abs(r)) != 0) { p /= x; q /= x; r /= x; }
                            }
                            var s = Sign(Math.Sqrt(p * p + q * q + r * r), p);
                            if (s != 0)
                            {
                                if (k == m)
                                {
                                    if (l != m) a[k, k - 1] = -a[k, k - 1];
                                }
                                else a[k, k - 1] = -s * x;
                                p += s;
                                x = p / s;
                                y = q / s;
                                var z = r / s;
                                q /= p;
                                r /= p;
                                for (var j = k; j <= nn; j++)
                                {
                                    p = a[k, j] + q * a[k + 1, j];
                                    if (k != nn - 1)
                                    {
                                        p += r * a[k + 2, j];
                                        a[k + 2, j] -= p * z;
                                    }
                                    a[k + 1, j] -= p * y;
                                    a[k, j] -= p * x;
                                }
                                var mmin = nn < k + 3 ? nn : k + 3;
                                for (var i = l; i <= mmin; i++)
                                {
                                    p = x * a[i, k] + y * a[i, k + 1];
                                    if (k != nn - 1)
                                    {
                                        p += z * a[i, k + 2];
                                        a[i, k + 2] -= p * r;
                                    }
                                    a[i, k + 1] -= p * q;
                                    a[i, k] -= p;
                                }
                            }
                            pp = p; qq = q; rr = r;
                        }
                    }
                }
            } while (l < nn - 1);
        }
    }

    /// <summary>An eigenvalue as words: "3", "−2.5", "1 ± 2i" is two values, written "1 + 2i" and "1 − 2i".</summary>
    public static string Text(Complex c)
    {
        if (c.Imaginary == 0) return StatsMath.Sig(c.Real);
        var im = StatsMath.Sig(Math.Abs(c.Imaginary));
        if (im == "1") im = "";
        if (c.Real == 0) return $"{(c.Imaginary < 0 ? "−" : "")}{im}i";
        return $"{StatsMath.Sig(c.Real)} {(c.Imaginary < 0 ? "−" : "+")} {im}i";
    }
}
