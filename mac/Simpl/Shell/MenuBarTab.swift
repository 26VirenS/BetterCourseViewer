import AppKit
import SwiftUI

// (1.3.153, 1.3.155, 1.3.157) New Window: while a large popup is up over the window — a tool, a Knewton Alta
// assignment, the feedback, the rubric — a little black tab hangs from the menu bar over the empty start of the toolbar,
// as if the menu bar went on down there, and says New Window; pressed, another Simpl window opens on the screen this one
// shows (File ▸ New Window's), the popup staying where it is — something else looked up while the assignment waits. It lives in the window's own title bar, which in full screen is the bar that slides
// down with the menu bar as the pointer reaches the top of the screen — so the tab drops down with it and goes back up
// with it. Out of full screen there is no menu bar to hang from: it is a small black New Window button beside the
// window's buttons instead. While the sidebar is folded away its button sits there, so the tab steps aside.

/// Puts the tab in its window's title bar (one per window), and keeps it to the window's state and its popups.
struct MenuBarTabProbe: NSViewRepresentable {
    /// The sidebar out: the room the tab takes is free.
    var sidebarShown: Bool
    /// The window's engine: its popups (the tab shows while a large one is up), and the screen it shows (where the new
    /// window opens).
    let engine: Engine

    func makeNSView(context: Context) -> MenuBarTabProbeView { MenuBarTabProbeView(engine: engine) }

    func updateNSView(_ v: MenuBarTabProbeView, context: Context) {
        v.sidebarShown = sidebarShown
    }
}

final class MenuBarTabProbeView: NSView {
    var sidebarShown = true { didSet { if sidebarShown != oldValue { placeSoon() } } }
    private weak var engine: Engine?
    private let popups: FloatingPopups
    private let tab = MenuBarTabView()
    private var watching: [NSObjectProtocol] = []
    /// A placing asked for, not yet done (many asks in one turn of the run loop, one placing).
    private var placing = false

    init(engine: Engine) {
        self.engine = engine
        popups = engine.popups
        super.init(frame: .zero)
        // (File ▸ New Window's: a window more of Simpl, on the screen this one shows — RootView opens it)
        tab.pressed = { [weak self] in
            guard let engine = self?.engine else { return }
            NotificationCenter.default.post(name: .simplOpenWindow, object: Engine.name(of: engine.nav.current.place))
        }
    }

    required init?(coder: NSCoder) { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        watching.forEach(NotificationCenter.default.removeObserver)
        watching = []
        guard let window else { tab.removeFromSuperview(); return }
        let center = NotificationCenter.default
        for name in [NSWindow.didEnterFullScreenNotification, NSWindow.didExitFullScreenNotification, NSWindow.willEnterFullScreenNotification, NSWindow.didResizeNotification] {
            watching.append(center.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.placeSoon() }
            })
        }
        // (a popup up or gone: the tab shown or not)
        watching.append(center.addObserver(forName: FloatingPopups.changed, object: popups, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.placeSoon() }
        })
        // (the title bar is laid out a moment after the window first shows)
        placeSoon()
    }

    /// (1.3.157) Placed on the next turn of the run loop, never there and then: a resize is told of in the middle of the
    /// window laying itself out, and the tab moved or shown in the title bar then had AppKit lay the window out again,
    /// and again, until it stopped the app.
    private func placeSoon() {
        guard !placing else { return }
        placing = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.placing = false
            self.place()
        }
    }

    /// The tab put in the title bar again (in full screen it moves to the bar that drops with the menu bar), as a tab
    /// under the menu bar in full screen and a button beside the window's buttons otherwise — shown only while a large
    /// popup is up. Placed by its frame, never by constraints: the title bar lays itself out, and constraints of ours in
    /// it looped its layout on narrow windows.
    private func place() {
        guard let window, let close = window.standardWindowButton(.closeButton), let bar = close.superview else { return }
        let full = window.styleMask.contains(.fullScreen)
        if tab.superview !== bar {
            tab.removeFromSuperview()
            tab.isHidden = true // (shown below once placed, if a large popup is up)
            tab.translatesAutoresizingMaskIntoConstraints = true
            bar.addSubview(tab)
        }
        let frame: NSRect
        // (past the window's buttons either way: (1.3.157) in full screen macOS shows them too as the bar drops down,
        //  and the green one lay over the tab's start)
        let zoom = (window.standardWindowButton(.zoomButton) ?? close).frame
        if full {
            let size = MenuBarTabView.tabSize
            let x = max(MenuBarTabView.tabLeading, zoom.maxX + 16)
            // (1.3.158) as wide as the room before the toolbar's first button (the sidebar's) allows, up to its most —
            // the room moves with the sidebar's width; not found, the least
            let room = toolbarStart(in: bar).map { $0 - 12 - x } ?? size.width
            let width = min(max(room, MenuBarTabView.tabLeast), max(MenuBarTabView.tabMost, MenuBarTabView.tabLeast)).rounded()
            frame = NSRect(x: x, y: bar.isFlipped ? 0 : bar.bounds.height - size.height, width: width, height: size.height)
            tab.autoresizingMask = bar.isFlipped ? [.maxYMargin] : [.minYMargin]
        } else {
            let size = MenuBarTabView.buttonSize
            frame = NSRect(x: zoom.maxX + 16, y: close.frame.midY - size.height / 2, width: size.width, height: size.height)
            tab.autoresizingMask = close.autoresizingMask
        }
        // (nothing set that is so already: each change has the title bar laid out again)
        if tab.frame != frame { tab.frame = frame }
        if tab.full != full { tab.full = full }
        let hidden = !sidebarShown || !popups.hasLarge
        if tab.isHidden != hidden { tab.isHidden = hidden }
    }

    /// (1.3.158) Where the toolbar's first item starts across the title bar (the sidebar's button, past the window's
    /// buttons), in the title bar's own points: the most the tab may reach, less a margin.
    private func toolbarStart(in bar: NSView) -> CGFloat? {
        var start: CGFloat?
        func walk(_ v: NSView) {
            for s in v.subviews where s !== tab && !s.isHidden {
                if String(describing: type(of: s)).contains("ToolbarItemViewer") {
                    let r = s.convert(s.bounds, to: bar)
                    if r.width > 0, r.minX > 40 { start = min(start ?? r.minX, r.minX) }
                } else {
                    walk(s)
                }
            }
        }
        walk(bar.superview ?? bar)
        return start
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    deinit {
        watching.forEach(NotificationCenter.default.removeObserver)
        tab.removeFromSuperview()
    }
}

