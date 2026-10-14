import Foundation

// The step-through's model (spec §3.6): a process as an ordered list of steps — each a short title, a one-to-three
// sentence explanation, an optional stage group (number keys jump to groups) and what to notice — and the timing every
// process tool shares: 600 ms between keyframes at 1× (ease in-out; back plays it in reverse), 2.4 s on a step while
// playing, 0.5×, 1× and 2× speeds, and a 200 ms crossfade instead of motion under Reduce Motion. The controller and its
// controls (BioStepThrough.swift) drive a tool's drawing through `BioStepPhase`.

/// A step of a process.
struct BioStep: Identifiable, Equatable {
    var id: Int { index }
    let index: Int
    /// At most 40 characters ("Elongation").
    let title: String
    /// One to three sentences, naming the molecule at work.
    let text: String
    /// The stage group the number keys jump to (1 = Interphase … 6 = Cytokinesis), if the tool has groups.
    var group: Int?
    /// What to notice (the tool lights these).
    var notice: [String] = []

    init(_ index: Int, _ title: String, _ text: String, group: Int? = nil, notice: [String] = []) {
        self.index = index
        self.title = title
        self.text = text
        self.group = group
        self.notice = notice
    }
}

/// Where a step-through is between two keyframes: from step `from` to step `to`, `t` of the way (0…1, eased). A tool
/// draws step `from` moved `t` toward step `to` — or, with `crossfade`, both, faded (Reduce Motion).
struct BioStepPhase: Equatable {
    let from: Int
    let to: Int
    let t: Double
    let crossfade: Bool

    /// At rest on a step.
    static func at(_ i: Int) -> BioStepPhase { BioStepPhase(from: i, to: i, t: 1, crossfade: false) }

    var isMoving: Bool { from != to && t < 1 }
    /// The step it is nearest.
    var current: Int { t < 0.5 ? from : to }

    /// A value between two keyframes' values.
    func mix(_ a: Double, _ b: Double) -> Double { a + (b - a) * t }
}

/// The timing the step-throughs share.
enum BioStepTiming {
    /// Between keyframes at 1×.
    static let move = 0.6
    /// On a step while playing, at 1×, before the next.
    static let dwell = 2.4
    /// Reduce Motion's crossfade.
    static let crossfade = 0.2
    static let speeds: [Double] = [0.5, 1, 2]

    /// Ease in-out (cubic), 0…1 → 0…1.
    static func ease(_ t: Double) -> Double {
        let x = min(max(t, 0), 1)
        return x < 0.5 ? 4 * x * x * x : 1 - pow(-2 * x + 2, 3) / 2
    }

    /// The step a group starts at (the first step in it), nil if no step is in it.
    static func start(ofGroup g: Int, in steps: [BioStep]) -> Int? { steps.first { $0.group == g }?.index }

    /// "Step 4 of 12 · Elongation".
    static func caption(_ i: Int, of steps: [BioStep]) -> String {
        guard steps.indices.contains(i) else { return "" }
        return "Step \(i + 1) of \(steps.count) · \(steps[i].title)"
    }
}
