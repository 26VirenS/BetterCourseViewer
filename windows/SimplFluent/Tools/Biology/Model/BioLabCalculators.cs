using System.Globalization;

namespace Simpl.Tools;

// The Bio Lab Calculator's calculators (spec §4.8, §5.5), each worked the way it is written on paper: the formula, the
// values substituted with their units, the answer to sensible significant figures, and the steps — unit conversions
// first — behind a "Show steps". A calculator is described here as values (its fields with their unit menus, the
// things it can solve for, and how it solves), so the view draws every one the same way and the self-check runs the
// same code the student does. Plain .NET, no WPF.

/// <summary>What a field's value must be (a field that breaks it turns red with the reason, spec §9).</summary>
public enum LabCheck
{
    Any,
    /// <summary>More than 0.</summary>
    Positive,
    /// <summary>0 or more.</summary>
    NonNegative,
    /// <summary>A whole number, 1 or more.</summary>
    Count,
    /// <summary>A whole number, 0 or more.</summary>
    WholeOrZero,
    /// <summary>A temperature above absolute zero.</summary>
    Temperature,
    /// <summary>A list of numbers, at least one.</summary>
    List,
    /// <summary>A list of whole numbers, 0 or more each, at least one.</summary>
    Counts,
}

/// <summary>A calculator's field: its id, its label, the symbol the formula uses, what it measures, its starting text
/// and unit, what it must be, whether it is exact (a count does not limit the answer's figures), and a hint.</summary>
public sealed record LabField(string Id, string Label, string Symbol, LabDim Dim, string Text, string Unit = "",
    LabCheck Check = LabCheck.Positive, bool Exact = false, string? Hint = null, IReadOnlyList<LabPreset>? Presets = null, bool Optional = false)
{
    public bool IsList => Check is LabCheck.List or LabCheck.Counts;
}

/// <summary>A value a field offers in its menu (a solute's molar mass, a lens): its words and the text it puts in.</summary>
public sealed record LabPreset(string Label, string Text);

/// <summary>A way a calculator can work: what it solves for (its title), its formula, the fields it reads, and the field
/// its answer fills (whose unit menu chooses the answer's unit), if any.</summary>
public sealed record LabMode(string Id, string Title, string Formula, IReadOnlyList<string> Inputs, string? Output = null);

/// <summary>A further result beside the main answer (SA and V beside SA:V; the statistics of a data set).</summary>
public sealed record LabRow(string Name, string Value, bool Strong = false);

/// <summary>A worked answer, or why there is none.</summary>
public sealed class LabResult
{
    /// <summary>Why there is no answer, in a line (the fields' own reasons are in <see cref="FieldErrors"/>).</summary>
    public string? Problem { get; set; }
    public Dictionary<string, string> FieldErrors { get; } = new();
    /// <summary>"m", "V₁", "RCF".</summary>
    public string Symbol { get; set; } = "";
    /// <summary>"7.31".</summary>
    public string Value { get; set; } = "";
    /// <summary>"g", "× g", "µg/mL".</summary>
    public string Unit { get; set; } = "";
    /// <summary>The answer in a handier unit ("80.4 µM"), if any.</summary>
    public string? Alt { get; set; }
    /// <summary>The answer unrounded, in the answer's unit.</summary>
    public double Raw { get; set; } = double.NaN;
    /// <summary>The answer in its quantity's base unit (M, L, g …), unrounded.</summary>
    public double RawBase { get; set; } = double.NaN;
    /// <summary>The formula with the values substituted and the unrounded answer ("m = 0.50 mol/L × 0.25 L × 58.44 g/mol = 7.305 g").</summary>
    public string Substitution { get; set; } = "";
    public List<string> Steps { get; } = new();
    public List<string> Notes { get; } = new();
    public List<LabRow> Rows { get; } = new();
    /// <summary>A table or a chart's numbers, for the view (LabTable, LabCurve, LabBars).</summary>
    public object? Extra { get; set; }
    public bool Ok => Problem == null && FieldErrors.Count == 0 && Value.Length > 0;

    /// <summary>"m = 7.31 g", "total = 400×", "% w/v = 2.0%".</summary>
    public string Sentence => Ok ? $"{Symbol} = {Value}{UnitAfter(Unit)}" : "";

    /// <summary>A unit as it follows its number: "×" and "%" close up, the rest after a space.</summary>
    public static string UnitAfter(string unit) => unit.Length == 0 ? "" : unit is "×" or "%" ? unit : " " + unit;
}

/// <summary>A table a calculator shows (the serial dilution's tubes).</summary>
public sealed record LabTable(IReadOnlyList<string> Heads, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>A chart's numbers: its curves, a marked point, its axes' names.</summary>
public sealed record LabCurve(string XLabel, string YLabel, IReadOnlyList<LabSeries> Series, (double X, double Y, string Label)? Mark, double? HLine = null, string? HLineLabel = null);

/// <summary>A curve: its name, its points, dashed or not, and which colour of the chart's (0: the tool's, 1: a quieter one).</summary>
public sealed record LabSeries(string Name, IReadOnlyList<(double X, double Y)> Points, bool Dashed = false, int Tone = 0);

/// <summary>A data set's error bar: its name, its mean, and the bar's ends (mean ± 2 SE).</summary>
public sealed record LabBar(string Name, double Mean, double Low, double High);

/// <summary>A scale bar to draw: its length on the image in millimetres, and what it stands for ("10 µm").</summary>
public sealed record LabScaleBar(double Millimetres, string Label);

/// <summary>A calculator: its id, its page, its title and line, its fields, the ways it works, and how it solves.</summary>
public sealed class LabCalc
{
    public required string Id { get; init; }
    public required string Page { get; init; }
    public required string Title { get; init; }
    public required string Help { get; init; }
    public required IReadOnlyList<LabField> Fields { get; init; }
    public required IReadOnlyList<LabMode> Modes { get; init; }
    /// <summary>What the choice of mode is called ("Solve for", "Shape", "Kind").</summary>
    public string ModeLabel { get; init; } = "Solve for";
    public required Func<LabRun, LabResult> Solve { get; init; }

    public LabField Field(string id) => Fields.First(f => f.Id == id);
    public LabMode Mode(string? id) => Modes.FirstOrDefault(m => m.Id == id) ?? Modes[0];

    /// <summary>Worked with these texts and units (missing ones: the fields' own), in this mode, to the setting's figures.</summary>
    public LabResult Run(string? mode = null, IReadOnlyDictionary<string, string>? texts = null, IReadOnlyDictionary<string, string>? units = null, int figures = 3)
    {
        var run = new LabRun(this, Mode(mode), texts, units, figures);
        try { return Solve(run); }
        catch (Exception e) { run.R.Problem = "This could not be worked out: " + e.Message; return run.R; }
    }
}

/// <summary>
/// One working of a calculator: its fields' values read (each in a unit of the caller's choosing, a conversion step
/// written where the unit changes), the significant figures the answer may have, and the answer written out.
/// </summary>
public sealed class LabRun
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly IReadOnlyDictionary<string, string> texts, units;
    private readonly List<(string Id, LabNumber N)> measured = new();
    private bool anyMeasured;

    public LabRun(LabCalc calc, LabMode mode, IReadOnlyDictionary<string, string>? texts, IReadOnlyDictionary<string, string>? units, int figures)
    {
        Calc = calc;
        Mode = mode;
        this.texts = texts ?? new Dictionary<string, string>();
        this.units = units ?? new Dictionary<string, string>();
        Setting = Math.Clamp(figures, 2, 6);
    }

    public LabCalc Calc { get; }
    public LabMode Mode { get; }
    /// <summary>The significant-figures setting (2–6).</summary>
    public int Setting { get; }
    public LabResult R { get; } = new();
    public bool Failed => R.FieldErrors.Count > 0 || R.Problem != null;

    public string Text(string id) => texts.TryGetValue(id, out var t) ? t : Calc.Field(id).Text;
    public LabUnit Unit(string id) => LabUnits.Find(Calc.Field(id).Dim, units.TryGetValue(id, out var u) ? u : Calc.Field(id).Unit);
    private static string Sym(LabField f) => f.Symbol;

    /// <summary>The figures the answer may have: the setting, but no more than the least precise measured input plus one.</summary>
    public int Figures => LabNumbers.Allowed(Setting, measured.Select(m => m.N));

    /// <summary>Every input exact (counts, magnifications): the answer is written without trailing zeros.</summary>
    public bool Exact => !anyMeasured;

    /// <summary>A number in a field, checked; recorded for the answer's figures (unless the field is exact).</summary>
    public LabNumber? Number(string id)
    {
        var f = Calc.Field(id);
        var text = Text(id);
        if (string.IsNullOrWhiteSpace(text)) { R.FieldErrors[id] = $"Type {Article(f.Label)}."; return null; }
        if (LabNumbers.Parse(text, f.Exact) is not { } n) { R.FieldErrors[id] = $"“{text.Trim()}” isn’t a number."; return null; }
        if (Why(f, n.Value) is { } why) { R.FieldErrors[id] = why; return null; }
        if (!n.Exact) { measured.Add((id, n)); anyMeasured = true; }
        return n;
    }

    private static string Article(string label) => "AEIOU".Contains(char.ToUpperInvariant(label[0])) ? "an " + label.ToLowerInvariant() : "a " + label.ToLowerInvariant();

    /// <summary>Why a value is out of range for its field, or null.</summary>
    private static string? Why(LabField f, double v) => f.Check switch
    {
        LabCheck.Positive when v <= 0 => $"{f.Label} must be more than 0.",
        LabCheck.NonNegative when v < 0 => $"{f.Label} can’t be negative.",
        LabCheck.Count when v < 1 || Math.Abs(v - Math.Round(v)) > 1e-9 => $"{f.Label} must be a whole number, 1 or more.",
        LabCheck.WholeOrZero when v < 0 || Math.Abs(v - Math.Round(v)) > 1e-9 => $"{f.Label} must be a whole number.",
        LabCheck.Temperature when f.Dim == LabDim.Temperature && v < -273 => "Nothing is colder than −273 °C (0 K).",
        _ => null,
    };

    /// <summary>A field's value in a unit of its quantity (its base unit if none is named); null with the field's reason.</summary>
    public double? Get(string id, string? unit = null)
    {
        if (Number(id) is not { } n) return null;
        var f = Calc.Field(id);
        var b = Unit(id).ToBase(n.Value);
        if (f.Dim == LabDim.Temperature && b < -273) { R.FieldErrors[id] = "Nothing is colder than −273 °C (0 K)."; return null; }
        return unit == null ? b : LabUnits.Find(f.Dim, unit).FromBase(b);
    }

    /// <summary>An optional field's value (null, with no complaint, when it is left empty).</summary>
    public double? Optional(string id, string? unit = null) => string.IsNullOrWhiteSpace(Text(id)) ? null : Get(id, unit);

    /// <summary>A list of numbers in a field (counts, data); null with the field's reason.</summary>
    public List<LabNumber>? List(string id)
    {
        var f = Calc.Field(id);
        var (list, bad) = LabNumbers.ParseList(Text(id), f.Exact || f.Check == LabCheck.Counts);
        if (bad != null) { R.FieldErrors[id] = $"“{bad}” isn’t a number."; return null; }
        if (list == null || list.Count == 0) { R.FieldErrors[id] = "Type at least one number."; return null; }
        if (f.Check == LabCheck.Counts && list.Any(n => n.Value < 0 || Math.Abs(n.Value - Math.Round(n.Value)) > 1e-9))
        {
            R.FieldErrors[id] = "Counts are whole numbers, 0 or more.";
            return null;
        }
        foreach (var n in list.Where(n => !n.Exact)) { measured.Add((id, n)); anyMeasured = true; }
        return list;
    }

    /// <summary>A field's number as typed, written neatly: "2.0", "1 × 10⁻⁷".</summary>
    public string T(string id) => LabNumbers.Pretty(Text(id));

    /// <summary>A field as typed, with its unit: "250 mL", "1 × 10⁻⁷ M".</summary>
    public string Typed(string id)
    {
        var u = Unit(id).Symbol;
        var t = LabNumbers.Pretty(Text(id));
        return u.Length == 0 ? t : $"{t} {u}";
    }

    /// <summary>
    /// A field's value written in a unit of its quantity, for the substitution: as typed when that is its unit, else
    /// converted — and the conversion written as a step ("V = 250 mL = 0.25 L").
    /// </summary>
    public string In(string id, string unit, string? shownUnit = null)
    {
        var f = Calc.Field(id);
        var from = Unit(id);
        var shown = shownUnit ?? unit;
        if (from.Symbol == unit) return $"{LabNumbers.Pretty(Text(id))} {shown}";
        var to = LabUnits.Find(f.Dim, unit);
        var v = LabNumbers.Parse(Text(id), f.Exact)?.Value ?? double.NaN;
        var converted = to.FromBase(from.ToBase(v));
        var text = $"{LabNumbers.Plain(converted)} {shown}";
        var step = f.Dim == LabDim.Temperature
            ? $"{Sym(f)} = {Typed(id)} {(to.Symbol == "K" ? "+ 273" : "− 273")} = {text}"
            : $"{Sym(f)} = {Typed(id)} = {text}";
        if (!R.Steps.Contains(step)) R.Steps.Add(step);
        return text;
    }

    /// <summary>
    /// The answer: <paramref name="value"/> in <paramref name="valueUnit"/> (a unit of the output field's quantity),
    /// written in the unit chosen for the output field, to the figures allowed; a conversion step if the units differ,
    /// then the rounding. For an answer with no field, <paramref name="valueUnit"/> is simply its unit's words.
    /// </summary>
    public void Answer(double value, string valueUnit, string? symbol = null, string? field = null, bool alt = true)
    {
        field ??= Mode.Output;
        var r = R;
        if (!double.IsFinite(value)) { r.Problem = "The answer isn’t a finite number: check the values."; return; }
        LabField? f = field != null ? Calc.Field(field) : null;
        r.Symbol = symbol ?? f?.Symbol ?? "";
        double shown = value;
        string unit = valueUnit;
        if (f != null && f.Dim is not (LabDim.Number or LabDim.Label or LabDim.PerTime))
        {
            var from = LabUnits.Find(f.Dim, valueUnit);
            var to = Unit(field!);
            r.RawBase = from.ToBase(value);
            shown = to.FromBase(r.RawBase);
            unit = to.Symbol;
            if (to.Symbol != from.Symbol) r.Steps.Add($"{r.Symbol} = {LabNumbers.Plain(value)} {from.Symbol} = {LabNumbers.Plain(shown)} {to.Symbol}");
            if (alt && LabUnits.Nice(f.Dim, r.RawBase) is { } nice && nice.Symbol != to.Symbol && !(Math.Abs(shown) >= 1 && Math.Abs(shown) < 1000))
                r.Alt = $"{LabNumbers.Sig(nice.FromBase(r.RawBase), Figures, Exact)} {nice.Symbol}";
        }
        else r.RawBase = value;
        r.Raw = shown;
        r.Unit = unit;
        r.Value = Format(shown);
        r.Steps.Add(Rounding(shown, unit));
    }

    /// <summary>A value written to the answer's figures (an exact answer without trailing zeros).</summary>
    public string Format(double v) => LabNumbers.Minus(LabNumbers.Sig(v, Figures, Exact));

    /// <summary>The rounding, said: "7.305 g → 7.31 g (3 significant figures; 0.50 M has 2: never more than one past the least precise)".</summary>
    public string Rounding(double raw, string unit)
    {
        var u = LabResult.UnitAfter(unit);
        var plain = LabNumbers.Plain(raw);
        var shown = Format(raw);
        string how;
        if (Exact) how = $"{Figures} significant figures; every input is exact";
        else
        {
            var least = measured.OrderBy(m => m.N.Figures).First();
            how = Figures < Setting
                ? $"{Figures} significant figures: {Typed(least.Id)} has {least.N.Figures}, and an answer has no more than one figure past its least precise input"
                : $"{Figures} significant figures";
        }
        if (plain == shown) return Exact ? $"= {shown}{u} exactly" : $"= {shown}{u} ({how})";
        return $"{plain}{u} → {shown}{u} ({how})";
    }

    /// <summary>No answer, and why.</summary>
    public LabResult Fail(string why)
    {
        R.Problem = why;
        return R;
    }
}

