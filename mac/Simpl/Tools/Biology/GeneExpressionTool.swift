import SwiftUI

// Gene Expression (spec §4.3): transcription and translation step by step, the codon table, reading frames and
// mutations. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header.
// Its pages are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as
// `state` (-SimplTool gene -SimplToolState <state>; see docs/mac-biology.md).

struct GeneExpressionTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.gene)`.
    static let pages: [BioPage] = [
        BioPage(id: "express", title: "Transcribe & translate", symbol: "play.rectangle", help: "A gene made into a protein, step by step"),
        BioPage(id: "codons", title: "Codon table", symbol: "tablecells", help: "The genetic code"),
        BioPage(id: "frames", title: "Reading frames", symbol: "text.justify.left", help: "Six frames and the open reading frames"),
        BioPage(id: "mutations", title: "Mutations", symbol: "arrow.triangle.swap", help: "What a change in the DNA does to the protein"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .gene, state: state)
    }
}

/// Gene Expression in its pin's popover (spec §3.2 item 3): until it is built, its tile, its line and Open. `openFull` opens
/// the whole tool with what was begun here (["sequence": …] and so on).
struct GeneExpressionCompact: View {
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        BioQuickOpen(kind: .gene) { openFull([:]) }
    }
}
