import Foundation

// Genetics Crosses's own model (spec §4.5, §5.3): the cross as the Cross page edits it — one to three genes, each with
// its letter, alleles and pattern from §5.3.1's table, and each parent's genotypes — and what the pages show of it: the
// gametes, the Punnett square tinted by phenotype, the forked-line diagram, the product and sum rules and the chance over
// several children, and the chi-square test with its null hypothesis written out. The arithmetic is BioCross's
// (BioGenetics.swift), exact throughout. Foundation only, so the Models job compiles and checks it with the rest of
// Biology/Model; the pedigree and its mode checker are in PedigreeModel.swift.

// MARK: - Examples (§5.3.1)

/// A gene from §5.3.1's table, ready to cross: the gene, the parents it starts from, and a line on what it shows.
struct CrossExample: Identifiable {
    let id: String
    let title: String
    let gene: BioGene
    let parent1: [Int]
    let parent2: [Int]
    let note: String
}

enum CrossExamples {
    static let all: [CrossExample] = [
        CrossExample(id: "pea", title: "Pea seed shape", gene: BioGene("R", .complete, traitNames: ["round", "wrinkled"]),
                     parent1: [0, 1], parent2: [0, 1], note: "R (round) is dominant to r (wrinkled): RR and Rr are round, rr is wrinkled."),
        CrossExample(id: "snapdragon", title: "Snapdragon colour", gene: BioGene("C", .incomplete, alleles: ["C^R", "C^W"], traitNames: ["red", "pink", "white"]),
                     parent1: [0, 1], parent2: [0, 1], note: "Incomplete dominance: the heterozygote is in between, so C^R C^W is pink."),
        CrossExample(id: "mn", title: "MN blood group", gene: BioGene("L", .codominant, alleles: ["L^M", "L^N"], traitNames: ["M", "MN", "N"]),
                     parent1: [0, 1], parent2: [0, 1], note: "Codominance: L^M L^N makes both the M and the N antigen."),
        CrossExample(id: "roan", title: "Roan cattle", gene: BioGene("C", .codominant, alleles: ["C^R", "C^W"], traitNames: ["red", "roan", "white"]),
                     parent1: [0, 1], parent2: [0, 1], note: "Codominance: a roan coat has red hairs and white hairs side by side, not pink ones."),
        CrossExample(id: "abo", title: "ABO blood groups", gene: BioGene("I", .abo),
                     parent1: [0, 2], parent2: [1, 2], note: "I^A and I^B are codominant and both dominant to i: I^A i is type A, I^A I^B is AB, ii is O."),
        CrossExample(id: "colourblind", title: "Red–green colour blindness", gene: BioGene("B", .xRecessive, traitNames: ["normal vision", "colour-blind"]),
                     parent1: [0, 1], parent2: [0, 2], note: "X-linked recessive: a son's only X comes from his mother, so a carrier mother passes it to half her sons."),
        CrossExample(id: "haemophilia", title: "Haemophilia A", gene: BioGene("H", .xRecessive, traitNames: ["unaffected", "haemophilia"]),
                     parent1: [0, 1], parent2: [0, 2], note: "X-linked recessive: males are hemizygous, so one X^h is enough to show it."),
        CrossExample(id: "rickets", title: "Hypophosphataemic rickets", gene: BioGene("R", .xDominant, traitNames: ["rickets", "unaffected"]),
                     parent1: [1, 1], parent2: [0, 2], note: "X-linked dominant: an affected father passes it to every daughter and to no son."),
        CrossExample(id: "yellow", title: "Yellow mice", gene: BioGene("A", .lethalDominantHomozygote, alleles: ["A^Y", "a"], traitNames: ["yellow", "non-yellow"]),
                     parent1: [0, 1], parent2: [0, 1], note: "A^Y A^Y dies before birth, so A^Y a × A^Y a is born 2 yellow : 1 non-yellow."),
        CrossExample(id: "lethalRecessive", title: "A recessive lethal", gene: BioGene("A", .lethalRecessive),
                     parent1: [0, 1], parent2: [0, 1], note: "aa dies before birth, so Aa × Aa is born 1 AA : 2 Aa, all showing the dominant trait."),
    ]

    static func find(_ id: String) -> CrossExample? { all.first { $0.id == id } }
}

// MARK: - The cross being edited

/// The cross on the Cross page: its genes (one to three, at most one X-linked) and the two parents' genotypes per gene
/// (allele indices, sorted). Parent 1 is the mother when a gene is X-linked; parent 2 the father, an X and the Y.
struct CrossSetup: Equatable {
    var genes: [BioGene]
    var parent1: [[Int]]
    var parent2: [[Int]]
    /// The example each gene was set from, if any (its menu ticks it).
    var examples: [String?]

    static let maxGenes = 3
    /// The letters a gene may take: not I (ABO's), X or Y (the X-linked notation's).
    static let letters: [Character] = Array("ABCDEFGHJKLMNOPQRSTUVWZ")

    init(genes: [BioGene], parent1: [[Int]], parent2: [[Int]], examples: [String?]? = nil) {
        self.genes = genes
        self.parent1 = parent1.map { $0.sorted() }
        self.parent2 = parent2.map { $0.sorted() }
        self.examples = examples ?? Array(repeating: nil, count: genes.count)
    }

    init(_ c: BioCross) { self.init(genes: c.genes, parent1: c.parent1, parent2: c.parent2) }

    /// Aa × Aa: where the page starts.
    static let standard = CrossSetup(genes: [BioGene("A")], parent1: [[0, 1]], parent2: [[0, 1]])

    var cross: BioCross { BioCross(genes: genes, parent1: parent1, parent2: parent2) }

    /// The X-linked gene, if there is one.
    var xIndex: Int? { genes.firstIndex { $0.pattern.isXLinked } }

    /// The cross as it is written: "AaBb × aaBb", "X^A X^a × X^A Y".
    var text: String {
        let c = cross
        return c.genotypeText(parent1) + " × " + c.genotypeText(parent2)
    }

    /// The genotypes a parent may have for a gene (what its menu lists): an X-linked gene's mother X X and father X Y;
    /// never a lethal homozygote (it dies before it could be a parent).
    func genotypes(gene k: Int, parent: Int) -> [[Int]] {
        guard genes.indices.contains(k) else { return [] }
        let g = genes[k]
        var out: [[Int]] = []
        if g.pattern.isXLinked {
            out = parent == 1 ? [[0, 0], [0, 1], [1, 1]] : [[0, 2], [1, 2]]
        } else {
            let n = g.alleles.count
            for i in 0..<n { for j in i..<n { out.append([i, j]) } }
        }
        switch g.pattern {
        case .lethalDominantHomozygote: out.removeAll { $0 == [0, 0] }
        case .lethalRecessive: out.removeAll { $0 == [1, 1] }
        default: break
        }
        return out
    }

