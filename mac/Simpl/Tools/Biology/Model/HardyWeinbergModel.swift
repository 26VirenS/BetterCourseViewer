import Foundation

// Hardy–Weinberg's own model (spec §4.7, §5.4): the calculator (everything from any one of p, q, p², 2pq, q², the share
// showing either trait, or genotype counts, each step written out), the HWE chi-square test (df = 1, editable), and the
// simulation — mutation, migration, mating with inbreeding, selection and drift, in exactly §5.4.3's order, drift drawn
// from SplitMix64 so a seed gives the same populations on the Mac and on Windows (replicate r, counting from 0, uses
// seed S + r). Foundation only, so the Models job compiles and checks it with the rest of Biology/Model.

// MARK: - The calculator (§4.7 Calculate, §5.4.1)

/// What the calculator is given.
enum HWGiven: String, CaseIterable, Identifiable {
    case q2, p, q, p2, twoPQ, recessivePercent, dominantPercent, counts

    var id: String { rawValue }

    /// Its menu's line.
    var title: String {
        switch self {
        case .q2: return "q² — the aa frequency"
        case .p: return "p — the A allele's frequency"
        case .q: return "q — the a allele's frequency"
        case .p2: return "p² — the AA frequency"
        case .twoPQ: return "2pq — the Aa frequency"
        case .recessivePercent: return "% showing the recessive trait"
        case .dominantPercent: return "% showing the dominant trait"
        case .counts: return "Genotype counts (AA, Aa, aa)"
        }
    }

    /// Its field's label.
    var short: String {
        switch self {
        case .q2: return "q²"
        case .p: return "p"
        case .q: return "q"
        case .p2: return "p²"
        case .twoPQ: return "2pq"
        case .recessivePercent: return "% recessive"
        case .dominantPercent: return "% dominant"
        case .counts: return "Counts"
        }
    }

    var isPercent: Bool { self == .recessivePercent || self == .dominantPercent }

    /// An example value for its field.
    var placeholder: String {
        switch self {
        case .q2: return "0.16"
        case .p: return "0.6"
        case .q: return "0.4"
        case .p2: return "0.36"
        case .twoPQ: return "0.48"
        case .recessivePercent: return "16"
        case .dominantPercent: return "84"
        case .counts: return ""
        }
    }
}

/// A step of the working, as it is written on paper.
struct HWStep: Equatable {
    /// What the step finds ("The a allele").
    let label: String
    /// "q = √q² = √0.16 = 0.40".
    let math: String
}

/// Genotype counts from a sample.
struct HWCounts: Equatable {
    let AA: Int
    let Aa: Int
    let aa: Int
    var n: Int { AA + Aa + aa }
    /// p = (2·n_AA + n_Aa) / 2N (§5.4.1).
    var p: Double { n > 0 ? Double(2 * AA + Aa) / Double(2 * n) : 0 }
}

/// Everything the calculator works out.
struct HWCalc {
    let p: Double
    let q: Double
    let steps: [HWStep]
    /// From 2pq alone: the other answer (p and q the other way round).
    let otherP: Double?
    let notes: [String]
    /// When given counts: the sample.
    let counts: HWCounts?

    var p2: Double { p * p }
    var twoPQ: Double { 2 * p * q }
    var q2: Double { q * q }
    var carriersPer1000: Double { twoPQ * 1000 }
}

