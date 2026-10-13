namespace Simpl.Tools;

// The biology toolkit's colours (spec §3.8), the Mac's BioPalette.swift: light and dark hex pairs — dark brighter, for
// dark backgrounds — used in 2D and in 3D materials alike (BioColors.cs makes them WPF colours and brushes). Never colour
// alone: a base always shows its letter, an amino acid its class's name or a legend, an organelle its label. Atoms stay
// CPK (MBChem.ColorOf); the organelles' own colours are the Cell Explorer's (§5.1).

/// <summary>A colour in light and in dark.</summary>
public readonly record struct BioHexPair(string Light, string Dark)
{
    public string HexFor(bool dark) => dark ? Dark : Light;
}

public static class BioPalette
{
    // bases
    public static readonly BioHexPair Adenine = new("#2e9e48", "#30d158");
    public static readonly BioHexPair Thymine = new("#d63a34", "#ff6961");
    /// <summary>Orange, so U never looks like T.</summary>
    public static readonly BioHexPair Uracil = new("#e07800", "#ffa133");
    public static readonly BioHexPair Guanine = new("#c79a00", "#ffd60a");
    public static readonly BioHexPair Cytosine = new("#1f6fd1", "#5ea8ff");

    // strands
    public static readonly BioHexPair Strand1 = new("#7d5cf5", "#a58bff");
    public static readonly BioHexPair Strand2 = new("#3aa3b8", "#6fd3e6");
    public static readonly BioHexPair Rna = new("#e07800", "#ffa133");

    // amino-acid classes
    public static readonly BioHexPair Nonpolar = new("#c08a2b", "#f0b44a");
    public static readonly BioHexPair Polar = new("#2e9e78", "#4fd1a5");
    public static readonly BioHexPair Acidic = new("#d63a34", "#ff6961");
    public static readonly BioHexPair Basic = new("#1f6fd1", "#5ea8ff");

    // protein cartoons
    public static readonly BioHexPair Helix = new("#e8508a", "#ff7aa8");
    public static readonly BioHexPair Strand = new("#c79a00", "#ffd60a");
    public static readonly BioHexPair Coil = new("#8e8e93", "#aeaeb2");

    // chromosomes
    public static readonly BioHexPair Maternal = new("#e5484d", "#ff6b6b");
    public static readonly BioHexPair Paternal = new("#3e63dd", "#6e8bff");

    /// <summary>H-bond dashes (1.5 in 2D, 0.15 Å in 3D).</summary>
    public static readonly BioHexPair HBond = new("#8e8e93", "#aeaeb2");

    /// <summary>A base's colour by its letter (A, C, G, T, U); grey for anything else (N).</summary>
    public static BioHexPair Base(char b) => char.ToUpperInvariant(b) switch
    {
        'A' => Adenine,
        'T' => Thymine,
        'U' => Uracil,
        'G' => Guanine,
        'C' => Cytosine,
        _ => Coil,
    };

    /// <summary>An amino acid's class colour.</summary>
    public static BioHexPair AminoAcid(BioAAClass c) => c switch
    {
        BioAAClass.Nonpolar => Nonpolar,
        BioAAClass.Polar => Polar,
        BioAAClass.Acidic => Acidic,
        _ => Basic,
    };

    /// <summary>An amino acid's colour by its one-letter code (grey for X, a stop or anything else).</summary>
    public static BioHexPair AminoAcid(char one) =>
        BioAminoAcids.ByOne.TryGetValue(char.ToUpperInvariant(one), out var aa) ? AminoAcid(aa.Group) : Coil;

    /// <summary>A secondary-structure code's colour (H helix, E strand, else coil).</summary>
    public static BioHexPair Secondary(char ss) => ss == 'H' ? Helix : (ss == 'E' ? Strand : Coil);

    /// <summary>"#2e9e48" → (r, g, b) in 0…1 (grey for anything unreadable).</summary>
    public static (double R, double G, double B) Rgb(string hex)
    {
        var s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)) return (0.56, 0.56, 0.58);
        return (((v >> 16) & 0xff) / 255.0, ((v >> 8) & 0xff) / 255.0, (v & 0xff) / 255.0);
    }
}
