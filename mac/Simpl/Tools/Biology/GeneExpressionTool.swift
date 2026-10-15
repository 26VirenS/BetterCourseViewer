import SwiftUI

// Gene Expression (spec §4.3): a gene transcribed and translated step by step (GeneExpressionExpressPage.swift), the
// codon table as a grid and a wheel with its lookup both ways (GeneExpressionCodonsPage.swift), the six reading frames
// and their ORFs (GeneExpressionFramesPage.swift), and what a mutation does to the protein
// (GeneExpressionMutationsPage.swift). This file holds the tool itself — its pages, what it remembers (GXStore, kept
// while the tool is closed and in SimplBio.gene.*), what a launch or another tool asks of it — and its pin's quick
// codon lookup. The arithmetic is Model/GeneExpression*.swift.

struct GeneExpressionTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.gene)`.
    static let pages: [BioPage] = [
        BioPage(id: "express", title: "Transcribe & translate", symbol: "play.rectangle", help: "A gene made into a protein, step by step"),
        BioPage(id: "codons", title: "Codon table", symbol: "tablecells", help: "The genetic code"),
        BioPage(id: "frames", title: "Reading frames", symbol: "text.justify.left", help: "Six frames and the open reading frames"),
        BioPage(id: "mutations", title: "Mutations", symbol: "arrow.triangle.swap", help: "What a change in the DNA does to the protein"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var store = GXStore.shared

    var body: some View {
        Group {
            switch nav.page(.gene) {
            case "codons": GXCodonsPage(store: store)
            case "frames": GXFramesPage(store: store)
            case "mutations": GXMutationsPage(store: store)
            default: GXExpressPage(store: store, steps: store.steps)
            }
        }
        .onAppear {
            store.applyLaunch(state)
            store.takeHandOff()
        }
    }
}

// MARK: - What the tool remembers

/// A view of the codon table.
enum GXCodonView: String, CaseIterable, Identifiable {
    case grid, wheel
    var id: String { rawValue }
    var label: String { self == .grid ? "Grid" : "Wheel" }
}

/// Which coding sequence the Mutations page starts from.
enum GXMutationGene: String, CaseIterable, Identifiable {
    case teaching, hbb, current
    var id: String { rawValue }
    var label: String {
        switch self {
        case .teaching: return "Teaching gene"
        case .hbb: return "β-globin"
        case .current: return "Your gene"
        }
    }
}

/// Gene Expression's state, shared by its pages and kept while the tool is closed: the gene and the step-through, the
/// codon table's view and lookup, the reading frames' sequence, and the mutation. Choices are remembered on this Mac
/// (SimplBio.gene.*).
@MainActor
final class GXStore: ObservableObject {
    static let shared = GXStore()

    // MARK: Transcribe & translate

    @Published var source: GXSource { didSet { if source != oldValue { BioPrefs.set(.gene, "source", source.rawValue); rebuild() } } }
    @Published var cell: GXCell { didSet { if cell != oldValue { BioPrefs.set(.gene, "cell", cell.rawValue); rebuild() } } }
    /// "Type your own": coding-strand DNA or mRNA, as typed.
    @Published var ownText: String { didSet { if ownText != oldValue { BioPrefs.set(.gene, "own", ownText); if source == .own { rebuild() } } } }
    @Published var ownKind: BioSeqKind { didSet { if ownKind != oldValue { BioPrefs.set(.gene, "ownKind", ownKind.rawValue); if source == .own { rebuild() } } } }
    /// The step-through of the gene shown (nil: "Type your own" has nothing to use yet).
    @Published private(set) var process: GXProcess?
    let steps: BioStepController

    // MARK: Codon table

    @Published var codonView: GXCodonView { didSet { BioPrefs.set(.gene, "codonView", codonView.rawValue) } }
    /// The table in DNA coding-strand letters (T for U), clearly titled so.
    @Published var dnaLetters: Bool { didSet { BioPrefs.set(.gene, "dnaLetters", dnaLetters) } }
    @Published var lookup = ""
    /// The codon pressed in the table (its card on the right).
    @Published var picked: String?

    // MARK: Reading frames

    @Published var framesText: String { didSet { if framesText != oldValue { BioPrefs.set(.gene, "frames", framesText); refreshFrames() } } }
    @Published var framesKind: BioSeqKind { didSet { if framesKind != oldValue { BioPrefs.set(.gene, "framesKind", framesKind.rawValue); refreshFrames() } } }
    /// The ORF finder's shortest, in codons (stop included).
    @Published var minCodons: Int { didSet { if minCodons != oldValue { BioPrefs.set(.gene, "minORF", String(minCodons)); refreshFrames() } } }
    @Published var showReverse: Bool { didSet { if showReverse != oldValue { BioPrefs.set(.gene, "reverse", showReverse); refreshFrames() } } }
    /// The rows and ORFs of the sequence (worked out when it changes, not on every draw).
    @Published private(set) var frameRows: [GXFrameRow] = []
    @Published private(set) var orfs: [BioORF] = []
    @Published var pickedORF: Int?

    // MARK: Mutations

    @Published var mutationGene: GXMutationGene { didSet { if mutationGene != oldValue { BioPrefs.set(.gene, "mutationGene", mutationGene.rawValue); edit = nil } } }
    @Published var edit: GXEdit?

    init() {
        source = BioPrefs.string(.gene, "source").flatMap(GXSource.init(rawValue:)) ?? .teaching
        cell = BioPrefs.string(.gene, "cell").flatMap(GXCell.init(rawValue:)) ?? .eukaryote
        ownText = BioPrefs.string(.gene, "own") ?? "ATGGCCATTGTAATGGGCCGCTGAAAGGGTGCCCGATAG"
        ownKind = BioPrefs.string(.gene, "ownKind").flatMap(BioSeqKind.init(rawValue:)) ?? .dna
        codonView = BioPrefs.string(.gene, "codonView").flatMap(GXCodonView.init(rawValue:)) ?? .grid
        dnaLetters = BioPrefs.bool(.gene, "dnaLetters", default: false)
        framesText = BioPrefs.string(.gene, "frames") ?? BioGenes.teaching.matureMRNA
        framesKind = BioPrefs.string(.gene, "framesKind").flatMap(BioSeqKind.init(rawValue:)) ?? .rna
        minCodons = BioPrefs.string(.gene, "minORF").flatMap(Int.init) ?? 10
        showReverse = BioPrefs.bool(.gene, "reverse", default: true)
        mutationGene = BioPrefs.string(.gene, "mutationGene").flatMap(GXMutationGene.init(rawValue:)) ?? .teaching
        steps = BioStepController(steps: [])
        rebuild()
        refreshFrames()
        // (1.3.78) the launch's state as the store is made, not when the page first appears: the screenshot suite's
        // codon grid was pictured without its lookup (the wheel, the same lookup, with it)
        applyLaunch(BioLaunchState.forTool(.gene))
    }

    /// "Type your own" cleaned (§3.7), kept to the step-through's 600 nt.
    var ownInput: BioSequenceInput { BioSequenceInput(ownText, as: ownKind, cap: GXProcess.cap) }

    /// The gene and its steps made again (another gene, cell type or sequence): the step-through starts over.
    private func rebuild() {
        let gene: GXGene?
        if source == .own {
            gene = ownInput.sequence.flatMap { GXGene.own($0, cell: cell) }
        } else {
            gene = GXGene.make(source, cell: cell)
        }
        process = gene.map { GXProcess(gene: $0) }
        steps.setSteps(process?.steps ?? [])
        // (the Mutations page's "Your gene" only while there is one)
        if mutationGene == .current && !hasOwnCDS { mutationGene = .teaching }
    }

    // MARK: Reading frames

    var framesInput: BioSequenceInput { BioSequenceInput(framesText, as: framesKind, cap: 3000) }

    private func refreshFrames() {
        guard let s = framesInput.sequence else {
            frameRows = []
            orfs = []
            pickedORF = nil
            return
        }
        let dna = framesKind == .dna
        frameRows = GXReadingFrames.rows(s, reverse: dna && showReverse)
        orfs = BioSeq.orfs(s, minCodons: max(minCodons, 1), reverseStrand: dna && showReverse)
        pickedORF = nil
    }

    // MARK: Mutations

    /// The coding sequence the Mutations page works on (the current gene's own, when it has one and it is chosen).
    var mutationSource: GXMutations.Source {
        switch mutationGene {
        case .teaching: return GXMutations.teaching
        case .hbb: return GXMutations.hbb
        case .current:
            guard source == .own, let g = process?.gene, !g.codons.isEmpty else { return GXMutations.teaching }
            return GXMutations.Source(name: "Your gene", cds: g.cdsDNA, downstream: g.downstreamDNA, matureNumbering: false)
        }
    }

    /// Whether "Your gene" can be offered (a sequence of one's own with a coding sequence).
    var hasOwnCDS: Bool { source == .own && !(process?.gene.codons.isEmpty ?? true) }

    /// The sickle-cell preset: β-globin with c.20A>T.
    func sickle() {
        mutationGene = .hbb
        edit = GXMutations.sickle
    }

    // MARK: What a launch or another tool asks

    private static var launchApplied = false

    /// (-SimplTool gene -SimplToolState <page>:<…>) Applied once a launch: gene=teaching|hbb|own, seq=<letters>,
    /// prokaryote, step=<n> (1-based), wheel, dna, find=<codon or amino acid>, example=<n> (the reading frames' nth
    /// example), min=<codons>, sickle, mut=<c.…>, mutgene=teaching|hbb. A seq goes to the reading frames when the page is
    /// theirs, else to "Type your own".
    func applyLaunch(_ s: BioLaunchState?) {
        guard let s, !GXStore.launchApplied else { return }
        GXStore.launchApplied = true
        let page = s.page ?? BioNav.shared.page(.gene)
        if let seq = s.value("seq"), !seq.isEmpty {
            let rna = seq.uppercased().contains("U") && !seq.uppercased().contains("T")
            if page == "frames" {
                framesKind = rna ? .rna : .dna
                framesText = seq
            } else {
                ownKind = rna ? .rna : .dna
                ownText = seq
                source = .own
            }
        }
        if let g = s.value("gene").flatMap({ GXSource(rawValue: $0.lowercased()) }) { source = g }
        if s.has("prokaryote") || s.value("cell")?.lowercased() == "prokaryote" { cell = .prokaryote }
        if s.has("eukaryote") || s.value("cell")?.lowercased() == "eukaryote" { cell = .eukaryote }
        if s.has("wheel") || s.value("view")?.lowercased() == "wheel" { codonView = .wheel }
        if s.has("grid") || s.value("view")?.lowercased() == "grid" { codonView = .grid }
        if s.has("dna") { dnaLetters = true }
        if let f = s.value("find") ?? s.value("codon") { lookup = f }
        if let k = s.value("example").flatMap(Int.init), GXReadingFrames.presets.indices.contains(k - 1) {
            let p = GXReadingFrames.presets[k - 1]
            framesKind = p.dna ? .dna : .rna
            framesText = p.sequence
        }
        if let m = s.value("min").flatMap(Int.init) { minCodons = m }
        if let g = s.value("mutgene").flatMap({ GXMutationGene(rawValue: $0.lowercased()) }) { mutationGene = g }
        if s.has("sickle") { sickle() }
        if let m = s.value("mut") {
            edit = GXEdit.parse(m, on: Array(mutationSource.cds)).edit
        }
        if let n = s.value("step").flatMap(Int.init), n >= 1 {
            steps.go(to: min(n, steps.steps.count) - 1, animated: false)
        }
        if OpenClock.verbose {
            NSLog("[Simpl] bio launch: gene %@: %@ gene, %@, codons as a %@, lookup %@, step %d of %d", s.raw, source.rawValue,
                  cell.rawValue, codonView.rawValue, lookup.isEmpty ? "none" : lookup, steps.index + 1, steps.steps.count)
        }
    }

    /// What another tool handed over, taken once (spec §3.2): "sequence" (a coding strand: "Transcribe" on the DNA &
    /// RNA tool's Sequence page), "codon" (a lookup), "mutation" ("sickle" from the Biomolecule Builder's haemoglobin, or
    /// c.…), "page".
    func takeHandOff() {
        let tools = ToolsCenter.shared
        var page: String?
        if let seq = tools.takeInfo("sequence"), !seq.isEmpty {
            let up = seq.uppercased()
            ownKind = up.contains("U") && !up.contains("T") ? .rna : .dna
            ownText = seq
            source = .own
            page = "express"
        }
        if let c = tools.takeInfo("codon"), !c.isEmpty {
            lookup = c
            page = "codons"
        }
        if let m = tools.takeInfo("mutation") {
            if m.lowercased() == "sickle" {
                sickle()
            } else {
                edit = GXEdit.parse(m, on: Array(mutationSource.cds)).edit
            }
            page = "mutations"
        }
        if let p = tools.takeInfo("page"), GeneExpressionTool.pages.contains(where: { $0.id == p }) { page = p }
        if let page { BioNav.shared.set(.gene, page) }
    }
}

// MARK: - Shared pieces

/// The tool's own colours for the molecules drawn (proteins in quiet colours so the bases' and amino acids' stay the
/// loudest).
enum GXColors {
    static let polymerase = BioHexPair("#9b8ec4", "#b9aee3")
    static let ribosome = BioHexPair("#c49a6c", "#d9b48f")
    static let trna = BioHexPair("#6f8296", "#a3b4c6")
    static let releaseFactor = BioHexPair("#c27c9b", "#e09bbb")
    static let factor = BioHexPair("#b08bd9", "#c9aaf0")
    static let start = BioHexPair("#2e9e48", "#30d158")
    static let stop = BioHexPair("#d63a34", "#ff6961")

    /// A band's tint under the strands (and in the mRNA strip).
    static func region(_ k: GXRegion.Kind) -> Color {
        switch k {
        case .promoter: return Color.gray.opacity(0.16)
        case .utr5, .utr3: return Color(hex: "#5ea8ff").opacity(0.13)
        case .exon, .cds: return Color(hex: "#30d158").opacity(0.13)
        case .intron: return Color(hex: "#a58bff").opacity(0.18)
        default: return Color.gray.opacity(0.08)
        }
    }

    /// The letter colour that reads on a fill (an amino acid's circle): near-black on the lighter class colours, white on
    /// the darker ones — whichever contrasts more (by relative luminance), in light or dark.
    static func letter(on pair: BioHexPair, dark: Bool) -> Color {
        let c = BioPalette.rgb(pair.hex(dark: dark))
        func linear(_ v: Double) -> Double { v <= 0.04045 ? v / 12.92 : pow((v + 0.055) / 1.055, 2.4) }
        let l = 0.2126 * linear(c.r) + 0.7152 * linear(c.g) + 0.0722 * linear(c.b)
        return l > 0.18 ? Color.black.opacity(0.85) : Color.white
    }

    /// A band's colour at full strength (the overview strip, the mRNA strip).
    static func regionSolid(_ k: GXRegion.Kind) -> Color {
        switch k {
        case .promoter: return Color.gray.opacity(0.55)
        case .utr5, .utr3: return Color(hex: "#5ea8ff").opacity(0.7)
        case .exon, .cds: return Color(hex: "#30d158").opacity(0.75)
        case .intron: return Color(hex: "#a58bff").opacity(0.75)
        default: return Color.gray.opacity(0.4)
        }
    }
}

/// A codon's answer: its amino acid's name, three- and one-letter codes and class, with Start or Stop where it applies
/// (the pin's quick version and the codon table's lookup).
struct GXCodonAnswer: View {
    let query: GXCodonTable.Query
    var dna = false

    var body: some View {
        switch query {
        case .codon(let c):
            let aa = BioCode.aminoAcid(c) ?? "?"
            if aa == "*" {
                answer(title: "Stop", line: "\(GXCodonTable.letters(c, dna: dna)) · “\(BioCode.stopNames[c] ?? "stop")” · no amino acid", colour: GXColors.stop.color, badge: "Stop")
            } else if let a = BioAminoAcids.byOne[aa] {
                answer(title: a.name, line: "\(GXCodonTable.letters(c, dna: dna)) → \(a.three) · \(a.one) · \(a.group.label)", colour: a.group.color, badge: c == "AUG" ? "Start" : nil)
            }
        case .aminoAcid(let one):
            if let a = BioAminoAcids.byOne[one] {
                let n = a.codons.count
                answer(title: a.name, line: "\(a.three) · \(a.one) · \(a.group.label) · \(n) codon\(n == 1 ? "" : "s")", colour: a.group.color, badge: one == "M" ? "Start" : nil)
            }
        case .stop:
            answer(title: "Stop codons", line: "UAA, UAG, UGA · read by release factors", colour: GXColors.stop.color, badge: "Stop")
        case .invalid(let why):
            Label(why, systemImage: "exclamationmark.circle")
                .font(.sCallout)
                .foregroundStyle(.orange)
                .fixedSize(horizontal: false, vertical: true)
        case .empty:
            Text("Type a codon (AUG or ATG) or an amino acid (Lys, K, lysine).")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func answer(title: String, line: String, colour: Color, badge: String?) -> some View {
        HStack(alignment: .top, spacing: 10) {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(colour)
                .frame(width: 12, height: 12)
                .padding(.top, 5)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 6) {
                    Text(title).font(.sHeadline)
                    if let badge { GXBadge(text: badge, colour: badge == "Stop" ? GXColors.stop.color : GXColors.start.color) }
                }
                Text(line).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

/// A small capsule badge ("Start", "Stop", "Made up").
struct GXBadge: View {
    let text: String
    var colour: Color = .secondary

    var body: some View {
        Text(text)
            .font(.system(size: 10.5, weight: .bold))
            .foregroundStyle(colour)
            .padding(.horizontal, 6)
            .padding(.vertical, 1.5)
            .background(Capsule().fill(colour.opacity(0.14)))
            .overlay(Capsule().strokeBorder(colour.opacity(0.35), lineWidth: 0.5))
    }
}

/// Bases in their colours, monospaced (each letter always shown: never colour alone).
func gxBases(_ s: String, size: CGFloat = 13, weight: Font.Weight = .semibold) -> Text {
    s.reduce(Text("")) { $0 + Text(String($1)).foregroundColor(BioColor.base($1)) }
        .font(.system(size: size, weight: weight, design: .monospaced))
}

// MARK: - The pin's quick version

/// Gene Expression in its pin's popover (spec §4.3, §3.2 item 3): a codon field and its answer — the amino acid's
/// name, three- and one-letter codes and class, Start or Stop — and the code as a compact 4 × 4 grid under it (a press
/// fills the field). An amino acid typed lights its codons. The header's name opens the whole tool; "Open in the codon
/// table" takes the lookup with it.
struct GeneExpressionCompact: View {
    let openFull: (_ info: [String: String]) -> Void
    @State private var query = ""
    @FocusState private var focused: Bool

    var body: some View {
        let q = GXCodonTable.parse(query)
        let dna = query.uppercased().contains("T") && !query.uppercased().contains("U") && { if case .codon = q { return true } else { return false } }()
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 12) {
                TextField("AUG", text: $query)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 15, weight: .semibold, design: .monospaced))
                    .frame(width: 78)
                    .focused($focused)
                    .help("A codon (AUG or ATG) or an amino acid (Lys, K, lysine)")
                    .accessibilityLabel("Codon or amino acid")
                GXCodonAnswer(query: q, dna: dna)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            GXCodonMiniGrid(lit: GXCodonTable.matches(q), dna: dna) { query = GXCodonTable.letters($0, dna: dna) }
            HStack {
                Text("AUG starts translation and codes Met.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                Spacer(minLength: 6)
                Button("Open in the codon table") { openFull(["codon": query, "page": "codons"]) }
                    .buttonStyle(.link)
                    .font(.sCaption)
                    .help("The whole codon table, with this lookup")
            }
        }
        // (1.3.83) Open Full Screen carries the lookup to the codon table, as Open in the codon table does
        .quickCarry(query.trimmingCharacters(in: .whitespaces).isEmpty ? [:] : ["codon": query, "page": "codons"])
        .onAppear {
            // (the screenshot suite: -SimplPinCodon UGG fills the field)
            if query.isEmpty, let c = UserDefaults.standard.string(forKey: "SimplPinCodon") { query = c }
            DispatchQueue.main.async { focused = true }
        }
    }
}

/// The genetic code small (the pin's): 4 × 4 blocks, first base down the side, second across the top, third within a
/// block; each codon with its amino acid's three letters on its class's tint. A press picks the codon.
struct GXCodonMiniGrid: View {
    let lit: Set<String>
    var dna = false
    let pick: (String) -> Void

    var body: some View {
        Grid(horizontalSpacing: 4, verticalSpacing: 4) {
            GridRow {
                Text("").frame(width: 12)
                ForEach(0..<4, id: \.self) { second in
                    Text(String(GXCodonTable.letters(String(BioCode.bases[second]), dna: dna)))
                        .font(.system(size: 11, weight: .bold, design: .monospaced))
                        .foregroundColor(BioColor.base(Character(GXCodonTable.letters(String(BioCode.bases[second]), dna: dna))))
                        .frame(maxWidth: .infinity)
                }
            }
            ForEach(0..<4, id: \.self) { first in
                GridRow {
                    Text(GXCodonTable.letters(String(BioCode.bases[first]), dna: dna))
                        .font(.system(size: 11, weight: .bold, design: .monospaced))
                        .foregroundColor(BioColor.base(Character(GXCodonTable.letters(String(BioCode.bases[first]), dna: dna))))
                        .frame(width: 12)
                    ForEach(0..<4, id: \.self) { second in block(first, second) }
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("The genetic code")
    }

    private func block(_ first: Int, _ second: Int) -> some View {
        VStack(spacing: 1) {
            ForEach(0..<4, id: \.self) { third in
                let c = GXCodonTable.codon(first: first, second: second, third: third)
                let aa = BioCode.aminoAcid(c) ?? "?"
                let on = lit.contains(c)
                let tint = aa == "*" ? GXColors.stop.color : BioColor.aminoAcid(aa)
                Button { pick(c) } label: {
                    HStack(spacing: 3) {
                        Text(GXCodonTable.letters(c, dna: dna)).font(.system(size: 10.5, weight: .semibold, design: .monospaced))
                        Spacer(minLength: 0)
                        Text(aa == "*" ? "Stop" : (BioAminoAcids.byOne[aa]?.three ?? "?"))
                            .font(.system(size: 10.5, weight: c == "AUG" || aa == "*" ? .bold : .regular))
                            .foregroundStyle(aa == "*" ? GXColors.stop.color : Color.primary)
                    }
                    .padding(.horizontal, 4)
                    .frame(height: 15)
                    .background(RoundedRectangle(cornerRadius: 3, style: .continuous).fill(tint.opacity(on ? 0.4 : 0.13)))
                    .overlay(RoundedRectangle(cornerRadius: 3, style: .continuous).strokeBorder(on ? Theme.accent : (c == "AUG" ? GXColors.start.color.opacity(0.7) : .clear), lineWidth: on ? 1.5 : 1))
                    .opacity(lit.isEmpty || on ? 1 : 0.45)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("\(GXCodonTable.letters(c, dna: dna)) → \(aa == "*" ? "stop" : (BioAminoAcids.byOne[aa]?.name ?? "?"))")
                .accessibilityLabel("\(GXCodonTable.letters(c, dna: dna).map(String.init).joined(separator: " ")): \(aa == "*" ? "stop" : (BioAminoAcids.byOne[aa]?.name ?? "?"))")
            }
        }
        .frame(maxWidth: .infinity)
    }
}