enum HardyWeinberg {
    /// Everything from one value (a frequency 0–1, or a percent 0–100 for the trait shares), or what is wrong with it.
    static func calculate(_ given: HWGiven, _ value: Double) -> (calc: HWCalc?, error: String?) {
        guard value.isFinite else { return (nil, "Enter a number.") }
        var steps: [HWStep] = []
        var notes: [String] = []
        var otherP: Double? = nil
        let p: Double
        switch given {
        case .counts:
            return (nil, "Enter the three genotype counts.")
        case .q2, .recessivePercent, .dominantPercent:
            var q2 = value
            if given == .recessivePercent {
                guard (0...100).contains(value) else { return (nil, "A percent must be between 0 and 100.") }
                q2 = value / 100
                steps.append(HWStep(label: "The aa frequency", math: "q² = \(plain(value))% ÷ 100 = \(num(q2))"))
                notes.append("Everyone showing a recessive trait is aa, so their share is q².")
            } else if given == .dominantPercent {
                guard (0...100).contains(value) else { return (nil, "A percent must be between 0 and 100.") }
                q2 = 1 - value / 100
                steps.append(HWStep(label: "The aa frequency", math: "q² = 1 − \(num(value / 100)) = \(num(q2))"))
                notes.append("Everyone not showing the dominant trait is aa, so q² = 1 − the dominant share.")
            } else {
                guard (0...1).contains(value) else { return (nil, "q² must be between 0 and 1.") }
                steps.append(HWStep(label: "Given", math: "q² = \(num(q2))"))
            }
            let q = q2.squareRoot()
            steps.append(HWStep(label: "The a allele", math: "q = √q² = √\(num(q2)) = \(num(q))"))
            p = 1 - q
            steps.append(HWStep(label: "The A allele", math: "p = 1 − q = 1 − \(num(q)) = \(num(p))"))
            notes.append("q = √q² holds only if the population is in Hardy–Weinberg equilibrium.")
        case .p:
            guard (0...1).contains(value) else { return (nil, "p must be between 0 and 1.") }
            p = value
            steps.append(HWStep(label: "Given", math: "p = \(num(p))"))
            steps.append(HWStep(label: "The a allele", math: "q = 1 − p = 1 − \(num(p)) = \(num(1 - p))"))
        case .q:
            guard (0...1).contains(value) else { return (nil, "q must be between 0 and 1.") }
            p = 1 - value
            steps.append(HWStep(label: "Given", math: "q = \(num(value))"))
            steps.append(HWStep(label: "The A allele", math: "p = 1 − q = 1 − \(num(value)) = \(num(p))"))
        case .p2:
            guard (0...1).contains(value) else { return (nil, "p² must be between 0 and 1.") }
            p = value.squareRoot()
            steps.append(HWStep(label: "Given", math: "p² = \(num(value))"))
            steps.append(HWStep(label: "The A allele", math: "p = √p² = √\(num(value)) = \(num(p))"))
            steps.append(HWStep(label: "The a allele", math: "q = 1 − p = 1 − \(num(p)) = \(num(1 - p))"))
            notes.append("p = √p² holds only if the population is in Hardy–Weinberg equilibrium.")
        case .twoPQ:
            guard (0...0.5).contains(value) else { return (nil, "2pq must be between 0 and 0.5 (its largest value, when p = q = 0.5).") }
            let root = max(0, 1 - 2 * value).squareRoot()
            p = (1 + root) / 2
            steps.append(HWStep(label: "Given", math: "2pq = \(num(value)), with q = 1 − p, so p² − p + \(num(value / 2)) = 0"))
            steps.append(HWStep(label: "The A allele", math: "p = (1 ± √(1 − 2 × \(num(value)))) ÷ 2 = \(num(p))\(root > 0 ? " or \(num(1 - p))" : "")"))
            steps.append(HWStep(label: "The a allele", math: "q = 1 − p = \(num(1 - p))"))
            if root > 0 {
                otherP = 1 - p
                notes.append("2pq alone can't say which allele is commoner: p = \(num(p)) or p = \(num(1 - p)). The larger p is shown.")
            }
            notes.append("2pq = \(num(value)) assumes the population is in Hardy–Weinberg equilibrium.")
        }
        let q = 1 - p
        steps += genotypeSteps(p, q)
        return (HWCalc(p: p, q: q, steps: steps, otherP: otherP, notes: notes, counts: nil), nil)
    }

