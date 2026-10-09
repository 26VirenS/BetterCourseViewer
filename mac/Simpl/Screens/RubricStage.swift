import SwiftUI

// The rubric ring's stage (1.2.3): rubric-ring.js's drawing and motion on the Mac. A few values sit on one clock
// (RingMotion) — how far the ring has bloomed in, how far it has bent to its marks, how far it has opened a criterion
// into its bar, how far a switch along the bar has got, and each slice's swell under the pointer — and every point of
// the geometry is drawn from them each frame (RingPainter, in a Canvas under a TimelineView). A tween cut short starts
// again from where it is, so a press part-way through turns the ring round from where it stands. The stage is the web
// ring's own 760 × 560 units, scaled to the window; the words on it (the labels, the centre, the bar's levels) are
// real views placed on it, every label a button named for its criterion.

private let tau = 2 * Double.pi
private func clamp01(_ v: Double) -> Double { min(1, max(0, v)) }
private func smooth(_ v: Double) -> Double { v * v * (3 - 2 * v) }
/// From 0 at `a` to 1 at `b`, on an S.
private func span(_ e: Double, _ a: Double, _ b: Double) -> Double { smooth(clamp01((e - a) / (b - a))) }
/// The web ring's tween: in and out, cubic.
private func ringEase(_ x: Double) -> Double { x < 0.5 ? 4 * x * x * x : 1 - pow(-2 * x + 2, 3) / 2 }
/// Out, cubic: quick, then settling.
private func easeOut(_ x: Double) -> Double { 1 - pow(1 - clamp01(x), 3) }
/// A spring's step from 0 to 1: a little past 1, then settling (a slice growing in).
private func springOut(_ x: Double) -> Double {
    if x <= 0 { return 0 }
    if x >= 1 { return 1 }
    return 1 - exp(-5.5 * x) * cos(8.5 * x)
}

/// A point `r` from the ring's middle, at an angle clockwise from twelve o'clock.
private func pt(_ r: Double, _ a: Double) -> CGPoint {
    CGPoint(x: RingUnits.cx + r * sin(a), y: RingUnits.cy - r * cos(a))
}

private func disc(_ c: CGPoint, _ r: Double) -> Path {
    Path(ellipseIn: CGRect(x: Double(c.x) - r, y: Double(c.y) - r, width: 2 * r, height: 2 * r))
}

/// The stage's units: the web ring's.
enum RingUnits {
    static let width: Double = 760
    static let height: Double = 560
    static let cx: Double = 380
    static let cy: Double = 280
    static let radius: Double = 176
    /// The bar's line, its top, and its foot (shorter with a marker's note under it).
    static let barX: Double = 66
    static let barTop: Double = 158
    static let barFoot: Double = 500
    /// Where the bar's words start, and the room kept right of them.
    static let rowsX: Double = 108
    static let rowsRight: Double = 28
    /// The little ring on top of the bar (the way back): its middle and its radius.
    static let miniX: Double = 66
    static let miniY: Double = 56
    static let miniRadius: Double = 14.5
}

// MARK: - Where the ring stands

/// The ring at one moment: the clock's values, and which criterion is picked, was picked, and is under the pointer.
struct RingPose {
    /// From the ring (0) to the picked criterion's bar (1).
    var t: Double = 0
    /// How far it has bent to its marks and turned to its grades' colours.
    var g: Double = 0
    /// A switch along the bar, from the criterion before (0) to this one (1).
    var w: Double = 1
    /// How far it has bloomed in.
    var bloom: Double = 1
    var sel = 0
    var prev = -1
    var hov = -1
    var swell: [Double] = []
    /// The bar's foot before a switch and after it.
    var foot0: Double = RingUnits.barFoot
    var foot: Double = RingUnits.barFoot

    /// The ring turns the slice to its left side…
    var p1: Double { span(t, 0, 0.5) }
    /// (its bend evened out first, so the straightening starts from a true arc)
    var pf: Double { span(t, 0, 0.2) }
    /// …while it straightens into the bar…
    var u: Double { span(t, 0.22, 1) }
    /// …and the rest of the ring sinks into the centre.
    var q: Double { span(t, 0.02, 0.62) }
    var barFoot: Double { foot0 + (foot - foot0) * w }
    var ringAlpha: Double { clamp01(1 - q * 1.8) }

    func swellOf(_ k: Int) -> Double { k >= 0 && k < swell.count ? swell[k] : 0 }
}

// MARK: - The clock

/// The ring's few moving values on one clock, the way the web's frame loop keeps them: each moved by a tween that,
/// cut short by another, starts from where it is; the pointer's swell easing in and out slice by slice. While any of
/// them moves, `running` keeps the stage drawing every frame; still, it is drawn as it has settled. Under Reduce
/// Motion (`instant`) every change is at once.
@MainActor
final class RingMotion: ObservableObject {
    enum Key: Hashable {
        case open, marks, swap, bloom
    }

    private struct Tween {
        let from: Double
        let to: Double
        let start: Double
        let duration: Double
        let linear: Bool
        let serial: Int
    }

    /// A time past every tween: where everything has settled.
    static let settled: Double = 1e12
    static var now: Double { Date().timeIntervalSinceReferenceDate }

    let count: Int
    private let mids: [Double]
    private let bottoms: [Double]
    var instant = false

    @Published private(set) var running = false
    @Published private(set) var sel = 0
    @Published private(set) var prev = -1
    @Published private(set) var hov = -1
    /// The little ring's turn so far, carried on so a switch turns it the short way.
    @Published private(set) var spun: Double = 0
    @Published private(set) var foot0: Double = RingUnits.barFoot
    @Published private(set) var foot: Double = RingUnits.barFoot

    private var resting: [Key: Double] = [.open: 0, .marks: 0, .swap: 1, .bloom: 0]
    private var tweens: [Key: Tween] = [:]
    private var serial = 0
    private var swellFrom: [Double]
    private var swellAim: [Double]
    private var swellAt: [Double]
    private var settleAt: Double = 0
    private var settleTask: Task<Void, Never>?
    private var started = false

    init(geometry: RingGeometry) {
        count = geometry.n
        mids = geometry.model.mids
        bottoms = geometry.bottoms
        swellFrom = Array(repeating: 0, count: geometry.n)
        swellAim = Array(repeating: 0, count: geometry.n)
        swellAt = Array(repeating: 0, count: geometry.n)
    }

    // MARK: Reading it

    func value(_ key: Key, at now: Double) -> Double {
        guard let tw = tweens[key] else { return resting[key] ?? 0 }
        if now <= tw.start { return tw.from }
        let x = min(1, (now - tw.start) / tw.duration)
        return tw.from + (tw.to - tw.from) * (tw.linear ? x : ringEase(x))
    }

    /// Where a value is going (or is).
    func target(_ key: Key) -> Double { resting[key] ?? 0 }

    var openNow: Double { value(.open, at: RingMotion.now) }

    func swell(_ k: Int, at now: Double) -> Double {
        guard k >= 0, k < count else { return 0 }
        let aim = swellAim[k]
        if instant { return aim }
        let v = aim + (swellFrom[k] - aim) * exp(-max(0, now - swellAt[k]) / 0.115)
        return abs(v - aim) < 0.003 ? aim : v
    }

    func pose(at now: Double) -> RingPose {
        var p = RingPose()
        p.t = value(.open, at: now)
        p.g = value(.marks, at: now)
        p.w = value(.swap, at: now)
        p.bloom = value(.bloom, at: now)
        p.sel = sel
        p.prev = prev
        p.hov = hov
        p.swell = (0..<count).map { swell($0, at: now) }
        p.foot0 = foot0
        p.foot = foot
        return p
    }

