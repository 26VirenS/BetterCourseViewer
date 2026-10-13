import Foundation

// The Crosses tool's pedigree (spec §4.5 Pedigree): a family drawn generation by generation — squares for males, circles
// for females, filled when affected, half-filled for a known carrier — and its mode checker: for autosomal dominant,
// autosomal recessive, X-linked dominant and X-linked recessive, whether some set of genotypes explains everyone, and if
// not, the people that rule the mode out. Full penetrance, no new mutations; at most 30 people. The search is a
// backtracking one over each person's genotypes with constraint propagation over each family (two parents and their
// children). Foundation only (CI's Models job checks it).

/// A person in a pedigree.
struct PedigreePerson: Identifiable, Equatable, Codable {
    let id: Int
    var male: Bool
    var affected: Bool
    /// Known to carry one recessive allele without showing it (drawn half-filled).
    var carrier: Bool
    var father: Int?
    var mother: Int?
}

/// A placed person: generation (0 at the top), slot across (half slots between partners), and label ("II-3").
struct PedigreeSpot: Equatable {
    let id: Int
    let generation: Int
    let x: Double
    let label: String
}

/// A pedigree: its people and its couples. It grows from the first person down: a partner for someone without one, a
/// child for a couple — so it lays out as a family tree.
struct Pedigree: Equatable, Codable {
    var people: [PedigreePerson]
    /// Couples, each [male, female].
    var couples: [[Int]]
    var nextID: Int

    static let maxPeople = 30

    init(people: [PedigreePerson], couples: [[Int]]) {
        self.people = people
        self.couples = couples
        nextID = (people.map(\.id).max() ?? -1) + 1
    }

    /// One couple, neither affected: where a pedigree starts.
    static let start = Pedigree(people: [PedigreePerson(id: 0, male: true, affected: false, carrier: false),
                                         PedigreePerson(id: 1, male: false, affected: false, carrier: false)], couples: [[0, 1]])

    func person(_ id: Int) -> PedigreePerson? { people.first { $0.id == id } }

    func partner(of id: Int) -> Int? {
        for c in couples where c.contains(id) { return c[0] == id ? c[1] : c[0] }
        return nil
    }

    func children(of id: Int) -> [Int] { people.filter { $0.father == id || $0.mother == id }.map(\.id) }

    /// Whether someone joined the family as a partner (no parents drawn, and not the first person).
    func marriedIn(_ id: Int) -> Bool {
        guard let p = person(id), p.father == nil, p.mother == nil else { return false }
        return id != people.first?.id
    }

    var isFull: Bool { people.count >= Pedigree.maxPeople }

    /// A partner of the other sex for someone without one.
    @discardableResult
    mutating func addPartner(to id: Int) -> Int? {
        guard !isFull, let p = person(id), partner(of: id) == nil else { return nil }
        let n = nextID
        nextID += 1
        people.append(PedigreePerson(id: n, male: !p.male, affected: false, carrier: false))
        couples.append(p.male ? [id, n] : [n, id])
        return n
    }

    /// A child of a couple (someone and their partner).
    @discardableResult
    mutating func addChild(of id: Int, male: Bool, affected: Bool = false) -> Int? {
        guard !isFull, let mate = partner(of: id), let p = person(id) else { return nil }
        let n = nextID
        nextID += 1
        people.append(PedigreePerson(id: n, male: male, affected: affected, carrier: false, father: p.male ? id : mate, mother: p.male ? mate : id))
        return n
    }

    /// Whether someone may go: everyone but the first person.
    func canRemove(_ id: Int) -> Bool { id != people.first?.id && person(id) != nil }

    /// Someone taken out, with their descendants, and a partner who joined only through them.
    mutating func remove(_ id: Int) {
        guard canRemove(id) else { return }
        var gone: Set<Int> = [id]
        var queue = [id]
        while let x = queue.popLast() {
            for c in children(of: x) where !gone.contains(c) { gone.insert(c); queue.append(c) }
            if let m = partner(of: x), marriedIn(m), !gone.contains(m) {
                // (a partner with no family of their own goes with them)
                gone.insert(m)
                queue.append(m)
            }
        }
        people.removeAll { gone.contains($0.id) }
        couples.removeAll { $0.contains { gone.contains($0) } }
    }

    /// Sex can change only for someone without a partner (a couple is a father and a mother).
    func canChangeSex(_ id: Int) -> Bool { partner(of: id) == nil }

