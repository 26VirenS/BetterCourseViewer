import SwiftUI

// DNA & RNA (spec §4.2): the double helix, nucleotides, a sequence's facts, replication and RNA. Until its builder
// replaces this file, a placeholder: its name, its line and the page chosen in its header. Its pages are listed here
// (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as `state` (-SimplTool dna
// -SimplToolState <state>; see docs/mac-biology.md).

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

    var body: some View {
        BioToolPlaceholder(kind: .dna, state: state)
    }
}
