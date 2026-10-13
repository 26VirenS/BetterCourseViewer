import AppKit
import Foundation
import SwiftUI

// The Biomolecule Builder's state (spec §4.4, §3.2 Both): every page's choices — the shared look (style, Show H, the pH 7
// form, Colour by, labels), the amino acid shown, the peptide being built and its structure, the structure level, the
// nucleotide or strand, the sugar, the lipid — kept while the tool's view comes and goes and remembered on this Mac
// (SimplBio.biomol.<field>); the peptide built off the main thread; and the short motions (a residue or a nucleotide
// joining or leaving, a sugar split by water, ubiquitin unfolding) the scenes play once.

/// A motion a scene is playing: which, and the serial that makes its scene new.
struct BiomolMotionState: Equatable {
    enum Kind: String { case peptideAdded, peptideRemoved, strandAdded, strandRemoved, sugarSplit, sugarJoined }
    let kind: Kind
    let serial: Int
    /// What was there before (a peptide or a strand taken off is drawn from it).
    var oldPeptide: BiomolPeptide?
    var oldStrand: String?

    static func == (a: BiomolMotionState, b: BiomolMotionState) -> Bool { a.kind == b.kind && a.serial == b.serial }
}

@MainActor
final class BiomolState: ObservableObject {
    static let shared = BiomolState()

    enum ProteinMode: String, CaseIterable { case build, levels }
    enum Level: String, CaseIterable {
        case primary, secondary, tertiary, quaternary
        var title: String { rawValue.prefix(1).uppercased() + rawValue.dropFirst() }
    }
    enum NucleicMode: String, CaseIterable { case nucleotides, strand }
    enum CarbMode: String, CaseIterable { case mono, di, poly }

    // MARK: The shared look

    @Published var style: BioAtomStyle = .ballStick { didSet { BioPrefs.set(.biomol, "style", style.rawValue) } }
    @Published var ph7 = true { didSet { BioPrefs.set(.biomol, "ph7", ph7); if ph7 != oldValue { rebuildPeptide() } } }
    @Published var colourBy: BiomolColourBy = .element { didSet { BioPrefs.set(.biomol, "colour", colourBy.rawValue) } }
    @Published var labels = true { didSet { BioPrefs.set(.biomol, "labels", labels) } }
    /// Show H, where a page's own choice overrides its default (on for monomers, off for polymers over 200 atoms).
    @Published var showH: [String: Bool] = [:]
    @Published var turning = false
    @Published var reset = 0
    @Published var selection: String?

    // MARK: Amino acids

    @Published var aminoAcid: Character = "F" { didSet { BioPrefs.set(.biomol, "aa", String(aminoAcid)); selection = nil } }
    /// The mirror image, "D-form (not used in proteins)".
    @Published var dForm = false

    // MARK: Proteins

    @Published var proteinMode: ProteinMode = .build { didSet { BioPrefs.set(.biomol, "proteinMode", proteinMode.rawValue); selection = nil } }
    @Published var level: Level = .primary { didSet { BioPrefs.set(.biomol, "level", level.rawValue); selection = nil } }
    /// The sequence as typed, and what is built from it.
    @Published var sequenceText = BiomolPeptideBuilder.defaultSequence
    @Published private(set) var sequence = BiomolPeptideBuilder.defaultSequence
    @Published private(set) var sequenceMessage: String?
    /// Each residue's structure (as asked).
    @Published var structure: [BiomolSS] = Array(repeating: .coil, count: BiomolPeptideBuilder.defaultSequence.count)
    /// The structure a press or a drag across the chips gives.
    @Published var brush: BiomolSS = .helix
    @Published var hBonds = true { didSet { BioPrefs.set(.biomol, "hbonds", hBonds) } }
    @Published var ribbon: BiomolRibbon = .off { didSet { BioPrefs.set(.biomol, "ribbon", ribbon.rawValue) } }
    @Published var sheetDemo = false { didSet { selection = nil; peptideFrame += 1 } }
    @Published private(set) var peptide: BiomolPeptide?
    /// Raised with each peptide built (the scene's key).
    @Published private(set) var peptideVersion = 0
    /// Raised when the peptide's scene must frame afresh (it grew out of view, or changed shape).
    @Published private(set) var peptideFrame = 0
    private var framed: (centre: BioVec, radius: Double)?
    private var buildSerial = 0
    @Published var show: BiomolProteins.Show = .all
    @Published var denatured = false
    /// Folding back up: the coil shown going back to the fold (1.5 s) before the cartoon returns.
    @Published private(set) var renaturing = false
    /// When the denaturing (or its undoing) was asked for: the scene animates it if it is new.
    @Published private(set) var denatureAsked: Date?

