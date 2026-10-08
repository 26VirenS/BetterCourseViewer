import SwiftUI

// The rubric as a ring (1.2): the web's rubric-ring.js on the Mac. One slice per criterion, as long as its share of the
// points; marked and posted, each slice turns to its grade's colour (green near full marks, yellow to orange for part
// marks, red where most was lost) and bends out where the work did well and in where it lost points, its colour,
// thickness and bend carried smoothly into its neighbours'. The total in the middle; a slice, its label or the keys open
// a criterion beside the ring (RubricPanels.swift).

private let tau = 2 * Double.pi

private func clamp01(_ v: Double) -> Double { min(1, max(0, v)) }
/// Smoothstep: an S from 0 to 1.
private func smooth(_ v: Double) -> Double { v * v * (3 - 2 * v) }

// MARK: - Colour

/// A colour as the ring mixes it: three channels from 0 to 255.
struct RingRGB: Hashable {
    var r: Double
    var g: Double
    var b: Double

    /// A criterion not marked yet, in a marked ring: grey, not a grade.
    static let unmarked = RingRGB(r: 142, g: 142, b: 150)
    static let black = RingRGB(r: 0, g: 0, b: 0)
    static let white = RingRGB(r: 255, g: 255, b: 255)

    init(r: Double, g: Double, b: Double) {
        self.r = r
        self.g = g
        self.b = b
    }

    init(hex: String) {
        var s = hex
        if s.hasPrefix("#") { s.removeFirst() }
        let v = UInt32(s, radix: 16) ?? 0x8e8e93
        self.init(r: Double((v >> 16) & 0xff), g: Double((v >> 8) & 0xff), b: Double(v & 0xff))
    }

    func mix(_ other: RingRGB, _ t: Double) -> RingRGB {
        RingRGB(r: r + (other.r - r) * t, g: g + (other.g - g) * t, b: b + (other.b - b) * t)
    }

    var color: Color { Color(red: r / 255, green: g / 255, blue: b / 255) }

    /// The words on this colour: dark on a light one (green, orange), white on the rest.
    var ink: Color {
        func lin(_ v: Double) -> Double {
            let c = v / 255
            return c <= 0.03928 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)
        }
        let luma = 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
        return luma > 0.3 ? Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255) : .white
    }

    static func hsl(_ hue: Double, _ s: Double, _ l: Double) -> RingRGB {
        let a = s * min(l, 1 - l)
        func f(_ q: Double) -> Double {
            let k = (q + hue / 30).truncatingRemainder(dividingBy: 12)
            return 255 * (l - a * max(-1, min(k - 3, min(9 - k, 1))))
        }
        return RingRGB(r: f(0), g: f(8), b: f(4))
    }

    /// A criterion's mark as a colour: green at full marks or near them (90 % and up), yellow to orange for part marks
    /// (80 % down to half), red only where most was lost (30 % or less) — a smooth run of hue, so the ring's colours
    /// still flow from one slice into the next. The yellows a little lighter, so they read as yellow.
    static func grade(_ fraction: Double) -> RingRGB {
        let x = clamp01(fraction)
        let hue: Double
        if x >= 0.9 {
            hue = 140
        } else if x >= 0.8 {
            hue = 48 + 92 * smooth((x - 0.8) / 0.1)
        } else if x >= 0.5 {
            hue = 30 + 18 * smooth((x - 0.5) / 0.3)
        } else if x >= 0.3 {
            hue = 2 + 28 * smooth((x - 0.3) / 0.2)
        } else {
            hue = 2
        }
        return hsl(hue, 0.8, 0.52 + 0.04 * smooth(clamp01(1 - abs(hue - 48) / 40)))
    }
}

// MARK: - The rubric as the ring reads it

/// Where an angle falls between two criteria's middles: this one, the next, and how far across on an S-curve. A value
/// given at each middle (the bend, the thickness, the colour) is carried round the circle through it.
struct RingBlend {
    let from: Int
    let to: Int
    let t: Double
}

/// A rubric's criteria with their levels and marks, and the ring's shape of them: where each slice starts and ends, how
/// thick it is, how far it bends, its colours.
struct RubricModel {
    struct Level: Hashable {
        let label: String
        let text: String
        let pts: Double?
    }

