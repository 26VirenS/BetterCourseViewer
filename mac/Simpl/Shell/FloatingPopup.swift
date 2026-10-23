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
// (1.3.80) A large one spans the window's whole content, over the sidebar too, and its edges and corners drag it larger
// or smaller — the size picked kept for next time, a tool's for that tool (`PopupSizes`).

/// Where a popup over the window sits, and how large it is.
enum PopupLayout {
    /// (1.3.80) The window's whole content — under the toolbar, over the sidebar — less a margin all round, centred and
    /// sized again as the window is; dragged by its edges and corners, it takes the size the student gives it, kept for
    /// next time under its size key (a tool's own): a whole tool, an Alta or Macmillan assignment, the feedback, the
    /// rubric. (1.3.79 kept it clear of the sidebar while the sidebar was out: a tool had 740 points of a window 970 wide.)
    case large
    /// As large as what it shows asks (as a sheet is), (1.3.82) in the middle of the whole window: What's New, Hand In, a
    /// new task or message, a short form.
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
    /// (1.3.155) The keys in the order their popups came up: the last a large one's is the one New Window takes.
    private var order: [String] = []
    /// (1.3.155) Popups made windows of their own (New Window): no longer over the window, closed with it still.
    private var outside: [PopupPanel] = []

    /// (1.3.155) Posted (object: these popups) as a popup comes up, goes, or is made a window of its own.
    static let changed = Notification.Name("SimplPopupsChanged")
    private var watches: [NSObjectProtocol] = []
    private weak var watched: NSWindow?

    /// The room round a popup, in the screen's side of the window.
    static let margin: CGFloat = 20

    init(engine: Engine) {
        self.engine = engine
        FloatingPopups.watchKeys()
    }

    func isOpen(_ key: String) -> Bool { panels[key] != nil }

    /// (1.3.80) The keys of the popups up now (a live check waits while a dialog is up: Engine/LiveRefresh.swift).
    var openKeys: [String] { Array(panels.keys) }

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
    /// ground in a rounded card (false: what it shows draws its own, as a counter's glass list does). `sizeKey`: what a
    /// large popup's size is kept under (a tool's own, `tool.dna`; nil, `key` up to a colon — "feedback:…" is "feedback").
    func present<V: View>(_ key: String, title: String, layout: PopupLayout = .fitted, ground: Bool = true, anchor: UnitPoint = .top,
                          sizeKey: String? = nil, onClose: (() -> Void)? = nil, @ViewBuilder content: () -> V) {
        let kept = sizeKey ?? String(key.split(separator: ":", maxSplits: 1).first ?? Substring(key))
        put(key, title: title, layout: layout, ground: ground, anchor: anchor, sizeKey: kept, onClose: onClose, view: AnyView(content()), tries: 6)
    }

    /// A popup closed — its ×, Escape, ⌘W, what it shows saying it is done, or its window closing — and whoever put it
    /// up told.
    func close(_ key: String, animated: Bool = true) {
        guard let panel = panels.removeValue(forKey: key) else { return }
        order.removeAll { $0 == key }
        panel.goOut(animated: animated)
        panel.onClose?()
        said()
    }

    /// Every popup of the window closed (the window closing) — those made windows of their own too.
    func closeAll() {
        for key in Array(panels.keys) { close(key, animated: false) }
        for panel in outside { closedOutside(panel, animated: false) }
    }

    // MARK: New Window (1.3.155)

    /// The large popup up last (a tool, an Alta or Macmillan assignment, the feedback, the rubric), which New Window
    /// makes a window of its own.
    private var frontLarge: PopupPanel? {
        order.reversed().lazy.compactMap { self.panels[$0] }.first { $0.popupLayout.isLarge }
    }

    /// A large popup is up: the menu bar's tab (Shell/MenuBarTab.swift) offers New Window.
    var canOpenInWindow: Bool { frontLarge != nil }

