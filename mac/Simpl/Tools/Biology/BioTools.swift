import AppKit
import SwiftUI

// The biology toolkit's place among the tools (spec §2, §3.2): its ten tools' names, lines, symbols and colours; the
// "Biology" section of the Tools page; each tool's pages in its header (the Periodic Table's pattern, remembered per
// tool); what a launch asks of a tool (-SimplTool cell -SimplToolState plant:cutaway, for the screenshot suite); and the
// self-check on the console (-SimplBioCheck YES). Each tool's view is in a file of its own (CellExplorerTool.swift …):
// a tool's builder changes that file, never this one.

/// A page of a biology tool, picked in its header.
struct BioPage: Identifiable, Hashable {
    /// What the tool switches on, and what a launch's state names ("plant").
    let id: String
    let title: String
    let symbol: String
    /// The page button's tooltip.
    var help: String = ""
}

/// What the Tools page, the toolbar and a pin's popover show of a biology tool.
struct BioToolInfo {
    let name: String
    /// For pins and tooltips (spec §2.1).
    let shortName: String
    let note: String
    let symbol: String
    /// Used when the first symbol is not in this Mac's SF Symbols.
    let fallback: String?
    let hex: String
}

enum BioTools {
    /// The ten, in the Tools page's order.
    static let kinds: [ToolKind] = [.cell, .dna, .gene, .biomol, .cross, .divide, .popgen, .biolab, .enzyme, .energy]

    static func info(_ k: ToolKind) -> BioToolInfo {
        switch k {
        case .cell: return BioToolInfo(name: "Cell Explorer", shortName: "Cells", note: "Animal, plant and bacterial cells in 3D.",
                                       symbol: "allergens", fallback: "circle.hexagongrid", hex: "#2fb67c")
        case .dna: return BioToolInfo(name: "DNA & RNA", shortName: "DNA", note: "The double helix, nucleotides and RNA, in 3D.",
                                      symbol: "link", fallback: "waveform.path", hex: "#7d5cf5")
        case .gene: return BioToolInfo(name: "Gene Expression", shortName: "Genes", note: "Transcribe and translate a gene, step by step.",
                                       symbol: "text.badge.plus", fallback: "textformat.abc", hex: "#f2994a")
        case .biomol: return BioToolInfo(name: "Biomolecule Builder", shortName: "Biomolecules", note: "Build proteins, nucleic acids, sugars and lipids.",
                                         symbol: "puzzlepiece", fallback: "cube", hex: "#e8508a")
        case .cross: return BioToolInfo(name: "Genetics Crosses", shortName: "Crosses", note: "Punnett squares, probability and chi-square.",
                                        symbol: "square.grid.2x2", fallback: nil, hex: "#3b82f6")
        case .divide: return BioToolInfo(name: "Cell Division", shortName: "Division", note: "Mitosis and meiosis, stage by stage, with counts.",
                                         symbol: "arrow.triangle.branch", fallback: nil, hex: "#14b8a6")
        case .popgen: return BioToolInfo(name: "Hardy–Weinberg", shortName: "Hardy–Weinberg", note: "Allele frequencies, equilibrium and evolution.",
                                         symbol: "person.3", fallback: nil, hex: "#8bbf3f")
        case .biolab: return BioToolInfo(name: "Bio Lab Calculator", shortName: "Lab maths", note: "Dilutions, microscopy, stats and the AP formula sheet.",
                                         symbol: "testtube.2", fallback: nil, hex: "#c27c4a")
        case .enzyme: return BioToolInfo(name: "Enzyme Lab", shortName: "Enzymes", note: "How temperature, pH and inhibitors change rate.",
                                         symbol: "bolt", fallback: nil, hex: "#d9a406")
        case .energy: return BioToolInfo(name: "Photosynthesis & Respiration", shortName: "Energy", note: "Where each step happens and what goes in and out.",
                                         symbol: "leaf", fallback: nil, hex: "#65a30d")
        default: return BioToolInfo(name: k.rawValue, shortName: k.rawValue, note: "", symbol: "questionmark", fallback: nil, hex: "#8e8e93")
        }
    }

    /// The symbol drawn: the first choice if this Mac has it, else its fallback (spec §2.1), found once.
    static func symbol(_ k: ToolKind) -> String { resolvedSymbols[k] ?? info(k).symbol }

    private static let resolvedSymbols: [ToolKind: String] = {
        var out: [ToolKind: String] = [:]
        for k in kinds {
            let i = info(k)
            if NSImage(systemSymbolName: i.symbol, accessibilityDescription: nil) == nil, let f = i.fallback { out[k] = f } else { out[k] = i.symbol }
        }
        return out
    }()

    /// The pin popover's width (spec §3.2): Gene Expression's codon lookup is widest.
    static func popoverWidth(_ k: ToolKind) -> CGFloat {
        switch k {
        case .gene: return 420
        case .cross: return 400
        case .biolab: return 380
        case .popgen: return 360
        default: return 330
        }
    }

