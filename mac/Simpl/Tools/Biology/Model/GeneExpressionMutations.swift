import Foundation

// Gene Expression's mutations (spec §4.3 Mutations, §6.12, §7 items 16–17): a change to a coding sequence — one base
// substituted, or 1–6 bases inserted or deleted — written the way genetics writes it (c.20A>T, c.6_7insA, c.7del,
// c.13_15del), and what it does to the protein: silent, missense (conservative or not, with the charge change),
// nonsense (where it is cut and how much is kept), stop-loss, frameshift (only for insertions and deletions that are
// not a multiple of 3), in-frame insertion or deletion, and start-loss. The original and the mutant are lined up base
// for base (a gap where a base is missing) so the view can show both, codon boxes and all. Foundation only.

/// A change to a coding sequence. Positions are 0-based on the original (HGVS c. numbers are 1-based from the A of ATG).
enum GXEdit: Equatable {
    case substitute(at: Int, to: Character)
    /// Bases inserted after base `after`.
    case insert(after: Int, bases: String)
    /// Bases `from`…`to` (inclusive) deleted.
    case delete(from: Int, to: Int)

    /// At most this many bases inserted or deleted at once.
    static let maxLength = 6

    /// The sequence changed.
    func apply(to s: [Character]) -> [Character] {
        var out = s
        switch self {
        case .substitute(let at, let to):
            if out.indices.contains(at) { out[at] = to }
        case .insert(let after, let bases):
            let at = min(max(after + 1, 0), out.count)
            out.insert(contentsOf: Array(bases), at: at)
        case .delete(let from, let to):
            let a = max(from, 0), b = min(to, out.count - 1)
            if a <= b { out.removeSubrange(a...b) }
        }
        return out
    }

    /// Bases gained (+) or lost (−).
    var lengthChange: Int {
        switch self {
        case .substitute: return 0
        case .insert(_, let b): return b.count
        case .delete(let a, let b): return -(b - a + 1)
        }
    }

    /// The first base it touches (an insertion: the base after the gap).
    var position: Int {
        switch self {
        case .substitute(let at, _): return at
        case .insert(let after, _): return after + 1
        case .delete(let from, _): return from
        }
    }

    /// Whether it fits the sequence: a base there, 1–6 bases of A, C, G, T, an insertion between two bases.
    func fits(_ s: [Character]) -> Bool {
        let ok = Set("ACGT")
        switch self {
        case .substitute(let at, let to): return s.indices.contains(at) && ok.contains(to) && s[at] != to
        case .insert(let after, let b): return after >= 0 && after < s.count - 1 && (1...GXEdit.maxLength).contains(b.count) && b.allSatisfy { ok.contains($0) }
        case .delete(let a, let b): return a >= 0 && b < s.count && a <= b && b - a + 1 <= GXEdit.maxLength
        }
    }

    /// The change as genetics writes it: c.20A>T, c.6_7insA, c.7del, c.13_15del.
    func hgvs(_ s: [Character]) -> String {
        switch self {
        case .substitute(let at, let to): return "c.\(at + 1)\(s.indices.contains(at) ? String(s[at]) : "?")>\(to)"
        case .insert(let after, let b): return "c.\(after + 1)_\(after + 2)ins\(b)"
        case .delete(let a, let b): return a == b ? "c.\(a + 1)del" : "c.\(a + 1)_\(b + 1)del"
        }
    }

    /// The change in words ("A inserted after c.6", "c.13–15 (GAT) deleted").
    func words(_ s: [Character]) -> String {
        switch self {
        case .substitute(let at, let to): return "c.\(at + 1) \(s.indices.contains(at) ? String(s[at]) : "?") → \(to)"
        case .insert(let after, let b): return "\(b) inserted after c.\(after + 1)"
        case .delete(let a, let b):
            let bases = a >= 0 && b < s.count && a <= b ? String(s[a...b]) : ""
            return a == b ? "c.\(a + 1) (\(bases)) deleted" : "c.\(a + 1)–\(b + 1) (\(bases)) deleted"
        }
    }

