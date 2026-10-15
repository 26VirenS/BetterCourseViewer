namespace Simpl.Tools;

// Solutions & pH's sums: molarity from any two of moles (or grams of a formula), volume and molarity, with mass percent
// and ppm; a dilution M1V1 = M2V2 with one blank; pH, pOH, [H+] and [OH-] from any one (Kw = 1.0e-14 at 25 °C, the only
// temperature here); the pH of a strong or weak acid or base from its concentration, the weak ones by the exact
// quadratic; a titration's equivalence volume, its pH at the points that matter and its whole curve by charge balance;
// and a buffer by Henderson–Hasselbalch. Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>pH, pOH, [H+] and [OH-] together.</summary>
public sealed record PhSet(double PH, double POH, double H, double OH);

/// <summary>Which of the four was given.</summary>
public enum PhGiven { PH, POH, H, OH }

/// <summary>What is in the solution.</summary>
public enum AcidBaseKind { StrongAcid, StrongBase, WeakAcid, WeakBase }

/// <summary>The pH of an acid or base from its concentration, and how much of a weak one ionised.</summary>
public sealed class AcidBasePh
{
    public required PhSet Set { get; init; }
    /// <summary>A weak acid or base: the part of it that ionised, in percent.</summary>
    public double? PercentIonised { get; init; }
    /// <summary>A weak acid's [H+] (or a weak base's [OH-]) from the quadratic.</summary>
    public double? Ionised { get; init; }
}

public static class ChemPh
{
    /// <summary>Water's ion product at 25 °C.</summary>
    public const double Kw = 1.0e-14;

    /// <summary>The other three from any one of pH, pOH, [H+] and [OH-]; null for a concentration at or below zero.</summary>
    public static PhSet? From(PhGiven given, double value)
    {
        double h;
        switch (given)
        {
            case PhGiven.PH: h = Math.Pow(10, -value); break;
            case PhGiven.POH: h = Kw / Math.Pow(10, -value); break;
            case PhGiven.H: if (value <= 0) return null; h = value; break;
            default: if (value <= 0) return null; h = Kw / value; break;
        }
        if (!double.IsFinite(h) || h <= 0) return null;
        return Of(h);
    }

    /// <summary>The set from [H+].</summary>
    public static PhSet Of(double h) => new(-Math.Log10(h), -Math.Log10(Kw / h), h, Kw / h);

    /// <summary>
    /// The pH of a solution from its concentration: a strong acid or base wholly ionised (water's own ions counted, so a
    /// very dilute acid never comes out basic); a weak one by the exact quadratic x² + Kx − KC = 0, with the percent
    /// ionised. Null for a concentration at or below zero, or a weak one without its K.
    /// </summary>
    public static AcidBasePh? OfSolution(AcidBaseKind kind, double concentration, double? k)
    {
        if (!(concentration > 0)) return null;
        var c = concentration;
        switch (kind)
        {
            case AcidBaseKind.StrongAcid:
                return new AcidBasePh { Set = Of((c + Math.Sqrt(c * c + 4 * Kw)) / 2) };
            case AcidBaseKind.StrongBase:
                return new AcidBasePh { Set = Of(Kw / ((c + Math.Sqrt(c * c + 4 * Kw)) / 2)) };
        }
        if (k is not { } ka || !(ka > 0)) return null;
        var x = (-ka + Math.Sqrt(ka * ka + 4 * ka * c)) / 2;
        var set = kind == AcidBaseKind.WeakAcid ? Of(x) : Of(Kw / x);
        return new AcidBasePh { Set = set, Ionised = x, PercentIonised = x / c * 100 };
    }

    /// <summary>Henderson–Hasselbalch: pH = pKa + log([A-]/[HA]); null when either is at or below zero.</summary>
    public static double? Buffer(double pKa, double baseConc, double acidConc) =>
        baseConc > 0 && acidConc > 0 ? pKa + Math.Log10(baseConc / acidConc) : null;
}

