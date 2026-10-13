import Foundation

// Biomolecule Builder's own model (spec §4.4, §6.4): what the tool works out without a window — the peptide builder (NeRF, side chains, clash relief), the strand and sugar builders —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `biomolChecks` with the others.

extension BioSelfCheck {
    /// Biomolecule Builder's own `bio-check` lines, against its section's test vectors (none yet).
    static func biomolChecks(_ r: inout BioCheckReport) {}
}
