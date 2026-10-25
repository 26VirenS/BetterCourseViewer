import AppKit
import SwiftUI

// (1.3.73) A pin's quick panel pulled out of its popover into a small window of its own, as the web's quick panels come
// out of their tray: dragged by the grip along its foot, or (tools-20) by Pop Out — (1.3.83) the pin's menu's, where the
// popover's head had it until Open Full Screen took its place. It floats over everything — a quiz or an Alta
// assignment's popup too — where it is put, and stays open; its head is the popover's: the tool's name, (1.3.83) Open
// Full Screen at its right end (the whole tool, with what was begun in the panel, the panel put back) and, at its top
// left as a Mac panel has it, a plain ✕ that puts it back (Escape too). The panel never takes the app's activation (a
// nonactivating panel), so what is under it carries on as it was; it hides with the app when another app comes to the
// front. The pins themselves come to a popup's own bar (PopupPins), since the window's toolbar is under the sheet.
// (1.3.79) Out, it is moved by its ground, its head — the name too — and its grip (PanelMover), kept on the screen, and
// it comes out again where it was last put.

/// The panels out, by tool.
@MainActor
final class ToolFloats: ObservableObject {
    static let shared = ToolFloats()

    @Published private(set) var out: Set<ToolKind> = []
    private var panels: [ToolKind: ToolFloatPanel] = [:]
    private var dragMonitor: Any?

    func isOut(_ kind: ToolKind) -> Bool { out.contains(kind) }

    /// The panel made (or brought back) with its top-left corner where the popover's was — (1.3.79) or, not dragged out,
    /// where it was last put. `mouse`: the pointer, still down on the grip — the panel then follows it until the button
    /// is let go.
    func pullOut(_ kind: ToolKind, engine: Engine, topLeft: NSPoint, mouse: NSPoint?) {
        let panel = panels[kind] ?? make(kind, engine: engine)
        panels[kind] = panel
        let size = panel.frame.size
        let top = mouse == nil ? (Self.place(of: kind) ?? topLeft) : topLeft
        let origin = Self.kept(NSPoint(x: top.x, y: top.y - size.height), size: size, at: top)
        panel.setFrameOrigin(origin)
        panel.orderFront(nil)
        out.insert(kind)
        if let mouse { follow(panel, grab: NSPoint(x: mouse.x - origin.x, y: mouse.y - origin.y)) }
    }

    /// (tools-20) Out by Pop Out — (1.3.83) the pin's menu's: put where it was last put, else where a popover under the
    /// pointer is, else at the window's top right, and left there.
    func popOut(_ kind: ToolKind, engine: Engine) {
        let mouse = NSEvent.mouseLocation
        let popover = NSApp.windows.first { $0.isVisible && String(describing: type(of: $0)).contains("Popover") && $0.frame.contains(mouse) }
        let topLeft: NSPoint
        if let f = popover?.frame {
            topLeft = NSPoint(x: f.minX, y: f.maxY)
        } else if let w = engine.hostView.window {
            topLeft = NSPoint(x: w.frame.maxX - kind.popoverWidth - 48, y: w.frame.maxY - 112)
        } else {
            topLeft = NSPoint(x: mouse.x - kind.popoverWidth / 2, y: mouse.y)
        }
        pullOut(kind, engine: engine, topLeft: topLeft, mouse: nil)
    }

    /// Back into the tray (its ✕, Escape): the panel put away.
    func dock(_ kind: ToolKind) {
        guard let panel = panels[kind] else { return }
        panel.orderOut(nil)
        out.remove(kind)
    }

    /// A pin pressed while its panel is out: the panel brought to the front.
    func raise(_ kind: ToolKind) {
        guard let panel = panels[kind] else { return }
        panel.orderFront(nil)
        panel.makeKey()
    }