    struct Criterion: Identifiable, Hashable {
        let id: String
        let index: Int
        let name: String
        /// A short name for the ring: the whole name when it is short, else its first words.
        let short: String
        let desc: String
        /// Its levels, the best first (one without points last).
        let levels: [Level]
        let worth: Double
        let score: Double?
        /// The level given (an index into `levels`), or -1.
        let mark: Int
        let comment: String
        /// How far up its bar its mark sits (1 with nothing marked).
        let frac: Double
    }

    let criteria: [Criterion]
    /// Marked and posted: at least one criterion has a score.
    let graded: Bool
    /// What the rubric is worth, and what it gave.
    let possible: Double
    let earned: Double
    /// Each slice's own colour, and its grade's (its own when nothing is marked).
    let hues: [RingRGB]
    let grades: [RingRGB]
    /// Each slice's start, end and middle, in radians clockwise from twelve o'clock.
    let starts: [Double]
    let ends: [Double]
    let mids: [Double]
    /// How far each slice bends out (+) or in (−), and how thick it is, in the web ring's units (its radius 176).
    let bend: [Double]
    let thick: [Double]

    var n: Int { criteria.count }
    var colors: [RingRGB] { graded ? grades : hues }

    init(rows: [RubricRow]) {
        let list = rows.enumerated().map { RubricModel.criterion($0.element, $0.offset) }
        criteria = list
        let count = list.count
        let isGraded = list.contains { $0.score != nil }
        graded = isGraded
        let worthSum = list.reduce(0.0) { $0 + max(0, $1.worth) }
        possible = worthSum
        earned = list.reduce(0.0) { $0 + ($1.score ?? 0) }
        let palette = ["#5e5ce6", "#0a84ff", "#30b0c7", "#30d158", "#ff9f0a", "#ff375f"]
        let own: [RingRGB] = (0..<count).map { k -> RingRGB in
            if count <= palette.count { return RingRGB(hex: palette[k]) }
            return RingRGB.hsl((248 + Double(k) * 360 / Double(count)).truncatingRemainder(dividingBy: 360), 0.78, 0.56)
        }
        hues = own
        // marked and posted, each slice's colour is its grade's (one not marked yet: grey)
        let marked: [RingRGB] = list.map { c -> RingRGB in
            guard let s = c.score else { return RingRGB.unmarked }
            return RingRGB.grade(c.worth > 0 ? s / c.worth : 1)
        }
        grades = isGraded ? marked : own
        // each slice's share of the circle: its points, with a sliver for a criterion worth nothing
        let weights = list.map { max(0, $0.worth) }
        let total = weights.reduce(0, +)
        let shares = total > 0 ? weights.map { max($0, total * 0.03) } : weights.map { _ in 1.0 }
        let sum = max(1e-9, shares.reduce(0, +))
        var acc = 0.0
        var s0: [Double] = []
        var s1: [Double] = []
        for sh in shares {
            s0.append(acc / sum * tau)
            acc += sh
            s1.append(acc / sum * tau)
        }
        starts = s0
        ends = s1
        mids = zip(s0, s1).map { ($0 + $1) / 2 }
        // a criterion marked low bends in, one marked high bends out — gently (the colour says the rest), and damped
        // as the count rises
        bend = list.map { c -> Double in
            guard isGraded, let s = c.score, c.worth > 0 else { return 0 }
            let fr = s / c.worth
            let amp = min(1, max(0.35, (c.worth / max(1e-9, worthSum)) * Double(count) / 3))
            return max(-18, min(12, 60 * (fr - 0.85))) * min(1, 6 / Double(count) + 0.25) * amp
        }
        // a slice is thicker the more of the rubric it carries — growing inward, its outer edge on the circle
        let avg = sum / Double(max(1, count))
        thick = shares.map { max(5, min(28, 10 * pow($0 / max(1e-9, avg), 1.5))) }
    }

    // MARK: Reading a criterion

