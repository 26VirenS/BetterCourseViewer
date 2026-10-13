import Foundation

// Labels with leader lines (spec §3.5), laid out the same way on both platforms: each visible anchor's label goes in
// the column on its side of the model (left when the anchor is left of the model's centre), the columns R + 24 pt out
// from the model's screen circle and kept inside the view; in a column, labels follow their anchors down and are
// pushed apart to 22 pt; a column too long for the view is pushed up from the bottom. Each leader runs anchor → elbow
// (12 pt short of the column, toward the model) → label. At most 14 at once: the objects covering the most of the
// screen keep theirs, the rest are counted ("+N more"). Screen points, y down.

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
    static let maxLabels = 14

    /// The labels placed round a model whose screen circle is centred at (cx, cy) with radius r, in a view w × h.
    /// Returns the placements and how many visible labels were left out.
    static func layout(_ inputs: [BioLabelInput], cx: Double, cy: Double, r: Double, width w: Double, height h: Double)
        -> (placed: [BioLabelPlacement], more: Int) {
        var shown = inputs.filter { $0.visible }
        var more = 0
        if shown.count > maxLabels {
            let keep = Set(shown.enumerated().sorted { a, b in
                a.element.area != b.element.area ? a.element.area > b.element.area : a.offset < b.offset
            }.prefix(maxLabels).map(\.offset))
            more = shown.count - maxLabels
            shown = shown.enumerated().filter { keep.contains($0.offset) }.map(\.element)
        }
        let step = height + gap
        var out: [BioLabelPlacement] = []
        for left in [true, false] {
            let column = shown.filter { ($0.x < cx) == left }.sorted { $0.y != $1.y ? $0.y < $1.y : $0.id < $1.id }
            guard !column.isEmpty else { continue }
            let widest = column.map(\.width).max() ?? 0
            // (the column's edge toward the model, kept so its widest label stays inside the view)
            var edge = left ? cx - (r + columnOffset) : cx + (r + columnOffset)
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
