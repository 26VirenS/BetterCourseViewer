import SwiftUI

// The Transcribe & translate page's scene (spec §4.3): a 2D drawing — sharper for letters than 3D — in four lanes, top
// to bottom: the coding strand (5′→3′), the template strand (3′→5′) aligned base for base under it, the RNA as it is
// made (aligned with the DNA it came from, its 3′ end paired with the template inside the polymerase's bubble, then let
// go), and below the nuclear envelope the ribosome's lane (the mRNA with its codons boxed from the AUG, the ribosome's
// E, P and A sites, the tRNAs with their anticodons written 3′→5′ over the codons they pair with, and the growing
// polypeptide, each amino acid over the codon it came from). The promoter, UTRs, exons and intron are tinted
// bands; the TATA box, start and stop codons and poly-A signal are boxed. An overview strip shows the whole gene and the
// part in view. The camera follows the work (the polymerase, the processing, the ribosome); a drag looks aside.

/// The scene for a moment of the step-through: at rest on a step, moving between two neighbouring steps (GXScene.make),
/// or — for a jump, and under Reduce Motion — the two steps crossfaded.
struct GXSceneCanvas: View {
    let process: GXProcess
    let phase: BioStepPhase
    var pan: CGFloat = 0
    var notice: [String] = []
    var hover: CGPoint?
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let dark = scheme == .dark
        return Canvas { ctx, size in
            let p = process
            let n = p.frames.count
            guard n > 0 else { return }
            let from = min(max(phase.from, 0), n - 1), to = min(max(phase.to, 0), n - 1)
            if phase.isMoving && (phase.crossfade || abs(from - to) != 1) {
                var a = ctx
                a.opacity = 1 - phase.t
                GXPainter(process: p, scene: .rest(p, from), size: size, pan: pan, dark: dark).draw(&a)
                var b = ctx
                b.opacity = phase.t
                GXPainter(process: p, scene: .rest(p, to), size: size, pan: pan, dark: dark).draw(&b)
            } else {
                let scene = phase.isMoving ? GXScene.make(p, from: from, to: to, t: phase.t) : GXScene.rest(p, to)
                GXPainter(process: p, scene: scene, size: size, pan: pan, notice: Set(notice), hover: phase.isMoving ? nil : hover, dark: dark).draw(&ctx)
            }
        }
    }
}

/// Draws a GXScene. Lanes are laid out for a 400 pt tall view (squeezed when shorter); columns are a base pair's place.
final class GXPainter {
    let p: GXProcess
    let s: GXScene
    let size: CGSize
    let notice: Set<String>
    let hover: CGPoint?
    /// Drawn for the dark appearance (letters on fills pick their colour by it).
    let dark: Bool
    var g: GXGene { p.gene }
    var f: GXFrame { s.frame }

    static let gutter: CGFloat = 150
    // (the lanes' heights in a 400 pt view)
    static let yOverview: CGFloat = 8, yMarks: CGFloat = 26, yRegions: CGFloat = 54
    static let y1: CGFloat = 84, y2: CGFloat = 110, y3: CGFloat = 164, yEnvelope: CGFloat = 200, y4: CGFloat = 360

    let left: CGFloat
    let right: CGFloat
    let width: CGFloat
    let sy: CGFloat
    let top: CGFloat
    /// Points per column.
    let cell: CGFloat
    /// The x of column 0.
    let x0: CGFloat
    private var letters: [Character: GraphicsContext.ResolvedText] = [:]

    init(process: GXProcess, scene: GXScene, size: CGSize, pan: CGFloat = 0, notice: Set<String> = [], hover: CGPoint? = nil, dark: Bool = false) {
        p = process
        self.dark = dark
        s = scene
        self.size = size
        self.notice = notice
        self.hover = hover
        left = GXPainter.gutter + 8
        right = size.width - 14
        width = max(right - left, 40)
        sy = min(1, size.height / 400)
        top = max(0, (size.height - 400) / 2) * 0.6
        // (the camera: from the whole gene to bases at full size, centred on the work, kept on the gene)
        let span = process.gene.columnSpan
        let total = CGFloat(span.upperBound - span.lowerBound + 1)
        let full: CGFloat = 17
        let fitCell = min(full, width / (total + 2))
        let c = full + (fitCell - full) * CGFloat(min(max(scene.frame.fit, 0), 1))
        cell = c
        let visible = width / c
        let lo = CGFloat(span.lowerBound) - 1 + visible / 2, hi = CGFloat(span.upperBound) + 1 - visible / 2
        let focus = lo >= hi ? (CGFloat(span.lowerBound) + CGFloat(span.upperBound)) / 2 : min(max(CGFloat(scene.frame.focus), lo), hi)
        let reach = total * c
        x0 = left + width / 2 - focus * c + min(max(pan, -reach), reach)
    }

    func x(_ col: Double) -> CGFloat { x0 + CGFloat(col) * cell }
    func y(_ v: CGFloat) -> CGFloat { top + v * sy }

    private func ease(_ t: Double) -> Double {
        let u = min(max(t, 0), 1)
        return u < 0.5 ? 4 * u * u * u : 1 - pow(-2 * u + 2, 3) / 2
    }

    /// The columns in view (and one more each side).
    private var visible: ClosedRange<Int> {
        let a = Int(floor((left - x0) / cell)) - 1, b = Int(ceil((right - x0) / cell)) + 1
        return a...max(a, b)
    }

    // MARK: Drawing

    func draw(_ ctx: inout GraphicsContext) {
        for b in "ACGTUN" {
            letters[b] = ctx.resolve(Text(String(b)).font(.system(size: 12, weight: .bold, design: .monospaced)).foregroundColor(BioColor.base(b)))
        }
        overview(&ctx)
        var c = ctx
        c.clip(to: Path(CGRect(x: left - 4, y: 0, width: width + 8, height: size.height)))
        regions(&c)
        factors(&c)
        polymerase(&c)
        dna(&c)
        codonBoxes(&c)
        rna(&c)
        envelope(&c)
        ribosome(&c)
        for t in s.trnas { trna(&c, t) }
        releaseFactor(&c)
        chain(&c)
        coupling(&c)
        tooltip(&c)
        gutterLabels(&ctx)
    }

    private func text(_ c: inout GraphicsContext, _ s: String, _ at: CGPoint, size: CGFloat = 11, weight: Font.Weight = .semibold,
                      colour: Color = .secondary, anchor: UnitPoint = .center, alpha: Double = 1) {
        guard alpha > 0.01 else { return }
        var cc = c
        cc.opacity *= alpha
        cc.draw(Text(s).font(.system(size: size, weight: weight)).foregroundColor(colour), at: at, anchor: anchor)
    }