    // MARK: Moving it

    /// The ring blooms in; marked, it then bends to its marks (and, for the screenshot suite, opens a criterion).
    func start(graded: Bool, opening k: Int?) {
        guard !started else { return }
        started = true
        if instant {
            set(.bloom, 1)
            set(.marks, graded ? 1 : 0)
            if let k { select(k) }
            return
        }
        tween(.bloom, to: 1, duration: 1.1, linear: true)
        if graded { tween(.marks, to: 1, duration: 1.15, delay: 0.5) }
        if let k {
            Task { [weak self] in
                try? await Task.sleep(nanoseconds: 850_000_000)
                self?.select(k)
            }
        }
    }

    /// A criterion opened (a slice, its label, Return): from the ring, the slice unrolls into its bar; with the bar out,
    /// the bar switches to it in place; part-way, the ring goes back round first.
    func select(_ raw: Int) {
        guard count > 0 else { return }
        let k = ((raw % count) + count) % count
        let t = openNow
        if k == sel && t > 0.98 { return }
        if t > 0.98 && k != sel {
            prev = sel
            sel = k
            fill(k, swap: true)
            set(.swap, 0)
            tween(.swap, to: 1, duration: 0.52)
            return
        }
        if t > 0.02 && k != sel {
            tween(.open, to: 0, duration: 0.48) { [weak self] in self?.select(k) }
            return
        }
        sel = k
        prev = -1
        set(.swap, 1)
        fill(k, swap: false)
        tween(.open, to: 1, duration: 1.15)
        aimSwell()
    }

    /// The bar rolls back up into the ring.
    func toRing() {
        guard openNow > 0.001 else { return }
        tween(.open, to: 0, duration: 0.95) { [weak self] in self?.aimSwell() }
    }

    /// ← → : along the ring (the slice swells and its criterion shows in the middle), or along the bar.
    func step(_ d: Int) {
        guard count > 0 else { return }
        let k = ((sel + d) % count + count) % count
        if openNow > 0.5 {
            select(k)
        } else {
            sel = k
            hover(k)
        }
    }

    func openCurrent() { select(sel) }

    func hover(_ k: Int) {
        guard hov != k else { return }
        hov = k
        aimSwell()
    }

    // MARK: The clock's workings

    /// The bar's foot for a criterion, and the little ring turned so the slice's gap faces the bar, the short way.
    private func fill(_ k: Int, swap: Bool) {
        var to = 180 - mids[k] * 180 / Double.pi
        let d = (to - spun).truncatingRemainder(dividingBy: 360)
        to = spun + (d + 540).truncatingRemainder(dividingBy: 360) - 180
        spun = to
        let next = k < bottoms.count ? bottoms[k] : RingUnits.barFoot
        foot0 = swap ? foot : next
        foot = next
    }

    /// Each slice swells while the pointer (or the keys) are on it and the ring is showing; the rest settle back.
    private func aimSwell() {
        let now = RingMotion.now
        let ringShowing = target(.open) < 0.5 && value(.open, at: now) < 0.02
        var changed = false
        for i in 0..<count {
            let aim: Double = i == hov && ringShowing ? 1 : 0
            if aim == swellAim[i] { continue }
            swellFrom[i] = swell(i, at: now)
            swellAim[i] = aim
            swellAt[i] = now
            changed = true
        }
        guard changed else { return }
        if instant {
            objectWillChange.send()
        } else {
            keepRunning(until: now + 0.8)
        }
    }

    private func set(_ key: Key, _ v: Double) {
        tweens[key] = nil
        resting[key] = v
        objectWillChange.send()
    }

    private func tween(_ key: Key, to: Double, duration: Double, delay: Double = 0, linear: Bool = false, done: (() -> Void)? = nil) {
        let now = RingMotion.now
        let from = value(key, at: now)
        serial += 1
        let mine = serial
        resting[key] = to
        if instant || duration <= 0 {
            tweens[key] = nil
            objectWillChange.send()
            done?()
            return
        }
        tweens[key] = Tween(from: from, to: to, start: now + delay, duration: duration, linear: linear, serial: mine)
        keepRunning(until: now + delay + duration)
        Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64((delay + duration) * 1_000_000_000))
            guard let self, self.tweens[key]?.serial == mine else { return }
            self.tweens[key] = nil
            done?()
        }
    }

    /// Frames while anything moves; once all has settled, none.
    private func keepRunning(until end: Double) {
        settleAt = max(settleAt, end)
        if !running { running = true }
        settleTask?.cancel()
        let wait = max(0, settleAt - RingMotion.now) + 0.06
        settleTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(wait * 1_000_000_000))
            guard !Task.isCancelled, let self else { return }
            if RingMotion.now >= self.settleAt {
                self.running = false
            } else {
                self.keepRunning(until: self.settleAt)
            }
        }
    }
}

// MARK: - The ring's shape

/// A level's place on the open criterion's bar: its tick (at the height of its points) and its row (pushed clear of
/// its neighbours), in the stage's units.
struct BarPlace {
    let tick: Double
    let row: Double
    let picked: Bool
}

/// A label's place round the ring: the point beside its slice, which way the label hangs from it, and whether it shows
/// (a crowded ring shows every other one).
struct LabelSpot {
    let point: CGPoint
    let anchor: UnitPoint
    let align: HorizontalAlignment
    let shown: Bool
}

/// The rubric's ring as it stands still: what does not change from frame to frame.
struct RingGeometry {
    let model: RubricModel
    /// How many pieces each slice is drawn in at rest: each turns at most 20°, an even number of them (so one starts at
    /// the slice's middle), and a straight gradient along each reads true to the curve.
    let pieces: [Int]
    /// Each criterion's bar foot: shorter with a marker's note under it.
    let bottoms: [Double]

    init(model: RubricModel) {
        self.model = model
        let piece = Double.pi / 9
        pieces = (0..<model.n).map { k in
            2 * max(1, Int(((model.ends[k] - model.starts[k]) / (2 * piece)).rounded(.up)))
        }
        bottoms = model.criteria.map { c in
            c.comment.isEmpty ? RingUnits.barFoot : min(466, 510 - RingGeometry.noteHeight(c.comment))
        }
    }

    var n: Int { model.n }

    /// About how tall a marker's note stands, in the stage's units (its box holds 92 at most and scrolls past that).
    static func noteHeight(_ text: String) -> Double {
        let breaks = Double(text.filter { $0 == "\n" }.count)
        let lines = max(1, (Double(text.count) / 78).rounded(.up)) + breaks
        return min(92, 20 + lines * 18)
    }

    /// Each slice's colour at a point of the turn to its marks: its own, turning round the colour wheel to its grade's.
    func tones(_ g: Double) -> [RingRGB] {
        guard model.graded else { return model.hues }
        return (0..<n).map { model.hues[$0].turn(to: model.grades[$0], g) }
    }

    /// Where each bar's colour stops (its mark), and each slice's bend, as far as the ring has gone to its marks.
    func fracs(_ g: Double) -> [Double] { model.criteria.map { 1 + ($0.frac - 1) * g } }
    func bends(_ g: Double) -> [Double] { model.bend.map { $0 * g } }

    /// The turn that brings slice `k` round to the ring's left side, the short way.
    func turn(to k: Int) -> Double {
        guard k >= 0, k < n else { return 0 }
        var d = 1.5 * Double.pi - model.mids[k]
        while d > Double.pi { d -= tau }
        while d < -Double.pi { d += tau }
        return d
    }

