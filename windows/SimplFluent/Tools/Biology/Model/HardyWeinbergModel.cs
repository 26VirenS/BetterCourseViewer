using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Simpl.Tools;

// Hardy–Weinberg's own model (spec §4.7, §5.4), the Mac's HardyWeinbergModel.swift line for line: the calculator
// (everything from any one of p, q, p², 2pq, q², the share showing either trait, or genotype counts, each step written
// out), the HWE chi-square test (df = 1, editable), and the simulation — mutation, migration, mating with inbreeding,
// selection and drift, in exactly §5.4.3's order, drift drawn from SplitMix64 so a seed gives the same populations here
// and on the Mac (replicate r, counting from 0, uses seed S + r). What the tool keeps between its views is
// HardyWeinbergModel at the foot. Plain .NET, no WPF, so the unit tests reach it.

// ---- numbers written as the Mac writes them ----

/// <summary>
/// Numbers written as the Mac writes them, rounded the toolkit's one way (BioRound: decimal half-up, so 0.125 is "0.13" and
/// 3.425 is "3.43" on both apps), so the working and the self-check read the same on both platforms.
/// </summary>
public static class HWFormat
{
    /// <summary>printf's "%.{decimals}f", rounded half-up (BioRound.Fixed).</summary>
    public static string Fixed(double x, int decimals) => BioRound.Fixed(x, decimals);

    /// <summary>printf's "%g": six significant figures (rounded half-up, as BioRound rounds), the exponent written when it
    /// is below −4 or above 5.</summary>
    public static string G(double x)
    {
        if (double.IsFinite(x) && x != 0 && Math.Abs(x) < 1e15)
        {
            var places = 5 - (int)Math.Floor(Math.Log10(Math.Abs(x)));
            if (places is >= 0 and <= 20) x = (double)Math.Round((decimal)x, places, MidpointRounding.AwayFromZero);
        }
        var s = x.ToString("G6", CultureInfo.InvariantCulture);
        return s.Replace("E", "e", StringComparison.Ordinal);
    }

    /// <summary>"²", "⁻⁵": a whole number's digits raised.</summary>
    public static string Superscript(int n)
    {
        var map = new Dictionary<char, char>
        {
            ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹', ['-'] = '⁻',
        };
        return new string(n.ToString(CultureInfo.InvariantCulture).Select(c => map.TryGetValue(c, out var m) ? m : c).ToArray());
    }
}

// ---- the calculator (§4.7 Calculate, §5.4.1) ----

/// <summary>What the calculator is given.</summary>
public enum HWGiven { Q2, P, Q, P2, TwoPQ, RecessivePercent, DominantPercent, Counts }

public static class HWGivens
{
    public static readonly HWGiven[] All = Enum.GetValues<HWGiven>();

    /// <summary>The Mac's raw value (a launch's and a hand-off's name for it): "q2", "twoPQ" …</summary>
    public static string Key(this HWGiven g) => g switch
    {
        HWGiven.Q2 => "q2", HWGiven.P => "p", HWGiven.Q => "q", HWGiven.P2 => "p2", HWGiven.TwoPQ => "twoPQ",
        HWGiven.RecessivePercent => "recessivePercent", HWGiven.DominantPercent => "dominantPercent", _ => "counts",
    };

    public static HWGiven? FromKey(string? key) => All.Cast<HWGiven?>().FirstOrDefault(g => g!.Value.Key() == key);

    /// <summary>Its menu's line.</summary>
    public static string Title(this HWGiven g) => g switch
    {
        HWGiven.Q2 => "q² — the aa frequency",
        HWGiven.P => "p — the A allele's frequency",
        HWGiven.Q => "q — the a allele's frequency",
        HWGiven.P2 => "p² — the AA frequency",
        HWGiven.TwoPQ => "2pq — the Aa frequency",
        HWGiven.RecessivePercent => "% showing the recessive trait",
        HWGiven.DominantPercent => "% showing the dominant trait",
        _ => "Genotype counts (AA, Aa, aa)",
    };

    /// <summary>Its field's label.</summary>
    public static string Short(this HWGiven g) => g switch
    {
        HWGiven.Q2 => "q²", HWGiven.P => "p", HWGiven.Q => "q", HWGiven.P2 => "p²", HWGiven.TwoPQ => "2pq",
        HWGiven.RecessivePercent => "% recessive", HWGiven.DominantPercent => "% dominant", _ => "Counts",
    };

    public static bool IsPercent(this HWGiven g) => g is HWGiven.RecessivePercent or HWGiven.DominantPercent;

    /// <summary>An example value for its field.</summary>
    public static string Placeholder(this HWGiven g) => g switch
    {
        HWGiven.Q2 => "0.16", HWGiven.P => "0.6", HWGiven.Q => "0.4", HWGiven.P2 => "0.36", HWGiven.TwoPQ => "0.48",
        HWGiven.RecessivePercent => "16", HWGiven.DominantPercent => "84", _ => "",
    };
}

/// <summary>A step of the working, as it is written on paper: what it finds ("The a allele") and its sum
/// ("q = √q² = √0.16 = 0.40").</summary>
public sealed record HWStep(string Label, string Math);

/// <summary>Genotype counts from a sample.</summary>
public readonly record struct HWCounts(int AA, int Aa, int aa)
{
    public int N => AA + Aa + aa;
    /// <summary>p = (2·n_AA + n_Aa) / 2N (§5.4.1).</summary>
    public double P => N > 0 ? (double)(2 * AA + Aa) / (2 * N) : 0;
}

/// <summary>Everything the calculator works out: p and q, the steps, the other answer when given 2pq alone, notes on
/// what was assumed, and the sample when given counts.</summary>
public sealed record HWCalc(double P, double Q, IReadOnlyList<HWStep> Steps, double? OtherP, IReadOnlyList<string> Notes, HWCounts? Counts)
{
    public double P2 => P * P;
    public double TwoPQ => 2 * P * Q;
    public double Q2 => Q * Q;
    public double CarriersPer1000 => TwoPQ * 1000;
}

