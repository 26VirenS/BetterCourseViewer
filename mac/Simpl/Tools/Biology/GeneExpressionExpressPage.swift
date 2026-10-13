import SwiftUI

// Gene Expression's Transcribe & translate page (spec §4.3): a gene made into a protein step by step — the gene, a
// nucleotide a press through transcription, the RNA processed (eukaryote), a codon a press through translation, and the
// protein — in the four-lane scene (GeneExpressionScene.swift) with the shared step-through controls under it. Left:
// the gene (the made-up teaching gene, β-globin's coding sequence, or one's own), the cell type, the stage groups and
// the colours. Right: what is happening now, the mRNA's parts, and the rules the model keeps. Run plays an elongation
// through quickly; Skip jumps to its end.

struct GXExpressPage: View {
    @ObservedObject var store: GXStore
    @ObservedObject var steps: BioStepController
    @State private var pan: CGFloat = 0
    @State private var panStart: CGFloat?
    @State private var hover: CGPoint?
    @State private var runner: Task<Void, Never>?
    @State private var speedBeforeRun: Double?
    @FocusState private var sceneFocused: Bool
    @FocusState private var ownFocused: Bool

    private var process: GXProcess? { store.process }
    private var frame: GXFrame? {
        guard let p = process, p.frames.indices.contains(steps.index) else { return nil }
        return p.frames[steps.index]
    }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            sceneCard
        } right: {
            facts
        }
        .onChange(of: steps.index) { _, _ in pan = 0 }
        .onAppear {
            // (the scene takes the keys at once: ← → step, Space plays — unless a sequence of one's own is to be typed)
            if store.source != .own { DispatchQueue.main.async { sceneFocused = true } }
        }
        .onDisappear { stopRun() }
    }

    // MARK: Left: the gene, the cell, the stages, the colours

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Gene")
            PresetMenu(sections: [("Genes", GXSource.allCases.map(\.label))], selected: store.source.label) { name in
                guard let s = GXSource.allCases.first(where: { $0.label == name }) else { return }
                stopRun()
                withAnimation(Motion.snappy) { store.source = s }
            }
            Text(process?.gene.note ?? "Type a coding-strand DNA or an mRNA sequence below.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            if store.source == .own { ownEditor }
            BioHeading("Cell type")
            Picker("", selection: Binding(get: { store.cell }, set: { c in stopRun(); store.cell = c })) {
                ForEach(GXCell.allCases) { c in Text(c.label).tag(c) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("A eukaryote processes its RNA in the nucleus; a prokaryote translates as it transcribes")
            Text(store.cell == .eukaryote
                 ? "A nucleus: the RNA is capped, tailed and spliced, then exported; the ribosome scans from the cap to the first AUG."
                 : "No nucleus and no introns: ribosomes bind the Shine–Dalgarno site, and can start before transcription ends.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            BioHeading("Go to")
            VStack(spacing: 6) {
                ForEach(GXProcess.groups.keys.sorted(), id: \.self) { g in groupButton(g) }
            }
            BioHeading("Colours")
            HStack(spacing: 10) {
                ForEach(["A", "T", "U", "G", "C"], id: \.self) { b in
                    Text(b)
                        .font(.system(size: 13, weight: .bold, design: .monospaced))
                        .foregroundColor(BioColor.base(Character(b)))
                        .frame(width: 24, height: 22)
                        .background(RoundedRectangle(cornerRadius: 5, style: .continuous).fill(BioColor.base(Character(b)).opacity(0.17)))
                        .help(["A": "Adenine", "T": "Thymine (DNA only)", "U": "Uracil (RNA only)", "G": "Guanine", "C": "Cytosine"][b] ?? b)
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Bases: A adenine, T thymine (DNA), U uracil (RNA), G guanine, C cytosine")
            BioLegend(items: BioAAClass.allCases.map { (color: $0.color, text: $0.label) })
        }
        .font(.sCallout)
    }

    private var ownEditor: some View {
        let input = store.ownInput
        return VStack(alignment: .leading, spacing: 8) {
            Picker("", selection: $store.ownKind) {
                Text("Coding-strand DNA").tag(BioSeqKind.dna)
                Text("mRNA").tag(BioSeqKind.rna)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            TextEditor(text: $store.ownText)
                .font(.system(size: 12.5, design: .monospaced))
                .scrollContentBackground(.hidden)
                .frame(height: 90)
                .padding(6)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.06)))
                .focused($ownFocused)
                .background(
                    // (⌘F: this field)
                    Button("") { ownFocused = true }
                        .keyboardShortcut("f", modifiers: .command)
                        .opacity(0)
                        .accessibilityHidden(true)
                )
                .accessibilityLabel("Your sequence, 5′ to 3′")
            if let m = input.message {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Label(m, systemImage: "exclamationmark.circle")
                        .font(.sCaption)
                        .foregroundStyle(.orange)
                        .fixedSize(horizontal: false, vertical: true)
                    if let s = input.suggestion {
                        Button("Switch") { store.ownKind = s }
                            .glassButton()
                            .help("Read it as \(s.label)")
                    }
                }
            }
            Text("\(input.letters.count) nt, 5′→3′ · up to \(GXProcess.cap) for the step-through")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
    }

    private func groupButton(_ g: Int) -> some View {
        let name = GXProcess.groups[g] ?? ""
        let available = BioStepTiming.start(ofGroup: g, in: steps.steps) != nil
        let current = steps.current?.group == g
        return Button {
            stopRun()
            steps.jump(toGroup: g)
            sceneFocused = true
        } label: {
            HStack(spacing: 10) {
                Text("\(g)")
                    .font(.system(size: 11, weight: .bold, design: .monospaced))
                    .frame(width: 20, height: 20)
                    .background(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color.secondary.opacity(0.5), lineWidth: 1))
                Text(name).font(.sCallout.weight(current ? .semibold : .regular))
                Spacer(minLength: 4)
                if current { Circle().fill(Theme.accent).frame(width: 7, height: 7) }
            }
            .padding(.horizontal, 10)
            .frame(height: 32)
            .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(current ? Theme.accent.opacity(0.14) : Color.primary.opacity(0.045)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(!available)
        .opacity(available ? 1 : 0.4)
        .help(available ? "\(name) (key \(g))" : "A prokaryote has no RNA processing: no nucleus, no introns")
        .accessibilityAddTraits(current ? .isSelected : [])
    }

    // MARK: Centre: the scene and the controls

    private var sceneCard: some View {
        VStack(spacing: 0) {
            topBar
                .padding(.horizontal, 16)
                .padding(.top, 14)
                .padding(.bottom, 4)
            if let p = process {
                TimelineView(.animation(paused: !steps.moving)) { tl in
                    GXSceneCanvas(process: p, phase: steps.phase(at: tl.date), pan: pan, notice: steps.current?.notice ?? [], hover: hover)
                }
                .frame(minHeight: 300, maxHeight: .infinity)
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 4)
                    .onChanged { v in
                        if panStart == nil { panStart = pan }
                        pan = (panStart ?? 0) + v.translation.width
                    }
                    .onEnded { _ in panStart = nil })
                .onTapGesture { sceneFocused = true }
                .onContinuousHover { phase in
                    switch phase {
                    case .active(let pt): hover = pt
                    case .ended: hover = nil
                    }
                }
                .bioStepKeys(steps)
                .focused($sceneFocused)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel("Gene expression: \(BioStepTiming.caption(steps.index, of: steps.steps))")
                .accessibilityValue(sceneDescription(p))
                .accessibilityHint("Left and right arrows step; Space plays; 1 to 5 jump to the stages")
                BioStepControls(controller: steps, groups: GXProcess.groups)
                    .padding(.horizontal, 16)
                    .padding(.top, 4)
                    .padding(.bottom, 14)
            } else {
                VStack(spacing: 10) {
                    Image(systemName: "text.cursor").font(.system(size: 28)).foregroundStyle(.secondary)
                    Text(store.ownInput.letters.isEmpty ? "Type a sequence on the left to transcribe and translate it." : "Fix the sequence on the left to transcribe and translate it.")
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .card(radius: 20)
    }

    private var topBar: some View {
        HStack(alignment: .center, spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 8) {
                    Text(process?.gene.title ?? "Your sequence").font(.system(size: 20, weight: .bold)).lineLimit(1)
                    if store.source == .teaching { GXBadge(text: "Made up for teaching") }
                    GXBadge(text: store.cell.label, colour: Theme.accent)
                }
                if let g = process?.gene {
                    Text(summary(g)).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            if let run = runRange {
                Button {
                    toggleRun(run.range)
                } label: {
                    Label(runner != nil ? "Stop" : (run.transcription ? "Run transcription" : "Run translation"),
                          systemImage: runner != nil ? "stop.fill" : "forward.fill")
                        .font(.sCallout.weight(.medium))
                }
                .glassButton(prominent: runner != nil)
                .help(run.transcription ? "Add the nucleotides one after another, quickly" : "Read the codons one after another, quickly")
                Button {
                    stopRun()
                    steps.pause()
                    steps.go(to: run.range.upperBound)
                } label: {
                    Label(run.transcription ? "Skip to end of transcription" : "Skip to end of translation", systemImage: "forward.end")
                        .labelStyle(SquishLabel(squished: true))
                }
                .glassButton()
                .help(run.transcription ? "Skip to the end of transcription" : "Skip to the end of translation")
            }
        }
    }

    private func summary(_ g: GXGene) -> String {
        var parts = ["\(g.coding.count) bp"]
        if !g.introns.isEmpty { parts.append("\(g.preMRNA.count)-nt pre-mRNA") }
        parts.append("\(g.mature.count)-nt mRNA")
        parts.append(g.protein.isEmpty ? "no protein" : "\(g.protein.count) amino acid\(g.protein.count == 1 ? "" : "s")")
        return parts.joined(separator: " · ")
    }

    /// The elongation the step is in or before (what Run plays and Skip jumps over).
    private var runRange: (range: ClosedRange<Int>, transcription: Bool)? {
        guard let p = process, let f = frame else { return nil }
        if f.stage <= .txElong, let r = p.steps(of: .txElong), steps.index < r.upperBound { return (r, true) }
        if f.stage >= .txEnd && f.stage <= .tlElong, let r = p.steps(of: .tlElong), steps.index < r.upperBound { return (r, false) }
        return nil
    }

    /// Run: one nucleotide (or codon) after another, each move straight after the last, faster than Play (at least 3×).
    private func toggleRun(_ r: ClosedRange<Int>) {
        if runner != nil {
            stopRun()
            return
        }
        steps.pause()
        speedBeforeRun = steps.speed
        steps.speed = max(steps.speed, 3)
        if steps.index < r.lowerBound - 1 { steps.go(to: r.lowerBound - 1, animated: false) }
        sceneFocused = true
        runner = Task { @MainActor in
            var expected = steps.index
            while !Task.isCancelled {
                guard steps.index == expected, steps.index < r.upperBound else { break }
                if !steps.isMoving() {
                    steps.next()
                    expected = steps.index
                }
                try? await Task.sleep(nanoseconds: 25_000_000)
            }
            finishRun()
        }
    }

    private func stopRun() {
        runner?.cancel()
        finishRun()
    }

    private func finishRun() {
        if let s = speedBeforeRun { steps.speed = s }
        speedBeforeRun = nil
        runner = nil
    }

    /// The scene in words (VoiceOver), for the step shown.
    private func sceneDescription(_ p: GXProcess) -> String {
        guard let f = frame else { return "" }
        let g = p.gene
        switch f.stage {
        case .gene: return "\(g.title): \(summary(g))."
        case .txInit, .txElong, .txEnd:
            let rna = p.rna(at: steps.index)
            return rna.isEmpty ? "The DNA opened at the start site." : "RNA made so far, 5′ to 3′: \(rna.suffix(30).map(String.init).joined(separator: " "))."
        case .cap, .tail, .splice, .export: return "The mRNA: \(g.mature.count) nucleotides\(f.cap > 0.5 ? ", capped" : "")\(f.tail > 0.5 ? ", with a poly-A tail" : "")."
        case .tlInit, .tlElong, .tlEnd, .noStart: return "Polypeptide so far, N to C: \(p.chain(at: steps.index).map(String.init).joined(separator: " "))."
        case .result: return "The protein: \(g.protein.map(String.init).joined(separator: " "))."
        }
    }

    // MARK: Right: what is happening, the mRNA, the rules

    @ViewBuilder
    private var facts: some View {
        if let p = process, let f = frame {
            VStack(alignment: .leading, spacing: 14) {
                nowCard(p, f)
                if f.made >= 1 { mrnaCard(p.gene, f) }
                rules
            }
        } else {
            rules
        }
    }

    @ViewBuilder
    private func nowCard(_ p: GXProcess, _ f: GXFrame) -> some View {
        let g = p.gene
        switch f.stage {
        case .gene:
            BioCard(title: g.title, subtitle: g.source == .teaching ? "Made up for this tool" : nil) {
                VStack(spacing: 0) {
                    BioValueRow(label: "Coding strand", value: "\(g.coding.count) bp")
                    BioValueRow(label: "Transcribed (from +1)", value: "\(g.preMRNA.count) nt")
                    BioValueRow(label: "Introns", value: "\(g.introns.count)")
                    BioValueRow(label: "Mature mRNA", value: "\(g.mature.count) nt")
                    BioValueRow(label: "Protein", value: g.protein.isEmpty ? "none" : "\(g.protein.count) aa", last: true)
                }
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.04)))
            }
        case .txInit, .txElong, .txEnd:
            let k = Int(f.made)
            BioCard(title: "Transcription", swatch: GXColors.polymerase.color, subtitle: g.cell == .eukaryote ? "RNA polymerase II, in the nucleus" : "RNA polymerase, in the cytoplasm") {
                if f.stage == .txElong, k >= 1 {
                    let col = g.txStart + k - 1
                    HStack(spacing: 14) {
                        pairBox("Template", String(g.template[col]), "3′→5′")
                        Image(systemName: "arrow.right").foregroundStyle(.secondary)
                        pairBox("Added to RNA", String(g.preMRNA[k - 1]), "at its 3′ end")
                    }
                    BioFactRow(label: "Pairing", text: "Template A → U, T → A, G → C, C → G. The RNA matches the coding strand, with U for T.")
                }
                let rna = p.rna(at: steps.index)
                BioFactRow(label: "RNA so far (\(rna.count) nt)", text: rna.isEmpty ? "None yet." : "5′-" + (rna.count > 24 ? "…" : "") + String(rna.suffix(24)) + "-3′")
            }
        case .cap, .tail, .splice, .export:
            BioCard(title: "RNA processing", swatch: BioPalette.rna.color, subtitle: "In the nucleus, before the mRNA leaves") {
                BioFactRow(label: "5′ cap", text: f.cap > 0.5 ? "Added: m⁷G, joined 5′ to 5′." : "Not yet.")
                BioFactRow(label: "Poly-A tail", text: f.tail > 0.5 ? "Added: about 200 A (no template)." : "Not yet.")
                BioFactRow(label: "Splicing", text: f.splice > 0.5 ? (g.introns.isEmpty ? "Nothing to cut in the sequence shown." : "The intron is out; the exons are joined.") : "Not yet.")
                BioFactRow(label: "Export", text: f.export > 0.5 ? "Through a nuclear pore, to the cytoplasm." : "Not yet.")
            }
        case .tlInit, .tlElong:
            let j = Int(f.pSite.rounded())
            BioCard(title: "Translation", swatch: GXColors.ribosome.color, subtitle: "The ribosome reads the mRNA 5′→3′, a codon at a time") {
                if g.codons.indices.contains(j) {
                    codonFacts("In the P site", g.codons[j])
                }
                if g.codons.indices.contains(j + 1) {
                    let next = g.codons[j + 1]
                    BioFactRow(label: "Next, in the A site", text: next.aa == "*" ? "\(next.codon): a stop codon (read by a release factor)" : "\(next.codon) → \(BioAminoAcids.byOne[next.aa]?.three ?? "?")")
                }
                chainRow(p.chain(at: steps.index))
            }
        case .tlEnd, .noStart:
            BioCard(title: f.stage == .noStart ? "No start codon" : (g.hasStop ? "Termination" : "No stop codon"), swatch: GXColors.stop.color) {
                if g.hasStop, let stop = g.codons.last {
                    BioFactRow(label: "Stop codon", text: "\(stop.codon) (“\(BioCode.stopNames[stop.codon] ?? "stop")”): no tRNA reads it; a release factor does.")
                }
                chainRow(p.chain(at: steps.index))
            }
        case .result:
            resultCard(g)
        }
    }

    private func pairBox(_ label: String, _ base: String, _ sub: String) -> some View {
        VStack(spacing: 3) {
            Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            Text(base)
                .font(.system(size: 24, weight: .bold, design: .monospaced))
                .foregroundColor(BioColor.base(Character(base)))
                .frame(width: 44, height: 40)
                .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(BioColor.base(Character(base)).opacity(0.16)))
            Text(sub).font(.sCaption2).foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
    }

    @ViewBuilder
    private func codonFacts(_ label: String, _ c: GXCodon) -> some View {
        let aa = BioAminoAcids.byOne[c.aa]
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(label).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                gxBases(c.codon, size: 18, weight: .bold)
                Text("codon \(c.index + 1), 5′→3′").font(.sCaption2).foregroundStyle(.secondary)
            }
            VStack(alignment: .leading, spacing: 3) {
                Text("Anticodon").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                (Text("3′-").font(.sCaption) + gxBases(BioCode.anticodon3to5(c.codon), size: 18, weight: .bold) + Text("-5′").font(.sCaption))
                Text("5′-\(BioCode.anticodon5to3(c.codon))-3′ in tRNA tables").font(.sCaption2).foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
        if let aa {
            HStack(spacing: 8) {
                Circle().fill(aa.group.color).frame(width: 10, height: 10)
                Text("\(aa.name) (\(aa.three), \(aa.one)) · \(aa.group.label)").font(.sCallout)
            }
            .accessibilityElement(children: .combine)
        }
    }

    private func chainRow(_ chain: String) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text("Polypeptide so far (\(chain.count) aa, N→C)").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            chain.reduce(Text("N-").font(.sCaption).foregroundColor(.secondary)) { t, a in
                t + Text(String(a)).font(.system(size: 14, weight: .bold, design: .monospaced)).foregroundColor(BioColor.aminoAcid(a))
            }
            .fixedSize(horizontal: false, vertical: true)
            .textSelection(.enabled)
        }
        .accessibilityElement(children: .combine)
    }

    private func resultCard(_ g: GXGene) -> some View {
        BioCard(title: "The protein", swatch: Theme.accent, subtitle: g.protein.isEmpty ? "None" : "N- to C-terminus") {
            if !g.protein.isEmpty {
                Text(g.protein)
                    .font(.system(size: 14, weight: .semibold, design: .monospaced))
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
                VStack(spacing: 0) {
                    BioValueRow(label: "Length", value: "\(g.protein.count) amino acids")
                    BioValueRow(label: "Mass", value: String(format: "%.0f g/mol", BioAminoAcids.peptideMass(g.protein)))
                    BioValueRow(label: "Net charge at pH 7", value: String(format: "%+d", BioAminoAcids.netCharge(g.protein)), last: true)
                }
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.04)))
                Button {
                    BioTools.open(.biomol, info: ["protein": g.protein, "page": "proteins"])
                } label: {
                    Label("Open in Biomolecule Builder", systemImage: "cube").frame(maxWidth: .infinity)
                }
                .glassButton(prominent: true)
                .controlSize(.large)
                .help("Build this chain in 3D (folded as an α-helix to start)")
            } else {
                Text("Without an AUG there is no reading frame, so no protein.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
    }

    /// The mRNA as a labelled strip (§4.2): 5′ cap, 5′ UTR, coding sequence, 3′ UTR and poly-A tail — the intron too while
    /// it is still in.
    private func mrnaCard(_ g: GXGene, _ f: GXFrame) -> some View {
        let spliced = g.introns.isEmpty || f.splice > 0.5
        var parts: [(name: String, length: Int, colour: Color)] = []
        if g.cell == .eukaryote && f.cap > 0.5 { parts.append(("5′ cap", 0, BioPalette.guanine.color)) }
        for r in g.regions.sorted(by: { $0.range.lowerBound < $1.range.lowerBound }) {
            let lo = max(r.range.lowerBound, g.txStart), hi = min(r.range.upperBound, g.txEnd)
            guard lo <= hi, !(spliced && r.kind == .intron) else { continue }
            parts.append((r.name, hi - lo + 1, GXColors.regionSolid(r.kind)))
        }
        if g.cell == .eukaryote && f.tail > 0.5 { parts.append(("Poly-A tail", 0, BioPalette.adenine.color.opacity(0.7))) }
        let total = max(parts.reduce(0) { $0 + max($1.length, 8) }, 1)
        return BioCard(title: spliced ? "The mRNA" : "The pre-mRNA", subtitle: "\(spliced ? g.mature.count : g.preMRNA.count) nt\(g.cell == .eukaryote && f.cap > 0.5 ? ", plus cap" : "")\(g.cell == .eukaryote && f.tail > 0.5 ? " and tail" : "")") {
            GeometryReader { geo in
                HStack(spacing: 2) {
                    ForEach(parts.indices, id: \.self) { i in
                        RoundedRectangle(cornerRadius: 4, style: .continuous)
                            .fill(parts[i].colour)
                            .frame(width: max(6, (geo.size.width - CGFloat(parts.count * 2)) * CGFloat(max(parts[i].length, 8)) / CGFloat(total)))
                    }
                }
            }
            .frame(height: 16)
            .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 4) {
                ForEach(parts.indices, id: \.self) { i in
                    HStack(spacing: 8) {
                        RoundedRectangle(cornerRadius: 3, style: .continuous).fill(parts[i].colour).frame(width: 12, height: 10)
                        Text(parts[i].name).font(.sCaption.weight(.medium))
                        Spacer(minLength: 4)
                        Text(parts[i].length > 0 ? "\(parts[i].length) nt" : (parts[i].name.hasPrefix("Poly") ? "≈200 A" : "m⁷G"))
                            .font(.sCaption.monospacedDigit())
                            .foregroundStyle(.secondary)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
        }
    }

    private var rules: some View {
        BioCard(title: "The rules it follows") {
            ForEach(GXExpressPage.ruleLines, id: \.self) { line in
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text("•").foregroundStyle(.secondary)
                    Text(line).font(.sCaption).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    static let ruleLines = [
        "Transcription copies the template strand, so the mRNA equals the coding strand with U for T.",
        "The ribosome reads codons 5′→3′ from the first AUG: never the DNA, never 3′→5′.",
        "Anticodons pair antiparallel: codon 5′-AUG-3′ with anticodon 3′-UAC-5′ (5′-CAU-3′ in tRNA tables).",
        "Stop codons code for no amino acid; release factors, not tRNAs, read them.",
        "AUG both starts translation and codes Met inside a protein.",
        "The codon chooses the amino acid through its tRNA, which brings it; the ribosome does not make amino acids.",
    ]
}
