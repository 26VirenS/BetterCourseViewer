namespace Simpl.Tools;

// DNA & RNA's own model (spec §4.2): what the tool keeps without a window — the strand the student typed (the Sequence
// page edits it; the double helix draws its first 40 base pairs), how the helix is drawn and what is laid over it, how
// far it is unzipped and what is selected, the nucleotide shown, the replication fork's step-through and the RNA page's
// view — in a static class, so it outlives the view being built again (the tools' rule: a view subscribes to Changed on
// Loaded and lets go on Unloaded). Plain .NET, no WPF, so the unit tests reach it. The scenes and drawings it describes
// are made in DnaHelix.cs, DnaNucleotides.cs, DnaReplication.cs and DnaRna.cs; its bio-check lines are at the foot.

/// <summary>How the double helix is drawn (§4.2): the cartoon, ball and stick (heavy atoms, CPK), or space filling.</summary>
public enum DnaStyle { Cartoon, Atoms, SpaceFill }

/// <summary>What the RNA page shows.</summary>
public enum DnaRnaView { Mrna, Cloverleaf, TrnaModel, Ribosomes }

public static class DnaStyles
{
    public static string Label(this DnaStyle s) => s switch { DnaStyle.Cartoon => "Cartoon", DnaStyle.Atoms => "Atoms", _ => "Space filling" };
    public static string Key(this DnaStyle s) => s switch { DnaStyle.Cartoon => "cartoon", DnaStyle.Atoms => "atoms", _ => "space" };

    /// <summary>"atoms", "space" (or "spacefill", "space-filling"), else the cartoon.</summary>
    public static DnaStyle? Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "cartoon" => DnaStyle.Cartoon,
        "atoms" or "atom" or "ballstick" or "ball-and-stick" => DnaStyle.Atoms,
        "space" or "spacefill" or "space-filling" or "spacefilling" => DnaStyle.SpaceFill,
        _ => null,
    };

    public static string Label(this DnaRnaView v) => v switch
    {
        DnaRnaView.Mrna => "mRNA",
        DnaRnaView.Cloverleaf => "tRNA cloverleaf",
        DnaRnaView.TrnaModel => "tRNA in 3D",
        _ => "Ribosomes",
    };

    public static string Key(this DnaRnaView v) => v switch
    {
        DnaRnaView.Mrna => "mrna",
        DnaRnaView.Cloverleaf => "trna",
        DnaRnaView.TrnaModel => "trna3d",
        _ => "ribosome",
    };

    public static DnaRnaView? ParseRna(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "mrna" => DnaRnaView.Mrna,
        "trna" or "cloverleaf" => DnaRnaView.Cloverleaf,
        "trna3d" or "trna-3d" or "lshape" or "l-shape" => DnaRnaView.TrnaModel,
        "ribosome" or "ribosomes" or "rrna" => DnaRnaView.Ribosomes,
        _ => null,
    };
}

/// <summary>DNA &amp; RNA's state, kept across its views.</summary>
public static class DNAToolModel
{
    /// <summary>The helix's sequence until the student types another (§4.2: 20 bp).</summary>
    public const string DefaultSequence = "ATGCGTACGTTAGCCATGCA";
    /// <summary>The most base pairs the helix draws (§4.2: 1–40).</summary>
    public const int HelixMax = 40;
    /// <summary>The most letters the Sequence page takes (§3.7).</summary>
    public const int Cap = 3000;

    /// <summary>Something the views show changed: they draw again.</summary>
    public static event Action? Changed;

    internal static void Tell() => Changed?.Invoke();

    /// <summary>Where a setting is kept between launches (the view hands in BioPrefs.Set: SimplBio.dna.&lt;field&gt;). Not
    /// called while a launch's state is put in (<see cref="Quietly"/>).</summary>
    public static Action<string, object?>? Remember { get; set; }

    private static bool quiet;

    /// <summary>Settings changed without being remembered (what a launch asked for).</summary>
    public static void Quietly(Action a)
    {
        quiet = true;
        try { a(); }
        finally { quiet = false; }
    }

    private static void Keep(string field, object? value)
    {
        if (!quiet) Remember?.Invoke(field, value);
    }

    // ---- the strand (Sequence page; the helix draws its first 40) ----

    private static string text = DefaultSequence;
    private static BioSequenceInput input = new(DefaultSequence, BioSeqKind.Dna, Cap);
    private static string sequence = DefaultSequence;

    /// <summary>The strand as typed (strand 1, 5′→3′).</summary>
    public static string Text => text;
    /// <summary>What was typed, cleaned (§3.7): its message when letters do not belong.</summary>
    public static BioSequenceInput Input => input;
    /// <summary>The last strand that was all A, C, G, T and N (what the pages show while the field holds a mistake).</summary>
    public static string Sequence => sequence;

    /// <summary>What the double helix draws: the strand's first 40 letters, any N (an unknown base, which has no shape) left out.</summary>
    public static string HelixSequence => HelixOf(sequence);

