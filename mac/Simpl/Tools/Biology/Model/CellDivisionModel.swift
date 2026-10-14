import Foundation

// Cell Division's own model (spec §4.6, §5.2): what the tool works out without a window — mitosis and meiosis step by
// step for the model organism (2n = 4: a long pair of 7 units and a short pair of 4, one homolog of each from the
// mother, red, and one from the father, blue; 2n = 6 adds a pair of 5 units), each step's keyframe
// (CellDivisionKeyframes.swift) and its counts — chromosomes (counted by centromeres), DNA molecules (chromatids),
// ploidy (complete sets, not chromatids), DNA amount (c), whether homologs are present and whether sisters are still
// joined — counted from the keyframe itself, so what is drawn and what is counted cannot disagree, and worded as the
// specification's tables word them (checked against them, below). Crossing over, independent assortment and
// nondisjunction change the keyframes and so the counts. Also the Compare page's paired steps and table (§5.2.3) and
// the Cell cycle page's wheel, checkpoints and cyclins (§4.6). Foundation only, so the Mac app's workflow (Models,
// scripts/dev/swift-checks) compiles and checks it with the rest of Biology/Model.

enum DivisionProcess: String, CaseIterable {
    case mitosis, meiosis

    var title: String { self == .mitosis ? "Mitosis" : "Meiosis" }
}

/// Nondisjunction (spec §4.6, Should): the short pair fails to separate in meiosis I (its homologs go to one pole), or
/// in one cell in meiosis II (one chromosome's sister chromatids go to one pole).
enum DivisionNondisjunction: String, CaseIterable {
    case none, meiosisI, meiosisII

    var title: String {
        switch self {
        case .none: return "None"
        case .meiosisI: return "In meiosis I"
        case .meiosisII: return "In meiosis II"
        }
    }
}

/// What the student set.
struct DivisionOptions: Equatable {
    var plant = false
    /// n: 2 (2n = 4) or 3 (2n = 6).
    var pairs = 2
    var crossingOver = true
    /// Where on the long pair's long arm the chiasma is, from the centromere: 0.2…0.8 of the arm.
    var chiasma = 0.5
    /// Per pair: its father's homolog faces the top pole in metaphase I (independent assortment; the long pair is the
    /// reference and is never flipped).
    var flipped: [Bool] = [false, false, false]
    var nondisjunction: DivisionNondisjunction = .none

    static let chiasmaRange = 0.2...0.8
}

/// A step: its title (≤ 40 characters), what happens (plain for the controls and VoiceOver; `rich` keeps the
/// specification's bold key terms for the stage card), its stage group (the number keys) and what to notice.
struct DivisionStage: Equatable {
    let key: DivStageKey
    let title: String
    let text: String
    let rich: String
    let group: Int
    let notice: [String]
}

/// The counts panel at a step (spec §4.6, §5.2), worded as the specification's tables word them. Values for a human
/// cell (2n = 46) beside the model's.
struct DivisionCounts: Equatable {
    var chromosomes: String
    var chromosomesHuman: String
    var chromatids: String
    var chromatidsHuman: String
    var ploidy: String
    var dna: String
    var homologs: String
    var sisters: String
    /// When cells (or poles) differ, as after nondisjunction: what the listed values are, in order.
    var listing: String?
}

/// What a set of chromatids (a cell, a pole, a nucleus) holds.
struct DivTally {
    let n: Int
    var chromosomes = 0
    var chromatids = 0
    /// DNA, in chromosome units (a chromatid of the long pair is 7).
    var units = 0.0
    /// Chromosomes and chromatids of each pair.
    var perPair: [Int]
    var perPairChromatids: [Int]
    /// Each pair's homologs present (0 the mother's, 1 the father's).
    var homologs: [Set<Int>]
    var joined = 0

    init(_ keys: [DivChromatidKey], pairs n: Int) {
        self.n = n
        perPair = Array(repeating: 0, count: n)
        perPairChromatids = Array(repeating: 0, count: n)
        homologs = Array(repeating: [], count: n)
        let existing = keys.filter(\.exists)
        chromatids = existing.count
        for k in existing {
            units += DivGeometry.pairs[k.id.pair].units
            perPairChromatids[k.id.pair] += 1
            homologs[k.id.pair].insert(k.id.homolog)
            if k.joined { joined += 1 }
        }
        // (a chromosome is a centromere: two sisters still joined count once, a chromatid on its own once)
        for unit in Set(existing.map(\.id.chromosome)).sorted() {
            let members = existing.filter { $0.id.chromosome == unit }
            let together = members.filter(\.joined).count
            let count = (together >= 2 ? 1 : together) + members.filter { !$0.joined }.count
            chromosomes += count
            perPair[unit / 2] += count
        }
    }

    /// Complete sets: "n", "2n", "4n", or with one pair off by some, "n+1", "n−1" (aneuploid).
    var ploidy: String {
        let base = perPair[0]
        if perPair.allSatisfy({ $0 == base }) { return DivTally.sets(base) }
        let d = perPair.dropFirst().reduce(0) { $0 + $1 - base }
        return DivTally.sets(base) + (d > 0 ? "+\(d)" : (d < 0 ? "−\(-d)" : "±"))
    }

    static func sets(_ m: Int) -> String { m == 1 ? "n" : "\(m)n" }

    /// DNA relative to one haploid set of unreplicated chromosomes (1c).
    var c: Double { units / DivGeometry.pairs.prefix(n).reduce(0) { $0 + $1.units } }

    /// The same cell in a human (23 pairs): each of the 23 as the long pair, the short pair's difference added.
    var humanChromosomes: Int { perPair[0] * 23 + perPair.dropFirst().reduce(0) { $0 + $1 - perPair[0] } }
    var humanChromatids: Int { perPairChromatids[0] * 23 + perPairChromatids.dropFirst().reduce(0) { $0 + $1 - perPairChromatids[0] } }

    /// Pairs with both homologs here.
    var bothHomologs: Int { homologs.filter { $0.count == 2 }.count }

    func same(_ o: DivTally) -> Bool { chromosomes == o.chromosomes && chromatids == o.chromatids && ploidy == o.ploidy && abs(units - o.units) < 1e-9 }
}

/// A step of the Compare page: mitosis's step beside meiosis's (spec §4.6: prophase ↔ prophase I … telophase ↔
/// telophase II). Mitosis is over after its fifth; it stays on its two cells while meiosis II runs. `rows` are the
/// §5.2.3 rows the step is about.
struct DivCompareStep: Equatable {
    let title: String
    let text: String
    let mitosis: Int
    let meiosis: Int
    let group: Int
    let rows: [Int]
}

/// A phase of the cell cycle (spec §4.6: G1 about 11 h, S about 8 h, G2 about 4 h, M about 1 h for a typical 24 h human
/// cell, Alberts; G0 off G1).
struct DivCyclePhase: Equatable {
    let id: String
    let name: String
    let hours: Double
    let title: String
    let text: String
}

/// A checkpoint: a gate on the wheel (degrees clockwise from the top), what it checks, the cyclin–CDK part, and what
/// goes wrong when it fails (the cancer view).
struct DivCheckpoint: Equatable {
    let id: String
    let name: String
    let short: String
    let angle: Double
    let checks: [String]
    let role: String
    let cyclin: String
    let cancer: String
}