    /// Whether gene k may take a pattern: one X-linked gene at a time (two on one X would be linked), one ABO gene.
    func allowed(_ p: BioInheritance, gene k: Int) -> Bool {
        let others = genes.indices.filter { $0 != k }.map { genes[$0] }
        if p.isXLinked && others.contains(where: { $0.pattern.isXLinked }) { return false }
        if p == .abo && others.contains(where: { $0.pattern == .abo || $0.letter == "I" }) { return false }
        return true
    }

    /// The letters gene k may change to (none for ABO, whose gene is I).
    func freeLetters(gene k: Int) -> [Character] {
        let used = Set(genes.indices.filter { $0 != k }.map { genes[$0].letter })
        return CrossSetup.letters.filter { !used.contains($0) }
    }

    private func freeLetter(except k: Int? = nil) -> Character {
        let used = Set(genes.indices.filter { $0 != k }.map { genes[$0].letter })
        return CrossSetup.letters.first { !used.contains($0) } ?? "Z"
    }

    /// A gene's pattern changed: its alleles become the pattern's own, and each parent keeps what it can (dominant stays
    /// dominant, recessive recessive; an X-linked father keeps his X).
    mutating func setPattern(_ p: BioInheritance, gene k: Int) {
        guard genes.indices.contains(k), genes[k].pattern != p, allowed(p, gene: k) else { return }
        let old = genes[k]
        var letter = old.letter
        if p == .abo { letter = "I" } else if old.pattern == .abo { letter = freeLetter(except: k) }
        // (within one way of writing alleles — A and a, or C^R and C^W — the gene keeps its own symbols and names:
        // A^Y and a stay A^Y and a when the yellow allele is made lethal)
        let family = { (x: BioInheritance) -> Int in
            switch x {
            case .complete, .lethalDominantHomozygote, .lethalRecessive: return 0
            case .incomplete, .codominant: return 1
            case .abo: return 2
            case .xRecessive, .xDominant: return 3
            }
        }
        let new = family(old.pattern) == family(p) ? BioGene(letter, p, alleles: old.alleles, traitNames: old.traitNames) : BioGene(letter, p)
        parent1[k] = CrossSetup.remap(parent1[k], from: old, to: new, parent: 1)
        parent2[k] = CrossSetup.remap(parent2[k], from: old, to: new, parent: 2)
        genes[k] = new
        examples[k] = nil
        keepAlive(k)
    }

    /// An allele's rank: 0 dominant, 1 recessive, 2 the Y.
    private static func rank(_ i: Int, _ g: BioGene) -> Int {
        if g.pattern.isXLinked { return i }
        if g.pattern == .abo { return i == 2 ? 1 : 0 }
        return min(i, 1)
    }

    private static func remap(_ genotype: [Int], from old: BioGene, to new: BioGene, parent: Int) -> [Int] {
        let ranks = genotype.map { rank($0, old) }
        let xs = ranks.filter { $0 < 2 }
        if new.pattern.isXLinked {
            if parent == 1 { return [xs.first ?? 0, xs.count > 1 ? xs[1] : (xs.first ?? 0)].sorted() }
            return [xs.min() ?? 0, 2]
        }
        if new.pattern == .abo {
            // (parent 1's dominant allele becomes I^A, parent 2's I^B: Aa × Aa becomes I^A i × I^B i)
            return ranks.map { $0 == 0 ? (parent == 1 ? 0 : 1) : 2 }.sorted()
        }
        let a = xs.first ?? 0
        return [a, xs.count > 1 ? xs[1] : a].sorted()
    }

    /// A lethal homozygote can't be a parent: it becomes the heterozygote.
    private mutating func keepAlive(_ k: Int) {
        let ok1 = genotypes(gene: k, parent: 1), ok2 = genotypes(gene: k, parent: 2)
        if !ok1.contains(parent1[k]) { parent1[k] = ok1.contains([0, 1]) ? [0, 1] : (ok1.first ?? [0, 1]) }
        if !ok2.contains(parent2[k]) { parent2[k] = ok2.contains([0, 1]) ? [0, 1] : (ok2.first ?? [0, 1]) }
    }

    /// A gene's letter changed: its alleles follow ("C^R" → "D^R", "X^a" → "X^d"; ABO keeps I).
    mutating func setLetter(_ l: Character, gene k: Int) {
        guard genes.indices.contains(k), genes[k].pattern != .abo, freeLetters(gene: k).contains(l) else { return }
        let old = genes[k]
        let alleles = old.alleles.map { a -> String in
            if old.pattern.isXLinked {
                if a == "Y" { return a }
                return "X^" + (BioGene.superscript(a).first?.isUppercase == true ? String(l) : l.lowercased())
            }
            guard let f = a.first, f.uppercased() == String(old.letter) else { return a }
            return (f.isUppercase ? String(l) : l.lowercased()) + a.dropFirst()
        }
        genes[k] = BioGene(l, old.pattern, alleles: alleles, traitNames: old.traitNames)
    }

    /// A gene more (up to three): the next free letter, complete dominance, both parents heterozygous.
    mutating func addGene() {
        guard genes.count < CrossSetup.maxGenes else { return }
        genes.append(BioGene(freeLetter()))
        parent1.append([0, 1])
        parent2.append([0, 1])
        examples.append(nil)
    }

    mutating func removeGene(_ k: Int) {
        guard genes.count > 1, genes.indices.contains(k) else { return }
        genes.remove(at: k)
        parent1.remove(at: k)
        parent2.remove(at: k)
        examples.remove(at: k)
    }

    /// Gene k set from an example (a free letter if its own is taken); false if the pattern can't be had here.
    @discardableResult
    mutating func applyExample(_ e: CrossExample, gene k: Int) -> Bool {
        guard genes.indices.contains(k), allowed(e.gene.pattern, gene: k) else { return false }
        genes[k] = e.gene
        parent1[k] = e.parent1.sorted()
        parent2[k] = e.parent2.sorted()
        examples[k] = e.id
        if genes.indices.filter({ $0 != k }).contains(where: { genes[$0].letter == e.gene.letter }) {
            let l = freeLetter(except: k)
            let id = examples[k]
            setLetter(l, gene: k)
            examples[k] = id
        }
        return true
    }

    /// The test cross: parent 2 made the homozygous recessive for every gene, the way to read an unknown genotype from
    /// its offspring. Returns a line for each gene that has no homozygous recessive to cross with.
    @discardableResult
    mutating func testCross() -> [String] {
        var notes: [String] = []
        for (k, g) in genes.enumerated() {
            switch g.pattern {
            case .xRecessive, .xDominant: parent2[k] = [1, 2]
            case .abo: parent2[k] = [2, 2]
            case .lethalRecessive:
                notes.append("\(g.text([1, 1])) dies before birth, so gene \(g.letter) can't be test-crossed; it was left as it was.")
            case .incomplete, .codominant:
                parent2[k] = [1, 1]
                notes.append("Gene \(g.letter) has no recessive allele; \(g.text([1, 1])) is used, and every genotype shows anyway.")
            case .complete, .lethalDominantHomozygote: parent2[k] = [1, 1]
            }
        }
        return notes
    }

