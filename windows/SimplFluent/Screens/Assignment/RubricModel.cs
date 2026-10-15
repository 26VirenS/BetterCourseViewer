using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Simpl.Models;

namespace Simpl.Screens;

// The rubric as the ring reads it (the Mac's RubricRing.swift and RubricStage.swift, their sums): one slice per criterion,
// as long as its share of the points; marked and posted, each slice turns to its grade's colour (green near full marks,
// yellow to orange for part marks, red where most was lost) and bends out where the work did well and in where it lost
// points. This file holds the numbers; RubricVisuals.cs draws them, RubricPopup.cs puts them over the window.

/// <summary>A colour as the ring mixes it: three channels from 0 to 255 (the Mac's RubricRGB).</summary>
internal readonly record struct RubricRGB(double R, double G, double B)
{
    /// <summary>A criterion not marked yet, in a marked ring: grey, not a grade.</summary>
    public static readonly RubricRGB Unmarked = new(142, 142, 150);
    public static readonly RubricRGB Black = new(0, 0, 0);
    public static readonly RubricRGB White = new(255, 255, 255);

    public static RubricRGB Hex(string hex)
    {
        var s = hex.TrimStart('#');
        var v = uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var x) ? x : 0x8e8e93u;
        return new RubricRGB((v >> 16) & 0xff, (v >> 8) & 0xff, v & 0xff);
    }

    public RubricRGB Mix(RubricRGB o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public Color Color => Color.FromRgb(Byte(R), Byte(G), Byte(B));

    public Color WithAlpha(double a) => Color.FromArgb(Byte(255 * Math.Clamp(a, 0, 1)), Byte(R), Byte(G), Byte(B));

    /// <summary>The words on this colour: dark on a light one (green, orange), white on the rest.</summary>
    public Color Ink
    {
        get
        {
            static double Lin(double v) { var c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            var luma = 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
            return luma > 0.3 ? Color.FromRgb(28, 28, 30) : Colors.White;
        }
    }

    public static RubricRGB Hsl(double hue, double s, double l)
    {
        var a = s * Math.Min(l, 1 - l);
        double F(double q)
        {
            var k = (q + hue / 30) % 12;
            return 255 * (l - a * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1))));
        }
        return new RubricRGB(F(0), F(8), F(4));
    }

    /// <summary>The colour as hue (degrees), saturation and lightness.</summary>
    public (double H, double S, double L) HslParts
    {
        get
        {
            double rr = R / 255, gg = G / 255, bb = B / 255;
            var mx = Math.Max(rr, Math.Max(gg, bb));
            var mn = Math.Min(rr, Math.Min(gg, bb));
            var l = (mx + mn) / 2;
            var d = mx - mn;
            if (d == 0) return (0, 0, l);
            double hh;
            if (mx == rr) hh = ((gg - bb) / d) % 6;
            else if (mx == gg) hh = (bb - rr) / d + 2;
            else hh = (rr - gg) / d + 4;
            return ((hh * 60 + 360) % 360, d / Math.Max(1e-9, 1 - Math.Abs(2 * l - 1)), l);
        }
    }

    /// <summary>From this colour to another round the colour wheel, the short way: a turn that stays vivid, where a
    /// straight mix of two far-apart colours goes grey half-way.</summary>
    public RubricRGB Turn(RubricRGB other, double t)
    {
        if (t <= 0) return this;
        if (t >= 1) return other;
        var a = HslParts;
        var b = other.HslParts;
        var dh = b.H - a.H;
        if (dh > 180) dh -= 360;
        if (dh < -180) dh += 360;
        var hue = (a.H + dh * t) % 360;
        if (hue < 0) hue += 360;
        return Hsl(hue, a.S + (b.S - a.S) * t, a.L + (b.L - a.L) * t);
    }

    /// <summary>A criterion's mark as a colour: green at full marks or near them (90 % and up), yellow to orange for part
    /// marks (80 % down to half), red only where most was lost (30 % or less) — a smooth run of hue, so the ring's colours
    /// still flow from one slice into the next. The yellows a little lighter, so they read as yellow.</summary>
    public static RubricRGB Grade(double fraction)
    {
        var x = RubricRing.Clamp01(fraction);
        double hue;
        if (x >= 0.9) hue = 140;
        else if (x >= 0.8) hue = 48 + 92 * RubricRing.Smooth((x - 0.8) / 0.1);
        else if (x >= 0.5) hue = 30 + 18 * RubricRing.Smooth((x - 0.5) / 0.3);
        else if (x >= 0.3) hue = 2 + 28 * RubricRing.Smooth((x - 0.3) / 0.2);
        else hue = 2;
        return Hsl(hue, 0.8, 0.52 + 0.04 * RubricRing.Smooth(RubricRing.Clamp01(1 - Math.Abs(hue - 48) / 40)));
    }
}