/// A labelled part's card: its name, what it is, and a common mix-up.
struct DivPartInfo: Equatable {
    let title: String
    let what: String
    let mixUp: String?
}

/// A cell at the end of a process: its chromatids, its ploidy and its chromosome count.
struct DivProduct {
    let cell: Int
    let chromatids: [DivChromatidKey]
    let tally: DivTally
}

/// A process worked out for some options: its stages, keyframes and counts.
struct DivisionSequence {
    let process: DivisionProcess
    let options: DivisionOptions
    let stages: [DivisionStage]
    let keyframes: [DivisionKeyframe]
    let counts: [DivisionCounts]

    var frames: [DivisionFrame] { keyframes.map(\.frame) }

    var steps: [BioStep] {
        stages.enumerated().map { BioStep($0.offset, $0.element.title, $0.element.text, group: $0.element.group, notice: $0.element.notice) }
    }

    /// A badge's words: the ploidy of that cell at that step ("2n", "n", "n+1").
    func badge(step: Int, cell: Int) -> String {
        DivTally(keyframes[step].frame.chromatids.filter { $0.cell == cell }, pairs: options.pairs).ploidy
    }

    /// The cells it ends with.
    var products: [DivProduct] {
        guard let last = keyframes.last else { return [] }
        let keys = last.frame.chromatids.filter(\.exists)
        return Dictionary(grouping: keys, by: \.cell).sorted { $0.key < $1.key }.map {
            DivProduct(cell: $0.key, chromatids: $0.value.sorted { $0.id < $1.id }, tally: DivTally($0.value, pairs: options.pairs))
        }
    }
}

enum DivisionModel {
    // MARK: The specification's tables (copied out of it by a script, as written)

    /// §5.2.1 as the specification writes it: Step | What happens | Chromosomes in the cell | DNA molecules (chromatids) | Ploidy | DNA.
    static let mitosisTable: [[String]] = [
        ["G1", "Cell grows, makes proteins and organelles. Chromosomes are long, uncondensed threads (chromatin).", "4 (46)", "4 (46)", "2n", "2c"],
        ["S", "DNA replication: each chromosome becomes two sister chromatids.", "4 (46)", "8 (92)", "2n", "4c"],
        ["G2", "More growth; preparation for division; centrosome has duplicated.", "4 (46)", "8 (92)", "2n", "4c"],
        ["Prophase", "Chromatin condenses into visible chromosomes (sisters joined at the centromere); spindle starts forming as centrosomes move apart; nucleolus disappears.", "4", "8", "2n", "4c"],
        ["Prometaphase", "Nuclear envelope breaks down; kinetochore microtubules attach to each chromatid's kinetochore (sisters to opposite poles).", "4", "8", "2n", "4c"],
        ["Metaphase", "Chromosomes line up singly on the metaphase plate. Spindle checkpoint: all attached?", "4", "8", "2n", "4c"],
        ["Anaphase", "Sister chromatids separate; each is now a chromosome and moves to opposite poles.", "8 (92)", "8", "4n in one cell (two diploid sets moving apart)", "4c"],
        ["Telophase", "Nuclear envelopes re-form; chromosomes decondense.", "8 (in two nuclei)", "8", "2n per nucleus", "4c total"],
        ["Cytokinesis", "Animal: cleavage furrow (actin–myosin ring). Plant: cell plate from Golgi vesicles.", "4 per cell", "4 per cell", "2n", "2c"],
        ["Result", "Two genetically identical diploid cells.", "", "", "", ""],
    ]
    /// §5.2.2 as the specification writes it: Step | What happens | Chromosomes per cell | DNA molecules per cell | Ploidy | DNA per cell.
    static let meiosisTable: [[String]] = [
        ["Interphase (G1, S, G2)", "As mitosis: DNA replicates **once**, before meiosis I.", "4", "4 → 8", "2n", "2c → 4c"],
        ["Prophase I", "Chromosomes condense; **homologs pair (synapsis)** to form **tetrads (bivalents)**, 2 here (23 in humans); **crossing over** between non-sister chromatids at chiasmata; spindle forms; envelope breaks down.", "4 (as 2 tetrads)", "8", "2n", "4c"],
        ["Metaphase I", "**Homologous pairs** line up at the plate (pairs, not single chromosomes). The orientation of each pair is random (**independent assortment**).", "4", "8", "2n", "4c"],
        ["Anaphase I", "**Homologs separate** to opposite poles; **sister chromatids stay together**.", "4 (2 to each pole)", "8", "2n → two sets of n", "4c"],
        ["Telophase I + cytokinesis", "Two cells.", "2 per cell", "4 per cell", "**n (haploid)**", "2c"],
        ["(no S phase)", "No DNA replication between meiosis I and II.", "", "", "", ""],
        ["Prophase II", "Spindle forms in each cell.", "2", "4", "n", "2c"],
        ["Metaphase II", "Chromosomes line up singly at each plate (like mitosis).", "2", "4", "n", "2c"],
        ["Anaphase II", "**Sister chromatids separate.**", "4 (2 to each pole)", "4", "n per pole", "2c"],
        ["Telophase II + cytokinesis", "Four haploid cells, genetically different (crossing over and independent assortment).", "2 per cell", "2 per cell", "n", "1c"],
    ]
    /// §5.2.3 as the specification writes it: Mitosis | Meiosis.
    static let compareTable: [[String]] = [
        ["Purpose", "Growth, repair, asexual reproduction", "Making gametes (sexual reproduction)"],
        ["DNA replications", "1", "1"],
        ["Divisions", "1", "2"],
        ["Synapsis of homologs / tetrads", "No", "Yes (prophase I)"],
        ["Crossing over", "No (normally)", "Yes (prophase I)"],
        ["What lines up at metaphase", "Individual chromosomes (metaphase)", "Homologous pairs (metaphase I); individual chromosomes (metaphase II)"],
        ["What separates at anaphase", "Sister chromatids", "Homologs (anaphase I); sister chromatids (anaphase II)"],
        ["Daughter cells", "2, diploid (2n), identical to the parent", "4, haploid (n), genetically different"],
        ["Where in humans", "Body (somatic) cells", "Ovaries and testes"],
    ]

    /// The tables' text without their bold marks.
    static func plain(_ s: String) -> String { s.replacingOccurrences(of: "**", with: "") }

    // MARK: Steps

    /// The stage groups the number keys jump to.
    static func groups(_ p: DivisionProcess) -> [Int: String] {
        p == .mitosis
            ? [1: "Interphase", 2: "Prophase", 3: "Metaphase", 4: "Anaphase", 5: "Telophase", 6: "Cytokinesis"]
            : [1: "Interphase", 2: "Prophase I", 3: "Metaphase I", 4: "Anaphase I", 5: "Telophase I", 6: "Prophase II",
               7: "Metaphase II", 8: "Anaphase II", 9: "Telophase II"]
    }

