import Foundation

// The Replication page's model (spec §4.2 Replication): a replication fork in 2D, drawn left to right, as nine steps
// on the shared step-through (§3.6) — the origin; helicase unwinding (topoisomerase ahead, single-strand binding
// proteins behind); primase's RNA primers; DNA polymerase III adding to the primer's 3′ end, reading the template 3′→5′
// and building 5′→3′; the leading strand made continuously toward the fork; the lagging strand made away from it in
// three Okazaki fragments, each from its own primer; DNA polymerase I replacing the primers with DNA; DNA ligase
// sealing the nicks; and two molecules, each one old strand and one new (semiconservative). Each step is a keyframe of
// numbers (where the fork is, how far each new strand has grown, which enzymes are there), so the drawing moves
// smoothly between steps and every claim about direction can be checked.
//
// Along the molecule x runs 0 (the origin, left) … 1 (right). The top parental strand runs 5′→3′ left to right (the
// lagging strand's template), the bottom one 3′→5′ (the leading strand's): the fork moves right.

/// An Okazaki fragment on the lagging strand: made from its primer at its 5′ end (`hi`, nearer the fork) back toward
/// the origin (`lo`).
struct DNAOkazaki: Equatable {
    let lo: Double
    let hi: Double
    /// How much of it is made, 0…1, from `hi` toward `lo` (the primer counts).
    var grown: Double = 0
    /// Its primer laid (0…1: shown).
    var primer: Double = 0

    /// Where its 3′ end has reached.
    var threePrime: Double { hi - (hi - lo) * grown }
}

/// The fork at one step: everything its drawing is made from.
struct DNAForkFrame: Equatable {
    /// How far the strands are open, from the origin (0 none … 1 all of it).
    var fork: Double = 0
    /// The two new molecules moved apart (the last step).
    var apart: Double = 0
    /// The leading strand's primer laid (0…1), and how far its new DNA reaches (its 3′ end, x).
    var leadingPrimer: Double = 0
    var leadingTo: Double = DNAReplication.leadingPrimerEnd
    var fragments: [DNAOkazaki] = DNAReplication.fragmentSpans.map { DNAOkazaki(lo: $0.lo, hi: $0.hi) }
    /// The RNA primers replaced by DNA (DNA polymerase I), 0…1.
    var primersReplaced: Double = 0
    /// The nicks sealed (DNA ligase), 0…1.
    var sealed: Double = 0
    // The enzymes, each 0 (gone) … 1 (there).
    var helicase: Double = 0
    var topoisomerase: Double = 0
    var ssb: Double = 0
    var primase: Double = 0
    var polIII: Double = 0
    var polI: Double = 0
    var ligase: Double = 0
    /// The close-up of the bases pairing as DNA polymerase III adds them (step 4).
    var closeUp: Double = 0

    /// The frame `t` of the way from `a` to `b` (0…1).
    static func mix(_ a: DNAForkFrame, _ b: DNAForkFrame, _ t: Double) -> DNAForkFrame {
        func m(_ x: Double, _ y: Double) -> Double { x + (y - x) * t }
        var f = a
        f.fork = m(a.fork, b.fork)
        f.apart = m(a.apart, b.apart)
        f.leadingPrimer = m(a.leadingPrimer, b.leadingPrimer)
        f.leadingTo = m(a.leadingTo, b.leadingTo)
        f.fragments = zip(a.fragments, b.fragments).map { DNAOkazaki(lo: $0.lo, hi: $0.hi, grown: m($0.grown, $1.grown), primer: m($0.primer, $1.primer)) }
        f.primersReplaced = m(a.primersReplaced, b.primersReplaced)
        f.sealed = m(a.sealed, b.sealed)
        f.helicase = m(a.helicase, b.helicase)
        f.topoisomerase = m(a.topoisomerase, b.topoisomerase)
        f.ssb = m(a.ssb, b.ssb)
        f.primase = m(a.primase, b.primase)
        f.polIII = m(a.polIII, b.polIII)
        f.polI = m(a.polI, b.polI)
        f.ligase = m(a.ligase, b.ligase)
        f.closeUp = m(a.closeUp, b.closeUp)
        return f
    }
}

enum DNAReplication {
    /// The leading strand's primer, at the origin: 5′ at 0, its 3′ end here.
    static let leadingPrimerEnd = 0.06
    /// An RNA primer's length along the drawing (about 10 nt).
    static let primerLength = 0.06
    /// The three Okazaki fragments' spans (each made from `hi` back to `lo`).
    static let fragmentSpans: [(lo: Double, hi: Double)] = [(0.02, 0.32), (0.32, 0.60), (0.60, 0.86)]
    /// Where the nicks are once the primers are replaced: at each primer's 3′ end.
    static let nicks: [Double] = [leadingPrimerEnd] + fragmentSpans.map { $0.hi - primerLength }

