import SwiftUI

// Gene Expression's Reading frames page (spec §4.3, §7 item 12): any DNA or mRNA in its three forward frames (+1, +2,
// +3, starting at the 1st, 2nd and 3rd nucleotide) and, for DNA, the three reverse frames read on the reverse
// complement (−1, −2, −3), each amino acid under the codon it comes from — stops as * on red, Met as M on green — and the
// ORF finder: every AUG… stop of at least L codons, longest first, its positions on the forward strand.

struct GXFramesPage: View {
    @ObservedObject var store: GXStore
    @State private var hover: String?
    @FocusState private var fieldFocused: Bool

    private var input: BioSequenceInput { store.framesInput }
    private var dna: Bool { store.framesKind == .dna }
    private var reverseShown: Bool { dna && store.showReverse }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            frames
        } right: {
            orfList
        }
    }

    // MARK: Left: the sequence and the finder's settings

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Sequence")
            PresetMenu(sections: [("Examples", GXReadingFrames.presets.map(\.name))], selected: preset?.name) { name in
                guard let p = GXReadingFrames.presets.first(where: { $0.name == name }) else { return }
                store.framesKind = p.dna ? .dna : .rna
                store.framesText = p.sequence
            }
            Picker("", selection: $store.framesKind) {
                Text("DNA").tag(BioSeqKind.dna)
                Text("mRNA").tag(BioSeqKind.rna)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("DNA shows six frames (three on each strand); mRNA, a single strand, three")
            TextEditor(text: $store.framesText)
                .font(.system(size: 12.5, design: .monospaced))
                .scrollContentBackground(.hidden)
                .frame(height: 110)
                .padding(6)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.06)))
                .focused($fieldFocused)
                .background(
                    // (⌘F: this field)
                    Button("") { fieldFocused = true }
                        .keyboardShortcut("f", modifiers: .command)
                        .opacity(0)
                        .accessibilityHidden(true)
                )
                .accessibilityLabel("Sequence, 5′ to 3′")
            if let m = input.message {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Label(m, systemImage: "exclamationmark.circle")
                        .font(.sCaption)
                        .foregroundStyle(.orange)
                        .fixedSize(horizontal: false, vertical: true)
                    if let s = input.suggestion {
                        Button("Switch") { store.framesKind = s }
                            .glassButton()
                            .help("Read it as \(s.label)")
                    }
                }
            }
            Text("\(input.letters.count) nt, written 5′→3′ · up to 3,000 · FASTA, spaces and numbers are fine")
                .font(.sCaption)
                .foregroundStyle(.secondary)
            BioHeading("Open reading frames")
            Stepper(value: $store.minCodons, in: 2...300) {
                Text("At least \(store.minCodons) codons").font(.sCallout)
            }
            .help("An ORF counts its codons from the AUG up to and including the stop")
            Toggle("Reverse frames (−1, −2, −3)", isOn: $store.showReverse)
                .disabled(!dna)
                .help(dna ? "Read the reverse complement too" : "An mRNA is single-stranded: three frames")
            BioLegend(items: [(color: GXColors.start.color, text: "M: Met, where an ORF can start"),
                              (color: GXColors.stop.color, text: "*: a stop codon"),
                              (color: Theme.accent, text: "Bar: an ORF (bold: the one picked)")])
        }
        .font(.sCallout)
    }

    private var preset: GXReadingFrames.Preset? {
        GXReadingFrames.presets.first { $0.sequence == input.letters && $0.dna == dna }
    }

    // MARK: Centre: the frames, line by line

    private var frames: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 2) {
                Text(reverseShown ? "Six reading frames" : "Three reading frames").font(.system(size: 20, weight: .bold))
                Text(hover ?? "Each amino acid sits under its codon. Point at one to see the codon it comes from.")
                    .font(.sCallout)
                    .foregroundStyle(hover == nil ? .secondary : .primary)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
            .padding(16)
            if let seq = input.sequence {
                GeometryReader { geo in
                    let cw: CGFloat = 14
                    let per = max(10, Int((geo.size.width - 32 - GXFrameBlock.gutter - 30) / cw) / 10 * 10)
                    let count = (seq.count + per - 1) / per
                    ScrollViewReader { proxy in
                        ScrollView {
                            LazyVStack(alignment: .leading, spacing: 16) {
                                ForEach(0..<count, id: \.self) { b in
                                    GXFrameBlock(sequence: Array(seq), start: b * per, count: min(per, seq.count - b * per), rows: store.frameRows,
                                                 orfs: store.orfs, picked: store.pickedORF, reverse: reverseShown, cw: cw) { hover = $0 }
                                        .id(b)
                                }
                            }
                            .padding(.horizontal, 16)
                            .padding(.bottom, 16)
                        }
                        .onChange(of: store.pickedORF) { _, k in
                            guard let k, store.orfs.indices.contains(k) else { return }
                            let o = store.orfs[k]
                            withAnimation(Motion.gentle) { proxy.scrollTo((min(o.start, o.end) - 1) / per, anchor: .top) }
                        }
                    }
                }
            } else {
                Text(input.letters.isEmpty ? "Type or paste a DNA or mRNA sequence on the left." : "Fix the sequence on the left to see its frames.")
                    .font(.sBody)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .card(radius: 20)
    }

    // MARK: Right: the ORFs, longest first

    private var orfList: some View {
        let orfs = store.orfs
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "Open reading frames", subtitle: "\(orfs.count) of at least \(store.minCodons) codons, longest first") {
                if orfs.isEmpty {
                    Text("None that long. Lower the length, or check that the sequence has an ATG (AUG).")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                ForEach(Array(orfs.prefix(40).enumerated()), id: \.offset) { i, o in orfRow(i, o) }
                if orfs.count > 40 {
                    Text("+\(orfs.count - 40) more").font(.sCaption).foregroundStyle(.secondary)
                }
            }
            BioCard(title: "Which frame is used?") {
                Text("Only one frame is actually translated: usually the longest ORF, starting at the first AUG in a good context. A shift of 1 or 2 nt changes every amino acid after it.")
                    .font(.sCallout)
                    .fixedSize(horizontal: false, vertical: true)
                Text("Positions are 1-based on the strand as typed. A reverse-frame ORF runs from its start (the higher number) down to its end.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func orfRow(_ i: Int, _ o: BioORF) -> some View {
        let on = store.pickedORF == i
        return Button {
            withAnimation(Motion.snappy) { store.pickedORF = on ? nil : i }
        } label: {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text("Frame \(o.frameLabel)").font(.sCallout.weight(.semibold))
                    Text("\(o.start)–\(o.end)").font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                    Spacer(minLength: 4)
                    if i == 0 { GXBadge(text: "Longest", colour: Theme.accent) }
                    Text("\(o.codons) codons").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                }
                Text(o.protein + (o.hasStop ? "" : "…"))
                    .font(.system(size: 12, design: .monospaced))
                    .lineLimit(2)
                    .truncationMode(.tail)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if !o.hasStop {
                    Text("open (no stop codon)").font(.sCaption).foregroundStyle(.orange)
                }
            }
            .padding(10)
            .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(on ? Theme.accent.opacity(0.16) : Color.primary.opacity(0.05)))
            .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(on ? Theme.accent : .clear, lineWidth: 1.5))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help("Show this ORF in the frames")
        .accessibilityLabel("Frame \(o.frameLabel), \(o.start) to \(o.end), \(o.codons) codons\(o.hasStop ? "" : ", open, no stop codon")\(i == 0 ? ", the longest" : "")")
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// A line of the frames: its positions, the strand 5′→3′, the three forward frames, and (DNA) the complement 3′→5′
/// with the three reverse frames — each amino acid centred under its codon, codons boxed in turn, ORFs barred.
struct GXFrameBlock: View {
    static let gutter: CGFloat = 40
    let sequence: [Character]
    let start: Int
    let count: Int
    let rows: [GXFrameRow]
    let orfs: [BioORF]
    let picked: Int?
    let reverse: Bool
    let cw: CGFloat
    let hovered: (String?) -> Void

    let row: CGFloat = 19
    private var shownRows: [GXFrameRow] { rows.filter { reverse || $0.frame > 0 } }
    private var height: CGFloat { 16 + row * CGFloat(1 + 3 + (reverse ? 4 : 0)) + 4 }

    var body: some View {
        Canvas { ctx, size in draw(&ctx, size) }
            .frame(height: height)
            .frame(maxWidth: .infinity, alignment: .leading)
            .onContinuousHover { phase in
                switch phase {
                case .active(let p): hovered(describe(p))
                case .ended: hovered(nil)
                }
            }
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("Positions \(start + 1) to \(start + count)")
            .accessibilityValue(shownRows.map { r in "frame \(r.label): " + String(r.cells.filter { $0.column + 1 >= start && $0.column + 1 < start + count }.map(\.aa)) }.joined(separator: "; "))
    }

    private func x(_ col: Int) -> CGFloat { GXFrameBlock.gutter + (CGFloat(col - start) + 0.5) * cw }

    /// The y of a line: 0 the strand, 1–3 frames +1…+3, 4 the complement, 5–7 frames −1…−3.
    private func y(_ line: Int) -> CGFloat { 16 + row * (CGFloat(line) + 0.5) }

    private func line(of r: GXFrameRow) -> Int { r.frame > 0 ? r.frame : 4 - r.frame }

    private func draw(_ ctx: inout GraphicsContext, _ size: CGSize) {
        let end = start + count
        let secondary = Color.secondary
        // (positions: the line's first, then every tenth)
        for col in start..<end where col == start || (col + 1) % 10 == 0 {
            ctx.draw(Text("\(col + 1)").font(.system(size: 9.5, design: .monospaced)).foregroundColor(secondary), at: CGPoint(x: x(col), y: 7))
        }
        // (the strand, and under the frames its complement)
        func strand(_ line: Int, _ letters: (Int) -> Character, _ left: String, _ right: String) {
            ctx.draw(Text(left).font(.system(size: 10.5, weight: .semibold)).foregroundColor(secondary), at: CGPoint(x: GXFrameBlock.gutter - 14, y: y(line)))
            for col in start..<end {
                let b = letters(col)
                ctx.draw(Text(String(b)).font(.system(size: 12.5, weight: .semibold, design: .monospaced)).foregroundColor(BioColor.base(b)), at: CGPoint(x: x(col), y: y(line)))
            }
            ctx.draw(Text(right).font(.system(size: 10.5, weight: .semibold)).foregroundColor(secondary), at: CGPoint(x: x(end - 1) + cw / 2 + 12, y: y(line)))
        }
        strand(0, { sequence[$0] }, "5′", "3′")
        if reverse { strand(4, { BioSeq.pair(sequence[$0], rna: false) }, "3′", "5′") }
        // (the frames)
        let pickedORF = picked.flatMap { orfs.indices.contains($0) ? orfs[$0] : nil }
        for r in shownRows {
            let ln = line(of: r)
            let yy = y(ln)
            ctx.draw(Text(r.label).font(.system(size: 10.5, weight: .bold, design: .monospaced)).foregroundColor(secondary), at: CGPoint(x: GXFrameBlock.gutter - 16, y: yy))
            let frameORFs = orfs.enumerated().filter { $0.element.frame == r.frame }
            for (k, c) in r.cells.enumerated() where c.column + 2 >= start && c.column < end {
                let a = max(c.column, start), b = min(c.column + 2, end - 1)
                let rect = CGRect(x: x(a) - cw / 2 + 1, y: yy - row / 2 + 1.5, width: CGFloat(b - a + 1) * cw - 2, height: row - 3)
                let box = Path(roundedRect: rect, cornerRadius: 4)
                let fill: Color = c.aa == "*" ? GXColors.stop.color.opacity(0.28) : (c.aa == "M" ? GXColors.start.color.opacity(0.26) : Color.primary.opacity(k % 2 == 0 ? 0.05 : 0.1))
                ctx.fill(box, with: .color(fill))
                // (an ORF's bar under its codons; the picked one bold)
                for (i, o) in frameORFs {
                    let lo = min(o.start, o.end) - 1, hi = max(o.start, o.end) - 1
                    guard c.column >= lo && c.column + 2 <= hi else { continue }
                    let isPicked = o == pickedORF
                    let bar = CGRect(x: rect.minX, y: rect.maxY - (isPicked ? 3 : 2), width: rect.width, height: isPicked ? 3 : 2)
                    ctx.fill(Path(bar), with: .color(Theme.accent.opacity(isPicked ? 1 : (i == 0 ? 0.75 : 0.4))))
                }
                let mid = c.column + 1
                if mid >= start && mid < end {
                    let colour: Color = c.aa == "*" ? GXColors.stop.color : (c.aa == "M" ? GXColors.start.color : .primary)
                    ctx.draw(Text(String(c.aa)).font(.system(size: 12.5, weight: c.aa == "M" || c.aa == "*" ? .heavy : .medium, design: .monospaced)).foregroundColor(colour),
                             at: CGPoint(x: x(mid), y: yy))
                }
            }
        }
    }

    /// What is under the pointer, in words: its frame, codon, amino acid and positions.
    private func describe(_ p: CGPoint) -> String? {
        let col = start + Int(floor((p.x - GXFrameBlock.gutter) / cw))
        guard col >= start && col < start + count else { return nil }
        let ln = Int(floor((p.y - 16) / row))
        guard let r = shownRows.first(where: { line(of: $0) == ln }) else {
            if ln == 0 { return "Position \(col + 1): \(sequence[col])" }
            return nil
        }
        guard let c = r.cells.first(where: { col >= $0.column && col <= $0.column + 2 }) else { return nil }
        let what = c.aa == "*" ? "stop" : "\(BioAminoAcids.byOne[c.aa]?.three ?? String(c.aa)) (\(BioAminoAcids.byOne[c.aa]?.name.lowercased() ?? "?"))"
        let strand = r.frame > 0 ? "nt \(c.column + 1)–\(c.column + 3)" : "nt \(c.column + 3)–\(c.column + 1) on the reverse strand"
        return "Frame \(r.label) · codon \(c.number) · \(c.codon) → \(what) · \(strand)"
    }
}
