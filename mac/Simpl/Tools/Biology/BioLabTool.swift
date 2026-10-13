import SwiftUI

// Bio Lab Calculator (spec §4.8): solutions, microscopy, cells, statistics, populations and molecules, worked as on
// paper. Until its builder replaces this file, a placeholder: its name, its line and the page chosen in its header. Its
// pages are listed here (`pages`, shown in the header by BioHeaderControls), and the launch's state reaches it as
// `state` (-SimplTool biolab -SimplToolState <state>; see docs/mac-biology.md).

struct BioLabTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.biolab)`.
    static let pages: [BioPage] = [
        BioPage(id: "solutions", title: "Solutions", symbol: "drop", help: "Molarity, dilutions and percent solutions"),
        BioPage(id: "microscopy", title: "Microscopy", symbol: "magnifyingglass", help: "Magnification, field of view and size"),
        BioPage(id: "cells", title: "Cells", symbol: "circle.dashed", help: "Surface area to volume, water potential, cell counts"),
        BioPage(id: "stats", title: "Statistics", symbol: "chart.bar", help: "Mean, standard deviation, error bars, chi-square"),
        BioPage(id: "ecology", title: "Populations", symbol: "leaf", help: "Growth, rate and diversity"),
        BioPage(id: "molecules", title: "Molecules", symbol: "atom", help: "Absorbance, PCR, centrifuges, pH and Q₁₀"),
    ]

    var body: some View {
        BioToolPlaceholder(kind: .biolab, state: state)
    }
}

/// Bio Lab Calculator in its pin's popover (spec §3.2 item 3): until it is built, its tile, its line and Open. `openFull` opens
/// the whole tool with what was begun here (["sequence": …] and so on).
struct BioLabCompact: View {
    let openFull: (_ info: [String: String]) -> Void

    var body: some View {
        BioQuickOpen(kind: .biolab) { openFull([:]) }
    }
}
