import SwiftUI

// DNA & RNA's Replication page (spec §4.2 Replication): a replication fork drawn left to right, stepped through with
// the shared controls (§3.6: ← → Space Home End, 1–9 for the nine steps; 600 ms between keyframes, a crossfade under
// Reduce Motion). The drawing is made from the step's keyframe (DNAReplication.frames): the parental strands in two
// shades, the new DNA in violet and the RNA primers in orange, each new piece with an arrowhead at its 3′ end, every
// strand's 5′ and 3′, and the enzymes at work — helicase at the fork, topoisomerase ahead, single-strand binding
// proteins on the single strands, primase, DNA polymerase III, DNA polymerase I and ligase — with a close-up of the
// bases pairing as polymerase adds one.

struct DNAReplicationPage: View {
    @ObservedObject var store: DNAStore
    @ObservedObject var steps: BioStepController
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        PTWorkLayout {
            left
        } centre: {
            centre
        } right: {
            right
        }
    }

    private var groups: [Int: String] {
        var g: [Int: String] = [:]
        for s in steps.steps { if let n = s.group { g[n] = s.title } }
        return g
    }

    // MARK: Middle: the fork and the controls

    private var centre: some View {
        let dark = scheme == .dark
        let current = steps.current
        return VStack(spacing: 14) {
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .firstTextBaseline) {
                    Text("Replication fork").font(.system(size: 22, weight: .bold))
                    Text(current?.title ?? "").font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                    Spacer()
                }
                .padding(16)
                TimelineView(.animation(minimumInterval: nil, paused: !steps.moving)) { timeline in
                    let phase = steps.phase(at: timeline.date)
                    Canvas { ctx, size in DNAForkDrawing.draw(&ctx, size: size, phase: phase, dark: dark) }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .padding(.horizontal, 8)
                .padding(.bottom, 8)
                .contentShape(Rectangle())
                .bioStepKeys(steps)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Replication fork, \(BioStepTiming.caption(steps.index, of: steps.steps)). \(current?.text ?? "")")
            }
            .frame(minHeight: 420)
            .card(radius: 20)
            BioStepControls(controller: steps, groups: groups)
        }
    }

    // MARK: Left: the key and the enzymes

    private var left: some View {
        let dark = scheme == .dark
        let acting = Set(DNAReplication.enzymes[min(steps.index, DNAReplication.enzymes.count - 1)])
        return VStack(alignment: .leading, spacing: 14) {
            BioHeading("Strands")
            BioLegend(items: [(Color(hex: DNAForkDrawing.parentalTop.hex(dark: dark)), "Parental strand (old)"),
                              (Color(hex: DNAForkDrawing.parentalBottom.hex(dark: dark)), "Parental strand (old), its partner"),
                              (BioPalette.strand1.color, "New DNA"),
                              (BioPalette.rna.color, "RNA primer")])
            BioHeading("Enzymes")
            VStack(alignment: .leading, spacing: 9) {
                ForEach(DNAReplication.enzymeRoles.indices, id: \.self) { i in
                    let e = DNAReplication.enzymeRoles[i]
                    let on = acting.contains(e.name)
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Circle().fill(Color(hex: DNAForkDrawing.colour(e.name).hex(dark: dark))).frame(width: 9, height: 9)
                            .alignmentGuide(.firstTextBaseline) { d in d[VerticalAlignment.center] + 4 }
                        VStack(alignment: .leading, spacing: 1) {
                            Text(e.name).font(.sCallout.weight(on ? .bold : .medium)).foregroundStyle(on ? Color.primary : Color.secondary)
                            Text(e.role).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    .padding(.vertical, 2)
                    .padding(.horizontal, 6)
                    .background(on ? Theme.accent.opacity(0.12) : Color.clear, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                    .accessibilityElement(children: .combine)
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
            Text("← → step · Space plays · 1–9 jump to a step")
                .font(.sCaption).foregroundStyle(.secondary)
        }
        .font(.sCallout)
    }

    // MARK: Right: the step's card and the notes

    private var right: some View {
        let i = steps.index
        let enzymes = DNAReplication.enzymes[min(i, DNAReplication.enzymes.count - 1)]
        return VStack(alignment: .leading, spacing: 14) {
            if let s = steps.current {
                BioCard(title: s.title, subtitle: "Step \(i + 1) of \(steps.steps.count)") {
                    BioFactRow(label: "What happens", text: s.text)
                    if !enzymes.isEmpty { BioFactRow(label: enzymes.count == 1 ? "Enzyme" : "Enzymes", text: enzymes.joined(separator: ", ")) }
                    if !s.notice.isEmpty, enzymes.isEmpty { BioFactRow(label: "What to notice", text: s.notice.joined(separator: "; ")) }
                }
            }
            BioCard(title: "Two ways to build a strand", subtitle: "Polymerase only adds to a 3′ end") {
                BioFactRow(label: "Leading strand", text: "Its template runs 3′→5′ toward the fork, so the new strand grows 5′→3′ toward the fork, in one piece.")
                BioFactRow(label: "Lagging strand", text: "Its template runs the other way, so the new strand grows 5′→3′ away from the fork, in Okazaki fragments, each from its own primer.")
            }
            BioCard(title: "Where the energy comes from") {
                Text("Nucleotides arrive as dNTPs (deoxynucleoside triphosphates). Polymerase joins one to the strand's 3′ end and releases pyrophosphate (two phosphates), and that pays for the bond. The Biomolecule Builder's “dehydration” (a water lost) is the textbook simplification of the same phosphodiester bond.")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
            }
            Text("Real origins open two forks that move apart; this shows one. Bacteria have one origin; our chromosomes have thousands.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}

// MARK: - The drawing

/// The fork drawn from a step's keyframe (or between two): Foundation numbers in, a Canvas out.
enum DNAForkDrawing {
    static let parentalTop = BioHexPair("#4a5878", "#a7b6d6")
    static let parentalBottom = BioHexPair("#8592ad", "#d0d9ec")

    /// Each enzyme's colour.
    static func colour(_ name: String) -> BioHexPair {
        switch name {
        case "Helicase": return BioHexPair("#d9a406", "#ffd60a")
        case "Topoisomerase": return BioHexPair("#14b8a6", "#5eead4")
        case "Single-strand binding proteins": return BioHexPair("#38bdf8", "#7dd3fc")
        case "Primase": return BioHexPair("#e8508a", "#ff7aa8")
        case "DNA polymerase III": return BioHexPair("#2563eb", "#7aa2ff")
        case "DNA polymerase I": return BioHexPair("#a855f7", "#c084fc")
        default: return BioHexPair("#2fb67c", "#5ee0a8")
        }
    }

    /// The width of the fork's opening along the molecule.
    static let spread = 0.06

    static func draw(_ ctx: inout GraphicsContext, size: CGSize, phase: BioStepPhase, dark: Bool) {
        let frames = DNAReplication.frames
        let a = frames[min(max(phase.from, 0), frames.count - 1)], b = frames[min(max(phase.to, 0), frames.count - 1)]
        if phase.crossfade && phase.from != phase.to {
            ctx.drawLayer { layer in
                layer.opacity = 1 - phase.t
                drawFrame(&layer, size: size, f: a, dark: dark)
            }
            ctx.drawLayer { layer in
                layer.opacity = phase.t
                drawFrame(&layer, size: size, f: b, dark: dark)
            }
        } else {
            drawFrame(&ctx, size: size, f: DNAForkFrame.mix(a, b, phase.t), dark: dark)
        }
    }

    static func drawFrame(_ ctx: inout GraphicsContext, size: CGSize, f: DNAForkFrame, dark: Bool) {
        let mainH = size.height * 0.70
        let x0: CGFloat = 46, x1 = max(size.width - 46, 100)
        let yc = mainH * 0.5 + 4
        let g0: CGFloat = 8, gap: CGFloat = 16
        let open = mainH * 0.30 + CGFloat(f.apart) * mainH * 0.08
        let fork = f.fork * (1 + spread) - spread / 2
        func X(_ x: Double) -> CGFloat { x0 + CGFloat(x) * (x1 - x0) }
        func sep(_ x: Double) -> CGFloat {
            let u = min(max((fork + spread / 2 - x) / spread, 0), 1)
            return CGFloat(u * u * (3 - 2 * u))
        }
        func topY(_ x: Double) -> CGFloat { yc - (g0 + (open - g0) * sep(x)) }
        func bottomY(_ x: Double) -> CGFloat { yc + (g0 + (open - g0) * sep(x)) }
        let behind = fork - spread / 2
        func hex(_ p: BioHexPair) -> Color { Color(hex: p.hex(dark: dark)) }
        let newDNA = hex(BioPalette.strand1), rna = hex(BioPalette.rna)
        let rung = Color.secondary.opacity(0.55)

        func strand(_ from: Double, _ to: Double, _ y: (Double) -> CGFloat, _ colour: Color, width: CGFloat = 3) {
            guard to > from else { return }
            var p = Path()
            let n = max(2, Int((to - from) * 160))
            for k in 0...n {
                let x = from + (to - from) * Double(k) / Double(n)
                let pt = CGPoint(x: X(x), y: y(x))
                if k == 0 { p.move(to: pt) } else { p.addLine(to: pt) }
            }
            ctx.stroke(p, with: .color(colour), style: StrokeStyle(lineWidth: width, lineCap: .round, lineJoin: .round))
        }
        func arrow(at x: Double, _ y: (Double) -> CGFloat, right: Bool, _ colour: Color) {
            let tip = CGPoint(x: X(x) + (right ? 7 : -7), y: y(x))
            var p = Path()
            p.move(to: tip)
            p.addLine(to: CGPoint(x: tip.x + (right ? -10 : 10), y: tip.y - 5.5))
            p.addLine(to: CGPoint(x: tip.x + (right ? -10 : 10), y: tip.y + 5.5))
            p.closeSubpath()
            ctx.fill(p, with: .color(colour))
        }
        func label(_ s: String, _ at: CGPoint, size: CGFloat = 10.5, weight: Font.Weight = .semibold, colour: Color = .primary, anchor: UnitPoint = .center) {
            ctx.draw(Text(s).font(.system(size: size, weight: weight)).foregroundColor(colour), at: at, anchor: anchor)
        }
        func blob(_ centre: CGPoint, w: CGFloat, h: CGFloat, _ colour: Color, opacity: Double) {
            let r = CGRect(x: centre.x - w / 2, y: centre.y - h / 2, width: w, height: h)
            ctx.fill(Path(roundedRect: r, cornerRadius: min(w, h) / 2.4), with: .color(colour.opacity(0.88 * opacity)))
            ctx.stroke(Path(roundedRect: r, cornerRadius: min(w, h) / 2.4), with: .color(colour.opacity(opacity)), lineWidth: 1.2)
        }

        // (where the new strands are: the leading strand's DNA and primer; each fragment's DNA and primer)
        let leadEnd = f.fork >= 1 ? 1.0 : min(f.leadingTo, behind - spread * 0.4)
        let laggingPieces: [(lo: Double, hi: Double, primerFrom: Double, fragment: DNAOkazaki)] = f.fragments.compactMap { fr in
            guard fr.grown > 0 else { return nil }
            let hi = f.fork >= 1 ? fr.hi : min(fr.hi, behind - spread * 0.3)
            let lo = fr.threePrime
            guard hi > lo else { return nil }
            return (lo, hi, fr.hi - DNAReplication.primerLength, fr)
        }
        func laggingCovers(_ x: Double) -> Bool {
            laggingPieces.contains { x >= $0.lo && x <= $0.hi } || (f.apart > 0.5 && x >= (laggingPieces.last?.hi ?? 1))
        }
        func leadingCovers(_ x: Double) -> Bool { (x <= DNAReplication.leadingPrimerEnd && f.leadingPrimer > 0.5) || (x <= leadEnd && leadEnd > DNAReplication.leadingPrimerEnd) }

        // Base-pair rungs: across the helix ahead of the fork; to the new strands behind it; stubs where unpaired.
        let rungs = 56
        for k in 0..<rungs {
            let x = (Double(k) + 0.5) / Double(rungs)
            var p = Path()
            if sep(x) < 0.5 {
                p.move(to: CGPoint(x: X(x), y: topY(x)))
                p.addLine(to: CGPoint(x: X(x), y: bottomY(x)))
            } else {
                p.move(to: CGPoint(x: X(x), y: topY(x)))
                p.addLine(to: CGPoint(x: X(x), y: topY(x) + (laggingCovers(x) ? gap : 5)))
                p.move(to: CGPoint(x: X(x), y: bottomY(x)))
                p.addLine(to: CGPoint(x: X(x), y: bottomY(x) - (leadingCovers(x) ? gap : 5)))
            }
            ctx.stroke(p, with: .color(rung), lineWidth: 1.2)
        }

        // The parental strands: top 5′→3′ left to right, bottom 3′→5′.
        strand(0, 1, topY, hex(parentalTop))
        strand(0, 1, bottomY, hex(parentalBottom))
        label("5′", CGPoint(x: X(0) - 14, y: topY(0)), size: 12, weight: .bold, colour: hex(parentalTop))
        label("3′", CGPoint(x: X(1) + 14, y: topY(1)), size: 12, weight: .bold, colour: hex(parentalTop))
        label("3′", CGPoint(x: X(0) - 14, y: bottomY(0)), size: 12, weight: .bold, colour: hex(parentalBottom))
        label("5′", CGPoint(x: X(1) + 14, y: bottomY(1)), size: 12, weight: .bold, colour: hex(parentalBottom))
        // (the origin marked while nothing else is working at that end)
        let originShown = max(0, 1 - f.polIII - f.apart)
        if originShown > 0.01 {
            var tri = Path()
            let o = CGPoint(x: X(0.0) + 4, y: topY(0) - 12)
            tri.move(to: CGPoint(x: o.x - 5, y: o.y - 6))
            tri.addLine(to: CGPoint(x: o.x + 5, y: o.y - 6))
            tri.addLine(to: CGPoint(x: o.x, y: o.y + 1))
            tri.closeSubpath()
            ctx.fill(tri, with: .color(Color.secondary.opacity(originShown)))
            label("Origin", CGPoint(x: o.x + 9, y: o.y - 3), colour: Color.secondary.opacity(originShown), anchor: .leading)
        }

        // The new strands: the leading strand (under the top's partner) and the lagging fragments, primers in orange
        // until DNA polymerase I replaces them.
        let leadingY: (Double) -> CGFloat = { bottomY($0) - gap }
        let laggingY: (Double) -> CGFloat = { topY($0) + gap }
        let primerColour = f.primersReplaced >= 1 ? newDNA : (f.primersReplaced <= 0 ? rna : rna.opacity(1 - f.primersReplaced))
        let primerWidth: CGFloat = f.primersReplaced >= 1 ? 3 : 4
        if f.leadingPrimer > 0 {
            if f.primersReplaced > 0 { strand(0, DNAReplication.leadingPrimerEnd, leadingY, newDNA.opacity(f.primersReplaced), width: 3) }
            strand(0, DNAReplication.leadingPrimerEnd, leadingY, primerColour.opacity(f.leadingPrimer), width: primerWidth)
        }
        if leadEnd > DNAReplication.leadingPrimerEnd + 0.004 {
            strand(DNAReplication.leadingPrimerEnd, leadEnd, leadingY, newDNA)
            arrow(at: leadEnd, leadingY, right: true, newDNA)
            label("5′", CGPoint(x: X(0) - 14, y: leadingY(0)), size: 11, weight: .bold, colour: newDNA)
            if leadEnd - DNAReplication.leadingPrimerEnd > 0.18 {
                label("Leading strand", CGPoint(x: X((DNAReplication.leadingPrimerEnd + leadEnd) / 2), y: leadingY(leadEnd / 2) - 14), colour: newDNA)
            }
        }
        for piece in laggingPieces {
            let primerOn = piece.fragment.primer
            let dnaTo = min(piece.primerFrom, piece.hi)
            if dnaTo > piece.lo { strand(piece.lo, dnaTo, laggingY, newDNA) }
            if primerOn > 0, piece.hi > piece.primerFrom {
                let from = max(piece.primerFrom, piece.lo)
                if f.primersReplaced > 0 { strand(from, piece.hi, laggingY, newDNA.opacity(f.primersReplaced), width: 3) }
                strand(from, piece.hi, laggingY, primerColour.opacity(primerOn), width: primerWidth)
            }
            if f.sealed < 0.5 || piece.fragment.lo == DNAReplication.fragmentSpans[0].lo {
                arrow(at: piece.lo, laggingY, right: false, newDNA)
            }
        }
        // (copying carries on to the end: the finished lagging strand is drawn whole)
        if f.apart > 0, let last = laggingPieces.last, last.hi < 1 {
            strand(last.hi, 1, laggingY, newDNA.opacity(f.apart))
        }
        if let first = laggingPieces.first, let last = laggingPieces.last, last.hi - first.lo > 0.15 {
            let text = laggingPieces.count > 1 && f.sealed < 0.5 ? "Lagging strand: Okazaki fragments" : "Lagging strand"
            // (below the enzymes working on the strand, when there are any)
            let below: CGFloat = f.polI > 0.01 || f.ligase > 0.01 ? 44 : 16
            label(text, CGPoint(x: X((first.lo + last.hi) / 2), y: laggingY((first.lo + last.hi) / 2) + below), colour: newDNA)
        }

        // Nicks between pieces once the primers are gone, until ligase seals them.
        if f.primersReplaced > 0.5 && f.sealed < 1 {
            for (k, x) in DNAReplication.nicks.enumerated() {
                let y = k == 0 ? leadingY(x) : laggingY(x)
                let r = CGRect(x: X(x) - 2, y: y - 4, width: 4, height: 8)
                ctx.fill(Path(r), with: .color(Theme.card.opacity(1 - f.sealed)))
                if k == 1 { label("nick", CGPoint(x: X(x) - 10, y: y + 12), size: 10, weight: .medium, colour: Color.secondary.opacity(1 - f.sealed), anchor: .trailing) }
            }
        }

        // Enzymes.
        let forkX = min(max(f.fork * (1 + spread) - spread / 2, 0), 1)
        if f.topoisomerase > 0.01 {
            let c = CGPoint(x: X(min(forkX + 0.09, 1.0)), y: yc)
            blob(c, w: 26, h: 20, hex(colour("Topoisomerase")), opacity: f.topoisomerase)
            label("Topoisomerase", CGPoint(x: c.x, y: c.y - 21), colour: Color.primary.opacity(f.topoisomerase))
        }
        if f.helicase > 0.01 {
            let c = CGPoint(x: X(forkX), y: yc)
            var hexagon = Path()
            for k in 0..<6 {
                let a = Double(k) * .pi / 3 + .pi / 6
                let pt = CGPoint(x: c.x + 15 * CGFloat(cos(a)), y: c.y + 15 * CGFloat(sin(a)))
                if k == 0 { hexagon.move(to: pt) } else { hexagon.addLine(to: pt) }
            }
            hexagon.closeSubpath()
            let col = hex(colour("Helicase"))
            ctx.fill(hexagon, with: .color(col.opacity(0.85 * f.helicase)))
            ctx.stroke(hexagon, with: .color(col.opacity(f.helicase)), lineWidth: 1.4)
            // (inside the fork's opening, left of it)
            label("Helicase", CGPoint(x: c.x - 20, y: c.y), colour: Color.primary.opacity(f.helicase), anchor: .trailing)
        }
        if f.ssb > 0.01 {
            // (on each template where it is single-stranded: behind the fork, not yet copied)
            let col = hex(colour("Single-strand binding proteins"))
            var first = true
            for top in [true, false] {
                var x = 0.04
                while x < behind - 0.03 {
                    let free = top ? !laggingCovers(x) : !leadingCovers(x)
                    if free && sep(x) > 0.9 {
                        let c = CGPoint(x: X(x), y: top ? topY(x) + 6 : bottomY(x) - 6)
                        ctx.fill(Path(ellipseIn: CGRect(x: c.x - 5, y: c.y - 5, width: 10, height: 10)), with: .color(col.opacity(f.ssb)))
                        if first {
                            // (above the strand, or under it where the origin's mark is above — no new strand is there yet)
                            let y = top ? (originShown > 0.5 && x < 0.2 ? topY(x) + 21 : topY(x) - 12) : bottomY(x) + 13
                            label("SSB", CGPoint(x: c.x, y: y), size: 10, colour: Color.secondary.opacity(f.ssb))
                            first = false
                        }
                    }
                    x += 0.035
                }
            }
        }
        if f.primase > 0.01, let newest = f.fragments.filter({ $0.primer > 0 }).max(by: { $0.hi < $1.hi }) {
            let x = min(newest.hi, behind - spread * 0.3) - DNAReplication.primerLength / 2
            let c = CGPoint(x: X(x), y: laggingY(x) + 14)
            blob(c, w: 24, h: 16, hex(colour("Primase")), opacity: f.primase)
            label("Primase", CGPoint(x: c.x, y: c.y + 17), colour: Color.primary.opacity(f.primase))
        }
        if f.polIII > 0.01 {
            let col = hex(colour("DNA polymerase III"))
            if leadEnd > DNAReplication.leadingPrimerEnd {
                let c = CGPoint(x: X(leadEnd), y: leadingY(leadEnd) + gap / 2)
                blob(c, w: 30, h: 26, col, opacity: f.polIII)
                label("DNA pol III", CGPoint(x: c.x, y: c.y + 24), colour: Color.primary.opacity(f.polIII))
            }
            if let growing = laggingPieces.first(where: { $0.fragment.grown < 1 }) {
                let c = CGPoint(x: X(growing.lo), y: laggingY(growing.lo) - gap / 2)
                blob(c, w: 30, h: 26, col, opacity: f.polIII)
                label("DNA pol III", CGPoint(x: c.x, y: c.y - 22), colour: Color.primary.opacity(f.polIII))
            }
        }
        if f.polI > 0.01 {
            let col = hex(colour("DNA polymerase I"))
            for (k, x) in DNAReplication.nicks.enumerated() {
                let mid = x + DNAReplication.primerLength / 2 * (k == 0 ? -1 : 1)
                let y = k == 0 ? leadingY(mid) : laggingY(mid)
                let c = CGPoint(x: X(mid), y: y + (k == 0 ? -12 : 12))
                blob(c, w: 22, h: 16, col, opacity: f.polI)
                if k == 1 { label("DNA pol I", CGPoint(x: c.x, y: c.y + 17), colour: Color.primary.opacity(f.polI)) }
            }
        }
        if f.ligase > 0.01 {
            let col = hex(colour("DNA ligase"))
            for (k, x) in DNAReplication.nicks.enumerated() {
                let y = k == 0 ? leadingY(x) : laggingY(x)
                let c = CGPoint(x: X(x), y: y + (k == 0 ? -12 : 12))
                ctx.fill(Path(ellipseIn: CGRect(x: c.x - 8, y: c.y - 8, width: 16, height: 16)), with: .color(col.opacity(0.88 * f.ligase)))
                if k == 1 { label("DNA ligase", CGPoint(x: c.x, y: c.y + 18), colour: Color.primary.opacity(f.ligase)) }
            }
        }
        // The two new molecules, each one old strand and one new.
        if f.apart > 0.01 {
            label("Molecule 1: old strand + new strand", CGPoint(x: X(1), y: (topY(1) + laggingY(1)) / 2 - 22), size: 11.5,
                  colour: Color.primary.opacity(f.apart), anchor: .trailing)
            label("Molecule 2: new strand + old strand", CGPoint(x: X(1), y: (bottomY(1) + leadingY(1)) / 2 + 22), size: 11.5,
                  colour: Color.primary.opacity(f.apart), anchor: .trailing)
        }

        if f.closeUp > 0.01 { closeUp(&ctx, size: size, top: mainH, opacity: f.closeUp, dark: dark) }
    }

    /// The close-up: the template read 3′→5′, the new strand built 5′→3′, the next nucleotide arriving as a
    /// triphosphate to pair with its partner.
    static func closeUp(_ ctx: inout GraphicsContext, size: CGSize, top: CGFloat, opacity: Double, dark: Bool) {
        let made = Array(DNAReplication.closeUpNew)
        let template = Array(BioSeq.complement(DNAReplication.closeUpNew))
        let k = DNAReplication.closeUpMade
        let tile: CGFloat = 22, step: CGFloat = 27
        let width = step * CGFloat(made.count) + 120
        let box = CGRect(x: (size.width - width) / 2, y: top + 4, width: width, height: max(size.height - top - 10, 90))
        ctx.drawLayer { layer in
            layer.opacity = opacity
            layer.fill(Path(roundedRect: box, cornerRadius: 12), with: .color(Color.primary.opacity(0.06)))
            let incoming = made[k], partner = template[k]
            layer.draw(Text("Close-up: DNA polymerase III adds \(String(incoming)) opposite \(String(partner)), at the new strand's 3′ end")
                        .font(.system(size: 11.5, weight: .semibold)).foregroundColor(.primary),
                       at: CGPoint(x: box.midX, y: box.minY + 13))
            let left = box.minX + 60
            let yNew = box.minY + 58, yTemplate = yNew + 36
            func tileAt(_ i: Int, _ y: CGFloat, _ letter: Character, faint: Bool = false) {
                let r = CGRect(x: left + CGFloat(i) * step, y: y - tile / 2, width: tile, height: tile)
                layer.fill(Path(roundedRect: r, cornerRadius: 5), with: .color(BioColor.base(letter).opacity(faint ? 0.18 : 0.3)))
                layer.draw(Text(String(letter)).font(.system(size: 13, weight: .bold, design: .monospaced)).foregroundColor(BioColor.base(letter)),
                           at: CGPoint(x: r.midX, y: r.midY))
            }
            for i in 0..<template.count { tileAt(i, yTemplate, template[i]) }
            for i in 0..<k {
                tileAt(i, yNew, made[i])
                var p = Path()
                p.move(to: CGPoint(x: left + CGFloat(i) * step + tile / 2, y: yNew + tile / 2))
                p.addLine(to: CGPoint(x: left + CGFloat(i) * step + tile / 2, y: yTemplate - tile / 2))
                layer.stroke(p, with: .color(BioPalette.hBond.color), style: StrokeStyle(lineWidth: 1.2, dash: [2.5, 2.5]))
            }
            // (the next one, arriving from above with its two extra phosphates, which leave as pyrophosphate)
            tileAt(k, yNew - 20, incoming, faint: true)
            var drop = Path()
            drop.move(to: CGPoint(x: left + CGFloat(k) * step + tile / 2, y: yNew - 20 + tile / 2 + 1))
            drop.addLine(to: CGPoint(x: left + CGFloat(k) * step + tile / 2, y: yNew - 2))
            layer.stroke(drop, with: .color(.secondary), lineWidth: 1)
            layer.draw(Text("+ PPᵢ released").font(.system(size: 10, weight: .semibold)).foregroundColor(.secondary),
                       at: CGPoint(x: left + CGFloat(k) * step + tile + 6, y: yNew - 20), anchor: .leading)
            func end(_ s: String, _ y: CGFloat) {
                layer.draw(Text(s).font(.system(size: 11, weight: .bold)).foregroundColor(.secondary), at: CGPoint(x: left - 22, y: y))
            }
            end("5′", yNew)
            end("3′", yTemplate)
            layer.draw(Text("3′").font(.system(size: 11, weight: .bold)).foregroundColor(.secondary),
                       at: CGPoint(x: left + CGFloat(k) * step + 1, y: yNew + 6), anchor: .leading)
            layer.draw(Text("5′").font(.system(size: 11, weight: .bold)).foregroundColor(.secondary),
                       at: CGPoint(x: left + CGFloat(template.count) * step + 10, y: yTemplate))
            layer.draw(Text("New strand, built 5′→3′ · template, read 3′→5′ · A pairs with T, G with C")
                        .font(.system(size: 10.5)).foregroundColor(.secondary),
                       at: CGPoint(x: box.midX, y: min(yTemplate + 26, box.maxY - 8)))
        }
    }
}
