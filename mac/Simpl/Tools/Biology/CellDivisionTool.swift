import SwiftUI

// Cell Division (spec §4.6): mitosis and meiosis stage by stage, with the counts at every step, and the cell cycle.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. Its pages
// are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as `state`
// (-SimplTool divide -SimplToolState <state>; see docs/mac-biology.md).

struct CellDivisionTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.divide)`.
    static let pages: [BioPage] = [
        BioPage(id: "mitosis", title: "Mitosis", symbol: "circle.grid.2x1", help: "Mitosis, stage by stage"),
        BioPage(id: "meiosis", title: "Meiosis", symbol: "circle.grid.2x2", help: "Meiosis I and II, stage by stage"),
        BioPage(id: "compare", title: "Compare", symbol: "square.split.2x1", help: "Mitosis and meiosis side by side"),
        BioPage(id: "cycle", title: "Cell cycle", symbol: "arrow.triangle.2.circlepath", help: "The cell cycle and its checkpoints"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .divide, state: state)
    }
}
