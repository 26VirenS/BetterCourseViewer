import SceneKit
import SwiftUI

// The Biomolecule Builder's Proteins page (spec §4.4 Proteins, §6.4, §6.10). Build: a sequence (one-letter codes, up to
// 40; a palette adds a residue at the C-terminus and plays its dehydration, Remove last its hydrolysis), each residue's
// secondary structure painted with chips (coil, α-helix, β-strand, and the rarer ones in a menu), the peptide built as
// §6.4 says with its backbone H-bonds and an optional cartoon, the β-sheet demo, and the results: primary structure,
// peptide bonds, water released, mass, net charge, GRAVY and counts by class. Structure levels: the primary structure
// as beads, the secondary as an α-helix beside a β-sheet, the tertiary as ubiquitin (its hydrophobic core and its
// surface; denatured into a random coil and back) and the quaternary as haemoglobin with its four hemes.

struct BiomolProteinPage: View {
    @ObservedObject private var s = BiomolState.shared
    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var sequenceFocused: Bool

    private func look(_ page: String, atoms: Int) -> BiomolLook {
        BiomolLook(style: s.style, hydrogens: s.hydrogens(page, atoms: atoms), ph7: s.ph7, colourBy: s.colourBy, labels: s.labels, dark: scheme == .dark)
    }

    var body: some View {
        PTWorkLayout {
            VStack(alignment: .leading, spacing: 14) {
                BiomolModePicker(items: [(BiomolState.ProteinMode.build, "Build"), (.levels, "Structure levels")], selection: $s.proteinMode)
                if s.proteinMode == .build { buildControls } else { levelControls }
            }
        } centre: {
            if s.proteinMode == .build { buildScene } else { levelScene }
        } right: {
            if s.proteinMode == .build { buildFacts } else { levelFacts }
        }
    }

    // MARK: - Build: the controls

