import Foundation

// The biology toolkit's shared datasets (spec §3.3, §6), parsed from the text in BioDataText.swift the first time each
// is asked for and kept (a static let: parsed once, safely, from any thread). The tools read them from here:
// BioData.molecule("ALA"), BioData.bdna, BioData.ubiquitin, BioData.trna, BioGenes.teaching … The genetic code and
// the amino acids' facts are in BioGeneticCode.swift.

/// The toolkit's datasets.
enum BioData {
    // MARK: Molecule blocks (§6.3, §6.6, §6.8, §6.9)

    /// §6.3: the 20 residue templates, in the specification's order (Ala, Arg, Asn, …, Val).
    static let residues: [BioMolecule] = BioBlockParser.parse(residueText)
    /// §6.6: dAMP, dCMP, dGMP, dTMP, AMP, CMP, GMP, UMP and ATP (codes DA DC DG DT A C G U ATP).
    static let nucleotides: [BioMolecule] = BioBlockParser.parse(nucleotideText)
    /// §6.8: α- and β-glucose, galactose, fructose, ribose, maltose, cellobiose, sucrose, lactose (GLC BGC GAL FRU BDR
    /// MAL CBI SUC LAT).
    static let sugars: [BioMolecule] = BioBlockParser.parse(sugarText)
    /// §6.9: glycerol, palmitic, stearic and oleic acids, tristearin, POPC, cholesterol (GOL PLM STE OLA TGL POV CLR).
    static let lipids: [BioMolecule] = BioBlockParser.parse(lipidText)

    private static let byCode: [String: BioMolecule] = {
        var d: [String: BioMolecule] = [:]
        for m in residues + nucleotides + sugars + lipids where d[m.code] == nil { d[m.code] = m }
        return d
    }()

    /// A molecule block by its code: "ALA" (or "Ala", or one letter "A" for an amino acid — but "A" alone is AMP, so
    /// one-letter amino acids go through `residue(_:)`), "DA", "ATP", "GLC", "POV".
    static func molecule(_ code: String) -> BioMolecule? {
        byCode[code] ?? byCode[code.uppercased()]
    }

    /// An amino acid's template by its three-letter ("Lys", "LYS") or one-letter ("K") code.
    static func residue(_ code: String) -> BioMolecule? {
        if code.count == 1, let aa = BioAminoAcids.byOne[Character(code.uppercased())] {
            return residues.first { $0.code == aa.three.uppercased() }
        }
        return residues.first { $0.code == code.uppercased() }
    }

    // MARK: B-DNA (§6.7)

    /// An atom of the B-DNA model's strand-I nucleotide, in helical coordinates: x along the axis (Å), r from it (Å),
    /// θ about it (degrees).
    struct HelixAtom: Equatable {
        let name: String
        let x: Double
        let r: Double
        let theta: Double
        /// "C", "N", "O" or "P" (the helix has no H).
        var element: String { String(name.prefix(1)) }
    }

    /// §6.7: the sugar–phosphate backbone (the same for every base) and each base's atoms.
    static let bdna: (backbone: [HelixAtom], bases: [Character: [HelixAtom]]) = {
        var backbone: [HelixAtom] = []
        var bases: [Character: [HelixAtom]] = [:]
        var current: Character?
        for line in bdnaText.split(separator: "\n").map(String.init) {
            if line.hasPrefix("# base ") {
                current = line.dropFirst(7).first
                continue
            }
            if line.hasPrefix("#") { continue }
            let f = line.split(separator: " ").map(String.init)
            guard f.count == 4, let x = Double(f[1]), let r = Double(f[2]), let t = Double(f[3]) else { continue }
            let a = HelixAtom(name: f[0], x: x, r: r, theta: t)
            if let c = current { bases[c, default: []].append(a) } else { backbone.append(a) }
        }
        return (backbone, bases)
    }()

    /// §6.7's test vectors: strand, base pair, atom, and where it is in the app's frame.
    static let bdnaTests: [(strand: Int, pair: Int, name: String, pos: BioVec)] = {
        bdnaTestText.split(separator: "\n").compactMap { line in
            let f = line.split(separator: " ").map(String.init)
            // "strand 1 bp 0 P     X    0.000  Y    0.000  Z    8.910"
            guard f.count == 11, f[0] == "strand", let s = Int(f[1]), let k = Int(f[3]),
                  let x = Double(f[6]), let y = Double(f[8]), let z = Double(f[10]) else { return nil }
            return (s, k, f[4], BioVec(x, y, z))
        }
    }()

    /// An atom of a base pair drawn in 2D (§6.7): its base, name and place (Å; origin midway between the C1′ atoms, +x
    /// toward the pyrimidine's C1′, +y toward the major groove).
    struct PairAtom: Equatable {
        let base: Character
        let name: String
        let x: Double
        let y: Double
    }