    /// A cross typed as text ("AaBb × aaBb", "X^A X^a × X^a Y", "I^A i × I^B i", "C^R C^W x C^R C^W"), patterns guessed
    /// by BioCross.parse; with an X-linked gene the mother (X X) is put first.
    static func parse(_ text: String) -> (setup: CrossSetup?, error: String?) {
        let r = BioCross.parse(text)
        guard var c = r.cross else { return (nil, r.error ?? "That cross could not be read.") }
        if c.genes.count > maxGenes { return (nil, "Up to three genes at a time.") }
        let xs = c.genes.indices.filter { c.genes[$0].pattern.isXLinked }
        if xs.count > 1 {
            return (nil, "One X-linked gene at a time: two genes on the X are linked, so they don't assort independently.")
        }
        if let x = xs.first {
            let y = c.genes[x].alleles.count - 1
            let ys1 = c.parent1[x].filter { $0 == y }.count, ys2 = c.parent2[x].filter { $0 == y }.count
            if ys1 > 1 || ys2 > 1 { return (nil, "Nobody has two Y chromosomes: write the father as X Y.") }
            if ys1 == ys2 {
                return (nil, ys1 == 0 ? "One parent must be the father, written X Y (like X^A Y)." : "Only one parent can be the father (X Y): the other is the mother, X X.")
            }
            if ys1 == 1 { c = BioCross(genes: c.genes, parent1: c.parent2, parent2: c.parent1) }
        }
        // (a gene written with superscripts has two alleles: a third is ABO's alone; one seen in both parents, as in
        // C^R C^R × C^R C^R, gets its partner, so the gene can still be changed and asked about)
        for (k, g) in c.genes.enumerated() where g.pattern != .abo && !g.pattern.isXLinked {
            if g.alleles.count > 2 { return (nil, "Gene \(g.letter) has more than two alleles: only ABO (I^A, I^B, i) has three here.") }
            if g.alleles.count == 1, let only = g.alleles.first {
                let sup = BioGene.superscript(only)
                let other = only.contains("^") ? String(g.letter) + "^" + (sup == "W" ? "R" : "W") : (only == only.uppercased() ? only.lowercased() : only.uppercased())
                var genes = c.genes
                genes[k] = BioGene(g.letter, g.pattern, alleles: only == only.lowercased() && !only.contains("^") ? [other, only] : [only, other], traitNames: g.traitNames)
                // (the indices move if the new allele went first)
                let shift = genes[k].alleles[0] == other ? 1 : 0
                let p1 = c.parent1.enumerated().map { $0.offset == k ? $0.element.map { $0 + shift } : $0.element }
                let p2 = c.parent2.enumerated().map { $0.offset == k ? $0.element.map { $0 + shift } : $0.element }
                c = BioCross(genes: genes, parent1: p1, parent2: p2)
            }
        }
        return (CrossSetup(c), nil)
    }

    // MARK: Remembered between launches

    private struct Stored: Codable {
        var genes: [StoredGene]
        var p1: [[Int]]
        var p2: [[Int]]
        var examples: [String?]
    }

    private struct StoredGene: Codable {
        var letter: String
        var pattern: String
        var alleles: [String]
        var traits: [String]?
    }

    /// The setup as text for UserDefaults (SimplBio.cross.setup).
    var encoded: String {
        let s = Stored(genes: genes.map { StoredGene(letter: String($0.letter), pattern: $0.pattern.rawValue, alleles: $0.alleles, traits: $0.traitNames) },
                       p1: parent1, p2: parent2, examples: examples)
        guard let d = try? JSONEncoder().encode(s) else { return "" }
        return String(decoding: d, as: UTF8.self)
    }

    /// A remembered setup, if it still makes sense (nil otherwise).
    init?(encoded: String) {
        guard let s = try? JSONDecoder().decode(Stored.self, from: Data(encoded.utf8)), (1...CrossSetup.maxGenes).contains(s.genes.count),
              s.p1.count == s.genes.count, s.p2.count == s.genes.count else { return nil }
        var genes: [BioGene] = []
        for g in s.genes {
            guard let p = BioInheritance(rawValue: g.pattern), let l = g.letter.first, g.alleles.count >= 2 else { return nil }
            genes.append(BioGene(l, p, alleles: g.alleles, traitNames: g.traits))
        }
        let setup = CrossSetup(genes: genes, parent1: s.p1, parent2: s.p2, examples: s.examples.count == genes.count ? s.examples : nil)
        for k in genes.indices {
            guard setup.genotypes(gene: k, parent: 1).contains(setup.parent1[k]), setup.genotypes(gene: k, parent: 2).contains(setup.parent2[k]) else { return nil }
        }
        if genes.filter({ $0.pattern.isXLinked }).count > 1 { return nil }
        self = setup
    }
}

// MARK: - What the Cross page draws

/// A gamete as a parent's row or column of the square shows it: its alleles and its chance.
struct CrossGamete {
    let text: String
    let p: BioFrac
}

extension BioCross {
    /// A gamete's alleles: "AB", "A X^B", "I^A b".
    func gameteText(_ g: Gamete) -> String {
        let parts = zip(genes, g).map { $0.0.alleles[$0.1] }
        return parts.allSatisfy { $0.count == 1 } ? parts.joined() : parts.joined(separator: " ")
    }
}

/// The Punnett square (§4.5): parent 1's gametes down the left, parent 2's across the top, each cell the offspring's
/// genotype in canonical order, with its phenotype's place in the phenotypic ratio (for its tint) — nil for a cell that
/// dies before birth.
struct CrossPunnett {
    struct Cell {
        let genotype: [[Int]]
        let text: String
        let phenotype: String
        /// The phenotype's index in the born phenotypes' conventional order (its tint), nil if it dies.
        let tint: Int?
        let dies: Bool
        let carrier: Bool
        let sex: BioSex?
    }

    let rows: [CrossGamete]
    let columns: [CrossGamete]
    let cells: [[Cell]]

    init(_ c: BioCross) {
        let g1 = c.gametes1, g2 = c.gametes2
        rows = g1.map { CrossGamete(text: c.gameteText($0.gamete), p: $0.p) }
        columns = g2.map { CrossGamete(text: c.gameteText($0.gamete), p: $0.p) }
        let order = c.result().phenotypes.map(\.text)
        cells = g1.map { a in
            g2.map { b in
                let g = c.child(a.gamete, b.gamete)
                let ps = c.phenotypes(g)
                let text = c.phenotypeText(g)
                let dies = ps.contains { $0.dies }
                return Cell(genotype: g, text: c.genotypeText(g), phenotype: dies ? "dies" : text, tint: dies ? nil : order.firstIndex(of: text),
                            dies: dies, carrier: ps.contains { $0.carrier }, sex: c.sex(g))
            }
        }
    }

