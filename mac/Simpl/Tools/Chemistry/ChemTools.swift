import AppKit
import SwiftUI

// (1.3.80) The chemistry tools' place among the tools, as the biology toolkit's is (Biology/BioTools.swift): the
// Periodic Table split into four tools under a "Chemistry" heading of their own on the Tools page, just before Biology —
// the Periodic Table (the table, coloured by any property with its trends, the element under the pointer, an element's
// facts page and the table's tour), Lewis Structures (the Lewis maker), the Molecule Builder (the 3D builder, its shapes
// by VSEPR) and Atoms & Orbitals (an element's atom, orbitals and electron cloud in 3D). Each has its own tile, pin,
// popup and heading, and keeps what it was left at (ChemPrefs). Where the table's pages and its element page's 3D view
// were, they now hand over to one another in the same popup: an element's facts page opens Atoms & Orbitals on it, which
// goes back to the facts; the Lewis maker's "Open in Molecule Builder" hands its molecule over. Each tool's view is its
// own file's (PeriodicTable.swift, LewisMaker.swift, MoleculeBuilder.swift, AtomsOrbitals.swift). (tools) After them
// the chemistry calculators: the Equation Balancer (EquationBalancer.swift), Molar Mass & Stoichiometry
// (MolarMass.swift), Gas Laws (GasLaws.swift) and Solutions & pH (SolutionsPH.swift), whose arithmetic needs no
// window and is checked by the Models job (Model/ChemFormula.swift, Model/ChemBalance.swift, Model/ChemStoich.swift,
// Model/ChemGas.swift, Model/ChemSolution.swift).

/// What the Tools page, the toolbar and a pin's popover show of a chemistry tool.
struct ChemToolInfo {
    let name: String
    /// For pins and tooltips.
    let shortName: String
    let note: String
    let symbol: String
    let hex: String
}

enum ChemTools {
    /// The tools in the Tools page's order: the Periodic Table's four, then (tools) the calculators.
    static let kinds: [ToolKind] = [.ptable, .lewis, .molecule, .atoms, .balance, .moles, .gas, .solution]

    /// Each tool's name, line, symbol and colour — a palette of their own (red, cyan, green and violet; the
    /// calculators' orange-red and the rest), none of them near a biology tile's colour.
    static func info(_ k: ToolKind) -> ChemToolInfo {
        switch k {
        case .balance: return ChemToolInfo(name: "Equation Balancer", shortName: "Balance", note: "Balance any chemical equation.",
                                           symbol: "equal.square", hex: "#ff6b35")
        case .moles: return ChemToolInfo(name: "Molar Mass & Stoichiometry", shortName: "Moles", note: "Molar mass, moles and yields.",
                                         symbol: "scalemass", hex: "#2fb37a")
        case .gas: return ChemToolInfo(name: "Gas Laws", shortName: "Gas", note: "PV = nRT and the combined law.",
                                       symbol: "wind", hex: "#3f8ef7")
        case .solution: return ChemToolInfo(name: "Solutions & pH", shortName: "pH", note: "Molarity, dilution, pH, titration.",
                                            symbol: "drop.fill", hex: "#a259ff")
        case .ptable: return ChemToolInfo(name: "Periodic Table", shortName: "Elements", note: "Every element, its trends and its facts.",
                                          symbol: "square.grid.3x3", hex: "#e5484d")
        case .lewis: return ChemToolInfo(name: "Lewis Structures", shortName: "Lewis", note: "Bonds, lone pairs, formal charges and resonance.",
                                         symbol: "circle.grid.cross", hex: "#0ea5c6")
        case .molecule: return ChemToolInfo(name: "Molecule Builder", shortName: "Molecules", note: "Build molecules in 3D, shaped by VSEPR.",
                                            symbol: "rotate.3d", hex: "#3faa4b")
        case .atoms: return ChemToolInfo(name: "Atoms & Orbitals", shortName: "Atoms", note: "Any element's shells, orbitals and cloud in 3D.",
                                         symbol: "atom", hex: "#b452e0")
        default: return ChemToolInfo(name: k.rawValue, shortName: k.rawValue, note: "", symbol: "questionmark", hex: "#8e8e93")
        }
    }

    /// The pin popover's width: (tools-34) the Periodic Table's wider, for squares big enough to point at; (tools) the
    /// Equation Balancer's a little, for a whole equation on its line.
    static func popoverWidth(_ k: ToolKind) -> CGFloat {
        switch k {
        case .ptable: return 480
        case .balance: return 400
        case .gas: return 360
        case .solution: return 340
        default: return 330
        }
    }

