import SwiftUI

// Cell Division's drawing (spec §4.6, §9): a SwiftUI Canvas — crisp, and redrawn every frame while a step is moving —
// that strokes what DivPainter lists for the frame between two keyframes (600 ms, eased; under Reduce Motion the two
// keyframes crossfade in 200 ms). One cell unit is 0.42 × the view's shorter side (spec §4.6), less when the cells of a
// step need more room or the labels need their columns, and the view eases from one to the other as cells divide.
// Labels sit in two columns with leader lines (§3.5, BioLabelLayout), each pressable for its part's card; a click on a
// chromosome picks it (both its chromatids outlined) for its own card; badges name each new cell's ploidy.

/// The colours of the drawing's inks, light and dark (the chromosomes are §3.8's maternal and paternal).
enum DivColors {
    static func ink(_ i: DivInk, dark: Bool) -> (hex: String, alpha: Double) {
        switch i {
        case .cytoplasm: return ("#14b8a6", dark ? 0.12 : 0.09)
        case .membrane: return (dark ? "#f5f5f7" : "#1d1d1f", 0.55)
        case .wall: return (dark ? "#8bbf3f" : "#65a30d", 0.45)
        case .nucleoplasm: return (dark ? "#aeaeb2" : "#6e6e73", dark ? 0.08 : 0.07)
        case .envelope: return (dark ? "#f5f5f7" : "#1d1d1f", 0.45)
        case .nucleolus: return (dark ? "#aeaeb2" : "#6e6e73", 0.35)
        case .furrow: return (dark ? "#ff7aa8" : "#e8508a", 0.9)
        case .cellPlate: return (dark ? "#ffd60a" : "#c79a00", 0.9)
        case .plate: return (dark ? "#aeaeb2" : "#8e8e93", 0.9)
        case .microtubule, .aster: return (dark ? "#4fd1a5" : "#2e9e78", dark ? 0.7 : 0.75)
        case .centrosome: return (dark ? "#ffd60a" : "#d9a406", 1)
        case .centriole: return (dark ? "#ffd60a" : "#9a6b00", 1)
        case .maternal: return (BioPalette.maternal.hex(dark: dark), 1)
        case .paternal: return (BioPalette.paternal.hex(dark: dark), 1)
        case .edge: return ("#000000", dark ? 0.5 : 0.3)
        case .centromere: return (dark ? "#f5f5f7" : "#1d1d1f", dark ? 0.9 : 0.85)
        }
    }

    static func color(_ i: DivInk, dark: Bool, alpha: Double = 1) -> Color {
        let c = ink(i, dark: dark)
        return Color(hex: c.hex).opacity(c.alpha * alpha)
    }

    /// Part way from one ink's colour to another's (a recombinant part, as crossing over recolours it).
    static func mixed(_ a: DivInk, _ b: DivInk, _ t: Double, dark: Bool) -> Color {
        let x = BioPalette.rgb(ink(a, dark: dark).hex), y = BioPalette.rgb(ink(b, dark: dark).hex)
        return Color(red: x.r + (y.r - x.r) * t, green: x.g + (y.g - x.g) * t, blue: x.b + (y.b - x.b) * t)
    }
}

/// How cell units map to the view: where the frame's middle goes and how many points a unit is.
struct DivViewMap {
    let size: CGSize
    let centre: DivPoint
    let scale: CGFloat

    /// 0.42 × the shorter side (spec §4.6), or less when the frame's cells, or the label columns, need the room.
    init(size: CGSize, bounds: DivRect, labels: Bool) {
        self.size = size
        let side: CGFloat = labels ? min(190, size.width * 0.24) : 14
        let w = max(size.width - 2 * side, 60), h = max(size.height - 24, 60)
        scale = max(min(0.42 * min(size.width, size.height), w / CGFloat(max(bounds.width, 0.1)), h / CGFloat(max(bounds.height, 0.1))), 1)
        centre = bounds.centre
    }

    func point(_ p: DivPoint) -> CGPoint {
        CGPoint(x: size.width / 2 + CGFloat(p.x - centre.x) * scale, y: size.height / 2 - CGFloat(p.y - centre.y) * scale)
    }