    /// A base: its letter on its colour's tint (a bar of its colour when zoomed out too far for letters).
    private func base(_ c: inout GraphicsContext, _ b: Character, _ at: CGPoint, alpha: Double = 1, ring: Color? = nil) {
        guard alpha > 0.01 else { return }
        var cc = c
        cc.opacity *= alpha
        let w = max(cell - 1.6, 1), h: CGFloat = 17
        let colour = BioColor.base(b)
        let lettered = Double(min(max((cell - 9) / 4, 0), 1))
        let rect = CGRect(x: at.x - w / 2, y: at.y - h / 2, width: w, height: h)
        if lettered < 1 {
            cc.fill(Path(rect), with: .color(colour.opacity(0.8 * (1 - lettered))))
        }
        if lettered > 0 {
            cc.fill(Path(roundedRect: rect, cornerRadius: 3.5), with: .color(colour.opacity(0.17 * lettered)))
            if let t = letters[b] ?? letters["N"] {
                var lc = cc
                lc.opacity *= lettered
                lc.draw(t, at: at)
            }
        }
        if let ring {
            cc.stroke(Path(roundedRect: rect.insetBy(dx: -1, dy: -1), cornerRadius: 4.5), with: .color(ring), lineWidth: 1.5)
        }
    }

    // MARK: The overview strip

    private func overview(_ c: inout GraphicsContext) {
        let span = g.columnSpan
        let total = span.upperBound - span.lowerBound + 1
        func ox(_ col: Double) -> CGFloat { left + CGFloat((col - span.lowerBound) / total) * width }
        let yy = y(GXPainter.yOverview)
        c.fill(Path(roundedRect: CGRect(x: left, y: yy - 3, width: width, height: 6), cornerRadius: 3), with: .color(Color.primary.opacity(0.07)))
        for r in g.regions {
            let a = ox(Double(r.range.lowerBound) - 0.5), b = ox(Double(r.range.upperBound) + 0.5)
            c.fill(Path(roundedRect: CGRect(x: a, y: yy - 3, width: max(b - a, 1), height: 6), cornerRadius: 2), with: .color(GXColors.regionSolid(r.kind)))
        }
        let a = Double((left - x0) / cell), b = Double((right - x0) / cell)
        let wa = max(ox(a), left), wb = min(ox(b), right)
        if wb - wa < width - 2 {
            c.stroke(Path(roundedRect: CGRect(x: wa, y: yy - 6, width: max(wb - wa, 3), height: 12), cornerRadius: 3), with: .color(Theme.accent), lineWidth: 1.5)
        }
        if f.polymerase > 0.05 {
            c.fill(Path(ellipseIn: CGRect(x: ox(f.polX) - 4, y: yy - 4, width: 8, height: 8)), with: .color(GXColors.polymerase.color.opacity(f.polymerase)))
        }
        if f.small > 0.05, g.cdsStart != nil {
            let col = f.large > 0.01 ? g.codonColumn(s.ribosome) : g.column(mature: f.smallAt)
            c.fill(Path(ellipseIn: CGRect(x: ox(col) - 4, y: yy - 4, width: 8, height: 8)), with: .color(GXColors.ribosome.color.opacity(f.small)))
        }
    }

    // MARK: The gene's bands and marks

    private func regions(_ c: inout GraphicsContext) {
        let bandTop = y(GXPainter.y1) - 15, bandBottom = y(GXPainter.y2) + 15
        for r in g.regions {
            let a = x(Double(r.range.lowerBound) - 0.5), b = x(Double(r.range.upperBound) + 0.5)
            guard b > left - 4 && a < right + 4 else { continue }
            c.fill(Path(CGRect(x: a, y: bandTop, width: b - a, height: bandBottom - bandTop)), with: .color(GXColors.region(r.kind)))
            let mid = min(max((a + b) / 2, max(a, left) + 34), min(b, right) - 34)
            let lit = r.kind == .intron && (notice.contains("intron") || notice.contains("gu-ag"))
            if min(b, right) - max(a, left) > 64 {
                text(&c, r.name, CGPoint(x: mid, y: y(GXPainter.yRegions)), size: 10.5, weight: lit ? .bold : .semibold, colour: lit ? Theme.accent : .secondary)
            }
        }
        // (the boxed marks, their names in one or two rows above)
        var rowEnds: [CGFloat] = [-.infinity, -.infinity]
        for m in g.marks {
            let a = x(Double(m.range.lowerBound) - 0.5), b = x(Double(m.range.upperBound) + 0.5)
            guard b > left - 40 && a < right + 40 else { continue }
            let colour = markColour(m.kind)
            let lit = notice.contains(markKey(m.kind))
            let box = Path(roundedRect: CGRect(x: a, y: bandTop + 2, width: b - a, height: bandBottom - bandTop - 4), cornerRadius: 4)
            if lit { c.stroke(box, with: .color(colour.opacity(0.3)), lineWidth: 6) }
            c.stroke(box, with: .color(colour), lineWidth: lit ? 2.2 : 1.3)
            let mid = (a + b) / 2
            let w = CGFloat(m.name.count) * 6.2 + 8
            let row = rowEnds[0] < mid - w / 2 ? 0 : 1
            rowEnds[row] = mid + w / 2
            text(&c, m.name, CGPoint(x: mid, y: y(GXPainter.yMarks + CGFloat(row) * 13)), size: 10.5, weight: lit ? .bold : .semibold, colour: colour)
        }
        // (+1: the transcription start site)
        let sx = x(Double(g.txStart) - 0.5), ty = y(GXPainter.y1) - 11 - 11 * CGFloat(open(Double(g.txStart)))
        let lit = notice.contains("plus1")
        var arrow = Path()
        arrow.move(to: CGPoint(x: sx, y: ty))
        arrow.addLine(to: CGPoint(x: sx, y: ty - 12))
        arrow.addLine(to: CGPoint(x: sx + max(cell * 1.4, 9), y: ty - 12))
        let colour: Color = lit ? Theme.accent : .primary
        c.stroke(arrow, with: .color(colour.opacity(0.8)), lineWidth: lit ? 2 : 1.4)
        var head = Path()
        let hx = sx + max(cell * 1.4, 9)
        head.move(to: CGPoint(x: hx + 4, y: ty - 12))
        head.addLine(to: CGPoint(x: hx - 2, y: ty - 16))
        head.addLine(to: CGPoint(x: hx - 2, y: ty - 8))
        head.closeSubpath()
        c.fill(head, with: .color(colour.opacity(0.8)))
        text(&c, "+1", CGPoint(x: sx - 3, y: ty - 12), size: 10.5, weight: .bold, colour: colour, anchor: .trailing)
        // (the splice sites, GU…AG, on the coding strand)
        if notice.contains("gu-ag") {
            for intron in g.introns {
                for r in [intron.lowerBound...(intron.lowerBound + 1), (intron.upperBound - 1)...intron.upperBound] {
                    let a = x(Double(r.lowerBound) - 0.5), b = x(Double(r.upperBound) + 0.5)
                    c.stroke(Path(roundedRect: CGRect(x: a, y: y(GXPainter.y1) - 10, width: b - a, height: 20), cornerRadius: 4), with: .color(Theme.accent), lineWidth: 2)
                }
            }
        }
    }

