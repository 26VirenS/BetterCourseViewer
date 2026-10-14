import AppKit
import SwiftUI

// (1.3.79) Popups over the window that never stand in its way — every popup but a quiz being taken. A whole tool, a
// Knewton Alta or Macmillan assignment, the feedback on a piece of work, the rubric ring, What's New, Hand In, a new
// task or message, a counter's list and the app's short forms each come up in a window of their own over the main
// window (a child panel, `FloatingPopups`), not a sheet: nothing round it is dimmed or caught, so the sidebar, the
// toolbar, the search and the screen under it are used as ever, and going to another screen leaves it where it is,
// over the window, until it is closed — its ×, Escape while it has the keys, ⌘W, or its own Done. Dragged by its head
// (`popupHeader()`) it moves; it goes with the window when the window moves, and a large one is sized again as the
// window is. It comes in a little smaller and faded on a short ease-out (a fade alone under Reduce Motion) and goes the
// same way, a soft shadow under it. Still sheets, blocking the window: a quiz being taken (its timer and its
// integrity), the first run's setup, the sign-in, the tour, and the Mac's own confirmations.

/// Where a popup over the window sits, and how large it is.
enum PopupLayout {
    /// Most of the screen's side of the window — under the toolbar, past the sidebar while it is out — centred with a
    /// margin all round, and sized again as the window is: a whole tool, an Alta or Macmillan assignment, the feedback,
    /// the rubric.
    case large
    /// As large as what it shows asks (as a sheet is), centred across the screen's side of the window a little above
    /// its middle: What's New, Hand In, a new task or message, a short form.
    case fitted
    /// As large as what it shows asks, its top left at a point on the screen (AppKit's coordinates): a counter's list,
    /// over its tile.
    case at(CGPoint)
    /// As large as what it shows asks, just under a rect on the screen (AppKit's coordinates) from its start — or just
    /// over it when the screen has no room under, as a menu from it: a work's card, from its row in a counter's list.
    case under(CGRect)

    var isLarge: Bool {
        if case .large = self { return true }
        return false
    }
}

/// The popup a view is shown in, as the view sees it (`@Environment(\.popup)`): closed with `close()` — or called, as
/// SwiftUI's `dismiss` is — and moved by its head. Outside a popup it does nothing.
struct PopupHandle {
    fileprivate weak var panel: PopupPanel?

    init() {}

    fileprivate init(panel: PopupPanel) {
        self.panel = panel
    }

    /// Shown in a popup over the window.
    var inPopup: Bool { panel != nil }

    /// Closed by what it shows (its Close, Cancel or Done, a hand-in sent) — even while it is held.
    @MainActor
    func close() { panel?.closePopup(force: true) }

    @MainActor
    func callAsFunction() { close() }

    @MainActor
    fileprivate func hold(_ on: Bool) { panel?.held = on }

    /// (1.3.79) What it shows, measured by itself: the popup made that size, and no longer measured from outside — for
    /// one as large as what it shows asks that draws its own glass (a counter's list, a work's card) and knows its own
    /// size best.
    @MainActor
    func fit(_ size: CGSize) {
        guard let panel else { return }
        panel.selfSized = true
        // (on the run loop's next turn: the window is not sized again while it is being laid out)
        DispatchQueue.main.async { [weak panel] in panel?.fit(size) }
    }

    /// Closed by the student — its ×, as Escape and ⌘W — unless what it shows holds it open for now.
    @MainActor
    func userClose() { panel?.closePopup() }

    @MainActor
    fileprivate func dragged() { panel?.dragged() }

    @MainActor
    fileprivate func dragEnded() { panel?.dragEnded() }
}

private struct PopupHandleKey: EnvironmentKey {
    static let defaultValue = PopupHandle()
}

private struct PopupSizeKey: EnvironmentKey {
    static let defaultValue: CGSize? = nil
}

extension EnvironmentValues {
    /// The popup over the window this view is shown in (nothing, outside one).
    var popup: PopupHandle {
        get { self[PopupHandleKey.self] }
        set { self[PopupHandleKey.self] = newValue }
    }

