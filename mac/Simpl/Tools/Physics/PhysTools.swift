import AppKit
import SwiftUI

// (tools) The physics tools' place among the tools, as the chemistry tools' is (Chemistry/ChemTools.swift): a
// "Physics" heading of their own on the Tools page, after Chemistry and before Biology — Kinematics & Projectiles
// (motion in a line from any three of its five quantities, and a projectile's flight drawn), Vectors (up to six added,
// dotted, crossed and drawn) and the Circuit Builder (a DC circuit built on a grid and solved as it changes). Each has
// its own tile, pin, popup and heading, keeps what it was left at (PhysPrefs), and does its arithmetic in
// Physics/Model, Foundation only, where the Models job checks it against the specification's test vectors. Each tool's
// view is its own file's (Kinematics.swift, VectorsTool.swift, CircuitTool.swift).

/// What the Tools page, the toolbar and a pin's popover show of a physics tool.
struct PhysToolInfo {
    let name: String
    /// For pins and tooltips.
    let shortName: String
    let note: String
    let symbol: String
    let hex: String
}

enum PhysTools {
    /// The tools, in the Tools page's order.
    static let kinds: [ToolKind] = [.motion, .vectors, .circuit]

    /// Each tool's name, line, symbol and colour — a family of blues, none of them a chemistry or biology tile's.
    static func info(_ k: ToolKind) -> PhysToolInfo {
        switch k {
        case .motion: return PhysToolInfo(name: "Kinematics & Projectiles", shortName: "Motion", note: "1D motion and projectile paths.",
                                          symbol: "figure.run", hex: "#0a84ff")
        case .vectors: return PhysToolInfo(name: "Vectors", shortName: "Vectors", note: "Add, dot, cross, components.",
                                           symbol: "arrow.up.right", hex: "#5ac8fa")
        case .circuit: return PhysToolInfo(name: "Circuit Builder", shortName: "Circuit", note: "Build a DC circuit, read V, I, R.",
                                           symbol: "bolt.circle", hex: "#ffd60a")
        default: return PhysToolInfo(name: k.rawValue, shortName: k.rawValue, note: "", symbol: "questionmark", hex: "#8e8e93")
        }
    }

    /// The pin popover's width: a little wider than most, for two numbers side by side and the answer under them;
    /// Vectors' for two vectors' four, the Circuit Builder's for Ohm's law's three.
    static func popoverWidth(_ k: ToolKind) -> CGFloat { k == .motion ? 360 : 380 }

    /// The tool the launch opened (the screenshot suite's -SimplTool): each physics tool takes the launch's own flags
    /// (-SimplMotion, -SimplMotionValues …) only when it is the one the launch opened.
    static let launched: ToolKind? = UserDefaults.standard.string(forKey: "SimplTool").flatMap { ToolKind(rawValue: ChemTools.launchKey($0)) }
}

extension ToolKind {
    /// One of the physics tools (they have a section of their own on the Tools page).
    var isPhysics: Bool { PhysTools.kinds.contains(self) }
}

// MARK: - What the tools keep

