import SceneKit
import SwiftUI

// DNA & RNA's Double helix page (spec §4.2 Double helix): the controls on the left (the sequence, the style, what to
// show, the unzip), the helix in 3D in the middle (DNAHelix makes it, off the main thread; the shared scene view turns,
// picks and labels it), and on the right the selected pair's, backbone's or groove's card, the base pairs to pick from
// (the parts list, for the keyboard and VoiceOver), and the helix's numbers.

struct DNAHelixPage: View {
    @ObservedObject var store: DNAStore
    @ObservedObject private var nav = BioNav.shared
    @Environment(\.colorScheme) private var scheme
    @State private var turning = false
    @State private var reset = 0
    @State private var zoom: BioZoomRequest?
    @State private var unzipping: Task<Void, Never>?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let seq = store.helixSequence
        PTWorkLayout {
            controls(seq)
        } centre: {
            centre(seq)
        } right: {
            side(seq)
        }
    }

    /// The scene's options, the unzip held to half-pair steps (each step a new scene).
    private func options(_ seq: String) -> DNAHelixOptions {
        let n = max(seq.count, 1)
        let unzip = (store.unzip * Double(2 * n)).rounded() / Double(2 * n)
        return DNAHelixOptions(sequence: seq, style: store.helixStyle, ends: store.showEnds, grooves: store.showGrooves,
                               measure: store.showMeasure, hBonds: store.showHBonds, unzip: unzip, dark: scheme == .dark)
    }

    // MARK: Left: the controls

    private func controls(_ seq: String) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            sequenceSection(seq)
            styleSection
            showSection
            unzipSection
            coloursSection
        }
        .font(.sCallout)
    }

    private func sequenceSection(_ seq: String) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Sequence")
            if seq.isEmpty {
                Text("No DNA to draw yet.").font(.sCallout).foregroundStyle(.secondary)
            } else {
                (Text("5′ ").foregroundColor(.secondary) + Text(seq.count > 24 ? String(seq.prefix(22)) + "…" : seq) + Text(" 3′").foregroundColor(.secondary))
                    .font(.system(size: 13, weight: .semibold, design: .monospaced))
                    .textSelection(.enabled)
                Text(toolPlural(seq.count, "base pair") + (store.input.letters.count > DNAModel.helixMax ? " (the first 40 drawn)" : ""))
                    .font(.sCaption).foregroundStyle(.secondary)
            }
            if store.kind == .rna {
                Text("The Sequence page holds RNA: the helix shows the DNA it is copied from (T for U).")
                    .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            Button {
                withAnimation(Motion.gentle) { nav.set(.dna, "sequence") }
            } label: {
                Label("Edit on the Sequence page", systemImage: "pencil").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("Type or paste another strand")
        }
    }

    private var styleSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Style")
            Picker("Style", selection: $store.helixStyle) {
                ForEach(DNAHelixStyle.allCases, id: \.self) { Text($0.label).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("Cartoon: backbones as tubes, bases as boxes. Atoms: ball and stick. Space filling: each atom its size.")
        }
    }

    private var showSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Show")
            Toggle("5′ and 3′ ends", isOn: $store.showEnds).help("Each strand's ends, and arrows pointing 5′→3′ along it")
            Toggle("Grooves", isOn: $store.showGrooves).help("The major and minor grooves between the backbones")
            Toggle("Measurements", isOn: $store.showMeasure).help("The helix's width, rise and turn")
            Toggle("Hydrogen bonds", isOn: $store.showHBonds).help("The H-bonds holding each base pair: 2 for A·T, 3 for G·C")
            Toggle("Labels", isOn: $store.showLabels).help("Names beside the model, with leader lines")
            if store.showGrooves && store.unzip > 0 {
                Text("The grooves are shown on the closed helix.").font(.sCaption).foregroundStyle(.secondary)
            }
        }
    }

    private var unzipSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Unzip")
            Slider(value: $store.unzip, in: 0...1,
                   label: { Text("Unzip") },
                   minimumValueLabel: { Text("Closed").font(.sCaption) },
                   maximumValueLabel: { Text("Open").font(.sCaption) },
                   onEditingChanged: { editing in if editing { unzipping?.cancel() } })
            .help("Open the base pairs one by one from strand 1's 5′ end")
            .accessibilityValue("\(Int((store.unzip * 100).rounded())) percent open")
            HStack(spacing: 8) {
                Button {
                    animateUnzip(to: store.unzip >= 0.999 ? 0 : 1)
                } label: {
                    Label(store.unzip >= 0.999 ? "Zip it up" : "Unzip", systemImage: store.unzip >= 0.999 ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right")
                        .frame(maxWidth: .infinity)
                }
                .glassButton()
                .help(store.unzip >= 0.999 ? "Close the helix again, pair by pair" : "Open the helix pair by pair")
            }
            Text("The H-bonds break and the strands part, as when helicase opens DNA to copy it.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Button {
                withAnimation(Motion.gentle) { nav.set(.dna, "replication") }
            } label: {
                Label("See replication, step by step", systemImage: "arrow.triangle.branch").frame(maxWidth: .infinity)
            }
            .glassButton()
        }
    }

    /// The unzip moved to `target` a half pair at a time (each a new scene), or at once under Reduce Motion.
    private func animateUnzip(to target: Double) {
        unzipping?.cancel()
        guard !reduceMotion else { store.unzip = target; return }
        let start = store.unzip
        let steps = Int((abs(target - start) * Double(2 * max(store.helixSequence.count, 1))).rounded(.up))
        guard steps > 0 else { return }
        unzipping = Task { @MainActor in
            for i in 1...steps {
                try? await Task.sleep(nanoseconds: 45_000_000)
                if Task.isCancelled { return }
                store.unzip = start + (target - start) * Double(i) / Double(steps)
            }
        }
    }

    private var coloursSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            BioHeading("Colours")
            BioLegend(items: legend)
            Text("DNA in cells has about 10.5 base pairs per turn (3.6 nm). Watson and Crick's 1953 model, and many textbooks, give 10 bp and 3.4 nm per turn.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var legend: [(color: Color, text: String)] {
        var items: [(color: Color, text: String)] = [
            (BioColor.base("A"), "A adenine (purine)"), (BioColor.base("T"), "T thymine (pyrimidine)"),
            (BioColor.base("G"), "G guanine (purine)"), (BioColor.base("C"), "C cytosine (pyrimidine)"),
            (BioPalette.strand1.color, "Backbone, strand 1"), (BioPalette.strand2.color, "Backbone, strand 2"),
        ]
        if store.helixStyle != .cartoon { items = [(BioPalette.strand1.color, "Arrows: strand 1, 5′→3′"), (BioPalette.strand2.color, "Arrows: strand 2, 5′→3′")] + atomLegend }
        if store.showGrooves {
            items.append((Color(hex: scheme == .dark ? "#7d7aff" : "#5e5ce6"), "Major groove"))
            items.append((Color(hex: scheme == .dark ? "#ff7aa8" : "#e8508a"), "Minor groove"))
        }
        return items
    }

    private var atomLegend: [(color: Color, text: String)] {
        [("C", "Carbon"), ("N", "Nitrogen"), ("O", "Oxygen"), ("P", "Phosphorus")].map { (MBChem.swatch($0.0), $0.1) }
    }

    // MARK: Middle: the helix

    @ViewBuilder
    private func centre(_ seq: String) -> some View {
        let o = options(seq)
        let look = DNASceneText.atomLook
        let key = o.key
        BioSceneCard(hint: "Drag to turn · scroll to zoom · click a base or a backbone · arrow keys turn it") {
            VStack(alignment: .leading, spacing: 2) {
                Text(seq.isEmpty ? "B-DNA" : "B-DNA · \(seq.count) bp").font(.system(size: 22, weight: .bold))
                Text("Right-handed · 10.5 bp per turn · antiparallel strands").font(.sCallout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            BioSceneButtons(turning: $turning) { reset += 1 }
        } scene: {
            if seq.isEmpty {
                VStack(spacing: 10) {
                    Image(systemName: "link").font(.system(size: 34)).foregroundStyle(.secondary)
                    Text("Type a DNA sequence on the Sequence page to build its helix.").font(.sCallout).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                BioSceneView(key: key,
                             build: BioSceneBuild {
                                 let s = DNAHelix.build(o, look: look)
                                 DNAMarkShelf.shared.put(key, s.marks)
                                 return s.content
                             },
                             extras: { DNASceneText.nodes(DNAMarkShelf.shared.take(key)) },
                             selection: $store.helixSelection,
                             showLabels: store.showLabels,
                             turning: turning,
                             frameKey: seq,
                             reset: reset,
                             zoom: zoom,
                             accessibility: accessibility(seq),
                             onPick: { store.pickHelix($0) },
                             onDoubleClick: { id in zoomTo(id) })
            }
        }
    }

    private func zoomTo(_ id: String?) {
        guard let id, let part = DNAHelix.part(id) else { return }
        zoom = BioZoomRequest(id: DNAHelix.selection(part), serial: (zoom?.serial ?? 0) + 1)
    }

    private func accessibility(_ seq: String) -> String {
        var s = "3D model of a B-DNA double helix, \(toolPlural(seq.count, "base pair")), \(store.helixStyle.label.lowercased())"
        if store.unzip > 0 { s += ", \(Int((store.unzip * 100).rounded())) percent unzipped" }
        if let part = DNAHelix.part(store.helixSelection) { s += "; selected: \(describe(part, seq))" }
        return s
    }

    private func describe(_ part: DNAHelixPart, _ seq: String) -> String {
        switch part {
        case .pair(let k): return "base pair \(k + 1), \(DNAHelix.pairFacts(seq, pair: k)?.name ?? "")"
        case .backbone(let s, let k): return "strand \(s) backbone at base pair \(k + 1)"
        case .groove(let major): return major ? "the major groove" : "the minor groove"
        }
    }

    // MARK: Right: the card, the pairs, the numbers

    private func side(_ seq: String) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            if let part = DNAHelix.part(store.helixSelection), !seq.isEmpty {
                selectionCard(part, seq)
            }
            if !seq.isEmpty { pairPicker(seq) }
            numbers(seq)
        }
    }

    @ViewBuilder
    private func selectionCard(_ part: DNAHelixPart, _ seq: String) -> some View {
        switch part {
        case .pair(let k):
            if let f = DNAHelix.pairFacts(seq, pair: k) { pairCard(f, seq) }
        case .backbone(let s, let k):
            backboneCard(strand: s, pair: k, seq)
        case .groove(let major):
            grooveCard(major)
        }
    }

    private func pairCard(_ f: DNAPairFacts, _ seq: String) -> some View {
        let name1 = DNAModel.baseName(f.base1), name2 = DNAModel.baseName(f.base2)
        let ring1 = DNAModel.isPurine(f.base1) ? "a purine: two rings" : "a pyrimidine: one ring"
        let ring2 = DNAModel.isPurine(f.base2) ? "a purine: two rings" : "a pyrimidine: one ring"
        let bonds = f.hBonds.map { "\($0.text) (\(String(halfUp: "%.2f", $0.distance)) Å)" }.joined(separator: "\n")
        return BioCard(title: "\(f.name) base pair", swatch: BioColor.base(f.base1), subtitle: "Base pair \(f.pair + 1) of \(seq.count)") {
            BioFactRow(label: "Bases", text: "\(f.base1) \(f.number1) on strand 1 (\(name1), \(ring1)) pairs with \(f.base2) \(f.number2) on strand 2 (\(name2), \(ring2)).")
            BioFactRow(label: "Hydrogen bonds: \(f.hBonds.count)", text: bonds)
            BioFactRow(label: "Why a purine with a pyrimidine", text: "A two-ring base always pairs with a one-ring base, so every pair is the same width (C1′ to C1′ \(String(halfUp: "%.1f", f.c1c1)) Å) and the helix stays 2 nm wide.")
            if let i = store.helixAtom { atomRow(i, seq) }
            BioFactRow(label: "Common mix-up", text: "H-bonds are weak and break one by one when DNA is copied or read; the bonds inside each strand are covalent. G·C, with three, holds a little tighter than A·T with two.")
            cardButtons(store.helixSelection ?? "")
        }
    }

    private func backboneCard(strand s: Int, pair k: Int, _ seq: String) -> some View {
        let bases = Array(seq)
        let base = s == 1 ? bases[min(k, bases.count - 1)] : BioSeq.pair(bases[min(k, bases.count - 1)], rna: false)
        let number = s == 1 ? k + 1 : seq.count - k
        let pairHex = s == 1 ? BioPalette.strand1 : BioPalette.strand2
        return BioCard(title: "Sugar–phosphate backbone", swatch: pairHex.color, subtitle: "Strand \(s) · nucleotide \(base) \(number) from its 5′ end") {
            BioFactRow(label: "Structure", text: "Sugars (deoxyribose) and phosphates in turn. A phosphodiester bond joins the 3′ carbon of one sugar, through an oxygen, to the 5′ phosphate of the next.")
            BioFactRow(label: "Direction", text: "Strand 1 runs 5′→3′ up the helix and strand 2 runs 5′→3′ down: the strands are antiparallel.")
            BioFactRow(label: "Ends", text: "A strand's 5′ end has a phosphate on its sugar's 5′ carbon; its 3′ end, an OH on the 3′ carbon. New nucleotides are added only to the 3′ end.")
            BioFactRow(label: "Charge", text: "Each phosphate is negatively charged at pH 7, which is why DNA is an acid and moves toward + in a gel.")
            if let i = store.helixAtom { atomRow(i, seq) }
            cardButtons(store.helixSelection ?? "")
        }
    }

    private func grooveCard(_ major: Bool) -> some View {
        let w = DNAHelix.grooveWidths
        let colour = Color(hex: major ? (scheme == .dark ? "#7d7aff" : "#5e5ce6") : (scheme == .dark ? "#ff7aa8" : "#e8508a"))
        return BioCard(title: major ? "Major groove" : "Minor groove", swatch: colour,
                       subtitle: major ? "The wide side: about 212° round the axis" : "The narrow side: about 148° round the axis") {
            BioFactRow(label: "What it is", text: major
                       ? "Where the backbones are far apart. The base pairs' edges show here, and proteins that read DNA (like transcription factors) mostly fit into it."
                       : "Where the backbones are close together, on the side the sugars of each pair point to.")
            BioFactRow(label: "Width in this model", text: String(halfUp: "%.1f Å", major ? w.major : w.minor)
                       + " — phosphate to phosphate across the groove, less 5.8 Å for the phosphate groups (textbooks: \(major ? "11.7" : "5.7") Å). Some books quote the raw distance between phosphorus atoms instead.")
            cardButtons(store.helixSelection ?? "")
        }
    }

    /// The atom picked in an atom view: its name, element and nucleotide.
    @ViewBuilder
    private func atomRow(_ i: Int, _ seq: String) -> some View {
        let d = BioBDNA.build(seq)
        if d.atoms.indices.contains(i) {
            let a = d.atoms[i]
            let names = ["C": "carbon", "N": "nitrogen", "O": "oxygen", "P": "phosphorus"]
            let number = a.strand == 1 ? a.pair + 1 : seq.count - a.pair
            let part = DNAHelix.backboneNames.contains(a.name) ? (a.name == "P" || a.name.hasPrefix("OP") ? "phosphate" : "sugar") : "base"
            BioFactRow(label: "Atom picked", text: "\(a.name.replacingOccurrences(of: "'", with: "′")) (\(names[a.element] ?? a.element)) in the \(part) of strand \(a.strand)'s \(a.base) \(number)")
        }
    }

    private func cardButtons(_ id: String) -> some View {
        HStack(spacing: 8) {
            Button("Zoom to") { zoom = BioZoomRequest(id: id, serial: (zoom?.serial ?? 0) + 1) }
                .glassButton()
                .help("Bring the camera to it (or double-click it)")
            Button("Deselect") { store.clearHelixSelection() }
                .glassButton()
                .help("Let go of it (or press Escape in the model)")
        }
    }

    /// The base pairs as buttons: the parts list (strand 1's base over strand 2's).
    private func pairPicker(_ seq: String) -> some View {
        let bases = Array(seq)
        return VStack(alignment: .leading, spacing: 8) {
            BioHeading("Base pairs")
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(24), spacing: 4), count: 10), alignment: .leading, spacing: 6) {
                ForEach(0..<bases.count, id: \.self) { k in
                    let b1 = bases[k], b2 = BioSeq.pair(b1, rna: false)
                    let on = store.helixSelection == DNAHelix.selection(.pair(k))
                    Button { store.selectPair(k) } label: {
                        VStack(spacing: 1) {
                            Text(String(b1)).foregroundColor(BioColor.base(b1))
                            Text(String(b2)).foregroundColor(BioColor.base(b2))
                        }
                        .font(.system(size: 12, weight: .bold, design: .monospaced))
                        .frame(width: 24, height: 36)
                        .background(on ? Theme.accent.opacity(0.22) : Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 6, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(on ? Theme.accent : Color.clear, lineWidth: 1.2))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help("Base pair \(k + 1): \(b1)·\(b2)")
                    .accessibilityLabel("Base pair \(k + 1), \(DNAModel.baseName(b1)) with \(DNAModel.baseName(b2))")
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
            Text("Strand 1 (5′→3′) on top, its partner on strand 2 below.").font(.sCaption).foregroundStyle(.secondary)
        }
    }

    private func numbers(_ seq: String) -> some View {
        let n = seq.count
        let w = DNAHelix.grooveWidths
        return VStack(alignment: .leading, spacing: 8) {
            BioHeading("The double helix")
            VStack(spacing: 0) {
                BioValueRow(label: "Base pairs", value: "\(n)")
                BioValueRow(label: "Turns", value: String(halfUp: "%.2f", DNAHelix.turns(n)))
                BioValueRow(label: "Length", value: String(halfUp: "%.2f nm", Double(n) * 0.34))
                BioValueRow(label: "Width", value: "2 nm")
                BioValueRow(label: "Rise per base pair", value: "0.34 nm")
                BioValueRow(label: "Twist per base pair", value: String(halfUp: "%.1f°", BioBDNA.twist))
                BioValueRow(label: "One turn", value: String(halfUp: "10.5 bp · %.2f nm", DNAHelix.pitch / 10))
                BioValueRow(label: "Grooves", value: String(halfUp: "minor %.2f · major %.2f nm", w.minor / 10, w.major / 10))
                BioValueRow(label: "Handedness", value: "Right-handed", last: true)
            }
            .card(radius: 14)
            Text("Groove widths: phosphate to phosphate across the groove, less 5.8 Å for the phosphate groups.")
                .font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}
