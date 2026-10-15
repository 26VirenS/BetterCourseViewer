using System.Globalization;
using System.Text;

namespace Simpl.Tools;

// The Equation Balancer's sums: an equation read into its reactants and products, the matrix of each element's (and the
// charge's) count in every species, its null space found by Gaussian elimination over exact fractions, and the one
// solution scaled to the smallest whole numbers — or why there is none (an element on one side only) or more than one
// (two reactions written as one). Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>How balancing went.</summary>
public enum BalanceOutcome
{
    /// <summary>Nothing typed yet.</summary>
    Empty,
    Balanced,
    /// <summary>A formula could not be read.</summary>
    Unreadable,
    /// <summary>No arrow, or a side with nothing on it.</summary>
    Malformed,
    /// <summary>No whole-number solution.</summary>
    Impossible,
    /// <summary>More than one way: the null space has more than one dimension.</summary>
    Ambiguous,
    /// <summary>The coefficients would overflow 64-bit integers.</summary>
    TooLarge,
}

/// <summary>An element's (or the charge's) count on each side, coefficients applied.</summary>
public sealed record BalanceRow(string Name, long Left, long Right)
{
    public bool Equal => Left == Right;
}

/// <summary>What the balancer found: the species each side, the coefficients, the count table and the message.</summary>
public sealed class BalanceResult
{
    public BalanceOutcome Outcome { get; init; }
    /// <summary>What to say under the field (empty when balanced or nothing is typed).</summary>
    public string Message { get; init; } = "";
    public IReadOnlyList<ChemFormula> Reactants { get; init; } = Array.Empty<ChemFormula>();
    public IReadOnlyList<ChemFormula> Products { get; init; } = Array.Empty<ChemFormula>();
    /// <summary>The coefficients in order of appearance, reactants then products (all 1 while unbalanced).</summary>
    public IReadOnlyList<long> Coefficients { get; init; } = Array.Empty<long>();
    /// <summary>Each element's count on each side (and the charge's, last, when any species is charged).</summary>
    public IReadOnlyList<BalanceRow> Rows { get; init; } = Array.Empty<BalanceRow>();
    /// <summary>Elements that appear on one side only (why it cannot balance).</summary>
    public IReadOnlyList<string> OneSided { get; init; } = Array.Empty<string>();
    /// <summary>The token that could not be read.</summary>
    public string? BadToken { get; init; }

    public bool Ok => Outcome == BalanceOutcome.Balanced;
    public IEnumerable<ChemFormula> Species => Reactants.Concat(Products);

    /// <summary>Total mass of each side (g/mol of the equation as written), coefficients applied.</summary>
    public double LeftMass => Reactants.Select((f, i) => f.MolarMass * Coefficients[i]).Sum();
    public double RightMass => Products.Select((f, i) => f.MolarMass * Coefficients[Reactants.Count + i]).Sum();

    /// <summary>The equation as plain text: "2H2 + O2 → 2H2O" (a coefficient of 1 left out).</summary>
    public string PlainText => Side(Reactants, 0) + " → " + Side(Products, Reactants.Count);

    /// <summary>The equation with Unicode subscripts: "2H₂ + O₂ → 2H₂O".</summary>
    public string PrettyText => Side(Reactants, 0, pretty: true) + " → " + Side(Products, Reactants.Count, pretty: true);

    private string Side(IReadOnlyList<ChemFormula> side, int offset, bool pretty = false) =>
        string.Join(" + ", side.Select((f, i) =>
        {
            var c = Coefficients.Count > offset + i ? Coefficients[offset + i] : 1;
            return (c == 1 ? "" : c.ToString(CultureInfo.InvariantCulture)) + (pretty ? f.Pretty : f.Text);
        }));
}

/// <summary>The Equation Balancer's reader and solver.</summary>
public static class ChemBalancer
{
    private static readonly string[] Arrows = { "<->", "<=>", "⇌", "⇄", "⟶", "→", "->", "=" };

