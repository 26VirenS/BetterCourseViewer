import Foundation

// Gene Expression's own model (spec §4.3, §6.1, §6.12): the genes it works on — the made-up teaching gene, human
// β-globin's coding sequence and a sequence of one's own — with what they are transcribed and translated into; the
// codon table's grid and wheel and its lookup both ways; and the reading frames. The step-through's steps and scenes
// are in GeneExpressionProcess.swift and the mutation classifier in GeneExpressionMutations.swift. Foundation only, so
// the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of Biology/Model; the
// views are Biology/GeneExpression*.swift.

// MARK: - Genes

/// What kind of cell the step-through shows (§4.3 Cell type).
enum GXCell: String, CaseIterable, Identifiable {
    /// A nucleus, RNA processing (cap, tail, splicing), export, and a ribosome that scans from the cap.
    case eukaryote
    /// No nucleus and no introns; the ribosome binds the Shine–Dalgarno site and can start before transcription ends.
    case prokaryote

    var id: String { rawValue }
    var label: String { self == .eukaryote ? "Eukaryote" : "Prokaryote" }
}

/// Which gene the tool works on (§4.3 Gene presets).
enum GXSource: String, CaseIterable, Identifiable {
    case teaching, hbb, own

    var id: String { rawValue }
    var label: String {
        switch self {
        case .teaching: return "Teaching gene"
        case .hbb: return "Human β-globin (HBB)"
        case .own: return "Type your own"
        }
    }
}

/// A stretch of a gene's coding strand: a band (promoter, UTR, exon, intron, coding sequence) or a boxed mark (TATA box,
/// start and stop codons, poly-A signal). Columns are 0-based and inclusive.
struct GXRegion: Equatable {
    enum Kind: String { case promoter, utr5, exon, intron, utr3, cds, transcribed, tata, start, stop, polyA }
    let kind: Kind
    let name: String
    let range: ClosedRange<Int>
}

/// A codon of the reading frame, from the start codon on.
struct GXCodon: Equatable {
    /// 0 for the start codon.
    let index: Int
    /// Its first base's index in the mature mRNA.
    let mature: Int
    /// RNA letters, 5′→3′.
    let codon: String
    /// One letter; "*" for a stop, "X" for a codon with N.
    let aa: Character
}

/// A gene as the step-through draws it: its coding strand by columns, its parts, and what it is transcribed and
/// translated into. A column is a base pair's place left to right; the RNA made from it is drawn under it.
struct GXGene: Equatable {
    let source: GXSource
    let cell: GXCell
    let title: String
    /// The gene in a sentence (the teaching gene says it is made up).
    let note: String
    /// The coding (sense) strand, 5′→3′, DNA letters.
    let coding: [Character]
    /// Bands: promoter, UTRs, exons, introns, coding sequence.
    let regions: [GXRegion]
    /// Boxes: TATA box, start codon, stop codon, poly-A signal.
    let marks: [GXRegion]
    /// The first column transcribed (+1) and the last.
    let txStart: Int
    let txEnd: Int
    /// Introns, in columns.
    let introns: [ClosedRange<Int>]

    /// The template strand, written 3′→5′ under the coding strand.
    let template: [Character]
    /// The RNA made from columns txStart…txEnd, 5′→3′, U for T.
    let preMRNA: [Character]
    /// The mature mRNA: introns out (cap and tail not counted).
    let mature: [Character]
    /// Each mature base's index in the pre-mRNA.
    let matureToPre: [Int]
    /// The first AUG of the mature mRNA, where the ribosome starts (nil: none).
    let cdsStart: Int?
    /// The reading frame's codons, from the AUG to the stop (included) or the last whole codon.
    let codons: [GXCodon]
    /// The protein, N→C, without the stop.
    let protein: String
    /// The frame ends in a stop codon (false: it runs to the end of the mRNA).
    let hasStop: Bool