    /// The close-up's template (the bottom strand, written 3′→5′ under the new strand) and how many bases are made.
    static let closeUpNew = "ATGCGTACGT"
    static let closeUpMade = 6

    /// The nine steps: title, explanation (naming the enzyme at work), the enzymes, and what to notice.
    static let steps: [BioStep] = [
        BioStep(0, "Origin", "Replication begins at an origin, where the parental double helix will open. Its two strands (two shades) are antiparallel: the top runs 5′→3′, the bottom 3′→5′.",
                group: 1, notice: ["The parental strands pair A with T and G with C."]),
        BioStep(1, "Helicase unwinds the helix", "Helicase separates the strands by breaking the hydrogen bonds between base pairs, opening a replication fork. Topoisomerase relieves the twisting ahead of the fork; single-strand binding proteins hold the strands apart.",
                group: 2, notice: ["Helicase", "Topoisomerase", "Single-strand binding proteins"]),
        BioStep(2, "Primase lays RNA primers", "Primase makes a short RNA primer (about 10 nucleotides, orange) on each template. DNA polymerase cannot start a strand: it can only add to a 3′ end.",
                group: 3, notice: ["Primase"]),
        BioStep(3, "DNA polymerase III adds nucleotides", "DNA polymerase III adds nucleotides to the 3′ end of the primer, reading the template 3′→5′ and building the new strand 5′→3′. Each new nucleotide pairs with the template: A with T, G with C.",
                group: 4, notice: ["DNA polymerase III"]),
        BioStep(4, "Leading strand: continuous", "On the leading strand, DNA polymerase III builds continuously toward the fork, following helicase as it opens more of the helix.",
                group: 5, notice: ["DNA polymerase III", "Helicase"]),
        BioStep(5, "Lagging strand: Okazaki fragments", "The lagging strand's template runs the other way, so DNA polymerase III builds it away from the fork in short Okazaki fragments, each started from its own RNA primer laid by primase.",
                group: 6, notice: ["Primase", "DNA polymerase III"]),
        BioStep(6, "DNA polymerase I replaces primers", "DNA polymerase I removes each RNA primer and fills the gap with DNA, leaving only a nick in the backbone between fragments.",
                group: 7, notice: ["DNA polymerase I"]),
        BioStep(7, "DNA ligase seals the nicks", "DNA ligase seals each nick by making the missing phosphodiester bond, joining the fragments into one continuous strand.",
                group: 8, notice: ["DNA ligase"]),
        BioStep(8, "Two semiconservative copies", "Two DNA molecules result, each with one old (parental) strand and one new strand: replication is semiconservative.",
                group: 9, notice: ["Each molecule: one old strand, one new"]),
    ]

    /// The enzymes acting at each step (the step's card and the list beside the drawing).
    static let enzymes: [[String]] = [
        [], ["Helicase", "Topoisomerase", "Single-strand binding proteins"], ["Primase"], ["DNA polymerase III"],
        ["DNA polymerase III", "Helicase"], ["Primase", "DNA polymerase III"], ["DNA polymerase I"], ["DNA ligase"], [],
    ]

    /// What each enzyme does, for the list beside the drawing.
    static let enzymeRoles: [(name: String, role: String)] = [
        ("Helicase", "Unwinds the helix, breaking the H-bonds between bases"),
        ("Topoisomerase", "Relieves the twisting ahead of the fork"),
        ("Single-strand binding proteins", "Hold the separated strands apart"),
        ("Primase", "Lays short RNA primers"),
        ("DNA polymerase III", "Adds nucleotides to a 3′ end, building 5′→3′"),
        ("DNA polymerase I", "Replaces RNA primers with DNA"),
        ("DNA ligase", "Seals nicks with phosphodiester bonds"),
    ]