/// <summary>A molarity worked out: the three together, with what was solved, and the mass percent and ppm when the mass is known.</summary>
public sealed class MolarityResult
{
    public required double Moles { get; init; }
    public double? Grams { get; init; }
    public required double VolumeL { get; init; }
    public required double Molarity { get; init; }
    /// <summary>"amount", "volume" or "molarity".</summary>
    public required string Solved { get; init; }
    public double? MassPercent { get; init; }
    public double? Ppm { get; init; }
}

public static class ChemMolarity
{
    /// <summary>
    /// Any two of the amount (moles, or grams of a formula), the volume (L) and the molarity, the third solved; with
    /// grams, the mass percent and ppm of the solution from its density (g/mL). Null with what is wanting.
    /// </summary>
    public static MolarityResult? Solve(double? moles, double? grams, ChemFormula? formula, double? volumeL, double? molarity, double density, out string? message)
    {
        message = null;
        var m = formula?.MolarMass;
        if (grams != null && moles == null)
        {
            if (m is not > 0) { message = "Give the solute’s formula to use grams."; return null; }
            moles = grams / m;
        }
        var blanks = new[] { moles == null, volumeL == null, molarity == null }.Count(b => b);
        if (blanks == 0) { message = "Leave exactly one blank to solve it."; return null; }
        if (blanks > 1) { message = "Fill in all but one."; return null; }
        if (moles is <= 0 || volumeL is <= 0 || molarity is <= 0) { message = "Every value must be above zero."; return null; }
        string solved;
        if (moles == null) { moles = molarity * volumeL; solved = "amount"; }
        else if (volumeL == null) { volumeL = moles / molarity; solved = "volume"; }
        else { molarity = moles / volumeL; solved = "molarity"; }
        var g = grams ?? (m is > 0 ? moles * m : null);
        double? percent = null, ppm = null;
        if (g is { } gr && density > 0)
        {
            var solutionGrams = volumeL!.Value * 1000 * density;
            if (solutionGrams > 0) { percent = gr / solutionGrams * 100; ppm = gr / solutionGrams * 1e6; }
        }
        return new MolarityResult { Moles = moles!.Value, Grams = g, VolumeL = volumeL!.Value, Molarity = molarity!.Value, Solved = solved, MassPercent = percent, Ppm = ppm };
    }
}

public static class ChemDilution
{
    /// <summary>M1V1 = M2V2 with one of the four blank (0: M1, 1: V1, 2: M2, 3: V2): which was solved and its value, in
    /// the units the volumes came in (both the same). Null with what is wanting.</summary>
    public static (int Solved, double Value)? Solve(double? m1, double? v1, double? m2, double? v2, out string? message)
    {
        message = null;
        var all = new[] { m1, v1, m2, v2 };
        var blanks = all.Count(x => x == null);
        if (blanks == 0) { message = "Leave exactly one blank to solve it."; return null; }
        if (blanks > 1) { message = "Fill in all but one."; return null; }
        if (all.Any(x => x is <= 0)) { message = "Every value must be above zero."; return null; }
        var k = Array.FindIndex(all, x => x == null);
        var value = k switch
        {
            0 => m2!.Value * v2!.Value / v1!.Value,
            1 => m2!.Value * v2!.Value / m1!.Value,
            2 => m1!.Value * v1!.Value / v2!.Value,
            _ => m1!.Value * v1!.Value / m2!.Value,
        };
        if (!double.IsFinite(value)) { message = "That does not work out — check the numbers."; return null; }
        if (k == 1 && v2 is { } vv && value > vv) message = "The stock is weaker than what you want: it cannot be diluted to that.";
        return (k, value);
    }
}

/// <summary>Which titration.</summary>
public enum TitrationKind { StrongAcidStrongBase, WeakAcidStrongBase, WeakBaseStrongAcid }