    var cellCount: Int { rows.count * columns.count }
}

/// The forked-line (branch) diagram: each gene's own outcomes from its own small cross, multiplied along every path —
/// the product rule drawn out, clearer than a 64-cell square for three genes. By phenotype (the born) or by genotype.
struct CrossForkedLine {
    struct Branch {
        let label: String
        let p: BioFrac
        let dies: Bool
    }

    struct Leaf {
        /// The branch taken at each gene.
        let path: [Int]
        let label: String
        let p: BioFrac
        let dies: Bool
    }

    /// Each gene's outcomes, in order.
    let levels: [[Branch]]
    let leaves: [Leaf]
    let byGenotype: Bool

    init(_ c: BioCross, genotypes: Bool) {
        byGenotype = genotypes
        var levels: [[Branch]] = []
        for k in c.genes.indices {
            let one = BioCross(genes: [c.genes[k]], parent1: [c.parent1[k]], parent2: [c.parent2[k]])
            if genotypes {
                levels.append(one.result().genotypes.map { Branch(label: $0.text, p: $0.p, dies: $0.dies) })
            } else if c.genes[k].pattern.isXLinked {
                // (a son or a daughter half the time: each sex's phenotypes at half their share within it)
                let half = BioFrac(1, 2)
                let d = one.result(sex: .female).phenotypes.map { Branch(label: "♀ " + $0.text, p: $0.p * half, dies: false) }
                let s = one.result(sex: .male).phenotypes.map { Branch(label: "♂ " + $0.text, p: $0.p * half, dies: false) }
                levels.append(d + s)
            } else {
                levels.append(one.result().phenotypes.map { Branch(label: $0.text, p: $0.p, dies: false) })
            }
        }
        self.levels = levels
        var leaves: [Leaf] = []
        func walk(_ k: Int, _ path: [Int]) {
            if k == levels.count {
                let bs = path.enumerated().map { levels[$0.offset][$0.element] }
                let labels = bs.map(\.label)
                let symbolic = labels.allSatisfy { !$0.contains(" ") && !$0.contains("^") }
                leaves.append(Leaf(path: path, label: symbolic ? labels.joined() : labels.joined(separator: ", "),
                                   p: bs.reduce(BioFrac.one) { $0 * $1.p }, dies: bs.contains { $0.dies }))
                return
            }
            for i in levels[k].indices { walk(k + 1, path + [i]) }
        }
        if !levels.isEmpty { walk(0, []) }
        self.leaves = leaves
    }

    /// The leaves' share that is born (1 unless a gene is lethal, by genotype).
    var total: BioFrac { leaves.reduce(BioFrac.zero) { $0 + $1.p } }
}

// MARK: - Probability (§4.5 Probability)

/// What the Probability page can ask of one gene: a genotype, or a group of them (A_, a blood type, daughters,
/// affected), or anything.
struct CrossTarget: Hashable, Identifiable {
    let id: String
    let label: String
    let genotypes: Set<[Int]>
    /// One genotype (these exclude each other, so the sum rule can add them).
    let single: Bool
    var isAny: Bool { id == "any" }
}

/// A chance worked out exactly where the fraction fits, else as a decimal.
struct CrossChance: Equatable {
    let exact: BioFrac?
    let value: Double

    /// "37/64 (57.8%)", or "57.8%" when the fraction is too long to read.
    var text: String {
        let pct = CrossProbability.percent(value)
        if let f = exact, f.den <= 1_000_000 { return f.den == 1 ? "\(f.num) (\(pct))" : "\(f.text) (\(pct))" }
        return pct
    }
}

enum CrossProbability {
    /// A gene's chips: its genotypes, then its groups, then any.
    static func targets(_ g: BioGene) -> [CrossTarget] {
        var out: [CrossTarget] = []
        func add(_ id: String, _ label: String, _ set: Set<[Int]>, single: Bool) { out.append(CrossTarget(id: id, label: label, genotypes: set, single: single)) }
        switch g.pattern {
        case .abo:
            for i in 0..<3 { for j in i..<3 { add("g\(i)\(j)", g.text([i, j]), [[i, j]], single: true) } }
            add("typeA", "type A", [[0, 0], [0, 2]], single: false)
            add("typeB", "type B", [[1, 1], [1, 2]], single: false)
            add("typeAB", "type AB", [[0, 1]], single: false)
            add("typeO", "type O", [[2, 2]], single: false)
        case .xRecessive, .xDominant:
            for gt in [[0, 0], [0, 1], [1, 1], [0, 2], [1, 2]] { add("g\(gt[0])\(gt[1])", g.text(gt), [gt], single: true) }
            add("daughter", "a daughter", [[0, 0], [0, 1], [1, 1]], single: false)
            add("son", "a son", [[0, 2], [1, 2]], single: false)
            let all: [[Int]] = [[0, 0], [0, 1], [1, 1], [0, 2], [1, 2]]
            let affected = Set(all.filter { g.phenotype($0).label == g.phenotype(g.pattern == .xRecessive ? [1, 1] : [0, 0]).label })
            add("affected", g.pattern == .xRecessive ? g.phenotype([1, 1]).label : g.phenotype([0, 0]).label, affected, single: false)
            add("unaffected", g.pattern == .xRecessive ? g.phenotype([0, 0]).label : g.phenotype([1, 1]).label, Set(all).subtracting(affected), single: false)
        default:
            for gt in [[0, 0], [0, 1], [1, 1]] { add("g\(gt[0])\(gt[1])", g.text(gt), [gt], single: true) }
            if g.pattern.hasDominance { add("dominant", g.alleles[0] + "_", [[0, 0], [0, 1]], single: false) }
        }
        out.append(CrossTarget(id: "any", label: "any", genotypes: [], single: false))
        return out
    }

    /// One gene's factor: its own small Punnett square's share for the target — among the born when the gene has a
    /// lethal genotype (the dead are never counted among the offspring).
    static func factor(_ c: BioCross, gene k: Int, _ t: CrossTarget) -> BioFrac {
        guard c.genes.indices.contains(k) else { return .zero }
        if t.isAny { return .one }
        let g = c.genes[k]
        let all: Set<[Int]> = Set(BioCross.gametes([c.parent1[k]]).flatMap { a in BioCross.gametes([c.parent2[k]]).map { b in [a.gamete[0], b.gamete[0]].sorted() } })
        let alive = all.filter { !g.phenotype($0).dies }
        let pAlive = c.probability(gene: k, of: alive)
        guard !pAlive.isZero else { return .zero }
        return c.probability(gene: k, of: t.genotypes.intersection(alive)) / pAlive
    }

    /// The product rule: one factor a gene, and their product.
    static func product(_ c: BioCross, _ targets: [CrossTarget]) -> (factors: [BioFrac], product: BioFrac) {
        let fs = targets.enumerated().filter { $0.offset < c.genes.count }.map { factor(c, gene: $0.offset, $0.element) }
        return (fs, fs.reduce(BioFrac.one, *))
    }