    /// The slice under a point of the stage: on its band, or just inside or outside it.
    func slice(x: Double, y: Double) -> Int? {
        guard n > 0 else { return nil }
        let dx = x - RingUnits.cx
        let dy = y - RingUnits.cy
        let r = (dx * dx + dy * dy).squareRoot()
        guard r >= RingUnits.radius - 30, r <= RingUnits.radius + 44 else { return nil }
        return model.slice(at: atan2(dx, -dy))
    }

    // MARK: The bar

    /// A criterion's levels up its bar — or, marked freely, its one score (or what it is worth) standing alone.
    func levels(_ k: Int) -> [RubricModel.Level] {
        guard k >= 0, k < n else { return [] }
        let c = model.criteria[k]
        if !c.levels.isEmpty { return c.levels }
        if model.graded, let s = c.score {
            return [RubricModel.Level(label: "Your score", text: "Out of \(RubricModel.num(c.worth)) — marked without set levels.", pts: s)]
        }
        return [RubricModel.Level(label: "Marked freely", text: "No set levels: your teacher gives a score up to this.", pts: c.worth)]
    }

    /// Whether a level is the one given.
    func picked(_ k: Int, _ i: Int) -> Bool {
        guard model.graded, k >= 0, k < n else { return false }
        let c = model.criteria[k]
        return c.levels.isEmpty ? c.score != nil : c.mark == i
    }

    /// Where a level sits on its bar: by points, the best at the top (evenly in order where there are none to go by).
    private func barY(_ k: Int, _ p: Double?, _ i: Int, _ foot: Double) -> Double {
        let c = model.criteria[k]
        let top = RingUnits.barTop
        if c.levels.isEmpty {
            let most = max(c.worth, c.score ?? 0, 1e-9)
            return foot - (foot - top) * clamp01((p ?? 0) / most)
        }
        let count = c.levels.count
        let even = count <= 1 ? top : top + (foot - top) * Double(i) / Double(count - 1)
        let vals = c.levels.compactMap(\.pts)
        guard let hi = vals.max(), let lo = vals.min(), hi - lo >= 1e-9, let p else { return even }
        return foot - (foot - top) * (p - lo) / (hi - lo)
    }

    /// The levels at the height of their points; where two rows would overlap they are pushed apart (a leader joins
    /// each moved row to its tick), and at the foot back up. `heights` are the rows' own, in the stage's units.
    func places(_ k: Int, foot: Double, heights: [Double]) -> [BarPlace] {
        let ls = levels(k)
        guard !ls.isEmpty else { return [] }
        let want = ls.indices.map { barY(k, ls[$0].pts, $0, foot) }
        let hs = ls.indices.map { $0 < heights.count ? heights[$0] : 46 }
        var ys = want
        for i in ys.indices.dropFirst() {
            ys[i] = max(ys[i], ys[i - 1] + hs[i - 1] / 2 + 6 + hs[i] / 2)
        }
        let last = ys.count - 1
        if ys[last] + hs[last] / 2 > foot + 30 {
            ys[last] = foot + 30 - hs[last] / 2
            for i in stride(from: last - 1, through: 0, by: -1) {
                ys[i] = min(ys[i], ys[i + 1] - hs[i + 1] / 2 - 6 - hs[i] / 2)
            }
        }
        return ls.indices.map { BarPlace(tick: want[$0], row: ys[$0], picked: picked(k, $0)) }
    }

    // MARK: The labels

    /// The labels ride round with their slices (turning as the ring turns) and sink in with the ring.
    func labelSpots(_ pose: RingPose, bends go: [Double]) -> [LabelSpot] {
        let q = pose.q
        let rot = turn(to: pose.sel) * pose.p1
        let crowd = n >= 15 ? 2 : 1
        return (0..<n).map { k in
            let mid = model.mids[k] + rot
            let r = (RingUnits.radius + go[k] + 36 + 9 * pose.swellOf(k) * (1 - q)) * (1 - 0.2 * q)
            let s = sin(mid)
            let co = cos(mid)
            let sx = max(-1, min(1, s / 0.3))
            let sy = max(-1, min(1, co / 0.3))
            let align: HorizontalAlignment = s > 0.3 ? .leading : (s < -0.3 ? .trailing : .center)
            let near = k == (pose.sel + 1) % n || k == (pose.sel + n - 1) % n
            let even = k % crowd == 0 && !(k == n - 1 && n % crowd != 0)
            let shown = n <= 10 || k == pose.sel || k == pose.hov || (!near && even)
            return LabelSpot(point: pt(r, mid), anchor: UnitPoint(x: 0.5 - 0.5 * sx, y: 0.5 + 0.5 * sy), align: align, shown: shown)
        }
    }
}

// MARK: - Drawing

/// A point along a band: its middle, the way out from it, its width.
private struct BandPoint {
    let x: Double
    let y: Double
    let nx: Double
    let ny: Double
    let w: Double
}

/// Every point of the ring for one pose, in the stage's units: the slices at rest (bloomed in, turned and sunk as a
/// whole while a criterion opens), the one on its way to the bar drawn afresh, the thread from the bar's top up to the
/// little ring, the ticks of the levels and the leaders to rows that had to move. The band is filled pieces, each with
/// a gradient along it, its bend, thickness and colour carried smoothly from slice to slice.
struct RingPainter {
    let geo: RingGeometry
    let pose: RingPose
    let tones: [RingRGB]
    let fracs: [Double]
    let go: [Double]
    let places: [BarPlace]

    /// A bar's unearned stretch.
    private static let track = RingRGB(r: 72, g: 72, b: 74)
    private static let tickFill = Color(red: 28 / 255, green: 28 / 255, blue: 30 / 255)

    func paint(_ ctx: inout GraphicsContext, scale: CGFloat) {
        guard geo.n > 0, pose.sel >= 0, pose.sel < geo.n else { return }
        ctx.scaleBy(x: scale, y: scale)
        let moving = pose.t > 0
        let sample = movingSampler()
        if moving { stem(ctx, top: sample(1)) }
        rest(ctx)
        if moving { band(ctx, sample) }
        ticks(ctx)
    }

    // MARK: Colour

    private func colourAt(_ k: Int, _ v: Double, _ ee: Double, _ u: Double) -> RingRGB {
        let m = geo.model
        let b = m.between(m.starts[k] + (m.ends[k] - m.starts[k]) * v)
        var col = tones[b.from].mix(tones[b.to], b.t) // (the ring's own run of colour, or its grades'…)
        if ee > 0 { // …becoming the bar's: its own colour, darker at the foot and lighter at the top
            let own = tones[k]
            let bar = v < 0.5 ? own.mix(RingRGB.black, 0.36 * (0.5 - v)) : own.mix(RingRGB.white, 0.44 * (v - 0.5))
            col = col.mix(bar, ee)
            if v > fracs[k] + 1e-6 { col = col.mix(RingPainter.track, 0.8 * u) } // (the stretch above the mark was not earned)
        }
        return col
    }

    /// …and across a switch along the bar, the old criterion's colour crossing over to the new one's.
    private func colourOf(_ k: Int, _ v: Double, _ ee: Double, _ u: Double) -> RingRGB {
        let col = colourAt(k, v, ee, u)
        let p = pose.prev
        guard k == pose.sel, pose.w < 1, p >= 0, p < geo.n, p != k else { return col }
        return colourAt(p, v, 1, 1).mix(col, pose.w)
    }