    /// New Window: the large popup up last made a window of its own, as it is — what it shows goes on where it was (an
    /// Alta session, a tool part-way) — no longer over the window or moving with it, in the window list, in its own
    /// full-screen space when the window is in full screen.
    func openFrontInWindow() {
        guard let panel = frontLarge, let over = panel.over else { return }
        panels.removeValue(forKey: panel.popupKey)
        order.removeAll { $0 == panel.popupKey }
        outside.append(panel)
        panel.becomeWindow(fullScreen: over.styleMask.contains(.fullScreen))
        said()
    }

    /// A popup made a window of its own closed (its Close, ⌘W, its window's ×, or the window it came from closing).
    fileprivate func closedOutside(_ panel: PopupPanel, animated: Bool = true) {
        outside.removeAll { $0 === panel }
        panel.goOut(animated: animated)
        panel.onClose?()
    }

    private func said() {
        NotificationCenter.default.post(name: Self.changed, object: self)
    }

    private func put(_ key: String, title: String, layout: PopupLayout, ground: Bool, anchor: UnitPoint, sizeKey: String,
                     onClose: (() -> Void)?, view: AnyView, tries: Int) {
        if let panel = panels[key] {
            panel.onClose = onClose
            panel.title = title
            panel.model.content = view
            panel.model.ground = ground
            panel.moveTo(layout)
            panel.keepSize(under: sizeKey) // (1.3.80: another tool in the tool's popup, at that tool's size)
            panel.makeKeyAndOrderFront(nil)
            return
        }
        guard let engine, let window = engine.hostView.window else {
            // (asked for before the window is up — a launch's: asked again a moment later, a few times)
            if tries > 0 {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
                    self?.put(key, title: title, layout: layout, ground: ground, anchor: anchor, sizeKey: sizeKey, onClose: onClose,
                              view: view, tries: tries - 1)
                }
            }
            return
        }
        let panel = PopupPanel(key: key, layout: layout, title: title, sizeKey: sizeKey)
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
        if layout.isLarge {
            // (1.3.80) its edges and corners, over what it shows, to resize it by
            let edges = PopupEdges(frame: holder.bounds)
            edges.autoresizingMask = [.width, .height]
            edges.panel = panel
            holder.addSubview(edges)
        }
        panel.contentView = holder
        panels[key] = panel
        order.append(key)
        said()
        watch(window)
        if layout.isLarge {
            // (one large popup over another opens a step down and along, so both are seen — (1.3.80) that much smaller,
            // so it still ends inside the window, which the first now fills)
            let others = panels.values.filter { $0 !== panel && $0.popupLayout.isLarge }.count
            panel.cascade = CGFloat(others) * 28
            panel.moved = CGSize(width: panel.cascade / 2, height: -panel.cascade / 2)
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

    /// (1.3.79) The window's sidebar is out, as the window last said (`sidebarMoved`): a fitted popup is centred clear of
    /// it. (Its view is still found where it was once it is folded away, so where it stands is not enough to say.)
    private(set) var sidebarShown = true
    /// (1.3.79) The window has a sidebar and has said whether it is out (a window without one never does).
    private(set) var sidebarSaid = false

    /// The window's sidebar shown or hidden, for the next fitted popup to be centred by. (1.3.80) A large popup spans the
    /// whole window, over the sidebar, so the sidebar's moving no longer moves it — 1.3.79 placed it again clear of the
    /// sidebar once the sidebar had moved, and over its first seconds, until the sidebar's view had said where it ends.
    func sidebarMoved(shown: Bool) {
        sidebarShown = shown
        sidebarSaid = true
    }

    // MARK: Where a popup goes

    /// (1.3.79) Where a sidebar last said it ends across its window — at first its usual width (MainShell's ideal 270)
    /// and the inset round it. A sidebar's view says where it stands only some seconds after the window opens, and a
    /// tool's popup put up as the window opened lay over the sidebar until then.
    private static var sidebarEdge: CGFloat = 278

    /// The screen's side of a window, on the screen: under its toolbar, past its sidebar while that is out (`sidebar`) —
    /// where its view says it ends, or (`guessed`: the window has a sidebar, out) where it last did.
    static func area(of window: NSWindow, sidebar: Bool = true, guessed: Bool = false) -> NSRect {
        var r = window.convertToScreen(window.contentLayoutRect)
        guard sidebar else { return r }
        var edge: CGFloat?
        if let side = TourFrames.shared.rect(.sidebar, in: window), side.maxX > 40 {
            edge = side.maxX
            if side.maxX < r.width * 0.5 { sidebarEdge = side.maxX }
        } else if guessed {
            edge = sidebarEdge
        }
        if let edge, edge < r.width * 0.5 {
            r.origin.x += edge
            r.size.width -= edge
        }
        return r
    }

    /// (1.3.80) The room a large popup has over a window: the window's whole content under its toolbar — over its
    /// sidebar, out or not — as much of it as is on the screen, less the margin all round.
    static func largeRoom(over window: NSWindow) -> NSRect {
        var a = area(of: window, sidebar: false)
        if let v = (window.screen ?? NSScreen.main)?.visibleFrame, a.intersects(v) { a = a.intersection(v) }
        return NSRect(x: a.minX + margin, y: a.minY + margin, width: max(0, a.width - margin * 2), height: max(0, a.height - margin * 2))
    }

    /// The most a large popup is made unless its student makes it larger: past 1480 × 1040, a tool has nothing more to
    /// show.
    static let largeCap = CGSize(width: 1480, height: 1040)

    /// Where a large popup opens over a window: (1.3.80) its room (`largeRoom`: the whole window, over the sidebar, less
    /// the margin) as far as `largeCap`, or the size the student gave it by its edges (`chosen`, a side nil filling the
    /// room) kept within the room — so a window made smaller shrinks it, and made larger again gives its size back;
    /// `less` points smaller each way (a popup opened a step down and along over another); centred, then as far as it
    /// was moved.
    static func largeFrame(over window: NSWindow, moved: CGSize, chosen: PopupSizes.Chosen = PopupSizes.Chosen(),
                           less: CGFloat = 0) -> NSRect {
        let room = largeRoom(over: window)
        let w = max(320, min(chosen.width ?? largeCap.width, room.width - less)).rounded()
        let h = max(260, min(chosen.height ?? largeCap.height, room.height - less)).rounded()
        return NSRect(x: (room.midX - w / 2 + moved.width).rounded(), y: (room.midY - h / 2 + moved.height).rounded(), width: w, height: h)
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
/// window (the window under it keeps its toolbar lit and its menus). (1.3.80) A large one is resized by its edges and
/// corners (`PopupEdges`: a borderless window has none of its own to take) and keeps the size it is given.
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
    /// (1.3.80) How much smaller each way a large popup opened a step down and along over another is, so it ends inside
    /// the window (none once the student has sized it).
    fileprivate var cascade: CGFloat = 0
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
    /// (1.3.80) What a large popup's size is kept under (`PopupSizes`): a tool's own, else its key's.
    private var sizeKey: String
    /// (1.3.80) The size its student gave it by its edges, as kept under `sizeKey` (sides nil: filling the room).
    private var chosen: PopupSizes.Chosen
    /// (1.3.80) Being resized by an edge or a corner: which, and where the pointer and the popup were as it began.
    private var resizing: (side: PopupEdges.Side, mouse: NSPoint, frame: NSRect)?
    /// (1.3.155) Made a window of its own (New Window): placed and sized by the student, as any window.
    fileprivate private(set) var detached = false
    private var resizeWatch: NSObjectProtocol?

    init(key: String, layout: PopupLayout, title: String, sizeKey: String) {
        popupKey = key
        popupLayout = layout
        self.sizeKey = sizeKey
        chosen = layout.isLarge ? PopupSizes.chosen(sizeKey) : PopupSizes.Chosen()
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
    override var canBecomeMain: Bool { detached }

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
        if detached, let owner { owner.closedOutside(self) } else if let owner { owner.close(popupKey) } else { goOut(animated: true) }
    }

    /// (1.3.155) Made a window of its own, where it is and as it is (FloatingPopups.openFrontInWindow): out of the window
    /// it was over, a titled window — its title bar unseen under the popup's own head, which keeps its Close — resized by
    /// its edges as any window, and in the window list. In full screen, in a full-screen space of its own, as a new
    /// Safari window is.
    fileprivate func becomeWindow(fullScreen: Bool) {
        guard !detached, !closing else { return }
        detached = true
        dragEnded()
        resizing = nil
        parent?.removeChildWindow(self)
        styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
        titlebarAppearsTransparent = true
        titleVisibility = .hidden
        for b in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] { standardWindowButton(b)?.isHidden = true }
        level = .normal
        collectionBehavior = [.managed, .participatesInCycle, .fullScreenPrimary]
        isExcludedFromWindowsMenu = false
        minSize = Self.least
        // (a step down and along from where it was, so it is seen to have come away)
        setFrameOrigin(NSPoint(x: frame.minX + 24, y: frame.minY - 24))
        model.size = frame.size
        resizeWatch = NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: self, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, self.detached, self.popupLayout.isLarge else { return }
                if self.model.size != self.frame.size { self.model.size = self.frame.size }
            }
        }
        makeKeyAndOrderFront(nil)
        invalidateShadow()
        if fullScreen { DispatchQueue.main.async { [weak self] in self?.toggleFullScreen(nil) } }
    }

    // MARK: Placing

    /// Put where it opens: a large one sized from the window (as far along as it was moved), a fitted one centred
    /// across the screen's side of the window a little above its middle, one at a point with its top left there — or,
    /// already placed, sized again with its top and its middle kept where they are.
    fileprivate func place(fitting size: CGSize? = nil) {
        guard let over, !detached else { return } // (1.3.155: a window of its own is where its student puts it)
        let m = FloatingPopups.margin
        var f: NSRect
        switch popupLayout {
        case .large:
            // (its edge in the student's hand: the size is theirs until let go — and a resize whose button came up
            // unseen, its view gone from under the pointer, ends here)
            if resizing != nil && NSEvent.pressedMouseButtons & 1 == 0 { resizeEnded() }
            guard resizing == nil else { return }
            f = FloatingPopups.largeFrame(over: over, moved: moved, chosen: chosen, less: cascade)
            if model.size != f.size { model.size = f.size } // (the same size: what it shows is not laid out again)
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
                // (1.3.82) in the middle of the whole window, across and down — the owner: "popups dont show up in the
                // middle" — not of the page beside the sidebar, nor a little above the middle
                let a = FloatingPopups.area(of: over, sidebar: false)
                let top = max(m, (a.height - s.height) / 2)
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
        guard !placed || f != frame else { return } // (where it is already)
        setFrame(f, display: true)
        placed = true
        invalidateShadow()
    }

    /// (1.3.79) A large popup placed again over the first seconds after it came in, until it is dragged or resized: one
    /// put up as the window opened (a tool asked for at launch) is placed before the window has settled — (1.3.80) its
    /// toolbar's height, its size; no longer its sidebar's edge, which a large popup now lies over — and nothing else
    /// says when it has.
    fileprivate func placeAgainSoon() {
        for delay in [0.5, 1.5, 4, 8, 14] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                guard let self, !self.closing, self.drag == nil, self.resizing == nil, !self.userMoved, self.popupLayout.isLarge else { return }
                self.place()
            }
        }
    }

    /// (1.3.80) A large popup asked to show something whose size is kept under another key (another tool in the tool's
    /// popup): sized as that one was left, where it stands.
    fileprivate func keepSize(under key: String) {
        guard popupLayout.isLarge, key != sizeKey else { return }
        sizeKey = key
        chosen = PopupSizes.chosen(key)
        place()
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
            let a = FloatingPopups.area(of: over, sidebar: owner?.sidebarShown ?? true, guessed: owner?.sidebarSaid ?? false)
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
        resizing = nil
        if let resizeWatch { NotificationCenter.default.removeObserver(resizeWatch) }
        resizeWatch = nil
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
            let home = FloatingPopups.largeFrame(over: over, moved: .zero, chosen: chosen, less: cascade)
            moved = CGSize(width: frame.minX - home.minX, height: frame.minY - home.minY)
        }
    }

    // MARK: Resizing (1.3.80)

    /// The least a large popup is made by its edges (less only where the window has less room).
    static let least = CGSize(width: 480, height: 340)

    /// Being resized by an edge or a corner (its edges' cursor left be meanwhile).
    fileprivate var isResizing: Bool { resizing != nil }

    /// An edge or a corner of a large popup taken: where the pointer and the popup are, kept for the drag.
    fileprivate func resizeBegan(_ side: PopupEdges.Side) {
        guard popupLayout.isLarge, !closing, drag == nil, !detached else { return } // (1.3.155: a window resizes as windows do)
        userMoved = true
        resizing = (side, NSEvent.mouseLocation, frame)
    }

    /// The edge or corner dragged: the opposite edges kept where they were, the size between the least and the room the
    /// window has (and the screen's edge), and what it shows laid out at the new size as it goes — a 3D model framed
    /// again for it.
    fileprivate func resizeDragged() {
        guard let r = resizing, let over else { return }
        guard NSEvent.pressedMouseButtons & 1 != 0 else { return resizeEnded() } // (let go where it was not seen)
        let m = NSEvent.mouseLocation
        let dx = (m.x - r.mouse.x).rounded(), dy = (m.y - r.mouse.y).rounded()
        let room = FloatingPopups.largeRoom(over: over).size
        let screen = (over.screen ?? NSScreen.main)?.visibleFrame
        let least = CGSize(width: min(Self.least.width, room.width), height: min(Self.least.height, room.height))
        // (a side as dragged, kept between the least and the most it may be: the room, and as far as the screen's edge)
        func side(_ wanted: CGFloat, least lo: CGFloat, most: CGFloat, screen edge: CGFloat?) -> CGFloat {
            let hi = max(lo, min(most, edge ?? most))
            return min(max(wanted, lo), hi)
        }
        var f = r.frame
        if r.side.contains(.right) {
            f.size.width = side(r.frame.width + dx, least: least.width, most: room.width, screen: screen.map { $0.maxX - r.frame.minX })
        } else if r.side.contains(.left) {
            f.size.width = side(r.frame.width - dx, least: least.width, most: room.width, screen: screen.map { r.frame.maxX - $0.minX })
            f.origin.x = r.frame.maxX - f.width
        }
        if r.side.contains(.top) {
            f.size.height = side(r.frame.height + dy, least: least.height, most: room.height, screen: screen.map { $0.maxY - r.frame.minY })
        } else if r.side.contains(.bottom) {
            f.size.height = side(r.frame.height - dy, least: least.height, most: room.height, screen: screen.map { r.frame.maxY - $0.minY })
            f.origin.y = r.frame.maxY - f.height
        }
        guard f != frame else { return }
        model.size = f.size
        setFrame(f, display: true)
        invalidateShadow()
    }

    /// Let go: the size kept for next time under its size key — a side left as large as the room would have it anyway
    /// kept as filling it, so it grows with a larger window — and how far it now stands from where it opens.
    fileprivate func resizeEnded() {
        guard resizing != nil else { return }
        resizing = nil
        guard let over else { return }
        let room = FloatingPopups.largeRoom(over: over).size
        let fills = CGSize(width: min(room.width, FloatingPopups.largeCap.width), height: min(room.height, FloatingPopups.largeCap.height))
        chosen = PopupSizes.Chosen(width: abs(frame.width - fills.width) < 2 ? nil : frame.width,
                                   height: abs(frame.height - fills.height) < 2 ? nil : frame.height)
        PopupSizes.keep(chosen, for: sizeKey)
        cascade = 0
        let home = FloatingPopups.largeFrame(over: over, moved: .zero, chosen: chosen)
        moved = CGSize(width: frame.minX - home.minX, height: frame.minY - home.minY)
        invalidateShadow()
        if OpenClock.verbose {
            NSLog("[Simpl] popup %@: resized to %.0f × %.0f, kept for %@", popupKey, frame.width, frame.height, sizeKey)
        }
    }
}

