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

    /// (1.3.79) Before the windows are made: the frames and the sidebar's width the last picture's app saved in this
    /// copy's home forgotten, so the window opens at its own size and is then sized as asked. A window that opened as
    /// the picture before left it — 720 points wide, its sidebar folded away for want of room — had its sidebar come out
    /// as it was widened, and a picture of a tool's popup with the sidebar folded showed it out, under the popup.
    static func forgetWindowFramesIfAsked() {
        let d = UserDefaults.standard
        guard let file = d.string(forKey: "SimplShotFile"), !file.isEmpty else { return }
        for key in d.dictionaryRepresentation().keys where key.hasPrefix("NSWindow Frame ") || key.hasPrefix("NSSplitView Subview Frames ") {
            d.removeObject(forKey: key)
        }
    }

    static func armIfAsked() {
        let d = UserDefaults.standard
        guard let file = d.string(forKey: "SimplShotFile"), !file.isEmpty else { return }
        let after = d.double(forKey: "SimplShotAfter") > 0 ? d.double(forKey: "SimplShotAfter") : 8
        // (-SimplShotSettle NO: the full wait, for a picture of what the engine's answers do not say — a web page loading)
        let settle = d.object(forKey: "SimplShotSettle") == nil || d.bool(forKey: "SimplShotSettle")
        started = Date()
        lastActivity = Date()
        // (1.3.80: -SimplLiveShotAfter 4 — the picture waits for that many live checks, Engine/LiveRefresh.swift)
        if d.integer(forKey: "SimplLiveShotAfter") > 0 { began() }
        NSApp.activate(ignoringOtherApps: true)
        for delay in [0.6, 2.0, 5.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { place() }
        }
        if settle {
            poll(file, after: after)
        } else {
            waitForScreens(file, after: after)
        }
    }

    /// (1.3.151) The full wait of a picture of a web page loading (-SimplShotSettle NO), counted from when the app's own
    /// screens are up, not from launch: on a runner slow enough that the Dashboard took 18 s to open, a pin's graph and a
    /// tool's page were pictured blank at 24 s. Screens up by their usual 6 s, the picture is taken when it always was;
    /// later, it waits as much longer — and never past the wait and 40 s more, up or not.
    private static func waitForScreens(_ file: String, after: Double) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            guard !taken else { return }
            let up = Date().timeIntervalSince(started)
            let ready = MainActor.assumeIsolated { AppModel.shared.engine.map { $0.phase == .native && $0.countsLive } ?? true }
            if ready || up >= after + 40 {
                let rest = max(after - up, ready ? after - 6 : 0)
                DispatchQueue.main.asyncAfter(deadline: .now() + rest) { take(file) }
            } else {
                waitForScreens(file, after: after)
            }
        }
    }

    /// When the app's own screens were up, if they are.
    nonisolated(unsafe) private static var readyAt: Date?

    /// Every quarter second: the picture taken once the app has been up a little (its arrivals played), and nothing has
    /// been asked of the page for 1.4 seconds — or at the longest wait whatever is under way. (1.3.178) That wait counted
    /// as `waitForScreens` counts it: screens up by their usual 6 s, it is `after` from launch; later, as much longer,
    /// and never past it and 40 s more. A runner slow enough that the Dashboard came 17 s in pictured the calculator's
    /// pin at its 14 s, the window still blank.
    private static func poll(_ file: String, after: Double) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            guard !taken else { return }
            let up = Date().timeIntervalSince(started), quiet = Date().timeIntervalSince(lastActivity)
            // (and the app's own screens are up — before then nothing is asked, which is not the same as done)
            let ready = MainActor.assumeIsolated { AppModel.shared.engine.map { $0.phase == .native && $0.countsLive } ?? true }
            if ready, readyAt == nil { readyAt = Date() }
            let late = readyAt.map { max(0, $0.timeIntervalSince(started) - 6) } ?? 0
            if up >= min(4, after) && ready && busy == 0 && quiet >= 1.4 {
                take(file)
            } else if (ready && up >= after + min(late, 40)) || up >= after + 40 {
                take(file)
            } else {
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
    /// (1.3.78) Never a window that belongs to another — the search field's suggestions, a child window — nor one without
    /// a title bar of its own (a sheet aside): the suggestions open in front of the window they belong to, borderless, a
    /// strip or the window's whole height tall, and were pictured alone.
    private static func target() -> NSWindow? {
        if let front = NSApp.orderedWindows.first(where: { $0.isVisible && !($0 is NSPanel) && $0.parent == nil && $0.frame.width > 300
                && $0.frame.height > 200 && ($0.styleMask.contains(.titled) || $0.sheetParent != nil) }) {
            return front.sheetParent ?? front
        }
        return mainWindow()
    }

    /// (1.3.78) What is in front of the window and over it without being its own child or sheet — the search field's
    /// suggestions — pictured with it.
    private static func above(_ w: NSWindow) -> [NSWindow] {
        NSApp.orderedWindows.prefix(while: { $0 !== w }).filter { $0.isVisible && $0.alphaValue > 0.01 && $0.frame.intersects(w.frame) }
    }

    private static func take(_ file: String) {
        guard !taken else { return }
        taken = true
        // (1.3.78) the window brought to the front first, and pictured a moment later: the suite's copies of the app share
        // the screen, and a copy another has covered draws nothing new — an animation under way in it (its screens
        // fading in as it opens) stopped where it was and was pictured so, faint or blank. In front, it finishes.
        chosen = target()
        front(noting: true)
        settle(file)
    }

    /// (1.3.78) The window chosen to picture when the picture was asked for: the windows brought back in front of it
    /// (the search field's suggestions, as tall as a window) are not chosen in its place as it is brought forward again
    /// and pictured — the Commands picture was of its suggestions alone, blank.
    nonisolated(unsafe) private static var chosen: NSWindow?

    /// The window to picture: the one chosen, while it is still up.
    private static func pictured() -> NSWindow? {
        if let w = chosen, w.isVisible { return w }
        return target()
    }

    /// The window to picture brought to the front, with what was in front of it and over it — the search field's
    /// suggestions — noted first (`noting`, the first time), and brought back to the front after it: the window brought
    /// forward went over them, and they were left out of the picture.
    private static func front(noting: Bool = false) {
        guard let w = pictured() else { return }
        if noting { lifted = above(w) }
        NSApp.activate(ignoringOtherApps: true)
        w.orderFrontRegardless()
        for s in lifted.reversed() { s.orderFrontRegardless() }
    }

    /// (1.3.78) Pictured once the app is in front — brought forward once more when another copy has taken the front at
    /// the same moment: a pin's popover closes when its app is put behind another (it opens again as the app comes back),
    /// and two of the four pins were pictured closed, their windows inactive. Once only, and never past two seconds: the
    /// copies bringing themselves forward in turn took the front from each other, and under the full set's load each
    /// turn took seconds — two pictures were never taken before the suite gave up on them.
    /// A pin's popover pictured (`-SimplPinOpen`), the one thing that is not there unless its app is in front, is waited
    /// for longer: brought forward again until the app is in front, for up to eight seconds (it is five pictures of the
    /// full set; with only one more try, Hardy–Weinberg's was pictured closed).
    private static func settle(_ file: String, again: Bool = true, since: Date = Date()) {
        // (1.3.165: the plan rail on To Do, -SimplPlanFirst, is a popover too; 1.3.170: and To Do's ⋯ menu, -SimplTodoMenu,
        //  a menu, closes the same way; 1.3.178: and a tool's list on External Tools, -SimplExternalToolList)
        let d = UserDefaults.standard
        let popover = d.string(forKey: "SimplPinOpen") != nil || d.bool(forKey: "SimplPlanFirst") || d.bool(forKey: "SimplTodoMenu")
            || d.string(forKey: "SimplExternalToolList") != nil
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) {
            let waited = Date().timeIntervalSince(since)
            if NSApp.isActive || (popover ? waited > 8 : (!again || waited > 2)) {
                // (1.3.166) a popover's app just brought back in front: a moment more for the popover to open again as it
                // came back — the plan rail's dark picture was taken as it was still opening, the window alone; (1.3.168)
                // and then until what it reads again as it opens has come (the plan rail's free times: its dark picture
                // was taken as it was still finding them)
                if d.bool(forKey: "SimplTodoMenu") {
                    menuOpen(file, until: Date().addingTimeInterval(8))
                } else if popover && !again && NSApp.isActive {
                    DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { whenIdle(file, until: Date().addingTimeInterval(8)) }
                } else if popover && busy > 0 {
                    // (1.3.178) a popover still waited for as its app came in front (a tool's list asked for again once
                    // it is): until it has come
                    whenIdle(file, until: Date().addingTimeInterval(8))
                } else {
                    capture(file)
                }
            } else {
                NSLog("[Simpl] shot: another copy of the app is in front; brought forward again")
                front()
                settle(file, again: false, since: since)
            }
        }
    }

    /// (1.3.170) To Do's ⋯ menu pictured once it is open (it opens a moment after its app is in front, and again each
    /// time the app comes back in front), or at `until` whatever it is.
    private static func menuOpen(_ file: String, until: Date) {
        let open = MainActor.assumeIsolated { PopMenu.isOpen }
        if open || Date() >= until {
            DispatchQueue.main.asyncAfter(deadline: .now() + (open ? 0.5 : 0)) { capture(file) }
            return
        }
        if !NSApp.isActive { front() }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { menuOpen(file, until: until) }
    }

    /// (1.3.168) Pictured once nothing is being waited for and nothing has come for half a second (what came has
    /// finished arriving), or at `until` whatever is still under way.
    private static func whenIdle(_ file: String, until: Date) {
        if (busy == 0 && Date().timeIntervalSince(lastActivity) >= 0.5) || Date() >= until {
            capture(file)
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) { whenIdle(file, until: until) }
    }

    /// (1.3.78) The windows that were in front of the one pictured, over it, when its picture was asked for.
    nonisolated(unsafe) private static var lifted: [NSWindow] = []

    private static func capture(_ file: String, attempt: Int = 0) {
        guard let w = pictured(), let primary = NSScreen.screens.first else { return }
        // (the window, then what belongs to it over it: its sheets, its child windows, a popover open on it)
        var parts = [w] + w.sheets + (w.attachedSheet.map { [$0] } ?? []) + (w.childWindows ?? []).filter(\.isVisible)
        parts += NSApp.windows.filter { $0.isVisible && String(describing: type(of: $0)).contains("Popover") && $0.frame.intersects(w.frame) }
        parts += NSApp.windows.filter { $0 is ToolFloatPanel && $0.isVisible } // (1.3.73: a quick panel pulled out, floating)
        // (1.3.103: Quick Look's panel up on a file, wherever on the screen it opened)
        parts += NSApp.windows.filter { $0.isVisible && String(describing: type(of: $0)).contains("QLPreviewPanel") }
        // (1.3.78: the search field's suggestions, in front and over it — after the rest, so they are laid on top)
        let over = lifted.reversed().filter { $0.isVisible && $0.frame.intersects(w.frame) } + above(w).reversed()
        parts += over.filter { s in !parts.contains(where: { $0 === s }) }
        var seen = Set<Int>()
        parts = parts.filter { seen.insert($0.windowNumber).inserted }
        var layers = parts.filter { $0.alphaValue > 0.01 }.map { (number: $0.windowNumber, frame: $0.frame) }
        // (1.3.170) a menu open over it (To Do's ⋯, -SimplTodoMenu): no window of AppKit's list, found in the window server's
        layers += menus(over: w.frame, primary: primary).filter { m in !seen.contains(m.number) }
        var rect = w.frame
        for l in layers { rect = rect.union(l.frame) }
        // (AppKit counts from the bottom left of the first screen; screencapture from its top left)
        let top = primary.frame.maxY - rect.maxY
        let spec = "\(Int(rect.minX.rounded())),\(Int(top.rounded())),\(Int(rect.width.rounded())),\(Int(rect.height.rounded()))"
        let base = file.hasSuffix(".png") ? String(file.dropLast(4)) : file
        let image = composite(layers, rect, scale: w.backingScaleFactor)
        // (1.3.78) the window server now and then gives a window back empty — nothing at all of it drawn: asked again,
        // half a second later, four times at most, before an empty picture is kept
        if attempt < 4, image.map(empty) ?? true {
            NSLog("[Simpl] shot: the window came back empty; asked again (%d)", attempt + 1)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { capture(file, attempt: attempt + 1) }
            return
        }
        if let image {
            let rep = NSBitmapImageRep(cgImage: image)
            if let png = rep.representation(using: .png, properties: [:]) {
                try? png.write(to: URL(fileURLWithPath: base + "-app.png"))
            }
        }
        try? spec.write(toFile: base + ".rect", atomically: true, encoding: .utf8)
    }

    /// Nothing drawn in a picture: every pixel of it, seen small, transparent.
    private static func empty(_ image: CGImage) -> Bool {
        let side = 24
        guard let ctx = CGContext(data: nil, width: side, height: side, bitsPerComponent: 8, bytesPerRow: side * 4,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
              let data = ctx.data else { return false }
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: side, height: side))
        let bytes = data.bindMemory(to: UInt8.self, capacity: side * side * 4)
        for i in 0..<(side * side) where bytes[i * 4 + 3] > 8 { return false }
        return true
    }

    /// (1.3.170) This app's menus open on the screen over `rect` (AppKit's coordinates): the window server's windows of
    /// this process at the pop-up menus' level, each by its number and its frame.
    private static func menus(over rect: CGRect, primary: NSScreen) -> [(number: Int, frame: CGRect)] {
        guard let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] else { return [] }
        let me = Int(getpid())
        let level = Int(NSWindow.Level.popUpMenu.rawValue)
        return list.compactMap { info -> (number: Int, frame: CGRect)? in
            guard (info[kCGWindowOwnerPID as String] as? Int) == me, (info[kCGWindowLayer as String] as? Int) == level,
                  let n = info[kCGWindowNumber as String] as? Int, let b = info[kCGWindowBounds as String] as? NSDictionary,
                  let r = CGRect(dictionaryRepresentation: b as CFDictionary) else { return nil }
            // (the window server counts from the first screen's top left)
            let f = CGRect(x: r.minX, y: primary.frame.maxY - r.maxY, width: r.width, height: r.height)
            return f.intersects(rect) ? (n, f) : nil
        }
    }

    /// The window and its own over it, in `rect` (AppKit's coordinates), each pictured on its own and laid in order — so
    /// another copy of the app, or anything else on the screen, never shows in the picture (1.3.1: the suite runs
    /// several copies at once). CGWindowListCreateImage is gone from the SDK's headers since macOS 15; it is looked up
    /// at run time, and its absence is only a missing picture.
    private static func composite(_ windows: [(number: Int, frame: CGRect)], _ rect: CGRect, scale: CGFloat) -> CGImage? {
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
        for win in windows {
            guard let image = capture(.null, including, UInt32(win.number), options)?.takeRetainedValue() else { continue }
            let f = win.frame
            ctx.draw(image, in: CGRect(x: (f.minX - rect.minX) * scale, y: (f.minY - rect.minY) * scale, width: f.width * scale, height: f.height * scale))
        }
        return ctx.makeImage()
    }
}