    private static func criterion(_ row: RubricRow, _ k: Int) -> Criterion {
        // its levels, the best first (one without points last), each remembering whether it was the one given
        let raw = row.ratings.enumerated().map { (i: $0.offset, got: $0.element.got == true, level: level($0.element)) }
        let sorted = raw.sorted { a, b in
            let x = a.level.pts ?? -Double.infinity
            let y = b.level.pts ?? -Double.infinity
            return x != y ? x > y : a.i < b.i
        }
        let levels = sorted.map { $0.level }
        let words = pair(row.pts)
        let worth = row.worth ?? words.of ?? levels.first?.pts ?? 0
        var score: Double? = row.worth != nil ? row.score : words.got // (the numbers when sent; else read from the words)
        var mark = sorted.firstIndex { $0.got } ?? -1
        if mark < 0, let s = score { // (no level named: the one nearest the points given)
            var best = Double.infinity
            for (i, l) in levels.enumerated() {
                if let p = l.pts, abs(p - s) < best {
                    best = abs(p - s)
                    mark = i
                }
            }
        }
        if score == nil, mark >= 0 { score = levels[mark].pts }
        if score == nil { mark = -1 }
        let name = row.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let full = name.isEmpty ? "Criterion \(k + 1)" : name
        let comment = (row.note ?? row.comment ?? "").trimmingCharacters(in: CharacterSet(charactersIn: "“”\" \n"))
        return Criterion(id: row.id, index: k, name: full, short: short(full), desc: plainLines(row.desc ?? ""),
                         levels: levels, worth: worth, score: score, mark: mark, comment: comment,
                         frac: fraction(levels, worth: worth, score: score, mark: mark))
    }

    private static func level(_ r: RubricRating) -> Level {
        let label = r.text.trimmingCharacters(in: .whitespacesAndNewlines)
        return Level(label: label.isEmpty ? "Rating" : label, text: (r.long ?? "").trimmingCharacters(in: .whitespacesAndNewlines),
                     pts: r.value ?? number(in: r.pts))
    }

    /// Where each level sits on its criterion's bar: 1 at the top (the best), 0 at the foot — by points, or evenly in
    /// their order when there are no points to place them by.
    static func positions(_ levels: [Level]) -> [Double] {
        let vals = levels.compactMap(\.pts)
        let count = levels.count
        let even: (Int) -> Double = { i in count <= 1 ? 1 : 1 - Double(i) / Double(count - 1) }
        guard let top = vals.max(), let lo = vals.min(), top - lo > 1e-9 else {
            return levels.indices.map(even)
        }
        return levels.enumerated().map { i, l in
            guard let p = l.pts else { return even(i) }
            return (p - lo) / (top - lo)
        }
    }

    /// How far up its bar a criterion's mark sits (the stretch above it was not earned).
    private static func fraction(_ levels: [Level], worth: Double, score: Double?, mark: Int) -> Double {
        guard let score else { return 1 }
        if levels.isEmpty { return clamp01(score / max(worth, score, 1e-9)) } // (marked freely: from nothing to what it is worth)
        let vals = levels.compactMap(\.pts)
        guard let top = vals.max(), let lo = vals.min(), top - lo > 1e-9 else {
            return mark >= 0 && levels.count > 1 ? 1 - Double(mark) / Double(levels.count - 1) : 1
        }
        return clamp01((score - lo) / (top - lo))
    }

    /// The first number in a few words ("3 pts" → 3).
    private static func number(in s: String?) -> Double? {
        guard let s else { return nil }
        let kept = s.filter { $0.isASCII && ($0.isNumber || $0 == "." || $0 == "-") }
        return Double(kept)
    }

    /// "4 / 6" → given 4 of 6; "6 pts" → worth 6.
    private static func pair(_ s: String?) -> (got: Double?, of: Double?) {
        guard let s, !s.isEmpty else { return (nil, nil) }
        let parts = s.split(separator: "/")
        if parts.count == 2 { return (number(in: String(parts[0])), number(in: String(parts[1]))) }
        return (nil, number(in: s))
    }