/// (1.3.80) The sizes students gave large popups by their edges, kept in the app's settings by size key (a tool's own,
/// `tool.dna`; "alta", "macmillan", "feedback", "rubric") and used each time one opens, within the window's room. A
/// launch's `-SimplPopupSize tool.dna=700x500` (several joined by commas) gives a popup a size for that launch alone:
/// the screenshot suite's picture of a tool reopened at the size it was left at.
enum PopupSizes {
    /// A size picked: each side in points, or nil — left as large as the room would have it — filling the room.
    struct Chosen: Equatable {
        var width: CGFloat?
        var height: CGFloat?
    }

    private static let defaultsKey = "SimplPopupSizes"

    /// (1.3.112) The size a tool opens at before its student sizes it, where filling the room would only stretch it: the
    /// Calculator (GeoGebra's) narrower than the window.
    private static let opening: [String: Chosen] = ["tool.calc": Chosen(width: 900, height: 860)]

    /// The size kept under `key` (nothing chosen: none).
    static func chosen(_ key: String) -> Chosen {
        if let seed = UserDefaults.standard.string(forKey: "SimplPopupSize"), let c = seeded(seed, key) { return c }
        guard let v = UserDefaults.standard.dictionary(forKey: defaultsKey)?[key] as? [Double], v.count == 2 else { return opening[key] ?? Chosen() }
        return Chosen(width: v[0] > 0 ? CGFloat(v[0]) : nil, height: v[1] > 0 ? CGFloat(v[1]) : nil)
    }