/// <summary>A titration worked out: where the equivalence point is, the pH at the points that matter, and the curve.</summary>
public sealed class TitrationResult
{
    public required double EquivalenceMl { get; init; }
    public required double StartPh { get; init; }
    public required double HalfPh { get; init; }
    public required double EquivalencePh { get; init; }
    /// <summary>At twice the equivalence volume.</summary>
    public required double AfterPh { get; init; }
    /// <summary>A weak acid's pKa (or a weak base's pKb).</summary>
    public double? PK { get; init; }
    /// <summary>Two hundred points from no titrant to twice the equivalence volume: mL added and pH.</summary>
    public required IReadOnlyList<(double Ml, double Ph)> Curve { get; init; }
}

public static class ChemTitration
{
    public const int CurvePoints = 200;

    /// <summary>
    /// The analyte's concentration (M) and volume (mL) against the titrant's concentration (M), with K (Ka or Kb) for a
    /// weak analyte: the equivalence volume, the pH at the start, at half-equivalence, at equivalence and past it, and
    /// the curve — every pH exact, from the charge balance. Null with what is wanting.
    /// </summary>
    public static TitrationResult? Run(TitrationKind kind, double analyteM, double analyteMl, double titrantM, double? k, out string? message)
    {
        message = null;
        if (!(analyteM > 0) || !(analyteMl > 0) || !(titrantM > 0)) { message = "Concentrations and the volume must be above zero."; return null; }
        var weak = kind != TitrationKind.StrongAcidStrongBase;
        if (weak && (k is not { } kk || !(kk > 0))) { message = kind == TitrationKind.WeakAcidStrongBase ? "Give the acid’s Ka." : "Give the base’s Kb."; return null; }
        var kv = k ?? 0;
        var veq = analyteM * analyteMl / titrantM;
        var curve = new List<(double, double)>(CurvePoints);
        for (var i = 0; i < CurvePoints; i++)
        {
            var ml = 2 * veq * i / (CurvePoints - 1);
            curve.Add((ml, PhAt(kind, analyteM, analyteMl, titrantM, ml, kv)));
        }
        return new TitrationResult
        {
            EquivalenceMl = veq,
            StartPh = PhAt(kind, analyteM, analyteMl, titrantM, 0, kv),
            HalfPh = PhAt(kind, analyteM, analyteMl, titrantM, veq / 2, kv),
            EquivalencePh = PhAt(kind, analyteM, analyteMl, titrantM, veq, kv),
            AfterPh = PhAt(kind, analyteM, analyteMl, titrantM, 2 * veq, kv),
            PK = weak ? -Math.Log10(kv) : null,
            Curve = curve,
        };
    }

    /// <summary>
    /// The pH with so much titrant added, from the charge balance solved for [H+] by bisection on the pH (the balance
    /// falls as the pH rises): a strong acid and base wholly ionised; a weak acid's A- as Ca·Ka/(Ka + [H+]); a weak
    /// base's BH+ as Cb·[H+]/([H+] + Kw/Kb).
    /// </summary>
    public static double PhAt(TitrationKind kind, double analyteM, double analyteMl, double titrantM, double titrantMl, double k)
    {
        var total = analyteMl + titrantMl;
        var ca = analyteM * analyteMl / total;
        var ct = titrantM * titrantMl / total;
        double Balance(double h) => kind switch
        {
            TitrationKind.StrongAcidStrongBase => h + ct - ca - ChemPh.Kw / h,
            TitrationKind.WeakAcidStrongBase => h + ct - ca * k / (k + h) - ChemPh.Kw / h,
            _ => ca * h / (h + ChemPh.Kw / k) + h - ct - ChemPh.Kw / h,
        };
        double lo = -2, hi = 16;
        for (var i = 0; i < 100; i++)
        {
            var mid = (lo + hi) / 2;
            if (Balance(Math.Pow(10, -mid)) > 0) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
