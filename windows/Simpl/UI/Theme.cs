using System.Globalization;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>A face, a size (DIPs) and a weight: one step of the type scale.</summary>
public readonly record struct Font(string Face, float Size, FontWeight Weight = FontWeight.Normal);

/// <summary>
/// Simpl's look on Windows, the Mac's measured: its type scale (mac/Simpl/Support/Design.swift — a step larger than
/// the system's own), its colours in light and dark (Support/Platform.swift's Theme, and the Mac's own label colours),
/// and Segoe UI Variable for San Francisco.
/// </summary>
public sealed class Theme
{
    public bool Dark = true;

    public const string TextFace = "Segoe UI Variable Text";
    public const string DisplayFace = "Segoe UI Variable Display";

    public static readonly Font LargeTitle = new(DisplayFace, 34, FontWeight.Bold);
    public static readonly Font Title = new(DisplayFace, 28, FontWeight.Bold);
    public static readonly Font Title2 = new(DisplayFace, 22, FontWeight.Bold);
    public static readonly Font Title3 = new(DisplayFace, 19, FontWeight.SemiBold);
    public static readonly Font Headline = new(TextFace, 16, FontWeight.SemiBold);
    public static readonly Font Body = new(TextFace, 15);
    public static readonly Font Callout = new(TextFace, 14);
    public static readonly Font Subheadline = new(TextFace, 13);
    public static readonly Font Footnote = new(TextFace, 12.5f);
    public static readonly Font Caption = new(TextFace, 12);
    public static readonly Font Caption2 = new(TextFace, 11);
    /// <summary>The window's toolbar title (the Mac's 15 semibold), and the small one over a subtitle.</summary>
    public static readonly Font ToolbarTitle = new(TextFace, 15, FontWeight.SemiBold);
    public static readonly Font ToolbarSmall = new(TextFace, 13, FontWeight.SemiBold);
    public static readonly Font ToolbarSub = new(TextFace, 11);
    /// <summary>A counter's number (the Mac's 34 bold rounded).</summary>
    public static readonly Font Count = new(DisplayFace, 34, FontWeight.Bold);

    public static Font Bold(Font f) => f with { Weight = FontWeight.Bold };
    public static Font Semibold(Font f) => f with { Weight = FontWeight.SemiBold };
    public static Font Medium(Font f) => f with { Weight = FontWeight.Medium };
    public static Font Sized(Font f, float size) => f with { Size = size };

    // ---- the Mac's label colours ----

    /// <summary>The words' colour (the Mac's primary label), at an opacity.</summary>
    public Color4 Ink(float a = 1) => Dark ? new Color4(1, 1, 1, 0.85f * a) : new Color4(0, 0, 0, 0.85f * a);
    /// <summary>Secondary words (the Mac's secondaryLabel).</summary>
    public Color4 Ink2 => Dark ? new Color4(1, 1, 1, 0.55f) : new Color4(0, 0, 0, 0.5f);
    /// <summary>Tertiary words: notes, placeholders, counts.</summary>
    public Color4 Ink3 => Dark ? new Color4(1, 1, 1, 0.25f) : new Color4(0, 0, 0, 0.25f);
    /// <summary>Quaternary: the faintest (a disabled glyph).</summary>
    public Color4 Ink4 => Dark ? new Color4(1, 1, 1, 0.1f) : new Color4(0, 0, 0, 0.1f);
    /// <summary>Primary at an opacity (a wash under a row).</summary>
    public Color4 Primary(float a) => Dark ? new Color4(1, 1, 1, a) : new Color4(0, 0, 0, a);

    // ---- the Mac's surfaces (Platform.swift's Theme, measured on the Mac's own pictures) ----