public static class HardyWeinberg
{
    /// <summary>Everything from one value (a frequency 0–1, or a percent 0–100 for the trait shares), or what is wrong with it.</summary>
    public static (HWCalc? Calc, string? Error) Calculate(HWGiven given, double value)
    {
        if (!double.IsFinite(value)) return (null, "Enter a number.");
        var steps = new List<HWStep>();
        var notes = new List<string>();
        double? otherP = null;
        double p;
        switch (given)
        {
            case HWGiven.Counts:
                return (null, "Enter the three genotype counts.");
            case HWGiven.Q2 or HWGiven.RecessivePercent or HWGiven.DominantPercent:
            {
                var q2 = value;
                if (given == HWGiven.RecessivePercent)
                {
                    if (value is < 0 or > 100) return (null, "A percent must be between 0 and 100.");
                    q2 = value / 100;
                    steps.Add(new("The aa frequency", $"q² = {Plain(value)}% ÷ 100 = {Num(q2)}"));
                    notes.Add("Everyone showing a recessive trait is aa, so their share is q².");
                }
                else if (given == HWGiven.DominantPercent)
                {
                    if (value is < 0 or > 100) return (null, "A percent must be between 0 and 100.");
                    q2 = 1 - value / 100;
                    steps.Add(new("The aa frequency", $"q² = 1 − {Num(value / 100)} = {Num(q2)}"));
                    notes.Add("Everyone not showing the dominant trait is aa, so q² = 1 − the dominant share.");
                }
                else
                {
                    if (value is < 0 or > 1) return (null, "q² must be between 0 and 1.");
                    steps.Add(new("Given", $"q² = {Num(q2)}"));
                }
                var q = Math.Sqrt(q2);
                steps.Add(new("The a allele", $"q = √q² = √{Num(q2)} = {Num(q)}"));
                p = 1 - q;
                steps.Add(new("The A allele", $"p = 1 − q = 1 − {Num(q)} = {Num(p)}"));
                notes.Add("q = √q² holds only if the population is in Hardy–Weinberg equilibrium.");
                break;
            }
            case HWGiven.P:
                if (value is < 0 or > 1) return (null, "p must be between 0 and 1.");
                p = value;
                steps.Add(new("Given", $"p = {Num(p)}"));
                steps.Add(new("The a allele", $"q = 1 − p = 1 − {Num(p)} = {Num(1 - p)}"));
                break;
            case HWGiven.Q:
                if (value is < 0 or > 1) return (null, "q must be between 0 and 1.");
                p = 1 - value;
                steps.Add(new("Given", $"q = {Num(value)}"));
                steps.Add(new("The A allele", $"p = 1 − q = 1 − {Num(value)} = {Num(p)}"));
                break;
            case HWGiven.P2:
                if (value is < 0 or > 1) return (null, "p² must be between 0 and 1.");
                p = Math.Sqrt(value);
                steps.Add(new("Given", $"p² = {Num(value)}"));
                steps.Add(new("The A allele", $"p = √p² = √{Num(value)} = {Num(p)}"));
                steps.Add(new("The a allele", $"q = 1 − p = 1 − {Num(p)} = {Num(1 - p)}"));
                notes.Add("p = √p² holds only if the population is in Hardy–Weinberg equilibrium.");
                break;
            default:
            {
                if (value is < 0 or > 0.5) return (null, "2pq must be between 0 and 0.5 (its largest value, when p = q = 0.5).");
                var root = Math.Sqrt(Math.Max(0, 1 - 2 * value));
                p = (1 + root) / 2;
                steps.Add(new("Given", $"2pq = {Num(value)}, with q = 1 − p, so p² − p + {Num(value / 2)} = 0"));
                steps.Add(new("The A allele", $"p = (1 ± √(1 − 2 × {Num(value)})) ÷ 2 = {Num(p)}{(root > 0 ? $" or {Num(1 - p)}" : "")}"));
                steps.Add(new("The a allele", $"q = 1 − p = {Num(1 - p)}"));
                if (root > 0)
                {
                    otherP = 1 - p;
                    notes.Add($"2pq alone can't say which allele is commoner: p = {Num(p)} or p = {Num(1 - p)}. The larger p is shown.");
                }
                notes.Add($"2pq = {Num(value)} assumes the population is in Hardy–Weinberg equilibrium.");
                break;
            }
        }
        var qq = 1 - p;
        steps.AddRange(GenotypeSteps(p, qq));
        return (new HWCalc(p, qq, steps, otherP, notes, null), null);
    }

    /// <summary>Everything from a sample's genotype counts (allele counting needs no assumption of equilibrium).</summary>
    public static (HWCalc? Calc, string? Error) Calculate(HWCounts c)
    {
        if (c.AA < 0 || c.Aa < 0 || c.aa < 0) return (null, "Counts can't be negative.");
        if (c.N <= 0) return (null, "Enter at least one individual.");
        var steps = new List<HWStep> { new("The sample", $"N = {c.AA} + {c.Aa} + {c.aa} = {c.N}") };
        var p = c.P;
        steps.Add(new("The A allele", $"p = (2 × {c.AA} + {c.Aa}) ÷ (2 × {c.N}) = {2 * c.AA + c.Aa} ÷ {2 * c.N} = {Num(p)}"));
        steps.Add(new("The a allele", $"q = 1 − p = 1 − {Num(p)} = {Num(1 - p)}"));
        double n = c.N;
        steps.Add(new("Observed", $"AA {Num(c.AA / n)} · Aa {Num(c.Aa / n)} · aa {Num(c.aa / n)}"));
        var all = GenotypeSteps(p, 1 - p);
        all[0] = new("Expected AA", all[0].Math + $" → {Num(p * p * n)} of {c.N}");
        all[1] = new("Expected Aa", all[1].Math + $" → {Num(2 * p * (1 - p) * n)} of {c.N}");
        all[2] = new("Expected aa", all[2].Math + $" → {Num((1 - p) * (1 - p) * n)} of {c.N}");
        steps.AddRange(all);
        var notes = new[] { "Counting alleles gives p and q without assuming equilibrium. The Test page asks whether the counts fit p², 2pq and q²." };
        return (new HWCalc(p, 1 - p, steps, null, notes, c), null);
    }