/// <summary>The calculators, page by page (spec §4.8; formulas and test vectors §5.5).</summary>
public static partial class LabCalcs
{
    /// <summary>The gas constant as the AP sheet gives it, L·bar/(mol·K).</summary>
    public const double GasConstant = 0.0831;

    /// <summary>The centrifuge's constant: RCF = 1.118 × 10⁻⁵ × r (cm) × rpm².</summary>
    public const double RcfConstant = 1.118e-5;

    /// <summary>Every calculator, in page order.</summary>
    public static IReadOnlyList<LabCalc> All => all.Value;

    // (made on first use, once every table they read is in place)
    private static readonly Lazy<IReadOnlyList<LabCalc>> all = new(Make);

    public static LabCalc? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>A page's calculators ("solutions", "microscopy", "cells", "stats", "ecology", "molecules").</summary>
    public static IEnumerable<LabCalc> On(string page) => All.Where(c => c.Page == page);

    private static string P(double v) => LabNumbers.Plain(v);

    /// <summary>Standard atomic weights (g/mol) of the elements the solute menu uses (the Mac's table).</summary>
    public static readonly IReadOnlyDictionary<string, double> AtomicWeights = new Dictionary<string, double>
    {
        ["H"] = 1.00794, ["C"] = 12.0107, ["N"] = 14.0067, ["O"] = 15.9994, ["Na"] = 22.98977, ["Mg"] = 24.3050,
        ["P"] = 30.973762, ["S"] = 32.065, ["Cl"] = 35.453, ["K"] = 39.0983, ["Ca"] = 40.078,
    };

    /// <summary>A formula's molar mass ("NaHCO3"), or null if it names an element not in the table.</summary>
    public static double? MolarMass(string formula)
    {
        var total = 0.0;
        var i = 0;
        while (i < formula.Length)
        {
            if (!char.IsUpper(formula[i])) return null;
            var symbol = formula[i++].ToString();
            while (i < formula.Length && char.IsLower(formula[i])) symbol += formula[i++];
            var digits = "";
            while (i < formula.Length && char.IsDigit(formula[i])) digits += formula[i++];
            if (!AtomicWeights.TryGetValue(symbol, out var w)) return null;
            total += w * (digits.Length > 0 ? int.Parse(digits, CultureInfo.InvariantCulture) : 1);
        }
        return total;
    }

    /// <summary>The solute menu: common lab solutes, their formulas and molar masses.</summary>
    public static readonly IReadOnlyList<(string Name, string Formula)> Solutes = new[]
    {
        ("Sodium chloride", "NaCl"), ("Potassium chloride", "KCl"), ("Calcium chloride", "CaCl2"), ("Magnesium chloride", "MgCl2"),
        ("Sodium hydroxide", "NaOH"), ("Sodium bicarbonate", "NaHCO3"), ("Glucose", "C6H12O6"), ("Sucrose", "C12H22O11"), ("Tris", "C4H11NO3"),
    };

    private static readonly IReadOnlyList<LabPreset> SolutePresets = Solutes.Select(s =>
    {
        var mass = LabNumbers.Fixed(MolarMass(s.Formula) ?? 0, 2).Replace(",", "");
        var written = string.Concat(s.Formula.Select(ch => char.IsDigit(ch) ? "₀₁₂₃₄₅₆₇₈₉"[ch - '0'] : ch));
        return new LabPreset($"{s.Name}, {written}: {mass}", mass);
    }).ToList();

    private static List<LabCalc> Make() => new()
    {
        // ---- Solutions ----
        Molarity(), Dilution(), Serial(), PlateCount(), Percent(), Units(),
        // ---- Microscopy ----
        TotalMagnification(), FieldOfView(), SpecimenSize(), Magnification(), ScaleBar(),
        // ---- Cells ----
        SurfaceVolume(), WaterPotential(), Hemocytometer(),
        // ---- Statistics ----
        Descriptive(), ChiSquare(),
        // ---- Populations ----
        Rate(), Growth(), Simpson(),
        // ---- Molecules ----
        BeerLambert(), A260(), Pcr(), Centrifuge(), Bacteria(), Ph(), Q10(),
    };

    // ======== Solutions ========

    private static LabCalc Molarity() => new()
    {
        Id = "molarity", Page = "solutions", Title = "Molarity",
        Help = "Mass, moles, concentration and volume of a solution: M = n / V and n = m / MW.",
        Fields = new LabField[]
        {
            new("c", "Concentration", "M", LabDim.Concentration, "0.50", "M"),
            new("v", "Volume", "V", LabDim.Volume, "250", "mL"),
            new("mw", "Molar mass", "MW", LabDim.MolarMass, "58.44", "g/mol", Hint: "NaCl is 58.44 g/mol; the menu has others.", Presets: SolutePresets),
            new("m", "Mass", "m", LabDim.Mass, "7.31", "g"),
            new("n", "Moles", "n", LabDim.Amount, "0.125", "mol"),
        },
        Modes = new LabMode[]
        {
            new("mass", "Mass", "m = M × V × MW", new[] { "c", "v", "mw" }, "m"),
            new("moles", "Moles", "n = m ÷ MW", new[] { "m", "mw" }, "n"),
            new("molarity", "Molarity", "M = m ÷ (MW × V)", new[] { "m", "mw", "v" }, "c"),
            new("volume", "Volume", "V = m ÷ (MW × M)", new[] { "m", "mw", "c" }, "v"),
        },
        Solve = run =>
        {
            var r = run.R;
            switch (run.Mode.Id)
            {
                case "mass":
                {
                    var (c, v, mw) = (run.Get("c"), run.Get("v"), run.Get("mw"));
                    if (run.Failed) return r;
                    string cs = run.In("c", "M", "mol/L"), vs = run.In("v", "L"), mws = run.In("mw", "g/mol");
                    var n = c!.Value * v!.Value;
                    var m = n * mw!.Value;
                    r.Steps.Add($"n = M × V = {cs} × {vs} = {P(n)} mol");
                    r.Steps.Add($"m = n × MW = {P(n)} mol × {mws} = {P(m)} g");
                    r.Substitution = $"m = {cs} × {vs} × {mws} = {P(m)} g";
                    run.Answer(m, "g");
                    break;
                }
                case "moles":
                {
                    var (m, mw) = (run.Get("m"), run.Get("mw"));
                    if (run.Failed) return r;
                    string ms = run.In("m", "g"), mws = run.In("mw", "g/mol");
                    var n = m!.Value / mw!.Value;
                    r.Substitution = $"n = {ms} ÷ {mws} = {P(n)} mol";
                    run.Answer(n, "mol");
                    break;
                }
                case "molarity":
                {
                    var (m, mw, v) = (run.Get("m"), run.Get("mw"), run.Get("v"));
                    if (run.Failed) return r;
                    string ms = run.In("m", "g"), mws = run.In("mw", "g/mol"), vs = run.In("v", "L");
                    var n = m!.Value / mw!.Value;
                    var c = n / v!.Value;
                    r.Steps.Add($"n = m ÷ MW = {ms} ÷ {mws} = {P(n)} mol");
                    r.Steps.Add($"M = n ÷ V = {P(n)} mol ÷ {vs} = {P(c)} mol/L");
                    r.Substitution = $"M = {ms} ÷ ({mws} × {vs}) = {P(c)} M";
                    run.Answer(c, "M");
                    break;
                }
                default:
                {
                    var (m, mw, c) = (run.Get("m"), run.Get("mw"), run.Get("c"));
                    if (run.Failed) return r;
                    string ms = run.In("m", "g"), mws = run.In("mw", "g/mol"), cs = run.In("c", "M", "mol/L");
                    var n = m!.Value / mw!.Value;
                    var v = n / c!.Value;
                    r.Steps.Add($"n = m ÷ MW = {ms} ÷ {mws} = {P(n)} mol");
                    r.Steps.Add($"V = n ÷ M = {P(n)} mol ÷ {cs} = {P(v)} L");
                    r.Substitution = $"V = {ms} ÷ ({mws} × {cs}) = {P(v)} L";
                    run.Answer(v, "L");
                    break;
                }
            }
            if (r.Ok && run.Mode.Id == "mass") r.Notes.Add($"Dissolve {r.Value} {r.Unit} in a little water, then add water up to {run.Typed("v")} in all.");
            return r;
        },
    };