    private func markColour(_ k: GXRegion.Kind) -> Color {
        switch k {
        case .tata: return BioPalette.guanine.color
        case .start: return GXColors.start.color
        case .stop: return GXColors.stop.color
        default: return BioPalette.cytosine.color
        }
    }

    private func markKey(_ k: GXRegion.Kind) -> String {
        switch k {
        case .tata: return "tata"
        case .start: return "start"
        case .stop: return "stop"
        default: return "polyA"
        }
    }

    // MARK: Transcription's machinery

    /// How far the DNA is opened at a column (the bubble round the polymerase: about 13 bp, from 9 behind it to 3 ahead).
    private func open(_ col: Double) -> Double {
        guard f.bubble > 0.001 else { return 0 }
        let d = col - f.polX
        let lo = -9.0, hi = 3.0, ramp = 1.5
        let v: Double
        if d < lo - ramp || d > hi + ramp {
            v = 0
        } else if d < lo {
            v = (d - (lo - ramp)) / ramp
        } else if d > hi {
            v = ((hi + ramp) - d) / ramp
        } else {
            v = 1
        }
        return v * v * (3 - 2 * v) * f.bubble
    }

    private func factors(_ c: inout GraphicsContext) {
        guard f.factors > 0.01 else { return }
        if g.cell == .eukaryote, let tata = g.marks.first(where: { $0.kind == .tata }) {
            let cx = x(Double(tata.range.lowerBound + tata.range.upperBound) / 2), cy = y(GXPainter.y1) - 22
            var cc = c
            cc.opacity *= f.factors
            for (dx, w) in [(-9.0, 26.0), (8.0, 22.0)] {
                cc.fill(Path(ellipseIn: CGRect(x: cx + CGFloat(dx) - CGFloat(w) / 2, y: cy - 8, width: CGFloat(w), height: 16)), with: .color(GXColors.factor.color.opacity(0.55)))
            }
            text(&c, "Transcription factors", CGPoint(x: cx - 26, y: cy), size: 10, weight: .semibold, colour: GXColors.factor.color, anchor: .trailing, alpha: f.factors)
        } else if g.cell == .prokaryote && f.polymerase > 0.01 {
            // (σ helps RNA polymerase find the promoter, and lets go after the first nucleotides)
            let a = f.factors * min(max((12 - f.made) / 4, 0), 1) * f.polymerase
            guard a > 0.01 else { return }
            let cx = x(f.polX - 11.5), cy = y(GXPainter.y1) - 18
            var cc = c
            cc.opacity *= a
            cc.fill(Path(ellipseIn: CGRect(x: cx - 10, y: cy - 10, width: 20, height: 20)), with: .color(GXColors.factor.color.opacity(0.6)))
            cc.draw(Text("σ").font(.system(size: 13, weight: .bold)).foregroundColor(.white), at: CGPoint(x: cx, y: cy))
        }
    }

    private func polymerase(_ c: inout GraphicsContext) {
        guard f.polymerase > 0.01 else { return }
        let a = x(f.polX - 10.5), b = x(f.polX + 4.5)
        let rect = CGRect(x: a, y: y(GXPainter.y1) - 26, width: b - a, height: y(GXPainter.y2) + 48 - (y(GXPainter.y1) - 26))
        let shape = Path(roundedRect: rect, cornerRadius: 26)
        let colour = GXColors.polymerase.color
        var cc = c
        cc.opacity *= f.polymerase
        cc.fill(shape, with: .color(colour.opacity(0.2)))
        cc.stroke(shape, with: .color(colour.opacity(0.65)), lineWidth: 1.5)
        let name = g.cell == .eukaryote ? "RNA polymerase II" : "RNA polymerase"
        let nameWidth = CGFloat(name.count) * 6.8
        if b + 6 + nameWidth <= right {
            text(&c, name, CGPoint(x: b + 6, y: y(GXPainter.y1) - 17), size: 11, weight: .bold, colour: colour, anchor: .leading, alpha: f.polymerase)
        } else {
            text(&c, name, CGPoint(x: a - 6, y: y(GXPainter.y2) + 30), size: 11, weight: .bold, colour: colour, anchor: .trailing, alpha: f.polymerase)
        }
        text(&c, "reads the template 3′→5′ →", CGPoint(x: b + 6, y: y(GXPainter.y2) + 24), size: 10, weight: .medium, colour: colour, anchor: .leading, alpha: f.polymerase)
        // (free nucleotides waiting downstream, under the template)
        if f.stage.isTranscription {
            let spots: [(Double, CGFloat, Character)] = [(6, -2, "A"), (8.5, 14, "U"), (11, 2, "G"), (13.5, 16, "C"), (15.5, 0, "A")]
            for (dx, dy, l) in spots {
                let at = CGPoint(x: x(f.polX + dx), y: y(GXPainter.y3) + dy)
                ntp(&c, l, at, alpha: 0.6 * f.polymerase)
            }
            text(&c, "free NTPs", CGPoint(x: x(f.polX + 10.5), y: y(GXPainter.y3) + 28), size: 10, colour: .secondary, alpha: f.polymerase)
        }
    }

    /// A nucleoside triphosphate: its base, and three small phosphates trailing.
    private func ntp(_ c: inout GraphicsContext, _ b: Character, _ at: CGPoint, alpha: Double, tail: Double = 1) {
        base(&c, b, at, alpha: alpha)
        guard tail > 0.01 else { return }
        var cc = c
        cc.opacity *= alpha * tail
        for k in 0..<3 {
            let px = at.x + max(cell, 10) / 2 + 3 + CGFloat(k) * 5
            cc.fill(Path(ellipseIn: CGRect(x: px - 2, y: at.y - 2, width: 4, height: 4)), with: .color(BioPalette.uracil.color.opacity(0.7)))
        }
    }

