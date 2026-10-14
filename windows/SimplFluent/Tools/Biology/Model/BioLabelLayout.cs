namespace Simpl.Tools;

// Labels with leader lines (spec §3.5), laid out as the Mac's BioLabelLayout.swift does: each visible anchor's label
// goes in the column on its side of the model (left when the anchor is left of the model's centre), the columns R + 24
// out from the model's screen circle; a label too wide for the room between its column and the view's edge is moved in
// on its own (the Mac moves the whole column in for its widest label, which in a narrow view here put the column's
// short labels over the model); in a column, labels follow their anchors down and are pushed apart to 22; a column too
// long for the view is pushed up from the bottom. Each leader runs anchor → elbow (12 short of the label, toward the
// model) → label. At most 14 at once: the objects covering the most of the screen keep theirs, the rest are counted
// ("+N more"). Device-independent pixels, y down.
//
// This app's middle column is narrower than the Mac's, so a long label moved in can land on what it names. Given the
// model's box on the view, a label that would hide its own anchor, or (beside a model narrower than 70% of the view)
// reach more than 40 into the model's box (or over half its own width, for a short label), goes instead to a row just
// above the model (an anchor in its upper half) or below it, as near over its anchor as the row allows, its leader
// straight up or down and never through a capsule in a row nearer the model; with no room there it stays in its column.

/// <summary>A label to place: its object, its words, where its anchor projects (y down), whether it can be seen (not
/// behind the camera or hidden by another object), how much of the screen its object covers (the larger keep their labels
/// when there are too many), and its width (its text at 12 semibold plus its capsule's padding).</summary>
public sealed record BioLabelInput(string Id, string Text, double X, double Y, bool Visible = true, double Area = 0, double Width = 80);

/// <summary>A placed label: its column (left: its capsule ends at EdgeX; right: it starts there), its capsule's edge nearest
/// the model, its vertical centre, its width, and its leader (anchor → elbow → (LeaderX, Y)); a label in a row above or
/// below the model starts at EdgeX, and its leader runs from the anchor straight to (ElbowX, EdgeY), its edge.</summary>
public sealed record BioLabelPlacement(string Id, string Text, bool Left, double EdgeX, double Y, double Width, double AnchorX, double AnchorY, double ElbowX, double? EdgeY = null)
{
    /// <summary>The capsule's frame (x, y, width, height), y down.</summary>
    public (double X, double Y, double Width, double Height) Frame => (Left ? EdgeX - Width : EdgeX, Y - BioLabelLayout.Height / 2, Width, BioLabelLayout.Height);

    /// <summary>Where the leader meets the capsule: its edge nearest the model, or its far edge when the anchor lies beyond
    /// that (an anchor at the view's very edge, left of a left label), so the leader never runs back under the words.</summary>
    public double LeaderX => Left ? (AnchorX < EdgeX - Width ? EdgeX - Width : EdgeX) : (AnchorX > EdgeX + Width ? EdgeX + Width : EdgeX);

    /// <summary>The leader's points after the anchor: the elbow and the capsule's edge, or for a label in a row above or
    /// below the model the point on its edge straight over or under the anchor.</summary>
    public (double X, double Y)[] Leader => EdgeY is { } ey ? new[] { (ElbowX, ey) } : new[] { (ElbowX, Y), (LeaderX, Y) };
}

/// <summary>The model's box on the view (y down).</summary>
public readonly record struct BioLabelBox(double X0, double Y0, double X1, double Y1);

public static class BioLabelLayout
{
    public const double Height = 18;
    public const double Gap = 4;
    public const double ColumnOffset = 24;
    public const double Elbow = 12;
    public const double Inset = 16;
    public const int MaxLabels = 14;
    /// <summary>Rows above (or below) the model a label may move to.</summary>
    public const int BandRows = 3;