    /// (1.3.82) The panel's SwiftUI view sits in a plain view of the panel's own, never as its window's content view (as
    /// a popup's does, Shell/FloatingPopup.swift): a hosting view that is its window's content view sizes the window to
    /// what it shows each time the window is laid out, and a window kept at another size — by the screen, or by what it
    /// shows asking for more the more it is given — is sized again, pass after pass, until AppKit stops the app. It is
    /// sized once from what it shows, then by what it shows measuring itself (`ToolFloatRoot`), on the run loop's next
    /// turn.
    private func make(_ kind: ToolKind, engine: Engine) -> ToolFloatPanel {
        let panel = ToolFloatPanel(kind: kind)
        let host = NSHostingView(rootView: ToolFloatRoot(kind: kind, engine: engine, panel: panel))
        let holder = NSView(frame: NSRect(origin: .zero, size: panel.frame.size))
        host.frame = holder.bounds
        host.autoresizingMask = [.width, .height]
        holder.addSubview(host)
        panel.contentView = holder
        // (its first size, as large as what it shows asks; then it is never sized by the hosting view itself)
        host.sizingOptions = [.intrinsicContentSize]
        host.layoutSubtreeIfNeeded()
        let size = host.fittingSize
        host.sizingOptions = []
        if size.width > 1, size.height > 1 { panel.setContentSize(size) }
        return panel
    }

    /// The panel moved with the pointer until the mouse button is let go: the drag that pulled it out carries on, now
    /// with the panel (the popover it came out of is gone). Where it is let go is where it comes out next time.
    private func follow(_ panel: ToolFloatPanel, grab: NSPoint) {
        if let m = dragMonitor { NSEvent.removeMonitor(m); dragMonitor = nil }
        dragMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDragged, .leftMouseUp]) { [weak self, weak panel] event in
            guard let self, let panel else { return event }
            if event.type == .leftMouseUp {
                if let m = self.dragMonitor { NSEvent.removeMonitor(m); self.dragMonitor = nil }
                self.placed(panel)
                return event
            }
            let mouse = NSEvent.mouseLocation
            panel.setFrameOrigin(Self.kept(NSPoint(x: mouse.x - grab.x, y: mouse.y - grab.y), size: panel.frame.size, at: mouse))
            return event
        }
    }

    /// An origin that keeps the panel on the screen (its head at least).
    static func kept(_ origin: NSPoint, size: NSSize, in screen: NSRect) -> NSPoint {
        NSPoint(x: min(max(origin.x, screen.minX - size.width + 80), screen.maxX - 80),
                y: min(max(origin.y, screen.minY - size.height + 40), screen.maxY - size.height))
    }

    /// (1.3.79) Kept on the screen `point` is on (the pointer's, as a panel is moved to another display; a place
    /// remembered on a display no longer there: the main one's).
    static func kept(_ origin: NSPoint, size: NSSize, at point: NSPoint) -> NSPoint {
        let screen = NSScreen.screens.first { NSMouseInRect(point, $0.frame, false) } ?? NSScreen.main ?? NSScreen.screens.first
        guard let frame = screen?.visibleFrame else { return origin }
        return kept(origin, size: size, in: frame)
    }

    // MARK: (1.3.79) Where each panel was put

    /// Each tool's panel's top-left corner where it was last put, on the screen: it comes out there again.
    private static let placesKey = "SimplToolFloatPlaces"

    private static func place(of kind: ToolKind) -> NSPoint? {
        guard let s = (UserDefaults.standard.dictionary(forKey: placesKey) as? [String: String])?[kind.rawValue] else { return nil }
        return NSPointFromString(s)
    }

    /// A panel put down where it is now: moved by its ground, its head or its grip (PanelMover), or let go after the
    /// drag that pulled it out.
    func placed(_ window: NSWindow) {
        guard let panel = window as? ToolFloatPanel else { return }
        var places = UserDefaults.standard.dictionary(forKey: Self.placesKey) as? [String: String] ?? [:]
        places[panel.kind.rawValue] = NSStringFromPoint(NSPoint(x: panel.frame.minX, y: panel.frame.maxY))
        UserDefaults.standard.set(places, forKey: Self.placesKey)
    }
}

/// The window a pulled-out panel floats in: no frame of its own (the panel draws its own glass), over everything,
/// moved by its ground, its head and its grip (PanelMover: a SwiftUI panel is never moved by AppKit's "movable by its
/// background", every press landing on the hosting view), key when clicked (for typing into a tool) without taking the
/// app's activation.
final class ToolFloatPanel: NSPanel {
    let kind: ToolKind