    /// Reads a change typed as genetics writes it — c.20A>T, c.6_7insA, c.7del, c.7delC, c.13_15del, c.13_15delGAT,
    /// c.5dup, c.13_15dup ("c." optional, any case) — checked against the sequence: the base named must be the base
    /// there. A duplication is an insertion of a copy.
    static func parse(_ text: String, on s: [Character]) -> (edit: GXEdit?, error: String?) {
        var t = text.uppercased().filter { !$0.isWhitespace }
        if t.hasPrefix("C.") { t.removeFirst(2) }
        guard !t.isEmpty else { return (nil, nil) }
        func number(_ x: Substring) -> Int? { Int(x) }
        func base(_ i: Int) -> String { s.indices.contains(i) ? String(s[i]) : "?" }
        let n = s.count
        func inRange(_ p: Int) -> Bool { p >= 1 && p <= n }
        // (a substitution: 20A>T)
        if let gt = t.firstIndex(of: ">") {
            let left = t[..<gt], right = t[t.index(after: gt)...]
            guard let ref = left.last, let p = number(left.dropLast()), right.count == 1, let alt = right.first,
                  "ACGT".contains(ref), "ACGT".contains(alt) else { return (nil, "Write a substitution as c.20A>T.") }
            guard inRange(p) else { return (nil, "c.\(p) is outside the coding sequence (1–\(n)).") }
            guard base(p - 1) == String(ref) else { return (nil, "c.\(p) is \(base(p - 1)), not \(ref).") }
            guard ref != alt else { return (nil, "That changes nothing.") }
            return (.substitute(at: p - 1, to: alt), nil)
        }
        // (positions, then what happens there)
        let digits = t.prefix { $0.isNumber || $0 == "_" }
        let rest = t.dropFirst(digits.count)
        let ps = digits.split(separator: "_").compactMap(number)
        guard !ps.isEmpty, ps.count <= 2, ps.allSatisfy(inRange) else {
            return (nil, ps.isEmpty ? "Write a change as c.20A>T, c.6_7insA, c.7del or c.13_15del." : "Positions run from 1 to \(n).")
        }
        let a = ps[0], b = ps.count == 2 ? ps[1] : ps[0]
        if rest.hasPrefix("INS") {
            let bases = String(rest.dropFirst(3))
            guard ps.count == 2, b == a + 1 else { return (nil, "An insertion goes between two neighbouring bases: c.6_7insA.") }
            guard (1...maxLength).contains(bases.count), bases.allSatisfy({ "ACGT".contains($0) }) else { return (nil, "Insert 1–\(maxLength) bases of A, C, G and T.") }
            guard a < n else { return (nil, "An insertion goes between two bases of the coding sequence.") }
            return (.insert(after: a - 1, bases: bases), nil)
        }
        if rest.hasPrefix("DUP") {
            guard b >= a, b - a + 1 <= maxLength else { return (nil, "Duplicate 1–\(maxLength) bases.") }
            guard b < n else { return (nil, "A duplication of the last base falls after the coding sequence.") }
            return (.insert(after: b - 1, bases: String(s[(a - 1)...(b - 1)])), nil)
        }
        if rest.hasPrefix("DEL") {
            let named = String(rest.dropFirst(3))
            guard b >= a, b - a + 1 <= maxLength else { return (nil, "Delete 1–\(maxLength) bases at once.") }
            let there = String(s[(a - 1)...(b - 1)])
            guard named.isEmpty || named == there else { return (nil, "c.\(a)\(a == b ? "" : "_\(b)") is \(there), not \(named).") }
            return (.delete(from: a - 1, to: b - 1), nil)
        }
        return (nil, "Write a change as c.20A>T, c.6_7insA, c.7del or c.13_15del.")
    }
}

/// A coding sequence read in frame from its first base: its codons (DNA letters) and its protein up to the first stop.
struct GXTranslation: Equatable {
    let codons: [String]
    /// One letter each, as far as the first stop (not included).
    let protein: String
    /// The codon index of the first stop, if any.
    let stopAt: Int?

    init(_ s: [Character]) {
        let cs = BioSeq.codons(String(s))
        codons = cs
        var p = ""
        var stop: Int?
        for (k, c) in cs.enumerated() {
            let aa = BioCode.aminoAcid(c) ?? "X"
            if aa == "*" {
                stop = k
                break
            }
            p.append(aa)
        }
        protein = p
        stopAt = stop
    }

    /// The amino acid of codon k ("*" a stop).
    func aa(_ k: Int) -> Character { codons.indices.contains(k) ? (BioCode.aminoAcid(codons[k]) ?? "X") : " " }
}

/// A base of the original and the mutant lined up: one side is nil where the other has a base the first lacks.
struct GXAlignedBase: Equatable {
    let original: Character?
    let mutant: Character?
    let originalIndex: Int?
    let mutantIndex: Int?
    /// Substituted, inserted or deleted here.
    var changed: Bool { original != mutant }
}

/// What a mutation does to the protein.
struct GXMutationResult: Equatable {
    enum Kind: String {
        case none, silent, missense, nonsense, stopLoss, frameshift, inFrameInsertion, inFrameDeletion, startLoss