    /// A large popup's size, for what it shows to lay itself out by (nil outside one).
    var popupSize: CGSize? {
        get { self[PopupSizeKey.self] }
        set { self[PopupSizeKey.self] = newValue }
    }
}

extension View {
    /// (1.3.79) A popup's head: dragged, the popup moves with the pointer (its buttons keep their presses). As it was,
    /// outside a popup.
    func popupHeader() -> some View {
        modifier(PopupHeader())
    }

    /// (1.3.79) While `held`, the popup this is in is not closed by Escape, ⌘W or a press of its × — as a sheet's
    /// interactive dismissal is turned off (a hand-in or a message on its way); what it shows can still close it.
    func popupCloseHeld(_ held: Bool) -> some View {
        modifier(PopupHold(held: held))
    }

    /// (1.3.79) A popup over the window while `item` is set, as `.sheet(item:)` puts one up — but never blocking the
    /// window (FloatingPopups); closed however it is closed, `item` is set to nil. `engine`: the window's (else the
    /// one this view is in).
    func floatingPopup<Item: Identifiable, V: View>(_ key: String, title: String, item: Binding<Item?>, layout: PopupLayout = .fitted,
                                                    engine: Engine? = nil, @ViewBuilder content: @escaping (Item) -> V) -> some View {
        modifier(FloatingPopupItem(key: key, title: title, item: item, layout: layout, engine: engine, popup: content))
    }

    /// (1.3.79) A popup over the window while `isPresented`, as `.sheet(isPresented:)` puts one up — never blocking the
    /// window; closed, `isPresented` is set to false and `onDismiss` told.
    func floatingPopup<V: View>(_ key: String, title: String, isPresented: Binding<Bool>, layout: PopupLayout = .fitted,
                                engine: Engine? = nil, onDismiss: (() -> Void)? = nil, @ViewBuilder content: @escaping () -> V) -> some View {
        modifier(FloatingPopupShown(key: key, title: title, shown: isPresented, layout: layout, engine: engine, onDismiss: onDismiss, popup: content))
    }
}

/// (1.3.79) A popup's ×, for one with no Close of its own: a plain ✕ in a small circle, as a floating panel's, brighter
/// under the pointer.
struct PopupCloseButton: View {
    @Environment(\.popup) private var popup
    @State private var hover = false

