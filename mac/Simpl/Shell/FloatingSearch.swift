import AppKit
import SwiftUI

// (1.3.152) Search as the web's box afloat (extension/content/app/search.js): a press in the toolbar's search field (1.3.154) — or
// ⌘K, or Edit ▸ Search Commands — brings a large field over the middle of the window with its results under it, and
// nothing else changes: no screen of its own, the page stays where it is (and is not dimmed). Before anything is typed,
// the four kinds to search in (Courses ⌘1, Work ⌘2, Files ⌘3, Actions ⌘4); a kind chosen sits in the field as a chip
// and lists its own things at once — your courses, what is due this week, the files changed last, every command —
// what is typed then searching that kind alone. Each letter is answered at once from what the app and the engine's
// index hold, and by the school once the typing pauses (SearchPalette.update). The first row is chosen as you type:
// Return opens it, ↑ ↓ walk the rows, Tab completes a command, Escape steps back (the kind, then the search goes),
// Backspace on an empty field lets the kind go; a press anywhere else puts it away.

/// One of the four kinds the search can be narrowed to, as the web box's (search.js SCOPES).
enum SearchScope: String, CaseIterable, Identifiable {
    case courses, work, files, actions
    var id: String { rawValue }

    var title: String {
        switch self {
        case .courses: return "Courses"
        case .work: return "Work"
        case .files: return "Files"
        case .actions: return "Actions"
        }
    }

    var hint: String {
        switch self {
        case .courses: return "Your courses and their sections"
        case .work: return "Assignments, quizzes, discussions and announcements"
        case .files: return "Course files and pages"
        case .actions: return "Every command: open, add, switch, go"
        }
    }

    var symbol: String {
        switch self {
        case .courses: return "books.vertical"
        case .work: return "doc.text"
        case .files: return "folder"
        case .actions: return "command"
        }
    }

    var key: String {
        switch self {
        case .courses: return "⌘1"
        case .work: return "⌘2"
        case .files: return "⌘3"
        case .actions: return "⌘4"
        }
    }

    /// The search's own kinds this one answers with (nil: none — its rows are the app's own).
    var kinds: [String] {
        switch self {
        case .courses, .actions: return []
        case .work: return ["Assignments", "Discussions", "Announcements"]
        case .files: return ["Files", "Pages"]
        }
    }
}

/// Whether the floating search is up, and the kind it is narrowed to.
@MainActor
final class FloatingSearch: ObservableObject {
    static let shared = FloatingSearch()
    @Published private(set) var shown = false
    /// Bumped each time it is asked for, so the field takes the cursor again (it may be up already).
    @Published private(set) var focusTick = 0
    weak var palette: SearchPalette?

    func show(_ engine: Engine) {
        if !shown {
            Task { _ = try? await engine.call("searchWarm", as: Ack.self) } // (the index taken now: the first letters answer from it)
            palette?.update(engine.query, engine: engine)
        }
        withAnimation(Self.arrive) { shown = true }
        focusTick += 1
    }

    /// (1.3.156) Its motion: in on a quick spring with no overshoot, out quicker on an ease-out (only a fade under Reduce
    /// Motion, where it is drawn); the list's height following what it shows on the same kind of spring.
    static let arrive = Animation.spring(response: 0.32, dampingFraction: 1)
    static let leave = Animation.easeOut(duration: 0.14)
    static let resize = Animation.spring(response: 0.28, dampingFraction: 1)

    /// Put away: what was typed and the kind go with it, as the web box's going home does.
    func hide(_ engine: Engine) {
        guard shown else { return }
        withAnimation(Self.leave) { shown = false }
        palette?.scope = nil
        if !engine.query.isEmpty { engine.query = "" }
    }

    func setScope(_ scope: SearchScope?, engine: Engine) {
        palette?.scope = scope
        palette?.update(engine.query, engine: engine)
        focusTick += 1
    }

    private struct Ack: Decodable {}
}

