import SwiftUI
import AppKit

/// (1.3.67) What native-app.js `externalTools` gives: every external tool in the student's current courses. (1.3.77) And
/// every place each is used: a course's assignments and module items that open it (`items`), how many in all
/// (`instances`) and the next of them still to do (`next`). Each is optional, so an answer kept from before still decodes.
struct ExternalToolsData: Codable {
    struct Course: Codable, Identifiable, Hashable {
        let id: String
        let name: String
        let color: String
        /// How it opens in this course: `course` with `tool` (its tab), `assignment` or `moduleItem`.
        let open: [String: String]
        /// (1.3.77) Every place it is used in this course, in the order to show them: what is still to do, the soonest
        /// first; then what has no date; then the rest, the latest first; then its module items.
        var items: [Item]?
    }
    /// (1.3.77) One place a tool is used: an assignment (`kind` "assignment") or a module item ("moduleItem"), its
    /// title, its line ("Due Oct 18 at 11:59 PM · 20 pts", "In Week 2: Forces"), where the work stands (as every other
    /// screen's statuses are), and whether it is still to do.
    struct Item: Codable, Hashable {
        let kind: String
        let id: String
        let title: String
        var due: String?
        var sub: String?
        var status: Status?
        var upcoming: Bool?
        /// (2.99.33) Its title without the tool's name in front ("Unit 2" for "Knewton Alta: Unit 2"; Windows' rule).
        var short: String?
    }
    /// (1.3.77) The next place still to do, of all its courses: its title, its line and its course's name.
    struct Next: Codable, Hashable {
        let title: String
        var sub: String?
        var course: String?
        /// (2.99.33) Its title without the tool's name in front.
        var short: String?
    }
    struct Tool: Codable, Identifiable {
        let id: String
        let name: String
        /// Its site; empty when the site is its name.
        let host: String
        let kinds: [String]
        let assignments: Int
        let courses: [Course]
        /// (1.3.77) How many assignments and module items use it, in all its courses.
        var instances: Int?
        /// (1.3.77) The soonest of them still to do.
        var next: Next?
    }
    var rows: [Tool]
    var sub: String?
    var empty: String?
}

