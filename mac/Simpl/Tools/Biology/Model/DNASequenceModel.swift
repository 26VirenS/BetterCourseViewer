import Foundation

// The Sequence page's facts (spec §4.2 Sequence, §3.7): for one strand typed 5′→3′ — its complement written 3′→5′
// under it (not the same as the reverse complement, which is the other strand read 5′→3′: both shown, each named), its
// length, GC content and base counts, the Chargaff check on the double strand it makes (A = T and G = C, so
// %A + %G = 50%), its exact molar mass (the monophosphates' masses less 18.02 for each phosphodiester bond, per strand;
// "about 650 g/mol per base pair" is the lab's estimate, for the sodium salt) and a rough melting temperature for a
// short primer in 50 mM salt (Wallace's rule to 14 nt, then the GC formula). RNA gets its own masses and no Chargaff
// or melting temperature: it is usually single-stranded.

/// What the Sequence page says of a strand.
struct DNASequenceFacts {
    let kind: BioSeqKind
    /// The strand, 5′→3′.
    let sequence: String
    let counts: BioBaseCounts
    /// The partner strand base by base, written 3′→5′ under this one.
    let complement: String
    /// The partner strand read 5′→3′.
    let reverseComplement: String
    /// This strand's molar mass (g/mol).
    let mass: Double
    /// The partner strand's.
    let partnerMass: Double

    init(_ sequence: String, kind: BioSeqKind = .dna) {
        self.kind = kind
        self.sequence = sequence
        let rna = kind == .rna
        counts = BioSeq.counts(sequence)
        complement = BioSeq.complement(sequence, rna: rna)
        reverseComplement = BioSeq.reverseComplement(sequence, rna: rna)
        mass = BioSeq.mass(sequence, rna: rna)
        partnerMass = BioSeq.mass(complement, rna: rna)
    }

    var length: Int { sequence.count }
    var isRNA: Bool { kind == .rna }
    var gcPercent: Double { counts.gcFraction * 100 }

    /// Both strands together (g/mol): DNA only.
    var duplexMass: Double? { isRNA ? nil : mass + partnerMass }
    /// The quick lab estimate: about 650 g/mol per base pair (the sodium salt).
    var labEstimate: Double { Double(length) * 650 }

    /// The duplex's counts and %A + %G (50% always: the check that A pairs with T and G with C). DNA only.
    var chargaff: (counts: BioBaseCounts, purinePercent: Double)? { isRNA ? nil : BioSeq.chargaff(sequence) }

    /// The melting temperature estimate (°C) and the rule used. DNA only, and nil with no A, C, G or T.
    var meltingTemperature: (value: Double, rule: String)? {
        let n = counts.a + counts.c + counts.g + counts.t
        guard !isRNA, n > 0 else { return nil }
        let rule = n <= 14 ? "Wallace rule: 2·(A+T) + 4·(G+C)" : "64.9 + 41·(G + C − 16.4) ÷ N"
        return (BioSeq.meltingTemperature(sequence), rule)
    }

    /// Lines of the two strands aligned (§3.7's groups of ten): each line's first position, the strand's groups and
    /// the complement's under them.
    func alignedLines(perLine groups: Int = 5) -> [(start: Int, top: [String], bottom: [String])] {
        let top = BioSeq.lines(sequence, perLine: groups), bottom = BioSeq.lines(complement, perLine: groups)
        return zip(top, bottom).map { (start: $0.start, top: $0.groups, bottom: $1.groups) }
    }

    // MARK: Checks

    static func checks(_ r: inout BioCheckReport) {
        let f = DNASequenceFacts(DNAModel.defaultSequence)
        r.check("dna.seq.counts", "A\(f.counts.a)T\(f.counts.t)G\(f.counts.g)C\(f.counts.c),GC\(Int(f.gcPercent.rounded()))%",
                f.counts.a == 5 && f.counts.t == 5 && f.counts.g == 5 && f.counts.c == 5 && abs(f.gcPercent - 50) < 1e-9)
        // (5 of each: 5 × (331.22 + 322.21 + 347.22 + 307.20) − 19 × 18.02)
        r.near("dna.seq.mass", f.mass, 6196.87, 0.005, digits: 2)
        r.near("dna.seq.duplex-mass", f.duplexMass ?? 0, 2 * 6196.87, 0.01, digits: 2)
        r.near("dna.seq.Tm", f.meltingTemperature?.value ?? 0, 64.9 + 41 * (10 - 16.4) / 20, 1e-9, digits: 2)
        r.near("dna.seq.Tm-Wallace", DNASequenceFacts("ATGCATGCATGC").meltingTemperature?.value ?? 0, 36, 1e-9, digits: 1)
        r.near("dna.seq.Chargaff", f.chargaff?.purinePercent ?? 0, 50, 1e-9, digits: 1)
        let small = DNASequenceFacts("ATGC")
        r.same("dna.seq.complements", "\(small.complement)/\(small.reverseComplement)", "TACG/GCAT")
        let rna = DNASequenceFacts("AUGC", kind: .rna)
        r.check("dna.seq.RNA", String(format: "%@,%.2f", rna.complement, rna.mass),
                rna.complement == "UACG" && abs(rna.mass - 1303.76) < 0.005 && rna.chargaff == nil && rna.meltingTemperature == nil)
        let lines = DNASequenceFacts(String(repeating: "ACGT", count: 30)).alignedLines()
        r.check("dna.seq.aligned", "\(lines.count)lines,\(lines.first?.start ?? 0),\(lines.last?.start ?? 0)",
                lines.count == 3 && lines[1].start == 51 && lines.allSatisfy { $0.top.map(\.count) == $0.bottom.map(\.count) })
    }
}