    init(source: GXSource, cell: GXCell, title: String, note: String, coding: [Character], regions: [GXRegion],
         marks: [GXRegion], txStart: Int, txEnd: Int, introns: [ClosedRange<Int>]) {
        self.source = source
        self.cell = cell
        self.title = title
        self.note = note
        self.coding = coding
        self.regions = regions
        self.marks = marks
        self.txStart = txStart
        self.txEnd = max(txStart, txEnd)
        self.introns = introns
        template = coding.map { BioSeq.pair($0, rna: false) }
        let pre: [Character] = coding.isEmpty ? [] : (txStart...self.txEnd).map { coding[$0] == "T" ? "U" : coding[$0] }
        preMRNA = pre
        var keep: [Int] = []
        for i in pre.indices where !introns.contains(where: { $0.contains(txStart + i) }) { keep.append(i) }
        matureToPre = keep
        let m = keep.map { pre[$0] }
        mature = m
        var start: Int?
        if m.count >= 3 {
            for i in 0...(m.count - 3) where m[i] == "A" && m[i + 1] == "U" && m[i + 2] == "G" {
                start = i
                break
            }
        }
        cdsStart = start
        var cs: [GXCodon] = []
        var stop = false
        if let s = start {
            var at = s
            while at + 3 <= m.count {
                let c = String(m[at..<(at + 3)])
                let aa = BioCode.aminoAcid(c) ?? "X"
                cs.append(GXCodon(index: cs.count, mature: at, codon: c, aa: aa))
                if aa == "*" {
                    stop = true
                    break
                }
                at += 3
            }
        }
        codons = cs
        protein = String(cs.filter { $0.aa != "*" }.map(\.aa))
        hasStop = stop
    }

    static func == (a: GXGene, b: GXGene) -> Bool {
        a.source == b.source && a.cell == b.cell && a.coding == b.coding && a.txStart == b.txStart && a.txEnd == b.txEnd && a.introns == b.introns
    }

    /// The gene a source names, as the cell has it (`own`: the cleaned sequence, DNA letters).
    static func make(_ source: GXSource, cell: GXCell, own: String = "") -> GXGene? {
        switch source {
        case .teaching: return teaching(cell)
        case .hbb: return hbb(cell)
        case .own: return GXGene.own(own, cell: cell)
        }
    }

    /// §6.12's teaching gene: in a eukaryote as written (promoter with TATA box, two exons, an intron, a poly-A signal);
    /// in a prokaryote with its intron left out of the DNA (bacterial genes have none) and its exons one coding sequence.
    static func teaching(_ cell: GXCell) -> GXGene {
        let g = BioGenes.teaching
        let all = Array(g.coding)
        func cols(_ p: BioGenes.Part) -> ClosedRange<Int> { (p.start - 1)...(p.end - 1) }
        let kinds: [String: GXRegion.Kind] = ["Promoter": .promoter, "5′ UTR": .utr5, "Exon 1": .exon, "Intron": .intron, "Exon 2": .exon, "3′ UTR": .utr3]
        let parts = g.parts.map { GXRegion(kind: kinds[$0.name] ?? .transcribed, name: $0.name, range: cols($0)) }
        let introns = parts.filter { $0.kind == .intron }.map(\.range)
        let start = GXRegion(kind: .start, name: "Start codon", range: cols(g.startCodon))
        let stop = GXRegion(kind: .stop, name: "Stop codon", range: cols(g.stopCodon))
        if cell == .eukaryote {
            return GXGene(source: .teaching, cell: cell, title: "Teaching gene",
                          note: "Made up for this tool: a promoter with a TATA box, two exons, one intron and a poly-A signal.",
                          coding: all, regions: parts,
                          marks: [GXRegion(kind: .tata, name: "TATA box", range: cols(g.tata)), start, stop,
                                  GXRegion(kind: .polyA, name: "Poly-A signal", range: cols(g.polyASignal))],
                          txStart: g.startSite - 1, txEnd: all.count - 1, introns: introns)
        }
        // (as a bacterium would have it: the intron out of the DNA itself, the exons one coding sequence)
        func shift(_ c: Int) -> Int { c - introns.filter { $0.upperBound < c }.reduce(0) { $0 + $1.count } }
        func shift(_ r: ClosedRange<Int>) -> ClosedRange<Int> { shift(r.lowerBound)...shift(r.upperBound) }
        let coding = all.indices.filter { i in !introns.contains { $0.contains(i) } }.map { all[$0] }
        let exons = parts.filter { $0.kind == .exon }
        var regions = parts.filter { $0.kind == .promoter || $0.kind == .utr5 }
        if let first = exons.first, let last = exons.last {
            regions.append(GXRegion(kind: .cds, name: "Coding sequence", range: shift(first.range.lowerBound)...shift(last.range.upperBound)))
        }
        regions += parts.filter { $0.kind == .utr3 }.map { GXRegion(kind: $0.kind, name: $0.name, range: shift($0.range)) }
        return GXGene(source: .teaching, cell: cell, title: "Teaching gene",
                      note: "Made up for this tool, shown as a bacterial gene: its intron is left out (bacterial genes have none).",
                      coding: coding, regions: regions,
                      marks: [GXRegion(kind: .start, name: start.name, range: shift(start.range)),
                              GXRegion(kind: .stop, name: stop.name, range: shift(stop.range))],
                      txStart: g.startSite - 1, txEnd: coding.count - 1, introns: [])
    }