    private func dna(_ c: inout GraphicsContext) {
        let n = g.coding.count
        guard n > 0 else { return }
        let range = max(visible.lowerBound, 0)...min(visible.upperBound, n - 1)
        guard range.lowerBound <= range.upperBound else { return }
        var coding = Path(), template = Path()
        for col in range {
            let o = CGFloat(open(Double(col)))
            let p1 = CGPoint(x: x(Double(col)), y: y(GXPainter.y1) - 11 * o), p2 = CGPoint(x: x(Double(col)), y: y(GXPainter.y2) + 11 * o)
            if col == range.lowerBound {
                coding.move(to: p1)
                template.move(to: p2)
            } else {
                coding.addLine(to: p1)
                template.addLine(to: p2)
            }
        }
        c.stroke(coding, with: .color(BioPalette.strand1.color.opacity(0.55)), lineWidth: 2)
        c.stroke(template, with: .color(BioPalette.strand2.color.opacity(0.55)), lineWidth: 2)
        let paired = cell >= 6
        for col in range {
            let o = open(Double(col))
            let cx = x(Double(col))
            let y1 = y(GXPainter.y1) - 11 * CGFloat(o), y2 = y(GXPainter.y2) + 11 * CGFloat(o)
            if paired && o < 0.9 {
                var h = Path()
                h.move(to: CGPoint(x: cx, y: y1 + 9))
                h.addLine(to: CGPoint(x: cx, y: y2 - 9))
                c.stroke(h, with: .color(BioPalette.hBond.color.opacity(0.6 * (1 - o))), style: StrokeStyle(lineWidth: 1.5, dash: [2, 2]))
            }
            base(&c, g.coding[col], CGPoint(x: cx, y: y1))
            base(&c, g.template[col], CGPoint(x: cx, y: y2))
        }
        let l = x(-0.5) - 10, r = x(Double(n) - 0.5) + 10
        text(&c, "5′", CGPoint(x: l, y: y(GXPainter.y1)), size: 11, weight: .bold)
        text(&c, "3′", CGPoint(x: r, y: y(GXPainter.y1)), size: 11, weight: .bold)
        text(&c, "3′", CGPoint(x: l, y: y(GXPainter.y2)), size: 11, weight: .bold)
        text(&c, "5′", CGPoint(x: r, y: y(GXPainter.y2)), size: 11, weight: .bold)
    }

    // MARK: The RNA

    /// Where a pre-mRNA base is now: its column and height (paired in the bubble, in its lane, exported, or an intron
    /// looping away), and how visible.
    private func place(_ i: Int) -> (x: CGFloat, y: CGFloat, alpha: Double) {
        let n = g.preMRNA.count
        let col = g.column(pre: i, splice: f.splice)
        var yy = y(GXPainter.y3)
        // (the last 8 nt paired with the template inside the bubble; further back the RNA peels away)
        let d = Double(g.txStart + i) - f.polX
        let hyb = (d >= -7 ? 1 : (d >= -10 ? (d + 10) / 3 : 0)) * (1 - f.released)
        if hyb > 0 {
            let paired = y(GXPainter.y2) + 11 * CGFloat(open(Double(g.txStart + i))) + 21
            yy += (paired - yy) * CGFloat(hyb)
        }
        // (an intron loops away and fades as it is cut out)
        var alpha = 1.0
        if g.inIntron(pre: i), let intron = g.introns.first(where: { $0.contains(g.txStart + i) }) {
            let q = Double(g.txStart + i - intron.lowerBound) / Double(max(intron.count - 1, 1))
            yy -= CGFloat(36 * sin(.pi * q) * f.splice)
            alpha = 1 - f.splice
        }
        // (export: down to the ribosome's lane, the 5′ end first)
        let e = min(max(f.export * 1.6 - 0.6 * Double(i) / Double(max(n - 1, 1)), 0), 1)
        if e > 0 { yy += (y(GXPainter.y4) - yy) * CGFloat(ease(e)) }
        return (x(col), yy, alpha)
    }

