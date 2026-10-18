using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using Simpl.Kit;

namespace Simpl.Tools;

// The Periodic Table's elements and what is worked out from them (the Mac's PeriodicTable.swift model half and
// PeriodicModel.swift): each element as the data has it, its kind and colour, more of it (ionization energies, radii,
// shells …), the properties the table can be coloured by and the trends they follow, and its electrons worked out — its
// configuration in full, its subshells, and how the electrons sit in their orbitals.

/// <summary>An element of the periodic table (PeriodicData.cs; the Mac's PTElement).</summary>
public sealed class PTElement
{
    public required int Number { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public double Mass { get; init; }
    public string Category { get; init; } = "unknown";
    public int? Group { get; init; }
    public int Period { get; init; }
    public string Block { get; init; } = "";
    public string Phase { get; init; } = "";
    public string Config { get; init; } = "";
    public double? En { get; init; }
    public double? Melt { get; init; }
    public double? Boil { get; init; }
    public double? Density { get; init; }
    public string? Found { get; init; }
    /// <summary>The column (1–18) and row (1–7, then 9 and 10 for the lanthanides and actinides) it sits in.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    public string Summary { get; init; } = "";

    /// <summary>A whole number is the longest-lived isotope's mass, the convention for an element with no stable one.</summary>
    /// <summary>(0.3.28) A whole number is the longest-lived isotope's mass, in brackets; every other to five significant
    /// figures (1.0080, 55.845, 238.03), as the Mac's.</summary>
    public string MassText => Mass == Math.Round(Mass)
        ? $"[{(int)Mass}]"
        : Mass.ToString("F" + Math.Max(0, 5 - Math.Max(1, (int)Math.Floor(Math.Log10(Mass)) + 1)).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>(0.3.28) The standard atomic weights the data gives to fewer than five significant figures, to five.</summary>
    internal static readonly Dictionary<int, double> Precise = new()
    {
        [1] = 1.0080, [2] = 4.0026, [3] = 6.9410, [4] = 9.0122, [5] = 10.811, [10] = 20.180, [11] = 22.990, [16] = 32.065, [17] = 35.453, [66] = 162.50,
    };

    public string Line => $"{Name} · {Number} · {MassText}";

    public PTCategory Kind => PTCategory.Of(Category);

    public static string Celsius(double? k) => k is { } v ? $"{(int)Math.Round(v - 273.15, MidpointRounding.AwayFromZero)} °C" : "—";

    public string DensityText => Density is { } d ? $"{Trim(d)} {(Phase == "Gas" ? "g/L" : "g/cm³")}" : "—";

    /// <summary>A whole number without its point; any other as short as it reads.</summary>
    public static string Trim(double v) => v == Math.Round(v) && Math.Abs(v) < 1e9 ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    // ---- more of it (the Mac's PTElement extension in PeriodicModel.swift) ----

    public PTExtra? Extra => PeriodicExtra.Of(Number);
    /// <summary>First ionization energy, kJ/mol.</summary>
    public double? Ionization => Extra is { Ionization.Count: > 0 } x ? x.Ionization[0] : null;
    /// <summary>Atomic radius, pm (calculated, else empirical).</summary>
    public double? Radius => Extra?.RadiusCalculated ?? Extra?.RadiusEmpirical;
    public double? Affinity => Extra?.Affinity;
    /// <summary>Neutrons in its commonest (or longest-lived) isotope, by its mass.</summary>
    public int Neutrons => Math.Max(0, (int)Math.Round(Mass, MidpointRounding.AwayFromZero) - Number);
    public IReadOnlyList<int> Shells => Extra?.Shells ?? Array.Empty<int>();
    /// <summary>Its CPK colour (the molecule models' atoms).</summary>
    public Color CpkColor => K.Hex(Extra is { Cpk.Length: > 0 } x ? x.Cpk : "#c8c8c8");

    /// <summary>Valence electrons: by group for the main groups (helium 2); the outer s and the d under it for the d-block.</summary>
    public int? ValenceElectrons
    {
        get
        {
            if (Number == 2) return 2;
            if (Group is not { } g) return null;
            if (g <= 2) return g;
            if (g >= 13) return g - 10;
            if (Block == "d")
            {
                var subs = PTConfig.Subshells(this);
                if (subs.Count == 0) return null;
                var top = subs.Max(s => s.N);
                return subs.Where(s => (s.N == top && s.L == 0) || (s.N == top - 1 && s.L == 2)).Sum(s => s.Count);
            }
            return null;
        }
    }

    /// <summary>The chips' "State" word for 25 °C.</summary>
    public string StateWord => Phase.Length == 0 ? "Unknown" : Phase;
}

/// <summary>A kind of element, and its colour (the Mac's PTCategory).</summary>
public sealed record PTCategory(string Key, string Label, string Hex)
{
    public Color Color => K.Hex(Hex);

    public static readonly PTCategory[] All =
    {
        new("alkali metal", "Alkali metal", "#ff453a"),
        new("alkaline earth metal", "Alkaline earth", "#ff9f0a"),
        new("transition metal", "Transition metal", "#ffd60a"),
        new("post-transition metal", "Post-transition", "#30d158"),
        new("metalloid", "Metalloid", "#64d2ff"),
        new("nonmetal", "Nonmetal", "#0a84ff"),
        new("noble gas", "Noble gas", "#bf5af2"),
        new("lanthanide", "Lanthanide", "#ff375f"),
        new("actinide", "Actinide", "#ac8e68"),
        new("unknown", "Unknown", "#8e8e93"),
    };

    public static PTCategory Of(string key) => All.FirstOrDefault(c => c.Key == key) ?? All[^1];
}

/// <summary>The elements, read once from their JSON (the Mac's PeriodicTable).</summary>
public static class PeriodicTable
{
    private static IReadOnlyList<PTElement>? elements;
    private static Dictionary<int, PTElement>? numbered;

    public static IReadOnlyList<PTElement> Elements => elements ??= Read();

    private static List<PTElement> Read()
    {
        var list = new List<PTElement>();
        try
        {
            using var doc = JsonDocument.Parse(PeriodicData.Json);
            foreach (var r in doc.RootElement.EnumerateArray())
                if (Element(r) is { } e) list.Add(e);
        }
        catch (Exception e) { Support.Log.Write($"periodic data: {e.Message}"); }
        return list;
    }

    private static PTElement? Element(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 18) return null;
        double? Num(int i) => r[i].ValueKind == JsonValueKind.Number ? r[i].GetDouble() : null;
        int? Int(int i) => r[i].ValueKind == JsonValueKind.Number ? (int)r[i].GetDouble() : null;
        string? Str(int i) => r[i].ValueKind == JsonValueKind.String ? r[i].GetString() : null;
        if (Int(0) is not { } n || Str(1) is not { } symbol || Str(2) is not { } name || Int(15) is not { } x || Int(16) is not { } y) return null;
        return new PTElement
        {
            Number = n, Symbol = symbol, Name = name, Mass = PTElement.Precise.TryGetValue(n, out var precise) ? precise : Num(3) ?? 0, Category = Str(4) ?? "unknown", Group = Int(5),
            Period = Int(6) ?? y, Block = Str(7) ?? "", Phase = Str(8) ?? "", Config = Str(9) ?? "", En = Num(10),
            Melt = Num(11), Boil = Num(12), Density = Num(13), Found = Str(14), X = x, Y = y, Summary = Str(17) ?? "",
        };
    }

    /// <summary>An element by its number, looked up rather than searched for.</summary>
    public static PTElement? ByNumber(int n)
    {
        numbered ??= Elements.GroupBy(e => e.Number).ToDictionary(g => g.Key, g => g.First());
        return numbered.TryGetValue(n, out var e) ? e : null;
    }

    /// <summary>An element by its number, symbol or name ("26", "Fe", "iron"), as a launch flag or a detail names it.</summary>
    public static PTElement? Named(string? s)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return ByNumber(n);
        return Elements.FirstOrDefault(e => string.Equals(e.Symbol, s, StringComparison.OrdinalIgnoreCase) || string.Equals(e.Name, s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What a search finds, best first: a symbol, name or number exactly; then ones starting with it; then names holding it.</summary>
    public static List<PTElement> Find(string q)
    {
        var s = q.Trim().ToLowerInvariant();
        if (s.Length == 0) return new();
        string Num(PTElement e) => e.Number.ToString(CultureInfo.InvariantCulture);
        var exact = Elements.Where(e => e.Symbol.ToLowerInvariant() == s || e.Name.ToLowerInvariant() == s || Num(e) == s).ToList();
        var starts = Elements.Where(e => !exact.Contains(e) && (e.Symbol.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal)
            || e.Name.ToLowerInvariant().StartsWith(s, StringComparison.Ordinal) || Num(e).StartsWith(s, StringComparison.Ordinal))).ToList();
        var within = Elements.Where(e => !exact.Contains(e) && !starts.Contains(e) && e.Name.ToLowerInvariant().Contains(s, StringComparison.Ordinal)).ToList();
        return exact.Concat(starts).Concat(within).ToList();
    }

    /// <summary>The element next to one in a direction (the arrow keys), skipping the gaps.</summary>
    public static PTElement? Step(PTElement e, int dx, int dy)
    {
        var x = e.X + dx;
        var y = e.Y + dy;
        var n = 0;
        while (x >= 1 && x <= 18 && y >= 1 && y <= 10 && n < 18)
        {
            if (Elements.FirstOrDefault(o => o.X == x && o.Y == y) is { } hit) return hit;
            x += dx;
            y += dy;
            n++;
        }
        return null;
    }
}

/// <summary>More of an element: ionization energies, electron affinity, radii, shells, its CPK colour, appearance, molar heat
/// and who named it (the Mac's PTExtra).</summary>
public sealed record PTExtra(IReadOnlyList<double> Ionization, double? Affinity, double? RadiusCalculated, double? RadiusEmpirical, double? Covalent,
    double? VanDerWaals, double? Metallic, IReadOnlyList<int> Shells, string Cpk, string Appearance, double? MolarHeat, string NamedBy);

/// <summary>PeriodicExtraData.cs read once (the Mac's PeriodicExtra).</summary>
public static class PeriodicExtra
{
    private static Dictionary<int, PTExtra>? byNumber;

    public static PTExtra? Of(int number) => (byNumber ??= Read()).TryGetValue(number, out var x) ? x : null;

    private static Dictionary<int, PTExtra> Read()
    {
        var out_ = new Dictionary<int, PTExtra>();
        try
        {
            using var doc = JsonDocument.Parse(PeriodicExtraData.Json);
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() < 13) continue;
                double? Num(int i) => r[i].ValueKind == JsonValueKind.Number ? r[i].GetDouble() : null;
                string Str(int i) => r[i].ValueKind == JsonValueKind.String ? r[i].GetString() ?? "" : "";
                if (Num(0) is not { } n) continue;
                var ie = r[1].ValueKind == JsonValueKind.Array ? r[1].EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetDouble()).ToList() : new List<double>();
                var shells = r[8].ValueKind == JsonValueKind.Array ? r[8].EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => (int)v.GetDouble()).ToList() : new List<int>();
                out_[(int)n] = new PTExtra(ie, Num(2), Num(3), Num(4), Num(5), Num(6), Num(7), shells, Str(9), Str(10), Num(11), Str(12));
            }
        }
        catch (Exception e) { Support.Log.Write($"periodic extra data: {e.Message}"); }
        return out_;
    }
}

