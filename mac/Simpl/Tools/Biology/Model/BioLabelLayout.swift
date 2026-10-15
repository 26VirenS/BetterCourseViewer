import Foundation

// Labels with leader lines (spec §3.5), laid out the same way on both platforms: each visible anchor's label goes in
// the column on its side of the model (left when the anchor is left of the model's centre) — (1.3.79) the columns kept
// within two labels of each other, the labels nearest the model's middle crossing over — the columns 24 pt out from the
// model (its outline on the screen where the view knows it, else its screen circle) — (1.3.80) each label out from the
// outline across its own row, and kept inside the view on its own; in a
// column, labels follow their anchors down and are pushed apart to 22 pt; a column too long for the view is pushed up
// from the bottom. Each leader runs anchor → elbow (12 pt short of the column, toward the model) → label. As many as
// the two columns hold (24 at most): the objects covering the most of the screen keep theirs, the rest are counted
// ("+N more") — before 1.3.79, 14 at most, and a cell's fifteenth part was "+1 more" with room to spare. Screen points,
// y down.

/// A label to place: its object, its words, where its anchor projects and how much of the screen its object covers.
struct BioLabelInput: Equatable {
    let id: String
    let text: String
    /// The anchor in view points (y down).
    let x: Double
    let y: Double
    /// False: behind the camera, or hidden by another object.
    var visible: Bool = true
    /// The object's screen area (or any measure of size): the larger keep their labels when there are too many.
    var area: Double = 0
    /// The label's width in points (its text at 12 pt semibold plus its capsule's padding).
    var width: Double = 80
}

/// A placed label: its column, its centre line, its capsule's edge nearest the model, and its leader.
struct BioLabelPlacement: Equatable {
    let id: String
    let text: String
    /// In the left column (its capsule ends at `edgeX`) or the right (it starts there).
    let left: Bool
    /// The capsule's edge nearest the model.
    let edgeX: Double
    /// The capsule's vertical centre.
    let y: Double
    let width: Double
    /// The leader: anchor → elbow → (edgeX, y).
    let anchorX: Double
    let anchorY: Double
    let elbowX: Double

    /// The capsule's frame (x, y, width, height), y down.
    var frame: (x: Double, y: Double, width: Double, height: Double) {
        (left ? edgeX - width : edgeX, y - BioLabelLayout.height / 2, width, BioLabelLayout.height)
    }
}

enum BioLabelLayout {
    static let height = 18.0
    static let gap = 4.0
    static let columnOffset = 24.0
    static let elbow = 12.0
    static let inset = 16.0
    static let maxLabels = 24

    /// How many labels one column holds in a view `h` tall (each 18 pt, 4 apart, 16 pt clear of the top and bottom).
    static func columnCapacity(_ h: Double) -> Int { max(1, Int(((h - 2 * inset - height) / (height + gap)).rounded(.down)) + 1) }

