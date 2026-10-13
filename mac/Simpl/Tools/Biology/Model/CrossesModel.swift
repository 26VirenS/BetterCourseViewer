import Foundation

// Genetics Crosses's own model (spec §4.5): what the tool works out without a window — the pedigree mode checker and anything the Cross page adds to BioCross —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `crossChecks` with the others.

extension BioSelfCheck {
    /// Genetics Crosses's own `bio-check` lines, against its section's test vectors (none yet).
    static func crossChecks(_ r: inout BioCheckReport) {}
}