    mutating func update(_ id: Int, _ change: (inout PedigreePerson) -> Void) {
        guard let i = people.firstIndex(where: { $0.id == id }) else { return }
        change(&people[i])
        if people[i].affected { people[i].carrier = false }
    }

    // MARK: Layout

    /// Everyone placed: each generation a row, each person's children centred under their couple, partners side by
    /// side, numbered left to right in each generation (I-1, I-2, II-1 …).
    func layout() -> [PedigreeSpot] {
        guard let root = people.first?.id else { return [] }
        var spots: [Int: (gen: Int, x: Double)] = [:]
        func kids(_ id: Int) -> [Int] {
            people.filter { $0.father == id || $0.mother == id }.map(\.id)
        }
        func width(_ id: Int) -> Double {
            let block: Double = partner(of: id) == nil ? 1 : 2
            let w = kids(id).reduce(0) { $0 + width($1) }
            return max(block, w)
        }
        func place(_ id: Int, gen: Int, from x0: Double) {
            let w = width(id)
            let ks = kids(id)
            let kw = ks.reduce(0) { $0 + width($1) }
            var x = x0 + (w - kw) / 2
            var centres: [Double] = []
            for k in ks {
                place(k, gen: gen + 1, from: x)
                centres.append(spots[k]?.x ?? x)
                x += width(k)
            }
            let mate = partner(of: id)
            var centre = centres.isEmpty ? x0 + w / 2 : ((centres.first ?? 0) + (centres.last ?? 0)) / 2
            if mate != nil { centre = min(max(centre, x0 + 1), x0 + w - 1) } else { centre = min(max(centre, x0 + 0.5), x0 + w - 0.5) }
            if let m = mate, let me = person(id), let them = person(m) {
                // (the man on the left of a couple, as most textbooks draw it)
                let left = me.male || !them.male
                spots[id] = (gen, left ? centre - 0.5 : centre + 0.5)
                spots[m] = (gen, left ? centre + 0.5 : centre - 0.5)
            } else {
                spots[id] = (gen, centre)
            }
        }
        place(root, gen: 0, from: 0)
        let roman = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"]
        var out: [PedigreeSpot] = []
        let gens = Set(spots.values.map(\.gen)).sorted()
        for g in gens {
            let row = spots.filter { $0.value.gen == g }.sorted { $0.value.x < $1.value.x }
            for (i, s) in row.enumerated() {
                let name = g < roman.count ? roman[g] : "\(g + 1)"
                out.append(PedigreeSpot(id: s.key, generation: g, x: s.value.x, label: "\(name)-\(i + 1)"))
            }
        }
        return out
    }

    /// Each person's label ("II-3"), by id.
    var labels: [Int: String] { Dictionary(uniqueKeysWithValues: layout().map { ($0.id, $0.label) }) }

    // MARK: Examples

    struct Example: Identifiable {
        let id: String
        let title: String
        let pedigree: Pedigree
        let note: String
    }

    static let examples: [Example] = [
        Example(id: "ar", title: "Two unaffected parents, an affected child", pedigree: build { p in
            let a = p.addChild(of: 0, male: false, affected: false)
            p.addChild(of: 0, male: true, affected: true)
            p.addChild(of: 0, male: false, affected: true)
            if let a { p.addPartner(to: a) }
        }, note: "An affected child of two unaffected parents: the allele was hidden in both, so it is recessive."),
        Example(id: "ad", title: "Affected in every generation", pedigree: build { p in
            p.update(0) { $0.affected = true }
            if let son = p.addChild(of: 0, male: true, affected: true), let wife = p.addPartner(to: son) {
                p.update(wife) { $0.affected = true }
                p.addChild(of: son, male: false, affected: false)
                p.addChild(of: son, male: true, affected: true)
            }
            p.addChild(of: 0, male: false, affected: false)
        }, note: "Two affected parents with an unaffected daughter: each must carry a hidden normal allele, so the trait is dominant."),
        Example(id: "xr", title: "Affected sons of unaffected parents", pedigree: build { p in
            p.addChild(of: 0, male: true, affected: true)
            if let d = p.addChild(of: 0, male: false, affected: false), p.addPartner(to: d) != nil {
                p.addChild(of: d, male: true, affected: true)
                p.addChild(of: d, male: false, affected: false)
                p.addChild(of: d, male: true, affected: false)
            }
        }, note: "Only sons are affected, through unaffected mothers: X-linked recessive fits best, though autosomal recessive can't be ruled out."),
        Example(id: "xd", title: "An affected father's daughters", pedigree: build { p in
            p.update(0) { $0.affected = true }
            if let d = p.addChild(of: 0, male: false, affected: true), let h = p.addPartner(to: d) {
                p.update(h) { $0.affected = true }
                p.addChild(of: d, male: true, affected: false)
                p.addChild(of: d, male: false, affected: true)
            }
            p.addChild(of: 0, male: true, affected: false)
            p.addChild(of: 0, male: false, affected: true)
        }, note: "An affected father passes it to every daughter and to no son: the pattern of an X-linked dominant trait."),
        Example(id: "start", title: "One couple", pedigree: Pedigree.start, note: "Add partners and children to draw your own."),
    ]