    init(kind: ToolKind) {
        self.kind = kind
        super.init(contentRect: NSRect(x: 0, y: 0, width: kind.popoverWidth, height: 240),
                   styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView], backing: .buffered, defer: false)
        level = .floating
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isMovableByWindowBackground = true
        isReleasedWhenClosed = false
        animationBehavior = .utilityWindow
        collectionBehavior = [.fullScreenAuxiliary, .moveToActiveSpace]
        // (hidden with the app when another comes to the front — except in the screenshot suite, where several copies run at once)
        hidesOnDeactivate = UserDefaults.standard.string(forKey: "SimplShotFile") == nil
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    /// Escape: back into the tray.
    override func cancelOperation(_ sender: Any?) { ToolFloats.shared.dock(kind) }

    /// (1.3.82) What it shows has measured itself: the panel made that size, its top left where it is — only when the
    /// size is another by more than half a point, and never a size no window can take.
    func fit(_ s: CGSize) {
        guard s.width > 1, s.height > 1, s.width.isFinite, s.height.isFinite, s.width < 20_000, s.height < 20_000 else { return }
        let w = s.width.rounded(.up), h = s.height.rounded(.up)
        guard abs(w - frame.width) > 0.5 || abs(h - frame.height) > 0.5 else { return }
        setFrame(NSRect(x: frame.minX, y: frame.maxY - h, width: w, height: h), display: true)
        invalidateShadow()
    }
}

/// (1.3.82) A pulled-out panel's SwiftUI view: what it shows at its own size, at the panel's top, measured as it appears
/// and as it changes — the panel made that size on the run loop's next turn (`ToolFloatPanel.fit`), never while it is
/// being laid out.
private struct ToolFloatRoot: View {
    let kind: ToolKind
    let engine: Engine
    weak var panel: ToolFloatPanel?

    var body: some View {
        ToolFloatView(kind: kind, engine: engine)
            .environmentObject(engine)
            .fixedSize()
            .background {
                GeometryReader { g in
                    Color.clear
                        .onAppear { measured(g.size) }
                        .onChange(of: g.size) { _, s in measured(s) }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func measured(_ s: CGSize) {
        let target = self.panel
        DispatchQueue.main.async { target?.fit(s) }
    }
}

/// What a pulled-out panel shows: (tools-20) its ✕ and the tool's name at its head, as the popover's — no window lights,
/// whose green would have opened another view instead of zooming — (1.3.83) and, as the popover's, Open Full Screen at
/// its head's right end; and its quick use (the popover's). (1.3.79) Its ground, its head and its grip move it
/// (PanelMover); the controls over them take their own presses.
struct ToolFloatView: View {
    let kind: ToolKind
    let engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var floats = ToolFloats.shared
    /// What the quick version has begun, for the whole tool (QuickCarry).
    @State private var carry = QuickCarry()

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                QuickClose { floats.dock(kind) }
                IconTile(symbol: kind.symbol, color: kind.color, size: 28)
                    .overlay(PanelMover(help: "Drag to move the panel"))
                // (the name as a window's title: dragged, it moves the panel — (1.3.83) and a press on it no longer
                // opens the whole tool: Open Full Screen does, the one way to it)
                QuickPanelName(kind: kind)
                    .overlay(PanelMover(help: "Drag to move the panel"))
                PanelMover(help: "Drag to move the panel")
                    .frame(minWidth: 8, maxWidth: .infinity)
                    .frame(height: 28)
                QuickFullScreenButton(kind: kind, action: openFullScreen)
            }
            PinnedToolQuick(kind: kind) { info, files in openFull(info: info, files: files) }
                .onPreferenceChange(QuickCarryKey.self) { c in MainActor.assumeIsolated { carry = c } }
            GripBar(movesPanel: true)
                .padding(.bottom, -10)
        }
        .padding(16)
        .frame(width: kind.popoverWidth)
        .background(PanelMover()) // (its ground, under everything in it)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(.primary.opacity(0.12), lineWidth: 1))
    }

    /// (1.3.83) Open Full Screen: the panel put back, the tool whole over the window with what was begun in it.
    private func openFullScreen() {
        floats.dock(kind)
        tools.openFullScreen(kind, engine: engine, carry: carry)
    }

