using System.Globalization;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Simpl.UI;

/// <summary>A face, a size (DIPs) and a weight: one step of the type scale.</summary>
public readonly record struct Font(string Face, float Size, FontWeight Weight = FontWeight.Normal);

/// <summary>
/// Simpl's look on Windows: the Mac's type scale (mac/Simpl/Support/Design.swift — a step larger than the system's own),
/// its colours in light and dark, and Segoe UI Variable for San Francisco.
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

    public static Font Bold(Font f) => f with { Weight = FontWeight.Bold };
    public static Font Semibold(Font f) => f with { Weight = FontWeight.SemiBold };

    /// <summary>The words' colour, at an opacity.</summary>
    public Color4 Ink(float a = 0.92f) => Dark ? new Color4(1, 1, 1, a) : new Color4(0, 0, 0, a * 0.95f);
    /// <summary>Secondary words.</summary>
    public Color4 Ink2 => Dark ? new Color4(0.92f, 0.92f, 0.96f, 0.6f) : new Color4(0.24f, 0.24f, 0.26f, 0.62f);
    /// <summary>Tertiary words: notes, placeholders.</summary>
    public Color4 Ink3 => Dark ? new Color4(0.92f, 0.92f, 0.96f, 0.36f) : new Color4(0.24f, 0.24f, 0.26f, 0.38f);
    public Color4 Accent => Rgb(0, 122, 255);
    public Color4 Separator => Dark ? new Color4(1, 1, 1, 0.09f) : new Color4(0, 0, 0, 0.08f);
    /// <summary>A row under the pointer.</summary>
    public Color4 Hover => Dark ? new Color4(1, 1, 1, 0.06f) : new Color4(0, 0, 0, 0.045f);
    /// <summary>The row chosen (the sidebar's place).</summary>
    public Color4 Selected => Dark ? new Color4(1, 1, 1, 0.12f) : new Color4(0, 0, 0, 0.08f);
    public Color4 Red => Rgb(255, 59, 48);
    public Color4 Orange => Rgb(255, 149, 0);
    public Color4 Green => Rgb(52, 199, 89);
    public Color4 Indigo => Rgb(88, 86, 214);

    public static Color4 Rgb(int r, int g, int b, float a = 1) => new(r / 255f, g / 255f, b / 255f, a);

    public static Color4 WithAlpha(Color4 c, float a) => new(c.R, c.G, c.B, a);

    /// <summary>A colour as the page gives it ("#34c759", "#3c9"), or a grey.</summary>
    public static Color4 Hex(string? hex, float a = 1)
    {
        if (string.IsNullOrEmpty(hex)) return new Color4(0.55f, 0.55f, 0.58f, a);
        var h = hex.TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => $"{c}{c}"));
        if (h.Length < 6 || !int.TryParse(h[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return new Color4(0.55f, 0.55f, 0.58f, a);
        return new Color4(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
    }

    /// <summary>A counter's tone (red, orange, green, …) as a colour.</summary>
    public Color4 Tone(string? tone) => tone switch
    {
        "red" or "danger" or "bad" => Red,
        "orange" or "warn" => Orange,
        "green" or "good" or "ok" => Green,
        "blue" => Accent,
        "purple" or "indigo" => Indigo,
        _ => Ink2,
    };
}
