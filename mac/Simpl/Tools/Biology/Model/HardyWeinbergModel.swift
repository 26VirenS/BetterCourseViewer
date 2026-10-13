import Foundation

// Hardy–Weinberg's own model (spec §4.7, §5.4): what the tool works out without a window — the calculator, the HWE test and the simulation —
// Foundation only, so the Mac app's workflow (Models, scripts/dev/swift-checks) compiles and checks it with the rest of
// Biology/Model. Its builder fills this in; the self-check (BioSelfCheck.run) calls `popgenChecks` with the others.

extension BioSelfCheck {
    /// Hardy–Weinberg's own `bio-check` lines, against its section's test vectors (none yet).
    static func popgenChecks(_ r: inout BioCheckReport) {}
}