    /// <summary>p², 2pq, q², the check that they add up to 1, and carriers per 1,000.</summary>
    private static List<HWStep> GenotypeSteps(double p, double q)
    {
        double p2 = p * p, h = 2 * p * q, q2 = q * q;
        return new List<HWStep>
        {
            new("AA (p²)", $"p² = {Num(p)}² = {Num(p2)}"),
            new("Aa (2pq)", $"2pq = 2 × {Num(p)} × {Num(q)} = {Num(h)}"),
            new("aa (q²)", $"q² = {Num(q)}² = {Num(q2)}"),
            new("Check", $"p² + 2pq + q² = {Num(p2)} + {Num(h)} + {Num(q2)} = {Num(p2 + h + q2)}"),
            new("Carriers", $"2pq × 1,000 = {Carriers(h * 1000)} heterozygous carriers per 1,000 people"),
        };
    }

    /// <summary>"480", "19.8", "0.2".</summary>
    public static string Carriers(double x) => x >= 10 ? HWFormat.Fixed(x, 0) : (x >= 1 ? HWFormat.Fixed(x, 1) : Num(x));

    /// <summary>A frequency as the working shows it: up to four decimals, at least two ("0.40", "0.4225", "1.00"); a tiny one
    /// in powers of ten ("2.50 × 10⁻⁵").</summary>
    public static string Num(double x, int places = 4)
    {
        if (x == 0) return "0";
        if (Math.Abs(x) < 0.0001)
        {
            var e = (int)Math.Floor(Math.Log10(Math.Abs(x)));
            var m = x / Math.Pow(10, e);
            return HWFormat.Fixed(m, 2) + " × 10" + HWFormat.Superscript(e);
        }
        var s = HWFormat.Fixed(x, places);
        while (s.EndsWith('0') && s.IndexOf('.') is var dot && dot >= 0 && s.Length - dot > 3) s = s[..^1];
        return s;
    }

    /// <summary>A typed number as typed: 16 → "16", 2.5 → "2.5".</summary>
    public static string Plain(double x) => x == Math.Round(x) && Math.Abs(x) < 1e12 ? HWFormat.Fixed(x, 0) : HWFormat.G(x);

    /// <summary>100 people by genotype (AA, Aa, aa) for the dot grid: the largest-remainder rounding, so they always add to 100.</summary>
    public static int[] People(IReadOnlyList<double> freqs)
    {
        var scaled = freqs.Select(f => Math.Max(0, f) * 100).ToArray();
        var outs = scaled.Select(s => (int)Math.Floor(s)).ToArray();
        var left = 100 - outs.Sum();
        var order = Enumerable.Range(0, scaled.Length).ToList();
        order.Sort((a, b) =>
        {
            double ra = scaled[a] - outs[a], rb = scaled[b] - outs[b];
            return ra != rb ? rb.CompareTo(ra) : a.CompareTo(b);
        });
        for (var i = 0; left > 0 && order.Count > 0; i++, left--) outs[order[i % order.Count]] += 1;
        return outs;
    }

    // ---- the HWE test (§5.4.2) ----

    /// <summary>The test of a sample against Hardy–Weinberg proportions from its own allele frequency: df 1 by default (3
    /// classes − 1 − 1 estimated frequency); some courses use 2.</summary>
    public static (HWTest? Test, string? Error) Test(HWCounts c, int df = 1)
    {
        if (c.AA < 0 || c.Aa < 0 || c.aa < 0 || c.N <= 0) return (null, "Enter the three genotype counts.");
        double p = c.P, q = 1 - p;
        if (p <= 0 || q <= 0) return (null, "Only one allele is in the sample, so every genotype is as expected: there is nothing to test.");
        double n = c.N;
        var expected = new[] { n * p * p, n * 2 * p * q, n * q * q };
        var r = BioChiSquare.Test(new double[] { c.AA, c.Aa, c.aa }, expected, df);
        return (new HWTest(c, p, q, r), null);
    }
}

/// <summary>The HWE test's working.</summary>
public sealed record HWTest(HWCounts Counts, double P, double Q, BioChiSquare.Result Result)
{
    public double[] Expected => Result.Expected;

    public string NullHypothesis =>
        $"The population is in Hardy–Weinberg equilibrium: the genotype counts fit p², 2pq and q² from the sample's own allele frequencies (p = {HardyWeinberg.Num(P)}, q = {HardyWeinberg.Num(Q)}).";

    public bool LowExpected => Expected.Any(e => e < 5);

    public string PText => Result.P < 0.001 ? "p = " + HWFormat.Fixed(Result.P, 5) : "p = " + HWFormat.Fixed(Result.P, 3);

    /// <summary>What the conclusion means here.</summary>
    public string Detail
    {
        get
        {
            if (!Result.Reject) return "The counts fit Hardy–Weinberg proportions as well as chance allows. This does not prove the population is in equilibrium.";
            return Counts.Aa < Expected[1]
                ? "There are fewer heterozygotes than equilibrium predicts — what inbreeding, or a population split into groups, gives. The population is probably not in equilibrium."
                : "There are more heterozygotes than equilibrium predicts — what heterozygote advantage, or mixing of two populations, can give. The population is probably not in equilibrium.";
        }
    }

    public const string DfNote = "Some courses use df = 2 (classes − 1). Population genetics uses 1, because p was estimated from the same data.";
}

// ---- the simulation (§4.7 Simulate, §5.4.3) ----