    private static LabCalc Dilution() => new()
    {
        Id = "dilution", Page = "solutions", Title = "Dilution",
        Help = "C₁V₁ = C₂V₂: the stock (C₁, V₁) and the dilution made from it (C₂, V₂). Solve for any one.",
        Fields = new LabField[]
        {
            new("C1", "Stock concentration", "C₁", LabDim.Concentration, "1.0", "M"),
            new("V1", "Stock volume", "V₁", LabDim.Volume, "5.0", "mL"),
            new("C2", "Final concentration", "C₂", LabDim.Concentration, "0.1", "M"),
            new("V2", "Final volume", "V₂", LabDim.Volume, "50", "mL"),
        },
        Modes = new LabMode[]
        {
            new("V1", "V₁", "V₁ = C₂V₂ ÷ C₁", new[] { "C1", "C2", "V2" }, "V1"),
            new("C1", "C₁", "C₁ = C₂V₂ ÷ V₁", new[] { "V1", "C2", "V2" }, "C1"),
            new("C2", "C₂", "C₂ = C₁V₁ ÷ V₂", new[] { "C1", "V1", "V2" }, "C2"),
            new("V2", "V₂", "V₂ = C₁V₁ ÷ C₂", new[] { "C1", "V1", "C2" }, "V2"),
        },
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            // (each pair in one unit: the concentrations in C₁'s, the volumes in V₂'s — or V₁'s when V₂ is the answer)
            var cu = run.Mode.Id == "C1" ? run.Unit("C2").Symbol : run.Unit("C1").Symbol;
            var vu = run.Mode.Id == "V2" ? run.Unit("V1").Symbol : run.Unit("V2").Symbol;
            double C(string id) => run.Get(id, cu)!.Value;
            double V(string id) => run.Get(id, vu)!.Value;
            switch (run.Mode.Id)
            {
                case "V1":
                {
                    string c1 = run.In("C1", cu), c2 = run.In("C2", cu), v2 = run.In("V2", vu);
                    if (C("C2") > C("C1")) return run.Fail("C₂ is more than C₁: a dilution can only make a solution weaker.");
                    var v1 = C("C2") * V("V2") / C("C1");
                    r.Substitution = $"V₁ = ({c2} × {v2}) ÷ {c1} = {P(v1)} {vu}";
                    run.Answer(v1, vu);
                    if (r.Ok)
                    {
                        var solvent = V("V2") - v1;
                        r.Steps.Add($"solvent = V₂ − V₁ = {v2} − {P(v1)} {vu} = {P(solvent)} {vu}");
                        r.Notes.Add($"Take {r.Value} {r.Unit} of the stock and add {run.Format(solvent)} {vu} of solvent, to make {run.Typed("V2")}.");
                    }
                    break;
                }
                case "C1":
                {
                    string v1 = run.In("V1", vu), c2 = run.In("C2", cu), v2 = run.In("V2", vu);
                    if (V("V1") > V("V2")) return run.Fail("V₁ is more than V₂: the final volume includes the stock, so it can’t be smaller.");
                    var c1 = C("C2") * V("V2") / V("V1");
                    r.Substitution = $"C₁ = ({c2} × {v2}) ÷ {v1} = {P(c1)} {cu}";
                    run.Answer(c1, cu);
                    break;
                }
                case "C2":
                {
                    string c1 = run.In("C1", cu), v1 = run.In("V1", vu), v2 = run.In("V2", vu);
                    if (V("V1") > V("V2")) return run.Fail("V₁ is more than V₂: the final volume includes the stock, so it can’t be smaller.");
                    var c2 = C("C1") * V("V1") / V("V2");
                    r.Substitution = $"C₂ = ({c1} × {v1}) ÷ {v2} = {P(c2)} {cu}";
                    run.Answer(c2, cu);
                    break;
                }
                default:
                {
                    string c1 = run.In("C1", cu), v1 = run.In("V1", vu), c2 = run.In("C2", cu);
                    if (C("C2") > C("C1")) return run.Fail("C₂ is more than C₁: a dilution can only make a solution weaker.");
                    var v2 = C("C1") * V("V1") / C("C2");
                    r.Substitution = $"V₂ = ({c1} × {v1}) ÷ {c2} = {P(v2)} {vu}";
                    run.Answer(v2, vu);
                    if (r.Ok) r.Notes.Add($"Add solvent to {run.Typed("V1")} of the stock until the total is {r.Value} {r.Unit}.");
                    break;
                }
            }
            return r;
        },
    };

    private static LabCalc Serial() => new()
    {
        Id = "serial", Page = "solutions", Title = "Serial dilution",
        Help = "The same dilution made again and again: each tube takes some of the one before.",
        Fields = new LabField[]
        {
            new("vs", "Sample moved each time", "V sample", LabDim.Volume, "1", "mL", Exact: true),
            new("vd", "Diluent in each tube", "V diluent", LabDim.Volume, "9", "mL", Exact: true),
            new("k", "Number of tubes", "k", LabDim.Number, "4", Check: LabCheck.Count, Exact: true),
            new("c0", "Starting concentration", "C₀", LabDim.Label, "2.5 × 10^8", "cells/mL", Hint: "Any unit: each tube is in the same one."),
        },
        Modes = new LabMode[] { new("serial", "Tubes", "f = V sample ÷ (V sample + V diluent);  C_k = C₀ × fᵏ", new[] { "vs", "vd", "k", "c0" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (vs, vd, kk, c0) = (run.Get("vs"), run.Get("vd"), run.Get("k"), run.Get("c0"));
            if (run.Failed) return r;
            var k = (int)Math.Round(kk!.Value);
            if (k > 15) return run.Fail("Up to 15 tubes.");
            var vsu = run.Unit("vs").Symbol;
            string vss = run.In("vs", vsu), vds = run.In("vd", vsu);
            var vdIn = run.Get("vd", vsu)!.Value;
            var vsIn = run.Get("vs", vsu)!.Value;
            var f = vsIn / (vsIn + vdIn);
            var ratio = 1 / f;
            var ratioText = Math.Abs(ratio - Math.Round(ratio)) < 1e-9 ? $"1:{Math.Round(ratio).ToString("N0", Inv)}" : $"1:{P(ratio)}";
            r.Steps.Add($"f = {vss} ÷ ({vss} + {vds}) = {P(f)}  (a {ratioText} dilution each time)");
            var total = Math.Pow(f, k);
            r.Steps.Add($"after {k} tube{(k == 1 ? "" : "s")}: f{LabNumbers.Sup(k)} = {P(f)}{LabNumbers.Sup(k)} = {Factor(total)}");
            var unit = run.Unit("c0").Symbol;
            var ck = c0!.Value * total;
            r.Steps.Add($"C{Sub(k)} = C₀ × f{LabNumbers.Sup(k)} = {run.Typed("c0")} × {Factor(total)} = {P(ck)} {unit}");
            r.Substitution = $"f = {P(f)} per tube;  total = {P(f)}{LabNumbers.Sup(k)} = {Factor(total)}";
            r.Symbol = $"C{Sub(k)}";
            r.Raw = ck;
            r.RawBase = ck;
            r.Unit = unit;
            r.Value = run.Format(ck);
            r.Steps.Add(run.Rounding(ck, unit));
            r.Rows.Add(new LabRow("Total dilution", $"{Factor(total)} ({TotalRatio(total)})", Strong: true));
            var rows = new List<IReadOnlyList<string>>();
            for (var i = 0; i <= k; i++)
            {
                var t = Math.Pow(f, i);
                rows.Add(new[] { i == 0 ? "Stock" : $"Tube {i}", i == 0 ? "1" : TotalRatio(t), i == 0 ? "1" : Factor(t), $"{run.Format(c0.Value * t)} {unit}" });
            }
            r.Extra = new LabTable(new[] { "Tube", "Dilution", "Factor", "Concentration" }, rows);
            r.Notes.Add("Mix each tube well before taking from it, and use a fresh tip each time.");
            return r;
        },
    };

    /// <summary>A dilution factor: "10⁻⁴" when it is a power of ten, else to four figures ("6.25 × 10⁻²").</summary>
    public static string Factor(double f)
    {
        var e = Math.Log10(f);
        if (Math.Abs(e - Math.Round(e)) < 1e-9) return Math.Round(e) == 0 ? "1" : "10" + LabNumbers.Sup((int)Math.Round(e));
        return LabNumbers.Minus(LabNumbers.Sig(f, 4, trim: true));
    }

    /// <summary>"1:10,000".</summary>
    private static string TotalRatio(double f)
    {
        var n = 1 / f;
        return n < 1e15 && Math.Abs(n - Math.Round(n)) < 1e-6 * n ? $"1:{Math.Round(n).ToString("N0", Inv)}" : $"1:{LabNumbers.Sig(n, 4, trim: true)}";
    }

    private static string Sub(int n) => string.Concat(n.ToString(Inv).Select(ch => "₀₁₂₃₄₅₆₇₈₉"[ch - '0']));

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static LabCalc PlateCount() => new()
    {
        Id = "plate", Page = "solutions", Title = "Plate count → original",
        Help = "The colonies on a plate back to the cells per mL in the original culture.",
        Fields = new LabField[]
        {
            new("colonies", "Colonies counted", "colonies", LabDim.Number, "150", Check: LabCheck.WholeOrZero, Exact: true, Hint: "Count a plate with 30–300 colonies."),
            new("vp", "Volume plated", "V", LabDim.Volume, "0.1", "mL"),
            new("dil", "Total dilution of that tube", "dilution", LabDim.Number, "10^-6", Exact: true, Hint: "As a factor: 10^-6 (1:1,000,000).",
                Presets: Enumerable.Range(1, 8).Select(k => new LabPreset("10" + LabNumbers.Sup(-k), $"10^-{k}")).ToList()),
        },
        Modes = new LabMode[] { new("plate", "CFU/mL", "cells/mL = colonies ÷ (volume plated in mL × total dilution)", new[] { "colonies", "vp", "dil" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (col, vp, dil) = (run.Get("colonies"), run.Get("vp", "mL"), run.Get("dil"));
            if (run.Failed) return r;
            var d = dil!.Value;
            if (d > 1)
            {
                r.Notes.Add($"Read as 1:{P(d)} — a dilution factor of {Factor(1 / d)}.");
                d = 1 / d;
            }
            var vps = run.In("vp", "mL");
            var cfu = col!.Value / (vp!.Value * d);
            r.Substitution = $"original = {P(col.Value)} ÷ ({vps} × {Factor(d)}) = {P(cfu)} CFU/mL";
            r.Steps.Add($"volume × dilution = {vps} × {Factor(d)} = {P(vp.Value * d)} mL of the original culture on the plate");
            run.Answer(cfu, "CFU/mL", "original");
            if (col.Value is > 0 and (< 30 or > 300)) r.Notes.Add("Plates with 30–300 colonies give the most reliable count.");
            return r;
        },
    };

    private static LabCalc Percent() => new()
    {
        Id = "percent", Page = "solutions", Title = "Percent solution",
        Help = "% w/v is grams of solute per 100 mL of solution; % v/v is millilitres of solute per 100 mL.",
        Fields = new LabField[]
        {
            new("pct", "Percent", "%", LabDim.Number, "2", Check: LabCheck.Positive),
            new("g", "Solute mass", "solute", LabDim.Mass, "2", "g"),
            new("sv", "Solute volume", "solute", LabDim.Volume, "5", "mL"),
            new("vol", "Solution volume", "solution", LabDim.Volume, "100", "mL"),
        },
        Modes = new LabMode[]
        {
            new("wv", "% w/v", "% w/v = g solute ÷ mL solution × 100", new[] { "g", "vol" }),
            new("wvMass", "Grams (w/v)", "g solute = % × mL solution ÷ 100", new[] { "pct", "vol" }, "g"),
            new("vv", "% v/v", "% v/v = mL solute ÷ mL solution × 100", new[] { "sv", "vol" }),
            new("vvVolume", "mL (v/v)", "mL solute = % × mL solution ÷ 100", new[] { "pct", "vol" }, "sv"),
        },
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            var vol = run.Get("vol", "mL")!.Value;
            var vols = run.In("vol", "mL");
            switch (run.Mode.Id)
            {
                case "wv":
                {
                    var m = run.Get("g", "g")!.Value;
                    var ms = run.In("g", "g");
                    var pct = m / vol * 100;
                    r.Substitution = $"% w/v = {ms} ÷ {vols} × 100 = {P(pct)}%";
                    run.Answer(pct, "%", "% w/v");
                    if (pct > 100) r.Notes.Add("More than 100%: more solute than solution — check the numbers.");
                    break;
                }
                case "wvmass":
                {
                    var pct = run.Get("pct")!.Value;
                    var m = pct * vol / 100;
                    r.Substitution = $"g solute = {P(pct)} × {vols} ÷ 100 = {P(m)} g";
                    run.Answer(m, "g");
                    if (r.Ok) r.Notes.Add($"Dissolve {r.Value} {r.Unit} of solute, then add solvent up to {run.Typed("vol")}.");
                    break;
                }
                case "vv":
                {
                    var sv = run.Get("sv", "mL")!.Value;
                    var svs = run.In("sv", "mL");
                    if (sv > vol) return run.Fail("The solute’s volume is more than the solution’s: it is part of the solution.");
                    var pct = sv / vol * 100;
                    r.Substitution = $"% v/v = {svs} ÷ {vols} × 100 = {P(pct)}%";
                    run.Answer(pct, "%", "% v/v");
                    break;
                }
                default:
                {
                    var pct = run.Get("pct")!.Value;
                    if (pct > 100) return run.Fail("% v/v can’t be more than 100.");
                    var sv = pct * vol / 100;
                    r.Substitution = $"mL solute = {P(pct)} × {vols} ÷ 100 = {P(sv)} mL";
                    run.Answer(sv, "mL");
                    if (r.Ok) r.Notes.Add($"Measure {r.Value} {r.Unit} of solute, then add solvent up to {run.Typed("vol")}.");
                    break;
                }
            }
            return r;
        },
    };

    private static LabCalc Units() => new()
    {
        Id = "convert", Page = "solutions", Title = "Unit conversion",
        Help = "Between M, mM, µM and nM; L, mL and µL; g, mg and µg; lengths and amounts — with the prefixes below.",
        Fields = new LabField[]
        {
            new("cIn", "Concentration", "c", LabDim.Concentration, "250", "µM"),
            new("cOut", "In", "c", LabDim.Concentration, "", "mM"),
            new("vIn", "Volume", "V", LabDim.Volume, "0.5", "mL"),
            new("vOut", "In", "V", LabDim.Volume, "", "µL"),
            new("mIn", "Mass", "m", LabDim.Mass, "0.025", "g"),
            new("mOut", "In", "m", LabDim.Mass, "", "mg"),
            new("lIn", "Length", "l", LabDim.Length, "1.8", "mm"),
            new("lOut", "In", "l", LabDim.Length, "", "µm"),
            new("nIn", "Amount", "n", LabDim.Amount, "0.125", "mol"),
            new("nOut", "In", "n", LabDim.Amount, "", "mmol"),
        },
        Modes = new LabMode[]
        {
            new("conc", "Concentration", "M, mM, µM, nM: each 1,000 times the next", new[] { "cIn" }, "cOut"),
            new("volume", "Volume", "L, mL, µL: each 1,000 times the next", new[] { "vIn" }, "vOut"),
            new("mass", "Mass", "g, mg, µg: each 1,000 times the next", new[] { "mIn" }, "mOut"),
            new("length", "Length", "m, cm, mm, µm, nm: 1 m = 100 cm = 1,000 mm", new[] { "lIn" }, "lOut"),
            new("amount", "Amount", "mol, mmol, µmol, nmol: each 1,000 times the next", new[] { "nIn" }, "nOut"),
        },
        ModeLabel = "Convert",
        Solve = run =>
        {
            var r = run.R;
            var input = run.Mode.Inputs[0];
            var output = run.Mode.Output!;
            var n = run.Number(input);
            if (run.Failed) return r;
            var from = run.Unit(input);
            var to = run.Unit(output);
            var value = to.FromBase(from.ToBase(n!.Value.Value));
            var step = from.Factor / to.Factor;
            var e = Math.Round(Math.Log10(step));
            var per = e >= 0 ? $"1 {from.Symbol} = {Factor(step)} {to.Symbol}" : $"1 {to.Symbol} = {Factor(1 / step)} {from.Symbol}";
            r.Steps.Add(per);
            r.Substitution = $"{run.Typed(input)} × {Factor(step)} = {P(value)} {to.Symbol}";
            r.Symbol = run.Calc.Field(input).Symbol;
            r.Raw = value;
            r.RawBase = to.ToBase(value);
            r.Unit = to.Symbol;
            // (a conversion keeps the figures it was given)
            var figures = n.Value.Exact ? run.Setting : n.Value.Figures;
            r.Value = LabNumbers.Minus(LabNumbers.Sig(value, figures, n.Value.Exact));
            var keeps = $"a conversion keeps the {figures} significant figure{(figures == 1 ? "" : "s")} it was given";
            r.Steps.Add(from.Symbol == to.Symbol ? "The same unit." : P(value) == r.Value ? $"= {r.Value} {to.Symbol} ({keeps})" : $"{P(value)} {to.Symbol} → {r.Value} {to.Symbol} ({keeps})");
            return r;
        },
    };

    // ======== Microscopy ========

    private static LabCalc TotalMagnification() => new()
    {
        Id = "totalMag", Page = "microscopy", Title = "Total magnification",
        Help = "The eyepiece (ocular) lens times the objective lens.",
        Fields = new LabField[]
        {
            new("ocular", "Ocular (eyepiece) lens", "ocular", LabDim.Number, "10", Exact: true, Hint: "Usually 10×.",
                Presets: new LabPreset[] { new("10×", "10"), new("15×", "15") }),
            new("objective", "Objective lens", "objective", LabDim.Number, "40", Exact: true, Hint: "4×, 10×, 40× or 100× (oil).",
                Presets: new LabPreset[] { new("4× scanning", "4"), new("10× low power", "10"), new("40× high power", "40"), new("100× oil immersion", "100") }),
        },
        Modes = new LabMode[] { new("total", "Total", "total magnification = ocular × objective", new[] { "ocular", "objective" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (o, b) = (run.Get("ocular"), run.Get("objective"));
            if (run.Failed) return r;
            var t = o!.Value * b!.Value;
            r.Substitution = $"total = {P(o.Value)}× × {P(b.Value)}× = {P(t)}×";
            run.Answer(t, "×", "total");
            return r;
        },
    };

    private static LabCalc FieldOfView() => new()
    {
        Id = "fov", Page = "microscopy", Title = "Field of view",
        Help = "The circle you see shrinks as the magnification grows, in the same proportion.",
        Fields = new LabField[]
        {
            new("fov1", "Field of view now", "FOV old", LabDim.Length, "1.8", "mm"),
            new("mag1", "Magnification now", "mag old", LabDim.Number, "100", Exact: true),
            new("mag2", "New magnification", "mag new", LabDim.Number, "400", Exact: true),
            new("fov2", "New field of view", "FOV new", LabDim.Length, "", "mm"),
        },
        Modes = new LabMode[] { new("fov", "FOV", "FOV new = FOV old × (mag old ÷ mag new)", new[] { "fov1", "mag1", "mag2" }, "fov2") },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (f1, m1, m2) = (run.Get("fov1"), run.Get("mag1"), run.Get("mag2"));
            if (run.Failed) return r;
            var u = run.Unit("fov1").Symbol;
            var f1u = run.Get("fov1", u)!.Value;
            var f2 = f1u * m1!.Value / m2!.Value;
            r.Substitution = $"FOV new = {run.Typed("fov1")} × ({P(m1.Value)}× ÷ {P(m2.Value)}×) = {P(f2)} {u}";
            run.Answer(f2, u, "FOV");
            if (r.Ok) r.Notes.Add(m2.Value > m1.Value ? "Higher magnification: a smaller circle, so fewer cells fit across." : "Lower magnification: a bigger circle, so more fits across.");
            return r;
        },
    };

    private static LabCalc SpecimenSize() => new()
    {
        Id = "specimen", Page = "microscopy", Title = "Specimen size",
        Help = "Estimate a cell's size from how many fit across the field of view.",
        Fields = new LabField[]
        {
            new("fov", "Field of view", "FOV", LabDim.Length, "450", "µm"),
            new("count", "Number that fit across", "number", LabDim.Number, "5", Exact: true),
            new("size", "Size of one", "size", LabDim.Length, "", "µm"),
        },
        Modes = new LabMode[] { new("specimen", "Size", "size ≈ field of view ÷ number that fit across", new[] { "fov", "count" }, "size") },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (fov, n) = (run.Get("fov"), run.Get("count"));
            if (run.Failed) return r;
            var u = run.Unit("fov").Symbol;
            var size = run.Get("fov", u)!.Value / n!.Value;
            r.Substitution = $"size ≈ {run.Typed("fov")} ÷ {P(n.Value)} = {P(size)} {u}";
            run.Answer(size, u);
            return r;
        },
    };

    private static LabCalc Magnification() => new()
    {
        Id = "magnification", Page = "microscopy", Title = "Magnification",
        Help = "Magnification = image size ÷ actual size, both in the same units. Solve for any one.",
        Fields = new LabField[]
        {
            new("image", "Image size", "image", LabDim.Length, "45", "mm", Hint: "Measured on the drawing or photo."),
            new("actual", "Actual size", "actual", LabDim.Length, "15", "µm"),
            new("mag", "Magnification", "M", LabDim.Number, "3000"),
        },
        Modes = new LabMode[]
        {
            new("mag", "Magnification", "M = image size ÷ actual size", new[] { "image", "actual" }),
            new("actual", "Actual size", "actual size = image size ÷ M", new[] { "image", "mag" }, "actual"),
            new("image", "Image size", "image size = M × actual size", new[] { "mag", "actual" }, "image"),
        },
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            switch (run.Mode.Id)
            {
                case "mag":
                {
                    var u = run.Unit("actual").Symbol;
                    string im = run.In("image", u), ac = run.Typed("actual");
                    var m = run.Get("image", u)!.Value / run.Get("actual", u)!.Value;
                    r.Substitution = $"M = {im} ÷ {ac} = {P(m)}×";
                    run.Answer(m, "×", "M");
                    if (r.Ok && m < 1) r.Notes.Add("Less than 1×: the image is smaller than the thing itself.");
                    break;
                }
                case "actual":
                {
                    var u = run.Unit("image").Symbol;
                    var m = run.Get("mag")!.Value;
                    var a = run.Get("image", u)!.Value / m;
                    r.Substitution = $"actual = {run.Typed("image")} ÷ {P(m)} = {P(a)} {u}";
                    run.Answer(a, u, "actual");
                    break;
                }
                default:
                {
                    var u = run.Unit("actual").Symbol;
                    var m = run.Get("mag")!.Value;
                    var im = m * run.Get("actual", u)!.Value;
                    r.Substitution = $"image = {P(m)} × {run.Typed("actual")} = {P(im)} {u}";
                    run.Answer(im, u, "image");
                    break;
                }
            }
            return r;
        },
    };

    private static LabCalc ScaleBar() => new()
    {
        Id = "scaleBar", Page = "microscopy", Title = "Scale bar",
        Help = "How long to draw a scale bar on an image of known magnification.",
        Fields = new LabField[]
        {
            new("mag", "Magnification", "M", LabDim.Number, "3000"),
            new("bar", "The bar stands for", "real length", LabDim.Length, "10", "µm", Exact: true),
            new("barImage", "Draw it", "bar", LabDim.Length, "", "mm"),
        },
        Modes = new LabMode[] { new("scaleBar", "Bar", "bar length on the image = real length × M", new[] { "mag", "bar" }, "barImage") },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (m, real) = (run.Get("mag"), run.Get("bar"));
            if (run.Failed) return r;
            var u = run.Unit("bar").Symbol;
            var bar = run.Get("bar", u)!.Value * m!.Value;
            r.Substitution = $"bar = {run.Typed("bar")} × {P(m.Value)} = {P(bar)} {u}";
            run.Answer(bar, u, "bar");
            // (a bar of a round length that draws 20–50 mm long at this magnification)
            var best = (Real: 0.0, Mm: 0.0);
            for (var e = -9; e <= -1; e++)
                foreach (var s in new[] { 1.0, 2.0, 5.0 })
                {
                    var realM = s * Math.Pow(10, e);
                    var mm = realM * m.Value * 1000;
                    if (mm >= 20 && mm <= 50 && (best.Real == 0 || Math.Abs(mm - 30) < Math.Abs(best.Mm - 30))) best = (realM, mm);
                }
            if (best.Real > 0)
            {
                var nice = LabUnits.Nice(LabDim.Length, best.Real) ?? LabUnits.Find(LabDim.Length, "µm");
                r.Notes.Add($"A bar about 20–50 mm long reads well: at {P(m.Value)}×, {P(nice.FromBase(best.Real))} {nice.Symbol} draws {P(best.Mm)} mm.");
            }
            r.Extra = new LabScaleBar(LabUnits.Find(LabDim.Length, u).ToBase(bar) * 1000, run.Typed("bar"));
            return r;
        },
    };

    // ======== Cells ========

    private static LabCalc SurfaceVolume() => new()
    {
        Id = "sav", Page = "cells", Title = "Surface area to volume",
        Help = "SA, V and SA:V for the AP sheet's shapes. Bigger cells have less surface for their volume.",
        Fields = new LabField[]
        {
            new("r", "Radius", "r", LabDim.Length, "1", "µm"),
            new("s", "Side", "s", LabDim.Length, "2", "µm"),
            new("l", "Length", "l", LabDim.Length, "3", "µm"),
            new("w", "Width", "w", LabDim.Length, "2", "µm"),
            new("h", "Height", "h", LabDim.Length, "1", "µm"),
        },
        Modes = new LabMode[]
        {
            new("sphere", "Sphere", "SA = 4πr²   V = 4/3 πr³", new[] { "r" }),
            new("cube", "Cube", "SA = 6s²   V = s³", new[] { "s" }),
            new("rect", "Rectangular solid", "SA = 2lh + 2lw + 2wh   V = lwh", new[] { "l", "w", "h" }),
            new("cylinder", "Cylinder", "SA = 2πrh + 2πr²   V = πr²h", new[] { "r", "h" }),
        },
        ModeLabel = "Shape",
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            // (every length in the first field's unit)
            var u = run.Unit(run.Mode.Inputs[0]).Symbol;
            double L(string id) => run.Get(id, u)!.Value;
            string T(string id) => run.In(id, u);
            double sa, v;
            string sas, vs;
            switch (run.Mode.Id)
            {
                case "sphere":
                {
                    var x = L("r");
                    sa = 4 * Math.PI * x * x;
                    v = 4.0 / 3 * Math.PI * x * x * x;
                    sas = $"SA = 4π({T("r")})² = {P(sa)} {u}²";
                    vs = $"V = 4/3 π({T("r")})³ = {P(v)} {u}³";
                    break;
                }
                case "cube":
                {
                    var x = L("s");
                    sa = 6 * x * x;
                    v = x * x * x;
                    sas = $"SA = 6({T("s")})² = {P(sa)} {u}²";
                    vs = $"V = ({T("s")})³ = {P(v)} {u}³";
                    break;
                }
                case "rect":
                {
                    double l = L("l"), w = L("w"), h = L("h");
                    sa = 2 * l * h + 2 * l * w + 2 * w * h;
                    v = l * w * h;
                    sas = $"SA = 2({T("l")})({T("h")}) + 2({T("l")})({T("w")}) + 2({T("w")})({T("h")}) = {P(sa)} {u}²";
                    vs = $"V = ({T("l")})({T("w")})({T("h")}) = {P(v)} {u}³";
                    break;
                }
                default:
                {
                    double x = L("r"), h = L("h");
                    sa = 2 * Math.PI * x * h + 2 * Math.PI * x * x;
                    v = Math.PI * x * x * h;
                    sas = $"SA = 2π({T("r")})({T("h")}) + 2π({T("r")})² = {P(sa)} {u}²";
                    vs = $"V = π({T("r")})²({T("h")}) = {P(v)} {u}³";
                    break;
                }
            }
            var ratio = sa / v;
            r.Steps.Add(sas);
            r.Steps.Add(vs);
            r.Steps.Add($"SA:V = {P(sa)} ÷ {P(v)} = {P(ratio)} per {u}");
            r.Substitution = $"SA:V = {P(sa)} {u}² ÷ {P(v)} {u}³ = {P(ratio)} {u}⁻¹";
            r.Symbol = "SA:V";
            r.Raw = ratio;
            r.RawBase = ratio;
            r.Unit = $"{u}⁻¹";
            r.Value = run.Format(ratio);
            r.Steps.Add(run.Rounding(ratio, $"{u}⁻¹"));
            r.Rows.Add(new LabRow("Surface area", $"{run.Format(sa)} {u}²"));
            r.Rows.Add(new LabRow("Volume", $"{run.Format(v)} {u}³"));
            // (SA:V against size: every length scaled together, so SA:V falls as 1 / size)
            var size0 = L(run.Mode.Inputs[0]);
            var pts = new List<(double, double)>();
            for (var i = 1; i <= 60; i++)
            {
                var x = size0 * 4 * i / 60.0;
                pts.Add((x, ratio * size0 / x));
            }
            var sym = run.Calc.Field(run.Mode.Inputs[0]).Symbol;
            r.Extra = new LabCurve($"{sym} ({u}){(run.Mode.Inputs.Count > 1 ? ", every length scaled with it" : "")}", $"SA:V ({u}⁻¹)",
                new[] { new LabSeries("SA:V", pts) }, (size0, ratio, $"{r.Value}"));
            r.Notes.Add("Double the size and SA:V halves: a big cell exchanges less with its surroundings for its volume.");
            return r;
        },
    };

    private static LabCalc WaterPotential() => new()
    {
        Id = "water", Page = "cells", Title = "Water potential",
        Help = "Ψ = ΨP + ΨS, and the solute potential ΨS = −iCRT. Water moves from higher Ψ to lower Ψ.",
        Fields = new LabField[]
        {
            new("i", "Ionization constant", "i", LabDim.Number, "1", Exact: true, Hint: "1 for sucrose, 2 for NaCl, 3 for CaCl₂.",
                Presets: new LabPreset[] { new("Sucrose or glucose: 1", "1"), new("NaCl or KCl: 2", "2"), new("CaCl₂: 3", "3") }),
            new("C", "Molar concentration", "C", LabDim.Concentration, "0.10", "M"),
            new("T", "Temperature", "T", LabDim.Temperature, "22", "°C", Check: LabCheck.Temperature),
            new("P", "Pressure potential", "ΨP", LabDim.Pressure, "0", "bar", Check: LabCheck.Any, Hint: "0 in an open beaker."),
            new("out", "Ψ of the surroundings (optional)", "Ψ outside", LabDim.Pressure, "", "bar", Check: LabCheck.Any, Optional: true,
                Hint: "Say which way water moves: from higher Ψ to lower."),
        },
        Modes = new LabMode[] { new("water", "Ψ", "ΨS = −iCRT   Ψ = ΨP + ΨS", new[] { "i", "C", "T", "P", "out" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (i, c, t, pp) = (run.Get("i"), run.Get("C", "M"), run.Get("T", "K"), run.Get("P", "bar"));
            if (run.Failed) return r;
            string cs = run.In("C", "M", "mol/L"), ts = run.In("T", "K"), pps = run.In("P", "bar");
            var ps = -i!.Value * c!.Value * GasConstant * t!.Value;
            var psi = pp!.Value + ps;
            r.Steps.Add($"ΨS = −iCRT = −({P(i.Value)})({cs})(0.0831 L·bar/(mol·K))({ts}) = {P(ps)} bar");
            r.Steps.Add($"Ψ = ΨP + ΨS = {pps} + ({P(ps)} bar) = {P(psi)} bar");
            r.Substitution = $"ΨS = −({P(i.Value)})({cs})(0.0831)({ts}) = {P(ps)} bar";
            r.Symbol = "Ψ";
            r.Raw = psi;
            r.RawBase = psi;
            r.Unit = "bar";
            r.Value = run.Format(psi);
            r.Rows.Add(new LabRow("Solute potential ΨS", $"{run.Format(ps)} bar", Strong: true));
            r.Rows.Add(new LabRow("Pressure potential ΨP", $"{LabNumbers.Pretty(run.Text("P"))} {run.Unit("P").Symbol}"));
            r.Steps.Add(run.Rounding(psi, "bar"));
            if (run.Optional("out", "bar") is { } outside)
            {
                var o = run.Typed("out");
                r.Rows.Add(new LabRow("Ψ of the surroundings", o));
                r.Notes.Add(Math.Abs(outside - psi) < 1e-9 ? $"Ψ is the same inside and out ({o}): no net movement of water."
                    : outside > psi ? $"The surroundings’ Ψ ({o}) is higher, so water moves in, from higher Ψ to lower."
                    : $"The surroundings’ Ψ ({o}) is lower, so water moves out, from higher Ψ to lower.");
            }
            r.Notes.Add("R = 0.0831 L·bar/(mol·K); T in kelvin is °C + 273. Pure water is 0 bar; adding solute makes Ψ more negative.");
            return r;
        },
    };

    private static LabCalc Hemocytometer() => new()
    {
        Id = "hemocytometer", Page = "cells", Title = "Hemocytometer",
        Help = "Cells per mL from the counts in the large squares (each holds 10⁻⁴ mL).",
        Fields = new LabField[]
        {
            new("counts", "Cells in each large square", "counts", LabDim.Number, "40, 44, 42, 42", Check: LabCheck.Counts, Exact: true, Hint: "One count per square, separated by commas."),
            new("dil", "Dilution factor", "dilution", LabDim.Number, "2", Exact: true, Hint: "2 for equal volumes of cells and trypan blue."),
        },
        Modes = new LabMode[] { new("hemocytometer", "Cells/mL", "cells/mL = mean count per large square × dilution factor × 10⁴", new[] { "counts", "dil" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var counts = run.List("counts");
            var dil = run.Get("dil");
            if (run.Failed || counts == null) return r;
            var mean = counts.Average(n => n.Value);
            r.Steps.Add(counts.Count == 1
                ? $"mean = {P(mean)} per large square"
                : $"mean = ({string.Join(" + ", counts.Select(n => P(n.Value)))}) ÷ {counts.Count} = {P(mean)} per large square");
            var cells = mean * dil!.Value * 1e4;
            r.Steps.Add("each large square is 1 mm × 1 mm × 0.1 mm = 0.1 mm³ = 10⁻⁴ mL, so × 10⁴ gives per mL");
            r.Substitution = $"cell density = {P(mean)} × {P(dil.Value)} × 10⁴ = {P(cells)} cells/mL";
            run.Answer(cells, "cells/mL", "cell density");
            return r;
        },
    };

    // ======== Statistics ========

    private static LabCalc Descriptive() => new()
    {
        Id = "stats", Page = "stats", Title = "Mean, standard deviation and error bars",
        Help = "Type each group's data. Error bars are x̄ ± 2 SE, about 95% confidence (the AP sheet's).",
        Fields = new LabField[]
        {
            new("g1", "Group 1", "group 1", LabDim.Number, "12.1, 13.4, 11.8, 12.9, 13.0", Check: LabCheck.List),
            new("g2", "Group 2 (optional)", "group 2", LabDim.Number, "", Check: LabCheck.List),
            new("g3", "Group 3 (optional)", "group 3", LabDim.Number, "", Check: LabCheck.List),
        },
        Modes = new LabMode[] { new("stats", "Statistics", "x̄ = Σx ÷ n   s = √(Σ(x − x̄)² ÷ (n − 1))   SE = s ÷ √n", new[] { "g1", "g2", "g3" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var groups = new List<(string Name, List<LabNumber> Data)>();
            foreach (var id in new[] { "g1", "g2", "g3" })
            {
                if (id != "g1" && string.IsNullOrWhiteSpace(run.Text(id))) continue;
                if (run.List(id) is { } d) groups.Add(($"Group {id[1]}", d));
            }
            if (run.Failed) return r;
            var bars = new List<LabBar>();
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var (name, data) = groups[gi];
                var s = Stats(data.Select(n => n.Value).ToList());
                // (the mean, median, range and interval one decimal place past the data; s and SE to the figures allowed)
                var dp = Math.Max(0, data.Max(n => n.Decimals)) + 1;
                var sf = LabNumbers.Allowed(run.Setting, data);
                string F(double v) => LabNumbers.Fixed(v, dp);
                string G(double v) => double.IsNaN(v) ? "—" : LabNumbers.Minus(LabNumbers.Sig(v, sf, data.All(n => n.Exact)));
                var prefix = groups.Count > 1 ? $"{name}: " : "";
                if (gi == 0)
                {
                    r.Symbol = "x̄";
                    r.Value = F(s.Mean);
                    r.Raw = s.Mean;
                    r.RawBase = s.Mean;
                    r.Substitution = $"x̄ = {P(s.Sum)} ÷ {s.N} = {P(s.Mean)}";
                }
                r.Rows.Add(new LabRow($"{prefix}n", $"{s.N}"));
                r.Rows.Add(new LabRow($"{prefix}Mean x̄", F(s.Mean), Strong: true));
                r.Rows.Add(new LabRow($"{prefix}Median", F(s.Median)));
                r.Rows.Add(new LabRow($"{prefix}Mode", s.Modes.Count == 0 ? "none (no value repeats)" : string.Join(", ", s.Modes.Select(m => LabNumbers.Fixed(m, dp - 1)))));
                r.Rows.Add(new LabRow($"{prefix}Range", $"{F(s.Range)} ({LabNumbers.Fixed(s.Min, dp - 1)} to {LabNumbers.Fixed(s.Max, dp - 1)})"));
                r.Rows.Add(new LabRow($"{prefix}Standard deviation s", G(s.Sd), Strong: true));
                r.Rows.Add(new LabRow($"{prefix}Standard error SE", G(s.Se), Strong: true));
                r.Rows.Add(new LabRow($"{prefix}x̄ ± 2 SE", s.N < 2 ? "—" : $"{F(s.Mean - 2 * s.Se)} to {F(s.Mean + 2 * s.Se)}", Strong: true));
                var head = groups.Count > 1 ? $"{name}: " : "";
                r.Steps.Add($"{head}Σx = {P(s.Sum)}, n = {s.N}, x̄ = {P(s.Sum)} ÷ {s.N} = {P(s.Mean)}");
                if (s.N >= 2)
                {
                    r.Steps.Add($"{head}Σ(x − x̄)² = {string.Join(" + ", data.Select(n => P((n.Value - s.Mean) * (n.Value - s.Mean))))} = {P(s.SumSq)}");
                    r.Steps.Add($"{head}s = √({P(s.SumSq)} ÷ {s.N - 1}) = √{P(s.SumSq / (s.N - 1))} = {P(s.Sd)}");
                    r.Steps.Add($"{head}SE = s ÷ √n = {P(s.Sd)} ÷ √{s.N} = {P(s.Se)};  2 SE = {P(2 * s.Se)}");
                    r.Steps.Add($"{head}x̄ ± 2 SE = {P(s.Mean)} ± {P(2 * s.Se)} = {P(s.Mean - 2 * s.Se)} to {P(s.Mean + 2 * s.Se)} → {F(s.Mean - 2 * s.Se)} to {F(s.Mean + 2 * s.Se)}");
                    bars.Add(new LabBar(name, s.Mean, s.Mean - 2 * s.Se, s.Mean + 2 * s.Se));
                }
                else r.Steps.Add($"{head}one value: no standard deviation (it needs n − 1 ≥ 1).");
            }
            r.Steps.Add($"The mean, median and interval are written one decimal place past the data; s and SE to {LabNumbers.Allowed(run.Setting, groups.SelectMany(g => g.Data))} significant figures.");
            r.Unit = "";
            r.Extra = bars;
            if (bars.Count >= 2)
            {
                for (var a = 0; a < bars.Count; a++)
                    for (var b = a + 1; b < bars.Count; b++)
                    {
                        var overlap = bars[a].High >= bars[b].Low && bars[b].High >= bars[a].Low;
                        r.Notes.Add(overlap
                            ? $"{bars[a].Name} and {bars[b].Name}: the bars overlap, so the difference may not be significant."
                            : $"{bars[a].Name} and {bars[b].Name}: the bars don’t overlap, so the difference is likely significant.");
                    }
            }
            else if (groups[0].Data.Count < 2) r.Notes.Add("Type two or more values for the standard deviation and error bars.");
            return r;
        },
    };

    /// <summary>A data set's statistics: n, Σx, mean, median, modes, range, Σ(x − x̄)², s (n − 1) and SE.</summary>
    public sealed record LabStats(int N, double Sum, double Mean, double Median, IReadOnlyList<double> Modes, double Min, double Max, double SumSq, double Sd, double Se)
    {
        public double Range => Max - Min;
    }

    public static LabStats Stats(IReadOnlyList<double> x)
    {
        var n = x.Count;
        var sum = x.Sum();
        var mean = sum / n;
        var sorted = x.OrderBy(v => v).ToList();
        var median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        var groups = x.GroupBy(v => v).Select(g => (g.Key, Count: g.Count())).ToList();
        var top = groups.Max(g => g.Count);
        var modes = top < 2 ? new List<double>() : groups.Where(g => g.Count == top).Select(g => g.Key).OrderBy(v => v).ToList();
        var ss = x.Sum(v => (v - mean) * (v - mean));
        var sd = n >= 2 ? Math.Sqrt(ss / (n - 1)) : double.NaN;
        return new LabStats(n, sum, mean, median, modes, sorted[0], sorted[^1], ss, sd, n >= 2 ? sd / Math.Sqrt(n) : double.NaN);
    }

    private static LabCalc ChiSquare() => new()
    {
        Id = "chisq", Page = "stats", Title = "Chi-square",
        Help = "Observed counts against the counts a hypothesis expects. df = categories − 1.",
        Fields = new LabField[]
        {
            new("o", "Observed counts", "o", LabDim.Number, "315, 108, 101, 32", Check: LabCheck.Counts, Exact: true),
            new("ratio", "Expected ratio", "ratio", LabDim.Number, "9:3:3:1", Check: LabCheck.List, Exact: true, Hint: "9:3:3:1, 3:1, 1:2:1 …"),
            new("e", "Expected counts", "e", LabDim.Number, "312.75, 104.25, 104.25, 34.75", Check: LabCheck.List, Exact: true),
        },
        Modes = new LabMode[]
        {
            new("ratio", "From a ratio", "χ² = Σ (o − e)² ÷ e", new[] { "o", "ratio" }),
            new("counts", "From expected counts", "χ² = Σ (o − e)² ÷ e", new[] { "o", "e" }),
        },
        ModeLabel = "Expected",
        Solve = run =>
        {
            var r = run.R;
            var obs = run.List("o");
            var other = run.List(run.Mode.Id == "ratio" ? "ratio" : "e");
            if (run.Failed) return r;
            var o = obs!.Select(n => n.Value).ToArray();
            if (o.Length < 2) return run.Fail("Chi-square needs two or more categories.");
            if (other!.Count != o.Length) { r.FieldErrors[run.Mode.Id == "ratio" ? "ratio" : "e"] = $"{o.Length} observed counts need {o.Length} expected values (there are {other.Count})."; return r; }
            if (other.Any(n => n.Value <= 0)) { r.FieldErrors[run.Mode.Id == "ratio" ? "ratio" : "e"] = "Each expected value must be more than 0."; return r; }
            var total = o.Sum();
            double[] e;
            if (run.Mode.Id == "ratio")
            {
                e = BioChiSquare.Expected(other.Select(n => n.Value).ToList(), total);
                r.Steps.Add($"total = {P(total)}; expected = total × each part ÷ {P(other.Sum(n => n.Value))}: {string.Join(", ", e.Select(P))}");
            }
            else e = other.Select(n => n.Value).ToArray();
            var t = BioChiSquare.Test(o, e);
            for (var i = 0; i < o.Length; i++) r.Steps.Add($"({P(o[i])} − {P(e[i])})² ÷ {P(e[i])} = {P(t.Terms[i])}");
            r.Substitution = $"χ² = {string.Join(" + ", t.Terms.Select(P))} = {P(t.Chi2)}";
            r.Symbol = "χ²";
            r.Raw = t.Chi2;
            r.RawBase = t.Chi2;
            r.Value = LabNumbers.Fixed(t.Chi2, 2);
            r.Rows.Add(new LabRow("Degrees of freedom", $"{t.Df} (categories − 1)"));
            r.Rows.Add(new LabRow("Critical value (p = 0.05)", LabNumbers.Fixed(t.Critical, 2)));
            r.Rows.Add(new LabRow("p", t.P < 0.001 ? "< 0.001" : LabNumbers.Fixed(t.P, 3)));
            r.Rows.Add(new LabRow("Conclusion", t.Conclusion, Strong: true));
            r.Steps.Add($"df = {o.Length} − 1 = {t.Df}; the critical value at p = 0.05 is {LabNumbers.Fixed(t.Critical, 2)}");
            r.Steps.Add(t.Conclusion);
            r.Extra = new LabTable(new[] { "Category", "Observed", "Expected", "(o − e)² ÷ e" },
                Enumerable.Range(0, o.Length).Select(i => (IReadOnlyList<string>)new[] { $"{i + 1}", P(o[i]), LabNumbers.Fixed(e[i], 2), LabNumbers.Fixed(t.Terms[i], 3) }).ToList());
            r.Notes.Add("Rejecting the null hypothesis means the difference is unlikely to be chance alone; failing to reject it doesn’t prove it.");
            return r;
        },
    };

    // ======== Populations ========

    private static LabCalc Rate() => new()
    {
        Id = "rate", Page = "ecology", Title = "Rate",
        Help = "How fast something changes: its change over the time it took.",
        Fields = new LabField[]
        {
            new("y1", "Start value", "Y₁", LabDim.Label, "10", "mL", Check: LabCheck.Any),
            new("y2", "End value", "Y₂", LabDim.Label, "50", "mL", Check: LabCheck.Any),
            new("t1", "Start time", "t₁", LabDim.Time, "0", "min", Check: LabCheck.NonNegative),
            new("t2", "End time", "t₂", LabDim.Time, "8", "min", Check: LabCheck.NonNegative),
        },
        Modes = new LabMode[] { new("rate", "Rate", "rate = ΔY ÷ Δt = (Y₂ − Y₁) ÷ (t₂ − t₁)", new[] { "y1", "y2", "t1", "t2" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (y1, y2) = (run.Get("y1"), run.Get("y2"));
            var tu = run.Unit("t2").Symbol;
            var (t1, t2) = (run.Get("t1", tu), run.Get("t2", tu));
            if (run.Failed) return r;
            if (t2 == t1) { r.FieldErrors["t2"] = "The end time must differ from the start time."; return r; }
            var yu = run.Unit("y1").Symbol;
            string t1s = run.In("t1", tu), t2s = run.In("t2", tu);
            var dy = y2!.Value - y1!.Value;
            var dt = t2!.Value - t1!.Value;
            var rate = dy / dt;
            r.Steps.Add($"ΔY = {LabNumbers.Pretty(run.Text("y2"))} − {LabNumbers.Pretty(run.Text("y1"))} = {P(dy)} {yu}");
            r.Steps.Add($"Δt = {t2s} − {t1s} = {P(dt)} {tu}");
            r.Substitution = $"rate = {P(dy)} {yu} ÷ {P(dt)} {tu} = {P(rate)} {yu}/{tu}";
            run.Answer(rate, $"{yu}/{tu}", "rate");
            if (r.Ok && rate < 0) r.Notes.Add("A negative rate: the value fell over the time.");
            return r;
        },
    };

    private static LabCalc Growth() => new()
    {
        Id = "growth", Page = "ecology", Title = "Population growth",
        Help = "Exponential growth has no limit; logistic growth slows as N nears the carrying capacity K.",
        Fields = new LabField[]
        {
            new("r", "Maximum growth rate", "r max", LabDim.PerTime, "0.5", "year", Hint: "Per individual, per unit of time."),
            new("N", "Population size", "N", LabDim.Number, "100", Exact: true),
            new("K", "Carrying capacity", "K", LabDim.Number, "1000", Exact: true),
        },
        Modes = new LabMode[]
        {
            new("exponential", "Exponential", "dN/dt = r max × N", new[] { "r", "N" }),
            new("logistic", "Logistic", "dN/dt = r max × N × (K − N) ÷ K", new[] { "r", "N", "K" }),
        },
        ModeLabel = "Model",
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            var rmax = run.Get("r")!.Value;
            var n = run.Get("N")!.Value;
            var per = run.Unit("r").Symbol;
            double rate;
            double? k = null;
            if (run.Mode.Id == "exponential")
            {
                rate = rmax * n;
                r.Substitution = $"dN/dt = {run.T("r")} × {P(n)} = {P(rate)} individuals per {per}";
            }
            else
            {
                k = run.Get("K")!.Value;
                rate = rmax * n * (k.Value - n) / k.Value;
                r.Steps.Add($"(K − N) ÷ K = ({P(k.Value)} − {P(n)}) ÷ {P(k.Value)} = {P((k.Value - n) / k.Value)}");
                r.Substitution = $"dN/dt = {run.T("r")} × {P(n)} × ({P(k.Value)} − {P(n)}) ÷ {P(k.Value)} = {P(rate)} individuals per {per}";
                if (n > k) r.Notes.Add("N is above K: the population shrinks back toward its carrying capacity.");
            }
            run.Answer(rate, $"individuals per {per}", "dN/dt");
            // (N over time from now, both models, so the two can be compared)
            var kk = k ?? 10 * n;
            var tEnd = rmax > 0 ? Math.Max(1, Math.Log(Math.Max(kk / Math.Max(n, 1e-9), 1.0001) * 9) / rmax * 1.3) : 10;
            var expPts = new List<(double, double)>();
            var logPts = new List<(double, double)>();
            for (var i = 0; i <= 80; i++)
            {
                var t = tEnd * i / 80;
                var ne = n * Math.Exp(rmax * t);
                if (ne <= kk * 1.6) expPts.Add((t, ne));
                logPts.Add((t, kk / (1 + (kk - n) / Math.Max(n, 1e-9) * Math.Exp(-rmax * t))));
            }
            r.Extra = new LabCurve($"time ({per}s)", "N", new[]
            {
                new LabSeries("Logistic", logPts, Dashed: run.Mode.Id == "exponential", Tone: run.Mode.Id == "exponential" ? 1 : 0),
                new LabSeries("Exponential", expPts, Dashed: run.Mode.Id != "exponential", Tone: run.Mode.Id == "exponential" ? 0 : 1),
            }, (0, n, $"N = {P(n)}"), kk, k == null ? $"K (for the logistic curve: {P(kk)})" : "K");
            return r;
        },
    };

    private static LabCalc Simpson() => new()
    {
        Id = "simpson", Page = "ecology", Title = "Simpson’s diversity index",
        Help = "D = 1 − Σ(n/N)², the AP sheet's form: 0 is one species only, nearer 1 is more diverse.",
        Fields = new LabField[]
        {
            new("counts", "Individuals of each species", "n", LabDim.Number, "10, 6, 4", Check: LabCheck.Counts, Exact: true, Hint: "One count per species, separated by commas."),
        },
        Modes = new LabMode[] { new("simpson", "D", "D = 1 − Σ(n ÷ N)²", new[] { "counts" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var counts = run.List("counts");
            if (run.Failed) return r;
            var n = counts!.Select(c => c.Value).ToList();
            var total = n.Sum();
            if (total <= 0) return run.Fail("Count at least one individual.");
            var terms = n.Select(x => (x / total) * (x / total)).ToList();
            var sum = terms.Sum();
            var d = 1 - sum;
            r.Steps.Add($"N = {string.Join(" + ", n.Select(P))} = {P(total)}");
            r.Steps.Add($"Σ(n/N)² = {string.Join(" + ", n.Select(x => $"({P(x)}/{P(total)})²"))} = {string.Join(" + ", terms.Select(P))} = {P(sum)}");
            r.Substitution = $"D = 1 − {P(sum)} = {P(d)}";
            run.Answer(d, "", "D");
            r.Rows.Add(new LabRow("Species", $"{n.Count(x => x > 0)}"));
            r.Rows.Add(new LabRow("Individuals N", P(total)));
            return r;
        },
    };

    // ======== Molecules ========

    private static LabCalc BeerLambert() => new()
    {
        Id = "beer", Page = "molecules", Title = "Beer–Lambert law",
        Help = "Absorbance is proportional to concentration: A = ε l c. Solve for any one.",
        Fields = new LabField[]
        {
            new("A", "Absorbance", "A", LabDim.Number, "0.500", Check: LabCheck.NonNegative),
            new("eps", "Molar absorptivity", "ε", LabDim.Absorptivity, "6220", "M⁻¹cm⁻¹", Hint: "NADH at 340 nm: 6,220 M⁻¹cm⁻¹.",
                Presets: new LabPreset[] { new("NADH at 340 nm: 6,220", "6220") }),
            new("l", "Path length", "l", LabDim.PathLength, "1.00", "cm"),
            new("c", "Concentration", "c", LabDim.Concentration, "8.04 × 10^-5", "M"),
        },
        Modes = new LabMode[]
        {
            new("c", "c", "c = A ÷ (ε l)", new[] { "A", "eps", "l" }, "c"),
            new("A", "A", "A = ε l c", new[] { "eps", "l", "c" }),
            new("eps", "ε", "ε = A ÷ (l c)", new[] { "A", "l", "c" }, "eps"),
            new("l", "l", "l = A ÷ (ε c)", new[] { "A", "eps", "c" }, "l"),
        },
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            string T(string id, string u) => run.In(id, u);
            double V(string id, string u) => run.Get(id, u)!.Value;
            switch (run.Mode.Id)
            {
                case "c":
                {
                    var c = V("A", "") / (V("eps", "M⁻¹cm⁻¹") * V("l", "cm"));
                    r.Substitution = $"c = {LabNumbers.Pretty(run.Text("A"))} ÷ ({T("eps", "M⁻¹cm⁻¹")} × {T("l", "cm")}) = {P(c)} M";
                    run.Answer(c, "M");
                    break;
                }
                case "A":
                {
                    var a = V("eps", "M⁻¹cm⁻¹") * V("l", "cm") * V("c", "M");
                    r.Substitution = $"A = {T("eps", "M⁻¹cm⁻¹")} × {T("l", "cm")} × {T("c", "M")} = {P(a)}";
                    run.Answer(a, "", "A");
                    if (r.Ok && a > 2) r.Notes.Add("Above about 2, a spectrophotometer reads poorly: dilute the sample.");
                    break;
                }
                case "eps":
                {
                    var eps = V("A", "") / (V("l", "cm") * V("c", "M"));
                    r.Substitution = $"ε = {LabNumbers.Pretty(run.Text("A"))} ÷ ({T("l", "cm")} × {T("c", "M")}) = {P(eps)} M⁻¹cm⁻¹";
                    run.Answer(eps, "M⁻¹cm⁻¹");
                    break;
                }
                default:
                {
                    var l = V("A", "") / (V("eps", "M⁻¹cm⁻¹") * V("c", "M"));
                    r.Substitution = $"l = {LabNumbers.Pretty(run.Text("A"))} ÷ ({T("eps", "M⁻¹cm⁻¹")} × {T("c", "M")}) = {P(l)} cm";
                    run.Answer(l, "cm");
                    break;
                }
            }
            return r;
        },
    };

    private static LabCalc A260() => new()
    {
        Id = "a260", Page = "molecules", Title = "Nucleic acid from A₂₆₀",
        Help = "A nucleic acid's concentration from its absorbance at 260 nm.",
        Fields = new LabField[]
        {
            new("a260", "A₂₆₀", "A₂₆₀", LabDim.Number, "0.25", Check: LabCheck.NonNegative),
            new("dil", "Dilution factor", "dilution", LabDim.Number, "10", Exact: true, Hint: "10 for 1 part sample in 10.",
                Presets: new LabPreset[] { new("Undiluted (1)", "1"), new("10", "10"), new("50", "50"), new("100", "100") }),
            new("a280", "A₂₈₀ (optional)", "A₂₈₀", LabDim.Number, "", Check: LabCheck.Positive, Optional: true, Hint: "For the purity ratio A₂₆₀/A₂₈₀."),
            new("c", "Concentration", "c", LabDim.MassConcentration, "", "µg/mL"),
        },
        Modes = new LabMode[]
        {
            new("ds", "dsDNA", "c = A₂₆₀ × 50 µg/mL × dilution", new[] { "a260", "dil", "a280" }, "c"),
            new("rna", "RNA", "c = A₂₆₀ × 40 µg/mL × dilution", new[] { "a260", "dil", "a280" }, "c"),
            new("ss", "ssDNA", "c = A₂₆₀ × 33 µg/mL × dilution", new[] { "a260", "dil", "a280" }, "c"),
        },
        ModeLabel = "Kind",
        Solve = run =>
        {
            var r = run.R;
            var (a, dil) = (run.Get("a260"), run.Get("dil"));
            if (run.Failed) return r;
            var factor = run.Mode.Id switch { "rna" => 40, "ss" => 33, _ => 50 };
            var c = a!.Value * factor * dil!.Value;
            r.Substitution = $"c = {LabNumbers.Pretty(run.Text("a260"))} × {factor} µg/mL × {P(dil.Value)} = {P(c)} µg/mL";
            r.Steps.Add($"An A₂₆₀ of 1 is {factor} µg/mL of {run.Mode.Title}.");
            run.Answer(c, "µg/mL");
            if (run.Optional("a280") is { } a280)
            {
                var ratio = a!.Value / a280;
                var pure = run.Mode.Id == "rna" ? 2.0 : 1.8;
                r.Rows.Add(new LabRow("A₂₆₀/A₂₈₀", $"{LabNumbers.Fixed(ratio, 2)}{(ratio < pure - 0.15 ? ": protein is likely there too" : ratio > pure + 0.25 ? ": higher than pure" : ": about pure")}", Strong: true));
            }
            r.Notes.Add("A₂₆₀/A₂₈₀ near 1.8 is pure DNA, near 2.0 pure RNA; lower means protein is there too.");
            return r;
        },
    };

    private static LabCalc Pcr() => new()
    {
        Id = "pcr", Page = "molecules", Title = "PCR copies",
        Help = "Each cycle doubles the target, at ideal efficiency.",
        Fields = new LabField[]
        {
            new("n0", "Starting copies", "N₀", LabDim.Number, "100", Exact: true),
            new("cycles", "Cycles", "n", LabDim.Number, "30", Check: LabCheck.WholeOrZero, Exact: true),
            new("eff", "Efficiency (%)", "E", LabDim.Number, "100", Exact: true, Hint: "100% is the ideal doubling every cycle."),
        },
        Modes = new LabMode[] { new("pcr", "Copies", "N = N₀ × (1 + E)ⁿ, which is N₀ × 2ⁿ at 100%", new[] { "n0", "cycles", "eff" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var (n0, n, eff) = (run.Get("n0"), run.Get("cycles"), run.Get("eff"));
            if (run.Failed) return r;
            var c = (int)Math.Round(n!.Value);
            if (c > 60) { r.FieldErrors["cycles"] = "Up to 60 cycles (real PCR runs 25–40)."; return r; }
            if (eff > 100) { r.FieldErrors["eff"] = "Efficiency is at most 100%: each copy can only double."; return r; }
            var e = eff!.Value / 100;
            var copies = n0!.Value * Math.Pow(1 + e, c);
            r.Substitution = e == 1
                ? $"N = {P(n0.Value)} × 2{LabNumbers.Sup(c)} = {P(n0.Value)} × {Math.Pow(2, c).ToString("N0", Inv)} = {P(copies)} copies"
                : $"N = {P(n0.Value)} × (1 + {P(e)}){LabNumbers.Sup(c)} = {P(n0.Value)} × {P(Math.Pow(1 + e, c))} = {P(copies)} copies";
            run.Answer(copies, "copies", "N");
            r.Notes.Add("Real PCR is a little less than 100% efficient each cycle, and levels off as primers and nucleotides run low.");
            return r;
        },
    };

    private static LabCalc Centrifuge() => new()
    {
        Id = "centrifuge", Page = "molecules", Title = "Centrifuge: RCF and rpm",
        Help = "Relative centrifugal force (× g) from the rotor's radius and its speed, or the speed for a force.",
        Fields = new LabField[]
        {
            new("r", "Rotor radius", "r", LabDim.PathLength, "10", "cm"),
            new("rpm", "Speed", "rpm", LabDim.Speed, "3000", "rpm"),
            new("rcf", "Force", "RCF", LabDim.Number, "1006"),
        },
        Modes = new LabMode[]
        {
            new("rcf", "RCF", "RCF = 1.118 × 10⁻⁵ × r × rpm²", new[] { "r", "rpm" }),
            new("rpm", "rpm", "rpm = √(RCF ÷ (1.118 × 10⁻⁵ × r))", new[] { "r", "rcf" }),
        },
        Solve = run =>
        {
            var r = run.R;
            foreach (var id in run.Mode.Inputs) run.Get(id);
            if (run.Failed) return r;
            var rad = run.Get("r", "cm")!.Value;
            var rs = run.In("r", "cm");
            if (run.Mode.Id == "rcf")
            {
                var rpm = run.Get("rpm")!.Value;
                var g = RcfConstant * rad * rpm * rpm;
                r.Substitution = $"RCF = 1.118 × 10⁻⁵ × {rs} × ({P(rpm)})² = {P(g)} × g";
                run.Answer(g, "× g", "RCF");
            }
            else
            {
                var g = run.Get("rcf")!.Value;
                var rpm = Math.Sqrt(g / (RcfConstant * rad));
                r.Substitution = $"rpm = √({P(g)} ÷ (1.118 × 10⁻⁵ × {rs})) = {P(rpm)} rpm";
                run.Answer(rpm, "rpm", "speed");
            }
            r.Notes.Add("r is from the centre of the rotor to the sample, in cm.");
            return r;
        },
    };

    private static LabCalc Bacteria() => new()
    {
        Id = "bacteria", Page = "molecules", Title = "Bacterial growth",
        Help = "Doubling once each generation time.",
        Fields = new LabField[]
        {
            new("n0", "Starting number", "N₀", LabDim.Number, "100", Exact: true),
            new("g", "Generation time", "g", LabDim.Time, "20", "min"),
            new("t", "Time", "t", LabDim.Time, "2", "h"),
        },
        Modes = new LabMode[] { new("bacteria", "Number", "N = N₀ × 2^(t ÷ g)", new[] { "n0", "g", "t" }) },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var gu = run.Unit("g").Symbol;
            var (n0, g, t) = (run.Get("n0"), run.Get("g", gu), run.Get("t", gu));
            if (run.Failed) return r;
            var ts = run.In("t", gu);
            var gens = t!.Value / g!.Value;
            var n = n0!.Value * Math.Pow(2, gens);
            r.Steps.Add($"generations = t ÷ g = {ts} ÷ {run.Typed("g")} = {P(gens)}");
            r.Substitution = Math.Abs(gens - Math.Round(gens)) < 1e-9 && gens < 100
                ? $"N = {P(n0.Value)} × 2{LabNumbers.Sup((int)Math.Round(gens))} = {P(n)}"
                : $"N = {P(n0.Value)} × 2^({P(gens)}) = {P(n)}";
            run.Answer(n, "cells", "N");
            r.Notes.Add("Only while food and space last: then growth slows, as in the logistic model.");
            return r;
        },
    };

    private static LabCalc Ph() => new()
    {
        Id = "ph", Page = "molecules", Title = "pH",
        Help = "pH = −log₁₀[H⁺], and back: [H⁺] = 10^(−pH).",
        Fields = new LabField[]
        {
            new("H", "Hydrogen ion concentration", "[H⁺]", LabDim.Concentration, "1e-7", "M"),
            new("pH", "pH", "pH", LabDim.Number, "7.00", Check: LabCheck.Any),
        },
        Modes = new LabMode[]
        {
            new("pH", "pH", "pH = −log₁₀[H⁺]", new[] { "H" }),
            new("H", "[H⁺]", "[H⁺] = 10^(−pH)", new[] { "pH" }, "H"),
        },
        Solve = run =>
        {
            var r = run.R;
            if (run.Mode.Id == "pH")
            {
                var hn = run.Number("H");
                if (run.Failed) return r;
                var h = run.Get("H", "M")!.Value;
                var hs = run.In("H", "M");
                var ph = -Math.Log10(h);
                // (a pH has as many decimal places as its [H⁺] has significant figures — one more at most, and the setting's)
                var dp = Math.Min(run.Setting - 1, (hn!.Value.Exact ? run.Setting - 1 : hn.Value.Figures + 1));
                r.Substitution = $"pH = −log₁₀({hs}) = {P(ph)}";
                r.Symbol = "pH";
                r.Raw = ph;
                r.RawBase = ph;
                r.Value = LabNumbers.Fixed(ph, dp);
                r.Steps.Add($"{P(ph)} → {r.Value} (a pH's decimal places match its [H⁺]'s significant figures, plus one at most)");
                r.Notes.Add(ph < 7 ? "Below 7: acidic." : ph > 7 ? "Above 7: basic." : "7: neutral at 25 °C.");
            }
            else
            {
                var pn = run.Number("pH");
                if (run.Failed) return r;
                var ph = pn!.Value.Value;
                var h = Math.Pow(10, -ph);
                var figures = Math.Min(run.Setting, Math.Max(1, pn.Value.Decimals) + 1);
                r.Substitution = $"[H⁺] = 10^(−{LabNumbers.Pretty(run.Text("pH"))}) = {P(h)} M";
                r.Symbol = "[H⁺]";
                var to = run.Unit("H");
                r.RawBase = h;
                r.Raw = to.FromBase(h);
                r.Unit = to.Symbol;
                r.Value = LabNumbers.Minus(LabNumbers.Sig(r.Raw, figures));
                if (to.Symbol != "M") r.Steps.Add($"[H⁺] = {P(h)} M = {P(r.Raw)} {to.Symbol}");
                r.Steps.Add($"{P(r.Raw)} {to.Symbol} → {r.Value} {to.Symbol} ({figures} significant figures: the pH's {pn.Value.Decimals} decimal place{(pn.Value.Decimals == 1 ? "" : "s")}, plus one at most)");
            }
            r.Notes.Add("Each step of 1 in pH is 10 times the [H⁺].");
            return r;
        },
    };

    private static LabCalc Q10() => new()
    {
        Id = "q10", Page = "molecules", Title = "Q₁₀",
        Help = "How many times faster a rate gets for a 10 °C rise.",
        Fields = new LabField[]
        {
            new("r1", "Rate at the lower temperature", "R₁", LabDim.Number, "2.0"),
            new("t1", "Lower temperature", "T₁", LabDim.Temperature, "20", "°C", Check: LabCheck.Temperature),
            new("r2", "Rate at the higher temperature", "R₂", LabDim.Number, "4.4"),
            new("t2", "Higher temperature", "T₂", LabDim.Temperature, "30", "°C", Check: LabCheck.Temperature),
            new("q", "Temperature coefficient", "Q₁₀", LabDim.Number, "2.2"),
        },
        Modes = new LabMode[]
        {
            new("q", "Q₁₀", "Q₁₀ = (R₂ ÷ R₁)^(10 ÷ (T₂ − T₁))", new[] { "r1", "t1", "r2", "t2" }),
            new("r2", "R₂", "R₂ = R₁ × Q₁₀^((T₂ − T₁) ÷ 10)", new[] { "r1", "t1", "t2", "q" }),
        },
        ModeLabel = "",
        Solve = run =>
        {
            var r = run.R;
            var tu = run.Unit("t1").Symbol;
            var (r1, t1, t2) = (run.Get("r1"), run.Get("t1", tu), run.Get("t2", tu));
            var other = run.Mode.Id == "r2" ? run.Get("q") : run.Get("r2");
            if (run.Failed) return r;
            if (t1 == t2) { r.FieldErrors["t2"] = "The two temperatures must differ."; return r; }
            var t2s = run.In("t2", tu);
            if (run.Mode.Id == "r2")
            {
                var power = (t2!.Value - t1!.Value) / 10;
                var r2 = r1!.Value * Math.Pow(other!.Value, power);
                r.Steps.Add($"(T₂ − T₁) ÷ 10 = ({t2s} − {run.Typed("t1")}) ÷ 10 = {P(power)}");
                r.Substitution = $"R₂ = {run.T("r1")} × {run.T("q")}^({P(power)}) = {P(r2)}";
                run.Answer(r2, "", "R₂");
                if (r.Ok) r.Notes.Add("In the same units as R₁.");
                return r;
            }
            var exp = 10 / (t2!.Value - t1!.Value);
            var q = Math.Pow(other!.Value / r1!.Value, exp);
            r.Steps.Add($"R₂ ÷ R₁ = {run.T("r2")} ÷ {run.T("r1")} = {P(other.Value / r1.Value)}");
            r.Steps.Add($"10 ÷ (T₂ − T₁) = 10 ÷ ({t2s} − {run.Typed("t1")}) = {P(exp)}");
            r.Substitution = $"Q₁₀ = ({run.T("r2")} ÷ {run.T("r1")})^({P(exp)}) = {P(q)}";
            run.Answer(q, "", "Q₁₀");
            if (r.Ok) r.Notes.Add(q is > 1.8 and < 3.2 ? "Q₁₀ of 2–3 is typical of enzyme-catalysed reactions in living things." : q < 1.3 ? "Q₁₀ near 1: hardly any change with temperature." : "Typical enzyme-catalysed reactions have a Q₁₀ of 2–3.");
            return r;
        },
    };
}