        var title: String {
            switch self {
            case .none: return "No change"
            case .silent: return "Silent"
            case .missense: return "Missense"
            case .nonsense: return "Nonsense"
            case .stopLoss: return "Stop-loss"
            case .frameshift: return "Frameshift"
            case .inFrameInsertion: return "In-frame insertion"
            case .inFrameDeletion: return "In-frame deletion"
            case .startLoss: return "Start-loss"
            }
        }
    }

    let kind: Kind
    let edit: GXEdit
    /// The change as genetics writes it (c.20A>T).
    let hgvs: String
    /// The protein change, short: Ala2Val, Phe3Phe, Gln7*, Phe3fs, Asp5 lost.
    let change: String
    /// What happens, a sentence or two each.
    let lines: [String]
    let original: GXTranslation
    let mutant: GXTranslation
    /// The mutant base for base under the original.
    let aligned: [GXAlignedBase]
    /// A missense's two amino acids, whether the change is conservative (same class), and the side chains' charges.
    let from: BioAminoAcid?
    let to: BioAminoAcid?
    let conservative: Bool?
    /// The original codons whose amino acid is changed or lost, and the mutant codons whose amino acid is new.
    let changedOriginal: Set<Int>
    let changedMutant: Set<Int>
    /// A nonsense mutation's share of the protein kept (0…1).
    let kept: Double?
    /// What the mutant makes after a stop-loss, reading on past the old stop (the 3′ UTR's codons).
    let readThrough: String?
}

