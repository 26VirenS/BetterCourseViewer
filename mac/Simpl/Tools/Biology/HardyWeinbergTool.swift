import SwiftUI

// Hardy–Weinberg (spec §4.7): allele and genotype frequencies worked out, tested and simulated. Until its builder
// replaces this file, a placeholder: its name, its line and the page chosen in its header. Its pages are listed here
// (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as `state` (-SimplTool popgen
// -SimplToolState <state>; see docs/mac-biology.md).

struct HardyWeinbergTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.popgen)`.
    static let pages: [BioPage] = [
        BioPage(id: "calculate", title: "Calculate", symbol: "function", help: "Everything from one value"),
        BioPage(id: "test", title: "Test", symbol: "checkmark.circle", help: "Is a population in equilibrium?"),
        BioPage(id: "simulate", title: "Simulate", symbol: "chart.xyaxis.line", help: "Selection, drift, mutation and migration over generations"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .popgen, state: state)
    }
}

/// Hardy–Weinberg in its pin's popover (spec §3.2 item 3): until it is built, its tile, its line and Open. `openFull` opens
/// the whole tool with what was begun here (["sequence": …] and so on).
struct HardyWeinbergCompact: View {
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        BioQuickOpen(kind: .popgen) { openFull([:]) }
    }
}