    // MARK: Pieces

    /// One piece of band from v0 to v1 (and on a hair to v1e, under the next piece): its outline from `sample`, its
    /// colour a gradient along it from `colour`.
    private func piece(_ ctx: GraphicsContext, _ sample: (Double) -> BandPoint, _ colour: (Double) -> RingRGB,
                       _ v0: Double, _ v1: Double, _ v1e: Double, _ len: Double) {
        let steps = max(3, min(90, Int((len / 2.5).rounded(.up))))
        var outer: [CGPoint] = []
        var inner: [CGPoint] = []
        outer.reserveCapacity(steps + 1)
        inner.reserveCapacity(steps + 1)
        for j in 0...steps {
            let s = sample(v0 + (v1e - v0) * Double(j) / Double(steps))
            let hw = s.w / 2
            outer.append(CGPoint(x: s.x + s.nx * hw, y: s.y + s.ny * hw))
            inner.append(CGPoint(x: s.x - s.nx * hw, y: s.y - s.ny * hw))
        }
        var path = Path()
        path.addLines(outer + Array(inner.reversed()))
        path.closeSubpath()
        let a = sample(v0)
        let b = sample(v1)
        var stops: [Gradient.Stop] = []
        for q in 0..<5 {
            let o = Double(q) / 4
            stops.append(Gradient.Stop(color: colour(v0 + (v1 - v0) * o).color, location: o))
        }
        if abs(b.x - a.x) + abs(b.y - a.y) < 0.05 {
            ctx.fill(path, with: .color(stops[2].color))
        } else {
            ctx.fill(path, with: .linearGradient(Gradient(stops: stops), startPoint: CGPoint(x: a.x, y: a.y), endPoint: CGPoint(x: b.x, y: b.y)))
        }
    }

    // MARK: The ring at rest

    /// The slices not on their way to the bar: one group, bloomed in (each slice sweeping round from its start and
    /// growing to its thickness on a spring, one after the other), turned and shrunk into the centre as one opens.
    private func rest(_ base: GraphicsContext) {
        let n = geo.n
        let moving = pose.t > 0
        let q = pose.q
        let fade = clamp01(1 - q * 1.25)
        if moving && fade < 0.005 { return }
        let rot = geo.turn(to: pose.sel) * pose.p1
        let bloom = pose.bloom
        let spin = -0.6 * (1 - easeOut(bloom))
        let size = (1 - 0.34 * q) * (0.82 + 0.18 * springOut(bloom))
        var ctx = base
        ctx.translateBy(x: RingUnits.cx, y: RingUnits.cy)
        ctx.rotate(by: .radians(rot + spin))
        ctx.scaleBy(x: size, y: size)
        ctx.translateBy(x: -RingUnits.cx, y: -RingUnits.cy)
        let groupA = moving ? fade : 1
        ctx.opacity = groupA
        let lead = n > 1 ? 0.42 / Double(n - 1) : 0
        let dotA = clamp01(1 - q * 1.8)
        for k in 0..<n {
            if moving && k == pose.sel { continue }
            let local = clamp01((bloom - lead * Double(k)) / 0.58)
            let sweep = easeOut(local)
            if sweep <= 0.001 { continue }
            restSlice(ctx, k, sweep: sweep, grow: springOut(local))
            let d = pt(RingUnits.radius + go[k] + 22 + 9 * pose.swellOf(k), geo.model.mids[k])
            let r = k == pose.sel || k == pose.hov ? 4.5 : (n > 15 ? 2.5 : 3.5)
            var dc = ctx
            dc.opacity = groupA * dotA * span(local, 0.5, 1)
            dc.fill(disc(d, r), with: .color(tones[k].color))
        }
    }

    private func restSlice(_ ctx: GraphicsContext, _ k: Int, sweep: Double, grow: Double) {
        let m = geo.model
        let a0 = m.starts[k]
        let a1 = m.ends[k]
        let bump = pose.swellOf(k)
        let bend = go
        let sample: (Double) -> BandPoint = { v in
            let th = a0 + (a1 - a0) * v * sweep
            let b = m.between(th)
            let s1 = sin(Double.pi * min(1, v))
            let sw = bump * s1 * s1 // (the slice under the pointer swells out, smoothly, and back)
            let thick = m.blend(m.thick, b) * grow
            let rad = RingUnits.radius + m.blend(bend, b) - (thick - 10) / 2 + 9 * sw // (thicker inward: the outer edge on the circle)
            let sn = sin(th)
            let cs = cos(th)
            return BandPoint(x: RingUnits.cx + rad * sn, y: RingUnits.cy - rad * cs, nx: sn, ny: -cs, w: thick + 2.5 * sw)
        }
        let colour: (Double) -> RingRGB = { v in self.colourAt(k, v * sweep, 0, 0) }
        let count = geo.pieces[k]
        let len = max(1, RingUnits.radius * (a1 - a0) * sweep)
        let eps = geo.n > 1 ? 0.5 / len : 0 // (each piece runs on a hair under the next: no seam shows)
        for i in 0..<count {
            let v0 = Double(i) / Double(count)
            let v1 = Double(i + 1) / Double(count)
            piece(ctx, sample, colour, v0, v1, v1 + eps, len / Double(count))
        }
    }

    // MARK: The slice on its way to the bar

    private func movingLength() -> Double {
        let m = geo.model
        let k = pose.sel
        let rs = RingUnits.radius + go[k]
        let arc = rs * (m.ends[k] - m.starts[k])
        return arc + (pose.barFoot - RingUnits.barTop - arc) * pose.u
    }

    /// The picked slice, every point: turning round the centre, its bend evening out to a true arc; then unrolling
    /// about its middle into the bar, turned back by what the ring has still to turn, so the two motions overlap.
    private func movingSampler() -> (Double) -> BandPoint {
        let m = geo.model
        let k = pose.sel
        let a0 = m.starts[k]
        let a1 = m.ends[k]
        let bump = pose.swellOf(k)
        let u = pose.u
        let pf = pose.pf
        let dRot = geo.turn(to: k)
        let rot = dRot * pose.p1
        let back = dRot * (pose.p1 - 1)
        let foot = pose.barFoot
        let bend = go
        let rs = RingUnits.radius + go[k]
        let length = rs * (a1 - a0) + (foot - RingUnits.barTop - rs * (a1 - a0)) * u
        let kap = (1 - u) / rs
        let mx = RingUnits.cx - rs + (RingUnits.barX - (RingUnits.cx - rs)) * u
        let my = RingUnits.cy + ((foot + RingUnits.barTop) / 2 - RingUnits.cy) * u
        let cr = cos(back)
        let sr = sin(back)
        return { v in
            let th = a0 + (a1 - a0) * v
            let b = m.between(th)
            let s1 = sin(Double.pi * clamp01(v))
            let sw = bump * s1 * s1
            let thick = m.blend(m.thick, b)
            let wd = thick + 2.5 * sw + (10 - thick - 2.5 * sw) * pf + 6 * u
            if u <= 0 {
                var rad = RingUnits.radius + m.blend(bend, b) - (thick - 10) / 2 + 9 * sw
                rad += (rs - rad) * pf
                let a = th + rot
                let sn = sin(a)
                let cs = cos(a)
                return BandPoint(x: RingUnits.cx + rad * sn, y: RingUnits.cy - rad * cs, nx: sn, ny: -cs, w: wd)
            }
            let s = (v - 0.5) * length
            let x: Double
            let y: Double
            let nx: Double
            let ny: Double
            if kap < 1e-5 {
                x = mx
                y = my - s
                nx = -1
                ny = 0
            } else {
                let ks = kap * s
                x = mx + (1 - cos(ks)) / kap
                y = my - sin(ks) / kap
                nx = -cos(ks)
                ny = -sin(ks)
            }
            if abs(back) < 1e-4 { return BandPoint(x: x, y: y, nx: nx, ny: ny, w: wd) }
            let dx = x - RingUnits.cx
            let dy = y - RingUnits.cy
            return BandPoint(x: RingUnits.cx + dx * cr - dy * sr, y: RingUnits.cy + dx * sr + dy * cr,
                             nx: nx * cr - ny * sr, ny: nx * sr + ny * cr, w: wd)
        }
    }

