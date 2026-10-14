import AppKit
import SceneKit
import SwiftUI

// DNA & RNA (spec §4.2): the double helix in 3D (cartoon, atoms or space filling; ends, grooves, measurements,
// H-bonds; unzipping), the nucleotides (in 3D, by part, numbered, DNA against RNA, and the bases in 2D), a strand's
// facts (its complement written 3′→5′ under it, the reverse complement, counts, Chargaff, mass, melting temperature),
// replication step by step, and RNA (an mRNA's parts, the tRNA's cloverleaf and its 3D L-shape, the ribosomes). Each
// page is a view of its own (DNAHelixPage.swift …); what they share — the sequence, each page's choices and selection,
// replication's step-through — is the store here, kept while the app runs and remembered (SimplBio.dna.<field>). The
// launch's state (-SimplTool dna -SimplToolState <page>:<options>) is applied once, on first appear.

struct DNATool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.dna)`.
    static let pages: [BioPage] = [
        BioPage(id: "helix", title: "Double helix", symbol: "link", help: "B-DNA in 3D"),
        BioPage(id: "nucleotides", title: "Nucleotides", symbol: "atom", help: "The eight nucleotides and ATP"),
        BioPage(id: "sequence", title: "Sequence", symbol: "textformat.abc", help: "A strand, its complement and its facts"),
        BioPage(id: "replication", title: "Replication", symbol: "arrow.triangle.branch", help: "A replication fork, step by step"),
        BioPage(id: "rna", title: "RNA", symbol: "waveform.path", help: "mRNA, tRNA and ribosomes"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var store = DNAStore.shared

    var body: some View {
        page
            .onAppear {
                store.apply(state)
                store.takeHandOff()
            }
    }

    @ViewBuilder
    private var page: some View {
        switch nav.page(.dna) {
        case "nucleotides": DNANucleotidesPage(store: store)
        case "sequence": DNASequencePage(store: store)
        case "replication": DNAReplicationPage(store: store, steps: store.replication)
        case "rna": DNARNAPage(store: store)
        default: DNAHelixPage(store: store)
        }
    }
}

/// The Nucleotides page's two views.
enum DNANucleotideView: String, CaseIterable {
    case model3D, bases2D
    var label: String { self == .model3D ? "3D" : "Bases in 2D" }
}

/// The RNA page's four views.
enum DNARNAView: String, CaseIterable {
    case cloverleaf, lShape, mrna, ribosomes

    var label: String {
        switch self {
        case .cloverleaf: return "tRNA"
        case .lShape: return "tRNA 3D"
        case .mrna: return "mRNA"
        case .ribosomes: return "rRNA"
        }
    }

    var help: String {
        switch self {
        case .cloverleaf: return "Transfer RNA's cloverleaf: its stems, loops and anticodon"
        case .lShape: return "Transfer RNA folded: its L-shape in 3D"
        case .mrna: return "A messenger RNA's parts, cap to tail"
        case .ribosomes: return "Ribosomal RNA and the ribosome's subunits"
        }
    }
}

/// What DNA & RNA's pages share and remember: the sequence, each page's choices and selection, and replication's
/// step-through. One for the app, so a page keeps its state when the tool is closed and opened again.
@MainActor
final class DNAStore: ObservableObject {
    static let shared = DNAStore()

    // The Sequence page (and the helix's sequence)
    @Published var text: String {
        didSet {
            BioPrefs.set(.dna, "sequence", text)
            // (a new strand: what was selected in the old helix may not be in this one)
            if oldValue != text { helixSelection = nil; helixAtom = nil }
        }
    }
    @Published var kind: BioSeqKind { didSet { BioPrefs.set(.dna, "kind", kind.rawValue) } }

    // The double helix
    @Published var helixStyle: DNAHelixStyle { didSet { BioPrefs.set(.dna, "style", helixStyle.rawValue) } }
    @Published var showEnds: Bool { didSet { BioPrefs.set(.dna, "ends", showEnds) } }
    @Published var showGrooves: Bool { didSet { BioPrefs.set(.dna, "grooves", showGrooves) } }
    @Published var showMeasure: Bool { didSet { BioPrefs.set(.dna, "measure", showMeasure) } }
    @Published var showHBonds: Bool { didSet { BioPrefs.set(.dna, "hbonds", showHBonds) } }
    @Published var showLabels: Bool { didSet { BioPrefs.set(.dna, "labels", showLabels) } }
    @Published var unzip = 0.0
    @Published var helixSelection: String?
    /// The atom picked in the atom views (its index in the helix), for the card.
    @Published var helixAtom: Int?

    // Nucleotides
    @Published var nucleotide: String { didSet { BioPrefs.set(.dna, "nucleotide", nucleotide) } }
    @Published var ntView: DNANucleotideView { didSet { BioPrefs.set(.dna, "ntView", ntView.rawValue) } }
    @Published var ntStyle: BioAtomStyle = .ballStick
    @Published var ntHydrogens = true
    @Published var ntPH7 = true
    @Published var ntParts: Bool { didSet { BioPrefs.set(.dna, "parts", ntParts) } }
    @Published var ntNumbers: Bool { didSet { BioPrefs.set(.dna, "numbers", ntNumbers) } }
    @Published var ntCompare: Bool { didSet { BioPrefs.set(.dna, "compare", ntCompare) } }
    @Published var ntLabels = true
    @Published var ntSelection: String?
    @Published var ntAtom: Int?
    /// The 2D panel's first pair: A·U (RNA) instead of A·T.
    @Published var pairWithU = false

    // RNA
    @Published var rnaView: DNARNAView { didSet { BioPrefs.set(.dna, "rnaView", rnaView.rawValue) } }
    @Published var showModified: Bool { didSet { BioPrefs.set(.dna, "modified", showModified) } }
    /// The cloverleaf's selected base (1–76).
    @Published var trnaBase: Int?
    @Published var trnaSelection: String?
    @Published var trnaNucleotide: Int?
    @Published var trnaLabels = true

    // Replication
    let replication: BioStepController

    private var launchApplied = false

    init() {
        replication = BioStepController(steps: DNAReplication.steps)
        text = BioPrefs.string(.dna, "sequence") ?? DNAModel.defaultSequence
        kind = BioSeqKind(rawValue: BioPrefs.string(.dna, "kind") ?? "") == .rna ? .rna : .dna
        helixStyle = DNAHelixStyle(rawValue: BioPrefs.string(.dna, "style") ?? "") ?? .cartoon
        showEnds = BioPrefs.bool(.dna, "ends", default: true)
        showGrooves = BioPrefs.bool(.dna, "grooves", default: false)
        showMeasure = BioPrefs.bool(.dna, "measure", default: false)
        showHBonds = BioPrefs.bool(.dna, "hbonds", default: true)
        showLabels = BioPrefs.bool(.dna, "labels", default: true)
        let nt = BioPrefs.string(.dna, "nucleotide") ?? "DA"
        nucleotide = DNANucleotides.all.contains(nt) ? nt : "DA"
        ntView = DNANucleotideView(rawValue: BioPrefs.string(.dna, "ntView") ?? "") ?? .model3D
        ntParts = BioPrefs.bool(.dna, "parts", default: true)
        ntNumbers = BioPrefs.bool(.dna, "numbers", default: false)
        ntCompare = BioPrefs.bool(.dna, "compare", default: false)
        rnaView = DNARNAView(rawValue: BioPrefs.string(.dna, "rnaView") ?? "") ?? .cloverleaf
        showModified = BioPrefs.bool(.dna, "modified", default: false)
        // (a launch's state from the start, before any page is drawn: applied as the tool appeared, some of it — the
        // grooves, the unzip, the bases in 2D, the tRNA's L-shape — never reached the pictures)
        apply(BioLaunchState.forTool(.dna))
    }

    // MARK: The sequence

    /// The typed strand, cleaned (§3.7), capped at 3,000.
    var input: BioSequenceInput { BioSequenceInput(text, as: kind, cap: DNAModel.sequenceCap) }

    /// The helix's strand 1: the typed DNA's first 40 of A, C, G and T — or, for RNA, the DNA coding strand it would be
    /// copied from (U → T), so DNA never shows a U.
    var helixSequence: String {
        let letters = input.letters
        return DNAModel.helixBases(kind == .rna ? BioSeq.toDNA(letters) : letters)
    }

    func randomSequence() {
        kind = .dna
        text = DNAModel.random(length: 20, seed: UInt64(max(0, Date().timeIntervalSince1970 * 1000)))
        clearHelixSelection()
    }

    // MARK: Picking

    func clearHelixSelection() {
        helixSelection = nil
        helixAtom = nil
    }

    /// A press in the helix (an item's or a label's id): its pair, a nucleotide's backbone, or a groove — again to let go.
    func pickHelix(_ id: String?) {
        guard let part = DNAHelix.part(id) else { clearHelixSelection(); return }
        let sel = DNAHelix.selection(part)
        let atom = DNAHelix.atomIndex(id)
        if sel == helixSelection && atom == helixAtom { clearHelixSelection(); return }
        helixSelection = sel
        helixAtom = atom
    }

    func selectPair(_ k: Int) {
        let sel = DNAHelix.selection(.pair(k))
        helixAtom = nil
        helixSelection = helixSelection == sel ? nil : sel
    }

    /// A press on a nucleotide: its part (every atom of it lit), the glycosidic bond, or a lit difference.
    func pickNucleotide(_ id: String?) {
        guard let id else { ntSelection = nil; ntAtom = nil; return }
        let f = id.split(separator: ":").map(String.init)
        let sel = f.count >= 2 ? "\(f[0]):\(f[1])" : id
        let atom = DNAHelix.atomIndex(id)
        if sel == ntSelection && atom == ntAtom { ntSelection = nil; ntAtom = nil; return }
        ntSelection = sel
        ntAtom = atom
    }

    /// A press on the tRNA's L-shape: the arm it is in (and the nucleotide, for the card).
    func pickTRNA(_ id: String?) {
        guard let arm = DNARNA.arm(id) else { trnaSelection = nil; trnaNucleotide = nil; return }
        let sel = "arm:\(arm.rawValue)"
        let last = id?.split(separator: ":").last.map(String.init) ?? ""
        let nt = Int(last.hasPrefix("rung") ? String(last.dropFirst(4)) : last)
        if sel == trnaSelection && nt == trnaNucleotide { trnaSelection = nil; trnaNucleotide = nil; return }
        trnaSelection = sel
        trnaNucleotide = nt
    }

    // MARK: Launch and hand-off

    /// The launch's state, once (spec §9 launch flags): `helix:style=atoms:grooves:measure:unzip=0.5:pair=3`,
    /// `nucleotides:mol=DT:numbers:compare`, `nucleotides:pairs:au`, `sequence:seq=ATGC`, `replication:step=4`,
    /// `rna:view=cloverleaf:modified:base=34`; and the flags -SimplBioDNAStyle, -SimplBioSeq, -SimplBioStep.
    func apply(_ state: BioLaunchState?) {
        guard !launchApplied else { return }
        launchApplied = true
        guard let s = state else { return }
        if let seq = s.value("seq") {
            let upper = seq.uppercased()
            kind = upper.contains("U") && !upper.contains("T") ? .rna : .dna
            text = seq
        }
        if let st = (s.value("style") ?? s.value("DNAStyle"))?.lowercased() {
            helixStyle = st.hasPrefix("atom") ? .atoms : (st.hasPrefix("space") ? .spaceFill : .cartoon)
        }
        if s.has("grooves") { showGrooves = true }
        if s.has("measure") { showMeasure = true }
        if s.has("noends") { showEnds = false }
        if s.has("ends") { showEnds = true }
        if s.has("nohbonds") { showHBonds = false }
        if s.has("nolabels") { showLabels = false; ntLabels = false; trnaLabels = false }
        if let u = s.value("unzip").flatMap(Double.init) { unzip = min(max(u, 0), 1) }
        if let k = s.value("pair").flatMap(Int.init), k >= 1, k <= helixSequence.count { helixSelection = DNAHelix.selection(.pair(k - 1)) }
        if let m = s.value("mol") ?? s.value("Nucleotide"), DNANucleotides.all.contains(m.uppercased()) { nucleotide = m.uppercased() }
        if s.has("pairs") || s.has("bases") { ntView = .bases2D }
        if s.has("model") { ntView = .model3D }
        if s.has("au") { pairWithU = true }
        if s.has("parts") { ntParts = true }
        if s.has("noparts") { ntParts = false }
        if s.has("numbers") { ntNumbers = true }
        if s.has("compare") { ntCompare = true }
        if let part = s.value("part") { ntSelection = "part:\(part)" }
        if let v = s.value("view"), let view = DNARNAView.allCases.first(where: { $0.rawValue.lowercased() == v.lowercased() || $0.label.lowercased() == v.lowercased() }) {
            rnaView = view
        } else if let v = s.value("view")?.lowercased() {
            if v == "3d" || v == "l" { rnaView = .lShape } else if v.hasPrefix("rib") || v == "rrna" { rnaView = .ribosomes }
        }
        if s.has("modified") { showModified = true }
        if let b = s.value("base").flatMap(Int.init), (1...76).contains(b) { trnaBase = b }
        if let arm = s.value("arm"), let a = DNATRNAArm(rawValue: arm) { trnaSelection = "arm:\(a.rawValue)" }
        if let step = s.value("step").flatMap(Int.init) {
            replication.go(to: min(max(step, 1), DNAReplication.steps.count) - 1, animated: false)
        }
        if OpenClock.verbose {
            NSLog("[Simpl] bio launch: dna %@: style %@, grooves %@, measure %@, unzip %.2f, nucleotide %@ (%@), rna %@, step %d", s.raw,
                  helixStyle.rawValue, showGrooves ? "on" : "off", showMeasure ? "on" : "off", unzip, nucleotide, ntView.rawValue,
                  rnaView.rawValue, replication.index + 1)
        }
    }

    /// What another tool handed over (spec §3.2): a sequence (and the page to show it on), or a nucleotide.
    func takeHandOff() {
        let tools = ToolsCenter.shared
        let page = tools.takeInfo("page").flatMap { p in DNATool.pages.contains(where: { $0.id == p }) ? p : nil }
        if let seq = tools.takeInfo("sequence") {
            let upper = seq.uppercased()
            kind = upper.contains("U") && !upper.contains("T") ? .rna : .dna
            text = seq
            clearHelixSelection()
            BioNav.shared.set(.dna, page ?? "sequence")
        } else if let nt = tools.takeInfo("nucleotide")?.uppercased(), DNANucleotides.all.contains(nt) {
            nucleotide = nt
            ntView = .model3D
            BioNav.shared.set(.dna, page ?? "nucleotides")
        } else if let page {
            BioNav.shared.set(.dna, page)
        }
    }
}

// MARK: - Words in a scene

/// The tool's words in 3D (DNATextMark) as SceneKit text facing the camera, the chemistry builder's way; and the atoms'
/// look from the chemistry tables. On the main thread (the scenes' extras are made there).
@MainActor
enum DNASceneText {
    /// CPK colours and radii for the nucleic acids' elements, from MBChem (read once, here on the main thread; the
    /// scenes made off it are handed the values).
    static let atomLook: DNAAtomLook = {
        var hex: [String: String] = [:], covalent: [String: Double] = [:], vdw: [String: Double] = [:]
        for e in DNAAtomLook.elements {
            hex[e] = BioMoleculeScene.cpkHex(e)
            covalent[e] = MBChem.covalent(e)
            vdw[e] = MBChem.vdw(e)
        }
        return DNAAtomLook(hex: hex, covalent: covalent, vdw: vdw)
    }()

    static func nodes(_ marks: [DNATextMark]) -> [SCNNode] { marks.map(node) }

    /// Text geometry made once for each look of each word (a helix's 80 letters share a handful), with its bounds.
    private static var made: [String: (text: SCNText, lo: SCNVector3, hi: SCNVector3)] = [:]

    private static func geometry(_ m: DNATextMark) -> (text: SCNText, lo: SCNVector3, hi: SCNVector3) {
        let key = "\(m.text)|\(m.hex)|\(m.bold)|\(m.onTop)|\(m.nearOnly ?? -1)"
        if let g = made[key] { return g }
        let text = SCNText(string: m.text, extrusionDepth: 0)
        text.font = NSFont.systemFont(ofSize: 1, weight: m.bold ? .bold : .semibold)
        text.flatness = 0.02
        let mat = SCNMaterial()
        mat.diffuse.contents = NSColor(ptHex: m.hex)
        mat.lightingModel = .constant
        mat.writesToDepthBuffer = false
        if m.onTop { mat.readsFromDepthBuffer = false }
        text.materials = [mat]
        if let near = m.nearOnly {
            // (beyond this distance from the camera the text is not drawn at all)
            text.levelsOfDetail = [SCNLevelOfDetail(geometry: nil, worldSpaceDistance: CGFloat(near))]
        }
        let (lo, hi) = SCNNode(geometry: text).boundingBox
        if made.count > 400 { made = [:] }
        made[key] = (text, lo, hi)
        return (text, lo, hi)
    }

    static func node(_ m: DNATextMark) -> SCNNode {
        let (text, lo, hi) = geometry(m)
        let n = SCNNode(geometry: text)
        let x: CGFloat
        switch m.align {
        case .leading: x = lo.x
        case .centre: x = (lo.x + hi.x) / 2
        case .trailing: x = hi.x
        }
        n.pivot = SCNMatrix4MakeTranslation(x, (lo.y + hi.y) / 2, 0)
        n.scale = SCNVector3(CGFloat(m.size), CGFloat(m.size), CGFloat(m.size))
        n.position = SCNVector3(0, 0, CGFloat(m.lift))
        n.renderingOrder = m.onTop ? 20 : 10
        let holder = SCNNode()
        holder.position = SCNVector3(CGFloat(m.pos.x), CGFloat(m.pos.y), CGFloat(m.pos.z))
        holder.addChildNode(n)
        holder.constraints = [SCNBillboardConstraint()]
        return holder
    }
}

/// The words a scene was made with off the main thread, kept by the scene's key until its extras are made on the main
/// thread when it lands (BioSceneView makes its extras then).
final class DNAMarkShelf: @unchecked Sendable {
    static let shared = DNAMarkShelf()
    private let lock = NSLock()
    private var byKey: [String: [DNATextMark]] = [:]
    private var order: [String] = []

    func put(_ key: String, _ marks: [DNATextMark]) {
        lock.lock()
        defer { lock.unlock() }
        if byKey[key] == nil { order.append(key) }
        byKey[key] = marks
        while order.count > 16 { byKey[order.removeFirst()] = nil }
    }

    func take(_ key: String) -> [DNATextMark] {
        lock.lock()
        defer { lock.unlock() }
        return byKey[key] ?? []
    }
}

// MARK: - Small pieces the pages share

enum DNAText {
    /// "C10 H14 N5 O6 P" → "C₁₀H₁₄N₅O₆P".
    static func formula(_ f: String) -> String {
        let sub: [Character: Character] = ["0": "₀", "1": "₁", "2": "₂", "3": "₃", "4": "₄", "5": "₅", "6": "₆", "7": "₇", "8": "₈", "9": "₉"]
        return String(f.filter { $0 != " " }.map { sub[$0] ?? $0 })
    }

    /// A number with a thousands separator and `digits` decimals, rounded half up (`Double.bioHalfUp`).
    static func number(_ v: Double, digits: Int = 2) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        f.roundingMode = .halfUp
        f.minimumFractionDigits = digits
        f.maximumFractionDigits = digits
        return f.string(from: NSNumber(value: v.bioHalfUp(digits))) ?? String(halfUp: "%.\(digits)f", v)
    }
}

/// A strand's letters, each in its base's colour (the letter always shown), monospaced.
struct DNABaseLetters: View {
    let letters: String
    var size: CGFloat = 14

    var body: some View {
        letters.reduce(Text("")) { $0 + Text(String($1)).foregroundColor(BioColor.base($1)) }
            .font(.system(size: size, weight: .semibold, design: .monospaced))
    }
}

/// A side column's choice made with a row of small buttons (the molecule builder's palette look).
struct DNAChip: View {
    let title: String
    var detail: String? = nil
    var swatch: Color? = nil
    let selected: Bool
    var help: String = ""
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 7) {
                if let swatch {
                    Circle().fill(swatch).frame(width: 10, height: 10)
                        .overlay(Circle().strokeBorder(Color.primary.opacity(0.25), lineWidth: 0.5))
                }
                VStack(alignment: .leading, spacing: 0) {
                    Text(title).font(.sCallout.weight(.semibold))
                    if let detail { Text(detail).font(.sCaption2).foregroundStyle(.secondary) }
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 10)
            .frame(maxWidth: .infinity, minHeight: 34, alignment: .leading)
            .background(selected ? Theme.accent.opacity(0.2) : Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(selected ? Theme.accent : Color.clear, lineWidth: 1.2))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help.isEmpty ? title : help)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}