    static func stages(_ p: DivisionProcess, _ o: DivisionOptions) -> [DivisionStage] {
        func stage(_ key: DivStageKey, _ title: String, _ rich: String, _ group: Int, _ notice: [String]) -> DivisionStage {
            DivisionStage(key: key, title: title, text: plain(rich), rich: rich, group: group, notice: notice)
        }
        let n = o.pairs, two = 2 * n, four = 4 * n
        let m = mitosisTable, e = meiosisTable
        if p == .mitosis {
            let cyto = o.plant
                ? "A **cell plate** from Golgi vesicles grows from the middle outward into a new wall. Result: two genetically identical diploid cells."
                : "A **cleavage furrow** (an actin–myosin ring) pinches the cell in two. Result: two genetically identical diploid cells."
            return [
                stage(.g1, "G1", m[0][1], 1, ["Each chromosome is one DNA molecule now: \(two) chromosomes, \(two) DNA molecules.",
                                               "Homologous chromosomes are here too (one red and one blue of each pair); in mitosis they never pair."]),
                stage(.s, "S phase", m[1][1], 1, ["DNA replicates here, in S phase of interphase, never in prophase.",
                                                  "Still \(two) chromosomes: a replicated chromosome is still ONE chromosome. DNA molecules double to \(four)."]),
                stage(.g2, "G2", m[2][1], 1, ["4c: twice G1's DNA, with the same number of chromosomes.",
                                              o.plant ? "Plant cells have no centrioles: their spindle forms without centrosomes." : "Two centrosomes now, each with a pair of centrioles."]),
                stage(.prophase, "Prophase", m[3][1], 2, ["Count centromeres to count chromosomes: still \(two).",
                                                          "Each chromosome is two sister chromatids joined at its centromere."]),
                stage(.prometaphase, "Prometaphase", m[4][1], 2, ["Each chromosome's two kinetochores face opposite poles."]),
                stage(.metaphase, "Metaphase", m[5][1], 3, ["Chromosomes line up one by one, not in pairs.",
                                                            "Spindle checkpoint: anaphase waits until every kinetochore is attached."]),
                stage(.anaphase, "Anaphase", m[6][1], 4, ["Sisters separate: each chromatid is now a chromosome, so the count doubles to \(four).",
                                                          "The DNA amount does not change: still 4c in the cell."]),
                stage(.telophase, "Telophase", m[7][1], 5, ["Two nuclei, each with a complete diploid set (2n).",
                                                            o.plant ? "A cell plate starts in the middle." : "The cleavage furrow starts to pinch the cell."]),
                stage(.cytokinesis, "Cytokinesis", cyto, 6, ["Two cells, each 2n = \(two) and 2c: identical to each other and to the parent cell."]),
            ]
        }
        let ndI = o.nondisjunction == .meiosisI, ndII = o.nondisjunction == .meiosisII
        let prophaseI = e[1][1].replacingOccurrences(of: "2 here (23 in humans)", with: "\(n) here (23 in humans)")
        var proNotice = ["Homologs pair up (synapsis): \(n) tetrads, each 2 chromosomes and 4 chromatids."]
        proNotice.append(o.crossingOver ? "Crossing over: two non-sister chromatids swap the part beyond the chiasma."
                                        : "Crossing over is off: no chiasma, no swapped parts.")
        var anaNotice = ["Homologs separate; each keeps both its chromatids.", "Still 4c in the cell: nothing has been copied or lost."]
        if ndI { anaNotice.insert("Nondisjunction: the short pair's homologs both go to the top pole.", at: 0) }
        var telINotice = ["Each cell is haploid (n) although every chromosome still has two chromatids.", "Ploidy counts sets, not chromatids."]
        if ndI { telINotice = ["After nondisjunction one cell has n+1 chromosomes and the other n−1."] + telINotice.suffix(1) }
        var anaIINotice = ["Now the sister chromatids separate, as in mitosis's anaphase."]
        if ndII { anaIINotice.insert("Nondisjunction: in the top cell one chromosome's sisters fail to separate.", at: 0) }
        var telIINotice = ["Four haploid cells, each 1c."]
        if o.nondisjunction == .none {
            telIINotice.append(o.crossingOver ? "All four are genetically different: crossing over and independent assortment."
                                              : "Without crossing over, the cells come in identical pairs; independent assortment still mixes the pairs.")
        } else {
            telIINotice.append("Gametes labelled n+1 and n−1 have one chromosome too many or too few. An n+1 gamete with an extra chromosome 21 joining a normal one makes trisomy 21 (Down syndrome, 47 chromosomes).")
        }
        // (the specification's sentence is the usual case; without crossing over, or with nondisjunction, it says what
        // is different)
        let telII: String
        if o.nondisjunction != .none {
            telII = "Four cells, but nondisjunction left \(ndI ? "all four" : "two of them") with a chromosome too many (n+1) or too few (n−1)."
        } else if !o.crossingOver {
            telII = "Four haploid cells. Without crossing over they come in two identical pairs; independent assortment decided which homologs went together."
        } else {
            telII = e[9][1]
        }
        return [
            stage(.g1, "G1", "As in mitosis, the cell grows and makes proteins and organelles. Chromosomes are long, uncondensed threads (chromatin).", 1,
                  ["Diploid: \(two) chromosomes, \(n) homologous pairs (one of each pair from each parent)."]),
            stage(.s, "S phase", "DNA replicates **once**, before meiosis I: each chromosome becomes two sister chromatids.", 1,
                  ["This is the only S phase: there is none between meiosis I and II.", "Still \(two) chromosomes; \(four) DNA molecules."]),
            stage(.g2, "G2", "More growth and preparation for division; the centrosome has duplicated.", 1, ["2n and 4c, as before mitosis."]),
            stage(.prophase1, "Prophase I", prophaseI, 2, proNotice),
            stage(.metaphase1, "Metaphase I", e[2][1], 3, ["Pairs line up, not single chromosomes.",
                                                           "Which homolog faces which pole is random for each pair: independent assortment."]),
            stage(.anaphase1, "Anaphase I", e[3][1], 4, anaNotice),
            stage(.telophase1, "Telophase I + cytokinesis", "Two cells, each **haploid (n)**: one chromosome of each pair, still made of two chromatids.", 5, telINotice),
            stage(.prophase2, "Prophase II", "No DNA replication between meiosis I and II. " + e[6][1], 6,
                  ["No S phase before meiosis II: each cell still has 2c."]),
            stage(.metaphase2, "Metaphase II", e[7][1], 7, ["Single chromosomes line up, as in mitosis."]),
            stage(.anaphase2, "Anaphase II", e[8][1], 8, anaIINotice),
            stage(.telophase2, "Telophase II + cytokinesis", telII, 9, telIINotice),
        ]
    }

    // MARK: Counts