    // MARK: Nucleic acids

    @Published var nucleicMode: NucleicMode = .nucleotides { didSet { BioPrefs.set(.biomol, "nucleicMode", nucleicMode.rawValue); selection = nil } }
    @Published var nucleotide = "DA" { didSet { BioPrefs.set(.biomol, "nt", nucleotide); selection = nil } }
    @Published var strandText = BiomolStrand.defaultSequence
    @Published private(set) var strand = BiomolStrand.defaultSequence
    @Published private(set) var strandMessage: String?
    @Published var complement = false { didSet { strandFrame += 1 } }
    @Published private(set) var strandFrame = 0

    // MARK: Carbohydrates

    @Published var carbMode: CarbMode = .mono { didSet { BioPrefs.set(.biomol, "carbMode", carbMode.rawValue); selection = nil } }
    @Published var mono = "GLC" { didSet { BioPrefs.set(.biomol, "mono", mono); selection = nil } }
    @Published var di = "MAL" { didSet { BioPrefs.set(.biomol, "di", di); selection = nil; split = false } }
    @Published var poly: BiomolPolysaccharide = .amylose { didSet { BioPrefs.set(.biomol, "poly", poly.rawValue) } }
    /// The disaccharide split by water (hydrolysed).
    @Published var split = false
    /// α- and β-glucose side by side.
    @Published var compareAnomers = false

    // MARK: Lipids

    /// A lipid's code, or "STE+OLA" for stearic and oleic acids side by side.
    @Published var lipid = "OLA" { didSet { BioPrefs.set(.biomol, "lipid", lipid); selection = nil } }

    // MARK: Motions

    @Published private(set) var motion: BiomolMotionState?
    private var motionSerial = 0

    private init() {
        let k = ToolKind.biomol
        if let s = BioPrefs.string(k, "style"), let v = BioAtomStyle(rawValue: s) { style = v }
        ph7 = BioPrefs.bool(k, "ph7", default: true)
        if let s = BioPrefs.string(k, "colour"), let v = BiomolColourBy(rawValue: s) { colourBy = v }
        labels = BioPrefs.bool(k, "labels", default: true)
        if let s = BioPrefs.string(k, "aa"), let c = s.first, BioAminoAcids.byOne[c] != nil { aminoAcid = c }
        if let s = BioPrefs.string(k, "proteinMode"), let v = ProteinMode(rawValue: s) { proteinMode = v }
        if let s = BioPrefs.string(k, "level"), let v = Level(rawValue: s) { level = v }
        if let s = BioPrefs.string(k, "sequence") { sequenceText = s }
        if let s = BioPrefs.string(k, "structure") { structure = BiomolSS.parse(s, count: s.count) }
        hBonds = BioPrefs.bool(k, "hbonds", default: true)
        if let s = BioPrefs.string(k, "ribbon"), let v = BiomolRibbon(rawValue: s) { ribbon = v }
        if let s = BioPrefs.string(k, "nucleicMode"), let v = NucleicMode(rawValue: s) { nucleicMode = v }
        if let s = BioPrefs.string(k, "nt"), BiomolNucleotide.codes.contains(s) { nucleotide = s }
        if let s = BioPrefs.string(k, "strand") { strandText = s }
        if let s = BioPrefs.string(k, "carbMode"), let v = CarbMode(rawValue: s) { carbMode = v }
        if let s = BioPrefs.string(k, "mono"), BiomolSugar.monosaccharides.contains(s) { mono = s }
        if let s = BioPrefs.string(k, "di"), BiomolSugar.disaccharides.contains(s) { di = s }
        if let s = BioPrefs.string(k, "poly"), let v = BiomolPolysaccharide(rawValue: s) { poly = v }
        if let s = BioPrefs.string(k, "lipid"), BiomolLipid.codes.contains(s) || s == "STE+OLA" { lipid = s }
        let saved = BioPrefs.string(k, "structure")
        applySequence(sequenceText, animate: false)
        if saved == nil { structure = BiomolState.defaultStructure(count: sequence.count) }
        applyStrand(strandText, animate: false)
        rebuildPeptide()
    }

