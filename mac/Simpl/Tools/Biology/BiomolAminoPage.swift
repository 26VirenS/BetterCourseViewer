import SceneKit
import SwiftUI

// The Biomolecule Builder's Amino acids page (spec §4.4 Amino acids, §6.2, §6.3): the 20 in a grid grouped by AP/Campbell
// class, each with its name, codes and its R group sketched in 2D; the one picked in 3D from its template (pH 7 form or
// neutral, coloured by element, part or class, its parts labelled), its mirror image as the D-form, and its card —
// class, polarity, side-chain pKa, charge at pH 7.4, hydropathy, mass, codons, its note, and the CORN rule.

struct BiomolAminoPage: View {
    @ObservedObject private var s = BiomolState.shared
    @Environment(\.colorScheme) private var scheme

    /// Each amino acid's R group in 2D (worked out once).
    private static let sketches: [Character: BiomolSketch] = Dictionary(uniqueKeysWithValues: BioAminoAcids.all.map { ($0.one, BiomolAminoAcid.sketch($0.one)) })

    private var aa: BioAminoAcid { BioAminoAcids.byOne[s.aminoAcid] ?? BioAminoAcids.all[0] }

    var body: some View {
        let aa = self.aa
        let model = BiomolAminoAcid.model(aa.one, ph7: s.ph7, mirrored: s.dForm)
        let look = BiomolLook(style: s.style, hydrogens: s.hydrogens("amino", atoms: model.atoms.count), ph7: s.ph7, colourBy: s.colourBy,
                              labels: s.labels, dark: scheme == .dark)
        PTWorkLayout {
            controls(model: model)
        } centre: {
            scene(aa, model: model, look: look)
        } right: {
            facts(aa, model: model, look: look)
        }
    }

    // MARK: The grid and the controls