enum GXMutations {
    /// Classifies a change to a coding sequence (DNA letters, ATG … stop). `downstream` is the mRNA after the stop (its 3′
    /// UTR, DNA letters) that a stop-loss reads on into; `matureNumbering` adds the mature protein's numbers (β-globin
    /// loses its first Met, so codon 7's Glu is the classic Glu6).
    static func classify(_ cds: String, _ edit: GXEdit, downstream: String = "", matureNumbering: Bool = false) -> GXMutationResult {
        let o = Array(cds.uppercased())
        let m = edit.apply(to: o)
        let ot = GXTranslation(o), mt = GXTranslation(m)
        let aligned = align(o, edit)
        let pos = edit.position
        let c = max(0, min(pos, max(o.count - 1, 0))) / 3
        let len = abs(edit.lengthChange)
        func three(_ a: Character) -> String { a == "*" ? "*" : (BioAminoAcids.byOne[a]?.three ?? String(a)) }
        func name(_ a: Character) -> String { a == "*" ? "a stop" : (BioAminoAcids.byOne[a]?.three ?? String(a)) }
        let total = ot.protein.count
        var kind: GXMutationResult.Kind = .none
        var change = ""
        var lines: [String] = []
        var from: BioAminoAcid?, to: BioAminoAcid?, conservative: Bool?
        var changedO = Set<Int>(), changedM = Set<Int>()
        var kept: Double?
        var readThrough: String?
        func mature(_ k: Int, _ a: Character, _ b: String) -> String? {
            matureNumbering && k >= 1 ? "In the mature protein (its first Met removed) this is \(three(a))\(k)\(b)." : nil
        }

        if m == o {
            lines = ["The sequence is the same as before."]
        } else if pos <= 2 && !(m.count >= 3 && String(m[0..<3]) == "ATG") {
            // (start-loss: the ribosome would start at the next AUG instead)
            kind = .startLoss
            change = "Met1 lost"
            changedO = Set(0..<max(ot.codons.count, 1))
            var next: Int?
            if m.count >= 4 {
                for i in 1...(m.count - 3) where String(m[i..<(i + 3)]) == "ATG" {
                    next = i
                    break
                }
            }
            let first = m.count >= 3 ? String(m[0..<3]) : String(m)
            lines.append("Start-loss: the start codon ATG becomes \(first), so translation cannot begin there (AUG is the only start codon this tool uses).")
            if let i = next {
                let q = GXTranslation(Array(m[i...])).protein
                if !q.isEmpty, ot.protein.hasSuffix(q) {
                    lines.append("The ribosome would start at the next AUG, c.\(i + 1), in the original frame: the protein would lack its first \(total - q.count) amino acid\(total - q.count == 1 ? "" : "s").")
                } else {
                    lines.append("The ribosome would start at the next AUG, c.\(i + 1), in a different frame: an unrelated chain of \(q.count) amino acid\(q.count == 1 ? "" : "s") (\(q.prefix(12))\(q.count > 12 ? "…" : "")).")
                }
            } else {
                lines.append("There is no other AUG in the coding sequence: no protein would be made from it.")
            }
        } else if len % 3 != 0 {
            // (frameshift: every codon after the change is read in a new frame)
            kind = .frameshift
            let p = commonPrefix(ot.protein + (ot.stopAt != nil ? "*" : ""), mt.protein + (mt.stopAt != nil ? "*" : ""))
            let oa = Array(ot.protein)
            change = p < oa.count ? "\(three(oa[p]))\(p + 1)fs" : "fs"
            changedO = Set(c..<max(ot.codons.count, c + 1))
            changedM = Set(c..<(mt.stopAt.map { $0 + 1 } ?? mt.codons.count))
            let what = edit.lengthChange > 0 ? "inserted" : "deleted"
            lines.append("Frameshift: \(len) base\(len == 1 ? "" : "s") \(what), not a multiple of 3, so every codon after it is read in a new frame.")
            let shown = mt.protein.count > 24 ? String(mt.protein.prefix(24)) + "…" : mt.protein
            if let stop = mt.stopAt {
                lines.append("The new frame's protein, \(shown), meets a stop at codon \(stop + 1): \(mt.protein.count) amino acids instead of \(total).")
            } else {
                lines.append("The new frame's protein, \(shown), has no stop codon before the coding sequence ends (it would read on into the 3′ UTR).")
            }
        } else if edit.lengthChange != 0 {
            // (in-frame: whole codons added or removed, the rest unchanged)
            let ins = edit.lengthChange > 0
            kind = ins ? .inFrameInsertion : .inFrameDeletion
            let oa = Array(ot.protein + (ot.stopAt != nil ? "*" : "")), ma = Array(mt.protein + (mt.stopAt != nil ? "*" : ""))
            let p = commonPrefix(String(oa), String(ma))
            let sfx = min(commonSuffix(String(oa), String(ma)), min(oa.count, ma.count) - p)
            let lost = Array(oa[p..<(oa.count - sfx)]), gained = Array(ma[p..<(ma.count - sfx)])
            changedO = Set(p..<(p + lost.count))
            changedM = Set(p..<(p + gained.count))
            func list(_ a: [Character], from k: Int) -> String {
                a.isEmpty ? "" : (a.count == 1 ? "\(three(a[0]))\(k + 1)" : "\(three(a[0]))\(k + 1)–\(three(a[a.count - 1]))\(k + a.count)")
            }
            let codons = len / 3
            // (where the old stop now falls: a stop before it cuts the protein short)
            let shiftedStop = (ot.stopAt ?? ot.codons.count) + edit.lengthChange / 3
            if let ms = mt.stopAt, ms < shiftedStop {
                change = "stop at codon \(ms + 1)"
                kept = Double(mt.protein.count) / Double(max(total, 1))
                lines.append("In-frame \(ins ? "insertion" : "deletion"): \(len) bases, a multiple of 3, but it makes a stop codon at codon \(ms + 1), so the protein is cut short as a nonsense mutation would: \(mt.protein.count) of \(total) amino acids kept.")
            } else if !lost.isEmpty && gained.isEmpty {
                change = "\(list(lost, from: p)) lost"
                lines.append("In-frame deletion: \(len) bases (\(codons) codon\(codons == 1 ? "" : "s")), a multiple of 3, so \(lost.count == 1 ? "one amino acid is" : "\(lost.count) amino acids are") removed (\(change)) and the rest of the protein is unchanged.")
            } else if lost.isEmpty && !gained.isEmpty {
                let after = p >= 1 ? " after \(three(oa[p - 1]))\(p)" : ""
                change = gained.map(three).joined(separator: "–") + " inserted" + after
                lines.append("In-frame insertion: \(len) bases (\(codons) codon\(codons == 1 ? "" : "s")), a multiple of 3, so \(gained.count == 1 ? "one amino acid is" : "\(gained.count) amino acids are") added (\(gained.map(three).joined(separator: "–"))\(after)) and the rest of the protein is unchanged.")
            } else {
                change = "\(list(lost, from: p)) → \(gained.map(three).joined(separator: "–"))"
                lines.append("In-frame \(ins ? "insertion" : "deletion"): \(len) bases, a multiple of 3, across a codon boundary: \(change). The rest of the protein is unchanged.")
            }
            if let line = matureNote(lost.first, p, matureNumbering) { lines.append(line) }
        } else {
            // (one base substituted: one codon changed)
            let oc = ot.codons.indices.contains(c) ? ot.codons[c] : "", mc = mt.codons.indices.contains(c) ? mt.codons[c] : ""
            let oa = ot.aa(c), ma = mt.aa(c)
            let ocR = oc.replacingOccurrences(of: "T", with: "U"), mcR = mc.replacingOccurrences(of: "T", with: "U")
            changedO = [c]
            changedM = [c]
            if ot.stopAt.map({ c > $0 }) == true {
                // (past the stop: not translated)
                kind = .silent
                change = "after the stop"
                lines.append("This base lies past the stop codon, in the 3′ UTR: it is not translated, so the protein is unchanged.")
            } else if oa == ma {
                kind = .silent
                change = oa == "*" ? "*\(c + 1)*" : "\(three(oa))\(c + 1)\(three(ma))"
                lines.append(oa == "*"
                    ? "Silent: the stop codon \(ocR) becomes \(mcR), still a stop. The protein is unchanged."
                    : "Silent: codon \(ocR) → \(mcR) still codes \(three(oa)) (most amino acids have more than one codon). The protein is unchanged.")
            } else if ma == "*" {
                kind = .nonsense
                change = "\(three(oa))\(c + 1)*"
                kept = Double(c) / Double(max(total, 1))
                let pct = Int((Double(c) / Double(max(total, 1)) * 100).rounded())
                lines.append("Nonsense: codon \(ocR) (\(three(oa))) → \(mcR), a stop codon, so translation ends at codon \(c + 1). The protein is \(c) amino acid\(c == 1 ? "" : "s") instead of \(total): \(pct)% kept.")
                changedO = Set(c..<max(ot.codons.count, c + 1))
                if let line = mature(c, oa, "*") { lines.append(line) }
            } else if oa == "*" {
                kind = .stopLoss
                change = "*\(c + 1)\(three(ma)) (reads on)"
                let tail = GXTranslation(m + Array(downstream.uppercased()))
                let extra = String(tail.protein.dropFirst(total + 1))
                readThrough = String(ma) + extra
                let after = downstream.isEmpty
                    ? "translation reads on past the old stop into the 3′ UTR (not included here) until it meets another stop."
                    : (tail.stopAt != nil
                       ? "translation reads on into the 3′ UTR and adds \(three(ma))\(extra.isEmpty ? "" : " and " + extra.map(three).joined(separator: "–")) before the next stop."
                       : "translation reads on into the 3′ UTR (adding \(([ma] + Array(extra)).map(three).joined(separator: "–"))) and runs off its end without another stop.")
                lines.append("Stop-loss: the stop codon \(ocR) → \(mcR) now codes \(three(ma)), so \(after)")
            } else {
                kind = .missense
                change = "\(three(oa))\(c + 1)\(three(ma))"
                let a = BioAminoAcids.byOne[oa], b = BioAminoAcids.byOne[ma]
                from = a
                to = b
                if let a, let b {
                    conservative = a.group == b.group
                    lines.append("Missense: codon \(ocR) → \(mcR) changes \(a.three) to \(b.three): \(a.three) (\(GXCodonTable.classWords(a))) → \(b.three) (\(GXCodonTable.classWords(b))).")
                    lines.append(a.group == b.group
                        ? "Conservative: both are \(a.group.label.lowercased()), so the protein may still fold and work."
                        : "Not conservative: the side chain's class changes, which can change how the protein folds or what it binds.")
                    if a.charge != b.charge {
                        lines.append("Charge at cell pH: \(chargeWord(a.charge)) → \(chargeWord(b.charge)).")
                    }
                } else {
                    lines.append("Missense: codon \(ocR) → \(mcR) changes \(name(oa)) to \(name(ma)).")
                }
                if let line = mature(c, oa, three(ma)) { lines.append(line) }
            }
        }
        return GXMutationResult(kind: kind, edit: edit, hgvs: edit.hgvs(o), change: change, lines: lines, original: ot, mutant: mt,
                                aligned: aligned, from: from, to: to, conservative: conservative, changedOriginal: changedO,
                                changedMutant: changedM, kept: kept, readThrough: readThrough)
    }

