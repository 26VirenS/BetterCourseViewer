import SwiftUI

/// The Dashboard: the day at a glance, as Simpl's web Dashboard has it (1.2). Six counters, each growing in place into a
/// panel of what it counted; then one of three views, chosen in the toolbar and kept on this Mac — Cards (the courses as
/// cards: score, what is due, unread announcements, how much is handed in, quick links), List (every course on one
/// line, then everything coming up day by day, ticked off where it is done) and Activity (Canvas's recent activity).
/// Beside the view on a wide window, and under it on a narrow one: the day's work, the grades skyline and the week's
/// load per course.
struct DashboardView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<Today>()
    /// The view chosen (Cards, List, Activity), kept on this Mac. (The screenshot suite: -SimplDashView list.)
    @AppStorage("SimplDashView") private var chosen: DashView = .list // (1.2.1: List unless another is picked)
    @State private var counts: TodayCounts?
    @State private var open: String?
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
    @Namespace private var morph
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Brightspace has no activity stream: there the Activity view is not offered.
    private var views: [DashView] { engine.onBrightspace ? [.cards, .list] : DashView.allCases }
    private var view: DashView { views.contains(chosen) ? chosen : .list }

    // MARK: - Widths

    /// The side column's width on a wide window.
    private var railWidth: CGFloat { min(440, max(340, (width * 0.3).rounded())) }
    /// Wide enough for the view and the side column side by side.
    private var twoColumns: Bool { width >= 900 }
    private var mainWidth: CGFloat { twoColumns ? width - railWidth - 28 : width }

    var body: some View {
        Group {
            if let d = model.data {
                page(d)
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Dashboard")
        .toolbar { toolbarItems }
        .task(id: engine.dataVersion) { await load() }
        .onChange(of: chosen) { _, _ in Task { await loadView() } }
    }

    @ToolbarContentBuilder
    private var toolbarItems: some ToolbarContent {
        ToolbarItem(placement: .primaryAction) {
            Picker("View", selection: $chosen.animation(Motion.gentle)) {
                ForEach(views) { v in
                    Text(v.title).tag(v)
                }
            }
            .pickerStyle(.segmented)
            .fixedSize()
            .help("Cards, a list by day, or recent activity")
        }
        ToolbarItem(placement: .primaryAction) {
            Button { engine.newTask = true } label: { Label("New Task", systemImage: "plus") }
                .help("New Task (⌘N)")
        }
    }

    // MARK: - The page

    private func page(_ d: Today) -> some View {
        Page(spacing: 26) {
            ScreenHeading(title: greeting(d), sub: d.dateLine)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background {
                    GeometryReader { g in
                        Color.clear
                            .onAppear { width = g.size.width }
                            .onChange(of: g.size.width) { _, w in width = w }
                    }
                }
            // (laid out once the page's width is known: the counters' rows and the columns follow it)
            if width > 0 {
                VStack(alignment: .leading, spacing: 26) {
                    counters(d)
                    content(d)
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
                // (1.2.2) a click anywhere round an opened counter's panel folds it back (the panel keeps its own)
                .contentShape(Rectangle())
                .onTapGesture { if open != nil { setOpen(nil) } }
            }
        }
    }

    private func greeting(_ d: Today) -> String {
        let first = d.me?.name.split(separator: " ").first.map(String.init) ?? ""
        let hour = Calendar.current.component(.hour, from: Date())
        let part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening"
        return first.isEmpty ? part : "\(part), \(first)"
    }

    // MARK: - Counters

    /// The counters in rows (six across where they fit, else three or two); a counter opened grows into its panel right
    /// under its own row.
    private func counters(_ d: Today) -> some View {
        let cols = width >= 900 ? 6 : (width >= 520 ? 3 : 2)
        let list = DashboardView.ordered(d.counters)
        let rows: [[Counter]] = stride(from: 0, to: list.count, by: cols).map { start in
            Array(list[start..<min(start + cols, list.count)])
        }
        return VStack(alignment: .leading, spacing: 14) {
            ForEach(Array(rows.enumerated()), id: \.offset) { _, row in
                counterRow(row, cols: cols)
                if let key = open, let c = row.first(where: { $0.key == key }) {
                    DashCounterPanel(counter: c, value: value(c), note: note(c), width: width, close: { setOpen(nil) })
                        .tourSpot(.counterPanel)
                        .id(key)
                        .dashMorph(key, in: morph, enabled: !reduceMotion)
                        .transition(.opacity)
                        .zIndex(1)
                }
            }
        }
    }

    private func counterRow(_ row: [Counter], cols: Int) -> some View {
        HStack(alignment: .top, spacing: 14) {
            ForEach(row) { c in
                counterSlot(c).frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            ForEach(0..<max(cols - row.count, 0), id: \.self) { _ in
                Color.clear.frame(maxWidth: .infinity, maxHeight: 1)
            }
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    /// A counter's place in its row: the tile, or — while it has grown into its panel — the same tile unseen, so the row
    /// keeps its shape. (Under Reduce Motion nothing grows: the tile stays, and the panel fades in under its row.)
    @ViewBuilder
    private func counterSlot(_ c: Counter) -> some View {
        if open == c.key && !reduceMotion {
            DashCounterTile(counter: c, value: value(c), note: note(c)) {}
                .hidden()
                .accessibilityHidden(true)
        } else {
            DashCounterTile(counter: c, value: value(c), note: note(c)) { setOpen(open == c.key ? nil : c.key) }
                .tourSpot(c.key == "next" ? .counterNext : nil)
                .dashMorph(c.key, in: morph, enabled: !reduceMotion)
                .transition(.opacity)
        }
    }

    /// A counter's number: never one from before (1.2) — while the page shows what was kept from last time, or the
    /// overdue and graded counts are still being made, the tile says it is counting.
    /// (1.2.2) The counters in the day's order: today, tomorrow, the next seven days, overdue, unread, graded.
    static func ordered(_ list: [Counter]) -> [Counter] {
        let order = ["today", "tomorrow", "next", "overdue", "unread", "graded"]
        let rank = { (k: String) in order.firstIndex(of: k) ?? order.count }
        return list.enumerated().sorted { a, b in
            rank(a.element.key) != rank(b.element.key) ? rank(a.element.key) < rank(b.element.key) : a.offset < b.offset
        }.map(\.element)
    }

    private func value(_ c: Counter) -> Int? {
        if model.kept { return nil }
        switch c.key {
        case "overdue": return counts?.overdue
        case "graded": return counts?.graded
        default: return c.value
        }
    }

    private func note(_ c: Counter) -> String? {
        if model.kept { return nil }
        switch c.key {
        case "overdue": return counts?.overdueNote
        case "graded": return counts?.gradedNote
        default: return c.note
        }
    }

    private func setOpen(_ key: String?) {
        withAnimation(reduceMotion ? .easeInOut(duration: 0.18) : Motion.gentle) { open = key }
        if key != nil { MacTour.shared.did(.counterOpened) } // (the tour's "The cards open")
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
            VStack(alignment: .leading, spacing: 26) {
                if view != .list { today(d) }
                main(d)
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
            courseLines
            upcoming
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
                    if i > 0 { RowDivider(inset: 42) }
                    Group {
                        if twoColumns {
                            DashTodayLine(row: row) { done in toggle(row, done) }
                        } else {
                            // (1.2.3) a press grows its preview out of the row; a double-click opens it
                            PreviewLink(item: .work(row, engine: engine, toggle: { done in toggle(row, done) })) {
                                WorkRowView(row: row) { done in toggle(row, done) }
                            }
                            .workMenu(WorkAction(row), engine: engine)
                        }
                    }
                    .transition(.opacity.combined(with: .move(edge: .top)))
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

    private var courseLines: some View {
        PageSection(title: "Courses", trailing: courses.map { "\($0.rows.count) \($0.rows.count == 1 ? "course" : "courses")" }) {
            if let c = courses {
                if c.rows.isEmpty {
                    EmptyNote(text: c.empty ?? "No courses chosen yet.", symbol: "books.vertical")
                } else {
                    VStack(spacing: 0) {
                        ForEach(Array(c.rows.enumerated()), id: \.element.id) { i, row in
                            if i > 0 { RowDivider(inset: 27) }
                            DashCourseLine(course: row, progress: progressOf(row.id), wide: mainWidth >= 860)
                        }
                    }
                    .padding(8)
                    .card(radius: 18)
                }
            } else {
                loadingNote("Reading your courses…")
            }
        }
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
            PageSection(title: "Week load", trailing: "Handed in of assigned") {
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
        // (the screenshot suite: -SimplSheet next opens a counter's panel)
        if let key = UserDefaults.standard.string(forKey: "SimplSheet"), !key.isEmpty, !sheetAsked {
            sheetAsked = true
            open = key
        }
    }

    /// The cards, the List, the activity and the skyline as they were last time (AnswerCache), until the live answers
    /// are in. Their counts wait for the live answer, as every count does: a card's due today and unread, a new dot.
    private func showKept() {
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) {
            if courses == nil, var c = engine.kept("dashCourses", as: DashCoursesData.self) {
                for i in c.rows.indices {
                    c.rows[i].unread = nil
                    c.rows[i].dueToday = nil
                }
                courses = c
            }
            if sky == nil, let s = engine.kept("dashSkyline", ["kept": false], as: DashSkylineData.self) { sky = s }
            if list == nil, let l = engine.kept("dashList", as: DashListData.self) { list = l }
            if activity == nil, var a = engine.kept("dashActivity", as: DashActivityData.self) {
                for i in a.rows.indices { a.rows[i].unread = nil }
                activity = a
            }
        }
    }

    private func loadCounts() async {
        // (1.2: only the live counts — the copy kept from the last visit is not shown, a count from before misleads)
        if let fresh = try? await engine.call("todayCounts", ["kept": false], as: TodayCounts.self) {
            withAnimation(Motion.snappy) { counts = fresh }
        }
    }

    private func loadCourses() async {
        if let c = try? await engine.call("dashCourses", as: DashCoursesData.self) {
            withAnimation(courses == nil ? nil : Motion.gentle) { courses = c }
        } else if courses == nil {
            courses = DashCoursesData(rows: [], empty: "Your courses could not be loaded.")
        }
        if let p = try? await engine.call("coursesProgress", as: [String: CourseProgress].self) {
            withAnimation(Motion.gentle) {
                progress = p
                progressRead = true
            }
        }
    }

    /// The skyline from the copy kept from the last visit first (at once), then from what Canvas says now.
    private func loadSkyline() async {
        if sky == nil, let kept = try? await engine.call("dashSkyline", ["kept": true], as: DashSkylineData.self), sky == nil {
            sky = kept
        }
        if let fresh = try? await engine.call("dashSkyline", ["kept": false], as: DashSkylineData.self) {
            withAnimation(Motion.gentle) { sky = fresh }
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
            let l = try await engine.call("dashList", args, as: DashListData.self)
            withAnimation(animated || hideDone != nil ? Motion.gentle : nil) {
                list = l
                listError = nil
            }
        } catch {
            if list == nil { listError = error.localizedDescription }
        }
    }

    private func loadActivity() async {
        do {
            let a = try await engine.call("dashActivity", as: DashActivityData.self)
            activity = a
            activityError = nil
        } catch {
            if activity == nil { activityError = error.localizedDescription }
        }
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
