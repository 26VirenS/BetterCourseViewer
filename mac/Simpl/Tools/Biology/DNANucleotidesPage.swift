import SceneKit
import SwiftUI

// DNA & RNA's Nucleotides page (spec §4.2 Nucleotides): DNA's four and RNA's four side by side, and ATP; one in 3D
// (by part or by element, its carbons numbered, DNA's and RNA's differences lit) with its card, the part pressed, and
// DNA against RNA; or the bases in 2D — purines (two rings) and pyrimidines (one), and the Watson–Crick pairs with
// their H-bonds drawn between the exact atoms (§6.5).

struct DNANucleotidesPage: View {
    @ObservedObject var store: DNAStore
    @Environment(\.colorScheme) private var scheme
    @State private var turning = false
    @State private var reset = 0

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            if store.ntView == .bases2D {
                DNABasesPanel(pairWithU: store.pairWithU)
            } else {
                scene
            }
        } right: {
            cards
        }
    }

    private var info: DNANucleotideInfo { DNANucleotides.info(store.nucleotide) }

    // MARK: Left: the chooser and the view

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            chooser
            viewSection
            if store.ntView == .model3D { modelOptions } else { pairOptions }
            if store.ntView == .model3D && store.ntParts { partsLegend }
        }
        .font(.sCallout)
    }

    private var chooser: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Nucleotide")
            HStack(alignment: .top, spacing: 8) {
                column("DNA", DNANucleotides.dna)
                column("RNA", DNANucleotides.rna)
            }
            chip("ATP")
        }
    }

    private func column(_ title: String, _ codes: [String]) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            ForEach(codes, id: \.self) { chip($0) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func chip(_ code: String) -> some View {
        let i = DNANucleotides.info(code)
        return DNAChip(title: i.short, detail: i.isTriphosphate ? "the energy carrier" : DNAModel.baseName(i.base), swatch: BioColor.base(i.base),
                       selected: store.nucleotide == code, help: i.name) {
            store.nucleotide = code
            store.ntSelection = nil
            store.ntAtom = nil
            if store.ntView != .model3D { store.ntView = .model3D }
        }
    }

    private var viewSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("View")
            Picker("View", selection: $store.ntView) {
                ForEach(DNANucleotideView.allCases, id: \.self) { Text($0.label).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("The nucleotide in 3D, or the bases and their pairs drawn flat")
        }
    }

    private var modelOptions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Picker("Style", selection: $store.ntStyle) {
                ForEach(BioAtomStyle.allCases, id: \.self) { Text($0.label).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            Toggle("Colour by part", isOn: $store.ntParts).help("Phosphate, sugar and base each in its colour (off: by element)")
            Toggle("Number the carbons", isOn: $store.ntNumbers).help("The sugar's carbons 1′ to 5′, and the base's ring atoms: where 5′ and 3′ come from")
            Toggle("Compare DNA and RNA", isOn: $store.ntCompare).help("Light the 2′ position and the 5-methyl, where DNA's and RNA's nucleotides differ")
            Toggle("Hydrogens", isOn: $store.ntHydrogens)
            Toggle("As at pH 7", isOn: $store.ntPH7).help("The phosphate's OH groups lose their H at the cell's pH: −2 (ATP −4)")
            Toggle("Labels", isOn: $store.ntLabels)
        }
    }

    private var pairOptions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Toggle("RNA's A·U pair", isOn: $store.pairWithU).help("Show A·U (RNA) in place of A·T: U is T without its 5-methyl")
            Text("Each pair joins a purine (two rings) to a pyrimidine (one ring), so every pair is the same width.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private var partsLegend: some View {
        let dark = scheme == .dark
        let sugar = info.isDNA ? DNANucleotides.deoxyriboseHex : DNANucleotides.riboseHex
        return VStack(alignment: .leading, spacing: 8) {
            BioHeading("Parts")
            BioLegend(items: [(Color(hex: DNANucleotides.phosphateHex.hex(dark: dark)), info.isTriphosphate ? "Three phosphate groups" : "Phosphate group"),
                              (Color(hex: sugar.hex(dark: dark)), info.isDNA ? "Deoxyribose (sugar)" : "Ribose (sugar)"),
                              (BioColor.base(info.base), "Base: \(DNAModel.baseName(info.base))")])
        }
    }

    // MARK: Middle: the nucleotide in 3D

    private var scene: some View {
        let o = DNANucleotideOptions(code: store.nucleotide, style: store.ntStyle, hydrogens: store.ntHydrogens, ph7: store.ntPH7, parts: store.ntParts,
                                     numbers: store.ntNumbers, compare: store.ntCompare, dark: scheme == .dark)
        let look = DNASceneText.atomLook
        let key = o.key
        let i = info
        return BioSceneCard(hint: "Drag to turn · scroll to zoom · click a part to learn about it") {
            VStack(alignment: .leading, spacing: 2) {
                Text(i.short).font(.system(size: 22, weight: .bold))
                Text(i.name).font(.sCallout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            BioSceneButtons(turning: $turning) { reset += 1 }
        } scene: {
            BioSceneView(key: key,
                         build: BioSceneBuild {
                             let s = DNANucleotides.build(o, look: look)
                             DNAMarkShelf.shared.put(key, s.marks)
                             return s.content
                         },
                         extras: { DNASceneText.nodes(DNAMarkShelf.shared.take(key)) },
                         selection: $store.ntSelection,
                         showLabels: store.ntLabels,
                         turning: turning,
                         frameKey: store.nucleotide,
                         reset: reset,
                         accessibility: "3D model of \(i.short), \(i.name)" + (store.ntSelection.map { "; selected: \(partName($0))" } ?? ""),
                         onPick: { store.pickNucleotide($0) })
        }
    }

    private func partName(_ sel: String) -> String {
        switch sel {
        case "part:phosphate": return info.isTriphosphate ? "the triphosphate" : "the phosphate group"
        case "part:sugar": return info.isDNA ? "deoxyribose" : "ribose"
        case "part:base": return DNAModel.baseName(info.base)
        case "part:glyco": return "the glycosidic bond"
        case "diff:2prime": return "the 2′ position"
        case "diff:methyl": return "the 5-methyl position"
        default: return sel
        }
    }

    // MARK: Right: the cards

    private var cards: some View {
        VStack(alignment: .leading, spacing: 14) {
            if store.ntView == .model3D, let sel = store.ntSelection { partCard(sel) }
            nucleotideCard
            compareCard
        }
    }

    private var nucleotideCard: some View {
        let i = info
        let mol = BioData.molecule(i.code)
        let sugar = i.isDNA ? "deoxyribose" : "ribose"
        let ring = DNAModel.isPurine(i.base) ? "a purine: two rings" : "a pyrimidine: one ring"
        return BioCard(title: i.short, swatch: BioColor.base(i.base), subtitle: i.name) {
            BioFactRow(label: "Parts", text: i.isTriphosphate ? "Three phosphate groups + ribose + adenine" : "A phosphate group + \(sugar) + \(DNAModel.baseName(i.base))")
            BioFactRow(label: "Base", text: "\(DNAModel.baseName(i.base).capitalized) (\(i.base)), \(ring). " + pairsWith(i))
            if let f = mol?.formula, let m = mol?.mass {
                BioFactRow(label: "Formula and mass", text: "\(DNAText.formula(f)) · \(String(halfUp: "%.2f", m)) g/mol (neutral, as drawn with “As at pH 7” off)")
            }
            BioFactRow(label: "Found in", text: i.isTriphosphate
                       ? "Every cell: ATP carries energy, released when its last phosphate is split off. It is also one of the four building blocks of RNA."
                       : (i.isDNA ? "DNA, as one of its four building blocks." : "RNA, as one of its four building blocks."))
            if store.ntPH7 {
                Text(i.isTriphosphate ? "At pH 7 its phosphates carry a charge of −4." : "At pH 7 its phosphate carries a charge of −2 (−1 once it is in a strand).")
                    .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func pairsWith(_ i: DNANucleotideInfo) -> String {
        switch i.base {
        case "A": return "Pairs with T in DNA, U in RNA (2 H-bonds)."
        case "G": return "Pairs with C (3 H-bonds)."
        case "C": return "Pairs with G (3 H-bonds)."
        case "T": return "Pairs with A (2 H-bonds). DNA only."
        default: return "Pairs with A (2 H-bonds). RNA only."
        }
    }

    @ViewBuilder
    private func partCard(_ sel: String) -> some View {
        let i = info
        let dark = scheme == .dark
        switch sel {
        case "part:phosphate":
            BioCard(title: i.isTriphosphate ? "Triphosphate" : "Phosphate group", swatch: Color(hex: DNANucleotides.phosphateHex.hex(dark: dark))) {
                BioFactRow(label: "Structure", text: i.isTriphosphate
                           ? "Three phosphates (α, β, γ) in a chain on the sugar's 5′ carbon, joined by phosphoanhydride bonds."
                           : "A phosphorus atom with four oxygens, on the sugar's 5′ carbon (through O5′).")
                BioFactRow(label: "In a strand", text: "It links the 3′ carbon of one sugar to the 5′ carbon of the next: a phosphodiester bond. That is what makes the 5′ and 3′ ends of a strand.")
                BioFactRow(label: "Charge", text: "Negative at pH 7, which is why nucleic acids are acids.")
                atomLine
            }
        case "part:sugar":
            BioCard(title: i.isDNA ? "Deoxyribose" : "Ribose", swatch: Color(hex: (i.isDNA ? DNANucleotides.deoxyriboseHex : DNANucleotides.riboseHex).hex(dark: dark)),
                    subtitle: "A five-carbon sugar (pentose)") {
                BioFactRow(label: "Carbons", text: "Numbered 1′ to 5′ (“one prime” …), primed to tell them from the base's atoms. The base is on 1′, the phosphate on 5′, and the next nucleotide joins at 3′.")
                BioFactRow(label: "At 2′", text: i.isDNA ? "Only H: “deoxy” means the oxygen is missing there." : "An OH group. It makes RNA easier to break than DNA.")
                atomLine
            }
        case "part:base":
            BioCard(title: DNAModel.baseName(i.base).capitalized, swatch: BioColor.base(i.base),
                    subtitle: DNAModel.isPurine(i.base) ? "A purine: two rings" : "A pyrimidine: one ring") {
                BioFactRow(label: "Pairing", text: pairsWith(i))
                BioFactRow(label: "Nitrogenous", text: "Its rings hold nitrogen atoms; the ones on its edge take part in the H-bonds of a base pair.")
                if i.base == "T" || i.base == "U" {
                    BioFactRow(label: "T and U", text: "Thymine is uracil with a methyl group (CH₃) on carbon 5. DNA uses T; RNA uses U.")
                }
                atomLine
            }
        case "part:glyco":
            BioCard(title: "Glycosidic bond", subtitle: "Sugar C1′ to the base's N\(DNAModel.isPurine(i.base) ? "9" : "1")") {
                BioFactRow(label: "What it joins", text: "The sugar's 1′ carbon to a nitrogen of the base: N9 of a purine, N1 of a pyrimidine.")
            }
        case "diff:2prime":
            BioCard(title: "The 2′ position", swatch: Color(hex: DNANucleotides.highlightHex.hex(dark: dark))) {
                BioFactRow(label: "DNA against RNA", text: "Deoxyribose has only H on its 2′ carbon; ribose has an OH there. The difference is at 2′, not 3′: both sugars keep their 3′-OH, where the next nucleotide joins.")
            }
        case "diff:methyl":
            BioCard(title: "The 5-methyl", swatch: Color(hex: DNANucleotides.highlightHex.hex(dark: dark))) {
                BioFactRow(label: "T against U", text: "Thymine has a methyl group (CH₃) on its carbon 5; uracil has an H there. Otherwise they are the same, and both pair with A.")
            }
        default:
            EmptyView()
        }
    }

    /// The atom pressed, if one was.
    @ViewBuilder
    private var atomLine: some View {
        if let i = store.ntAtom, let mol = BioData.molecule(store.nucleotide) {
            let atoms = mol.model(ph7: store.ntPH7, hydrogens: store.ntHydrogens).atoms
            if atoms.indices.contains(i) {
                let names = ["H": "hydrogen", "C": "carbon", "N": "nitrogen", "O": "oxygen", "P": "phosphorus"]
                BioFactRow(label: "Atom picked", text: "\(atoms[i].name.replacingOccurrences(of: "'", with: "′")) (\(names[atoms[i].element] ?? atoms[i].element))")
            }
        }
    }

    private var compareCard: some View {
        BioCard(title: "DNA and RNA", subtitle: "How their nucleotides and strands differ") {
            BioFactRow(label: "Sugar", text: "DNA: deoxyribose (H at 2′). RNA: ribose (OH at 2′).")
            BioFactRow(label: "Bases", text: "DNA: A, T, G, C. RNA: A, U, G, C — uracil is thymine without the 5-methyl.")
            BioFactRow(label: "Strands", text: "DNA: usually two, a double helix. RNA: usually one, folded on itself (as tRNA is).")
            BioFactRow(label: "Stability", text: "DNA lasts: it stores the genes. RNA's 2′-OH helps break its own backbone, so RNA is short-lived — fitting for messages.")
            if store.ntView == .model3D && !store.ntCompare {
                Button("Light the differences") { store.ntCompare = true }
                    .glassButton()
                    .help("Light the 2′ position and the 5-methyl in the model")
            }
        }
    }
}

// MARK: - The bases in 2D

/// Purines and pyrimidines, and the two Watson–Crick pairs with their H-bonds (spec §4.2 Purines vs pyrimidines).
struct DNABasesPanel: View {
    let pairWithU: Bool

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                HStack(alignment: .top, spacing: 18) {
                    group("Purines: two rings", ["A", "G"])
                    group("Pyrimidines: one ring", ["C", "T", "U"])
                }
                VStack(alignment: .leading, spacing: 8) {
                    Text("Watson–Crick pairs").font(.sHeadline)
                    HStack(alignment: .top, spacing: 14) {
                        DNABaseDrawing(model: DNANucleotides.pair2D(pairWithU ? "AU" : "AT"), title: pairWithU ? "A·U: 2 H-bonds (RNA)" : "A·T: 2 H-bonds", pair: true)
                            .frame(height: 230)
                        DNABaseDrawing(model: DNANucleotides.pair2D("GC"), title: "G·C: 3 H-bonds", pair: true)
                            .frame(height: 230)
                    }
                }
                Text("Dashed lines are hydrogen bonds, from the donor's H to the acceptor; lengths are heavy atom to heavy atom in the B-DNA model. Both pairs are 10.9 Å from C1′ to C1′. RNA also pairs G with U (a “wobble” pair, 2 H-bonds) in tRNA stems and at a codon's third base.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(20)
        }
        .card(radius: 20)
    }

    private func group(_ title: String, _ bases: [Character]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(.sHeadline)
            HStack(spacing: 8) {
                ForEach(bases, id: \.self) { b in
                    DNABaseDrawing(model: DNANucleotides.base2D(b), title: "\(b) · \(DNAModel.baseName(b))", pair: false)
                        .frame(width: 118, height: 150)
                }
            }
        }
    }
}

/// A base or a base pair drawn flat: rings tinted in the base's colour, bonds (double bonds doubled), N and O by
/// letter, the H atoms on N, the methyl as CH₃, C1′ where the sugar joins — and in a pair, the H-bonds dashed from the
/// donor's H to the acceptor with their lengths, the major groove's side up.
struct DNABaseDrawing: View {
    let model: DNABase2D
    let title: String
    let pair: Bool
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        Canvas { ctx, size in draw(&ctx, size) }
            .background(Color.primary.opacity(0.035), in: RoundedRectangle(cornerRadius: 12, style: .continuous))
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(accessibility)
    }

    private var accessibility: String {
        if pair { return title + ": " + model.hBonds.map { "\($0.text), \(String(halfUp: "%.2f", $0.distance)) ångströms" }.joined(separator: "; ") }
        return title + (model.rings.count == 2 ? ", a purine with two rings" : ", a pyrimidine with one ring")
    }

    /// Which atoms are drawn: the heavy ones, and H only on N (the ones that H-bond).
    private func shown(_ i: Int) -> Bool {
        let a = model.atoms[i]
        guard a.element == "H" else { return true }
        return model.bonds.contains { b in
            let other = b.a == i ? b.b : (b.b == i ? b.a : -1)
            return other >= 0 && model.atoms[other].element == "N"
        }
    }

    private func draw(_ ctx: inout GraphicsContext, _ size: CGSize) {
        let dark = scheme == .dark
        let atoms = model.atoms
        let visible = atoms.indices.filter(shown)
        guard !visible.isEmpty else { return }
        let xs = visible.map { atoms[$0].x }, ys = visible.map { atoms[$0].y }
        let minX = xs.min() ?? 0, maxX = xs.max() ?? 1, minY = ys.min() ?? 0, maxY = ys.max() ?? 1
        let top: CGFloat = 26, pad: CGFloat = 16, bottomPad: CGFloat = pair ? 26 : 10
        let s = min((size.width - 2 * pad) / CGFloat(max(maxX - minX, 1)), (size.height - top - bottomPad - pad) / CGFloat(max(maxY - minY, 1)), 30)
        let cx = (minX + maxX) / 2, cy = (minY + maxY) / 2
        let ox = size.width / 2, oy = top + (size.height - top - bottomPad) / 2
        func p(_ i: Int) -> CGPoint { CGPoint(x: ox + CGFloat(atoms[i].x - cx) * s, y: oy - CGFloat(atoms[i].y - cy) * s) }
        let ink = Color.primary
        let nColour = Color(hex: dark ? "#8ea6ff" : "#2f55d4"), oColour = Color(hex: dark ? "#ff7b72" : "#d32f2f")

        ctx.draw(Text(title).font(.system(size: 12, weight: .semibold)).foregroundColor(.primary), at: CGPoint(x: 10, y: 14), anchor: .leading)

        // Rings, tinted.
        for ring in model.rings {
            var path = Path()
            for (k, i) in ring.atoms.enumerated() { if k == 0 { path.move(to: p(i)) } else { path.addLine(to: p(i)) } }
            path.closeSubpath()
            ctx.fill(path, with: .color(BioColor.base(ring.base).opacity(0.16)))
        }
        // Bonds: shortened where a letter sits; a double bond's second line inside its ring (or beside it).
        func labelled(_ i: Int) -> Bool { atoms[i].element != "C" || atoms[i].name == "C7" || atoms[i].name == "C1'" }
        for b in model.bonds where shown(b.a) && shown(b.b) {
            var a = p(b.a), c = p(b.b)
            let dx = c.x - a.x, dy = c.y - a.y, len = max(hypot(dx, dy), 0.001)
            let ux = dx / len, uy = dy / len
            let trim: CGFloat = s * 0.28
            if labelled(b.a) { a = CGPoint(x: a.x + ux * trim, y: a.y + uy * trim) }
            if labelled(b.b) { c = CGPoint(x: c.x - ux * trim, y: c.y - uy * trim) }
            var line = Path()
            line.move(to: a)
            line.addLine(to: c)
            ctx.stroke(line, with: .color(ink.opacity(0.8)), lineWidth: 1.4)
            if b.order == 2 {
                // (toward the ring's middle when both atoms are in one ring, else to the left of the bond)
                var nx = -uy, ny = ux
                if let ring = model.rings.first(where: { $0.atoms.contains(b.a) && $0.atoms.contains(b.b) }) {
                    let mid = ring.atoms.map(p).reduce(CGPoint.zero) { CGPoint(x: $0.x + $1.x, y: $0.y + $1.y) }
                    let m = CGPoint(x: mid.x / CGFloat(ring.atoms.count), y: mid.y / CGFloat(ring.atoms.count))
                    if (m.x - (a.x + c.x) / 2) * nx + (m.y - (a.y + c.y) / 2) * ny < 0 { nx = -nx; ny = -ny }
                }
                let off: CGFloat = 3.6, shrink = len * 0.14
                var second = Path()
                second.move(to: CGPoint(x: a.x + nx * off + ux * shrink, y: a.y + ny * off + uy * shrink))
                second.addLine(to: CGPoint(x: c.x + nx * off - ux * shrink, y: c.y + ny * off - uy * shrink))
                ctx.stroke(second, with: .color(ink.opacity(0.8)), lineWidth: 1.4)
            }
        }
        // H-bonds: dashed from the donor's H to the acceptor, with the donor–acceptor length.
        for h in model.hBonds {
            let a = p(h.h), c = p(h.acceptor)
            var line = Path()
            line.move(to: a)
            line.addLine(to: c)
            ctx.stroke(line, with: .color(BioPalette.hBond.color), style: StrokeStyle(lineWidth: 1.5, dash: [3, 3]))
            // (the length just off the dashes, on the side away from the pair's middle)
            let mx = (a.x + c.x) / 2, my = (a.y + c.y) / 2
            let up = my <= oy + 4
            ctx.draw(Text(String(halfUp: "%.2f Å", h.distance)).font(.system(size: 10, weight: .medium).monospacedDigit()).foregroundColor(.secondary),
                     at: CGPoint(x: mx, y: my + (up ? -13 : 13)))
        }
        // Atoms: N and O by letter, H small, the methyl as CH₃, C1′ where the sugar joins.
        for i in visible {
            let a = atoms[i]
            let at = p(i)
            switch a.element {
            case "N": ctx.draw(Text("N").font(.system(size: 13, weight: .bold)).foregroundColor(nColour), at: at)
            case "O": ctx.draw(Text("O").font(.system(size: 13, weight: .bold)).foregroundColor(oColour), at: at)
            case "H": ctx.draw(Text("H").font(.system(size: 10.5, weight: .semibold)).foregroundColor(.secondary), at: at)
            default:
                if a.name == "C7" { ctx.draw(Text("CH₃").font(.system(size: 11.5, weight: .bold)).foregroundColor(.primary), at: at) }
                if a.name == "C1'" { ctx.draw(Text("C1′").font(.system(size: 10.5, weight: .semibold)).foregroundColor(.secondary), at: at) }
            }
        }
        if pair {
            ctx.draw(Text("↑ major groove side").font(.system(size: 10.5)).foregroundColor(.secondary), at: CGPoint(x: size.width - 10, y: 14), anchor: .trailing)
            ctx.draw(Text("minor groove side ↓  ·  C1′ to C1′ 10.9 Å").font(.system(size: 10.5)).foregroundColor(.secondary), at: CGPoint(x: size.width / 2, y: size.height - 12))
        }
    }
}