    /// The default peptide's structure: a helix in its middle (residues 3–14) between short coils, a strand at its end —
    /// all three kinds on show.
    static func defaultStructure(count: Int) -> [BiomolSS] {
        let s = BiomolSS.parse("CCHHHHHHHHHHHHCCEEE", count: count)
        return s
    }

    // MARK: Show H (§4.4: on for monomers, off for polymers over 200 atoms)

    func hydrogens(_ page: String, atoms: Int) -> Bool { showH[page] ?? (atoms <= 200) }

    func setHydrogens(_ page: String, _ on: Bool) { showH[page] = on }

    // MARK: The peptide

    /// The sequence field's text, cleaned (§3.7); the structure kept residue for residue, a residue added at the end
    /// taking the last one's structure. A single residue added or taken off the end plays its motion.
    func applySequence(_ text: String, animate: Bool = true) {
        sequenceText = text
        let (seq, message) = BiomolInput.protein(text)
        sequenceMessage = message
        guard seq != sequence || structure.count != seq.count else { return }
        let before = sequence
        var ss = structure
        if ss.count < seq.count { ss += Array(repeating: ss.last ?? .coil, count: seq.count - ss.count) }
        if ss.count > seq.count { ss = Array(ss.prefix(seq.count)) }
        sequence = seq
        structure = ss
        BioPrefs.set(.biomol, "sequence", text)
        saveStructure()
        var change: BiomolMotionState.Kind?
        if animate && seq.count == before.count + 1 && seq.hasPrefix(before) && !before.isEmpty { change = .peptideAdded }
        if animate && before.count == seq.count + 1 && before.hasPrefix(seq) && !seq.isEmpty { change = .peptideRemoved }
        rebuildPeptide(change)
    }

    /// A residue added at the C-terminus (the palette), or the last one taken off.
    func addResidue(_ one: Character) {
        guard sequence.count < BiomolPeptideBuilder.maxResidues else { return }
        applySequence(sequence + String(one))
    }

    func removeLastResidue() {
        guard !sequence.isEmpty else { return }
        applySequence(String(sequence.dropLast()))
    }

    /// Residues `range` given a structure (a press, a drag across the chips, or "All …").
    func setStructure(_ s: BiomolSS, at indices: [Int]) {
        var ss = structure
        var changed = false
        for i in indices where i >= 0 && i < ss.count && ss[i] != s {
            ss[i] = s
            changed = true
        }
        guard changed else { return }
        structure = ss
        saveStructure()
        rebuildPeptide()
    }

    func setStructure(_ text: String) {
        structure = BiomolSS.parse(text, count: sequence.count)
        saveStructure()
        rebuildPeptide()
    }

    private func saveStructure() { BioPrefs.set(.biomol, "structure", BiomolSS.text(structure)) }

