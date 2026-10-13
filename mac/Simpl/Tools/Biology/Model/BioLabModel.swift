import Foundation

// Bio Lab Calculator's own model (spec §4.8, §5.5): what the tool works out without a window — the calculators' formulas —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `biolabChecks` with the others.

extension BioSelfCheck {
    /// Bio Lab Calculator's own `bio-check` lines, against its section's test vectors (none yet).
    static func biolabChecks(_ r: inout BioCheckReport) {}
}
