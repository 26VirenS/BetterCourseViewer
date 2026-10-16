import AppKit
import QuartzCore
import SwiftUI
import WebKit

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

    /// The screen is on screen, its content or not (in the screenshot suite and developer mode: how much of an open is
    /// the window making the screen, and how much its content).
    static func appeared(_ screen: String) {
        guard verbose else { return }
        NSLog("[Simpl] appear %@: %d ms", screen, Int(((CACurrentMediaTime() - mark) * 1000).rounded()))
    }

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
                OpenClock.appeared(screen)
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
/// `push:<address>` (a link to it); (1.3.102) or `on:<address>`, its screen pushed on the place showing as a piece of
/// work's own action pushes it (Hand In, See Feedback: the Dashboard stays the place), and `again`, the sidebar's row of
/// the place showing pressed again (`Engine.rowPressed`), each said after as "walk: at <place>, <n> pushed"; (1.3.103) or
/// `filelink`, the first link to a school file in the screen's text clicked (`clickFileLink`). The console then has each
/// screen's first open and its opens again; the picture waits for the walk's end.
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
                if step == "filelink" {
                    await clickFileLink(engine)
                    continue
                }
                if step == "back" {
                    engine.goBack()
                } else if step.hasPrefix("push:") {
                    engine.openWeb(String(step.dropFirst(5)), title: "")
                } else if step.hasPrefix("on:"), let route = engine.nativeRoute(for: String(step.dropFirst(3)), title: "") {
                    engine.push(route)
                } else if step == "again" {
                    engine.rowPressed(engine.nav.current.place)
                } else if let place = Engine.place(named: step) {
                    engine.go(place)
                } else {
                    continue
                }
                // (Back within a place uncovers a screen that was never put away: it may say nothing of its open, and
                // nor may the place's root that its row pressed again uncovers)
                await until(step == "back" || step == "again" ? 3 : 15) { OpenClock.shown > before }
                try? await Task.sleep(nanoseconds: 1_000_000_000)
                if step == "again" || step.hasPrefix("on:") {
                    NSLog("[Simpl] walk: at %@, %ld pushed", Engine.name(of: engine.nav.current.place), engine.nav.current.path.count)
                }
            }
            NSLog("[Simpl] walk: done")
            Shot.ended()
        }
    }

    /// (1.3.103) `filelink`: the first link to a school file in the text on screen (an assignment's instructions, which
    /// Canvas writes as …/courses/1/files/2?wrap=1) clicked as a student clicks it — its web view given the keys, the link
    /// pressed in the page, so the app hears it as it hears a click — and Simpl kept in front (the suite's other copies
    /// take the front now and then) until Quick Look has been checked, 20 seconds at most: said as "walk: file link
    /// <address>, Quick Look checked" (or "not checked"). FilePreview says what the panel held ("[files] Quick Look: …"),
    /// which mac-shots.sh's quicklook_check reads.
    private static func clickFileLink(_ engine: Engine) async {
        let before = FilePreview.looked
        let find = "(function(){var a=document.querySelector('a[href*=\"/files/\"]');return a?a.href:'';})()"
        let press = "(function(){var a=document.querySelector('a[href*=\"/files/\"]');if(!a)return '';a.click();return a.href;})()"
        var clicked = ""
        let start = Date()
        while clicked.isEmpty && Date().timeIntervalSince(start) < 10 {
            for v in richTexts(in: engine.hostView.window?.contentView) {
                guard await run(find, in: v) != "" else { continue }
                NSApp.activate(ignoringOtherApps: true)
                v.window?.makeKeyAndOrderFront(nil)
                v.window?.makeFirstResponder(v)
                clicked = await run(press, in: v)
                break
            }
            if clicked.isEmpty { try? await Task.sleep(nanoseconds: 500_000_000) }
        }
        let asked = Date()
        while !clicked.isEmpty && FilePreview.looked == before && Date().timeIntervalSince(asked) < 20 {
            if !NSApp.isActive { NSApp.activate(ignoringOtherApps: true) }
            try? await Task.sleep(nanoseconds: 250_000_000)
        }
        NSLog("[Simpl] walk: file link %@, Quick Look %@", clicked.isEmpty ? "not found" : clicked, FilePreview.looked > before ? "checked" : "not checked")
    }

    /// The rich text's web views in a view (HTMLBlock's), in the order they are laid out.
    private static func richTexts(in view: NSView?) -> [WKWebView] {
        guard let view else { return [] }
        if let w = view as? PassThroughWebView { return [w] }
        return view.subviews.flatMap { richTexts(in: $0) }
    }

    private static func run(_ js: String, in v: WKWebView) async -> String {
        await withCheckedContinuation { (done: CheckedContinuation<String, Never>) in
            v.evaluateJavaScript(js) { result, _ in done.resume(returning: result as? String ?? "") }
        }
    }

    private static func until(_ seconds: Double, _ done: () -> Bool) async {
        let start = Date()
        while !done() && Date().timeIntervalSince(start) < seconds {
            try? await Task.sleep(nanoseconds: 40_000_000)
        }
    }
}
