import Foundation

// Crosses, worked exactly (spec §4.5, §5.3, §5.6.2): each gene's alleles and how they show (complete, incomplete and
// codominance, ABO, X-linked, a lethal homozygote), a parent's gametes as the product of its genes' alleles
// (independent assortment), the offspring as every pair of gametes, and the results the Cross page shows — genotypes
// and phenotypes in their conventional orders, their ratios kept apart (confusing them is a named exam error), daughters
// and sons apart for an X-linked gene, the dead left out of the born. Probabilities are fractions, never rounded.
// At most one X-linked gene in a cross (two on one X would be linked, not assorting independently).

/// How a gene's alleles show (§5.3.1).
enum BioInheritance: String, CaseIterable {
    case complete, incomplete, codominant, abo, xRecessive, xDominant
    /// The dominant allele's homozygote dies (yellow mice: A^Y A^Y), so A^Y a × A^Y a is born 2 : 1.
    case lethalDominantHomozygote
    /// The recessive homozygote dies.
    case lethalRecessive

    var label: String {
        switch self {
        case .complete: return "Complete dominance"
        case .incomplete: return "Incomplete dominance"
        case .codominant: return "Codominance"
        case .abo: return "ABO blood groups"
        case .xRecessive: return "X-linked recessive"
        case .xDominant: return "X-linked dominant"
        case .lethalDominantHomozygote: return "Lethal homozygote (dominant)"
        case .lethalRecessive: return "Lethal recessive"
        }
    }

    var isXLinked: Bool { self == .xRecessive || self == .xDominant }

    /// Shown as dominant or recessive (the phenotypic ratio's "dominant traits first").
    var hasDominance: Bool { self == .complete || isXLinked || self == .lethalDominantHomozygote || self == .lethalRecessive }
}

/// A gene of a cross: its letter, how it is inherited, its alleles (dominant first) and, if wanted, its phenotypes'
/// names.
struct BioGene: Equatable {
    /// The gene's letter, uppercase ("A").
    var letter: Character
    var pattern: BioInheritance
    /// Its alleles' symbols in order, dominant first: "A", "a"; "C^R", "C^W"; "I^A", "I^B", "i"; "X^A", "X^a", "Y".
    var alleles: [String]
    /// Its phenotypes' names, if not the usual ("A_" and "aa"; red, pink and white; A, B, AB and O; …): for a
    /// complete, X-linked or lethal gene [dominant, recessive]; for incomplete dominance [homozygote 1, heterozygote,
    /// homozygote 2].
    var traitNames: [String]?

    init(_ letter: Character, _ pattern: BioInheritance = .complete, alleles: [String]? = nil, traitNames: [String]? = nil) {
        let l = Character(letter.uppercased())
        self.letter = l
        self.pattern = pattern
        self.alleles = alleles ?? BioGene.defaultAlleles(l, pattern)
        self.traitNames = traitNames
    }

    static func defaultAlleles(_ l: Character, _ p: BioInheritance) -> [String] {
        let low = l.lowercased()
        switch p {
        case .complete, .lethalDominantHomozygote, .lethalRecessive: return [String(l), low]
        case .incomplete, .codominant: return ["\(l)^R", "\(l)^W"]
        case .abo: return ["I^A", "I^B", "i"]
        case .xRecessive, .xDominant: return ["X^\(l)", "X^\(low)", "Y"]
        }
    }

    /// The Y "allele" of an X-linked gene (its last).
    var yIndex: Int? { pattern.isXLinked ? alleles.count - 1 : nil }

    /// A genotype's text: "Aa", "C^R C^W", "I^A i", "X^A Y".
    func text(_ g: [Int]) -> String {
        let syms = g.map { alleles[$0] }
        return syms.allSatisfy({ $0.count == 1 }) ? syms.joined() : syms.joined(separator: " ")
    }

