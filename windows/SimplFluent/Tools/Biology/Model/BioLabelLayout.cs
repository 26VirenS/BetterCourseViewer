namespace Simpl.Tools;

// Labels with leader lines (spec §3.5), laid out as the Mac's BioLabelLayout.swift does: each visible anchor's label
// goes in the column on its side of the model (left when the anchor is left of the model's centre), the columns R + 24
// out from the model's screen circle — or, given the model's box on the view, 24 out from the box, so the labels of a
// model narrower than its circle (the double helix) sit beside it, not flung to the view's edges; a label too wide for
// the room between its column and the view's edge is moved in on its own (the Mac moves the whole column in for its
// widest label, which in a narrow view here put the column's short labels over the model); in a column, labels follow
// their anchors down and are pushed apart to 22; a column too long for the view is pushed up from the bottom. Each
// leader runs anchor → elbow (12 short of the label, toward the model) → label. The two columns are kept even: while one
// has three or more labels more than the other, its label nearest the model's middle (within the middle half) goes over
// — an animal cell's parts lie mostly right of its centre, and its right column ran out of room ("+1 more") beside a left
// one of four. A column holds as many as the view's height has room for, the objects covering the most of the screen
// keeping theirs when it has not; only those left over are counted ("+N more"). Device-independent pixels, y down.
//
// This app's middle column is narrower than the Mac's, so a long label moved in can land on what it names. Given the
// model's box on the view, a label that would hide its own anchor, or (beside a model narrower than 70% of the view)
// reach more than 40 into the model's box (or over half its own width, for a short label), goes instead to a row just
// above the model (an anchor in its upper half) or below it. A row's labels are packed side by side in their anchors'
// order, each as near over its anchor as the others allow (a label already there slides aside for a new one), their
// leaders straight up or down and never through a capsule in a row nearer the model; a label that fits no row (three
// at most) stays in its column.

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
    /// <summary>Spec §3.5's fourteen: what the helix's and the division stages' checks hold their labels to. The view
    /// itself shows as many labels as its height has room for.</summary>
    public const int MaxLabels = 14;
    /// <summary>Rows above (or below) the model a label may move to.</summary>
    public const int BandRows = 3;

    /// <summary>The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h;
    /// given the model's box, labels that would hide what they name move to rows above or below it; given a box to keep
    /// clear (a scale bar over the view's corner), no label goes over it. Returns the placements and how many visible
    /// labels were left out.</summary>
    public static (List<BioLabelPlacement> Placed, int More) Layout(IReadOnlyList<BioLabelInput> inputs, double cx, double cy, double r, double w, double h, BioLabelBox? model = null,
        BioLabelBox? keepClear = null)
    {
        var shown = inputs.Where(i => i.Visible).ToList();
        const double step = Height + Gap;
        // (each label's side, kept even: the side with three or more labels more than the other gives its label nearest
        //  the model's middle — within the middle half of the model — to the other, one at a time)
        var onLeft = shown.Select(l => l.X < cx).ToArray();
        var half = model is { } mb && mb.X1 > mb.X0 ? (mb.X1 - mb.X0) / 2 : r;
        while (true)
        {
            var nl = onLeft.Count(x => x);
            var nr = onLeft.Length - nl;
            if (Math.Abs(nl - nr) <= 2) break;
            var from = nl > nr;
            var k = Enumerable.Range(0, onLeft.Length).Where(i => onLeft[i] == from)
                .OrderBy(i => Math.Abs(shown[i].X - cx)).ThenBy(i => shown[i].Id, StringComparer.Ordinal).First();
            if (Math.Abs(shown[k].X - cx) > 0.5 * half) break;
            onLeft[k] = !from;
        }
        var more = 0;
        var out_ = new List<BioLabelPlacement>();
        foreach (var left in new[] { true, false })
        {
            var mine = Enumerable.Range(0, shown.Count).Where(i => onLeft[i] == left).ToList();
            if (mine.Count == 0) continue;
            // (the column's edge toward the model — 24 out from its box, when the box is narrower than its circle; a label
            //  too wide to fit outside it in the view is moved in alone, so one long label does not pull the column's
            //  shorter ones onto the model)
            var edge = left ? cx - (r + ColumnOffset) : cx + (r + ColumnOffset);
            if (model is { } b && b.X1 > b.X0) edge = left ? Math.Max(edge, b.X0 - ColumnOffset) : Math.Min(edge, b.X1 + ColumnOffset);
            double EdgeOf(BioLabelInput l) => left ? Math.Max(edge, Inset + l.Width) : Math.Min(edge, w - Inset - l.Width);
            double top = Inset + Height / 2, bottom = h - Inset - Height / 2;
            // (a column that reaches over the box kept clear ends above it, with a line's room for "+N more")
            if (keepClear is { } kc)
            {
                double x0 = mine.Min(i => left ? EdgeOf(shown[i]) - shown[i].Width : EdgeOf(shown[i]));
                double x1 = mine.Max(i => left ? EdgeOf(shown[i]) : EdgeOf(shown[i]) + shown[i].Width);
                if (x0 < kc.X1 && x1 > kc.X0) bottom = Math.Min(bottom, kc.Y0 - Gap - Height);
            }
            // (a column holds what its height has room for, 22 apart inside its edges: past that, the objects covering the
            //  most of the screen keep their labels and the rest are counted)
            var holds = Math.Max(1, (int)Math.Floor((bottom - top) / step + 1e-9) + 1);
            if (mine.Count > holds)
            {
                var keep = mine.OrderByDescending(i => shown[i].Area).ThenBy(i => i).Take(holds).ToHashSet();
                more += mine.Count - holds;
                mine = mine.Where(keep.Contains).ToList();
            }
            var column = mine.Select(i => shown[i]).OrderBy(l => l.Y).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
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
        if (model is { } box) IntoBands(out_, box, w, h, keepClear);
        return (out_, more);
    }

    /// <summary>Each label that would hide its own anchor, or reach well into a narrow model, moved to a row just above the
    /// model (an anchor in its upper half) or below it, as near over its anchor as the row allows (and never over the box
    /// kept clear).</summary>
    private static void IntoBands(List<BioLabelPlacement> placed, BioLabelBox box, double w, double h, BioLabelBox? keepClear)
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
        double RowY(bool above, int k) => above ? box.Y0 - space - Height / 2 - k * step : box.Y1 + space + Height / 2 + k * step;
        // (each row's capsules left in the columns that reach into it, which stay put)
        var fixedIn = new Dictionary<(bool Above, int Row), List<(double X0, double X1)>>();
        List<(double X0, double X1)> Fixed(bool above, int k)
        {
            if (fixedIn.TryGetValue((above, k), out var list)) return list;
            var y = RowY(above, k);
            list = placed.Where(p => !moving.Contains(p) && Math.Abs(p.Y - y) < Height + Gap / 2)
                .Select(p => (p.Frame.X - space, p.Frame.X + p.Frame.Width + space)).ToList();
            // (the box kept clear stands in a row that reaches it, as a capsule would)
            if (keepClear is { } kc && y + Height / 2 > kc.Y0 && y - Height / 2 < kc.Y1) list.Add((kc.X0 - space, kc.X1 + space));
            fixedIn[(above, k)] = list;
            return list;
        }
        // (the labels moved to each row, with where each sits)
        var rows = new Dictionary<(bool Above, int Row), List<(BioLabelPlacement P, double X)>>();
        List<(BioLabelPlacement P, double X)> Row(bool above, int k) =>
            rows.TryGetValue((above, k), out var r) ? r : rows[(above, k)] = new List<(BioLabelPlacement P, double X)>();
        double Want(BioLabelPlacement p) => Math.Clamp(p.AnchorX - p.Width / 2, Inset, Math.Max(Inset, w - Inset - p.Width));
        double ElbowOf(BioLabelPlacement p, double x) => Math.Clamp(p.AnchorX, x + 9, x + p.Width - 9);
        // (where a row's labels go, each as near over its anchor as it can: left to right in their anchors' order, pushed
        //  apart, then back from the right edge if that ran over; null when they do not fit between the view's edges)
        List<double>? Pack(List<BioLabelPlacement> ps)
        {
            var xs = ps.Select(Want).ToList();
            for (var i = 1; i < xs.Count; i++) xs[i] = Math.Max(xs[i], xs[i - 1] + ps[i - 1].Width + space);
            if (xs[^1] + ps[^1].Width > w - Inset)
            {
                xs[^1] = w - Inset - ps[^1].Width;
                for (var i = xs.Count - 2; i >= 0; i--) xs[i] = Math.Min(xs[i], xs[i + 1] - space - ps[i].Width);
            }
            return xs[0] < Inset - 1e-9 ? null : xs;
        }
        // (whether a leader from an anchor to the edge of a capsule in row k runs through a capsule in a row nearer the
        //  model, given those rows' labels)
        bool Crosses(bool above, int k, BioLabelPlacement p, double x, Func<int, IEnumerable<(double X0, double X1)>> capsules)
        {
            double ax = p.AnchorX, ay = p.AnchorY, ex = ElbowOf(p, x), y = RowY(above, k), ey = above ? y + Height / 2 : y - Height / 2;
            for (var j = 0; j < k; j++)
            {
                double yj = RowY(above, j), ya = yj - Height / 2, yb = yj + Height / 2;
                double XAt(double yy) => Math.Abs(ey - ay) < 1e-9 ? ex : ax + (ex - ax) * (yy - ay) / (ey - ay);
                double x0 = Math.Min(XAt(ya), XAt(yb)), x1 = Math.Max(XAt(ya), XAt(yb));
                if (capsules(j).Any(t => x0 < t.X1 - space && x1 > t.X0 + space)) return true;
            }
            return false;
        }
        IEnumerable<(double X0, double X1)> Capsules(bool above, int j, (int Row, List<(BioLabelPlacement P, double X)> Trial)? swap = null)
        {
            var moved = swap is { } t && t.Row == j ? t.Trial : Row(above, j);
            return Fixed(above, j).Concat(moved.Select(m => (m.X - space, m.X + m.P.Width + space)));
        }
        foreach (var p in moving)
        {
            var above = p.AnchorY < mid;
            for (var k = 0; k < BandRows; k++)
            {
                var y = RowY(above, k);
                if (y < top || y > bottom) break;
                List<(BioLabelPlacement P, double X)>? trial = null;
                var fixedHere = Fixed(above, k);
                if (fixedHere.Count == 0)
                {
                    // (a row with no column labels in it: its moved labels and this one packed together)
                    var ps = Row(above, k).Select(m => m.P).Append(p).OrderBy(q => q.AnchorX).ThenBy(q => q.Id, StringComparer.Ordinal).ToList();
                    if (Pack(ps) is { } xs) trial = ps.Select((q, i) => (q, xs[i])).ToList();
                }
                else
                {
                    // (beside column labels: where it wants to be, or just beside a capsule already there)
                    var taken = Capsules(above, k).ToList();
                    var want = Want(p);
                    var tries = new List<double> { want };
                    foreach (var (x0, x1) in taken) { tries.Add(x1); tries.Add(x0 - p.Width); }
                    double? best = null;
                    foreach (var x in tries.OrderBy(x => Math.Abs(x - want)))
                    {
                        if (x < Inset - 1e-9 || x + p.Width > w - Inset + 1e-9 || taken.Any(t => x < t.X1 && x + p.Width > t.X0)) continue;
                        best = x;
                        break;
                    }
                    if (best is { } bx) trial = Row(above, k).Append((p, bx)).ToList();
                }
                if (trial == null) continue;
                // (every leader into this row clear of the rows nearer the model, and every leader into a row further out
                //  clear of this row as it would be)
                var swap = (k, trial);
                var clear = trial.All(m => !Crosses(above, k, m.P, m.X, j => Capsules(above, j)))
                    && Enumerable.Range(k + 1, BandRows - k - 1).All(o => Row(above, o).All(m => !Crosses(above, o, m.P, m.X, j => Capsules(above, j, swap))));
                if (!clear) continue;
                rows[(above, k)] = trial;
                break;
            }
        }
        // (a label that found no row stays in its column, where a row's label may since have been packed over it — the
        //  membrane's "Peripheral protein" under "Extracellular side": that one goes back to its own column too)
        var inRows = rows.Values.SelectMany(r => r.Select(m => m.P)).ToHashSet();
        var stuck = moving.Where(p => !inRows.Contains(p)).Select(p => p.Frame).ToList();
        foreach (var key in rows.Keys.ToList())
        {
            var y = RowY(key.Above, key.Row);
            rows[key] = rows[key].Where(m => !stuck.Any(f => Math.Abs(f.Y + Height / 2 - y) < Height + Gap / 2 && m.X < f.X + f.Width + space && m.X + m.P.Width > f.X - space)).ToList();
        }
        foreach (var ((above, k), list) in rows)
        {
            var yRow = RowY(above, k);
            foreach (var (p, x) in list)
                placed[placed.IndexOf(p)] = new BioLabelPlacement(p.Id, p.Text, false, x, yRow, p.Width, p.AnchorX, p.AnchorY, ElbowOf(p, x),
                    above ? yRow + Height / 2 : yRow - Height / 2);
        }
    }

    /// <summary>A label's width for its text at 12 semibold in a capsule (10 padding a side): an estimate for layouts made
    /// before the text is measured.</summary>
    public static double EstimatedWidth(string text) => text.Length * 6.9 + 20;
}