// ---- colour by ----

/// <summary>What the table can be coloured by: its kind, or a property on a scale (with its trend across a period and up a
/// group), its orbital block, or its state at 25 °C (the Mac's PTColouring; its raw values the launch flag's words).</summary>
public enum PTColouring { Category, Electronegativity, Radius, Ionization, Affinity, Melting, Boiling, Density, Block, State }

/// <summary>A colouring's words, values, scale and colours (the Mac's PTColouring members).</summary>
public static class PTColourings
{
    public static readonly PTColouring[] All = Enum.GetValues<PTColouring>();

    /// <summary>"electronegativity" → the colouring (the Mac's rawValue), else null.</summary>
    public static PTColouring? Parse(string? raw) => All.Cast<PTColouring?>().FirstOrDefault(c => string.Equals(c.ToString(), raw?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Label(this PTColouring c) => c switch
    {
        PTColouring.Category => "Category",
        PTColouring.Electronegativity => "Electronegativity",
        PTColouring.Radius => "Atomic radius",
        PTColouring.Ionization => "Ionization energy",
        PTColouring.Affinity => "Electron affinity",
        PTColouring.Melting => "Melting point",
        PTColouring.Boiling => "Boiling point",
        PTColouring.Density => "Density",
        PTColouring.Block => "Orbital block",
        _ => "State at 25 °C",
    };

    public static string Unit(this PTColouring c) => c switch
    {
        PTColouring.Electronegativity => "Pauling scale",
        PTColouring.Radius => "pm",
        PTColouring.Ionization or PTColouring.Affinity => "kJ/mol",
        PTColouring.Melting or PTColouring.Boiling => "K",
        PTColouring.Density => "g/cm³",
        _ => "",
    };

    /// <summary>A property on a scale (the others colour by kind).</summary>
    public static bool Scaled(this PTColouring c) => c is not (PTColouring.Category or PTColouring.Block or PTColouring.State);

    public static double? Value(this PTColouring c, PTElement e) => c switch
    {
        PTColouring.Electronegativity => e.En,
        PTColouring.Radius => e.Radius,
        PTColouring.Ionization => e.Ionization,
        PTColouring.Affinity => e.Affinity,
        PTColouring.Melting => e.Melt,
        PTColouring.Boiling => e.Boil,
        // (gases' densities are per litre: per cm³ here, so the scale is one)
        PTColouring.Density => e.Density is { } d ? (e.Phase == "Gas" ? d / 1000 : d) : null,
        _ => null,
    };

    /// <summary>The value as the cell and the card show it.</summary>
    public static string Text(this PTColouring c, PTElement e) =>
        Texts.TryGetValue(c, out var m) && m.TryGetValue(e.Number, out var t) ? t : MakeText(c, e);

    private static string MakeText(PTColouring c, PTElement e) => c switch
    {
        PTColouring.Category => e.Kind.Label,
        PTColouring.Block => $"{e.Block}-block",
        PTColouring.State => e.StateWord,
        PTColouring.Density => e.Density is { } d ? Short(d) + (e.Phase == "Gas" ? " g/L" : "") : "—",
        _ => c.Value(e) is { } v ? Short(v) : "—",
    };

    public static string Short(double v)
    {
        var inv = CultureInfo.InvariantCulture;
        if (Math.Abs(v) >= 1000) return ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(inv);
        if (Math.Abs(v) >= 100) return v.ToString("F0", inv);
        if (Math.Abs(v) >= 10) return v.ToString("F1", inv);
        return v.ToString("F2", inv);
    }

    /// <summary>Spread over a log scale (density: a few run far above the rest).</summary>
    private static bool LogScale(this PTColouring c) => c == PTColouring.Density;

    private static Dictionary<PTColouring, (double Lo, double Hi)>? ranges;

    /// <summary>Each view's scale, worked out once (not again for every cell, every time the table draws).</summary>
    private static (double Lo, double Hi) Range(this PTColouring c)
    {
        ranges ??= All.ToDictionary(k => k, k =>
        {
            var vs = PeriodicTable.Elements.Select(x => k.Value(x)).OfType<double>().Select(v => k.LogScale() ? Math.Log10(Math.Max(v, 1e-4)) : v).ToList();
            return vs.Count == 0 ? (0d, 1d) : (vs.Min(), vs.Max());
        });
        return ranges[c];
    }

    /// <summary>Where a value falls on the scale, 0…1.</summary>
    public static double? Fraction(this PTColouring c, PTElement e)
    {
        if (c.Value(e) is not { } v) return null;
        var (lo, hi) = c.Range();
        var x = c.LogScale() ? Math.Log10(Math.Max(v, 1e-4)) : v;
        return hi > lo ? Math.Clamp((x - lo) / (hi - lo), 0, 1) : 0.5;
    }

    private static Dictionary<PTColouring, (string Low, string High)>? ends;

    private static (string Low, string High) Ends(this PTColouring c)
    {
        ends ??= All.ToDictionary(k => k, k =>
        {
            var vs = PeriodicTable.Elements.Select(x => k.Value(x)).OfType<double>().ToList();
            return vs.Count == 0 ? ("", "") : (Short(vs.Min()), Short(vs.Max()));
        });
        return ends[c];
    }

    public static string LowText(this PTColouring c) => c.Ends().Low;
    public static string HighText(this PTColouring c) => c.Ends().High;

    /// <summary>The colour of a fraction of the scale: deep blue (low) through teal and green to amber and red (high).</summary>
    public static Color Ramp(double t)
    {
        (double R, double G, double B)[] stops = { (0.13, 0.27, 0.55), (0.12, 0.45, 0.55), (0.2, 0.52, 0.3), (0.62, 0.55, 0.15), (0.72, 0.22, 0.2) };
        var x = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        var i = Math.Min((int)x, stops.Length - 2);
        var f = x - i;
        var (a, b) = (stops[i], stops[i + 1]);
        byte C(double u, double v) => (byte)Math.Round(Math.Clamp(u + (v - u) * f, 0, 1) * 255);
        return System.Windows.Media.Color.FromRgb(C(a.R, b.R), C(a.G, b.G), C(a.B, b.B));
    }

    public static readonly Dictionary<string, string> BlockColors = new() { ["s"] = "#c0392b", ["p"] = "#c9a227", ["d"] = "#2e86c1", ["f"] = "#27ae60" };
    public static readonly Dictionary<string, string> StateColors = new() { ["Solid"] = "#5d6d7e", ["Liquid"] = "#2e86c1", ["Gas"] = "#17a589" };

    public static Color BlockColor(string block) => K.Hex(BlockColors.TryGetValue(block, out var h) ? h : "#8e8e93");
    public static Color StateColor(string phase) => K.Hex(StateColors.TryGetValue(phase, out var h) ? h : "#8e8e93");

    private static Dictionary<PTColouring, Dictionary<int, Color>>? colours;
    private static Dictionary<PTColouring, Dictionary<int, string>>? texts;

    /// <summary>Every element's words under every view, made once (a cell asks for them each time it draws).</summary>
    private static Dictionary<PTColouring, Dictionary<int, string>> Texts =>
        texts ??= All.ToDictionary(c => c, c => PeriodicTable.Elements.ToDictionary(e => e.Number, e => MakeText(c, e)));

    /// <summary>An element's colour under this view.</summary>
    public static Color Color(this PTColouring c, PTElement e)
    {
        colours ??= All.ToDictionary(k => k, k => PeriodicTable.Elements.ToDictionary(x => x.Number, x => MakeColor(k, x)));
        return colours[c].TryGetValue(e.Number, out var col) ? col : MakeColor(c, e);
    }

    private static Color MakeColor(PTColouring c, PTElement e) => c switch
    {
        PTColouring.Category => e.Kind.Color,
        PTColouring.Block => BlockColor(e.Block),
        PTColouring.State => StateColor(e.Phase),
        _ => c.Fraction(e) is { } f ? Ramp(f) : System.Windows.Media.Color.FromRgb(89, 89, 89),
    };

    /// <summary>Its trend: across a period (left to right) and up a group — +1 rising, −1 falling — and in words; null where
    /// there is no simple trend.</summary>
    public static (int Across, int Up, string AcrossWords, string UpWords)? Trend(this PTColouring c) => c switch
    {
        PTColouring.Electronegativity => (1, 1, "Electronegativity increases across a period", "Increases up a group"),
        PTColouring.Radius => (-1, -1, "Atomic radius decreases across a period", "Increases down a group"),
        PTColouring.Ionization => (1, 1, "Ionization energy increases across a period", "Increases up a group"),
        PTColouring.Affinity => (1, 1, "Electron affinity generally increases across a period", "Generally increases up a group"),
        _ => null,
    };
}

// ---- electrons ----

/// <summary>A subshell and its electrons: 2p⁴ is n 2, l 1 (s 0, p 1, d 2, f 3), count 4 (the Mac's PTSubshell).</summary>
public readonly record struct PTSubshell(int N, int L, int Count)
{
    public string Letter => new[] { "s", "p", "d", "f" }[Math.Clamp(L, 0, 3)];
    public string Id => $"{N}{Letter}";
    public string Name => Id;
    public int Capacity => 2 * (2 * L + 1);
    public int Orbitals => 2 * L + 1;

    /// <summary>Its electrons in its orbitals, by Hund's rule (each orbital one before any takes two): each orbital's count.</summary>
    public int[] Filling
    {
        get
        {
            var (count, orbitals) = (Count, Orbitals);
            return Enumerable.Range(0, orbitals).Select(i => (count > i ? 1 : 0) + (count > orbitals + i ? 1 : 0)).ToArray();
        }
    }
}

/// <summary>An element's electron configuration read and written out (the Mac's PTConfig).</summary>
public static class PTConfig
{
    private static readonly Dictionary<string, int> Cores = new() { ["He"] = 2, ["Ne"] = 10, ["Ar"] = 18, ["Kr"] = 36, ["Xe"] = 54, ["Rn"] = 86 };
    private static readonly Dictionary<int, List<PTSubshell>> SubshellCache = new();
    private static readonly Dictionary<int, List<PTSubshell>> ValenceCache = new();

    /// <summary>The configuration as written ("[He] 2s2 2p2"), its subshells read: digits after a letter taken while they fit
    /// the subshell (the data's "5d16s2" is 5d¹ 6s²).</summary>
    public static (string? Core, List<PTSubshell> Subs) Parts(string config)
    {
        var s = config.Replace("*", "");
        string? core = null;
        int open = s.IndexOf('['), close = s.IndexOf(']');
        if (open >= 0 && close >= 0 && open < close)
        {
            core = s[(open + 1)..close];
            s = s.Remove(open, close - open + 1);
        }
        var subs = new List<PTSubshell>();
        var chars = s.Where(ch => !char.IsWhiteSpace(ch)).ToArray();
        static int? Digit(char ch) => ch is >= '0' and <= '9' ? ch - '0' : null;
        var i = 0;
        while (i < chars.Length)
        {
            var l = i + 1 < chars.Length ? Array.IndexOf(new[] { 's', 'p', 'd', 'f' }, chars[i + 1]) : -1;
            if (Digit(chars[i]) is not { } n || l < 0) { i++; continue; }
            var cap = 2 * (2 * l + 1);
            var j = i + 2;
            var count = 0;
            while (j < chars.Length && Digit(chars[j]) is { } d && count * 10 + d <= cap)
            {
                count = count * 10 + d;
                j++;
                // (one digit is enough when the next makes a new subshell's n: 2p6 3s…)
                if (count * 10 > cap) break;
            }
            subs.Add(new PTSubshell(n, l, count));
            i = j;
        }
        return (core, subs);
    }

    /// <summary>Every subshell, the noble-gas core written out, in order of n then l (worked out once per element).</summary>
    public static IReadOnlyList<PTSubshell> Subshells(PTElement e)
    {
        if (SubshellCache.TryGetValue(e.Number, out var c)) return c;
        var merged = new Dictionary<string, PTSubshell>();
        foreach (var s in Expand(e.Config, 0))
            merged[s.Id] = new PTSubshell(s.N, s.L, (merged.TryGetValue(s.Id, out var m) ? m.Count : 0) + s.Count);
        var list = merged.Values.OrderBy(s => s.N).ThenBy(s => s.L).ToList();
        SubshellCache[e.Number] = list;
        return list;
    }

    private static List<PTSubshell> Expand(string config, int depth)
    {
        var p = Parts(config);
        var out_ = p.Subs;
        if (p.Core is { } core && depth < 8 && Cores.TryGetValue(core, out var n) && PeriodicTable.ByNumber(n) is { } coreElement)
            out_ = Expand(coreElement.Config, depth + 1).Concat(out_).ToList();
        return out_;
    }

    /// <summary>The outer subshells (those written after the noble-gas core).</summary>
    public static IReadOnlyList<PTSubshell> Valence(PTElement e)
    {
        if (ValenceCache.TryGetValue(e.Number, out var c)) return c;
        var p = Parts(e.Config);
        var list = p.Subs.Count == 0 ? Subshells(e).ToList() : p.Subs;
        ValenceCache[e.Number] = list;
        return list;
    }

    public static string Superscript(int n)
    {
        const string sup = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        return new string(n.ToString(CultureInfo.InvariantCulture).Select(ch => ch is >= '0' and <= '9' ? sup[ch - '0'] : ch).ToArray());
    }

    /// <summary>"[He] 2s² 2p²".</summary>
    public static string Pretty(PTElement e)
    {
        var p = Parts(e.Config);
        var subs = string.Join(" ", p.Subs.Select(s => $"{s.Name}{Superscript(s.Count)}"));
        return string.Join(" ", new[] { p.Core is { } c ? $"[{c}]" : null, subs.Length == 0 ? null : subs }.OfType<string>());
    }

    /// <summary>"1s² 2s² 2p²".</summary>
    public static string Full(PTElement e) => string.Join(" ", Subshells(e).Select(s => $"{s.Name}{Superscript(s.Count)}"));
}

/// <summary>How an element compares with every element (the Mac's PTRank).</summary>
public static class PTRank
{
    /// <summary>The share of elements with a value that this one is above, 0…1.</summary>
    public static double? Percentile(PTColouring c, PTElement e)
    {
        if (c.Value(e) is not { } v) return null;
        var all = PeriodicTable.Elements.Select(x => c.Value(x)).OfType<double>().ToList();
        if (all.Count <= 1) return null;
        return (double)all.Count(x => x < v) / (all.Count - 1);
    }
}
