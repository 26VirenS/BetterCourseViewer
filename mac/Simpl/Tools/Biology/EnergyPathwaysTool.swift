import SwiftUI

// Photosynthesis & Respiration (spec §4.10, §5.8): a 2D map of a plant cell's chloroplast and mitochondrion with the
// cytosol between them. Four pages:
// - Map: press a stage (light reactions, Calvin cycle, glycolysis, pyruvate oxidation, citric acid cycle, oxidative
//   phosphorylation, and fermentation as a branch) to light where it happens and read what goes in and out and its
//   counts per glucose; "Plants respire too" shows the cell in the light and in the dark side by side.
// - Respiration: one glucose step by step, with running totals of ATP, NADH, FADH₂ and CO₂ (or fermenting without O₂).
// - Photosynthesis: one turn of the light reactions and the Calvin cycle, to one G3P.
// - Follow the atoms: the O in O₂ back to water (not CO₂), and the C in glucose from CO₂ and back to CO₂.
// The data and the running totals are Model/EnergyPathwaysModel.swift's; the step-throughs use the shared controller.
//
// Launch: -SimplTool energy -SimplToolState map[:stage=<id>][:respire] | respiration[:step=<n>][:ferment=lactate|ethanol]
// | photosynthesis[:step=<n>] | atoms[:trace=oxygen|carbon][:step=<n>].