    /// A size kept under `key` (a side nil kept as 0; both nil, the key let go).
    static func keep(_ c: Chosen, for key: String) {
        var all = UserDefaults.standard.dictionary(forKey: defaultsKey) ?? [:]
        if c.width == nil && c.height == nil {
            all.removeValue(forKey: key)
        } else {
            all[key] = [Double(c.width ?? 0), Double(c.height ?? 0)]
        }
        UserDefaults.standard.set(all, forKey: defaultsKey)
    }

    /// The launch's size for `key` in "tool.dna=700x500,alta=900x600", if it gives one.
    private static func seeded(_ seed: String, _ key: String) -> Chosen? {
        for part in seed.split(separator: ",") {
            let kv = part.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
            guard kv.count == 2, kv[0] == key else { continue }
            let wh = kv[1].lowercased().split(separator: "x").compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }
            if wh.count == 2, wh[0] > 0, wh[1] > 0 { return Chosen(width: CGFloat(wh[0]), height: CGFloat(wh[1])) }
        }
        return nil
    }
}

/// (1.3.80) A large popup's edges and corners, to resize it by: a band 5 points wide just inside each edge (and 16
/// points along each side of a corner) takes the pointer — the frame-resize cursor over it, and its first click though
/// the popup is not key — and everything else goes through to what the popup shows, under it.
final class PopupEdges: NSView {
    /// The edges taken: one, or two at a corner.
    struct Side: OptionSet {
        let rawValue: Int
        static let left = Side(rawValue: 1)
        static let right = Side(rawValue: 2)
        static let top = Side(rawValue: 4)
        static let bottom = Side(rawValue: 8)
    }

