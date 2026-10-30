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
    /// (1.3.77) The tool whose list of places is open, if one is. (1.3.204: in a popup over the page, not a popover)
    @State private var listed: String?
    /// (1.3.178) The answer the cards were drawn from as a tool's list opened, held while it is open; a newer one (the
    /// school's after the kept one, a live check) put in once it closes. A card drawn again under its open list — moved
    /// to another column by the newer answer — had its popover presented afresh in the middle of laying the window out,
    /// and AppKit stopped the app (more passes laying it out than it has views; 1.3.175's dark-36b picture). (1.3.204:
    /// kept for the popup, so the list does not change under the pointer.)
    @State private var held: ExternalToolsData?
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
                                ToolCard(tool: t, listed: listedBinding(t), openTab: { c in open(t, in: c) })
                            }
                            .animation(reduceMotion ? nil : Motion.gentle, value: shown.map(\.id))
                        }
                    }
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
                    ToolPlacesList(tool: t, tint: Color(hex: t.courses.first?.color ?? "#8e8e93"),
                                   room: max(300, min(720, g.size.height - 56)),
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
/// many there are; the press opens the list (1.3.204: in a popup over the page). Its chips for a course's own tab (when it
/// is one too) stay buttons of their own, laid over the card's — never a button inside a button.
private struct ToolCard: View {
    let tool: ExternalToolsData.Tool
    @Binding var listed: Bool
    let openTab: (ExternalToolsData.Course) -> Void
    @State private var hover = false
    @State private var pressed = false
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
        .modifier(ListCardSurface(lit: hover || listed))
        .scaleEffect(pressed && !reduceMotion ? 0.98 : 1)
        .animation(.easeOut(duration: 0.1), value: pressed)
        .animation(Motion.hover, value: hover || listed)
        .onHover { hover = $0 }
    }

    /// The card's own button, the whole card under what it says: it tells the card when it is held, for the card to give.
    private var revealButton: some View {
        let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
        let label: String = "\(tool.name), \(tool.placesPhrase)"
        var next = ""
        if let n = tool.nextShown { next = "Next: \(n.title), \(n.line)" }
        return Button {
            listed = true
        } label: {
            shape.fill(Color.clear).contentShape(shape)
        }
        .buttonStyle(PressReport(pressed: $pressed))
        .help(tool.chooseHelp)
        .accessibilityLabel(label)
        .accessibilityValue(next)
        .accessibilityHint(tool.chooseHelp)
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

    /// (1.3.77) The card's foot: how many places it has, and a chevron — there is more inside. (1.3.204: to the right,
    /// the list opening in a popup of its own, no longer under the card)
    private var footer: some View {
        VStack(spacing: 10) {
            Divider()
            HStack(spacing: 6) {
                Text(tool.placesPhrase).font(.sCallout).foregroundStyle(.secondary)
                Spacer(minLength: 6)
                Image(systemName: "chevron.right")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.tertiary)
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

/// (1.3.77) Every place a tool is used, to choose the one to open — (1.3.204) in a popup over the page, centred, 600 points
/// wide (a popover from its card before, cramped by the room under it). One list of them all, in their order — under each
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
    /// The most the whole popup may be tall: 720 points, or the window's height less a margin.
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
    @State private var listHeight: CGFloat
    /// (1.3.82) The list's height as last measured, in whole points: taken up as `listHeight` a turn of the run loop
    /// later, never while the popover is being laid out.
    @State private var measured: CGFloat = 0
    @FocusState private var focus: Focus?

    private enum Focus: Hashable { case filter, list }

    init(tool: ExternalToolsData.Tool, tint: Color, room: CGFloat = 720, choose: @escaping (ExternalToolsData.Course, ExternalToolsData.Item) -> Void, close: @escaping () -> Void) {
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
        // (a first guess at its height, until it is measured: a place a row, a course a heading)
        let courses = Set(all.map(\.course.id)).count
        _listHeight = State(initialValue: CGFloat(all.count) * 50 + (courses > 1 ? CGFloat(courses) * 30 : 0) + 16)
    }

    private var courseCount: Int { Set(all.map(\.course.id)).count }
    private var grouped: Bool { courseCount > 1 }
    private var hasFilter: Bool { all.count > 8 }
    private var trimmed: String { query.trimmingCharacters(in: .whitespaces) }
    /// "knewton.com · 5 assignments in 2 courses".
    private var summary: String {
        let places = "\(tool.placesPhrase) in \(courseCount == 1 ? "1 course" : "\(courseCount) courses")"
        return tool.showsHost ? "\(tool.host) · \(places)" : places
    }
    /// The list's own height at most: the whole popup no taller than its room (its head, its filter and its foot take
    /// the rest).
    private var listMax: CGFloat {
        let head: CGFloat = 64 + (hasFilter ? 50 : 0) + 30
        return max(160, room - head)
    }

    /// The list's height: as tall as it is, up to its most.
    private var listFrame: CGFloat { min(max(listHeight, 40), listMax) }

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
            Divider()
            foot
        }
        .frame(width: 600)
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
            Text(String(tool.name.prefix(1)).uppercased())
                .font(.system(size: 17, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 38, height: 38)
                .background(tint.gradient, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
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

    /// (1.3.204) The popup's ×, as a floating panel's.
    private var closeButton: some View {
        Button(action: close) {
            Image(systemName: "xmark")
                .font(.system(size: 10, weight: .bold))
                .foregroundStyle(.secondary)
                .frame(width: 22, height: 22)
                .background(Circle().fill(Color.primary.opacity(0.08)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help("Close (Esc)")
        .accessibilityLabel("Close")
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
                .background {
                    GeometryReader { g in Color.clear.preference(key: PlacesListHeightKey.self, value: g.size.height) }
                }
            }
            .scrollBounceBehavior(.basedOnSize)
            // (1.3.82: as tall as the list, up to its most)
            .frame(minHeight: min(160, listFrame), idealHeight: listFrame, maxHeight: listFrame)
            .onPreferenceChange(PlacesListHeightKey.self) { h in
                // (measured whole, unfiltered: a filter narrowing the list leaves the popup its size)
                let whole = h.rounded(.up)
                if query.isEmpty, abs(whole - measured) > 0.5 { measured = whole }
            }
            .task(id: measured) {
                // (1.3.82: the measure taken up on a later turn of the run loop, and only when it is another by half a point)
                if measured > 0, abs(measured - listHeight) > 0.5 { listHeight = measured }
            }
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