    private static func matureNote(_ a: Character?, _ k: Int, _ on: Bool) -> String? {
        guard on, let a, k >= 1 else { return nil }
        return "In the mature protein (its first Met removed) the first change is at \(BioAminoAcids.byOne[a]?.three ?? String(a))\(k)."
    }

    /// "negative (−1)", "uncharged (0)", "positive (+1)".
    static func chargeWord(_ q: Int) -> String { q < 0 ? "negative (−1)" : (q > 0 ? "positive (+1)" : "uncharged (0)") }

    private static func commonPrefix(_ a: String, _ b: String) -> Int { zip(a, b).prefix { $0 == $1 }.count }
    private static func commonSuffix(_ a: String, _ b: String) -> Int { zip(a.reversed(), b.reversed()).prefix { $0 == $1 }.count }

    /// The original and the mutant lined up base for base: a gap in the mutant where bases were deleted, in the original
    /// where they were inserted.
    static func align(_ o: [Character], _ edit: GXEdit) -> [GXAlignedBase] {
        var out: [GXAlignedBase] = []
        switch edit {
        case .substitute(let at, let to):
            for (i, b) in o.enumerated() { out.append(GXAlignedBase(original: b, mutant: i == at ? to : b, originalIndex: i, mutantIndex: i)) }
        case .insert(let after, let bases):
            var mi = 0
            for (i, b) in o.enumerated() {
                out.append(GXAlignedBase(original: b, mutant: b, originalIndex: i, mutantIndex: mi))
                mi += 1
                if i == after {
                    for x in bases {
                        out.append(GXAlignedBase(original: nil, mutant: x, originalIndex: nil, mutantIndex: mi))
                        mi += 1
                    }
                }
            }
        case .delete(let from, let to):
            var mi = 0
            for (i, b) in o.enumerated() {
                if i >= from && i <= to {
                    out.append(GXAlignedBase(original: b, mutant: nil, originalIndex: i, mutantIndex: nil))
                } else {
                    out.append(GXAlignedBase(original: b, mutant: b, originalIndex: i, mutantIndex: mi))
                    mi += 1
                }
            }
        }
        return out
    }