    /// <summary>The equation balanced, or why it cannot be.</summary>
    public static BalanceResult Balance(string? typed)
    {
        var s = (typed ?? "").Trim();
        if (s.Length == 0) return new BalanceResult { Outcome = BalanceOutcome.Empty };
        // (one arrow, whichever way it is written)
        string? left = null, right = null;
        foreach (var arrow in Arrows)
        {
            var at = s.IndexOf(arrow, StringComparison.Ordinal);
            if (at < 0) continue;
            left = s[..at];
            right = s[(at + arrow.Length)..];
            break;
        }
        if (left == null || right == null)
            return new BalanceResult { Outcome = BalanceOutcome.Malformed, Message = "Write the reaction with an arrow: reactants → products." };
        if (right.IndexOfAny(new[] { '→', '⟶', '=' }) >= 0 || right.Contains("->", StringComparison.Ordinal))
            return new BalanceResult { Outcome = BalanceOutcome.Malformed, Message = "One arrow only: reactants → products." };
        var reactants = new List<ChemFormula>();
        var products = new List<ChemFormula>();
        foreach (var (side, into) in new[] { (left, reactants), (right, products) })
            foreach (var token in Species(side))
            {
                var f = ChemFormula.Parse(token, out var bad);
                if (f == null)
                    return new BalanceResult
                    {
                        Outcome = BalanceOutcome.Unreadable, BadToken = string.IsNullOrEmpty(bad) ? token : bad,
                        Message = $"Can’t read “{(string.IsNullOrEmpty(bad) ? token : bad)}” — is it a real formula?",
                    };
                into.Add(f);
            }
        if (reactants.Count == 0 || products.Count == 0)
            return new BalanceResult { Outcome = BalanceOutcome.Malformed, Message = "Both sides need at least one formula.", Reactants = reactants, Products = products };
        try { return Solve(reactants, products); }
        catch (OverflowException)
        {
            return new BalanceResult
            {
                Outcome = BalanceOutcome.TooLarge, Reactants = reactants, Products = products, Coefficients = Ones(reactants.Count + products.Count),
                Rows = Rows(reactants, products, Ones(reactants.Count + products.Count)), Message = "The numbers got too large to balance — check the formulas.",
            };
        }
    }