    /// Everything from a sample's genotype counts (allele counting needs no assumption of equilibrium).
    static func calculate(counts c: HWCounts) -> (calc: HWCalc?, error: String?) {
        guard c.AA >= 0, c.Aa >= 0, c.aa >= 0 else { return (nil, "Counts can't be negative.") }
        guard c.n > 0 else { return (nil, "Enter at least one individual.") }
        var steps: [HWStep] = []
        steps.append(HWStep(label: "The sample", math: "N = \(c.AA) + \(c.Aa) + \(c.aa) = \(c.n)"))
        let p = c.p
        steps.append(HWStep(label: "The A allele", math: "p = (2 × \(c.AA) + \(c.Aa)) ÷ (2 × \(c.n)) = \(2 * c.AA + c.Aa) ÷ \(2 * c.n) = \(num(p))"))
        steps.append(HWStep(label: "The a allele", math: "q = 1 − p = 1 − \(num(p)) = \(num(1 - p))"))
        let n = Double(c.n)
        steps.append(HWStep(label: "Observed", math: "AA \(num(Double(c.AA) / n)) · Aa \(num(Double(c.Aa) / n)) · aa \(num(Double(c.aa) / n))"))
        var all = genotypeSteps(p, 1 - p)
        all[0] = HWStep(label: "Expected AA", math: all[0].math + " → \(num(p * p * n)) of \(c.n)")
        all[1] = HWStep(label: "Expected Aa", math: all[1].math + " → \(num(2 * p * (1 - p) * n)) of \(c.n)")
        all[2] = HWStep(label: "Expected aa", math: all[2].math + " → \(num((1 - p) * (1 - p) * n)) of \(c.n)")
        steps += all
        let notes = ["Counting alleles gives p and q without assuming equilibrium. The Test page asks whether the counts fit p², 2pq and q²."]
        return (HWCalc(p: p, q: 1 - p, steps: steps, otherP: nil, notes: notes, counts: c), nil)
    }

    /// p², 2pq, q², the check that they add up to 1, and carriers per 1,000.
    private static func genotypeSteps(_ p: Double, _ q: Double) -> [HWStep] {
        let p2 = p * p, h = 2 * p * q, q2 = q * q
        return [
            HWStep(label: "AA (p²)", math: "p² = \(num(p))² = \(num(p2))"),
            HWStep(label: "Aa (2pq)", math: "2pq = 2 × \(num(p)) × \(num(q)) = \(num(h))"),
            HWStep(label: "aa (q²)", math: "q² = \(num(q))² = \(num(q2))"),
            HWStep(label: "Check", math: "p² + 2pq + q² = \(num(p2)) + \(num(h)) + \(num(q2)) = \(num(p2 + h + q2))"),
            HWStep(label: "Carriers", math: "2pq × 1,000 = \(carriers(h * 1000)) heterozygous carriers per 1,000 people"),
        ]
    }

    /// "480", "19.8", "0.2".
    static func carriers(_ x: Double) -> String {
        x >= 10 ? String(halfUp: "%.0f", x) : (x >= 1 ? String(halfUp: "%.1f", x) : num(x))
    }

    /// A frequency as the working shows it: up to four decimals, at least two ("0.40", "0.4225", "1.00"); a tiny one in
    /// powers of ten ("2.50 × 10⁻⁵").
    static func num(_ x: Double, places: Int = 4) -> String {
        if x == 0 { return "0" }
        if abs(x) < 0.0001 {
            let e = Int(floor(log10(abs(x))))
            let m = x / pow(10, Double(e))
            return String(halfUp: "%.2f × 10", m) + superscript(e)
        }
        var s = String(halfUp: "%.\(places)f", x)
        while s.hasSuffix("0"), let dot = s.firstIndex(of: "."), s.distance(from: dot, to: s.endIndex) > 3 { s.removeLast() }
        return s
    }

    /// A typed number as typed: 16 → "16", 2.5 → "2.5".
    static func plain(_ x: Double) -> String { x == x.rounded() && abs(x) < 1e12 ? String(halfUp: "%.0f", x) : String(halfUp: "%g", x) }

    static func superscript(_ n: Int) -> String {
        let digits: [Character: Character] = ["0": "⁰", "1": "¹", "2": "²", "3": "³", "4": "⁴", "5": "⁵", "6": "⁶", "7": "⁷", "8": "⁸", "9": "⁹", "-": "⁻"]
        return String(String(n).map { digits[$0] ?? $0 })
    }