    private func rna(_ c: inout GraphicsContext) {
        let n = g.preMRNA.count
        let settled = min(s.incoming ?? Int(f.made + 1e-6), n)
        guard settled > 0 || s.incoming != nil else { return }
        let colour = BioPalette.rna.color
        let places = (0..<settled).map(place)
        // (the backbone, then the bases)
        var backbone = Path()
        var started = false
        for (i, pl) in places.enumerated() where pl.alpha > 0.5 {
            let pt = CGPoint(x: pl.x, y: pl.y)
            if !started || (i > 0 && places[i - 1].alpha <= 0.5) {
                backbone.move(to: pt)
                started = true
            } else {
                backbone.addLine(to: pt)
            }
        }
        c.stroke(backbone, with: .color(colour.opacity(0.5)), lineWidth: 2)
        let vis = visible
        for (i, pl) in places.enumerated() {
            let col = Int(((pl.x - x0) / cell).rounded())
            guard col >= vis.lowerBound - 2 && col <= vis.upperBound + 2 else { continue }
            // (paired with the template inside the bubble: dashed H-bonds)
            let d = Double(g.txStart + i) - f.polX
            if d >= -7 && f.released < 0.5 && f.polymerase > 0.3 && cell >= 6 {
                var h = Path()
                h.move(to: CGPoint(x: pl.x, y: pl.y - 9))
                h.addLine(to: CGPoint(x: pl.x, y: y(GXPainter.y2) + 11 * CGFloat(open(Double(g.txStart + i))) + 9))
                c.stroke(h, with: .color(BioPalette.hBond.color.opacity(0.6)), style: StrokeStyle(lineWidth: 1.5, dash: [2, 2]))
            }
            base(&c, g.preMRNA[i], CGPoint(x: pl.x, y: pl.y), alpha: pl.alpha)
        }
        // (the nucleotide coming in from the free NTPs, to the RNA's 3′ end)
        if let k = s.incoming, k < n {
            let u = s.incomingProgress
            let to = place(k)
            let from = CGPoint(x: x(f.polX + 6), y: y(GXPainter.y3) - 2)
            let e = 1 - pow(1 - u, 3)
            let at = CGPoint(x: from.x + (to.x - from.x) * CGFloat(e), y: from.y + (to.y - from.y) * CGFloat(e))
            ntp(&c, g.preMRNA[k], at, alpha: min(1, 0.4 + u), tail: 1 - u)
            c.stroke(Path(roundedRect: CGRect(x: at.x - cell / 2, y: at.y - 9.5, width: cell, height: 19), cornerRadius: 4.5), with: .color(Theme.accent.opacity(0.9)), lineWidth: 1.5)
        } else if f.stage == .txElong, settled > 0, f.polymerase > 0.5 {
            let last = places[settled - 1]
            c.stroke(Path(roundedRect: CGRect(x: last.x - cell / 2, y: last.y - 9.5, width: cell, height: 19), cornerRadius: 4.5), with: .color(Theme.accent.opacity(0.9)), lineWidth: 1.5)
        }
        guard settled > 0 else { return }
        let first = places[0], last = places[settled - 1]
        // (the 5′ cap: 7-methylguanosine, joined 5′ to 5′)
        if f.cap > 0.01 {
            let cx = first.x - cell * 1.6 - 4
            var cc = c
            cc.opacity *= f.cap
            var link = Path()
            link.move(to: CGPoint(x: cx + 8, y: first.y))
            link.addLine(to: CGPoint(x: first.x - cell / 2, y: first.y))
            cc.stroke(link, with: .color(colour.opacity(0.6)), lineWidth: 2)
            let g7 = Path(ellipseIn: CGRect(x: cx - 9, y: first.y - 9, width: 18, height: 18))
            cc.fill(g7, with: .color(BioPalette.guanine.color.opacity(0.3)))
            cc.stroke(g7, with: .color(BioPalette.guanine.color), lineWidth: 1.5)
            cc.draw(Text("G").font(.system(size: 11, weight: .bold, design: .monospaced)).foregroundColor(BioPalette.guanine.color), at: CGPoint(x: cx, y: first.y))
            text(&c, "m⁷G cap", CGPoint(x: cx, y: first.y - 18), size: 10.5, weight: .bold, colour: notice.contains("cap") ? Theme.accent : .secondary, alpha: f.cap)
        }
        // (the poly-A tail, 20 of its ≈200 A drawn)
        var end = last.x
        if f.tail > 0.01 {
            let count = Int((f.tail * Double(GXProcess.tailDrawn)).rounded())
            for k in 0..<count {
                base(&c, "A", CGPoint(x: last.x + CGFloat(k + 1) * cell, y: last.y), alpha: min(1, f.tail * 1.5))
            }
            end = last.x + CGFloat(count) * cell
            if count > 0 {
                text(&c, "…", CGPoint(x: end + cell * 0.9, y: last.y), size: 12, weight: .bold, alpha: f.tail)
                text(&c, "poly-A tail (≈200 A)", CGPoint(x: last.x + CGFloat(count + 1) * cell / 2, y: last.y - 18), size: 10.5, weight: .bold,
                     colour: notice.contains("tail") ? Theme.accent : .secondary, alpha: f.tail)
                end += cell
            }
        }
        // (a bacterial terminator: a hairpin the RNA folds into at its 3′ end, drawn as a sketch)
        if f.hairpin > 0.01 {
            let hx = last.x - cell * 2.5
            var cc = c
            cc.opacity *= f.hairpin * (1 - f.export)
            var stem = Path()
            stem.move(to: CGPoint(x: hx - 4, y: last.y - 10))
            stem.addLine(to: CGPoint(x: hx - 4, y: last.y - 32))
            stem.move(to: CGPoint(x: hx + 4, y: last.y - 10))
            stem.addLine(to: CGPoint(x: hx + 4, y: last.y - 32))
            cc.stroke(stem, with: .color(colour), lineWidth: 2)
            cc.stroke(Path(ellipseIn: CGRect(x: hx - 8, y: last.y - 48, width: 16, height: 16)), with: .color(colour), lineWidth: 2)
            text(&c, "terminator hairpin", CGPoint(x: hx + 14, y: last.y - 40), size: 10.5, weight: .bold, colour: colour, anchor: .leading, alpha: f.hairpin * (1 - f.export))
        }
        // (the spliceosome over the intron while it works)
        if f.splice > 0.01 && f.splice < 0.99 {
            for intron in g.introns {
                let a = x(Double(intron.lowerBound) - 1), b = x(Double(intron.upperBound) + 1)
                let shape = Path(roundedRect: CGRect(x: a, y: y(GXPainter.y3) - 48, width: b - a, height: 64), cornerRadius: 26)
                let k = sin(.pi * f.splice)
                var cc = c
                cc.opacity *= 0.9 * k
                cc.fill(shape, with: .color(GXColors.polymerase.color.opacity(0.16)))
                cc.stroke(shape, with: .color(GXColors.polymerase.color.opacity(0.6)), lineWidth: 1.2)
                text(&c, "spliceosome", CGPoint(x: (a + b) / 2, y: y(GXPainter.y3) + 26), size: 11, weight: .bold, colour: GXColors.polymerase.color, alpha: k)
            }
        }
        // (where the exons now join, and the codon split across them)
        if notice.contains("junction"), f.splice > 0.5, let sc = g.splitCodon, let s0 = g.cdsStart {
            let j = g.junctions.first ?? 0
            let jx = x(g.column(mature: Double(j)) - 0.5)
            var mark = Path()
            mark.move(to: CGPoint(x: jx, y: first.y - 14))
            mark.addLine(to: CGPoint(x: jx, y: first.y + 14))
            c.stroke(mark, with: .color(Theme.accent), style: StrokeStyle(lineWidth: 2, dash: [3, 2]))
            let a = x(g.column(mature: Double(s0 + 3 * sc.index)) - 0.5), b = x(g.column(mature: Double(s0 + 3 * sc.index + 2)) + 0.5)
            c.stroke(Path(roundedRect: CGRect(x: a, y: first.y - 11, width: b - a, height: 22), cornerRadius: 5), with: .color(Theme.accent), lineWidth: 2)
            text(&c, "exon 1 | exon 2 · codon \(sc.index + 1) \(sc.codon) spans the joint", CGPoint(x: jx, y: first.y + 26), size: 10.5, weight: .bold, colour: Theme.accent)
        }
        // (the ends)
        let capped = f.cap > 0.5
        text(&c, "5′", CGPoint(x: first.x - cell / 2 - (capped ? cell * 1.6 + 22 : 10), y: first.y), size: 11, weight: .bold)
        text(&c, "3′", CGPoint(x: end + cell / 2 + 10, y: last.y), size: 11, weight: .bold)
    }

    // MARK: The nuclear envelope