/// (1.3.154, 1.3.156) The toolbar's search field (`.searchable`) only brings the floating search up: a press on it is
/// taken before the field has it — it never gets the cursor or its focus ring, the floating search does — and, tabbed to,
/// it gives the keyboard straight back. (Typed in before it is up, the words go with it: MainShell's onChange.)
struct ToolbarSearchWatch: NSViewRepresentable {
    let pressed: () -> Void

    func makeNSView(context: Context) -> ToolbarSearchWatchView {
        let v = ToolbarSearchWatchView()
        v.pressed = pressed
        return v
    }

    func updateNSView(_ v: ToolbarSearchWatchView, context: Context) { v.pressed = pressed }
}

final class ToolbarSearchWatchView: NSView {
    var pressed: (() -> Void)?
    private var watch: NSKeyValueObservation?
    private var clicks: Any?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        watch = nil
        if let clicks { NSEvent.removeMonitor(clicks) }
        clicks = nil
        guard let window else { return }
        // a press on the field: the floating search, and the press goes no further (in full screen the toolbar is in a
        // window of its own: the field's window, not this one, is the one pressed)
        clicks = NSEvent.addLocalMonitorForEvents(matching: .leftMouseDown) { [weak self] e in
            guard let self, let host = self.window, let field = Self.field(of: host), let w = e.window, field.window === w,
                  !field.isHiddenOrHasHiddenAncestor, field.bounds.contains(field.convert(e.locationInWindow, from: nil)) else { return e }
            self.pressed?()
            return nil
        }
        // tabbed to (the keyboard's way in): given straight back, and the floating search up
        watch = window.observe(\.firstResponder, options: [.new]) { [weak self] w, _ in
            MainActor.assumeIsolated {
                guard let editor = w.firstResponder as? NSTextView, let field = editor.delegate as? NSSearchField,
                      field === Self.field(of: w) else { return }
                w.makeFirstResponder(nil)
                self?.pressed?()
            }
        }
    }

    /// The toolbar's search field, where it is now.
    private static func field(of window: NSWindow) -> NSSearchField? {
        (window.toolbar?.items ?? []).lazy.compactMap { ($0 as? NSSearchToolbarItem)?.searchField }.first
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    deinit {
        if let clicks { NSEvent.removeMonitor(clicks) }
    }
}

/// The floating search over the window: its field and its results.
struct FloatingSearchOverlay: View {
    @ObservedObject var palette: SearchPalette
    @ObservedObject private var fs = FloatingSearch.shared
    @EnvironmentObject private var engine: Engine
    @FocusState private var focused: Bool
    @State private var chosen = 0
    @State private var byMouse = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// The rows that do something, in the order the arrows walk them.
    private var rows: [PaletteItem] {
        palette.sections.flatMap(\.items).filter { $0.action != .nothing }
    }

    var body: some View {
        GeometryReader { g in
            // (1.3.156: the panel comes and goes itself — its transition plays — inside a stack that is always there;
            // put in and taken out with the stack round it, it only ever popped)
            ZStack(alignment: .top) {
                if fs.shown {
                    // a press anywhere else puts it away (the page is not dimmed: Spotlight's way)
                    Color.black.opacity(0.001)
                        .contentShape(Rectangle())
                        .onTapGesture { fs.hide(engine) }
                        .transition(.identity)
                    panel(maxHeight: max(220, g.size.height * 0.62))
                        .frame(width: min(700, g.size.width - 48))
                        .padding(.top, min(96, g.size.height * 0.1))
                        .transition(arrival)
                }
            }
            .frame(width: g.size.width, height: g.size.height, alignment: .top)
        }
        .allowsHitTesting(fs.shown)
        .onChange(of: engine.query) { _, q in
            chosen = 0
            if palette.take(q, engine: engine) { return }
            palette.update(q, engine: engine)
        }
        .onChange(of: palette.sections) { _, _ in
            if chosen >= rows.count { chosen = 0 }
        }
        .onChange(of: fs.focusTick) { _, _ in
            DispatchQueue.main.async { focused = true }
        }
    }

