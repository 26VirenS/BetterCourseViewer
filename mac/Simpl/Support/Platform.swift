import AppKit
import SwiftUI

// What the code the Mac app shares with the iPhone app (ios/SimplCourses: the web layer, the models, the router, the
// quiz's run, reminders) expects to find in the app around it, in the Mac's own terms.

/// The iPhone app's taps, by name (the shared code and the page ask for them: light, medium, rigid, soft, select,
/// success, warning, error). A Mac gives no feel under a click — the trackpad's is for a thing snapping into place
/// while it is dragged — so on a Mac these are silent, and what they mark is said by the screen itself.
enum Haptics {
    static func play(_ kind: String) {}
    static func select() {}
    static func tap() {}
    static func success() {}
    static func error() {}
}

extension Color {
    /// "#34c759", "34c759", "#3c9" → the colour; anything else → gray.
    init(hex: String?) {
        var s = (hex ?? "").trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else {
            self = Color(nsColor: .systemGray)
            return
        }
        self = Color(red: Double((v >> 16) & 0xff) / 255, green: Double((v >> 8) & 0xff) / 255, blue: Double(v & 0xff) / 255)
    }
}

/// A file picked to hand in (an assignment's Hand In, a quiz's file question).
struct PickedFile: Identifiable, Hashable {
    let id = UUID()
    let name: String
    let type: String
    let data: Data

    var sizeText: String { ByteCountFormatter.string(fromByteCount: Int64(data.count), countStyle: .file) }
}

/// An external tool to open (an assignment's, a New Quizzes quiz's, a module item's, a course's own, a launch URL), or —
/// with `page` — a page of Canvas's own the app has no screen for. On a Mac each opens in a window of its own.
struct ToolLaunch: Identifiable, Hashable, Codable {
    var id = UUID()
    let title: String
    let args: [String: String]

    static func assignment(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "assignment": id])
    }

    static func moduleItem(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "moduleItem": id])
    }

    static func courseTool(course: String, id: String, title: String) -> ToolLaunch {
        ToolLaunch(title: title, args: ["course": course, "tool": id])
    }
}

/// The screenshot suite's way into a place or a sheet (-SimplOpen settings, -SimplPlace todo): each taken once a launch.
enum LaunchOpen {
    private static var used: Set<String> = []

    static func take(_ prefix: String, key: String = "SimplOpen") -> String? {
        guard !used.contains(key), let v = UserDefaults.standard.string(forKey: key), v.hasPrefix(prefix) else { return nil }
        used.insert(key)
        return String(v.dropFirst(prefix.count))
    }
}

/// The app's colours: Simpl's grouped look (a quiet page, cards lifted off it) in the Mac's light and dark.
enum Theme {
    static func dynamic(light: NSColor, dark: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
        })
    }

    private static func rgb(_ r: Double, _ g: Double, _ b: Double, _ a: Double = 1) -> NSColor {
        NSColor(srgbRed: r / 255, green: g / 255, blue: b / 255, alpha: a)
    }

    /// The page under the cards.
    static let page = dynamic(light: rgb(245, 245, 247), dark: rgb(22, 22, 24))
    /// A card on the page.
    static let card = dynamic(light: rgb(255, 255, 255), dark: rgb(36, 36, 38))
    /// A card under the pointer.
    static let cardHover = dynamic(light: rgb(250, 250, 252), dark: rgb(44, 44, 47))
    /// A well inside a card (a fact, a field): a little darker than the card in light, lighter in dark.
    static let well = dynamic(light: rgb(118, 118, 128, 0.08), dark: rgb(118, 118, 128, 0.2))
    /// A card's edge: a hairline, stronger in dark where the shadow says less.
    static let edge = dynamic(light: rgb(0, 0, 0, 0.06), dark: rgb(255, 255, 255, 0.08))
    /// The shadow under a card.
    static let shadow = dynamic(light: rgb(0, 0, 0, 0.07), dark: rgb(0, 0, 0, 0.35))
}

/// The house springs (Apple's two numbers: how quickly, and how much it settles past). Critically damped for anything
/// that simply arrives; a little bounce only where a gesture threw it.
enum Motion {
    /// A control answering a press, a row ticked, a chip changing.
    static let snappy = Animation.spring(response: 0.3, dampingFraction: 0.86)
    /// A card or a panel arriving, a list changing.
    static let gentle = Animation.spring(response: 0.42, dampingFraction: 0.92)
    /// A hover lifting a card: quick and without overshoot.
    static let hover = Animation.spring(response: 0.24, dampingFraction: 1)
    /// What fills (a ring, a bar): slower, and settling softly.
    static let fill = Animation.spring(response: 0.8, dampingFraction: 0.9)
}