/// <summary>The simulation's settings: the population size N (diploid; 2N alleles drawn each generation), or null for an
/// infinite population (no drift); the starting p; the fitnesses; mutation A → a (u) and a → A (v) per generation; the
/// share of each generation's alleles from migrants (m) and their p; the inbreeding coefficient F (0: random mating); and
/// the runs.</summary>
public sealed record HWSimParams
{
    public int? Size { get; init; } = 100;
    public double P0 { get; init; } = 0.5;
    public double WAA { get; init; } = 1;
    public double WAa { get; init; } = 1;
    public double Waa { get; init; } = 1;
    public double U { get; init; }
    public double V { get; init; }
    public double M { get; init; }
    public double Pm { get; init; } = 0.5;
    public double F { get; init; }
    public int Replicates { get; init; } = 5;
    public ulong Seed { get; init; } = 1;
    public int Generations { get; init; } = 100;

    public const int MinSize = 10, MaxSize = 10_000;
    public static readonly int[] GenerationChoices = { 25, 50, 100, 200 };

    /// <summary>The settings as text, for Prefs (SimplBio.popgen.sim).</summary>
    [JsonIgnore] public string Encoded => JsonSerializer.Serialize(this);

    public static HWSimParams? Decode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { return JsonSerializer.Deserialize<HWSimParams>(s); } catch (JsonException) { return null; }
    }
}

/// <summary>A generation of a population: p after it, the genotype frequencies after selection (P′, the bars), and the
/// allele count k drawn when N is finite.</summary>
public sealed record HWGeneration(double P, double AA, double Aa, double aa, int? K);

/// <summary>One replicate population's history, and the generation in which mean fitness reached 0, if it did.</summary>
public sealed record HWPopulation(ulong Seed, List<HWGeneration> Generations, int? ExtinctAt)
{
    public bool SameAs(HWPopulation o) => Seed == o.Seed && ExtinctAt == o.ExtinctAt && Generations.SequenceEqual(o.Generations);
}

/// <summary>A selection preset (§4.7): fitnesses w_AA, w_Aa, w_aa.</summary>
public enum HWSelection { Neutral, AgainstRecessive, AgainstDominant, HeterozygoteAdvantage, RecessiveLethal }

public static class HWSelections
{
    public static readonly HWSelection[] All = Enum.GetValues<HWSelection>();

    public static string Key(this HWSelection s) => s switch
    {
        HWSelection.Neutral => "neutral", HWSelection.AgainstRecessive => "againstRecessive", HWSelection.AgainstDominant => "againstDominant",
        HWSelection.HeterozygoteAdvantage => "heterozygoteAdvantage", _ => "recessiveLethal",
    };

    public static string Title(this HWSelection s) => s switch
    {
        HWSelection.Neutral => "None", HWSelection.AgainstRecessive => "Against the recessive", HWSelection.AgainstDominant => "Against the dominant",
        HWSelection.HeterozygoteAdvantage => "Heterozygote advantage", _ => "Recessive lethal",
    };

    public static (double AA, double Aa, double aa) Fitness(this HWSelection s) => s switch
    {
        HWSelection.Neutral => (1, 1, 1), HWSelection.AgainstRecessive => (1, 1, 0.6), HWSelection.AgainstDominant => (0.6, 0.6, 1),
        HWSelection.HeterozygoteAdvantage => (0.8, 1, 0.6), _ => (1, 1, 0),
    };

    /// <summary>The preset these fitnesses are, if any.</summary>
    public static HWSelection? Matching(HWSimParams s) =>
        All.Cast<HWSelection?>().FirstOrDefault(x => x!.Value.Fitness() is var f && f.AA == s.WAA && f.Aa == s.WAa && f.aa == s.Waa);

    public static HWSimParams With(this HWSimParams s, HWSelection preset)
    {
        var f = preset.Fitness();
        return s with { WAA = f.AA, WAa = f.Aa, Waa = f.aa };
    }
}

/// <summary>One of the five conditions (§5.4.1), whether the settings keep it, and how they break it.</summary>
public sealed record HWAssumption(string Id, string Title, bool Holds, string Detail);

public static class HWSimulation
{
    /// <summary>One generation from p (§5.4.3, in this order): mutation, migration, mating with inbreeding, selection,
    /// drift. Null when mean fitness is 0 (the population dies out).</summary>
    public static HWGeneration? Step(double p, HWSimParams s, ref BioRandom rng)
    {
        // 1. Mutation, in the gametes
        var p1 = p * (1 - s.U) + (1 - p) * s.V;
        // 2. Migration
        var p2 = (1 - s.M) * p1 + s.M * s.Pm;
        var q2 = 1 - p2;
        // 3. Mating with inbreeding F
        var aA = p2 * p2 + s.F * p2 * q2;
        var het = 2 * p2 * q2 * (1 - s.F);
        var rec = q2 * q2 + s.F * p2 * q2;
        // 4. Selection
        var wbar = aA * s.WAA + het * s.WAa + rec * s.Waa;
        if (!(wbar > 0)) return null;
        double sAA = aA * s.WAA / wbar, sAa = het * s.WAa / wbar, saa = rec * s.Waa / wbar;
        var p3 = sAA + sAa / 2;
        // 5. Drift: the next generation's 2N alleles drawn one by one
        if (s.Size is not { } n) return new HWGeneration(p3, sAA, sAa, saa, null);
        var k = 0;
        for (var i = 0; i < 2 * n; i++)
            if (rng.Uniform() < p3) k++;
        // 6. Recorded
        return new HWGeneration((double)k / (2 * n), sAA, sAa, saa, k);
    }

    /// <summary>Generation 0: p₀, its genotypes as mating with F makes them, and its allele count if it is a whole number.</summary>
    public static HWGeneration Start(HWSimParams s)
    {
        double p = s.P0, q = 1 - p;
        int? k = null;
        if (s.Size is { } n)
        {
            var x = p * (2 * n);
            if (Math.Abs(x - Math.Round(x)) < 1e-9) k = (int)Math.Round(x);
        }
        return new HWGeneration(p, p * p + s.F * p * q, 2 * p * q * (1 - s.F), q * q + s.F * p * q, k);
    }