    private static func build(_ make: (inout Pedigree) -> Void) -> Pedigree {
        var p = Pedigree.start
        make(&p)
        return p
    }

    // MARK: Remembered

    var encoded: String {
        guard let d = try? JSONEncoder().encode(self) else { return "" }
        return String(decoding: d, as: UTF8.self)
    }

    static func decode(_ s: String) -> Pedigree? {
        guard let p = try? JSONDecoder().decode(Pedigree.self, from: Data(s.utf8)), !p.people.isEmpty, p.people.count <= maxPeople else { return nil }
        let ids = Set(p.people.map(\.id))
        guard ids.count == p.people.count, p.couples.allSatisfy({ $0.count == 2 && ids.contains($0[0]) && ids.contains($0[1]) }) else { return nil }
        // (parents always come before their children, as the editor adds them: no loops to lay out)
        for (i, x) in p.people.enumerated() {
            let earlier = Set(p.people[..<i].map(\.id))
            if let f = x.father, !earlier.contains(f) { return nil }
            if let m = x.mother, !earlier.contains(m) { return nil }
        }
        return p
    }
}

// MARK: - The mode checker

/// A mode of inheritance the checker tries.
enum PedigreeMode: String, CaseIterable, Identifiable {
    case autosomalDominant, autosomalRecessive, xDominant, xRecessive

    var id: String { rawValue }

    var title: String {
        switch self {
        case .autosomalDominant: return "Autosomal dominant"
        case .autosomalRecessive: return "Autosomal recessive"
        case .xDominant: return "X-linked dominant"
        case .xRecessive: return "X-linked recessive"
        }
    }

    var short: String {
        switch self {
        case .autosomalDominant: return "AD"
        case .autosomalRecessive: return "AR"
        case .xDominant: return "XD"
        case .xRecessive: return "XR"
        }
    }

    var xLinked: Bool { self == .xDominant || self == .xRecessive }
    var dominant: Bool { self == .autosomalDominant || self == .xDominant }
}

/// A mode's verdict: consistent, or ruled out — by whom, and why.
struct PedigreeVerdict: Identifiable {
    let mode: PedigreeMode
    let consistent: Bool
    /// The people whose phenotypes together rule the mode out (empty when consistent).
    let culprits: [Int]
    /// One sentence for each conflict found.
    let reasons: [String]
    var id: String { mode.rawValue }
}

enum PedigreeChecker {
    /// A person's genotype: the number of disease alleles (0–2; a male's X-linked: 0 or 1).
    typealias Domain = Set<Int>

    /// Every mode's verdict.
    static func check(_ p: Pedigree) -> [PedigreeVerdict] { PedigreeMode.allCases.map { verdict(p, $0) } }

    static func verdict(_ p: Pedigree, _ mode: PedigreeMode) -> PedigreeVerdict {
        let everyone = Set(p.people.map(\.id))
        guard !consistent(p, mode, constrained: everyone) else { return PedigreeVerdict(mode: mode, consistent: true, culprits: [], reasons: []) }
        // (the people who rule it out: a smallest set whose phenotypes alone can't be explained — found by letting each
        // person's phenotype go in turn and keeping it only if the rest is then explainable — then the next such set
        // among the others, until what is left fits)
        let labels = p.labels
        let order = p.layout().map(\.id)
        var culprits: [Int] = []
        var reasons: [String] = []
        var relaxed: Set<Int> = []
        for _ in 0..<Pedigree.maxPeople {
            var s = everyone.subtracting(relaxed)
            guard !consistent(p, mode, constrained: s) else { break }
            for id in order.reversed() where s.contains(id) {
                let without = s.subtracting([id])
                if !consistent(p, mode, constrained: without) { s = without }
            }
            let set = order.filter { s.contains($0) }
            guard !set.isEmpty else { break }
            culprits += set.filter { !culprits.contains($0) }
            reasons.append(reason(p, mode, set, labels))
            relaxed.formUnion(set)
        }
        return PedigreeVerdict(mode: mode, consistent: false, culprits: culprits, reasons: reasons)
    }