    static func counts(_ kf: DivisionKeyframe, process: DivisionProcess, pairs n: Int) -> DivisionCounts {
        let keys = kf.frame.chromatids.filter(\.exists)
        let whole = DivTally(keys, pairs: n)
        let cellKeys = Dictionary(grouping: keys, by: \.cell).sorted { $0.key < $1.key }.map(\.value)
        let cells = cellKeys.map { DivTally($0, pairs: n) }
        func parts(_ ks: [DivChromatidKey]) -> [DivTally] {
            Dictionary(grouping: ks, by: \.part).sorted { $0.key < $1.key }.map { DivTally($0.value, pairs: n) }
        }
        func chr(_ t: DivTally, _ human: Bool) -> Int { human ? t.humanChromosomes : t.chromosomes }
        func cds(_ t: DivTally, _ human: Bool) -> Int { human ? t.humanChromatids : t.chromatids }
        func word(_ p: String) -> String { p == "2n" ? "diploid" : (p == "n" ? "haploid" : p) }

        var chromosomes: (Bool) -> String = { "\(chr(whole, $0))" }
        var chromatids: (Bool) -> String = { "\(cds(whole, $0))" }
        var ploidy = whole.ploidy
        var dna = cText(whole.c)
        var listing: String?

        switch kf.style {
        case .cell:
            break
        case .tetrads:
            let tetrads = (0..<n).filter { whole.homologs[$0].count == 2 }.count
            chromosomes = { "\(chr(whole, $0)) (as \($0 ? 23 : tetrads) tetrads)" }
        case .anaphase:
            let poles = parts(keys)
            if poles.count == 2, poles[0].ploidy == poles[1].ploidy {
                ploidy = "\(whole.ploidy) in one cell (two \(word(poles[0].ploidy)) sets moving apart)"
            } else {
                ploidy = "\(whole.ploidy) in one cell (\(poles.map(\.ploidy).joined(separator: " and ")) moving apart)"
            }
        case .twoNuclei:
            let nuclei = parts(keys)
            chromosomes = { "\(chr(whole, $0)) (in two nuclei)" }
            ploidy = Set(nuclei.map(\.ploidy)).count == 1 ? "\(nuclei[0].ploidy) per nucleus" : nuclei.map(\.ploidy).joined(separator: " and ")
            dna = cText(whole.c) + " total"
        case .anaphaseI:
            let poles = parts(keys)
            if poles.count == 2, poles[0].same(poles[1]) {
                chromosomes = { "\(chr(whole, $0)) (\(chr(poles[0], $0)) to each pole)" }
                ploidy = "\(whole.ploidy) → two sets of \(poles[0].ploidy)"
            } else {
                chromosomes = { h in "\(chr(whole, h)) (\(poles.map { "\(chr($0, h))" }.joined(separator: " and ")) to the poles)" }
                ploidy = "\(whole.ploidy) → " + poles.map(\.ploidy).joined(separator: " and ")
            }
        case .perCell, .haploidCells, .perCellPlain:
            let suffix = kf.style == .perCellPlain ? "" : " per cell"
            if let first = cells.first, cells.allSatisfy({ $0.same(first) }) {
                chromosomes = { "\(chr(first, $0))\(suffix)" }
                chromatids = { "\(cds(first, $0))\(suffix)" }
                ploidy = first.ploidy + (kf.style == .haploidCells && first.ploidy == "n" ? " (haploid)" : "")
                dna = cText(first.c)
            } else {
                chromosomes = { h in cells.map { "\(chr($0, h))" }.joined(separator: " · ") }
                chromatids = { h in cells.map { "\(cds($0, h))" }.joined(separator: " · ") }
                ploidy = cells.map(\.ploidy).joined(separator: " · ")
                dna = cells.map { cText($0.c) }.joined(separator: " · ")
                listing = cells.count == 2 ? "Top cell · bottom cell" : "Top left · top right · bottom left · bottom right"
            }
        case .anaphaseII:
            func cellChromosomes(_ i: Int, _ h: Bool) -> String {
                let poles = parts(cellKeys[i])
                if poles.count == 2, poles[0].same(poles[1]) { return "\(chr(cells[i], h)) (\(chr(poles[0], h)) to each pole)" }
                return "\(chr(cells[i], h)) (\(poles.map { "\(chr($0, h))" }.joined(separator: " and ")) to the poles)"
            }
            func cellPloidy(_ i: Int) -> String {
                let poles = parts(cellKeys[i])
                if poles.count == 2, poles[0].ploidy == poles[1].ploidy { return "\(poles[0].ploidy) per pole" }
                return poles.map(\.ploidy).joined(separator: " and ")
            }
            let texts = cells.indices.map { cellChromosomes($0, false) }
            if Set(texts).count == 1, let first = cells.first, cells.allSatisfy({ $0.same(first) }), Set(cells.indices.map(cellPloidy)).count == 1 {
                chromosomes = { cellChromosomes(0, $0) }
                chromatids = { "\(cds(first, $0))" }
                ploidy = cellPloidy(0)
                dna = cText(first.c)
            } else {
                chromosomes = { h in cells.indices.map { cellChromosomes($0, h) }.joined(separator: " · ") }
                chromatids = { h in cells.map { "\(cds($0, h))" }.joined(separator: " · ") }
                ploidy = cells.indices.map(cellPloidy).joined(separator: " · ")
                dna = cells.map { cText($0.c) }.joined(separator: " · ")
                listing = "Top cell · bottom cell"
            }
        }

        // (homologs: present in every diploid cell, in mitosis too; paired only in meiosis I)
        let homologs: String
        let perCellStyle = [DivCountStyle.perCell, .haploidCells, .perCellPlain, .anaphaseII].contains(kf.style)
        func presence(_ t: DivTally) -> String {
            let b = t.bothHomologs
            return b == n ? "Yes" : (b == 0 ? "No" : "\(b) of \(n) pairs")
        }
        if kf.paired {
            homologs = "Yes, paired as tetrads"
        } else if perCellStyle {
            let each = cells.map(presence)
            homologs = Set(each).count <= 1 ? (each.first ?? "No") : each.joined(separator: " · ")
        } else if whole.bothHomologs == n {
            homologs = process == .mitosis ? "Yes, but they don't pair" : (kf.style == .anaphaseI ? "Yes, separating" : "Yes")
        } else {
            homologs = presence(whole)
        }

        // (sisters: joined from S until anaphase, or anaphase II)
        let sisters: String
        let copied = kf.frame.chromatids.contains { $0.exists && $0.id.sister == 1 }
        if whole.chromatids > 0, whole.joined == whole.chromatids {
            sisters = kf.style == .anaphaseI ? "Yes, they stay together" : "Yes"
        } else if whole.joined == 0 {
            sisters = !copied ? "Not copied yet" : ([.anaphase, .anaphaseII].contains(kf.style) ? "No: separated, each now a chromosome" : "No: one chromatid each")
        } else {
            sisters = "Partly: one chromosome's sisters did not separate"
        }

        return DivisionCounts(chromosomes: chromosomes(false), chromosomesHuman: chromosomes(true), chromatids: chromatids(false),
                              chromatidsHuman: chromatids(true), ploidy: ploidy, dna: dna, homologs: homologs, sisters: sisters, listing: listing)
    }

    /// "2c", or "1.4c" for an aneuploid cell.
    static func cText(_ c: Double) -> String { abs(c - c.rounded()) < 1e-9 ? "\(Int(c.rounded()))c" : String(halfUp: "%.1fc", c) }

    // MARK: Sequences

    static func sequence(_ p: DivisionProcess, _ o: DivisionOptions) -> DivisionSequence {
        let kfs = DivisionBuilder(p, o).keyframes()
        return DivisionSequence(process: p, options: o, stages: stages(p, o), keyframes: kfs,
                                counts: kfs.map { counts($0, process: p, pairs: o.pairs) })
    }

    /// Independent assortment's count (spec §4.6): "2ⁿ = 4 possible assortments; humans 2²³ ≈ 8.4 million".
    static func assortments(pairs n: Int) -> String {
        "2ⁿ = \(1 << n) possible assortments; humans 2²³ ≈ \(String(halfUp: "%.1f", Double(1 << 23) / 1_000_000)) million"
    }

    // MARK: The parts' cards (a label pressed)

