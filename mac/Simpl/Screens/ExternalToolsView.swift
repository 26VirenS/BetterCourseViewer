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
/// is found, and each course it is in, a press away (Knewton Alta in its own popup, the rest in the
/// tool window). A filter narrows them by name, site or course.
///
/// (1.3.77) A press on a tool opens the list of every place it is used (`ToolPlacesList`), to choose the one to open.
///
/// (1.3.222) The tools are bubbles in a honeycomb, as the watch's apps are (ToolBubbles.swift): the one needed soonest in
/// the middle, the rest in rings round it; each filled with its tool's colour from the logo on its site, the logo's mark
/// in white on it and its name along its lower edge; a badge says how many are still to do. A press opens the tool's
/// list in a square popup — its courses' own tabs first, then its assignments and module items (the cards' chips before).
struct ExternalToolsView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<ExternalToolsData>()
    @State private var query = ""
    /// (1.3.77) The tool whose list of places is open, if one is. (1.3.204: in a popup over the page, not a popover)
    @State private var listed: String?
    /// (1.3.178) The answer the cards were drawn from as a tool's list opened, held while it is open; a newer one (the
    /// school's after the kept one, a live check) put in once it closes. A card drawn again under its open list — moved
    /// to another column by the newer answer — had its popover presented afresh in the middle of laying the window out,
    /// and AppKit stopped the app (more passes laying it out than it has views; 1.3.175's dark-36b picture). (1.3.204:
    /// kept for the popup, so the list does not change under the pointer.)
    @State private var held: ExternalToolsData?
    /// (1.3.222) The tools' logos, for the open list's colour.
    @ObservedObject private var logos = ToolLogos.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// What the cards are drawn from: the latest answer, or while a list is open the one it opened from.
    private var shownData: ExternalToolsData? { listed != nil ? held ?? model.data : model.data }

    var body: some View {
        Group {
            if let d = shownData {
                if d.rows.isEmpty {
                    ContentUnavailableView("No External Tools", systemImage: "puzzlepiece.extension",
                                           description: Text(d.empty ?? "None of your courses has an external tool."))
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                        .background(PageGround())
                } else {
                    bubblePage(d)
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .overlay { placesPopup }
        .navigationTitle("External Tools")
        .openTimed("External Tools", ready: model.data != nil, live: model.data != nil && !model.kept, source: model.source)
        .task(id: engine.dataVersion) {
            await load()
            await openAskedList()
        }
        .liveChecks(model) // (1.3.80: checked every minute while it is on show; only what changed moves)
        .onChange(of: query) { _, _ in listed = nil }
        .onChange(of: listed) { was, now in
            // (1.3.178: the answer held while a list is open, the newest drawn as it closes)
            if was == nil, now != nil { held = model.data }
            if now == nil { held = nil }
        }
    }

    /// (1.3.222) The page: its heading and filter, and under them the tools as bubbles in a honeycomb (ToolBubbles.swift),
    /// the one needed next in the middle — centred in the room the window leaves, scrolling when there are more than fit.
    private func bubblePage(_ d: ExternalToolsData) -> some View {
        GeometryReader { g in
            let pad: CGFloat = g.size.width < 700 ? 22 : 40
            let room = max(0, min(1480, g.size.width - pad * 2))
            ScrollView {
                VStack(alignment: .leading, spacing: 22) {
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
                    let shown = specs(filtered(d.rows))
                    if shown.isEmpty {
                        Text("No tool matches “\(query)”.").font(.sBody).foregroundStyle(.secondary)
                    } else {
                        ToolBubbleField(specs: shown, width: room, lit: listed) { id in press(id, in: d) }
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
                .frame(maxWidth: 1480, alignment: .leading)
                .padding(.horizontal, pad)
                .padding(.top, 26)
                .padding(.bottom, 40)
                .frame(maxWidth: .infinity, minHeight: g.size.height, alignment: .top)
            }
        }
        .background(PageGround())
    }

    /// (1.3.222) Each tool as its bubble: the one needed soonest first (the middle), then those with something missing,
    /// then those with places to choose from, then a course's own tabs — by name within each. Its badge: how many are
    /// still to do (else how many are missing), red with something missing, orange with something due today.
    private func specs(_ rows: [ExternalToolsData.Tool]) -> [BubbleSpec] {
        let ranked = rows.map { t -> (t: ExternalToolsData.Tool, group: Int, due: Date) in
            let due = t.nextDue
            let group = due != nil ? 0 : t.missingCount > 0 ? 1 : t.places.isEmpty ? 3 : 2
            return (t, group, due ?? .distantFuture)
        }
        .sorted { a, b in
            if a.group != b.group { return a.group < b.group }
            if a.due != b.due { return a.due < b.due }
            return a.t.name.localizedCaseInsensitiveCompare(b.t.name) == .orderedAscending
        }
        return ranked.map { x in
            let t = x.t
            let toDo = t.toDoCount, missing = t.missingCount
            let tone: BubbleTone = missing > 0 ? .late : (x.group == 0 && Calendar.current.isDateInToday(x.due) ? .today : .plain)
            // (a tool that is only courses' tabs has nothing to choose but the course)
            let onlyTabs = !t.places.contains { $0.item.kind != "tab" }
            var help = onlyTabs ? "Open \(t.name)" : t.chooseHelp
            if let n = t.nextShown { help = "Next: " + ([n.title, n.line].filter { !$0.isEmpty }.joined(separator: " · ")) }
            var said = [t.name]
            if toDo > 0 { said.append("\(toDo) to do") }
            if missing > 0 { said.append("\(missing) missing") }
            return BubbleSpec(id: t.id, name: t.name, host: t.host, color: t.courses.first?.color ?? "#8e8e93",
                              count: toDo > 0 ? toDo : missing, tone: tone, help: help, label: said.joined(separator: ", "))
        }
    }

    /// (1.3.222) A bubble pressed: its list of places (and its courses' own tabs) in the square popup — or, a tool that is
    /// only one course's tab, that tab at once (nothing to choose).
    private func press(_ id: String, in d: ExternalToolsData) {
        guard let t = d.rows.first(where: { $0.id == id }) else { return }
        let places = t.places
        if places.count == 1, let only = places.first, only.item.kind == "tab" {
            open(t, in: only.course)
            return
        }
        guard !places.isEmpty else {
            if let c = t.courses.first { open(t, in: c) }
            return
        }
        listed = id
    }

    /// (1.3.204) The open tool's list in a popup of its own over the page — a little wider than a card, centred, the
    /// page dimmed a touch under it — rather than a popover hanging from its card, cramped by the room under it. It
    /// grows in from just under its size and fades (220 ms, a strong ease-out; a fade alone under Reduce Motion), and
    /// goes the same way, quicker; a click outside it, Escape or its × closes it.
    private var placesPopup: some View {
        GeometryReader { g in
            ZStack {
                if let id = listed, let t = shownData?.rows.first(where: { $0.id == id }) {
                    Color.black.opacity(0.2)
                        .contentShape(Rectangle())
                        .onTapGesture { listed = nil }
                        .accessibilityHidden(true)
                        .transition(.opacity)
                    let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
                    ToolPlacesList(tool: t, tint: listTint(t),
                                   room: max(300, min(560, g.size.height - 56, g.size.width - 40)),
                                   choose: { c, item in choose(t, c, item) }, close: { listed = nil })
                        .clipShape(shape)
                        .overlay(shape.strokeBorder(Theme.edge, lineWidth: 1).allowsHitTesting(false))
                        .shadow(color: .black.opacity(0.24), radius: 30, y: 14)
                        .id(t.id)
                        .transition(popupArrival)
                        // (the screenshot suite's console: when a list shows and goes)
                        .onAppear {
                            if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { NSLog("[Simpl] tool list shown: %@", t.name) }
                            if UserDefaults.standard.string(forKey: "SimplExternalToolList") != nil { Self.askedListShown = true }
                        }
                        .onDisappear { if UserDefaults.standard.string(forKey: "SimplShotFile") != nil { NSLog("[Simpl] tool list closed: %@", t.name) } }
                }
            }
            .frame(width: g.size.width, height: g.size.height)
        }
        .allowsHitTesting(listed != nil)
        .animation(reduceMotion ? .easeOut(duration: 0.15) : .timingCurve(0.23, 1, 0.32, 1, duration: 0.22), value: listed)
    }

    /// (1.3.222) The list's colour: its logo's, else its first course's.
    private func listTint(_ t: ExternalToolsData.Tool) -> Color {
        if !t.host.isEmpty, let l = logos.look(t.host), l.kind != .none { return Color(hex: l.brand) }
        return Color(hex: t.courses.first?.color ?? "#8e8e93")
    }

    private var popupArrival: AnyTransition {
        if reduceMotion { return .opacity }
        return .asymmetric(insertion: .scale(scale: 0.96).combined(with: .opacity),
                           removal: .scale(scale: 0.98).combined(with: .opacity))
    }

    private func load() async { await model.load(engine, "externalTools") }

    /// (1.3.77) The screenshot suite's `-SimplExternalToolList knewton`: the list of the first tool whose name has that
    /// word in it, opened a moment after the page is up.
    private func openAskedList() async {
        // (the flag is only used up once its list has shown — 1.3.204: the page made afresh as its answers came in, a
        // list asked for in the copy made first never showed; each copy asks until one has)
        guard !Self.askedListShown, let word = UserDefaults.standard.string(forKey: "SimplExternalToolList")?.lowercased(), !word.isEmpty else { return }
        guard let t = model.data?.rows.first(where: { $0.name.lowercased().contains(word) && $0.listable }) else {
            NSLog("[Simpl] tool list: %@ not there yet (%d tools)", word, model.data?.rows.count ?? -1)
            return
        }
        guard listed != t.id else { return }
        NSLog("[Simpl] tool list: opening %@", t.name)
        // (for a picture, at once; otherwise once the page has been seen a moment)
        if UserDefaults.standard.string(forKey: "SimplShotFile") == nil { try? await Task.sleep(nanoseconds: 600_000_000) }
        listed = t.id
        // (a task of its own: the page's is cancelled as the engine's data changes, which would cut its wait short)
        if !Self.rereadAsked {
            Self.rereadAsked = true
            Task { await readAgainAsked(t) }
        }
    }

    /// (1.3.204) The suite's list has shown, and its reading again has been asked for: once a run.
    @MainActor private static var askedListShown = false
    @MainActor private static var rereadAsked = false

    /// (1.3.82) The screenshot suite's `-SimplExternalToolReread YES`: a moment after the list is open, the tools read
    /// again and put in place under it — as the tools' answer coming in a second time did in 1.3.81, when the list's
    /// popover was sized and sized again until AppKit stopped the app (1.3.178: held until the list closes, the cards
    /// under it left as they were). The console says whether the list is still open after it; the picture waits for it.
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
    /// window arriving over a list still closing is a stutter).
    private func choose(_ t: ExternalToolsData.Tool, _ c: ExternalToolsData.Course, _ item: ExternalToolsData.Item) {
        listed = nil
        // (1.3.222: a course's own tab, as its chip on the card opened it)
        if item.kind == "tab" {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { open(t, in: c) }
            return
        }
        // (1.3.101) a Knewton Alta assignment's launch asked of Canvas at once, while the list closes: only its popup waits
        let alta = altaLaunch(t, in: c, item: item)
        if let alta { engine.askAlta(alta) }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { open(t, in: c, item: item, alta: alta) }
    }

    /// Where a tool opens in a course (ToolOpening).
    private func place(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item?) -> (course: String, args: [String: String], title: String) {
        ToolOpening.place(t, in: c, item: item)
    }

    /// The Knewton Alta assignment this opens in its popup, if it is one (ToolOpening).
    private func altaLaunch(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item?) -> AltaLaunch? {
        ToolOpening.altaLaunch(t, in: c, item: item)
    }

    private func open(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item? = nil, alta: AltaLaunch? = nil) {
        ToolOpening.open(t, in: c, item: item, alta: alta, engine: engine)
    }
}

/// (1.3.152) How a tool opens in a course, on its own or on one of its places — the External Tools screen's, and the
/// search's /open (SearchCommands.swift) the same way.
@MainActor
enum ToolOpening {
    /// Where a tool opens in a course: the Canvas arguments it opens with, and its title.
    static func place(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item?) -> (course: String, args: [String: String], title: String) {
        let course = c.open["course"] ?? c.id
        guard let item else { return (course, c.open, t.name) }
        let args = item.kind == "moduleItem" ? ["course": course, "moduleItem": item.id] : ["course": course, "assignment": item.id]
        return (course, args, item.title)
    }

    /// The Knewton Alta assignment this opens in its popup, if it is one.
    static func altaLaunch(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item?) -> AltaLaunch? {
        let p = place(t, in: c, item: item)
        guard let a = p.args["assignment"], (t.name + " " + t.host).lowercased().contains("knewton") else { return nil }
        // (1.3.101) with the assignment's due line, as its row says it ("Due Oct 12 at 11:59 PM · 10 pts"; "" for "No due
        // date"), for the popup's header
        let first = item?.sub?.components(separatedBy: " · ").first
        let due = first.map { $0.hasPrefix("Due ") ? $0 : "" }
        return AltaLaunch(course: p.course, assignment: a, title: p.title, dueLine: due)
    }

    /// (1.3.201) The Macmillan Learning or ALEKS assignment this opens in its popup, if it is one: the tool known by the
    /// engine's provider ("macmillan", "aleks"), else by the host it launches on (lmslink.bfwpub.com, secure.aleks.com).
    /// (1.3.203) Or its module item, launched as that.
    static func lookLaunch(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item?) -> LookLaunch? {
        let p = place(t, in: c, item: item)
        guard p.args["assignment"] != nil || p.args["moduleItem"] != nil,
              let look = PageLook.forAssignment(macmillan: t.id == "macmillan" ? true : nil, aleks: t.id == "aleks" ? true : nil, host: t.host)
        else { return nil }
        let due = item?.sub?.components(separatedBy: " · ").first { $0.hasPrefix("Due ") } ?? ""
        return LookLaunch(look: look.name, course: p.course, assignment: p.args["assignment"] ?? "", title: p.title, dueLine: due,
                          moduleItem: p.args["moduleItem"])
    }

    /// Opened in that course: Knewton Alta's assignments in their own popup, (1.3.201) Macmillan Learning's and ALEKS's
    /// in theirs, the rest in the tool window.
    /// Without `item`, as the course's chip opens it (its tab, else its first assignment or module item there); (1.3.77)
    /// with one, that assignment or module item itself, under its own title.
    /// (1.3.101) `alta`: the Alta launch already asked for as the place was chosen.
    static func open(_ t: ExternalToolsData.Tool, in c: ExternalToolsData.Course, item: ExternalToolsData.Item? = nil, alta: AltaLaunch? = nil, engine: Engine) {
        if let a = alta ?? altaLaunch(t, in: c, item: item) {
            engine.alta = a
            return
        }
        if let l = lookLaunch(t, in: c, item: item) {
            NSLog("[Simpl] %@ popup: opened from External Tools (its tool on %@)", l.look, t.host)
            engine.lookTool = l
            return
        }
        let (_, args, title) = place(t, in: c, item: item)
        engine.openTool(ToolLaunch(title: title, args: args))
    }
}

// MARK: - What a card says

/// (1.3.222) Canvas's dates ("2026-11-23T07:59:00Z", with or without fractions of a second).
enum ToolDates {
    private static let plain: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f
    }()
    private static let fractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    static func date(_ s: String?) -> Date? {
        guard let s, !s.isEmpty else { return nil }
        return plain.date(from: s) ?? fractional.date(from: s)
    }
}

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
    /// (1.3.77) Every place it is used, course by course, in the order the answer gives them. (1.3.222) A course it is a
    /// tab in has that tab first, a row of its own (kind "tab"): its card's chip before.
    var places: [ToolPlace] {
        courses.flatMap { c -> [ToolPlace] in
            let tab: [ToolPlace] = c.open["tool"] == nil ? [] : [ToolPlace(course: c, item: ExternalToolsData.Item(
                kind: "tab", id: c.id, title: "\(name) in \(c.name)", sub: "The course’s own tab, in a Simpl window"))]
            return tab + (c.items ?? []).map { ToolPlace(course: c, item: $0) }
        }
    }

    /// (1.3.222) How many are still to do, and how many are missing.
    var toDoCount: Int { places.filter { $0.item.upcoming == true }.count }
    var missingCount: Int { places.filter { $0.item.status?.kind == "bad" }.count }

    /// (1.3.222) When the soonest still to do is due.
    var nextDue: Date? {
        places.compactMap { $0.item.upcoming == true ? ToolDates.date($0.item.due) : nil }.min()
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

/// (1.3.77) Every place a tool is used, to choose the one to open — (1.3.204) in a popup over the page, centred (a popover
/// from its card before, cramped by the room under it). (1.3.222) A square, 560 points a side (less in a small window),
/// the list scrolling inside it; its head has the tool's bubble. One list of them all, in their order — under each
/// course's name when there is more than one — as before 1.3.202's To Do and All and its Up Next card (1.3.205: one list
/// again, the next still to do in it with its Next tag, the highlight starting on it and the list scrolled to it). Its head
/// is washed in the tool's colour — its tile, its name, its site and how many there are, and how many are still to do; a
/// filter when there are more than eight; each place with a mark of where it stands (to do, done, missing, a module item),
/// its title and its line, its status and, under the highlight, a chevron; at the foot, the keys it takes. The pointer
/// and the arrow keys move the highlight, Return or a click opens it, Escape or its × closes the list (a filter with words
/// in it is emptied first). It is as tall as the list, up to 720 points (less on a short window), and scrolls past that —
/// a filter leaves it its size.
private struct ToolPlacesList: View {
    let tool: ExternalToolsData.Tool
    let tint: Color
    /// (1.3.222) The popup's side: 560 points, or less in a small window.
    let room: CGFloat
    let choose: (ExternalToolsData.Course, ExternalToolsData.Item) -> Void
    let close: () -> Void

    private let all: [ToolPlace]
    private let nextID: String?
    /// How many are still to do.
    private let toDo: Int
    @State private var query = ""
    @State private var lit: Int
    /// Raised by an arrow key: the highlighted place scrolled into view (a place under the pointer already is).
    @State private var keyMoves = 0
    @FocusState private var focus: Focus?

    private enum Focus: Hashable { case filter, list }

    init(tool: ExternalToolsData.Tool, tint: Color, room: CGFloat = 560, choose: @escaping (ExternalToolsData.Course, ExternalToolsData.Item) -> Void, close: @escaping () -> Void) {
        self.tool = tool
        self.tint = tint
        self.room = room
        self.choose = choose
        self.close = close
        let all = tool.places
        let next = tool.nextPlaceID
        self.all = all
        self.nextID = next
        self.toDo = all.filter { $0.item.upcoming == true }.count
        _lit = State(initialValue: all.firstIndex { $0.id == next } ?? 0)
    }

    private var courseCount: Int { Set(all.map(\.course.id)).count }
    private var grouped: Bool { courseCount > 1 }
    private var hasFilter: Bool { all.count > 8 }
    private var trimmed: String { query.trimmingCharacters(in: .whitespaces) }
    /// "knewton.com · 5 assignments in 2 courses".
    private var summary: String {
        let courses = courseCount == 1 ? "1 course" : "\(courseCount) courses"
        // (1.3.222: a tool that is only courses' own tabs)
        let places = all.contains { $0.item.kind != "tab" } ? "\(tool.placesPhrase) in \(courses)" : "Its tab in \(courses)"
        return tool.showsHost ? "\(tool.host) · \(places)" : places
    }
    /// What the list shows, in its order: all of it, or what the filter's words find.
    private var shown: [ToolPlace] {
        let q = trimmed.lowercased()
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
                .frame(maxHeight: .infinity)
            Divider()
            foot
        }
        .frame(width: min(560, room), height: min(560, room))
        // (solid: the popup over the dimmed page)
        .background(Color(nsColor: .windowBackgroundColor))
        .background { keyHolder }
        .background { escapeKey }
        .defaultFocus($focus, firstFocus)
        .task {
            // (the keys to the filter, or to the list: at once, and again once the popover's window is key if not taken)
            focus = firstFocus
            try? await Task.sleep(nanoseconds: 120_000_000)
            if focus == nil { focus = firstFocus }
        }
    }

    private var firstFocus: Focus { hasFilter ? .filter : .list }

    /// (1.3.202) The tool's tile, name and site, how many places in how many courses, and how many still to do — on a wash
    /// of its colour.
    private var header: some View {
        HStack(spacing: 12) {
            ToolOrb(name: tool.name, host: tool.host, color: tool.courses.first?.color ?? "#8e8e93", size: 42, named: false)
            VStack(alignment: .leading, spacing: 2) {
                Text(tool.name).font(.sHeadline).lineLimit(1)
                Text(summary).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 8)
            if toDo > 0 {
                VStack(alignment: .trailing, spacing: 0) {
                    Text("\(toDo)").font(.system(size: 20, weight: .bold, design: .rounded)).monospacedDigit()
                    Text("to do").font(.sCaption2.weight(.medium)).foregroundStyle(.secondary)
                }
                .accessibilityElement(children: .combine)
            }
            closeButton
        }
        .padding(.horizontal, 16)
        .frame(height: 64)
        .background(LinearGradient(colors: [tint.opacity(0.18), tint.opacity(0.02)], startPoint: .top, endPoint: .bottom))
    }

    /// (1.3.204) The popup's × — (UI review) every popup's.
    private var closeButton: some View {
        PopupCloseButton(action: close)
    }

    /// (1.3.204) Escape, wherever the keys are in the window: the filter emptied first, then the list closed.
    private var escapeKey: some View {
        Button("") { if query.isEmpty { close() } else { query = "" } }
            .keyboardShortcut(.cancelAction)
            .opacity(0)
            .frame(width: 0, height: 0)
            .accessibilityHidden(true)
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
                .onChange(of: query) { _, now in lit = now.isEmpty ? (all.firstIndex { $0.id == nextID } ?? 0) : 0 }
        }
        .padding(.horizontal, 10)
        .frame(height: 30)
        .background(Color.primary.opacity(0.06), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .padding(.horizontal, 12)
        .padding(.top, 10)
        .padding(.bottom, 10)
    }

    private func list(_ shown: [ToolPlace]) -> some View {
        ScrollViewReader { proxy in
            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    if shown.isEmpty {
                        Text("Nothing matches “\(trimmed)”.")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .padding(10)
                    }
                    ForEach(Array(shown.enumerated()), id: \.element.id) { k, p in
                        VStack(alignment: .leading, spacing: 0) {
                            if grouped && (k == 0 || shown[k - 1].course.id != p.course.id) {
                                courseHeading(p.course, count: shown.filter { $0.course.id == p.course.id }.count, first: k == 0)
                            }
                            row(p, at: k)
                        }
                        .id(p.id)
                    }
                }
                .padding(8)
            }
            .scrollBounceBehavior(.basedOnSize)
            .task {
                // (1.3.205: the next still to do in view as the list opens, with what comes before it above)
                try? await Task.sleep(nanoseconds: 60_000_000)
                if let id = nextID { proxy.scrollTo(id, anchor: UnitPoint(x: 0.5, y: 0.3)) }
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

    private func courseHeading(_ c: ExternalToolsData.Course, count: Int, first: Bool) -> some View {
        HStack(spacing: 6) {
            Circle().fill(Color(hex: c.color)).frame(width: 7, height: 7)
            Text(c.name).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary).lineLimit(1)
            Spacer(minLength: 6)
            Text("\(count)").font(.sCaption2.weight(.semibold)).monospacedDigit().foregroundStyle(.tertiary)
        }
        .padding(.horizontal, 10)
        .padding(.top, first ? 4 : 12)
        .padding(.bottom, 4)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isHeader)
    }

    private func title(_ p: ToolPlace) -> String {
        // (courses-26: "Unit 2" under the list's "Knewton Alta", not its name again on every row)
        (p.item.short ?? "").isEmpty ? p.item.title : (p.item.short ?? p.item.title)
    }

    /// (1.3.202) Where a place stands, at a glance: a tick for work done, a mark for work missing, a clock for work late,
    /// a calendar for work still to do, a list for a module item — in its status's colour, or its course's.
    private func mark(_ p: ToolPlace, size: CGFloat = 28) -> some View {
        let look = Self.markLook(p)
        let color = look.tone.map { StatusChip.color($0) } ?? Color(hex: p.course.color)
        return Image(systemName: look.symbol)
            .font(.system(size: size * 0.43, weight: .semibold))
            .foregroundStyle(color)
            .frame(width: size, height: size)
            .background(color.opacity(0.14), in: RoundedRectangle(cornerRadius: size * 0.3, style: .continuous))
            .accessibilityHidden(true)
    }

    private static func markLook(_ p: ToolPlace) -> (symbol: String, tone: String?) {
        if p.item.kind == "tab" { return ("macwindow", nil) } // (1.3.222: a course's own tab)
        if p.item.kind == "moduleItem" { return ("list.bullet", nil) }
        switch p.item.status?.kind ?? "" {
        case "good": return ("checkmark", "good")
        case "bad": return ("exclamationmark", "bad")
        case "warn": return ("clock", "warn")
        default: return p.item.upcoming == true ? ("calendar", nil) : ("doc.text", nil)
        }
    }

    private func row(_ p: ToolPlace, at k: Int) -> some View {
        Button { choose(p.course, p.item) } label: {
            HStack(alignment: .center, spacing: 11) {
                mark(p)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(title(p)).font(.sCallout.weight(.medium)).lineLimit(2)
                        if p.id == nextID { NextTag() }
                    }
                    if let sub = p.item.sub, !sub.isEmpty {
                        Text(sub).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                    }
                }
                Spacer(minLength: 8)
                if let s = p.item.status, !s.word.isEmpty { StatusChip(text: s.word, tone: s.kind) }
                Image(systemName: "chevron.right")
                    .font(.system(size: 10.5, weight: .bold))
                    .foregroundStyle(.tertiary)
                    .opacity(k == lit ? 1 : 0)
                    .accessibilityHidden(true)
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 7)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(k == lit ? Theme.accent.opacity(0.14) : Color.clear, in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(PlaceRowStyle())
        .onHover { inside in if inside { lit = k } }
    }

    /// (1.3.202) The keys it takes, at its foot.
    private var foot: some View {
        HStack(spacing: 14) {
            keyHint("return", "Open")
            keyHint("arrow.up.arrow.down", "Choose")
            keyHint("escape", "Close")
            Spacer(minLength: 0)
        }
        .font(.sCaption2)
        .foregroundStyle(.secondary)
        .padding(.horizontal, 14)
        .frame(height: 30)
        .background(Color.primary.opacity(0.03))
        .accessibilityHidden(true)
    }

    private func keyHint(_ symbol: String, _ word: String) -> some View {
        HStack(spacing: 4) {
            Image(systemName: symbol).font(.system(size: 9.5, weight: .semibold))
            Text(word)
        }
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
            .background(Theme.accent.opacity(configuration.isPressed ? 0.12 : 0), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            .scaleEffect(configuration.isPressed ? 0.99 : 1)
            .animation(.easeOut(duration: 0.1), value: configuration.isPressed)
    }
}