    /// (1.3.156) In: faded and grown from a little under its top, a soft blur clearing — the panel materialising;
    /// out: faded and settling a touch smaller. A fade alone under Reduce Motion.
    private var arrival: AnyTransition {
        if reduceMotion { return .opacity }
        return .asymmetric(
            insertion: .modifier(active: Materialise(scale: 0.96, lift: -8, blur: 8, opacity: 0), identity: Materialise()),
            removal: .modifier(active: Materialise(scale: 0.98, lift: -4, blur: 0, opacity: 0), identity: Materialise()))
    }

    private func panel(maxHeight: CGFloat) -> some View {
        VStack(spacing: 0) {
            field
            if !palette.sections.isEmpty {
                Divider().opacity(0.6)
                results(maxHeight: maxHeight)
                footer
            }
        }
        // (the results coming and going with the panel's height, not in a jump)
        .animation(reduceMotion ? nil : FloatingSearch.resize, value: palette.sections.isEmpty)
        .glassCard(radius: 22)
        .shadow(color: .black.opacity(0.22), radius: 30, y: 14)
        .onAppear { DispatchQueue.main.async { focused = true } }
    }

    private var field: some View {
        HStack(spacing: 10) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 19, weight: .medium))
                .foregroundStyle(.secondary)
            if let s = palette.scope {
                HStack(spacing: 5) {
                    Image(systemName: s.symbol)
                    Text(s.title)
                    Button {
                        fs.setScope(nil, engine: engine)
                    } label: {
                        Image(systemName: "xmark")
                            .font(.system(size: 9, weight: .bold))
                    }
                    .buttonStyle(.plain)
                    .help("Search everything again")
                }
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(Theme.accent)
                .padding(.horizontal, 9)
                .padding(.vertical, 4)
                .background(Capsule().fill(Theme.accent.opacity(0.14)))
            }
            TextField(palette.scope.map { "Search \($0.title.lowercased())" } ?? "Search or type /", text: $engine.query)
                .textFieldStyle(.plain)
                .font(.system(size: 21))
                .focused($focused)
                .onSubmit { run(chosenRow) }
                .onKeyPress(.downArrow) { move(1); return .handled }
                .onKeyPress(.upArrow) { move(-1); return .handled }
                .onKeyPress(.tab) {
                    if let r = chosenRow, case .fill(let text) = r.action { engine.query = text; return .handled }
                    return .ignored
                }
                .onKeyPress(.escape) { escape(); return .handled }
                .onKeyPress(.delete) {
                    guard engine.query.isEmpty, palette.scope != nil else { return .ignored }
                    fs.setScope(nil, engine: engine)
                    return .handled
                }
                .onKeyPress(characters: CharacterSet(charactersIn: "1234")) { press in
                    guard press.modifiers.contains(.command), let n = Int(press.characters), n >= 1, n <= 4 else { return .ignored }
                    fs.setScope(SearchScope.allCases[n - 1], engine: engine)
                    return .handled
                }
            if palette.searching {
                ProgressView()
                    .controlSize(.small)
            } else if !engine.query.isEmpty {
                Button {
                    engine.query = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .foregroundStyle(.tertiary)
                }
                .buttonStyle(.plain)
                .help("Clear")
            }
        }
        .padding(.horizontal, 18)
        .frame(height: 58)
    }

    private func results(maxHeight: CGFloat) -> some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(palette.sections) { section in
                        sectionView(section)
                    }
                }
                .padding(8)
                // (sections answering later — the school's, after a pause in the typing — fade in where they go)
                .animation(reduceMotion ? nil : .easeOut(duration: 0.16), value: palette.sections.map(\.title))
            }
            .frame(height: min(maxHeight, listHeight))
            .animation(reduceMotion ? nil : FloatingSearch.resize, value: listHeight)
            .onChange(of: chosen) { _, _ in
                guard !byMouse, let r = chosenRow else { return }
                withAnimation(reduceMotion ? nil : Motion.snappy) { proxy.scrollTo(r.id) }
            }
        }
    }

    private func sectionView(_ section: PaletteSection) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(section.title)
                .font(.sFootnote.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.horizontal, 12)
                .padding(.top, 10)
                .padding(.bottom, 2)
            ForEach(section.items) { item in
                row(item)
                    .id(item.id)
            }
        }
        .transition(.opacity)
    }

    private func row(_ item: PaletteItem) -> some View {
        let on = chosenRow?.id == item.id
        let live = item.action != .nothing
        return HStack(spacing: 12) {
            Image(systemName: item.symbol)
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(item.color.map { Color(hex: $0) } ?? Theme.accent)
                .frame(width: 26)
            VStack(alignment: .leading, spacing: 1) {
                Text(item.title)
                    .font(.sBody.weight(on ? .medium : .regular))
                    .lineLimit(1)
                if let sub = item.sub, !sub.isEmpty {
                    Text(sub)
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 12)
            if let t = item.trailing, !t.isEmpty {
                Text(t)
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(on ? Theme.accent.opacity(0.16) : .clear))
        .contentShape(Rectangle())
        .opacity(live ? 1 : 0.75)
        .onHover { inside in
            guard inside, live, let i = rows.firstIndex(where: { $0.id == item.id }) else { return }
            byMouse = true
            chosen = i
        }
        .onTapGesture { if live { run(item) } }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
    }

    /// The list's own height: each heading and row as it is drawn (a row with a line under its title, taller), so the
    /// panel is as tall as its results, up to the most the window allows.
    private var listHeight: CGFloat {
        var h: CGFloat = 16
        for s in palette.sections {
            h += 30
            for it in s.items { h += (it.sub ?? "").isEmpty ? 34 : 48 }
        }
        return h
    }

    private var footer: some View {
        HStack(spacing: 14) {
            Text("↑↓ to choose")
            Text("↩ to open")
            if completes { Text("⇥ to complete") }
            Spacer()
            Text("esc to close")
        }
        .font(.sFootnote)
        .foregroundStyle(.tertiary)
        .padding(.horizontal, 18)
        .padding(.vertical, 8)
    }

    /// The row chosen completes a command (Tab).
    private var completes: Bool {
        if case .fill = chosenRow?.action { return true }
        return false
    }

    private var chosenRow: PaletteItem? {
        let r = rows
        return r.isEmpty ? nil : r[min(chosen, r.count - 1)]
    }

    private func move(_ by: Int) {
        let n = rows.count
        guard n > 0 else { return }
        byMouse = false
        chosen = (min(chosen, n - 1) + by + n) % n
    }

    private func run(_ item: PaletteItem?) {
        guard let item else {
            // (Return on a sum's words: its answer copied, as its row offers)
            palette.submit(engine.query, engine: engine)
            return
        }
        SearchPalette.perform(item.action, engine: engine, fromField: true)
    }

    /// Escape steps back: the words typed, then the kind, then the search goes.
    private func escape() {
        if !engine.query.isEmpty, palette.scope != nil {
            engine.query = ""
        } else if palette.scope != nil {
            fs.setScope(nil, engine: engine)
        } else {
            fs.hide(engine)
        }
    }
}

/// (1.3.156) The floating search's panel part-way in or out: scaled from its top, lifted, blurred and faded.
private struct Materialise: ViewModifier {
    var scale: CGFloat = 1
    var lift: CGFloat = 0
    var blur: CGFloat = 0
    var opacity: Double = 1

    func body(content: Content) -> some View {
        content
            .scaleEffect(scale, anchor: .top)
            .offset(y: lift)
            .blur(radius: blur)
            .opacity(opacity)
    }
}