    /// <summary>Every replicate (replicate r, from 0, drawn from seed S + r), each for the asked generations or until it dies
    /// out. An infinite population has no drift, so it is run once. <paramref name="cancelled"/> is asked between generations.</summary>
    public static List<HWPopulation> Run(HWSimParams s, Func<bool>? cancelled = null)
    {
        var count = s.Size == null ? 1 : Math.Max(1, Math.Min(10, s.Replicates));
        var outs = new List<HWPopulation>();
        for (var r = 0; r < count; r++)
        {
            var seed = unchecked(s.Seed + (ulong)r);
            var rng = new BioRandom(seed);
            var gens = new List<HWGeneration> { Start(s) };
            int? extinct = null;
            var p = s.P0;
            for (var t = 1; t <= Math.Max(1, s.Generations); t++)
            {
                if (cancelled?.Invoke() == true) return outs;
                if (Step(p, s, ref rng) is not { } g) { extinct = t; break; }
                gens.Add(g);
                p = g.P;
            }
            outs.Add(new HWPopulation(seed, gens, extinct));
        }
        return outs;
    }

    /// <summary>The five conditions, and which the settings break.</summary>
    public static List<HWAssumption> Assumptions(HWSimParams s)
    {
        static string F(double x) => HardyWeinberg.Num(x);
        return new List<HWAssumption>
        {
            new("selection", "No selection", s.WAA == s.WAa && s.WAa == s.Waa, $"Fitnesses {F(s.WAA)}, {F(s.WAa)}, {F(s.Waa)}: some genotypes leave more offspring."),
            new("mutation", "No mutation", s.U == 0 && s.V == 0, $"u = {F(s.U)}, v = {F(s.V)}: alleles change into each other."),
            new("migration", "No migration", s.M == 0, $"m = {F(s.M)}: migrants bring alleles at p = {F(s.Pm)}."),
            new("size", "Very large population", s.Size == null, $"N = {s.Size ?? 0}: chance alone changes allele frequencies (genetic drift)."),
            new("mating", "Random mating", s.F == 0, $"F = {F(s.F)}: relatives mate, so there are more homozygotes than p² and q²."),
        };
    }

    /// <summary>What breaking those conditions does.</summary>
    public static string Verdict(HWSimParams s)
    {
        var broken = Assumptions(s).Where(a => !a.Holds).Select(a => a.Id).ToList();
        if (broken.Count == 0) return "All five hold: allele and genotype frequencies stay the same — no evolution, whichever allele is dominant.";
        if (broken.SequenceEqual(new[] { "mating" })) return "→ Genotype frequencies change (more homozygotes), but allele frequencies stay the same.";
        return "→ Allele frequencies can change (evolution).";
    }
}

/// <summary>A number as typed: "0.16", "16%", "1/2500" ("%" makes a frequency of a percent where a frequency is wanted).</summary>
public static class HWInput
{
    public static double? Number(string text, bool percent = false)
    {
        var t = text.Trim().Replace(",", "", StringComparison.Ordinal);
        if (t.Length == 0) return null;
        var scale = 1.0;
        if (t.EndsWith('%'))
        {
            t = t[..^1].TrimEnd();
            if (!percent) scale = 0.01;
        }
        var slash = t.IndexOf('/');
        if (slash >= 0)
        {
            if (BioText.Number(t[..slash].Trim()) is not { } a || BioText.Number(t[(slash + 1)..].Trim()) is not { } b || b == 0) return null;
            return a / b * scale * (percent ? 100 : 1);
        }
        return BioText.Number(t, unicodeMinus: true) is { } v ? v * scale : null;
    }

    public static int? Count(string text)
    {
        var t = text.Trim().Replace(",", "", StringComparison.Ordinal);
        return BioText.Int(t) is { } n && n >= 0 ? n : null;
    }
}

// ---- what the tool keeps ----

/// <summary>
/// Hardy–Weinberg's state, kept across its views (the Mac's HardyWeinbergState): the calculator's given and value, the
/// test's counts and df, the simulation's settings and the populations last run. Remembered between launches through
/// <see cref="Persist"/> (the view hands it the tool's Prefs: SimplBio.popgen.*).
/// </summary>
public static class HardyWeinbergModel
{
    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    /// <summary>The simulation's settings changed: the view runs it again (off the UI thread).</summary>
    public static event Action? SimChanged;

    private static Func<string, string?>? read;
    private static Action<string, string>? write;

    /// <summary>Where the tool's settings are kept (Prefs SimplBio.popgen.&lt;field&gt;), read back the first time.</summary>
    public static void Persist(Func<string, string?> read, Action<string, string> write)
    {
        if (HardyWeinbergModel.read != null) return;
        HardyWeinbergModel.read = read;
        HardyWeinbergModel.write = write;
        Given = HWGivens.FromKey(read("given")) ?? HWGiven.Q2;
        Value = read("value") ?? "0.16";
        var t = (read("test") ?? "50,30,20").Split(',');
        if (t.Length == 3) (TestAA, TestAa, Testaa) = (t[0], t[1], t[2]);
        Sim = HWSimParams.Decode(read("sim")) ?? new HWSimParams();
    }

    private static void Save(string field, string value) => write?.Invoke(field, value);

    // ---- Calculate ----

    public static HWGiven Given { get; private set; } = HWGiven.Q2;
    public static string Value { get; private set; } = "0.16";
    public static string CountAA { get; private set; } = "50";
    public static string CountAa { get; private set; } = "30";
    public static string Countaa { get; private set; } = "20";

    public static void SetGiven(HWGiven g)
    {
        if (g == Given) return;
        Given = g;
        Save("given", g.Key());
        Tell();
    }

    public static void SetValue(string v)
    {
        if (v == Value) return;
        Value = v;
        Save("value", v);
        Tell();
    }