    /// A long description's lines as one: bullets off, joined by middots.
    private static func plainLines(_ s: String) -> String {
        s.components(separatedBy: .newlines)
            .map { line -> String in
                var l = Substring(line)
                while let c = l.first, c == " " || c == "\t" || c == "•" || c == "-" || c == "*" || c == "·" { l.removeFirst() }
                return l.trimmingCharacters(in: .whitespaces)
            }
            .filter { !$0.isEmpty }
            .joined(separator: " · ")
    }

    /// A short name for the ring: the whole name when it is short, else its first words ("Figures and tables":
    /// Figures, not "Figures and").
    static func short(_ name: String) -> String {
        if name.count <= 16 { return name }
        var out = ""
        for w in name.split(whereSeparator: \.isWhitespace) {
            let next = out.isEmpty ? String(w) : out + " " + String(w)
            if next.count > 14 { break }
            out = next
        }
        while let last = out.last, " /&,:;–—-".contains(last) { out.removeLast() }
        let small: Set<String> = ["and", "or", "of", "the", "to", "for", "with", "in", "on", "a", "an", "&"]
        if let space = out.range(of: " ", options: .backwards), small.contains(out[space.upperBound...].lowercased()) {
            out = String(out[..<space.lowerBound])
        }
        return out.isEmpty ? String(name.prefix(13)) + "…" : out
    }

    // MARK: Words

    static func num(_ v: Double) -> String {
        if !v.isFinite { return "—" }
        if v == v.rounded() && abs(v) < 1e15 { return String(Int(v)) }
        return String(format: "%g", (v * 100).rounded() / 100)
    }

    /// "4 / 6", "6 pts · not marked", "6 pts".
    func pointsLabel(_ c: Criterion) -> String {
        if graded {
            guard let s = c.score else { return "\(RubricModel.num(c.worth)) pts · not marked" }
            return "\(RubricModel.num(s)) / \(RubricModel.num(c.worth))"
        }
        return "\(RubricModel.num(c.worth)) \(c.worth == 1 ? "pt" : "pts")"
    }

    /// The criterion as a screen reader says it.
    func spoken(_ c: Criterion) -> String {
        if graded {
            guard let s = c.score else { return "\(c.name), not marked yet, worth \(RubricModel.num(c.worth))" }
            let level = c.mark >= 0 ? ", \(c.levels[c.mark].label)" : ""
            return "\(c.name), \(RubricModel.num(s)) of \(RubricModel.num(c.worth))\(level)"
        }
        return "\(c.name), worth \(RubricModel.num(c.worth)) \(c.worth == 1 ? "point" : "points")"
    }

    /// The whole rubric in a few words: "8 / 10 · 80%", or "10 pts".
    var summary: String {
        if graded {
            let pct = possible > 0 ? Int((earned / possible * 100).rounded()) : 0
            return "\(RubricModel.num(earned)) / \(RubricModel.num(possible)) · \(pct)%"
        }
        return "\(RubricModel.num(possible)) \(possible == 1 ? "pt" : "pts")"
    }

    // MARK: The ring's shape

    func between(_ th: Double) -> RingBlend {
        if n <= 1 { return RingBlend(from: 0, to: 0, t: 0) }
        var x = th - mids[0]
        x -= (x / tau).rounded(.down) * tau
        var k = n - 1
        while k > 0 && mids[k] - mids[0] > x { k -= 1 }
        let lo = mids[k] - mids[0]
        let hi = k + 1 < n ? mids[k + 1] - mids[0] : tau
        return RingBlend(from: k, to: (k + 1) % n, t: (1 - cos(Double.pi * (x - lo) / max(1e-9, hi - lo))) / 2)
    }

    func blend(_ values: [Double], _ b: RingBlend) -> Double {
        values[b.from] + (values[b.to] - values[b.from]) * b.t
    }

    func colour(_ cols: [RingRGB], at th: Double) -> RingRGB {
        let b = between(th)
        return cols[b.from].mix(cols[b.to], b.t)
    }

    /// The run of colour round the whole ring, for an angular gradient from twelve o'clock.
    func gradient(_ cols: [RingRGB]) -> Gradient {
        guard n > 0 else { return Gradient(colors: [.gray, .gray]) }
        let steps = 120
        return Gradient(stops: (0...steps).map { i in
            let t = Double(i) / Double(steps)
            return Gradient.Stop(color: colour(cols, at: t * tau).color, location: t)
        })
    }