    private func openFull(info: [String: String] = [:], files: [URL] = []) {
        floats.dock(kind)
        // (1.3.79: in its popup over the window; 1.3.83: made afresh to take what it is handed if it is open already)
        tools.popUp(kind, engine: engine, files: files, info: info, afresh: !info.isEmpty || !files.isEmpty)
    }
}

/// (tools-20) A floating panel's close: a plain ✕ in a small circle at its top left, as a Mac panel's (a HUD's) is — it
/// puts the panel back in the tray. Brighter under the pointer.
struct QuickClose: View {
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            Image(systemName: "xmark")
                .font(.system(size: 9, weight: .bold))
                .foregroundStyle(.secondary)
                .frame(width: 20, height: 20)
                .background(Circle().fill(Color.primary.opacity(hover ? 0.16 : 0.08)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .animation(Motion.hover, value: hover)
        .help("Close (Esc). The pin opens it again.")
        .accessibilityLabel("Close")
    }
}

/// The grip along a panel's foot (the web's bcv-quick__grip): a short bar, brighter under the pointer.
struct GripBar: View {
    /// (1.3.79) On a floating panel: a press here moves the panel, as its ground and its head do.
    var movesPanel = false
    @State private var hover = false

    var body: some View {
        Capsule()
            .fill(.primary.opacity(hover ? 0.35 : 0.18))
            .frame(width: 46, height: 4)
            .frame(maxWidth: .infinity)
            .frame(height: 20)
            .contentShape(Rectangle())
            .onHover { hover = $0 }
            .overlay {
                if movesPanel { PanelMover(help: "Drag to move the panel", hover: { hover = $0 }) }
            }
    }
}

/// (1.3.79) A place a floating panel is moved by — its ground, its head, its grip: pressed and dragged, the panel goes
/// with the pointer, kept on the screen (ToolFloats.kept), and where it is let go is where it comes out next time
/// (ToolFloats.placed). (A press that did not move opened the whole tool on the name until 1.3.83, when Open Full Screen
/// became the one way to it.) It lies under the panel's controls, or over a part that has no press of its own, and
/// takes the first click even while the panel is not key. (The panel's window is "movable by its background", but no
/// press ever reached AppKit as one: SwiftUI's views are drawn in the one hosting view, which never lets a mouse-down
/// move its window.)
struct PanelMover: NSViewRepresentable {
    var help: String? = nil
    var hover: ((Bool) -> Void)? = nil

    func makeNSView(context: Context) -> MoverView { MoverView() }

    func updateNSView(_ v: MoverView, context: Context) {
        v.hover = hover
        v.toolTip = help
    }

    final class MoverView: NSView {
        var hover: ((Bool) -> Void)?
        private var tracking: NSTrackingArea?

        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
        // (it moves its window itself, kept on the screen and remembered)
        override var mouseDownCanMoveWindow: Bool { false }

        override func mouseDown(with event: NSEvent) {
            guard let window else { return }
            let start = NSEvent.mouseLocation
            let origin = window.frame.origin
            var moved = false
            // (the drag followed here until the button is let go, as a control tracks its own)
            while let e = window.nextEvent(matching: [.leftMouseDragged, .leftMouseUp]), e.type == .leftMouseDragged {
                let now = NSEvent.mouseLocation
                guard moved || abs(now.x - start.x) >= 3 || abs(now.y - start.y) >= 3 else { continue }
                moved = true
                let to = NSPoint(x: origin.x + now.x - start.x, y: origin.y + now.y - start.y)
                window.setFrameOrigin(ToolFloats.kept(to, size: window.frame.size, at: now))
            }
            if moved { ToolFloats.shared.placed(window) }
        }

        override func updateTrackingAreas() {
            super.updateTrackingAreas()
            if let tracking { removeTrackingArea(tracking) }
            let t = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self, userInfo: nil)
            addTrackingArea(t)
            tracking = t
        }

        override func mouseEntered(with event: NSEvent) { hover?(true) }
        override func mouseExited(with event: NSEvent) { hover?(false) }
    }
}