    func cell(_ p: CGPoint) -> DivPoint {
        DivPoint(centre.x + Double((p.x - size.width / 2) / scale), centre.y - Double((p.y - size.height / 2) / scale))
    }
}

/// The drawing: a sequence's keyframes, at the step its controller is on (or between two), mapped from the
/// controller's steps by `keyframe` (the Compare page's steps pair the two processes' steps).
struct DivisionCanvas: View {
    let sequence: DivisionSequence
    @ObservedObject var steps: BioStepController
    var keyframe: (Int) -> Int = { $0 }
    var labels = true
    @Binding var picked: Int?
    /// A label pressed: its part.
    var onPart: (String) -> Void = { _ in }
    @Environment(\.colorScheme) private var scheme
    @State private var hovered: String?

    var body: some View {
        GeometryReader { geo in
            TimelineView(.animation(paused: !steps.moving)) { ctx in
                let ph = steps.phase(at: ctx.date)
                stage(ph, size: geo.size)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(description)
    }

    /// The keyframe indices a phase is between.
    private func frames(_ ph: BioStepPhase) -> (from: Int, to: Int) {
        let n = sequence.keyframes.count
        return (min(max(keyframe(ph.from), 0), n - 1), min(max(keyframe(ph.to), 0), n - 1))
    }

    @ViewBuilder
    private func stage(_ ph: BioStepPhase, size: CGSize) -> some View {
        let idx = frames(ph)
        let dark = scheme == .dark
        let current = ph.t < 0.5 ? idx.from : idx.to
        // (what is shown: the frame part way between the two, or under Reduce Motion both, crossfading)
        let shown: DivisionFrame = ph.crossfade ? sequence.frames[current] : sequence.frames[idx.from].mixed(sequence.frames[idx.to], ph.t)
        let map = DivViewMap(size: size, bounds: shown.bounds, labels: labels)
        let painted = DivPainter.paint(shown, chiasma: sequence.options.chiasma)
        let selected = selection(in: shown)
        ZStack(alignment: .topLeading) {
            Canvas { gc, _ in
                if ph.crossfade && idx.from != idx.to {
                    for (i, a) in [(idx.from, 1 - ph.t), (idx.to, ph.t)] where a > 0.001 {
                        let f = sequence.frames[i]
                        let m = DivViewMap(size: size, bounds: f.bounds, labels: labels)
                        DivisionCanvas.draw(DivPainter.paint(f, chiasma: sequence.options.chiasma, fade: a).primitives, in: gc, map: m, dark: dark,
                                            selected: i == current ? selected : [])
                    }
                } else {
                    DivisionCanvas.draw(painted.primitives, in: gc, map: map, dark: dark, selected: selected)
                }
                if let h = hovered, let l = sequence.keyframes[current].labels.first(where: { $0.id == h }) {
                    let a = DivPainter.anchor(l.anchor, shown, painted.shapes)
                    let p = map.point(a.point)
                    gc.stroke(Path(ellipseIn: CGRect(x: p.x - 9, y: p.y - 9, width: 18, height: 18)), with: .color(Theme.accent), lineWidth: 2)
                }
            }
            if labels {
                DivisionLabelsOverlay(placements: placements(current, shown, painted.shapes, map), hovered: $hovered) { id in
                    if let l = sequence.keyframes[current].labels.first(where: { $0.id == id }) { onPart(l.part) }
                }
            }
            badges(current, map: map, visible: !ph.isMoving || ph.t > 0.85)
        }
        .contentShape(Rectangle())
        .gesture(SpatialTapGesture().onEnded { value in
            let location = value.location
            let rest = sequence.frames[current]
            let m = DivViewMap(size: size, bounds: rest.bounds, labels: labels)
            let shapes = rest.shapes(chiasma: sequence.options.chiasma)
            let hit = DivPainter.hit(m.cell(location), rest, shapes, tolerance: Double(6 / m.scale))
            withAnimation(Motion.snappy) { picked = hit == picked ? nil : hit }
        })
    }

    /// The picked chromatid and, while they are joined, its sister: outlined together, as one chromosome.
    private func selection(in f: DivisionFrame) -> Set<Int> {
        guard let p = picked, f.chromatids.indices.contains(p) else { return [] }
        var out: Set<Int> = [p]
        let c = f.chromatids[p]
        if c.joined {
            let sister = DivChromatidID(pair: c.id.pair, homolog: c.id.homolog, sister: 1 - c.id.sister).index
            if f.chromatids.indices.contains(sister), f.chromatids[sister].joined { out.insert(sister) }
        }
        return out
    }

    private func placements(_ step: Int, _ f: DivisionFrame, _ shapes: [DivChromatidShape], _ map: DivViewMap) -> (placed: [BioLabelPlacement], more: Int) {
        let inputs: [BioLabelInput] = sequence.keyframes[step].labels.map { l in
            let a = DivPainter.anchor(l.anchor, f, shapes)
            let p = map.point(a.point)
            return BioLabelInput(id: l.id, text: l.text, x: Double(p.x), y: Double(p.y), visible: a.visible, area: 0,
                                 width: BioLabelLayout.estimatedWidth(l.text))
        }
        let r = Double(map.scale) * f.bounds.width / 2
        return BioLabelLayout.layout(inputs, cx: Double(map.size.width / 2), cy: Double(map.size.height / 2), r: r,
                                     width: Double(map.size.width), height: Double(map.size.height))
    }

    @ViewBuilder
    private func badges(_ step: Int, map: DivViewMap, visible: Bool) -> some View {
        ForEach(Array(sequence.keyframes[step].badges.enumerated()), id: \.offset) { _, b in
            let text = sequence.badge(step: step, cell: b.cell)
            Text(text)
                .font(.system(size: 13, weight: .bold).monospacedDigit())
                .foregroundStyle(text.contains("+") || text.contains("−") ? Color.orange : Theme.accent)
                .padding(.horizontal, 9)
                .padding(.vertical, 3)
                .background(.regularMaterial, in: Capsule())
                .position(map.point(b.at))
                .opacity(visible ? 1 : 0)
                .accessibilityLabel("A cell with \(text)")
                .allowsHitTesting(false)
        }
    }

    /// What the drawing shows, for VoiceOver.
    private var description: String {
        let i = min(max(keyframe(steps.index), 0), sequence.stages.count - 1)
        let s = sequence.stages[i], c = sequence.counts[i]
        return "\(sequence.process.title), \(s.title), \(sequence.options.plant ? "plant" : "animal") cell. Chromosomes: \(c.chromosomes). DNA molecules: \(c.chromatids). Ploidy \(c.ploidy), DNA \(c.dna). The mother's chromosomes are red, the father's blue."
    }

    // MARK: Drawing

    static func path(_ pts: [DivPoint], _ map: DivViewMap) -> Path {
        var p = Path()
        guard let first = pts.first else { return p }
        p.move(to: map.point(first))
        for q in pts.dropFirst() { p.addLine(to: map.point(q)) }
        return p
    }

    static func draw(_ prims: [DivPrimitive], in gc: GraphicsContext, map: DivViewMap, dark: Bool, selected: Set<Int>) {
        let round = StrokeStyle(lineWidth: 1, lineCap: .round, lineJoin: .round)
        for prim in prims {
            switch prim {
            case .fill(let polys, let ink, let alpha):
                var p = Path()
                for poly in polys {
                    p.addPath(path(poly, map))
                    p.closeSubpath()
                }
                gc.fill(p, with: .color(DivColors.color(ink, dark: dark, alpha: alpha)))
            case .line(let subs, let ink, let width, let alpha, let dashed):
                var p = Path()
                for s in subs { p.addPath(path(s, map)) }
                var style = round
                style.lineWidth = width
                if dashed { style.dash = [5, 4] }
                gc.stroke(p, with: .color(DivColors.color(ink, dark: dark, alpha: alpha)), style: style)
            case .band(let subs, let ink, let width, let alpha):
                var p = Path()
                for s in subs { p.addPath(path(s, map)) }
                var style = round
                style.lineWidth = max(CGFloat(width) * map.scale, 0.8)
                gc.stroke(p, with: .color(DivColors.color(ink, dark: dark, alpha: alpha)), style: style)
            case .disc(let c, let r, let ink, let alpha):
                let p = map.point(c), rr = max(CGFloat(r) * map.scale, 0.8)
                gc.fill(Path(ellipseIn: CGRect(x: p.x - rr, y: p.y - rr, width: 2 * rr, height: 2 * rr)),
                        with: .color(DivColors.color(ink, dark: dark, alpha: alpha)))
            case .chromatid(let c):
                if c.alpha < 0.999 {
                    var layer = gc
                    layer.opacity = c.alpha
                    layer.drawLayer { l in chromatid(c, in: l, map: map, dark: dark, selected: selected.contains(c.index)) }
                } else {
                    chromatid(c, in: gc, map: map, dark: dark, selected: selected.contains(c.index))
                }
            }
        }
    }

    /// A chromatid: (picked: an accent outline), a dark edge, its colours in runs, and a soft sheen as it condenses.
    static func chromatid(_ c: DivChromatidPaint, in gc: GraphicsContext, map: DivViewMap, dark: Bool, selected: Bool) {
        let whole = path(c.points, map)
        let w = max(CGFloat(c.width) * map.scale, 1.2)
        var style = StrokeStyle(lineWidth: w, lineCap: .round, lineJoin: .round)
        if selected {
            style.lineWidth = w + 7
            gc.stroke(whole, with: .color(Theme.accent.opacity(0.9)), style: style)
        }
        style.lineWidth = w + 1.4
        gc.stroke(whole, with: .color(DivColors.color(.edge, dark: dark)), style: style)
        style.lineWidth = w
        for r in c.runs {
            let pts = Array(c.points[max(r.from, 0)...min(r.to, c.points.count - 1)])
            let colour = r.mix > 0 ? DivColors.mixed(r.ink, r.other, r.mix, dark: dark) : DivColors.color(r.ink, dark: dark)
            gc.stroke(path(pts, map), with: .color(colour), style: style)
        }
        if c.k > 0.3 {
            style.lineWidth = max(w * 0.28, 1)
            gc.stroke(whole, with: .color(.white.opacity(0.22 * c.k)), style: style)
        }
    }
}

/// The drawing's labels (spec §3.5): leader lines, then a capsule a press opens the part's card from; hovering one
/// rings its anchor. They fade as they come and go (not under Reduce Motion).
struct DivisionLabelsOverlay: View {
    let placements: (placed: [BioLabelPlacement], more: Int)
    @Binding var hovered: String?
    let pick: (String) -> Void
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack(alignment: .topLeading) {
            Canvas { gc, _ in
                for l in placements.placed {
                    var path = Path()
                    path.move(to: CGPoint(x: l.anchorX, y: l.anchorY))
                    path.addLine(to: CGPoint(x: l.elbowX, y: l.y))
                    path.addLine(to: CGPoint(x: l.edgeX, y: l.y))
                    gc.stroke(path, with: .color(.primary.opacity(0.55)), lineWidth: 1)
                    gc.fill(Path(ellipseIn: CGRect(x: l.anchorX - 1.5, y: l.anchorY - 1.5, width: 3, height: 3)), with: .color(.primary.opacity(0.8)))
                }
            }
            .allowsHitTesting(false)
            ForEach(placements.placed, id: \.id) { l in
                let f = l.frame
                Button { pick(l.id) } label: {
                    Text(l.text)
                        .font(.system(size: 12, weight: .semibold))
                        .lineLimit(1)
                        .padding(.horizontal, 10)
                        .frame(width: f.width, height: f.height)
                        .background(.regularMaterial, in: Capsule())
                        .overlay(Capsule().strokeBorder(hovered == l.id ? Theme.accent : Color.primary.opacity(0.12), lineWidth: 1))
                }
                .buttonStyle(.plain)
                .onHover { inside in hovered = inside ? l.id : (hovered == l.id ? nil : hovered) }
                .position(x: f.x + f.width / 2, y: l.y)
                .transition(.opacity)
                .help("\(l.text): what it is")
                .accessibilityHint("Shows what it is")
            }
        }
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.12), value: placements.placed.map(\.id))
    }
}
