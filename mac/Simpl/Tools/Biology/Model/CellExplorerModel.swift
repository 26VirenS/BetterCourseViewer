import Foundation

// Cell Explorer's own model (spec §4.1, §5.1): what the tool works out without a window — the cells' recipes and placement, the organelles' facts —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `cellChecks` with the others.

extension BioSelfCheck {
    /// Cell Explorer's own `bio-check` lines, against its section's test vectors (none yet).
    static func cellChecks(_ r: inout BioCheckReport) {}
}
