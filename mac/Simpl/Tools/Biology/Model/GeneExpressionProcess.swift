import Foundation

// Gene Expression's step-through (spec §4.3 Transcribe & translate, §3.6): a gene made into a protein — the gene;
// transcription's initiation, a step for each nucleotide and termination; in a eukaryote the 5′ cap, the poly-A tail,
// splicing and export; translation's initiation, a step for each codon and termination; and the result. Each step has
// its title, its explanation (naming the molecule at work) and the scene at its end (a GXFrame). Between two steps the
// view draws `GXScene.make`: the two frames mixed, each value in its own part of the move — a tRNA comes in, the peptide
// bond forms, then the ribosome moves on — so the order of events reads in the motion. Foundation only (checked by the
// Models job with the rest of Biology/Model).

/// What a step is.
enum GXStage: Int, Comparable {
    case gene, txInit, txElong, txEnd, cap, tail, splice, export, tlInit, tlElong, tlEnd, noStart, result

    static func < (a: GXStage, b: GXStage) -> Bool { a.rawValue < b.rawValue }

    /// The stage group the number keys jump to: 1 the gene, 2 transcription, 3 RNA processing, 4 translation, 5 the result.
    var group: Int {
        switch self {
        case .gene: return 1
        case .txInit, .txElong, .txEnd: return 2
        case .cap, .tail, .splice, .export: return 3
        case .tlInit, .tlElong, .tlEnd, .noStart: return 4
        case .result: return 5
        }
    }

    var isTranscription: Bool { self == .txInit || self == .txElong || self == .txEnd }
    var isTranslation: Bool { self >= .tlInit && self <= .noStart }
}

/// The scene at the end of a step: every value the drawing needs, as numbers that mix between two steps.
struct GXFrame: Equatable {
    var stage: GXStage = .gene
    /// 1: the whole gene in view; 0: bases at full size.
    var fit: Double = 0
    /// The column at the view's centre.
    var focus: Double = 0
    /// Transcription factors on the TATA box (in a prokaryote, the σ factor with the polymerase).
    var factors: Double = 0
    /// RNA polymerase on the DNA (0…1), and the column of the last nucleotide it added (txStart − 1 before the first).
    var polymerase: Double = 0
    var polX: Double = 0
    /// The DNA opened into a bubble round the polymerase.
    var bubble: Double = 0
    /// Nucleotides of RNA made.
    var made: Double = 0
    /// The RNA let go of the template (its 3′ end no longer paired in the bubble).
    var released: Double = 0
    /// A prokaryote's terminator hairpin.
    var hairpin: Double = 0
    var cap: Double = 0
    var tail: Double = 0
    var splice: Double = 0
    /// The mRNA moved down from the nucleus's lane to the ribosome's.
    var export: Double = 0
    /// The small subunit, and its centre as a mature-mRNA index (it scans from the cap to the AUG).
    var small: Double = 0
    var smallAt: Double = 0
    /// The large subunit joined.
    var large: Double = 0
    /// The initiator tRNA in the P site.
    var initiator: Double = 0
    /// The codon in the P site (0 = the start codon).
    var pSite: Double = 0
    /// Amino acids in the chain.
    var chain: Int = 0
    /// A release factor in the A site.
    var releaseFactor: Double = 0
    /// The polypeptide let go and the ribosome's subunits apart.
    var freed: Double = 0
    /// The result laid out.
    var result: Double = 0

