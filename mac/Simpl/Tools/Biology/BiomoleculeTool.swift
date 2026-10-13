import SwiftUI

// Biomolecule Builder (spec §4.4): amino acids and proteins, nucleic acids, sugars and lipids, built and seen in 3D.
// Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. Its pages
// are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as `state`
// (-SimplTool biomol -SimplToolState <state>; see docs/mac-biology.md).

struct BiomoleculeTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.biomol)`.
    static let pages: [BioPage] = [
        BioPage(id: "amino", title: "Amino acids", symbol: "a.circle", help: "The 20 amino acids"),
        BioPage(id: "proteins", title: "Proteins", symbol: "scribble", help: "Build a peptide; the four levels of structure"),
        BioPage(id: "nucleic", title: "Nucleic acids", symbol: "link", help: "Nucleotides joined into a strand"),
        BioPage(id: "carbs", title: "Carbohydrates", symbol: "hexagon", help: "Sugars and the glycosidic bond"),
        BioPage(id: "lipids", title: "Lipids", symbol: "drop", help: "Fatty acids, fats, phospholipids and cholesterol"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .biomol, state: state)
    }
}