    /// The genotypes a person can have in a mode, from their phenotype (or every genotype, if their phenotype is not
    /// counted).
    static func domain(_ x: PedigreePerson, _ mode: PedigreeMode, constrained: Bool) -> Domain {
        let all: Domain = mode.xLinked && x.male ? [0, 1] : [0, 1, 2]
        guard constrained else { return all }
        return all.filter { g in
            let shows: Bool
            switch mode {
            case .autosomalDominant: shows = g >= 1
            case .autosomalRecessive: shows = g == 2
            case .xDominant: shows = g >= 1
            case .xRecessive: shows = x.male ? g == 1 : g == 2
            }
            if x.affected { return shows }
            if x.carrier { return !shows && g == 1 && !(mode.xLinked && x.male) }
            return !shows
        }
    }

    /// The alleles a parent can pass on (0 normal, 1 disease).
    private static func alleles(_ g: Int, hemizygous: Bool) -> [Int] {
        if hemizygous { return [g] }
        switch g {
        case 0: return [0]
        case 1: return [0, 1]
        default: return [1]
        }
    }

    /// Whether a child's genotype can come from these parents' (Mendelian transmission; a son's X from his mother).
    static func transmits(father f: Int, mother m: Int, child c: Int, male: Bool, mode: PedigreeMode) -> Bool {
        let fromMother = alleles(m, hemizygous: false)
        if mode.xLinked {
            if male { return fromMother.contains(c) }
            return fromMother.contains(c - f)
        }
        let fromFather = alleles(f, hemizygous: false)
        return fromFather.contains { a in fromMother.contains(c - a) }
    }

    /// Whether some genotypes explain everyone, counting only the phenotypes of `constrained`.
    static func consistent(_ p: Pedigree, _ mode: PedigreeMode, constrained: Set<Int>) -> Bool {
        var d: [Int: Domain] = [:]
        for x in p.people { d[x.id] = domain(x, mode, constrained: constrained.contains(x.id)) }
        // (each family: two parents and their children)
        var families: [String: (father: Int, mother: Int, kids: [Int])] = [:]
        var keys: [String] = []
        for x in p.people {
            guard let f = x.father, let m = x.mother, d[f] != nil, d[m] != nil else { continue }
            let key = "\(f),\(m)"
            if families[key] == nil { families[key] = (f, m, []); keys.append(key) }
            families[key]?.kids.append(x.id)
        }
        let fams = keys.compactMap { families[$0] }
        let males = Dictionary(uniqueKeysWithValues: p.people.map { ($0.id, $0.male) })
        return search(&d, fams, males, mode)
    }

    private static func propagate(_ d: inout [Int: Domain], _ fams: [(father: Int, mother: Int, kids: [Int])], _ males: [Int: Bool], _ mode: PedigreeMode) -> Bool {
        var changed = true
        while changed {
            changed = false
            for fam in fams {
                guard let df = d[fam.father], let dm = d[fam.mother] else { continue }
                var sf: Domain = [], sm: Domain = []
                var sk: [Domain] = Array(repeating: [], count: fam.kids.count)
                for f in df {
                    for m in dm {
                        let fits = fam.kids.map { k in (d[k] ?? []).filter { transmits(father: f, mother: m, child: $0, male: males[k] ?? false, mode: mode) } }
                        guard fits.allSatisfy({ !$0.isEmpty }) else { continue }
                        sf.insert(f)
                        sm.insert(m)
                        for i in fits.indices { sk[i].formUnion(fits[i]) }
                    }
                }
                if sf.isEmpty { return false }
                if sf != df { d[fam.father] = sf; changed = true }
                if sm != dm { d[fam.mother] = sm; changed = true }
                for (i, k) in fam.kids.enumerated() where sk[i] != d[k] {
                    if sk[i].isEmpty { return false }
                    d[k] = sk[i]
                    changed = true
                }
            }
        }
        return !d.values.contains { $0.isEmpty }
    }