/// What the physics tools keep from one opening to the next, on this Mac only: UserDefaults `SimplPhys.<key>.<field>` —
/// Kinematics & Projectiles' page and every field on it (`motion.tab`, `motion.x` … `motion.target`, `motion.imperial`),
/// Vectors' rows and settings (`vectors.rows`, `.threeD`, `.unit`, `.tipToTail`), the Circuit Builder's circuit as JSON
/// and its strip (`circuit.doc`, `.ohmV`, `.ohmI`, `.ohmR`, `.list`).
/// A picture's launch (-SimplShotFile) keeps them in a suite of its own that starts empty, as ChemPrefs and BioPrefs do,
/// so each picture shows its tool as its launch asks and nothing more.
enum PhysPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplPhysPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func key(_ k: ToolKind, _ field: String) -> String { "SimplPhys.\(k.rawValue).\(field)" }
    static func string(_ k: ToolKind, _ field: String) -> String? { store.string(forKey: key(k, field)) }

    /// Kept, only when it has changed (nil or empty: forgotten).
    static func set(_ k: ToolKind, _ field: String, _ value: String?) {
        let v = (value?.isEmpty ?? true) ? nil : value
        guard store.string(forKey: key(k, field)) != v else { return }
        store.set(v, forKey: key(k, field))
    }

    static func bool(_ k: ToolKind, _ field: String, default d: Bool) -> Bool {
        store.object(forKey: key(k, field)) == nil ? d : store.bool(forKey: key(k, field))
    }

    static func set(_ k: ToolKind, _ field: String, _ value: Bool) {
        guard store.object(forKey: key(k, field)) == nil || store.bool(forKey: key(k, field)) != value else { return }
        store.set(value, forKey: key(k, field))
    }

    /// Everything the physics tools keep, forgotten (the Developer window's Interface page).
    static func forgetAll() {
        for name in store.dictionaryRepresentation().keys where name.hasPrefix("SimplPhys.") {
            store.removeObject(forKey: name)
        }
    }
}

// MARK: - The Tools page's section, a pin's quick version

/// The Tools page's "Physics" section, after Chemistry and before Biology: its heading and its tiles, headed and laid
/// out as the Chemistry section is (ChemToolsSection); -SimplToolsScroll physics scrolls the page to it.
struct PhysToolsSection: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Physics")
                    .font(.sTitle3)
                    .accessibilityAddTraits(.isHeader)
                Text("Motion and projectiles, vectors, and DC circuits you build — solved and drawn.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            LazyVGrid(columns: ToolTile.columns, spacing: 14) {
                ForEach(PhysTools.kinds) { k in ToolTile(kind: k) }
            }
        }
        .id("physics")
    }
}

/// A physics tool's quickest use in its pin's popover: a projectile's speed and angle to its range and height
/// (MotionCompact), two vectors to their resultant (VectorsCompact), Ohm's law and a series/parallel list
/// (CircuitCompact); for any other, the tool's tile, its line and Open.
struct PhysQuick: View {
    let kind: ToolKind
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        switch kind {
        case .motion: MotionCompact(openFull: openFull)
        case .vectors: VectorsCompact(openFull: openFull)
        case .circuit: CircuitCompact(openFull: openFull)
        default: BioQuickOpen(kind: kind) { openFull([:]) }
        }
    }
}

/// A row of a results card: its name left, its value right, as the biology tools' facts rows are.
struct PhysValueRow: View {
    let label: String
    let value: String
    var color: Color = .primary
    var last = false

    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(label).font(.sCallout).foregroundStyle(.secondary)
                Spacer(minLength: 8)
                Text(value)
                    .font(.sCallout.weight(.semibold).monospacedDigit())
                    .foregroundStyle(color)
                    .multilineTextAlignment(.trailing)
                    .textSelection(.enabled)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 9)
            if !last { Divider().padding(.leading, 14) }
        }
        .accessibilityElement(children: .combine)
    }
}

/// A small heading inside a tool's page ("1D motion", "Results"): small capitals, as the side columns' are.
struct PhysHeading: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: 11.5, weight: .semibold))
            .tracking(0.6)
            .foregroundStyle(.secondary)
            .accessibilityAddTraits(.isHeader)
    }
}

/// A one-line field of a tool with its label and unit beside it, for a grid of numbers (the five of a motion, a
/// projectile's four): narrower than ToolField, whose label stands over it.
struct PhysField: View {
    let label: String
    @Binding var text: String
    var unit: String = ""
    var placeholder: String = ""
    var help: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            HStack(spacing: 6) {
                TextField(placeholder, text: $text)
                    .textFieldStyle(.roundedBorder)
                    .font(.sBody.monospacedDigit())
                    .controlSize(.large)
                if !unit.isEmpty { Text(unit).font(.sCallout).foregroundStyle(.secondary).fixedSize() }
            }
        }
        .help(help ?? label)
    }
}
