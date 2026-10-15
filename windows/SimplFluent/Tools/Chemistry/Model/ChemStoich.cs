using System.Globalization;

namespace Simpl.Tools;

// Molar Mass & Stoichiometry's sums beyond the molar mass itself (ChemMolarMass): an amount converted between grams,
// moles, particles and litres of gas at STP; a balanced reaction with what was given of each reactant — the limiting
// reagent, every product's theoretical yield, what is left of the others, and the percent yield of an actual one; and an
// empirical formula from mass percents (or grams), with the molecular formula when the molar mass is known. Plain .NET,
// no WPF, so the unit tests check it on any machine.

/// <summary>How an amount was given.</summary>
public enum AmountKind { Grams, Moles, Particles, LitresStp }

/// <summary>An amount of a substance every way: grams, moles, molecules or formula units, and litres of gas at STP.</summary>
public sealed record ConvertResult(double MolarMass, double Grams, double Moles, double Particles, double Litres);

/// <summary>What was given of a reactant: the number and whether it is grams (else moles); null, nothing.</summary>
public readonly record struct GivenAmount(double? Value, bool Grams);

/// <summary>A species of a reaction as the stoichiometry works it out.</summary>
public sealed class ReactionRow
{
    public required ChemFormula Formula { get; init; }
    public required long Coefficient { get; init; }
    public required bool Reactant { get; init; }
    /// <summary>What was given, as moles and as grams (null: nothing given; a reactant then assumed in excess).</summary>
    public double? GivenMoles { get; init; }
    public double? GivenGrams { get; init; }
    /// <summary>A reactant: the moles the reaction uses up; a product: the moles made.</summary>
    public double UsedOrMadeMoles { get; init; }
    public double UsedOrMadeGrams { get; init; }
    /// <summary>A reactant with a given amount: what is left over (0 for the limiting one).</summary>
    public double? LeftMoles { get; init; }
    public double? LeftGrams { get; init; }
    public bool Limiting { get; init; }
}

/// <summary>A reaction worked out from a balanced equation and what was given.</summary>
public sealed class ReactionResult
{
    public required IReadOnlyList<ReactionRow> Rows { get; init; }
    public ChemFormula? Limiting { get; init; }
    /// <summary>How many times the equation as written runs (mol).</summary>
    public double Extent { get; init; }
    public double? PercentYield { get; init; }
    /// <summary>What is wanting (nothing given), or null.</summary>
    public string? Message { get; init; }

    public IEnumerable<ReactionRow> Reactants => Rows.Where(r => r.Reactant);
    public IEnumerable<ReactionRow> Products => Rows.Where(r => !r.Reactant);
}

/// <summary>An element's line of an empirical formula: what was given, its moles, its ratio to the smallest, and the whole number it became.</summary>
public sealed record EmpiricalRow(string Symbol, double Amount, double Moles, double Ratio, int Whole);

/// <summary>An empirical formula and, when the molar mass is known, the molecular one.</summary>
public sealed class EmpiricalResult
{
    public required IReadOnlyList<EmpiricalRow> Rows { get; init; }
    /// <summary>What the ratios were multiplied by to come out whole (1 to 6).</summary>
    public required int Multiplier { get; init; }
    public required string Formula { get; init; }
    public required double FormulaMass { get; init; }
    /// <summary>True when no multiplier up to 6 brought every ratio within 0.08 of a whole number: rounded as best it could be.</summary>
    public required bool Approximate { get; init; }
    public string? Molecular { get; init; }
    public int? MolecularMultiplier { get; init; }
}

public static class ChemStoich
{
    public const double Avogadro = 6.02214076e23;
    /// <summary>The molar volume of an ideal gas at STP as taught (22.4 L/mol).</summary>
    public const double StpLitresPerMole = 22.4;

    /// <summary>An amount of a formula every way.</summary>
    public static ConvertResult Convert(ChemFormula f, double amount, AmountKind kind)
    {
        var m = f.MolarMass;
        var moles = kind switch
        {
            AmountKind.Grams => m > 0 ? amount / m : 0,
            AmountKind.Particles => amount / Avogadro,
            AmountKind.LitresStp => amount / StpLitresPerMole,
            _ => amount,
        };
        return new ConvertResult(m, moles * m, moles, moles * Avogadro, moles * StpLitresPerMole);
    }

