import Foundation

// Enzyme Lab's own model (spec §4.9, §5.7): what the tool works out without a window — the rate models —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `enzymeChecks` with the others.

extension BioSelfCheck {
    /// Enzyme Lab's own `bio-check` lines, against its section's test vectors (none yet).
    static func enzymeChecks(_ r: inout BioCheckReport) {}
}