    public static string HelixOf(string s)
    {
        var first = s.Length > HelixMax ? s[..HelixMax] : s;
        var known = new string(first.Where(c => c is 'A' or 'C' or 'G' or 'T').ToArray());
        return known.Length > 0 ? known : DefaultSequence;
    }

    /// <summary>The field's text changed: kept as typed; the strand changes only when every letter belongs.</summary>
    public static void SetText(string t)
    {
        if (t == text) return;
        text = t;
        input = new BioSequenceInput(t, BioSeqKind.Dna, Cap);
        if (input.Sequence is { } s && s != sequence)
        {
            sequence = s;
            if (Selected is { } sel && SelectedPair(sel) is { } k && k >= HelixSequence.Length) Selected = null;
            Keep("sequence", s);
        }
        Tell();
    }

    /// <summary>A new strand (Random, a hand-off, a launch): written into the field too.</summary>
    public static void SetSequence(string s)
    {
        var clean = new BioSequenceInput(s, BioSeqKind.Dna, Cap);
        if (clean.Sequence is not { } ok) return;
        text = ok;
        input = clean;
        sequence = ok;
        Selected = null;
        Keep("sequence", ok);
        Tell();
    }

    /// <summary>A random strand of <paramref name="length"/> (§4.2 Random: 20 bp, seeded by the time), SplitMix64's draws in order.</summary>
    public static string RandomStrand(ulong seed, int length = 20)
    {
        var rng = new BioRandom(seed);
        var chars = new char[Math.Max(1, length)];
        for (var i = 0; i < chars.Length; i++) chars[i] = "ACGT"[rng.Int(4)];
        return new string(chars);
    }

    // ---- the double helix ----

    private static DnaStyle style = DnaStyle.Cartoon;
    private static bool ends = true, grooves, measure, hbonds = true;
    private static double unzip;

    public static DnaStyle Style
    {
        get => style;
        set { if (style == value) return; style = value; Keep("style", value.Key()); Tell(); }
    }

    /// <summary>The 5′ and 3′ ends labelled, with arrowheads along each backbone pointing 5′→3′.</summary>
    public static bool ShowEnds
    {
        get => ends;
        set { if (ends == value) return; ends = value; Keep("ends", value); Tell(); }
    }

    /// <summary>The major and minor grooves shaded as translucent helical bands, and labelled.</summary>
    public static bool ShowGrooves
    {
        get => grooves;
        set { if (grooves == value) return; grooves = value; Keep("grooves", value); Tell(); }
    }

    /// <summary>The three measurements as dimension lines (2 nm, 0.34 nm, 3.6 nm).</summary>
    public static bool ShowMeasure
    {
        get => measure;
        set { if (measure == value) return; measure = value; Keep("measure", value); Tell(); }
    }

    public static bool ShowHBonds
    {
        get => hbonds;
        set { if (hbonds == value) return; hbonds = value; Keep("hbonds", value); Tell(); }
    }

    /// <summary>How far the helix is unzipped from its bottom end, 0…1 (§4.2: pair k opens once this passes k/N). Not remembered.</summary>
    public static double Unzip
    {
        get => unzip;
        set
        {
            var v = Math.Round(Math.Clamp(value, 0, 1), 3);
            if (Math.Abs(unzip - v) < 1e-9) return;
            unzip = v;
            Tell();
        }
    }

    /// <summary>What is selected in the helix: "bp:5" (base pair 5, both nucleotides), "bp:5:1" (strand 1's nucleotide of
    /// pair 5: its backbone pressed), "groove:major", "arrow:2", "measure:rise" — or nothing.</summary>
    public static string? Selected { get; set; }

    /// <summary>The base pair a selection names ("bp:5…" → 5).</summary>
    public static int? SelectedPair(string? id)
    {
        if (id == null || !id.StartsWith("bp:", StringComparison.Ordinal)) return null;
        var rest = id[3..];
        var colon = rest.IndexOf(':');
        return int.TryParse(colon < 0 ? rest : rest[..colon], out var k) ? k : null;
    }

    /// <summary>The strand a selection names ("bp:5:2…" → 2, "arrow:1" → 1).</summary>
    public static int? SelectedStrand(string? id)
    {
        if (id == null) return null;
        var parts = id.Split(':');
        if (parts[0] == "bp" && parts.Length >= 3 && int.TryParse(parts[2], out var s)) return s;
        if (parts[0] is "arrow" or "end" && parts.Length >= 2 && int.TryParse(parts[1], out var t)) return t;
        return null;
    }

    public static void Select(string? id)
    {
        if (Selected == id) return;
        Selected = id;
        Tell();
    }

    // ---- the nucleotides ----

    private static string nucleotide = "DA";
    private static bool parts = true, numbers, differences = true, hydrogens = true, ph7 = true, pairs, uracil;

    /// <summary>The nucleotide shown (§6.6's code: DA DC DG DT A C G U ATP).</summary>
    public static string Nucleotide
    {
        get => nucleotide;
        set
        {
            if (nucleotide == value || !DnaNucleotides.Codes.Contains(value)) return;
            nucleotide = value;
            NucleotidePart = null;
            Keep("nucleotide", value);
            Tell();
        }
    }