    /// Human β-globin's coding sequence (§6.12): the real gene's 3 exons joined, without its promoter, UTRs or introns.
    static func hbb(_ cell: GXCell) -> GXGene {
        let c = Array(BioGenes.hbbCDS)
        let n = c.count
        return GXGene(source: .hbb, cell: cell, title: "Human β-globin (HBB)",
                      note: "Human β-globin's coding sequence (NM_000518.5), its 3 exons joined; the promoter, UTRs and 2 introns are not shown.",
                      coding: c, regions: [GXRegion(kind: .cds, name: "Coding sequence (3 exons joined)", range: 0...(n - 1))],
                      marks: [GXRegion(kind: .start, name: "Start codon", range: 0...2), GXRegion(kind: .stop, name: "Stop codon", range: (n - 3)...(n - 1))],
                      txStart: 0, txEnd: n - 1, introns: [])
    }

    /// A sequence of one's own (already cleaned, DNA letters), read as the coding strand and transcribed whole; its coding
    /// sequence runs from its first ATG to the first stop in that frame. Nil when it is empty.
    static func own(_ letters: String, cell: GXCell) -> GXGene? {
        let c = Array(BioSeq.toDNA(letters.uppercased()))
        let n = c.count
        guard n > 0 else { return nil }
        var start: Int?
        if n >= 3 {
            for i in 0...(n - 3) where c[i] == "A" && c[i + 1] == "T" && c[i + 2] == "G" {
                start = i
                break
            }
        }
        var regions: [GXRegion] = []
        var marks: [GXRegion] = []
        if let s = start {
            var end: Int?
            var j = s
            while j + 3 <= n {
                if BioCode.isStop(String(c[j..<(j + 3)])) {
                    end = j + 2
                    break
                }
                j += 3
            }
            let last = end ?? (s + (n - s) / 3 * 3 - 1)
            if s > 0 { regions.append(GXRegion(kind: .utr5, name: "5′ UTR", range: 0...(s - 1))) }
            regions.append(GXRegion(kind: .cds, name: "Coding sequence", range: s...max(s, last)))
            if last < n - 1 { regions.append(GXRegion(kind: .utr3, name: "3′ UTR", range: (last + 1)...(n - 1))) }
            marks.append(GXRegion(kind: .start, name: "Start codon", range: s...(s + 2)))
            if let e = end { marks.append(GXRegion(kind: .stop, name: "Stop codon", range: (e - 2)...e)) }
        } else {
            regions.append(GXRegion(kind: .transcribed, name: "Transcribed", range: 0...(n - 1)))
        }
        return GXGene(source: .own, cell: cell, title: "Your sequence",
                      note: "Your sequence, read as the coding strand and transcribed whole; translation starts at its first AUG.",
                      coding: c, regions: regions, marks: marks, txStart: 0, txEnd: n - 1, introns: [])
    }

    // MARK: Where things are

    /// The coding sequence in DNA letters, from the start codon to the stop (the Mutations page's starting point).
    var cdsDNA: String { BioSeq.toDNA(String(codons.map(\.codon).joined())) }

    /// The mRNA after the coding sequence's stop (its 3′ UTR), DNA letters — where a stop-loss reads on into.
    var downstreamDNA: String {
        guard let s = cdsStart, hasStop else { return "" }
        let from = s + codons.count * 3
        return from < mature.count ? BioSeq.toDNA(String(mature[from...])) : ""
    }