    /// A gene's coding sequence for the Mutations page, and the 3′ UTR after it.
    struct Source: Equatable {
        let name: String
        let cds: String
        let downstream: String
        let matureNumbering: Bool
    }

    static var teaching: Source { Source(name: "Teaching gene", cds: BioGenes.teaching.cds, downstream: "GCAATAAAGCC", matureNumbering: false) }
    static var hbb: Source { Source(name: "Human β-globin (HBB)", cds: BioGenes.hbbCDS, downstream: "", matureNumbering: true) }

    /// The sickle-cell mutation (§6.12): HBB c.20A>T.
    static var sickle: GXEdit { .substitute(at: BioGenes.sickle.position - 1, to: BioGenes.sickle.to) }

    /// The teaching gene's six test vectors (§6.12) as edits, in the table's order.
    static var teachingVectors: [(label: String, edit: GXEdit)] {
        [("c.9C>T (silent)", .substitute(at: 8, to: "T")),
         ("c.5C>T (missense)", .substitute(at: 4, to: "T")),
         ("c.19C>T (nonsense)", .substitute(at: 18, to: "T")),
         ("A inserted after c.6 (frameshift)", .insert(after: 5, bases: "A")),
         ("c.7 deleted (frameshift)", .delete(from: 6, to: 6)),
         ("c.13_15 deleted (in-frame)", .delete(from: 12, to: 14))]
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    static func geneMutationChecks(_ r: inout BioCheckReport) {
        let cds = BioGenes.teaching.cds
        let o = Array(cds)
        // (§6.12's table: each edit makes the listed mutant CDS and protein, and is classified as listed)
        let want: [GXMutationResult.Kind] = [.silent, .missense, .nonsense, .frameshift, .frameshift, .inFrameDeletion]
        let changes = ["Phe3Phe", "Ala2Val", "Gln7*", "Phe3fs", "Phe3fs", "Asp5 lost"]
        let table = BioGenes.teaching.mutations
        for (k, v) in GXMutations.teachingVectors.enumerated() where k < table.count {
            let res = GXMutations.classify(cds, v.edit, downstream: GXMutations.teaching.downstream)
            let mutant = String(v.edit.apply(to: o))
            let listed = table[k]
            let protein = res.mutant.protein + (res.mutant.stopAt != nil ? "*" : "")
            let proteinOK: Bool
            if listed.protein.contains("…") {
                proteinOK = res.mutant.protein.hasPrefix(listed.protein.components(separatedBy: "…").first ?? "") && res.mutant.stopAt == nil
            } else {
                proteinOK = protein == listed.protein || protein == listed.protein + "*"
            }
            r.check("gx.mutation.\(k + 1)", "\(res.hgvs):\(res.kind.rawValue):\(res.change)",
                    mutant == listed.cds.replacingOccurrences(of: "**", with: "") && proteinOK && res.kind == want[k] && res.change == changes[k])
        }
        // (the HGVS-style field reads each back to the same edit)
        let typed = ["c.9C>T", "c.5C>T", "c.19C>T", "c.6_7insA", "c.7del", "c.13_15del"].map { GXEdit.parse($0, on: o).edit }
        r.check("gx.mutation.parse", typed.compactMap { $0?.hgvs(o) }.joined(separator: ","),
                typed == GXMutations.teachingVectors.map { Optional($0.edit) } && GXEdit.parse("c.9A>T", on: o).error != nil
                && GXEdit.parse("c.13_15delGAT", on: o).edit == .delete(from: 12, to: 14) && GXEdit.parse("c.13_15delGGG", on: o).edit == nil
                && GXEdit.parse("c.4dup", on: o).edit == .insert(after: 3, bases: "G") && GXEdit.parse("c.6_8insA", on: o).edit == nil
                && GXEdit.parse("c.99C>T", on: o).edit == nil)
        // (the sickle-cell mutation: HBB c.20A>T, GAG → GTG, Glu7Val = Glu6Val mature, acidic −1 → nonpolar 0)
        let hbb = GXMutations.hbb
        let s = GXMutations.classify(hbb.cds, GXMutations.sickle, matureNumbering: true)
        r.check("gx.mutation.sickle", "\(s.hgvs):\(s.kind.rawValue):\(s.change)",
                s.hgvs == "c.20A>T" && s.kind == .missense && s.change == "Glu7Val" && s.conservative == false
                && s.from?.charge == -1 && s.to?.charge == 0 && s.mutant.protein.hasPrefix(BioGenes.sickle.proteinStart)
                && s.mutant.protein.count == 147 && s.lines.contains { $0.contains("Glu (acidic, charged) → Val (nonpolar)") }
                && s.lines.contains { $0.contains("Glu6Val") } && s.lines.contains { $0.contains("negative (−1) → uncharged (0)") })
        // (the other classes: start-loss, stop-loss into the 3′ UTR, an in-frame insertion, a stop to another stop)
        let start = GXMutations.classify(cds, .substitute(at: 2, to: "A"))
        let stop = GXMutations.classify(cds, .substitute(at: 27, to: "C"), downstream: GXMutations.teaching.downstream)
        let ins = GXMutations.classify(cds, .insert(after: 5, bases: "GGC"))
        let stopStop = GXMutations.classify(cds, .substitute(at: 29, to: "G"))
        let stopIns = GXMutations.classify(cds, .insert(after: 5, bases: "TAA"))
        r.check("gx.mutation.classes", [start, stop, ins, stopStop, stopIns].map(\.kind.rawValue).joined(separator: ","),
                start.kind == .startLoss && stop.kind == .stopLoss && stop.readThrough == "QAIK" && ins.kind == .inFrameInsertion
                && ins.change == "Gly inserted after Ala2" && ins.mutant.protein == "MAGFSDWQKG" && stopStop.kind == .silent
                && stopIns.kind == .inFrameInsertion && stopIns.kept.map { abs($0 - 2.0 / 9) < 1e-9 } == true)
        let nonsense = GXMutations.classify(cds, .substitute(at: 18, to: "T"))
        r.check("gx.mutation.nonsense-kept", String(format: "%.3f", nonsense.kept ?? -1), nonsense.kept.map { abs($0 - 6.0 / 9) < 1e-9 } == true)
        // (aligned base for base: a deletion leaves gaps in the mutant, an insertion in the original)
        let del = GXMutations.align(o, .delete(from: 12, to: 14))
        let add = GXMutations.align(o, .insert(after: 5, bases: "A"))
        r.check("gx.mutation.align", "\(del.count),\(add.count)",
                del.count == 30 && del.filter { $0.mutant == nil }.count == 3 && add.count == 31 && add[6].original == nil && add[6].mutant == "A"
                && add.last?.mutantIndex == 30)
    }

    static func geneProcessChecks(_ r: inout BioCheckReport) {
        let g = GXGene.teaching(.eukaryote)
        let p = GXProcess(gene: g)
        let t = BioGenes.teaching
        let tx = p.steps(of: .txElong)
        let tl = p.steps(of: .tlElong)
        r.check("gx.steps.teaching", "\(p.steps.count)",
                p.steps.count == 99 && tx?.count == 81 && tl?.count == 8 && p.steps.allSatisfy { $0.title.count <= 40 }
                && p.frames.map(\.stage.group) == p.steps.map { $0.group ?? 0 } && p.steps.last?.group == 5)
        // (one nucleotide a step, each the template base's partner, joined at the 3′ end; at the end, the pre-mRNA)
        var each = true
        for i in tx ?? 0...0 {
            let k = Int(p.frames[i].made)
            let col = g.txStart + k - 1
            each = each && p.frames[i].polX == Double(col) && g.preMRNA[k - 1] == BioSeq.pair(g.template[col], rna: true)
                && p.rna(at: i).count == k
        }
        let lastTx = tx?.upperBound ?? 0
        r.check("gx.steps.transcription", "\(p.rna(at: lastTx).count)nt", each && p.rna(at: lastTx) == t.preMRNA)
        // (processing in a eukaryote: cap, tail, splicing, export, in that order)
        let order = p.frames.map(\.stage).filter { [.cap, .tail, .splice, .export].contains($0) }
        r.check("gx.steps.processing", order.map { "\($0.rawValue)" }.joined(separator: ","), order == [.cap, .tail, .splice, .export])
        // (one codon a step from the AUG; the chain at each step; MAFSDWQKG at the end, and termination by a release factor)
        let lastTl = tl?.upperBound ?? 0
        let endIdx = p.steps(of: .tlEnd)?.lowerBound ?? 0
        r.check("gx.steps.translation", p.chain(at: lastTl),
                p.chain(at: lastTl) == t.protein && p.chain(at: (tl?.lowerBound ?? 1) - 1) == "M" && p.frames[endIdx].releaseFactor == 1
                && p.steps[endIdx].text.contains("UAA") && p.steps.last?.text.contains("MAFSDWQKG") == true
                && p.steps[(tl?.lowerBound ?? 0)].title == "Elongation · codon 2: GCC → Ala")
        // (the scene: the initiator in the P site; during a codon's step the tRNA comes in first, the peptide bond next,
        // the move last; back is the same move in reverse)
        let e = tl?.lowerBound ?? 0
        let first = GXScene.rest(p, e - 1)
        let s1 = GXScene.make(p, from: e, to: e + 1, t: 0.2)
        let s2 = GXScene.make(p, from: e, to: e + 1, t: 0.5)
        let s3 = GXScene.make(p, from: e, to: e + 1, t: 0.75)
        let back = GXScene.make(p, from: e + 1, to: e, t: 0.25)
        let rest = GXScene.rest(p, e + 1)
        r.check("gx.scene.elongation", String(format: "%.2f,%.2f,%.2f", s1.ribosome, s2.ribosome, s3.ribosome),
                first.trnas.first?.initiator == true && first.residues.map(\.aa) == ["M"] && first.residues.first?.onTRNA == 1
                && s1.ribosome == 1 && (s1.trnas.first?.alpha ?? 0) > 0 && (s1.trnas.first?.alpha ?? 1) < 1 && s1.residues.last?.bonded == 0
                && s2.ribosome == 1 && (s2.residues.first { $0.index == 1 }?.onTRNA ?? 0) < 1 && s3.ribosome > 1 && s3.ribosome < 2
                && back == s3 && rest.ribosome == 2 && rest.trnas.map(\.codon) == [2, 1] && rest.residues.map(\.aa) == ["M", "A", "F"]
                && rest.residues.filter { $0.onTRNA == 1 }.map(\.index) == [2])
        let nt = GXScene.make(p, from: 2, to: 3, t: 0.4)
        r.check("gx.scene.transcription", "\(nt.incoming ?? -1)", nt.incoming == 1 && abs(nt.incomingProgress - 0.4) < 1e-9 && GXScene.rest(p, 3).incoming == nil)
        // (a prokaryote: no processing, the intron left out of the DNA, the same protein)
        let pk = GXProcess(gene: GXGene.teaching(.prokaryote))
        r.check("gx.steps.prokaryote", "\(pk.steps.count)",
                pk.steps.count == 1 + 1 + 54 + 1 + 1 + 8 + 1 + 1 && !pk.frames.contains { $0.stage.group == 3 }
                && pk.steps.last?.text.contains("MAFSDWQKG") == true && pk.steps.contains { $0.notice == ["coupled"] })
        // (β-globin: 444 nucleotides, then 147 codons; a sequence without AUG makes nothing)
        let h = GXProcess(gene: GXGene.hbb(.eukaryote))
        r.check("gx.steps.HBB", "\(h.steps.count)", h.steps(of: .txElong)?.count == 444 && h.steps(of: .tlElong)?.count == 146
                && h.chain(at: h.steps(of: .tlElong)?.upperBound ?? 0) == BioGenes.hbbProtein && h.steps.allSatisfy { $0.title.count <= 40 })
        let none = GXGene.own("CCCGGGTTT", cell: .eukaryote).map { GXProcess(gene: $0) }
        r.check("gx.steps.no-start", none.map { "\($0.steps.count)" } ?? "nil", none?.steps.contains { $0.title == "No start codon" } == true && none?.frames.last?.stage == .result)
    }
}
