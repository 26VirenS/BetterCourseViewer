import Foundation

// The shared sequence model (spec §3.7), used by DNA & RNA, Gene Expression and the Biomolecule Builder: a typed
// sequence cleaned the same way everywhere (whitespace, digits and a FASTA header dropped; U in DNA or T in RNA
// offered as a switch, never converted silently; other letters refused with their positions), and the operations the
// tools show — complement, reverse complement, transcription, translation in any frame on either strand, ORFs, GC
// content, mass, the Chargaff check and a melting temperature.

/// What a sequence is.
enum BioSeqKind: String, CaseIterable {
    case dna, rna, protein

    var label: String { self == .dna ? "DNA" : (self == .rna ? "RNA" : "Protein") }

    /// The letters it takes (N for an unknown base; X and * in a protein).
    var alphabet: Set<Character> {
        switch self {
        case .dna: return Set("ACGTN")
        case .rna: return Set("ACGUN")
        case .protein: return Set("ACDEFGHIKLMNPQRSTVWYX*")
        }
    }
}

/// A sequence as typed or pasted, cleaned (§3.7): the letters kept, what was wrong with them, and what to say.
struct BioSequenceInput: Equatable {
    let kind: BioSeqKind
    /// What was typed, as typed.
    let original: String
    /// The sequence's letters, uppercased, without whitespace, digits or a FASTA header — bad letters included, so
    /// their positions are where they stand.
    let letters: String
    /// Each letter that does not belong, with its 1-based positions in `letters`.
    let invalid: [(letter: Character, positions: [Int])]
    /// The kind it looks like instead: RNA when DNA has U (and no T), DNA when RNA has T (and no U).
    let suggestion: BioSeqKind?
    /// Cut to the tool's cap.
    let truncated: Bool
    let cap: Int?

    static func == (a: BioSequenceInput, b: BioSequenceInput) -> Bool {
        a.kind == b.kind && a.original == b.original && a.letters == b.letters && a.truncated == b.truncated
    }

    /// The sequence when every letter belongs (nil otherwise).
    var sequence: String? { invalid.isEmpty && !letters.isEmpty ? letters : nil }
    var isValid: Bool { invalid.isEmpty }

    /// The inline message: the letters that do not belong and where they are, or the switch to offer, or the cap.
    var message: String? {
        if let s = suggestion { return "This looks like \(s.label). Switch?" }
        if !invalid.isEmpty {
            let what = invalid.map { item -> String in
                let at = item.positions.prefix(6).map(String.init).joined(separator: ", ") + (item.positions.count > 6 ? "…" : "")
                return "“\(item.letter)” at \(at)"
            }.joined(separator: "; ")
            return "\(kind.label) uses only \(BioSequenceInput.alphabetText(kind)). Not allowed: \(what)."
        }
        if truncated, let cap { return "Only the first \(cap) letters are used." }
        return nil
    }

    static func alphabetText(_ kind: BioSeqKind) -> String {
        switch kind {
        case .dna: return "A, C, G, T and N"
        case .rna: return "A, C, G, U and N"
        case .protein: return "the 20 amino-acid letters, X and *"
        }
    }

    /// The text cleaned as `kind`, kept to `cap` letters if one is given.
    init(_ text: String, as kind: BioSeqKind, cap: Int? = nil) {
        self.kind = kind
        original = text
        self.cap = cap
        var lines = text.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        // (a FASTA header line, ">name …", dropped)
        if let first = lines.first, first.trimmingCharacters(in: .whitespaces).hasPrefix(">") { lines.removeFirst() }
        var kept = String(lines.joined().uppercased().filter { !$0.isWhitespace && !$0.isNumber })
        // (a pasted 5′- and -3′, or 5'- and -3', taken off its ends)
        for prefix in ["5′-", "5'-", "5′", "5'"] where kept.hasPrefix(prefix) { kept.removeFirst(prefix.count); break }
        for suffix in ["-3′", "-3'", "3′", "3'"] where kept.hasSuffix(suffix) { kept.removeLast(suffix.count); break }
        var cut = false
        if let cap, kept.count > cap {
            kept = String(kept.prefix(cap))
            cut = true
        }
        letters = kept
        truncated = cut
        var bad: [Character: [Int]] = [:]
        var order: [Character] = []
        for (i, c) in kept.enumerated() where !kind.alphabet.contains(c) {
            if bad[c] == nil { order.append(c) }
            bad[c, default: []].append(i + 1)
        }
        invalid = order.map { ($0, bad[$0] ?? []) }
        if kind == .dna, kept.contains("U"), !kept.contains("T"), kept.allSatisfy({ BioSeqKind.rna.alphabet.contains($0) }) {
            suggestion = .rna
        } else if kind == .rna, kept.contains("T"), !kept.contains("U"), kept.allSatisfy({ BioSeqKind.dna.alphabet.contains($0) }) {
            suggestion = .dna
        } else {
            suggestion = nil
        }
    }
}

