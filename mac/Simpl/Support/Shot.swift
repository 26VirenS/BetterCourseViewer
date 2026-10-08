import AppKit

/// The screenshot suite's camera (scripts/dev/mac-shots.sh). With `-SimplShotFile <path>.png` at launch: the main
/// window is sized as asked (`-SimplWindowSize 1280x820`), and once the app has settled (`-SimplShotAfter`, seconds)
/// the rect of the window in front (with any sheet over it) is written beside the picture's path (`<path>.rect`, in
/// the screen's top-left coordinates) for the suite's own `screencapture`, and a picture is taken from inside the app
/// too (`<path>-app.png`, the window server's own composite of that rect) in case the suite may not record the screen.
/// Nothing of this runs without the argument.
enum Shot {
    static func armIfAsked() {
        let d = UserDefaults.standard
        guard let file = d.string(forKey: "SimplShotFile"), !file.isEmpty else { return }
        let after = d.double(forKey: "SimplShotAfter") > 0 ? d.double(forKey: "SimplShotAfter") : 8
        NSApp.activate(ignoringOtherApps: true)
        for delay in [0.6, 2.0, 5.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { place() }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + after) { take(file) }
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

    /// The window to picture: the one in front (Settings, a tool's window), else the main window; with its sheets.
    private static func target() -> NSWindow? {
        if let key = NSApp.keyWindow {
            return key.sheetParent ?? key
        }
        return mainWindow()
    }

    private static func take(_ file: String) {
        guard let w = target(), let primary = NSScreen.screens.first else { return }
        var rect = w.frame
        for sheet in w.sheets { rect = rect.union(sheet.frame) }
        if let child = w.attachedSheet { rect = rect.union(child.frame) }
        // (AppKit counts from the bottom left of the first screen; screencapture from its top left)
        let top = primary.frame.maxY - rect.maxY
        let spec = "\(Int(rect.minX.rounded())),\(Int(top.rounded())),\(Int(rect.width.rounded())),\(Int(rect.height.rounded()))"
        let base = file.hasSuffix(".png") ? String(file.dropLast(4)) : file
        if let image = composite(of: CGRect(x: rect.minX, y: top, width: rect.width, height: rect.height)) {
            let rep = NSBitmapImageRep(cgImage: image)
            if let png = rep.representation(using: .png, properties: [:]) {
                try? png.write(to: URL(fileURLWithPath: base + "-app.png"))
            }
        }
        try? spec.write(toFile: base + ".rect", atomically: true, encoding: .utf8)
    }

    /// What the window server shows in a rect of the screen, the app's own windows composited (no permission is asked
    /// for a picture of the app's own windows). CGWindowListCreateImage is gone from the SDK's headers since macOS 15;
    /// it is looked up at run time, and its absence is only a missing picture.
    private static func composite(of rect: CGRect) -> CGImage? {
        typealias Capture = @convention(c) (CGRect, UInt32, UInt32, UInt32) -> Unmanaged<CGImage>?
        guard let handle = dlopen("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", RTLD_NOW),
              let symbol = dlsym(handle, "CGWindowListCreateImage") else { return nil }
        let capture = unsafeBitCast(symbol, to: Capture.self)
        let onScreenOnly: UInt32 = 1 << 0 // kCGWindowListOptionOnScreenOnly
        let bestResolution: UInt32 = 1 << 3 // kCGWindowImageBestResolution
        return capture(rect, onScreenOnly, 0, bestResolution)?.takeRetainedValue()
    }
}
