import SwiftUI

// Enzyme Lab (spec §4.9): rate against substrate, temperature and pH, with inhibitors. Until its builder replaces this
// file, a placeholder: its name, its line and the page chosen in its header. Its pages are listed here (`pages`, shown
// in the header by BioHeaderControls), and the launch's state reaches it as `state` (-SimplTool enzyme -SimplToolState
// <state>; see docs/mac-biology.md).

struct EnzymeLabTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// One page: none in its header.
    static let pages: [BioPage] = []

    var body: some View {
        BioToolPlaceholder(kind: .enzyme, state: state)
    }
}
