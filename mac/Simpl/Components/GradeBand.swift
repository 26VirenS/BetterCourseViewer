import AppKit
import SwiftUI

/// (1.3.78) A grade's colour by its band — the letter's first letter, from the figure shown (the engine's `band`) — so a
/// ring and a letter say how it went before they are read: A green, B teal, C yellow (darker in light, to be read on
/// white), D orange, F red. A grade with no band (pass/fail, complete) takes the colour it is given. The Grades screen's
/// letter chips use the same five colours.
enum GradeBand {
    static func color(_ band: String?) -> Color? {
        switch (band ?? "").uppercased().prefix(1) {
        case "A": return .green
        case "B": return .teal
        case "C": return yellow
        case "D": return .orange
        case "E", "F": return .red
        default: return nil
        }
    }

    private static let yellow = Theme.dynamic(light: NSColor(srgbRed: 0.72, green: 0.53, blue: 0, alpha: 1), dark: .systemYellow)
}