/// (1.3.67) External Tools, in the sidebar: every tool the school provides in the student's courses — a course's own
/// (its navigation's) and those found only in its assignments and modules — a card a tool: its name, its site, where it
/// is found, and each course it is in, a press away (Knewton Alta and Macmillan in their own popups, the rest in the
/// tool window). A filter narrows them by name, site or course.
///
/// (1.3.77) The cards stand in columns of their own heights (`ExternalToolColumns`), each in the shortest column so far, so a short
/// card beside a tall one leaves no hole under it; a long course name is cut short inside its chip. A tool used in
/// assignments or module items is a card that is a button as a whole: it says the next of them still to do and how many
/// there are, and a press opens the list of every one of them beside it (`ToolPlacesList`), to choose the one to open —
/// before, only the first in each course could be reached. A tool that is a course's own tab keeps a chip for each such
/// course, which opens the tab itself.
struct ExternalToolsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ExternalToolsData>()
    @State private var query = ""
    /// (1.3.77) The tool whose list of places is open, if one is.
    @State private var listed: String?
    /// (1.3.77) The screenshot suite's list, kept open for its picture: the suite runs several copies of the app at once,
    /// and another coming to the front closes a popover (its app no longer active), so it is opened again.
    @State private var keptOpen: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Group {
            if let d = model.data {
                if d.rows.isEmpty {
                    ContentUnavailableView("No External Tools", systemImage: "puzzlepiece.extension",
                                           description: Text(d.empty ?? "None of your courses has an external tool."))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(PageGround())
                } else {
                    Page {
                        HStack(alignment: .bottom, spacing: 16) {
                            ScreenHeading(title: "External Tools", sub: d.sub ?? "")
                            Spacer(minLength: 12)
                            // (the filter on the page: the window's toolbar already has the app's own search field)
                            HStack(spacing: 6) {
                                Image(systemName: "line.3.horizontal.decrease").foregroundStyle(.secondary)
                                TextField("Filter tools", text: $query).textFieldStyle(.plain).frame(width: 180)
                            }
                            .padding(.horizontal, 12)
                            .frame(height: 32)
                            .background(Color.primary.opacity(0.06), in: Capsule())
                        }
                        let shown = filtered(d.rows)
                        if shown.isEmpty {
                            Text("No tool matches “\(query)”.").font(.sBody).foregroundStyle(.secondary)
                        } else {
                            // (1.3.77) columns of their own heights, not rows as tall as their tallest card: plain stacks side
                            // by side, each card put in the column with least in it (a popover would not show from a card
                            // placed by a Layout of the app's own)
                            ExternalToolColumns(tools: shown) { t in
                                ToolCard(tool: t, listed: listedBinding(t),
                                         openTab: { c in open(t, in: c) },
                                         choose: { c, item in choose(t, c, item) })
                            }
                            .animation(reduceMotion ? nil : Motion.gentle, value: shown.map(\.id))
                        }
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("External Tools")
        .openTimed("External Tools", ready: model.data != nil, live: model.data != nil && !model.kept, source: model.source)
        .task(id: engine.dataVersion) {
            await load()
            await openAskedList()
        }
        .liveChecks(model) // (1.3.80: checked every minute while it is on show; only what changed moves)
        .onChange(of: query) { _, _ in
            keptOpen = nil
            listed = nil
        }
        .onChange(of: listed) { _, now in
            guard now == nil, let id = keptOpen else { return }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { if keptOpen == id { listed = id } }
        }
    }

    private func load() async { await model.load(engine, "externalTools") }

    /// (1.3.77) The screenshot suite's `-SimplExternalToolList knewton`: the list of the first tool whose name has that
    /// word in it, opened a moment after the page is up (its card laid out in the window, for the list to come from).
    private func openAskedList() async {
        // (the flag is only taken once its tool is there: a first run of this task before the answer came, or one the
        // engine's first data change cut short, must not use it up)
        guard let word = UserDefaults.standard.string(forKey: "SimplExternalToolList")?.lowercased(), !word.isEmpty else { return }
        guard let t = model.data?.rows.first(where: { $0.name.lowercased().contains(word) && $0.listable }) else {
            NSLog("[Simpl] tool list: %@ not there yet (%d tools)", word, model.data?.rows.count ?? -1)
            return
        }
        guard LaunchOpen.take("", key: "SimplExternalToolList") != nil else { return }
        NSLog("[Simpl] tool list: opening %@", t.name)
        // (a popover shows for the app in front: the suite runs several copies at once, so this one is brought forward,
        // its window made key, before the list is asked for — as a student's own click always is)
        if UserDefaults.standard.string(forKey: "SimplShotFile") != nil {
            NSApp.activate()
            NSApp.windows.first(where: { $0.isVisible && $0.canBecomeKey })?.makeKeyAndOrderFront(nil)
        }
        // (for a picture, at once: a popover shows only while its app is in front, and the suite starts other copies
        // that take the front a few seconds after this one starts)
        if UserDefaults.standard.string(forKey: "SimplShotFile") == nil { try? await Task.sleep(nanoseconds: 600_000_000) }
        if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { keptOpen = t.id }
        listed = t.id
        // (a task of its own: the page's is cancelled as the engine's data changes, which would cut its wait short)
        Task { await readAgainAsked(t) }
    }

    /// (1.3.82) The screenshot suite's `-SimplExternalToolReread YES`: a moment after the list is open, the tools read
    /// again and put in place under it, the card under the list drawn again — as the tools' answer coming in a second
    /// time did in 1.3.81, when the list's popover was sized and sized again until AppKit stopped the app. The console
    /// says whether the list is still open after it; the picture waits for it.
    private func readAgainAsked(_ t: ExternalToolsData.Tool) async {
        guard UserDefaults.standard.bool(forKey: "SimplExternalToolReread") else { return }
        Shot.began()
        defer { Shot.ended() }
        try? await Task.sleep(nanoseconds: 1_500_000_000)
        NSLog("[Simpl] tool list: the tools read again with %@'s list open", t.name)
        if let d = try? await engine.fetch("externalTools", as: ExternalToolsData.self, quiet: false) {
            withAnimation(Motion.gentle) { model.data = d }
        }
        try? await Task.sleep(nanoseconds: 600_000_000)
        NSLog("[Simpl] tool list: read again; %@'s list %@", t.name, listed == t.id ? "still open" : "closed")
    }

    private func filtered(_ rows: [ExternalToolsData.Tool]) -> [ExternalToolsData.Tool] {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return rows }
        return rows.filter { t in
            t.name.lowercased().contains(q) || t.host.lowercased().contains(q) || t.courses.contains { $0.name.lowercased().contains(q) }
        }
    }

    /// (1.3.77) Whether this tool's list is the one open: set by its card's press, cleared as the list closes.
    private func listedBinding(_ t: ExternalToolsData.Tool) -> Binding<Bool> {
        Binding(get: { listed == t.id }, set: { on in
            if on { listed = t.id } else if listed == t.id { listed = nil }
        })
    }

    /// (1.3.77) A place chosen in a tool's list: the list closes first, and that place opens as it goes (a popup or a
    /// window arriving over a popover still closing is a stutter).
    private func choose(_ t: ExternalToolsData.Tool, _ c: ExternalToolsData.Course, _ item: ExternalToolsData.Item) {
        keptOpen = nil
        listed = nil
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { open(t, in: c, item: item) }
    }

    /// Opened in that course: Knewton Alta's and Macmillan's assignments in their own popups, the rest in the tool window.
    /// Without `item`, as the course's chip opens it (its tab, else its first assignment or module item there); (1.3.77)
    /// with one, that assignment or module item itself, under its own title.
    private func open(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item? = nil) {
        let course = c.open["course"] ?? c.id
        var args = c.open
        var title = t.name
        if let item {
            args = item.kind == "moduleItem" ? ["course": course, "moduleItem": item.id] : ["course": course, "assignment": item.id]
            title = item.title
        }
        let label = (t.name + " " + t.host).lowercased()
        if let a = args["assignment"] {
            if label.contains("knewton") {
                engine.alta = AltaLaunch(course: course, assignment: a, title: title)
                return
            }
            if label.contains("macmillan") || label.contains("sapling") || label.contains("achieve.") {
                engine.macmillan = AltaLaunch(course: course, assignment: a, title: title)
                return
            }
        }
        engine.openTool(ToolLaunch(title: title, args: args))
    }
}

// MARK: - What a card says

/// (1.3.77) One place a tool is used — an assignment or a module item — and the course it is in.
private struct ToolPlace: Identifiable {
    let course: ExternalToolsData.Course
    let item: ExternalToolsData.Item
    var id: String { "\(course.id)/\(item.kind)/\(item.id)" }
}

/// (1.3.77) The next place still to do, as its card says it: its title, when it is due and in which course, and that
/// course's colour.
private struct ToolNext {
    let title: String
    let line: String
    let color: String?
}

private extension ExternalToolsData.Tool {
    /// (1.3.77) Every place it is used, course by course, in the order the answer gives them.
    var places: [ToolPlace] {
        courses.flatMap { c in (c.items ?? []).map { ToolPlace(course: c, item: $0) } }
    }

    /// (1.3.77) Used in assignments or module items: its card opens the list of them.
    var listable: Bool {
        (instances ?? 0) > 0 && courses.contains { !($0.items ?? []).isEmpty }
    }

    /// Its site, unless it has none of its own (an answer from before 1.3.77 gave the site again when it was the name).
    var showsHost: Bool { !host.isEmpty && host.lowercased() != name.lowercased() }

    /// "5 assignments", "3 module items", "5 places" (some of each).
    var placesPhrase: String {
        let all = courses.flatMap { $0.items ?? [] }
        let a = all.filter { $0.kind != "moduleItem" }.count, m = all.count - a
        if m == 0 { return a == 1 ? "1 assignment" : "\(a) assignments" }
        if a == 0 { return m == 1 ? "1 module item" : "\(m) module items" }
        return "\(all.count) places"
    }

    /// What its list is a list of: "assignments", "module items", "places".
    var placesNoun: String {
        let all = courses.flatMap { $0.items ?? [] }
        if all.allSatisfy({ $0.kind != "moduleItem" }) { return "assignments" }
        if all.allSatisfy({ $0.kind == "moduleItem" }) { return "module items" }
        return "places"
    }

    /// The card's tooltip: "Choose an assignment to open".
    var chooseHelp: String {
        switch placesNoun {
        case "assignments": return "Choose an assignment to open"
        case "module items": return "Choose a module item to open"
        default: return "Choose an assignment or module item to open"
        }
    }

    /// Where it was found: how many courses; a course's navigation; (a tool with no list) its assignments, how many,
    /// and its modules — a list's own foot says how many places it has.
    var whereFound: String {
        var parts = [courses.count == 1 ? "1 course" : "\(courses.count) courses"]
        if kinds.contains("Course navigation") { parts.append("Course navigation") }
        if !listable {
            if assignments > 0 { parts.append(assignments == 1 ? "1 assignment" : "\(assignments) assignments") }
            if kinds.contains("Modules") { parts.append("Modules") }
        }
        return parts.joined(separator: " · ")
    }

    /// (1.3.77) The next still to do: "Unit 2" (courses-26: under the tool's own name, without it again), "Due Oct 18
    /// at 11:59 PM · F26-SPRK 010 103".
    var nextShown: ToolNext? {
        guard let n = next else { return nil }
        let due = (n.sub ?? "").components(separatedBy: " · ").first ?? ""
        let line = [due, n.course ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
        let title = (n.short ?? "").isEmpty ? n.title : (n.short ?? n.title)
        return ToolNext(title: title, line: line, color: courses.first { $0.name == n.course }?.color)
    }

    /// (1.3.77) Which place in the list is that next one (the soonest still to do across its courses, not merely the
    /// first in the first course).
    var nextPlaceID: String? {
        let upcoming = places.filter { $0.item.upcoming == true }
        if let n = next, let p = upcoming.first(where: { $0.item.title == n.title && (n.course == nil || $0.course.name == n.course) }) {
            return p.id
        }
        return next == nil ? nil : upcoming.first?.id
    }
}

// MARK: - The card

/// (1.3.67) A tool's card: its monogram in its first course's colour, its name, its site and where it is found.
/// A tool found only in courses' navigation has a chip a course, opening its tab there; the card itself is not a button.
/// (1.3.77) A tool used in assignments or module items is a button as a whole: it gives a little under a press (scale
/// 0.98, 100 ms), washes under the pointer and while its list is open, says the next still to do and, at its foot, how
/// many there are; the press opens the list beside it. Its chips for a course's own tab (when it is one too) stay buttons
/// of their own, laid over the card's — never a button inside a button.
private struct ToolCard: View {
    let tool: ExternalToolsData.Tool
    @Binding var listed: Bool
    let openTab: (ExternalToolsData.Course) -> Void
    let choose: (ExternalToolsData.Course, ExternalToolsData.Item) -> Void
    @State private var hover = false
    @State private var pressed = false
    /// When its list last closed: a press that closed it (a transient popover closes as the mouse goes down outside it)
    /// must not open it again as the mouse comes up.
    @State private var closedAt = Date.distantPast
    /// Whether this card's list is showing — the card's own, as a pinned tool keeps its popover's, kept in step with the
    /// page's `listed` (which card's list is open) both ways.
    @State private var shown = false
    /// (1.3.82) The card as AppKit has it — its window, and where its foot is on the screen — for the room its list has.
    @State private var spot = PreviewAnchor()
    /// (courses-27) The most the list's popover may be tall: (1.3.82) reckoned as the list opens (`reckonRoom`), and
    /// kept while it is open.
    @State private var room: CGFloat = 460
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var tint: Color { Color(hex: tool.courses.first?.color ?? "#8e8e93") }

    var body: some View {
        if tool.listable { listCard } else { tabCard }
    }

    private var tabCard: some View {
        VStack(alignment: .leading, spacing: 12) {
            header
            chips(tool.courses)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 18)
    }

    private var listCard: some View {
        let tabs = tool.courses.filter { $0.open["tool"] != nil }
        return VStack(alignment: .leading, spacing: 12) {
            // (what it says lets a press through to the card's button under it; the button says it all to VoiceOver)
            Group {
                header
                if let n = tool.nextShown { nextLine(n) }
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
            if !tabs.isEmpty { chips(tabs) }
            footer
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background { revealButton }
        // (courses-27: where its foot is, read as its list opens — not on every frame of a scroll)
        .background { PreviewProbe(anchor: spot) }
        .modifier(ListCardSurface(lit: hover || listed))
        .scaleEffect(pressed && !reduceMotion ? 0.98 : 1)
        .animation(.easeOut(duration: 0.1), value: pressed)
        .animation(Motion.hover, value: hover || listed)
        .onHover { hover = $0 }
        .onChange(of: listed) { _, on in
            if !on { closedAt = Date() }
            if on && !shown { reckonRoom() }
            if shown != on { shown = on }
        }
        .onChange(of: shown) { _, on in if listed != on { listed = on } }
        .onAppear {
            // (a card made again while its list is open — moved to another column by a new answer: its list opened again
            // a turn of the run loop later, once the card is in its window, for the room round it to be reckoned there)
            guard listed, !shown else { return }
            DispatchQueue.main.async {
                guard listed, !shown else { return }
                reckonRoom()
                shown = true
            }
        }
    }

    /// The card's own button, the whole card under what it says: it tells the card when it is held, for the card to give.
    private var revealButton: some View {
        let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
        let label: String = "\(tool.name), \(tool.placesPhrase)"
        var next = ""
        if let n = tool.nextShown { next = "Next: \(n.title), \(n.line)" }
        return Button {
            guard Date().timeIntervalSince(closedAt) > 0.3 else { return }
            reckonRoom()
            shown = true
        } label: {
            shape.fill(Color.clear).contentShape(shape)
        }
        .buttonStyle(PressReport(pressed: $pressed))
        .help(tool.chooseHelp)
        .accessibilityLabel(label)
        .accessibilityValue(next)
        .accessibilityHint(tool.chooseHelp)
        // (the list hangs from this button — the whole card, under its words — not from the card's glass surface over
        // it, from which a popover did not show. courses-27: from the card's foot, under it, its arrow at "5 assignments"
        // — beside the card it covered the page's heading and the card itself; with no room below, it goes above)
        .popover(isPresented: $shown, attachmentAnchor: .point(.bottom), arrowEdge: .bottom) {
            ToolPlacesList(tool: tool, tint: tint, room: room, choose: choose, close: { shown = false })
                // (the screenshot suite's console: when a list shows and goes)
                .onAppear { if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { NSLog("[Simpl] tool list shown: %@", tool.name) } }
                .onDisappear { if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { NSLog("[Simpl] tool list closed: %@", tool.name) } }
        }
    }

    /// (courses-27) The room between the card's foot and the window's, less a margin: the list under the card is no
    /// taller than that. With too little room the list goes above the card, as tall as it likes (460 points at most).
    /// (1.3.82) Reckoned as the list opens, from the card's own window and its screen, and kept while the list is open —
    /// and never more than the screen has room for on the side the list goes. 1.3.81 reckoned it each time the card
    /// was drawn, from the key window, which once the list had the keys was the list's own: a card drawn again under its
    /// open list (the tools' answer coming in a second time) asked 460 points of a screen with some 330 over and under
    /// the card, AppKit kept the popover on the screen, SwiftUI made its window as tall as the list again, and the two
    /// went on until AppKit stopped the app (more passes laying a window out than it has views).
    private func reckonRoom() {
        guard let v = spot.view, let window = v.window else { return }
        let card = window.convertToScreen(v.convert(v.bounds, to: nil))
        let screen = (window.screen ?? NSScreen.main)?.visibleFrame ?? window.frame
        // (the popover's arrow and its gap from the card take some 40 points of the screen's room on its side)
        let under = min(card.minY - window.frame.minY - 24, card.minY - screen.minY - 40)
        let over = screen.maxY - card.minY - 40
        let fits = under >= 240 ? min(460, under) : min(460, max(under, over))
        room = max(160, fits.rounded(.down))
        if UserDefaults.standard.string(forKey: "SimplShotFile") != nil {
            NSLog("[Simpl] tool list room: %.0f (%.0f under the card, %.0f over its foot)", room, under, over)
        }
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(String(tool.name.prefix(1)).uppercased())
                .font(.system(size: 19, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 40, height: 40)
                .background(tint.gradient, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
            VStack(alignment: .leading, spacing: 2) {
                Text(tool.name).font(.sHeadline).lineLimit(2)
                if tool.showsHost {
                    Text(tool.host).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                }
                Text(tool.whereFound).font(.sCaption).foregroundStyle(.secondary).lineLimit(2)
            }
            Spacer(minLength: 0)
        }
    }

    /// (1.3.77) The next still to do, in a well: its course's dot, its title and the Next tag; when it is due, and where.
    private func nextLine(_ n: ToolNext) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 7) {
                Circle().fill(Color(hex: n.color)).frame(width: 7, height: 7)
                Text(n.title).font(.sCallout.weight(.medium)).lineLimit(1)
                Spacer(minLength: 6)
                NextTag()
            }
            if !n.line.isEmpty {
                Text(n.line).font(.sCaption).foregroundStyle(.secondary).lineLimit(1).padding(.leading, 14)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
    }

    /// (1.3.77) The card's foot: how many places it has, and a chevron — there is more inside. (courses-29) It points
    /// down, to the list that opens under it here (a right chevron promised a page of its own), and up while it is open.
    private var footer: some View {
        VStack(spacing: 10) {
            Divider()
            HStack(spacing: 6) {
                Text(tool.placesPhrase).font(.sCallout).foregroundStyle(.secondary)
                Spacer(minLength: 6)
                Image(systemName: "chevron.down")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.tertiary)
                    .rotationEffect(.degrees(listed ? 180 : 0))
                    .animation(reduceMotion ? nil : .timingCurve(0.23, 1, 0.32, 1, duration: 0.2), value: listed)
            }
        }
    }

    /// Each course it is a tab in, opened there. (1.3.77) A long course name is cut short inside its chip (its tooltip
    /// says it whole). (courses-31) Each ends in the window's symbol, as the course home's More rows do: it opens in a
    /// Simpl window of its own — ↗ is kept for what leaves for the browser.
    private func chips(_ courses: [ExternalToolsData.Course]) -> some View {
        WrapLayout(spacing: 6) {
            ForEach(courses) { c in
                Button { openTab(c) } label: {
                    HStack(spacing: 6) {
                        Circle().fill(Color(hex: c.color)).frame(width: 7, height: 7)
                        Text(c.name).font(.sCallout.weight(.medium)).lineLimit(1).truncationMode(.tail)
                        Image(systemName: "macwindow").font(.system(size: 10, weight: .semibold)).foregroundStyle(.secondary)
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 6)
                }
                .buttonStyle(ChipStyle())
                .help("Open \(tool.name) in \(c.name)")
            }
        }
    }
}

/// (1.3.77) A list card's ground: the card's own (glass on macOS 26, the card colour before), washed a little under the
/// pointer and while its list is open, so the list is seen to come from it.
private struct ListCardSurface: ViewModifier {
    let lit: Bool

    func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
        if #available(macOS 26.0, *) {
            content
                .background(shape.fill(Color.primary.opacity(lit ? 0.04 : 0)))
                .glassCard(radius: 18)
        } else {
            content
                .background(shape.fill(lit ? Theme.cardHover : Theme.card))
                .overlay(shape.strokeBorder(Theme.edge, lineWidth: 1).allowsHitTesting(false))
                .shadow(color: Theme.shadow, radius: 1.5, y: 1)
        }
    }
}

/// (1.3.77) A button that draws its label alone and tells `pressed` while it is held, for what it lies under to give.
private struct PressReport: ButtonStyle {
    @Binding var pressed: Bool

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .onChange(of: configuration.isPressed) { _, now in pressed = now }
    }
}