    /// How a genotype (two allele indices, sorted) shows.
    func phenotype(_ g: [Int]) -> BioPhenotype {
        let i = g.first ?? 0, j = g.count > 1 ? g[1] : i
        let names = traitNames
        func named(_ k: Int, _ fallback: String) -> String { (names?.count ?? 0) > k ? names![k] : fallback }
        let low = letter.lowercased()
        switch pattern {
        case .complete:
            return i == 0 ? BioPhenotype(named(0, "\(letter)_"), dominant: true) : BioPhenotype(named(1, "\(low)\(low)"), dominant: false)
        case .lethalDominantHomozygote:
            if i == 0 && j == 0 { return BioPhenotype("dies", dominant: true, dies: true) }
            return i == 0 ? BioPhenotype(named(0, "\(letter)_"), dominant: true) : BioPhenotype(named(1, "\(low)\(low)"), dominant: false)
        case .lethalRecessive:
            if i == 0 { return BioPhenotype(named(0, "\(letter)_"), dominant: true) }
            return BioPhenotype("dies", dominant: false, dies: true)
        case .incomplete:
            let k = i == j ? (i == 0 ? 0 : 2) : 1
            return BioPhenotype(named(k, ["red", "pink", "white"][k]), dominant: false)
        case .codominant:
            let sups = Set([i, j]).sorted().map { BioGene.superscript(alleles[$0]) }
            return BioPhenotype(named(i == j ? (i == 0 ? 0 : 2) : 1, sups.joined()), dominant: false)
        case .abo:
            let s = Set([i, j])
            let label = s == [0] || s == [0, 2] ? "A" : (s == [1] || s == [1, 2] ? "B" : (s == [0, 1] ? "AB" : "O"))
            return BioPhenotype(label, dominant: false)
        case .xRecessive:
            let xs = [i, j].filter { $0 != yIndex }
            let affected = !xs.contains(0)
            let carrier = !affected && xs.count == 2 && xs.contains(1)
            return BioPhenotype(affected ? named(1, "affected") : named(0, "unaffected"), dominant: !affected, carrier: carrier)
        case .xDominant:
            let affected = [i, j].contains(0)
            return BioPhenotype(affected ? named(0, "affected") : named(1, "unaffected"), dominant: affected)
        }
    }

    /// "C^R" → "R"; "A" → "A".
    static func superscript(_ allele: String) -> String {
        if let r = allele.range(of: "^") { return String(allele[r.upperBound...]) }
        return allele
    }
}

/// How one gene shows in an individual.
struct BioPhenotype: Hashable {
    let label: String
    /// Shows the dominant trait (complete, lethal and X-linked genes: these come first in a phenotypic ratio).
    var dominant: Bool
    var dies: Bool = false
    /// X-linked recessive: an unaffected female with one recessive allele.
    var carrier: Bool = false

    init(_ label: String, dominant: Bool, dies: Bool = false, carrier: Bool = false) {
        self.label = label
        self.dominant = dominant
        self.dies = dies
        self.carrier = carrier
    }
}

/// A sex, for X-linked genes.
enum BioSex: String { case female, male }

/// A cross: its genes and its two parents' genotypes (per gene, two allele indices — an X-linked father's an X and Y).
/// Parent 1 is the mother when a gene is X-linked.
struct BioCross {
    var genes: [BioGene]
    var parent1: [[Int]]
    var parent2: [[Int]]

    init(genes: [BioGene], parent1: [[Int]], parent2: [[Int]]) {
        self.genes = genes
        self.parent1 = parent1.map { $0.sorted() }
        self.parent2 = parent2.map { $0.sorted() }
    }

    /// A gamete: one allele index per gene.
    typealias Gamete = [Int]

    /// A parent's gametes (§5.6.2): the Cartesian product of its genes' alleles (1/2 each from a heterozygote, 1 from
    /// a homozygote), identical gametes merged, in canonical order.
    static func gametes(_ genotype: [[Int]]) -> [(gamete: Gamete, p: BioFrac)] {
        var out: [(Gamete, BioFrac)] = [([], .one)]
        for g in genotype {
            let choices: [(Int, BioFrac)] = g.count == 2 && g[0] != g[1] ? [(g[0], BioFrac(1, 2)), (g[1], BioFrac(1, 2))] : [(g.first ?? 0, .one)]
            var next: [(Gamete, BioFrac)] = []
            for (partial, p) in out {
                for (a, q) in choices { next.append((partial + [a], p * q)) }
            }
            out = next
        }
        var merged: [Gamete: BioFrac] = [:]
        for (g, p) in out { merged[g] = (merged[g] ?? .zero) + p }
        return merged.keys.sorted { $0.lexicographicallyPrecedes($1) }.map { (gamete: $0, p: merged[$0] ?? .zero) }
    }

    var gametes1: [(gamete: Gamete, p: BioFrac)] { BioCross.gametes(parent1) }
    var gametes2: [(gamete: Gamete, p: BioFrac)] { BioCross.gametes(parent2) }

    /// The offspring genotype from two gametes: each gene's allele pair, sorted (dominant first, X before Y).
    func child(_ a: Gamete, _ b: Gamete) -> [[Int]] {
        zip(a, b).map { [$0.0, $0.1].sorted() }
    }

    /// The Punnett square: parent 1's gametes down the side, parent 2's across the top.
    var square: [[[[Int]]]] {
        gametes1.map { g1 in gametes2.map { g2 in child(g1.gamete, g2.gamete) } }
    }