    /// The slice an angle falls in.
    func slice(at th: Double) -> Int {
        var a = th.truncatingRemainder(dividingBy: tau)
        if a < 0 { a += tau }
        return ends.firstIndex { a < $0 } ?? max(0, n - 1)
    }

    /// A point along slice `k` (`v` from 0 to 1, a hair past): its angle, the radius of the band's middle, its width.
    /// `swell` pushes it out under the pointer; `grow` is how far it has bent to its marks.
    func sample(_ k: Int, _ v: Double, radius: Double, swell: Double, grow: Double) -> (theta: Double, rad: Double, width: Double) {
        let z = radius / 176
        let th = starts[k] + (ends[k] - starts[k]) * v
        let b = between(th)
        let s1 = sin(Double.pi * clamp01(v))
        let sw = swell * s1 * s1
        let width = blend(thick, b) * z
        let go = blend(bend, b) * z * grow
        let rad = radius + go - (width - 10 * z) / 2 + 9 * z * sw // (thicker inward: the outer edge stays on the circle)
        return (th, rad, width + 2.5 * z * sw)
    }
}

// MARK: - Drawing

/// One criterion's slice of the ring, drawn as one smooth band: its outline sampled along it, so its bend and
/// thickness run on into its neighbours' with no step where two meet.
struct RingSlice: Shape {
    let model: RubricModel
    let k: Int
    let radius: CGFloat
    var swell: Double
    var grow: Double

    var animatableData: AnimatablePair<Double, Double> {
        get { AnimatablePair(swell, grow) }
        set {
            swell = newValue.first
            grow = newValue.second
        }
    }

    func path(in rect: CGRect) -> Path {
        var p = Path()
        guard k < model.n else { return p }
        let cx = Double(rect.midX)
        let cy = Double(rect.midY)
        let r = Double(radius)
        let len = max(1, r * (model.ends[k] - model.starts[k]))
        let steps = max(8, min(260, Int(len / 2.5)))
        let over = model.n > 1 ? 0.6 / len : 0 // (each slice runs on a hair under the next: no seam shows)
        var outer: [CGPoint] = []
        var inner: [CGPoint] = []
        outer.reserveCapacity(steps + 1)
        inner.reserveCapacity(steps + 1)
        for j in 0...steps {
            let v = Double(j) / Double(steps) * (1 + over)
            let s = model.sample(k, v, radius: r, swell: swell, grow: grow)
            let sn = sin(s.theta)
            let cs = cos(s.theta)
            let ro = s.rad + s.width / 2
            let ri = s.rad - s.width / 2
            outer.append(CGPoint(x: cx + ro * sn, y: cy - ro * cs))
            inner.append(CGPoint(x: cx + ri * sn, y: cy - ri * cs))
        }
        p.move(to: outer[0])
        for q in outer.dropFirst() { p.addLine(to: q) }
        for q in inner.reversed() { p.addLine(to: q) }
        p.closeSubpath()
        return p
    }
}

/// How large the ring is drawn: the stage it stands on (room round it for the labels) and its radius.
struct RingStage: Equatable {
    let width: CGFloat
    let height: CGFloat
    let radius: CGFloat

    static let large = RingStage(width: 560, height: 450, radius: 136)
    static let medium = RingStage(width: 480, height: 390, radius: 112)
    static let small = RingStage(width: 380, height: 320, radius: 88)

    /// The web ring's units in this one's points.
    var z: CGFloat { radius / 176 }
}

/// The rubric as a ring: its slices, a dot beyond each, a label for each round it (a button: what a screen reader and
/// the keys work), and the total in the middle — or, under the pointer, the criterion the pointer is on. A slice or its
/// label picks the criterion (shown beside the ring); a press beside the ring lets it go. With the ring focused, ← →
/// walk the criteria, Return opens the one under the pointer, Escape lets go. It blooms in, then bends to its marks.
struct RubricRingView: View {
    let model: RubricModel
    let stage: RingStage
    @Binding var selected: Int?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// How far it has bent to its marks (0 to 1): it blooms in round, then bends.
    @State private var grow: Double = 0
    @State private var shown = false
    /// The slice under the pointer on the ring, and the label under it.
    @State private var ringHover: Int?
    @State private var labelHover: Int?
    @FocusState private var focused: Bool

