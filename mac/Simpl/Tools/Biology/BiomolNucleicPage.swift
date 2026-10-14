import SceneKit
import SwiftUI

// The Biomolecule Builder's Nucleic acids page (spec §4.4 Nucleic acids, §6.6, §6.7): the eight nucleotides and ATP in
// 3D with their phosphate, sugar and base (the sugar's carbons numbered 1′–5′), and a DNA strand built one nucleotide
// at a time as strand 1 of the B-DNA model — each added one sliding down as its phosphate bonds to the 3′-OH and water
// leaves — its 5′ phosphate and 3′-OH ends and its phosphodiester bonds marked, and its complementary strand beside it
// if asked, which DNA & RNA opens.

struct BiomolNucleicPage: View {
    @ObservedObject private var s = BiomolState.shared
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var strandFocused: Bool

    private func look(_ page: String, atoms: Int, noH: Bool = false) -> BiomolLook {
        BiomolLook(style: s.style, hydrogens: !noH && s.hydrogens(page, atoms: atoms), ph7: s.ph7, colourBy: s.colourBy, labels: s.labels, dark: scheme == .dark)
    }

    var body: some View {
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                BiomolModePicker(items: [(BiomolState.NucleicMode.nucleotides, "Nucleotides"), (.strand, "Build a strand")], selection: $s.nucleicMode)
                if s.nucleicMode == .nucleotides { nucleotideControls } else { strandControls }
            }
        } centre: {
            if s.nucleicMode == .nucleotides { nucleotideScene } else { strandScene }
        } right: {
            if s.nucleicMode == .nucleotides { nucleotideFacts } else { strandFacts }
        }
    }

    // MARK: - Nucleotides

    private var nucleotideControls: some View {
        let items: [(id: String, title: String, colour: Color?)] = BiomolNucleotide.codes.map {
            (id: $0, title: BiomolNucleotide.shortName($0), colour: BioColor.base(BiomolNucleotide.baseLetter($0)))
        }
        let atoms = BioData.molecule(s.nucleotide)?.atoms.count ?? 0
        return VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Nucleotide")
                BiomolChoices(items: items, selected: s.nucleotide, columns: 4) { s.nucleotide = $0 }
                BiomolNote(text: "Top row DNA (deoxyribose), then RNA (ribose), then ATP.")
            }
            BiomolLookControls(page: "nucleotide", atoms: atoms, handOff: {
                guard let m = BioData.molecule(s.nucleotide) else { return nil }
                return (m.model(ph7: s.ph7), BiomolNucleotide.shortName(s.nucleotide))
            })
            Button {
                BioNav.shared.set(.dna, "nucleotides")
                BioTools.open(.dna)
            } label: {
                Label("More in DNA & RNA", systemImage: "link").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Open DNA & RNA's Nucleotides page: base pairing in detail")
        }
    }

    private var nucleotideScene: some View {
        let code = s.nucleotide
        let mol = BioData.molecule(code)
        let model = mol?.model(ph7: s.ph7) ?? BioMoleculeModel()
        let lk = look("nucleotide", atoms: mol?.atoms.count ?? 0)
        let chem = BiomolNodes.chem
        let letter = BiomolNucleotide.baseLetter(code)
        let charges: [String: Int] = s.ph7 ? Dictionary(uniqueKeysWithValues: (mol?.ph7?.remove ?? []).compactMap { h -> (String, Int)? in
            guard let o = mol?.bonds.first(where: { $0.a == h || $0.b == h }).map({ $0.a == h ? $0.b : $0.a }) else { return nil }
            return (o, -1)
        }) : [:]
        return BiomolSceneCard(
            title: BiomolNucleotide.shortName(code),
            subtitle: mol?.title ?? "",
            badge: s.ph7 ? (code == "ATP" ? "pH 7: ATP⁴⁻" : "pH 7: phosphate −2") : "Neutral form",
            key: "nt/\(code)/\(lk.key)",
            frameKey: "nt/\(code)",
            build: BioSceneBuild { BiomolScenes.nucleotide(code, look: lk, chem: chem) },
            extras: {
                var nodes: [SCNNode] = []
                if lk.labels && lk.style != .sticks {
                    nodes += BiomolNodes.atomLabels(model, look: lk) { a in
                        // (the sugar's carbons by number, 1′–5′; the rest by symbol)
                        if a.element == "C", a.name.hasSuffix("'"), let d = a.name.dropFirst().first, d.isNumber { return "\(d)′" }
                        return a.element
                    }
                }
                nodes += BiomolNodes.marks(BiomolScenes.chargeMarks(model, charges: charges, look: lk, chem: chem))
                return nodes
            },
            accessibility: "3D model of \(BiomolNucleotide.shortName(code)): a phosphate, a \(BiomolNucleotide.isDeoxy(code) ? "deoxyribose" : "ribose") sugar and the base \(BiomolNucleotide.baseName(letter).lowercased()); \(model.atoms.count) atoms.")
    }

    private var nucleotideFacts: some View {
        let code = s.nucleotide
        let mol = BioData.molecule(code)
        let letter = BiomolNucleotide.baseLetter(code)
        let deoxy = BiomolNucleotide.isDeoxy(code)
        let purine = BiomolNucleotide.isPurine(letter)
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection, let k = BiomolText.atomIndex(id), let m = mol {
                let model = m.model(ph7: s.ph7)
                if k < model.atoms.count {
                    BiomolAtomCard(title: "\(BiomolNucleotide.shortName(code)) · \(model.atoms[k].name)", element: model.atoms[k].element,
                                   detail: BiomolNucleotide.part(model.atoms[k].name).name)
                }
            }
            BioCard(title: BiomolNucleotide.shortName(code), swatch: BioColor.base(letter), subtitle: mol?.title) {
                BiomolFacts(rows: [
                    ("Base", "\(BiomolNucleotide.baseName(letter)) (\(String(letter)))"),
                    ("Base type", purine ? "Purine: two rings" : "Pyrimidine: one ring"),
                    ("Sugar", deoxy ? "Deoxyribose (DNA)" : "Ribose (RNA)"),
                    ("Phosphates", code == "ATP" ? "3" : "1"),
                    ("Formula", BiomolText.formula(mol?.formula ?? "")),
                    ("Molar mass", String(halfUp: "%.2f g/mol", mol?.mass ?? 0)),
                ])
                BioFactRow(label: "Ribose or deoxyribose", text: deoxy
                           ? "Deoxyribose has only H at its 2′ carbon (no OH): that is the “deoxy”."
                           : "Ribose has an OH on its 2′ carbon; DNA's deoxyribose has only H there.")
                BioFactRow(label: "5′ and 3′", text: "The phosphate sits on the sugar's 5′ carbon. The 3′ carbon's OH is where the next nucleotide joins, so strands grow 5′→3′.")
                if letter == "T" || letter == "U" {
                    BioFactRow(label: "T or U", text: "Thymine (DNA) is uracil (RNA) with a methyl group at C5.")
                }
                if code == "ATP" {
                    BioFactRow(label: "Energy", text: "Its two outer phosphate bonds hold energy the cell spends: ATP → ADP + phosphate.")
                }
            }
            BiomolPartsList(items: [(id: "part:phosphate", title: code == "ATP" ? "Phosphates" : "Phosphate"),
                                    (id: "part:sugar", title: deoxy ? "Deoxyribose" : "Ribose"),
                                    (id: "part:base", title: "Base: \(BiomolNucleotide.baseName(letter).lowercased())")])
            BiomolColourLegend(items: legend(letter))
        }
    }

    private func legend(_ letter: Character) -> [(color: Color, text: String)] {
        switch s.colourBy {
        case .element:
            return BiomolColourLegend.elements(["C", "H", "N", "O", "P"])
        case .part:
            return [(BiomolParts.phosphate.colour.color, "Phosphate"), (BiomolParts.sugar.colour.color, "Sugar"), (BioColor.base(letter), "Base")]
        case .aaClass:
            return [(BioColor.base(letter), "Base \(String(letter))"), (BiomolParts.grey.color, "Sugar and phosphate")]
        }
    }

    // MARK: - A strand

    private var strandControls: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Strand, 5′ → 3′")
                TextField("A, C, G and T", text: Binding(get: { s.strandText }, set: { s.applyStrand($0) }))
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 14, design: .monospaced))
                    .focused($strandFocused)
                    .help("Up to 12 DNA bases, 5′ end first (⌘F)")
                    .accessibilityLabel("Strand sequence")
                if let m = s.strandMessage { BiomolNote(text: m, warning: true) }
                Button("Edit the strand") { strandFocused = true }
                    .keyboardShortcut("f", modifiers: .command)
                    .frame(width: 0, height: 0)
                    .opacity(0)
                    .accessibilityHidden(true)
                HStack(spacing: 6) {
                    ForEach(["A", "C", "G", "T"].map { Character($0) }, id: \.self) { b in
                        Button { s.addNucleotide(b) } label: {
                            Text(String(b))
                                .font(.system(size: 16, weight: .bold, design: .monospaced))
                                .foregroundStyle(BioColor.base(b))
                                .frame(maxWidth: .infinity, minHeight: 36)
                                .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(BioColor.base(b).opacity(0.16)))
                                .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .disabled(s.strand.count >= BiomolStrand.maxLength)
                        .help("Add d\(String(b))MP at the 3′ end (its phosphate bonds to the 3′-OH; water is released)")
                        .accessibilityLabel("Add \(BiomolNucleotide.baseName(b))")
                    }
                }
                Button { s.removeLastNucleotide() } label: { Label("Remove last", systemImage: "drop").frame(maxWidth: .infinity) }
                    .glassButton()
                    .disabled(s.strand.isEmpty)
                    .help("Take the 3′ nucleotide off: hydrolysis of its phosphodiester bond")
            }
            VStack(alignment: .leading, spacing: 10) {
                Toggle("Complementary strand", isOn: $s.complement)
                    .help("Add strand 2, paired A·T and G·C and running the other way")
                Button {
                    BioNav.shared.set(.dna, "helix")
                    BioTools.open(.dna, info: ["sequence": s.strand])
                } label: {
                    Label("Open in DNA & RNA", systemImage: "link").frame(maxWidth: .infinity)
                }
                .glassButton()
                .controlSize(.large)
                .disabled(s.strand.isEmpty)
                .help("See this strand's double helix in DNA & RNA")
            }
            .font(.sCallout)
            BiomolLookControls(page: "strand", atoms: 0, noHydrogens: "The strand is drawn with heavy atoms only, as the B-DNA model has it.",
                               noPH: "The strand is drawn without H, so its form does not change.", handOff: {
                guard !s.strand.isEmpty else { return nil }
                return (BiomolStrand.build(s.strand, complement: s.complement).model, "DNA \(s.strand)")
            })
        }
    }

    @ViewBuilder
    private var strandScene: some View {
        if s.strand.isEmpty {
            BiomolFlatCard(title: "A DNA strand", subtitle: "Add nucleotides to start") {
                Text("Add A, C, G or T: each new nucleotide joins the strand's 3′ end.").font(.sBody).foregroundStyle(.secondary).padding(40)
            }
        } else {
            let seq = s.strand, comp = s.complement
            let lk = look("strand", atoms: 0, noH: true)
            let chem = BiomolNodes.chem
            let motion = reduceMotion ? nil : s.motion([.strandAdded, .strandRemoved])
            let kind = motion?.kind
            let old = motion?.oldStrand
            let n = seq.count
            BiomolSceneCard(
                title: "5′-\(seq)-3′",
                subtitle: comp ? "Both strands: antiparallel, paired A·T and G·C" : "\(n) nucleotide\(n == 1 ? "" : "s") · \(max(n - 1, 0)) phosphodiester bond\(n == 2 ? "" : "s")",
                badge: "B-DNA, heavy atoms",
                key: "strand/\(seq)/\(comp)/\(lk.key)/\(motion?.serial ?? 0)",
                // (framed afresh every third nucleotide as it grows, and when the complement comes or goes)
                frameKey: "strand/\(s.strandFrame)/\(n / 3)",
                build: BioSceneBuild {
                    let d = BiomolStrand.build(seq, complement: comp)
                    var opts = BiomolScenes.StrandOptions()
                    if kind == .strandAdded { opts.hiddenPair = d.sequence.count - 1 }
                    return BiomolScenes.strand(d, look: lk, options: opts, chem: chem)
                },
                extras: {
                    switch kind {
                    case .strandAdded?: return BiomolNodes.motion(BiomolScenes.strandAdded(BiomolStrand.build(seq, complement: comp), look: lk, chem: chem))
                    case .strandRemoved?:
                        guard let old else { return [] }
                        return BiomolNodes.motion(BiomolScenes.strandRemoved(old: BiomolStrand.build(old, complement: comp), look: lk, chem: chem))
                    default: return []
                    }
                },
                accessibility: "3D model of a DNA strand, 5 prime \(seq.map(String.init).joined(separator: " ")) 3 prime\(comp ? ", with its complementary strand" : ""), heavy atoms only.")
        }
    }

    private var strandFacts: some View {
        let seq = s.strand
        let n = seq.count
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection, let k = BiomolText.atomIndex(id) {
                let d = BiomolStrand.build(seq, complement: s.complement)
                if k < d.atoms.count {
                    let a = d.atoms[k]
                    let number = a.strand == 1 ? a.pair + 1 : n - a.pair
                    BiomolAtomCard(title: "Strand \(a.strand) · \(String(a.base)) \(number) · \(a.name)", element: a.element,
                                   detail: BiomolNucleotide.part(a.name).name)
                }
            } else if s.selection?.hasPrefix("pdiester") == true {
                BioCard(title: "Phosphodiester bond", swatch: BiomolParts.bondHighlight.color) {
                    Text("C3′–O3′–P–O5′–C5′: the phosphate joins the 3′ carbon of one sugar to the 5′ carbon of the next. These bonds make the sugar–phosphate backbone.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: "Building a strand", subtitle: "Strand 1 of B-DNA, 5′ → 3′") {
                BiomolFacts(rows: [("Nucleotides", "\(n)"), ("Phosphodiester bonds", "\(max(n - 1, 0))"), ("Water released", "\(max(n - 1, 0)) H₂O")])
                BioFactRow(label: "Which end grows", text: "Each nucleotide's phosphate bonds to the 3′-OH at the end, so a strand grows 5′→3′. Its 5′ end keeps a free phosphate.")
                BioFactRow(label: "In cells", text: "The nucleotide that comes in is a triphosphate (dNTP), and pyrophosphate (two phosphates) is released, not water.")
            }
            if s.complement && n > 0 {
                BioCard(title: "Complementary strand") {
                    VStack(alignment: .leading, spacing: 6) {
                        BioSequenceText(sequence: seq)
                        BioSequenceText(sequence: BioSeq.complement(seq), fivePrimeFirst: false)
                    }
                    BioFactRow(label: "Antiparallel", text: "Strand 2 pairs base for base (A·T, G·C) but runs 3′→5′ beside it. Written 5′→3′ it is \(BioSeq.reverseComplement(seq)) (the reverse complement).")
                }
            }
            if n > 0 { BiomolPartsList(items: strandParts(n)) }
            BiomolColourLegend(items: legend())
        }
    }

    private func strandParts(_ n: Int) -> [(id: String, title: String)] {
        var out: [(id: String, title: String)] = [(id: "nt:1:0", title: "5′ end (phosphate)"), (id: "nt:1:\(n - 1)", title: "3′ end (–OH)")]
        if n > 1 { out.append((id: "pdiester", title: "Phosphodiester bonds (\(n - 1))")) }
        if s.complement {
            out.append((id: "nt:2", title: "Complementary strand"))
            out.append((id: "bp", title: "Base-pair H-bonds"))
        }
        return out
    }

    private func legend() -> [(color: Color, text: String)] {
        var out: [(color: Color, text: String)] = []
        switch s.colourBy {
        case .element: out = BiomolColourLegend.elements(["C", "N", "O", "P"])
        case .part:
            out.append((BiomolParts.phosphate.colour.color, "Phosphate"))
            out.append((BiomolParts.sugar.colour.color, "Sugar"))
            for b in "ACGT" { out.append((BioColor.base(b), "Base \(String(b))")) }
        case .aaClass:
            out.append((BioPalette.strand1.color, "Strand 1 backbone"))
            out.append((BioPalette.strand2.color, "Strand 2 backbone"))
            for b in "ACGT" { out.append((BioColor.base(b), "Base \(String(b))")) }
        }
        out.append((BiomolParts.bondHighlight.color, "Phosphodiester bond"))
        if s.complement { out.append((BioPalette.hBond.color, "Base-pair H-bond")) }
        return out
    }
}