    /// What a labelled part is (spec §4.6 Labels; §5.2's definitions), and the mix-up students make about it.
    static let parts: [String: DivPartInfo] = [
        "nucleus": DivPartInfo(title: "Nucleus", what: "Holds the chromosomes, inside the nuclear envelope. It goes as mitosis starts and two form again at telophase.", mixUp: nil),
        "envelope": DivPartInfo(title: "Nuclear envelope", what: "A double membrane round the nucleus. It breaks down in prometaphase (late prophase I in meiosis) and re-forms in telophase.", mixUp: nil),
        "nucleolus": DivPartInfo(title: "Nucleolus", what: "Where the nucleus makes ribosomal RNA. It disappears in prophase and returns in telophase.", mixUp: "It is not a chromosome and not a second nucleus."),
        "chromatin": DivPartInfo(title: "Chromosomes as chromatin", what: "In interphase each chromosome is a long, loose thread of DNA and protein (chromatin), too thin to see in a light microscope.", mixUp: "The chromosomes are there all the time: they only condense, and become visible, for division."),
        "replication": DivPartInfo(title: "DNA replication (S phase)", what: "Each chromosome's DNA is copied: one chromosome becomes two identical sister chromatids, joined at the centromere.", mixUp: "Replication happens in S phase, never in prophase, and it doubles the DNA, not the number of chromosomes."),
        "sisters": DivPartInfo(title: "Sister chromatids", what: "The two identical copies made in S phase, joined at the centromere until anaphase (mitosis) or anaphase II (meiosis).", mixUp: "Sister chromatids are copies of one chromosome. Homologous chromosomes are two different chromosomes, one from each parent."),
        "homologs": DivPartInfo(title: "Homologous chromosomes", what: "One from each parent, with the same genes in the same order (their alleles may differ). Every diploid cell has them, in mitosis too; they pair up only in meiosis I.", mixUp: "Homologs are not sister chromatids: they were never copies of each other."),
        "chromosome": DivPartInfo(title: "Chromosome", what: "One DNA molecule with its proteins, or after replication two sister chromatids joined at a centromere. Either way it counts as one chromosome.", mixUp: "Count chromosomes by counting centromeres. A replicated chromosome is still ONE chromosome."),
        "daughterChromosome": DivPartInfo(title: "A chromatid becomes a chromosome", what: "When sister chromatids separate, each has its own centromere, so each now counts as a chromosome: the count doubles while the DNA stays the same.", mixUp: nil),
        "centromere": DivPartInfo(title: "Centromere", what: "The narrow waist where sister chromatids are held together. A kinetochore forms on each side of it, where spindle microtubules attach.", mixUp: "One centromere means one chromosome."),
        "kinetochore": DivPartInfo(title: "Kinetochore microtubules", what: "Spindle fibres that attach to the kinetochores at each centromere and pull chromosomes, or chromatids, toward the poles.", mixUp: nil),
        "spindle": DivPartInfo(title: "Spindle", what: "Microtubules that move the chromosomes: kinetochore microtubules attach to them, polar microtubules overlap in the middle and push the poles apart.", mixUp: nil),
        "polar": DivPartInfo(title: "Polar microtubules", what: "Spindle fibres from the two poles that overlap in the middle; sliding past each other, they push the poles apart and lengthen the cell.", mixUp: nil),
        "centrosome": DivPartInfo(title: "Centrosome", what: "An animal cell's microtubule-organising centre, with a pair of centrioles at right angles. It duplicates before division, and the two move apart to become the spindle's poles.", mixUp: "Plant cells have no centrioles: their spindle forms without centrosomes."),
        "pole": DivPartInfo(title: "Spindle pole (plant)", what: "Plant cells have no centrosomes or centrioles: their spindle forms from broad microtubule-organising regions at each pole.", mixUp: nil),
        "plate": DivPartInfo(title: "Metaphase plate", what: "The plane midway between the poles where the chromosomes line up. It is a position, not a structure.", mixUp: "Mitosis lines up single chromosomes; meiosis I lines up homologous pairs."),
        "furrow": DivPartInfo(title: "Cleavage furrow", what: "In animal cells a ring of actin and myosin under the membrane tightens and pinches the cell in two.", mixUp: "Plant cells have a wall and cannot pinch: they build a cell plate instead."),
        "cellplate": DivPartInfo(title: "Cell plate", what: "In plant cells, Golgi vesicles carrying wall material gather in the middle and fuse from the middle outward into a new cell wall between the two cells.", mixUp: nil),
        "membrane": DivPartInfo(title: "Cell membrane", what: "The plasma membrane round the cell. In an animal cell the cleavage furrow pinches it in two.", mixUp: nil),
        "wall": DivPartInfo(title: "Cell wall", what: "A plant cell's rigid wall outside its membrane. It keeps the cell's shape, so a plant cell divides by building a new wall between the daughters.", mixUp: nil),
        "daughter": DivPartInfo(title: "Daughter cells", what: "Mitosis gives two cells with the same chromosomes as the parent and as each other: diploid (2n) and 2c, ready to start G1.", mixUp: nil),
        "tetrad": DivPartInfo(title: "Tetrad (bivalent)", what: "A pair of homologous chromosomes joined along their length in prophase I: 2 chromosomes, 4 chromatids.", mixUp: "A tetrad is 2 chromosomes, not 4: count its centromeres."),
        "chiasma": DivPartInfo(title: "Chiasma", what: "Where two non-sister chromatids of a pair crossed over and swapped the parts beyond it, in prophase I. It holds the homologs together until anaphase I.", mixUp: "Crossing over is between non-sister chromatids; sister chromatids are identical, so swapping between them would change nothing."),
        "recombinant": DivPartInfo(title: "Recombinant chromatid", what: "A chromatid that crossed over: beyond the chiasma it carries the other parent's DNA. It makes gametes genetically different.", mixUp: nil),
        "haploid": DivPartInfo(title: "Haploid cell (n)", what: "One complete set of chromosomes: one of each pair. After meiosis I each cell is haploid, although each chromosome still has two chromatids.", mixUp: "Ploidy counts sets, not chromatids."),
        "noS": DivPartInfo(title: "No S phase between meiosis I and II", what: "DNA is copied once only, before meiosis I. Meiosis II separates the sister chromatids made then.", mixUp: nil),
        "gamete": DivPartInfo(title: "Gametes (n)", what: "Meiosis gives four haploid cells (eggs or sperm in animals), 1c each, genetically different from each other.", mixUp: nil),
        "nondisjunction": DivPartInfo(title: "Nondisjunction", what: "Chromosomes fail to separate: a homologous pair in meiosis I, or one chromosome's sister chromatids in meiosis II. Gametes get one too many (n+1) or one too few (n−1).", mixUp: "An n+1 gamete with an extra chromosome 21 joining a normal gamete gives trisomy 21 (Down syndrome): 47 chromosomes."),
    ]

    // MARK: Compare (spec §4.6, §5.2.3)