    /// The tool key a launch's -SimplTool means: the Periodic Table's old page and view flags read as the tools they are
    /// now (ChemLaunch) — -SimplTool ptable -SimplPTPage lewis is Lewis Structures — so the pictures' flags keep working.
    static func launchKey(_ tool: String) -> String {
        let d = UserDefaults.standard
        return ChemLaunch.tool(tool, page: d.string(forKey: "SimplPTPage"), model: d.string(forKey: "SimplPTModel")) ?? tool
    }

    /// The tool the launch opened (the screenshot suite's -SimplTool, read as `launchKey` reads it): each chemistry tool
    /// takes the launch's own flags (-SimplPTElement, -SimplPTMolecule …) only when it is the one the launch opened.
    static let launched: ToolKind? = UserDefaults.standard.string(forKey: "SimplTool").flatMap { ToolKind(rawValue: ChemTools.launchKey($0)) }

    // MARK: The Lewis maker's hand-off

    /// The Lewis maker's molecule on its way to the Molecule Builder (a graph and its electrons, more than a hand-off's
    /// words carry), taken once by the builder as it opens.
    @MainActor private static var lewisHandOff: (graph: LGraph, structure: LStructure)?

    /// The Lewis maker's "Open in Molecule Builder": the molecule handed over, the builder opened in the same popup.
    @MainActor
    static func openInMoleculeBuilder(_ graph: LGraph, _ structure: LStructure) {
        lewisHandOff = (graph, structure)
        ToolsCenter.shared.popUp(.molecule)
    }

    /// The Lewis maker's molecule, if one is on its way, taken once.
    @MainActor
    static func takeLewisHandOff() -> (graph: LGraph, structure: LStructure)? {
        defer { lewisHandOff = nil }
        return lewisHandOff
    }
}

// MARK: - What the tools keep

/// What the chemistry tools keep from one opening to the next, on this Mac only: UserDefaults
/// `SimplChem.<key>.<field>` — the Periodic Table's colouring and the last element opened (`ptable.colour`,
/// `ptable.element`), the Lewis maker's molecule (`lewis.draft`, ChemLewisDraft), the Molecule Builder's and how it is
/// shown (`molecule.saved`, ChemMoleculeDraft; `.style`, `.lonePairs`, `.labels`), Atoms & Orbitals' element and
/// view (`atoms.element`, `atoms.view`), and (tools) the calculators' last inputs (`balance.equation`, `moles.*`, `gas.*`, `solution.*`). A picture's
/// launch (-SimplShotFile) keeps them in a suite of its own that starts
/// empty, as BioPrefs does, so each picture shows its tool as its launch asks and nothing more.
enum ChemPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplChemPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func key(_ k: ToolKind, _ field: String) -> String { "SimplChem.\(k.rawValue).\(field)" }
    static func string(_ k: ToolKind, _ field: String) -> String? { store.string(forKey: key(k, field)) }
    static func int(_ k: ToolKind, _ field: String) -> Int? { string(k, field).flatMap { Int($0) } }

    /// Kept, only when it has changed (nil: forgotten).
    static func set(_ k: ToolKind, _ field: String, _ value: String?) {
        guard store.string(forKey: key(k, field)) != value else { return }
        store.set(value, forKey: key(k, field))
    }

    /// Several fields at once, each kept only when it has changed: a view watches them as one list (a run of ten or
    /// more .onChange modifiers is more than the compiler type-checks in reasonable time).
    static func remember(_ k: ToolKind, _ fields: [String], _ values: [String]) {
        for (field, value) in zip(fields, values) { set(k, field, value) }
    }

    static func bool(_ k: ToolKind, _ field: String, default d: Bool) -> Bool {
        store.object(forKey: key(k, field)) == nil ? d : store.bool(forKey: key(k, field))
    }

    static func set(_ k: ToolKind, _ field: String, _ value: Bool) {
        guard store.object(forKey: key(k, field)) == nil || store.bool(forKey: key(k, field)) != value else { return }
        store.set(value, forKey: key(k, field))
    }

    /// Everything the chemistry tools keep, forgotten (the Developer window's Interface page).
    static func forgetAll() {
        for name in store.dictionaryRepresentation().keys where name.hasPrefix("SimplChem.") {
            store.removeObject(forKey: name)
        }
    }
}

// MARK: - The Tools page's section, the tools' screens, a pin's quick version