/// An open reading frame (§4.3): an AUG and the codons after it in the same frame up to and including the first stop.
struct BioORF: Equatable {
    /// +1, +2, +3 on the forward strand; −1, −2, −3 on the reverse complement.
    let frame: Int
    /// 1-based on the forward strand: the first base of the start codon and the last of the stop (or of the last whole
    /// codon when there is no stop). On the reverse strand start > end, as ORFfinder writes them.
    let start: Int
    let end: Int
    /// Codons including the stop.
    let codons: Int
    /// The protein, N→C, without the stop.
    let protein: String
    /// False: no stop before the sequence ends ("open").
    let hasStop: Bool

    var frameLabel: String { frame > 0 ? "+\(frame)" : "−\(-frame)" }
}

/// Counts of a nucleic acid's bases.
struct BioBaseCounts: Equatable {
    var a = 0, c = 0, g = 0, t = 0, u = 0, n = 0
    var total: Int { a + c + g + t + u + n }
    /// G + C over the known bases.
    var gcFraction: Double {
        let known = a + c + g + t + u
        return known > 0 ? Double(g + c) / Double(known) : 0
    }
}

/// The operations on nucleic-acid and protein letters.
enum BioSeq {
    /// A base's Watson–Crick partner (A–T or A–U, G–C; N stays N).
    static func pair(_ b: Character, rna: Bool) -> Character {
        switch b {
        case "A": return rna ? "U" : "T"
        case "T", "U": return "A"
        case "G": return "C"
        case "C": return "G"
        default: return b
        }
    }

    /// Each base's partner, in the same order: the strand written 3′→5′ under this one.
    static func complement(_ s: String, rna: Bool = false) -> String { String(s.map { pair($0, rna: rna) }) }

    static func reverse(_ s: String) -> String { String(s.reversed()) }

    /// The other strand read 5′→3′ (not the same as the complement, which runs 3′→5′).
    static func reverseComplement(_ s: String, rna: Bool = false) -> String { String(s.reversed().map { pair($0, rna: rna) }) }

    /// The mRNA a coding (sense) strand gives: the same letters with U for T (transcription copies the template).
    static func transcribe(codingStrand s: String) -> String { s.replacingOccurrences(of: "T", with: "U") }

    /// The template strand under a coding strand, written 3′→5′ left to right.
    static func templateStrand(_ coding: String) -> String { complement(coding) }

    /// RNA letters back to DNA (U → T).
    static func toDNA(_ s: String) -> String { s.replacingOccurrences(of: "U", with: "T") }

    /// The codons of a frame (0, 1 or 2 bases in), whole codons only.
    static func codons(_ s: String, frame: Int = 0) -> [String] {
        let chars = Array(s)
        var out: [String] = []
        var i = max(frame, 0)
        while i + 3 <= chars.count {
            out.append(String(chars[i..<(i + 3)]))
            i += 3
        }
        return out
    }

    /// The amino acids of a frame (0, 1 or 2 bases in) on the forward strand, or on the reverse complement: one letter
    /// each, * for a stop, X for a codon with N, ? for anything else.
    static func translate(_ s: String, frame: Int = 0, reverse: Bool = false) -> String {
        let seq = reverse ? reverseComplement(toDNA(s)) : s
        return String(codons(seq, frame: frame).map { BioCode.aminoAcid($0) ?? "?" })
    }

    /// The protein from the first AUG (or ATG) to the first stop in its frame, without the stop (nil: no AUG).
    static func proteinFromFirstStart(_ s: String) -> String? {
        let rna = s.uppercased().replacingOccurrences(of: "T", with: "U")
        guard let r = rna.range(of: "AUG") else { return nil }
        let from = rna.distance(from: rna.startIndex, to: r.lowerBound)
        var out = ""
        for c in codons(String(rna.dropFirst(from))) {
            let aa = BioCode.aminoAcid(c) ?? "?"
            if aa == "*" { break }
            out.append(aa)
        }
        return out
    }

