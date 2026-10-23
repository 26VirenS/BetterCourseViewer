import AppKit
import SwiftUI

// (1.3.153) A little black tab hanging from the menu bar over the empty start of the toolbar, as if the menu bar went on
// down there. It lives in the window's own title bar, which in full screen is the bar that slides down with the menu
// bar as the pointer reaches the top of the screen — so the tab drops down with it and goes back up with it. Out of
// full screen there is no menu bar to hang from: it is a small black button beside the window's buttons instead, that
// takes the window full screen. While the sidebar is folded away its button sits there, so the tab steps aside.

/// Puts the tab in its window's title bar (one per window), and keeps it to the window's state.
struct MenuBarTabProbe: NSViewRepresentable {
    /// The sidebar out: the room the tab takes is free.
    var sidebarShown: Bool

    func makeNSView(context: Context) -> MenuBarTabProbeView { MenuBarTabProbeView() }

    func updateNSView(_ v: MenuBarTabProbeView, context: Context) {
        v.sidebarShown = sidebarShown
    }
}

final class MenuBarTabProbeView: NSView {
    var sidebarShown = true { didSet { if sidebarShown != oldValue { place() } } }
    private let tab = MenuBarTabView()
    private var watching: [NSObjectProtocol] = []

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        watching.forEach(NotificationCenter.default.removeObserver)
        watching = []
        guard let window else { tab.removeFromSuperview(); return }
        for name in [NSWindow.didEnterFullScreenNotification, NSWindow.didExitFullScreenNotification, NSWindow.willEnterFullScreenNotification] {
            watching.append(NotificationCenter.default.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in self?.place() })
        }
        // (the title bar is laid out a moment after the window first shows)
        DispatchQueue.main.async { [weak self] in self?.place() }
    }

    /// The tab put in the title bar again (in full screen it moves to the bar that drops with the menu bar), as a tab
    /// under the menu bar in full screen and a button beside the window's buttons otherwise.
    private func place() {
        guard let window, let close = window.standardWindowButton(.closeButton), let bar = close.superview else { return }
        let full = window.styleMask.contains(.fullScreen)
        if tab.superview !== bar {
            tab.removeFromSuperview()
            tab.translatesAutoresizingMaskIntoConstraints = false
            bar.addSubview(tab)
        }
        NSLayoutConstraint.deactivate(tab.placed)
        let zoom = window.standardWindowButton(.zoomButton) ?? close
        tab.placed = full
            ? [tab.topAnchor.constraint(equalTo: bar.topAnchor),
               tab.leadingAnchor.constraint(equalTo: bar.leadingAnchor, constant: MenuBarTabView.tabLeading),
               tab.widthAnchor.constraint(equalToConstant: MenuBarTabView.tabSize.width),
               tab.heightAnchor.constraint(equalToConstant: MenuBarTabView.tabSize.height)]
            : [tab.centerYAnchor.constraint(equalTo: close.centerYAnchor),
               tab.leadingAnchor.constraint(equalTo: zoom.trailingAnchor, constant: 14),
               tab.widthAnchor.constraint(equalToConstant: MenuBarTabView.buttonSize.width),
               tab.heightAnchor.constraint(equalToConstant: MenuBarTabView.buttonSize.height)]
        NSLayoutConstraint.activate(tab.placed)
        tab.full = full
        tab.isHidden = !sidebarShown
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    deinit {
        watching.forEach(NotificationCenter.default.removeObserver)
        tab.removeFromSuperview()
    }
}

/// The tab itself: under the menu bar, black with its top corners flared into the menu bar and its bottom ones
/// rounded (the shape of the screen's notch); out of full screen, a black capsule with the full-screen arrows.
final class MenuBarTabView: NSView {
    static let tabLeading: CGFloat = 70
    static let tabSize = CGSize(width: 78, height: 22)
    static let buttonSize = CGSize(width: 34, height: 20)

    var placed: [NSLayoutConstraint] = []
    var full = false {
        didSet {
            toolTip = full ? nil : "Enter Full Screen"
            setAccessibilityElement(!full)
            needsDisplay = true
        }
    }
    private var pressed = false { didSet { needsDisplay = true } }

    override init(frame: NSRect) {
        super.init(frame: frame)
        setAccessibilityRole(.button)
        setAccessibilityLabel("Enter Full Screen")
        toolTip = "Enter Full Screen"
    }

    required init?(coder: NSCoder) { nil }

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        let w = bounds.width, h = bounds.height
        NSColor.black.setFill()
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
            return
        }
        NSBezierPath(roundedRect: bounds, xRadius: h / 2, yRadius: h / 2).fill()
        let config = NSImage.SymbolConfiguration(pointSize: 9, weight: .semibold).applying(NSImage.SymbolConfiguration(paletteColors: [pressed ? .lightGray : .white]))
        guard let arrows = NSImage(systemSymbolName: "arrow.up.left.and.arrow.down.right", accessibilityDescription: nil)?.withSymbolConfiguration(config) else { return }
        let s = arrows.size
        arrows.draw(in: NSRect(x: (w - s.width) / 2, y: (h - s.height) / 2, width: s.width, height: s.height))
    }

    // (in full screen the tab is only the menu bar's: presses go past it)
    override func hitTest(_ point: NSPoint) -> NSView? { full ? nil : super.hitTest(point) }

    override func mouseDown(with event: NSEvent) { pressed = true }

    override func mouseUp(with event: NSEvent) {
        pressed = false
        if bounds.contains(convert(event.locationInWindow, from: nil)) { window?.toggleFullScreen(nil) }
    }

    override func accessibilityPerformPress() -> Bool {
        window?.toggleFullScreen(nil)
        return true
    }
}
