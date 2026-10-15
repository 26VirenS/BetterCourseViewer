namespace Simpl.Tools;

// Molar mass from a formula (the Equation Balancer's mass check and Molar Mass & Stoichiometry's first tab): each
// element's count, atomic mass and subtotal, and its share of the whole (percent composition), from the elements'
// table (ChemElements, IUPAC 2021 abridged weights as the apps' periodic data has them). Plain .NET, no WPF.

/// <summary>One element's line of a molar mass: how many, each one's mass, their mass together and its share.</summary>
public sealed record MolarMassRow(string Symbol, string Name, int Count, double AtomicMass, double Subtotal, double Percent);

/// <summary>A formula's molar mass and its breakdown by element.</summary>
public sealed class MolarMassResult
{
    public required ChemFormula Formula { get; init; }
    public required double Total { get; init; }
    public required IReadOnlyList<MolarMassRow> Rows { get; init; }
}

public static class ChemMolarMass
{
    /// <summary>The molar mass of a formula typed, or null with what could not be read.</summary>
    public static MolarMassResult? Of(string? typed, out string? bad)
    {
        var f = ChemFormula.Parse(typed, out bad);
        return f == null ? null : Of(f);
    }

    public static MolarMassResult Of(ChemFormula f)
    {
        var total = f.MolarMass;
        var rows = f.Atoms.Select(a =>
        {
            var e = ChemElements.Of(a.Symbol);
            var mass = e?.Mass ?? 0;
            var sub = mass * a.Count;
            return new MolarMassRow(a.Symbol, e?.Name ?? a.Symbol, a.Count, mass, sub, total > 0 ? sub / total * 100 : 0);
        }).ToList();
        return new MolarMassResult { Formula = f, Total = total, Rows = rows };
    }

    /// <summary>The molar mass alone (g/mol), or null for a formula that cannot be read.</summary>
    public static double? Mass(string? typed) => ChemFormula.Parse(typed)?.MolarMass;
}