/// (1.3.77) A course's chip: a quiet capsule, a little deeper under the pointer and deeper again under a click.
private struct ChipStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        ChipBody(configuration: configuration)
    }

    private struct ChipBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hover = false

        var body: some View {
            configuration.label
                .background(Color.primary.opacity(configuration.isPressed ? 0.14 : (hover ? 0.1 : 0.07)), in: Capsule())
                .contentShape(Capsule())
                .animation(Motion.hover, value: hover)
                .onHover { hover = $0 }
        }
    }
}

/// (1.3.77) "Next": the place still to do soonest, in its card and in its list.
private struct NextTag: View {
    var body: some View {
        Text("Next")
            .font(.sCaption2.weight(.semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 6)
            .padding(.vertical, 1.5)
            .background(Theme.accent, in: Capsule())
            .fixedSize()
    }
}

// MARK: - The list of places

/// (1.3.77) Every place a tool is used, to choose the one to open, in a popover from its card: the tool's tile, name and
/// count; a filter when there are more than eight; the places under each course's name when there is more than one
/// course, each its title, its line and where the work stands, the next still to do tagged. The pointer and the arrow
/// keys move one highlight (it starts on the next still to do), Return or a click opens it, Escape closes the list (a
/// filter with words in it is emptied first). It is as tall as what it holds, up to 460 points, and scrolls past that.
private struct ToolPlacesList: View {
    let tool: ExternalToolsData.Tool
    let tint: Color
    /// (courses-27) The most the whole popover may be tall: the room under its card — (1.3.82) or over it, on the screen;
    /// reckoned once as the list opened.
    let room: CGFloat
    let choose: (ExternalToolsData.Course, ExternalToolsData.Item) -> Void
    let close: () -> Void