    weak var panel: PopupPanel?
    private static let band: CGFloat = 5
    private static let corner: CGFloat = 16
    private var tracking: NSTrackingArea?
    /// The resize cursor shown (set back to the arrow once the pointer leaves the edges).
    private var showing = false

    /// The edges at a point of this view: none away from them; two near a corner.
    func side(at p: NSPoint) -> Side {
        let b = bounds
        guard b.contains(p) else { return [] }
        let l = p.x < b.minX + Self.band, r = p.x > b.maxX - Self.band
        let lo = p.y < b.minY + Self.band, hi = p.y > b.maxY - Self.band
        var s: Side = []
        if l || ((lo || hi) && p.x < b.minX + Self.corner) { s.insert(.left) }
        if r || ((lo || hi) && p.x > b.maxX - Self.corner) { s.insert(.right) }
        if lo || ((l || r) && p.y < b.minY + Self.corner) { s.insert(.bottom) }
        if hi || ((l || r) && p.y > b.maxY - Self.corner) { s.insert(.top) }
        return s
    }

    /// The edges' own: anywhere else, what the popup shows.
    override func hitTest(_ point: NSPoint) -> NSView? {
        guard !isHidden, panel?.popupLayout.isLarge == true else { return nil }
        return side(at: convert(point, from: superview)).isEmpty ? nil : self
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let t = NSTrackingArea(rect: .zero, options: [.mouseMoved, .mouseEnteredAndExited, .cursorUpdate, .activeInActiveApp, .inVisibleRect],
                               owner: self, userInfo: nil)
        addTrackingArea(t)
        tracking = t
    }