    public static void SetCounts(string aA, string het, string rec)
    {
        (CountAA, CountAa, Countaa) = (aA, het, rec);
        Tell();
    }

    /// <summary>The calculator's answer for what is typed, or what is wrong with it.</summary>
    public static (HWCalc? Calc, string? Error) Calculation
    {
        get
        {
            if (Given == HWGiven.Counts)
            {
                if (HWInput.Count(CountAA) is not { } a || HWInput.Count(CountAa) is not { } b || HWInput.Count(Countaa) is not { } c)
                    return (null, "Counts are whole numbers, 0 or more.");
                return HardyWeinberg.Calculate(new HWCounts(a, b, c));
            }
            if (HWInput.Number(Value, Given.IsPercent()) is not { } v) return (null, Value.Trim().Length == 0 ? null : $"Enter a number, like {Given.Placeholder()}.");
            return HardyWeinberg.Calculate(Given, v);
        }
    }

    // ---- Test ----

    public static string TestAA { get; private set; } = "50";
    public static string TestAa { get; private set; } = "30";
    public static string Testaa { get; private set; } = "20";
    public static int Df { get; private set; } = 1;

    public static void SetTest(string aA, string het, string rec)
    {
        (TestAA, TestAa, Testaa) = (aA, het, rec);
        Save("test", string.Join(",", aA, het, rec));
        Tell();
    }

    public static void SetDf(int df)
    {
        if (df == Df || df < 1) return;
        Df = df;
        Tell();
    }

    public static (HWTest? Test, string? Error) Test
    {
        get
        {
            if (HWInput.Count(TestAA) is not { } a || HWInput.Count(TestAa) is not { } b || HWInput.Count(Testaa) is not { } c)
                return (null, "Counts are whole numbers, 0 or more.");
            return HardyWeinberg.Test(new HWCounts(a, b, c), Df);
        }
    }

    // ---- Simulate ----

    public static HWSimParams Sim { get; private set; } = new();
    /// <summary>The populations of the last finished run (for <see cref="RunFor"/>'s settings).</summary>
    public static List<HWPopulation> Populations { get; private set; } = new();
    /// <summary>The settings the populations were run with (null: none run yet).</summary>
    public static HWSimParams? RunFor { get; private set; }
    /// <summary>The generation under the pointer on the chart (null: the last).</summary>
    public static int? Hovered { get; private set; }

    public static bool Running => !Equals(RunFor, Sim);

    public static void SetSim(HWSimParams s)
    {
        if (s == Sim) return;
        Sim = s;
        Save("sim", s.Encoded);
        SimChanged?.Invoke();
        Tell();
    }

    /// <summary>A run finished: kept if it was for the settings as they are now.</summary>
    public static void Finished(HWSimParams s, List<HWPopulation> pops)
    {
        if (s != Sim) return;
        RunFor = s;
        Populations = pops;
        if (Hovered is { } h && h > s.Generations) Hovered = null;
        Tell();
    }

    public static void SetHovered(int? g)
    {
        if (g == Hovered) return;
        Hovered = g;
        Tell();
    }

    /// <summary>The generation shown in the bars and the readout: the one under the pointer, else the last.</summary>
    public static int Shown
    {
        get
        {
            var last = Populations.Count == 0 ? 0 : Populations.Max(p => p.Generations.Count - 1);
            return Math.Min(Hovered ?? last, Math.Max(last, 0));
        }
    }

    // ---- a launch's state, a hand-off ----

    private static bool launched;

    /// <summary>What a launch asked (--tool popgen --tool-state calculate:q2=0.16 …), once: <paramref name="value"/> reads a
    /// "name=value" part, <paramref name="has"/> a bare one.</summary>
    public static void ApplyLaunch(Func<string, string?> value, Func<string, bool> has, string? page)
    {
        if (launched) return;
        launched = true;
        if (HWGivens.FromKey(value("given")) is { } g) Given = g;
        if (value("value") is { } v) Value = v;
        foreach (var x in HWGivens.All.Where(x => x != HWGiven.Counts))
            if (value(x.Key()) is { } xv) { Given = x; Value = xv; }
        if (value("counts") is { } c && c.Split(',') is { Length: 3 } parts)
        {
            (TestAA, TestAa, Testaa) = (parts[0], parts[1], parts[2]);
            (CountAA, CountAa, Countaa) = (parts[0], parts[1], parts[2]);
            if (page == "calculate") Given = HWGiven.Counts;
        }
        if (BioText.Int(value("df") ?? "") is { } df && df >= 1) Df = df;
        var s = Sim;
        if (has("infinite")) s = s with { Size = null };
        if (BioText.Int(value("n") ?? "") is { } n) s = s with { Size = Math.Clamp(n, HWSimParams.MinSize, HWSimParams.MaxSize) };
        if (BioText.Number(value("p0") ?? value("p") ?? "") is { } p) s = s with { P0 = Math.Clamp(p, 0, 1) };
        if (BioText.Int(value("reps") ?? "") is { } r) s = s with { Replicates = Math.Clamp(r, 1, 10) };
        if (ulong.TryParse(value("seed") ?? "", NumberStyles.None, CultureInfo.InvariantCulture, out var seed)) s = s with { Seed = seed };
        if (BioText.Int(value("gens") ?? "") is { } gens) s = s with { Generations = Math.Clamp(gens, 1, 200) };
        if (HWSelections.All.Cast<HWSelection?>().FirstOrDefault(x => x!.Value.Key() == value("preset")) is { } preset) s = s.With(preset);
        if (BioText.Number(value("F") ?? "") is { } f) s = s with { F = Math.Clamp(f, 0, 1) };
        if (BioText.Number(value("u") ?? "") is { } u) s = s with { U = u };
        if (BioText.Number(value("v") ?? "") is { } vv) s = s with { V = vv };
        if (BioText.Number(value("m") ?? "") is { } m) s = s with { M = m };
        if (BioText.Number(value("pm") ?? "") is { } pm) s = s with { Pm = pm };
        SetSim(s);
        if (BioText.Int(value("hover") ?? "") is { } h) Hovered = h;
        Tell();
    }