    private let all: [ToolPlace]
    private let nextID: String?
    @State private var query = ""
    @State private var lit: Int
    /// Raised by an arrow key: the highlighted place scrolled into view (a place under the pointer already is).
    @State private var keyMoves = 0
    @State private var listHeight: CGFloat
    /// (1.3.82) The list's height as last measured, in whole points: taken up as `listHeight` a turn of the run loop
    /// later, never while the popover is being laid out.
    @State private var measured: CGFloat = 0
    @FocusState private var focus: Focus?

    private enum Focus: Hashable { case filter, list }

    init(tool: ExternalToolsData.Tool, tint: Color, room: CGFloat = 460, choose: @escaping (ExternalToolsData.Course, ExternalToolsData.Item) -> Void, close: @escaping () -> Void) {
        self.tool = tool
        self.tint = tint
        self.room = room
        self.choose = choose
        self.close = close
        let all = tool.places
        let next = tool.nextPlaceID
        self.all = all
        self.nextID = next
        _lit = State(initialValue: all.firstIndex { $0.id == next } ?? 0)
        // (a first guess at its height, until it is measured: a place a row, a course a heading)
        let courses = Set(all.map(\.course.id)).count
        _listHeight = State(initialValue: CGFloat(all.count) * 52 + (courses > 1 ? CGFloat(courses) * 30 : 0) + 12)
    }