    /// The sum rule for one gene: outcomes that exclude each other add.
    static func sum(_ c: BioCross, gene k: Int, _ targets: [CrossTarget]) -> (terms: [BioFrac], total: BioFrac) {
        let ts = targets.map { factor(c, gene: k, $0) }
        return (ts, ts.reduce(BioFrac.zero, +))
    }

    /// The offspring asked for, written as one genotype: "aaB_Cc", or "C^R C^W, any B".
    static func targetText(_ c: BioCross, _ targets: [CrossTarget]) -> String {
        let named = targets.enumerated().filter { $0.offset < c.genes.count && !$0.element.isAny }
        guard !named.isEmpty else { return "any offspring" }
        let labels = named.map(\.element.label)
        let symbolic = labels.allSatisfy { !$0.contains(" ") && !$0.contains("^") }
        var s = symbolic ? labels.joined() : labels.joined(separator: ", ")
        let anys = targets.enumerated().filter { $0.offset < c.genes.count && $0.element.isAny }.map { String(c.genes[$0.offset].letter) }
        if !anys.isEmpty { s += " (any \(anys.joined(separator: ", ")))" }
        return s
    }

    /// 1 − (1 − p)ⁿ: at least one of n children.
    static func atLeastOne(_ p: BioFrac, of n: Int) -> CrossChance {
        let none = power(BioFrac.one - p, n)
        let exact = none.map { BioFrac.one - $0 }
        return CrossChance(exact: exact, value: 1 - pow(1 - p.double, Double(n)))
    }

    /// C(n, k) pᵏ (1 − p)ⁿ⁻ᵏ: exactly k of n children.
    static func exactly(_ k: Int, of n: Int, _ p: BioFrac) -> CrossChance {
        guard k >= 0, k <= n else { return CrossChance(exact: .zero, value: 0) }
        let ways = choose(n, k)
        var exact: BioFrac? = nil
        if let a = power(p, k), let b = power(BioFrac.one - p, n - k), let ab = multiply(a, b) { exact = multiply(ab, BioFrac(Int64(ways), 1)) }
        let value = Double(ways) * pow(p.double, Double(k)) * pow(1 - p.double, Double(n - k))
        return CrossChance(exact: exact, value: value)
    }

    static func choose(_ n: Int, _ k: Int) -> Int {
        guard k >= 0, k <= n else { return 0 }
        var r = 1
        for i in 0..<min(k, n - k) { r = r * (n - i) / (i + 1) }
        return r
    }

    /// fⁿ exactly, or nil if it would not fit in 64 bits.
    static func power(_ f: BioFrac, _ n: Int) -> BioFrac? {
        var num: Int64 = 1, den: Int64 = 1
        for _ in 0..<max(n, 0) {
            let a = num.multipliedReportingOverflow(by: f.num), b = den.multipliedReportingOverflow(by: f.den)
            if a.overflow || b.overflow { return nil }
            num = a.partialValue
            den = b.partialValue
        }
        return BioFrac(num, den)
    }

    /// a × b exactly, or nil if it would not fit in 64 bits.
    static func multiply(_ a: BioFrac, _ b: BioFrac) -> BioFrac? {
        let g1 = max(BioFrac.gcd(a.num, b.den), 1), g2 = max(BioFrac.gcd(b.num, a.den), 1)
        let n = (a.num / g1).multipliedReportingOverflow(by: b.num / g2), d = (a.den / g2).multipliedReportingOverflow(by: b.den / g1)
        if n.overflow || d.overflow { return nil }
        return BioFrac(n.partialValue, d.partialValue)
    }

    /// "57.8%"; "< 0.1%" for a sliver; "100%" only when it is.
    static func percent(_ v: Double) -> String {
        if v <= 0 { return "0%" }
        if v >= 1 { return "100%" }
        if v < 0.001 { return "< 0.1%" }
        if v > 0.999 { return "> 99.9%" }
        return String(format: "%.1f%%", v * 100)
    }
}

// MARK: - Chi-square (§4.5 Chi-square, §5.3.3)

/// A row of the chi-square page: a category's name, its part of the expected ratio and its observed count, as typed.
struct ChiCategory: Codable, Equatable, Identifiable {
    var id: Int
    var name: String
    var ratio: String
    var observed: String
}

/// The test worked out the way it is written on paper: the null hypothesis, expected = ratio × total, each category's
/// (o − e)²/e, χ², df = categories − 1, the critical value at p = 0.05 from the AP table, the p-value, and the conclusion
/// in AP's words (reject or fail to reject — never "accept", never "prove").
struct ChiSquareWork {
    let names: [String]
    let ratio: [Double]
    let observed: [Double]
    let total: Double
    let result: BioChiSquare.Result

    var expected: [Double] { result.expected }

    /// "9:3:3:1".
    var ratioText: String { ratio.map { ChiSquareWork.number($0) }.joined(separator: ":") }

    var nullHypothesis: String { ChiSquareWork.nullHypothesis(ratioText) }

    /// The null hypothesis for a ratio, written out (§4.5): what the test assumes before any count is in.
    static func nullHypothesis(_ ratioText: String) -> String {
        "There is no statistically significant difference between the observed and expected counts; any difference is due to chance. The data fit a \(ratioText) ratio."
    }

    /// The expected ratio as typed ("9:3:3:1"), if every part is a number above 0.
    static func ratioText(_ rows: [ChiCategory]) -> String? {
        let parts = rows.map { Double($0.ratio.trimmingCharacters(in: .whitespaces)) }
        guard parts.count >= 2, parts.allSatisfy({ ($0 ?? 0) > 0 }) else { return nil }
        return parts.map { number($0 ?? 0) }.joined(separator: ":")
    }

    /// Some expected count is under 5, where the test is unreliable.
    var lowExpected: Bool { expected.contains { $0 < 5 } }

    /// "p = 0.925", "p < 0.001".
    var pText: String { result.p < 0.001 ? "p < 0.001" : String(format: "p = %.3f", result.p) }

    /// What the p-value means, in a sentence.
    var meaning: String {
        let share = result.p < 0.001 ? "less than 0.1% of the time" : (result.p >= 0.995 ? "almost every time" : "about \(CrossProbability.percent(result.p)) of the time")
        return "If the null hypothesis is true, chance alone gives a difference at least this large \(share)."
    }

    /// The conclusion in AP's words, and what it means for the ratio.
    var conclusionDetail: String {
        result.reject
            ? "The difference is too big to put down to chance at p = 0.05: the data do not fit a \(ratioText) ratio."
            : "The difference could be chance: the data are consistent with a \(ratioText) ratio (this does not prove it)."
    }

    /// A number as typed: 9 → "9", 0.5 → "0.5".
    static func number(_ v: Double) -> String {
        v == v.rounded() && abs(v) < 1e12 ? String(Int64(v)) : String(format: "%g", v)
    }