    /// A tool's pages, as its own file lists them (none: a tool of one page).
    @MainActor
    static func pages(_ k: ToolKind) -> [BioPage] {
        switch k {
        case .cell: return CellExplorerTool.pages
        case .dna: return DNATool.pages
        case .gene: return GeneExpressionTool.pages
        case .biomol: return BiomoleculeTool.pages
        case .cross: return CrossesTool.pages
        case .divide: return CellDivisionTool.pages
        case .popgen: return HardyWeinbergTool.pages
        case .biolab: return BioLabTool.pages
        case .enzyme: return EnzymeLabTool.pages
        case .energy: return EnergyPathwaysTool.pages
        default: return []
        }
    }

    /// Another tool opened with something handed to it (spec §3.2: "Open in Gene Expression" hands over
    /// ["sequence": "ATG…"]; the tool takes it once with ToolsCenter.shared.takeInfo("sequence")) — in the pin's sheet
    /// when the tool was opened there, else on the Tools page.
    @MainActor
    static func open(_ k: ToolKind, info: [String: String] = [:]) {
        let tools = ToolsCenter.shared
        if tools.popup != nil { tools.popUp(k, info: info) } else { tools.show(k, engine: AppModel.shared.engine, info: info) }
    }

    @MainActor private static var selfCheckStarted = false

    /// (-SimplBioCheck YES) The self-check's lines on the console, once a launch, off the main thread.
    @MainActor
    static func selfCheckIfAsked() {
        guard !selfCheckStarted, UserDefaults.standard.bool(forKey: "SimplBioCheck") else { return }
        selfCheckStarted = true
        Task.detached(priority: .utility) { BioSelfCheck.printAll() }
    }
}

extension ToolKind {
    /// One of the biology toolkit's ten (they have a section of their own on the Tools page).
    var isBiology: Bool { BioTools.kinds.contains(self) }
}

// MARK: - What a launch asks of a tool

/// What a launch asked of a tool (spec §9 Screenshots and launch flags): `-SimplTool <key> -SimplToolState <state>`,
/// the state free-form, its parts split at ":" — `plant:cutaway`, `helix:style=atoms:seq=ATGC`. A first part that names
/// one of the tool's pages opens that page (BioNav does it); the rest is the tool's to read: `has("cutaway")`,
/// `value("style")`. The specification's own flags read the same way: `-SimplBioSeq ATGC` is `value("Seq")`,
/// `-SimplBioStep 4` is `value("Step")` (any `-SimplBio<Name> <value>`). Only the tool the launch opened gets one.
struct BioLaunchState: Equatable {
    let raw: String
    let parts: [String]
    let kind: ToolKind

    /// The page the state names, if its first part (or -SimplBioPage) is one of the tool's.
    @MainActor
    var page: String? {
        let ids = BioTools.pages(kind).map(\.id)
        if let p = UserDefaults.standard.string(forKey: "SimplBioPage"), ids.contains(p) { return p }
        if let first = parts.first, ids.contains(first) { return first }
        return nil
    }

    /// A bare part ("cutaway").
    func has(_ word: String) -> Bool { parts.contains { $0.caseInsensitiveCompare(word) == .orderedSame } }

    /// A "key=value" part, or the flag -SimplBio<Key>.
    func value(_ key: String) -> String? {
        for p in parts {
            let kv = p.split(separator: "=", maxSplits: 1).map(String.init)
            if kv.count == 2, kv[0].caseInsensitiveCompare(key) == .orderedSame { return kv[1] }
        }
        let flag = "SimplBio" + key.prefix(1).uppercased() + String(key.dropFirst())
        return UserDefaults.standard.string(forKey: flag)
    }

    /// The launch's state for this tool, if the launch opened it (-SimplTool <its key>) with a state or any
    /// -SimplBio… flag.
    static func forTool(_ k: ToolKind) -> BioLaunchState? {
        let d = UserDefaults.standard
        guard d.string(forKey: "SimplTool") == k.rawValue else { return nil }
        let raw = d.string(forKey: "SimplToolState") ?? ""
        guard !raw.isEmpty || launchFlags else { return nil }
        return BioLaunchState(raw: raw, parts: raw.split(separator: ":").map(String.init), kind: k)
    }

    /// Whether the launch has any -SimplBio… flag (but the self-check's).
    private static let launchFlags = ProcessInfo.processInfo.arguments.contains { $0.hasPrefix("-SimplBio") && $0 != "-SimplBioCheck" }
}

// MARK: - Pages

/// Which page each biology tool shows (kept per tool: `SimplBio.<key>.page`), chosen in its header.
@MainActor
final class BioNav: ObservableObject {
    static let shared = BioNav()
    @Published private var pages: [ToolKind: String] = [:]

    /// The page shown: the one chosen last, the launch's (for the tool a launch opened, the first time), else the first.
    func page(_ k: ToolKind) -> String {
        if let p = pages[k] { return p }
        let ids = BioTools.pages(k).map(\.id)
        if let launched = BioLaunchState.forTool(k)?.page { return launched }
        if let saved = BioPrefs.string(k, "page"), ids.contains(saved) { return saved }
        return ids.first ?? ""
    }