    /// The column a pre-mRNA base is drawn at, `splice` of the way through splicing: bases after an intron close up
    /// leftward by its length (an intron's own bases stay where they were, to loop away).
    func column(pre i: Int, splice s: Double = 0) -> Double {
        let col = txStart + i
        let before = introns.filter { $0.upperBound < col }.reduce(0) { $0 + $1.count }
        return Double(col) - s * Double(before)
    }

    /// The column of a mature-mRNA base once spliced (the mature mRNA lies contiguous from +1).
    func column(mature m: Double) -> Double { Double(txStart) + m }

    /// The column of a codon's middle base on the mature mRNA (codon 0 = the start codon).
    func codonColumn(_ j: Double) -> Double { column(mature: Double(cdsStart ?? 0) + 3 * j + 1) }

    /// Whether a pre-mRNA base is in an intron.
    func inIntron(pre i: Int) -> Bool { introns.contains { $0.contains(txStart + i) } }

    /// The columns the scene spans: from the cap's place left of +1 to the end of the poly-A tail (20 drawn).
    var columnSpan: ClosedRange<Double> {
        let left = min(0, Double(txStart) - 2)
        let right = max(Double(coding.count - 1), column(pre: preMRNA.count - 1) + Double(GXProcess.tailDrawn) + 1)
        return left...right
    }

    /// The mature mRNA's base that starts a Kozak context GCCACCAUGG around the start codon, if it has one.
    var kozak: Int? {
        guard let s = cdsStart, s >= 6, s + 4 <= mature.count, String(mature[(s - 6)..<(s + 4)]) == "GCCACCAUGG" else { return nil }
        return s - 6
    }

    /// Exon junctions in the mature mRNA (the index of exon 2's first base, and so on).
    var junctions: [Int] {
        var out: [Int] = []
        for intron in introns {
            // (the mature bases before the intron)
            let before = matureToPre.filter { txStart + $0 < intron.lowerBound }.count
            if before > 0 && before < mature.count { out.append(before) }
        }
        return out
    }

    /// A codon split across an exon junction: its index, its bases and where the junction falls in it (1 or 2).
    var splitCodon: (index: Int, codon: String, cut: Int)? {
        guard let s = cdsStart else { return nil }
        for j in junctions where j > s {
            let into = (j - s) % 3
            let k = (j - s) / 3
            if into != 0, k < codons.count { return (k, codons[k].codon, into) }
        }
        return nil
    }
}

// MARK: - The codon table (§6.1)

/// The codon table's grid and wheel, and its lookup (§4.3 Codon table).
enum GXCodonTable {
    /// The codon in a block of the grid: rows are the first base (U, C, A, G top to bottom), columns the second, and the
    /// third base runs U, C, A, G down inside each block.
    static func codon(first: Int, second: Int, third: Int) -> String {
        String([BioCode.bases[first], BioCode.bases[second], BioCode.bases[third]])
    }

    /// The grid as §6.1 lays it out: 16 rows (4 per first base, third base within), 4 columns (second base).
    static var gridRows: [[String]] {
        (0..<4).flatMap { first in (0..<4).map { third in (0..<4).map { second in codon(first: first, second: second, third: third) } } }
    }

    /// RNA letters, or DNA coding-strand letters (T for U) when the table is switched to them.
    static func letters(_ s: String, dna: Bool) -> String { dna ? BioSeq.toDNA(s) : s }

    /// The wheel's ring radii as fractions of its radius (§4.3): first base between 0.18 and 0.36, second to 0.56, third
    /// to 0.78; the amino acids' labels at 0.90.
    static let ringRadii: [Double] = [0.18, 0.36, 0.56, 0.78]
    static let labelRadius = 0.90

    /// A sector of the wheel: angles in degrees clockwise from 12 o'clock.
    struct Sector: Equatable {
        /// 0 first base, 1 second, 2 third.
        let ring: Int
        /// The codon's first one, two or three bases.
        let prefix: String
        let from: Double
        let to: Double
        var mid: Double { (from + to) / 2 }
    }

    /// The sectors of a ring, in U, C, A, G order clockwise from 12 o'clock: 4 of 90°, 16 of 22.5°, 64 of 5.625°.
    static func sectors(ring: Int) -> [Sector] {
        let count = [4, 16, 64][min(max(ring, 0), 2)]
        let width = 360 / Double(count)
        return (0..<count).map { k in
            var prefix = ""
            var rest = k
            for level in stride(from: ring, through: 0, by: -1) {
                let p = Int(pow(4, Double(level)))
                prefix.append(BioCode.bases[rest / p])
                rest %= p
            }
            return Sector(ring: ring, prefix: prefix, from: Double(k) * width, to: Double(k + 1) * width)
        }
    }

