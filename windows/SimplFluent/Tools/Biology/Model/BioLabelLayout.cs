namespace Simpl.Tools;

// Labels with leader lines (spec §3.5), laid out the same way on both platforms (the Mac's BioLabelLayout.swift): each
// visible anchor's label goes in the column on its side of the model (left when the anchor is left of the model's
// centre), the columns R + 24 out from the model's screen circle and kept inside the view; in a column, labels follow
// their anchors down and are pushed apart to 22; a column too long for the view is pushed up from the bottom. Each
// leader runs anchor → elbow (12 short of the column, toward the model) → label. At most 14 at once: the objects
// covering the most of the screen keep theirs, the rest are counted ("+N more"). Device-independent pixels, y down.

/// <summary>A label to place: its object, its words, where its anchor projects (y down), whether it can be seen (not
/// behind the camera or hidden by another object), how much of the screen its object covers (the larger keep their labels
/// when there are too many), and its width (its text at 12 semibold plus its capsule's padding).</summary>
public sealed record BioLabelInput(string Id, string Text, double X, double Y, bool Visible = true, double Area = 0, double Width = 80);

/// <summary>A placed label: its column (left: its capsule ends at EdgeX; right: it starts there), its capsule's edge nearest
/// the model, its vertical centre, its width, and its leader (anchor → elbow → (EdgeX, Y)).</summary>
public sealed record BioLabelPlacement(string Id, string Text, bool Left, double EdgeX, double Y, double Width, double AnchorX, double AnchorY, double ElbowX)
{
    /// <summary>The capsule's frame (x, y, width, height), y down.</summary>
    public (double X, double Y, double Width, double Height) Frame => (Left ? EdgeX - Width : EdgeX, Y - BioLabelLayout.Height / 2, Width, BioLabelLayout.Height);
}

public static class BioLabelLayout
{
    public const double Height = 18;
    public const double Gap = 4;
    public const double ColumnOffset = 24;
    public const double Elbow = 12;
    public const double Inset = 16;
    public const int MaxLabels = 14;

    /// <summary>The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h.
    /// Returns the placements and how many visible labels were left out.</summary>
    public static (List<BioLabelPlacement> Placed, int More) Layout(IReadOnlyList<BioLabelInput> inputs, double cx, double cy, double r, double w, double h)
    {
        var shown = inputs.Where(i => i.Visible).ToList();
        var more = 0;
        if (shown.Count > MaxLabels)
        {
            var keep = shown.Select((l, i) => (l, i)).OrderByDescending(x => x.l.Area).ThenBy(x => x.i).Take(MaxLabels).Select(x => x.i).ToHashSet();
            more = shown.Count - MaxLabels;
            shown = shown.Where((_, i) => keep.Contains(i)).ToList();
        }
        const double step = Height + Gap;
        var out_ = new List<BioLabelPlacement>();
        foreach (var left in new[] { true, false })
        {
            var column = shown.Where(l => (l.X < cx) == left).OrderBy(l => l.Y).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
            if (column.Count == 0) continue;
            var widest = column.Max(l => l.Width);
            // (the column's edge toward the model, kept so its widest label stays inside the view)
            var edge = left ? cx - (r + ColumnOffset) : cx + (r + ColumnOffset);
            edge = left ? Math.Max(edge, Inset + widest) : Math.Min(edge, w - Inset - widest);
            double top = Inset + Height / 2, bottom = h - Inset - Height / 2;
            var ys = column.Select(l => Math.Max(l.Y, top)).ToArray();
            for (var i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + step);
            if (ys[^1] > bottom)
            {
                ys[^1] = bottom;
                for (var i = ys.Length - 2; i >= 0; i--) ys[i] = Math.Min(ys[i], ys[i + 1] - step);
                // (and down again from the top, if pushing up took the first above it: they then share the room)
                if (ys[0] < top)
                {
                    ys[0] = top;
                    for (var i = 1; i < ys.Length; i++) ys[i] = Math.Max(ys[i], ys[i - 1] + step);
                }
            }
            for (var k = 0; k < column.Count; k++)
            {
                var l = column[k];
                out_.Add(new BioLabelPlacement(l.Id, l.Text, left, edge, ys[k], l.Width, l.X, l.Y, left ? edge + Elbow : edge - Elbow));
            }
        }
        return (out_, more);
    }

    /// <summary>A label's width for its text at 12 semibold in a capsule (10 padding a side): an estimate for layouts made
    /// before the text is measured.</summary>
    public static double EstimatedWidth(string text) => text.Length * 6.9 + 20;
}
