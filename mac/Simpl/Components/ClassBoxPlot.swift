import SwiftUI

/// (1.3.10) How the class did, as the web draws it (course-detail.js classPlot): a box plot on a track from nothing to
/// the points possible — the whiskers from the low to the high, the box from the lower to the upper quartile, a line at
/// the median, a ring at the mean — and your own score as a dot in the course's colour. The pointer over it reads the
/// mark nearest to it (its name and value in a small label over it, the mark itself a little larger).
struct ClassBoxPlot: View {
    let stats: ClassStats
    let tint: Color
    @State private var lit: Int?

    private struct Mark {
        let name: String
        let value: Double
        let key: String
    }

    /// Out of what: the points possible, or the largest number there is.
    private var outOf: Double {
        if let p = stats.possible, p > 0 { return p }
        return Swift.max(stats.max ?? 0, stats.mine ?? 0, 1)
    }

    /// Every mark there is, your own first so that one at the same place as another is read as yours.
    private var marks: [Mark] {
        [("You", stats.mine, "me"), ("Low", stats.min ?? 0, "lo"), ("Lower quartile", stats.lowerQ, "q1"), ("Median", stats.median, "md"),
         ("Upper quartile", stats.upperQ, "q3"), ("High", stats.max, "hi"), ("Mean", stats.mean, "mean")]
            .compactMap { name, v, key in v.map { Mark(name: name, value: $0, key: key) } }
    }

    var body: some View {
        HStack(spacing: 10) {
            Text("Class")
                .font(.sCaption.weight(.medium))
                .foregroundStyle(.secondary)
            GeometryReader { g in
                let w = g.size.width
                let x = { (v: Double) -> CGFloat in CGFloat(Swift.min(1, Swift.max(0, v / outOf))) * w }
                let litKey = lit.map { marks[$0].key }
                ZStack(alignment: .leading) {
                    plot(x: x, width: w, litKey: litKey)
                    if let i = lit {
                        tip(marks[i])
                            .position(x: Swift.min(Swift.max(x(marks[i].value), 34), w - 34), y: -12)
                            .transition(.opacity)
                    }
                }
                .frame(width: w, height: 24)
                .contentShape(Rectangle())
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let p):
                        let near = marks.indices.min { abs(x(marks[$0].value) - p.x) < abs(x(marks[$1].value) - p.x) }
                        if near != lit { withAnimation(Motion.snappy) { lit = near } }
                    case .ended:
                        withAnimation(Motion.snappy) { lit = nil }
                    }
                }
            }
            .frame(height: 24)
        }
        .padding(.top, 6)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("How the class did, out of \(Self.num(outOf)): " + marks.map { "\($0.name) \(Self.num($0.value))" }.joined(separator: ", "))
    }

    @ViewBuilder
    private func plot(x: (Double) -> CGFloat, width w: CGFloat, litKey: String?) -> some View {
        let ink = Color.secondary
        let dim = { (key: String) -> Double in litKey == nil || litKey == key || (key == "box" && (litKey == "q1" || litKey == "q3")) ? 1 : 0.45 }
        let grow = { (key: String) -> CGFloat in litKey == key ? 1.35 : 1 }
        let lo = stats.min ?? 0
        let hi = stats.max ?? lo
        // the track, nothing to the points possible
        Capsule().fill(Color.primary.opacity(0.08)).frame(width: w, height: 2)
        // the whiskers, low to high, with their caps
        Rectangle().fill(ink.opacity(0.55)).frame(width: Swift.max(1, x(hi) - x(lo)), height: 2).offset(x: x(lo))
        cap.scaleEffect(grow("lo")).offset(x: x(lo) - 1).opacity(dim("lo") * 0.8)
        cap.scaleEffect(grow("hi")).offset(x: x(hi) - 1).opacity(dim("hi") * 0.8)
        // the box, lower to upper quartile
        if let q1 = stats.lowerQ, let q3 = stats.upperQ {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(Color.primary.opacity(0.08))
                .overlay(RoundedRectangle(cornerRadius: 4, style: .continuous).strokeBorder(litKey == "q1" || litKey == "q3" ? Color.primary.opacity(0.7) : ink.opacity(0.7), lineWidth: 1.5))
                .frame(width: Swift.max(3, x(q3) - x(q1)), height: 14)
                .offset(x: x(q1))
                .opacity(dim("box"))
        }
        if let md = stats.median {
            RoundedRectangle(cornerRadius: 1).fill(Color.primary.opacity(0.75)).frame(width: 2, height: 14)
                .scaleEffect(grow("md")).offset(x: x(md) - 1).opacity(dim("md"))
        }
        if let mean = stats.mean {
            Circle().strokeBorder(Color.primary.opacity(0.75), lineWidth: 1.5).background(Circle().fill(Color(nsColor: .windowBackgroundColor)))
                .frame(width: 7, height: 7)
                .scaleEffect(grow("mean")).offset(x: x(mean) - 3.5).opacity(dim("mean"))
        }
        if let me = stats.mine {
            Circle().fill(tint)
                .overlay(Circle().strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: 2))
                .frame(width: 12, height: 12)
                .scaleEffect(grow("me")).offset(x: x(me) - 6)
        }
    }

    private var cap: some View {
        RoundedRectangle(cornerRadius: 1).fill(Color.secondary).frame(width: 2, height: 10)
    }

    private func tip(_ m: Mark) -> some View {
        // (1.3.11) solid colours of its own, never the system's: over a card's glass those came out grey on grey
        HStack(alignment: .firstTextBaseline, spacing: 5) {
            Text(m.name).font(.sCaption.weight(.medium)).foregroundStyle(Self.tipInk.opacity(0.8))
            Text(Self.num(m.value)).font(.sCallout.weight(.bold).monospacedDigit()).foregroundStyle(Self.tipInk)
        }
        .padding(.horizontal, 9)
        .padding(.vertical, 5)
        .background(Self.tipGround, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .shadow(color: .black.opacity(0.25), radius: 6, y: 2)
        .compositingGroup()
        .fixedSize()
        .allowsHitTesting(false)
    }

    /// The label's ground and words: near-black with white words by day, near-white with black ones by night.
    private static let tipGround = Theme.dynamic(light: NSColor(white: 0.12, alpha: 1), dark: NSColor(white: 0.94, alpha: 1))
    private static let tipInk = Theme.dynamic(light: NSColor.white, dark: NSColor(white: 0.06, alpha: 1))

    private static func num(_ v: Double) -> String {
        if !v.isFinite { return "—" }
        if v == v.rounded() && abs(v) < 1e15 { return String(Int(v)) }
        return String(format: "%g", (v * 10).rounded() / 10)
    }
}
