import Foundation

// Photosynthesis & Respiration's own model (spec §4.10, §5.8): what the tool works out without a window — the stages' inputs, outputs and running totals —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `energyChecks` with the others.

extension BioSelfCheck {
    /// Photosynthesis & Respiration's own `bio-check` lines, against its section's test vectors (none yet).
    static func energyChecks(_ r: inout BioCheckReport) {}
}