    func set(_ k: ToolKind, _ page: String) {
        guard pages[k] != page else { return }
        pages[k] = page
        BioPrefs.set(k, "page", page)
    }
}

/// A biology tool's remembered settings (spec §3.2): UserDefaults `SimplBio.<key>.<field>`, on this Mac only. A
/// picture's launch (-SimplShotFile, the screenshot suite) keeps them for itself in a suite of its own that starts
/// empty, so one picture's choices (a plant cell, a cross typed, 2n = 6) never carry into the next picture taken in the
/// same home: each shows its tool as its launch asks and nothing more.
enum BioPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplBioPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func key(_ k: ToolKind, _ field: String) -> String { "SimplBio.\(k.rawValue).\(field)" }
    static func string(_ k: ToolKind, _ field: String) -> String? { store.string(forKey: key(k, field)) }
    static func set(_ k: ToolKind, _ field: String, _ value: String?) { store.set(value, forKey: key(k, field)) }
    static func bool(_ k: ToolKind, _ field: String, default d: Bool) -> Bool {
        store.object(forKey: key(k, field)) == nil ? d : store.bool(forKey: key(k, field))
    }
    static func set(_ k: ToolKind, _ field: String, _ value: Bool) { store.set(value, forKey: key(k, field)) }
}

/// A biology tool's pages in its header, left of its pin (the Periodic Table's PTHeaderControls): their names where
/// the header has room, else their symbols — as the header decides (`squished`), or as they fit (nil).
struct BioHeaderControls: View {
    let kind: ToolKind
    var squished: Bool? = nil
    @ObservedObject private var nav = BioNav.shared

    var body: some View {
        let pages = BioTools.pages(kind)
        if pages.count > 1 {
            if let squished {
                row(pages, squished: squished)
            } else {
                ViewThatFits(in: .horizontal) {
                    row(pages, squished: false)
                    row(pages, squished: true)
                }
            }
        }
    }

    private func row(_ pages: [BioPage], squished: Bool) -> some View {
        GlassGroup(spacing: 6) {
            HStack(spacing: 6) {
                ForEach(pages) { p in
                    Button {
                        withAnimation(Motion.gentle) { nav.set(kind, p.id) }
                    } label: {
                        Label(p.title, systemImage: p.symbol).labelStyle(SquishLabel(squished: squished))
                    }
                    .glassButton(prominent: nav.page(kind) == p.id)
                    .controlSize(.large)
                    .help(p.help.isEmpty ? p.title : p.help)
                    .accessibilityAddTraits(nav.page(kind) == p.id ? .isSelected : [])
                }
            }
        }
        .fixedSize()
    }
}

// MARK: - The Tools page's section, a tool's screen, a pin's quick version

/// The Tools page's "Biology" section: its heading and the ten tiles (spec §3.2 item 4) — (tools-32) headed and laid out
/// as the page's other sections (Tools.swift ToolGroupSection).
struct BioToolsSection: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Biology")
                    .font(.sTitle3)
                    .accessibilityAddTraits(.isHeader)
                Text("Cells, DNA, genes and biomolecules in 3D, and the maths of crosses, populations and the lab.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            LazyVGrid(columns: ToolTile.columns, spacing: 14) {
                ForEach(BioTools.kinds) { k in ToolTile(kind: k) }
            }
        }
        .id("biology")
        .onAppear { BioTools.selfCheckIfAsked() }
    }
}

/// A biology tool, open on the Tools page or in a pin's sheet: its own view, given what the launch asked of it.
struct BioToolScreen: View {
    let kind: ToolKind

    var body: some View {
        content
            .onAppear { BioTools.selfCheckIfAsked() }
    }

    @ViewBuilder
    private var content: some View {
        let state = BioLaunchState.forTool(kind)
        switch kind {
        case .cell: CellExplorerTool(state: state)
        case .dna: DNATool(state: state)
        case .gene: GeneExpressionTool(state: state)
        case .biomol: BiomoleculeTool(state: state)
        case .cross: CrossesTool(state: state)
        case .divide: CellDivisionTool(state: state)
        case .popgen: HardyWeinbergTool(state: state)
        case .biolab: BioLabTool(state: state)
        case .enzyme: EnzymeLabTool(state: state)
        case .energy: EnergyPathwaysTool(state: state)
        default: EmptyView()
        }
    }
}

/// A biology tool's quickest use in its pin's popover (spec §3.2 item 3): Gene Expression's codon lookup, a monohybrid
/// Punnett square, Hardy–Weinberg from one value and C₁V₁ with molarity — each in its tool's own file — and for the
/// rest, the tool's tile, its line and Open.
struct BioQuick: View {
    let kind: ToolKind
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        switch kind {
        case .gene: GeneExpressionCompact(openFull: openFull)
        case .cross: CrossesCompact(openFull: openFull)
        case .popgen: HardyWeinbergCompact(openFull: openFull)
        case .biolab: BioLabCompact(openFull: openFull)
        default: BioQuickOpen(kind: kind) { openFull([:]) }
        }
    }
}