    private var courseCount: Int { Set(all.map(\.course.id)).count }
    private var grouped: Bool { courseCount > 1 }
    private var hasFilter: Bool { all.count > 8 }
    /// "5 assignments in 2 courses".
    private var summary: String { "\(tool.placesPhrase) in \(courseCount == 1 ? "1 course" : "\(courseCount) courses")" }
    /// The list's own height at most: the whole popover no taller than 460 points, (courses-27) nor than the room under
    /// its card (its head and filter take 64 or 108 of it).
    private var listMax: CGFloat {
        let head: CGFloat = hasFilter ? 108 : 64
        return max(120, min(460, room) - head)
    }

    /// The list's height: as tall as it is, up to its most.
    private var listFrame: CGFloat { min(max(listHeight, 40), listMax) }

    private var shown: [ToolPlace] {
        let q = query.trimmingCharacters(in: .whitespaces).lowercased()
        guard !q.isEmpty else { return all }
        return all.filter { p in
            p.item.title.lowercased().contains(q) || (p.item.sub ?? "").lowercased().contains(q) || p.course.name.lowercased().contains(q)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            if hasFilter { filterField }
            Divider()
            list(shown)
        }
        .frame(width: 380)
        // (courses-27: solid under the list — the card behind showed through the popover's foot as a cut-off line)
        .background(Color(nsColor: .windowBackgroundColor))
        .background { keyHolder }
        .defaultFocus($focus, firstFocus)
        .task {
            // (the keys to the filter, or to the list: at once, and again once the popover's window is key if not taken)
            focus = firstFocus
            try? await Task.sleep(nanoseconds: 120_000_000)
            if focus == nil { focus = firstFocus }
        }
    }