/// The tab itself: under the menu bar, black with its top corners flared into the menu bar and its bottom ones rounded
/// (the shape of the screen's notch); out of full screen, a black capsule. Either says New Window.
final class MenuBarTabView: NSView {
    static let tabLeading: CGFloat = 76
    static let tabSize = CGSize(width: 88, height: 28)
    /// (1.3.159) In full screen the label is in the menu bar's own font and size (File, Edit, View…), so the tab is as
    /// tall as it needs and never narrower than its words with room round them.
    static let menuFont = NSFont.menuBarFont(ofSize: 0)
    static var tabLeast: CGFloat {
        let words = (label as NSString).size(withAttributes: [.font: menuFont]).width
        return max(tabSize.width, (words + 2 * 6 + 28).rounded(.up))
    }
    /// (1.3.158) The widest the tab grows in full screen, given the room.
    static let tabMost: CGFloat = 150
    static let buttonSize = CGSize(width: 90, height: 20)
    private static let label = "New Window"

    var pressed: (() -> Void)?
    var full = false { didSet { needsDisplay = true } }
    private var down = false { didSet { needsDisplay = true } }

    override init(frame: NSRect) {
        super.init(frame: frame)
        setAccessibilityElement(true)
        setAccessibilityRole(.button)
        setAccessibilityLabel(Self.label)
        toolTip = "Open another Simpl window"
    }

    required init?(coder: NSCoder) { nil }

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        let w = bounds.width, h = bounds.height
        NSColor.black.setFill()
        var text = bounds
        if full {
            let f: CGFloat = 6, r: CGFloat = 9
            let p = NSBezierPath()
            p.move(to: NSPoint(x: 0, y: 0))
            p.curve(to: NSPoint(x: f, y: f), controlPoint1: NSPoint(x: f * 0.6, y: 0), controlPoint2: NSPoint(x: f, y: f * 0.4))
            p.line(to: NSPoint(x: f, y: h - r))
            p.curve(to: NSPoint(x: f + r, y: h), controlPoint1: NSPoint(x: f, y: h - r * 0.45), controlPoint2: NSPoint(x: f + r * 0.45, y: h))
            p.line(to: NSPoint(x: w - f - r, y: h))
            p.curve(to: NSPoint(x: w - f, y: h - r), controlPoint1: NSPoint(x: w - f - r * 0.45, y: h), controlPoint2: NSPoint(x: w - f, y: h - r * 0.45))
            p.line(to: NSPoint(x: w - f, y: f))
            p.curve(to: NSPoint(x: w, y: 0), controlPoint1: NSPoint(x: w - f, y: f * 0.4), controlPoint2: NSPoint(x: w - f * 0.6, y: 0))
            p.close()
            p.fill()
            text = bounds.insetBy(dx: f, dy: 0)
        } else {
            NSBezierPath(roundedRect: bounds, xRadius: h / 2, yRadius: h / 2).fill()
        }
        let font = full ? Self.menuFont : NSFont.systemFont(ofSize: 11, weight: .semibold)
        let words = NSAttributedString(string: Self.label, attributes: [.font: font, .foregroundColor: down ? NSColor.lightGray : NSColor.white])
        let s = words.size()
        words.draw(at: NSPoint(x: text.midX - s.width / 2, y: text.midY - s.height / 2))
    }

    override func mouseDown(with event: NSEvent) { down = true }

    override func mouseUp(with event: NSEvent) {
        down = false
        if bounds.contains(convert(event.locationInWindow, from: nil)) { pressed?() }
    }

    override func accessibilityPerformPress() -> Bool {
        pressed?()
        return true
    }
}