    /// Two frames mixed `t` of the way from `a` to the later `b`, each value in its own part of the move (where `b` is
    /// going decides which): at termination the RNA lets go before the polymerase leaves; at translation's start the
    /// small subunit binds and scans, then the initiator tRNA comes, then the large subunit joins.
    static func mix(_ a: GXFrame, _ b: GXFrame, _ t: Double) -> GXFrame {
        func w(_ t0: Double, _ t1: Double) -> Double { min(max((t - t0) / (t1 - t0), 0), 1) }
        var pFit = t, pFocus = t, pRelease = t, pPol = t, pBubble = t, pExport = t, pSmall = t, pSmallAt = t
        var pInit = t, pLarge = t, pRF = t, pFreed = t, pHairpin = t
        switch b.stage {
        case .txEnd:
            pRelease = w(0, 0.5)
            pHairpin = w(0, 0.5)
            pPol = w(0.3, 1)
            pBubble = w(0.3, 1)
        case .tlInit:
            pExport = w(0, 0.25)
            pSmall = w(0.2, 0.3)
            pSmallAt = w(0.3, 0.6)
            pInit = w(0.6, 0.8)
            pLarge = w(0.8, 1)
            pFit = w(0, 0.4)
            pFocus = w(0, 0.6)
        case .tlEnd:
            pRF = w(0, 0.35)
            pFreed = w(0.65, 1)
        default:
            break
        }
        func m(_ x: Double, _ y: Double, _ p: Double) -> Double { x + (y - x) * p }
        var f = GXFrame()
        f.stage = t < 0.5 ? a.stage : b.stage
        f.fit = m(a.fit, b.fit, pFit)
        f.focus = m(a.focus, b.focus, pFocus)
        f.factors = m(a.factors, b.factors, pPol)
        f.polymerase = m(a.polymerase, b.polymerase, pPol)
        f.polX = m(a.polX, b.polX, pPol)
        f.bubble = m(a.bubble, b.bubble, pBubble)
        f.made = m(a.made, b.made, t)
        f.released = m(a.released, b.released, pRelease)
        f.hairpin = m(a.hairpin, b.hairpin, pHairpin)
        f.cap = m(a.cap, b.cap, t)
        f.tail = m(a.tail, b.tail, t)
        f.splice = m(a.splice, b.splice, t)
        f.export = m(a.export, b.export, pExport)
        f.small = m(a.small, b.small, pSmall)
        f.smallAt = m(a.smallAt, b.smallAt, pSmallAt)
        f.large = m(a.large, b.large, pLarge)
        f.initiator = m(a.initiator, b.initiator, pInit)
        f.pSite = m(a.pSite, b.pSite, t)
        f.chain = t < 0.5 ? a.chain : b.chain
        f.releaseFactor = m(a.releaseFactor, b.releaseFactor, pRF)
        f.freed = m(a.freed, b.freed, pFreed)
        f.result = m(a.result, b.result, t)
        return f
    }
}

/// A gene's whole step-through: its steps (for BioStepController) and each step's frame.
struct GXProcess {
    let gene: GXGene
    let steps: [BioStep]
    let frames: [GXFrame]

    /// A poly-A tail is 50–250 A long; this many are drawn, then "…".
    static let tailDrawn = 20
    /// The step-through takes this many nucleotides at most (§3.7), and says so.
    static let cap = 600
    /// In a bacterium, a ribosome is sketched on the RNA once this many nucleotides past the start codon's A are made
    /// (the start codon then clear of the polymerase).
    static let couplingLag = 13
    /// The number keys' groups.
    static let groups: [Int: String] = [1: "The gene", 2: "Transcription", 3: "RNA processing", 4: "Translation", 5: "Result"]

