import AppKit
import SwiftUI

/// The Dashboard: the day at a glance, as Simpl's web Dashboard has it (1.2). Six counters, each growing in place into a
/// panel of what it counted; then one of three views, chosen in the toolbar and kept on this Mac — Cards (the courses as
/// cards: score, what is due, unread announcements, how much is handed in, quick links), List (every course on one
/// line, then everything coming up day by day, ticked off where it is done) and Activity (Canvas's recent activity).
/// Beside the view on a wide window, and under it on a narrow one: the day's work, the grades skyline and the week's
/// load per course. (1.3.78) Kept once opened (DetailStack): `active` while it is the screen showing; put away, it keeps
/// everything it shows, and reads again — quietly, each card changing in place — when something was done meanwhile.
/// (1.3.80) While it is the screen showing it is checked live every minute (LiveRefresh): each part asked again, and only
/// the parts that changed put in place — a counter's number, a card, a row, the skyline — the rest left as it is; a check
/// that finds nothing new draws nothing. Put away, it is not checked; shown again on what is more than a minute old, it
/// is checked at once.
struct DashboardView: View {
    /// (1.3.78) It is the screen showing (not put away under another).
    let active: Bool
    /// (1.3.78) The engine's `dataVersion` and whether the school is on Brightspace, handed in by the stack: the
    /// Dashboard holds the engine as a plain value (EngineLink), so it is drawn again only when these change — not, put
    /// away, at every change of the engine.
    let version: Int
    let brightspace: Bool
    @Environment(\.engineLink) private var link
    private var engine: Engine { link.acting }
    @StateObject private var model = Loader<Today>()
    /// (1.3.78) Something was done while it was put away (`dataVersion` moved on): it reads again when it is shown.
    @State private var behind = false
    /// The view chosen (Cards, List, Activity), kept on this Mac. (The screenshot suite: -SimplDashView list.)
    @AppStorage("SimplDashView") private var chosen: DashView = .list // (1.2.1: List unless another is picked)
    @State private var counts: TodayCounts?
    /// (home-25) Overdue's and Graded's numbers are Canvas's word now, not the ones counted from what was kept.
    @State private var countsLive = false
    @State private var open: String?
    /// (home-18) How tall the window is: a counter's panel stays in it.
    @State private var windowHeight: CGFloat = 0
    /// (1.3.62) The watch for a click anywhere in the app but the opened counter's panel.
    @State private var clickWatch: Any?
    /// (1.3.80) Where each counter's panel is in the window, for a click on it to be its own: read from AppKit through an
    /// unseen view under the panel (SwiftUI's global frame stood 20 points off the window's own here), one for each
    /// counter, so a panel folding back as another grows never answers for the one open.
    @State private var panelAt = DashPanelAnchors()
    /// (1.2.8) The opened counter's panel, as tall as what it lists (measured).
    @State private var panelHeight: CGFloat = 0
    @State private var sheetAsked = false
    @State private var width: CGFloat = 0
    @State private var courses: DashCoursesData?
    @State private var progress: [String: CourseProgress] = [:]
    @State private var progressRead = false
    @State private var sky: DashSkylineData?
    @State private var list: DashListData?
    @State private var listError: String?
    @State private var activity: DashActivityData?
    @State private var activityError: String?
    /// (1.3.80) What the live checks know of the parts' answers shown (their fingerprints), and when they were last read
    /// or found the same: never drawn from, so a check that finds nothing new changes nothing the page draws.
    @State private var marks = LiveMarks()
    /// (1.3.80) Who holds the live checks back while a counter's panel is open.
    @State private var holdToken = UUID()
    @Namespace private var morph
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    // (written out: the state above is private)
    init(active: Bool, version: Int, brightspace: Bool) {
        self.active = active
        self.version = version
        self.brightspace = brightspace
    }

    /// Brightspace has no activity stream: there the Activity view is not offered.
    private var views: [DashView] { DashView.offered(brightspace: brightspace) }
    private var view: DashView { views.contains(chosen) ? chosen : .list }

    // MARK: - Widths

    /// The side column's width on a wide window.
    private var railWidth: CGFloat { min(440, max(340, (width * 0.3).rounded())) }
    /// Wide enough for the view and the side column side by side.
    private var twoColumns: Bool { width >= 900 }
    private var mainWidth: CGFloat { twoColumns ? width - railWidth - 28 : width }