    /// The offspring's sex, when a gene is X-linked (a Y from the father makes a son).
    func sex(_ genotype: [[Int]]) -> BioSex? {
        for (k, g) in genes.enumerated() where g.pattern.isXLinked {
            return genotype[k].contains(g.yIndex ?? -1) ? .male : .female
        }
        return nil
    }

    var hasXLinked: Bool { genes.contains { $0.pattern.isXLinked } }

    /// Every offspring genotype and its probability (merged), in canonical order.
    var offspring: [(genotype: [[Int]], p: BioFrac)] {
        var d: [[[Int]]: BioFrac] = [:]
        for (g1, p1) in gametes1 {
            for (g2, p2) in gametes2 {
                let c = child(g1, g2)
                d[c] = (d[c] ?? .zero) + p1 * p2
            }
        }
        return d.keys.sorted { BioCross.precedes($0, $1) }.map { (genotype: $0, p: d[$0] ?? .zero) }
    }

    static func precedes(_ a: [[Int]], _ b: [[Int]]) -> Bool {
        for (x, y) in zip(a, b) where x != y { return x.lexicographicallyPrecedes(y) }
        return false
    }

    func genotypeText(_ g: [[Int]]) -> String {
        let parts = zip(genes, g).map { $0.0.text($0.1) }
        return parts.allSatisfy({ !$0.contains(" ") && !$0.contains("^") }) ? parts.joined() : parts.joined(separator: " ")
    }

    func phenotypes(_ g: [[Int]]) -> [BioPhenotype] { zip(genes, g).map { $0.0.phenotype($0.1) } }

    func phenotypeText(_ g: [[Int]]) -> String {
        let ps = phenotypes(g)
        let symbolic = zip(genes, ps).allSatisfy { gene, p in gene.pattern == .complete && gene.traitNames == nil && !p.label.contains(" ") }
        return symbolic ? ps.map(\.label).joined() : ps.map(\.label).joined(separator: ", ")
    }

    /// The results, for everyone, or for daughters or sons alone (probabilities then within that sex).
    func result(sex only: BioSex? = nil) -> BioCrossResult {
        let n1 = gametes1.count, n2 = gametes2.count
        var cells: [[[Int]]: Int] = [:]
        for g1 in gametes1 { for g2 in gametes2 { cells[child(g1.gamete, g2.gamete), default: 0] += 1 } }
        var outs = offspring
        if let s = only { outs = outs.filter { sex($0.genotype) == s } }
        let total = outs.reduce(BioFrac.zero) { $0 + $1.p }
        guard !total.isZero else { return BioCrossResult(genotypes: [], phenotypes: [], squareSize: n1 * n2, bornFraction: .zero) }
        var rows: [BioCrossResult.GenotypeRow] = []
        for o in outs {
            let ps = phenotypes(o.genotype)
            rows.append(BioCrossResult.GenotypeRow(text: genotypeText(o.genotype), genotype: o.genotype, count: cells[o.genotype] ?? 0,
                                                   p: o.p / total, phenotype: phenotypeText(o.genotype),
                                                   dies: ps.contains { $0.dies }, carrier: ps.contains { $0.carrier }))
        }
        // (the phenotypes of the born: in order of dominant traits shown, most first, then by gene in the order each
        // gene's phenotypes first appear among the genotypes)
        let born = rows.filter { !$0.dies }
        let bornTotal = born.reduce(BioFrac.zero) { $0 + $1.p }
        var firstSeen: [[String: Int]] = Array(repeating: [:], count: genes.count)
        for r in rows {
            for (k, p) in phenotypes(r.genotype).enumerated() where firstSeen[k][p.label] == nil {
                firstSeen[k][p.label] = firstSeen[k].count
            }
        }
        var groups: [String: (p: BioFrac, key: (Int, [Int]), carriers: BioFrac)] = [:]
        var order: [String] = []
        for r in born {
            let ps = phenotypes(r.genotype)
            let dominantCount = zip(genes, ps).filter { $0.0.pattern.hasDominance && $0.1.dominant }.count
            let key = (-dominantCount, ps.enumerated().map { firstSeen[$0.offset][$0.element.label] ?? 0 })
            if groups[r.phenotype] == nil { order.append(r.phenotype) }
            let old = groups[r.phenotype]
            groups[r.phenotype] = ((old?.p ?? .zero) + r.p, key, (old?.carriers ?? .zero) + (r.carrier ? r.p : .zero))
        }
        order.sort { a, b in
            guard let x = groups[a]?.key, let y = groups[b]?.key else { return false }
            return x.0 != y.0 ? x.0 < y.0 : x.1.lexicographicallyPrecedes(y.1)
        }
        let phen = order.map { name -> BioCrossResult.PhenotypeRow in
            let g = groups[name]
            let p = bornTotal.isZero ? BioFrac.zero : (g?.p ?? .zero) / bornTotal
            let c = bornTotal.isZero ? BioFrac.zero : (g?.carriers ?? .zero) / bornTotal
            return BioCrossResult.PhenotypeRow(text: name, p: p, carriers: c)
        }
        return BioCrossResult(genotypes: rows, phenotypes: phen, squareSize: n1 * n2, bornFraction: bornTotal)
    }

