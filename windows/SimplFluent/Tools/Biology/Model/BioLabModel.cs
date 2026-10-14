using System.Text.Json;

namespace Simpl.Tools;

// Bio Lab Calculator's own model (spec §4.8, §5.5): what the tool keeps without a window — each calculator's mode, the
// texts typed and the units chosen, whether its steps are open, the significant-figures setting — in a static class,
// so it outlives the view being built again (a view subscribes to Changed on Loaded and lets go on Unloaded). The
// whole tool and its pin's quick version share it, so what was begun in the flyout is there when the tool opens. The
// calculators themselves are in BioLabCalculators.cs, their numbers in BioLabNumbers.cs. Plain .NET, no WPF, so the
// unit tests reach it; its self-check lines (BiolabChecks) run every row of §5.5 through the same code.

/// <summary>A calculator as the student left it: its mode, the texts typed and the units chosen (only those changed from
/// the calculator's own), and whether its steps are open.</summary>
public sealed class LabCalcState
{
    public string Mode { get; set; } = "";
    public Dictionary<string, string> Texts { get; set; } = new();
    public Dictionary<string, string> Units { get; set; } = new();
    public bool Steps { get; set; }
}

/// <summary>Bio Lab Calculator's state, kept across its views.</summary>
public static class BioLabModel
{
    private static readonly Dictionary<string, LabCalcState> states = new();
    private static readonly Dictionary<string, LabResult> results = new();
    private static int figures = 3;

    /// <summary>A calculator's inputs changed (its id), or every one's ("": the figures setting).</summary>
    public static event Action<string>? Changed;

    internal static void Tell(string id = "")
    {
        if (id.Length == 0) results.Clear(); else results.Remove(id);
        Changed?.Invoke(id);
    }

    /// <summary>The significant figures an answer is given to (2–6; 3 unless changed), never more than the least precise
    /// input plus one.</summary>
    public static int Figures
    {
        get => figures;
        set
        {
            var f = Math.Clamp(value, 2, 6);
            if (f == figures) return;
            figures = f;
            Tell();
        }
    }

    /// <summary>A calculator to bring into view when the tool opens (a pin's quick version hands it over), taken once.</summary>
    public static string? Focus { get; set; }

    public static LabCalcState State(string id)
    {
        if (!states.TryGetValue(id, out var s))
        {
            s = new LabCalcState { Mode = LabCalcs.Find(id)?.Modes[0].Id ?? "" };
            states[id] = s;
        }
        return s;
    }

    public static string Mode(string id) => State(id).Mode;

    public static string Text(string id, string field) =>
        State(id).Texts.TryGetValue(field, out var t) ? t : LabCalcs.Find(id)?.Field(field).Text ?? "";

    public static string Unit(string id, string field) =>
        State(id).Units.TryGetValue(field, out var u) ? u : LabCalcs.Find(id)?.Field(field).Unit ?? "";

    public static bool StepsOpen(string id) => State(id).Steps;

    public static void SetText(string id, string field, string text)
    {
        if (Text(id, field) == text) return;
        State(id).Texts[field] = text;
        Tell(id);
    }

    public static void SetUnit(string id, string field, string unit)
    {
        if (Unit(id, field) == unit) return;
        State(id).Units[field] = unit;
        Tell(id);
    }

    public static void SetSteps(string id, bool open)
    {
        if (StepsOpen(id) == open) return;
        State(id).Steps = open;
        Changed?.Invoke(id);
    }

    /// <summary>
    /// What the calculator solves for (or its shape, its kind) changed. The answer it had moves into the field it came
    /// from, so the numbers stay together: solve for the mass, then for the concentration, and the mass is the one found.
    /// </summary>
    public static void SetMode(string id, string mode)
    {
        if (LabCalcs.Find(id) is not { } calc || Mode(id) == mode) return;
        var old = calc.Mode(Mode(id));
        var next = calc.Mode(mode);
        var was = Result(id);
        var field = old.Output ?? (calc.Fields.Any(f => f.Id == old.Id) ? old.Id : null);
        if (was.Ok && field != null && next.Inputs.Contains(field) && double.IsFinite(was.Raw))
            State(id).Texts[field] = was.Value.Replace("−", "-");
        State(id).Mode = next.Id;
        Tell(id);
    }