    /// The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h —
    /// (1.3.79) the columns out from `extent` (the model's leftmost and rightmost points on the screen) when it is
    /// given, so they sit beside a thin model rather than out at its circle; (1.3.80) and, given the model's `outline`
    /// on the screen, each label 24 pt out from the outline's edge across its own row (its convex hull's), so the labels
    /// beside a round cell's narrower top and bottom come in toward it, leaving its widest middle the most room. Each
    /// label is kept inside the view on its own: a wide one moves in alone, no longer its whole column with it (with a
    /// model filling most of a narrow view, a column moved in for its widest label lay over the model, its short labels
    /// too). Returns the placements and how many visible labels were left out.
    static func layout(_ inputs: [BioLabelInput], cx: Double, cy: Double, r: Double, width w: Double, height h: Double,
                       extent: (left: Double, right: Double)? = nil,
                       outline: [(x: Double, y: Double)] = []) -> (placed: [BioLabelPlacement], more: Int) {
        var shown = inputs.filter { $0.visible }
        var more = 0
        let capacity = columnCapacity(h)
        let most = min(maxLabels, 2 * capacity)
        if shown.count > most {
            let keep = Set(shown.enumerated().sorted { a, b in
                a.element.area != b.element.area ? a.element.area > b.element.area : a.offset < b.offset
            }.prefix(most).map(\.offset))
            more = shown.count - most
            shown = shown.enumerated().filter { keep.contains($0.offset) }.map(\.element)
        }
        // (each label's side: its anchor's, then the columns evened out — while one has more than two more than the
        // other, or more than it holds, its label nearest the model's middle line crosses over)
        var onLeft = shown.map { $0.x < cx }
        for _ in 0..<shown.count {
            let l = onLeft.filter { $0 }.count, rt = shown.count - l
            let fuller = l > rt
            let larger = max(l, rt), smaller = min(l, rt)
            guard (larger - smaller > 2 || larger > capacity) && smaller < capacity else { break }
            guard let k = shown.indices.filter({ onLeft[$0] == fuller })
                .min(by: { abs(shown[$0].x - cx) < abs(shown[$1].x - cx) }) else { break }
            onLeft[k] = !fuller
        }
        let step = height + gap
        let shape = hull(outline)
        var out: [BioLabelPlacement] = []
        for left in [true, false] {
            let column = shown.indices.filter { onLeft[$0] == left }.map { shown[$0] }
                .sorted { $0.y != $1.y ? $0.y < $1.y : $0.id < $1.id }
            guard !column.isEmpty else { continue }
            // (the column's edge toward the model)
            let edge: Double
            if let extent {
                edge = left ? min(extent.left, cx) - columnOffset : max(extent.right, cx) + columnOffset
            } else {
                edge = left ? cx - (r + columnOffset) : cx + (r + columnOffset)
            }
            let top = inset + height / 2, bottom = h - inset - height / 2
            var ys = column.map { max($0.y, top) }
            for i in 1..<ys.count { ys[i] = max(ys[i], ys[i - 1] + step) }
            if let last = ys.last, last > bottom {
                ys[ys.count - 1] = bottom
                for i in stride(from: ys.count - 2, through: 0, by: -1) { ys[i] = min(ys[i], ys[i + 1] - step) }
                // (and down again from the top, if pushing up took the first above it: they then share the room)
                if let first = ys.first, first < top {
                    ys[0] = top
                    for i in 1..<ys.count { ys[i] = max(ys[i], ys[i - 1] + step) }
                }
            }
            for (k, l) in column.enumerated() {
                // (out from the outline across the label's row, where the outline is known; then inside the view)
                var e = edge
                if let row = across(shape, from: ys[k] - height / 2 - gap, to: ys[k] + height / 2 + gap) {
                    e = left ? min(row.left, cx) - columnOffset : max(row.right, cx) + columnOffset
                }
                e = left ? max(e, inset + l.width) : min(e, w - inset - l.width)
                out.append(BioLabelPlacement(id: l.id, text: l.text, left: left, edgeX: e, y: ys[k], width: l.width,
                                             anchorX: l.x, anchorY: l.y, elbowX: left ? e + elbow : e - elbow))
            }
        }
        return (out, more)
    }

    /// (1.3.80) The convex hull of points on the view, in order round it (Andrew's monotone chain); fewer than three
    /// points, none.
    static func hull(_ points: [(x: Double, y: Double)]) -> [(x: Double, y: Double)] {
        guard points.count >= 3 else { return [] }
        let p = points.sorted { $0.x != $1.x ? $0.x < $1.x : $0.y < $1.y }
        func turn(_ o: (x: Double, y: Double), _ a: (x: Double, y: Double), _ b: (x: Double, y: Double)) -> Double {
            (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x)
        }
        var lower: [(x: Double, y: Double)] = [], upper: [(x: Double, y: Double)] = []
        for q in p {
            while lower.count >= 2 && turn(lower[lower.count - 2], lower[lower.count - 1], q) <= 0 { lower.removeLast() }
            lower.append(q)
        }
        for q in p.reversed() {
            while upper.count >= 2 && turn(upper[upper.count - 2], upper[upper.count - 1], q) <= 0 { upper.removeLast() }
            upper.append(q)
        }
        let round = Array(lower.dropLast()) + Array(upper.dropLast())
        return round.count >= 3 ? round : []
    }

    /// (1.3.80) How far a convex hull reaches across — its leftmost and rightmost x — within the rows y0…y1, or, rows
    /// above or below it, at its top or its bottom (nil: no hull).
    static func across(_ hull: [(x: Double, y: Double)], from y0: Double, to y1: Double) -> (left: Double, right: Double)? {
        guard hull.count >= 3, let lo = hull.map({ $0.y }).min(), let hi = hull.map({ $0.y }).max() else { return nil }
        var a = max(y0, lo), b = min(y1, hi)
        if a > b {
            a = y1 < lo ? lo : hi
            b = a
        }
        var xs: [Double] = []
        for i in hull.indices {
            let p = hull[i], q = hull[(i + 1) % hull.count]
            if p.y >= a && p.y <= b { xs.append(p.x) }
            // (where the edge crosses the rows' top and bottom)
            for y in [a, b] where (p.y - y) * (q.y - y) < 0 {
                xs.append(p.x + (y - p.y) / (q.y - p.y) * (q.x - p.x))
            }
        }
        guard let l = xs.min(), let r = xs.max() else { return nil }
        return (l, r)
    }

    /// A label's width for its text at 12 pt semibold in a capsule (10 pt padding a side): an estimate for layouts made
    /// before the text is measured.
    static func estimatedWidth(_ text: String) -> Double { Double(text.count) * 6.9 + 20 }
}