    /// <summary>
    /// A balanced reaction with what was given of its reactants (one entry per reactant, in order): the limiting reagent
    /// (the smallest moles given over coefficient), how far the reaction runs, every product's yield, what is left of
    /// the other reactants, and the percent yield of <paramref name="actualProduct"/> from <paramref name="actualGrams"/>.
    /// </summary>
    public static ReactionResult React(BalanceResult balanced, IReadOnlyList<GivenAmount> given, int? actualProduct = null, double? actualGrams = null)
    {
        var rows = new List<ReactionRow>();
        var n = balanced.Reactants.Count;
        double? extent = null;
        var limiting = -1;
        for (var i = 0; i < n; i++)
        {
            var g = i < given.Count ? given[i] : default;
            if (g.Value is not { } v || v < 0) continue;
            var f = balanced.Reactants[i];
            var moles = g.Grams ? (f.MolarMass > 0 ? v / f.MolarMass : 0) : v;
            var runs = moles / balanced.Coefficients[i];
            if (extent == null || runs < extent) { extent = runs; limiting = i; }
        }
        if (extent == null)
            return new ReactionResult { Rows = Plain(balanced), Extent = 0, Message = "Give the amount of at least one reactant." };
        var e = extent.Value;
        var species = balanced.Species.ToList();
        for (var i = 0; i < species.Count; i++)
        {
            var f = species[i];
            var c = balanced.Coefficients[i];
            var reactant = i < n;
            var change = e * c;
            double? givenMoles = null, givenGrams = null, leftMoles = null, leftGrams = null;
            if (reactant && i < given.Count && given[i].Value is { } v && v >= 0)
            {
                givenMoles = given[i].Grams ? (f.MolarMass > 0 ? v / f.MolarMass : 0) : v;
                givenGrams = givenMoles * f.MolarMass;
                leftMoles = Math.Max(0, givenMoles.Value - change);
                leftGrams = leftMoles * f.MolarMass;
            }
            rows.Add(new ReactionRow
            {
                Formula = f, Coefficient = c, Reactant = reactant, GivenMoles = givenMoles, GivenGrams = givenGrams,
                UsedOrMadeMoles = change, UsedOrMadeGrams = change * f.MolarMass, LeftMoles = leftMoles, LeftGrams = leftGrams, Limiting = i == limiting,
            });
        }
        double? percent = null;
        if (actualProduct is { } p && p >= 0 && p < balanced.Products.Count && actualGrams is { } a && a >= 0)
        {
            var theoretical = rows[n + p].UsedOrMadeGrams;
            if (theoretical > 0) percent = a / theoretical * 100;
        }
        return new ReactionResult { Rows = rows, Limiting = balanced.Reactants[limiting], Extent = e, PercentYield = percent };
    }

    private static List<ReactionRow> Plain(BalanceResult balanced) =>
        balanced.Species.Select((f, i) => new ReactionRow { Formula = f, Coefficient = balanced.Coefficients[i], Reactant = i < balanced.Reactants.Count }).ToList();

    /// <summary>
    /// An empirical formula from each element's mass percent or grams (the same sums): moles of each, divided by the
    /// smallest, multiplied by 1 to 6 until every ratio is within 0.08 of a whole number; and with a molar mass, the
    /// molecular formula. Null with what is wrong in <paramref name="bad"/>: an unknown element, a missing amount.
    /// </summary>
    public static EmpiricalResult? Empirical(IReadOnlyList<(string Symbol, double Amount)> parts, double? molarMass, out string? bad)
    {
        bad = null;
        var moles = new List<(string Symbol, double Amount, double Moles)>();
        foreach (var (symbol, amount) in parts)
        {
            var s = symbol.Trim();
            if (s.Length == 0) continue;
            var e = ChemElements.Of(Capitalised(s));
            if (e == null) { bad = $"“{s}” is not an element"; return null; }
            if (!(amount > 0)) { bad = $"{e.Name} needs an amount above zero"; return null; }
            moles.Add((e.Symbol, amount, amount / e.Mass));
        }
        if (moles.Count == 0) { bad = "Give at least one element and its amount."; return null; }
        var smallest = moles.Min(m => m.Moles);
        var ratios = moles.Select(m => m.Moles / smallest).ToList();
        var multiplier = 0;
        for (var k = 1; k <= 6 && multiplier == 0; k++)
            if (ratios.All(r => Math.Abs(r * k - Math.Round(r * k)) <= 0.08)) multiplier = k;
        var approximate = multiplier == 0;
        if (approximate) multiplier = 1;
        var rows = moles.Select((m, i) => new EmpiricalRow(m.Symbol, m.Amount, m.Moles, ratios[i], Math.Max(1, (int)Math.Round(ratios[i] * multiplier)))).ToList();
        var formula = Written(rows.Select(r => (r.Symbol, r.Whole)));
        var mass = rows.Sum(r => (ChemElements.Of(r.Symbol)?.Mass ?? 0) * r.Whole);
        string? molecular = null;
        int? times = null;
        if (molarMass is { } mm && mm > 0 && mass > 0)
        {
            var t = Math.Max(1, (int)Math.Round(mm / mass));
            times = t;
            molecular = Written(rows.Select(r => (r.Symbol, r.Whole * t)));
        }
        return new EmpiricalResult { Rows = rows, Multiplier = multiplier, Formula = formula, FormulaMass = mass, Approximate = approximate, Molecular = molecular, MolecularMultiplier = times };
    }

    private static string Written(IEnumerable<(string Symbol, int Count)> parts) =>
        string.Concat(parts.Select(p => p.Symbol + (p.Count == 1 ? "" : p.Count.ToString(CultureInfo.InvariantCulture))));

    /// <summary>"cl" and "CL" as "Cl": a symbol typed any way.</summary>
    public static string Capitalised(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