    /// The chance of one gene's genotype in the offspring (that gene's own small Punnett square).
    func probability(gene k: Int, of genotypes: Set<[Int]>) -> BioFrac {
        let a = BioCross.gametes([parent1[k]]), b = BioCross.gametes([parent2[k]])
        var p = BioFrac.zero
        for (x, px) in a { for (y, py) in b where genotypes.contains([x[0], y[0]].sorted()) { p = p + px * py } }
        return p
    }

    /// The product rule (§4.5 Probability): the chance of a genotype asked gene by gene (AA, Aa, aa, A_ or any, for a
    /// two-allele gene), each gene's factor and their product.
    func probability(of targets: [BioTarget]) -> (factors: [BioFrac], product: BioFrac) {
        var fs: [BioFrac] = []
        for (k, t) in targets.enumerated() where k < genes.count {
            fs.append(probability(gene: k, of: t.genotypes))
        }
        return (fs, fs.reduce(BioFrac.one, *))
    }

    // MARK: Typed genotypes

    /// A cross typed as text: "AaBb × aaBb", "AaBb x aaBb", "I^A i × I^B i", "X^A X^a × X^a Y", "C^RC^W x C^RC^W".
    /// Genes are taken in the order they first appear; "aA" is read as "Aa". Inheritance is guessed from the symbols
    /// (X^ X-linked recessive, I^A/I^B ABO, another ^ incomplete dominance, else complete) and can be changed after.
    static func parse(_ text: String) -> (cross: BioCross?, error: String?) {
        let sides = text.replacingOccurrences(of: "×", with: " x ").components(separatedBy: " x ")
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        guard sides.count == 2 else { return (nil, "Write two genotypes with × between them, like AaBb × aaBb.") }
        let t1 = tokens(sides[0]), t2 = tokens(sides[1])
        guard !t1.isEmpty, !t2.isEmpty else { return (nil, "Each parent needs its alleles, like Aa.") }
        // (each gene by its key: its letter, or "X:a" for an X-linked gene's letter, or "I" for ABO)
        var order: [String] = []
        var symbols: [String: [String]] = [:]
        for tok in t1 + t2 {
            let k = geneKey(tok, all: t1 + t2)
            if symbols[k] == nil { order.append(k); symbols[k] = [] }
            if !(symbols[k] ?? []).contains(tok) { symbols[k]?.append(tok) }
        }
        var genes: [BioGene] = []
        for k in order {
            let syms = symbols[k] ?? []
            if k.hasPrefix("X:") {
                let l = Character(String(k.dropFirst(2)))
                genes.append(BioGene(l, .xRecessive))
            } else if k == "I", syms.contains(where: { $0 == "I^A" || $0 == "I^B" }) {
                genes.append(BioGene("I", .abo))
            } else if syms.contains(where: { $0.contains("^") }) {
                // (superscripts: incomplete dominance, the alleles as typed, an uppercase plain one first, else typed order)
                let l = Character(k)
                let sorted = syms.enumerated().sorted { a, b in
                    rank(a.element) != rank(b.element) ? rank(a.element) < rank(b.element) : a.offset < b.offset
                }.map(\.element)
                let plainLow = syms.contains { !$0.contains("^") && $0 == $0.lowercased() }
                genes.append(BioGene(l, plainLow ? .complete : .incomplete, alleles: sorted))
            } else {
                genes.append(BioGene(Character(k), .complete))
            }
        }
        func genotype(_ toks: [String]) -> [[Int]]? {
            var out: [[Int]] = Array(repeating: [], count: genes.count)
            for tok in toks {
                let k = geneKey(tok, all: t1 + t2)
                guard let gi = order.firstIndex(of: k) else { return nil }
                let gene = genes[gi]
                guard let ai = gene.alleles.firstIndex(of: tok) else { return nil }
                out[gi].append(ai)
            }
            return out
        }
        guard var g1 = genotype(t1), var g2 = genotype(t2) else { return (nil, "Those alleles could not be read.") }
        // (a lone Y token joins the X-linked gene)
        if let xi = genes.firstIndex(where: { $0.pattern.isXLinked }), let yi = order.firstIndex(of: "Y") {
            for gi in [0, 1] {
                var g = gi == 0 ? g1 : g2
                if !g[yi].isEmpty { g[xi].append(genes[xi].alleles.count - 1) }
                if gi == 0 { g1 = g } else { g2 = g }
            }
            genes.remove(at: yi)
            g1.remove(at: yi)
            g2.remove(at: yi)
        }
        for (k, gene) in genes.enumerated() {
            guard g1[k].count == 2, g2[k].count == 2 else {
                return (nil, "Each parent needs two alleles of \(gene.letter) (an X-linked gene: X X for the mother, X Y for the father).")
            }
        }
        return (BioCross(genes: genes, parent1: g1, parent2: g2), nil)
    }