    var body: some View {
        Button {
            popup.userClose()
        } label: {
            Image(systemName: "xmark")
                .font(.system(size: 10, weight: .bold))
                .foregroundStyle(.secondary)
                .frame(width: 22, height: 22)
                .background(Circle().fill(Color.primary.opacity(hover ? 0.16 : 0.08)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .animation(Motion.hover, value: hover)
        .help("Close (Esc)")
        .accessibilityLabel("Close")
    }
}

private struct PopupHold: ViewModifier {
    let held: Bool
    @Environment(\.popup) private var popup

    func body(content: Content) -> some View {
        content
            .onAppear { popup.hold(held) }
            .onChange(of: held) { _, now in popup.hold(now) }
    }
}

private struct PopupHeader: ViewModifier {
    @Environment(\.popup) private var popup

    @ViewBuilder
    func body(content: Content) -> some View {
        if popup.inPopup {
            content
                .contentShape(Rectangle())
                .gesture(
                    DragGesture(minimumDistance: 3, coordinateSpace: .global)
                        .onChanged { _ in popup.dragged() }
                        .onEnded { _ in popup.dragEnded() }
                )
        } else {
            content
        }
    }
}

private struct FloatingPopupItem<Item: Identifiable, V: View>: ViewModifier {
    let key: String
    let title: String
    @Binding var item: Item?
    let layout: PopupLayout
    let engine: Engine?
    let popup: (Item) -> V
    @Environment(\.engineLink) private var link

    func body(content: Content) -> some View {
        content
            .onAppear {
                if let item { show(item) }
            }
            .onChange(of: item?.id) { _, _ in
                if let item { show(item) } else { (engine ?? link.engine)?.popups.close(key) }
            }
    }

    @MainActor
    private func show(_ it: Item) {
        let binding = $item
        // (another item in the same popup is shown afresh, never with the last one's state)
        (engine ?? link.acting).popups.present(key, title: title, layout: layout, onClose: { binding.wrappedValue = nil }) {
            popup(it).id(it.id)
        }
    }
}

private struct FloatingPopupShown<V: View>: ViewModifier {
    let key: String
    let title: String
    @Binding var shown: Bool
    let layout: PopupLayout
    let engine: Engine?
    let onDismiss: (() -> Void)?
    let popup: () -> V
    @Environment(\.engineLink) private var link

    func body(content: Content) -> some View {
        content
            .onAppear {
                if shown { show() }
            }
            .onChange(of: shown) { _, now in
                if now { show() } else { (engine ?? link.engine)?.popups.close(key) }
            }
    }

    @MainActor
    private func show() {
        let binding = $shown
        let dismissed = onDismiss
        (engine ?? link.acting).popups.present(key, title: title, layout: layout, onClose: {
            binding.wrappedValue = false
            dismissed?()
        }) {
            popup()
        }
    }
}

// MARK: - A window's popups

/// A window's popups (each window has its own: `Engine.popups`), by key: put up over the window, shown something new,
/// sized again as the window is, and closed — with the window too.
@MainActor
final class FloatingPopups {
    private weak var engine: Engine?
    private var panels: [String: PopupPanel] = [:]
    private var watches: [NSObjectProtocol] = []
    private weak var watched: NSWindow?

    /// The room round a popup, in the screen's side of the window.
    static let margin: CGFloat = 20

    init(engine: Engine) {
        self.engine = engine
        FloatingPopups.watchKeys()
    }

    func isOpen(_ key: String) -> Bool { panels[key] != nil }

    /// The popup under `key`, if it is up, brought to the front and given the keys.
    func raise(_ key: String) {
        panels[key]?.makeKeyAndOrderFront(nil)
    }

    /// What the popup under `key` shows, given afresh where it is, as it is (nothing, when it is not up).
    func update<V: View>(_ key: String, @ViewBuilder content: () -> V) {
        panels[key]?.model.content = AnyView(content())
    }

    /// A popup put up over the window — or, one already up under `key`, shown what is asked of it now where it is, and
    /// brought to the front. `onClose` is told when it closes, however it is closed. `ground`: drawn on the page's
    /// ground in a rounded card (false: what it shows draws its own, as a counter's glass list does).
    func present<V: View>(_ key: String, title: String, layout: PopupLayout = .fitted, ground: Bool = true, anchor: UnitPoint = .top,
                          onClose: (() -> Void)? = nil, @ViewBuilder content: () -> V) {
        put(key, title: title, layout: layout, ground: ground, anchor: anchor, onClose: onClose, view: AnyView(content()), tries: 6)
    }

    /// A popup closed — its ×, Escape, ⌘W, what it shows saying it is done, or its window closing — and whoever put it
    /// up told.
    func close(_ key: String, animated: Bool = true) {
        guard let panel = panels.removeValue(forKey: key) else { return }
        panel.goOut(animated: animated)
        panel.onClose?()
    }

    /// Every popup of the window closed (the window closing).
    func closeAll() {
        for key in Array(panels.keys) { close(key, animated: false) }
    }

    private func put(_ key: String, title: String, layout: PopupLayout, ground: Bool, anchor: UnitPoint, onClose: (() -> Void)?,
                     view: AnyView, tries: Int) {
        if let panel = panels[key] {
            panel.onClose = onClose
            panel.title = title
            panel.model.content = view
            panel.model.ground = ground
            panel.moveTo(layout)
            panel.makeKeyAndOrderFront(nil)
            return
        }
        guard let engine, let window = engine.hostView.window else {
            // (asked for before the window is up — a launch's: asked again a moment later, a few times)
            if tries > 0 {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
                    self?.put(key, title: title, layout: layout, ground: ground, anchor: anchor, onClose: onClose, view: view, tries: tries - 1)
                }
            }
            return
        }
        let panel = PopupPanel(key: key, layout: layout, title: title)
        panel.owner = self
        panel.over = window
        panel.onClose = onClose
        panel.model.anchor = anchor
        panel.model.ground = ground
        panel.model.content = view
        let host = NSHostingView(rootView: PopupRoot(model: panel.model, engine: engine, handle: PopupHandle(panel: panel)))
        host.sizingOptions = [] // (the popup is sized here, never by what it shows pulling at the window)
        // (in a plain view of the popup's own, sized with it, not as the window's content view: a hosting view that is
        // its window's content view sizes the window to what it shows each time it is laid out — a tool whose content
        // asks for more room the more it is given grew its popup past the screen, pass after pass, until AppKit stopped
        // the app for laying the window out too many times)
        let holder = NSView(frame: NSRect(origin: .zero, size: panel.frame.size))
        host.frame = holder.bounds
        host.autoresizingMask = [.width, .height]
        holder.addSubview(host)
        panel.contentView = holder
        panels[key] = panel
        watch(window)
        if layout.isLarge {
            // (one large popup over another opens a step down and along, so both are seen)
            let others = panels.values.filter { $0 !== panel && $0.popupLayout.isLarge }.count
            panel.moved = CGSize(width: CGFloat(others) * 28, height: -CGFloat(others) * 28)
            panel.place()
            panel.comeIn()
            panel.placeAgainSoon()
        } else {
            panel.prepare()
        }
    }

    /// The window watched: sized again, its large popups are too; closed, its popups go with it.
    private func watch(_ window: NSWindow) {
        guard watched !== window else { return }
        for w in watches { NotificationCenter.default.removeObserver(w) }
        watched = window
        let center = NotificationCenter.default
        watches = [
            center.addObserver(forName: NSWindow.didResizeNotification, object: window, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    if let self { self.windowResized() }
                }
            },
            center.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    if let self { self.closeAll() }
                }
            },
        ]
    }

    private func windowResized() {
        for panel in panels.values where panel.popupLayout.isLarge { panel.place() }
    }

    /// (1.3.79) The window's sidebar is out, as the window last said (`sidebarMoved`): a large popup keeps clear of it.
    /// (Its view is still found where it was once it is folded away, so where it stands is not enough to say.)
    private(set) var sidebarShown = true

    /// The window's sidebar shown or hidden: its large popups placed again in the screen's side of the window once the
    /// sidebar has moved — one put up while the sidebar was out kept clear of where it had been, off the window's middle,
    /// and one put up while it was folded away lay over it when it came out.
    func sidebarMoved(shown: Bool) {
        sidebarShown = shown
        // (and again once it has surely finished sliding: placed while it was still on its way in, a popup lay over it)
        for delay in [0.6, 1.6] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                self?.windowResized()
            }
        }
    }

    // MARK: Where a popup goes

    /// The screen's side of a window, on the screen: under its toolbar, past its sidebar while that is out (`sidebar`).
    static func area(of window: NSWindow, sidebar: Bool = true) -> NSRect {
        var r = window.convertToScreen(window.contentLayoutRect)
        if sidebar, let edge = sidebarEdge(in: window), edge > 40, edge < r.width * 0.5 {
            r.origin.x += edge
            r.size.width -= edge
        }
        return r
    }

    /// (1.3.79) Where a window's sidebar ends, across its content: as its view says it stands (TourFrames), else from the
    /// split view the window's columns are in — a tool's popup put up as the window opened, before the sidebar's view had
    /// said where it is, lay over the sidebar.
    private static func sidebarEdge(in window: NSWindow) -> CGFloat? {
        if let side = TourFrames.shared.rect(.sidebar, in: window), side.maxX > 40 { return side.maxX }
        // (the outermost split view across the window: its first pane, unless folded away)
        var queue: [NSView] = window.contentView.map { [$0] } ?? []
        var i = 0
        while i < queue.count {
            let v = queue[i]
            i += 1
            if let split = v as? NSSplitView, split.isVertical, split.arrangedSubviews.count >= 2 {
                let first = split.arrangedSubviews[0]
                guard !split.isSubviewCollapsed(first), !first.isHidden else { return nil }
                return first.convert(first.bounds, to: nil).maxX
            }
            queue.append(contentsOf: v.subviews)
        }
        return nil
    }

    /// Where a large popup opens over a window: the screen's side of it less the margin all round (no wider or taller
    /// than a tool needs), centred — then as far as it was moved.
    static func largeFrame(over window: NSWindow, moved: CGSize, sidebar: Bool = true) -> NSRect {
        let a = area(of: window, sidebar: sidebar)
        let w = max(320, min(a.width - margin * 2, 1480))
        let h = max(260, min(a.height - margin * 2, 1040))
        return NSRect(x: (a.midX - w / 2 + moved.width).rounded(), y: (a.midY - h / 2 + moved.height).rounded(), width: w, height: h)
    }

    /// A popup `s` large just under `r` (8 points in from its start), or just over it when the screen has no room under
    /// it — on the roomier side when there is room for neither (then kept on the screen).
    static func under(_ r: CGRect, size s: CGSize, on screen: NSScreen?) -> NSRect {
        let gap: CGFloat = 12
        let below = NSRect(x: r.minX + 8, y: r.minY - gap - s.height, width: s.width, height: s.height)
        let above = NSRect(x: r.minX + 8, y: r.maxY + gap, width: s.width, height: s.height)
        guard let v = (screen ?? NSScreen.main)?.visibleFrame else { return below }
        if below.minY >= v.minY { return below }
        if above.maxY <= v.maxY { return above }
        return r.minY - v.minY >= v.maxY - r.maxY ? below : above
    }

    /// A popup's frame all on the screen where it fits there (moved, never shrunk; a taller one with its top on it).
    static func within(_ f: NSRect, on screen: NSScreen?) -> NSRect {
        guard let v = (screen ?? NSScreen.main)?.visibleFrame else { return f }
        var r = f
        if r.width <= v.width { r.origin.x = min(max(r.minX, v.minX), v.maxX - r.width) }
        r.origin.y = r.height <= v.height ? min(max(r.minY, v.minY), v.maxY - r.height) : v.maxY - r.height
        return r
    }

    /// A popup being dragged kept with its head on the screen (it may go past the window, and partly off the screen).
    static func kept(_ f: NSRect, on screen: NSScreen?) -> NSRect {
        guard let v = (screen ?? NSScreen.main)?.visibleFrame else { return f }
        var r = f
        r.origin.x = min(max(r.minX, v.minX - r.width + 120), v.maxX - 120)
        r.origin.y = max(min(r.minY, v.maxY - r.height), v.minY - r.height + 60)
        return r
    }

    /// The popup motion: in on a short ease-out, out a little quicker (the scale is left out under Reduce Motion, where
    /// the popup is drawn).
    static func motion(in coming: Bool) -> Animation {
        coming ? .easeOut(duration: 0.22) : .easeIn(duration: 0.16)
    }

    private static var keyWatch: Any?

    /// ⌘W while a popup has the keys closes the popup, not the window under it.
    private static func watchKeys() {
        guard keyWatch == nil else { return }
        keyWatch = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { e in
            guard e.modifierFlags.intersection(.deviceIndependentFlagsMask) == .command,
                  e.charactersIgnoringModifiers?.lowercased() == "w",
                  let panel = NSApp.keyWindow as? PopupPanel else { return e }
            panel.closePopup()
            return nil
        }
    }
}