    /// Each step's keyframe.
    static let frames: [DNAForkFrame] = {
        var f = DNAForkFrame()
        var out = [f]
        // 2. Helicase opens a fork; topoisomerase ahead; SSBs on the single strands.
        f.fork = 0.40; f.helicase = 1; f.topoisomerase = 1; f.ssb = 1
        out.append(f)
        // 3. Primase: a primer on each template (the leading strand's at the origin, the first fragment's by the fork).
        f.primase = 1; f.leadingPrimer = 1; f.fragments[0].primer = 1; f.fragments[0].grown = primerLength / (fragmentSpans[0].hi - fragmentSpans[0].lo)
        out.append(f)
        // 4. DNA polymerase III extends both primers' 3′ ends; the close-up shows the bases pairing.
        f.primase = 0; f.polIII = 1; f.leadingTo = 0.30; f.fragments[0].grown = 0.45; f.closeUp = 1
        out.append(f)
        // 5. Leading strand: continuous, toward the fork (which has moved on).
        f.closeUp = 0; f.fork = 0.70; f.leadingTo = 0.66; f.fragments[0].grown = 1
        out.append(f)
        // 6. Lagging strand: two more fragments, each from its own primer, made away from the fork.
        f.fork = 0.92; f.topoisomerase = 0; f.primase = 1; f.leadingTo = 0.88
        f.fragments[1].primer = 1; f.fragments[1].grown = 1
        f.fragments[2].primer = 1; f.fragments[2].grown = 0.55
        out.append(f)
        // 7. DNA polymerase I replaces the primers with DNA; nicks remain.
        f.primase = 0; f.polIII = 0; f.helicase = 0; f.topoisomerase = 0; f.ssb = 0; f.fragments[2].grown = 1
        f.polI = 1; f.primersReplaced = 1
        out.append(f)
        // 8. DNA ligase seals the nicks.
        f.polI = 0; f.ligase = 1; f.sealed = 1
        out.append(f)
        // 9. Two molecules, each one old strand and one new (the copying carried on to the end).
        f.ligase = 0; f.fork = 1; f.leadingTo = 1; f.apart = 1
        out.append(f)
        return out
    }()

    /// The frame drawn at a phase of the step-through (between two steps, or both for a crossfade).
    static func frame(_ phase: BioStepPhase) -> DNAForkFrame {
        let a = frames[min(max(phase.from, 0), frames.count - 1)], b = frames[min(max(phase.to, 0), frames.count - 1)]
        return phase.crossfade ? (phase.t < 0.5 ? a : b) : DNAForkFrame.mix(a, b, phase.t)
    }

    // MARK: Checks

    static func checks(_ r: inout BioCheckReport) {
        let titles = steps.map(\.title)
        r.check("dna.repl.steps", "\(steps.count)", steps.count == 9 && frames.count == 9 && enzymes.count == 9
                && titles.allSatisfy { $0.count <= 40 } && Set(titles).count == 9)
        // (each step from 2 to 8 names its enzyme in its words)
        let named = (1...7).allSatisfy { i in !enzymes[i].isEmpty && enzymes[i].allSatisfy { steps[i].text.localizedCaseInsensitiveContains($0) } }
        r.check("dna.repl.enzymes", "\(enzymes.flatMap { $0 }.count)", named)
        // (the fork only moves on; the leading strand never passes it and only grows, toward it)
        let forkOK = zip(frames, frames.dropFirst()).allSatisfy { $1.fork >= $0.fork && $1.leadingTo >= $0.leadingTo } && frames.allSatisfy { $0.leadingTo <= $0.fork + 1e-9 || $0.leadingTo == leadingPrimerEnd }
        r.check("dna.repl.leading", String(format: "%.2f", frames[4].leadingTo), forkOK)
        // (three Okazaki fragments, each begun at its 5′ end — the end nearer the fork — and made back toward the origin,
        // never past the fork; by the end every one is made)
        let lagging = frames.allSatisfy { f in f.fragments.allSatisfy { $0.threePrime <= $0.hi && ($0.grown == 0 || $0.hi <= f.fork + 0.02) } }
        let lastFrags = frames[5].fragments.filter { $0.primer > 0 }.count
        r.check("dna.repl.Okazaki", "\(fragmentSpans.count),\(lastFrags)", fragmentSpans.count == 3 && lagging && lastFrags == 3
                && frames.last?.fragments.allSatisfy { $0.grown == 1 } == true)
        // (semiconservative at the end: primers gone, nicks sealed, the two molecules apart)
        let end = frames[8]
        r.check("dna.repl.end", "replaced\(end.primersReplaced),sealed\(end.sealed),apart\(end.apart)",
                end.primersReplaced == 1 && end.sealed == 1 && end.apart == 1 && end.fork == 1
                && frames[6].sealed == 0 && frames[6].primersReplaced == 1)
        let mid = DNAForkFrame.mix(frames[3], frames[4], 0.5)
        r.check("dna.repl.mix", String(format: "%.3f", mid.fork), abs(mid.fork - 0.55) < 1e-12
                && DNAForkFrame.mix(frames[3], frames[4], 0) == frames[3] && DNAForkFrame.mix(frames[3], frames[4], 1) == frames[4])
        // (the close-up: the new strand matches the template's partners, built 5′→3′)
        let template = BioSeq.complement(closeUpNew)
        r.check("dna.repl.close-up", "\(closeUpNew.prefix(closeUpMade))/\(template)", BioSeq.complement(template) == closeUpNew && closeUpMade < closeUpNew.count)
    }
}