    var body: some View {
        let _ = LiveTrace.drew("dashboard") // (1.3.80: a live check's line says how many times it drew the page)
        Group {
            if let d = model.data {
                page(d)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        // (1.3.78: its title and toolbar are DashboardChrome's, in the window's stack)
        .background {
            // (home-18) how tall the window is, for a counter's panel to stay in it
            WindowWidthProbe { size in
                if abs(size.height - windowHeight) > 0.5 { windowHeight = size.height }
            }
        }
        .openTimed("Dashboard", ready: model.data != nil, live: model.data != nil && !model.kept, source: model.source)
        // (1.3.80) checked live while it is the screen showing: only what changed is put in place
        .liveChecks("dashboard", shown: active, since: { marks.at }) { _, age in try await liveCheck(age) }
        .task(id: version) {
            if active { await load() } else { behind = true }
        }
        .onChange(of: active) { _, now in
            if now {
                // (shown again as it was: timed as such, and read again only if something was done meanwhile — on what
                // has gone stale, the live check runs at once and changes only what changed, 1.3.80)
                if model.data != nil { DispatchQueue.main.async { OpenClock.report("Dashboard", "alive") } }
                if behind {
                    behind = false
                    Task { await load() }
                }
            } else if open != nil {
                setOpen(nil) // (a counter left open folds back as the Dashboard is put away)
            }
        }
        .onChange(of: chosen) { _, _ in Task { await loadView() } }
    }

    /// (1.3.78) What the Dashboard opens on, as it asks for it (read ahead when it is not the first screen).
    static func reads(_ engine: Engine) -> [ReadAhead.Ask?] {
        let chosen = DashView(rawValue: UserDefaults.standard.string(forKey: "SimplDashView") ?? "") ?? .list
        var asks: [ReadAhead.Ask?] = [
            .read("today", Today.self), .read("todayCounts", ["kept": false], TodayCounts.self),
            .read("dashSkyline", ["kept": false], DashSkylineData.self),
            .read("dashCourses", DashCoursesData.self), .read("coursesProgress", [String: CourseProgress].self),
        ]
        switch DashView.offered(brightspace: engine.onBrightspace).contains(chosen) ? chosen : .list {
        case .cards: break
        case .list: asks.append(.read("dashList", DashListData.self))
        case .activity: asks.append(.read("dashActivity", DashActivityData.self))
        }
        return asks
    }

    // MARK: - The page

    private func page(_ d: Today) -> some View {
        // (1.3: the width the window gives the page — read inside the page, a wide layout once drawn held itself wide)
        Page(spacing: 26, onWidth: { w in if abs(w - width) > 0.5 { width = w } }) {
            header(d)
            // (laid out once the page's width is known: the counters' rows and the columns follow it)
            if width > 0 {
                VStack(alignment: .leading, spacing: 26) {
                    counters(d)
                    content(d)
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
            }
        }
    }

    /// (home-4) The greeting and the day, with the page's own controls beside them — its view (Cards, List, Activity) and
    /// New Task — in the page, not the toolbar, so a tool pinned to the toolbar never pushes them into its » menu (as
    /// Windows has them); under the greeting where the page is too narrow for both on one line. (home-25) While the page
    /// shows what was kept from last time, the day's line says it is updating.
    private func header(_ d: Today) -> some View {
        let heading = ScreenHeading(title: greeting(d), sub: [d.dateLine, model.kept ? "Updating…" : nil].compactMap { $0 }.joined(separator: " · "))
        return ViewThatFits(in: .horizontal) {
            HStack(alignment: .center, spacing: 16) {
                heading
                Spacer(minLength: 12)
                viewControls
            }
            VStack(alignment: .leading, spacing: 14) {
                heading
                viewControls
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// The view chosen (kept on this Mac) and New Task (⌘N): its words beside its symbol.
    private var viewControls: some View {
        HStack(spacing: 10) {
            Picker("View", selection: $chosen.animation(Motion.gentle)) {
                ForEach(views) { v in
                    Text(v.title).tag(v).help(v.help)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            .help("Cards, a list by day, or recent activity")
            Button { engine.newTask() } label: {
                Label("New Task", systemImage: "plus")
            }
            .glassButton()
            .help("New Task (⌘N)")
        }
        .controlSize(.large)
    }

    private func greeting(_ d: Today) -> String {
        let first = d.me?.name.split(separator: " ").first.map(String.init) ?? ""
        let hour = Calendar.current.component(.hour, from: Date())
        let part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening"
        return first.isEmpty ? part : "\(part), \(first)"
    }

    // MARK: - Counters

    /// The counters in rows (1.3.80: as 1.3.77 had them again) — six across where the page has room for six, else three,
    /// or two on a very narrow page — each tile as tall as what is on it, and every tile in a row as tall as the row's
    /// tallest (not the squares of 1.3.79). (1.2.8) A counter opened grows out of its own tile into a panel that floats
    /// over the page — as wide as a panel needs, not the page — over its tile, placed by where the tile is (1.3.16): the
    /// left-most tile's panel from its left edge, the right-most's from its right, one in the middle centred on it, each
    /// kept on the page; nothing under it moves. Closed, it folds back into its tile. (1.3.80: the popup over the window
    /// of 1.3.79, which faded in instead, is gone.)
    private func counters(_ d: Today) -> some View {
        let cols = width >= 900 ? 6 : (width >= 520 ? 3 : 2)
        let list = DashboardView.ordered(d.counters)
        let rows: [[Counter]] = stride(from: 0, to: list.count, by: cols).map { start in
            Array(list[start..<min(start + cols, list.count)])
        }
        let tile = (width - Self.counterSpacing * CGFloat(cols - 1)) / CGFloat(cols)
        let (style, full) = Self.wording(list, tileWidth: tile)
        // (a tile's line on one line in a row of six — the row as low as 1.3.77's; on two at most in a row of three or two)
        let lines = cols == 6 ? 1 : 2
        return VStack(alignment: .leading, spacing: Self.counterSpacing) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                counterRow(row, cols: cols, style: style, full: full, tile: tile, lines: lines)
            }
        }
        .tourSpot(.counters) // (the tour's first step: everything due, at a glance)
        .overlayPreferenceValue(DashTileAnchors.self) { anchors in
            GeometryReader { g in
                if let key = open, let c = list.first(where: { $0.key == key }), let a = anchors[key] {
                    let tile = g[a]
                    let w = min(DashCounterPanel.width, g.size.width)
                    // (1.3.16) placed by where its tile sits across the row: the first tile's panel from its left edge,
                    // the last one's from its right, one between them centred on it in step — kept on the page
                    let along = g.size.width > tile.width ? min(max((tile.minX) / (g.size.width - tile.width), 0), 1) : 0
                    let x = min(max(tile.minX + (tile.width - w) * along, 0), max(0, g.size.width - w))
                    // (home-18) its list no taller than the window has room for under the tile's top, less its header
                    // (about 130 points of it) and a margin at the window's foot
                    let room = windowHeight > 0 ? windowHeight - (g.frame(in: .global).minY + tile.minY) - 150 : 240
                    DashCounterPanel(counter: c, value: value(c), note: fullNote(c), width: w, room: room, close: { setOpen(nil) },
                                     onHeight: { h in
                                         if abs(h - panelHeight) > 0.5 { withAnimation(heightMotion) { panelHeight = h } }
                                     },
                                     onCleared: cleared)
                    // (1.2.9: the shape that moves is the panel itself, sized to the tile on its way in and out — the
                    // size it rests at is the frame round it — so it grows out of its tile and folds back into it)
                    .dashMorph(key, in: morph, enabled: grows)
                    .frame(width: w, height: panelHeight > 0 ? panelHeight : max(tile.height, 160), alignment: .topLeading)
                    .tourSpot(.counterPanel)
                    .background { PreviewProbe(anchor: panelAt(key)).allowsHitTesting(false) }
                    .shadow(color: Color.black.opacity(0.18), radius: 24, y: 10)
                    .id(key)
                    .transition(.opacity)
                    .padding(.leading, x)
                    .padding(.top, tile.minY)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
            }
        }
        .zIndex(1) // (over the page below it)
        .onDisappear {
            watchClicks(false)
            LiveHolds.shared.set("counter panel", holdToken, false)
        }
    }

    /// The room between two tiles, across and down.
    static let counterSpacing: CGFloat = 14

    /// A row of tiles: each as tall as the row's tallest, which is as tall as what is on it.
    private func counterRow(_ row: [Counter], cols: Int, style: DashCounterTile.Style, full: Bool, tile: CGFloat, lines: Int) -> some View {
        HStack(alignment: .top, spacing: Self.counterSpacing) {
            ForEach(row) { c in
                counterSlot(c, style: style, full: full, tile: tile, lines: lines).frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            ForEach(0..<max(cols - row.count, 0), id: \.self) { _ in
                Color.clear.frame(maxWidth: .infinity, maxHeight: 1)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    /// (home-17) Which names a row of tiles shows, decided once for the row where it measures them: every counter's full
    /// name — the one that says exactly what it counts ("Announcements", "Graded this week") — where every one of them
    /// fits beside its symbol as the tile draws it; else every short name ("Unread", "Graded"), whose line says the rest
    /// ("announcements · 2 courses"). (1.3.79) A tile too narrow for even those beside their symbols puts each symbol over
    /// its name. Never a mix, and never a name cut.
    static func wording(_ list: [Counter], tileWidth: CGFloat) -> (style: DashCounterTile.Style, full: Bool) {
        let full = list.map { $0.fullLabel ?? $0.label }
        let short = list.map(\.label)
        if DashCounterTile.namesFit(full, tileWidth: tileWidth, style: .beside) { return (.beside, true) }
        if DashCounterTile.namesFit(short, tileWidth: tileWidth, style: .beside) { return (.beside, false) }
        return (.over, DashCounterTile.namesFit(full, tileWidth: tileWidth, style: .over))
    }

    /// A counter's place in its row: the tile, or — while it has grown into its panel — the same tile unseen, so the row
    /// keeps its shape. (Under Reduce Motion nothing grows: the panel fades in over its tile as the tile fades out under
    /// it, and back — 1.3.80: the tile no longer stays under it, its coloured edge showing round the panel's rounder
    /// corners.)
    @ViewBuilder
    private func counterSlot(_ c: Counter, style: DashCounterTile.Style, full: Bool, tile: CGFloat, lines: Int) -> some View {
        let name = full ? (c.fullLabel ?? c.label) : c.label
        let line = full ? fullNote(c) : note(c)
        // (1.3.80: with its short name, its full name's line — "19 / 49 points" — where only that one fits)
        let spare = full ? nil : fullNote(c)
        Group {
            if open == c.key {
                DashCounterTile(counter: c, name: name, value: value(c), note: line, stale: stale(c), style: style,
                                width: tile, lines: lines, spare: spare, tip: fullNote(c)) {}
                    .hidden()
                    .accessibilityHidden(true)
            } else {
                DashCounterTile(counter: c, name: name, value: value(c), note: line, stale: stale(c), style: style,
                                width: tile, lines: lines, spare: spare, tip: fullNote(c)) { setOpen(open == c.key ? nil : c.key) }
                    .tourSpot(c.key == "next" ? .counterNext : nil)
                    .dashMorph(c.key, in: morph, enabled: grows)
                    .transition(.opacity)
            }
        }
        .anchorPreference(key: DashTileAnchors.self, value: .bounds) { [c.key: $0] } // (where its panel grows from)
    }

    /// (1.2.2) The counters in the day's order: today, tomorrow, the next seven days, overdue, unread, graded.
    static func ordered(_ list: [Counter]) -> [Counter] {
        let order = ["today", "tomorrow", "next", "overdue", "unread", "graded"]
        let rank = { (k: String) in order.firstIndex(of: k) ?? order.count }
        return list.enumerated().sorted { a, b in
            rank(a.element.key) != rank(b.element.key) ? rank(a.element.key) < rank(b.element.key) : a.offset < b.offset
        }.map(\.element)
    }

    /// A counter's number. (home-25) While the page shows what was kept from last time — or Overdue and Graded are
    /// counted from what was kept — that number shows, faint (`stale`), and rolls to the live one in place, as the list
    /// under it changes in place: never six blank tiles over a full list.
    private func value(_ c: Counter) -> Int? {
        switch c.key {
        case "overdue": return counts?.overdue
        case "graded": return counts?.graded
        default: return c.value
        }
    }

    private func stale(_ c: Counter) -> Bool {
        switch c.key {
        case "overdue", "graded": return !countsLive
        default: return model.kept
        }
    }

    /// (1.2.18) Overdue's number as rows are dismissed from its panel: one less at the press, then what Canvas counts
    /// once it is told (or one back, if it could not be).
    private func cleared(_ step: DashCounterPanel.Cleared) {
        guard var c = counts, let n = c.overdue else { return }
        switch step {
        case .pressed:
            c.overdue = max(n - 1, 0)
        case .failed:
            c.overdue = n + 1
        case .done(let left):
            c.overdue = left.overdue
        }
        let m = c.overdue ?? 0
        c.overdueNote = m > 0 ? "\(m) not submitted" : "Nothing overdue"
        withAnimation(Motion.snappy) { counts = c }
    }

    /// The line that goes with a counter's short name ("announcements · 2 courses", "this week · 19 / 49 points").
    private func note(_ c: Counter) -> String? {
        switch c.key {
        case "overdue": return counts?.overdueNote
        case "graded": return counts?.gradedNote
        default: return c.note
        }
    }

    /// (home-17) The line that goes with its full name ("3 unread · 2 courses", "19 / 49 points"); the panel's, too.
    private func fullNote(_ c: Counter) -> String? {
        switch c.key {
        case "overdue": return counts?.overdueNote
        case "graded": return counts?.gradedFullNote ?? counts?.gradedNote
        default: return c.fullNote ?? c.note
        }
    }

    /// A counter grown out of its tile into its panel, or folded back into it (`nil`); another counter's folds the one
    /// open back as it grows.
    private func setOpen(_ key: String?) {
        if key != open { panelHeight = 0 } // (a new panel is measured afresh)
        withAnimation(morphMotion) { open = key }
        if key != nil { MacTour.shared.did(.counterOpened) } // (the tour's "The cards open")
        watchClicks(key != nil)
        LiveHolds.shared.set("counter panel", holdToken, key != nil) // (1.3.80: nothing changes under an open panel)
        if OpenClock.verbose {
            let how = DashMorphHold.asked.map { "grows, held \($0.fraction) of the way" } ?? (grows ? "grows" : "fades in (Reduce Motion)")
            NSLog("[Simpl] counter: %@", key.map { "\($0) \(how)" } ?? "folded back")
        }
    }

    /// A tile grows into its panel and folds back into it — but not under Reduce Motion, where the panel fades in over
    /// its tile; (the screenshot suite's picture of it part-way, `-SimplMorphHold`) always, so the grow is pictured on a
    /// Mac that reduces motion too.
    private var grows: Bool { !reduceMotion || DashMorphHold.asked != nil }

    /// How a tile grows into its panel and folds back: the house's gentle spring, as 1.3.77 had it; under Reduce Motion a
    /// short fade. (The screenshot suite: `-SimplMorphHold 0.5` holds it half-way, DashMorphHold.)
    private var morphMotion: Animation {
        if let hold = DashMorphHold.asked { return Animation(hold) }
        return reduceMotion ? .easeInOut(duration: 0.18) : Motion.gentle
    }

    /// How the open panel takes the height of what it shows, as that is measured: on the same spring (held with the grow
    /// in the screenshot suite's picture of it part-way, so the panel's height stays part-way too); (1.3.80) under Reduce
    /// Motion at once — nothing grows there, its height either.
    private var heightMotion: Animation? {
        if let hold = DashMorphHold.asked { return Animation(hold) }
        return reduceMotion ? nil : Motion.gentle
    }

    /// (1.3.62) While a counter is open, a click anywhere in the app but its panel — the sidebar, the toolbar, the page —
    /// folds it back (the click still does what it does; on another counter, that one opens). A click on the card of one
    /// of its rows (1.3.79) is the card's, and leaves the panel under it. (1.3.80) Where the panel and the card are is
    /// read from AppKit (`panelAt`; the card's as the tour reads it, TourFrames).
    private func watchClicks(_ on: Bool) {
        if on, clickWatch == nil {
            clickWatch = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown]) { e in
                guard let key = open, let window = e.window, let view = window.contentView else { return e }
                let p = CGPoint(x: e.locationInWindow.x, y: view.bounds.height - e.locationInWindow.y)
                let hit = { (r: CGRect?) in r?.insetBy(dx: -2, dy: -2).contains(p) == true }
                // (a click in another window — a popover, a popup — folds it back too, as one round it in this one does)
                let anchor = panelAt(key)
                let panel = anchor.view == nil ? TourFrames.shared.rect(.counterPanel, in: window)
                    : (anchor.view?.window === window ? anchor.rect() : nil)
                let card = WorkPreview.shared.item == nil ? nil : TourFrames.shared.rect(.preview, in: window)
                // (1.3.187: a press on a row's card — over the window or in a popup of its own — is the card's, wherever
                //  its place is read from)
                if !hit(panel) && !hit(card) && !WorkPreview.shared.isOnCard(e) { setOpen(nil) }
                return e
            }
        } else if !on, let w = clickWatch {
            NSEvent.removeMonitor(w)
            clickWatch = nil
        }
    }

    // MARK: - The view and the side column

    @ViewBuilder
    private func content(_ d: Today) -> some View {
        if twoColumns {
            HStack(alignment: .top, spacing: 28) {
                VStack(alignment: .leading, spacing: 26) { main(d) }
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                VStack(alignment: .leading, spacing: 26) { rail(d, narrow: false) }
                    .frame(width: railWidth, alignment: .topLeading)
            }
        } else {
            // (home-11) the view chosen straight under the counters — picking Activity shows the activity on the first
            // screen — then the day's work (List has it already), then the rest
            VStack(alignment: .leading, spacing: 26) {
                main(d)
                if view != .list { today(d) }
                rail(d, narrow: true)
            }
        }
    }

    /// The view chosen.
    @ViewBuilder
    private func main(_ d: Today) -> some View {
        switch view {
        case .cards: courseCards
        case .list:
            upcoming // (1.3.1: no Courses card here — the sidebar and the Cards view have the courses)
        case .activity: activityList
        }
    }

    /// The side column: the day's work (but in List, which has it), the grades skyline and the week's load — under the
    /// view on a narrow window, the skyline and the load side by side where they fit.
    @ViewBuilder
    private func rail(_ d: Today, narrow: Bool) -> some View {
        if !narrow && view != .list { today(d) }
        if narrow && width >= 760 {
            HStack(alignment: .top, spacing: 22) {
                skyline.frame(maxWidth: .infinity)
                weekLoad(d).frame(maxWidth: .infinity)
            }
        } else {
            skyline
            weekLoad(d)
        }
    }

    // MARK: - Sections

    /// The day's work (or what is next, when nothing is due today), each with its tick.
    private func today(_ d: Today) -> some View {
        PageSection(title: d.list.heading, trailing: d.list.note) {
            VStack(spacing: 0) {
                if d.list.rows.isEmpty {
                    EmptyNote(text: d.list.empty ?? "Nothing due.")
                        .padding(.horizontal, 10)
                }
                ForEach(Array(d.list.rows.enumerated()), id: \.element.id) { i, row in
                    if i > 0 { RowDivider(inset: 92) }
                    // (home-21) drawn as Coming up draws work; in the side column, its figures one over the other.
                    // (1.2.3) a press grows its preview out of the row; a double-click opens it
                    // (1.3.80: a row a live check adds or takes away fades in or out where it is, under Reduce Motion
                    // without moving)
                    DashTodayLine(row: row, narrow: twoColumns) { done in toggle(row, done) }
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.opacity.combined(with: .move(edge: .top)))
                }
            }
            .padding(8)
            .card(radius: 18)
        }
    }

    private var courseCards: some View {
        PageSection(title: "Courses", trailing: courses.map { "\($0.rows.count) \($0.rows.count == 1 ? "course" : "courses")" }, accessory: {
            Button("All Courses") { engine.go(.courses) }
                .buttonStyle(.link)
                .font(.sCallout)
        }) {
            if let c = courses {
                if c.rows.isEmpty {
                    EmptyNote(text: c.empty ?? "No courses chosen yet.", symbol: "books.vertical")
                } else {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 260), spacing: 18, alignment: .top)], alignment: .leading, spacing: 18) {
                        ForEach(c.rows) { row in
                            DashCourseCard(course: row, progress: progressOf(row.id))
                        }
                    }
                }
            } else {
                loadingNote("Reading your courses…")
            }
        }
    }

    /// A course's share handed in: nil until read; a course left out of the answer has nothing to hand in.
    private func progressOf(_ id: String) -> CourseProgress? {
        progress[id] ?? (progressRead ? CourseProgress(done: 0, total: 0) : nil)
    }

    private var upcoming: some View {
        PageSection(title: "Coming up", trailing: upcomingNote, accessory: { hideDoneButton }) {
            if let l = list {
                DashDayList(data: l, wide: mainWidth >= 760) { row, done in toggleListRow(row, done) }
            } else if let listError {
                EmptyNote(text: listError, symbol: "exclamationmark.triangle")
            } else {
                loadingNote("Reading your planner…")
            }
        }
    }

    private var upcomingNote: String? {
        guard let l = list, let total = l.total, total > 0 else { return nil }
        let done = l.done ?? 0
        return "\(total - done) to do · \(done) done"
    }

    @ViewBuilder
    private var hideDoneButton: some View {
        if let l = list, (l.done ?? 0) > 0 || l.hideDone == true {
            let hiding = l.hideDone == true
            Button {
                Task { await loadList(hideDone: !hiding) }
            } label: {
                Label(hiding ? "Show Completed · \(l.done ?? 0)" : "Hide Completed", systemImage: hiding ? "eye" : "checkmark.circle")
                    .font(.sCallout)
            }
            .glassButton()
            .help(hiding ? "Completed and handed-in work is hidden" : "Hide completed and handed-in work")
        }
    }

    private var activityList: some View {
        PageSection(title: "Recent activity", trailing: activity.map { a in
            let n = a.rows.filter { $0.unread == true }.count
            return n > 0 ? "\(n) new" : ""
        }) {
            if let a = activity {
                DashActivityList(data: a) { row in openActivity(row) }
            } else if let activityError {
                EmptyNote(text: activityError, symbol: "exclamationmark.triangle")
            } else {
                loadingNote("Reading recent activity…")
            }
        }
    }

    @ViewBuilder
    private var skyline: some View {
        if let s = sky {
            PageSection(title: "Grades", accessory: {
                Button("All Grades") { engine.go(.grades) }
                    .buttonStyle(.link)
                    .font(.sCallout)
            }) {
                DashSkyline(courses: s.courses)
            }
        }
    }

    @ViewBuilder
    private func weekLoad(_ d: Today) -> some View {
        if let load = d.load, d.hasCourses ?? true {
            PageSection(title: "Week load", trailing: d.loadCaption ?? "Handed in this week") { // (home-22)
                DashWeekLoad(load: load, idle: d.idle)
            }
        }
    }

    private func loadingNote(_ text: String) -> some View {
        HStack(spacing: 10) {
            ProgressView().controlSize(.small)
            Text(text).font(.sCallout).foregroundStyle(.secondary)
        }
        .padding(.vertical, 12)
    }

    // MARK: - Loading

    /// `animated`: after a tick here, the row ticked off leaves and the next one slides in (as To Do's do); any other
    /// load is a redraw, not an arrival.
    private func load(animated: Bool = false) async {
        // (1.2.3: every part opens on what it showed last time, and each asks Canvas at once, not after the day's)
        showKept()
        Task { await loadCounts() }
        Task { await loadCourses() }
        Task { await loadSkyline() }
        Task { await loadView() }
        await model.load(engine, "today", animated: animated)
        guard model.data != nil else { return }
        marks.at = Date() // (1.3.80: read now — the next live check is a minute on)
        // (the screenshot suite: -SimplSheet next grows a counter's panel out of its tile, once the tile has been laid out)
        if let key = UserDefaults.standard.string(forKey: "SimplSheet"), !key.isEmpty, !sheetAsked {
            sheetAsked = true
            try? await Task.sleep(nanoseconds: 600_000_000)
            setOpen(key)
        }
    }

    /// The cards, the List, the activity and the skyline as they were last time (AnswerCache), until the live answers
    /// are in. Their counts wait for the live answer, as every count does: a card's due today and unread, a new dot.
    /// (1.3.78) This session's answers first, whole (read ahead, or from a visit before): nothing held back.
    private func showKept() {
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) {
            if courses == nil, let c = engine.recent("dashCourses", as: DashCoursesData.self)?.value {
                courses = c
                if let p = engine.recent("coursesProgress", as: [String: CourseProgress].self)?.value {
                    progress = p
                    progressRead = true
                }
            } else if courses == nil, var c = engine.kept("dashCourses", as: DashCoursesData.self) {
                for i in c.rows.indices {
                    c.rows[i].unread = nil
                    c.rows[i].dueToday = nil
                }
                courses = c
            }
            if counts == nil, let c = engine.recent("todayCounts", ["kept": false], as: TodayCounts.self) {
                counts = c.value
                countsLive = c.fresh
            }
            if sky == nil, let s = engine.recent("dashSkyline", ["kept": false], as: DashSkylineData.self)?.value ?? engine.kept("dashSkyline", ["kept": false], as: DashSkylineData.self) { sky = s }
            if list == nil, let l = engine.recent("dashList", as: DashListData.self)?.value ?? engine.kept("dashList", as: DashListData.self) { list = l }
            if activity == nil, let a = engine.recent("dashActivity", as: DashActivityData.self)?.value {
                activity = a
            } else if activity == nil, var a = engine.kept("dashActivity", as: DashActivityData.self) {
                for i in a.rows.indices { a.rows[i].unread = nil }
                activity = a
            }
        }
    }

    private func loadCounts() async {
        // (1.2.12) Overdue and Graded at once, as the web Dashboard has them: counted from the assignments the page keeps
        // (read on the last visit), then from Canvas's answer now — reading every course again took a while
        async let quick = try? engine.read("todayCounts", ["kept": true], as: TodayCounts.self)
        async let fresh = try? engine.read("todayCounts", ["kept": false], as: TodayCounts.self)
        if let k = await quick, k.overdue != nil, counts == nil {
            withAnimation(Motion.snappy) { counts = k }
        }
        if let f = await fresh {
            withAnimation(Motion.snappy) {
                counts = f
                countsLive = f.overdue != nil
            }
            marks.shown("todayCounts", ["kept": false], by: engine) // (1.3.80: the answer a live check asks after)
        }
    }

    private func loadCourses() async {
        if let c = try? await engine.read("dashCourses", as: DashCoursesData.self) {
            withAnimation(courses == nil ? nil : Motion.gentle) { courses = c }
            marks.shown("dashCourses", by: engine)
        } else if courses == nil {
            courses = DashCoursesData(rows: [], empty: "Your courses could not be loaded.")
        }
        if let p = try? await engine.read("coursesProgress", as: [String: CourseProgress].self) {
            withAnimation(Motion.gentle) {
                progress = p
                progressRead = true
            }
            marks.shown("coursesProgress", by: engine)
        }
    }

    /// The skyline from the copy kept from the last visit first (at once), then from what Canvas says now.
    private func loadSkyline() async {
        if sky == nil, let kept = try? await engine.read("dashSkyline", ["kept": true], as: DashSkylineData.self), sky == nil {
            sky = kept
        }
        if let fresh = try? await engine.read("dashSkyline", ["kept": false], as: DashSkylineData.self) {
            withAnimation(Motion.gentle) { sky = fresh }
            marks.shown("dashSkyline", ["kept": false], by: engine)
        }
    }

    private func loadView() async {
        switch view {
        case .cards: break
        case .list: await loadList()
        case .activity: await loadActivity()
        }
    }

    private func loadList(hideDone: Bool? = nil, animated: Bool = false) async {
        var args: [String: Any] = [:]
        if let hideDone { args["hideDone"] = hideDone }
        do {
            let l = try await engine.read("dashList", args, as: DashListData.self)
            withAnimation(animated || hideDone != nil ? Motion.gentle : nil) {
                list = l
                listError = nil
            }
            marks.shown("dashList", args, by: engine)
        } catch {
            if list == nil { listError = error.localizedDescription }
        }
    }

    private func loadActivity() async {
        do {
            let a = try await engine.read("dashActivity", as: DashActivityData.self)
            activity = a
            activityError = nil
            marks.shown("dashActivity", by: engine)
        } catch {
            if activity == nil { activityError = error.localizedDescription }
        }
    }

    // MARK: - Live checks (1.3.80)

    /// The live check (LiveRefresh): each part's read asked again with the check's age — the day and its counters,
    /// Overdue's and Graded's numbers, the courses' cards and how much of each is handed in (in Cards), Coming up (List)
    /// or the activity (Activity), the skyline — each answer compared with the one shown, and only the parts that changed
    /// put in place, together: a counter's number rolls to its new count, a card or a row changes, arrives or goes, and
    /// every other part keeps its value, so it is not drawn again (the rows and cards by their ids). A check that finds
    /// nothing new changes nothing: the page is not drawn at all. (Only what the view shows is read: no reads for a view
    /// not chosen.)
    private func liveCheck(_ age: Double) async throws -> LiveFound {
        let e = engine
        var parts: [String] = []
        var changes: [@MainActor () -> Void] = []
        func note(_ part: String) { if !parts.contains(part) { parts.append(part) } }
        if let c = try await e.liveCheck("today", as: Today.self, shown: model.data, marks: model.marks, maxAge: age) {
            for p in Self.changedParts(model.data, c.value) { note(p) }
            changes.append { c.put { model.data = c.value } }
        }
        if let c = try await e.liveCheck("todayCounts", ["kept": false], as: TodayCounts.self, shown: counts, marks: marks, maxAge: age) {
            note("counters")
            changes.append {
                c.put {
                    counts = c.value
                    countsLive = c.value.overdue != nil
                }
            }
        }
        if view == .cards {
            if let c = try await e.liveCheck("dashCourses", as: DashCoursesData.self, shown: courses, marks: marks, maxAge: age) {
                note("cards")
                changes.append { c.put { courses = c.value } }
            }
            if let c = try await e.liveCheck("coursesProgress", as: [String: CourseProgress].self, shown: progressRead ? progress : nil, marks: marks, maxAge: age) {
                note("handed-in")
                changes.append {
                    c.put {
                        progress = c.value
                        progressRead = true
                    }
                }
            }
        }
        if view == .list, list != nil {
            if let c = try await e.liveCheck("dashList", as: DashListData.self, shown: list, marks: marks, maxAge: age) {
                note("list")
                changes.append {
                    c.put {
                        list = c.value
                        listError = nil
                    }
                }
            }
        }
        if view == .activity, activity != nil {
            if let c = try await e.liveCheck("dashActivity", as: DashActivityData.self, shown: activity, marks: marks, maxAge: age) {
                note("activity")
                changes.append {
                    c.put {
                        activity = c.value
                        activityError = nil
                    }
                }
            }
        }
        if sky != nil, let c = try await e.liveCheck("dashSkyline", ["kept": false], as: DashSkylineData.self, shown: sky, marks: marks, maxAge: age) {
            note("skyline")
            changes.append { c.put { sky = c.value } }
        }
        guard !changes.isEmpty else {
            marks.at = Date()
            return .unchanged
        }
        let partMarks = marks
        return LiveFound(parts: parts) {
            // (one transaction: the parts that changed move to where they are now on the house spring, a counter's number
            // rolling — under Reduce Motion a short fade, and the number simply the new one)
            withAnimation(LiveRefresh.motion) {
                for apply in changes { apply() }
            }
            partMarks.at = Date()
        }
    }

    /// The parts of the day a new answer changes: the counters, the day's work (its rows by their ids), the week's load,
    /// the greeting's line.
    private static func changedParts(_ old: Today?, _ new: Today) -> [String] {
        guard let old else { return ["day"] }
        var parts: [String] = []
        if old.counters != new.counters { parts.append("counters") }
        if !LiveCompare.same(old.list, new.list) { parts.append("day") }
        if old.load != new.load || old.idle != new.idle || old.loadCaption != new.loadCaption { parts.append("load") }
        if old.dateLine != new.dateLine || old.me != new.me || old.hasCourses != new.hasCourses || old.notifUnread != new.notifUnread {
            parts.append("greeting")
        }
        return parts.isEmpty ? ["day"] : parts
    }

    // MARK: - Doing

    private func toggle(_ row: WorkRow, _ done: Bool) {
        setDone(row.id, done)
        Task {
            if !(await engine.act("complete", ["id": row.id, "done": done])) {
                setDone(row.id, !done)
            } else {
                try? await Task.sleep(nanoseconds: 450_000_000) // (the tick shows before the row moves, as on To Do)
                await load(animated: true)
                engine.changed()
            }
        }
    }

    private func setDone(_ id: String, _ done: Bool) {
        guard var d = model.data, let i = d.list.rows.firstIndex(where: { $0.id == id }) else { return }
        d.list.rows[i].done = done
        withAnimation(Motion.snappy) { model.data = d }
    }

    /// A tick in the List: shown at once, then Canvas told; put back if it refuses.
    private func toggleListRow(_ row: DashRow, _ done: Bool) {
        setListDone(row.id, done)
        Task {
            if !(await engine.act("complete", ["id": row.id, "done": done])) {
                setListDone(row.id, !done)
            } else {
                try? await Task.sleep(nanoseconds: 450_000_000)
                await loadList(animated: true)
                engine.changed()
            }
        }
    }

    private func setListDone(_ id: String, _ done: Bool) {
        guard var l = list else { return }
        for di in l.days.indices {
            if let ri = l.days[di].rows.firstIndex(where: { $0.id == id }) {
                l.days[di].rows[ri].done = done
            }
        }
        withAnimation(Motion.snappy) { list = l }
    }

    /// An activity item opened: its dot goes (here and on the web Dashboard), and it opens.
    private func openActivity(_ row: DashActivityRow) {
        if row.unread == true, var a = activity, let i = a.rows.firstIndex(where: { $0.id == row.id }) {
            a.rows[i].unread = false
            withAnimation(Motion.snappy) { activity = a }
        }
        Task { _ = await engine.act("dashSeen", ["id": row.id]) }
        if let url = row.url { engine.openWeb(url, title: row.title) }
    }
}

/// (1.3.78) The Dashboard's title in the window's stack, where every screen's is; the Dashboard itself is kept over the
/// stack (DetailStack), so going back to it never draws it again. (home-4) Its view (Cards, List, Activity) and New Task
/// are in its page, by the greeting: the toolbar keeps Back, Refresh, the pinned tools and Search, the same on every
/// screen, and a pinned tool never pushes the page's own controls into the toolbar's » menu.
struct DashboardChrome: View {
    var body: some View {
        Color.clear
            .navigationTitle("Dashboard")
    }
}

extension DashView {
    /// The views offered: Brightspace has no activity stream, so no Activity there.
    static func offered(brightspace: Bool) -> [DashView] {
        brightspace ? [.cards, .list] : allCases
    }
}

/// (1.3.78) The size of the window a view is in — said once it is in a window, and again each time the window is
/// resized — for what follows the window itself rather than the room its column happens to have beside the sidebar:
/// (home-18) a counter's panel that stays inside the window. An unseen view (it takes no presses).
struct WindowWidthProbe: NSViewRepresentable {
    let onSize: (CGSize) -> Void

    func makeNSView(context: Context) -> WindowWidthProbeView {
        let v = WindowWidthProbeView()
        v.onSize = onSize
        return v
    }

    func updateNSView(_ v: WindowWidthProbeView, context: Context) { v.onSize = onSize }
}

final class WindowWidthProbeView: NSView {
    var onSize: (CGSize) -> Void = { _ in }
    private var watch: NSObjectProtocol?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let watch { NotificationCenter.default.removeObserver(watch) }
        watch = nil
        guard let window else { return }
        // (said on the run loop's next turn: this is called while SwiftUI is putting the view in place)
        let size = window.frame.size
        DispatchQueue.main.async { [weak self] in self?.onSize(size) }
        watch = NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: window, queue: .main) { [weak self] n in
            MainActor.assumeIsolated {
                guard let w = n.object as? NSWindow else { return }
                self?.onSize(w.frame.size)
            }
        }
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }
}