    /// <summary>The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h;
    /// given the model's box, labels that would hide what they name move to rows above or below it. Returns the placements
    /// and how many visible labels were left out.</summary>
    public static (List<BioLabelPlacement> Placed, int More) Layout(IReadOnlyList<BioLabelInput> inputs, double cx, double cy, double r, double w, double h, BioLabelBox? model = null)
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
            // (the column's edge toward the model; a label too wide to fit outside it in the view is moved in alone, so
            //  one long label does not pull the column's shorter ones onto the model)
            var edge = left ? cx - (r + ColumnOffset) : cx + (r + ColumnOffset);
            double EdgeOf(BioLabelInput l) => left ? Math.Max(edge, Inset + l.Width) : Math.Min(edge, w - Inset - l.Width);
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
                var e = EdgeOf(l);
                var elbow = left ? e + Elbow : e - Elbow;
                // (an anchor beyond the label's far edge — at the view's very edge — is led to that edge instead)
                if (left && l.X < e - l.Width) elbow = e - l.Width - Elbow;
                if (!left && l.X > e + l.Width) elbow = e + l.Width + Elbow;
                out_.Add(new BioLabelPlacement(l.Id, l.Text, left, e, ys[k], l.Width, l.X, l.Y, elbow));
            }
        }
        if (model is { } box) IntoBands(out_, box, w, h);
        return (out_, more);
    }

    /// <summary>Each label that would hide its own anchor, or cover a narrow model for more than half its width, moved to
    /// a row just above the model (an anchor in its upper half) or below it, as near over its anchor as the row allows.</summary>
    private static void IntoBands(List<BioLabelPlacement> placed, BioLabelBox box, double w, double h)
    {
        const double step = Height + Gap, space = 6;
        var narrow = box.X1 - box.X0 < 0.7 * w;
        bool Hides(BioLabelPlacement p)
        {
            var f = p.Frame;
            var onAnchor = p.AnchorX >= f.X - 4 && p.AnchorX <= f.X + f.Width + 4 && Math.Abs(p.AnchorY - p.Y) <= Height;
            var acrossModel = p.Y + Height / 2 >= box.Y0 && p.Y - Height / 2 <= box.Y1
                && Math.Max(0, Math.Min(f.X + f.Width, box.X1) - Math.Max(f.X, box.X0)) > Math.Min(40, 0.5 * f.Width);
            return onAnchor || (narrow && acrossModel);
        }
        var moving = placed.Where(Hides).OrderBy(p => p.AnchorX).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        if (moving.Count == 0) return;
        var mid = (box.Y0 + box.Y1) / 2;
        double top = Inset + Height / 2, bottom = h - Inset - Height / 2;
        // (what each row already holds: the capsules left in the columns that reach into it, then the labels moved there)
        var taken = new Dictionary<(bool Above, int Row), List<(double X0, double X1)>>();
        double RowY(bool above, int k) => above ? box.Y0 - space - Height / 2 - k * step : box.Y1 + space + Height / 2 + k * step;
        List<(double X0, double X1)> Taken(bool above, int k)
        {
            if (taken.TryGetValue((above, k), out var list)) return list;
            var y = RowY(above, k);
            list = placed.Where(p => !moving.Contains(p) && Math.Abs(p.Y - y) < Height + Gap / 2)
                .Select(p => (p.Frame.X - space, p.Frame.X + p.Frame.Width + space)).ToList();
            taken[(above, k)] = list;
            return list;
        }
        // (whether a leader from the anchor to a label in row k would run through a capsule in a row nearer the model)
        bool Crosses(bool above, int k, double ax, double ay, double ex, double ey)
        {
            for (var j = 0; j < k; j++)
            {
                double yj = RowY(above, j), ya = yj - Height / 2, yb = yj + Height / 2;
                double XAt(double y) => Math.Abs(ey - ay) < 1e-9 ? ex : ax + (ex - ax) * (y - ay) / (ey - ay);
                double x0 = Math.Min(XAt(ya), XAt(yb)), x1 = Math.Max(XAt(ya), XAt(yb));
                if (Taken(above, j).Any(t => x0 < t.X1 - space && x1 > t.X0 + space)) return true;
            }
            return false;
        }
        foreach (var p in moving)
        {
            var above = p.AnchorY < mid;
            var want = Math.Clamp(p.AnchorX - p.Width / 2, Inset, Math.Max(Inset, w - Inset - p.Width));
            (int Row, double X)? best = null;
            var bestCost = double.MaxValue;
            for (var k = 0; k < BandRows; k++)
            {
                var y = RowY(above, k);
                if (y < top || y > bottom) break;
                var row = Taken(above, k);
                // (the places it could go in this row: where it wants to be, or just beside a capsule already there)
                var tries = new List<double> { want };
                foreach (var (x0, x1) in row) { tries.Add(x1); tries.Add(x0 - p.Width); }
                foreach (var x in tries)
                {
                    if (x < Inset - 1e-9 || x + p.Width > w - Inset + 1e-9) continue;
                    if (row.Any(t => x < t.X1 && x + p.Width > t.X0)) continue;
                    var ex = Math.Clamp(p.AnchorX, x + 9, x + p.Width - 9);
                    if (Crosses(above, k, p.AnchorX, p.AnchorY, ex, above ? y + Height / 2 : y - Height / 2)) continue;
                    var cost = Math.Abs(x - want) + 40 * k;
                    if (cost < bestCost) { bestCost = cost; best = (k, x); }
                }
            }
            if (best is not { } b) continue;
            var yRow = RowY(above, b.Row);
            Taken(above, b.Row).Add((b.X - space, b.X + p.Width + space));
            var at = placed.IndexOf(p);
            var elbowX = Math.Clamp(p.AnchorX, b.X + 9, b.X + p.Width - 9);
            placed[at] = new BioLabelPlacement(p.Id, p.Text, false, b.X, yRow, p.Width, p.AnchorX, p.AnchorY, elbowX,
                above ? yRow + Height / 2 : yRow - Height / 2);
        }
    }

    /// <summary>A label's width for its text at 12 semibold in a capsule (10 padding a side): an estimate for layouts made
    /// before the text is measured.</summary>
    public static double EstimatedWidth(string text) => text.Length * 6.9 + 20;
}