    private var firstFocus: Focus { hasFilter ? .filter : .list }

    private var header: some View {
        HStack(spacing: 10) {
            Text(String(tool.name.prefix(1)).uppercased())
                .font(.system(size: 12, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 24, height: 24)
                .background(tint.gradient, in: RoundedRectangle(cornerRadius: 6, style: .continuous))
            VStack(alignment: .leading, spacing: 1) {
                Text(tool.name).font(.sHeadline).lineLimit(1)
                Text(summary)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 16)
        .padding(.top, 14)
        .padding(.bottom, 12)
        .accessibilityElement(children: .combine)
    }

    private var filterField: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
            TextField("Filter \(tool.placesNoun)", text: $query)
                .textFieldStyle(.plain)
                .focused($focus, equals: .filter)
                .onSubmit { openLit() }
                .onKeyPress(.downArrow) { move(1); return .handled }
                .onKeyPress(.upArrow) { move(-1); return .handled }
                .onExitCommand { if query.isEmpty { close() } else { query = "" } }
                .onChange(of: query) { _, _ in lit = 0 }
        }
        .padding(.horizontal, 10)
        .frame(height: 30)
        .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .padding(.horizontal, 12)
        .padding(.bottom, 10)
    }

    private func list(_ shown: [ToolPlace]) -> some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    if shown.isEmpty {
                        Text("Nothing matches “\(query.trimmingCharacters(in: .whitespaces))”.")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .padding(10)
                    }
                    ForEach(Array(shown.enumerated()), id: \.element.id) { k, p in
                        VStack(alignment: .leading, spacing: 0) {
                            if grouped && (k == 0 || shown[k - 1].course.id != p.course.id) {
                                courseHeading(p.course, first: k == 0)
                            }
                            row(p, at: k)
                        }
                        .id(p.id)
                    }
                }
                .padding(6)
                .background {
                    GeometryReader { g in Color.clear.preference(key: PlacesListHeightKey.self, value: g.size.height) }
                }
            }
            .scrollBounceBehavior(.basedOnSize)
            // (1.3.82: as tall as the list, up to its most — and shorter where AppKit has less room for the popover on
            // the screen: SwiftUI sizes a popover's window to what it shows each time it lays it out, and a list of one
            // height only, in a popover the screen kept shorter, had it sized back up, pass after pass)
            .frame(minHeight: min(120, listFrame), idealHeight: listFrame, maxHeight: listFrame)
            .onPreferenceChange(PlacesListHeightKey.self) { h in
                // (measured whole, unfiltered: a filter narrowing the list leaves the popover its size)
                let whole = h.rounded(.up)
                if query.isEmpty, abs(whole - measured) > 0.5 { measured = whole }
            }
            .task(id: measured) {
                // (1.3.82: the measure taken up on a later turn of the run loop — the popover sized again while it was
                // being laid out was laid out again, and measured again — and only when it is another by half a point)
                if measured > 0, abs(measured - listHeight) > 0.5 { listHeight = measured }
            }
            .onChange(of: keyMoves) { _, _ in
                if shown.indices.contains(lit) { proxy.scrollTo(shown[lit].id) }
            }
        }
        .animation(Motion.snappy, value: lit)
    }

    /// The list's keys, held by an unseen view of their own under it (as the quiz's are): held by the list itself, a
    /// click on a place after typing in the filter would go to taking the keys back, not to the place. It takes them
    /// whether or not keyboard navigation is on.
    private var keyHolder: some View {
        Color.clear
            .frame(width: 1, height: 1)
            .focusable(interactions: .edit)
            .focused($focus, equals: .list)
            .focusEffectDisabled()
            .onKeyPress(.downArrow) { move(1); return .handled }
            .onKeyPress(.upArrow) { move(-1); return .handled }
            .onKeyPress(.return) { openLit(); return .handled }
            .onExitCommand { close() }
            .accessibilityHidden(true)
    }

    private func courseHeading(_ c: ExternalToolsData.Course, first: Bool) -> some View {
        HStack(spacing: 6) {
            Circle().fill(Color(hex: c.color)).frame(width: 7, height: 7)
            Text(c.name).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary).lineLimit(1)
        }
        .padding(.horizontal, 10)
        .padding(.top, first ? 4 : 12)
        .padding(.bottom, 4)
        .accessibilityAddTraits(.isHeader)
    }

    private func row(_ p: ToolPlace, at k: Int) -> some View {
        Button { choose(p.course, p.item) } label: {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        // (courses-26: "Unit 2" under the list's "Knewton Alta", not its name again on every row)
                        Text((p.item.short ?? "").isEmpty ? p.item.title : (p.item.short ?? p.item.title))
                            .font(.sCallout.weight(.medium))
                            .lineLimit(2)
                        if p.id == nextID { NextTag() }
                    }
                    if let sub = p.item.sub, !sub.isEmpty {
                        Text(sub).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                    }
                }
                Spacer(minLength: 8)
                if let s = p.item.status, !s.word.isEmpty { StatusChip(text: s.word, tone: s.kind) }
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(k == lit ? Theme.accent.opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(PlaceRowStyle())
        .onHover { inside in if inside { lit = k } }
    }

    private func move(_ by: Int) {
        let n = shown.count
        guard n > 0 else { return }
        lit = min(max(lit + by, 0), n - 1)
        keyMoves += 1
    }

    private func openLit() {
        let s = shown
        guard s.indices.contains(lit) else { return }
        choose(s[lit].course, s[lit].item)
    }
}