/// The Tools page's "Chemistry" section, just before Biology: its heading and the four tiles, headed and laid out as the
/// Biology section is (BioToolsSection); -SimplToolsScroll chemistry scrolls the page to it.
struct ChemToolsSection: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Chemistry")
                    .font(.sTitle3)
                    .accessibilityAddTraits(.isHeader)
                Text("The periodic table, Lewis structures, molecules in 3D, each element's atom and orbitals, and the chemistry calculators.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            LazyVGrid(columns: ToolTile.columns, spacing: 14) {
                ForEach(ChemTools.kinds) { k in ToolTile(kind: k) }
            }
        }
        .id("chemistry")
    }
}

/// Lewis Structures, a tool of its own: the Lewis maker (LewisMaker.swift) as it was on the Periodic Table's page, its
/// "Open in Molecule Builder" handing the molecule to the Molecule Builder in the same popup.
struct LewisStructuresTool: View {
    var body: some View {
        LewisMaker { graph, structure in
            ChemTools.openInMoleculeBuilder(graph, structure)
        }
    }
}

/// The Molecule Builder, a tool of its own (MoleculeBuilder.swift). As it opens: a molecule handed to it — the Lewis
/// maker's, or the Biomolecule Builder's, its shape kept — else the launch's preset (-SimplPTMolecule Ethanol), else the
/// molecule it was left with, else water; and the view as it was left (ball and stick or another, lone pairs, labels).
/// What it shows is kept as it changes.
struct MoleculeBuilderTool: View {
    @StateObject private var model = MoleculeModel(empty: true)
    /// Whether the first molecule is in: nothing is kept before then (the empty builder it starts as).
    @State private var started = false

    var body: some View {
        MoleculeBuilder(model: model)
            .onAppear(perform: start)
            .onChange(of: model.savedText) { _, text in
                if started { ChemPrefs.set(.molecule, "saved", text) }
            }
            .onChange(of: model.style) { _, s in ChemPrefs.set(.molecule, "style", s.rawValue) }
            .onChange(of: model.showLonePairs) { _, on in ChemPrefs.set(.molecule, "lonePairs", on) }
            .onChange(of: model.showLabels) { _, on in ChemPrefs.set(.molecule, "labels", on) }
    }

    private func start() {
        guard !started else { return }
        if let s = ChemPrefs.string(.molecule, "style").flatMap({ MoleculeModel.Style(rawValue: $0) }) { model.style = s }
        model.showLonePairs = ChemPrefs.bool(.molecule, "lonePairs", default: true)
        model.showLabels = ChemPrefs.bool(.molecule, "labels", default: true)
        loadFirst()
        started = true
    }

    /// The first molecule: handed over, the launch's, the one left, else water.
    private func loadFirst() {
        if let h = ChemTools.takeLewisHandOff() {
            model.load(graph: h.graph, structure: h.structure)
            return
        }
        // (1.3.78) a molecule handed over by the Biomolecule Builder, shown as it is
        if let text = ToolsCenter.shared.takeInfo("molecule"), model.load(handOff: text) { return }
        // (the screenshot suite: -SimplPTMolecule Ethanol)
        if ChemTools.launched == .molecule, let preset = UserDefaults.standard.string(forKey: "SimplPTMolecule") {
            model.loadPreset(preset)
            return
        }
        if let saved = ChemPrefs.string(.molecule, "saved"), model.restore(saved) { return }
        model.loadPreset("Water")
    }
}

/// A chemistry tool's quickest use in its pin's popover: the Periodic Table small (PeriodicTableCompact), an element to
/// see in Atoms & Orbitals (AtomsOrbitalsCompact), (tools) an equation balanced (EquationBalancerCompact), a formula's
/// molar mass (MolarMassCompact), the ideal gas law small (GasLawsCompact), a pH from any one of its four
/// (SolutionsPHCompact), and for the Lewis maker and the builder, the tool's tile, its line and Open.
struct ChemQuick: View {
    let kind: ToolKind
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        switch kind {
        case .ptable: PeriodicTableCompact(openFull: openFull)
        case .atoms: AtomsOrbitalsCompact(openFull: openFull)
        case .balance: EquationBalancerCompact(openFull: openFull)
        case .moles: MolarMassCompact(openFull: openFull)
        case .gas: GasLawsCompact(openFull: openFull)
        case .solution: SolutionsPHCompact(openFull: openFull)
        default: BioQuickOpen(kind: kind) { openFull([:]) }
        }
    }
}
