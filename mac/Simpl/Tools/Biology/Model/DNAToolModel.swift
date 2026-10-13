import Foundation

// DNA & RNA's own model (spec §4.2): what the tool works out without a window — the double helix's cartoon, the tRNA cloverleaf's layout, replication's steps —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `dnaChecks` with the others.

extension BioSelfCheck {
    /// DNA & RNA's own `bio-check` lines, against its section's test vectors (none yet).
    static func dnaChecks(_ r: inout BioCheckReport) {}
}
