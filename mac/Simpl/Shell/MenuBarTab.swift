import AppKit
import SwiftUI

// (1.3.153, 1.3.155) New Window: while a large popup is up over the window — a tool, a Knewton Alta or Macmillan
// assignment, the feedback, the rubric — a little black tab hangs from the menu bar over the empty start of the toolbar,
// as if the menu bar went on down there, and says New Window; pressed, the popup becomes a window of its own
// (FloatingPopups.openFrontInWindow). It lives in the window's own title bar, which in full screen is the bar that slides
// down with the menu bar as the pointer reaches the top of the screen — so the tab drops down with it and goes back up
// with it. Out of full screen there is no menu bar to hang from: it is a small black New Window button beside the
// window's buttons instead. While the sidebar is folded away its button sits there, so the tab steps aside.

/// Puts the tab in its window's title bar (one per window), and keeps it to the window's state and its popups.
struct MenuBarTabProbe: NSViewRepresentable {
    /// The sidebar out: the room the tab takes is free.
    var sidebarShown: Bool
    /// The window's popups: the tab shows while a large one is up, and takes it to a window of its own.
    let popups: FloatingPopups

    func makeNSView(context: Context) -> MenuBarTabProbeView { MenuBarTabProbeView(popups: popups) }

    func updateNSView(_ v: MenuBarTabProbeView, context: Context) {
        v.sidebarShown = sidebarShown
    }
}

final class MenuBarTabProbeView: NSView {
    var sidebarShown = true { didSet { if sidebarShown != oldValue { place() } } }
    private let popups: FloatingPopups
    private let tab = MenuBarTabView()
    private var watching: [NSObjectProtocol] = []

    init(popups: FloatingPopups) {
        self.popups = popups
        super.init(frame: .zero)
        tab.pressed = { [weak self] in self?.popups.openFrontInWindow() }
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
                MainActor.assumeIsolated { self?.place() }
            })
        }
        // (a popup up, gone, or made a window of its own: the tab shown or not)
        watching.append(center.addObserver(forName: FloatingPopups.changed, object: popups, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.place() }
        })
        // (the title bar is laid out a moment after the window first shows)
        DispatchQueue.main.async { [weak self] in self?.place() }
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
            tab.translatesAutoresizingMaskIntoConstraints = true
            bar.addSubview(tab)
        }
        let frame: NSRect
        // (past the window's buttons either way: (1.3.157) in full screen macOS shows them too as the bar drops down,
        //  and the green one lay over the tab's start)
        let zoom = (window.standardWindowButton(.zoomButton) ?? close).frame
        if full {
            let size = MenuBarTabView.tabSize
            frame = NSRect(x: max(MenuBarTabView.tabLeading, zoom.maxX + 8), y: bar.isFlipped ? 0 : bar.bounds.height - size.height, width: size.width, height: size.height)
            tab.autoresizingMask = bar.isFlipped ? [.maxYMargin] : [.minYMargin]
        } else {
            let size = MenuBarTabView.buttonSize
            frame = NSRect(x: zoom.maxX + 14, y: close.frame.midY - size.height / 2, width: size.width, height: size.height)
            tab.autoresizingMask = close.autoresizingMask
        }
        if tab.frame != frame { tab.frame = frame }
        if tab.full != full { tab.full = full }
        tab.isHidden = !sidebarShown || !popups.canOpenInWindow
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
    static let tabLeading: CGFloat = 58
    static let tabSize = CGSize(width: 92, height: 24)
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
        toolTip = "Open the popup in a window of its own"
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
        let font = NSFont.systemFont(ofSize: 11, weight: .semibold)
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