    private func envelope(_ c: inout GraphicsContext) {
        guard g.cell == .eukaryote else { return }
        let ye = y(GXPainter.yEnvelope)
        let colour = Color.secondary.opacity(0.55)
        // (two membranes, with a pore every so often)
        let spacing: CGFloat = 180
        let phase = (x0.truncatingRemainder(dividingBy: spacing) + spacing).truncatingRemainder(dividingBy: spacing)
        var lines = Path()
        var xx = left - spacing + phase
        var pores: [CGFloat] = []
        while xx < right + spacing {
            pores.append(xx)
            xx += spacing
        }
        var from = left - 4
        for p in pores where p > left - 4 && p < right + 4 {
            for dy in [-3.0, 3.0] {
                lines.move(to: CGPoint(x: from, y: ye + CGFloat(dy)))
                lines.addLine(to: CGPoint(x: p - 8, y: ye + CGFloat(dy)))
            }
            from = p + 8
        }
        for dy in [-3.0, 3.0] {
            lines.move(to: CGPoint(x: from, y: ye + CGFloat(dy)))
            lines.addLine(to: CGPoint(x: right + 4, y: ye + CGFloat(dy)))
        }
        c.stroke(lines, with: .color(colour), lineWidth: 1.5)
        for p in pores where p > left && p < right {
            var ring = Path()
            ring.addArc(center: CGPoint(x: p - 8, y: ye), radius: 3, startAngle: .degrees(90), endAngle: .degrees(270), clockwise: false)
            ring.addArc(center: CGPoint(x: p + 8, y: ye), radius: 3, startAngle: .degrees(-90), endAngle: .degrees(90), clockwise: false)
            c.stroke(ring, with: .color(colour), lineWidth: 1.5)
        }
        if f.stage == .export || (f.export > 0.01 && f.export < 0.99), let p = pores.first(where: { $0 > left + 40 }) {
            text(&c, "nuclear pore", CGPoint(x: p + 12, y: ye + 12), size: 10, weight: .semibold, anchor: .leading)
        }
    }

    // MARK: Translation

    /// The reading frame on the ribosome's lane: alternating boxes round each codon from the AUG on, numbered every 5
    /// (the 5′ UTR before it unboxed: it is not read).
    private func codonBoxes(_ c: inout GraphicsContext) {
        guard let s0 = g.cdsStart, f.export > 0.5 else { return }
        let a = min((f.export - 0.5) * 2, 1) * (1 - 0.5 * f.result)
        let yy = y(GXPainter.y4)
        var cc = c
        cc.opacity *= a
        let boxed = cell >= 8
        for cd in g.codons {
            let l = x(g.column(mature: Double(s0 + 3 * cd.index)) - 0.5), r = x(g.column(mature: Double(s0 + 3 * cd.index + 2)) + 0.5)
            guard r > left - 4 && l < right + 4 else { continue }
            let rect = CGRect(x: l + 0.5, y: yy - 12, width: r - l - 1, height: 24)
            let box = Path(roundedRect: rect, cornerRadius: min(5, rect.width / 4))
            if boxed { cc.fill(box, with: .color(Color.primary.opacity(cd.index % 2 == 0 ? 0.05 : 0.1))) }
            if cd.index == 0 { cc.stroke(box, with: .color(GXColors.start.color), lineWidth: 1.5) }
            if cd.aa == "*" { cc.stroke(box, with: .color(GXColors.stop.color), lineWidth: 1.5) }
            if cd.index == 0 || (cd.index + 1) % 5 == 0 {
                cc.draw(Text("\(cd.index + 1)").font(.system(size: 9.5, weight: .semibold, design: .monospaced)).foregroundColor(.secondary),
                        at: CGPoint(x: (l + r) / 2, y: yy + 38))
            }
        }
        // (the context the scanning ribosome stops at: Kozak in a eukaryote; where a Shine–Dalgarno site would be in a bacterium)
        if notice.contains("kozak"), let k = g.kozak {
            let l = x(g.column(mature: Double(k)) - 0.5), r = x(g.column(mature: Double(k + 9)) + 0.5)
            c.stroke(Path(roundedRect: CGRect(x: l, y: yy - 14, width: r - l, height: 28), cornerRadius: 6), with: .color(Theme.accent), style: StrokeStyle(lineWidth: 1.8, dash: [4, 3]))
            text(&c, "Kozak context", CGPoint(x: l - 6, y: yy - 20), size: 10.5, weight: .bold, colour: Theme.accent, anchor: .trailing, alpha: a)
        }
        if notice.contains("sd"), s0 >= 9 {
            let l = x(g.column(mature: Double(s0 - 10)) - 0.5), r = x(g.column(mature: Double(s0 - 5)) + 0.5)
            c.stroke(Path(roundedRect: CGRect(x: l, y: yy - 14, width: r - l, height: 28), cornerRadius: 6), with: .color(Theme.accent), style: StrokeStyle(lineWidth: 1.8, dash: [4, 3]))
            text(&c, "Shine–Dalgarno region", CGPoint(x: l - 6, y: yy - 20), size: 10.5, weight: .bold, colour: Theme.accent, anchor: .trailing, alpha: a)
        }
    }

    private var apart: Double { g.hasStop ? f.freed : 0 }