    /// The picked slice in pieces — eighths, split where the colour steps (its mark, the old one's mid-switch) — its
    /// ends rounding off as it comes away from its neighbours, and its dot.
    private func band(_ ctx: GraphicsContext, _ sample: (Double) -> BandPoint) {
        let k = pose.sel
        let u = pose.u
        var cuts: [Double] = [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1]
        var steps = [fracs[k]]
        if pose.w < 1, pose.prev >= 0, pose.prev < geo.n { steps.append(fracs[pose.prev]) }
        for fr in steps where fr > 1e-3 && fr < 1 - 1e-3 {
            if cuts.allSatisfy({ abs($0 - fr) > 1e-3 }) { cuts.append(fr) }
        }
        cuts.sort()
        let capT = span(pose.t, 0.02, 0.3)
        let length = max(movingLength(), 1)
        let colour: (Double) -> RingRGB = { v in self.colourOf(k, v, u, u) }
        for i in 0..<(cuts.count - 1) {
            let v0 = cuts[i]
            let v1 = cuts[i + 1]
            let v1e = i == cuts.count - 2 ? 1 + (0.5 / length) * (1 - capT) : v1 + 0.4 / length
            piece(ctx, sample, colour, v0, v1, v1e, length * (v1 - v0))
        }
        for v in [0.0, 1.0] {
            let s = sample(v)
            let r = s.w / 2 * capT
            if r > 0.01 { ctx.fill(disc(CGPoint(x: s.x, y: s.y), r), with: .color(colour(v).color)) }
        }
        let rot = geo.turn(to: k) * pose.p1
        let d = pt(RingUnits.radius + go[k] + 22 + 9 * pose.swellOf(k), geo.model.mids[k] + rot)
        var dc = ctx
        dc.opacity = clamp01(1 - pose.q * 1.8)
        dc.fill(disc(d, 4.5), with: .color(tones[k].color))
    }

    /// The thread from the bar's top up into the little ring (the way back), drawn out of the bar as it straightens.
    private func stem(_ base: GraphicsContext, top: BandPoint) {
        let grow = span(pose.u, 0.76, 1)
        guard grow > 0 else { return }
        let k = pose.sel
        let u = pose.u
        let fx = RingUnits.miniX
        let fy = RingUnits.miniY + RingUnits.miniRadius
        var line = Path()
        line.move(to: CGPoint(x: top.x, y: top.y))
        line.addLine(to: CGPoint(x: top.x + (fx - top.x) * grow, y: top.y + (fy - top.y) * grow))
        let p = pose.prev
        let end = pose.w < 1 && p >= 0 && p < geo.n ? tones[p].mix(tones[k], pose.w) : tones[k]
        var ctx = base
        ctx.opacity = grow
        ctx.stroke(line,
                   with: .linearGradient(Gradient(colors: [colourOf(k, 1, u, u).color, end.color]),
                                         startPoint: CGPoint(x: top.x, y: top.y), endPoint: CGPoint(x: fx, y: fy)),
                   style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
    }

    /// The levels' ticks on the bar, row by row, and a leader to any row that had to move.
    private func ticks(_ base: GraphicsContext) {
        let u = pose.u
        let w = pose.w
        guard u > 0, !places.isEmpty else { return }
        let bx = RingUnits.barX
        let lead = span(u, 0.7, 1) * span(w, 0.4, 1)
        if lead > 0 {
            var ctx = base
            ctx.opacity = lead
            for p in places where abs(p.row - p.tick) >= 3 {
                var path = Path()
                path.move(to: CGPoint(x: bx + 12, y: p.tick))
                path.addCurve(to: CGPoint(x: 102, y: p.row), control1: CGPoint(x: bx + 28, y: p.tick), control2: CGPoint(x: 86, y: p.row))
                ctx.stroke(path, with: .color(Color.white.opacity(0.24)), lineWidth: 1.5)
            }
        }
        let mine = geo.model.colors[pose.sel].color
        for (i, p) in places.enumerated() {
            let x = Double(i)
            let a = span(u, 0.8 + 0.04 * x, 0.96 + 0.04 * x) * span(w, 0.3 + 0.06 * x, 0.8 + 0.06 * x)
            if a <= 0.001 { continue }
            let z = 0.2 + 0.8 * a
            var ctx = base
            ctx.opacity = a
            let dot = disc(CGPoint(x: bx, y: p.tick), (p.picked ? 8 : 5.5) * z)
            ctx.fill(dot, with: .color(p.picked ? Color.white : RingPainter.tickFill))
            ctx.stroke(dot, with: .color(p.picked ? mine : Color.white.opacity(0.82)), lineWidth: 2.5 * z)
        }
    }
}

// MARK: - The stage

/// Each row of the open criterion's bar says how tall it is (keyed by criterion × 1000 + level), so the rows can be
/// placed at the height of their points and pushed apart where two would overlap.
struct BarRowHeights: PreferenceKey {
    static let defaultValue: [Int: CGFloat] = [:]

    static func reduce(value: inout [Int: CGFloat], nextValue: () -> [Int: CGFloat]) {
        value.merge(nextValue()) { _, new in new }
    }
}

/// The ring on its stage as the clock has it now: drawn every frame while anything moves, still otherwise. Under
/// Reduce Motion the ring and a criterion's bar cross-fade instead of the unroll.
struct RubricStageView: View {
    let geo: RingGeometry
    @ObservedObject var motion: RingMotion
    let scale: CGFloat
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        TimelineView(.animation(minimumInterval: nil, paused: !motion.running)) { tl in
            let now = motion.running ? tl.date.timeIntervalSinceReferenceDate : RingMotion.settled
            ZStack(alignment: .topLeading) {
                RingStageFrame(geo: geo, pose: motion.pose(at: now), motion: motion, scale: scale, spun: motion.spun)
                    .id(phase)
                    .transition(.opacity.animation(.easeInOut(duration: 0.3)))
            }
        }
    }

    private var phase: String {
        guard reduceMotion else { return "live" }
        return motion.target(.open) > 0.5 ? "bar\(motion.sel)" : "ring"
    }
}

/// One frame of the stage: the drawing, the labels round it, the total in the middle, and the open criterion's bar
/// with its words beside it. Everything here comes from the pose; the presses go to the clock. The bar's rows are
/// measured first (unseen copies), so each can be placed at the height of its points.
private struct RingStageFrame: View {
    let geo: RingGeometry
    let pose: RingPose
    let motion: RingMotion
    let scale: CGFloat
    let spun: Double

    var body: some View {
        BarMeasure(geo: geo, k: pose.sel, width: BarSide.rowWidth(scale))
            .frame(width: CGFloat(RingUnits.width) * scale, height: CGFloat(RingUnits.height) * scale, alignment: .topLeading)
            .overlayPreferenceValue(BarRowHeights.self) { measured in
                content(measured)
            }
    }