    /// A genotype's allele tokens: "AaBb" → A a B b; "C^RC^W" → C^R C^W; "X^AY" → X^A Y.
    static func tokens(_ s: String) -> [String] {
        let c = Array(s.filter { !$0.isWhitespace })
        var out: [String] = []
        var i = 0
        while i < c.count {
            guard c[i].isLetter else { i += 1; continue }
            if i + 2 < c.count, c[i + 1] == "^" {
                out.append(String([c[i], "^", c[i + 2]]))
                i += 3
            } else {
                out.append(String(c[i]))
                i += 1
            }
        }
        return out
    }

    /// Which gene an allele token belongs to.
    private static func geneKey(_ tok: String, all: [String]) -> String {
        if tok.hasPrefix("X^") { return "X:" + tok.dropFirst(2).uppercased() }
        if tok == "Y", all.contains(where: { $0.hasPrefix("X^") }) { return "Y" }
        if tok == "i" || tok.hasPrefix("I^"), all.contains(where: { $0 == "I^A" || $0 == "I^B" }) { return "I" }
        return String(tok.prefix(1)).uppercased()
    }

    /// Dominant first: an uppercase symbol before a lowercase one, then as typed.
    private static func rank(_ tok: String) -> Int {
        guard let f = tok.first else { return 2 }
        return f.isUppercase ? 0 : 1
    }
}

/// What the Probability page asks of one gene.
enum BioTarget: String, CaseIterable {
    case homDominant = "AA", heterozygous = "Aa", homRecessive = "aa", dominant = "A_", any = "any"

    /// The genotypes (allele indices, sorted) it means for a two-allele gene.
    var genotypes: Set<[Int]> {
        switch self {
        case .homDominant: return [[0, 0]]
        case .heterozygous: return [[0, 1]]
        case .homRecessive: return [[1, 1]]
        case .dominant: return [[0, 0], [0, 1]]
        case .any: return [[0, 0], [0, 1], [1, 1]]
        }
    }
}

/// A cross's results: its genotypes (all, the dead marked) and its born phenotypes, with their ratios.
struct BioCrossResult {
    struct GenotypeRow {
        let text: String
        let genotype: [[Int]]
        /// Cells of the Punnett square with it.
        let count: Int
        /// Its share of the offspring (of this sex, when split).
        let p: BioFrac
        let phenotype: String
        let dies: Bool
        let carrier: Bool
    }

    struct PhenotypeRow {
        let text: String
        /// Its share of the born.
        let p: BioFrac
        /// The share of the born that show it and are carriers (X-linked recessive).
        let carriers: BioFrac
    }

    let genotypes: [GenotypeRow]
    let phenotypes: [PhenotypeRow]
    let squareSize: Int
    /// The share of offspring born alive (1 unless a homozygote is lethal).
    let bornFraction: BioFrac

    /// "1 : 2 : 1": every genotype, the dead included.
    var genotypicRatio: [Int] { BioRatio.whole(genotypes.map(\.p)) }
    /// The born genotypes' ratio ("2 : 1" for A^Y a × A^Y a).
    var bornGenotypicRatio: [Int] { BioRatio.whole(genotypes.filter { !$0.dies }.map(\.p)) }
    var phenotypicRatio: [Int] { BioRatio.whole(phenotypes.map(\.p)) }

    /// "1 AA : 2 Aa : 1 aa".
    var genotypicRatioText: String {
        zip(genotypicRatio, genotypes).map { "\($0.0) \($0.1.text)" }.joined(separator: " : ")
    }

    /// "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb".
    var phenotypicRatioText: String {
        zip(phenotypicRatio, phenotypes).map { "\($0.0) \($0.1.text)" }.joined(separator: " : ")
    }
}