    /// The amino acids round the wheel: runs of the same amino acid within each second-base block, with their angles.
    static var wheelLabels: [(aa: Character, codons: [String], from: Double, to: Double)] {
        var out: [(aa: Character, codons: [String], from: Double, to: Double)] = []
        for s in sectors(ring: 2) {
            let aa = BioCode.aminoAcid(s.prefix) ?? "?"
            if let last = out.last, last.aa == aa, last.codons.last.map({ $0.prefix(2) == s.prefix.prefix(2) }) == true {
                out[out.count - 1] = (aa, last.codons + [s.prefix], last.from, s.to)
            } else {
                out.append((aa, [s.prefix], s.from, s.to))
            }
        }
        return out
    }

    /// The codon (or the first one or two bases) at a point of the wheel: `r` as a fraction of the radius, `angle` in
    /// degrees clockwise from 12 o'clock. Past the third ring (the amino acids' band) it is the codon under it.
    static func hit(r: Double, angle: Double) -> String? {
        let a = (angle.truncatingRemainder(dividingBy: 360) + 360).truncatingRemainder(dividingBy: 360)
        let rings = ringRadii
        guard r >= rings[0], r <= 1 else { return nil }
        let ring = r < rings[1] ? 0 : (r < rings[2] ? 1 : 2)
        return sectors(ring: ring).first { a >= $0.from && a < $0.to }?.prefix
    }

    /// What a lookup asks for: a codon (DNA or RNA letters), an amino acid (name, three or one letter), the stops, or
    /// nothing it can read.
    enum Query: Equatable {
        case codon(String)
        case aminoAcid(Character)
        case stop
        case empty
        case invalid(String)
    }

    /// Reads the lookup field: three bases are a codon ("AUG", "atg"); "stop", "*" or "ter" the stops; anything else an
    /// amino acid by name ("lysine"), three letters ("Lys") or one letter ("K").
    static func parse(_ text: String) -> Query {
        let q = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty else { return .empty }
        let up = q.uppercased().filter { !$0.isWhitespace && $0 != "-" }
        let bare = up.replacingOccurrences(of: "5′", with: "").replacingOccurrences(of: "3′", with: "")
            .replacingOccurrences(of: "5'", with: "").replacingOccurrences(of: "3'", with: "")
        if bare.count == 3, bare.allSatisfy({ "ACGTU".contains($0) }) {
            if bare.contains("T") && bare.contains("U") { return .invalid("A codon has U (RNA) or T (DNA), not both.") }
            return .codon(bare.replacingOccurrences(of: "T", with: "U"))
        }
        if ["STOP", "*", "TER", "END"].contains(bare) { return .stop }
        if let aa = BioAminoAcids.find(q) { return .aminoAcid(aa.one) }
        if bare.count < 3, bare.allSatisfy({ "ACGTU".contains($0) }) { return .invalid("A codon is three bases.") }
        return .invalid("No codon or amino acid “\(q)”.")
    }

    /// The codons a lookup lights (RNA letters).
    static func matches(_ q: Query) -> Set<String> {
        switch q {
        case .codon(let c): return [c]
        case .aminoAcid(let a): return Set(BioCode.codons(for: a))
        case .stop: return Set(BioCode.codons(for: "*"))
        default: return []
        }
    }

    /// The class words a mutation card and a codon card use ("acidic, charged").
    static func classWords(_ aa: BioAminoAcid) -> String {
        switch aa.group {
        case .nonpolar: return "nonpolar"
        case .polar: return "polar, uncharged"
        case .acidic: return "acidic, charged"
        case .basic: return aa.one == "H" ? "basic, mostly uncharged at pH 7.4" : "basic, charged"
        }
    }
}

// MARK: - Reading frames (§4.3)

/// An amino acid of a reading frame, under the codon it comes from.
struct GXFrameCell: Equatable {
    /// One letter; "*" a stop.
    let aa: Character
    /// The codon as read, 5′→3′ on its own strand (RNA letters).
    let codon: String
    /// The forward strand's first column the codon covers (0-based; a reverse frame's codon covers the same three
    /// columns, read the other way).
    let column: Int
    /// The codon's number in its frame (1-based).
    let number: Int
}