    private func content(_ measured: [Int: CGFloat]) -> some View {
        let tones = geo.tones(pose.g)
        let go = geo.bends(pose.g)
        let places = geo.places(pose.sel, foot: pose.foot, heights: unitHeights(measured))
        let painter = RingPainter(geo: geo, pose: pose, tones: tones, fracs: geo.fracs(pose.g), go: go, places: places)
        return ZStack(alignment: .topLeading) {
            Canvas { ctx, _ in painter.paint(&ctx, scale: scale) }
                .allowsHitTesting(false)
                .accessibilityHidden(true)
            RingLabels(geo: geo, pose: pose, go: go, tones: tones, motion: motion, scale: scale)
            RingCentre(model: geo.model, pose: pose, tones: tones, scale: scale)
            BarSide(geo: geo, pose: pose, places: places, motion: motion, scale: scale, spun: spun)
        }
        .frame(width: CGFloat(RingUnits.width) * scale, height: CGFloat(RingUnits.height) * scale, alignment: .topLeading)
    }

    /// The open criterion's rows' heights, in the stage's units (a guess until they have been measured).
    private func unitHeights(_ measured: [Int: CGFloat]) -> [Double] {
        geo.levels(pose.sel).indices.map { i -> Double in
            guard let h = measured[pose.sel * 1000 + i], scale > 0 else { return 46 }
            return Double(h / scale)
        }
    }
}

/// The open criterion's rows, unseen, each saying how tall it stands at the bar's width.
private struct BarMeasure: View {
    let geo: RingGeometry
    let k: Int
    let width: CGFloat

    var body: some View {
        let levels = geo.levels(k)
        let lines = BarLevelRow.lines(levels.count)
        let tone = k >= 0 && k < geo.n ? geo.model.colors[k] : RingRGB.unmarked
        VStack(alignment: .leading, spacing: 0) {
            ForEach(levels.indices, id: \.self) { i in
                BarLevelRow(level: levels[i], picked: geo.picked(k, i), dim: false, tone: tone, lines: lines)
                    .frame(width: width, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
                    .background(GeometryReader { g in
                        Color.clear.preference(key: BarRowHeights.self, value: [k * 1000 + i: g.size.height])
                    })
            }
        }
        .opacity(0)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

/// Views placed at points of the stage, each hanging from its point the way its anchor says.
private struct PinnedLayout: Layout {
    let points: [CGPoint]
    let anchors: [UnitPoint]

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        proposal.replacingUnspecifiedDimensions()
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (i, s) in subviews.enumerated() where i < points.count && i < anchors.count {
            s.place(at: CGPoint(x: bounds.minX + points[i].x, y: bounds.minY + points[i].y), anchor: anchors[i], proposal: .unspecified)
        }
    }
}

/// A criterion's short name and points beside its slice: a button (what the keys, the pointer and a screen reader
/// work), riding round with the ring and fading as it sinks.
private struct RingLabels: View {
    let geo: RingGeometry
    let pose: RingPose
    let go: [Double]
    let tones: [RingRGB]
    let motion: RingMotion
    let scale: CGFloat

    var body: some View {
        let spots = geo.labelSpots(pose, bends: go)
        let live = pose.t < 0.3
        PinnedLayout(points: spots.map { CGPoint(x: $0.point.x * scale, y: $0.point.y * scale) }, anchors: spots.map(\.anchor)) {
            ForEach(0..<geo.n, id: \.self) { k in
                label(k, spots[k], live: live)
            }
        }
        .frame(width: CGFloat(RingUnits.width) * scale, height: CGFloat(RingUnits.height) * scale)
        .opacity(pose.ringAlpha * span(pose.bloom, 0.45, 0.9))
        .accessibilityHidden(!live)
    }

    private var nameFont: Font {
        if geo.n <= 6 { return Font.sCallout.weight(.semibold) }
        if geo.n <= 10 { return Font.sFootnote.weight(.semibold) }
        return Font.sCaption.weight(.semibold)
    }

    private var pointsFont: Font {
        geo.n <= 10 ? Font.sCaption.monospacedDigit() : Font.sCaption2.monospacedDigit()
    }

    private func textAlign(_ a: HorizontalAlignment) -> TextAlignment {
        if a == .leading { return .leading }
        if a == .trailing { return .trailing }
        return .center
    }

    private func label(_ k: Int, _ spot: LabelSpot, live: Bool) -> some View {
        let m = geo.model
        let c = m.criteria[k]
        let hot = pose.hov == k && pose.t < 0.02
        return Button {
            motion.select(k)
        } label: {
            VStack(alignment: spot.align, spacing: 1) {
                Text(c.short)
                    .font(nameFont)
                    .foregroundStyle(hot ? tones[k].color : Color.white)
                if m.n <= 15 {
                    Text(m.pointsLabel(c))
                        .font(pointsFont)
                        .foregroundStyle(Color.white.opacity(hot ? 0.84 : 0.62))
                }
            }
            .multilineTextAlignment(textAlign(spot.align))
            .fixedSize()
            .padding(.horizontal, 6)
            .padding(.vertical, 3)
            .contentShape(Rectangle())
            .shadow(color: Color.black.opacity(0.55), radius: 2, y: 1)
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside {
                motion.hover(k)
            } else if motion.hov == k {
                motion.hover(-1)
            }
        }
        .opacity(spot.shown ? 1 : 0)
        .allowsHitTesting(spot.shown && live)
        .help(c.name)
        .accessibilityLabel(m.spoken(c))
        .accessibilityHint("Opens this criterion’s levels and the comment on it")
        .accessibilityHidden(!spot.shown)
    }
}

/// The total in the middle of the ring — or, while the pointer or the keys are on a slice, that criterion.
private struct RingCentre: View {
    let model: RubricModel
    let pose: RingPose
    let tones: [RingRGB]
    let scale: CGFloat

    var body: some View {
        let hot: Int? = pose.hov >= 0 && pose.hov < model.n && pose.t < 0.02 ? pose.hov : nil
        let fade = span(pose.bloom, 0.3, 0.75)
        let size = (1 - 0.12 * pose.q) * (0.9 + 0.1 * fade)
        Group {
            if let k = hot {
                criterion(k)
            } else {
                total
            }
        }
        .multilineTextAlignment(.center)
        .frame(width: 220 * scale)
        .shadow(color: Color.black.opacity(0.5), radius: 3, y: 1)
        .scaleEffect(CGFloat(size))
        .opacity(pose.ringAlpha * fade)
        .position(x: CGFloat(RingUnits.cx) * scale, y: CGFloat(RingUnits.cy) * scale)
        .allowsHitTesting(false)
        .animation(Motion.snappy, value: hot)
    }

    private var big: Font { .system(size: 46 * scale, weight: .bold, design: .rounded).monospacedDigit() }

    private func criterion(_ k: Int) -> some View {
        let c = model.criteria[k]
        return VStack(spacing: 3) {
            Text(c.short)
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(tones[k].color)
                .lineLimit(1)
            Text(RubricModel.num(c.score ?? c.worth))
                .font(big)
                .foregroundStyle(Color.white)
            Text(line(c))
                .font(.sCallout)
                .foregroundStyle(Color.white.opacity(0.66))
                .lineLimit(2)
        }
    }

    private var total: some View {
        VStack(spacing: 3) {
            Text(model.graded ? "SCORE" : "TOTAL")
                .font(.sCaption.weight(.semibold))
                .tracking(0.8)
                .foregroundStyle(Color.white.opacity(0.64))
            Text(RubricModel.num(model.graded ? model.earned : model.possible))
                .font(big)
                .foregroundStyle(Color.white)
            Text(totalLine)
                .font(.sCallout)
                .foregroundStyle(Color.white.opacity(0.66))
            Text("Pick a colour to open it")
                .font(.sCaption)
                .foregroundStyle(Color.white.opacity(0.5))
                .padding(.top, 8)
        }
    }

    private var totalLine: String {
        guard model.graded else { return model.possible == 1 ? "point" : "points" }
        let pct = model.possible > 0 ? Int((model.earned / model.possible * 100).rounded()) : 0
        return "of \(RubricModel.num(model.possible)) · \(pct)%"
    }

    private func line(_ c: RubricModel.Criterion) -> String {
        guard model.graded else { return c.worth == 1 ? "point" : "points" }
        guard c.score != nil else { return "of \(RubricModel.num(c.worth)) · not marked" }
        let level = c.mark >= 0 ? " · \(c.levels[c.mark].label)" : ""
        return "of \(RubricModel.num(c.worth))\(level)"
    }
}

// MARK: - The bar's side

/// Beside the open criterion's bar: the way back (a little ring on top of the bar), which criterion this is with its
/// name, description and the dots to switch, its levels at the height of their points (the one given marked), and the
/// marker's note under them. It comes out from the bar once the bar is nearly straight, row by row.
private struct BarSide: View {
    let geo: RingGeometry
    let pose: RingPose
    let places: [BarPlace]
    let motion: RingMotion
    let scale: CGFloat
    let spun: Double

    private func px(_ v: Double) -> CGFloat { CGFloat(v) * scale }
    private var rowWidth: CGFloat { BarSide.rowWidth(scale) }

    /// How wide the bar's words run, from beside the bar to the stage's right.
    static func rowWidth(_ scale: CGFloat) -> CGFloat {
        CGFloat(RingUnits.width - RingUnits.rowsX - RingUnits.rowsRight) * scale
    }

    var body: some View {
        let k = pose.sel
        let live = pose.u >= 0.8
        let headA = span(pose.u, 0.7, 1)
        let backA = span(pose.u, 0.86, 1)
        let tone = geo.model.colors[k]
        ZStack(alignment: .topLeading) {
            rows(k, tone)
            note(k)
                .opacity(headA)
            BarHead(model: geo.model, k: k, tone: tone, scale: scale) { motion.select($0) }
                .frame(width: rowWidth, alignment: .topLeading)
                .offset(x: px(RingUnits.rowsX), y: px(24) + CGFloat((1 - headA) * 8))
                .opacity(headA)
            MiniRingBack(model: geo.model, k: k, spun: spun, z: scale, tone: tone.color) { motion.toRing() }
                .opacity(backA)
                .scaleEffect(CGFloat(0.6 + 0.4 * backA))
                .position(x: px(RingUnits.miniX), y: px(RingUnits.miniY))
        }
        .frame(width: px(RingUnits.width), height: px(RingUnits.height), alignment: .topLeading)
        .allowsHitTesting(live)
        .accessibilityHidden(!live)
    }

    /// The rows of a criterion's levels; switching along the bar, the old rows fade as the new ones come.
    private func rows(_ k: Int, _ tone: RingRGB) -> some View {
        let levels = geo.levels(k)
        let lines = BarLevelRow.lines(levels.count)
        let marked = geo.model.graded && geo.model.criteria[k].score != nil
        return ZStack(alignment: .topLeading) {
            ForEach(levels.indices.map { k * 1000 + $0 }, id: \.self) { key in
                let i = key % 1000
                let picked = geo.picked(k, i)
                let a = rowAlpha(i)
                BarLevelRow(level: levels[i], picked: picked, dim: marked && !picked, tone: tone, lines: lines)
                    .frame(width: rowWidth, alignment: .leading)
                    .fixedSize(horizontal: false, vertical: true)
                    .opacity(a)
                    .offset(x: CGFloat((1 - a) * -18) * scale)
                    .position(x: px(RingUnits.rowsX) + rowWidth / 2, y: px(i < places.count ? places[i].row : RingUnits.barTop))
                    .transition(.asymmetric(insertion: .identity, removal: .opacity.animation(.easeOut(duration: 0.18))))
            }
        }
    }

    private func rowAlpha(_ i: Int) -> Double {
        let x = Double(i)
        return span(pose.u, 0.62 + 0.06 * x, 0.92 + 0.06 * x) * span(pose.w, 0.25 + 0.08 * x, 0.75 + 0.08 * x)
    }

    @ViewBuilder
    private func note(_ k: Int) -> some View {
        let c = geo.model.criteria[k]
        if !c.comment.isEmpty {
            BarNote(text: c.comment, maxHeight: px(92))
                .frame(width: rowWidth, alignment: .leading)
                .padding(.leading, px(RingUnits.rowsX))
                .padding(.bottom, px(14))
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottomLeading)
        }
    }
}

/// Which criterion is open: its colour, which of how many and its points, its name, its description (More shows the
/// rest), and a dot per criterion to switch to another in place.
private struct BarHead: View {
    let model: RubricModel
    let k: Int
    let tone: RingRGB
    let scale: CGFloat
    let select: (Int) -> Void
    @State private var more = false