    /// Mitosis's steps (0 G1 … 8 cytokinesis) beside meiosis's (0 G1 … 10 telophase II).
    static let compareSteps: [DivCompareStep] = [
        DivCompareStep(title: "Interphase", text: "Both copy their DNA once, in S phase, before they divide: 4 chromosomes, 8 chromatids, 4c.",
                       mitosis: 2, meiosis: 2, group: 1, rows: [1]),
        DivCompareStep(title: "Prophase · Prophase I", text: "Mitosis: each chromosome condenses on its own. Meiosis I: homologs pair as tetrads and cross over.",
                       mitosis: 3, meiosis: 3, group: 2, rows: [3, 4]),
        DivCompareStep(title: "Metaphase · Metaphase I", text: "Mitosis: single chromosomes line up. Meiosis I: homologous pairs line up.",
                       mitosis: 5, meiosis: 4, group: 3, rows: [5]),
        DivCompareStep(title: "Anaphase · Anaphase I", text: "Mitosis: sister chromatids separate. Meiosis I: homologs separate and sisters stay together.",
                       mitosis: 6, meiosis: 5, group: 4, rows: [6]),
        DivCompareStep(title: "Telophase, cytokinesis · Telophase I", text: "Mitosis ends with 2 identical diploid cells. Meiosis I ends with 2 haploid cells whose chromosomes still have 2 chromatids.",
                       mitosis: 8, meiosis: 6, group: 5, rows: [2]),
        DivCompareStep(title: "Prophase II", text: "Mitosis is over. Meiosis goes on to a second division, with no S phase first: a spindle forms in each cell.",
                       mitosis: 8, meiosis: 7, group: 6, rows: [1, 2]),
        DivCompareStep(title: "Metaphase II", text: "In meiosis II single chromosomes line up, as in mitosis's metaphase.",
                       mitosis: 8, meiosis: 8, group: 6, rows: [5]),
        DivCompareStep(title: "Anaphase II", text: "Now meiosis separates sister chromatids, as mitosis did in its anaphase.",
                       mitosis: 8, meiosis: 9, group: 6, rows: [6]),
        DivCompareStep(title: "Telophase II · result", text: "Mitosis: 2 diploid cells, identical to the parent. Meiosis: 4 haploid cells, genetically different.",
                       mitosis: 8, meiosis: 10, group: 6, rows: [0, 7, 8]),
    ]

    static let compareGroups: [Int: String] = [1: "Interphase", 2: "Prophase", 3: "Metaphase", 4: "Anaphase", 5: "Telophase", 6: "Meiosis II"]

    static var compareBioSteps: [BioStep] {
        compareSteps.enumerated().map { BioStep($0.offset, $0.element.title, $0.element.text, group: $0.element.group) }
    }

    // MARK: The cell cycle (spec §4.6)

    static let cyclePhases: [DivCyclePhase] = [
        DivCyclePhase(id: "g1", name: "G1", hours: 11, title: "G1: growth",
                      text: "The cell grows and makes proteins and organelles. Chromosomes are uncondensed threads: 46 chromosomes, 2c."),
        DivCyclePhase(id: "s", name: "S", hours: 8, title: "S: DNA synthesis",
                      text: "Every chromosome is copied into two sister chromatids. Still 46 chromosomes; now 92 chromatids; 2c becomes 4c."),
        DivCyclePhase(id: "g2", name: "G2", hours: 4, title: "G2: growth and preparation",
                      text: "More growth; the centrosome has duplicated; the copied DNA is checked. 46 chromosomes, 4c."),
        DivCyclePhase(id: "m", name: "M", hours: 1, title: "M: mitosis and cytokinesis",
                      text: "Prophase, prometaphase, metaphase, anaphase and telophase, then cytokinesis: two cells, each 46 chromosomes and 2c."),
    ]

    static let g0 = DivCyclePhase(id: "g0", name: "G0", hours: 0, title: "G0: resting",
                                  text: "A state outside the cycle, entered from G1. Many cells stay here for good (most neurons and muscle cells); others wait for a signal and return to G1.")

    /// The wheel: each phase's start and end, degrees clockwise from the top, M centred at the top and G1 after it.
    static var cycleArcs: [(phase: DivCyclePhase, start: Double, end: Double)] {
        let total = cyclePhases.reduce(0) { $0 + $1.hours }
        let m = cyclePhases[3]
        var start = -m.hours / total * 360 / 2 + m.hours / total * 360
        var out: [(phase: DivCyclePhase, start: Double, end: Double)] = []
        for p in cyclePhases.prefix(3) {
            let sweep = p.hours / total * 360
            out.append((p, start, start + sweep))
            start += sweep
        }
        out.append((m, start, start + m.hours / total * 360))
        return out
    }

    static let checkpoints: [DivCheckpoint] = [
        DivCheckpoint(id: "g1s", name: "G1/S checkpoint (restriction point)", short: "G1/S", angle: 172.5,
                      checks: ["Is the cell big enough?", "Are there enough nutrients?", "Are growth factors telling it to divide?", "Is its DNA undamaged?"],
                      role: "Passing it commits the cell to copying its DNA. If not, the cell waits, or leaves the cycle for G0.",
                      cyclin: "G1/S cyclins (such as cyclin E) rise late in G1; bound to their CDK they push the cell into S phase, then are destroyed.",
                      cancer: "Tumour suppressors such as p53 and Rb hold the cell here. When they are lost, and oncogenes such as a mutant Ras keep signalling, cells copy damaged DNA and divide without growth signals."),
        DivCheckpoint(id: "g2m", name: "G2/M checkpoint", short: "G2/M", angle: 352.5,
                      checks: ["Has all the DNA been copied?", "Was it copied correctly, with any damage repaired?"],
                      role: "Only a cell whose DNA is fully and correctly copied goes into mitosis.",
                      cyclin: "M cyclin (cyclin B) builds up through S and G2; with its CDK (together called MPF) it pushes the cell into mitosis.",
                      cancer: "Cells go into mitosis with damaged or unfinished DNA, and their mutations pile up."),
        DivCheckpoint(id: "spindle", name: "Spindle assembly checkpoint (in M)", short: "Spindle", angle: 0,
                      checks: ["Is every kinetochore attached to the spindle?"],
                      role: "Anaphase waits until every chromosome is attached and lined up, so each daughter gets one copy of each.",
                      cyclin: "Once every kinetochore is attached, cyclin B is destroyed and the sisters are released: anaphase begins.",
                      cancer: "Chromosomes are pulled apart before all are attached, so daughter cells get too many or too few (aneuploidy)."),
    ]

    /// CDKs and cyclins in a sentence (each gate's card ends with it).
    static let cyclinRule = "CDKs (cyclin-dependent kinases) are there all through the cycle but work only bound to a cyclin. Cyclin levels rise and fall each cycle: each cyclin–CDK complex pushes the cell past one point, then its cyclin is destroyed."

    /// The cyclins' levels (illustrative shapes, 0…1) at a time in a 24 h cycle that starts with G1: the G1/S cyclin
    /// peaks at the G1/S boundary (11 h); the M cyclin builds through S and G2, peaks in M and drops at anaphase.
    static func cyclinG1S(_ t: Double) -> Double { exp(-((t - 11) / 1.8) * ((t - 11) / 1.8)) }

    static func cyclinM(_ t: Double) -> Double {
        if t < 11 { return 0.04 }
        if t < 23.5 { return 0.04 + 0.96 * pow((t - 11) / 12.5, 1.4) }
        return 1 - 0.96 * DivMath.smoothstep(23.5, 24, t)
    }
}

// MARK: - Self-check