/// A frame: +1, +2, +3 on the strand as typed; −1, −2, −3 on its reverse complement.
struct GXFrameRow: Equatable {
    let frame: Int
    let cells: [GXFrameCell]
    var label: String { frame > 0 ? "+\(frame)" : "−\(-frame)" }
}

enum GXReadingFrames {
    /// The three forward frames and, for DNA, the three reverse ones, each amino acid placed under its codon's columns.
    static func rows(_ sequence: String, reverse: Bool) -> [GXFrameRow] {
        let s = Array(sequence.uppercased())
        let n = s.count
        var out: [GXFrameRow] = []
        func rna(_ c: [Character]) -> String { String(c).replacingOccurrences(of: "T", with: "U") }
        for f in 0..<3 {
            var cells: [GXFrameCell] = []
            var i = f
            while i + 3 <= n {
                let c = rna(Array(s[i..<(i + 3)]))
                cells.append(GXFrameCell(aa: BioCode.aminoAcid(c) ?? "?", codon: c, column: i, number: cells.count + 1))
                i += 3
            }
            out.append(GXFrameRow(frame: f + 1, cells: cells))
        }
        guard reverse else { return out }
        let rc = Array(BioSeq.reverseComplement(BioSeq.toDNA(String(s))))
        for f in 0..<3 {
            var cells: [GXFrameCell] = []
            var i = f
            while i + 3 <= n {
                let c = rna(Array(rc[i..<(i + 3)]))
                // (reverse-complement positions i…i+2 are forward columns n−3−i…n−1−i)
                cells.append(GXFrameCell(aa: BioCode.aminoAcid(c) ?? "?", codon: c, column: n - 3 - i, number: cells.count + 1))
                i += 3
            }
            out.append(GXFrameRow(frame: -(f + 1), cells: cells))
        }
        return out
    }

    /// A reading-frame preset: its name, its sequence and whether it is DNA.
    struct Preset: Equatable {
        let name: String
        let sequence: String
        let dna: Bool
    }

