import AppKit
import SwiftUI

// (1.3.73) A pin's quick panel pulled out of its popover into a small window of its own, as the web's quick panels come
// out of their tray: (tools-20) by the popover's Pop Out button, or dragged by the grip along its foot. It floats over
// everything — a quiz or an Alta assignment's popup too — where it is put, and stays open; its head is the popover's:
// the tool's name (a press opens the whole tool) and, at its top left as a Mac panel has it, a plain ✕ that puts it back
// (Escape too). The panel never takes the app's activation (a nonactivating panel), so what is under it carries on as it
// was; it hides with the app when another app comes to the front. The pins themselves come to a popup's own bar
// (PopupPins), since the window's toolbar is under the sheet.

/// The panels out, by tool.
@MainActor
final class ToolFloats: ObservableObject {
    static let shared = ToolFloats()

    @Published private(set) var out: Set<ToolKind> = []
    private var panels: [ToolKind: ToolFloatPanel] = [:]
    private var dragMonitor: Any?

    func isOut(_ kind: ToolKind) -> Bool { out.contains(kind) }

    /// The panel made (or brought back) with its top-left corner where the popover's was. `mouse`: the pointer, still
    /// down on the grip — the panel then follows it until the button is let go.
    func pullOut(_ kind: ToolKind, engine: Engine, topLeft: NSPoint, mouse: NSPoint?) {
        let panel = panels[kind] ?? make(kind, engine: engine)
        panels[kind] = panel
        let size = panel.frame.size
        var origin = NSPoint(x: topLeft.x, y: topLeft.y - size.height)
        if let screen = NSScreen.main?.visibleFrame ?? NSScreen.screens.first?.visibleFrame { origin = Self.kept(origin, size: size, in: screen) }
        panel.setFrameOrigin(origin)
        panel.orderFront(nil)
        out.insert(kind)
        if let mouse { follow(panel, grab: NSPoint(x: mouse.x - origin.x, y: mouse.y - origin.y)) }
    }

    /// (tools-20) Out by the popover's Pop Out button: put where the popover was (the window under the pointer that
    /// pressed it), and left there.
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

    private func make(_ kind: ToolKind, engine: Engine) -> ToolFloatPanel {
        let panel = ToolFloatPanel(kind: kind)
        let host = NSHostingController(rootView: ToolFloatView(kind: kind, engine: engine).environmentObject(engine))
        host.sizingOptions = [.intrinsicContentSize, .preferredContentSize]
        panel.contentViewController = host
        host.view.layoutSubtreeIfNeeded()
        panel.setContentSize(host.view.fittingSize)
        return panel
    }

    /// The panel moved with the pointer until the mouse button is let go: the drag that pulled it out carries on, now
    /// with the panel (the popover it came out of is gone).
    private func follow(_ panel: ToolFloatPanel, grab: NSPoint) {
        if let m = dragMonitor { NSEvent.removeMonitor(m); dragMonitor = nil }
        dragMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDragged, .leftMouseUp]) { [weak self, weak panel] event in
            guard let self, let panel else { return event }
            if event.type == .leftMouseUp {
                if let m = self.dragMonitor { NSEvent.removeMonitor(m); self.dragMonitor = nil }
                return event
            }
            let mouse = NSEvent.mouseLocation
            var origin = NSPoint(x: mouse.x - grab.x, y: mouse.y - grab.y)
            if let screen = panel.screen?.visibleFrame ?? NSScreen.main?.visibleFrame { origin = Self.kept(origin, size: panel.frame.size, in: screen) }
            panel.setFrameOrigin(origin)
            return event
        }
    }

    /// An origin that keeps the panel on the screen (its head at least).
    private static func kept(_ origin: NSPoint, size: NSSize, in screen: NSRect) -> NSPoint {
        NSPoint(x: min(max(origin.x, screen.minX - size.width + 80), screen.maxX - 80),
                y: min(max(origin.y, screen.minY - size.height + 40), screen.maxY - size.height))
    }
}

/// The window a pulled-out panel floats in: no frame of its own (the panel draws its own glass), over everything,
/// movable by its background, key when clicked (for typing into a tool) without taking the app's activation.
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
}

/// What a pulled-out panel shows: (tools-20) its ✕ and the tool's name at its head, as the popover's — no window lights,
/// whose green would have opened another view instead of zooming — and its quick use (the popover's).
struct ToolFloatView: View {
    let kind: ToolKind
    let engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var floats = ToolFloats.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                QuickClose { floats.dock(kind) }
                IconTile(symbol: kind.symbol, color: kind.color, size: 28)
                QuickPanelName(kind: kind) { openFull() }
                Spacer(minLength: 8)
            }
            PinnedToolQuick(kind: kind) { info, files in openFull(info: info, files: files) }
            GripBar()
                .padding(.bottom, -10)
                .help("Drag anywhere on the panel to move it")
        }
        .padding(16)
        .frame(width: kind.popoverWidth)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(.primary.opacity(0.12), lineWidth: 1))
    }

    private func openFull(info: [String: String] = [:], files: [URL] = []) {
        floats.dock(kind)
        tools.popUp(kind, engine: engine, files: files, info: info) // (1.3.79: in its popup over the window)
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
    @State private var hover = false

    var body: some View {
        Capsule()
            .fill(.primary.opacity(hover ? 0.35 : 0.18))
            .frame(width: 46, height: 4)
            .frame(maxWidth: .infinity)
            .frame(height: 20)
            .contentShape(Rectangle())
            .onHover { hover = $0 }
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
            .help("Drag to pull this out into a window of its own (or press Pop Out)")
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

/// The tools pinned to the toolbar, on a popup's own bar (a quiz, an Alta or a Macmillan assignment): the window's
/// toolbar is under a quiz's sheet, so its pins come here — (1.3.79) and stay on an assignment's popup, by the work —
/// each opening its quick panel over the popup; pulled out they float over it. (tools-6) As in the toolbar: three at
/// most, past that two and the Tools pull-down.
struct PopupPins: View {
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var tools = ToolsCenter.shared
    @ObservedObject private var focus = FocusTimer.shared

    var body: some View {
        let pins = tools.toolbarSplit(timerActive: focus.active)
        if !pins.shown.isEmpty {
            GlassGroup(spacing: 8) {
                HStack(spacing: 8) {
                    ForEach(pins.shown) { kind in
                        PinnedToolButton(kind: kind, engine: engine)
                            .labelStyle(.iconOnly)
                            .glassButton()
                    }
                    if !pins.more.isEmpty {
                        MorePinsMenu(kinds: pins.more, engine: engine)
                            .labelStyle(.iconOnly)
                            .menuStyle(.button)
                            .glassButton()
                            .fixedSize()
                    }
                }
            }
        }
    }
}