    /// The peptide built again off the main thread (the clash relief takes a moment for 40 residues); the newest wins.
    func rebuildPeptide(_ change: BiomolMotionState.Kind? = nil) {
        buildSerial += 1
        let serial = buildSerial
        let seq = sequence, ss = structure, form = ph7
        let old = peptide
        Task.detached(priority: .userInitiated) {
            let p = BiomolPeptideBuilder.build(seq, ss: ss, ph7: form)
            await MainActor.run {
                let me = BiomolState.shared
                guard me.buildSerial == serial else { return }
                me.peptide = p
                me.peptideVersion += 1
                me.reframePeptide(p)
                if let change, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
                    me.play(BiomolMotionState(kind: change, serial: 0, oldPeptide: old))
                }
            }
        }
    }

    /// The camera frames the peptide afresh when it has grown out of the ball last framed, or shrunk well inside it.
    private func reframePeptide(_ p: BiomolPeptide) {
        let b = BioV.boundingSphere(p.caTrace)
        let r = b.radius + 6
        if let f = framed, BioV.distance(b.centre, f.centre) + r <= f.radius * 1.1, r >= f.radius * 0.55 { return }
        framed = (b.centre, r)
        peptideFrame += 1
    }

    // MARK: The strand

    func applyStrand(_ text: String, animate: Bool = true) {
        strandText = text
        let (seq, message) = BiomolInput.dna(text)
        strandMessage = message
        guard seq != strand else { return }
        let before = strand
        strand = seq
        BioPrefs.set(.biomol, "strand", text)
        guard animate, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { strandFrame += 1; return }
        if seq.count == before.count + 1 && seq.hasPrefix(before) && !before.isEmpty {
            play(BiomolMotionState(kind: .strandAdded, serial: 0))
        } else if before.count == seq.count + 1 && before.hasPrefix(seq) && !seq.isEmpty {
            play(BiomolMotionState(kind: .strandRemoved, serial: 0, oldStrand: before))
        } else {
            strandFrame += 1
        }
    }

    func addNucleotide(_ b: Character) {
        guard strand.count < BiomolStrand.maxLength else { return }
        applyStrand(strand + String(b))
    }

    func removeLastNucleotide() {
        guard !strand.isEmpty else { return }
        applyStrand(String(strand.dropLast()))
    }

    // MARK: Sugars

    /// The disaccharide split by water, or joined again with water released.
    func toggleSplit() {
        split.toggle()
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { return }
        play(BiomolMotionState(kind: split ? .sugarSplit : .sugarJoined, serial: 0))
    }

    // MARK: Denaturing

    func setDenatured(_ on: Bool) {
        guard on != denatured else { return }
        denatured = on
        denatureAsked = Date()
        selection = nil
        guard !on, !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion else { renaturing = false; return }
        renaturing = true
        let asked = denatureAsked
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 1_650_000_000)
            let me = BiomolState.shared
            if me.denatureAsked == asked { me.renaturing = false }
        }
    }

    /// Whether a denaturing asked for just now should still play (rather than show its end).
    var denatureIsFresh: Bool { denatureAsked.map { Date().timeIntervalSince($0) < 0.6 } ?? false }

    // MARK: Motions

    /// A motion played: its scene shows it for as long as it runs, then the plain scene comes back.
    private func play(_ m: BiomolMotionState) {
        motionSerial += 1
        let serial = motionSerial
        let playing = BiomolMotionState(kind: m.kind, serial: serial, oldPeptide: m.oldPeptide, oldStrand: m.oldStrand)
        motion = playing
        let length: Double
        switch m.kind {
        case .sugarSplit, .sugarJoined: length = 1.15
        default: length = 1.0
        }
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64((length + 0.15) * 1_000_000_000))
            let me = BiomolState.shared
            if me.motion?.serial == serial { me.motion = nil }
        }
    }

    /// The motion now playing of some kinds.
    func motion(_ kinds: Set<BiomolMotionState.Kind>) -> BiomolMotionState? {
        guard let m = motion, kinds.contains(m.kind) else { return nil }
        return m
    }
}