    /// The presets: the teaching gene's mature mRNA (its ORF in frame +2), its whole gene as DNA, and HBB's coding sequence.
    static var presets: [Preset] {
        [Preset(name: "Teaching gene mRNA", sequence: BioGenes.teaching.matureMRNA, dna: false),
         Preset(name: "Teaching gene DNA (112 bp)", sequence: BioGenes.teaching.coding, dna: true),
         Preset(name: "β-globin coding sequence", sequence: BioGenes.hbbCDS, dna: true)]
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Gene Expression's own `bio-check` lines (§10.1 item 10, §10.2): the genes as the step-through builds them, the
    /// steps' ends, the codon table's grid, wheel and lookup, the reading frames and the mutation classifier against
    /// §6.12's table and the sickle-cell mutation.
    static func geneChecks(_ r: inout BioCheckReport) {
        geneGeneChecks(&r)
        geneProcessChecks(&r)
        geneCodonChecks(&r)
        geneFrameChecks(&r)
        geneMutationChecks(&r)
    }

    static func geneGeneChecks(_ r: inout BioCheckReport) {
        let g = GXGene.teaching(.eukaryote)
        let t = BioGenes.teaching
        r.check("gx.teaching", "\(g.coding.count)nt,+1@\(g.txStart + 1)",
                String(g.coding) == t.coding && g.txStart == 31 && String(g.preMRNA) == t.preMRNA && String(g.mature) == t.matureMRNA
                && String(g.template) == t.template && g.introns == [55...81])
        r.check("gx.teaching.cds", "AUG@\((g.cdsStart ?? -1) + 1),\(g.protein)",
                g.cdsStart == 13 && g.protein == t.protein && g.hasStop && g.codons.count == 10 && g.cdsDNA == t.cds && g.codons.last?.codon == "UAA")
        r.same("gx.teaching.kozak", g.kozak.map { String(g.mature[$0..<($0 + 10)]) } ?? "none", "GCCACCAUGG")
        let split = g.splitCodon
        r.check("gx.teaching.split-codon", split.map { "\($0.index + 1):\($0.codon)|\($0.cut)" } ?? "none",
                split?.index == 3 && split?.codon == "AGC" && split?.cut == 2 && g.junctions == [24])
        r.check("gx.teaching.columns", "\(g.column(pre: 24, splice: 1))", g.column(pre: 23, splice: 1) == 54 && g.column(pre: 51, splice: 1) == 55
                && g.column(mature: 24) == 55 && g.codonColumn(0) == 45)
        let p = GXGene.teaching(.prokaryote)
        r.check("gx.teaching.prokaryote", "\(p.coding.count)nt,\(p.protein)",
                p.coding.count == 85 && p.introns.isEmpty && String(p.mature) == t.matureMRNA && p.protein == t.protein
                && p.marks.map(\.range) == [44...46, 71...73] && !p.marks.contains { $0.kind == .tata })
        let h = GXGene.hbb(.eukaryote)
        r.check("gx.hbb", "\(h.protein.count)aa", h.protein == BioGenes.hbbProtein && h.hasStop && h.cdsStart == 0 && h.cdsDNA == BioGenes.hbbCDS)
        let o = GXGene.own("ccATGAAATAGgg", cell: .eukaryote)
        let none = GXGene.own("CCCGGG", cell: .eukaryote)
        let open = GXGene.own("AUGGCC", cell: .eukaryote)
        r.check("gx.own", (o?.protein ?? "nil") + "," + (none?.protein ?? "nil") + "," + (open?.protein ?? "nil"),
                o?.protein == "MK" && o?.cdsStart == 2 && o?.marks.map(\.range) == [2...4, 8...10] && o?.regions.map(\.kind) == [.utr5, .cds, .utr3]
                && none?.cdsStart == nil && none?.protein == "" && open?.protein == "MA" && open?.hasStop == false && GXGene.own("", cell: .eukaryote) == nil)
    }

    static func geneCodonChecks(_ r: inout BioCheckReport) {
        // (the grid reads §6.1's table row by row: UUU UCU UAU UGU / UUC UCC UAC UGC / …)
        let rows = GXCodonTable.gridRows
        let tableOrder = rows.map { $0.map { "\($0)=\(BioCode.aminoAcid($0) ?? "?")" }.joined(separator: " ") }
        r.check("gx.codon.grid", "\(rows.count)x\(rows.first?.count ?? 0)",
                rows.count == 16 && rows.allSatisfy { $0.count == 4 } && Set(rows.flatMap { $0 }).count == 64
                && tableOrder[0] == "UUU=F UCU=S UAU=Y UGU=C" && tableOrder[3] == "UUG=L UCG=S UAG=* UGG=W"
                && tableOrder[11] == "AUG=M ACG=T AAG=K AGG=R" && tableOrder[15] == "GUG=V GCG=A GAG=E GGG=G")
        // (the wheel: U C A G clockwise from 12 o'clock in every ring, read from the centre out)
        let third = GXCodonTable.sectors(ring: 2)
        let entries = BioCode.entries.map(\.codon)
        r.check("gx.codon.wheel", "\(GXCodonTable.sectors(ring: 0).map(\.prefix).joined()),\(third.count)",
                GXCodonTable.sectors(ring: 0).map(\.prefix) == ["U", "C", "A", "G"] && GXCodonTable.sectors(ring: 1).count == 16
                && third.map(\.prefix) == entries && abs(third[35].from - 196.875) < 1e-9 && third[35].prefix == "AUG"
                && GXCodonTable.sectors(ring: 1)[4].prefix == "CU" && abs(GXCodonTable.sectors(ring: 1)[4].from - 90) < 1e-9)
        let labels = GXCodonTable.wheelLabels
        let leu = labels.filter { $0.aa == "L" }.map { $0.codons.count }
        r.check("gx.codon.wheel-labels", "\(labels.count)", labels.reduce(0) { $0 + $1.codons.count } == 64 && leu == [2, 4]
                && labels.first.map { $0.aa == "F" && $0.from == 0 && abs($0.to - 11.25) < 1e-9 } == true)
        let hits = [GXCodonTable.hit(r: 0.27, angle: 10), GXCodonTable.hit(r: 0.46, angle: 100), GXCodonTable.hit(r: 0.67, angle: 199), GXCodonTable.hit(r: 0.9, angle: 359.9), GXCodonTable.hit(r: 0.1, angle: 0)]
        r.same("gx.codon.wheel-hit", hits.map { $0 ?? "-" }.joined(separator: ","), "U,CU,AUG,GGG,-")
        // (lookup both ways: a codon to its amino acid, an amino acid to its codons)
        let q = ["AUG", "atg", "UGA", "Lys", "leucine", "W", "stop", "ATU", "XYZ", "AU"].map(GXCodonTable.parse)
        let ok = q[0] == .codon("AUG") && q[1] == .codon("AUG") && q[2] == .codon("UGA") && q[3] == .aminoAcid("K")
            && q[4] == .aminoAcid("L") && q[5] == .aminoAcid("W") && q[6] == .stop
            && { if case .invalid = q[7] { return true } else { return false } }()
            && { if case .invalid = q[8] { return true } else { return false } }()
            && { if case .invalid = q[9] { return true } else { return false } }()
        r.check("gx.codon.lookup", GXCodonTable.matches(q[4]).sorted().joined(separator: "/"),
                ok && GXCodonTable.matches(q[4]).count == 6 && GXCodonTable.matches(q[3]) == ["AAA", "AAG"] && GXCodonTable.matches(q[6]).count == 3)
        r.same("gx.codon.dna-letters", GXCodonTable.letters("AUG", dna: true) + "," + GXCodonTable.letters("UUU", dna: false), "ATG,UUU")
        r.same("gx.anticodon", "3′-\(BioCode.anticodon3to5("AUG"))-5′,5′-\(BioCode.anticodon5to3("AUG"))-3′", "3′-UAC-5′,5′-CAU-3′")
        r.same("gx.class-words", [BioAminoAcids.find("Glu"), BioAminoAcids.find("Val"), BioAminoAcids.find("His")].compactMap { $0 }.map(GXCodonTable.classWords).joined(separator: ";"),
               "acidic, charged;nonpolar;basic, mostly uncharged at pH 7.4")
    }

    static func geneFrameChecks(_ r: inout BioCheckReport) {
        let mrna = BioGenes.teaching.matureMRNA
        let rows = GXReadingFrames.rows(mrna, reverse: false)
        let frames = rows.map { String($0.cells.map(\.aa)) }
        // (frame +2 of ACUCAGCGCCACCAUGG…: CUC AGC GCC ACC AUG GCC … UAA GCA AUA AAG)
        r.check("gx.frames.mRNA", frames[1], rows.count == 3 && frames[1] == "LSATMAFSDWQKG*AIK"
                && rows.allSatisfy { row in row.cells.allSatisfy { $0.column % 3 == (row.frame - 1) } })
        let orfs = BioSeq.orfs(mrna, minCodons: 10, reverseStrand: false)
        r.check("gx.frames.ORF", orfs.first.map { "\($0.frameLabel)@\($0.start)-\($0.end)" } ?? "none",
                orfs.count == 1 && orfs.first?.frame == 2 && orfs.first?.start == 14 && orfs.first?.end == 43 && orfs.first?.protein == "MAFSDWQKG")
        // (six frames on its DNA copy: the reverse frames' codons placed back on forward columns; +2 still the longest)
        let dna = BioSeq.toDNA(mrna)
        let six = GXReadingFrames.rows(dna, reverse: true)
        let rc = BioSeq.reverseComplement(dna)
        let n = dna.count
        let placed = six.filter { $0.frame < 0 }.allSatisfy { row in
            row.cells.allSatisfy { c in
                let fwd = String(Array(dna)[c.column..<(c.column + 3)])
                return BioSeq.reverseComplement(fwd).replacingOccurrences(of: "T", with: "U") == c.codon
            }
        }
        let all = BioSeq.orfs(dna, minCodons: 10, reverseStrand: true)
        r.check("gx.frames.six", "\(six.count),\(all.first.map { $0.frameLabel } ?? "none")",
                six.count == 6 && placed && six[3].cells.first?.column == n - 3 && String(six[3].cells.map(\.aa)) == BioSeq.translate(rc)
                && all.first?.frame == 2 && all.first?.start == 14)
        let small = GXReadingFrames.rows("ATGGCCTAA", reverse: true).map { String($0.cells.map(\.aa)) }
        r.same("gx.frames.small", small.joined(separator: ","), "MA*,WP,GL,LGH,*A,RP")
    }
}
