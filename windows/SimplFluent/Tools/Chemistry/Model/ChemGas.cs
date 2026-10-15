namespace Simpl.Tools;

// Gas Laws' sums: PV = nRT with any one of the four left blank and solved, each in the unit its field chose (atm, kPa,
// mmHg, bar, psi; L, mL, m³; mol or grams of a formula; K, °C, °F), and the combined law P1V1/T1 = P2V2/T2 with one of
// its six blank — or four, with a pair held constant (Boyle, Charles, Gay-Lussac). R = 0.082057 L·atm/(mol·K); every
// sum is done in atm, L, mol and K. Plain .NET, no WPF, so the unit tests check it on any machine.

/// <summary>The units each field of the gas laws can be in, and the sums between them.</summary>
public static class ChemGasUnits
{
    public static readonly string[] Pressure = { "atm", "kPa", "mmHg", "bar", "psi" };
    public static readonly string[] Volume = { "L", "mL", "m³" };
    public static readonly string[] Amount = { "mol", "g" };
    public static readonly string[] Temperature = { "K", "°C", "°F" };

    /// <summary>How many of a pressure unit make one atmosphere.</summary>
    public static double PerAtm(string unit) => unit switch { "kPa" => 101.325, "mmHg" or "Torr" => 760, "bar" => 1.01325, "psi" => 14.6959, _ => 1 };
    public static double ToAtm(double v, string unit) => v / PerAtm(unit);
    public static double FromAtm(double atm, string unit) => atm * PerAtm(unit);

    /// <summary>How many litres make one of a volume unit.</summary>
    public static double LitresPer(string unit) => unit switch { "mL" => 0.001, "m³" or "m3" => 1000, _ => 1 };
    public static double ToLitres(double v, string unit) => v * LitresPer(unit);
    public static double FromLitres(double l, string unit) => l / LitresPer(unit);

    public static double ToKelvin(double v, string unit) => unit switch { "°C" or "C" => v + 273.15, "°F" or "F" => (v - 32) * 5 / 9 + 273.15, _ => v };
    public static double FromKelvin(double k, string unit) => unit switch { "°C" or "C" => k - 273.15, "°F" or "F" => (k - 273.15) * 9 / 5 + 32, _ => k };
}

/// <summary>The four of the ideal gas law.</summary>
public enum GasVar { P, V, N, T }

/// <summary>A field of the gas laws as typed: its number (null, blank) and its unit.</summary>
public readonly record struct GasValue(double? Value, string Unit)
{
    public bool Blank => Value == null;
}

/// <summary>What the ideal gas law gave: the one solved, its value in its field's unit, the four in base units, or what is wanting.</summary>
public sealed class GasSolution
{
    public GasVar? Solved { get; init; }
    /// <summary>The answer in the blank field's own unit.</summary>
    public double Value { get; init; }
    public string Unit { get; init; } = "";
    public string? Message { get; init; }
    public bool Ok => Message == null && Solved != null;
    /// <summary>The four in atm, L, mol and K once solved.</summary>
    public double Atm { get; init; }
    public double Litres { get; init; }
    public double Moles { get; init; }
    public double Kelvin { get; init; }
}

/// <summary>What the combined law gave: which of the six was solved (0–5: P1, V1, T1, P2, V2, T2), its value in its unit, or what is wanting.</summary>
public sealed class CombinedSolution
{
    public int Solved { get; init; } = -1;
    public double Value { get; init; }
    public string Unit { get; init; } = "";
    public string? Message { get; init; }
    public bool Ok => Message == null && Solved >= 0;
    /// <summary>The six in atm, L and K once solved (P1, V1, T1, P2, V2, T2).</summary>
    public double[] Base { get; init; } = new double[6];
}

public static class ChemGas
{
    /// <summary>The gas constant in L·atm/(mol·K).</summary>
    public const double R = 0.082057;

