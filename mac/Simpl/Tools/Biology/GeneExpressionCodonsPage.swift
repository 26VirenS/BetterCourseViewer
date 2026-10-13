import SwiftUI

// Gene Expression's Codon table page (spec §4.3, §6.1, §7 item 13): the standard genetic code as a grid — first base
// down the side, second across the top, third within each block, U C A G everywhere — or as a wheel read from the centre
// out; a lookup both ways (a codon to its amino acid, an amino acid to all its codons); a switch to DNA coding-strand
// letters, clearly titled; and the card of the amino acid pressed, with "See in 3D" in the Biomolecule Builder.

struct GXCodonsPage: View {
    @ObservedObject var store: GXStore
    @FocusState private var lookupFocused: Bool
    @State private var hovered: String?

    private var query: GXCodonTable.Query { GXCodonTable.parse(store.lookup) }
    private var lit: Set<String> { GXCodonTable.matches(query) }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            table
        } right: {
            card
        }
    }

    // MARK: Left: the view, the letters, the lookup

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("View")
            Picker("", selection: $store.codonView) {
                ForEach(GXCodonView.allCases) { v in Text(v.label).tag(v) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("The table as a grid, or as a wheel read from the centre out")
            Toggle("DNA coding-strand letters (T for U)", isOn: $store.dnaLetters)
                .help("The coding strand reads like the mRNA, with T for U")
            BioHeading("Look up")
            HStack(spacing: 8) {
                TextField("AUG, ATG, Lys, K, lysine…", text: $store.lookup)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 14, design: .monospaced))
                    .focused($lookupFocused)
                    .background(
                        // (⌘F: this field)
                        Button("") { lookupFocused = true }
                            .keyboardShortcut("f", modifiers: .command)
                            .opacity(0)
                            .accessibilityHidden(true)
                    )
                    .onSubmit { pickFromLookup() }
                    .accessibilityLabel("Codon or amino acid")
                if !store.lookup.isEmpty {
                    Button { store.lookup = "" } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.secondary) }
                        .buttonStyle(.plain)
                        .help("Clear the lookup")
                }
            }
            GXCodonAnswer(query: query, dna: store.dnaLetters)
            BioHeading("Classes")
            BioLegend(items: BioAAClass.allCases.map { (color: $0.color, text: $0.label) } + [(color: GXColors.stop.color, text: "Stop (no amino acid)")])
            VStack(alignment: .leading, spacing: 8) {
                Text("AUG is the start codon, and inside a gene it codes Met. UUG and CUG can rarely start translation too; this tool uses AUG only.")
                Text("Mitochondria use a slightly different code (in vertebrate mitochondria UGA codes Trp). This is the standard code (NCBI table 1).")
            }
            .font(.sCaption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    private func pickFromLookup() {
        if case .codon(let c) = query { store.picked = c }
        if case .aminoAcid(let a) = query { store.picked = BioCode.codons(for: a).first }
        if case .stop = query { store.picked = "UAA" }
    }

    // MARK: Centre: the grid or the wheel

    private var table: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(store.dnaLetters ? "The genetic code in DNA letters" : "The genetic code")
                        .font(.system(size: 20, weight: .bold))
                    Text(store.dnaLetters ? "Coding-strand codons, 5′→3′ (T for U). The ribosome reads the mRNA: the same, with U."
                                          : "mRNA codons, read 5′→3′. 64 codons: 61 for amino acids, 3 stops.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if store.dnaLetters { GXBadge(text: "DNA letters", colour: BioPalette.thymine.color) }
            }
            .padding(16)
            Group {
                if store.codonView == .grid {
                    ScrollView { grid.padding(.horizontal, 16).padding(.bottom, 16) }
                } else {
                    GXCodonWheel(lit: lit, picked: store.picked, dna: store.dnaLetters, hovered: $hovered) { store.picked = $0 }
                        .padding(16)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            Text(store.codonView == .grid ? "Read it: first base on the left, second across the top, third on the right. Press a codon for its amino acid."
                                          : "Read it from the centre out: first base, second, third, then the amino acid. The start codon AUG is outlined in green.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .padding(12)
        }
        .card(radius: 20)
    }

    private var grid: some View {
        Grid(horizontalSpacing: 6, verticalSpacing: 6) {
            GridRow {
                Text("1st").font(.sCaption2.weight(.semibold)).foregroundStyle(.secondary)
                ForEach(0..<4, id: \.self) { second in
                    VStack(spacing: 0) {
                        Text("2nd base").font(.sCaption2).foregroundStyle(.secondary).opacity(second == 0 ? 1 : 0)
                        baseLetter(second, size: 18)
                    }
                    .frame(maxWidth: .infinity)
                }
                Text("3rd").font(.sCaption2.weight(.semibold)).foregroundStyle(.secondary)
            }
            ForEach(0..<4, id: \.self) { first in
                GridRow {
                    baseLetter(first, size: 22).frame(width: 30)
                    ForEach(0..<4, id: \.self) { second in block(first, second) }
                    VStack(spacing: 4) {
                        ForEach(0..<4, id: \.self) { third in baseLetter(third, size: 13).frame(height: 28) }
                    }
                    .frame(width: 24)
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("The genetic code, first base by row, second by column, third within each block")
    }

    private func baseLetter(_ i: Int, size: CGFloat) -> some View {
        let s = GXCodonTable.letters(String(BioCode.bases[i]), dna: store.dnaLetters)
        return Text(s)
            .font(.system(size: size, weight: .bold, design: .monospaced))
            .foregroundColor(BioColor.base(Character(s)))
    }

    private func block(_ first: Int, _ second: Int) -> some View {
        VStack(spacing: 4) {
            ForEach(0..<4, id: \.self) { third in
                cell(GXCodonTable.codon(first: first, second: second, third: third))
            }
        }
        .padding(5)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.04)))
        .frame(maxWidth: .infinity)
    }

    private func cell(_ c: String) -> some View {
        let aa = BioCode.aminoAcid(c) ?? "?"
        let on = lit.contains(c)
        let chosen = store.picked == c
        let tint = aa == "*" ? GXColors.stop.color : BioColor.aminoAcid(aa)
        let shown = GXCodonTable.letters(c, dna: store.dnaLetters)
        let three = aa == "*" ? "Stop" : (BioAminoAcids.byOne[aa]?.three ?? "?")
        return Button {
            withAnimation(Motion.snappy) { store.picked = c }
        } label: {
            HStack(spacing: 6) {
                gxBases(shown, size: 13)
                Spacer(minLength: 2)
                Text(three)
                    .font(.system(size: 13, weight: aa == "*" ? .bold : .medium))
                    .foregroundStyle(aa == "*" ? GXColors.stop.color : Color.primary)
                if c == "AUG" { GXBadge(text: "Start", colour: GXColors.start.color) }
            }
            .padding(.horizontal, 8)
            .frame(height: 28)
            .background(RoundedRectangle(cornerRadius: 7, style: .continuous).fill(tint.opacity(on || chosen ? 0.32 : 0.12)))
            .overlay(RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(chosen ? Theme.accent : (on ? Theme.accent.opacity(0.7) : .clear), lineWidth: chosen ? 2 : 1.5))
            .opacity(lit.isEmpty || on || chosen ? 1 : 0.38)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(shown) → \(aa == "*" ? "stop" : (BioAminoAcids.byOne[aa]?.name ?? "?"))")
        .accessibilityLabel("\(shown.map(String.init).joined(separator: " ")): \(aa == "*" ? "stop codon" : (BioAminoAcids.byOne[aa]?.name ?? "?"))\(c == "AUG" ? ", start codon" : "")")
        .accessibilityAddTraits(chosen ? .isSelected : [])
    }

    // MARK: Right: the amino acid's card

    @ViewBuilder
    private var card: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let c = store.picked ?? (hovered.flatMap { $0.count == 3 ? $0 : nil }) {
                codonCard(c)
            } else if case .aminoAcid(let one) = query, let a = BioAminoAcids.byOne[one] {
                aminoAcidCard(a, codon: nil)
            } else {
                BioCard(title: "Press a codon") {
                    Text("Its amino acid's card comes here: its class, charge, side chain and every codon that codes it. Type in the lookup to light codons.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: "Reading a codon") {
                BioFactRow(label: "Direction", text: "Codons are read on the mRNA, 5′→3′, three bases at a time from the start codon AUG.")
                BioFactRow(label: "Anticodon", text: "A tRNA's anticodon pairs antiparallel: codon 5′-AUG-3′ pairs with anticodon 3′-UAC-5′ (written 5′-CAU-3′ in tRNA tables).")
                BioFactRow(label: "Redundancy", text: "Most amino acids have 2–6 codons, often differing only in the third base; Met and Trp have one each.")
            }
        }
    }

    @ViewBuilder
    private func codonCard(_ c: String) -> some View {
        let aa = BioCode.aminoAcid(c) ?? "?"
        if aa == "*" {
            BioCard(title: "Stop codon", swatch: GXColors.stop.color, subtitle: "\(GXCodonTable.letters(c, dna: store.dnaLetters)) · “\(BioCode.stopNames[c] ?? "stop")”") {
                BioFactRow(label: "What it does", text: "A stop codon codes for no amino acid. A release factor, a protein and not a tRNA, binds it in the ribosome's A site, and the finished polypeptide is let go.")
                BioFactRow(label: "The three stops", text: "UAA (“ochre”), UAG (“amber”) and UGA (“opal”).")
                BioFactRow(label: "Footnote", text: "In vertebrate mitochondria UGA codes Trp instead; this table is the standard code.")
            }
        } else if let a = BioAminoAcids.byOne[aa] {
            aminoAcidCard(a, codon: c)
        }
    }

    private func aminoAcidCard(_ a: BioAminoAcid, codon: String?) -> some View {
        let codons = a.codons.map { GXCodonTable.letters($0, dna: store.dnaLetters) }
        return BioCard(title: a.name, swatch: a.group.color, subtitle: "\(a.three) · \(a.one) · \(a.group.label)") {
            if let codon {
                BioFactRow(label: "This codon", text: "\(GXCodonTable.letters(codon, dna: store.dnaLetters)) → \(a.three)\(codon == "AUG" ? ": the start codon, and Met inside a gene" : "")")
                BioFactRow(label: "Its tRNA's anticodon", text: "3′-\(BioCode.anticodon3to5(codon))-5′ (5′-\(BioCode.anticodon5to3(codon))-3′ in tRNA tables)")
            }
            BioFactRow(label: a.codonCount == 1 ? "Its codon" : "Its \(a.codonCount) codons", text: codons.joined(separator: "  "))
            BioFactRow(label: "Class", text: "\(a.group.label) · \(a.lehninger)")
            BioFactRow(label: "Side chain at pH 7.4", text: a.pKa.map { "Charge \(a.chargeText) · pKa ≈ \(String(format: "%.2f", $0)) (can shift inside a protein)" } ?? "Charge \(a.chargeText)")
            BioFactRow(label: "Hydropathy (Kyte–Doolittle)", text: String(format: "%+.1f", a.hydropathy) + (a.hydropathy > 0 ? " · hydrophobic" : " · hydrophilic"))
            BioFactRow(label: "Mass", text: String(format: "%.2f g/mol free · %.2f g/mol in a chain", a.mass, a.residueMass) + " · \(a.formula)")
            BioFactRow(label: "Note", text: a.note)
            Button {
                BioTools.open(.biomol, info: ["molecule": a.three.uppercased(), "page": "amino"])
            } label: {
                Label("See in 3D", systemImage: "cube").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Open \(a.name) in the Biomolecule Builder")
        }
    }
}

// MARK: - The wheel

/// The codon wheel (§4.3): three rings from the centre — first base (4 × 90°), second (16 × 22.5°), third (64 × 5.625°) —
/// in U, C, A, G order clockwise from 12 o'clock, ring radii 0.18, 0.36, 0.56 and 0.78 of the wheel's, and the amino
/// acids outside at 0.90. Codons a lookup lights are outlined; the one under the pointer shows in the centre; a press
/// picks it (a press on an inner ring lights the codons it begins).
struct GXCodonWheel: View {
    let lit: Set<String>
    let picked: String?
    let dna: Bool
    @Binding var hovered: String?
    let pick: (String) -> Void

    var body: some View {
        GeometryReader { geo in
            let side = min(geo.size.width, geo.size.height)
            let centre = CGPoint(x: geo.size.width / 2, y: geo.size.height / 2)
            let radius = side / 2 - 6
            Canvas { ctx, _ in draw(&ctx, centre: centre, radius: radius) }
                .contentShape(Rectangle())
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let p): hovered = hit(p, centre: centre, radius: radius)
                    case .ended: hovered = nil
                    }
                }
                .gesture(DragGesture(minimumDistance: 0).onEnded { v in
                    guard hypot(v.translation.width, v.translation.height) < 5, let c = hit(v.location, centre: centre, radius: radius) else { return }
                    if c.count == 3 { pick(c) } else { pick(BioCode.entries.first { $0.codon.hasPrefix(c) }?.codon ?? c) }
                })
        }
        .aspectRatio(1, contentMode: .fit)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Codon wheel, read from the centre out")
        .accessibilityValue(hovered.map(spoken) ?? "The grid view lists every codon")
    }

    /// A codon and its amino acid, as VoiceOver says them.
    private func spoken(_ codon: String) -> String {
        guard let aa = BioCode.aminoAcid(codon) else { return codon }
        return "\(codon): " + (aa == "*" ? "stop" : (BioAminoAcids.byOne[aa]?.name ?? ""))
    }

    private func hit(_ p: CGPoint, centre: CGPoint, radius: CGFloat) -> String? {
        let dx = Double(p.x - centre.x), dy = Double(p.y - centre.y)
        let r = (dx * dx + dy * dy).squareRoot() / Double(radius)
        var a = atan2(dx, -dy) * 180 / .pi
        if a < 0 { a += 360 }
        return GXCodonTable.hit(r: r, angle: a)
    }

    private func point(_ c: CGPoint, _ r: CGFloat, _ deg: Double) -> CGPoint {
        let t = deg * .pi / 180
        return CGPoint(x: c.x + r * CGFloat(sin(t)), y: c.y - r * CGFloat(cos(t)))
    }

    private func sector(_ c: CGPoint, _ r0: CGFloat, _ r1: CGFloat, _ a0: Double, _ a1: Double) -> Path {
        var p = Path()
        let n = max(2, Int((a1 - a0) / 2))
        p.move(to: point(c, r0, a0))
        for k in 0...n { p.addLine(to: point(c, r1, a0 + (a1 - a0) * Double(k) / Double(n))) }
        for k in stride(from: n, through: 0, by: -1) { p.addLine(to: point(c, r0, a0 + (a1 - a0) * Double(k) / Double(n))) }
        p.closeSubpath()
        return p
    }

    private func draw(_ ctx: inout GraphicsContext, centre c: CGPoint, radius R: CGFloat) {
        let rings = GXCodonTable.ringRadii.map { CGFloat($0) * R }
        let picking = picked.map { [$0] } ?? []
        let hoverPrefix = hovered ?? ""
        // (the three rings of bases, each sector on its base's tint with its letter)
        for ring in 0..<3 {
            let r0 = rings[ring], r1 = rings[ring + 1]
            for s in GXCodonTable.sectors(ring: ring) {
                let last = s.prefix.last ?? "N"
                let shown = Character(GXCodonTable.letters(String(last), dna: dna))
                let colour = BioColor.base(shown)
                let inLit = lit.contains { $0.hasPrefix(s.prefix) } || picking.contains { $0.hasPrefix(s.prefix) }
                let dim = !(lit.isEmpty && picking.isEmpty) && !inLit
                let under = !hoverPrefix.isEmpty && (hoverPrefix.hasPrefix(s.prefix) || s.prefix.hasPrefix(hoverPrefix))
                let path = sector(c, r0, r1, s.from, s.to)
                ctx.fill(path, with: .color(colour.opacity(dim ? 0.06 : (under ? 0.36 : 0.18))))
                ctx.stroke(path, with: .color(Color.primary.opacity(0.18)), lineWidth: ring == 2 ? 0.5 : 1)
                let mid = point(c, (r0 + r1) / 2, s.mid)
                let size: CGFloat = ring == 0 ? 22 : (ring == 1 ? 15 : max(8, min(11, R * 0.045)))
                ctx.draw(Text(String(shown)).font(.system(size: size, weight: .bold, design: .monospaced)).foregroundColor(colour.opacity(dim ? 0.35 : 1)), at: mid)
            }
        }
        // (the amino acids outside: a band in each class's colour, and the name read outward)
        let band0 = rings[3] + 3, band1 = rings[3] + R * 0.05
        for g in GXCodonTable.wheelLabels {
            let on = g.codons.contains { lit.contains($0) || picking.contains($0) }
            let dim = !(lit.isEmpty && picking.isEmpty) && !on
            let colour = g.aa == "*" ? GXColors.stop.color : BioColor.aminoAcid(g.aa)
            let band = sector(c, band0, band1, g.from + 0.4, g.to - 0.4)
            ctx.fill(band, with: .color(colour.opacity(dim ? 0.2 : 0.85)))
            if on { ctx.stroke(sector(c, rings[2], band1, g.from, g.to), with: .color(Theme.accent), lineWidth: 2) }
            let label = g.aa == "*" ? "Stop" : (BioAminoAcids.byOne[g.aa]?.three ?? "?")
            let mid = (g.from + g.to) / 2
            let at = point(c, CGFloat(GXCodonTable.labelRadius) * R, mid)
            var t = ctx
            t.translateBy(x: at.x, y: at.y)
            // (read outward on the right half, inward on the left, so no name is upside down)
            t.rotate(by: .degrees(mid <= 180 ? mid - 90 : mid + 90))
            t.draw(Text(label).font(.system(size: max(9, min(12, R * 0.05)), weight: on ? .bold : .semibold)).foregroundColor(dim ? Color.secondary.opacity(0.6) : colour), at: .zero)
            if g.codons.contains("AUG") {
                // (the start codon outlined in green)
                ctx.stroke(sector(c, rings[2], rings[3], g.from, g.to), with: .color(GXColors.start.color), lineWidth: 2)
            }
        }
        // (the centre: what is under the pointer, else how to read it)
        let hub = Path(ellipseIn: CGRect(x: c.x - rings[0] + 2, y: c.y - rings[0] + 2, width: 2 * rings[0] - 4, height: 2 * rings[0] - 4))
        ctx.fill(hub, with: .color(Color.primary.opacity(0.04)))
        if let h = hovered, h.count == 3, let aa = BioCode.aminoAcid(h) {
            ctx.draw(Text(GXCodonTable.letters(h, dna: dna)).font(.system(size: 15, weight: .bold, design: .monospaced)), at: CGPoint(x: c.x, y: c.y - 8))
            ctx.draw(Text(aa == "*" ? "Stop" : (BioAminoAcids.byOne[aa]?.three ?? "?")).font(.system(size: 12, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: c.x, y: c.y + 10))
        } else {
            ctx.draw(Text("5′→3′").font(.system(size: 11, weight: .semibold)).foregroundColor(.secondary), at: CGPoint(x: c.x, y: c.y - 7))
            ctx.draw(Text("from here").font(.system(size: 10)).foregroundColor(.secondary), at: CGPoint(x: c.x, y: c.y + 8))
        }
    }
}
