import SwiftUI

// Genetics Crosses (spec §4.5): Punnett squares with every gamete, probability by the product rule, and chi-square.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. Its pages
// are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as `state`
// (-SimplTool cross -SimplToolState <state>; see docs/mac-biology.md).

struct CrossesTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.cross)`.
    static let pages: [BioPage] = [
        BioPage(id: "cross", title: "Cross", symbol: "square.grid.2x2", help: "A cross and its Punnett square"),
        BioPage(id: "probability", title: "Probability", symbol: "percent", help: "The product and sum rules"),
        BioPage(id: "chi", title: "Chi-square", symbol: "chart.bar", help: "Observed against expected"),
        BioPage(id: "pedigree", title: "Pedigree", symbol: "person.2", help: "Which inheritance a pedigree fits"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .cross, state: state)
    }
}

/// Genetics Crosses in its pin's popover (spec §3.2 item 3): until it is built, its tile, its line and Open. `openFull` opens
/// the whole tool with what was begun here (["sequence": …] and so on).
struct CrossesCompact: View {
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        BioQuickOpen(kind: .cross) { openFull([:]) }
    }
}
