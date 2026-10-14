import Foundation

// Labels with leader lines (spec §3.5), laid out the same way on both platforms: each visible anchor's label goes in
// the column on its side of the model (left when the anchor is left of the model's centre) — (1.3.79) the columns kept
// within two labels of each other, the labels nearest the model's middle crossing over — the columns 24 pt out from the
// model (its outline on the screen where the view knows it, else its screen circle) and kept inside the view; in a
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
    /// given, so they sit beside a thin model rather than out at its circle. Returns the placements and how many
    /// visible labels were left out.
    static func layout(_ inputs: [BioLabelInput], cx: Double, cy: Double, r: Double, width w: Double, height h: Double,
                       extent: (left: Double, right: Double)? = nil) -> (placed: [BioLabelPlacement], more: Int) {
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
        var out: [BioLabelPlacement] = []
        for left in [true, false] {
            let column = shown.indices.filter { onLeft[$0] == left }.map { shown[$0] }
                .sorted { $0.y != $1.y ? $0.y < $1.y : $0.id < $1.id }
            guard !column.isEmpty else { continue }
            let widest = column.map(\.width).max() ?? 0
            // (the column's edge toward the model, kept so its widest label stays inside the view)
            var edge: Double
            if let extent {
                edge = left ? min(extent.left, cx) - columnOffset : max(extent.right, cx) + columnOffset
            } else {
                edge = left ? cx - (r + columnOffset) : cx + (r + columnOffset)
            }
            edge = left ? max(edge, inset + widest) : min(edge, w - inset - widest)
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
                out.append(BioLabelPlacement(id: l.id, text: l.text, left: left, edgeX: edge, y: ys[k], width: l.width,
                                             anchorX: l.x, anchorY: l.y, elbowX: left ? edge + elbow : edge - elbow))
            }
        }
        return (out, more)
    }

    /// A label's width for its text at 12 pt semibold in a capsule (10 pt padding a side): an estimate for layouts made
    /// before the text is measured.
    static func estimatedWidth(_ text: String) -> Double { Double(text.count) * 6.9 + 20 }
}