    /// Every ORF of at least `minCodons` codons (stop included; an open one counts its codons) on the forward frames and,
    /// if asked, the reverse ones — longest first. Each stop ends one ORF, begun at the first AUG after the previous
    /// stop in its frame (ORFfinder's way).
    static func orfs(_ s: String, minCodons: Int = 10, reverseStrand: Bool = true) -> [BioORF] {
        let n = s.count
        var out: [BioORF] = []
        func scan(_ seq: String, reverse: Bool) {
            for f in 0..<3 {
                let cs = codons(seq, frame: f)
                var start: Int?
                var protein = ""
                func emit(lastCodon k: Int, stop: Bool) {
                    guard let st = start else { return }
                    let count = k - st + 1
                    guard count >= minCodons else { return }
                    // (positions on the scanned strand, 1-based, then back to the forward strand)
                    let a = f + st * 3 + 1, b = f + k * 3 + 3
                    let (s1, e1) = reverse ? (n - a + 1, n - b + 1) : (a, b)
                    out.append(BioORF(frame: reverse ? -(f + 1) : f + 1, start: s1, end: e1, codons: count, protein: protein, hasStop: stop))
                }
                for (k, c) in cs.enumerated() {
                    let aa = BioCode.aminoAcid(c) ?? "?"
                    if start == nil {
                        if isStartCodon(c) {
                            start = k
                            protein = "M"
                        }
                        continue
                    }
                    if aa == "*" {
                        emit(lastCodon: k, stop: true)
                        start = nil
                        protein = ""
                    } else {
                        protein.append(aa)
                    }
                }
                if start != nil { emit(lastCodon: cs.count - 1, stop: false) }
            }
        }
        let fwd = s.uppercased()
        scan(fwd, reverse: false)
        if reverseStrand { scan(reverseComplement(toDNA(fwd)), reverse: true) }
        return out.sorted { $0.codons != $1.codons ? $0.codons > $1.codons : (abs($0.frame) != abs($1.frame) ? $0.frame > $1.frame : $0.start < $1.start) }
    }

    private static func isStartCodon(_ c: String) -> Bool { c == "AUG" || c == "ATG" }

    static func counts(_ s: String) -> BioBaseCounts {
        var k = BioBaseCounts()
        for c in s {
            switch c {
            case "A": k.a += 1
            case "C": k.c += 1
            case "G": k.g += 1
            case "T": k.t += 1
            case "U": k.u += 1
            default: k.n += 1
            }
        }
        return k
    }

    /// GC content, 0…1.
    static func gcContent(_ s: String) -> Double { counts(s).gcFraction }

    /// Nucleoside monophosphate masses (g/mol): the DNA ones of §4.2, the RNA ones of §6.6.
    static let monophosphateMass: [Character: Double] = ["A": 331.22, "C": 307.20, "G": 347.22, "T": 322.21]
    static let rnaMonophosphateMass: [Character: Double] = ["A": 347.22, "C": 323.20, "G": 363.22, "U": 324.18]

    /// One strand's molar mass (g/mol): the monophosphates' masses minus 18.02 for each phosphodiester bond (an N
    /// counted as the four's mean). "About 650 g/mol per base pair" is the quick lab estimate, for the sodium salt.
    static func mass(_ s: String, rna: Bool = false) -> Double {
        let table = rna ? rnaMonophosphateMass : monophosphateMass
        let mean = table.values.reduce(0, +) / Double(table.count)
        let sum = s.reduce(0.0) { $0 + (table[$1] ?? mean) }
        return s.isEmpty ? 0 : sum - 18.02 * Double(s.count - 1)
    }

    /// A rough melting temperature for a short DNA primer in 50 mM salt (°C): Wallace's 2·(A+T) + 4·(G+C) for 14 nt
    /// or fewer, else 64.9 + 41·(G + C − 16.4)/N.
    static func meltingTemperature(_ s: String) -> Double {
        let k = counts(s)
        let n = Double(k.a + k.c + k.g + k.t)
        guard n > 0 else { return 0 }
        if n <= 14 { return Double(2 * (k.a + k.t) + 4 * (k.g + k.c)) }
        return 64.9 + 41 * (Double(k.g + k.c) - 16.4) / n
    }

    /// The Chargaff check on double-stranded DNA made from this strand and its complement: A = T and G = C, so
    /// %A + %G = 50%. Returns the duplex's counts and %A + %G.
    static func chargaff(_ s: String) -> (counts: BioBaseCounts, purinePercent: Double) {
        let one = counts(s), two = counts(complement(s))
        var both = BioBaseCounts()
        both.a = one.a + two.a
        both.c = one.c + two.c
        both.g = one.g + two.g
        both.t = one.t + two.t
        both.n = one.n + two.n
        let known = both.a + both.c + both.g + both.t
        return (both, known > 0 ? Double(both.a + both.g) / Double(known) * 100 : 0)
    }

    /// The sequence in groups of `size` (10) with the 1-based position of each line's first letter: the display of
    /// §3.7, `perLine` groups to a line.
    static func lines(_ s: String, size: Int = 10, perLine: Int = 6) -> [(start: Int, groups: [String])] {
        let chars = Array(s)
        var out: [(Int, [String])] = []
        var i = 0
        while i < chars.count {
            var groups: [String] = []
            let lineStart = i
            for _ in 0..<perLine where i < chars.count {
                let j = min(i + size, chars.count)
                groups.append(String(chars[i..<j]))
                i = j
            }
            out.append((lineStart + 1, groups))
        }
        return out
    }
}