    /// The work for the rows as typed, or what is wrong with them.
    static func make(_ rows: [ChiCategory]) -> (work: ChiSquareWork?, problems: [String]) {
        var problems: [String] = []
        guard rows.count >= 2 else { return (nil, ["A chi-square test needs at least two categories."]) }
        var ratio: [Double] = [], observed: [Double] = []
        for (i, r) in rows.enumerated() {
            let name = r.name.trimmingCharacters(in: .whitespaces).isEmpty ? "Category \(i + 1)" : r.name
            let e = Double(r.ratio.trimmingCharacters(in: .whitespaces))
            if e == nil || (e ?? 0) <= 0 { problems.append("\(name): the expected ratio needs a number above 0.") }
            let o = Double(r.observed.trimmingCharacters(in: .whitespaces))
            if r.observed.trimmingCharacters(in: .whitespaces).isEmpty {
                problems.append("\(name): enter the observed count.")
            } else if o == nil || (o ?? -1) < 0 || (o ?? 0) != (o ?? 0).rounded() {
                problems.append("\(name): an observed count is a whole number, 0 or more.")
            }
            ratio.append(e ?? 0)
            observed.append(o ?? 0)
        }
        let total = observed.reduce(0, +)
        if problems.isEmpty && total <= 0 { problems.append("The observed counts add up to 0.") }
        guard problems.isEmpty else { return (nil, problems) }
        let expected = BioChiSquare.expected(ratio: ratio, total: total)
        let names = rows.enumerated().map { $0.element.name.trimmingCharacters(in: .whitespaces).isEmpty ? "Category \($0.offset + 1)" : $0.element.name }
        return (ChiSquareWork(names: names, ratio: ratio, observed: observed, total: total,
                              result: BioChiSquare.test(observed: observed, expected: expected)), [])
    }