    var body: some View {
        let c = model.criteria[k]
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 8) {
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .fill(tone.color)
                        .frame(width: 10, height: 10)
                    Text("Criterion \(k + 1) of \(model.n) · \(model.pointsLabel(c))".uppercased())
                        .font(.sCaption.weight(.semibold))
                        .tracking(0.6)
                        .foregroundStyle(Color.white.opacity(0.64))
                        .lineLimit(1)
                }
                Text(c.name)
                    .font(.sTitle2)
                    .foregroundStyle(Color.white)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityAddTraits(.isHeader)
                if !c.desc.isEmpty { description(c.desc, lines: c.name.count > 44 ? 1 : 2) } // (a long name: a line less, clear of the top level)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            PillFlow(spacing: 2, lineSpacing: 2) {
                ForEach(model.criteria, id: \.index) { other in dot(other) }
            }
            .frame(width: 150 * scale, alignment: .trailing)
        }
        .contentShape(Rectangle()) // (a press on the head is not a press beside the bar)
        .shadow(color: Color.black.opacity(0.6), radius: 6)
        .onChange(of: k) { _, _ in more = false }
    }

    private func description(_ text: String, lines: Int) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(text)
                .font(.sCallout)
                .foregroundStyle(Color.white.opacity(0.8))
                .lineLimit(lines)
                .fixedSize(horizontal: false, vertical: true)
            if text.count > lines * 66 {
                Button {
                    more = true
                } label: {
                    Text("More")
                        .font(.sCallout.weight(.semibold))
                        .underline()
                        .foregroundStyle(Color.white)
                }
                .buttonStyle(.plain)
                .help("The whole description")
                .popover(isPresented: $more, arrowEdge: .bottom) {
                    ScrollView {
                        Text(text)
                            .font(.sBody)
                            .textSelection(.enabled)
                            .padding(16)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .frame(width: 420)
                    .frame(maxHeight: 320)
                }
            }
        }
        .frame(maxWidth: 470 * scale, alignment: .leading)
    }

    /// A dot per criterion in its colour, the open one larger in a ring of its colour; a press switches to it.
    private func dot(_ other: RubricModel.Criterion) -> some View {
        let on = other.index == k
        let n = model.n
        let d: CGFloat = on ? (n > 10 ? 11 : 14) : (n > 15 ? 6 : (n > 10 ? 8 : 11))
        let hit: CGFloat = n > 15 ? 17 : 23
        let colour = model.colors[other.index].color
        return Button {
            select(other.index)
        } label: {
            Circle()
                .fill(colour)
                .frame(width: d, height: d)
                .overlay {
                    if on {
                        Circle()
                            .strokeBorder(colour, lineWidth: 2)
                            .frame(width: d + 8, height: d + 8)
                    }
                }
                .frame(width: hit, height: hit)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(other.name)
        .accessibilityLabel("\(other.name), criterion \(other.index + 1) of \(n)")
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A level of the open criterion: its points in the criterion's colour, its name ("Your mark" on the one given) and
/// what it asks for, on a card of its own beside the bar.
private struct BarLevelRow: View {
    let level: RubricModel.Level
    let picked: Bool
    let dim: Bool
    let tone: RingRGB
    /// How many lines of what it asks for (fewer as the levels crowd the bar; none at all past six).
    let lines: Int

    private static let ground = RingRGB(r: 24, g: 24, b: 27)

    /// Two lines of what each level asks for, one as the levels crowd the bar, none past six.
    static func lines(_ count: Int) -> Int { count <= 4 ? 2 : (count <= 6 ? 1 : 0) }

    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            Text(level.pts.map { RubricModel.num($0) } ?? "–")
                .font(.system(size: 21, weight: .bold, design: .rounded).monospacedDigit())
                .foregroundStyle(dim ? Color.white.opacity(0.6) : tone.color)
                .frame(width: 48, alignment: .leading)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(level.label)
                        .font(.sBody.weight(.semibold))
                        .foregroundStyle(dim ? Color.white.opacity(0.62) : Color.white)
                        .lineLimit(2)
                    Spacer(minLength: 0)
                    if picked { YourMark(colour: tone) }
                }
                if lines > 0 && !level.text.isEmpty {
                    Text(level.text)
                        .font(.sCallout)
                        .foregroundStyle(Color.white.opacity(dim ? 0.55 : 0.78))
                        .lineLimit(lines)
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 9)
            .background(boxFill, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .strokeBorder(picked ? tone.color : Color.white.opacity(0.1), lineWidth: picked ? 1.5 : 1)
            }
            .shadow(color: Color.black.opacity(0.28), radius: 11, y: 8)
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(picked ? .isSelected : [])
    }

    private var boxFill: Color {
        picked ? BarLevelRow.ground.mix(tone, 0.22).color.opacity(0.94) : BarLevelRow.ground.color.opacity(0.88)
    }
}