    /// 100 people by genotype (AA, Aa, aa) for the dot grid: the largest-remainder rounding, so they always add to 100.
    static func people(_ freqs: [Double]) -> [Int] {
        let scaled = freqs.map { max(0, $0) * 100 }
        var out = scaled.map { Int($0.rounded(.down)) }
        var left = 100 - out.reduce(0, +)
        let order = scaled.indices.sorted { a, b in
            let ra = scaled[a] - Double(out[a]), rb = scaled[b] - Double(out[b])
            return ra != rb ? ra > rb : a < b
        }
        var i = 0
        while left > 0, !order.isEmpty {
            out[order[i % order.count]] += 1
            left -= 1
            i += 1
        }
        return out
    }

    // MARK: The HWE test (§5.4.2)

    /// The test of a sample against Hardy–Weinberg proportions from its own allele frequency: df 1 by default (3 classes
    /// − 1 − 1 estimated frequency); some courses use 2.
    static func test(_ c: HWCounts, df: Int = 1) -> (test: HWTest?, error: String?) {
        guard c.AA >= 0, c.Aa >= 0, c.aa >= 0, c.n > 0 else { return (nil, "Enter the three genotype counts.") }
        let p = c.p, q = 1 - p
        guard p > 0, q > 0 else { return (nil, "Only one allele is in the sample, so every genotype is as expected: there is nothing to test.") }
        let n = Double(c.n)
        let expected = [n * p * p, n * 2 * p * q, n * q * q]
        let r = BioChiSquare.test(observed: [Double(c.AA), Double(c.Aa), Double(c.aa)], expected: expected, df: df)
        return (HWTest(counts: c, p: p, q: q, result: r), nil)
    }
}

/// The HWE test's working.
struct HWTest {
    let counts: HWCounts
    let p: Double
    let q: Double
    let result: BioChiSquare.Result

    var expected: [Double] { result.expected }

    var nullHypothesis: String {
        "The population is in Hardy–Weinberg equilibrium: the genotype counts fit p², 2pq and q² from the sample's own allele frequencies (p = \(HardyWeinberg.num(p)), q = \(HardyWeinberg.num(q)))."
    }

    var lowExpected: Bool { expected.contains { $0 < 5 } }

    var pText: String { result.p < 0.001 ? String(halfUp: "p = %.5f", result.p) : String(halfUp: "p = %.3f", result.p) }

    /// What the conclusion means here.
    var detail: String {
        guard result.reject else {
            return "The counts fit Hardy–Weinberg proportions as well as chance allows. This does not prove the population is in equilibrium."
        }
        let fewer = Double(counts.Aa) < expected[1]
        return fewer
            ? "There are fewer heterozygotes than equilibrium predicts — what inbreeding, or a population split into groups, gives. The population is probably not in equilibrium."
            : "There are more heterozygotes than equilibrium predicts — what heterozygote advantage, or mixing of two populations, can give. The population is probably not in equilibrium."
    }

    static let dfNote = "Some courses use df = 2 (classes − 1). Population genetics uses 1, because p was estimated from the same data."
}

// MARK: - The simulation (§4.7 Simulate, §5.4.3)

/// The simulation's settings.
struct HWSimParams: Codable, Equatable {
    /// The population size N (diploid; 2N alleles drawn each generation), or nil for an infinite population (no drift).
    var size: Int? = 100
    var p0: Double = 0.5
    var wAA: Double = 1
    var wAa: Double = 1
    var waa: Double = 1
    /// A → a and a → A per generation.
    var u: Double = 0
    var v: Double = 0
    /// The share of each generation's alleles from migrants, and their p.
    var m: Double = 0
    var pm: Double = 0.5
    /// The inbreeding coefficient (0: random mating).
    var F: Double = 0
    var replicates: Int = 5
    var seed: UInt64 = 1
    var generations: Int = 100

    static let sizes = 10...10_000
    static let generationChoices = [25, 50, 100, 200]
}

/// A generation of a population: p after it, the genotype frequencies after selection (P′, the bars), and the allele
/// count k drawn when N is finite.
struct HWGeneration: Equatable {
    let p: Double
    let AA: Double
    let Aa: Double
    let aa: Double
    let k: Int?
}

