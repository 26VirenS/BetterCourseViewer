import AppKit
import QuartzCore
import SwiftUI

/// (1.3.78) How long a screen takes to show what it is for, from the press that opened it: one line on the console for
/// each screen opened — `[Simpl] open Grades: 14 ms (cached)` — so a slow screen is seen, and so is a change that makes
/// one slower. The time runs from the navigation (a sidebar row, a link, Back; the engine starting, for the first
/// screen) to the frame that has the screen's content, and the word after it says where that content came from:
/// `cached` (this session's answer, held in memory), `kept` (an answer kept from before, read at the open), `fetched`
/// (asked of the page while the screen waited) or `alive` (the screen was never put away).
@MainActor
enum OpenClock {
    private static var mark = CACurrentMediaTime()
    /// How many screens have shown their content so far (the walk waits on it).
    private(set) static var shown = 0

    /// A navigation began: the next screen to show its content is timed from here.
    static func navigated() {
        mark = CACurrentMediaTime()
    }

    /// The calls' own times on the console too (a screen's call, a read ahead): in the screenshot suite and in developer
    /// mode.
    static var verbose: Bool { shooting || DevMode.shared.unlocked }
    private static let shooting = UserDefaults.standard.string(forKey: "SimplShotFile") != nil

    static func report(_ screen: String, _ source: String) {
        let ms = Int(((CACurrentMediaTime() - mark) * 1000).rounded())
        NSLog("[Simpl] open %@: %d ms (%@)", screen, ms, source)
        shown += 1
    }
}

private struct OpenTimed: ViewModifier {
    let screen: String
    let ready: Bool
    let live: Bool
    let source: () -> String
    /// What this appearance has said so far: nothing, its content (and where it came from), its live content.
    @State private var said: String?
    /// It is on screen (SwiftUI may say a screen appeared twice: it is timed once).
    @State private var visible = false
    /// It has appeared before: shown again, it was never put away (a screen uncovered by Back).
    @State private var seen = false

    func body(content: Content) -> some View {
        content
            .preference(key: PageReadyKey.self, value: ready) // (a page arriving waits for its content: PageArrival)
            .onAppear {
                guard !visible else { return }
                visible = true
                said = nil
                if ready { report(seen ? "alive" : source()) }
                seen = true
            }
            .onDisappear { visible = false }
            .onChange(of: ready) { _, now in
                if now && said == nil { report(source()) }
            }
            .onChange(of: live) { _, now in
                // (content kept from before, its counts held back: the live answer is a second line)
                if now && said == "kept" { report("live") }
            }
    }

    private func report(_ from: String) {
        said = from
        // (on the run loop's next turn: once the frame with the content has been committed)
        DispatchQueue.main.async { OpenClock.report(screen, from) }
    }
}

extension View {
    /// (1.3.78) This screen's open, timed on the console (OpenClock): `ready` once its content is there, `source` where
    /// that content came from; content kept from before (`kept`) is timed again once it is `live`.
    func openTimed(_ screen: String, ready: Bool, live: Bool? = nil, source: @escaping @autoclosure () -> String) -> some View {
        modifier(OpenTimed(screen: screen, ready: ready, live: live ?? ready, source: source))
    }
}

/// (1.3.78) The screenshot suite's walk through the app (`-SimplWalk "todo,grades,dashboard,todo"`): once the first
/// screen is up and a few seconds more have passed (`-SimplWalkWait`, 5 by default — a student reading it), each step
/// in turn, as a student's clicks would make it, the next once the one before has shown its content (15 seconds at
/// most) and stood for a moment. A step is a place as `-SimplPlace` names it, `back` (the toolbar's Back) or
/// `push:<address>` (a link to it). The console then has each screen's first open and its opens again; the picture waits
/// for the walk's end.
@MainActor
enum Walk {
    private static var started = false

    static func startIfAsked(_ engine: Engine) {
        guard !started, let raw = UserDefaults.standard.string(forKey: "SimplWalk"), !raw.isEmpty else { return }
        started = true
        let steps = raw.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        let d = UserDefaults.standard
        let pause = d.object(forKey: "SimplWalkWait") == nil ? 5 : d.double(forKey: "SimplWalkWait")
        Shot.began() // (the picture is taken once the walk is over)
        Task { @MainActor in
            await until(20) { OpenClock.shown > 0 }
            try? await Task.sleep(nanoseconds: UInt64(pause * 1_000_000_000))
            for step in steps {
                let before = OpenClock.shown
                NSLog("[Simpl] walk: %@", step)
                if step == "back" {
                    engine.goBack()
                } else if step.hasPrefix("push:") {
                    engine.openWeb(String(step.dropFirst(5)), title: "")
                } else if let place = Engine.place(named: step) {
                    engine.go(place)
                } else {
                    continue
                }
                // (Back within a place uncovers a screen that was never put away: it may say nothing of its open)
                await until(step == "back" ? 3 : 15) { OpenClock.shown > before }
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
            NSLog("[Simpl] walk: done")
            Shot.ended()
        }
    }

    private static func until(_ seconds: Double, _ done: () -> Bool) async {
        let start = Date()
        while !done() && Date().timeIntervalSince(start) < seconds {
            try? await Task.sleep(nanoseconds: 40_000_000)
        }
    }
}