    init(gene g: GXGene) {
        gene = g
        var steps: [BioStep] = []
        var frames: [GXFrame] = []
        func add(_ title: String, _ text: String, _ f: GXFrame, _ notice: [String] = []) {
            steps.append(BioStep(steps.count, title, text, group: f.stage.group, notice: notice))
            frames.append(f)
        }
        let euk = g.cell == .eukaryote
        let pol = euk ? "RNA polymerase II" : "RNA polymerase"
        let n = g.preMRNA.count
        let span = g.columnSpan
        let tata = g.marks.first { $0.kind == .tata }
        let sameAsCoding = "Only the template strand is read; the mRNA will match the coding strand, with U for T."

        // 1. The gene
        var f = GXFrame()
        f.stage = .gene
        f.fit = 1
        f.focus = (span.lowerBound + span.upperBound) / 2
        f.polX = Double(g.txStart - 1)
        f.smallAt = euk ? -2 : Double((g.cdsStart ?? 0) - 7)
        let geneText: String
        switch g.source {
        case .teaching:
            geneText = euk
                ? "A made-up teaching gene: a promoter with its TATA box, the start site (+1), two exons, one intron and a poly-A signal. \(sameAsCoding)"
                : "A made-up teaching gene as a bacterium would have it: a promoter, the start site (+1) and one coding sequence, with no intron. \(sameAsCoding)"
        case .hbb:
            geneText = "Human β-globin's coding sequence, \(n) nt: the real gene's 3 exons joined (its promoter and 2 introns are not shown). \(sameAsCoding)"
        case .own:
            geneText = "Your sequence, \(n) nt, read as the coding strand and transcribed whole from +1 (no promoter shown). \(sameAsCoding)"
        }
        add("The gene", geneText, f, ["regions"])

        // 2. Initiation of transcription
        f.stage = .txInit
        f.fit = 0
        f.factors = 1
        f.polymerase = 1
        f.bubble = 1
        f.focus = tata.map { Double($0.range.lowerBound + g.txStart) / 2 + 2 } ?? Double(g.txStart + 6)
        let bubble = "The DNA opens into a bubble about 12–14 bp wide at the start site (+1)."
        let initText: String
        if euk {
            initText = tata != nil
                ? "Transcription factors bind the TATA box, and RNA polymerase II binds the promoter. \(bubble)"
                : "Transcription factors and RNA polymerase II bind the promoter (not shown here). \(bubble)"
        } else {
            initText = "RNA polymerase, led by its σ factor, binds the promoter (its −35 and −10 boxes\(g.source == .teaching ? "" : ", not shown")). \(bubble)"
        }
        add("Initiation", initText, f, tata != nil ? ["tata", "plus1"] : ["plus1"])

        // 3. Elongation, a nucleotide a step
        let startPre = g.cdsStart.map { g.matureToPre[$0] }
        let stopPre: Int? = g.hasStop ? g.cdsStart.flatMap { s in g.codons.last.map { g.matureToPre[s + $0.index * 3] } } : nil
        let polyA = g.marks.first { $0.kind == .polyA }.map { $0.range.lowerBound - g.txStart }
        let intronStarts = Set(g.introns.map { $0.lowerBound - g.txStart })
        let coupled = !euk ? startPre.map { $0 + GXProcess.couplingLag } : nil
        for k in 1...max(n, 1) where n > 0 {
            let i = k - 1
            let col = g.txStart + i
            let tb = g.template[col]
            let rb = g.preMRNA[i]
            let reads = "\(pol) reads the template's \(tb) and adds \(rb)"
            var text: String
            var notice: [String] = []
            if k == 1 {
                text = "\(pol) starts the RNA at +1: it reads the template's \(tb) and lays down \(rb), the RNA's 5′ end. Each new nucleotide joins the RNA's 3′ end, so the RNA grows 5′→3′."
                notice = ["plus1"]
            } else if intronStarts.contains(i) {
                text = "\(reads): the intron begins (GU…). It is copied into the pre-mRNA too, and cut out later."
                notice = ["intron"]
            } else if i == startPre {
                text = "\(reads), the first base of the start codon (AUG). Nothing is translated yet: this is still transcription."
                notice = ["start"]
            } else if i == stopPre {
                text = "\(reads), the first base of the stop codon. RNA polymerase does not stop here: only ribosomes read codons."
                notice = ["stop"]
            } else if i == polyA {
                text = "\(reads): AAUAAA, the poly-A signal, is being copied. It marks where the RNA will be cut."
                notice = ["polyA"]
            } else if k == coupled {
                text = "\(reads). In bacteria a ribosome can already bind this RNA and start translating while transcription goes on."
                notice = ["coupled"]
            } else {
                switch k % 3 {
                case 0: text = "\(reads) opposite it, at the RNA's 3′ end. It moves along the template 3′→5′, so the RNA grows 5′→3′."
                case 1: text = "A free nucleotide, \(rb)TP, pairs with the template's \(tb), and \(pol) joins it to the RNA's 3′ end. Behind the polymerase the DNA closes and the RNA peels away."
                default: text = "\(reads). The new RNA matches the coding strand above (\(g.coding[col]) there), with U for T."
                }
            }
            f.stage = .txElong
            f.made = Double(k)
            f.polX = Double(col)
            f.focus = Double(col)
            add("Elongation · nucleotide \(k) of \(n)", text, f, notice)
        }

        // 4. Termination of transcription
        f.stage = .txEnd
        f.released = 1
        f.polymerase = 0
        f.bubble = 0
        f.factors = 0
        f.polX += 5
        f.hairpin = euk ? 0 : 1
        f.focus = Double(g.txEnd) - 6
        let termText: String
        if !euk {
            termText = "The RNA folds into a terminator hairpin (drawn as a sketch), which makes RNA polymerase let go. The RNA and the DNA separate, and the DNA closes."
        } else if polyA != nil {
            termText = "Past the AAUAAA poly-A signal the RNA is cut (in real genes 10–35 nt past it) and let go. RNA polymerase II falls off and the DNA closes. This pre-mRNA still has its intron."
        } else {
            termText = "The RNA is cut past a poly-A signal (after the part shown) and let go. RNA polymerase II falls off and the DNA closes."
        }
        add("Termination", termText, f, euk ? ["polyA"] : [])

        // 5–8. RNA processing (eukaryote)
        if euk {
            f.stage = .cap
            f.cap = 1
            f.focus = Double(g.txStart) + 8
            add("5′ cap", "A 7-methylguanosine (m⁷G) cap is added to the RNA's 5′ end, joined 5′ to 5′. It protects the mRNA, and it is where the ribosome's small subunit will bind.", f, ["cap"])

            f.stage = .tail
            f.tail = 1
            f.focus = g.column(pre: n - 1) + 6
            add("Poly-A tail", "Poly-A polymerase adds 50–250 A's to the 3′ end, with no DNA template (about 200; \(GXProcess.tailDrawn) are drawn). The tail protects the mRNA and helps it leave the nucleus.", f, ["tail"])

            f.stage = .splice
            f.splice = 1
            let spliceText: String
            if let j = g.junctions.first {
                f.focus = g.column(mature: Double(j))
                if let s = g.splitCodon {
                    let cut = String(s.codon.prefix(s.cut)) + " | " + String(s.codon.dropFirst(s.cut))
                    spliceText = "The spliceosome cuts out the intron (it begins GU and ends AG) and joins exon 1 to exon 2. Codon \(s.index + 1), \(s.codon), is split across the junction (\(cut)), so splicing must be exact to the base."
                } else {
                    spliceText = "The spliceosome cuts out the intron (it begins GU and ends AG) and joins the exons on either side, exact to the base."
                }
            } else {
                f.fit = 1
                f.focus = g.column(mature: Double(g.mature.count) / 2)
                spliceText = g.source == .hbb
                    ? "The real HBB gene has 3 exons and 2 introns, and the spliceosome cuts the introns out at this step. This tool shows only the coding sequence, so here there is nothing to cut."
                    : "Your sequence has no intron marked, so the spliceosome has nothing to cut. Most human genes have several introns removed at this step."
            }
            add("Splicing", spliceText, f, ["gu-ag", "junction"])

            f.stage = .export
            f.export = 1
            f.fit = 1
            f.focus = g.column(mature: Double(g.mature.count) / 2)
            add("Export", "The mature mRNA (cap, exons and tail) leaves the nucleus through a nuclear pore. In the cytoplasm, ribosomes will read it.", f)
        }

        // 9–11. Translation
        let protein = g.protein
        guard let s = g.cdsStart, !protein.isEmpty else {
            f.stage = .noStart
            f.export = 1
            f.fit = 1
            f.focus = g.column(mature: Double(g.mature.count) / 2)
            add("No start codon", "This mRNA has no AUG, so a ribosome has nowhere to start and no protein is made. Try a sequence with ATG (DNA) or AUG (mRNA) in it.", f)
            f.stage = .result
            f.result = 1
            add("Result", "No protein: without a start codon there is no reading frame to translate.", f)
            self.steps = steps
            self.frames = frames
            return
        }
        f.stage = .tlInit
        f.export = 1
        f.fit = 0
        f.small = 1
        f.smallAt = Double(s + 1)
        f.large = 1
        f.initiator = 1
        f.pSite = 0
        f.chain = 1
        f.focus = g.codonColumn(0) + 1.5
        let initTl: String
        if euk {
            let kozak = g.kozak != nil ? " (in a Kozak context, GCCACCAUGG)" : ""
            initTl = "The small subunit binds the 5′ cap and scans to the first AUG\(kozak). The initiator tRNA (anticodon 3′-UAC-5′, carrying Met) pairs with AUG in the P site, then the large subunit joins."
        } else {
            initTl = "The small subunit binds the Shine–Dalgarno sequence just upstream of the AUG (bacteria have no cap). The initiator tRNA (anticodon 3′-UAC-5′, carrying formyl-Met) pairs in the P site, then the large subunit joins."
        }
        add("Translation initiation", initTl, f, euk ? (g.kozak != nil ? ["kozak"] : ["start"]) : ["sd"])

        let sense = g.codons.filter { $0.aa != "*" }
        for j in 1..<max(sense.count, 1) where sense.count > 1 {
            let c = sense[j]
            let aa = BioAminoAcids.byOne[c.aa]
            let three = aa?.three ?? String(c.aa)
            let ac = BioCode.anticodon3to5(c.codon)
            var text: String
            if j == 1 {
                text = "A tRNA with anticodon 3′-\(ac)-5′ brings \(three) to codon \(c.codon) in the A site. A peptide bond joins it to Met (the large subunit's rRNA catalyses it); then the ribosome moves one codon toward 3′ and the empty tRNA moves to the E site."
            } else if c.aa == "M" {
                text = "Codon AUG in the A site: inside a gene, AUG simply codes Met. Its tRNA (anticodon 3′-UAC-5′) brings it, a peptide bond joins it to the chain, and the ribosome moves on one codon."
            } else {
                text = "Codon \(c.codon) in the A site: the tRNA with anticodon 3′-\(ac)-5′ brings \(three). A peptide bond adds it to the chain's C-terminal end, and the ribosome moves on one codon (3 nt) toward 3′."
            }
            f.stage = .tlElong
            f.pSite = Double(j)
            f.chain = j + 1
            f.focus = g.codonColumn(Double(j)) + 1.5
            add("Elongation · codon \(j + 1): \(c.codon) → \(three)", text, f)
        }

        f.stage = .tlEnd
        if g.hasStop, let stop = g.codons.last {
            let name = BioCode.stopNames[stop.codon].map { " (“\($0)”)" } ?? ""
            f.releaseFactor = 1
            f.freed = 1
            f.focus = g.codonColumn(Double(stop.index)) - 1
            add("Termination", "Stop codon \(stop.codon)\(name) reaches the A site. A release factor, a protein and not a tRNA, binds it; the polypeptide is let go and the ribosome's subunits separate.", f, ["stop"])
        } else {
            add("No stop codon", "The mRNA ends before any stop codon, so no release factor comes and the ribosome stalls at the end. Cells destroy such mRNAs; the chain is shown as far as it got.", f)
        }

        // 12. The result
        f.stage = .result
        f.result = 1
        f.releaseFactor = 0
        if g.hasStop {
            f.small = 0
            f.large = 0
        }
        f.focus = g.codonColumn(Double(sense.count - 1) / 2)
        let shown = protein.count > 30 ? String(protein.prefix(30)) + "…" : protein
        let met = euk ? "AUG started it and also coded its first Met (many proteins later lose it)." : "AUG started it and coded its first amino acid (formyl-Met in bacteria)."
        add("Result", "The protein, N- to C-terminus: \(shown) (\(protein.count) amino acid\(protein.count == 1 ? "" : "s")). \(met)", f)

        self.steps = steps
        self.frames = frames
    }

