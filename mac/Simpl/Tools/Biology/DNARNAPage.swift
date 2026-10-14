import SceneKit
import SwiftUI

// DNA & RNA's RNA page (spec §4.2 RNA): yeast tRNA-Phe as a cloverleaf (stems as ladders, loops as circles, the
// anticodon GAA pairing antiparallel with the codon UUC under it, the amino acid's place at A76, the real
// modifications as badges if asked) and folded into its L-shape in 3D (each arm in its colour, the ruler from the
// anticodon to the 3′ end); an mRNA as a labelled strip, cap to tail; and the ribosomes and their RNA. Bases and arms
// are pressed for their cards; ← and → step through the cloverleaf's bases.

struct DNARNAPage: View {
    @ObservedObject var store: DNAStore
    @Environment(\.colorScheme) private var scheme
    @State private var turning = false
    @State private var reset = 0

    var body: some View {
        PTWorkLayout {
            left
        } centre: {
            centre
        } right: {
            right
        }
    }

    // MARK: Left

    private var left: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Show")
            VStack(alignment: .leading, spacing: 6) {
                ForEach(DNARNAView.allCases, id: \.self) { v in
                    DNAChip(title: v.label, detail: subtitle(v), selected: store.rnaView == v, help: v.help) { store.rnaView = v }
                }
            }
            switch store.rnaView {
            case .cloverleaf: cloverleafOptions
            case .lShape: lShapeOptions
            case .mrna, .ribosomes: BioLegend(items: baseLegend)
            }
        }
        .font(.sCallout)
    }

    private func subtitle(_ v: DNARNAView) -> String {
        switch v {
        case .cloverleaf: return "Cloverleaf, in 2D"
        case .lShape: return "Folded, in 3D"
        case .mrna: return "Messenger RNA"
        case .ribosomes: return "Ribosomal RNA"
        }
    }

    /// The cloverleaf's elements, to pick from (the parts list), with the base each starts at.
    static let elements: [(name: String, base: Int)] = [("Acceptor stem", 1), ("D arm", 14), ("Anticodon arm", 28), ("Anticodon", 35),
                                                        ("Variable loop", 46), ("T arm (TΨC)", 56), ("3′ CCA end", 76)]

    private var cloverleafOptions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Toggle("Modified bases", isOn: $store.showModified).help("The real molecule's chemically changed bases, as badges")
            BioHeading("Parts")
            ForEach(Self.elements.indices, id: \.self) { k in
                let e = Self.elements[k]
                let on = store.trnaBase.map { DNARNA.element(of: $0).name == DNARNA.element(of: e.base).name } ?? false
                DNAChip(title: e.name, selected: on) { store.trnaBase = on ? nil : e.base }
            }
            BioLegend(items: baseLegend)
            Text("Click a base, or use ← and →.").font(.sCaption).foregroundStyle(.secondary)
        }
    }

    private var armLegend: [(color: Color, text: String)] {
        let dark = scheme == .dark
        var items: [(color: Color, text: String)] = []
        for arm in [DNATRNAArm.acceptor, .d, .anticodon, .variable, .t] { items.append((Color(hex: arm.colour.hex(dark: dark)), arm.name)) }
        items.append((Color(hex: DNATRNAArm.connector.colour.hex(dark: dark)), "Connectors (8–9, 26)"))
        return items
    }

    private var baseLegend: [(color: Color, text: String)] {
        let bases: [Character] = ["A", "U", "G", "C"]
        return bases.map { b in (color: BioColor.base(b), text: "\(b) \(DNAModel.baseName(b))") }
    }

    private var lShapeOptions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Toggle("Labels", isOn: $store.trnaLabels)
            BioHeading("Arms")
            BioLegend(items: armLegend)
            Text("The tube runs through each nucleotide's phosphorus atom (PDB 1EHZ); rungs stand for the paired bases.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Middle

    @ViewBuilder
    private var centre: some View {
        switch store.rnaView {
        case .cloverleaf:
            VStack(alignment: .leading, spacing: 0) {
                header("Transfer RNA: the cloverleaf", "Yeast tRNA-Phe, 76 nucleotides · it carries phenylalanine")
                DNACloverleafView(store: store)
                    .padding(.horizontal, 12)
                    .padding(.bottom, 12)
            }
            .card(radius: 20)
        case .lShape:
            lShape
        case .mrna:
            VStack(alignment: .leading, spacing: 0) {
                header("Messenger RNA", "The teaching gene's mRNA (made up), after processing")
                DNAMRNAStrip()
                    .frame(minHeight: 300)
                    .padding(.horizontal, 12)
                Text("The cap and tail are added in the nucleus; they protect the mRNA and help the ribosome find it. Translation starts at the first AUG the ribosome reaches (here at nucleotide \(DNARNA.cdsStart)), not at nucleotide 1, and ends at the stop codon.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(16)
            }
            .card(radius: 20)
        case .ribosomes:
            VStack(alignment: .leading, spacing: 0) {
                header("Ribosomes", "Made of rRNA and proteins, in two subunits")
                DNARibosomeDiagram()
                    .frame(minHeight: 280)
                    .padding(.horizontal, 12)
                    .padding(.bottom, 12)
            }
            .card(radius: 20)
        }
    }

    private func header(_ title: String, _ sub: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.system(size: 22, weight: .bold))
            Text(sub).font(.sCallout).foregroundStyle(.secondary)
        }
        .padding(16)
    }

    private var lShape: some View {
        let dark = scheme == .dark
        let key = "trna|\(dark)"
        return BioSceneCard(hint: "Drag to turn · scroll to zoom · click an arm to learn about it") {
            VStack(alignment: .leading, spacing: 2) {
                Text("Transfer RNA: the L-shape").font(.system(size: 22, weight: .bold))
                Text("Yeast tRNA-Phe as it folds (PDB 1EHZ)").font(.sCallout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            BioSceneButtons(turning: $turning) { reset += 1 }
        } scene: {
            BioSceneView(key: key,
                         build: BioSceneBuild {
                             let s = DNARNA.lShape(dark: dark)
                             DNAMarkShelf.shared.put(key, s.marks)
                             return s.content
                         },
                         extras: { DNASceneText.nodes(DNAMarkShelf.shared.take(key)) },
                         selection: $store.trnaSelection,
                         showLabels: store.trnaLabels,
                         turning: turning,
                         frameKey: "trna",
                         reset: reset,
                         accessibility: "3D model of transfer RNA folded into an L-shape; the anticodon and the amino acid's end are about 7 nanometres apart"
                             + (DNARNA.arm(store.trnaSelection).map { "; selected: \($0.name)" } ?? ""),
                         onPick: { store.pickTRNA($0) })
        }
    }

    // MARK: Right

    @ViewBuilder
    private var right: some View {
        VStack(alignment: .leading, spacing: 14) {
            switch store.rnaView {
            case .cloverleaf:
                if let b = store.trnaBase { elementCard(b) } else { trnaCard }
                codonCard
                if store.showModified { modifiedCard }
            case .lShape:
                if let arm = DNARNA.arm(store.trnaSelection) { armCard(arm) }
                lCard
                codonCard
            case .mrna:
                mrnaCard
            case .ribosomes:
                ribosomeCard
            }
        }
    }

    private var trnaCard: some View {
        BioCard(title: "Transfer RNA (tRNA)", swatch: BioPalette.rna.color, subtitle: "Brings amino acids to the ribosome") {
            BioFactRow(label: "Shape", text: "One RNA strand of 76 nucleotides that pairs with itself: four stems, three loops and a variable loop — a cloverleaf, folded in the cell into an L.")
            BioFactRow(label: "Two ends that matter", text: "The anticodon (bottom) pairs with a codon on the mRNA; the amino acid rides on the 3′ end (top right, A76).")
            BioFactRow(label: "This one", text: "Yeast tRNA for phenylalanine: anticodon GAA, so it reads the codon UUC (and UUU, by wobble).")
        }
    }

    private func elementCard(_ i: Int) -> some View {
        let e = DNARNA.element(of: i)
        let seq = Array(BioData.trnaSequence)
        let base = seq[i - 1]
        let mod = BioData.trna.modified.first { $0.position == i }
        return BioCard(title: e.name, swatch: BioColor.base(base), subtitle: e.positions.isEmpty ? nil : "Positions \(e.positions)") {
            BioFactRow(label: "Base picked", text: "\(base)\(i) (\(DNAModel.baseName(base)))" + (mod.map { " — in the real molecule, \($0.label)\(i): \(Self.modificationName($0.label))" } ?? ""))
            if !e.pairs.isEmpty && e.pairs != "—" { BioFactRow(label: e.name == "Anticodon" || e.name.hasPrefix("Discriminator") ? "Notes" : "Pairs", text: e.pairs) }
            BioFactRow(label: "Role", text: Self.role(e.name))
        }
    }

    static func role(_ name: String) -> String {
        if name.hasPrefix("Acceptor") { return "The 5′ and 3′ ends pair here; the amino acid is attached just beyond it, at the 3′ end." }
        if name.hasPrefix("Discriminator") { return "The 3′ end, always CCA in tRNAs. The enzyme that loads the amino acid (aminoacyl-tRNA synthetase) attaches it to A76's 3′ (or 2′) OH." }
        if name.hasPrefix("D stem") { return "Named for its dihydrouridines (D). It helps fold the tRNA into its L." }
        if name == "Anticodon" { return "Three bases that pair antiparallel with the mRNA's codon in the ribosome: anticodon 5′-GAA-3′ reads codon 5′-UUC-3′, phenylalanine." }
        if name.hasPrefix("Anticodon stem") { return "Holds the anticodon loop out at the bottom of the cloverleaf (the far end of the L)." }
        if name.hasPrefix("Variable") { return "Its length differs from one tRNA to another." }
        if name.hasPrefix("T stem") { return "Named for its T, Ψ and C (TΨC); it touches the D loop in the folded L." }
        return "Unpaired bases joining the arms."
    }

    static func modificationName(_ label: String) -> String {
        switch label {
        case "m²G": return "N2-methylguanosine"
        case "D": return "dihydrouridine"
        case "m²₂G": return "N2,N2-dimethylguanosine"
        case "Cm": return "2′-O-methylcytidine"
        case "Gm": return "2′-O-methylguanosine"
        case "yW": return "wybutosine"
        case "Ψ": return "pseudouridine"
        case "m⁵C": return "5-methylcytidine"
        case "m⁷G": return "7-methylguanosine"
        case "T": return "ribothymidine (5-methyluridine)"
        case "m¹A": return "1-methyladenosine"
        default: return "a modified base"
        }
    }

    private var codonCard: some View {
        BioCard(title: "Codon and anticodon", subtitle: "They pair antiparallel") {
            BioFactRow(label: "Pairing", text: "mRNA codon 5′-UUC-3′ pairs with anticodon 3′-AAG-5′ — the anticodon G34 A35 A36, written 5′→3′ as GAA. The first base of the codon pairs with the last of the anticodon.")
            BioFactRow(label: "Wobble", text: "G34 can also pair with U, so this tRNA reads UUU as well: both codons mean phenylalanine.")
            BioFactRow(label: "Stop codons", text: "UAA, UAG and UGA have no tRNA: release factors end translation there.")
        }
    }

    private var modifiedCard: some View {
        BioCard(title: "Modified bases", subtitle: "Made after transcription") {
            ForEach(BioData.trna.modified.indices, id: \.self) { k in
                let m = BioData.trna.modified[k]
                HStack(alignment: .firstTextBaseline) {
                    Text("\(m.label)\(m.position)").font(.sCallout.weight(.semibold)).frame(width: 64, alignment: .leading)
                    Text(Self.modificationName(m.label)).font(.sCallout).foregroundStyle(.secondary)
                }
            }
            Text("The cloverleaf writes each as its parent base (A, C, G or U), as the PDB's sequence does.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func armCard(_ arm: DNATRNAArm) -> some View {
        let dark = scheme == .dark
        let nt = store.trnaNucleotide.flatMap { i -> String? in
            let seq = Array(BioData.trnaSequence)
            return seq.indices.contains(i - 1) ? "\(seq[i - 1])\(i)" : nil
        }
        let name: String
        switch arm {
        case .acceptor: name = "Acceptor stem"
        case .d: name = "D stem"
        case .anticodon: name = "Anticodon stem"
        case .variable: name = "Variable"
        case .t: name = "T stem"
        case .connector: name = "Connectors"
        }
        return BioCard(title: arm.name, swatch: Color(hex: arm.colour.hex(dark: dark)), subtitle: nt.map { "Nucleotide \($0)" }) {
            BioFactRow(label: "Role", text: Self.role(name))
        }
    }

    private var lCard: some View {
        BioCard(title: "The L-shape", subtitle: String(halfUp: "Anticodon to 3′ end: %.0f Å (about 7 nm)", DNARNA.anticodonTo3Prime)) {
            BioFactRow(label: "How it folds", text: "The acceptor stem stacks on the T arm and the anticodon arm on the D arm, making two helices at right angles.")
            BioFactRow(label: "Why it matters", text: "The anticodon (one end) reads the mRNA in the ribosome's small subunit while the amino acid (the other end, about 7 nm away) reaches the large subunit, where peptide bonds form.")
        }
    }

    private var mrnaCard: some View {
        BioCard(title: "Messenger RNA (mRNA)", swatch: BioPalette.rna.color, subtitle: "A gene's message, read by ribosomes") {
            BioFactRow(label: "5′ cap", text: "A modified G (7-methylguanosine) joined 5′ to 5′. It protects the mRNA and helps the ribosome bind.")
            BioFactRow(label: "UTRs", text: "Untranslated regions before the start codon and after the stop: read by the ribosome but not turned into protein.")
            BioFactRow(label: "Coding sequence", text: "From AUG (start, methionine) to a stop codon, read three bases at a time: here MAFSDWQKG.")
            BioFactRow(label: "Poly-A tail", text: "A run of about 150–250 A, added after transcription; it protects the 3′ end.")
            BioFactRow(label: "Common mix-up", text: "mRNA has U where DNA has T. The mRNA matches the gene's coding strand (T → U); it is copied from the template strand.")
        }
    }

    private var ribosomeCard: some View {
        BioCard(title: "rRNA and ribosomes", subtitle: "S is the Svedberg unit: how fast a particle settles") {
            ForEach(DNARNA.ribosomes.indices, id: \.self) { k in
                let r = DNARNA.ribosomes[k]
                BioFactRow(label: "\(r.name): \(r.whole)", text: "\(r.small); \(r.large)")
            }
            BioFactRow(label: "Not additive", text: "30S + 50S make a 70S ribosome, not 80S: how fast a particle settles depends on its shape as well as its mass.")
            BioFactRow(label: "Mitochondria and chloroplasts", text: "Have 70S-type ribosomes, like bacteria — a clue to where they came from.")
            Button {
                BioTools.open(.gene)
            } label: {
                Label("See a ribosome translate", systemImage: "text.badge.plus").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("Open Gene Expression")
        }
    }
}

// MARK: - The cloverleaf

/// Where the cloverleaf's units go on the screen.
struct DNACloverleafFit {
    let scale: CGFloat
    let ox: CGFloat
    let oy: CGFloat
    let cx: Double
    let cy: Double

    init(size: CGSize) {
        let cl = DNARNA.cloverleaf
        let xs = cl.bases.map(\.x) + cl.codon.map(\.x), ys = cl.bases.map(\.y) + cl.codon.map(\.y)
        let minX = (xs.min() ?? 0) - 1.6, maxX = (xs.max() ?? 1) + 3.2, minY = (ys.min() ?? 0) - 2.2, maxY = (ys.max() ?? 1) + 1.0
        scale = max(min(size.width / CGFloat(maxX - minX), size.height / CGFloat(maxY - minY)), 1)
        cx = (minX + maxX) / 2
        cy = (minY + maxY) / 2
        ox = size.width / 2
        oy = size.height / 2
    }

    func point(_ x: Double, _ y: Double) -> CGPoint { CGPoint(x: ox + CGFloat(x - cx) * scale, y: oy - CGFloat(y - cy) * scale) }
}

struct DNACloverleafView: View {
    @ObservedObject var store: DNAStore
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        GeometryReader { geo in
            let fit = DNACloverleafFit(size: geo.size)
            Canvas { ctx, _ in draw(&ctx, fit) }
                .contentShape(Rectangle())
                .onTapGesture(coordinateSpace: .local) { pt in tap(pt, fit) }
        }
        // (fitted to whatever room the card gives it: at 420 at least, the card ran out over the page under it)
        .frame(minHeight: 300)
        .focusable()
        .focusEffectDisabled()
        .onKeyPress(.leftArrow) { move(-1); return .handled }
        .onKeyPress(.rightArrow) { move(1); return .handled }
        .onKeyPress(.escape) { store.trnaBase = nil; return .handled }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibility)
        .accessibilityAdjustableAction { direction in
            switch direction {
            case .increment: move(1)
            case .decrement: move(-1)
            @unknown default: break
            }
        }
    }

    private var accessibility: String {
        var s = "Cloverleaf of yeast transfer RNA for phenylalanine: acceptor stem at the top with the 3 prime CCA end, D arm on the left, anticodon arm at the bottom with the anticodon G A A pairing with the codon U U C, T arm on the right."
        if let b = store.trnaBase {
            let base = Array(BioData.trnaSequence)[b - 1]
            s += " Selected: \(base) \(b), in the \(DNARNA.element(of: b).name)."
        }
        return s
    }

    /// Which way a modified base's badge sits from its base (units, y up): out of a loop, away from a stem partner,
    /// or as set here for the few by the junction.
    static func badgeDirection(_ i: Int) -> (x: Double, y: Double) {
        let fixed: [Int: (Double, Double)] = [10: (-0.6, 0.8), 26: (-0.7, -0.7), 34: (-1, 0), 49: (-0.8, -0.6)]
        if let f = fixed[i] { return f }
        let cl = DNARNA.cloverleaf
        let b = cl.base(i)
        if let l = cl.loops.first(where: { $0.members.contains(i) }) {
            let dx = b.x - l.cx, dy = b.y - l.cy, len = max(hypot(dx, dy), 1e-9)
            return (dx / len, dy / len)
        }
        if let pr = cl.pairs.first(where: { $0.i == i || $0.j == i }) {
            let o = cl.base(pr.i == i ? pr.j : pr.i)
            let dx = b.x - o.x, dy = b.y - o.y, len = max(hypot(dx, dy), 1e-9)
            return (dx / len, dy / len)
        }
        let len = max(hypot(b.x, b.y), 1e-9)
        return (b.x / len, b.y / len)
    }

    private func move(_ d: Int) {
        let now = store.trnaBase ?? (d > 0 ? 0 : 77)
        store.trnaBase = min(max(now + d, 1), 76)
    }

    private func tap(_ pt: CGPoint, _ fit: DNACloverleafFit) {
        let cl = DNARNA.cloverleaf
        let near = cl.bases.min { hypot(fit.point($0.x, $0.y).x - pt.x, fit.point($0.x, $0.y).y - pt.y) < hypot(fit.point($1.x, $1.y).x - pt.x, fit.point($1.x, $1.y).y - pt.y) }
        if let n = near, hypot(fit.point(n.x, n.y).x - pt.x, fit.point(n.x, n.y).y - pt.y) < fit.scale * 0.6 {
            store.trnaBase = store.trnaBase == n.i ? nil : n.i
        } else {
            store.trnaBase = nil
        }
    }

    private func draw(_ ctx: inout GraphicsContext, _ fit: DNACloverleafFit) {
        let cl = DNARNA.cloverleaf
        let s = fit.scale
        let r = s * 0.38
        func p(_ i: Int) -> CGPoint { fit.point(cl.base(i).x, cl.base(i).y) }
        let selectedElement = store.trnaBase.map { DNARNA.element(of: $0).name }
        let accent = Theme.accent

        // Loops as faint circles.
        for l in cl.loops {
            let c = fit.point(l.cx, l.cy)
            let rr = CGFloat(l.radius) * s
            ctx.stroke(Path(ellipseIn: CGRect(x: c.x - rr, y: c.y - rr, width: 2 * rr, height: 2 * rr)), with: .color(Color.secondary.opacity(0.18)), lineWidth: 1)
        }
        // The backbone, 5′ to 3′.
        var backbone = Path()
        for i in 1...76 { if i == 1 { backbone.move(to: p(i)) } else { backbone.addLine(to: p(i)) } }
        ctx.stroke(backbone, with: .color(Color.secondary.opacity(0.5)), style: StrokeStyle(lineWidth: max(1.2, s * 0.08), lineCap: .round, lineJoin: .round))
        // Pairs as rungs (a G·U wobble pair dashed, with a dot).
        for pr in cl.pairs {
            var rung = Path()
            rung.move(to: p(pr.i))
            rung.addLine(to: p(pr.j))
            if pr.wobble {
                ctx.stroke(rung, with: .color(Color.primary.opacity(0.5)), style: StrokeStyle(lineWidth: max(1.2, s * 0.08), dash: [3, 3]))
                let m = CGPoint(x: (p(pr.i).x + p(pr.j).x) / 2, y: (p(pr.i).y + p(pr.j).y) / 2)
                ctx.fill(Path(ellipseIn: CGRect(x: m.x - 2.5, y: m.y - 2.5, width: 5, height: 5)), with: .color(.primary))
            } else {
                ctx.stroke(rung, with: .color(Color.primary.opacity(0.5)), lineWidth: max(1.4, s * 0.1))
            }
        }
        // The codon under the anticodon: an mRNA stretch, written 3′ … 5′ left to right, paired base by base.
        if let first = cl.codon.first, let last = cl.codon.last {
            let y = first.y
            var mrna = Path()
            mrna.move(to: fit.point(first.x - 1.4, y))
            mrna.addLine(to: fit.point(last.x + 1.4, y))
            ctx.stroke(mrna, with: .color(BioPalette.rna.color.opacity(0.7)), style: StrokeStyle(lineWidth: max(1.5, s * 0.1), lineCap: .round))
            ctx.draw(Text("3′").font(.system(size: max(9, s * 0.45), weight: .bold)).foregroundColor(.secondary), at: fit.point(first.x - 1.9, y))
            ctx.draw(Text("5′").font(.system(size: max(9, s * 0.45), weight: .bold)).foregroundColor(.secondary), at: fit.point(last.x + 1.9, y))
            for c in cl.codon {
                var rung = Path()
                rung.move(to: p(c.partner))
                rung.addLine(to: fit.point(c.x, c.y))
                ctx.stroke(rung, with: .color(Color.primary.opacity(0.5)), lineWidth: max(1.4, s * 0.1))
            }
            ctx.draw(Text("mRNA codon 5′-UUC-3′ (Phe), read right to left here")
                        .font(.system(size: max(9.5, s * 0.42), weight: .medium)).foregroundColor(.secondary),
                     at: fit.point((first.x + last.x) / 2, y - 1.3))
        }
        // Bases: a circle each in its colour, with its letter.
        func baseCircle(_ at: CGPoint, _ letter: Character, lit: Bool, ring: Bool) {
            let rect = CGRect(x: at.x - r, y: at.y - r, width: 2 * r, height: 2 * r)
            let colour = BioColor.base(letter)
            ctx.fill(Path(ellipseIn: rect), with: .color(lit ? accent.opacity(0.28) : colour.opacity(0.2)))
            ctx.stroke(Path(ellipseIn: rect), with: .color(lit ? accent : colour), lineWidth: lit || ring ? 2.2 : 1.2)
            ctx.draw(Text(String(letter)).font(.system(size: max(8, s * 0.48), weight: .bold, design: .rounded)).foregroundColor(colour), at: at)
        }
        for b in cl.bases {
            let lit = selectedElement != nil && DNARNA.element(of: b.i).name == selectedElement
            baseCircle(p(b.i), b.base, lit: lit || store.trnaBase == b.i, ring: (34...36).contains(b.i))
        }
        for c in cl.codon { baseCircle(fit.point(c.x, c.y), c.letter, lit: false, ring: false) }
        if let b = store.trnaBase {
            let at = p(b)
            ctx.stroke(Path(ellipseIn: CGRect(x: at.x - r - 3, y: at.y - r - 3, width: 2 * r + 6, height: 2 * r + 6)), with: .color(accent), lineWidth: 1.5)
        }
        // Modified bases as badges beside their base: out of its loop, or away from its partner, into free space.
        if store.showModified {
            for m in BioData.trna.modified {
                let at = p(m.position)
                let dir = DNACloverleafView.badgeDirection(m.position)
                let c = CGPoint(x: at.x + CGFloat(dir.x) * (r + s * 0.55), y: at.y - CGFloat(dir.y) * (r + s * 0.55))
                let text = Text(m.label).font(.system(size: max(8.5, s * 0.36), weight: .bold)).foregroundColor(.white)
                let w = CGFloat(m.label.count) * max(5.5, s * 0.25) + 8, h = max(12, s * 0.5)
                ctx.fill(Path(roundedRect: CGRect(x: c.x - w / 2, y: c.y - h / 2, width: w, height: h), cornerRadius: h / 2), with: .color(Color(hex: "#6e6e73")))
                ctx.draw(text, at: c)
            }
        }
        // Names: the ends and the arms.
        let small = Font.system(size: max(10, s * 0.46), weight: .semibold)
        func name(_ text: String, _ x: Double, _ y: Double, anchor: UnitPoint = .center, colour: Color = .secondary) {
            ctx.draw(Text(text).font(small).foregroundColor(colour), at: fit.point(x, y), anchor: anchor)
        }
        let g1 = cl.base(1), a76 = cl.base(76)
        name("5′", g1.x - 0.75, g1.y, anchor: .trailing, colour: .primary)
        name("3′", a76.x + 0.75, a76.y + 0.1, anchor: .leading, colour: .primary)
        name("Amino acid attaches here (A76)", a76.x + 0.75, a76.y - 0.75, anchor: .leading, colour: Color(hex: "#c79a00"))
        name("Acceptor stem", g1.x - 0.9, cl.base(4).y, anchor: .trailing)
        name("D arm", cl.base(17).x, cl.base(15).y + 1.0)
        name("Anticodon arm", cl.base(29).x - 0.9, cl.base(29).y, anchor: .trailing)
        name("Anticodon", cl.base(36).x + 0.7, cl.base(36).y - 0.6, anchor: .leading, colour: .primary)
        let variable = (44...48).map { cl.base($0) }
        name("Variable loop", variable.map(\.x).reduce(0, +) / 5 + 0.6, (variable.map(\.y).min() ?? 0) - 1.15, anchor: .top)
        name("T arm (TΨC)", cl.base(57).x, cl.base(59).y + 1.0)
    }
}

// MARK: - The mRNA strip

/// An mRNA as a labelled strip (spec §4.2 RNA): the 5′ cap, the 5′ UTR, the coding sequence's codons with their amino
/// acids (start and stop marked), the 3′ UTR and the poly-A tail; each base in its colour with its letter.
struct DNAMRNAStrip: View {
    var body: some View {
        Canvas { ctx, size in draw(&ctx, size) }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("mRNA from 5 prime to 3 prime: cap, 5 prime untranslated region of \(DNARNA.cdsStart - 1) nucleotides, coding sequence from A U G to the stop codon U A A coding M A F S D W Q K G, 3 prime untranslated region, poly A tail")
    }

    private func draw(_ ctx: inout GraphicsContext, _ size: CGSize) {
        let seq = Array(DNARNA.mrna)
        let start = DNARNA.cdsStart, end = DNARNA.cdsEnd
        let split = start + 15 - 1 // (the first line ends after the coding sequence's fifth codon)
        let lines: [ClosedRange<Int>] = [1...split, (split + 1)...seq.count]
        let units = 34.0
        let tile = min(20, max(10, (size.width - 40) / CGFloat(units)))
        let left: CGFloat = 20
        let lineHeight: CGFloat = 112
        let top: CGFloat = 26
        let codonAA = Dictionary(uniqueKeysWithValues: DNARNA.codons.map { ($0.start, $0.aa) })
        func region(_ i: Int) -> (name: String, colour: Color) {
            if i < start { return ("5′ UTR", .secondary) }
            if i <= end { return ("Coding sequence", BioPalette.strand1.color) }
            return ("3′ UTR", .secondary)
        }
        for (li, range) in lines.enumerated() {
            let y = top + CGFloat(li) * lineHeight + 30
            var x = left
            if li == 0 {
                // The cap.
                let w = tile * 2.4
                let rect = CGRect(x: x, y: y - tile / 2, width: w, height: tile)
                ctx.fill(Path(roundedRect: rect, cornerRadius: tile / 2), with: .color(Color(hex: "#6e6e73")))
                ctx.draw(Text("m⁷G").font(.system(size: tile * 0.5, weight: .bold)).foregroundColor(.white), at: CGPoint(x: rect.midX, y: rect.midY))
                ctx.draw(Text("5′ cap").font(.system(size: 11, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: rect.midX, y: y + tile / 2 + 14))
                ctx.draw(Text("5′").font(.system(size: 12, weight: .bold)).foregroundColor(.secondary), at: CGPoint(x: rect.minX - 2, y: y - tile / 2 - 10), anchor: .leading)
                x += w + tile * 0.4
            }
            var bandStart = x
            var bandName = region(range.lowerBound).name
            var bandColour = region(range.lowerBound).colour
            func closeBand(_ to: CGFloat) {
                let bar = CGRect(x: bandStart, y: y + tile / 2 + 4, width: max(to - bandStart - 2, 2), height: 4)
                ctx.fill(Path(roundedRect: bar, cornerRadius: 2), with: .color(bandColour.opacity(0.7)))
                ctx.draw(Text(bandName).font(.system(size: 11, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: (bandStart + to) / 2, y: y + tile / 2 + 18))
            }
            for i in range {
                let reg = region(i)
                if reg.name != bandName {
                    closeBand(x)
                    bandStart = x
                    bandName = reg.name
                    bandColour = reg.colour
                }
                // (a codon's first base: a little room before it, its amino acid over it)
                if i >= start && i <= end && (i - start) % 3 == 0 {
                    if i > start { x += tile * 0.25 }
                    if let aa = codonAA[i] {
                        let name = aa == "*" ? "Stop" : (BioAminoAcids.byOne[aa]?.three ?? String(aa))
                        let colour: Color = aa == "*" ? .red : (i == start ? .green : BioColor.aminoAcid(aa))
                        let cxp = x + tile * 1.5
                        var bracket = Path()
                        bracket.move(to: CGPoint(x: x + 1, y: y - tile / 2 - 4))
                        bracket.addLine(to: CGPoint(x: x + 1, y: y - tile / 2 - 8))
                        bracket.addLine(to: CGPoint(x: x + tile * 3 - 1, y: y - tile / 2 - 8))
                        bracket.addLine(to: CGPoint(x: x + tile * 3 - 1, y: y - tile / 2 - 4))
                        ctx.stroke(bracket, with: .color(colour.opacity(0.8)), lineWidth: 1.2)
                        ctx.draw(Text(name).font(.system(size: 11, weight: .bold)).foregroundColor(colour), at: CGPoint(x: cxp, y: y - tile / 2 - 18))
                        if i == start { ctx.draw(Text("Start").font(.system(size: 10, weight: .semibold)).foregroundColor(.green), at: CGPoint(x: cxp, y: y - tile / 2 - 32)) }
                    }
                }
                let letter = seq[i - 1]
                let rect = CGRect(x: x + 0.5, y: y - tile / 2, width: tile - 1, height: tile)
                ctx.fill(Path(roundedRect: rect, cornerRadius: 3), with: .color(BioColor.base(letter).opacity(0.2)))
                ctx.draw(Text(String(letter)).font(.system(size: tile * 0.6, weight: .bold, design: .monospaced)).foregroundColor(BioColor.base(letter)),
                         at: CGPoint(x: rect.midX, y: rect.midY))
                if (i - 1) % 10 == 0 {
                    ctx.draw(Text("\(i)").font(.system(size: 9, design: .monospaced)).foregroundColor(.secondary), at: CGPoint(x: rect.midX, y: y + tile / 2 + 32))
                }
                x += tile
            }
            closeBand(x)
            if li == lines.count - 1 {
                // The tail.
                x += tile * 0.4
                let w = tile * 5
                let rect = CGRect(x: x, y: y - tile / 2, width: w, height: tile)
                ctx.fill(Path(roundedRect: rect, cornerRadius: tile / 2), with: .color(BioColor.base("A").opacity(0.25)))
                ctx.draw(Text("AAAA…A").font(.system(size: tile * 0.5, weight: .bold, design: .monospaced)).foregroundColor(BioColor.base("A")), at: CGPoint(x: rect.midX, y: rect.midY))
                ctx.draw(Text("Poly-A tail").font(.system(size: 11, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: rect.midX, y: y + tile / 2 + 18))
                ctx.draw(Text("3′").font(.system(size: 12, weight: .bold)).foregroundColor(.secondary), at: CGPoint(x: rect.maxX + 6, y: y), anchor: .leading)
            } else {
                ctx.draw(Text("…").font(.system(size: 14, weight: .bold)).foregroundColor(.secondary), at: CGPoint(x: x + 8, y: y))
            }
        }
    }
}

// MARK: - Ribosomes

/// The two kinds of ribosome, each its small subunit under its large one, with their S values and rRNAs.
struct DNARibosomeDiagram: View {
    var body: some View {
        Canvas { ctx, size in
            let items: [(title: String, large: String, small: String, largeRNA: String, smallRNA: String)] = [
                ("Bacteria: 70S", "50S", "30S", "23S + 5S rRNA, about 33 proteins", "16S rRNA, about 21 proteins"),
                ("Eukaryotes: 80S", "60S", "40S", "28S + 5.8S + 5S rRNA", "18S rRNA"),
            ]
            let w = size.width / CGFloat(items.count)
            for (k, it) in items.enumerated() {
                let cx = w * (CGFloat(k) + 0.5)
                let scale: CGFloat = k == 0 ? 0.85 : 1
                let large = CGRect(x: cx - 80 * scale, y: 70, width: 160 * scale, height: 92 * scale)
                let small = CGRect(x: cx - 64 * scale, y: large.maxY - 14, width: 128 * scale, height: 58 * scale)
                ctx.draw(Text(it.title).font(.system(size: 15, weight: .bold)).foregroundColor(.primary), at: CGPoint(x: cx, y: 40))
                ctx.fill(Path(ellipseIn: small), with: .color(Color(hex: "#1f6fd1").opacity(0.75)))
                ctx.fill(Path(ellipseIn: large), with: .color(Color(hex: "#7d5cf5").opacity(0.75)))
                ctx.draw(Text(it.large).font(.system(size: 16, weight: .bold)).foregroundColor(.white), at: CGPoint(x: large.midX, y: large.midY - 6))
                ctx.draw(Text(it.small).font(.system(size: 14, weight: .bold)).foregroundColor(.white), at: CGPoint(x: small.midX, y: small.midY + 6))
                ctx.draw(Text("Large: " + it.largeRNA).font(.system(size: 11.5)).foregroundColor(.secondary), at: CGPoint(x: cx, y: small.maxY + 20))
                ctx.draw(Text("Small: " + it.smallRNA).font(.system(size: 11.5)).foregroundColor(.secondary), at: CGPoint(x: cx, y: small.maxY + 38))
            }
            ctx.draw(Text("Svedberg values do not add: 30S + 50S = 70S, and 40S + 60S = 80S.").font(.system(size: 12, weight: .medium)).foregroundColor(.primary),
                     at: CGPoint(x: size.width / 2, y: min(size.height - 14, 290)))
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Bacterial ribosome 70S: large subunit 50S, small 30S. Eukaryotic ribosome 80S: large 60S, small 40S. Svedberg values do not add.")
    }
}