/// (1.3.77) A place's row under a click: a little deeper than its highlight.
private struct PlaceRowStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .background(Theme.accent.opacity(configuration.isPressed ? 0.12 : 0), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
    }
}

private struct PlacesListHeightKey: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

// MARK: - Layouts

/// (1.3.77) The cards in columns of their own heights, not rows as tall as their tallest card: as many 320pt columns as
/// the page has room for (Page's width), plain stacks side by side, each card in turn put in the column whose cards are
/// the shortest so far — reckoned by what each draws: its next, its list's foot, its rows of chips. (A Layout of the
/// app's own placed them before, and a card's popover would not show from it.)
private struct ExternalToolColumns<Card: View>: View {
    let tools: [ExternalToolsData.Tool]
    @ViewBuilder let card: (ExternalToolsData.Tool) -> Card
    @Environment(\.pageWidth) private var pageWidth

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            ForEach(Array(columns().enumerated()), id: \.offset) { _, column in
                VStack(alignment: .leading, spacing: 16) {
                    ForEach(column) { t in card(t) }
                }
                .frame(maxWidth: .infinity, alignment: .top)
            }
        }
    }

    private func columns() -> [[ExternalToolsData.Tool]] {
        let width = pageWidth ?? 700
        let count = max(1, min(tools.count, Int((width + 16) / (320 + 16))))
        var cols = Array(repeating: [ExternalToolsData.Tool](), count: count)
        var heights = Array(repeating: CGFloat(0), count: count)
        for t in tools {
            let tabs = t.courses.filter { $0.open["tool"] != nil }.count
            let estimate: CGFloat = 90 + (t.listable ? (t.next != nil ? 110 : 50) : 0) + CGFloat((tabs + 1) / 2) * 36
            let i = heights.indices.min(by: { heights[$0] < heights[$1] }) ?? 0
            cols[i].append(t)
            heights[i] += estimate + 16
        }
        return cols
    }
}