/// One replicate population's history.
struct HWPopulation: Equatable {
    let seed: UInt64
    var generations: [HWGeneration]
    /// The generation in which mean fitness reached 0, if it did.
    var extinctAt: Int?
}

/// A selection preset (§4.7): fitnesses w_AA, w_Aa, w_aa.
enum HWSelection: String, CaseIterable, Identifiable {
    case neutral, againstRecessive, againstDominant, heterozygoteAdvantage, recessiveLethal

    var id: String { rawValue }

    var title: String {
        switch self {
        case .neutral: return "None"
        case .againstRecessive: return "Against the recessive"
        case .againstDominant: return "Against the dominant"
        case .heterozygoteAdvantage: return "Heterozygote advantage"
        case .recessiveLethal: return "Recessive lethal"
        }
    }

    var fitness: (AA: Double, Aa: Double, aa: Double) {
        switch self {
        case .neutral: return (1, 1, 1)
        case .againstRecessive: return (1, 1, 0.6)
        case .againstDominant: return (0.6, 0.6, 1)
        case .heterozygoteAdvantage: return (0.8, 1, 0.6)
        case .recessiveLethal: return (1, 1, 0)
        }
    }

    /// The preset these fitnesses are, if any.
    static func matching(_ s: HWSimParams) -> HWSelection? {
        allCases.first { $0.fitness.AA == s.wAA && $0.fitness.Aa == s.wAa && $0.fitness.aa == s.waa }
    }
}

/// One of the five conditions (§5.4.1), and whether the settings break it.
struct HWAssumption: Identifiable, Equatable {
    let id: String
    let title: String
    let holds: Bool
    /// How the settings break it.
    let detail: String
}

enum HWSimulation {
    /// One generation from p (§5.4.3, in this order): mutation, migration, mating with inbreeding, selection, drift.
    /// nil when mean fitness is 0 (the population dies out).
    static func step(_ p: Double, _ s: HWSimParams, _ rng: inout BioRandom) -> HWGeneration? {
        // 1. Mutation, in the gametes
        let p1 = p * (1 - s.u) + (1 - p) * s.v
        // 2. Migration
        let p2 = (1 - s.m) * p1 + s.m * s.pm
        let q2 = 1 - p2
        // 3. Mating with inbreeding F
        let AA = p2 * p2 + s.F * p2 * q2
        let Aa = 2 * p2 * q2 * (1 - s.F)
        let aa = q2 * q2 + s.F * p2 * q2
        // 4. Selection
        let wbar = AA * s.wAA + Aa * s.wAa + aa * s.waa
        guard wbar > 0 else { return nil }
        let sAA = AA * s.wAA / wbar, sAa = Aa * s.wAa / wbar, saa = aa * s.waa / wbar
        let p3 = sAA + sAa / 2
        // 5. Drift: the next generation's 2N alleles drawn one by one
        guard let n = s.size else { return HWGeneration(p: p3, AA: sAA, Aa: sAa, aa: saa, k: nil) }
        var k = 0
        for _ in 0..<(2 * n) {
            if rng.uniform() < p3 { k += 1 }
        }
        // 6. Recorded
        return HWGeneration(p: Double(k) / Double(2 * n), AA: sAA, Aa: sAa, aa: saa, k: k)
    }

    /// Generation 0: p₀, its genotypes as mating with F makes them, and its allele count if it is a whole number.
    static func start(_ s: HWSimParams) -> HWGeneration {
        let p = s.p0, q = 1 - p
        var k: Int? = nil
        if let n = s.size {
            let x = p * Double(2 * n)
            if abs(x - x.rounded()) < 1e-9 { k = Int(x.rounded()) }
        }
        return HWGeneration(p: p, AA: p * p + s.F * p * q, Aa: 2 * p * q * (1 - s.F), aa: q * q + s.F * p * q, k: k)
    }