    /// The steps of a stage, if it has any (the elongation runs are what Run and Skip work on).
    func steps(of stage: GXStage) -> ClosedRange<Int>? {
        guard let a = frames.firstIndex(where: { $0.stage == stage }), let b = frames.lastIndex(where: { $0.stage == stage }) else { return nil }
        return a...b
    }

    /// The protein made so far at a step (N→C).
    func chain(at i: Int) -> String {
        guard frames.indices.contains(i) else { return "" }
        let f = frames[i]
        return String(gene.protein.prefix(f.chain))
    }

    /// The RNA made so far at a step (5′→3′).
    func rna(at i: Int) -> String {
        guard frames.indices.contains(i) else { return "" }
        return String(gene.preMRNA.prefix(Int(frames[i].made)))
    }
}

// MARK: - The scene between two steps

/// What the view draws at a moment: a frame (two steps' frames mixed), and the translation's pieces worked out from it
/// — the tRNAs in their sites, the chain's amino acids, the release factor — and the nucleotide on its way in.
struct GXScene: Equatable {
    struct TRNA: Equatable {
        /// The codon it pairs with (its anticodon under it).
        let codon: Int
        /// 0 docked on its codon … 1 far above (coming in or leaving).
        var lift: Double
        var alpha: Double
        /// Carries its amino acid (or the chain) on top.
        var charged: Bool
        var initiator: Bool
    }

