namespace Simpl.Tools;

// Labels with leader lines (spec §3.5), laid out as the Mac's BioLabelLayout.swift does: each visible anchor's label
// goes in the column on its side of the model (left when the anchor is left of the model's centre), the columns R + 24
// out from the model's screen circle — or, given the model's box on the view, 24 out from the box, so the labels of a
// model narrower than its circle (the double helix) sit beside it, not flung to the view's edges; a label too wide for
// the room between its column and the view's edge is moved in on its own (the Mac moves the whole column in for its
// widest label, which in a narrow view here put the column's short labels over the model); in a column, labels follow
// their anchors down and are pushed apart to 22; a column too long for the view is pushed up from the bottom; two
// leaders in a column that cross swap places (an anchor deep in the model above one near its edge crossed it on its
// way out). Each leader runs anchor → elbow (12 short of the label, toward the model) → label. The two columns are kept
// even: while one has three or more labels more than the other, its label nearest the model's middle (within the middle
// half) goes over — an animal cell's parts lie mostly right of its centre, and its right column ran out of room ("+1
// more") beside a left one of four. A column holds as many as the view's height has room for; one with more than that
// gives its labels nearest the model's middle to the other while the other has room — however far out they lie, as
// the Mac does: held to the middle half, the cell's right column in a tool's popup on a 1024 window dropped Vesicles
// beside a left one with room for two — and past that the objects covering the most of the screen keep theirs; only
// those left over are counted ("+N more"). Device-independent pixels, y down.
//
// This app's middle column is narrower than the Mac's, so a long label moved in can land on what it names. Given the
// model's box on the view, a label that would hide its own anchor or another label's, or (beside a model narrower than
// 70% of the view) reach more than Reach into the model's box (40; over half its own width, for a short label), goes
// instead to a row just above the model (an anchor in its
// upper half) or below it, or (an anchor in the middle half of its height) the other side where its own has no room. A
// row's labels are packed side by side in their anchors' order, each as near over its anchor as the others allow (a
// label already there slides aside for a new one) and clear of the leaders crossing the row on their way to a column,
// their own leaders straight up or down and never through a capsule in a row nearer the model; a label that fits no row
// (three at most) stays in its column. An anchor straight over or under its own column label is
// led straight to the label's edge. The 3D view stands its camera back until no label lies on the model (Conflicts).
//
// Given the model's outline on the view (BioOutline: the hull of its outermost points as the camera sees them), the 3D
// view's labels are kept off the model itself, not its box: each column's labels, in order round the model's middle,
// are spread down the column to the heights nearest their anchors where each fits beside the outline (24 from it, inside
// the view, off the box kept clear) — so a cell filling three quarters of its view has its labels in the corners its
// round outline leaves, not in columns beside its widest part that held it to half the view. A label with no such place
// goes to a row above or below the model; one still in the way (on the model, over another's anchor, or led through
// another's capsule) is folded into "+N more", the smallest object's first.

/// <summary>A label to place: its object, its words, where its anchor projects (y down), whether it can be seen (not
/// behind the camera or hidden by another object), how much of the screen its object covers (the larger keep their labels
/// when there are too many), and its width (its text at 12 semibold plus its capsule's padding).</summary>
public sealed record BioLabelInput(string Id, string Text, double X, double Y, bool Visible = true, double Area = 0, double Width = 80);

/// <summary>A placed label: its column (left: its capsule ends at EdgeX; right: it starts there), its capsule's edge nearest
/// the model, its vertical centre, its width, and its leader (anchor → elbow → (LeaderX, Y)); a label in a row above or
/// below the model (<paramref name="Row"/>) starts at EdgeX, and its leader runs from the anchor straight to (ElbowX,
/// EdgeY), its edge — as does a column's label whose anchor lies straight over or under it.</summary>
public sealed record BioLabelPlacement(string Id, string Text, bool Left, double EdgeX, double Y, double Width, double AnchorX, double AnchorY, double ElbowX, double? EdgeY = null,
    bool Row = false)
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

/// <summary>
/// The model's outline on the view (y down): the convex hull of its outermost points as the camera shows them. A round
/// cell leaves its box's corners empty, and the box of a model seen in perspective is wider than the model by its near
/// corners: labels kept off the box held a cell to half its view, so with an outline they are kept off the model itself.
/// </summary>
public sealed class BioOutline
{
    private readonly (double X, double Y)[] hull;