    /// Every replicate (replicate r, from 0, drawn from seed S + r), each for the asked generations or until it dies
    /// out. An infinite population has no drift, so it is run once. `cancelled` is asked between generations.
    static func run(_ s: HWSimParams, cancelled: () -> Bool = { false }) -> [HWPopulation] {
        let count = s.size == nil ? 1 : max(1, min(10, s.replicates))
        var out: [HWPopulation] = []
        for r in 0..<count {
            let seed = s.seed &+ UInt64(r)
            var rng = BioRandom(seed: seed)
            var pop = HWPopulation(seed: seed, generations: [start(s)], extinctAt: nil)
            var p = s.p0
            for t in 1...max(1, s.generations) {
                if cancelled() { return out }
                guard let g = step(p, s, &rng) else { pop.extinctAt = t; break }
                pop.generations.append(g)
                p = g.p
            }
            out.append(pop)
        }
        return out
    }

    /// The five conditions, and which the settings break.
    static func assumptions(_ s: HWSimParams) -> [HWAssumption] {
        let f = { (x: Double) in HardyWeinberg.num(x) }
        return [
            HWAssumption(id: "selection", title: "No selection", holds: s.wAA == s.wAa && s.wAa == s.waa,
                         detail: "Fitnesses \(f(s.wAA)), \(f(s.wAa)), \(f(s.waa)): some genotypes leave more offspring."),
            HWAssumption(id: "mutation", title: "No mutation", holds: s.u == 0 && s.v == 0,
                         detail: "u = \(f(s.u)), v = \(f(s.v)): alleles change into each other."),
            HWAssumption(id: "migration", title: "No migration", holds: s.m == 0,
                         detail: "m = \(f(s.m)): migrants bring alleles at p = \(f(s.pm))."),
            HWAssumption(id: "size", title: "Very large population", holds: s.size == nil,
                         detail: "N = \(s.size ?? 0): chance alone changes allele frequencies (genetic drift)."),
            HWAssumption(id: "mating", title: "Random mating", holds: s.F == 0,
                         detail: "F = \(f(s.F)): relatives mate, so there are more homozygotes than p² and q²."),
        ]
    }