    struct Residue: Equatable {
        /// 0 = the N-terminal Met.
        let index: Int
        let aa: Character
        /// The codon it is drawn over (it came from that codon).
        var x: Double
        /// 0 in the chain's row; 1 on top of its tRNA.
        var onTRNA: Double
        var alpha: Double
        /// Joined to the one before it by a peptide bond (drawn as a line), 0…1.
        var bonded: Double
    }

    var frame: GXFrame
    /// The P site's codon (fractional while the ribosome moves).
    var ribosome: Double
    var trnas: [TRNA]
    var residues: [Residue]
    /// The release factor's way in: 1 away … 0 in the A site; nil when there is none.
    var releaseLift: Double?
    /// The nucleotide on its way in (its pre-mRNA index), and how far it has come.
    var incoming: Int?
    var incomingProgress: Double = 0

    /// The scene at rest at a step.
    static func rest(_ p: GXProcess, _ i: Int) -> GXScene {
        let f = p.frames[max(0, min(i, p.frames.count - 1))]
        let protein = Array(p.gene.protein)
        let at = Int(f.pSite.rounded())
        var trnas: [TRNA] = []
        var residues: [Residue] = []
        var rf: Double?
        func row(_ upTo: Int, onTRNA last: Bool) {
            for k in 0..<min(upTo, protein.count) {
                residues.append(Residue(index: k, aa: protein[k], x: Double(k), onTRNA: last && k == upTo - 1 ? 1 : 0, alpha: 1, bonded: k > 0 ? 1 : 0))
            }
        }
        switch f.stage {
        case .tlInit:
            trnas = [TRNA(codon: 0, lift: 0, alpha: 1, charged: true, initiator: true)]
            row(1, onTRNA: true)
        case .tlElong:
            trnas = [TRNA(codon: at, lift: 0, alpha: 1, charged: true, initiator: false)]
            if at >= 1 { trnas.append(TRNA(codon: at - 1, lift: 0.15, alpha: 0.55, charged: false, initiator: at == 1)) }
            row(at + 1, onTRNA: true)
        case .tlEnd:
            if p.gene.hasStop {
                rf = 0
                row(protein.count, onTRNA: false)
            } else {
                trnas = [TRNA(codon: at, lift: 0, alpha: 1, charged: true, initiator: at == 0)]
                if at >= 1 { trnas.append(TRNA(codon: at - 1, lift: 0.15, alpha: 0.55, charged: false, initiator: at == 1)) }
                row(at + 1, onTRNA: true)
            }
        case .result:
            row(protein.count, onTRNA: false)
        default:
            break
        }
        return GXScene(frame: f, ribosome: f.pSite, trnas: trnas, residues: residues, releaseLift: rf, incoming: nil)
    }

