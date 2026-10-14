import AppKit
import SwiftUI

// Gene Expression's Mutations page (spec §4.3 Mutations, §7 items 16–17): a coding sequence (the teaching gene's, human
// β-globin's or one's own) changed by a press — a base substituted, 1–6 bases inserted at a gap, 1–6 deleted — or typed
// as genetics writes it (c.20A>T); the original and the mutant lined up base for base, with their mRNA codons and their
// proteins, the changes lit; and what kind of mutation it is, with its consequence. The sickle-cell preset (HBB c.20A>T)
// explains its numbering and its charge change.

struct GXMutationsPage: View {
    @ObservedObject var store: GXStore
    /// The original bases picked (a first and a last, at most 6 apart), or the gap after a base for an insertion.
    @State private var selStart: Int?
    @State private var selEnd: Int?
    @State private var gapAfter: Int?
    @State private var insertText = ""
    @State private var typed = ""
    @State private var typedError: String?
    @State private var hoverBase: Int?
    @FocusState private var typedFocused: Bool
    @FocusState private var rowsFocused: Bool

    private var source: GXMutations.Source { store.mutationSource }
    private var original: [Character] { Array(source.cds) }
    /// The change made and what it does (none when it no longer fits the sequence: "Your gene" was retyped).
    private var result: GXMutationResult? {
        guard let e = store.edit, e.fits(original) else { return nil }
        return GXMutations.classify(source.cds, e, downstream: source.downstream, matureNumbering: source.matureNumbering)
    }
    private var selection: ClosedRange<Int>? {
        guard let a = selStart else { return nil }
        let b = selEnd ?? a
        let r = min(a, b)...max(a, b)
        return r.upperBound < original.count ? r : nil
    }

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            comparison
        } right: {
            cards
        }
        .onChange(of: store.mutationGene) { _, _ in clearSelection() }
    }

    // MARK: Left: the gene, the change, the examples

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Coding sequence")
            Picker("", selection: $store.mutationGene) {
                ForEach(GXMutationGene.allCases.filter { $0 != .current || store.hasOwnCDS }) { g in Text(g.label).tag(g) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("The teaching gene's, human β-globin's, or your own sequence's coding sequence (Transcribe & translate page)")
            Text("\(source.name): \(original.count) nt, \(original.count / 3) codons, ATG to the stop.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
            Button {
                withAnimation(Motion.snappy) {
                    clearSelection()
                    store.sickle()
                }
            } label: {
                Label("Sickle-cell (HBB c.20A>T)", systemImage: "drop.fill").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: store.mutationGene == .hbb && store.edit == GXMutations.sickle)
            .controlSize(.large)
            .help("β-globin with the sickle-cell mutation: GAG → GTG in codon 7")

            BioHeading("Make a change")
            tools
            BioHeading("Or type it")
            TextField("c.20A>T, c.6_7insA, c.7del…", text: $typed)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 13, design: .monospaced))
                .focused($typedFocused)
                .background(
                    // (⌘F: this field)
                    Button("") { typedFocused = true }
                        .keyboardShortcut("f", modifiers: .command)
                        .opacity(0)
                        .accessibilityHidden(true)
                )
                .onSubmit { applyTyped() }
                .accessibilityLabel("A mutation, as genetics writes it")
            if let e = typedError {
                Label(e, systemImage: "exclamationmark.circle").font(.sCaption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
            } else {
                Text("Substitution c.20A>T · insertion c.6_7insA · deletion c.7del or c.13_15del · duplication c.5dup. Positions count from the A of ATG.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            BioHeading("Try one")
            Menu {
                ForEach(examples.indices, id: \.self) { i in
                    Button(examples[i].label) {
                        withAnimation(Motion.snappy) {
                            clearSelection()
                            store.edit = examples[i].edit
                        }
                    }
                }
            } label: {
                Text("Examples").frame(maxWidth: .infinity, alignment: .leading)
            }
            .menuStyle(.button)
            .help("Mutations of this coding sequence, one of each kind")
            Button {
                withAnimation(Motion.snappy) { store.edit = nil }
            } label: {
                Text("Back to the original").frame(maxWidth: .infinity)
            }
            .glassButton()
            .disabled(store.edit == nil)
        }
        .font(.sCallout)
    }

    /// One of each kind for the sequence shown: §6.12's table for the teaching gene; for β-globin the sickle-cell
    /// mutation and its neighbours HbC, HbE and a β⁰-thalassaemia nonsense mutation.
    private var examples: [(label: String, edit: GXEdit)] {
        switch store.mutationGene {
        case .teaching:
            return GXMutations.teachingVectors + [("c.3G>A (start-loss)", .substitute(at: 2, to: "A")),
                                                  ("c.28T>C (stop-loss)", .substitute(at: 27, to: "C")),
                                                  ("c.6_7insGGC (in-frame insertion)", .insert(after: 5, bases: "GGC"))]
        case .hbb:
            return [("Sickle cell, HbS: c.20A>T (missense)", GXMutations.sickle),
                    ("HbC: c.19G>A (missense)", .substitute(at: 18, to: "A")),
                    ("HbE: c.79G>A (missense)", .substitute(at: 78, to: "A")),
                    ("β⁰-thalassaemia: c.118C>T (nonsense)", .substitute(at: 117, to: "T")),
                    ("c.9T>C (silent)", .substitute(at: 8, to: "C")),
                    ("c.20del (frameshift)", .delete(from: 19, to: 19)),
                    ("c.19_21del (in-frame deletion)", .delete(from: 18, to: 20))]
        case .current:
            let n = original.count
            var out: [(label: String, edit: GXEdit)] = []
            if n >= 6 { out.append(("c.4del (frameshift)", .delete(from: 3, to: 3))) }
            if n >= 9 { out.append(("c.4_6del (in-frame deletion)", .delete(from: 3, to: 5))) }
            if n >= 6 { out.append(("c.3G>A (start-loss)", .substitute(at: 2, to: "A"))) }
            return out
        }
    }

    @ViewBuilder
    private var tools: some View {
        if let g = gapAfter {
            Text("Insert between c.\(g + 1) and c.\(g + 2):").font(.sCallout.weight(.semibold))
            HStack(spacing: 8) {
                TextField("A", text: $insertText)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 13, design: .monospaced))
                    .onSubmit { insert() }
                    .accessibilityLabel("Bases to insert")
                Button("Insert") { insert() }
                    .glassButton(prominent: true)
                    .disabled(!insertValid)
            }
            Text("1–\(GXEdit.maxLength) bases of A, C, G and T. Or type them with the gap picked.").font(.sCaption).foregroundStyle(.secondary)
        } else if let r = selection {
            if r.count == 1 {
                let b = original[r.lowerBound]
                Text("c.\(r.lowerBound + 1) is \(String(b)) (codon \(r.lowerBound / 3 + 1)). Change it to:").font(.sCallout.weight(.semibold))
                HStack(spacing: 6) {
                    ForEach(["A", "C", "G", "T"], id: \.self) { s in
                        let c = Character(s)
                        Button { apply(.substitute(at: r.lowerBound, to: c)) } label: {
                            Text(s).font(.system(size: 15, weight: .bold, design: .monospaced)).foregroundColor(BioColor.base(c)).frame(maxWidth: .infinity, minHeight: 30)
                        }
                        .glassButton()
                        .disabled(c == b)
                        .help(c == b ? "It is \(s) already" : "Substitute \(String(b)) with \(s) (or press \(s))")
                    }
                }
            } else {
                Text("c.\(r.lowerBound + 1)–\(r.upperBound + 1): \(String(original[r])) (\(r.count) bases)").font(.sCallout.weight(.semibold))
            }
            Button(role: .destructive) { apply(.delete(from: r.lowerBound, to: r.upperBound)) } label: {
                Text(r.count == 1 ? "Delete c.\(r.lowerBound + 1)" : "Delete these \(r.count) bases").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("Delete (⌫)")
        } else {
            Text("Press a base of the original to change or delete it (Shift-press to take up to \(GXEdit.maxLength)), or press between two bases to insert there. Keys: A C G T, ⌫, ← →. One change at a time: a new one replaces the last.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var insertValid: Bool {
        let b = insertText.uppercased().filter { !$0.isWhitespace }
        return (1...GXEdit.maxLength).contains(b.count) && b.allSatisfy { "ACGT".contains($0) }
    }

    private func insert() {
        guard let g = gapAfter, insertValid else { return }
        apply(.insert(after: g, bases: insertText.uppercased().filter { !$0.isWhitespace }))
    }

    private func apply(_ e: GXEdit) {
        guard e.fits(original) else { return }
        withAnimation(Motion.snappy) {
            store.edit = e
            clearSelection()
        }
        typedError = nil
    }

    private func applyTyped() {
        let r = GXEdit.parse(typed, on: original)
        if let e = r.edit {
            apply(e)
        } else {
            typedError = r.error
        }
    }

    private func clearSelection() {
        selStart = nil
        selEnd = nil
        gapAfter = nil
        insertText = ""
    }

    // MARK: Centre: the original and the mutant, base for base

    private var comparison: some View {
        let res = result
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(res == nil ? "The original" : "Original and mutant").font(.system(size: 20, weight: .bold))
                    Text(res.map { "\($0.hgvs) · \($0.edit.words(original))" } ?? "Codons boxed in turn from ATG; each amino acid under its codon.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                if let res { GXBadge(text: res.kind.title, colour: GXMutationsPage.colour(res.kind)) }
            }
            .padding(16)
            GeometryReader { geo in
                let cw: CGFloat = 17
                let per = max(15, Int((geo.size.width - 32 - GXMutationRows.gutter - 8) / cw) / 3 * 3)
                let aligned = res?.aligned ?? original.enumerated().map { i, b in GXAlignedBase(original: b, mutant: b, originalIndex: i, mutantIndex: i) }
                let count = (aligned.count + per - 1) / per
                let firstChanged = aligned.firstIndex { $0.changed }
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 18) {
                            ForEach(0..<count, id: \.self) { r in
                                GXMutationRows(aligned: aligned, from: r * per, count: min(per, aligned.count - r * per), result: res,
                                               original: originalTranslation, cw: cw, selection: selection, gapAfter: gapAfter,
                                               hover: $hoverBase) { pick in press(pick) }
                                    .id(r)
                            }
                        }
                        .padding(.horizontal, 16)
                        .padding(.bottom, 16)
                    }
                    .onChange(of: store.edit) { _, _ in
                        if let f = firstChanged { withAnimation(Motion.gentle) { proxy.scrollTo(f / per, anchor: .center) } }
                    }
                }
            }
        }
        .card(radius: 20)
        .focusable()
        .focusEffectDisabled()
        .focused($rowsFocused)
        .onKeyPress(characters: CharacterSet(charactersIn: "acgtACGT"), phases: .down) { press in
            guard let b = press.characters.uppercased().first else { return .ignored }
            if let g = gapAfter {
                apply(.insert(after: g, bases: String(b)))
                return .handled
            }
            if let r = selection, r.count == 1 {
                apply(.substitute(at: r.lowerBound, to: b))
                return .handled
            }
            return .ignored
        }
        .onKeyPress(.delete) { deleteSelected() }
        .onKeyPress(.deleteForward) { deleteSelected() }
        .onKeyPress(.escape) {
            clearSelection()
            return .handled
        }
        .onKeyPress(.leftArrow) { move(-1) }
        .onKeyPress(.rightArrow) { move(1) }
    }

    private func deleteSelected() -> KeyPress.Result {
        guard let r = selection else { return .ignored }
        apply(.delete(from: r.lowerBound, to: r.upperBound))
        return .handled
    }

    private func move(_ d: Int) -> KeyPress.Result {
        let n = original.count
        guard n > 0 else { return .ignored }
        let at = (selStart ?? gapAfter ?? -1) + d
        selStart = min(max(at, 0), n - 1)
        selEnd = nil
        gapAfter = nil
        return .handled
    }

    /// The original read in frame (its codons' amino acids).
    private var originalTranslation: GXTranslation { result?.original ?? GXTranslation(original) }

    /// A press on the original: a base (Shift extends the pick, up to 6) or the gap after one.
    private func press(_ pick: GXMutationRows.Pick) {
        rowsFocused = true
        switch pick {
        case .base(let i):
            if NSEvent.modifierFlags.contains(.shift), let a = selStart {
                selEnd = min(max(i, a - (GXEdit.maxLength - 1)), a + (GXEdit.maxLength - 1))
            } else {
                selStart = i
                selEnd = nil
            }
            gapAfter = nil
        case .gap(let i):
            gapAfter = i
            selStart = nil
            selEnd = nil
        }
    }

    // MARK: Right: what kind of mutation, and what it does

    static func colour(_ k: GXMutationResult.Kind) -> Color {
        switch k {
        case .none: return .secondary
        case .silent: return BioPalette.adenine.color
        case .missense: return BioPalette.guanine.color
        case .nonsense, .startLoss: return BioPalette.thymine.color
        case .stopLoss: return BioPalette.uracil.color
        case .frameshift: return BioPalette.strand1.color
        case .inFrameInsertion, .inFrameDeletion: return BioPalette.cytosine.color
        }
    }

    private var cards: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let res = result {
                BioCard(title: res.kind.title, swatch: GXMutationsPage.colour(res.kind), subtitle: "\(res.hgvs)\(res.change.isEmpty ? "" : " · " + res.change)") {
                    ForEach(res.lines, id: \.self) { line in
                        Text(line).font(.sCallout).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
                    }
                    if let a = res.from, let b = res.to {
                        HStack(spacing: 8) {
                            aaChip(a)
                            Image(systemName: "arrow.right").foregroundStyle(.secondary)
                            aaChip(b)
                        }
                    }
                    if let k = res.kept {
                        VStack(alignment: .leading, spacing: 4) {
                            ProgressView(value: k).tint(GXColors.stop.color)
                            Text("\(Int((k * 100).rounded()))% of the protein kept").font(.sCaption).foregroundStyle(.secondary)
                        }
                    }
                    BioValueRow(label: "Protein", value: "\(res.original.protein.count) → \(res.mutant.protein.count) aa\(res.mutant.stopAt == nil ? " (no stop)" : "")", last: true)
                }
                if store.mutationGene == .hbb && store.edit == GXMutations.sickle { sickleCard }
            } else {
                BioCard(title: "Make a mutation") {
                    Text("Press a base, a gap or an example on the left. The card names the kind of mutation and what it does to the protein.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: "Good to know") {
                Text("Most point mutations are silent or missense, not frameshifts. Mutations do not “denature” proteins; they change the sequence, which may change folding. Many mutations have no effect.")
                    .font(.sCallout)
                    .fixedSize(horizontal: false, vertical: true)
            }
            BioCard(title: "The kinds") {
                kind(.silent, "codon changed, same amino acid")
                kind(.missense, "a different amino acid")
                kind(.nonsense, "a sense codon becomes a stop: the protein is cut short")
                kind(.frameshift, "an insertion or deletion not a multiple of 3: every codon after it changes")
                kind(.inFrameDeletion, "a multiple of 3 inserted or deleted: amino acids added or lost, the rest unchanged")
                kind(.startLoss, "AUG destroyed: translation begins at the next AUG")
                kind(.stopLoss, "the stop becomes a sense codon: translation runs on")
            }
        }
    }

    private func kind(_ k: GXMutationResult.Kind, _ text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Circle().fill(GXMutationsPage.colour(k)).frame(width: 8, height: 8)
            (Text(k == .inFrameDeletion ? "In-frame" : k.title).bold() + Text(": " + text))
                .font(.sCaption)
                .fixedSize(horizontal: false, vertical: true)
        }
        .accessibilityElement(children: .combine)
    }

    private func aaChip(_ a: BioAminoAcid) -> some View {
        HStack(spacing: 5) {
            Circle().fill(a.group.color).frame(width: 9, height: 9)
            Text("\(a.three) · \(GXCodonTable.classWords(a))").font(.sCaption.weight(.medium))
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(Capsule().fill(a.group.color.opacity(0.14)))
    }

    private var sickleCard: some View {
        BioCard(title: "Sickle-cell haemoglobin (HbS)", swatch: BioPalette.thymine.color) {
            BioFactRow(label: "Numbering", text: "Codon 7 counting ATG as codon 1 (p.Glu7Val in HGVS) is the classic Glu6Val (E6V): the β-globin chain in red cells has lost its first Met, so its Glu is number 6.")
            BioFactRow(label: "The change", text: "One base, A → T (GAG → GTG): a missense mutation, not a frameshift. The protein keeps its 147 amino acids (146 once the Met is removed).")
            BioFactRow(label: "Charge", text: "Glu is negatively charged at cell pH; Val is nonpolar and uncharged. Each β chain loses a negative charge and gains a hydrophobic patch on its surface.")
            BioFactRow(label: "What it does", text: "When deoxygenated, HbS molecules stick to each other through that patch and form long fibres that bend red cells into sickles.")
            Text("This tool uses only HBB's coding sequence (its 3 exons joined).")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
    }
}

/// A run of the original and the mutant, lined up: the codon numbers; the original DNA (pressable), its mRNA and its
/// protein; then the mutant's DNA (gaps where bases went), mRNA and protein — codons boxed in turn on each, the
/// changed bases, codons and amino acids lit.
struct GXMutationRows: View {
    enum Pick: Equatable {
        case base(Int)
        case gap(Int)
    }

    static let gutter: CGFloat = 66
    let aligned: [GXAlignedBase]
    let from: Int
    let count: Int
    let result: GXMutationResult?
    /// The original read in frame.
    let original: GXTranslation
    let cw: CGFloat
    let selection: ClosedRange<Int>?
    let gapAfter: Int?
    @Binding var hover: Int?
    let pick: (Pick) -> Void
    @Environment(\.colorScheme) private var scheme

    let row: CGFloat = 20
    private var lines: Int { result == nil ? 4 : 7 }

    var body: some View {
        Canvas { ctx, size in draw(&ctx) }
            .frame(height: CGFloat(lines) * row + 6)
            .frame(maxWidth: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .onContinuousHover { phase in
                switch phase {
                case .active(let p):
                    if case .base(let i) = locate(p) { hover = i } else { hover = nil }
                case .ended: hover = nil
                }
            }
            .gesture(DragGesture(minimumDistance: 0).onEnded { v in
                guard hypot(v.translation.width, v.translation.height) < 5, let p = locate(v.location) else { return }
                pick(p)
            })
            .accessibilityElement(children: .ignore)
            .accessibilityLabel(accessibilityText)
    }

    private var accessibilityText: String {
        let o = aligned[from..<(from + count)].compactMap(\.original)
        let m = aligned[from..<(from + count)].compactMap(\.mutant)
        return "Original \(String(o))" + (result == nil ? "" : ", mutant \(String(m))")
    }

    private func x(_ col: Int) -> CGFloat { GXMutationRows.gutter + (CGFloat(col - from) + 0.5) * cw }
    private func y(_ line: Int) -> CGFloat { row * (CGFloat(line) + 0.5) + 2 }

    /// What a point on the original's DNA is: a base, or (near a base's edge) the gap after the base to its left.
    private func locate(_ p: CGPoint) -> Pick? {
        guard p.y >= y(1) - row / 2, p.y <= y(1) + row / 2 else { return nil }
        let u = (p.x - GXMutationRows.gutter) / cw
        let col = from + Int(floor(u))
        guard col >= from, col < from + count else { return nil }
        let into = u - floor(u)
        let n = aligned.compactMap(\.originalIndex).count
        func originalLeft(of c: Int) -> Int? { stride(from: c, through: 0, by: -1).lazy.compactMap { aligned[$0].originalIndex }.first }
        if into > 0.82, let i = aligned[col].originalIndex, i < n - 1 { return .gap(i) }
        if into < 0.18, col > 0, let i = originalLeft(of: col - 1), i < n - 1 { return .gap(i) }
        if let i = aligned[col].originalIndex { return .base(i) }
        return nil
    }

    /// Codon spans on the aligned columns: for each codon of one side, the columns its three bases are in.
    private func spans(_ side: KeyPath<GXAlignedBase, Int?>) -> [(codon: Int, first: Int, last: Int)] {
        var out: [Int: (Int, Int)] = [:]
        for (col, a) in aligned.enumerated() {
            guard let i = a[keyPath: side] else { continue }
            let k = i / 3
            if let s = out[k] { out[k] = (s.0, col) } else { out[k] = (col, col) }
        }
        return out.keys.sorted().compactMap { k in out[k].map { (k, $0.0, $0.1) } }
    }

    private func draw(_ ctx: inout GraphicsContext) {
        let end = from + count
        let secondary = Color.secondary
        let names = result == nil ? ["codon", "Original", "mRNA", "protein"] : ["codon", "Original", "mRNA", "protein", "Mutant", "mRNA", "protein"]
        for (k, n) in names.enumerated() {
            let bold = n == "Original" || n == "Mutant"
            ctx.draw(Text(n).font(.system(size: 10.5, weight: bold ? .bold : .medium)).foregroundColor(bold ? .primary : secondary),
                     at: CGPoint(x: GXMutationRows.gutter - 8, y: y(k)), anchor: .trailing)
        }
        // (the original: codon numbers, boxes, bases, mRNA, protein)
        let oSpans = spans(\.originalIndex)
        let o = original
        side(&ctx, spans: oSpans, line: 1, base: { $0.original }, index: { $0.originalIndex }, stopAt: o.stopAt ?? Int.max,
             total: o.codons.count * 3, aa: { o.aa($0) }, changedCodons: result?.changedOriginal ?? [], end: end, interactive: true)
        for s in oSpans where s.first >= from && s.first < end && (s.codon == 0 || (s.codon + 1) % 5 == 0) {
            ctx.draw(Text("\(s.codon + 1)").font(.system(size: 9.5, design: .monospaced)).foregroundColor(secondary),
                     at: CGPoint(x: (x(s.first) + x(min(s.last, end - 1))) / 2, y: y(0)))
        }
        if let r = result {
            side(&ctx, spans: spans(\.mutantIndex), line: 4, base: { $0.mutant }, index: { $0.mutantIndex }, stopAt: r.mutant.stopAt ?? Int.max,
                 total: r.mutant.codons.count * 3, aa: { r.mutant.aa($0) }, changedCodons: r.changedMutant, end: end, interactive: false)
        }
    }

    private func side(_ ctx: inout GraphicsContext, spans: [(codon: Int, first: Int, last: Int)], line: Int, base: (GXAlignedBase) -> Character?,
                      index: (GXAlignedBase) -> Int?, stopAt: Int, total: Int, aa: (Int) -> Character, changedCodons: Set<Int>, end: Int,
                      interactive: Bool) {
        let dnaY = y(line), rnaY = y(line + 1), aaY = y(line + 2)
        // (codon boxes in turn, a changed codon lit, codons past the stop dashed: not translated)
        for s in spans where s.last >= from && s.first < end {
            let a = max(s.first, from), b = min(s.last, end - 1)
            for yy in [dnaY, rnaY] {
                let rect = CGRect(x: x(a) - cw / 2 + 1, y: yy - row / 2 + 2, width: CGFloat(b - a + 1) * cw - 2, height: row - 4)
                let path = Path(roundedRect: rect, cornerRadius: 4)
                if s.codon > stopAt {
                    ctx.stroke(path, with: .color(Color.secondary.opacity(0.35)), style: StrokeStyle(lineWidth: 1, dash: [2, 2]))
                } else if changedCodons.contains(s.codon) {
                    ctx.fill(path, with: .color(Color.orange.opacity(0.2)))
                    ctx.stroke(path, with: .color(Color.orange.opacity(0.8)), lineWidth: 1)
                } else {
                    ctx.fill(path, with: .color(Color.primary.opacity(s.codon % 2 == 0 ? 0.05 : 0.1)))
                }
            }
            // (the amino acid under the codon's middle; a last codon of fewer than three bases has none)
            let mid = (s.first + s.last) / 2
            guard mid >= from && mid < end, 3 * s.codon + 2 < total else { continue }
            let letter = aa(s.codon)
            if s.codon > stopAt {
                ctx.draw(Text("·").font(.system(size: 12)).foregroundColor(Color.secondary), at: CGPoint(x: x(mid), y: aaY))
                continue
            }
            let colour: Color = letter == "*" ? GXColors.stop.color : BioColor.aminoAcid(letter)
            let circle = Path(ellipseIn: CGRect(x: x(mid) - 8.5, y: aaY - 8.5, width: 17, height: 17))
            ctx.fill(circle, with: .color(colour.opacity(letter == " " ? 0 : 0.85)))
            if changedCodons.contains(s.codon) { ctx.stroke(Path(ellipseIn: CGRect(x: x(mid) - 10.5, y: aaY - 10.5, width: 21, height: 21)), with: .color(Color.orange), lineWidth: 1.5) }
            let pair = letter == "*" ? GXColors.stop : BioPalette.aminoAcid(one: letter)
            ctx.draw(Text(String(letter)).font(.system(size: 11, weight: .bold, design: .monospaced)).foregroundColor(GXColors.letter(on: pair, dark: scheme == .dark)),
                     at: CGPoint(x: x(mid), y: aaY))
        }
        // (the bases: DNA, then mRNA (U for T); gaps as dashes; changed bases lit)
        for col in from..<end {
            let a = aligned[col]
            let i = index(a)
            if interactive, let i {
                if let r = selection, r.contains(i) {
                    ctx.fill(Path(roundedRect: CGRect(x: x(col) - cw / 2 + 1, y: dnaY - row / 2 + 1, width: cw - 2, height: row - 2), cornerRadius: 4), with: .color(Theme.accent.opacity(0.35)))
                } else if hover == i {
                    ctx.fill(Path(roundedRect: CGRect(x: x(col) - cw / 2 + 1, y: dnaY - row / 2 + 1, width: cw - 2, height: row - 2), cornerRadius: 4), with: .color(Theme.accent.opacity(0.15)))
                }
                if gapAfter == i {
                    let gx = x(col) + cw / 2
                    ctx.fill(Path(roundedRect: CGRect(x: gx - 1.5, y: dnaY - row / 2, width: 3, height: row), cornerRadius: 1.5), with: .color(Theme.accent))
                }
            }
            guard let b = base(a) else {
                ctx.draw(Text("–").font(.system(size: 13, weight: .bold, design: .monospaced)).foregroundColor(Color.orange), at: CGPoint(x: x(col), y: dnaY))
                ctx.draw(Text("–").font(.system(size: 12, design: .monospaced)).foregroundColor(Color.orange), at: CGPoint(x: x(col), y: rnaY))
                continue
            }
            if a.changed && !interactive {
                ctx.fill(Path(roundedRect: CGRect(x: x(col) - cw / 2 + 2, y: dnaY - row / 2 + 2, width: cw - 4, height: row - 4), cornerRadius: 3), with: .color(Color.orange.opacity(0.45)))
            }
            ctx.draw(Text(String(b)).font(.system(size: 13, weight: .semibold, design: .monospaced)).foregroundColor(BioColor.base(b)), at: CGPoint(x: x(col), y: dnaY))
            let r: Character = b == "T" ? "U" : b
            ctx.draw(Text(String(r)).font(.system(size: 12, design: .monospaced)).foregroundColor(BioColor.base(r)), at: CGPoint(x: x(col), y: rnaY))
        }
    }
}