    /// <summary>The page under the cards.</summary>
    public Color4 Page => Dark ? Rgb(22, 22, 24) : Rgb(245, 245, 247);
    /// <summary>The sidebar's column (the Mac's: a shade apart from the page — lighter by day, darker by night).</summary>
    public Color4 Sidebar => Dark ? Rgb(20, 20, 20) : Rgb(250, 250, 250);
    /// <summary>The line between the sidebar and the page.</summary>
    public Color4 SidebarEdge => Dark ? Rgb(48, 48, 51) : new Color4(0, 0, 0, 0.05f);
    /// <summary>A card on the page (where it is not glass).</summary>
    public Color4 Card => Dark ? Rgb(36, 36, 38) : Rgb(255, 255, 255);
    /// <summary>A well inside a card (a fact, a field).</summary>
    public Color4 Well => Dark ? Rgb(118, 118, 128, 0.2f) : Rgb(118, 118, 128, 0.08f);
    /// <summary>A card's edge: a hairline.</summary>
    public Color4 Edge => Dark ? new Color4(1, 1, 1, 0.08f) : new Color4(0, 0, 0, 0.06f);
    /// <summary>The hairline between rows (the Mac's Divider).</summary>
    public Color4 Separator => Dark ? new Color4(1, 1, 1, 0.1f) : new Color4(0, 0, 0, 0.08f);
    /// <summary>A row under the pointer (RowButtonStyle), and pressed.</summary>
    public Color4 Hover => Primary(0.05f);
    public Color4 Pressed => Primary(0.09f);
    /// <summary>The sidebar's chosen row.</summary>
    public Color4 Selected => Dark ? new Color4(1, 1, 1, 0.21f) : new Color4(0, 0, 0, 0.12f);

    // ---- the Mac's system colours ----

    /// <summary>The accent chosen in Settings ▸ Appearance (the theme's, or one of the web's colours), when it is not the regular one.</summary>
    public static Color4? CustomAccent;

    public Color4 Accent => CustomAccent ?? (Dark ? Rgb(10, 132, 255) : Rgb(0, 122, 255));
    public Color4 Red => Dark ? Rgb(255, 69, 58) : Rgb(255, 59, 48);
    public Color4 Orange => Dark ? Rgb(255, 159, 10) : Rgb(255, 149, 0);
    public Color4 Yellow => Dark ? Rgb(255, 214, 10) : Rgb(255, 204, 0);
    public Color4 Green => Dark ? Rgb(48, 209, 88) : Rgb(52, 199, 89);
    public Color4 Teal => Dark ? Rgb(64, 200, 224) : Rgb(48, 176, 199);
    public Color4 Blue => Accent;
    public Color4 Indigo => Dark ? Rgb(94, 92, 230) : Rgb(88, 86, 214);
    public Color4 Purple => Dark ? Rgb(191, 90, 242) : Rgb(175, 82, 222);
    public Color4 Pink => Dark ? Rgb(255, 55, 95) : Rgb(255, 45, 85);
    public Color4 Gray => Dark ? Rgb(152, 152, 157) : Rgb(142, 142, 147);

    public static Color4 Rgb(int r, int g, int b, float a = 1) => new(r / 255f, g / 255f, b / 255f, a);

    public static Color4 WithAlpha(Color4 c, float a) => new(c.R, c.G, c.B, a);

    public static Color4 Mix(Color4 a, Color4 b, float t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    /// <summary>A colour as the page gives it ("#34c759", "#3c9"), or the Mac's systemGray.</summary>
    public static Color4 Hex(string? hex, float a = 1)
    {
        if (string.IsNullOrEmpty(hex)) return new Color4(142 / 255f, 142 / 255f, 147 / 255f, a);
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length < 6 || !int.TryParse(h[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return new Color4(142 / 255f, 142 / 255f, 147 / 255f, a);
        return new Color4(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
    }

    /// <summary>A status's colour by its tone (the Mac's StatusChip): bad, warn, good, info, purple; else secondary.</summary>
    public Color4 Tone(string? tone) => tone switch
    {
        "bad" or "red" or "danger" => Red,
        "warn" or "orange" => Orange,
        "good" or "green" or "ok" => Green,
        "info" or "blue" => Accent,
        "purple" or "indigo" => Purple,
        _ => Ink2,
    };
}