    /// §6.7: the A·T and G·C pairs in 2D, keyed "AT" and "GC".
    static let basePairs2D: [String: [PairAtom]] = {
        var out: [String: [PairAtom]] = [:]
        var pair = "", base: Character = "?"
        for line in basePairText.split(separator: "\n").map(String.init) {
            if line.hasPrefix("# A·T") { pair = "AT"; continue }
            if line.hasPrefix("# G·C") { pair = "GC"; continue }
            if line.hasPrefix("# "), let b = line.dropFirst(2).first, "ATGC".contains(b) { base = b; continue }
            let f = line.split(separator: " ").map(String.init)
            guard f.count == 3, let x = Double(f[1]), let y = Double(f[2]), !pair.isEmpty else { continue }
            out[pair, default: []].append(PairAtom(base: base, name: f[0], x: x, y: y))
        }
        return out
    }()

    // MARK: Proteins (§6.10)

    /// A residue of a protein's trace: its chain, number, one-letter amino acid, secondary structure (H helix, E
    /// strand, C coil), CA atom and (ubiquitin) side-chain centroid, in Å.
    struct TraceResidue: Equatable {
        let chain: Character
        let i: Int
        let aa: Character
        let ss: Character
        let ca: BioVec
        var centroid: BioVec?
    }

    /// A heme of haemoglobin: its chain, the iron and the four pyrrole N atoms around it (NA, NB, NC, ND).
    struct Heme: Equatable {
        let chain: Character
        let fe: BioVec
        let n: [BioVec]
    }

    /// §6.10: ubiquitin (1UBQ), 76 residues, with side-chain centroids.
    static let ubiquitin: [TraceResidue] = ubiquitinText.split(separator: "\n").compactMap { line in
        let f = line.split(separator: " ").map(String.init)
        guard f.count == 9, let i = Int(f[0]), let aa = f[1].first, let ss = f[2].first else { return nil }
        let v = f[3...].compactMap(Double.init)
        guard v.count == 6 else { return nil }
        return TraceResidue(chain: "A", i: i, aa: aa, ss: ss, ca: BioVec(v[0], v[1], v[2]), centroid: BioVec(v[3], v[4], v[5]))
    }

    /// §6.10: deoxy-haemoglobin (2HHB): chains A and C α (141), B and D β (146), and the four hemes.
    static let hemoglobin: (residues: [TraceResidue], hemes: [Heme]) = {
        var rs: [TraceResidue] = []
        var hs: [Heme] = []
        for line in hemoglobinText.split(separator: "\n").map(String.init) where !line.hasPrefix("#") {
            let f = line.split(separator: " ").map(String.init)
            if f.count == 17, f[1] == "HEM", let c = f[0].first {
                let v = f[2...].compactMap(Double.init)
                guard v.count == 15 else { continue }
                let p = stride(from: 0, to: 15, by: 3).map { BioVec(v[$0], v[$0 + 1], v[$0 + 2]) }
                hs.append(Heme(chain: c, fe: p[0], n: Array(p[1...])))
            } else if f.count == 7, let c = f[0].first, let i = Int(f[1]), let aa = f[2].first, let ss = f[3].first,
                      let x = Double(f[4]), let y = Double(f[5]), let z = Double(f[6]) {
                rs.append(TraceResidue(chain: c, i: i, aa: aa, ss: ss, ca: BioVec(x, y, z)))
            }
        }
        return (rs, hs)
    }()

    // MARK: Transfer RNA (§6.11)

    /// A nucleotide of yeast tRNA-Phe: its number, base, P atom (C4′ for G1) in Å, and its modification's code (the
    /// PDB's, "2MG") where the real molecule has one.
    struct TRNANucleotide: Equatable {
        let i: Int
        let base: Character
        let p: BioVec
        var modified: String?
    }

    /// An element of the cloverleaf: its name, positions and pairs as the specification's table writes them.
    struct TRNAElement: Equatable {
        let name: String
        let positions: String
        let pairs: String
    }