    /// The scene `t` of the way between two neighbouring steps (either way: back plays the move in reverse).
    static func make(_ p: GXProcess, from a: Int, to b: Int, t: Double) -> GXScene {
        let lo = max(0, min(min(a, b), p.frames.count - 1)), hi = max(0, min(max(a, b), p.frames.count - 1))
        let u = min(max(a <= b ? t : 1 - t, 0), 1)
        if lo == hi || u >= 1 { return rest(p, hi) }
        if u <= 0 { return rest(p, lo) }
        let fa = p.frames[lo], fb = p.frames[hi]
        var s = rest(p, hi)
        s.frame = GXFrame.mix(fa, fb, u)
        let protein = Array(p.gene.protein)
        func w(_ t0: Double, _ t1: Double) -> Double { min(max((u - t0) / (t1 - t0), 0), 1) }
        func ease(_ x: Double) -> Double { 1 - pow(1 - x, 3) }
        switch (fa.stage, fb.stage) {
        case (.txInit, .txElong), (.txElong, .txElong):
            // (the next nucleotide drops in from the free NTPs)
            s.incoming = Int(fa.made)
            s.incomingProgress = u
        case (.tlInit, .tlElong), (.tlElong, .tlElong):
            // (a) the charged tRNA comes into the A site; (b) the peptide bond moves the chain onto its amino acid;
            // (c) the ribosome moves one codon on, the empty tRNA now in the E site; the E site's old tRNA leaves in (a)
            let pp = Int(fa.pSite.rounded())
            let u1 = w(0, 0.35), u2 = w(0.35, 0.65), u3 = w(0.65, 1)
            var ts = [TRNA(codon: pp + 1, lift: 1 - ease(u1), alpha: u1, charged: true, initiator: false),
                      TRNA(codon: pp, lift: 0.15 * u3, alpha: 1 - 0.45 * u3, charged: u2 < 0.5, initiator: pp == 0)]
            if pp >= 1 { ts.append(TRNA(codon: pp - 1, lift: 0.15 + 0.85 * u1, alpha: 0.55 * (1 - u1), charged: false, initiator: pp == 1)) }
            s.trnas = ts
            var rs: [Residue] = []
            for k in 0...min(pp + 1, protein.count - 1) {
                if k < pp {
                    rs.append(Residue(index: k, aa: protein[k], x: Double(k), onTRNA: 0, alpha: 1, bonded: k > 0 ? 1 : 0))
                } else if k == pp {
                    rs.append(Residue(index: k, aa: protein[k], x: Double(k), onTRNA: 1 - u2, alpha: 1, bonded: k > 0 ? 1 : 0))
                } else {
                    rs.append(Residue(index: k, aa: protein[k], x: Double(k), onTRNA: 1, alpha: u1, bonded: u2))
                }
            }
            s.residues = rs
            s.ribosome = fa.pSite + u3
            s.frame.chain = u2 < 0.5 ? fa.chain : fb.chain
        case (.tlInit, .tlEnd), (.tlElong, .tlEnd):
            let pp = Int(fa.pSite.rounded())
            if p.gene.hasStop {
                // (a release factor into the A site; the chain let go; the subunits part and the last tRNA leaves)
                let u1 = w(0, 0.35), u2 = w(0.35, 0.65), u3 = w(0.65, 1)
                s.releaseLift = 1 - ease(u1)
                var ts = [TRNA(codon: pp, lift: u3, alpha: 1 - u3, charged: u2 < 0.5, initiator: pp == 0)]
                if pp >= 1 { ts.append(TRNA(codon: pp - 1, lift: 0.15 + 0.85 * u1, alpha: 0.55 * (1 - u1), charged: false, initiator: pp == 1)) }
                s.trnas = ts
                s.residues = (0..<min(pp + 1, protein.count)).map { k in
                    Residue(index: k, aa: protein[k], x: Double(k), onTRNA: k == pp ? 1 - u2 : 0, alpha: 1, bonded: k > 0 ? 1 : 0)
                }
            } else {
                s = rest(p, lo)
                s.frame = GXFrame.mix(fa, fb, u)
            }
            s.ribosome = fa.pSite
        case (_, .tlInit):
            // (the initiator tRNA with its Met comes into the P site as the small subunit reaches the AUG)
            let a = s.frame.initiator
            s.trnas = [TRNA(codon: 0, lift: 1 - ease(a), alpha: a, charged: true, initiator: true)]
            s.residues = protein.isEmpty ? [] : [Residue(index: 0, aa: protein[0], x: 0, onTRNA: 1, alpha: a, bonded: 0)]
        case (.tlEnd, .result):
            s.residues = (0..<protein.count).map { k in Residue(index: k, aa: protein[k], x: Double(k), onTRNA: 0, alpha: 1, bonded: k > 0 ? 1 : 0) }
            if p.gene.hasStop { s.releaseLift = w(0, 0.5) }
        default:
            break
        }
        return s
    }
}
