import SwiftUI

// The rubric as a ring (1.2): the web's rubric-ring.js on the Mac. One slice per criterion, as long as its share of the
// points; marked and posted, each slice turns to its grade's colour (green near full marks, yellow to orange for part
// marks, red where most was lost) and bends out where the work did well and in where it lost points, its colour,
// thickness and bend carried smoothly into its neighbours'. This file reads the rubric (RubricModel) and draws it in
// miniature; the ring itself floats over the window (1.2.2: RubricPopup.swift, drawn by RubricStage.swift).

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

    /// The colour as hue (degrees), saturation and lightness.
    var hslParts: (h: Double, s: Double, l: Double) {
        let rr = r / 255, gg = g / 255, bb = b / 255
        let mx = max(rr, gg, bb), mn = min(rr, gg, bb)
        let l = (mx + mn) / 2
        let d = mx - mn
        if d == 0 { return (0, 0, l) }
        let hh: Double
        if mx == rr {
            hh = ((gg - bb) / d).truncatingRemainder(dividingBy: 6)
        } else if mx == gg {
            hh = (bb - rr) / d + 2
        } else {
            hh = (rr - gg) / d + 4
        }
        return ((hh * 60 + 360).truncatingRemainder(dividingBy: 360), d / max(1e-9, 1 - abs(2 * l - 1)), l)
    }

    /// From this colour to another round the colour wheel, the short way (1.2.2): a turn that stays vivid, where a
    /// straight mix of two far-apart colours goes grey half-way.
    func turn(to other: RingRGB, _ t: Double) -> RingRGB {
        if t <= 0 { return self }
        if t >= 1 { return other }
        let a = hslParts
        let b = other.hslParts
        var dh = b.h - a.h
        if dh > 180 { dh -= 360 }
        if dh < -180 { dh += 360 }
        var hue = (a.h + dh * t).truncatingRemainder(dividingBy: 360)
        if hue < 0 { hue += 360 }
        return RingRGB.hsl(hue, a.s + (b.s - a.s) * t, a.l + (b.l - a.l) * t)
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

    /// The slice an angle falls in.
    func slice(at th: Double) -> Int {
        var a = th.truncatingRemainder(dividingBy: tau)
        if a < 0 { a += tau }
        return ends.firstIndex { a < $0 } ?? max(0, n - 1)
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