    /// <summary>What the pin handed over: given and value, or counts; the page they belong on, if any.</summary>
    public static string? TakeHandoff(Func<string, string?> take)
    {
        string? page = null;
        if (HWGivens.FromKey(take("given")) is { } g) { Given = g; page = "calculate"; }
        if (take("value") is { } v) Value = v;
        if (take("counts") is { } c && c.Split(',') is { Length: 3 } parts)
        {
            (TestAA, TestAa, Testaa) = (parts[0], parts[1], parts[2]);
            page = "test";
        }
        if (page != null) Tell();
        return page;
    }
}

public static partial class BioSelfCheck
{
    private static string Pf(double x, int d) => HWFormat.Fixed(x, d);

    /// <summary>Hardy–Weinberg's own bio-check lines: every vector of §5.4.2–§5.4.3, the drift sequence included (the Mac's
    /// popgenChecks, line for line).</summary>
    static partial void PopgenChecks(BioCheckReport r)
    {
        // (§5.4.2's second vector: q² = 0.16 → q = 0.40, p = 0.60, 2pq = 0.48, each step written out)
        if (HardyWeinberg.Calculate(HWGiven.Q2, 0.16).Calc is { } c)
            r.Check("hw.q2=0.16", $"q{HardyWeinberg.Num(c.Q)},p{HardyWeinberg.Num(c.P)},2pq{HardyWeinberg.Num(c.TwoPQ)}",
                Math.Abs(c.Q - 0.4) < 1e-12 && Math.Abs(c.P - 0.6) < 1e-12 && Math.Abs(c.TwoPQ - 0.48) < 1e-12
                && c.Steps.Any(s => s.Math == "q = √q² = √0.16 = 0.40") && c.Steps.Any(s => s.Math == "2pq = 2 × 0.60 × 0.40 = 0.48")
                && c.Steps.Any(s => s.Math.StartsWith("2pq × 1,000 = 480 ", StringComparison.Ordinal)));
        else r.Check("hw.q2=0.16", "error", false);
        // (every other way in gives the same population)
        var ways = new (HWGiven G, double V)[] { (HWGiven.P, 0.6), (HWGiven.Q, 0.4), (HWGiven.P2, 0.36), (HWGiven.TwoPQ, 0.48), (HWGiven.RecessivePercent, 16), (HWGiven.DominantPercent, 84) };
        var wrong = ways.Where(w => HardyWeinberg.Calculate(w.G, w.V).Calc is not { } x || Math.Abs(x.P - 0.6) > 1e-12 || Math.Abs(x.Q2 - 0.16) > 1e-12).Select(w => w.G.Key()).ToList();
        var twoRoots = HardyWeinberg.Calculate(HWGiven.TwoPQ, 0.48).Calc?.OtherP;
        r.Check("hw.calculator", wrong.Count == 0 ? "6ways" : string.Join(",", wrong), wrong.Count == 0 && twoRoots != null && Math.Abs((twoRoots ?? 0) - 0.4) < 1e-12);
        var impossible = new[] { HardyWeinberg.Calculate(HWGiven.Q2, 1.2).Error, HardyWeinberg.Calculate(HWGiven.P, -0.1).Error, HardyWeinberg.Calculate(HWGiven.TwoPQ, 0.6).Error,
            HardyWeinberg.Calculate(HWGiven.RecessivePercent, 120).Error };
        r.Check("hw.impossible", $"{impossible.Count(e => e != null)}said", impossible.All(e => e != null) && impossible[0] == "q² must be between 0 and 1.");
        // (the dot grid's 100 people always add to 100)
        int[] grid = HardyWeinberg.People(new[] { 0.36, 0.48, 0.16 }), grid2 = HardyWeinberg.People(new[] { 0.4225, 0.455, 0.1225 });
        r.Check("hw.people", string.Join(",", grid) + "|" + string.Join(",", grid2), grid.SequenceEqual(new[] { 36, 48, 16 }) && grid2.SequenceEqual(new[] { 42, 46, 12 }));
        r.Same("hw.format", string.Join(",", HardyWeinberg.Num(1), HardyWeinberg.Num(0.4225), HardyWeinberg.Num(0.000025), HardyWeinberg.Carriers(19.8), HardyWeinberg.Carriers(4.56)),
            "1.00,0.4225,2.50 × 10⁻⁵,20,4.6");

        // (§5.4.2's first vector: 50, 30, 20 → p 0.65, expected 42.25, 45.50, 12.25, χ² 11.6049, p 0.00066 → reject)
        var sample = new HWCounts(50, 30, 20);
        if (HardyWeinberg.Calculate(sample).Calc is { } cc && HardyWeinberg.Test(sample).Test is { } t)
        {
            r.Check("hw.counts", $"p{HardyWeinberg.Num(cc.P)},q{HardyWeinberg.Num(cc.Q)}", Math.Abs(cc.P - 0.65) < 1e-12 && Math.Abs(cc.Q - 0.35) < 1e-12);
            r.Check("hw.test", $"p{Pf(t.P, 4)},q{Pf(t.Q, 4)},e{Pf(t.Expected[0], 2)}/{Pf(t.Expected[1], 2)}/{Pf(t.Expected[2], 2)},chi{Pf(t.Result.Chi2, 4)},P{Pf(t.Result.P, 5)}",
                Pf(t.P, 4) + " " + Pf(t.Q, 4) == "0.6500 0.3500" && t.Expected.Zip(new[] { 42.25, 45.5, 12.25 }).All(z => Math.Abs(z.First - z.Second) < 1e-9)
                && Math.Abs(t.Result.Chi2 - 11.6049) < 5e-5 && Math.Abs(t.Result.P - 0.00066) < 5e-6 && t.Result.Df == 1 && t.Result.Reject
                && t.Result.Critical == 3.84 && t.Detail.Contains("fewer heterozygotes", StringComparison.Ordinal));
            var two = HardyWeinberg.Test(sample, 2).Test;
            r.Check("hw.test.df2", Pf(two?.Result.Critical ?? 0, 2), two?.Result.Df == 2 && two?.Result.Critical == 5.99);
        }
        else r.Check("hw.test", "error", false);
        r.Check("hw.test.monomorphic", "said", HardyWeinberg.Test(new HWCounts(10, 0, 0)).Test == null);

        // (§5.4.3: recessive lethal from p = 0.5, infinite N: q = 1/3, 1/4, 1/5)
        var lethal = new HWSimParams { Size = null, P0 = 0.5, Waa = 0, Generations = 3 };
        var lq = HWSimulation.Run(lethal).FirstOrDefault()?.Generations.Skip(1).Select(g => 1 - g.P).ToList() ?? new List<double>();
        r.Check("hw.sim.lethal", string.Join(",", lq.Select(q => Pf(q, 6))), lq.Count == 3 && lq.Zip(new[] { 1.0 / 3, 0.25, 0.2 }).All(z => Math.Abs(z.First - z.Second) < 1e-6));
        static HWGeneration? Once(HWSimParams s) => HWSimulation.Run(s with { Size = null, Generations = 1 }).FirstOrDefault()?.Generations.LastOrDefault();
        r.Near("hw.sim.mutation", Once(new HWSimParams { P0 = 0.9, U = 1e-4 })?.P ?? 0, 0.89991, 1e-12, 5);
        r.Near("hw.sim.migration", Once(new HWSimParams { P0 = 0.2, M = 0.1, Pm = 0.8 })?.P ?? 0, 0.26, 1e-12, 2);
        var inbred = Once(new HWSimParams { P0 = 0.6, F = 0.25 });
        r.Check("hw.sim.inbreeding", $"{Pf(inbred?.AA ?? 0, 2)},{Pf(inbred?.Aa ?? 0, 2)},{Pf(inbred?.aa ?? 0, 2)},p{Pf(inbred?.P ?? 0, 2)}",
            Math.Abs((inbred?.AA ?? 0) - 0.42) < 1e-12 && Math.Abs((inbred?.Aa ?? 0) - 0.36) < 1e-12 && Math.Abs((inbred?.aa ?? 0) - 0.22) < 1e-12 && Math.Abs((inbred?.P ?? 0) - 0.6) < 1e-12);
        // (drift: N = 10, p₀ = 0.5, seed 7, nothing else: k = 10, 10, 12, 13, 12, 10 for generations 0–5)
        var drift = new HWSimParams { Size = 10, P0 = 0.5, Seed = 7, Replicates = 2, Generations = 5 };
        var pops = HWSimulation.Run(drift);
        var ks = pops.FirstOrDefault()?.Generations.Where(g => g.K != null).Select(g => g.K!.Value).ToList() ?? new List<int>();
        r.Check("hw.sim.drift", string.Join(",", ks), ks.SequenceEqual(new[] { 10, 10, 12, 13, 12, 10 }) && pops.FirstOrDefault()?.Seed == 7);
        // (replicate 1 is seed 8's population; the same seed gives the same populations every time)
        var eight = HWSimulation.Run(drift with { Seed = 8, Replicates = 1 }).FirstOrDefault();
        var again = HWSimulation.Run(drift);
        r.Check("hw.sim.replicates", $"{pops.Count}", pops.Count == 2 && eight != null && pops[^1].Generations.SequenceEqual(eight.Generations)
            && again.Count == pops.Count && again.Zip(pops).All(z => z.First.SameAs(z.Second)) && !pops[0].Generations.SequenceEqual(pops[1].Generations));
        // (no forces: p stays put, the dominant allele no more likely to spread than the recessive; mean fitness 0: extinct)
        var stillP = HWSimulation.Run(new HWSimParams { Size = null, P0 = 0.3, Generations = 200 }).FirstOrDefault()?.Generations.Select(g => g.P).ToList() ?? new List<double>();
        var dead = HWSimulation.Run(new HWSimParams { Size = 50, P0 = 0.5, WAA = 0, WAa = 0, Waa = 0, Generations = 10 }).FirstOrDefault();
        r.Check("hw.sim.no-forces", Pf(stillP.LastOrDefault(), 6), stillP.Count == 201 && stillP.All(p => Math.Abs(p - 0.3) < 1e-12) && dead?.ExtinctAt == 1);
        // (a big run stays quick: N = 10,000, 10 replicates, 200 generations)
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var bigRun = HWSimulation.Run(new HWSimParams { Size = 10_000, P0 = 0.5, Replicates = 10, Generations = 200 });
        var ms = watch.Elapsed.TotalMilliseconds;
        r.Check("hw.sim.size", $"{bigRun.Count}x{bigRun.FirstOrDefault()?.Generations.Count ?? 0}", bigRun.Count == 10 && bigRun.All(p => p.Generations.Count == 201) && ms < 20_000);
        // (the five conditions: all hold with no forces; inbreeding alone moves genotypes, not alleles)
        var calm = new HWSimParams { Size = null };
        var onlyF = calm with { F = 0.3 };
        var finite = new HWSimParams { Size = 100 };
        var finiteBroken = HWSimulation.Assumptions(finite).Where(a => !a.Holds).Select(a => a.Id).ToList();
        r.Check("hw.assumptions", string.Join(",", finiteBroken),
            HWSimulation.Assumptions(calm).All(a => a.Holds) && HWSimulation.Verdict(onlyF).Contains("allele frequencies stay the same", StringComparison.Ordinal)
            && finiteBroken.SequenceEqual(new[] { "size" }) && HWSimulation.Verdict(finite).Contains("Allele frequencies can change", StringComparison.Ordinal)
            && HWSelections.Matching(lethal) == HWSelection.RecessiveLethal && HWSelections.Matching(calm) == HWSelection.Neutral);
    }
}