    /// A cross's born phenotypes and their ratio as the rows (the observed counts left for the student, or kept from
    /// `keeping` where a category of the same name had one).
    static func rows(from r: BioCrossResult, keeping old: [ChiCategory] = []) -> [ChiCategory] {
        zip(r.phenotypes, r.phenotypicRatio).enumerated().map { i, pair in
            let kept = old.first { $0.name == pair.0.text }?.observed ?? ""
            return ChiCategory(id: i, name: pair.0.text, ratio: String(pair.1), observed: kept)
        }
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Genetics Crosses's own `bio-check` lines: §5.3.2 through the Cross page's own path (typed, picked, the examples and
    /// the test cross), the square, the forked line, the product and sum rules, the chi-square page and the pedigree.
    static func crossChecks(_ r: inout BioCheckReport) {
        func setup(_ text: String) -> CrossSetup? { CrossSetup.parse(text).setup }

        // (§5.3.2, every row, through the page's typed cross)
        let rows: [(String, String, String, String)] = [
            ("Aa × Aa", "1 AA : 2 Aa : 1 aa", "3 A_ : 1 aa", "3:1"),
            ("Aa × aa", "1 Aa : 1 aa", "1 A_ : 1 aa", "1:1"),
            ("AA × aa", "1 Aa", "1 A_", "1"),
            ("C^R C^W × C^R C^W", "1 C^R C^R : 2 C^R C^W : 1 C^W C^W", "1 red : 2 pink : 1 white", "1:2:1"),
            ("AaBb × AaBb", "1 AABB : 2 AABb : 1 AAbb : 2 AaBB : 4 AaBb : 2 Aabb : 1 aaBB : 2 aaBb : 1 aabb", "9 A_B_ : 3 A_bb : 3 aaB_ : 1 aabb", "9:3:3:1"),
            ("AaBb × aabb", "1 AaBb : 1 Aabb : 1 aaBb : 1 aabb", "1 A_B_ : 1 A_bb : 1 aaB_ : 1 aabb", "1:1:1:1"),
            ("I^A i × I^B i", "1 I^A I^B : 1 I^A i : 1 I^B i : 1 ii", "1 AB : 1 A : 1 B : 1 O", "1:1:1:1"),
            ("I^A I^B × I^A I^B", "1 I^A I^A : 2 I^A I^B : 1 I^B I^B", "1 A : 2 AB : 1 B", "1:2:1"),
        ]
        for (text, geno, pheno, ratio) in rows {
            guard let s = setup(text) else { r.check("cross.page.\(text.replacingOccurrences(of: " ", with: ""))", "unparsed", false); continue }
            let res = s.cross.result()
            r.check("cross.page.\(text.replacingOccurrences(of: " ", with: ""))", res.phenotypicRatio.map(String.init).joined(separator: ":"),
                    res.genotypicRatioText == geno && res.phenotypicRatioText == pheno && res.phenotypicRatio.map(String.init).joined(separator: ":") == ratio
                    && s.text == text)
        }
        if let s = setup("AaBbCc × AaBbCc") {
            let res = s.cross.result()
            r.check("cross.page.AaBbCc", "\(res.genotypes.count)|" + res.phenotypicRatio.map(String.init).joined(separator: ":"),
                    res.genotypes.count == 27 && res.phenotypicRatio == [27, 9, 9, 9, 3, 3, 3, 1] && res.phenotypes.count == 8)
        } else {
            r.check("cross.page.AaBbCc", "unparsed", false)
        }
        // (X-linked, the mother first even when typed second; daughters and sons apart, and the carriers among them)
        if let s = setup("X^a Y × X^A X^a") {
            let d = s.cross.result(sex: .female), m = s.cross.result(sex: .male)
            r.check("cross.page.X-carrier×affected", s.text + "|" + d.genotypicRatioText + "|" + m.genotypicRatioText,
                    s.text == "X^A X^a × X^a Y" && d.genotypicRatioText == "1 X^A X^a : 1 X^a X^a" && d.phenotypicRatioText == "1 unaffected : 1 affected"
                    && d.phenotypes.first?.carriers == BioFrac(1, 2) && m.genotypicRatioText == "1 X^A Y : 1 X^a Y" && m.phenotypicRatio == [1, 1])
        } else {
            r.check("cross.page.X-carrier×affected", "unparsed", false)
        }
        if let s = setup("X^A X^a × X^A Y") {
            let d = s.cross.result(sex: .female), m = s.cross.result(sex: .male)
            let sonsAffected = m.phenotypes.first { $0.text == "affected" }?.p
            r.check("cross.page.X-carrier×normal", d.phenotypicRatioText + "|carriers" + (d.phenotypes.first?.carriers.text ?? "-") + "|sons" + (sonsAffected?.text ?? "-"),
                    d.genotypicRatioText == "1 X^A X^A : 1 X^A X^a" && d.phenotypes.count == 1 && d.phenotypes.first?.text == "unaffected"
                    && d.phenotypes.first?.carriers == BioFrac(1, 2) && m.genotypicRatioText == "1 X^A Y : 1 X^a Y" && sonsAffected == BioFrac(1, 2))
        } else {
            r.check("cross.page.X-carrier×normal", "unparsed", false)
        }
        // (the lethal row, by switching the typed gene's pattern as the page's menu does)
        if var s = setup("A^Y a × A^Y a") {
            s.setPattern(.lethalDominantHomozygote, gene: 0)
            let res = s.cross.result()
            r.check("cross.page.lethal", res.genotypicRatioText + "|born" + BioRatio.text(res.bornGenotypicRatio) + "|" + res.phenotypicRatioText,
                    res.genotypicRatioText == "1 A^Y A^Y : 2 A^Y a : 1 aa" && res.genotypes.first?.dies == true && res.bornGenotypicRatio == [2, 1]
                    && res.phenotypicRatio == [2, 1] && s.parent1 == [[0, 1]])
        } else {
            r.check("cross.page.lethal", "unparsed", false)
        }
        // (the examples give §5.3.1's phenotypes)
        func example(_ id: String) -> BioCrossResult? {
            guard let e = CrossExamples.find(id) else { return nil }
            var s = CrossSetup.standard
            guard s.applyExample(e, gene: 0) else { return nil }
            return s.cross.result()
        }
        r.check("cross.examples", "\(CrossExamples.all.count)",
                example("snapdragon")?.phenotypicRatioText == "1 red : 2 pink : 1 white"
                && example("mn")?.phenotypicRatioText == "1 M : 2 MN : 1 N"
                && example("roan")?.phenotypicRatioText == "1 red : 2 roan : 1 white"
                && example("abo")?.phenotypicRatioText == "1 AB : 1 A : 1 B : 1 O"
                && example("yellow")?.phenotypicRatioText == "2 yellow : 1 non-yellow"
                && example("lethalRecessive")?.bornGenotypicRatio == [1, 2]
                && example("pea")?.phenotypicRatioText == "3 round : 1 wrinkled"
                && example("rickets")?.phenotypes.first?.text == "rickets")
        // (the pickers: a gene added, its pattern changed and its letter; only valid genotypes offered)
        var picked = CrossSetup.standard
        picked.addGene()
        picked.setPattern(.xRecessive, gene: 1)
        let xMenus = picked.genotypes(gene: 1, parent: 1).map { picked.genes[1].text($0) } + picked.genotypes(gene: 1, parent: 2).map { picked.genes[1].text($0) }
        r.check("cross.pickers", picked.text + "|" + xMenus.joined(separator: ","),
                picked.text == "Aa X^B X^b × Aa X^B Y" && xMenus == ["X^B X^B", "X^B X^b", "X^b X^b", "X^B Y", "X^b Y"]
                && !picked.allowed(.xDominant, gene: 0) && picked.allowed(.abo, gene: 0)
                && CrossSetup.standard.genotypes(gene: 0, parent: 1).count == 3)
        var lethal = CrossSetup.standard
        lethal.setPattern(.lethalDominantHomozygote, gene: 0)
        var abo = CrossSetup.standard
        abo.setPattern(.abo, gene: 0)
        var lettered = CrossSetup.standard
        if let e = CrossExamples.find("snapdragon") { lettered.applyExample(e, gene: 0) }
        lettered.setLetter("D", gene: 0)
        r.check("cross.pickers.patterns", abo.text + "|" + lettered.text,
                lethal.genotypes(gene: 0, parent: 1).count == 2 && abo.text == "I^A i × I^B i" && lettered.text == "D^R D^W × D^R D^W"
                && lettered.cross.result().phenotypicRatioText == "1 red : 2 pink : 1 white")
        // (the test cross: AaBb × AaBb becomes AaBb × aabb, 1 : 1 : 1 : 1)
        if var t = setup("AaBb × AaBb") {
            t.testCross()
            r.check("cross.test-cross", t.text + "|" + BioRatio.text(t.cross.result().phenotypicRatio),
                    t.text == "AaBb × aabb" && t.cross.result().phenotypicRatio == [1, 1, 1, 1])
        } else {
            r.check("cross.test-cross", "unparsed", false)
        }
        // (typed mistakes are said, not guessed)
        let twoX = CrossSetup.parse("X^A X^a X^B X^b × X^A Y X^B Y"), four = CrossSetup.parse("AaBbCcDd × AaBbCcDd"), noFather = CrossSetup.parse("X^A X^a × X^A X^a")
        r.check("cross.parse-errors", "said", twoX.setup == nil && twoX.error != nil && four.setup == nil && noFather.setup == nil && noFather.error != nil
                && CrossSetup.parse("C^R C^W × C^B C^B").setup == nil)
        // (a superscript allele seen alone gets its partner, so the gene can still be changed and asked about)
        let lone = CrossSetup.parse("C^R C^R × C^R C^R").setup
        r.check("cross.parse-lone-allele", lone?.genes.first?.alleles.joined(separator: ",") ?? "-",
                lone?.genes.first?.alleles == ["C^R", "C^W"] && lone?.cross.result().genotypicRatioText == "1 C^R C^R"
                && lone.map { CrossProbability.targets($0.genes[0]).count } == 4)
        // (remembered and read back)
        r.check("cross.remembered", "\(picked.encoded.count)chars", CrossSetup(encoded: picked.encoded) == picked && CrossSetup(encoded: "{}") == nil)

        // (the square: 4 × 4 for AaBb × AaBb, each cell in canonical order and tinted by its phenotype's place)
        if let s = setup("AaBb × AaBb") {
            let sq = CrossPunnett(s.cross)
            let tints = Set(sq.cells.flatMap { $0.compactMap(\.tint) })
            r.check("cross.square", "\(sq.rows.count)x\(sq.columns.count)," + sq.rows.map(\.text).joined(separator: ","),
                    sq.cellCount == 16 && sq.rows.map(\.text) == ["AB", "Ab", "aB", "ab"] && sq.cells[0][0].text == "AABB" && sq.cells[3][3].text == "aabb"
                    && sq.cells[0][3].text == "AaBb" && sq.cells[3][3].tint == 3 && tints == [0, 1, 2, 3] && sq.rows.allSatisfy { $0.p == BioFrac(1, 4) })
        }
        if let s = setup("AaBbCc × AaBbCc"), let x = setup("X^A X^a × X^a Y"), let y = setup("A^Y a × A^Y a") {
            var yellow = y
            yellow.setPattern(.lethalDominantHomozygote, gene: 0)
            let sq = CrossPunnett(s.cross), xs = CrossPunnett(x.cross), ys = CrossPunnett(yellow.cross)
            r.check("cross.square.kinds", "\(sq.cellCount)," + xs.cells.flatMap { $0.map(\.text) }.joined(separator: ","),
                    sq.cellCount == 64 && xs.columns.map(\.text) == ["X^a", "Y"] && xs.cells[0][1].text == "X^A Y" && xs.cells[0][1].sex == .male
                    && xs.cells[0][0].carrier && ys.cells[0][0].dies && ys.cells[0][0].tint == nil)
        }
        // (the forked line: AaBbCc × AaBbCc's eight phenotypes, 27/64 … 1/64, adding to 1; by genotype, 27 branches)
        if let s = setup("AaBbCc × AaBbCc") {
            let f = CrossForkedLine(s.cross, genotypes: false), g = CrossForkedLine(s.cross, genotypes: true)
            r.check("cross.forked-line", f.leaves.map { String($0.p.num) }.joined(separator: ","),
                    f.leaves.map(\.p) == [27, 9, 9, 3, 9, 3, 3, 1].map { BioFrac($0, 64) } && f.leaves.first?.label == "A_B_C_" && f.leaves.last?.label == "aabbcc"
                    && f.total == .one && g.leaves.count == 27 && g.total == .one && g.leaves[13].label == "AaBbCc" && g.leaves[13].p == BioFrac(8, 64))
        }
        if let x = setup("X^A X^a × X^A Y") {
            let f = CrossForkedLine(x.cross, genotypes: false)
            r.check("cross.forked-line.X", f.leaves.map { $0.label + "=" + $0.p.text }.joined(separator: ","),
                    f.leaves.map(\.label) == ["♀ unaffected", "♂ unaffected", "♂ affected"] && f.leaves.map(\.p) == [BioFrac(1, 2), BioFrac(1, 4), BioFrac(1, 4)])
        }

        // (probability: §5.3.2's last row by the chips, the sum rule, and over several children)
        if let s = setup("AaBbCc × AaBbCc") {
            let c = s.cross
            func chip(_ k: Int, _ label: String) -> CrossTarget? { CrossProbability.targets(c.genes[k]).first { $0.label == label } }
            if let a = chip(0, "aa"), let b = chip(1, "B_"), let cc = chip(2, "Cc") {
                let p = CrossProbability.product(c, [a, b, cc])
                r.check("cross.probability", CrossProbability.targetText(c, [a, b, cc]) + "=" + p.factors.map(\.text).joined(separator: "×") + "=" + p.product.text,
                        p.product == BioFrac(3, 32) && p.factors == [BioFrac(1, 4), BioFrac(3, 4), BioFrac(1, 2)]
                        && CrossProbability.targetText(c, [a, b, cc]) == "aaB_Cc" && p.product.percentText == "9.4%")
            } else {
                r.check("cross.probability", "no-chips", false)
            }
            if let aa = chip(0, "AA"), let rec = chip(0, "aa") {
                let sum = CrossProbability.sum(c, gene: 0, [aa, rec])
                r.check("cross.sum-rule", sum.terms.map(\.text).joined(separator: "+") + "=" + sum.total.text, sum.total == BioFrac(1, 2))
            }
        }
        let one = CrossProbability.atLeastOne(BioFrac(1, 4), of: 3), two = CrossProbability.exactly(2, of: 3, BioFrac(1, 4))
        let big = CrossProbability.atLeastOne(BioFrac(3, 128), of: 20)
        r.check("cross.children", (one.exact?.text ?? "-") + "," + (two.exact?.text ?? "-") + "," + big.text,
                one.exact == BioFrac(37, 64) && two.exact == BioFrac(9, 64) && abs(big.value - (1 - pow(125.0 / 128, 20))) < 1e-12 && big.exact == nil)
        if let y = setup("A^Y a × A^Y a") {
            var yellow = y
            yellow.setPattern(.lethalDominantHomozygote, gene: 0)
            let het = CrossProbability.targets(yellow.genes[0]).first { $0.id == "g01" }
            r.check("cross.probability.born", het.map { CrossProbability.factor(yellow.cross, gene: 0, $0).text } ?? "-",
                    het.map { CrossProbability.factor(yellow.cross, gene: 0, $0) } == BioFrac(2, 3))
        }

        // (chi-square from the cross: Mendel's 315 : 108 : 101 : 32 against 9 : 3 : 3 : 1)
        if let s = setup("AaBb × AaBb") {
            var rows = ChiSquareWork.rows(from: s.cross.result())
            for (i, o) in [315, 108, 101, 32].enumerated() { rows[i].observed = String(o) }
            let w = ChiSquareWork.make(rows).work
            r.check("cross.chi-square", String(format: "%.3f,df%ld,%.2f", w?.result.chi2 ?? -1, w?.result.df ?? 0, w?.result.critical ?? 0),
                    w != nil && abs((w?.result.chi2 ?? 0) - 0.470) < 0.001 && w?.result.df == 3 && w?.result.critical == 7.81 && w?.result.reject == false
                    && w?.nullHypothesis.contains("The data fit a 9:3:3:1 ratio.") == true && w?.nullHypothesis.hasPrefix("There is no statistically significant difference") == true
                    && w?.result.conclusion.contains("fail to reject the null hypothesis") == true && w?.expected == [312.75, 104.25, 104.25, 34.75])
            let words = [w?.result.conclusion ?? "", w?.conclusionDetail ?? "", w?.meaning ?? "", w?.nullHypothesis ?? ""].joined(separator: " ").lowercased()
            r.check("cross.chi-square.words", "no-accept-no-prove", !words.contains("accept the null") && !words.contains("proves") && !words.contains("proven"))
            var bad = rows
            bad[0].observed = "3.5"
            bad[1].ratio = "0"
            r.check("cross.chi-square.problems", "\(ChiSquareWork.make(bad).problems.count)", ChiSquareWork.make(bad).work == nil && ChiSquareWork.make(bad).problems.count == 2)
        }
        // (prefilled from the cross: its phenotypes and ratio, counts already typed kept by name; H₀ before any count)
        if let mono = setup("Aa × Aa") {
            let filled = ChiSquareWork.rows(from: mono.cross.result(), keeping: [ChiCategory(id: 5, name: "A_", ratio: "9", observed: "75")])
            r.check("cross.chi-square.from-cross", filled.map { "\($0.name)=\($0.ratio)/\($0.observed)" }.joined(separator: ","),
                    filled.map(\.name) == ["A_", "aa"] && filled.map(\.ratio) == ["3", "1"] && filled[0].observed == "75" && filled[1].observed.isEmpty
                    && ChiSquareWork.ratioText(filled) == "3:1" && ChiSquareWork.nullHypothesis("3:1").hasSuffix("The data fit a 3:1 ratio.")
                    && ChiSquareWork.make(filled).work == nil)
        }
        let reject = ChiSquareWork.make([ChiCategory(id: 0, name: "A", ratio: "3", observed: "50"), ChiCategory(id: 1, name: "a", ratio: "1", observed: "50")]).work
        r.check("cross.chi-square.reject", String(format: "%.2f", reject?.result.chi2 ?? 0),
                reject?.result.reject == true && reject?.result.conclusion.contains("so we reject the null hypothesis") == true && reject?.pText == "p < 0.001")

        pedigreeChecks(&r)
    }
}