    private static func search(_ d: inout [Int: Domain], _ fams: [(father: Int, mother: Int, kids: [Int])], _ males: [Int: Bool], _ mode: PedigreeMode) -> Bool {
        guard propagate(&d, fams, males, mode) else { return false }
        guard let (id, dom) = d.filter({ $0.value.count > 1 }).min(by: { $0.value.count != $1.value.count ? $0.value.count < $1.value.count : $0.key < $1.key }) else {
            return true
        }
        for v in dom.sorted() {
            var e = d
            e[id] = [v]
            if search(&e, fams, males, mode) { return true }
        }
        return false
    }

    /// Why a set of people rules a mode out, in a sentence: the textbook's reasons where they fit, else a general one.
    static func reason(_ p: Pedigree, _ mode: PedigreeMode, _ set: [Int], _ labels: [Int: String]) -> String {
        func name(_ id: Int) -> String { labels[id] ?? "?" }
        let people = set.compactMap { p.person($0) }
        if people.count == 1, let x = people.first, x.carrier {
            if mode.xLinked && x.male { return "\(name(x.id)) is marked as a carrier, but a male has one X: he either shows it or doesn't have it." }
            if mode.dominant { return "\(name(x.id)) is marked as a carrier, but a dominant allele always shows (full penetrance)." }
        }
        // (a child and both parents)
        if people.count == 3, let child = people.first(where: { c in c.father.map(set.contains) == true && c.mother.map(set.contains) == true }),
           let f = child.father, let m = child.mother, let father = p.person(f), let mother = p.person(m) {
            if mode.dominant && child.affected && !father.affected && !mother.affected {
                return "\(name(child.id)) is affected but neither parent (\(name(f)), \(name(m))) is: a dominant allele would have come from an affected parent."
            }
            if !mode.dominant && !child.affected && father.affected && mother.affected && !mode.xLinked {
                return "\(name(f)) and \(name(m)) are both affected, so both are aa and every child would be too — but \(name(child.id)) is not."
            }
            if mode == .xRecessive && !child.affected && father.affected && mother.affected {
                return "Both parents (\(name(f)), \(name(m))) are affected, so every child gets the recessive X from each — but \(name(child.id)) is not affected."
            }
        }
        if people.count == 2, let a = people.first, let b = people.last {
            let (parent, child) = a.id == b.father || a.id == b.mother ? (a, b) : (b, a)
            if child.father == parent.id || child.mother == parent.id {
                let isMother = child.mother == parent.id
                switch mode {
                case .xRecessive where isMother && parent.affected && child.male && !child.affected:
                    return "\(name(parent.id)) is an affected mother, so she passes the recessive X to every son — but her son \(name(child.id)) is not affected."
                case .xRecessive where !isMother && !parent.affected && !child.male && child.affected:
                    return "\(name(child.id)) is an affected daughter, so she got a recessive X from her father — but \(name(parent.id)) is not affected."
                case .xDominant where !isMother && parent.affected && !child.male && !child.affected:
                    return "\(name(parent.id)) is an affected father, so every daughter gets his X — but \(name(child.id)) is not affected."
                case .xDominant where isMother && !parent.affected && child.male && child.affected:
                    return "\(name(child.id)) is an affected son, whose only X came from his mother — but \(name(parent.id)) is not affected."
                default:
                    break
                }
            }
        }
        let list = set.map(name).joined(separator: ", ")
        return "No genotypes explain \(list) together under \(mode.title.lowercased()) inheritance."
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// The pedigree's lines: the layout's labels, and each example's verdicts with the people that rule modes out.
    static func pedigreeChecks(_ r: inout BioCheckReport) {
        func verdicts(_ id: String) -> [PedigreeMode: PedigreeVerdict] {
            guard let e = Pedigree.examples.first(where: { $0.id == id }) else { return [:] }
            return Dictionary(uniqueKeysWithValues: PedigreeChecker.check(e.pedigree).map { ($0.mode, $0) })
        }
        func summary(_ v: [PedigreeMode: PedigreeVerdict], _ labels: [Int: String]) -> String {
            PedigreeMode.allCases.map { m in
                guard let x = v[m] else { return "\(m.short)?" }
                return x.consistent ? "\(m.short)ok" : "\(m.short)out(" + x.culprits.compactMap { labels[$0] }.joined(separator: ",") + ")"
            }.joined(separator: ";")
        }
        func labels(_ id: String) -> [Int: String] { Pedigree.examples.first { $0.id == id }?.pedigree.labels ?? [:] }

        // (an affected child of unaffected parents: only autosomal recessive is left; X-linked recessive falls to an
        // affected daughter of an unaffected father)
        let ar = verdicts("ar"), arL = labels("ar")
        let arOut = { (m: PedigreeMode) in Set((ar[m]?.culprits ?? []).compactMap { arL[$0] }) }
        r.check("pedigree.AR", summary(ar, arL),
                ar[.autosomalRecessive]?.consistent == true && ar[.autosomalDominant]?.consistent == false && ar[.xDominant]?.consistent == false
                && ar[.xRecessive]?.consistent == false && arOut(.xRecessive).isSuperset(of: ["I-1"]) && arOut(.autosomalDominant).contains("I-1")
                && arOut(.autosomalDominant).contains("I-2"))
        let ad = verdicts("ad"), adL = labels("ad")
        r.check("pedigree.AD", summary(ad, adL),
                ad[.autosomalDominant]?.consistent == true && ad[.autosomalRecessive]?.consistent == false && ad[.xDominant]?.consistent == false
                && ad[.xRecessive]?.consistent == false)
        let xr = verdicts("xr"), xrL = labels("xr")
        r.check("pedigree.XR", summary(xr, xrL),
                xr[.xRecessive]?.consistent == true && xr[.autosomalRecessive]?.consistent == true && xr[.autosomalDominant]?.consistent == false
                && xr[.xDominant]?.consistent == false && (xr[.xDominant]?.reasons.first?.contains("affected son") ?? false))
        let xd = verdicts("xd"), xdL = labels("xd")
        r.check("pedigree.XD", summary(xd, xdL), xd[.xDominant]?.consistent == true && xd[.autosomalDominant]?.consistent == true
                && xd[.xRecessive]?.consistent == false && xd[.autosomalRecessive]?.consistent == false)

        // (the layout: generations numbered left to right, children under their parents, nobody on top of anybody)
        if let e = Pedigree.examples.first(where: { $0.id == "xr" }) {
            let spots = e.pedigree.layout()
            let overlap = Dictionary(grouping: spots, by: \.generation).values.contains { row in
                let xs = row.map(\.x).sorted()
                return zip(xs, xs.dropFirst()).contains { $1 - $0 < 0.99 }
            }
            r.check("pedigree.layout", spots.map(\.label).sorted().joined(separator: ","),
                    spots.count == e.pedigree.people.count && !overlap && spots.filter { $0.generation == 0 }.map(\.label).sorted() == ["I-1", "I-2"]
                    && Set(spots.map(\.label)).count == spots.count)
        }

        // (editing: a child needs a couple; removing someone takes their descendants and their partner who married in)
        var p = Pedigree.start
        let kid = p.addChild(of: 0, male: true)
        let mate = kid.flatMap { p.addPartner(to: $0) }
        let grandchild = kid.flatMap { p.addChild(of: $0, male: false, affected: true) }
        let alone = p.addChild(of: grandchild ?? -1, male: true)
        let before = p.people.count
        if let kid { p.remove(kid) }
        r.check("pedigree.edit", "\(before)->\(p.people.count)", kid != nil && mate != nil && grandchild != nil && alone == nil && before == 5 && p.people.count == 2
                && p.couples == [[0, 1]] && !p.canRemove(0) && Pedigree.decode(p.encoded) == p)

        // (a carrier marked in a dominant mode rules it out by itself; a lone affected child of a carrier mother and
        // an unaffected father fits X-linked recessive)
        var c = Pedigree.start
        c.update(1) { $0.carrier = true }
        if let son = c.addChild(of: 0, male: true, affected: true) {
            let v = Dictionary(uniqueKeysWithValues: PedigreeChecker.check(c).map { ($0.mode, $0) })
            r.check("pedigree.carrier", summary(v, c.labels), v[.xRecessive]?.consistent == true && v[.autosomalDominant]?.culprits == [1]
                    && v[.xDominant]?.consistent == false && v[.autosomalRecessive]?.consistent == true && son > 1)
        }
        // (thirty people still check at once: a chain of five generations)
        var big = Pedigree.start
        var line = 0
        for g in 0..<9 {
            guard let a = big.addChild(of: line, male: g % 2 == 0, affected: g % 3 == 0) else { break }
            big.addChild(of: line, male: g % 2 == 1)
            if big.addPartner(to: a) != nil { line = a }
        }
        let started = Date()
        let bigV = PedigreeChecker.check(big)
        let ms = Date().timeIntervalSince(started) * 1000
        r.check("pedigree.size", "\(big.people.count)people", big.people.count >= 25 && bigV.count == 4 && ms < 2000)
    }
}