// MARK: - A popup's window

/// What a popup shows, and whether it is in at its place.
@MainActor
final class PopupModel: ObservableObject {
    @Published var content = AnyView(EmptyView())
    /// In at its place (the open motion's end); false as it comes in and as it goes.
    @Published var out = false
    /// A large popup's size (what it shows is laid out at it); nil: as large as what it shows asks.
    @Published var size: CGSize?
    /// Drawn on the page's ground in a rounded card (else what it shows draws its own).
    @Published var ground = true
    /// Where it grows from as it comes in.
    var anchor: UnitPoint = .top
    fileprivate weak var panel: PopupPanel?
}

/// A popup's window: no frame of its own (the card draws its corners, the window server its shadow), a child of the
/// window it is over — above it, and moving with it — key when clicked, for typing into it, without ever being the main
/// window (the window under it keeps its toolbar lit and its menus).
final class PopupPanel: NSPanel {
    static let radius: CGFloat = 18

    /// Its key among its window's popups.
    let popupKey: String
    fileprivate(set) var popupLayout: PopupLayout
    let model = PopupModel()
    fileprivate var onClose: (() -> Void)?
    fileprivate weak var owner: FloatingPopups?
    /// The window it is over.
    fileprivate weak var over: NSWindow?
    /// How far a large popup was moved from where it opens (kept as the window is sized again).
    fileprivate var moved = CGSize.zero
    /// (1.3.79) Dragged since it was put where it opens: sized again, it keeps its top where it was left.
    private var userMoved = false
    /// Put where it opens (a fitted one once what it shows has been measured), and come in.
    private var placed = false
    private var shown = false
    private var closing = false
    /// Held open by what it shows (`popupCloseHeld`): Escape, ⌘W and its × do nothing meanwhile.
    fileprivate var held = false
    /// (1.3.79) What it shows measures itself (`PopupHandle.fit`): the measure from outside is not taken.
    fileprivate var selfSized = false
    private var drag: (mouse: NSPoint, origin: NSPoint)?
    private var dragWatch: Any?