/// <summary>The ring's small sums and its stage's units (the web ring's own 760 × 560).</summary>
internal static class RubricRing
{
    public const double Tau = 2 * Math.PI;
    public const double Width = 760, Height = 560, Cx = 380, Cy = 280, Radius = 176;
    /// <summary>The bar's line, its top and its foot (shorter with a marker's note under it).</summary>
    public const double BarX = 66, BarTop = 158, BarFoot = 500;
    /// <summary>Where the bar's words start, and the room kept right of them.</summary>
    public const double RowsX = 108, RowsRight = 28;
    /// <summary>The little ring on top of the bar (the way back): its middle and its radius.</summary>
    public const double MiniX = 66, MiniY = 56, MiniRadius = 14.5;

    public static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));
    /// <summary>Smoothstep: an S from 0 to 1.</summary>
    public static double Smooth(double v) => v * v * (3 - 2 * v);
    /// <summary>From 0 at a to 1 at b, on an S.</summary>
    public static double Span(double e, double a, double b) => Smooth(Clamp01((e - a) / (b - a)));
    /// <summary>The web ring's tween: in and out, cubic.</summary>
    public static double Ease(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
    /// <summary>Out, cubic: quick, then settling.</summary>
    public static double EaseOut(double x) => 1 - Math.Pow(1 - Clamp01(x), 3);
    /// <summary>A spring's step from 0 to 1: a little past 1, then settling (a slice growing in).</summary>
    public static double SpringOut(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 1 - Math.Exp(-5.5 * x) * Math.Cos(8.5 * x);

    /// <summary>A point r from the ring's middle, at an angle clockwise from twelve o'clock.</summary>
    public static Point Pt(double r, double a) => new(Cx + r * Math.Sin(a), Cy - r * Math.Cos(a));

    public static string Num(double v)
    {
        if (!double.IsFinite(v)) return "—";
        if (v == Math.Round(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
        return (Math.Round(v * 100, MidpointRounding.AwayFromZero) / 100).ToString("0.##", CultureInfo.InvariantCulture);
    }
}

/// <summary>Where an angle falls between two criteria's middles: this one, the next, and how far across on an S-curve.</summary>
internal readonly record struct RubricBlend(int From, int To, double T);

/// <summary>
/// A rubric's criteria with their levels and marks, and the ring's shape of them (the Mac's RubricModel): where each
/// slice starts and ends, how thick it is, how far it bends, its colours.
/// </summary>
internal sealed class RubricModel
{
    public sealed record Level(string Label, string Text, double? Pts);

    /// <summary>A criterion: its levels, what it is worth and the score given, the level given (<c>Mark</c>), and — when
    /// the score given is not that level's points (a marker picks Partial, 3 pts, and gives 4, as Canvas lets them) —
    /// <c>Adjusted</c>, its mark then drawn at the points given; <c>MarkText</c> is the mark's badge ("Your mark", "Your
    /// mark · 4 pts").</summary>
    public sealed record Criterion(string Id, int Index, string Name, string Short, string Desc, List<Level> Levels, double Worth,
        double? Score, int Mark, string Comment, double Frac, bool Adjusted = false, string MarkText = "Your mark");

    public List<Criterion> Criteria { get; }
    /// <summary>Marked and posted: at least one criterion has a score.</summary>
    public bool Graded { get; }
    public double Possible { get; }
    public double Earned { get; }
    /// <summary>Each slice's own colour, and its grade's (its own when nothing is marked).</summary>
    public RubricRGB[] Hues { get; }
    public RubricRGB[] Grades { get; }
    /// <summary>Each slice's start, end and middle, in radians clockwise from twelve o'clock.</summary>
    public double[] Starts { get; }
    public double[] Ends { get; }
    public double[] Mids { get; }
    /// <summary>How far each slice bends out (+) or in (−), and how thick it is, in the web ring's units.</summary>
    public double[] Bend { get; }
    public double[] Thick { get; }

    public int N => Criteria.Count;
    public RubricRGB[] Colors => Graded ? Grades : Hues;

    public RubricModel(IReadOnlyList<RubricRow> rows)
    {
        Criteria = rows.Select((r, k) => Read(r, k)).ToList();
        var list = Criteria;
        var count = list.Count;
        Graded = list.Any(c => c.Score != null);
        var worthSum = list.Sum(c => Math.Max(0, c.Worth));
        Possible = worthSum;
        Earned = list.Sum(c => c.Score ?? 0);
        string[] palette = { "#5e5ce6", "#0a84ff", "#30b0c7", "#30d158", "#ff9f0a", "#ff375f" };
        Hues = Enumerable.Range(0, count).Select(k => count <= palette.Length
            ? RubricRGB.Hex(palette[k])
            : RubricRGB.Hsl((248 + k * 360.0 / count) % 360, 0.78, 0.56)).ToArray();
        // marked and posted, each slice's colour is its grade's (one not marked yet: grey)
        var marked = list.Select(c => c.Score is { } s ? RubricRGB.Grade(c.Worth > 0 ? s / c.Worth : 1) : RubricRGB.Unmarked).ToArray();
        Grades = Graded ? marked : Hues;
        // each slice's share of the circle: its points, with a sliver for a criterion worth nothing
        var weights = list.Select(c => Math.Max(0, c.Worth)).ToArray();
        var total = weights.Sum();
        var shares = total > 0 ? weights.Select(w => Math.Max(w, total * 0.03)).ToArray() : weights.Select(_ => 1.0).ToArray();
        var sum = Math.Max(1e-9, shares.Sum());
        Starts = new double[count];
        Ends = new double[count];
        Mids = new double[count];
        var acc = 0.0;
        for (var i = 0; i < count; i++)
        {
            Starts[i] = acc / sum * RubricRing.Tau;
            acc += shares[i];
            Ends[i] = acc / sum * RubricRing.Tau;
            Mids[i] = (Starts[i] + Ends[i]) / 2;
        }
        // a criterion marked low bends in, one marked high bends out — gently (the colour says the rest), and damped
        // as the count rises
        var graded = Graded;
        Bend = list.Select(c =>
        {
            if (!graded || c.Score is not { } s || c.Worth <= 0) return 0.0;
            var fr = s / c.Worth;
            var amp = Math.Min(1, Math.Max(0.35, c.Worth / Math.Max(1e-9, worthSum) * count / 3));
            return Math.Max(-18, Math.Min(12, 60 * (fr - 0.85))) * Math.Min(1, 6.0 / count + 0.25) * amp;
        }).ToArray();
        // a slice is thicker the more of the rubric it carries — growing inward, its outer edge on the circle
        var avg = sum / Math.Max(1, count);
        Thick = shares.Select(sh => Math.Max(5, Math.Min(28, 10 * Math.Pow(sh / Math.Max(1e-9, avg), 1.5)))).ToArray();
    }

    // ---- reading a criterion ----

    private static Criterion Read(RubricRow row, int k)
    {
        // its levels, the best first (one without points last), each remembering whether it was the one given
        var raw = (row.Ratings ?? new List<RubricRating>()).Select((r, i) => (I: i, Got: r.Got == true, Level: LevelOf(r))).ToList();
        var sorted = raw.OrderByDescending(x => x.Level.Pts ?? double.NegativeInfinity).ThenBy(x => x.I).ToList();
        var levels = sorted.Select(x => x.Level).ToList();
        var words = Pair(row.Pts);
        var worth = row.Worth ?? words.Of ?? levels.FirstOrDefault()?.Pts ?? 0;
        var score = row.Worth != null ? row.Score : words.Got; // (the numbers when sent; else read from the words)
        var mark = sorted.FindIndex(x => x.Got);
        if (mark < 0 && score is { } s0) // (no level named: the one nearest the points given)
        {
            var best = double.PositiveInfinity;
            for (var i = 0; i < levels.Count; i++)
                if (levels[i].Pts is { } p && Math.Abs(p - s0) < best) { best = Math.Abs(p - s0); mark = i; }
        }
        if (score == null && mark >= 0) score = levels[mark].Pts;
        if (score == null) mark = -1;
        var name = (row.Name ?? "").Trim();
        var full = name.Length == 0 ? $"Criterion {k + 1}" : name;
        var comment = (row.Note ?? row.Comment ?? "").Trim('“', '”', '"', ' ', '\n');
        // the points given apart from the level's: the engine's word (2.99.33), else read here
        var given = mark >= 0 ? levels[mark].Pts : null;
        var adjusted = row.Adjusted ?? (score is { } sc && given is { } gv && Math.Abs(sc - gv) > 1e-9);
        var points = score is { } sp ? $"{RubricRing.Num(sp)} {(sp == 1 ? "pt" : "pts")}" : "";
        var badge = !string.IsNullOrEmpty(row.MarkText) ? row.MarkText! : adjusted && points.Length > 0 ? $"Your mark · {points}" : "Your mark";
        return new Criterion(row.Id ?? "", k, full, Short(full), PlainLines(row.Desc ?? ""), levels, worth, score, mark, comment,
            Fraction(levels, worth, score, mark), adjusted, badge);
    }

    private static Level LevelOf(RubricRating r)
    {
        var label = (r.Text ?? "").Trim();
        return new Level(label.Length == 0 ? "Rating" : label, (r.Long ?? "").Trim(), r.Value ?? Number(r.Pts));
    }

    /// <summary>How far up its bar a criterion's mark sits (the stretch above it was not earned).</summary>
    private static double Fraction(List<Level> levels, double worth, double? score, int mark)
    {
        if (score is not { } s) return 1;
        if (levels.Count == 0) return RubricRing.Clamp01(s / Math.Max(worth, Math.Max(s, 1e-9))); // (marked freely: from nothing to what it is worth)
        var vals = levels.Where(l => l.Pts != null).Select(l => l.Pts!.Value).ToList();
        if (vals.Count == 0 || vals.Max() - vals.Min() <= 1e-9)
            return mark >= 0 && levels.Count > 1 ? 1 - (double)mark / (levels.Count - 1) : 1;
        return RubricRing.Clamp01((s - vals.Min()) / (vals.Max() - vals.Min()));
    }

    /// <summary>The first number in a few words ("3 pts" → 3).</summary>
    private static double? Number(string? s)
    {
        if (s == null) return null;
        var kept = new string(s.Where(c => c < 128 && (char.IsDigit(c) || c == '.' || c == '-')).ToArray());
        return double.TryParse(kept, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>"4 / 6" → given 4 of 6; "6 pts" → worth 6.</summary>
    private static (double? Got, double? Of) Pair(string? s)
    {
        if (string.IsNullOrEmpty(s)) return (null, null);
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2) return (Number(parts[0]), Number(parts[1]));
        return (null, Number(s));
    }

    /// <summary>A long description's lines as one: bullets off, joined by middots.</summary>
    private static string PlainLines(string s) => string.Join(" · ", s.Split('\n')
        .Select(line => line.TrimStart(' ', '\t', '•', '-', '*', '·').Trim())
        .Where(line => line.Length > 0));

    private static readonly HashSet<string> Small = new() { "and", "or", "of", "the", "to", "for", "with", "in", "on", "a", "an", "&" };

    /// <summary>A short name for the ring: the whole name when it is short, else its first words ("Figures and tables":
    /// Figures, not "Figures and").</summary>
    public static string Short(string name)
    {
        if (name.Length <= 16) return name;
        var o = "";
        foreach (var w in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = o.Length == 0 ? w : o + " " + w;
            if (next.Length > 14) break;
            o = next;
        }
        o = o.TrimEnd(' ', '/', '&', ',', ':', ';', '–', '—', '-');
        var space = o.LastIndexOf(' ');
        if (space >= 0 && Small.Contains(o[(space + 1)..].ToLowerInvariant())) o = o[..space];
        return o.Length == 0 ? name[..Math.Min(13, name.Length)] + "…" : o;
    }

    // ---- words ----

    /// <summary>"4 / 6", "6 pts · not marked", "6 pts".</summary>
    public string PointsLabel(Criterion c)
    {
        if (Graded) return c.Score is { } s ? $"{RubricRing.Num(s)} / {RubricRing.Num(c.Worth)}" : $"{RubricRing.Num(c.Worth)} pts · not marked";
        return $"{RubricRing.Num(c.Worth)} {(c.Worth == 1 ? "pt" : "pts")}";
    }

    /// <summary>The criterion as a screen reader says it.</summary>
    public string Spoken(Criterion c)
    {
        if (Graded)
        {
            if (c.Score is not { } s) return $"{c.Name}, not marked yet, worth {RubricRing.Num(c.Worth)}";
            var level = c.Mark >= 0 ? $", {c.Levels[c.Mark].Label}{(c.Adjusted ? ", adjusted" : "")}" : "";
            return $"{c.Name}, {RubricRing.Num(s)} of {RubricRing.Num(c.Worth)}{level}";
        }
        return $"{c.Name}, worth {RubricRing.Num(c.Worth)} {(c.Worth == 1 ? "point" : "points")}";
    }

    public int Percent => Possible > 0 ? (int)Math.Round(Earned / Possible * 100, MidpointRounding.AwayFromZero) : 0;

    public string Many => N == 1 ? "1 criterion" : $"{N} criteria";

    // ---- the ring's shape ----

    public RubricBlend Between(double th)
    {
        var n = N;
        if (n <= 1) return new RubricBlend(0, 0, 0);
        var x = th - Mids[0];
        x -= Math.Floor(x / RubricRing.Tau) * RubricRing.Tau;
        var k = n - 1;
        while (k > 0 && Mids[k] - Mids[0] > x) k--;
        var lo = Mids[k] - Mids[0];
        var hi = k + 1 < n ? Mids[k + 1] - Mids[0] : RubricRing.Tau;
        return new RubricBlend(k, (k + 1) % n, (1 - Math.Cos(Math.PI * (x - lo) / Math.Max(1e-9, hi - lo))) / 2);
    }

    public static double Blend(double[] values, RubricBlend b) => values[b.From] + (values[b.To] - values[b.From]) * b.T;

    /// <summary>The slice an angle falls in.</summary>
    public int SliceAt(double th)
    {
        var a = th % RubricRing.Tau;
        if (a < 0) a += RubricRing.Tau;
        for (var i = 0; i < Ends.Length; i++) if (a < Ends[i]) return i;
        return Math.Max(0, N - 1);
    }
}

/// <summary>A level's place on the open criterion's bar: its tick (at the height of its points) and its row (beside the
/// tick, or under the row before it when there is no room, a leader joining it to its tick), in the stage's units.</summary>
internal readonly record struct RubricBarPlace(double Tick, double Row, bool Picked);

/// <summary>The rubric's ring as it stands still (the Mac's RubricGeometry): what does not change from frame to frame.</summary>
internal sealed class RubricGeometry
{
    public RubricModel Model { get; }
    /// <summary>How many pieces each slice is drawn in: each turns at most 20°, an even number of them.</summary>
    public int[] Pieces { get; }
    /// <summary>Each criterion's bar foot: shorter with a marker's note under it.</summary>
    public double[] Bottoms { get; }

    public RubricGeometry(RubricModel model)
    {
        Model = model;
        var piece = Math.PI / 9;
        Pieces = Enumerable.Range(0, model.N).Select(k => 2 * Math.Max(1, (int)Math.Ceiling((model.Ends[k] - model.Starts[k]) / (2 * piece)))).ToArray();
        Bottoms = model.Criteria.Select(c => c.Comment.Length == 0 ? RubricRing.BarFoot : Math.Min(466, 510 - NoteHeight(c.Comment))).ToArray();
    }

    public int N => Model.N;

    /// <summary>About how tall a marker's note stands, in the stage's units (its box holds 92 at most and scrolls past that).</summary>
    public static double NoteHeight(string text)
    {
        var breaks = text.Count(c => c == '\n');
        var lines = Math.Max(1, Math.Ceiling(text.Length / 78.0)) + breaks;
        return Math.Min(92, 20 + lines * 18);
    }

    /// <summary>Each slice's colour at a point of the turn to its marks: its own, turning round the colour wheel to its grade's.</summary>
    public RubricRGB[] Tones(double g) => !Model.Graded ? Model.Hues : Enumerable.Range(0, N).Select(i => Model.Hues[i].Turn(Model.Grades[i], g)).ToArray();

    /// <summary>Where each bar's colour stops (its mark), and each slice's bend, as far as the ring has gone to its marks.</summary>
    public double[] Fracs(double g) => Model.Criteria.Select(c => 1 + (c.Frac - 1) * g).ToArray();
    public double[] Bends(double g) => Model.Bend.Select(b => b * g).ToArray();

    /// <summary>The slice under a point of the stage: on its band, or just inside or outside it.</summary>
    public int? Slice(double x, double y)
    {
        if (N == 0) return null;
        var dx = x - RubricRing.Cx;
        var dy = y - RubricRing.Cy;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r < RubricRing.Radius - 30 || r > RubricRing.Radius + 44) return null;
        return Model.SliceAt(Math.Atan2(dx, -dy));
    }

    /// <summary>A criterion's levels up its bar — or, marked freely, its one score (or what it is worth) standing alone.</summary>
    public List<RubricModel.Level> Levels(int k)
    {
        if (k < 0 || k >= N) return new();
        var c = Model.Criteria[k];
        if (c.Levels.Count > 0) return c.Levels;
        if (Model.Graded && c.Score is { } s)
            return new() { new RubricModel.Level("Your score", $"Out of {RubricRing.Num(c.Worth)} — marked without set levels.", s) };
        return new() { new RubricModel.Level("Marked freely", "No set levels: your teacher gives a score up to this.", c.Worth) };
    }

    /// <summary>Whether a level is the one given.</summary>
    public bool Picked(int k, int i)
    {
        if (!Model.Graded || k < 0 || k >= N) return false;
        var c = Model.Criteria[k];
        return c.Levels.Count == 0 ? c.Score != null : c.Mark == i;
    }

    /// <summary>Where a level sits on its bar: by points, the best at the top (evenly in order where there are none to go by).</summary>
    private double BarY(int k, double? p, int i, double foot)
    {
        var c = Model.Criteria[k];
        const double top = RubricRing.BarTop;
        if (c.Levels.Count == 0)
        {
            var most = Math.Max(c.Worth, Math.Max(c.Score ?? 0, 1e-9));
            return foot - (foot - top) * RubricRing.Clamp01((p ?? 0) / most);
        }
        var count = c.Levels.Count;
        var even = count <= 1 ? top : top + (foot - top) * i / (count - 1);
        var vals = c.Levels.Where(l => l.Pts != null).Select(l => l.Pts!.Value).ToList();
        if (vals.Count == 0 || p is not { } pv) return even;
        var hi = vals.Max();
        var lo = vals.Min();
        if (hi - lo < 1e-9) return even;
        return foot - (foot - top) * (pv - lo) / (hi - lo);
    }

    /// <summary>
    /// The levels' ticks at the height of their points, and (0.3.17) their rows beside their ticks, each at the height
    /// of its points — moved down only as far as the row above it needs (and up, at the foot), a leader joining a row
    /// that had to move to its tick. (The rows had been stacked from the top whatever their points, the room going to
    /// the words; the owner wanted each level where its points put it, the bar read as a scale.) Heights are the rows'
    /// own, in the stage's units; <paramref name="top"/> is where the first row may start (under the criterion's whole
    /// description, where it is shown).
    /// </summary>
    public List<RubricBarPlace> Places(int k, double foot, IReadOnlyList<double> heights, double? top = null)
    {
        var ls = Levels(k);
        if (ls.Count == 0) return new();
        var want = ls.Select((l, i) => BarY(k, l.Pts, i, foot)).ToArray();
        var hs = ls.Select((_, i) => i < heights.Count ? heights[i] : 46).ToArray();
        var ys = (double[])want.Clone();
        if (top is { } t) ys[0] = Math.Max(ys[0], t + hs[0] / 2);
        for (var i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + hs[i - 1] / 2 + 8 + hs[i] / 2);
        var last = ys.Length - 1;
        if (ys[last] + hs[last] / 2 > foot + 30)
        {
            ys[last] = foot + 30 - hs[last] / 2;
            for (var i = last - 1; i >= 0; i--) ys[i] = Math.Min(ys[i], ys[i + 1] - hs[i + 1] / 2 - 8 - hs[i] / 2);
        }
        return ls.Select((_, i) => new RubricBarPlace(want[i], ys[i], Picked(k, i))).ToList();
    }

    /// <summary>Where the rows may start under the criterion's whole description (<paramref name="head"/>: how tall the
    /// head stands with all of it), when they all still fit above the bar's foot that way; else null (the head keeps its
    /// two lines and More).</summary>
    public static double? FullHeadTop(double foot, IReadOnlyList<double> heights, double head)
    {
        if (head <= 0) return null;
        var rows = heights.Sum() + 8 * Math.Max(0, heights.Count - 1);
        var top = 24 + head + 14;
        return top + rows <= foot + 30 ? top : null;
    }

    /// <summary>Where the points given sit on the bar when they are not the given level's (a marker picked Partial, 3 pts,
    /// and gave 4): the mark's dot goes there, where the bar's colour stops; null otherwise.</summary>
    public double? MarkY(int k, double foot)
    {
        if (!Model.Graded || k < 0 || k >= N) return null;
        var c = Model.Criteria[k];
        if (!c.Adjusted || c.Score is not { } s || c.Levels.Count == 0) return null;
        return Math.Clamp(BarY(k, s, Math.Max(0, c.Mark), foot), RubricRing.BarTop, foot);
    }

    /// <summary>A label's place round the ring: the point beside its slice, which way it hangs from it, whether it shows (a
    /// crowded ring shows every other one).</summary>
    public (Point At, double AnchorX, double AnchorY, TextAlignment Align, bool Shown)[] LabelSpots(int sel, int hov, double[] go, Func<int, double> swell)
    {
        var n = N;
        var crowd = n >= 15 ? 2 : 1;
        var spots = new (Point, double, double, TextAlignment, bool)[n];
        for (var k = 0; k < n; k++)
        {
            var mid = Model.Mids[k];
            var r = RubricRing.Radius + go[k] + 36 + 9 * swell(k);
            var s = Math.Sin(mid);
            var co = Math.Cos(mid);
            var sx = Math.Max(-1, Math.Min(1, s / 0.3));
            var sy = Math.Max(-1, Math.Min(1, co / 0.3));
            var align = s > 0.3 ? TextAlignment.Left : s < -0.3 ? TextAlignment.Right : TextAlignment.Center;
            var near = k == (sel + 1) % n || k == (sel + n - 1) % n;
            var even = k % crowd == 0 && !(k == n - 1 && n % crowd != 0);
            var shown = n <= 10 || k == sel || k == hov || (!near && even);
            spots[k] = (RubricRing.Pt(r, mid), 0.5 - 0.5 * sx, 0.5 + 0.5 * sy, align, shown);
        }
        return spots;
    }
}
