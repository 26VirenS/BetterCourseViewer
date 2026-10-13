using System.Windows.Media;
using Simpl.Kit;
using Simpl.Support;

namespace Simpl.Tools;

// The biology toolkit's colours as WPF colours and brushes (spec §3.8; the hex pairs are BioPalette's), the Mac's
// BioColors.swift: the light or dark value as the app is drawn now (Appearance.Dark; a view draws again on
// Appearance.Changed, as the chemistry views do, and a 3D scene puts light or dark in its key).

public static class BioColors
{
    /// <summary>The colour as the app is drawn now (light or dark).</summary>
    public static Color ToColor(this BioHexPair p) => K.Hex(p.HexFor(Appearance.Dark));

    /// <summary>The colour for light or dark.</summary>
    public static Color ToColor(this BioHexPair p, bool dark) => K.Hex(p.HexFor(dark));

    /// <summary>Its brush (made once, frozen).</summary>
    public static SolidColorBrush ToBrush(this BioHexPair p) => K.Brush(p.ToColor());

    /// <summary>A base's colour by its letter (always drawn with the letter: never colour alone).</summary>
    public static Color Base(char b) => BioPalette.Base(b).ToColor();

    /// <summary>An amino acid's class colour by its one-letter code.</summary>
    public static Color AminoAcid(char one) => BioPalette.AminoAcid(one).ToColor();

    /// <summary>An amino-acid class's colour (nonpolar amber, polar green, acidic red, basic blue).</summary>
    public static Color Of(BioAAClass c) => BioPalette.AminoAcid(c).ToColor();

    /// <summary>A tool's tile colour.</summary>
    public static Color Tool(string key) => BioTools.Kind(key)?.Color ?? Color.FromRgb(0x8e, 0x8e, 0x93);

    /// <summary>"#rrggbb" for a colour (a BioMaterial's).</summary>
    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
}