    init(key: String, layout: PopupLayout, title: String) {
        popupKey = key
        popupLayout = layout
        super.init(contentRect: NSRect(x: 0, y: 0, width: 480, height: 360), styleMask: [.borderless], backing: .buffered, defer: false)
        self.title = title
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        isRestorable = false
        hidesOnDeactivate = false
        isFloatingPanel = false
        isExcludedFromWindowsMenu = true
        animationBehavior = .none
        collectionBehavior = [.fullScreenAuxiliary]
        model.panel = self
    }

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    /// Escape — while it has the keys, and nothing in it took the key for itself — closes it.
    override func cancelOperation(_ sender: Any?) { closePopup() }

    /// File ▸ Close while it has the keys closes it, not the window under it.
    override func performClose(_ sender: Any?) { closePopup() }

    /// Closed by the student (Escape, ⌘W, its ×) — not while what it shows holds it open — or, `force`, by what it shows.
    func closePopup(force: Bool = false) {
        guard force || !held else {
            NSSound.beep()
            return
        }
        if let owner { owner.close(popupKey) } else { goOut(animated: true) }
    }

    // MARK: Placing

    /// Put where it opens: a large one sized from the window (as far along as it was moved), a fitted one centred
    /// across the screen's side of the window a little above its middle, one at a point with its top left there — or,
    /// already placed, sized again with its top and its middle kept where they are.
    fileprivate func place(fitting size: CGSize? = nil) {
        guard let over else { return }
        let m = FloatingPopups.margin
        var f: NSRect
        switch popupLayout {
        case .large:
            f = FloatingPopups.largeFrame(over: over, moved: moved, sidebar: owner?.sidebarShown ?? true)
            model.size = f.size
            f = moved == .zero ? FloatingPopups.within(f, on: over.screen) : FloatingPopups.kept(f, on: over.screen)
            if OpenClock.verbose, f != frame {
                NSLog("[Simpl] popup %@: %.0f × %.0f, %.0f in from the window (sidebar %@)", popupKey, f.width, f.height,
                      f.minX - over.frame.minX, (owner?.sidebarShown ?? true) ? "out" : "away")
            }
        case .fitted:
            let s = size ?? frame.size
            if placed {
                f = NSRect(x: (frame.midX - s.width / 2).rounded(), y: frame.maxY - s.height, width: s.width, height: s.height)
            } else {
                let a = FloatingPopups.area(of: over, sidebar: owner?.sidebarShown ?? true)
                let top = max(m, (a.height - s.height) * 0.3)
                f = NSRect(x: (a.midX - s.width / 2).rounded(), y: (a.maxY - top - s.height).rounded(), width: s.width, height: s.height)
            }
            f = FloatingPopups.within(f, on: over.screen)
        case .at(let p):
            let s = size ?? frame.size
            f = placed ? NSRect(x: frame.minX, y: frame.maxY - s.height, width: s.width, height: s.height)
                : NSRect(x: p.x, y: p.y - s.height, width: s.width, height: s.height)
            f = FloatingPopups.within(f, on: over.screen)
        case .under(let r):
            // (under its row, or over it, again as it grows — until it is dragged somewhere of its own)
            let s = size ?? frame.size
            f = placed && userMoved ? NSRect(x: frame.minX, y: frame.maxY - s.height, width: s.width, height: s.height)
                : FloatingPopups.under(r, size: s, on: over.screen)
            f = FloatingPopups.within(f, on: over.screen)
        }
        setFrame(f, display: true)
        placed = true
        invalidateShadow()
    }

