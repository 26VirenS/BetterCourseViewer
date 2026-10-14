import Foundation

// The standard genetic code (spec §6.1, NCBI table 1) and the 20 amino acids' facts (§6.2), read from the text both
// apps share. The tools use AUG as the only start codon and say so (UUG and CUG are rare alternatives); a codon with N
// in it reads as X.

/// The standard genetic code.
enum BioCode {
    /// The order of the bases everywhere a table is drawn: U, C, A, G.
    static let bases: [Character] = ["U", "C", "A", "G"]

    /// The 64 codons (RNA letters) and their amino acids (one letter; "*" a stop), in U C A G order.
    static let entries: [(codon: String, aa: Character)] = BioData.codeText
        .split(whereSeparator: { $0 == " " || $0 == "\n" })
        .compactMap { pair in
            let p = pair.split(separator: "=")
            guard p.count == 2, p[0].count == 3, let aa = p[1].first else { return nil }
            return (String(p[0]), aa)
        }

    private static let table: [String: Character] = Dictionary(entries.map { ($0.codon, $0.aa) }, uniquingKeysWith: { a, _ in a })

    /// The amino acid a codon codes ("AUG" → M, "UAA" → *); DNA letters too ("ATG"). X for a codon with N; nil for
    /// anything that is not three bases.
    static func aminoAcid(_ codon: String) -> Character? {
        let c = codon.uppercased().replacingOccurrences(of: "T", with: "U")
        guard c.count == 3, c.allSatisfy({ "UCAGN".contains($0) }) else { return nil }
        if c.contains("N") { return "X" }
        return table[c]
    }

    /// The codons of an amino acid (one letter; "*" for the stops), RNA letters, in table order.
    static func codons(for aa: Character) -> [String] {
        let a = Character(aa.uppercased())
        return entries.filter { $0.aa == a }.map(\.codon)
    }

    static func isStart(_ codon: String) -> Bool { codon.uppercased().replacingOccurrences(of: "T", with: "U") == "AUG" }
    static func isStop(_ codon: String) -> Bool { aminoAcid(codon) == "*" }

    /// The stop codons' old names: UAA ochre, UAG amber, UGA opal.
    static let stopNames: [String: String] = ["UAA": "ochre", "UAG": "amber", "UGA": "opal"]

    /// The anticodon that pairs with a codon, written 3′→5′ as it sits under the codon (codon 5′-AUG-3′ → 3′-UAC-5′).
    static func anticodon3to5(_ codon: String) -> String {
        String(codon.uppercased().replacingOccurrences(of: "T", with: "U").map { BioSeq.pair($0, rna: true) })
    }

    /// The same anticodon written 5′→3′, as tRNA tables write it (5′-CAU-3′ for AUG).
    static func anticodon5to3(_ codon: String) -> String { String(anticodon3to5(codon).reversed()) }
}

/// An amino acid's group as AP Biology and Campbell group them (the colours of §3.8).
enum BioAAClass: String, CaseIterable {
    case nonpolar, polar, acidic, basic

    var label: String {
        switch self {
        case .nonpolar: return "Nonpolar"
        case .polar: return "Polar (uncharged)"
        case .acidic: return "Acidic (−)"
        case .basic: return "Basic (+)"
        }
    }
}

/// One of the 20 amino acids (§6.2).
struct BioAminoAcid: Equatable {
    let name: String
    /// "Gly".
    let three: String
    /// "G".
    let one: Character
    let group: BioAAClass
    /// Lehninger's group ("Nonpolar, aliphatic").
    let lehninger: String
    /// The side chain's pKa (Lehninger), nil when it has none to speak of.
    let pKa: Double?
    /// The side chain's charge at pH 7.4 as a whole number (His 0).
    let charge: Int
    /// The charge as the table writes it ("≈0 (about 4% +1 at pH 7.4, 9% at 7.0)").
    let chargeText: String
    /// Kyte–Doolittle hydropathy.
    let hydropathy: Double
    /// The free amino acid's molar mass (g/mol).
    let mass: Double
    /// Its mass as a residue in a chain (free − 18.02).
    let residueMass: Double
    let formula: String
    /// How many codons code it.
    let codonCount: Int
    let note: String

    var codons: [String] { BioCode.codons(for: one) }
    /// Glycine's side chain is H: the only amino acid that is not chiral.
    var isChiral: Bool { one != "G" }
}

enum BioAminoAcids {
    /// The 20, in §6.2's order (Gly, Ala, Val, Leu, Ile, Met, Pro, Phe, Trp, Tyr, Ser, Thr, Cys, Asn, Gln, Lys, Arg, His,
    /// Asp, Glu).
    static let all: [BioAminoAcid] = BioData.aminoAcidText.split(separator: "\n").compactMap { line in
        let c = line.split(separator: "\t", omittingEmptySubsequences: false).map { String($0).trimmingCharacters(in: .whitespaces) }
        guard c.count == 13, let one = c[2].first else { return nil }
        let group: BioAAClass
        switch c[3] {
        case let g where g.hasPrefix("Nonpolar"): group = .nonpolar
        case let g where g.hasPrefix("Polar"): group = .polar
        case let g where g.hasPrefix("Acidic"): group = .acidic
        default: group = .basic
        }
        func number(_ s: String) -> Double? { Double(s.replacingOccurrences(of: "−", with: "-").replacingOccurrences(of: "+", with: "")) }
        let charge = c[6].hasPrefix("≈") ? 0 : Int(number(c[6]) ?? 0)
        return BioAminoAcid(name: c[0], three: c[1], one: one, group: group, lehninger: c[4], pKa: number(c[5]), charge: charge,
                            chargeText: c[6], hydropathy: number(c[7]) ?? 0, mass: number(c[8]) ?? 0, residueMass: number(c[9]) ?? 0,
                            formula: c[10], codonCount: Int(c[11]) ?? 0, note: c[12])
    }

    static let byOne: [Character: BioAminoAcid] = Dictionary(all.map { ($0.one, $0) }, uniquingKeysWith: { a, _ in a })

    /// An amino acid by its name ("lysine"), three letters ("Lys") or one letter ("K"), any case.
    static func find(_ query: String) -> BioAminoAcid? {
        let q = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !q.isEmpty else { return nil }
        if q.count == 1, let c = q.uppercased().first { return byOne[c] }
        return all.first { $0.three.lowercased() == q || $0.name.lowercased() == q || $0.name.lowercased().hasPrefix(q + " ") }
    }

    /// A peptide's mass: the residues' masses plus one water (18.02 g/mol).
    static func peptideMass(_ protein: String) -> Double {
        protein.compactMap { byOne[$0]?.residueMass }.reduce(0, +) + 18.02
    }

    /// A peptide's net charge at pH 7 by the textbook count: +1 per K and R, −1 per D and E, +1 for the N-terminus and
    /// −1 for the C-terminus; His counted as 0.
    static func netCharge(_ protein: String) -> Int {
        guard !protein.isEmpty else { return 0 }
        return protein.reduce(0) { n, c in n + (c == "K" || c == "R" ? 1 : (c == "D" || c == "E" ? -1 : 0)) }
    }

    /// GRAVY: the mean Kyte–Doolittle hydropathy (> 0 is hydrophobic).
    static func gravy(_ protein: String) -> Double {
        let v = protein.compactMap { byOne[$0]?.hydropathy }
        return v.isEmpty ? 0 : v.reduce(0, +) / Double(v.count)
    }
}