/// The grip along a pin's popover: dragged, the panel comes out of the popover into a window of its own, under the
/// pointer, and follows it until the button is let go.
struct PullOutGrip: View {
    let kind: ToolKind
    let engine: Engine
    let close: () -> Void
    @State private var pulled = false

    var body: some View {
        GripBar()
            .help("Drag to pull this out into a window of its own (or choose Pop Out in its pin's menu)")
            .gesture(DragGesture(minimumDistance: 4, coordinateSpace: .global).onChanged { _ in
                guard !pulled else { return }
                pulled = true
                let mouse = NSEvent.mouseLocation
                // (the popover's window is the one under the pointer; the panel is put where it was, then follows)
                let popover = NSApp.windows.first { $0.isVisible && String(describing: type(of: $0)).contains("Popover") && $0.frame.contains(mouse) }
                let f = popover?.frame ?? NSRect(x: mouse.x - kind.popoverWidth / 2, y: mouse.y, width: kind.popoverWidth, height: 0)
                ToolFloats.shared.pullOut(kind, engine: engine, topLeft: NSPoint(x: f.minX, y: f.maxY), mouse: mouse)
                close()
            })
    }
}

/// The tools pinned to the toolbar, on a popup's own bar (a quiz, an Alta assignment): the window's
/// toolbar is under a quiz's sheet, so its pins come here — (1.3.79) and stay on an assignment's popup, by the work —
/// each opening its quick panel over the popup; pulled out they float over it. (tools-6) As in the toolbar, the pins past
/// the bar's room in the Tools pull-down — (1.3.79) only those: every pin that fits in the bar is shown, the popup's name
/// keeping `titleRoom` points beside them, where tools-6 had three at most whatever the room (and never fewer than that:
/// a narrow quiz's bar, its clock and Instructions in it, keeps what it had).
struct PopupPins: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared

    /// What the pins leave the rest of the bar (the popup's name) at the least.
    private static let titleRoom: CGFloat = 240

    var body: some View {
        let total = tools.toolbarPins(timerActive: focus.active).count
        // (never fewer than 1.3.78 showed: three at most, two and the pull-down past that)
        let least = total <= ToolsCenter.toolbarRoom ? total : ToolsCenter.toolbarRoom - 1
        let leaving = LeavingRoom(room: Self.titleRoom)
        if total > 0 {
            // (the most pins that fit: all of them, else one fewer and the pull-down, and so on — the last when none fits)
            leaving {
                ViewThatFits(in: .horizontal) {
                    ForEach(Array(stride(from: total, through: least, by: -1)), id: \.self) { n in
                        bar(tools.toolbarSplit(timerActive: focus.active, fit: n))
                    }
                }
            }
            .layoutPriority(1)
        }
    }

    // (1.3.97) One small glass pill with the pins in it, as the window's toolbar groups them — each pin had been a
    // glass button of its own, a row of large capsules beside the popup's name. The pins are accessory-bar buttons
    // (plain, lit under the pointer), the pull-down the same, in a capsule of their own.
    private func bar(_ pins: (shown: [ToolKind], more: [ToolKind])) -> some View {
        HStack(spacing: 2) {
            ForEach(pins.shown) { kind in
                PinnedToolButton(kind: kind, engine: engine)
                    .labelStyle(.iconOnly)
                    .buttonStyle(.accessoryBar)
                    .frame(minWidth: 30, minHeight: 24)
            }
            if !pins.more.isEmpty {
                MorePinsMenu(kinds: pins.more, engine: engine)
                    .labelStyle(.iconOnly)
                    .menuStyle(.button)
                    .buttonStyle(.accessoryBar)
                    .fixedSize()
            }
        }
        .controlSize(.regular)
        .padding(.horizontal, 6)
        .padding(.vertical, 4)
        .glassCapsule()
    }
}

/// (1.3.79) Its one view offered the width it is offered less `room`: what it leaves its neighbours in a row.
private struct LeavingRoom: Layout {
    let room: CGFloat

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        guard let view = subviews.first else { return .zero }
        return view.sizeThatFits(ProposedViewSize(width: proposal.width.map { max($0 - room, 0) }, height: proposal.height))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        subviews.first?.place(at: bounds.origin, proposal: ProposedViewSize(bounds.size))
    }
}
