import AppKit
import WebKit

/// (1.3.212) A web view's picture, in a time that can be waited for. WebKit's snapshot "after the screen updates" waits
/// for the window's next drawing, which may never come while the window is out of sight (behind other windows, on
/// another Space, or one of the picture suite's copies of the app under another), and a page recording waited on it for
/// good: its first step never saved, and no step after it. Asked that way first for at most `wait` seconds; then as the
/// page last drew, which does not wait for the screen.
@MainActor
enum WebSnapshot {
    static func take(_ web: WKWebView, width: CGFloat? = nil, wait: Double = 3) async -> NSImage? {
        if let image = await ask(web, width: width, afterUpdates: true, wait: wait) { return image }
        return await ask(web, width: width, afterUpdates: false, wait: wait)
    }

    private static func ask(_ web: WKWebView, width: CGFloat?, afterUpdates: Bool, wait: Double) async -> NSImage? {
        await withCheckedContinuation { (go: CheckedContinuation<NSImage?, Never>) in
            let once = Once()
            let config = WKSnapshotConfiguration()
            config.afterScreenUpdates = afterUpdates
            if let width { config.snapshotWidth = NSNumber(value: Double(width)) }
            web.takeSnapshot(with: config) { image, _ in if once.first() { go.resume(returning: image) } }
            DispatchQueue.main.asyncAfter(deadline: .now() + wait) { if once.first() { go.resume(returning: nil) } }
        }
    }
}

/// True the first time it is asked, false after: one answer from WebKit or a timer, whichever comes first. Only ever
/// asked on the main thread (WebKit answers there, and the timer is the main queue's), so it needs no lock.
final class Once: @unchecked Sendable {
    private var done = false

    func first() -> Bool {
        if done { return false }
        done = true
        return true
    }
}
