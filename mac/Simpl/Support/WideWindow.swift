import AppKit

/// (1.2.1) The window's first opening: wide enough for the screens' side-by-side layouts (the Dashboard's view with its
/// side column, Grades' list beside its course, the Inbox's two panes) — most of the screen, centred — once; after
/// that it opens as it was left. (Not in the screenshot suite, which sizes the window itself: -SimplWindowSize.)
@MainActor
enum WideWindow {
    private static let key = "window:wide1"

    static func openWideOnce(_ host: NSView) {
        let d = UserDefaults.standard
        guard !d.bool(forKey: key), d.string(forKey: "SimplWindowSize") == nil else { return }
        // (after the window has taken the frame it was restored to, so this one is the last word)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) {
            guard let w = host.window, let screen = w.screen ?? NSScreen.main else { return }
            d.set(true, forKey: key)
            let v = screen.visibleFrame
            let width = v.width < 1400 ? v.width : min(v.width - 80, max(1440, v.width * 0.9))
            let height = v.height < 900 ? v.height : min(v.height - 40, max(900, v.height * 0.9))
            let frame = NSRect(x: v.midX - width / 2, y: v.midY - height / 2, width: width, height: height)
            w.setFrame(frame, display: true, animate: false)
        }
    }
}