    private var hot: Int? { ringHover ?? labelHover }
    private var z: CGFloat { stage.z }

    var body: some View {
        let hueFill = AngularGradient(gradient: model.gradient(model.hues), center: .center, startAngle: .degrees(-90), endAngle: .degrees(270))
        let gradeFill = AngularGradient(gradient: model.gradient(model.grades), center: .center, startAngle: .degrees(-90), endAngle: .degrees(270))
        return ZStack {
            ForEach(0..<model.n, id: \.self) { k in
                slice(k, hueFill: hueFill, gradeFill: gradeFill)
            }
            ForEach(0..<model.n, id: \.self) { k in dot(k) }
            focusHalo
            centre
            ForEach(0..<model.n, id: \.self) { k in label(k) }
        }
        .frame(width: stage.width, height: stage.height)
        .contentShape(Rectangle())
        .onContinuousHover { phase in
            switch phase {
            case .active(let at): ringHover = hit(at)
            case .ended: ringHover = nil
            }
        }
        .gesture(SpatialTapGesture().onEnded { value in tapped(value.location) })
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(.rightArrow) { step(1) }
        .onKeyPress(.downArrow) { step(1) }
        .onKeyPress(.leftArrow) { step(-1) }
        .onKeyPress(.upArrow) { step(-1) }
        .onKeyPress(.return) { openHot() }
        .onKeyPress(.space) { openHot() }
        .onKeyPress(.escape) { letGo() }
        .scaleEffect(shown || reduceMotion ? 1 : 0.94)
        .opacity(shown ? 1 : 0)
        .onAppear(perform: bloom)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Rubric ring, \(model.n == 1 ? "1 criterion" : "\(model.n) criteria")")
    }

    // MARK: Parts

    private func slice(_ k: Int, hueFill: AngularGradient, gradeFill: AngularGradient) -> some View {
        let on = hot == k || selected == k
        let dim = selected != nil && selected != k && hot != k
        let shape = RingSlice(model: model, k: k, radius: stage.radius, swell: on ? 1 : 0, grow: grow)
        return ZStack {
            shape.fill(hueFill)
            if model.graded {
                shape.fill(gradeFill).opacity(grow)
            }
        }
        .opacity(dim ? 0.38 : 1)
        .animation(reduceMotion ? nil : Motion.hover, value: on)
        .animation(Motion.gentle, value: dim)
        .accessibilityHidden(true)
    }

    /// A dot beyond each slice's middle, in its colour.
    private func dot(_ k: Int) -> some View {
        let on = hot == k || selected == k
        let r = stage.radius + CGFloat(model.bend[k] * grow) * z + 22 * z + (on ? 9 * z : 0)
        let mid = model.mids[k]
        let colour = model.hues[k].mix(model.grades[k], model.graded ? grow : 0).color
        let size: CGFloat = on ? 9 : (model.n > 15 ? 5 : 7)
        return Circle()
            .fill(colour)
            .frame(width: size, height: size)
            .position(x: stage.width / 2 + r * CGFloat(sin(mid)), y: stage.height / 2 - r * CGFloat(cos(mid)))
            .opacity(selected != nil && selected != k && hot != k ? 0.38 : 1)
            .animation(reduceMotion ? nil : Motion.hover, value: on)
            .accessibilityHidden(true)
    }