    override func mouseEntered(with event: NSEvent) { pointed(event) }
    override func mouseMoved(with event: NSEvent) { pointed(event) }
    override func mouseExited(with event: NSEvent) {
        guard showing, panel?.isResizing != true else { return }
        showing = false
        NSCursor.arrow.set()
    }

    override func cursorUpdate(with event: NSEvent) {
        if !pointed(event) { super.cursorUpdate(with: event) }
    }

    /// The pointer over the popup: the resize cursor for the edges under it, or — leaving them — the arrow again.
    @discardableResult
    private func pointed(_ event: NSEvent) -> Bool {
        if panel?.isResizing == true { return true }
        let s = side(at: convert(event.locationInWindow, from: nil))
        guard s.isEmpty else {
            Self.cursor(s).set()
            showing = true
            return true
        }
        if showing {
            showing = false
            NSCursor.arrow.set()
        }
        return false
    }

    override func mouseDown(with event: NSEvent) {
        let s = side(at: convert(event.locationInWindow, from: nil))
        guard !s.isEmpty else { return super.mouseDown(with: event) }
        panel?.resizeBegan(s)
    }

    override func mouseDragged(with event: NSEvent) { panel?.resizeDragged() }

    override func mouseUp(with event: NSEvent) {
        panel?.resizeEnded()
        pointed(event)
    }

    /// The cursor for resizing by `s`: the Mac's frame-resize cursors (macOS 15), else the column and row ones.
    private static func cursor(_ s: Side) -> NSCursor {
        if #available(macOS 15.0, *) {
            let at: NSCursor.FrameResizePosition
            switch s {
            case [.top, .left]: at = .topLeft
            case [.top, .right]: at = .topRight
            case [.bottom, .left]: at = .bottomLeft
            case [.bottom, .right]: at = .bottomRight
            case .top: at = .top
            case .bottom: at = .bottom
            case .left: at = .left
            default: at = .right
            }
            return NSCursor.frameResize(position: at, directions: .all)
        }
        return s.contains(.left) || s.contains(.right) ? .resizeLeftRight : .resizeUpDown
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