    /// What breaking those conditions does.
    static func verdict(_ s: HWSimParams) -> String {
        let broken = assumptions(s).filter { !$0.holds }.map(\.id)
        if broken.isEmpty { return "All five hold: allele and genotype frequencies stay the same — no evolution, whichever allele is dominant." }
        if broken == ["mating"] { return "→ Genotype frequencies change (more homozygotes), but allele frequencies stay the same." }
        return "→ Allele frequencies can change (evolution)."
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Hardy–Weinberg's own `bio-check` lines: every vector of §5.4.2–§5.4.3, the drift sequence included.
    static func popgenChecks(_ r: inout BioCheckReport) {
        // (§5.4.2's second vector: q² = 0.16 → q = 0.40, p = 0.60, 2pq = 0.48, each step written out)
        if let c = HardyWeinberg.calculate(.q2, 0.16).calc {
            r.check("hw.q2=0.16", "q\(HardyWeinberg.num(c.q)),p\(HardyWeinberg.num(c.p)),2pq\(HardyWeinberg.num(c.twoPQ))",
                    abs(c.q - 0.4) < 1e-12 && abs(c.p - 0.6) < 1e-12 && abs(c.twoPQ - 0.48) < 1e-12
                    && c.steps.contains { $0.math == "q = √q² = √0.16 = 0.40" } && c.steps.contains { $0.math == "2pq = 2 × 0.60 × 0.40 = 0.48" }
                    && c.steps.contains { $0.math.hasPrefix("2pq × 1,000 = 480 ") })
        } else {
            r.check("hw.q2=0.16", "error", false)
        }
        // (every other way in gives the same population)
        let ways: [(HWGiven, Double)] = [(.p, 0.6), (.q, 0.4), (.p2, 0.36), (.twoPQ, 0.48), (.recessivePercent, 16), (.dominantPercent, 84)]
        let wrong = ways.filter { w in
            guard let c = HardyWeinberg.calculate(w.0, w.1).calc else { return true }
            return abs(c.p - 0.6) > 1e-12 || abs(c.q2 - 0.16) > 1e-12
        }.map(\.0.rawValue)
        let twoRoots = HardyWeinberg.calculate(.twoPQ, 0.48).calc?.otherP
        r.check("hw.calculator", wrong.isEmpty ? "6ways" : wrong.joined(separator: ","), wrong.isEmpty && twoRoots != nil && abs((twoRoots ?? 0) - 0.4) < 1e-12)
        let impossible = [HardyWeinberg.calculate(.q2, 1.2).error, HardyWeinberg.calculate(.p, -0.1).error, HardyWeinberg.calculate(.twoPQ, 0.6).error,
                          HardyWeinberg.calculate(.recessivePercent, 120).error]
        r.check("hw.impossible", "\(impossible.compactMap { $0 }.count)said", impossible.allSatisfy { $0 != nil }
                && impossible[0] == "q² must be between 0 and 1.")
        // (the dot grid's 100 people always add to 100)
        let grid = HardyWeinberg.people([0.36, 0.48, 0.16]), grid2 = HardyWeinberg.people([0.4225, 0.455, 0.1225])
        r.check("hw.people", grid.map(String.init).joined(separator: ",") + "|" + grid2.map(String.init).joined(separator: ","),
                grid == [36, 48, 16] && grid2 == [42, 46, 12])
        r.same("hw.format", [HardyWeinberg.num(1), HardyWeinberg.num(0.4225), HardyWeinberg.num(0.000025), HardyWeinberg.carriers(19.8), HardyWeinberg.carriers(4.56)].joined(separator: ","),
               "1.00,0.4225,2.50 × 10⁻⁵,20,4.6")

        // (§5.4.2's first vector: 50, 30, 20 → p 0.65, expected 42.25, 45.50, 12.25, χ² 11.6049, p 0.00066 → reject)
        let sample = HWCounts(AA: 50, Aa: 30, aa: 20)
        if let c = HardyWeinberg.calculate(counts: sample).calc, let t = HardyWeinberg.test(sample).test {
            r.check("hw.counts", "p\(HardyWeinberg.num(c.p)),q\(HardyWeinberg.num(c.q))", abs(c.p - 0.65) < 1e-12 && abs(c.q - 0.35) < 1e-12)
            r.check("hw.test", String(format: "p%.4f,q%.4f,e%.2f/%.2f/%.2f,chi%.4f,P%.5f", t.p, t.q, t.expected[0], t.expected[1], t.expected[2], t.result.chi2, t.result.p),
                    String(format: "%.4f %.4f", t.p, t.q) == "0.6500 0.3500" && zip(t.expected, [42.25, 45.5, 12.25]).allSatisfy { abs($0 - $1) < 1e-9 }
                    && abs(t.result.chi2 - 11.6049) < 5e-5 && abs(t.result.p - 0.00066) < 5e-6 && t.result.df == 1 && t.result.reject
                    && t.result.critical == 3.84 && t.detail.contains("fewer heterozygotes"))
            let two = HardyWeinberg.test(sample, df: 2).test
            r.check("hw.test.df2", String(format: "%.2f", two?.result.critical ?? 0), two?.result.df == 2 && two?.result.critical == 5.99)
        } else {
            r.check("hw.test", "error", false)
        }
        r.check("hw.test.monomorphic", "said", HardyWeinberg.test(HWCounts(AA: 10, Aa: 0, aa: 0)).test == nil)

        // (§5.4.3: recessive lethal from p = 0.5, infinite N: q = 1/3, 1/4, 1/5)
        var lethal = HWSimParams(size: nil, p0: 0.5, wAA: 1, wAa: 1, waa: 0)
        lethal.generations = 3
        let lq = HWSimulation.run(lethal).first?.generations.dropFirst().map { 1 - $0.p } ?? []
        r.check("hw.sim.lethal", lq.map { String(format: "%.6f", $0) }.joined(separator: ","),
                lq.count == 3 && zip(lq, [1.0 / 3, 0.25, 0.2]).allSatisfy { abs($0 - $1) < 1e-6 })
        func once(_ s: HWSimParams) -> HWGeneration? {
            var x = s
            x.size = nil
            x.generations = 1
            return HWSimulation.run(x).first?.generations.last
        }
        r.near("hw.sim.mutation", once(HWSimParams(p0: 0.9, u: 1e-4))?.p ?? 0, 0.89991, 1e-12, digits: 5)
        r.near("hw.sim.migration", once(HWSimParams(p0: 0.2, m: 0.1, pm: 0.8))?.p ?? 0, 0.26, 1e-12, digits: 2)
        let inbred = once(HWSimParams(p0: 0.6, F: 0.25))
        r.check("hw.sim.inbreeding", String(format: "%.2f,%.2f,%.2f,p%.2f", inbred?.AA ?? 0, inbred?.Aa ?? 0, inbred?.aa ?? 0, inbred?.p ?? 0),
                abs((inbred?.AA ?? 0) - 0.42) < 1e-12 && abs((inbred?.Aa ?? 0) - 0.36) < 1e-12 && abs((inbred?.aa ?? 0) - 0.22) < 1e-12 && abs((inbred?.p ?? 0) - 0.6) < 1e-12)
        // (drift: N = 10, p₀ = 0.5, seed 7, nothing else: k = 10, 10, 12, 13, 12, 10 for generations 0–5)
        var drift = HWSimParams(size: 10, p0: 0.5)
        drift.seed = 7
        drift.replicates = 2
        drift.generations = 5
        let pops = HWSimulation.run(drift)
        let ks = pops.first?.generations.compactMap(\.k) ?? []
        r.check("hw.sim.drift", ks.map(String.init).joined(separator: ","), ks == [10, 10, 12, 13, 12, 10] && pops.first?.seed == 7)
        // (replicate 1 is seed 8's population; the same seed gives the same populations every time)
        var eight = drift
        eight.seed = 8
        eight.replicates = 1
        r.check("hw.sim.replicates", "\(pops.count)", pops.count == 2 && pops.last?.generations == HWSimulation.run(eight).first?.generations
                && HWSimulation.run(drift) == pops && pops[0].generations != pops[1].generations)
        // (no forces: p stays put, the dominant allele no more likely to spread than the recessive; mean fitness 0: extinct)
        var still = HWSimParams(size: nil, p0: 0.3)
        still.generations = 200
        let stillP = HWSimulation.run(still).first?.generations.map(\.p) ?? []
        var dead = HWSimParams(size: 50, p0: 0.5, wAA: 0, wAa: 0, waa: 0)
        dead.generations = 10
        r.check("hw.sim.no-forces", String(format: "%.6f", stillP.last ?? 0),
                stillP.count == 201 && stillP.allSatisfy { abs($0 - 0.3) < 1e-12 } && HWSimulation.run(dead).first?.extinctAt == 1)
        // (a big run stays quick: N = 10,000, 10 replicates, 200 generations)
        var big = HWSimParams(size: 10_000, p0: 0.5)
        big.replicates = 10
        big.generations = 200
        let started = Date()
        let bigRun = HWSimulation.run(big)
        let ms = Date().timeIntervalSince(started) * 1000
        r.check("hw.sim.size", "\(bigRun.count)x\(bigRun.first?.generations.count ?? 0)", bigRun.count == 10 && bigRun.allSatisfy { $0.generations.count == 201 } && ms < 20_000)
        // (the five conditions: all hold with no forces; inbreeding alone moves genotypes, not alleles)
        let calm = HWSimParams(size: nil)
        var onlyF = calm
        onlyF.F = 0.3
        let finite = HWSimParams(size: 100)
        r.check("hw.assumptions", HWSimulation.assumptions(finite).filter { !$0.holds }.map(\.id).joined(separator: ","),
                HWSimulation.assumptions(calm).allSatisfy(\.holds) && HWSimulation.verdict(onlyF).contains("allele frequencies stay the same")
                && HWSimulation.assumptions(finite).filter { !$0.holds }.map(\.id) == ["size"] && HWSimulation.verdict(finite).contains("Allele frequencies can change")
                && HWSelection.matching(lethal) == .recessiveLethal && HWSelection.matching(calm) == .neutral)
    }
}
