import Foundation

// Gene Expression's own model (spec §4.3): what the tool works out without a window — the transcription and translation steps, the mutation classes —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `geneChecks` with the others.

extension BioSelfCheck {
    /// Gene Expression's own `bio-check` lines, against its section's test vectors (none yet).
    static func geneChecks(_ r: inout BioCheckReport) {}
}