    private func controls(model: BioMoleculeModel) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("The 20 amino acids")
            ForEach(BioAAClass.allCases, id: \.self) { cls in
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 6) {
                        Circle().fill(cls.color).frame(width: 9, height: 9)
                        Text(cls.label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                    }
                    LazyVGrid(columns: [GridItem(.flexible(), spacing: 6), GridItem(.flexible(), spacing: 6)], spacing: 6) {
                        ForEach(BioAminoAcids.all.filter { $0.group == cls }, id: \.one) { a in cell(a) }
                    }
                }
            }
            BiomolLookControls(page: "amino", atoms: model.atoms.count, handOff: { (model, aa.name) })
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Chirality")
                Toggle("Mirror image (D-form)", isOn: $s.dForm)
                    .disabled(!aa.isChiral)
                    .help(aa.isChiral ? "Show the mirror image: the D-form, not used in proteins" : "Glycine is not chiral: its mirror image is itself")
                BiomolNote(text: aa.isChiral ? "Proteins are built only from L-amino acids." : "Glycine has two H on its α-carbon, so it has no mirror-image form.")
            }
            .font(.sCallout)
        }
    }

    private func cell(_ a: BioAminoAcid) -> some View {
        let on = a.one == s.aminoAcid
        return Button {
            withAnimation(Motion.snappy) {
                s.aminoAcid = a.one
                if !a.isChiral { s.dForm = false }
            }
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 4) {
                    Text(a.name).font(.system(size: 12, weight: .semibold)).lineLimit(1).minimumScaleFactor(0.7)
                    Spacer(minLength: 2)
                    Text("\(a.three) \(String(a.one))").font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                }
                BiomolSketchView(sketch: Self.sketches[a.one] ?? BiomolSketch(atoms: [], bonds: []))
                    .frame(height: 40)
                    .accessibilityHidden(true)
            }
            .padding(6)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(on ? a.group.color.opacity(0.2) : Color.primary.opacity(0.05)))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(on ? a.group.color : Color.clear, lineWidth: 1.5))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("\(a.name) (\(a.three), \(String(a.one))): \(a.group.label)")
        .accessibilityLabel("\(a.name), \(a.three), \(a.group.label)")
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    // MARK: The 3D card

    private func scene(_ aa: BioAminoAcid, model: BioMoleculeModel, look: BiomolLook) -> some View {
        let one = aa.one, mirrored = s.dForm
        let chem = BiomolNodes.chem
        let charges = BiomolAminoAcid.charges(one, ph7: look.ph7)
        let badge = mirrored ? "D-form (not used in proteins)" : (look.ph7 ? "pH 7 form (zwitterion)" : "Neutral form")
        let selected = s.selection.map { " Selected: \(selectionName($0, model: model))." } ?? ""
        return BiomolSceneCard(
            title: mirrored ? "D-\(aa.name.lowercased())" : aa.name,
            subtitle: "\(aa.three) · \(String(aa.one)) · \(aa.group.label)",
            badge: badge,
            key: "amino/\(one)/\(mirrored)/\(look.key)",
            frameKey: "amino/\(one)",
            build: BioSceneBuild { BiomolScenes.aminoAcid(one, look: look, mirrored: mirrored, chem: chem) },
            extras: {
                var nodes: [SCNNode] = []
                if look.labels && look.style != .sticks { nodes += BiomolNodes.atomLabels(model, look: look) }
                nodes += BiomolNodes.marks(BiomolScenes.chargeMarks(model, charges: charges, look: look, chem: chem))
                return nodes
            },
            accessibility: "3D model of \(aa.name.lowercased())\(mirrored ? ", its D-form mirror image" : ""), \(look.ph7 ? "pH 7 form" : "neutral form"), \(model.atoms.count) atoms.\(selected)")
    }

    private func selectionName(_ id: String, model: BioMoleculeModel) -> String {
        if let k = BiomolText.atomIndex(id), k < model.atoms.count { return "\(model.atoms[k].tag) \(model.atoms[k].name)" }
        return partInfo(id)?.title ?? id
    }

    // MARK: The card

    private func facts(_ aa: BioAminoAcid, model: BioMoleculeModel, look: BiomolLook) -> some View {
        let pKa = aa.pKa.map { String(format: "≈ %.2f", $0) } ?? "—"
        let codons = aa.codons.joined(separator: " ")
        let corn = BiomolAminoAcid.cornClockwise(model)
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection { selectionCard(id, model: model) }
            BioCard(title: aa.name, swatch: aa.group.color, subtitle: "\(aa.three) · \(String(aa.one)) · \(aa.lehninger)") {
                BiomolFacts(rows: [
                    ("Class", aa.group.label),
                    ("Polarity", polarity(aa)),
                    ("Side-chain pKa", pKa),
                    ("Charge at pH 7.4", aa.one == "H" ? "≈ 0 (4% +1)" : BiomolText.signed(aa.charge)),
                    ("Hydropathy (Kyte–Doolittle)", String(format: "%+.1f", aa.hydropathy)),
                    ("Mass (free)", String(format: "%.2f g/mol", aa.mass)),
                    ("Mass in a chain", String(format: "%.2f g/mol", aa.residueMass)),
                    ("Formula", BiomolText.formula(aa.formula)),
                    ("Codons (\(aa.codonCount))", codons),
                ])
                BioFactRow(label: "Note", text: aa.note)
                if aa.pKa != nil { BioFactRow(label: "pKa", text: "Values differ by up to about 0.5 between references, and can shift inside a protein.") }
                if aa.one == "G" {
                    BioFactRow(label: "Not chiral", text: "Glycine's R group is just H, so its α-carbon carries two H atoms. It is the only amino acid that is not chiral.")
                }
                if aa.one == "P" {
                    BioFactRow(label: "A ring", text: "Proline's R group bonds back to its own amino N, closing a ring. In a chain it has no N–H to H-bond, so it breaks α-helices.")
                }
            }
            BioCard(title: look.ph7 ? "At pH 7: a zwitterion" : "The neutral form") {
                Text(look.ph7
                     ? "In cells the amino group carries an extra H⁺ (–NH₃⁺) and the carboxyl group has lost one (–COO⁻)\(sideChainCharge(aa)). Turn off “pH 7 form” for the neutral form textbooks draw."
                     : "–NH₂ and –COOH, as textbooks often draw it. In cells (pH 7) the groups are ionised: turn on “pH 7 form”.")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
            }
            if aa.isChiral {
                BioCard(title: "Chirality: the CORN rule") {
                    Text("Look from the H on the α-carbon toward it: in the L-form, the one proteins use, **CO → R → N** run clockwise.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    if let corn {
                        BioFactRow(label: "This model", text: corn ? "Clockwise: the L-form." : "Counter-clockwise: the D-form (not used in proteins).",
                                   tint: corn ? nil : .orange)
                    }
                }
            }
            BiomolPartsList(items: [(id: "part:amino", title: "Amino group"), (id: "part:alpha", title: "α-carbon"),
                                    (id: "part:carboxyl", title: "Carboxyl group"), (id: "part:r", title: "R group (side chain)")])
            BiomolColourLegend(items: legend(aa, model: model, look: look))
        }
    }

    private func selectionCard(_ id: String, model: BioMoleculeModel) -> some View {
        Group {
            if let k = BiomolText.atomIndex(id), k < model.atoms.count {
                let a = model.atoms[k]
                let part = BiomolAminoAcid.template(aa.one).map { BiomolAminoAcid.part(a.name, in: $0).name } ?? ""
                BiomolAtomCard(title: "\(a.tag) · \(a.name)", element: a.element, detail: part)
            } else if let info = partInfo(id) {
                BioCard(title: info.title, swatch: info.colour) {
                    Text(info.text).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private func partInfo(_ id: String) -> (title: String, text: String, colour: Color)? {
        let aa = self.aa
        switch id {
        case "part:amino":
            return ("Amino group", "–NH₂ (–NH₃⁺ at pH 7) on the α-carbon. In a chain it becomes the N–H of a peptide bond.", BiomolParts.amino.colour.color)
        case "part:alpha":
            return ("α-carbon", "The central carbon: bonded to the amino group, the carboxyl group, an H and the R group. Four different groups make it chiral (all but glycine).", BiomolParts.alpha.colour.color)
        case "part:carboxyl":
            return ("Carboxyl group", "–COOH (–COO⁻ at pH 7). In a chain its carbon forms the peptide bond with the next amino acid's N, and water is released.", BiomolParts.carboxyl.colour.color)
        case "part:r":
            return ("R group (side chain)", "The only part that differs between the 20 amino acids. It decides the class: \(aa.name.lowercased()) is \(aa.group.label.lowercased()).", BiomolParts.rGroup.colour.color)
        default:
            return nil
        }
    }

    private func polarity(_ aa: BioAminoAcid) -> String {
        switch aa.group {
        case .nonpolar: return "Nonpolar (hydrophobic)"
        case .polar: return "Polar, uncharged (hydrophilic)"
        case .acidic: return "Charged − (hydrophilic)"
        case .basic: return aa.one == "H" ? "Basic, mostly neutral at 7.4" : "Charged + (hydrophilic)"
        }
    }

    private func sideChainCharge(_ aa: BioAminoAcid) -> String {
        switch aa.one {
        case "D", "E": return ", and the side chain's acid is ionised too (−1), so the net charge is −1"
        case "K", "R": return ", and the side chain carries a + charge, so the net charge is +1"
        case "H": return "; the side chain (pKa ≈ 6) is mostly neutral at pH 7.4"
        default: return ", so the net charge is 0"
        }
    }

    private func legend(_ aa: BioAminoAcid, model: BioMoleculeModel, look: BiomolLook) -> [(color: Color, text: String)] {
        switch look.colourBy {
        case .element: return BiomolColourLegend.elements(model.atoms.map(\.element))
        case .part: return BiomolColourLegend.parts([BiomolParts.amino, BiomolParts.alpha, BiomolParts.carboxyl, BiomolParts.rGroup])
        case .aaClass: return [(aa.group.color, "R group: \(aa.group.label)"), (BiomolParts.grey.color, "The rest (amino group, α-carbon, carboxyl)")]
        }
    }
}

/// An R group in 2D: its bonds as lines (double bonds doubled), its heteroatoms as coloured letters, the α-carbon it
/// hangs from as a dot.
struct BiomolSketchView: View {
    let sketch: BiomolSketch

    var body: some View {
        Canvas { ctx, size in
            guard !sketch.atoms.isEmpty else { return }
            let b = sketch.bounds
            let w = CGFloat(max(b.2 - b.0, 0.6)), h = CGFloat(max(b.3 - b.1, 0.6))
            let scale: CGFloat = min((size.width - 14) / w, (size.height - 12) / h, 15)
            let ox: CGFloat = size.width / 2 - CGFloat(b.0 + b.2) / 2 * scale
            let oy: CGFloat = size.height / 2 + CGFloat(b.1 + b.3) / 2 * scale
            func pt(_ k: Int) -> CGPoint { CGPoint(x: ox + CGFloat(sketch.atoms[k].x) * scale, y: oy - CGFloat(sketch.atoms[k].y) * scale) }
            func labelled(_ k: Int) -> Bool { sketch.atoms[k].element != "C" && !sketch.atoms[k].attachment }
            for bond in sketch.bonds {
                var p = pt(bond.a), q = pt(bond.b)
                let dx = q.x - p.x, dy = q.y - p.y
                let len = max(hypot(dx, dy), 0.001)
                let ux = dx / len, uy = dy / len
                // (lines stop short of a letter)
                if labelled(bond.a) { p = CGPoint(x: p.x + ux * 5, y: p.y + uy * 5) }
                if labelled(bond.b) { q = CGPoint(x: q.x - ux * 5, y: q.y - uy * 5) }
                let offsets: [CGFloat] = bond.order == 2 ? [-1.6, 1.6] : (bond.order == 3 ? [-2.4, 0, 2.4] : [0])
                for o in offsets {
                    var path = Path()
                    path.move(to: CGPoint(x: p.x - uy * o, y: p.y + ux * o))
                    path.addLine(to: CGPoint(x: q.x - uy * o, y: q.y + ux * o))
                    ctx.stroke(path, with: .color(.primary.opacity(0.75)), lineWidth: 1.3)
                }
            }
            for (k, a) in sketch.atoms.enumerated() {
                let p = pt(k)
                if a.attachment {
                    ctx.fill(Path(ellipseIn: CGRect(x: p.x - 2.5, y: p.y - 2.5, width: 5, height: 5)), with: .color(.secondary))
                } else if a.element != "C" {
                    let colour: Color = a.element == "O" ? .red : (a.element == "N" ? .blue : (a.element == "S" ? Color(hex: "#c79a00") : .primary))
                    ctx.draw(Text(a.element).font(.system(size: 10, weight: .bold)).foregroundColor(colour), at: p)
                }
            }
        }
    }
}