    /// <summary>Coloured by part (phosphate, sugar, base) rather than by element.</summary>
    public static bool ShowParts
    {
        get => parts;
        set { if (parts == value) return; parts = value; Keep("parts", value); Tell(); }
    }

    /// <summary>The sugar's carbons numbered 1′–5′ and the base's ring atoms 1–9.</summary>
    public static bool ShowNumbers
    {
        get => numbers;
        set { if (numbers == value) return; numbers = value; Keep("numbers", value); Tell(); }
    }

    /// <summary>DNA vs RNA: the 2′ position and the 5-methyl marked.</summary>
    public static bool ShowDifferences
    {
        get => differences;
        set { if (differences == value) return; differences = value; Keep("differences", value); Tell(); }
    }

    public static bool ShowHydrogens
    {
        get => hydrogens;
        set { if (hydrogens == value) return; hydrogens = value; Keep("hydrogens", value); Tell(); }
    }

    /// <summary>The phosphates as at pH 7 (their H gone: −2 on a monophosphate, −4 on ATP).</summary>
    public static bool AtPh7
    {
        get => ph7;
        set { if (ph7 == value) return; ph7 = value; Keep("ph7", value); Tell(); }
    }

    /// <summary>The 2D purines-and-pyrimidines panel instead of the 3D nucleotide.</summary>
    public static bool ShowPairs
    {
        get => pairs;
        set { if (pairs == value) return; pairs = value; Keep("pairs", value); Tell(); }
    }

    /// <summary>The 2D panel's first pair drawn A·U (RNA) instead of A·T.</summary>
    public static bool PairWithUracil
    {
        get => uracil;
        set { if (uracil == value) return; uracil = value; Tell(); }
    }

    /// <summary>The part of the nucleotide selected ("phosphate", "sugar", "base", "glycosidic") or none.</summary>
    public static string? NucleotidePart { get; set; }

    // ---- replication (§4.2 Should) ----

    /// <summary>The replication fork's nine steps.</summary>
    public static readonly BioStepController Replication = new(DnaReplication.Steps);

    // ---- RNA ----

    private static DnaRnaView rnaView = DnaRnaView.Cloverleaf;
    private static bool modified;

    public static DnaRnaView RnaView
    {
        get => rnaView;
        set { if (rnaView == value) return; rnaView = value; Keep("rna", value.Key()); Tell(); }
    }

    /// <summary>The cloverleaf's real modified bases as badges (D, Ψ, T, m¹A, Y …).</summary>
    public static bool ShowModified
    {
        get => modified;
        set { if (modified == value) return; modified = value; Keep("modified", value); Tell(); }
    }

    /// <summary>The tRNA nucleotide (1–76) selected on the cloverleaf, or the arm selected in 3D ("arm:anticodon").</summary>
    public static int? TrnaSelected { get; set; }
    public static string? TrnaArmSelected { get; set; }

    /// <summary>Settings as remembered (the view reads them from Prefs once): each known field set without remembering it again.</summary>
    public static void Restore(Func<string, string?> read)
    {
        Quietly(() =>
        {
            if (read("sequence") is { Length: > 0 } s && new BioSequenceInput(s, BioSeqKind.Dna, Cap).Sequence is { } ok) { text = ok; input = new(ok, BioSeqKind.Dna, Cap); sequence = ok; }
            if (DnaStyles.Parse(read("style")) is { } st) style = st;
            static bool? B(string? v) => v is null ? null : v is "true" or "True" or "1" or "yes";
            ends = B(read("ends")) ?? ends;
            grooves = B(read("grooves")) ?? grooves;
            measure = B(read("measure")) ?? measure;
            hbonds = B(read("hbonds")) ?? hbonds;
            if (read("nucleotide") is { } n && DnaNucleotides.Codes.Contains(n)) nucleotide = n;
            parts = B(read("parts")) ?? parts;
            numbers = B(read("numbers")) ?? numbers;
            differences = B(read("differences")) ?? differences;
            hydrogens = B(read("hydrogens")) ?? hydrogens;
            ph7 = B(read("ph7")) ?? ph7;
            pairs = B(read("pairs")) ?? pairs;
            if (DnaStyles.ParseRna(read("rna")) is { } rv) rnaView = rv;
            modified = B(read("modified")) ?? modified;
        });
    }
}

public static partial class BioSelfCheck
{
    /// <summary>DNA &amp; RNA's own bio-check lines (§4.2, §6.5–§6.7, §6.11): the cartoon's geometry, unzipping, the grooves,
    /// the sequence facts, the nucleotides' parts and numbers, the 2D base pairs, replication's steps, the cloverleaf's
    /// layout, the tRNA model and the mRNA strip.</summary>
    static partial void DnaChecks(BioCheckReport r) => DnaSelfCheck.Run(r);
}