/// The marker's note on the open criterion, under its levels.
private struct BarNote: View {
    let text: String
    let maxHeight: CGFloat

    var body: some View {
        // (1.2.3: the box is as tall as its words — at most `maxHeight`, then they scroll — and sits at the bottom of its
        // room, so a short note never reaches up into the lowest level)
        ViewThatFits(in: .vertical) {
            box(words)
            box(ScrollView { words })
        }
        .frame(maxHeight: maxHeight, alignment: .bottom)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Comment: \(text)")
    }

    private func box(_ content: some View) -> some View {
        HStack(alignment: .top, spacing: 9) {
            Image(systemName: "text.bubble.fill")
                .font(.sCallout)
                .foregroundStyle(Color.white.opacity(0.6))
                .padding(.top, 2)
                .accessibilityHidden(true)
            content
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(Color(red: 24 / 255, green: 24 / 255, blue: 27 / 255).opacity(0.88), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(Color.white.opacity(0.08), lineWidth: 1)
        }
    }

    private var words: some View {
        Text(text)
            .font(.sCallout)
            .foregroundStyle(Color.white.opacity(0.84))
            .textSelection(.enabled)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: - The way back

/// The way back to the ring: the ring in miniature on top of the bar, the open criterion's slice missing from it where
/// the thread from the bar comes in — the bar is that slice, pulled out. Under the pointer the slice fills back into
/// its place and the little ring swells, as a press does to the real one.
private struct MiniRingBack: View {
    let model: RubricModel
    let k: Int
    let spun: Double
    let z: CGFloat
    let tone: Color
    let action: () -> Void
    @State private var hover = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let r = CGFloat(RingUnits.miniRadius) * z
        let lw = 3.5 * z
        let gap = model.n > 1 ? min(0.09, 2 * Double.pi / Double(model.n) / 5) : 0
        let one = model.n == 1 ? 0.001 : 0
        let a0 = model.starts[k]
        let a1 = model.ends[k]
        let mid = model.mids[k]
        return Button(action: action) {
            ZStack {
                Circle()
                    .stroke(Color.white.opacity(0.13), lineWidth: lw)
                    .frame(width: 2 * r, height: 2 * r)
                ZStack {
                    ForEach(0..<model.n, id: \.self) { i in
                        MiniArc(from: model.starts[i] + gap / 2, to: model.ends[i] - gap / 2, radius: r)
                            .stroke(model.colors[i].color, lineWidth: lw)
                            .opacity(i == k ? 0 : 1)
                    }
                    MiniArc(from: mid, to: a1 - gap / 2 - one, radius: r)
                        .trim(from: 0, to: hover ? 1 : 0)
                        .stroke(tone, lineWidth: lw)
                    MiniArc(from: mid, to: a0 + gap / 2 + one, radius: r)
                        .trim(from: 0, to: hover ? 1 : 0)
                        .stroke(tone, lineWidth: lw)
                }
                .rotationEffect(.degrees(spun))
                .animation(reduceMotion ? nil : Animation.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.6), value: spun)
                MiniChevron()
                    .stroke(Color.white, style: StrokeStyle(lineWidth: 2.2 * z, lineCap: .round, lineJoin: .round))
                    .offset(x: hover && !reduceMotion ? -1.5 * z : 0)
            }
            .frame(width: 44 * z, height: 44 * z)
            .scaleEffect(hover && !reduceMotion ? 1.1 : 1)
            .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .animation(reduceMotion ? nil : Animation.timingCurve(0.2, 0.8, 0.2, 1, duration: 0.5), value: hover)
        .help("Back to the ring (Esc)")
        .accessibilityLabel("Back to the ring")
    }
}

/// An arc of the little ring, between two angles clockwise from twelve o'clock (anticlockwise when `to` is before
/// `from`), drawn from `from`.
private struct MiniArc: Shape {
    let from: Double
    let to: Double
    let radius: CGFloat

    func path(in rect: CGRect) -> Path {
        var p = Path()
        // (SwiftUI's angles run from three o'clock; with y down, "not clockwise" is clockwise on screen)
        p.addArc(center: CGPoint(x: rect.midX, y: rect.midY), radius: radius,
                 startAngle: .radians(from - Double.pi / 2), endAngle: .radians(to - Double.pi / 2), clockwise: to < from)
        return p
    }
}

/// The little ring's chevron, pointing back.
private struct MiniChevron: Shape {
    func path(in rect: CGRect) -> Path {
        let s = rect.width / 44
        let c = CGPoint(x: rect.midX, y: rect.midY)
        var p = Path()
        p.move(to: CGPoint(x: c.x + 1.6 * s, y: c.y - 5 * s))
        p.addLine(to: CGPoint(x: c.x - 3.4 * s, y: c.y))
        p.addLine(to: CGPoint(x: c.x + 1.6 * s, y: c.y + 5 * s))
        return p
    }
}