    private func ribosome(_ c: inout GraphicsContext) {
        guard g.cdsStart != nil, f.small > 0.01 || f.large > 0.01 else { return }
        let codonW = 3 * cell
        let yy = y(GXPainter.y4)
        let joined = f.large > 0.01 || f.stage > .tlInit
        let cx = x(joined ? g.codonColumn(s.ribosome) : g.column(mature: f.smallAt))
        let fade = (1 - 0.55 * apart) * (1 - f.result)
        let colour = GXColors.ribosome.color
        // (the small subunit, under the mRNA)
        let sw = 4 * codonW
        let small = Path(roundedRect: CGRect(x: cx - sw / 2, y: yy + 9 + CGFloat(14 * apart), width: sw, height: 21), cornerRadius: 10.5)
        var sc = c
        sc.opacity *= f.small * fade
        sc.fill(small, with: .color(colour.opacity(0.45)))
        sc.stroke(small, with: .color(colour.opacity(0.9)), lineWidth: 1.2)
        if f.large < 0.99 {
            text(&c, "small subunit", CGPoint(x: cx, y: yy + 19.5 + CGFloat(14 * apart)), size: 10, weight: .bold, colour: .primary,
                 alpha: f.small * fade * 0.8 * (1 - f.large))
        }
        // (the large subunit, over it, with its E, P and A sites one codon each: their letters on the small subunit, under
        // the codon each holds)
        guard f.large > 0.01 else { return }
        let lw = 4.5 * codonW
        let drop = CGFloat((1 - f.large) * 34 + 24 * apart)
        let rect = CGRect(x: cx - lw / 2, y: yy - 92 - drop, width: lw, height: 84)
        let large = Path(roundedRect: rect, cornerRadius: 26)
        var lc = c
        lc.opacity *= f.large * fade
        lc.fill(large, with: .color(colour.opacity(0.26)))
        lc.stroke(large, with: .color(colour.opacity(0.85)), lineWidth: 1.2)
        for (k, name) in [(-1.0, "E"), (0.0, "P"), (1.0, "A")] {
            let mid = x(g.codonColumn(s.ribosome + k))
            lc.draw(Text(name).font(.system(size: 12, weight: .heavy)).foregroundColor(.primary.opacity(0.75)),
                    at: CGPoint(x: mid, y: yy + 19.5 + CGFloat(14 * apart)))
            if k < 1 {
                var line = Path()
                let bx = mid + codonW / 2
                line.move(to: CGPoint(x: bx, y: rect.minY + 20))
                line.addLine(to: CGPoint(x: bx, y: rect.maxY - 6))
                lc.stroke(line, with: .color(colour.opacity(0.4)), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
            }
        }
        text(&c, "ribosome", CGPoint(x: rect.minX - 6, y: rect.minY + 12), size: 11, weight: .bold, colour: colour, anchor: .trailing, alpha: f.large * fade)
    }

    /// Where a tRNA's anticodon sits (over its codon, or lifted away up and to the side it comes from or leaves to).
    private func trnaAnchor(_ t: GXScene.TRNA) -> (x: CGFloat, y: CGFloat) {
        let x0 = x(g.codonColumn(Double(t.codon)))
        let dir: CGFloat = Double(t.codon) > s.ribosome + 0.01 ? 1 : (Double(t.codon) < s.ribosome - 0.01 ? -1 : 0.4)
        let lift = CGFloat(t.lift)
        return (x0 + dir * lift * 3 * cell * 0.9, y(GXPainter.y4) - 22 - lift * 60)
    }

    private func trna(_ c: inout GraphicsContext, _ t: GXScene.TRNA) {
        guard t.alpha > 0.01, g.codons.indices.contains(t.codon) else { return }
        let codon = g.codons[t.codon].codon
        let (cx, by) = trnaAnchor(t)
        let colour = GXColors.trna.color
        var cc = c
        cc.opacity *= t.alpha * (1 - f.result)
        // (a cloverleaf in small: the acceptor stem up to the amino acid, two side loops, the anticodon loop at the foot)
        var stem = Path()
        stem.move(to: CGPoint(x: cx, y: by - 10))
        stem.addLine(to: CGPoint(x: cx, y: by - 46))
        cc.stroke(stem, with: .color(colour.opacity(0.9)), lineWidth: 6)
        for dx in [-13.0, 13.0] {
            let loop = Path(ellipseIn: CGRect(x: cx + CGFloat(dx) - 6.5, y: by - 36, width: 13, height: 13))
            cc.fill(loop, with: .color(colour.opacity(0.18)))
            cc.stroke(loop, with: .color(colour.opacity(0.9)), lineWidth: 2)
        }
        let w = 3 * cell - 3
        let foot = Path(roundedRect: CGRect(x: cx - w / 2, y: by - 10, width: w, height: 20), cornerRadius: 6)
        cc.fill(foot, with: .color(colour.opacity(0.16)))
        cc.stroke(foot, with: .color(colour.opacity(0.9)), lineWidth: 1.5)
        // (the anticodon, 3′→5′ left to right, each base over the codon base it pairs with)
        for (k, b) in BioCode.anticodon3to5(codon).enumerated() {
            base(&cc, b, CGPoint(x: cx + CGFloat(k - 1) * cell, y: by))
        }
        // (the directions, on the tRNA in the P site)
        if t.lift < 0.05, abs(Double(t.codon) - s.ribosome) < 0.01, cell >= 12 {
            text(&cc, "3′", CGPoint(x: cx - w / 2 - 7, y: by), size: 9.5, weight: .bold)
            text(&cc, "5′", CGPoint(x: cx + w / 2 + 7, y: by), size: 9.5, weight: .bold)
            if t.initiator && f.stage == .tlInit {
                text(&cc, "initiator tRNA", CGPoint(x: cx + 22, y: by - 26), size: 10, weight: .bold, colour: colour, anchor: .leading)
            }
        }
    }

    private func releaseFactor(_ c: inout GraphicsContext) {
        guard let lift = s.releaseLift, f.releaseFactor > 0.01, g.hasStop, let stop = g.codons.last else { return }
        let x0 = x(g.codonColumn(Double(stop.index))) + CGFloat(lift) * 3 * cell * 0.9
        let by = y(GXPainter.y4) - 22 - CGFloat(lift) * 60
        let colour = GXColors.releaseFactor.color
        let w = 3 * cell - 3
        let body = Path(roundedRect: CGRect(x: x0 - w / 2, y: by - 50, width: w, height: 60), cornerRadius: 10)
        var cc = c
        cc.opacity *= f.releaseFactor * (1 - f.result)
        cc.fill(body, with: .color(colour.opacity(0.35)))
        cc.stroke(body, with: .color(colour), lineWidth: 1.5)
        cc.draw(Text("RF").font(.system(size: 12, weight: .heavy)).foregroundColor(colour), at: CGPoint(x: x0, y: by - 20))
        text(&c, "release factor (a protein)", CGPoint(x: x0 + w / 2 + 6, y: by - 40), size: 10.5, weight: .bold, colour: colour, anchor: .leading,
             alpha: f.releaseFactor * (1 - f.result))
    }

    /// Each amino acid of the chain: over the codon it came from, or on top of its tRNA.
    private func residuePoints() -> [CGPoint] {
        let row = y(GXPainter.y4) - 118 - CGFloat(18 * apart)
        return s.residues.map { r in
            let rowPt = CGPoint(x: x(g.codonColumn(r.x)), y: row)
            guard r.onTRNA > 0, let t = s.trnas.first(where: { $0.codon == r.index }) else { return rowPt }
            let a = trnaAnchor(t)
            let top = CGPoint(x: a.x, y: a.y - 58)
            let o = CGFloat(r.onTRNA)
            return CGPoint(x: rowPt.x + (top.x - rowPt.x) * o, y: rowPt.y + (top.y - rowPt.y) * o)
        }
    }

    private func chain(_ c: inout GraphicsContext) {
        guard !s.residues.isEmpty else { return }
        let pts = residuePoints()
        // (peptide bonds)
        for (k, r) in s.residues.enumerated() where k > 0 && r.bonded > 0.01 {
            var bond = Path()
            bond.move(to: pts[k - 1])
            bond.addLine(to: pts[k])
            c.stroke(bond, with: .color(Color.primary.opacity(0.45 * r.bonded * min(r.alpha, s.residues[k - 1].alpha))), lineWidth: 2.5)
        }
        for (k, r) in s.residues.enumerated() {
            let p = pts[k]
            guard p.x > left - 20 && p.x < right + 20, r.alpha > 0.01 else { continue }
            let colour = BioColor.aminoAcid(r.aa)
            var cc = c
            cc.opacity *= r.alpha
            let circle = Path(ellipseIn: CGRect(x: p.x - 11, y: p.y - 11, width: 22, height: 22))
            cc.fill(circle, with: .color(colour))
            cc.stroke(circle, with: .color(Color.white.opacity(0.7)), lineWidth: 1)
            cc.draw(Text(String(r.aa)).font(.system(size: 12, weight: .heavy, design: .rounded))
                .foregroundColor(GXColors.letter(on: BioPalette.aminoAcid(one: r.aa), dark: dark)), at: p)
        }
        if let p0 = pts.first, s.residues.count > 1 || f.stage >= .tlEnd {
            text(&c, "N-terminus", CGPoint(x: p0.x - 16, y: p0.y), size: 10, weight: .bold, anchor: .trailing)
        }
        if f.result > 0.01, let pl = pts.last {
            text(&c, "C-terminus", CGPoint(x: pl.x + 16, y: pl.y), size: 10, weight: .bold, anchor: .leading, alpha: f.result)
        }
    }

    /// In a bacterium a ribosome can start on the RNA while it is still being made: sketched once the start codon is out.
    private func coupling(_ c: inout GraphicsContext) {
        guard g.cell == .prokaryote, f.stage == .txElong, let s0 = g.cdsStart, f.made >= Double(s0 + GXProcess.couplingLag) else { return }
        let pl = place(s0 + 1)
        let sw = 8 * cell
        let shape = Path(roundedRect: CGRect(x: pl.x - sw / 2, y: pl.y + 9, width: sw, height: 22), cornerRadius: 11)
        c.fill(shape, with: .color(GXColors.ribosome.color.opacity(0.18)))
        c.stroke(shape, with: .color(GXColors.ribosome.color), style: StrokeStyle(lineWidth: 1.4, dash: [4, 3]))
        text(&c, "a ribosome can start already", CGPoint(x: pl.x - sw / 2 + 2, y: pl.y + 42), size: 10.5, weight: .bold,
             colour: GXColors.ribosome.color, anchor: .leading)
    }

    // MARK: A tooltip on an amino acid or a codon

    private func tooltip(_ c: inout GraphicsContext) {
        guard let h = hover else { return }
        var line: String?
        let pts = residuePoints()
        if let k = pts.indices.first(where: { hypot(pts[$0].x - h.x, pts[$0].y - h.y) < 12 }) {
            let r = s.residues[k]
            if let aa = BioAminoAcids.byOne[r.aa], g.codons.indices.contains(r.index) {
                line = "\(aa.three) (\(aa.name.lowercased()), \(aa.group.label.lowercased())) · codon \(r.index + 1), \(g.codons[r.index].codon)"
            }
        } else if f.export > 0.9, let s0 = g.cdsStart, abs(h.y - y(GXPainter.y4)) < 13 {
            let col = Double((h.x - x0) / cell)
            let m = Int((col - Double(g.txStart)).rounded()) - s0
            if m >= 0, m / 3 < g.codons.count {
                let cd = g.codons[m / 3]
                let what = cd.aa == "*" ? "stop" : (BioAminoAcids.byOne[cd.aa].map { "\($0.three) (\($0.name.lowercased()))" } ?? "?")
                line = "Codon \(cd.index + 1): \(cd.codon) → \(what)"
            }
        }
        guard let line else { return }
        let t = c.resolve(Text(line).font(.system(size: 11.5, weight: .medium)).foregroundColor(.primary))
        let sz = t.measure(in: CGSize(width: 400, height: 40))
        var at = CGPoint(x: h.x + 14, y: h.y - 26)
        at.x = min(at.x, right - sz.width - 12)
        let rect = CGRect(x: at.x - 8, y: at.y - sz.height / 2 - 5, width: sz.width + 16, height: sz.height + 10)
        c.fill(Path(roundedRect: rect, cornerRadius: rect.height / 2), with: .color(Theme.card))
        c.stroke(Path(roundedRect: rect, cornerRadius: rect.height / 2), with: .color(Color.primary.opacity(0.18)), lineWidth: 1)
        c.draw(t, at: at, anchor: .leading)
    }

    // MARK: The lanes' names

    private func gutterLabels(_ c: inout GraphicsContext) {
        let gx = GXPainter.gutter - 2
        func label(_ title: String, _ sub: String?, _ yy: CGFloat, alpha: Double = 1) {
            guard alpha > 0.01 else { return }
            text(&c, title, CGPoint(x: gx, y: sub == nil ? yy : yy - 7), size: 11.5, weight: .semibold, colour: .primary, anchor: .trailing, alpha: alpha * 0.85)
            if let sub { text(&c, sub, CGPoint(x: gx, y: yy + 7), size: 10.5, weight: .medium, colour: .secondary, anchor: .trailing, alpha: alpha) }
        }
        label("Whole gene", nil, y(GXPainter.yOverview))
        label("Coding (sense) strand", "5′→3′", y(GXPainter.y1))
        label("Template (antisense)", "strand 3′→5′", y(GXPainter.y2))
        let made = (s.incoming ?? Int(f.made + 1e-6)) > 0 || s.incoming != nil
        if made {
            let mature = g.introns.isEmpty || f.splice > 0.5
            label(mature ? "mRNA" : "pre-mRNA", "5′→3′", y(GXPainter.y3), alpha: 1 - f.export)
        }
        if g.cell == .eukaryote {
            text(&c, "NUCLEUS ↑", CGPoint(x: gx, y: y(GXPainter.yEnvelope) - 11), size: 10, weight: .bold, anchor: .trailing)
            text(&c, "CYTOPLASM ↓", CGPoint(x: gx, y: y(GXPainter.yEnvelope) + 11), size: 10, weight: .bold, anchor: .trailing)
        } else {
            label("No nucleus:", "all in the cytoplasm", y(GXPainter.yEnvelope))
        }
        if f.export > 0.01 {
            label("mRNA 5′→3′", "the ribosome's lane", y(GXPainter.y4), alpha: min(f.export * 1.5, 1))
        }
    }
}