    /// <summary>
    /// PV = nRT with exactly one of the four blank: the blank one solved in its own unit. n in grams needs the gas's
    /// formula (its molar mass). Temperatures must be above absolute zero and the rest above zero.
    /// </summary>
    public static GasSolution Ideal(GasValue p, GasValue v, GasValue n, GasValue t, ChemFormula? formula)
    {
        var blanks = new[] { p.Blank, v.Blank, n.Blank, t.Blank }.Count(b => b);
        if (blanks == 0) return new GasSolution { Message = "Leave exactly one blank to solve it." };
        if (blanks > 1) return new GasSolution { Message = "Fill in all but one." };
        var grams = n.Unit == "g";
        var m = formula?.MolarMass ?? 0;
        if (grams && (formula == null || m <= 0)) return new GasSolution { Message = "Give the gas’s formula to use grams." };
        double atm = 0, litres = 0, moles = 0, kelvin = 0;
        if (!p.Blank) { atm = ChemGasUnits.ToAtm(p.Value!.Value, p.Unit); if (atm <= 0) return new GasSolution { Message = "Pressure must be above zero." }; }
        if (!v.Blank) { litres = ChemGasUnits.ToLitres(v.Value!.Value, v.Unit); if (litres <= 0) return new GasSolution { Message = "Volume must be above zero." }; }
        if (!n.Blank) { moles = grams ? n.Value!.Value / m : n.Value!.Value; if (moles <= 0) return new GasSolution { Message = "The amount must be above zero." }; }
        if (!t.Blank) { kelvin = ChemGasUnits.ToKelvin(t.Value!.Value, t.Unit); if (kelvin <= 0) return new GasSolution { Message = "Temperature must be above absolute zero (0 K, −273.15 °C)." }; }
        GasVar solved;
        double value;
        string unit;
        if (p.Blank) { atm = moles * R * kelvin / litres; solved = GasVar.P; value = ChemGasUnits.FromAtm(atm, p.Unit); unit = p.Unit; }
        else if (v.Blank) { litres = moles * R * kelvin / atm; solved = GasVar.V; value = ChemGasUnits.FromLitres(litres, v.Unit); unit = v.Unit; }
        else if (n.Blank) { moles = atm * litres / (R * kelvin); solved = GasVar.N; value = grams ? moles * m : moles; unit = n.Unit; }
        else { kelvin = atm * litres / (moles * R); solved = GasVar.T; value = ChemGasUnits.FromKelvin(kelvin, t.Unit); unit = t.Unit; }
        if (!double.IsFinite(value)) return new GasSolution { Message = "That does not work out — check the numbers." };
        return new GasSolution { Solved = solved, Value = value, Unit = unit, Atm = atm, Litres = litres, Moles = moles, Kelvin = kelvin };
    }

    /// <summary>
    /// P1V1/T1 = P2V2/T2 with one of the six blank (P1, V1, T1, P2, V2, T2); <paramref name="constant"/> "T", "P" or "V"
    /// holds that pair equal (Boyle, Charles, Gay-Lussac), so that pair may be left blank too and is not solved.
    /// </summary>
    public static CombinedSolution Combined(GasValue[] six, string? constant)
    {
        if (six.Length != 6) throw new ArgumentException("six values", nameof(six));
        var hidden = constant switch { "T" => new[] { 2, 5 }, "P" => new[] { 0, 3 }, "V" => new[] { 1, 4 }, _ => Array.Empty<int>() };
        var asked = Enumerable.Range(0, 6).Where(i => !hidden.Contains(i)).ToList();
        var blanks = asked.Where(i => six[i].Blank).ToList();
        if (blanks.Count == 0) return new CombinedSolution { Message = "Leave exactly one blank to solve it." };
        if (blanks.Count > 1) return new CombinedSolution { Message = "Fill in all but one." };
        var b = new double[6];
        for (var i = 0; i < 6; i++)
        {
            if (hidden.Contains(i)) { b[i] = 1; continue; }
            if (six[i].Blank) continue;
            var x = six[i].Value!.Value;
            b[i] = (i % 3) switch { 0 => ChemGasUnits.ToAtm(x, six[i].Unit), 1 => ChemGasUnits.ToLitres(x, six[i].Unit), _ => ChemGasUnits.ToKelvin(x, six[i].Unit) };
            if (b[i] <= 0) return new CombinedSolution { Message = i % 3 == 2 ? "Temperatures must be above absolute zero (0 K, −273.15 °C)." : "Pressures and volumes must be above zero." };
        }
        // (P1·V1·T2 = P2·V2·T1: the blank one is the other five's product over its own side's two)
        var k = blanks[0];
        b[k] = k switch
        {
            0 => b[3] * b[4] * b[2] / (b[1] * b[5]),
            1 => b[3] * b[4] * b[2] / (b[0] * b[5]),
            2 => b[0] * b[1] * b[5] / (b[3] * b[4]),
            3 => b[0] * b[1] * b[5] / (b[4] * b[2]),
            4 => b[0] * b[1] * b[5] / (b[3] * b[2]),
            _ => b[3] * b[4] * b[2] / (b[0] * b[1]),
        };
        var unit = six[k].Unit;
        var value = (k % 3) switch { 0 => ChemGasUnits.FromAtm(b[k], unit), 1 => ChemGasUnits.FromLitres(b[k], unit), _ => ChemGasUnits.FromKelvin(b[k], unit) };
        if (!double.IsFinite(value)) return new CombinedSolution { Message = "That does not work out — check the numbers." };
        return new CombinedSolution { Solved = k, Value = value, Unit = unit, Base = b };
    }
}