extension BioSelfCheck {
    /// Cell Division's own `bio-check` lines: the counts at every step against §5.2's tables, the keyframes against
    /// §5.2.4's rules, crossing over, independent assortment, nondisjunction, and the cell cycle's wheel.
    static func divideChecks(_ r: inout BioCheckReport) {
        let o = DivisionOptions()
        let mit = DivisionModel.sequence(.mitosis, o)
        let mei = DivisionModel.sequence(.meiosis, o)
        let t = DivisionModel.self

        // (§5.2.1, a step a line: the model's counts, the human's where the table has them)
        func cell(_ table: String, _ model: String, _ human: String) -> Bool {
            let plainTable = t.plain(table)
            if plainTable.range(of: #"^\d+ \(\d+\)$"#, options: .regularExpression) != nil { return plainTable == "\(model) (\(human))" }
            return plainTable == model
        }
        for (i, c) in mit.counts.enumerated() {
            let row = t.mitosisTable[i]
            let ok = cell(row[2], c.chromosomes, c.chromosomesHuman) && cell(row[3], c.chromatids, c.chromatidsHuman)
                && t.plain(row[4]) == c.ploidy && t.plain(row[5]) == c.dna && row[0].hasPrefix(String(mit.stages[i].title.prefix(1)))
            r.check("divide.mitosis.\(mit.stages[i].key.rawValue)", "\(c.chromosomes)|\(c.chromatids)|\(c.ploidy)|\(c.dna)", ok)
        }
        r.check("divide.mitosis.steps", "\(mit.stages.count)", mit.stages.count == t.mitosisTable.count - 1)

        // (§5.2.2: the interphase row is G1 → G2; the rest a step each; "(no S phase)": no new chromatids between
        // telophase I and prophase II)
        let mc = mei.counts
        let inter = t.meiosisTable[0]
        r.check("divide.meiosis.interphase", "\(mc[0].chromosomes)|\(mc[0].chromatids)→\(mc[2].chromatids)|\(mc[0].dna)→\(mc[2].dna)",
                mc[0].chromosomes == inter[2] && mc[2].chromosomes == inter[2] && "\(mc[0].chromatids) → \(mc[2].chromatids)" == inter[3]
                    && mc[0].ploidy == inter[4] && mc[2].ploidy == inter[4] && "\(mc[0].dna) → \(mc[2].dna)" == inter[5]
                    && mc[1].chromatids == "8" && mc[1].dna == "4c")
        let rows: [(step: Int, row: Int)] = [(3, 1), (4, 2), (5, 3), (6, 4), (7, 6), (8, 7), (9, 8), (10, 9)]
        for (step, row) in rows {
            let c = mc[step], x = t.meiosisTable[row]
            let ok = t.plain(x[2]) == c.chromosomes && t.plain(x[3]) == c.chromatids && t.plain(x[4]) == c.ploidy && t.plain(x[5]) == c.dna
            r.check("divide.meiosis.\(mei.stages[step].key.rawValue)", "\(c.chromosomes)|\(c.chromatids)|\(c.ploidy)|\(c.dna)", ok)
        }
        let telI = DivTally(mei.frames[6].chromatids.filter { $0.cell == 0 }, pairs: 2)
        let proII = DivTally(mei.frames[7].chromatids.filter { $0.cell == 0 }, pairs: 2)
        r.check("divide.meiosis.noS", "\(telI.chromatids)→\(proII.chromatids)", telI.chromatids == proII.chromatids && telI.units == proII.units)
        r.check("divide.meiosis.haploidReplicated", "\(telI.ploidy)/\(telI.chromatids / max(telI.chromosomes, 1))", telI.ploidy == "n" && telI.chromatids == 2 * telI.chromosomes)
        r.check("divide.meiosis.tetradsHuman", mc[3].chromosomesHuman, mc[3].chromosomesHuman == "46 (as 23 tetrads)")
        r.check("divide.homologs.mitosis", mit.counts[5].homologs, mit.counts[5].homologs.hasPrefix("Yes"))
        r.check("divide.homologs.afterMeiosisI", mc[6].homologs, mc[6].homologs == "No")
        r.check("divide.sisters", "\(mit.counts[0].sisters)|\(mit.counts[5].sisters)|\(mit.counts[6].sisters)|\(mc[5].sisters)|\(mc[9].sisters)",
                mit.counts[0].sisters == "Not copied yet" && mit.counts[5].sisters == "Yes" && mit.counts[6].sisters.hasPrefix("No")
                    && mc[5].sisters.hasPrefix("Yes") && mc[9].sisters.hasPrefix("No"))

        // (2n = 6: the same rules, three pairs)
        let six = DivisionOptions(pairs: 3)
        let mit6 = DivisionModel.sequence(.mitosis, six), mei6 = DivisionModel.sequence(.meiosis, six)
        r.check("divide.six", "\(mit6.counts[5].chromosomes)|\(mit6.counts[6].chromosomes)|\(mei6.counts[3].chromosomes)|\(mei6.counts[10].chromosomes)",
                mit6.counts[5].chromosomes == "6" && mit6.counts[6].chromosomes == "12" && mei6.counts[3].chromosomes == "6 (as 3 tetrads)"
                    && mei6.counts[10].chromosomes == "3 per cell" && mei6.counts[10].dna == "1c" && mit6.counts[0].chromosomesHuman == "46")

        // (§5.2.4's geometry)
        let g = DivGeometry.self
        r.near("divide.thread.long", g.threadLength(g.pairs[0], amplitude: g.wiggle), 0.9, 0.002, digits: 4)
        var straight = DivChromatidKey(id: DivChromatidID(pair: 1, homolog: 0, sister: 0))
        straight.k = 1
        r.near("divide.condensed.short", g.length(g.shape(straight, pair: g.pairs[1], chiasma: 0.5).points), 0.24, 1e-6, digits: 4)
        r.near("divide.condensed.long", g.span(g.pairs[0], 1), 0.42, 1e-9, digits: 4)
        r.near("divide.sisters.apart", 2 * g.sisterOffset(1), 0.055 + 0.012, 1e-9, digits: 4)
        let zeroExtents = Array(repeating: (back: 0.0, forward: 0.0), count: 4)
        let spec = DivisionBuilder.plate(zeroExtents, spacing: 0.22, room: 2).enumerated().allSatisfy { abs($0.element - (Double($0.offset) - 1.5) * 0.22) < 1e-12 }
        r.check("divide.plate.spacing", "0.22", spec)
        let meta = mit.frames[5]
        let onPlate = meta.chromatids.allSatisfy { abs($0.c.y) < 1e-12 && abs(sin($0.a2)) < 1e-12 }
        let clear = zip(meta.chromatids.map(\.c.x).sorted(), meta.chromatids.map(\.c.x).sorted().dropFirst()).allSatisfy { $1 - $0 < 1e-12 || $1 - $0 >= 0.22 - 1e-12 }
        r.check("divide.metaphase.plate", "y0,φ0", onPlate && clear && meta.poles[0].p == DivPoint(0, 0.82) && meta.poles[1].p == DivPoint(0, -0.82))
        let ana = mit.frames[6]
        r.check("divide.anaphase", "±0.62,V55", ana.chromatids.allSatisfy { abs(abs($0.c.y) - 0.62) < 1e-12 && abs(abs(DivMath.turn(from: $0.a1, to: $0.a2)) - DivMath.radians(110)) < 1e-9 })
        let tel = mit.frames[7]
        // (the furrow: the cell 0.2 wide where it crosses y = 0, the waist, and its outline drawn pinched there)
        let pinched = DivShape.pinched(1, 0.1)
        let narrowest = tel.cells.flatMap { c in (0..<DivCellKey.samples).filter { c.membrane[$0] > 0.5 }.map { c.point($0) } }
            .filter { abs($0.y) < 0.12 }.map { abs($0.x) }.min() ?? 1
        r.check("divide.telophase", String(format: "waist 0.2, drawn %.3f", narrowest),
                tel.nuclei[0].c == DivPoint(0, 0.62) && tel.nuclei[1].c == DivPoint(0, -0.62) && tel.nuclei.allSatisfy { $0.r == 0.32 }
                    && pinched.inside(DivPoint(0.0999, 0)) && !pinched.inside(DivPoint(0.1001, 0)) && narrowest < 0.25)
        let m1 = mei.frames[4]
        let homologYs = Set(m1.chromatids.map { ($0.c.y * 1000).rounded() / 1000 })
        r.check("divide.metaphaseI", homologYs.sorted().map { String($0) }.joined(separator: ","), homologYs == [0.06, -0.06])
        let t1 = mei.frames[6]
        r.near("divide.telophaseI.cells", t1.bounds.maxY, 0.9 + 0.72, 0.01, digits: 3)
        let m2 = mei.frames[8]
        r.check("divide.metaphaseII.poles", "±0.55", m2.poles.map(\.p) == [DivPoint(-0.55, 0.9), DivPoint(0.55, 0.9), DivPoint(-0.55, -0.9), DivPoint(0.55, -0.9)]
                    && m2.chromatids.allSatisfy { abs($0.c.x) < 1e-12 && abs(cos($0.a2)) < 1e-12 })
        let t2 = mei.frames[10]
        r.near("divide.telophaseII.cells", t2.bounds.maxX, 0.62 + 0.48, 0.01, digits: 3)
        let mixed = mit.frames[5].mixed(mit.frames[6], 0)
        r.check("divide.mix.ends", "0,1", mixed == mit.frames[5] && mit.frames[5].mixed(mit.frames[6], 1) == mit.frames[6])

        // (crossing over: the long pair's two non-sister chromatids, swapped beyond the chiasma; four different
        // gametes; without it, gametes in identical pairs)
        func signature(_ p: DivProduct) -> String {
            p.chromatids.map { "\($0.id.pair)\($0.id.homolog)\($0.recombined > 0.5 ? "r" : "")" }.joined(separator: ",")
        }
        let recombinants = mei.frames[5].chromatids.filter { $0.recombined > 0.5 }
        r.check("divide.crossing.recombinants", recombinants.map { "\($0.id.pair)\($0.id.homolog)\($0.id.sister)" }.joined(separator: ","),
                recombinants.count == 2 && Set(recombinants.map(\.id.homolog)) == [0, 1] && recombinants.allSatisfy { $0.id.pair == 0 })
        let crossed = Set(mei.products.map(signature))
        var noCross = o
        noCross.crossingOver = false
        let plainGametes = Set(DivisionModel.sequence(.meiosis, noCross).products.map(signature))
        r.check("divide.crossing.gametes", "\(crossed.count)/\(plainGametes.count)", crossed.count == 4 && plainGametes.count == 2)

        // (independent assortment: over the 2ⁿ⁻¹ ways to flip the other pairs, 2ⁿ different gametes)
        for n in [2, 3] {
            var kinds = Set<String>()
            for mask in 0..<(1 << (n - 1)) {
                var x = noCross
                x.pairs = n
                x.flipped = [false] + (1..<3).map { mask & (1 << ($0 - 1)) != 0 }
                for p in DivisionModel.sequence(.meiosis, x).products { kinds.insert(signature(p)) }
            }
            r.check("divide.assortment.\(2 * n)", "\(kinds.count)", kinds.count == 1 << n)
        }
        r.same("divide.assortment.text", DivisionModel.assortments(pairs: 2), "2ⁿ = 4 possible assortments; humans 2²³ ≈ 8.4 million")

        // (nondisjunction: n+1, n+1, n−1, n−1 after meiosis I; n+1, n−1, n, n after meiosis II)
        for (nd, want) in [(DivisionNondisjunction.meiosisI, ["n+1", "n+1", "n−1", "n−1"]), (.meiosisII, ["n+1", "n−1", "n", "n"])] {
            var x = o
            x.nondisjunction = nd
            let s = DivisionModel.sequence(.meiosis, x)
            let got = s.products.map(\.tally.ploidy)
            r.check("divide.nondisjunction.\(nd.rawValue)", got.joined(separator: ","), got == want && s.counts[10].listing != nil)
        }

        // (the cell cycle: 24 h, drawn in proportion)
        let arcs = DivisionModel.cycleArcs
        let sweeps = arcs.map { $0.end - $0.start }
        r.check("divide.cycle", sweeps.map { String(format: "%.1f", $0) }.joined(separator: ","),
                DivisionModel.cyclePhases.reduce(0) { $0 + $1.hours } == 24 && zip(sweeps, [165.0, 120, 60, 15]).allSatisfy { abs($0 - $1) < 1e-9 }
                    && abs(arcs[0].start - 7.5) < 1e-9 && DivisionModel.checkpoints.count == 3)
        // (every label, in every case, has a card; every step's labels fit (≤ 14) and point at something shown)
        var missing: Set<String> = []
        var hidden: [String] = []
        for p in DivisionProcess.allCases {
            for plant in [false, true] {
                for nd in DivisionNondisjunction.allCases {
                    let s = DivisionModel.sequence(p, DivisionOptions(plant: plant, nondisjunction: nd))
                    for (i, kf) in s.keyframes.enumerated() {
                        let shapes = kf.frame.shapes(chiasma: 0.5)
                        for l in kf.labels {
                            if DivisionModel.parts[l.part] == nil { missing.insert(l.part) }
                            if !DivPainter.anchor(l.anchor, kf.frame, shapes).visible { hidden.append("\(p.rawValue)\(i + 1):\(l.part)") }
                        }
                        if kf.labels.count > BioLabelLayout.maxLabels { hidden.append("\(p.rawValue)\(i + 1):too-many") }
                        if Set(kf.labels.map(\.id)).count != kf.labels.count { hidden.append("\(p.rawValue)\(i + 1):same-label") }
                    }
                }
            }
        }
        r.check("divide.labels", missing.isEmpty && hidden.isEmpty ? "all" : (missing.sorted() + hidden).joined(separator: ","), missing.isEmpty && hidden.isEmpty)
        // (§3.6: a step's title is at most 40 characters, its text one to three sentences)
        let titles = mit.stages.map(\.title) + mei.stages.map(\.title) + DivisionModel.compareSteps.map(\.title)
        let longest = titles.map(\.count).max() ?? 0
        r.check("divide.titles", "\(longest)", longest <= 40 && (mit.stages + mei.stages).allSatisfy { !$0.text.isEmpty && ($0.text.hasSuffix(".") || $0.text.hasSuffix("?")) })
        r.check("divide.compare", "\(DivisionModel.compareSteps.count)/\(DivisionModel.compareTable.count)",
                DivisionModel.compareTable.count == 9 && DivisionModel.compareSteps.allSatisfy { $0.mitosis < mit.stages.count && $0.meiosis < mei.stages.count })
    }
}