    /// §6.11: yeast tRNA-Phe — its 76 nucleotides, its base pairs (i < j), its elements and its modified bases
    /// ("m²G", 10).
    static let trna: (nucleotides: [TRNANucleotide], pairs: [(Int, Int)], elements: [TRNAElement], modified: [(label: String, position: Int)]) = {
        let ns: [TRNANucleotide] = trnaText.split(separator: "\n").compactMap { line in
            guard !line.hasPrefix("#") else { return nil }
            let f = line.split(separator: " ").map(String.init)
            guard f.count >= 5, let i = Int(f[0]), let b = f[1].first, let x = Double(f[2]), let y = Double(f[3]), let z = Double(f[4]) else { return nil }
            let mod = f.count >= 6 ? f[5].trimmingCharacters(in: CharacterSet(charactersIn: "[]")) : nil
            return TRNANucleotide(i: i, base: b, p: BioVec(x, y, z), modified: mod)
        }
        var elements: [TRNAElement] = []
        var pairs: [(Int, Int)] = []
        for line in trnaStructureText.split(separator: "\n") {
            let c = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            guard c.count == 3 else { continue }
            elements.append(TRNAElement(name: c[0], positions: c[1], pairs: c[2]))
            // ("G1–C72", "G4·U69 (wobble)": a pair is two numbered bases joined by – or ·)
            for token in c[2].components(separatedBy: ", ") {
                let parts = token.split(separator: " ").first.map(String.init) ?? ""
                let sides = parts.components(separatedBy: CharacterSet(charactersIn: "–·"))
                guard sides.count == 2, let i = Int(sides[0].dropFirst()), let j = Int(sides[1].dropFirst()) else { continue }
                pairs.append((min(i, j), max(i, j)))
            }
        }
        let modified: [(label: String, position: Int)] = trnaModifiedText.components(separatedBy: ", ").compactMap { entry in
            let word = entry.split(separator: " ").first.map(String.init) ?? ""
            let digits = word.reversed().prefix(while: { $0.isASCII && $0.isNumber })
            guard !digits.isEmpty, let n = Int(String(digits.reversed())) else { return nil }
            return (String(word.dropLast(digits.count)), n)
        }
        return (ns, pairs, elements, modified)
    }()
}

// MARK: - Example genes (§6.12)

/// The example genes: the made-up teaching gene (labelled so in the UI) and human β-globin's coding sequence.
enum BioGenes {
    /// A stretch of the teaching gene: its name and its 1-based positions (inclusive) on the 112-nt coding strand.
    struct Part: Equatable {
        let name: String
        let start: Int
        let end: Int
        var range: ClosedRange<Int> { start...end }
    }

    /// The teaching gene (§6.12): coding strand 5′→3′, its parts, its marks, and what it becomes.
    struct TeachingGene {
        /// The coding (sense) strand, 5′→3′, 112 nt.
        let coding: String
        /// Promoter 1–31, 5′ UTR 32–44, Exon 1 45–55, Intron 56–82, Exon 2 83–101, 3′ UTR 102–112.
        let parts: [Part]
        /// TATA box 5–11.
        let tata = Part(name: "TATA box", start: 5, end: 11)
        /// The transcription start site (+1).
        let startSite = 32
        /// The start codon ATG, 45–47.
        let startCodon = Part(name: "Start codon", start: 45, end: 47)
        /// The stop codon TAA, 99–101.
        let stopCodon = Part(name: "Stop codon", start: 99, end: 101)
        /// The poly-A signal AATAAA, 104–109.
        let polyASignal = Part(name: "Poly-A signal", start: 104, end: 109)
        /// The template strand written 3′→5′ under the coding strand.
        let template = BioData.teachingTemplate
        let preMRNA = BioData.teachingPreMRNA
        /// Mature mRNA (cap and tail not shown).
        let matureMRNA = BioData.teachingMatureMRNA
        let cds = BioData.teachingCDS
        /// MAFSDWQKG.
        let protein = BioData.teachingProtein
        /// Read from the AUG with the intron kept: MAFR*.
        let unsplicedProtein = BioData.teachingUnspliced
        /// The mutation test vectors: mutation, mutant CDS (changed bases as the specification bolds them), protein, class.
        let mutations: [(mutation: String, cds: String, protein: String, kind: String)]
    }

    static let teaching: TeachingGene = {
        let names = ["Promoter", "5′ UTR", "Exon 1", "Intron", "Exon 2", "3′ UTR"]
        let chunks = (BioData.teachingGeneText.split(separator: "\n").first ?? "").split(separator: " ").map(String.init)
        var parts: [Part] = []
        var at = 1
        for (k, c) in chunks.enumerated() {
            parts.append(Part(name: k < names.count ? names[k] : "Part \(k + 1)", start: at, end: at + c.count - 1))
            at += c.count
        }
        let mutations: [(mutation: String, cds: String, protein: String, kind: String)] = BioData.teachingMutationText
            .split(separator: "\n").compactMap { line in
                let c = line.split(separator: "\t").map(String.init)
                return c.count == 4 ? (mutation: c[0], cds: c[1], protein: c[2], kind: c[3]) : nil
            }
        return TeachingGene(coding: chunks.joined(), parts: parts, mutations: mutations)
    }()

    /// Human β-globin (HBB), its coding sequence only (the real gene's 3 exons joined; NM_000518.5), 444 nt.
    static let hbbCDS = BioData.hbbCDS
    /// Its protein (NP_000509.1), 147 aa with the initiator Met.
    static let hbbProtein = BioData.hbbProtein
    /// The sickle-cell mutation: c.20A>T, codon 7 GAG → GTG (counting ATG as codon 1), p.Glu7Val = the classic Glu6Val.
    static let sickle = (position: 20, from: Character("A"), to: Character("T"), proteinStart: BioData.hbbSickleStart)
}