    private BioOutline((double X, double Y)[] hull, BioLabelBox box)
    {
        this.hull = hull;
        Box = box;
    }

    /// <summary>Its corners, in order round it.</summary>
    public IReadOnlyList<(double X, double Y)> Points => hull;

    /// <summary>Its box.</summary>
    public BioLabelBox Box { get; }

    /// <summary>The outline round some points (Andrew's monotone chain); null for none.</summary>
    public static BioOutline? Round(IEnumerable<(double X, double Y)> points)
    {
        var p = points.Where(q => double.IsFinite(q.X) && double.IsFinite(q.Y)).Distinct().OrderBy(q => q.X).ThenBy(q => q.Y).ToArray();
        if (p.Length == 0) return null;
        var box = new BioLabelBox(p.Min(q => q.X), p.Min(q => q.Y), p.Max(q => q.X), p.Max(q => q.Y));
        if (p.Length < 3) return new BioOutline(p, box);
        static double Turn((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var h = new (double X, double Y)[2 * p.Length];
        var k = 0;
        for (var i = 0; i < p.Length; i++)
        {
            while (k >= 2 && Turn(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Length - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Turn(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        return new BioOutline(h[..Math.Max(1, k - 1)], box);
    }

    /// <summary>The outline kept to the view (a model running off its edges), for the labels' sake.</summary>
    public BioOutline Within(double w, double h) => new(hull, new BioLabelBox(Math.Max(Box.X0, 0), Math.Max(Box.Y0, 0), Math.Min(Box.X1, w), Math.Min(Box.Y1, h)));

    /// <summary>The least and greatest x the outline reaches between the heights <paramref name="y0"/> and
    /// <paramref name="y1"/>; null where it does not reach them.</summary>
    public (double X0, double X1)? Across(double y0, double y1)
    {
        if (y1 < Box.Y0 || y0 > Box.Y1 || hull.Length == 0) return null;
        // (a pixel row at a time, the rows reaching the band's ends taken whole: never less than the outline is)
        Rows();
        int from = Math.Max(0, (int)Math.Floor(y0) - rowFirst), to = Math.Min(rowMin!.Length - 1, (int)Math.Ceiling(y1) - rowFirst);
        double x0 = double.MaxValue, x1 = double.MinValue;
        for (var k = from; k <= to; k++)
        {
            x0 = Math.Min(x0, rowMin[k]);
            x1 = Math.Max(x1, rowMax![k]);
        }
        return x0 <= x1 ? (x0, x1) : null;
    }

    private double[]? rowMin, rowMax;
    private int rowFirst;

    /// <summary>The outline's least and greatest x in each pixel row its box spans (y to y + 1), worked out once: the labels'
    /// layout asks for them a few thousand times.</summary>
    private void Rows()
    {
        if (rowMin != null) return;
        rowFirst = (int)Math.Floor(Box.Y0);
        var n = Math.Max(1, (int)Math.Ceiling(Box.Y1) - rowFirst + 1);
        rowMin = new double[n];
        rowMax = new double[n];
        Array.Fill(rowMin, double.MaxValue);
        Array.Fill(rowMax, double.MinValue);
        void Take(int k, double x)
        {
            if (k < 0 || k >= n) return;
            rowMin[k] = Math.Min(rowMin[k], x);
            rowMax[k] = Math.Max(rowMax[k], x);
        }
        for (var i = 0; i < hull.Length; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            // (each side where it crosses each row's top and bottom, and its ends in their own rows)
            Take((int)Math.Floor(a.Y) - rowFirst, a.X);
            double lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
            for (var y = (int)Math.Ceiling(lo); y <= hi; y++)
            {
                var x = hi - lo < 1e-9 ? a.X : a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                Take(y - rowFirst, x);
                Take(y - 1 - rowFirst, x);
            }
        }
    }

    /// <summary>Whether a rectangle lies on the outline by more than <paramref name="by"/> each way: the rectangle drawn in
    /// by it on each side, and the two tested as the convex shapes they are (no line between them).</summary>
    public bool Overlaps(double x0, double y0, double x1, double y1, double by = 0)
    {
        (x0, y0, x1, y1) = (x0 + by, y0 + by, x1 - by, y1 - by);
        if (x1 <= x0 || y1 <= y0 || hull.Length == 0) return false;
        // (along x and y: the outline's box)
        if (x1 <= Box.X0 || x0 >= Box.X1 || y1 <= Box.Y0 || y0 >= Box.Y1) return false;
        // (across each of the outline's sides)
        var corners = new[] { (X: x0, Y: y0), (X: x1, Y: y0), (X: x1, Y: y1), (X: x0, Y: y1) };
        for (var i = 0; i < hull.Length && hull.Length >= 2; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            double nx = b.Y - a.Y, ny = a.X - b.X;
            if (nx == 0 && ny == 0) continue;
            double h0 = double.MaxValue, h1 = double.MinValue, r0 = double.MaxValue, r1 = double.MinValue;
            foreach (var q in hull) { var t = q.X * nx + q.Y * ny; h0 = Math.Min(h0, t); h1 = Math.Max(h1, t); }
            foreach (var c in corners) { var t = c.X * nx + c.Y * ny; r0 = Math.Min(r0, t); r1 = Math.Max(r1, t); }
            if (r1 <= h0 || r0 >= h1) return false;
        }
        return true;
    }
}

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
    /// <summary>How far a column's label may reach into a narrow model before it moves to a row (or half its width, for a
    /// short label).</summary>
    public const double Reach = 40;

    /// <summary>The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h;
    /// given the model's box, labels that would hide what they name, or reach more than <paramref name="reach"/> into a
    /// narrow model, move to rows above or below it; given a box to keep clear (a scale bar over the view's corner), no
    /// label goes over it. Given the model's outline (its box is then the outline's), each label is kept off the model
    /// itself rather than its box: a column's label comes in from its column, where the view's edge asks it to, no nearer
    /// the outline than a column is to the box (<see cref="ColumnOffset"/>), and the column's labels, in their anchors'
    /// order, each sit as near its anchor's height as leaves it beside the outline — a long label beside the middle of a
    /// round cell goes up or down to where the cell narrows, so the cell can fill its view and its labels use the corners
    /// its box left empty; and given <paramref name="folded"/> too, a label that still lies on the model, over another's
    /// anchor or with its leader through another's capsule tries a row above or below the model, and then, the smallest
    /// object's first, is folded into "+N more" (its id added to the list). No label goes above <paramref name="clearTop"/>
    /// (a note laid over the view's top). Returns the placements and how many visible labels were left out.</summary>
    public static (List<BioLabelPlacement> Placed, int More) Layout(IReadOnlyList<BioLabelInput> inputs, double cx, double cy, double r, double w, double h, BioLabelBox? model = null,
        BioLabelBox? keepClear = null, double reach = Reach, BioOutline? outline = null, List<string>? folded = null, double clearTop = 0)
    {
        if (outline == null || folded == null) return Place(inputs, cx, cy, r, w, h, model, keepClear, reach, outline, clearTop: clearTop);
        // (beside an outline, asked to: a label that cannot be placed off the model, or whose leader runs through another's
        //  capsule, is folded into "+N more" — the smallest object's first, of those in one another's way — and the rest
        //  laid out again, until none is: the 3D view does not stand its camera back past SceneFrame's least for a label)
        var shownNow = inputs.ToList();
        var toRows = new HashSet<(string, string)>();
        while (true)
        {
            var (placed, more) = Place(shownNow, cx, cy, r, w, h, model, keepClear, reach, outline, toRows, clearTop);
            var bad = Conflicting(placed, outline.Box, outline, withOthers: true);
            if (bad.Count == 0) return (placed, more + folded.Count);
            // (a label in the way that has not tried a row above or below the model tries one first)
            var unrowed = bad.Where(p => !p.Row && !toRows.Contains((p.Id, p.Text))).ToList();
            if (unrowed.Count > 0)
            {
                foreach (var p in unrowed) toRows.Add((p.Id, p.Text));
                continue;
            }
            // (one with no room beside the model at all: any label of its column, folded, would make room for it)
            var crowded = bad.Where(p => !p.Row && outline.Overlaps(p.Frame.X, p.Frame.Y, p.Frame.X + p.Frame.Width, p.Frame.Y + p.Frame.Height, 4)).ToList();
            var drop = bad.Concat(placed.Where(o => !o.Row && crowded.Any(p => p.Left == o.Left))).Distinct()
                .OrderBy(p => shownNow.First(i => i.Id == p.Id && i.Text == p.Text).Area).ThenBy(p => p.Id, StringComparer.Ordinal).First();
            var at = shownNow.FindIndex(i => i.Id == drop.Id && i.Text == drop.Text);
            shownNow[at] = shownNow[at] with { Visible = false };
            folded.Add(drop.Id);
        }
    }

    private static (List<BioLabelPlacement> Placed, int More) Place(IReadOnlyList<BioLabelInput> inputs, double cx, double cy, double r, double w, double h, BioLabelBox? model,
        BioLabelBox? keepClear, double reach, BioOutline? outline, HashSet<(string, string)>? toRows = null, double clearTop = 0)
    {
        if (outline != null) model = outline.Box;
        var hugging = outline != null;
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
        // (the column's edge toward the model — 24 out from its box, when the box is narrower than its circle; a label too
        //  wide to fit outside it in the view is moved in alone, so one long label does not pull the column's shorter ones
        //  onto the model)
        double EdgeFor(bool left)
        {
            var edge = left ? cx - (r + ColumnOffset) : cx + (r + ColumnOffset);
            if (model is { } b && b.X1 > b.X0) edge = left ? Math.Max(edge, b.X0 - ColumnOffset) : Math.Min(edge, b.X1 + ColumnOffset);
            return edge;
        }
        double EdgeOfSide(bool left, BioLabelInput l) => left ? Math.Max(EdgeFor(true), Inset + l.Width) : Math.Min(EdgeFor(false), w - Inset - l.Width);
        double top = Math.Max(Inset, clearTop) + Height / 2;
        // (a column's lowest place: a column that reaches over the box kept clear ends above it, with a line's room for
        //  "+N more")
        double BottomOf(bool left, List<int> mine)
        {
            var bottom = h - Inset - Height / 2;
            if (keepClear is { } kc && mine.Count > 0)
            {
                double x0 = mine.Min(i => left ? EdgeOfSide(left, shown[i]) - shown[i].Width : EdgeOfSide(left, shown[i]));
                double x1 = mine.Max(i => left ? EdgeOfSide(left, shown[i]) : EdgeOfSide(left, shown[i]) + shown[i].Width);
                if (x0 < kc.X1 && x1 > kc.X0) bottom = Math.Min(bottom, kc.Y0 - Gap - Height);
            }
            return bottom;
        }
        // (a column holds what its height has room for, 22 apart inside its edges)
        int Holds(bool left, List<int> mine) => Math.Max(1, (int)Math.Floor((BottomOf(left, mine) - top) / step + 1e-9) + 1);
        List<int> Members(bool left) => Enumerable.Range(0, shown.Count).Where(i => onLeft[i] == left).ToList();
        // (a column with more than it holds — the left one cut short by Cell Explorer's scale bar, the right one of a cell
        //  whose parts lie mostly right of its centre — gives its labels nearest the model's middle to the other while
        //  that has room, rather than counting them out: a label shown with a longer leader, not one dropped)
        for (var guard = 0; guard < shown.Count; guard++)
        {
            List<int> ls = Members(true), rs = Members(false);
            bool leftOver = ls.Count > Holds(true, ls) && rs.Count < Holds(false, rs);
            bool rightOver = rs.Count > Holds(false, rs) && ls.Count < Holds(true, ls);
            if (!leftOver && !rightOver) break;
            var k = (leftOver ? ls : rs).OrderBy(i => Math.Abs(shown[i].X - cx)).ThenBy(i => shown[i].Id, StringComparer.Ordinal).Select(i => (int?)i).FirstOrDefault();
            if (k is not { } move) break;
            onLeft[move] = !leftOver;
        }
        var more = 0;
        var out_ = new List<BioLabelPlacement>();
        var homeless = new HashSet<(string, string)>(toRows ?? new HashSet<(string, string)>());
        foreach (var left in new[] { true, false })
        {
            var mine = Members(left);
            if (mine.Count == 0) continue;
            double EdgeOf(BioLabelInput l) => EdgeOfSide(left, l);
            var bottom = BottomOf(left, mine);
            // (past what a column holds, the objects covering the most of the screen keep their labels and the rest are
            //  counted)
            var holds = Holds(left, mine);
            if (mine.Count > holds)
            {
                var keep = mine.OrderByDescending(i => shown[i].Area).ThenBy(i => i).Take(holds).ToHashSet();
                more += mine.Count - holds;
                mine = mine.Where(keep.Contains).ToList();
            }
            // (in their anchors' order down the column — or, beside an outline, round the model's middle from its top to its
            //  bottom: a label sent up to a corner is led there from the anchor highest round that side, not across the
            //  leaders of those below it)
            var column = (!hugging ? mine.Select(i => shown[i]).OrderBy(l => l.Y)
                    : mine.Select(i => shown[i]).OrderBy(l => -Math.Atan2(cy - l.Y, left ? cx - l.X : l.X - cx)))
                .ThenBy(l => l.Id, StringComparer.Ordinal).ToList();
            // (with the outline: a label's edge at a height — its column's, or nearer the model where the view's edge asks,
            //  but never nearer the outline there than a column is to the box; where the outline does not reach that
            //  height, its side of the middle — and whether it fits there, inside the view and off the box kept clear. The
            //  outline's edge at each height is worked out once a column: the spread asks for it at every height for every
            //  label)
            var hugs = hugging ? new double[Math.Max(1, (int)Math.Ceiling(h - Inset - Height / 2 - top) + 2)] : Array.Empty<double>();
            Array.Fill(hugs, double.NaN);
            double Hug(double y)
            {
                var s = (int)Math.Round(y - top);
                if (s >= 0 && s < hugs.Length && !double.IsNaN(hugs[s])) return hugs[s];
                var across = outline!.Across(y - Height / 2 - Gap, y + Height / 2 + Gap);
                var hug = across is { } a ? (left ? a.X0 - ColumnOffset : a.X1 + ColumnOffset) : (left ? cx - Gap : cx + Gap);
                if (s >= 0 && s < hugs.Length && Math.Abs(y - top - s) < 1e-9) hugs[s] = hug;
                return hug;
            }
            double Beside(BioLabelInput l, double y)
            {
                var e = EdgeOf(l);
                if (!hugging) return e;
                return left ? Math.Min(e, Hug(y)) : Math.Max(e, Hug(y));
            }
            // (one with no room there kept inside the view all the same: it then lies on the model, and counts against the fit)
            double EdgeAt(BioLabelInput l, double y) => left ? Math.Max(Beside(l, y), Inset + l.Width) : Math.Min(Beside(l, y), w - Inset - l.Width);
            bool Fits(BioLabelInput l, double y)
            {
                var e = Beside(l, y);
                var x0 = left ? e - l.Width : e;
                if (x0 < Inset - 1e-9 || x0 + l.Width > w - Inset + 1e-9) return false;
                return keepClear is not { } kc || x0 >= kc.X1 || x0 + l.Width <= kc.X0 || y + Height / 2 <= kc.Y0 || y - Height / 2 >= kc.Y1;
            }
            double[] ys;
            if (hugging) ys = Spread(column, Fits, top, h - Inset - Height / 2, step);
            else
            {
                ys = column.Select(l => Math.Max(l.Y, top)).ToArray();
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
            }
            double ElbowOf(BioLabelInput l, double y)
            {
                var e = EdgeAt(l, y);
                // (an anchor beyond the label's far edge — at the view's very edge — is led to that edge instead)
                if (left && l.X < e - l.Width) return e - l.Width - Elbow;
                if (!left && l.X > e + l.Width) return e + l.Width + Elbow;
                return left ? e + Elbow : e - Elbow;
            }
            // (two leaders that cross swap places: in order of their anchors' heights, an anchor deep in the model above
            //  one near its edge crossed it on the way out — the membrane's "Cytoplasm side" over "Phospholipid bilayer")
            for (var pass = 0; pass < column.Count * column.Count; pass++)
            {
                var swapped = false;
                for (var i = 0; i < column.Count && !swapped; i++)
                    for (var j = i + 1; j < column.Count && !swapped; j++)
                    {
                        if (!Crossing(column[i].X, column[i].Y, ElbowOf(column[i], ys[i]), ys[i], column[j].X, column[j].Y, ElbowOf(column[j], ys[j]), ys[j])) continue;
                        // (beside an outline, only where each still fits at the other's height, and the swap uncrosses them)
                        if (hugging && (!Fits(column[i], ys[j]) || !Fits(column[j], ys[i])
                            || Crossing(column[i].X, column[i].Y, ElbowOf(column[i], ys[j]), ys[j], column[j].X, column[j].Y, ElbowOf(column[j], ys[i]), ys[i]))) continue;
                        (ys[i], ys[j]) = (ys[j], ys[i]);
                        swapped = true;
                    }
                if (!swapped) break;
            }
            for (var k = 0; k < column.Count; k++)
            {
                var l = column[k];
                var e = EdgeAt(l, ys[k]);
                var x0 = left ? e - l.Width : e;
                if (hugging && !Fits(l, ys[k])) homeless.Add((l.Id, l.Text));
                // (an anchor straight over or under its own capsule — out past the model, in the column's way: the
                //  bacterium's flagella — is led straight to the capsule's edge facing it, not round the capsule's end)
                if (l.X >= x0 && l.X <= x0 + l.Width && Math.Abs(l.Y - ys[k]) > Height / 2)
                    out_.Add(new BioLabelPlacement(l.Id, l.Text, left, e, ys[k], l.Width, l.X, l.Y, Math.Clamp(l.X, x0 + 9, x0 + l.Width - 9),
                        l.Y < ys[k] ? ys[k] - Height / 2 : ys[k] + Height / 2));
                else out_.Add(new BioLabelPlacement(l.Id, l.Text, left, e, ys[k], l.Width, l.X, l.Y, ElbowOf(l, ys[k])));
            }
        }
        if (model is { } box) IntoBands(out_, box, w, h, keepClear, reach, outline, homeless, clearTop);
        return (out_, more);
    }

    /// <summary>A column's heights beside an outline: its labels kept in their anchors' order and at least
    /// <paramref name="step"/> apart between <paramref name="top"/> and <paramref name="bottom"/>, each where it fits
    /// (<paramref name="fits"/>) and, of the ways that do, the one nearest their anchors' heights in all (worked out two
    /// pixels at a time). A label that fits nowhere is put nearest its anchor all the same: it then lies on the model, and
    /// the 3D view stands its camera further back.</summary>
    private static double[] Spread(IReadOnlyList<BioLabelInput> column, Func<BioLabelInput, double, bool> fits, double top, double bottom, double step)
    {
        var n = column.Count;
        const double pitch = 2;
        var slots = Math.Max(1, (int)Math.Floor((bottom - top) / pitch) + 1);
        var gap = (int)Math.Ceiling(step / pitch - 1e-9);
        // (more than the height holds: as many as it holds are spread, the rest follow on below, as the column did)
        if ((n - 1) * gap >= slots)
        {
            var ys = new double[n];
            for (var i = 0; i < n; i++) ys[i] = top + i * step;
            return ys;
        }
        const double Off = 1e6;
        var cost = new double[n, slots];
        var from = new int[n, slots];
        for (var i = 0; i < n; i++)
        {
            // (the best place for the labels before this one, at or above each height)
            double best = double.PositiveInfinity;
            var bestAt = -1;
            for (var s = 0; s < slots; s++)
            {
                var y = top + s * pitch;
                var here = Math.Abs(y - column[i].Y) + (fits(column[i], y) ? 0 : Off);
                if (i == 0) { cost[i, s] = here; continue; }
                var before = s - gap;
                if (before >= 0 && cost[i - 1, before] < best) (best, bestAt) = (cost[i - 1, before], before);
                cost[i, s] = bestAt < 0 ? double.PositiveInfinity : here + best;
                from[i, s] = bestAt;
            }
        }
        var end = 0;
        for (var s = 1; s < slots; s++) if (cost[n - 1, s] < cost[n - 1, end]) end = s;
        var placed = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            placed[i] = top + end * pitch;
            end = from[i, end];
        }
        return placed;
    }

    /// <summary>Whether two leaders' slanting runs (anchor → elbow) cross.</summary>
    private static bool Crossing(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
    {
        static double Turn(double px, double py, double qx, double qy, double rx, double ry) => (qx - px) * (ry - py) - (qy - py) * (rx - px);
        return Turn(x3, y3, x4, y4, x1, y1) * Turn(x3, y3, x4, y4, x2, y2) < 0 && Turn(x1, y1, x2, y2, x3, y3) * Turn(x1, y1, x2, y2, x4, y4) < 0;
    }

    /// <summary>Whether a label's capsule lies over another label's anchor: that one's leader then seems to end in it (the
    /// bacterium's "Flagella", led into "Fimbriae (pili)").</summary>
    private static bool CoversAnchor(BioLabelPlacement p, IReadOnlyList<BioLabelPlacement> placed)
    {
        var f = p.Frame;
        return placed.Any(o => !ReferenceEquals(o, p) && o.AnchorX >= f.X - 2 && o.AnchorX <= f.X + f.Width + 2 && o.AnchorY >= f.Y - 2 && o.AnchorY <= f.Y + f.Height + 2);
    }

    /// <summary>How many placed labels lie on the model's box — or, given it, its outline — (more than 4 into it each way)
    /// or over another label's anchor: the 3D view stands its camera back until there are none.</summary>
    public static int Conflicts(IReadOnlyList<BioLabelPlacement> placed, BioLabelBox box, BioOutline? outline = null) => Conflicting(placed, box, outline).Count;

    /// <summary>The placed labels that lie on the model, over another's anchor or — beside an outline — whose leader runs
    /// through another's capsule; <paramref name="withOthers"/>: with the labels they lie over or run through, either of
    /// which, folded away, would clear it.</summary>
    private static List<BioLabelPlacement> Conflicting(IReadOnlyList<BioLabelPlacement> placed, BioLabelBox box, BioOutline? outline, bool withOthers = false)
    {
        var bad = new List<BioLabelPlacement>();
        foreach (var p in placed)
        {
            var f = p.Frame;
            var across = Math.Min(f.X + f.Width, box.X1) - Math.Max(f.X, box.X0);
            var down = Math.Min(f.Y + f.Height, box.Y1) - Math.Max(f.Y, box.Y0);
            var on = outline?.Overlaps(f.X, f.Y, f.X + f.Width, f.Y + f.Height, 4) ?? (across > 4 && down > 4);
            var over = placed.Where(o => !ReferenceEquals(o, p) && o.AnchorX >= f.X - 2 && o.AnchorX <= f.X + f.Width + 2 && o.AnchorY >= f.Y - 2 && o.AnchorY <= f.Y + f.Height + 2).ToList();
            // (beside an outline, labels sent up or down to a corner: a leader that runs through another label's capsule
            //  on its way there counts too)
            var through = outline != null ? RunsThrough(p, placed) : new List<BioLabelPlacement>();
            if (!on && over.Count == 0 && through.Count == 0) continue;
            bad.Add(p);
            if (withOthers) bad.AddRange(over.Concat(through));
        }
        return bad.Distinct().ToList();
    }

    /// <summary>The labels whose capsules a label's leader runs through (more than 2 into them).</summary>
    private static List<BioLabelPlacement> RunsThrough(BioLabelPlacement p, IReadOnlyList<BioLabelPlacement> placed)
    {
        var points = new List<(double X, double Y)> { (p.AnchorX, p.AnchorY) };
        points.AddRange(p.Leader);
        var hit = new List<BioLabelPlacement>();
        foreach (var o in placed)
        {
            if (ReferenceEquals(o, p)) continue;
            var f = o.Frame;
            double x0 = f.X + 2, y0 = f.Y + 2, x1 = f.X + f.Width - 2, y1 = f.Y + f.Height - 2;
            for (var i = 0; i + 1 < points.Count; i++)
                if (SegmentMeets(points[i], points[i + 1], x0, y0, x1, y1)) { hit.Add(o); break; }
        }
        return hit;
    }

    /// <summary>Whether a line from a to b meets a rectangle (clipped to it, Liang–Barsky).</summary>
    private static bool SegmentMeets((double X, double Y) a, (double X, double Y) b, double x0, double y0, double x1, double y1)
    {
        double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (pk, qk) in new[] { (-dx, a.X - x0), (dx, x1 - a.X), (-dy, a.Y - y0), (dy, y1 - a.Y) })
        {
            if (Math.Abs(pk) < 1e-12) { if (qk < 0) return false; continue; }
            var t = qk / pk;
            if (pk < 0) t0 = Math.Max(t0, t); else t1 = Math.Min(t1, t);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>Each label that would hide its own anchor or another label's, or reach well into a narrow model, moved to a
    /// row just above the model (an anchor in its upper half) or below it, as near over its anchor as the row allows (and
    /// never over the box kept clear, or the leaders crossing the row to a column).</summary>
    private static void IntoBands(List<BioLabelPlacement> placed, BioLabelBox box, double w, double h, BioLabelBox? keepClear, double reach, BioOutline? outline,
        HashSet<(string, string)>? homeless = null, double clearTop = 0)
    {
        const double step = Height + Gap, space = 6;
        var narrow = box.X1 - box.X0 < 0.7 * w;
        bool Hides(BioLabelPlacement p)
        {
            var f = p.Frame;
            var onAnchor = p.AnchorX >= f.X - 4 && p.AnchorX <= f.X + f.Width + 4 && Math.Abs(p.AnchorY - p.Y) <= Height;
            // (how far it reaches into the model: its box, or across the outline at the label's own height)
            var (m0, m1) = outline == null ? (box.X0, box.X1) : outline.Across(f.Y, f.Y + f.Height) ?? (0, 0);
            var acrossModel = p.Y + Height / 2 >= box.Y0 && p.Y - Height / 2 <= box.Y1
                && Math.Max(0, Math.Min(f.X + f.Width, m1) - Math.Max(f.X, m0)) > Math.Min(reach, 0.5 * f.Width);
            return onAnchor || (narrow && acrossModel) || CoversAnchor(p, placed) || homeless?.Contains((p.Id, p.Text)) == true;
        }
        var moving = placed.Where(Hides).OrderBy(p => p.AnchorX).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
        if (moving.Count == 0) return;
        var mid = (box.Y0 + box.Y1) / 2;
        double top = Math.Max(Inset, clearTop) + Height / 2, bottom = h - Inset - Height / 2;
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
            // (and so does a column's leader where it crosses the row: the membrane's "Hydrophilic head (polar)", put in a
            //  row below the slab, lay over the leaders to the left column's "Cytoplasm side" and "Peripheral protein")
            double r0 = y - Height / 2, r1 = y + Height / 2;
            foreach (var p in placed.Where(p => !moving.Contains(p)))
            {
                double ax = p.AnchorX, ay = p.AnchorY, ex = p.ElbowX, ey = p.EdgeY ?? p.Y;
                if (Math.Max(ay, ey) < r0 || Math.Min(ay, ey) > r1) continue;
                if (Math.Abs(ey - ay) < 1e-9) { list.Add((Math.Min(ax, ex) - space, Math.Max(ax, ex) + space)); continue; }
                double XAt(double yy) => ax + (ex - ax) * (Math.Clamp(yy, Math.Min(ay, ey), Math.Max(ay, ey)) - ay) / (ey - ay);
                list.Add((Math.Min(XAt(r0), XAt(r1)) - space, Math.Max(XAt(r0), XAt(r1)) + space));
            }
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
        // (each label in the first row on its own side that takes it — above for an anchor in the model's upper half —
        //  or, for an anchor in the middle half of the model's height where none on its side does, on the other: a
        //  longer leader across the model rather than the label left on it, as the helix's C-terminus was beside the
        //  H-bond's and the peptide bond's below it; an anchor near the top or bottom is never led right across it)
        foreach (var p in moving)
            if (!IntoRow(p, p.AnchorY < mid) && Math.Abs(p.AnchorY - mid) <= 0.25 * (box.Y1 - box.Y0)) IntoRow(p, p.AnchorY >= mid);
        bool IntoRow(BioLabelPlacement p, bool above)
        {
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
                return true;
            }
            return false;
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
                    above ? yRow + Height / 2 : yRow - Height / 2, Row: true);
        }
    }

    /// <summary>A label's width for its text at 12 semibold in a capsule (10 padding a side): an estimate for layouts made
    /// before the text is measured.</summary>
    public static double EstimatedWidth(string text) => text.Length * 6.9 + 20;
}