    /// <summary>
    /// Cell Explorer's "Why are cells small?" taken (the hand-off both apps send): <c>savShape</c> sphere, box or cylinder,
    /// <c>savSize</c> its sizes across — a sphere's diameter, a box's "l,w,h", a cylinder's "diameter,length" — in
    /// <c>savUnit</c>, and <c>from</c> (the tool it came from). The earlier words are read too: <c>calc</c> = sav with
    /// <c>mode</c> and the fields themselves (r, s, l, w, h). The surface-area-to-volume calculator is set up with the cell's
    /// shape, its steps open, and brought into view; false when nothing of the kind was handed over.
    /// </summary>
    public static bool TakeCells(Func<string, string?> take)
    {
        var shape = take("savShape");
        var size = take("savSize");
        var unit = take("savUnit");
        take("from");
        var calc = shape == null ? take("calc") : null;
        if (shape == null && calc != "sav") return false;
        const string id = "sav";
        if (LabCalcs.Find(id) is not { } sav) return false;
        var mode = (shape ?? take("mode"))?.Trim().ToLowerInvariant() switch
        {
            "sphere" => "sphere", "cube" => "cube", "box" or "rect" or "rectangular" or "rectangular solid" => "rect", "cylinder" => "cylinder", _ => null,
        };
        if (mode != null) SetMode(id, mode);
        var m = mode ?? Mode(id);
        static string Half(string s) => BioText.Number(s.Trim()) is { } d && d > 0 ? LabNumbers.Plain(d / 2) : s.Trim();
        if (shape != null && size != null)
        {
            var parts = size.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string[] fields = m switch { "sphere" => new[] { "r" }, "cube" => new[] { "s" }, "rect" => new[] { "l", "w", "h" }, _ => new[] { "r", "h" } };
            for (var i = 0; i < Math.Min(parts.Length, fields.Length); i++)
                SetText(id, fields[i], fields[i] == "r" ? Half(parts[i]) : parts[i]);
        }
        else
            foreach (var f in new[] { "r", "s", "l", "w", "h" })
                if (take(f) is { Length: > 0 } t) SetText(id, f, t.Trim());
        if (unit is { Length: > 0 } && LabUnits.Of(LabDim.Length).Any(u => u.Symbol == unit))
            foreach (var f in sav.Fields) SetUnit(id, f.Id, unit);
        SetSteps(id, true);
        Focus = id;
        return true;
    }

    /// <summary>The calculator's own values back.</summary>
    public static void Reset(string id)
    {
        states.Remove(id);
        Tell(id);
    }

    /// <summary>The calculator worked out as it stands (kept until something changes).</summary>
    public static LabResult Result(string id)
    {
        if (results.TryGetValue(id, out var r)) return r;
        if (LabCalcs.Find(id) is not { } calc) return new LabResult { Problem = "No such calculator." };
        var s = State(id);
        var texts = calc.Fields.ToDictionary(f => f.Id, f => Text(id, f.Id));
        var units = calc.Fields.ToDictionary(f => f.Id, f => Unit(id, f.Id));
        r = calc.Run(s.Mode, texts, units, figures);
        results[id] = r;
        return r;
    }

    // ---- remembered (the view keeps them in Prefs: SimplBio.biolab.calc.<id>) ----

    /// <summary>A calculator's state as text, for Prefs.</summary>
    public static string Save(string id) => JsonSerializer.Serialize(State(id));

