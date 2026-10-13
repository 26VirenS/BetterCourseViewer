import SwiftUI

// Cell Explorer (spec §4.1): animal, plant and bacterial cells in 3D, a patch of membrane up close, and the three
// compared. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header.
// Its pages are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as
// `state` (-SimplTool cell -SimplToolState <state>; see docs/mac-biology.md).

struct CellExplorerTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.cell)`.
    static let pages: [BioPage] = [
        BioPage(id: "animal", title: "Animal", symbol: "pawprint", help: "An animal cell"),
        BioPage(id: "plant", title: "Plant", symbol: "leaf", help: "A plant cell"),
        BioPage(id: "bacterium", title: "Bacterium", symbol: "capsule", help: "A bacterial cell (E. coli)"),
        BioPage(id: "membrane", title: "Membrane", symbol: "line.3.horizontal", help: "The plasma membrane up close"),
        BioPage(id: "compare", title: "Compare", symbol: "square.split.2x1", help: "Animal, plant and bacterial cells side by side"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .cell, state: state)
    }
}