    /// (1.3.79) A large popup placed again a moment after it came in, and once more a little later: one put up as the
    /// window opened (a tool asked for at launch) was placed before the sidebar had said where it is, and lay over the
    /// sidebar until the window was next sized. Not while it is being dragged; one dragged keeps how far it was moved.
    fileprivate func placeAgainSoon() {
        for delay in [0.5, 1.5] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                guard let self, !self.closing, self.drag == nil, self.popupLayout.isLarge else { return }
                self.place()
            }
        }
    }

    /// Asked to show something under a new layout: a popup at a point or by a rect moved to the new one (a counter's
    /// list, opened from another tile; a work's card, from another row); any other kept where it is.
    fileprivate func moveTo(_ next: PopupLayout) {
        switch (next, popupLayout) {
        case (.at(let p), .at(let was)) where p == was: return
        case (.under(let r), .under(let was)) where r == was: return
        case (.at, _), (.under, _): break
        default: return
        }
        popupLayout = next
        placed = false
        userMoved = false
        place()
    }

    /// (a fitted popup) Ordered in unseen, at a guess, for what it shows to be laid out and measured: in it comes once
    /// it has been (`fit`), or a moment on at the latest.
    fileprivate func prepare() {
        guard let over, !shown else { return } // (measured already, and in)
        if !placed {
            let a = FloatingPopups.area(of: over, sidebar: owner?.sidebarShown ?? true)
            setFrame(NSRect(x: a.midX - 240, y: a.maxY - 420, width: 480, height: 360), display: false)
        }
        if parent == nil { over.addChildWindow(self, ordered: .above) }
        contentView?.layoutSubtreeIfNeeded()
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { [weak self] in
            guard let self, !self.shown else { return }
            if !self.placed { self.place() }
            self.comeIn()
        }
    }

    /// What a popup that is as large as it asks shows has been measured: the window made its size — its top kept — and,
    /// the first time, in it comes.
    fileprivate func fit(_ s: CGSize) {
        // (never a size no window can take: a measure that is not a number, or larger than any screen)
        guard model.size == nil, !closing, s.width > 1, s.height > 1, s.width.isFinite, s.height.isFinite,
              s.width < 20_000, s.height < 20_000 else { return }
        if placed && abs(s.width - frame.width) < 0.5 && abs(s.height - frame.height) < 0.5 { return }
        if OpenClock.verbose { NSLog("[Simpl] popup %@: %.0f × %.0f", popupKey, s.width, s.height) }
        place(fitting: CGSize(width: s.width.rounded(.up), height: s.height.rounded(.up)))
        if !shown { comeIn() }
    }

    /// In it comes: up over the window, given the keys, grown to its place on the popup motion.
    fileprivate func comeIn() {
        guard let over, !closing, !shown else { return }
        shown = true
        if parent == nil { over.addChildWindow(self, ordered: .above) }
        makeKeyAndOrderFront(nil)
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            withAnimation(FloatingPopups.motion(in: true)) { self.model.out = true }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self, !self.closing else { return }
            self.invalidateShadow()
        }
    }

    /// Out it goes the way it came, then away — what it showed let go first, so it is told it has gone.
    fileprivate func goOut(animated: Bool) {
        guard !closing else { return }
        closing = true
        dragEnded()
        let wasKey = isKeyWindow
        let gone = { [self] in
            model.content = AnyView(EmptyView())
            parent?.removeChildWindow(self)
            orderOut(nil)
            if wasKey, let over, over.isVisible { over.makeKey() }
            DispatchQueue.main.async { [self] in contentView = nil }
        }
        guard animated, shown else {
            gone()
            return
        }
        hasShadow = false
        withAnimation(FloatingPopups.motion(in: false)) { model.out = false }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) { gone() }
    }

    // MARK: Moving

    /// Its head dragged: the popup follows the pointer until the button is let go.
    fileprivate func dragged() {
        guard drag == nil, !closing else { return }
        userMoved = true
        drag = (NSEvent.mouseLocation, frame.origin)
        dragWatch = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDragged, .leftMouseUp]) { [weak self] e in
            guard let self, let d = self.drag else { return e }
            if e.type == .leftMouseUp {
                self.dragEnded()
                return e
            }
            let m = NSEvent.mouseLocation
            let f = NSRect(origin: NSPoint(x: d.origin.x + m.x - d.mouse.x, y: d.origin.y + m.y - d.mouse.y), size: self.frame.size)
            self.setFrameOrigin(FloatingPopups.kept(f, on: self.screen).origin)
            return e
        }
    }

    /// Let go: a large popup keeps how far it was moved, for when the window is sized again.
    fileprivate func dragEnded() {
        guard drag != nil else { return }
        drag = nil
        if let w = dragWatch { NSEvent.removeMonitor(w) }
        dragWatch = nil
        if popupLayout.isLarge, let over {
            let home = FloatingPopups.largeFrame(over: over, moved: .zero, sidebar: owner?.sidebarShown ?? true)
            moved = CGSize(width: frame.minX - home.minX, height: frame.minY - home.minY)
        }
    }
}