    /// <summary>A calculator's state from Prefs (unread text is left alone).</summary>
    public static void Load(string id, string? saved)
    {
        if (string.IsNullOrEmpty(saved) || LabCalcs.Find(id) is not { } calc) return;
        try
        {
            if (JsonSerializer.Deserialize<LabCalcState>(saved) is not { } s) return;
            s.Mode = calc.Mode(s.Mode).Id;
            s.Texts = s.Texts.Where(kv => calc.Fields.Any(f => f.Id == kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            s.Units = s.Units.Where(kv => calc.Fields.Any(f => f.Id == kv.Key && LabUnits.Of(f.Dim).Any(u => u.Symbol == kv.Value))).ToDictionary(kv => kv.Key, kv => kv.Value);
            states[id] = s;
            results.Remove(id);
        }
        catch (JsonException) { /* an older or damaged save: the calculator's own values */ }
    }
}

public static partial class BioSelfCheck
{
    /// <summary>
    /// Bio Lab Calculator's own bio-check lines: the numbers it reads and writes, then every row of §5.5 worked by the
    /// calculators the student uses (the lines named as the Mac's are, biolab.*).
    /// </summary>
    static partial void BiolabChecks(BioCheckReport r)
    {
        LabResult Run(string calc, string? mode = null, params (string Field, string Text)[] texts)
        {
            var c = LabCalcs.Find(calc)!;
            return c.Run(mode, texts.ToDictionary(t => t.Field, t => t.Text), null, 3);
        }
        bool Near(double a, double b, double rel = 1e-9) => Math.Abs(a - b) <= rel * Math.Max(1, Math.Abs(b));
        string Shown(LabResult x) => x.Ok ? $"{x.Value}{LabResult.UnitAfter(x.Unit)}" : "no-answer:" + (x.Problem ?? string.Join(",", x.FieldErrors.Values));
        void Line(string name, LabResult x, double expected, string shown, double rel = 1e-9) =>
            r.Check(name, Shown(x), x.Ok && Near(x.Raw, expected, rel) && x.Value == shown);

        // ---- every calculator, in every mode, works its starting example out ----
        var broken = LabCalcs.All.SelectMany(c => c.Modes.Where(m => !c.Run(m.Id).Ok).Select(m => $"{c.Id}.{m.Id}")).ToList();
        r.Check("biolab.calculators", broken.Count == 0 ? $"{LabCalcs.All.Count}" : string.Join(",", broken.Take(3)), broken.Count == 0 && LabCalcs.All.Count == 26);

        // ---- reading and writing numbers (§5.5: figures, the least precise input plus one) ----
        var parsed = new[] { "1.5 × 10^9", "1.5x10^9", "10⁻⁶", "1:10", "3,000", "−2.4", "1e-7" }.Select(t => LabNumbers.Parse(t)?.Value ?? double.NaN).ToList();
        r.Check("biolab.parse", string.Join(",", parsed.Select(LabNumbers.Plain)),
            parsed.SequenceEqual(new[] { 1.5e9, 1.5e9, 1e-6, 0.1, 3000, -2.4, 1e-7 }) && LabNumbers.Parse("abc") == null && LabNumbers.Parse("1,5") == null);
        var figs = new[] { "250", "250.", "0.0500", "1.0", "58.44", "1e-7" }.Select(t => LabNumbers.Parse(t)!.Value.Figures).ToList();
        r.Check("biolab.sigfigs", string.Join(",", figs), figs.SequenceEqual(new[] { 2, 3, 3, 2, 4, 1 }) && LabNumbers.Parse("10^-6") is { Exact: true });
        var shown = new[]
        {
            LabNumbers.Sig(7.305, 3), LabNumbers.Sig(0.125 * 58.44, 3), LabNumbers.Sig(1.073741824e11, 3), LabNumbers.Sig(8.0386e-5, 3),
            LabNumbers.Sig(1006.2, 4), LabNumbers.Sig(0.45, 3), LabNumbers.Sig(9.996, 3), LabNumbers.Sig(-2.45145, 3), LabNumbers.Fixed(12.6447, 2),
            LabNumbers.Sig(840000, 2), LabCalcs.Factor(Math.Pow(0.1, 4)),
        };
        r.Same("biolab.format", string.Join("|", shown), "7.31|7.31|1.07 × 10¹¹|8.04 × 10⁻⁵|1,006|0.450|10.0|−2.45|12.64|8.4 × 10⁵|10⁻⁴");
        var p = LabUnits.Prefixes;
        r.Check("biolab.prefixes", string.Join(",", p.Select(x => $"{x.Symbol}{x.Power}")),
            p.Select(x => x.Power).SequenceEqual(new[] { 9, 6, 3, -1, -2, -3, -6, -9, -12 }) && string.Concat(p.Select(x => x.Symbol)) == "GMkdcmµnp");
        var masses = new[] { "NaCl", "C6H12O6", "C12H22O11" }.Select(f => LabNumbers.Fixed(LabCalcs.MolarMass(f) ?? 0, 2));
        r.Same("biolab.molar-masses", string.Join(",", masses), "58.44,180.16,342.30");

        // ---- §5.5, row by row ----
        Line("biolab.molarity", Run("molarity", "mass"), 7.305, "7.31");
        var dil = Run("dilution", "V1");
        r.Check("biolab.dilution", dil.Ok ? $"{Shown(dil)}+{(dil.Notes.Any(n => n.Contains("add 45 mL of solvent")) ? "45 mL" : "?")}" : Shown(dil),
            dil.Ok && Near(dil.Raw, 5.0) && dil.Value == "5.0" && dil.Unit == "mL" && dil.Notes.Any(n => n.Contains("add 45 mL of solvent")));
        var serial = Run("serial");
        r.Check("biolab.serial", serial.Rows.FirstOrDefault()?.Value ?? "none", serial.Ok && serial.Rows.Count > 0 && serial.Rows[0].Value.StartsWith("10⁻⁴ (1:10,000)")
            && Near(serial.Raw / LabNumbers.Parse(LabCalcs.Find("serial")!.Field("c0").Text)!.Value.Value, 1e-4));
        Line("biolab.plate", Run("plate"), 1.5e9, "1.5 × 10⁹");
        Line("biolab.percent", Run("percent", "wv"), 2.0, "2.0");
        Line("biolab.magnification-total", Run("totalMag"), 400, "400");
        var fov = Run("fov");
        r.Check("biolab.field-of-view", $"{Shown(fov)}={fov.Alt}", fov.Ok && Near(fov.Raw, 0.45) && fov.Value == "0.450" && fov.Alt == "450 µm");
        Line("biolab.specimen", Run("specimen"), 90, "90.0");
        Line("biolab.magnification", Run("magnification", "mag"), 3000, "3,000");
        var sphere = Run("sav", "sphere", ("r", "1"));
        var cube = Run("sav", "cube", ("s", "2"));
        var box = Run("sav", "rect", ("l", "3.0"), ("w", "2.0"), ("h", "1.0"));
        var cyl = Run("sav", "cylinder", ("r", "1.0"), ("h", "2.0"));
        r.Check("biolab.sa-v", $"{Shown(sphere)},{Shown(cube)}", sphere.Value == "3.0" && cube.Value == "3.0" && Near(sphere.Raw, 3) && Near(cube.Raw, 3)
            && box.Ok && box.Rows[0].Value == "22.0 µm²" && box.Rows[1].Value == "6.00 µm³" && Near(box.Raw, 22.0 / 6)
            && cyl.Ok && cyl.Rows[0].Value == "18.8 µm²" && cyl.Rows[1].Value == "6.28 µm³" && Near(cyl.Raw, 3.0));
        var sucrose = Run("water");
        r.Check("biolab.water-sucrose", sucrose.Ok ? sucrose.Rows[0].Value : Shown(sucrose), sucrose.Ok && sucrose.Rows[0].Value == "−2.45 bar" && Near(sucrose.Raw, -2.451450));
        var nacl = Run("water", null, ("i", "2"));
        r.Check("biolab.water-NaCl", nacl.Ok ? nacl.Rows[0].Value : Shown(nacl), nacl.Ok && nacl.Rows[0].Value == "−4.90 bar" && Near(nacl.Raw, -4.902900));
        Line("biolab.hemocytometer", Run("hemocytometer", null, ("counts", "42")), 8.4e5, "8.4 × 10⁵");
        var stats = Run("stats");
        string Row(string name) => stats.Rows.FirstOrDefault(x => x.Name == name)?.Value ?? "";
        var mean = LabCalcs.Stats(new[] { 12.1, 13.4, 11.8, 12.9, 13.0 });
        r.Check("biolab.mean", Row("Mean x̄"), Row("Mean x̄") == "12.64" && Near(mean.Mean, 12.64));
        r.Check("biolab.sd", $"{Row("Standard deviation s")}({mean.Sd.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})", Row("Standard deviation s") == "0.666" && Math.Abs(mean.Sd - 0.6656) < 5e-5);
        r.Check("biolab.se", $"{Row("Standard error SE")}({mean.Se.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})", Row("Standard error SE") == "0.298" && Math.Abs(mean.Se - 0.2977) < 5e-5);
        r.Check("biolab.error-bars", Row("x̄ ± 2 SE"), Row("x̄ ± 2 SE") == "12.04 to 13.24");
        Line("biolab.rate", Run("rate"), 5.0, "5.0");
        Line("biolab.growth-exponential", Run("growth", "exponential"), 50, "50");
        Line("biolab.growth-logistic", Run("growth", "logistic"), 45, "45");
        Line("biolab.simpson", Run("simpson"), 0.62, "0.62");
        var beer = Run("beer", "c");
        r.Check("biolab.beer-lambert", $"{Shown(beer)}={beer.Alt}", beer.Ok && Near(beer.Raw, 0.5 / 6220) && beer.Value == "8.04 × 10⁻⁵" && beer.Alt == "80.4 µM");
        Line("biolab.a260", Run("a260", "ds"), 125, "125");
        Line("biolab.pcr", Run("pcr"), 100 * Math.Pow(2, 30), "1.07 × 10¹¹");
        // (§5.5's 1,006 × g is the value; 10 cm and 3,000 rpm have one significant figure each, so it shows to two)
        Line("biolab.centrifuge", Run("centrifuge", "rcf"), 1006.2, "1,000");
        Line("biolab.bacteria", Run("bacteria"), 6400, "6,400");
        Line("biolab.pH", Run("ph", "pH"), 7.0, "7.00");
        Line("biolab.q10", Run("q10"), 2.2, "2.2");
        var chi = Run("chisq", "ratio");
        r.Check("biolab.chi-square", chi.Ok ? $"{chi.Value},{(chi.Rows.Last().Value.Contains("fail to reject") ? "fail-to-reject" : "?")}" : Shown(chi),
            chi.Ok && Math.Abs(chi.Raw - 0.470) < 0.0005 && chi.Rows[0].Value.StartsWith("3 ") && chi.Rows.Last().Value.Contains("fail to reject"));

        // ---- solved the other way round, the inputs come back; a field out of range says why ----
        var back = new[]
        {
            Run("molarity", "molarity", ("m", "7.305"), ("mw", "58.44"), ("v", "250")).Raw - 0.5,
            Run("dilution", "C2", ("C1", "1.0"), ("V1", "5.0"), ("V2", "50")).Raw - 0.1,
            Run("magnification", "actual", ("image", "45"), ("mag", "3000")).RawBase - 15e-6,
            Run("centrifuge", "rpm", ("r", "10"), ("rcf", "1006.2")).Raw - 3000,
            Run("ph", "H", ("pH", "7.00")).RawBase - 1e-7,
            Run("beer", "A", ("eps", "6220"), ("l", "1"), ("c", "8.0386e-5")).Raw - 0.5,
            Run("q10", "r2", ("r1", "2.0"), ("q", "2.2")).Raw - 4.4,
        };
        r.Check("biolab.inverses", string.Join(",", back.Select(d => Math.Abs(d) < 1e-3 ? "ok" : d.ToString("G3", System.Globalization.CultureInfo.InvariantCulture))), back.All(d => Math.Abs(d) < 1e-3));
        var bad = Run("dilution", "V1", ("C2", "2"));
        r.Check("biolab.reasons", bad.Problem?.Split(':')[0] ?? "none", bad.Problem != null && !bad.Ok && Run("molarity", "mass", ("v", "-3")).FieldErrors.ContainsKey("v"));
    }
}