struct EnergyPathwaysTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.energy)`.
    static let pages: [BioPage] = [
        BioPage(id: "map", title: "Map", symbol: "map", help: "Where each stage happens, and what goes in and out"),
        BioPage(id: "respiration", title: "Respiration", symbol: "flame", help: "Follow one glucose through cellular respiration"),
        BioPage(id: "photosynthesis", title: "Photosynthesis", symbol: "sun.max", help: "One turn of the light reactions and the Calvin cycle, to one G3P"),
        BioPage(id: "atoms", title: "Follow the atoms", symbol: "atom", help: "The O in O₂ and the C in glucose, atom by atom"),
    ]

    @ObservedObject private var nav = BioNav.shared

    var body: some View {
        switch nav.page(.energy) {
        case "respiration": EnergyRespirationPage(state: state)
        case "photosynthesis": EnergyPhotosynthesisPage(state: state)
        case "atoms": EnergyAtomsPage(state: state)
        default: EnergyMapPage(state: state)
        }
    }
}

/// A launch's state, for the page it names (the map if it names none), the first time that page appears.
@MainActor
enum EnergyLaunch {
    private static var done: Set<String> = []

    static func take(_ page: String, _ state: BioLaunchState?) -> BioLaunchState? {
        guard let state, !done.contains(page), (state.page ?? "map") == page else { return nil }
        done.insert(page)
        return state
    }
}

// MARK: - Colours and text

/// The map's colours (light and dark): the cytosol, the chloroplast's greens, the mitochondrion's oranges, and grey for
/// an organelle idle in the dark. Each place is also named, never shown by colour alone.
enum EnergyColors {
    static let cytosol = BioHexPair("#f6f2e4", "#2a2822")
    static let wall = BioHexPair("#8bbf3f", "#6f9a35")
    static let stroma = BioHexPair("#dcefc9", "#2c4322")
    static let chloroMembrane = BioHexPair("#4d8a2a", "#8bd16a")
    static let thylakoid = BioHexPair("#5aa13a", "#5fb83f")
    static let thylakoidEdge = BioHexPair("#3c7a22", "#9be27a")
    static let matrix = BioHexPair("#fbe0cc", "#4a3022")
    static let intermembrane = BioHexPair("#fff5ed", "#33261e")
    static let mitoMembrane = BioHexPair("#c8662f", "#ff9a5c")
    static let idle = BioHexPair("#e2e2e6", "#38383c")
    static let idleLine = BioHexPair("#a2a2a8", "#6c6c72")
    static let sun = BioHexPair("#e0a000", "#ffd60a")

    /// A place's swatch in the lists and cards.
    static func swatch(_ p: EnergyPlace) -> BioHexPair {
        switch p {
        case .thylakoid: return BioHexPair("#5aa13a", "#7ed957")
        case .stroma: return BioHexPair("#2e9e78", "#4fd1a5")
        case .cytosol: return BioHexPair("#c79a00", "#ffd60a")
        case .matrix: return BioHexPair("#e07800", "#ffa133")
        case .innerMembrane: return BioHexPair("#d63a34", "#ff6961")
        }
    }
}

enum EnergyText {
    /// Text with **bold** words (the model's emphasis).
    static func rich(_ s: String) -> Text {
        if let a = try? AttributedString(markdown: s, options: AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)) {
            return Text(a)
        }
        return Text(s)
    }

    /// A formula with the followed atom in [ ] drawn bold, underlined, in the accent ("H₂[O]").
    static func formula(_ f: String) -> Text {
        var out = Text("")
        var rest = Substring(f)
        while let open = rest.firstIndex(of: "["), let close = rest[open...].firstIndex(of: "]") {
            out = out + Text(String(rest[..<open]))
            out = out + Text(String(rest[rest.index(after: open)..<close])).bold().underline().foregroundColor(Theme.accent)
            rest = rest[rest.index(after: close)...]
        }
        return out + Text(String(rest))
    }

    /// The formula without its marks ("H₂O").
    static func plain(_ f: String) -> String { f.replacingOccurrences(of: "[", with: "").replacingOccurrences(of: "]", with: "") }
}

/// A labelled fact of a card, its words allowed **bold**.
struct EnergyFact: View {
    let label: String
    let text: String
    var tint: Color? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(tint ?? .secondary)
            EnergyText.rich(text).font(.sCallout).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityElement(children: .combine)
    }
}

// MARK: - The map

/// Something moving on the map: a label in a capsule (what is being followed) at a point of the design space.
struct EnergyToken {
    var at: CGPoint
    var label: String
    var opacity: Double = 1
}

/// An arrow of the map: what moves between the places (light, water, O₂, glucose, pyruvate, CO₂, ATP).
struct EnergyFlow {
    let id: String
    let label: String
    let start: CGPoint
    let control: CGPoint
    let end: CGPoint
    let labelAt: CGPoint
    /// Shown only while the chloroplast works.
    let needsLight: Bool
}

/// The map's design space (1100 × 560), scaled to fit a view; the organelles' shapes and the places' anchors.
struct EnergyMapLayout {
    static let width: CGFloat = 1100
    static let height: CGFloat = 560

    let scale: CGFloat
    let origin: CGPoint

    init(_ size: CGSize) {
        scale = max(min(size.width / Self.width, size.height / Self.height), 0.01)
        origin = CGPoint(x: (size.width - Self.width * scale) / 2, y: (size.height - Self.height * scale) / 2)
    }

    func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: origin.x + x * scale, y: origin.y + y * scale) }
    func p(_ q: CGPoint) -> CGPoint { p(q.x, q.y) }
    func s(_ v: CGFloat) -> CGFloat { v * scale }
    func rect(_ r: CGRect) -> CGRect { CGRect(origin: p(r.origin), size: CGSize(width: s(r.width), height: s(r.height))) }
    func ellipse(_ cx: CGFloat, _ cy: CGFloat, _ rx: CGFloat, _ ry: CGFloat) -> Path {
        Path(ellipseIn: rect(CGRect(x: cx - rx, y: cy - ry, width: 2 * rx, height: 2 * ry)))
    }
    func path(_ points: [CGPoint]) -> Path {
        var out = Path()
        out.addLines(points.map { p($0) })
        out.closeSubpath()
        return out
    }

    // (the chloroplast and the mitochondrion: centres and radii)
    static let chloroplast = (cx: CGFloat(250), cy: CGFloat(290), rx: CGFloat(192), ry: CGFloat(140))
    static let mitochondrion = (cx: CGFloat(850), cy: CGFloat(300), rx: CGFloat(182), ry: CGFloat(112))

    /// The grana: four stacks of five thylakoid discs.
    static let grana: [CGPoint] = [CGPoint(x: 150, y: 300), CGPoint(x: 222, y: 252), CGPoint(x: 296, y: 318), CGPoint(x: 360, y: 266)]
    static let discs: [CGRect] = grana.flatMap { g in
        (0..<5).map { k in CGRect(x: g.x - 27, y: g.y - 5 + CGFloat(k - 2) * 13, width: 54, height: 10) }
    }

    /// The inner mitochondrial membrane: an ellipse folded into cristae, from the top and from the bottom in turn.
    static let innerMembrane: [CGPoint] = {
        let cx = 850.0, cy = 300.0, rx = 168.0, ry = 98.0
        let depth = 56.0, half = 7.0
        func edge(_ dx: Double, _ sign: Double) -> Double { cy + sign * ry * max(0, 1 - (dx / rx) * (dx / rx)).squareRoot() }
        var points: [CGPoint] = []
        func run(from: Double, to: Double, sign: Double, fingers: [Double]) {
            let forward = to > from
            let step = forward ? 4.0 : -4.0
            var pending = fingers.sorted { forward ? $0 < $1 : $0 > $1 }
            var dx = from
            while forward ? dx <= to : dx >= to {
                if let f = pending.first, forward ? dx >= f - half : dx <= f + half {
                    let a = forward ? f - half : f + half, b = forward ? f + half : f - half
                    let base = edge(f, sign) - sign * depth
                    points.append(CGPoint(x: cx + a, y: edge(a, sign)))
                    points.append(CGPoint(x: cx + a, y: base))
                    for k in 1..<8 {
                        let t = forward ? Double.pi - Double.pi * Double(k) / 8 : Double.pi * Double(k) / 8
                        points.append(CGPoint(x: cx + f + half * cos(t), y: base - sign * half * sin(t)))
                    }
                    points.append(CGPoint(x: cx + b, y: base))
                    points.append(CGPoint(x: cx + b, y: edge(b, sign)))
                    pending.removeFirst()
                    dx = b + step
                    continue
                }
                points.append(CGPoint(x: cx + dx, y: edge(dx, sign)))
                dx += step
            }
        }
        run(from: -rx, to: rx, sign: -1, fingers: [-112, -48, 16, 80])
        run(from: rx, to: -rx, sign: 1, fingers: [-80, -16, 48, 112])
        return points
    }()

    /// Where a place's things are drawn (and the token stops).
    static func anchor(_ place: EnergyPlace) -> CGPoint {
        switch place {
        case .thylakoid: return CGPoint(x: 222, y: 252)
        case .stroma: return CGPoint(x: 300, y: 392)
        case .cytosol: return CGPoint(x: 555, y: 315)
        case .matrix: return CGPoint(x: 860, y: 300)
        case .innerMembrane: return CGPoint(x: 930, y: 268)
        }
    }

    /// Each stage's pressable name on the map.
    static let pills: [(id: String, name: String, at: CGPoint)] = [
        ("light", "Light reactions", CGPoint(x: 215, y: 196)),
        ("calvin", "Calvin cycle", CGPoint(x: 300, y: 392)),
        ("glycolysis", "Glycolysis", CGPoint(x: 555, y: 315)),
        ("fermentation", "Fermentation", CGPoint(x: 555, y: 430)),
        ("pyruvate", "Pyruvate oxidation", CGPoint(x: 760, y: 244)),
        ("krebs", "Krebs cycle", CGPoint(x: 870, y: 300)),
        ("oxphos", "ETC + ATP synthase", CGPoint(x: 850, y: 440)),
    ]

    static let flows: [EnergyFlow] = [
        EnergyFlow(id: "light", label: "Light", start: CGPoint(x: 78, y: 62), control: CGPoint(x: 100, y: 120), end: CGPoint(x: 132, y: 176),
                   labelAt: CGPoint(x: 104, y: 52), needsLight: true),
        EnergyFlow(id: "water", label: "H₂O", start: CGPoint(x: 60, y: 512), control: CGPoint(x: 86, y: 470), end: CGPoint(x: 132, y: 410),
                   labelAt: CGPoint(x: 100, y: 512), needsLight: true),
        EnergyFlow(id: "oxygen", label: "O₂", start: CGPoint(x: 395, y: 190), control: CGPoint(x: 555, y: 104), end: CGPoint(x: 712, y: 214),
                   labelAt: CGPoint(x: 555, y: 136), needsLight: true),
        EnergyFlow(id: "sugar", label: "Glucose", start: CGPoint(x: 444, y: 300), control: CGPoint(x: 470, y: 286), end: CGPoint(x: 498, y: 304),
                   labelAt: CGPoint(x: 470, y: 268), needsLight: true),
        EnergyFlow(id: "pyruvate", label: "Pyruvate", start: CGPoint(x: 612, y: 322), control: CGPoint(x: 640, y: 338), end: CGPoint(x: 670, y: 316),
                   labelAt: CGPoint(x: 640, y: 358), needsLight: false),
        EnergyFlow(id: "carbonDioxide", label: "CO₂", start: CGPoint(x: 760, y: 404), control: CGPoint(x: 555, y: 548), end: CGPoint(x: 352, y: 414),
                   labelAt: CGPoint(x: 555, y: 492), needsLight: false),
        EnergyFlow(id: "atp", label: "ATP", start: CGPoint(x: 992, y: 362), control: CGPoint(x: 1030, y: 396), end: CGPoint(x: 1046, y: 436),
                   labelAt: CGPoint(x: 1046, y: 456), needsLight: false),
    ]
}

/// The map drawn: the cell, the chloroplast (stroma and grana), the cytosol, the mitochondrion (matrix and folded inner
/// membrane), the arrows between them, the place lit, and anything being followed.
struct EnergyMapPainter {
    let layout: EnergyMapLayout
    let dark: Bool
    var highlight: EnergyPlace?
    var lit: Set<String>
    var chloroplastActive = true
    var tokens: [EnergyToken] = []

    private func c(_ pair: BioHexPair) -> Color { Color(hex: pair.hex(dark: dark)) }
    private var ink: Color { dark ? .white : .black }

    func paint(_ ctx: inout GraphicsContext) {
        let L = layout
        let accent = Theme.accent

        // (the cell: wall, membrane, cytosol)
        let cell = Path(roundedRect: L.rect(CGRect(x: 16, y: 16, width: 1068, height: 528)), cornerRadius: L.s(70))
        ctx.fill(cell, with: .color(c(EnergyColors.cytosol)))
        if highlight == .cytosol { ctx.fill(cell, with: .color(accent.opacity(0.14))) }
        ctx.stroke(cell, with: .color(c(EnergyColors.wall)), lineWidth: max(L.s(10), 2))
        let membrane = Path(roundedRect: L.rect(CGRect(x: 26, y: 26, width: 1048, height: 508)), cornerRadius: L.s(61))
        ctx.stroke(membrane, with: .color(c(EnergyColors.wall).opacity(0.8)), lineWidth: max(L.s(2), 1))
        text(&ctx, "Cytosol", CGPoint(x: 555, y: 226), size: 12, weight: .semibold, colour: highlight == .cytosol ? accent : ink.opacity(0.55))

        // (the chloroplast)
        let ch = EnergyMapLayout.chloroplast
        let on = chloroplastActive
        let outer = L.ellipse(ch.cx, ch.cy, ch.rx, ch.ry), inner = L.ellipse(ch.cx, ch.cy, ch.rx - 9, ch.ry - 9)
        let line = on ? c(EnergyColors.chloroMembrane) : c(EnergyColors.idleLine)
        ctx.fill(outer, with: .color(on ? c(EnergyColors.stroma) : c(EnergyColors.idle)))
        ctx.stroke(outer, with: .color(line), lineWidth: max(L.s(3), 1))
        if highlight == .stroma { ctx.fill(inner, with: .color(accent.opacity(0.22))) }
        ctx.stroke(inner, with: .color(line.opacity(0.8)), lineWidth: max(L.s(2), 1))
        var lamellae = Path()
        for (a, b) in zip(EnergyMapLayout.grana, EnergyMapLayout.grana.dropFirst()) {
            lamellae.move(to: L.p(a))
            lamellae.addLine(to: L.p(b))
        }
        ctx.stroke(lamellae, with: .color(on ? c(EnergyColors.thylakoid) : c(EnergyColors.idleLine)), lineWidth: max(L.s(3), 1))
        for d in EnergyMapLayout.discs {
            let disc = Path(roundedRect: L.rect(d), cornerRadius: L.s(4))
            ctx.fill(disc, with: .color(on ? c(EnergyColors.thylakoid) : c(EnergyColors.idleLine)))
            if highlight == .thylakoid { ctx.fill(disc, with: .color(accent.opacity(0.55))) }
            ctx.stroke(disc, with: .color(highlight == .thylakoid ? accent : (on ? c(EnergyColors.thylakoidEdge) : c(EnergyColors.idleLine))),
                       lineWidth: max(L.s(highlight == .thylakoid ? 2 : 1), 0.5))
        }
        text(&ctx, "Chloroplast", CGPoint(x: ch.cx, y: ch.cy - ch.ry - 16), size: 13, weight: .semibold, colour: ink.opacity(0.75))
        if !on { text(&ctx, "Idle in the dark", CGPoint(x: ch.cx, y: ch.cy + 70), size: 13, weight: .bold, colour: ink.opacity(0.6)) }

        // (the mitochondrion)
        let mi = EnergyMapLayout.mitochondrion
        let mOuter = L.ellipse(mi.cx, mi.cy, mi.rx, mi.ry)
        let mInner = L.path(EnergyMapLayout.innerMembrane)
        ctx.fill(mOuter, with: .color(c(EnergyColors.intermembrane)))
        ctx.stroke(mOuter, with: .color(c(EnergyColors.mitoMembrane)), lineWidth: max(L.s(3), 1))
        ctx.fill(mInner, with: .color(c(EnergyColors.matrix)))
        if highlight == .matrix { ctx.fill(mInner, with: .color(accent.opacity(0.22))) }
        ctx.stroke(mInner, with: .color(highlight == .innerMembrane ? accent : c(EnergyColors.mitoMembrane)),
                   lineWidth: max(L.s(highlight == .innerMembrane ? 6 : 2.5), 1))
        text(&ctx, "Mitochondrion", CGPoint(x: mi.cx, y: mi.cy - mi.ry - 16), size: 13, weight: .semibold, colour: ink.opacity(0.75))

        // (the arrows)
        for f in EnergyMapLayout.flows where on || !f.needsLight {
            let lit = self.lit.contains(f.id)
            let colour = f.id == "light" ? c(EnergyColors.sun).opacity(lit ? 1 : 0.5) : ink.opacity(lit ? 0.85 : 0.3)
            var arc = Path()
            arc.move(to: L.p(f.start))
            arc.addQuadCurve(to: L.p(f.end), control: L.p(f.control))
            ctx.stroke(arc, with: .color(colour), style: StrokeStyle(lineWidth: lit ? 2.2 : 1.4, lineCap: .round,
                                                                    dash: f.id == "light" ? [max(L.s(7), 3), max(L.s(5), 2)] : []))
            ctx.fill(head(L.p(f.end), from: L.p(f.control), size: lit ? 9 : 7), with: .color(colour))
            text(&ctx, f.label, f.labelAt, size: 12, weight: lit ? .bold : .semibold, colour: colour)
        }
        if on {
            ctx.draw(Text(Image(systemName: "sun.max.fill")).font(.system(size: max(14, 22 * min(L.scale * 1.4, 1)))).foregroundColor(c(EnergyColors.sun)),
                     at: L.p(52, 50))
        }

        // (what is being followed)
        for t in tokens where t.opacity > 0.01 {
            let label = ctx.resolve(Text(t.label).font(.system(size: 11.5, weight: .bold)).foregroundColor(.white))
            let size = label.measure(in: CGSize(width: 400, height: 40))
            let at = L.p(t.at)
            let box = CGRect(x: at.x - size.width / 2 - 8, y: at.y - size.height / 2 - 4, width: size.width + 16, height: size.height + 8)
            var layer = ctx
            layer.opacity = t.opacity
            layer.fill(Path(roundedRect: box, cornerRadius: box.height / 2), with: .color(accent))
            layer.stroke(Path(roundedRect: box, cornerRadius: box.height / 2), with: .color(.white.opacity(0.8)), lineWidth: 1.5)
            layer.draw(label, at: at, anchor: .center)
        }
    }

    private func text(_ ctx: inout GraphicsContext, _ s: String, _ at: CGPoint, size: CGFloat, weight: Font.Weight, colour: Color) {
        let scaled = max(10, size * min(layout.scale * 1.45, 1.1))
        ctx.draw(Text(s).font(.system(size: scaled, weight: weight)).foregroundColor(colour), at: layout.p(at), anchor: .center)
    }

    private func head(_ tip: CGPoint, from: CGPoint, size: CGFloat) -> Path {
        let dx = tip.x - from.x, dy = tip.y - from.y
        let len = max((dx * dx + dy * dy).squareRoot(), 0.001)
        let ux = dx / len, uy = dy / len
        let base = CGPoint(x: tip.x - ux * size, y: tip.y - uy * size)
        var out = Path()
        out.move(to: tip)
        out.addLine(to: CGPoint(x: base.x - uy * size * 0.55, y: base.y + ux * size * 0.55))
        out.addLine(to: CGPoint(x: base.x + uy * size * 0.55, y: base.y - ux * size * 0.55))
        out.closeSubpath()
        return out
    }
}

/// The map as a view, with the stages' names pressable on it when asked.
struct EnergyMapView: View {
    var highlight: EnergyPlace? = nil
    var lit: Set<String> = []
    var chloroplastActive = true
    var tokens: [EnergyToken] = []
    var pills = false
    var selected: String? = nil
    var onPick: ((String) -> Void)? = nil
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        GeometryReader { g in
            let layout = EnergyMapLayout(g.size)
            ZStack(alignment: .topLeading) {
                Canvas { ctx, size in
                    EnergyMapPainter(layout: EnergyMapLayout(size), dark: scheme == .dark, highlight: highlight, lit: lit,
                                     chloroplastActive: chloroplastActive, tokens: tokens).paint(&ctx)
                }
                .accessibilityHidden(true)
                if pills {
                    ForEach(EnergyMapLayout.pills, id: \.id) { pill in
                        pillButton(pill.id, pill.name)
                            .position(layout.p(pill.at))
                    }
                }
            }
        }
        .aspectRatio(EnergyMapLayout.width / EnergyMapLayout.height, contentMode: .fit)
        .accessibilityElement(children: pills ? .contain : .ignore)
        .accessibilityLabel(description)
    }

    private var description: String {
        var s = "Map of a plant cell: a chloroplast, the cytosol and a mitochondrion"
        if !chloroplastActive { s += "; the chloroplast is idle in the dark" }
        if let highlight { s += "; lit: \(highlight.name)" }
        return s
    }

    private func pillButton(_ id: String, _ name: String) -> some View {
        let on = selected == id
        return Button { onPick?(id) } label: {
            Text(name)
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(on ? Theme.accent : Color.primary)
                .lineLimit(1)
                .fixedSize()
                .padding(.horizontal, 9)
                .padding(.vertical, 4)
                .background(.regularMaterial, in: Capsule())
                .overlay(Capsule().stroke(on ? Theme.accent : Color.primary.opacity(0.15), lineWidth: on ? 2 : 1))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .help("Show where \(name.lowercased()) happens")
        .accessibilityLabel(EnergyPathways.stage(id)?.name ?? name)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

// MARK: - Map page

private struct EnergyMapPage: View {
    var state: BioLaunchState?
    @ObservedObject private var nav = BioNav.shared
    @State private var selected = BioPrefs.string(.energy, "stage") ?? "light"
    @State private var respire = BioPrefs.bool(.energy, "respire", default: false)

    private var stage: EnergyStage { EnergyPathways.stage(selected) ?? EnergyPathways.stages[0] }
    private static let order = ["light", "calvin", "glycolysis", "pyruvate", "krebs", "oxphos", "fermentation"]

    var body: some View {
        PTWorkLayout {
            left
        } centre: {
            centre
        } right: {
            right
        }
        .onAppear(perform: apply)
    }

    private func select(_ id: String) {
        selected = id
        BioPrefs.set(.energy, "stage", id)
    }

    private func setRespire(_ on: Bool) {
        respire = on
        BioPrefs.set(.energy, "respire", on)
    }

    private func apply() {
        guard let s = EnergyLaunch.take("map", state) else { return }
        if let id = s.value("stage"), EnergyPathways.stage(id) != nil { select(id) }
        if s.has("respire") || s.has("dark") { setRespire(true) }
    }

    private var left: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Photosynthesis")
            stageButtons(["light", "calvin"])
            BioHeading("Cellular respiration")
            stageButtons(["glycolysis", "pyruvate", "krebs", "oxphos"])
            BioHeading("Without oxygen")
            stageButtons(["fermentation"])
            BioHeading("View")
            Toggle("Plants respire too", isOn: Binding(get: { respire }, set: { setRespire($0) }))
                .help("Show the cell in the light and in the dark: the mitochondrion works in both")
            Text("Press a stage, or its name on the map. ↑ ↓ or 1–7 move between them.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    private func stageButtons(_ ids: [String]) -> some View {
        VStack(spacing: 6) {
            ForEach(ids, id: \.self) { id in
                if let s = EnergyPathways.stage(id) { stageButton(s, number: (Self.order.firstIndex(of: id) ?? 0) + 1) }
            }
        }
    }

    private func stageButton(_ s: EnergyStage, number: Int) -> some View {
        let on = selected == s.id
        return Button { select(s.id) } label: {
            HStack(spacing: 10) {
                Circle().fill(EnergyColors.swatch(s.place).color).frame(width: 10, height: 10)
                VStack(alignment: .leading, spacing: 1) {
                    Text(s.name).font(.sCallout.weight(.semibold)).foregroundStyle(on ? Theme.accent : Color.primary)
                    Text(s.place.name).font(.sCaption).foregroundStyle(.secondary)
                }
                Spacer(minLength: 4)
                Text("\(number)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .background(on ? Theme.accent.opacity(0.16) : Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(s.name): \(s.place.name) (\(number))")
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    private func move(_ by: Int) {
        let i = Self.order.firstIndex(of: selected) ?? 0
        select(Self.order[min(max(i + by, 0), Self.order.count - 1)])
    }

    private var centre: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text(respire ? "Day and night" : "Inside a plant cell").font(.sHeadline)
                Spacer(minLength: 8)
                if !respire {
                    Label(stage.whereText, systemImage: "mappin.and.ellipse")
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(Theme.accent)
                        .lineLimit(1)
                }
            }
            if respire {
                HStack(alignment: .top, spacing: 14) {
                    mini(light: true)
                    mini(light: false)
                }
                Text(EnergyPathways.respireText).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            } else {
                EnergyMapView(highlight: stage.place, lit: Set(stage.flows), pills: true, selected: stage.id, onPick: select)
                Text("Arrows show what moves between the places; the stage pressed lights its own. Not to scale.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card(radius: 20)
        .focusable()
        .focusEffectDisabled()
        .onKeyPress(.upArrow) { move(-1); return .handled }
        .onKeyPress(.downArrow) { move(1); return .handled }
        .onKeyPress(characters: .decimalDigits, phases: .down) { press in
            guard let n = Int(press.characters), n >= 1, n <= Self.order.count else { return .ignored }
            select(Self.order[n - 1])
            return .handled
        }
    }

    private func mini(light: Bool) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Label(light ? "In the light" : "In the dark", systemImage: light ? "sun.max.fill" : "moon.fill")
                .font(.sCallout.weight(.semibold))
            EnergyMapView(lit: light ? ["light", "water", "oxygen", "sugar", "pyruvate", "carbonDioxide", "atp"] : ["pyruvate", "carbonDioxide", "atp"],
                          chloroplastActive: light)
            Text(light ? "Chloroplast and mitochondrion both at work." : "The chloroplast is idle; the mitochondrion still respires.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var right: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioCard(title: stage.name, swatch: EnergyColors.swatch(stage.place).color, subtitle: stage.process.title) {
                EnergyFact(label: "Where", text: stage.whereText)
                EnergyFact(label: "In (per glucose unless stated)", text: stage.inputs.joined(separator: "; "))
                EnergyFact(label: "Out", text: stage.outputs.joined(separator: "; "))
                if !stage.notes.isEmpty { EnergyFact(label: "Notes", text: stage.notes) }
                if let m = stage.mixUp { EnergyFact(label: "Common mix-up", text: m, tint: .orange) }
                Button(stage.process == .photosynthesis ? "Step through photosynthesis" : "Follow one glucose") {
                    nav.set(.energy, stage.process == .photosynthesis ? "photosynthesis" : "respiration")
                }
                .glassButton()
                .help("Step by step, with running totals")
            }
            BioCard(title: "Overall") {
                EnergyFact(label: "Photosynthesis", text: EnergyPathways.photosynthesisEquation)
                EnergyFact(label: "Cellular respiration", text: EnergyPathways.respirationEquation)
                EnergyFact(label: "From one glucose", text: EnergyPathways.respirationTotal)
            }
        }
    }
}

// MARK: - The step-throughs' pieces

/// A step-through's running totals: each count, this step's change, and the total so far.
private struct EnergyTotals: View {
    let counts: [EnergyCount]
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(counts.enumerated()), id: \.element.id) { i, c in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(c.name).font(.sCallout).foregroundStyle(.secondary)
                    Spacer(minLength: 6)
                    if !c.changeText.isEmpty {
                        Text(c.changeText)
                            .font(.sCaption.weight(.semibold).monospacedDigit())
                            .foregroundStyle(c.changeLow < 0 ? Color.red : Theme.accent)
                    }
                    Text(c.text)
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .frame(minWidth: 46, alignment: .trailing)
                        .contentTransition(.numericText())
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .accessibilityElement(children: .combine)
                if i < counts.count - 1 { Divider().padding(.leading, 12) }
            }
        }
        .card(radius: 14)
        .animation(reduceMotion ? nil : Motion.snappy, value: counts.map(\.text))
    }
}

/// Where glucose's carbons are now: each molecule, how many, and its carbons as beads.
private struct EnergyCarbonList: View {
    let carbons: [EnergyCarbons]

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            ForEach(carbons, id: \.molecule) { c in
                HStack(spacing: 8) {
                    HStack(spacing: 3) {
                        ForEach(0..<c.carbonsEach, id: \.self) { _ in
                            Circle().fill(Color.primary.opacity(0.7)).frame(width: 7, height: 7)
                        }
                    }
                    .frame(width: 56, alignment: .leading)
                    Text("\(c.count) \(c.molecule)").font(.sCallout)
                    Text("\(c.carbonsEach) C each").font(.sCaption).foregroundStyle(.secondary)
                }
                .accessibilityElement(children: .combine)
            }
        }
    }
}

/// The step's card: what happens, where, what goes in and out, and the stage on the map.
private struct EnergyStepCard: View {
    let step: EnergyStep
    @ObservedObject private var nav = BioNav.shared

    var body: some View {
        BioCard(title: step.step.title, swatch: step.place.map { EnergyColors.swatch($0).color },
                subtitle: step.place.map { "\($0.name) · \($0.organelle)" } ?? "The whole cell") {
            EnergyFact(label: "What happens", text: step.step.text)
            if !step.inputs.isEmpty { EnergyFact(label: "In", text: step.inputs.joined(separator: "; ")) }
            if !step.outputs.isEmpty { EnergyFact(label: "Out", text: step.outputs.joined(separator: "; ")) }
            if let s = EnergyPathways.stage(step.stage) {
                Button("\(s.name) on the map") {
                    BioPrefs.set(.energy, "stage", s.id)
                    nav.set(.energy, "map")
                }
                .glassButton()
                .help("See where \(s.name.lowercased()) happens, with its inputs and outputs per glucose")
            }
        }
    }
}

/// The token for a step-through's phase: moving from one step's place to the next's (or, under Reduce Motion, fading).
private func energyTokens(_ ph: BioStepPhase, _ places: [EnergyPlace?], _ labels: [String]) -> [EnergyToken] {
    func at(_ i: Int) -> EnergyToken? {
        guard places.indices.contains(i), let place = places[i] else { return nil }
        return EnergyToken(at: EnergyMapLayout.anchor(place), label: labels[i])
    }
    if ph.crossfade {
        var out: [EnergyToken] = []
        if var a = at(ph.from) { a.opacity = 1 - ph.t; out.append(a) }
        if var b = at(ph.to) { b.opacity = ph.t; out.append(b) }
        return out
    }
    guard let a = at(ph.from) ?? at(ph.to), let b = at(ph.to) ?? at(ph.from) else { return [] }
    let p = CGPoint(x: a.at.x + (b.at.x - a.at.x) * ph.t, y: a.at.y + (b.at.y - a.at.y) * ph.t)
    return [EnergyToken(at: p, label: ph.t < 0.5 ? a.label : b.label)]
}

/// What a step's token says: the carbons being followed, or the electrons on a membrane.
private func energyTokenLabel(_ s: EnergyStep) -> String {
    if s.place == .thylakoid || s.place == .innerMembrane { return "e⁻ and H⁺" }
    guard let c = s.carbons.first(where: { $0.molecule != "CO₂" }) ?? s.carbons.first else { return "" }
    return c.count == 1 ? c.molecule : "\(c.count) \(c.molecule)"
}

/// A step-through page's centre: the map following the steps, and the controls under it.
private struct EnergyStepScene: View {
    @ObservedObject var steps: BioStepController
    let data: [EnergyStep]
    let lit: (EnergyStep) -> Set<String>
    var groups: [Int: String] = [:]
    /// What the token says at each step (the carbons, or the electrons, unless given).
    var labels: [String]? = nil

    var body: some View {
        let places = data.map(\.place), labels = self.labels ?? data.map(energyTokenLabel)
        VStack(spacing: 14) {
            TimelineView(.animation(paused: !steps.moving)) { ctx in
                let ph = steps.phase(at: ctx.date)
                let current = data[min(ph.current, data.count - 1)]
                EnergyMapView(highlight: current.place, lit: lit(current), tokens: energyTokens(ph, places, labels))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .bioStepKeys(steps)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Map of the cell. \(steps.current?.title ?? ""): \(data[min(steps.index, data.count - 1)].place?.name ?? "the whole cell")")
            BioStepControls(controller: steps, groups: groups)
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .card(radius: 20)
    }
}

// MARK: - Respiration page

private struct EnergyRespirationPage: View {
    var state: BioLaunchState?
    @StateObject private var steps = BioStepController(steps: EnergyPathways.respiration().map(\.step))
    @State private var aerobic = true
    @State private var fermentation: EnergyFermentation = .lactate

    private var data: [EnergyStep] { EnergyPathways.respiration(fermenting: aerobic ? nil : fermentation) }

    var body: some View {
        let data = self.data
        let step = data[min(steps.index, data.count - 1)]
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                BioHeading("Oxygen")
                Picker("Oxygen", selection: Binding(get: { aerobic }, set: { setPath($0, fermentation) })) {
                    Text("With O₂").tag(true)
                    Text("Without O₂").tag(false)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .help("With oxygen the glucose goes on into the mitochondrion; without, it ferments in the cytosol")
                if !aerobic {
                    Picker("Fermentation", selection: Binding(get: { fermentation }, set: { setPath(false, $0) })) {
                        ForEach(EnergyFermentation.allCases, id: \.self) { f in Text(f.title).tag(f) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                }
                BioHeading("Running totals")
                EnergyTotals(counts: step.counts)
                BioHeading("Glucose's six carbons")
                EnergyCarbonList(carbons: step.carbons)
                Text("Totals count what has been made so far (ATP net of the 2 invested). NADH and FADH₂ are spent at the electron transport chain.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .font(.sCallout)
        } centre: {
            EnergyStepScene(steps: steps, data: data, lit: { Set(EnergyPathways.stage($0.stage)?.flows ?? []) },
                            groups: aerobic ? [1: "Glycolysis", 2: "Pyruvate oxidation", 3: "Citric acid cycle", 4: "Oxidative phosphorylation", 5: "Total"]
                                : [1: "Glycolysis", 2: "Fermentation", 3: "Total"])
        } right: {
            VStack(alignment: .leading, spacing: 14) {
                EnergyStepCard(step: step)
                BioCard(title: "One glucose, in all") {
                    EnergyFact(label: "With oxygen", text: EnergyPathways.respirationTotal)
                    EnergyFact(label: "Without oxygen", text: "only glycolysis's 2 ATP")
                    EnergyFact(label: "Equation", text: EnergyPathways.respirationEquation)
                }
            }
        }
        .onAppear(perform: apply)
    }

    private func setPath(_ a: Bool, _ f: EnergyFermentation) {
        aerobic = a
        fermentation = f
        steps.setSteps(EnergyPathways.respiration(fermenting: a ? nil : f).map(\.step), keepIndex: true)
    }

    private func apply() {
        guard let s = EnergyLaunch.take("respiration", state) else { return }
        if let f = s.value("ferment").flatMap(EnergyFermentation.init(rawValue:)) { setPath(false, f) }
        if s.has("anaerobic") { setPath(false, fermentation) }
        if let n = s.value("step").flatMap({ Int($0) }) { steps.go(to: n - 1, animated: false) }
    }
}

// MARK: - Photosynthesis page

private struct EnergyPhotosynthesisPage: View {
    var state: BioLaunchState?
    @StateObject private var steps = BioStepController(steps: EnergyPathways.photosynthesis.map(\.step))

    var body: some View {
        let data = EnergyPathways.photosynthesis
        let step = data[min(steps.index, data.count - 1)]
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                BioHeading("Made by the light reactions")
                EnergyTotals(counts: Array(step.counts.prefix(4)))
                BioHeading("Used by the Calvin cycle")
                EnergyTotals(counts: Array(step.counts.dropFirst(4)))
                BioHeading("Carbons")
                if step.carbons.isEmpty {
                    Text("No carbon yet: the light reactions make ATP and NADPH, and O₂ from water.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    EnergyCarbonList(carbons: step.carbons)
                }
            }
            .font(.sCallout)
        } centre: {
            EnergyStepScene(steps: steps, data: data, lit: { s in
                s.place == .thylakoid ? ["light", "water", "oxygen"] : ["carbonDioxide", "sugar"]
            }, groups: [1: "Light reactions", 2: "Calvin cycle"])
        } right: {
            VStack(alignment: .leading, spacing: 14) {
                EnergyStepCard(step: step)
                BioCard(title: "One G3P, in all") {
                    EnergyFact(label: "Calvin cycle", text: "3 CO₂, 9 ATP and 6 NADPH per G3P; a glucose takes two: 6 CO₂, 18 ATP, 12 NADPH.")
                    EnergyFact(label: "Light reactions", text: "6 H₂O split for 3 O₂: the O₂ comes from **water**.")
                    EnergyFact(label: "Equation", text: EnergyPathways.photosynthesisEquation)
                }
            }
        }
        .onAppear(perform: apply)
    }

    private func apply() {
        guard let s = EnergyLaunch.take("photosynthesis", state) else { return }
        if let n = s.value("step").flatMap({ Int($0) }) { steps.go(to: n - 1, animated: false) }
    }
}

// MARK: - Follow the atoms page

private struct EnergyAtomsPage: View {
    var state: BioLaunchState?
    @StateObject private var steps = BioStepController(steps: EnergyPathways.traceSteps(.oxygen))
    @State private var trace: EnergyTrace = .oxygen

    var body: some View {
        let data = EnergyPathways.trace(trace)
        let i = min(steps.index, data.count - 1)
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                BioHeading("Follow")
                Picker("Follow", selection: Binding(get: { trace }, set: { setTrace($0) })) {
                    ForEach(EnergyTrace.allCases, id: \.self) { t in Text(t.title).tag(t) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                BioHeading("The whole equation")
                EnergyFact(label: "Photosynthesis, with the water written out", text: EnergyPathways.photosynthesisFull)
                Text(trace == .oxygen
                     ? "All the O₂ released comes from the 12 H₂O split by photosystem II. The oxygen of CO₂ ends up in the sugar and the new water."
                     : "Every carbon in glucose was fixed from CO₂ by rubisco, and respiration returns each one to CO₂.")
                    .font(.sCallout)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .font(.sCallout)
        } centre: {
            VStack(spacing: 14) {
                chain(data, current: i)
                EnergyStepScene(steps: steps, data: data.map(Self.asStep),
                                lit: { _ in trace == .oxygen ? ["water", "oxygen"] : ["carbonDioxide", "sugar", "pyruvate"] },
                                labels: data.map { EnergyText.plain($0.formula.count <= 6 ? $0.formula : $0.molecule) })
            }
        } right: {
            VStack(alignment: .leading, spacing: 14) {
                BioCard(title: data[i].title, swatch: data[i].place.map { EnergyColors.swatch($0).color }, subtitle: data[i].process) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Now in").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        EnergyText.formula(data[i].formula).font(.system(size: 22, weight: .semibold))
                        Text(data[i].molecule).font(.sCallout).foregroundStyle(.secondary)
                    }
                    .accessibilityElement(children: .combine)
                    EnergyFact(label: "What happens", text: data[i].text)
                    EnergyFact(label: "Where", text: data[i].place.map { "\($0.name) (\($0.organelle.lowercased()))" } ?? "The whole cell")
                }
                BioCard(title: "Common mix-up") {
                    EnergyFact(label: "Where does the O₂ come from?", text: "From **water**, split in the light reactions; not from CO₂.", tint: .orange)
                    EnergyFact(label: "Do plants respire?", text: "Yes: their mitochondria use O₂ and release CO₂ day and night.", tint: .orange)
                }
            }
        }
        .onAppear(perform: apply)
    }

    /// A trace step as the scene's step (its place).
    private static func asStep(_ t: EnergyTraceStep) -> EnergyStep {
        EnergyStep(step: BioStep(0, t.title, t.text), place: t.place, stage: nil, inputs: [], outputs: [], counts: [], carbons: [])
    }

    private func setTrace(_ t: EnergyTrace) {
        trace = t
        steps.setSteps(EnergyPathways.traceSteps(t))
    }

    /// The molecules the atom passes through, in a row: the one it is in now lit, the ones to come faint.
    private func chain(_ data: [EnergyTraceStep], current: Int) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 6) {
                ForEach(data.indices, id: \.self) { k in
                    if k > 0 {
                        Image(systemName: "arrow.right").font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
                    }
                    Button {
                        steps.pause()
                        steps.go(to: k)
                    } label: {
                        chip(data[k], on: k == current)
                    }
                    .buttonStyle(.plain)
                    .opacity(k > current ? 0.45 : 1)
                    .help("\(data[k].title): \(data[k].molecule)")
                    .accessibilityLabel("\(data[k].title): \(data[k].molecule)")
                }
            }
            .padding(10)
        }
        .frame(maxWidth: .infinity)
        .card(radius: 16)
    }

    private func chip(_ t: EnergyTraceStep, on: Bool) -> some View {
        VStack(spacing: 4) {
            EnergyText.formula(t.formula.count <= 6 ? t.formula : t.molecule).font(.system(size: 15, weight: .semibold)).lineLimit(1)
            if t.carbons > 0 {
                HStack(spacing: 3) {
                    ForEach(0..<t.carbons, id: \.self) { k in
                        Circle()
                            .fill(k == t.traced ? Theme.accent : Color.primary.opacity(0.35))
                            .frame(width: 7, height: 7)
                    }
                }
            }
            Text(t.carbons > 0 ? "\(t.carbons) C" : t.molecule).font(.sCaption2).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .background(on ? Theme.accent.opacity(0.14) : Color.primary.opacity(0.05), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).stroke(on ? Theme.accent : Color.clear, lineWidth: 1.5))
    }

    private func apply() {
        guard let s = EnergyLaunch.take("atoms", state) else { return }
        if let t = s.value("trace").flatMap(EnergyTrace.init(rawValue:)) { setTrace(t) }
        if let n = s.value("step").flatMap({ Int($0) }) { steps.go(to: n - 1, animated: false) }
    }
}
