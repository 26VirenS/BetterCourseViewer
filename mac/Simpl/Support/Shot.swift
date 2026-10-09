import AppKit

/// The screenshot suite's camera (scripts/dev/mac-shots.sh). With `-SimplShotFile <path>.png` at launch: the main
/// window is sized as asked (`-SimplWindowSize 1280x820`), and once the app has settled (`-SimplShotAfter`, seconds)
/// the rect of the window in front (with any sheet over it) is written beside the picture's path (`<path>.rect`, in
/// the screen's top-left coordinates) for the suite's own `screencapture`, and a picture is taken from inside the app
/// too (`<path>-app.png`, the window server's own composite of that rect) in case the suite may not record the screen.
/// Nothing of this runs without the argument.
enum Shot {
    /// (1.3.1) The page's answers being waited for, and when the last one came: a picture is taken once nothing has been
    /// asked for a moment (`settled`), not after a fixed wait — `-SimplShotAfter` is now the longest it waits.
    nonisolated(unsafe) private static var busy = 0
    nonisolated(unsafe) private static var lastActivity = Date()
    nonisolated(unsafe) private static var started = Date()
    nonisolated(unsafe) private static var taken = false

    static func began() {
        busy += 1
        lastActivity = Date()
    }

    static func ended() {
        busy = max(0, busy - 1)
        lastActivity = Date()
    }

    static func armIfAsked() {
        let d = UserDefaults.standard
        guard let file = d.string(forKey: "SimplShotFile"), !file.isEmpty else { return }
        let after = d.double(forKey: "SimplShotAfter") > 0 ? d.double(forKey: "SimplShotAfter") : 8
        // (-SimplShotSettle NO: the full wait, for a picture of what the engine's answers do not say — a web page loading)
        let settle = d.object(forKey: "SimplShotSettle") == nil || d.bool(forKey: "SimplShotSettle")
        started = Date()
        lastActivity = Date()
        NSApp.activate(ignoringOtherApps: true)
        for delay in [0.6, 2.0, 5.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { place() }
        }
        if settle { poll(file, after: after) }
        DispatchQueue.main.asyncAfter(deadline: .now() + after) { take(file) }
    }

    /// Every quarter second: the picture taken once the app has been up a little (its arrivals played), and nothing has
    /// been asked of the page for 1.4 seconds.
    private static func poll(_ file: String, after: Double) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            guard !taken else { return }
            let up = Date().timeIntervalSince(started), quiet = Date().timeIntervalSince(lastActivity)
            // (and the app's own screens are up — before then nothing is asked, which is not the same as done)
            let ready = MainActor.assumeIsolated { AppModel.shared.engine.map { $0.phase == .native && $0.countsLive } ?? true }
            if up >= min(4, after) && ready && busy == 0 && quiet >= 1.4 {
                take(file)
            } else if up < after {
                poll(file, after: after)
            }
        }
    }

    /// The main window at the size asked, at the top left of the screen.
    private static func place() {
        guard let raw = UserDefaults.standard.string(forKey: "SimplWindowSize") else { return }
        let parts = raw.lowercased().split(separator: "x").compactMap { Double($0) }
        guard parts.count == 2, let w = mainWindow(), let screen = w.screen ?? NSScreen.main else { return }
        let visible = screen.visibleFrame
        let size = NSSize(width: min(parts[0], visible.width), height: min(parts[1], visible.height))
        w.setFrame(NSRect(x: visible.minX, y: visible.maxY - size.height, width: size.width, height: size.height), display: true)
    }

    private static func mainWindow() -> NSWindow? {
        NSApp.windows
            .filter { $0.isVisible && !($0 is NSPanel) && $0.sheetParent == nil && $0.frame.width > 400 }
            .max { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }
    }

    /// The window to picture: this app's frontmost (Settings, a tool's window), else the main window; with its sheets.
    /// (1.3.1: by the app's own order, not the key window — the suite runs several copies at once, and only one is key.)
    private static func target() -> NSWindow? {
        if let front = NSApp.orderedWindows.first(where: { $0.isVisible && !($0 is NSPanel) && $0.frame.width > 300 }) {
            return front.sheetParent ?? front
        }
        return mainWindow()
    }

    private static func take(_ file: String) {
        guard !taken else { return }
        taken = true
        guard let w = target(), let primary = NSScreen.screens.first else { return }
        // (the window, then what belongs to it over it: its sheets, its child windows, a popover open on it)
        var parts = [w] + w.sheets + (w.attachedSheet.map { [$0] } ?? []) + (w.childWindows ?? []).filter(\.isVisible)
        parts += NSApp.windows.filter { $0.isVisible && String(describing: type(of: $0)).contains("Popover") && $0.frame.intersects(w.frame) }
        var seen = Set<Int>()
        parts = parts.filter { seen.insert($0.windowNumber).inserted }
        var rect = w.frame
        for p in parts { rect = rect.union(p.frame) }
        // (AppKit counts from the bottom left of the first screen; screencapture from its top left)
        let top = primary.frame.maxY - rect.maxY
        let spec = "\(Int(rect.minX.rounded())),\(Int(top.rounded())),\(Int(rect.width.rounded())),\(Int(rect.height.rounded()))"
        let base = file.hasSuffix(".png") ? String(file.dropLast(4)) : file
        if let image = composite(parts, rect, scale: w.backingScaleFactor) {
            let rep = NSBitmapImageRep(cgImage: image)
            if let png = rep.representation(using: .png, properties: [:]) {
                try? png.write(to: URL(fileURLWithPath: base + "-app.png"))
            }
        }
        try? spec.write(toFile: base + ".rect", atomically: true, encoding: .utf8)
    }

    /// The window and its own over it, in `rect` (AppKit's coordinates), each pictured on its own and laid in order — so
    /// another copy of the app, or anything else on the screen, never shows in the picture (1.3.1: the suite runs
    /// several copies at once). CGWindowListCreateImage is gone from the SDK's headers since macOS 15; it is looked up
    /// at run time, and its absence is only a missing picture.
    private static func composite(_ windows: [NSWindow], _ rect: CGRect, scale: CGFloat) -> CGImage? {
        typealias Capture = @convention(c) (CGRect, UInt32, UInt32, UInt32) -> Unmanaged<CGImage>?
        guard let handle = dlopen("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", RTLD_NOW),
              let symbol = dlsym(handle, "CGWindowListCreateImage") else { return nil }
        let capture = unsafeBitCast(symbol, to: Capture.self)
        let including: UInt32 = 1 << 3 // kCGWindowListOptionIncludingWindow
        let options: UInt32 = (1 << 0) | (1 << 3) // kCGWindowImageBoundsIgnoreFraming | kCGWindowImageBestResolution
        let width = Int((rect.width * scale).rounded()), height = Int((rect.height * scale).rounded())
        guard width > 0, height > 0,
              let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                  space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        for win in windows where win.alphaValue > 0.01 {
            guard let image = capture(.null, including, UInt32(win.windowNumber), options)?.takeRetainedValue() else { continue }
            let f = win.frame
            ctx.draw(image, in: CGRect(x: (f.minX - rect.minX) * scale, y: (f.minY - rect.minY) * scale, width: f.width * scale, height: f.height * scale))
        }
        return ctx.makeImage()
    }
}