/// A popup's window, drawn: what it shows — on the page's ground in a rounded card with a hairline edge, unless it
/// draws its own — at the popup's size (or its own, measured), coming in and going on the popup motion; given the
/// window's engine, its accent, and the popup itself to close and move it by.
private struct PopupRoot: View {
    @ObservedObject var model: PopupModel
    let engine: Engine
    let handle: PopupHandle
    @ObservedObject private var appearance = AppearanceStore.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Group {
            if let size = model.size {
                card.frame(width: size.width, height: size.height)
            } else {
                // (measured as it appears and as it changes — a measure handed up as a preference never reached the
                // popup from a hosting view that is not its window's content view, and every popup as large as what it
                // shows asks stayed at the size it was guessed at, what it showed cut off at its sides)
                card
                    .fixedSize()
                    .background {
                        GeometryReader { g in
                            Color.clear
                                .onAppear { measured(g.size) }
                                .onChange(of: g.size) { _, s in measured(s) }
                        }
                    }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .scaleEffect(model.out || reduceMotion ? 1 : 0.96, anchor: model.anchor)
        .opacity(model.out ? 1 : 0)
        .environment(\.popup, handle)
        .environment(\.popupSize, model.size)
        .environmentObject(engine)
        .environmentObject(engine.whereabouts)
        .environment(\.engineLink, EngineLink(engine: engine))
        .tint(appearance.accentColor)
    }

    @ViewBuilder
    private var card: some View {
        let shape = RoundedRectangle(cornerRadius: PopupPanel.radius, style: .continuous)
        if model.ground {
            model.content
                .background(PageGround())
                // (a strip along its top edge moves it too, for what shows no head of its own — a short form)
                .overlay(alignment: .top) { Color.clear.frame(height: 10).popupHeader() }
                .clipShape(shape)
                .overlay(shape.strokeBorder(Theme.edge, lineWidth: 1))
        } else {
            model.content
        }
    }

    /// What it shows, measured from outside: the popup made that size — on the run loop's next turn (the window is not
    /// sized again while it is being laid out), and never for what measures itself (`PopupHandle.fit`).
    private func measured(_ s: CGSize) {
        let panel = model.panel
        DispatchQueue.main.async {
            if panel?.selfSized == false { panel?.fit(s) }
        }
    }
}