/// Views laid left to right, onto a new line when the next would not fit. (1.3.77) One wider than a whole line is
/// offered the line's width, and placed at that, so it fits by cutting itself short (a chip's long course name).
private struct WrapLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 320
        var x: CGFloat = 0, y: CGFloat = 0, line: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let s = size(of: v, within: width)
            if x > 0 && x + s.width > width {
                x = 0
                y += line + spacing
                line = 0
            }
            x += s.width + spacing
            line = max(line, s.height)
            widest = max(widest, x - spacing)
        }
        return CGSize(width: min(width, widest), height: y + line)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, line: CGFloat = 0
        for v in subviews {
            let s = size(of: v, within: bounds.width)
            if x > bounds.minX && x + s.width > bounds.maxX {
                x = bounds.minX
                y += line + spacing
                line = 0
            }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(width: s.width, height: s.height))
            x += s.width + spacing
            line = max(line, s.height)
        }
    }

    /// A view's own size, or — wider than the line — its size offered the line's width.
    private func size(of v: LayoutSubview, within width: CGFloat) -> CGSize {
        let ideal = v.sizeThatFits(.unspecified)
        guard ideal.width > width else { return ideal }
        return v.sizeThatFits(ProposedViewSize(width: max(0, width), height: nil))
    }
}