    /// While the ring has the keys: a soft ring round it, so it is clear where they go.
    @ViewBuilder
    private var focusHalo: some View {
        if focused {
            Circle()
                .strokeBorder(Color.accentColor.opacity(0.45), lineWidth: 2)
                .frame(width: (stage.radius + 36 * z) * 2, height: (stage.radius + 36 * z) * 2)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }

    /// The total in the middle; the criterion under the pointer while it is on one.
    private var centre: some View {
        let inner = max(120, (stage.radius - 30 * z) * 2 * 0.86)
        let big = 46 * stage.radius / 136
        return VStack(spacing: 2) {
            if let k = hot, k < model.n {
                let c = model.criteria[k]
                Text(c.short)
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(model.colors[k].color)
                    .lineLimit(1)
                Text(RubricModel.num(c.score ?? c.worth))
                    .font(.system(size: big, weight: .bold, design: .rounded).monospacedDigit())
                Text(hoverLine(c))
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            } else {
                Text(model.graded ? "Score" : "Total")
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(.secondary)
                Text(RubricModel.num(model.graded ? model.earned : model.possible))
                    .font(.system(size: big, weight: .bold, design: .rounded).monospacedDigit())
                    .contentTransition(.numericText())
                Text(totalLine)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                if selected == nil {
                    Text("Pick a colour to open it")
                        .font(.sCaption)
                        .foregroundStyle(.tertiary)
                        .padding(.top, 4)
                }
            }
        }
        .multilineTextAlignment(.center)
        .frame(width: inner)
        .allowsHitTesting(false)
        .animation(Motion.snappy, value: hot)
        .accessibilityElement(children: .combine)
    }

    private var totalLine: String {
        guard model.graded else { return model.possible == 1 ? "point" : "points" }
        let pct = model.possible > 0 ? Int((model.earned / model.possible * 100).rounded()) : 0
        return "of \(RubricModel.num(model.possible)) · \(pct)%"
    }

    private func hoverLine(_ c: RubricModel.Criterion) -> String {
        guard model.graded else { return c.worth == 1 ? "point" : "points" }
        guard c.score != nil else { return "of \(RubricModel.num(c.worth)) · not marked" }
        let level = c.mark >= 0 ? " · \(c.levels[c.mark].label)" : ""
        return "of \(RubricModel.num(c.worth))\(level)"
    }

    /// A criterion's name and points round the ring, beside its slice: a button.
    private func label(_ k: Int) -> some View {
        let c = model.criteria[k]
        let mid = model.mids[k]
        let on = hot == k || selected == k
        let r = stage.radius + CGFloat(model.bend[k] * grow) * z + 36 * z + (on ? 9 * z : 0)
        let s = sin(mid)
        let co = cos(mid)
        let side: HorizontalAlignment = s > 0.3 ? .leading : (s < -0.3 ? .trailing : .center)
        let anchor = Alignment(horizontal: side, vertical: co > 0.3 ? .bottom : (co < -0.3 ? .top : .center))
        // (many criteria: every other label, and the ones the pointer or the pick is on)
        let quiet = model.n > 12 && k % 2 == 1 && !on
        return Color.clear
            .frame(width: 1, height: 1)
            .overlay(alignment: anchor) {
                labelButton(c, k, side: side, on: on)
                    .opacity(quiet ? 0 : 1)
            }
            .position(x: stage.width / 2 + r * CGFloat(s), y: stage.height / 2 - r * CGFloat(co))
            .animation(reduceMotion ? nil : Motion.hover, value: on)
    }

    /// The labels' size: smaller as the criteria crowd the ring.
    private var labelFont: Font {
        if model.n <= 6 { return Font.sCallout.weight(.semibold) }
        if model.n <= 10 { return Font.sFootnote.weight(.semibold) }
        return Font.sCaption.weight(.semibold)
    }

    private func labelButton(_ c: RubricModel.Criterion, _ k: Int, side: HorizontalAlignment, on: Bool) -> some View {
        Button {
            pick(k)
        } label: {
            VStack(alignment: side, spacing: 1) {
                Text(c.short)
                    .font(labelFont)
                    .foregroundStyle(on ? model.colors[k].color : Color.primary)
                if model.n <= 15 {
                    Text(model.pointsLabel(c))
                        .font((model.n <= 10 ? Font.sCaption : Font.sCaption2).monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            .multilineTextAlignment(side == .leading ? .leading : (side == .trailing ? .trailing : .center))
            .fixedSize()
            .padding(.horizontal, 5)
            .padding(.vertical, 3)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside {
                labelHover = k
            } else if labelHover == k {
                labelHover = nil
            }
        }
        .help(c.name)
        .accessibilityLabel(model.spoken(c))
        .accessibilityHint("Shows this criterion’s levels and comments")
        .accessibilityAddTraits(selected == k ? .isSelected : [])
    }

    // MARK: The pointer and the keys

    /// The slice under a point of the stage (on the band, or just inside or outside it).
    private func hit(_ at: CGPoint) -> Int? {
        guard model.n > 0 else { return nil }
        let dx = Double(at.x - stage.width / 2)
        let dy = Double(at.y - stage.height / 2)
        let r = (dx * dx + dy * dy).squareRoot()
        let R = Double(stage.radius)
        let zz = Double(z)
        guard r >= R - 34 * zz, r <= R + 48 * zz else { return nil }
        return model.slice(at: atan2(dx, -dy))
    }

    private func tapped(_ at: CGPoint) {
        focused = true
        if let k = hit(at) {
            pick(k)
        } else {
            withAnimation(Motion.gentle) { selected = nil }
        }
    }

    /// A criterion picked (the one already picked, let go).
    private func pick(_ k: Int) {
        withAnimation(Motion.gentle) { selected = selected == k ? nil : k }
    }

    private func step(_ d: Int) -> KeyPress.Result {
        guard model.n > 0 else { return .ignored }
        let from = selected ?? (d > 0 ? -1 : model.n)
        let k = ((from + d) % model.n + model.n) % model.n
        withAnimation(Motion.gentle) { selected = k }
        return .handled
    }

    private func openHot() -> KeyPress.Result {
        guard model.n > 0 else { return .ignored }
        let k = hot ?? selected ?? 0
        withAnimation(Motion.gentle) { selected = k }
        return .handled
    }

    private func letGo() -> KeyPress.Result {
        guard selected != nil else { return .ignored }
        withAnimation(Motion.gentle) { selected = nil }
        return .handled
    }

    /// In: round and in its own colours, then — marked — bending to its marks and turning to its grades' colours.
    private func bloom() {
        guard !shown else { return }
        if reduceMotion {
            shown = true
            grow = 1
            return
        }
        withAnimation(Motion.gentle) { shown = true }
        if model.graded {
            withAnimation(Motion.fill.delay(0.45)) { grow = 1 }
        } else {
            grow = 1
        }
    }
}

// MARK: - In miniature

/// The ring in miniature, for a rubric's button: each criterion's slice as long as its points, as thick as its share,
/// bent out where it did well and in where it lost points, its colours running into each other.
struct RubricMiniRing: View {
    let model: RubricModel
    var size: CGFloat = 30

    var body: some View {
        Canvas { ctx, box in
            guard model.n > 0 else { return }
            let cx = Double(box.width) / 2
            let cy = Double(box.height) / 2
            let zk = Double(size) / 56
            let outer = Double(size) / 2 - 2.5 * zk
            let cols = model.colors
            let th = model.thick.map { zk * min(8, max(5, $0 * 0.5)) }
            let go = model.bend.map { zk * min(1.5, max(-1.8, $0 * 0.15)) }
            let steps = 96
            func pt(_ r: Double, _ a: Double) -> CGPoint { CGPoint(x: cx + r * sin(a), y: cy - r * cos(a)) }
            for i in 0..<steps {
                let t0 = Double(i) / Double(steps) * tau
                let t1 = Double(i + 1) / Double(steps) * tau + 0.012 // (a hair over the next: no seam)
                let b0 = model.between(t0)
                let b1 = model.between(t1)
                let bm = model.between((t0 + t1) / 2)
                let o0 = outer + model.blend(go, b0)
                let o1 = outer + model.blend(go, b1)
                var p = Path()
                p.move(to: pt(o0, t0))
                p.addLine(to: pt(o1, t1))
                p.addLine(to: pt(o1 - model.blend(th, b1), t1))
                p.addLine(to: pt(o0 - model.blend(th, b0), t0))
                p.closeSubpath()
                ctx.fill(p, with: .color(cols[bm.from].mix(cols[bm.to], bm.t).color))
            }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}