    private var buildControls: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Sequence, N → C")
                TextField("One-letter codes", text: Binding(get: { s.sequenceText }, set: { s.applySequence($0) }))
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 14, design: .monospaced))
                    .focused($sequenceFocused)
                    .help("Up to 40 amino acids by their one-letter codes, N-terminus first (⌘F)")
                    .accessibilityLabel("Sequence, one-letter codes")
                if let m = s.sequenceMessage { BiomolNote(text: m, warning: true) }
                Button("Edit the sequence") { sequenceFocused = true }
                    .keyboardShortcut("f", modifiers: .command)
                    .frame(width: 0, height: 0)
                    .opacity(0)
                    .accessibilityHidden(true)
            }
            VStack(alignment: .leading, spacing: 6) {
                BioHeading("Add an amino acid")
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 4), count: 5), spacing: 4) {
                    ForEach(BioAminoAcids.all, id: \.one) { a in paletteButton(a) }
                }
                HStack(spacing: 6) {
                    Button { s.removeLastResidue() } label: { Label("Remove last", systemImage: "drop").frame(maxWidth: .infinity) }
                        .glassButton()
                        .disabled(s.sequence.isEmpty)
                        .help("Take the C-terminal residue off: hydrolysis, using one water")
                    Button {
                        s.applySequence(BiomolPeptideBuilder.defaultSequence, animate: false)
                        s.setStructure(BiomolSS.text(BiomolState.defaultStructure(count: BiomolPeptideBuilder.defaultSequence.count)))
                    } label: { Text("Example").frame(maxWidth: .infinity) }
                        .glassButton()
                        .help("Back to the example peptide, \(BiomolPeptideBuilder.defaultSequence)")
                }
                .font(.sCallout)
            }
            VStack(alignment: .leading, spacing: 8) {
                BioHeading("Secondary structure")
                HStack(spacing: 6) {
                    ForEach([BiomolSS.coil, .helix, .strand], id: \.self) { ss in brushButton(ss) }
                    Menu {
                        ForEach([BiomolSS.parallel, .helix310, .leftHelix], id: \.self) { ss in
                            Button(ss.label) { s.brush = ss }
                        }
                    } label: {
                        Text([BiomolSS.parallel, .helix310, .leftHelix].contains(s.brush) ? s.brush.label : "More")
                    }
                    .menuStyle(.button)
                    .fixedSize()
                    .help("Rarer structures: parallel β-strand, 3₁₀ helix, left-handed α-helix")
                }
                chips
                BiomolNote(text: "Pick a structure, then press or drag across residues to give it to them.")
                HStack(spacing: 6) {
                    ForEach([BiomolSS.coil, .helix, .strand], id: \.self) { ss in
                        Button("All \(ss == .coil ? "coil" : (ss == .helix ? "helix" : "strand"))") {
                            s.setStructure(ss, at: Array(0..<s.sequence.count))
                        }
                        .glassButton()
                        .help("Build every residue as \(ss.label.lowercased())")
                    }
                }
                .font(.sCallout)
            }
            VStack(alignment: .leading, spacing: 10) {
                BioHeading("Show")
                Toggle("Backbone H-bonds", isOn: $s.hBonds)
                    .help("Dashed lines from each backbone N–H to the C=O it bonds to")
                HStack(spacing: 8) {
                    Text("Ribbon").foregroundStyle(.secondary)
                    Picker("Ribbon", selection: $s.ribbon) {
                        ForEach(BiomolRibbon.allCases, id: \.self) { r in Text(r.label).tag(r) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                }
                .help("The cartoon: helices as ribbons, strands as arrows, the rest as tubes — over the atoms or alone")
                Button {
                    withAnimation(Motion.gentle) { s.sheetDemo.toggle() }
                } label: {
                    Label(s.sheetDemo ? "Back to the peptide" : "β-sheet demo", systemImage: s.sheetDemo ? "arrow.uturn.backward" : "square.stack.3d.up")
                        .frame(maxWidth: .infinity)
                }
                .glassButton(prominent: s.sheetDemo)
                .controlSize(.large)
                .help("Three antiparallel strands held together by backbone H-bonds")
            }
            .font(.sCallout)
            BiomolLookControls(page: "peptide", atoms: s.peptide?.atoms.count ?? 0, handOff: {
                guard let p = s.peptide, !s.sheetDemo else { return nil }
                return (p.model(hydrogens: true).model, "Peptide \(String(p.sequence))")
            })
        }
    }

    private func paletteButton(_ a: BioAminoAcid) -> some View {
        Button { s.addResidue(a.one) } label: {
            VStack(spacing: 0) {
                Text(String(a.one)).font(.system(size: 14, weight: .bold, design: .monospaced))
                Text(a.three).font(.system(size: 9)).foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 36)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(a.group.color.opacity(0.22)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(s.sequence.count >= BiomolPeptideBuilder.maxResidues)
        .help("Add \(a.name) at the C-terminus (dehydration synthesis: one water released)")
        .accessibilityLabel("Add \(a.name)")
    }

    private func brushButton(_ ss: BiomolSS) -> some View {
        Button { s.brush = ss } label: {
            HStack(spacing: 5) {
                RoundedRectangle(cornerRadius: 2).fill(BioPalette.secondary(ss.cartoon).color).frame(width: 10, height: 10)
                Text(ss.label).font(.sCallout.weight(.medium)).lineLimit(1)
            }
        }
        .glassButton(prominent: s.brush == ss)
        .help("Paint residues as \(ss.label.lowercased()) (φ \(Int(ss.angles.phi))°, ψ \(Int(ss.angles.psi))°)")
        .accessibilityAddTraits(s.brush == ss ? .isSelected : [])
    }

    /// The residues as chips, ten to a row: each its letter in its class's colour over its structure's colour; pressed or
    /// dragged across, they take the picked structure.
    private var chips: some View {
        let seq = Array(s.sequence)
        let n = seq.count
        let cols = 10
        let rows = (n + cols - 1) / cols
        return VStack(alignment: .leading, spacing: 3) {
            ForEach(0..<max(rows, 0), id: \.self) { row in
                HStack(spacing: 3) {
                    ForEach((row * cols)..<min(n, row * cols + cols), id: \.self) { i in chip(i, seq[i]) }
                }
            }
        }
        .coordinateSpace(.named("biomolChips"))
        .gesture(DragGesture(minimumDistance: 0, coordinateSpace: .named("biomolChips")).onChanged { v in
            let col = Int(v.location.x / 27), row = Int(v.location.y / 33)
            guard col >= 0, col < cols, row >= 0, v.location.x >= 0, v.location.y >= 0 else { return }
            let i = row * cols + col
            if i < n { s.setStructure(s.brush, at: [i]) }
        })
    }

    private func chip(_ i: Int, _ aa: Character) -> some View {
        let ss = i < s.structure.count ? s.structure[i] : .coil
        let built = s.peptide.flatMap { $0.count == s.sequence.count ? $0.built[i] : nil } ?? ss
        return VStack(spacing: 2) {
            Text(String(aa)).font(.system(size: 13, weight: .bold, design: .monospaced)).foregroundStyle(BioColor.aminoAcid(aa))
            RoundedRectangle(cornerRadius: 1.5).fill(BioPalette.secondary(built.cartoon).color).frame(height: 4)
        }
        .frame(width: 24, height: 30)
        .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(BioPalette.secondary(ss.cartoon).color.opacity(0.16)))
        .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(built != ss ? Color.orange : Color.clear, lineWidth: 1))
        .accessibilityElement()
        .accessibilityLabel("\(BioAminoAcids.byOne[aa]?.name ?? String(aa)) \(i + 1), \(ss.label)")
        .accessibilityAction(named: "Make \(s.brush.label)") { s.setStructure(s.brush, at: [i]) }
        .help("\(BioAminoAcids.byOne[aa]?.three ?? String(aa)) \(i + 1): \(ss.label)\(built != ss ? " (built as coil: proline breaks the helix)" : "")")
    }

    // MARK: - Build: the scene

    @ViewBuilder
    private var buildScene: some View {
        if s.sheetDemo {
            let lk = look("peptide", atoms: 3 * 7 * 10)
            let chem = BiomolNodes.chem
            BiomolSceneCard(title: "β-sheet demo", subtitle: "Three antiparallel strands, 7 residues each", badge: "Backbone H-bonds",
                            key: "sheet/\(lk.key)", frameKey: "sheet",
                            build: BioSceneBuild { BiomolScenes.sheetScene(BiomolScenes.sheetUpright(look: lk), look: lk, chem: chem) },
                            accessibility: "3D model of a β-sheet: three antiparallel strands joined by backbone H-bonds.")
        } else if let p = s.peptide, p.count > 0 {
            peptideScene(p)
        } else {
            BiomolFlatCard(title: "Peptide", subtitle: s.sequence.isEmpty ? "Type a sequence or add amino acids." : "Building…") {
                if s.sequence.isEmpty {
                    Text("Add amino acids from the palette, or type their one-letter codes.").font(.sBody).foregroundStyle(.secondary).padding(40)
                } else {
                    ProgressView().controlSize(.small)
                }
            }
        }
    }

    private func peptideScene(_ p: BiomolPeptide) -> some View {
        let lk = look("peptide", atoms: p.atoms.count)
        let chem = BiomolNodes.chem
        let motion = reduceMotion ? nil : s.motion([.peptideAdded, .peptideRemoved])
        var options = BiomolScenes.PeptideOptions(hBonds: s.hBonds, ribbon: s.ribbon)
        if motion?.kind == .peptideAdded { options.hidden = [p.count - 1] }
        if motion?.kind == .peptideRemoved { options.hideTerminalOH = true }
        let opts = options
        let old = motion?.oldPeptide
        let kind = motion?.kind
        let helixCount = p.hBonds.count
        let segs = BiomolScenes.segmentOf(p).segments.filter { $0.kind != "C" }
        let described = segs.map { "\(BiomolCartoon.name($0.kind)) \($0.first + 1)–\($0.last + 1)" }.joined(separator: ", ")
        return BiomolSceneCard(
            title: "Peptide · \(p.count) residue\(p.count == 1 ? "" : "s")",
            subtitle: p.prolineBreaks.isEmpty ? "N-terminus \(p.residueName(0)) → C-terminus \(p.residueName(p.count - 1))" : "Proline breaks α-helices",
            badge: s.ph7 ? "pH 7 form" : "Neutral form",
            key: "peptide/\(s.peptideVersion)/\(opts.hBonds)/\(opts.ribbon.rawValue)/\(lk.key)/\(motion?.serial ?? 0)",
            frameKey: "peptide/\(s.peptideFrame)",
            build: BioSceneBuild { BiomolScenes.peptide(p, look: lk, options: opts, chem: chem) },
            extras: {
                var nodes: [SCNNode] = []
                if opts.ribbon != .only { nodes += BiomolNodes.marks(BiomolScenes.peptideMarks(p, look: lk, chem: chem, hidden: opts.hidden)) }
                switch kind {
                case .peptideAdded?: nodes += BiomolNodes.motion(BiomolScenes.peptideAdded(p, look: lk, chem: chem))
                case .peptideRemoved?: if let old { nodes += BiomolNodes.motion(BiomolScenes.peptideRemoved(old: old, look: lk, chem: chem)) }
                default: break
                }
                return nodes
            },
            accessibility: "3D model of a peptide of \(p.count) amino acids, \(String(p.sequence))\(described.isEmpty ? ", all coil" : ": " + described); \(helixCount) backbone H-bonds.")
    }

    // MARK: - Build: the results

    private var buildFacts: some View {
        VStack(alignment: .leading, spacing: 14) {
            if s.sheetDemo {
                sheetFacts
            } else if let p = s.peptide, p.count > 0 {
                if let id = s.selection { peptideSelection(id, p) }
                peptideFacts(p)
            }
        }
    }

    private var sheetFacts: some View {
        let sheet = BiomolSheet.build("AAAAAAA", ph7: s.ph7)
        let h12 = sheet.hBonds.filter { Set([$0.donor, $0.acceptor]) == [0, 1] }.count
        let h23 = sheet.hBonds.filter { Set([$0.donor, $0.acceptor]) == [1, 2] }.count
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "A β-pleated sheet", swatch: BioPalette.strand.color, subtitle: "Three strands of Ala₇, side by side") {
                BiomolFacts(rows: [("Strands", "3, antiparallel"), ("H-bonds, strands 1–2", "\(h12)"), ("H-bonds, strands 2–3", "\(h23)"),
                                   ("Strand φ / ψ", "−139° / +135°"), ("Rise per residue", "≈ 3.5 Å")])
                BioFactRow(label: "What holds it", text: "H-bonds between the backbone N–H of one strand and the C=O of the next — not the side chains, which point above and below the sheet.")
                BioFactRow(label: "In real proteins", text: "Strands of a sheet come from different parts of one chain, joined by turns and loops.")
            }
        }
    }

    private func peptideFacts(_ p: BiomolPeptide) -> some View {
        let seq = String(p.sequence)
        let n = p.count
        let mass = BioAminoAcids.peptideMass(seq)
        let charge = BioAminoAcids.netCharge(seq)
        let gravy = BioAminoAcids.gravy(seq)
        let counts = BioAAClass.allCases.map { c in (c, p.sequence.filter { BioAminoAcids.byOne[$0]?.group == c }.count) }
        let helixBonds = p.hBonds.filter { $0.donor - $0.acceptor == 4 }.count
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "Primary structure", subtitle: "The order of its amino acids, N-terminus to C-terminus") {
                VStack(alignment: .leading, spacing: 4) {
                    Text(s.ph7 ? "N-terminus (–NH₃⁺)" : "N-terminus (–NH₂)").font(.sCaption).foregroundStyle(.secondary)
                    seq.enumerated().reduce(Text("")) { t, e in
                        t + Text(String(e.element)).foregroundColor(BioColor.aminoAcid(e.element)) + Text((e.offset + 1) % 10 == 0 ? " " : "")
                    }
                    .font(.system(size: 15, weight: .semibold, design: .monospaced))
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                    .accessibilityLabel("Sequence \(seq.map(String.init).joined(separator: " "))")
                    Text(s.ph7 ? "C-terminus (–COO⁻)" : "C-terminus (–COOH)").font(.sCaption).foregroundStyle(.secondary)
                }
            }
            BiomolFacts(rows: [
                ("Residues", "\(n)"),
                ("Peptide bonds", "\(max(n - 1, 0))"),
                ("Water released", "\(max(n - 1, 0)) H₂O"),
                ("Molecular mass", String(format: "%.2f g/mol", mass)),
                ("Net charge at pH 7", BiomolText.signed(charge)),
                ("GRAVY (hydropathy)", String(format: "%+.2f", gravy) + (gravy > 0 ? " hydrophobic" : " hydrophilic")),
            ])
            VStack(alignment: .leading, spacing: 6) {
                BioHeading("By class")
                ForEach(counts.indices, id: \.self) { i in
                    let (c, k) = counts[i]
                    HStack(spacing: 8) {
                        Circle().fill(c.color).frame(width: 10, height: 10)
                        Text(c.label).font(.sCallout)
                        Spacer()
                        Text("\(k)").font(.sCallout.weight(.semibold).monospacedDigit())
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            .padding(14)
            .card(radius: 14)
            if p.sequence.contains("H") {
                BiomolNote(text: "Net charge counts +1 per Lys and Arg, −1 per Asp and Glu, and +1 −1 for the two ends. His (pKa ≈ 6) counts 0: only about 4% of it is +1 at pH 7.4.")
            }
            BioCard(title: "Backbone H-bonds", swatch: BioPalette.hBond.color) {
                Text(helixBonds > 0
                     ? "\(p.hBonds.count) H-bonds. In a helix each backbone N–H bonds to the C=O four residues back (i → i−4): between backbone atoms, not side chains. 3.6 residues a turn."
                     : (p.hBonds.isEmpty ? "None. A helix makes them inside itself; a strand needs a second strand beside it (try the β-sheet demo)." : "\(p.hBonds.count) H-bonds between backbone N–H and C=O."))
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                if !p.prolineBreaks.isEmpty {
                    BioFactRow(label: "Proline breaks α-helices", text: "Pro \(p.prolineBreaks.map { String($0 + 1) }.joined(separator: ", ")) has no N–H to H-bond, and its ring clashes with the C=O four back, so it and the residue before it are built as coil.", tint: .orange)
                }
                if p.sequence.contains("P") && p.built.contains(where: \.isStrand) {
                    BioFactRow(label: "Proline in a strand", text: "Proline's ring holds its φ near −65°, so in a strand it is built with the coil's φ.")
                }
            }
            BiomolColourLegend(items: peptideLegend(p))
        }
    }

    private func peptideSelection(_ id: String, _ p: BiomolPeptide) -> some View {
        Group {
            if let k = BiomolText.atomIndex(id), k < p.atoms.count {
                let a = p.atoms[k]
                let aa = BioAminoAcids.byOne[p.sequence[a.residue]]
                BiomolAtomCard(title: "\(p.residueName(a.residue)) · \(a.name)", element: a.element,
                               detail: "\(aa?.group.label ?? "") · \(p.built[a.residue].label)")
            } else if id.hasPrefix("hbond") {
                BioCard(title: "A backbone H-bond", swatch: BioPalette.hBond.color) {
                    Text("The N–H of one residue's backbone and the C=O of another: weak on its own, strong in their dozens. It holds the helix (or sheet) — the side chains are not part of it.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            } else if id.hasPrefix("pbond") {
                BioCard(title: "Peptide bond", swatch: BiomolParts.bondHighlight.color) {
                    Text("C–N between one residue's C=O carbon and the next residue's N (about 1.33 Å). Flat and rigid (ω = 180°): it is made by dehydration synthesis, one water for each.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            } else if let k = segmentIndex(id), case let segs = BiomolScenes.segmentOf(p).segments, k < segs.count {
                let sg = segs[k]
                BioCard(title: BiomolCartoon.name(sg.kind), swatch: BioPalette.secondary(sg.kind).color,
                        subtitle: "Residues \(sg.first + 1)–\(sg.last + 1) (\(sg.count))") {
                    Text(sg.kind == "H" ? "A right-handed coil, 3.6 residues a turn, held by backbone H-bonds from each N–H to the C=O four back."
                         : (sg.kind == "E" ? "An extended, pleated stretch. It H-bonds to another strand beside it to make a sheet."
                            : "Turns and loops: no regular pattern of backbone H-bonds."))
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    /// "ss:3" or "ss:3:res:…" → 3.
    private func segmentIndex(_ id: String) -> Int? {
        let parts = id.split(separator: ":")
        guard parts.count >= 2, parts[0] == "ss" else { return nil }
        return Int(parts[1])
    }

    private var quaternaryLegend: [(color: Color, text: String)] {
        var out: [(color: Color, text: String)] = []
        for c in ["A", "B", "C", "D"].map({ Character($0) }) {
            out.append((Color(hex: BiomolProteins.chainHex[c] ?? "#8e8e93"), "\(BiomolProteins.chainName[c] ?? "") chain"))
        }
        out.append((Color(hex: BiomolProteins.hemeHex), "Heme"))
        out.append((Color(hex: BiomolProteins.ironHex), "Iron (Fe²⁺)"))
        out.append((BioPalette.acidic.color, "β6 Glu"))
        return out
    }

    private func peptideLegend(_ p: BiomolPeptide) -> [(color: Color, text: String)] {
        var out: [(color: Color, text: String)] = []
        switch s.colourBy {
        case .element: out = BiomolColourLegend.elements(p.atoms.map(\.element))
        case .part:
            out.append((BiomolParts.backbone.colour.color, "Backbone"))
            out.append((BiomolParts.bondHighlight.color, "Peptide bonds"))
            for c in BioAAClass.allCases { out.append((c.color, "R group: \(c.label)")) }
        case .aaClass: out = BiomolColourLegend.classes()
        }
        if s.ribbon != .off {
            out += [(BioPalette.helix.color, "α-helix ribbon"), (BioPalette.strand.color, "β-strand arrow"), (BioPalette.coil.color, "Coil tube")]
        }
        if s.hBonds { out.append((BioPalette.hBond.color, "H-bond (dashed)")) }
        if s.ph7 { out += [(BioPalette.basic.color, "+ charge"), (BioPalette.acidic.color, "− charge")] }
        return out
    }

    // MARK: - Structure levels

    private var levelControls: some View {
        VStack(alignment: .leading, spacing: 14) {
            let levels: [(id: BiomolState.Level, title: String, colour: Color?)] = BiomolState.Level.allCases.map { (id: $0, title: $0.title, colour: nil) }
            let shows: [(id: BiomolProteins.Show, title: String, colour: Color?)] = BiomolProteins.Show.allCases.map { (id: $0, title: $0.label, colour: nil) }
            BioHeading("Level")
            BiomolChoices(items: levels, selected: s.level, columns: 2) { s.level = $0 }
            switch s.level {
            case .primary:
                BiomolNote(text: "The sequence built on Build, \(s.sequence.count) residues. Change it there.")
                Button { s.proteinMode = .build } label: { Text("Edit the sequence").frame(maxWidth: .infinity) }.glassButton()
            case .secondary:
                BiomolLookControls(page: "secondary", atoms: 330)
            case .tertiary:
                VStack(alignment: .leading, spacing: 10) {
                    BioHeading("Side chains")
                    BiomolChoices(items: shows, selected: s.show, columns: 1) { s.show = $0 }
                        .disabled(s.denatured)
                    BiomolNote(text: "Each sphere is a side chain's centre, coloured by class.")
                    Button {
                        s.setDenatured(!s.denatured)
                    } label: {
                        Label(s.denatured ? "Fold it back" : "Denature", systemImage: s.denatured ? "arrow.uturn.backward" : "flame")
                            .frame(maxWidth: .infinity)
                    }
                    .glassButton(prominent: s.denatured)
                    .controlSize(.large)
                    .help("Unfold ubiquitin into a random coil: the shape is lost, the peptide bonds stay")
                    Toggle("Labels", isOn: $s.labels)
                }
                .font(.sCallout)
            case .quaternary:
                VStack(alignment: .leading, spacing: 10) {
                    Toggle("Labels", isOn: $s.labels)
                    Button {
                        BioNav.shared.set(.gene, "mutations")
                        BioTools.open(.gene, info: ["mutation": "sickle"])
                    } label: {
                        Label("The sickle-cell mutation", systemImage: "arrow.triangle.swap").frame(maxWidth: .infinity)
                    }
                    .glassButton()
                    .controlSize(.large)
                    .help("Open Gene Expression's Mutations page: β6 Glu → Val")
                }
                .font(.sCallout)
            }
        }
    }

    @ViewBuilder
    private var levelScene: some View {
        let dark = scheme == .dark
        let labels = s.labels
        switch s.level {
        case .primary:
            BiomolFlatCard(title: "Primary structure", subtitle: "\(s.sequence.count) amino acids joined by \(max(s.sequence.count - 1, 0)) peptide bonds, N-terminus to C-terminus",
                           hint: "Each bead is an amino acid, coloured by class") {
                BiomolBeads(sequence: Array(s.sequence))
            }
        case .secondary:
            let lk = look("secondary", atoms: 330)
            let chem = BiomolNodes.chem
            BiomolSceneCard(title: "Secondary structure", subtitle: "An α-helix and a β-pleated sheet, held by backbone H-bonds", badge: "Ala₁₂ · Ala₇ × 3",
                            key: "secondary/\(lk.key)", frameKey: "secondary",
                            build: BioSceneBuild { BiomolScenes.secondary(look: lk, chem: chem) },
                            accessibility: "3D model of an α-helix beside a three-stranded β-sheet, with their backbone H-bonds dashed.")
        case .tertiary:
            let show = s.show
            if s.denatured || s.renaturing {
                let denatured = s.denatured
                let fresh = s.denatureIsFresh && !reduceMotion
                let meshes = BiomolProteins.denatureMeshes(dark: dark)
                BiomolSceneCard(title: denatured ? "Ubiquitin, denatured" : "Ubiquitin, folding back", subtitle: "The same 76 amino acids, the same peptide bonds",
                                badge: "Random coil", key: "tertiary/den/\(denatured)/\(dark)", frameKey: "tertiary",
                                build: BioSceneBuild { BioSceneContent(items: [], labels: [], frame: meshes.frame) },
                                extras: { BiomolNodes.morph(meshes.parts, from: denatured ? 0 : 1, to: denatured ? 1 : 0, animate: fresh) },
                                accessibility: "3D model of ubiquitin unfolded into a random coil: its helix and strands are gone.")
            } else {
                BiomolSceneCard(title: "Tertiary structure", subtitle: "Ubiquitin: one chain of 76 amino acids folded", badge: "PDB 1UBQ",
                                key: "tertiary/\(show.rawValue)/\(labels)/\(dark)", frameKey: "tertiary",
                                build: BioSceneBuild {
                                    let c = BiomolProteins.tertiary(dark: dark, show: show, labels: labels)
                                    let all = BiomolProteins.denatureMeshes(dark: dark).frame
                                    return BioSceneContent(items: c.items, labels: c.labels, frame: all)
                                },
                                accessibility: "3D model of ubiquitin: a ribbon with one α-helix and a five-stranded β-sheet, side chains as spheres coloured by class\(show == .all ? "" : ", \(show.label.lowercased()) shown").")
            }
        case .quaternary:
            BiomolSceneCard(title: "Quaternary structure", subtitle: "Haemoglobin: four chains, four hemes", badge: "PDB 2HHB",
                            key: "quaternary/\(labels)/\(dark)", frameKey: "quaternary",
                            build: BioSceneBuild { BiomolProteins.quaternary(dark: dark, labels: labels) },
                            accessibility: "3D model of haemoglobin: two α chains and two β chains as ribbons, each holding a heme disk with an iron atom.")
        }
    }

    @ViewBuilder
    private var levelFacts: some View {
        VStack(alignment: .leading, spacing: 14) {
            switch s.level {
            case .primary: primaryFacts
            case .secondary: secondaryFacts
            case .tertiary: tertiaryFacts
            case .quaternary: quaternaryFacts
            }
        }
    }

    private var primaryFacts: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "Primary structure") {
                Text("The order of the amino acids in the chain, set by the gene (DNA → mRNA → protein). Peptide bonds join each amino acid's C=O carbon to the next one's N.")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                BioFactRow(label: "Why it matters", text: "Every other level follows from it: change one amino acid and the folding can change (sickle-cell haemoglobin).")
            }
            BiomolColourLegend(items: BiomolColourLegend.classes())
        }
    }

    private var secondaryFacts: some View {
        let ala = BiomolPeptideBuilder.build(String(repeating: "A", count: 20), ss: Array(repeating: .helix, count: 20))
        let hp = BiomolPeptideBuilder.helixParameters(ala.caTrace)
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "α-helix", swatch: BioPalette.helix.color) {
                BiomolFacts(rows: [("Residues a turn", String(format: "%.1f", hp.perTurn)), ("Rise a residue", String(format: "%.2f Å", hp.rise)),
                                   ("A turn", String(format: "%.1f Å", hp.perTurn * hp.rise)), ("Hand", hp.rise > 0 ? "Right-handed" : "Left-handed")])
                BioFactRow(label: "Held by", text: "H-bonds from each backbone N–H to the C=O four residues back — backbone atoms, not side chains, which point outward.")
            }
            BioCard(title: "β-pleated sheet", swatch: BioPalette.strand.color) {
                Text("Extended strands side by side, H-bonded backbone to backbone. Side chains point above and below the sheet.")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
            }
            BiomolColourLegend(items: BiomolColourLegend.elements(["C", "H", "N", "O"]) + [(BioPalette.hBond.color, "H-bond (dashed)")])
        }
    }

    private var tertiaryFacts: some View {
        let u = BioData.ubiquitin
        let counts = BioAAClass.allCases.map { c in (c, u.filter { BiomolProteins.aaClass($0.aa) == c }.count) }
        return VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection, id.hasPrefix("res:"), let i = Int(id.dropFirst(4)), let r = u.first(where: { $0.i == i }),
               let aa = BioAminoAcids.byOne[r.aa] {
                BioCard(title: "\(aa.name) \(i)", swatch: aa.group.color, subtitle: "\(aa.group.label) · \(r.ss == "H" ? "in the α-helix" : (r.ss == "E" ? "in the β-sheet" : "in a loop"))") {
                    Text(aa.group == .nonpolar ? "Nonpolar side chains gather inside, away from water: the hydrophobic core." : "Polar and charged side chains face the water outside.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: "Tertiary structure", subtitle: "Ubiquitin, 76 amino acids (PDB 1UBQ)") {
                Text("The whole chain's 3D shape, held by interactions between R groups:").font(.sCallout).fixedSize(horizontal: false, vertical: true)
                BioFactRow(label: "Hydrophobic interactions", text: "Nonpolar side chains cluster in the core, away from water.")
                BioFactRow(label: "Hydrogen bonds", text: "Between polar side chains.")
                BioFactRow(label: "Ionic bonds", text: "Between + and − side chains (Lys, Arg with Asp, Glu).")
                BioFactRow(label: "Disulfide bridges", text: "Covalent S–S bonds between two Cys. Ubiquitin has no Cys, so it has none.")
            }
            if s.denatured || s.renaturing {
                BioCard(title: "Denatured", swatch: .orange) {
                    Text("Heat or extreme pH breaks the weak interactions and the chain unfolds into a random coil. The primary structure — the peptide bonds — is unchanged: only the shape is lost, and with it the function.")
                        .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            VStack(alignment: .leading, spacing: 6) {
                BioHeading("Its side chains")
                ForEach(counts.indices, id: \.self) { i in
                    let (c, k) = counts[i]
                    HStack(spacing: 8) {
                        Circle().fill(c.color).frame(width: 10, height: 10)
                        Text(c.label).font(.sCallout)
                        Spacer()
                        Text("\(k)").font(.sCallout.weight(.semibold).monospacedDigit())
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            .padding(14)
            .card(radius: 14)
            BiomolColourLegend(items: [(BioPalette.helix.color, "α-helix"), (BioPalette.strand.color, "β-strand"), (BioPalette.coil.color, "Loop")])
        }
    }

    private var quaternaryFacts: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let id = s.selection {
                if id.hasPrefix("chain:"), let c = id.dropFirst(6).first {
                    let n = BiomolProteins.chain(c).count
                    BioCard(title: "\(BiomolProteins.chainName[c] ?? "") chain", swatch: Color(hex: BiomolProteins.chainHex[c] ?? "#8e8e93"),
                            subtitle: "\(c == "A" || c == "C" ? "α-globin" : "β-globin"), \(n) amino acids") {
                        Text("One subunit: its own tertiary structure, mostly α-helix, holding one heme.").font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    }
                } else if id.hasPrefix("heme") {
                    BioCard(title: "Heme", swatch: Color(hex: BiomolProteins.hemeHex)) {
                        Text("A flat ring of four pyrrole N around an iron (Fe²⁺). Each heme binds one O₂, so one haemoglobin carries four.")
                            .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    }
                } else if id.hasPrefix("glu6") {
                    BioCard(title: "β6 Glu", swatch: BioPalette.acidic.color) {
                        Text("Glutamate 6 of each β chain, on the surface. In sickle-cell haemoglobin it is valine: one base changed in the HBB gene.")
                            .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            BioCard(title: "Quaternary structure", subtitle: "Haemoglobin (deoxy, PDB 2HHB)") {
                Text("More than one chain fitted together. Haemoglobin has four subunits: two α (141 amino acids) and two β (146), each holding a heme.")
                    .font(.sCallout).fixedSize(horizontal: false, vertical: true)
                BioFactRow(label: "Four hemes", text: "Each binds one O₂; oxygen binding at one heme makes the others bind more easily.")
                BioFactRow(label: "Sickle cell", text: "β6 Glu → Val (Glu6Val in the mature chain; codon 7 counting the first Met, p.Glu7Val). Valine is nonpolar, so deoxy haemoglobin S sticks together in fibres.")
                BioFactRow(label: "The gene", text: "The β chains' sequence here is the HBB gene's protein without its first Met.")
            }
            BiomolColourLegend(items: quaternaryLegend)
        }
    }
}

/// The primary structure as beads (§4.4 item 6): each amino acid a bead in its class's colour with its letter, joined
/// by peptide bonds, in rows that snake back and forth, the N-terminus and C-terminus named.
struct BiomolBeads: View {
    let sequence: [Character]

    var body: some View {
        GeometryReader { g in
            let bead: CGFloat = 34, gap: CGFloat = 16, rowGap: CGFloat = 30
            let perRow = max(1, Int((g.size.width - 120) / (bead + gap)))
            let n = sequence.count
            let rows = max(1, (n + perRow - 1) / perRow)
            let width = CGFloat(perRow) * (bead + gap) - gap
            let left = (g.size.width - width) / 2
            let top: CGFloat = 36
            let points: [CGPoint] = (0..<n).map { i in
                let r = i / perRow, k = i % perRow
                let col = r % 2 == 0 ? k : perRow - 1 - k
                return CGPoint(x: left + CGFloat(col) * (bead + gap) + bead / 2, y: top + CGFloat(r) * (bead + rowGap) + bead / 2)
            }
            ScrollView {
                ZStack(alignment: .topLeading) {
                    Canvas { ctx, _ in
                        for i in 0..<max(n - 1, 0) {
                            var path = Path()
                            path.move(to: points[i])
                            path.addLine(to: points[i + 1])
                            ctx.stroke(path, with: .color(BiomolParts.bondHighlight.color), lineWidth: 3)
                        }
                    }
                    .frame(width: g.size.width, height: top + CGFloat(rows) * (bead + rowGap) + 20)
                    ForEach(0..<n, id: \.self) { i in
                        let aa = sequence[i]
                        let cls = BioAminoAcids.byOne[aa]?.group ?? .nonpolar
                        VStack(spacing: 2) {
                            Text(String(aa))
                                .font(.system(size: 15, weight: .bold, design: .monospaced))
                                .foregroundStyle(.white)
                                .frame(width: bead, height: bead)
                                .background(Circle().fill(cls.color))
                            if (i + 1) % 5 == 0 || i == 0 {
                                Text("\(i + 1)").font(.system(size: 10).monospacedDigit()).foregroundStyle(.secondary)
                            }
                        }
                        .position(x: points[i].x, y: points[i].y + ((i + 1) % 5 == 0 || i == 0 ? 7 : 0))
                        .help("\(BioAminoAcids.byOne[aa]?.name ?? String(aa)) \(i + 1)")
                        .accessibilityLabel("\(BioAminoAcids.byOne[aa]?.name ?? String(aa)) \(i + 1)")
                    }
                    if n > 0 {
                        Text("N-terminus").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                            .position(x: points[0].x, y: points[0].y - bead / 2 - 12)
                        Text("C-terminus").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                            .position(x: points[n - 1].x, y: points[n - 1].y + bead / 2 + 22)
                    }
                    if n > 1 {
                        Text("peptide bond").font(.system(size: 10)).foregroundStyle(BiomolParts.bondHighlight.color)
                            .position(x: (points[0].x + points[1].x) / 2, y: points[0].y - bead / 2 - 2)
                    }
                }
                .frame(width: g.size.width, height: top + CGFloat(rows) * (bead + rowGap) + 30, alignment: .topLeading)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Primary structure: \(sequence.count) amino acids")
    }
}