    /// <summary>A side's formulas: split at a + between species ("H2 + O2", "H2+O2"), never at an ion's own + ("Ag+ + Cu",
    /// "H+ + OH-"); a number on its own is the coefficient of what follows (ignored: the balancer finds its own).</summary>
    public static IReadOnlyList<string> Species(string side)
    {
        var list = new List<string>();
        var current = new StringBuilder();
        void Push()
        {
            var t = current.ToString().Trim();
            if (t.Length > 0) list.Add(t);
            current.Clear();
        }
        foreach (var token in side.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "+") { Push(); continue; }
            if (token.All(char.IsDigit)) { Push(); current.Append(token); continue; }
            var piece = new StringBuilder();
            for (var i = 0; i < token.Length; i++)
            {
                var c = token[i];
                // (a + followed by a letter, a bracket or a digit joins two species; a + at the end or before another + is a charge)
                var joins = c == '+' && i + 1 < token.Length && (char.IsLetter(token[i + 1]) || token[i + 1] is '(' or '[' || char.IsDigit(token[i + 1]))
                    && i > 0 && token[i - 1] != '^' && token[i - 1] != '{';
                if (joins) { current.Append(piece); Push(); piece.Clear(); }
                else piece.Append(c);
            }
            current.Append(piece);
            Push();
        }
        Push();
        return list;
    }

    private static long[] Ones(int n) => Enumerable.Repeat(1L, n).ToArray();

    private static BalanceResult Solve(List<ChemFormula> reactants, List<ChemFormula> products)
    {
        var species = reactants.Concat(products).ToList();
        var n = species.Count;
        var elements = new List<string>();
        foreach (var f in species) foreach (var (symbol, _) in f.Atoms) if (!elements.Contains(symbol)) elements.Add(symbol);
        var charged = species.Any(f => f.Charge != 0);
        var rows = elements.Count + (charged ? 1 : 0);
        var m = new ChemFraction[rows, n];
        for (var c = 0; c < n; c++)
        {
            var sign = c < reactants.Count ? 1 : -1;
            for (var r = 0; r < elements.Count; r++) m[r, c] = ChemFraction.Of(sign * species[c].Count(elements[r]));
            if (charged) m[elements.Count, c] = ChemFraction.Of(sign * species[c].Charge);
        }
        // (an element on one side only can never balance; said by name)
        var oneSided = new List<string>();
        for (var r = 0; r < elements.Count; r++)
        {
            var onLeft = reactants.Any(f => f.Count(elements[r]) > 0);
            var onRight = products.Any(f => f.Count(elements[r]) > 0);
            if (onLeft != onRight) oneSided.Add(elements[r]);
        }
        var pivots = Reduce(m, rows, n);
        var free = Enumerable.Range(0, n).Where(c => !pivots.Contains(c)).ToList();
        var ones = Ones(n);
        if (free.Count == 0 || oneSided.Count > 0)
            return new BalanceResult
            {
                Outcome = BalanceOutcome.Impossible, Reactants = reactants, Products = products, Coefficients = ones, Rows = Rows(reactants, products, ones), OneSided = oneSided,
                Message = oneSided.Count > 0
                    ? $"This can’t be balanced — {Named(oneSided)} {(oneSided.Count == 1 ? "is" : "are")} on one side only. Check the formulas."
                    : "This can’t be balanced — check the formulas (an element on only one side?)",
            };
        if (free.Count > 1)
            return new BalanceResult
            {
                Outcome = BalanceOutcome.Ambiguous, Reactants = reactants, Products = products, Coefficients = ones, Rows = Rows(reactants, products, ones),
                Message = "More than one way to balance this — split it into separate equations.",
            };
        // (the free column set to 1, each pivot's column follows from its reduced row)
        var x = new ChemFraction[n];
        x[free[0]] = ChemFraction.One;
        for (var r = 0; r < pivots.Count; r++) x[pivots[r]] = -m[r, free[0]];
        long lcm = 1;
        foreach (var v in x) lcm = ChemNum.Lcm(lcm, v.Den);
        var coefficients = x.Select(v => checked(v.Num * (lcm / v.Den))).ToArray();
        var gcd = coefficients.Aggregate(0L, ChemNum.Gcd);
        if (gcd > 1) for (var i = 0; i < n; i++) coefficients[i] /= gcd;
        if (coefficients.All(v => v < 0)) for (var i = 0; i < n; i++) coefficients[i] = -coefficients[i];
        if (coefficients.Any(v => v <= 0))
            return new BalanceResult
            {
                Outcome = BalanceOutcome.Impossible, Reactants = reactants, Products = products, Coefficients = ones, Rows = Rows(reactants, products, ones),
                Message = "This can’t be balanced as written — a species may be on the wrong side. Check the formulas.",
            };
        return new BalanceResult
        {
            Outcome = BalanceOutcome.Balanced, Reactants = reactants, Products = products, Coefficients = coefficients, Rows = Rows(reactants, products, coefficients),
        };
    }

    /// <summary>Gauss–Jordan elimination to reduced row echelon form over exact fractions: the pivot column of each
    /// reduced row, in row order.</summary>
    private static List<int> Reduce(ChemFraction[,] m, int rows, int cols)
    {
        var pivots = new List<int>();
        var row = 0;
        for (var col = 0; col < cols && row < rows; col++)
        {
            var p = -1;
            for (var r = row; r < rows; r++) if (!m[r, col].IsZero) { p = r; break; }
            if (p < 0) continue;
            if (p != row) for (var c = 0; c < cols; c++) (m[row, c], m[p, c]) = (m[p, c], m[row, c]);
            var inv = ChemFraction.One / m[row, col];
            for (var c = 0; c < cols; c++) m[row, c] = m[row, c] * inv;
            for (var r = 0; r < rows; r++)
            {
                if (r == row || m[r, col].IsZero) continue;
                var factor = m[r, col];
                for (var c = 0; c < cols; c++) m[r, c] = m[r, c] - factor * m[row, c];
            }
            pivots.Add(col);
            row++;
        }
        return pivots;
    }

    private static List<BalanceRow> Rows(List<ChemFormula> reactants, List<ChemFormula> products, long[] coefficients)
    {
        var elements = new List<string>();
        foreach (var f in reactants.Concat(products)) foreach (var (symbol, _) in f.Atoms) if (!elements.Contains(symbol)) elements.Add(symbol);
        var rows = new List<BalanceRow>();
        foreach (var e in elements)
        {
            var left = reactants.Select((f, i) => checked(f.Count(e) * coefficients[i])).Sum();
            var right = products.Select((f, i) => checked(f.Count(e) * coefficients[reactants.Count + i])).Sum();
            rows.Add(new BalanceRow(e, left, right));
        }
        if (reactants.Concat(products).Any(f => f.Charge != 0))
        {
            var left = reactants.Select((f, i) => checked(f.Charge * coefficients[i])).Sum();
            var right = products.Select((f, i) => checked(f.Charge * coefficients[reactants.Count + i])).Sum();
            rows.Add(new BalanceRow("Charge", left, right));
        }
        return rows;
    }

    private static string Named(IReadOnlyList<string> symbols)
    {
        var names = symbols.Select(s => ChemElements.Of(s)?.Name is { } n ? $"{n} ({s})" : s).ToList();
        return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }
}
