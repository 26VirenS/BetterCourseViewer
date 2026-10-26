import AppKit
import SwiftUI

/// (1.3.170) To Do, beside the calendar in To Do & Calendar — the web's 2.99.112 (content/app/screens/todo.js, its panel
/// mode). To Do's own screen is gone: its list lives here, beside the month, the week and the agenda alike. At its top a
/// small summary (the ring, the most pressing line, how much is done), then Add a Task — the composer opening in place:
/// what it is, its day, a course, a repeat and until when, its priority — then the groups: by date, or by priority or
/// by course, with what is done and dismissed or without it, from the ⋯ in its head. A row is ticked off where it is,
/// opened with a click (its preview grows out of it; a double-click opens it whole) and dragged onto a day or a time of
/// the calendar to plan it there; its own ⋯ (under the pointer) or a right-click has its priority, a time for it, Submit,
/// and Delete for a task of your own or Dismiss and Restore for Canvas's work; a flag says it has a priority. Drawn
/// again in place as anything changes (a plan made, moved or cleared on the calendar among them); the calendar says what
/// came of a row let go on it (`said`).
struct TodoPanel: View {
    /// What came of the last row let go on the calendar ("Planned Wednesday 4:00 PM").
    var said: String?
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<TodoData>()
    /// The grouping and Show Completed as just chosen, shown at once while the page regroups.
    @State private var pendingGroup: String?
    @State private var pendingShowDone: Bool?
    @State private var regrouping = 0
    /// The composer open in place of Add a Task (its day, when a day asked for it), and which asking it is.
    @State private var composing = false
    @State private var composeDay: Date?
    @State private var composeSerial = 0
    /// A repeating task of your own on its way out: which of the series goes is asked first.
    @State private var deleting: WorkRow?
    /// The ⋯ in the head, for its menu to come down from.
    @State private var menuAnchor = PreviewAnchor()
    /// (1.3.165) The screenshot suite's plan rail, opened once, and the work it is open on, to open it again as the app
    /// comes back in front.
    @State private var planShown = false
    @State private var planShownID: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Its width beside the calendar.
    static let width: CGFloat = 264
    /// What it reads (native-app.js `todo`, its panel: To Do's own grouping and Show Completed).
    static let args: [String: Any] = ["panel": true]

    /// The screenshot suite's ⋯ menu (-SimplTodoMenu YES), opened once To Do is in.
    private static let menuAsked = UserDefaults.standard.bool(forKey: "SimplTodoMenu")

    /// (1.3.78) Its answer read ahead, beside the calendar's.
    @MainActor static var readAhead: ReadAhead.Ask? { .read("todo", args, TodoData.self) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            head
            Divider()
            ScrollViewReader { proxy in
                ScrollView {
                    list
                        .padding(.horizontal, 8)
                        .padding(.top, 8)
                        .padding(.bottom, 12)
                }
                .scrollIndicators(.automatic)
                .onChange(of: composeSerial) { _, _ in
                    withAnimation(reduceMotion ? nil : Motion.gentle) { proxy.scrollTo("composer", anchor: .top) }
                }
            }
            if let said {
                Divider()
                Label(said, systemImage: "checkmark.circle.fill")
                    .font(.sCaption.weight(.medium))
                    .foregroundStyle(.green)
                    .lineLimit(2)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .transition(.opacity)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .card()
        .font(.sBody)
        .animation(reduceMotion ? nil : Motion.snappy, value: said)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("To Do")
        .task(id: engine.dataVersion) {
            await load()
            shots()
        }
        .liveChecks(model) // (checked every minute while it is on show: only what changed moves)
        .onChange(of: engine.taskAsked, initial: true) { _, asked in
            guard let asked else { return }
            engine.taskAsked = nil
            compose(on: asked.day)
        }
        // (the suite runs several copies at once: a popover or a menu closes as its app goes behind another, so it is opened
        //  again each time this one comes back in front — as it is for its picture)
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            if let id = planShownID { PlanAsk.shared.id = id }
            if Self.menuAsked && model.data != nil { openMenuSoon() }
        }
        .confirmationDialog(deleteQuestion, isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } }), titleVisibility: .visible) {
            if let row = deleting {
                Button("Delete This Task", role: .destructive) { delete(row, series: false) }
                Button("Delete All \((row.series ?? 0) + 1) Repeats", role: .destructive) { delete(row, series: true) }
            }
            Button("Cancel", role: .cancel) { deleting = nil }
        } message: {
            Text(engine.onBrightspace ? "This removes it from your To Do." : "This removes it from your Canvas planner.")
        }
    }

    private var deleteQuestion: String {
        guard let row = deleting else { return "Delete Task?" }
        return "Delete “\(row.title)”?"
    }

    // MARK: - The head

    /// "To Do", what is open and what a drag does, and the ⋯ of its grouping and Show Completed.
    private var head: some View {
        let open = model.data?.left ?? 0
        return HStack(alignment: .center, spacing: 8) {
            VStack(alignment: .leading, spacing: 1) {
                Text("To Do").font(.sHeadline)
                if model.data != nil {
                    Text(open > 0 ? "\(open) open · drag onto the calendar to plan" : "Nothing left to do")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(2)
                        .contentTransition(.numericText(value: Double(open)))
                }
            }
            Spacer(minLength: 4)
            Button {
                openMenu()
            } label: {
                Image(systemName: "ellipsis")
                    .font(.system(size: 13, weight: .bold))
                    .foregroundStyle(.secondary)
                    .frame(width: 26, height: 26)
                    .background(Circle().fill(Color.primary.opacity(0.06)))
                    .contentShape(Circle())
            }
            .buttonStyle(.plain)
            .background { PreviewProbe(anchor: menuAnchor).allowsHitTesting(false) }
            .disabled(model.data == nil)
            .help("Group and show")
            .accessibilityLabel("To Do options")
        }
        .padding(.leading, 14)
        .padding(.trailing, 10)
        .padding(.top, 12)
        .padding(.bottom, 10)
    }

    /// The ⋯'s menu, under it: the grouping (the one in force ticked), then Show Completed with how many — done and
    /// dismissed, this week — or Hide Completed.
    private func openMenu() {
        guard let d = model.data else { return }
        let group = pendingGroup ?? d.group
        let showing = pendingShowDone ?? d.showDone
        let finished = d.done + (d.dismissedN ?? 0)
        PopMenu.show([
            .choice("By Date", on: group == "date", act: { regroup(group: "date") }),
            .choice("By Priority", on: group == "priority", act: { regroup(group: "priority") }),
            .choice("By Course", on: group == "course", act: { regroup(group: "course") }),
            .line,
            .choice(showing ? "Hide Completed" : (finished > 0 ? "Show Completed · \(finished)" : "Show Completed"), act: { regroup(showDone: !showing) }),
        ], under: menuAnchor.view, trailing: true)
    }

    /// (the screenshot suite's -SimplTodoMenu YES) The ⋯'s menu opened from the run loop's timer, not the main queue: a
    /// menu holds what calls it until it closes, and the picture is taken from the main queue meanwhile.
    private func openMenuSoon() {
        guard !PopMenu.isOpen else { return }
        _ = Timer.scheduledTimer(withTimeInterval: 1.2, repeats: false) { _ in
            MainActor.assumeIsolated {
                guard !PopMenu.isOpen, NSApp.isActive else { return }
                openMenu()
            }
        }
    }

    // MARK: - The list

    @ViewBuilder
    private var list: some View {
        if let d = model.data {
            LazyVStack(alignment: .leading, spacing: 0, pinnedViews: [.sectionHeaders]) {
                summary(d)
                    .padding(.horizontal, 4)
                    .padding(.bottom, 8)
                composer(d)
                    .id("composer")
                    .padding(.horizontal, 4)
                    .padding(.bottom, 6)
                if d.sections.isEmpty {
                    Label((pendingShowDone ?? d.showDone) ? "Nothing this week." : "Nothing left to do this week.", systemImage: "checkmark.circle")
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 16)
                }
                ForEach(d.sections) { s in
                    Section {
                        ForEach(s.rows) { r in row(r) }
                    } header: {
                        header(s)
                    }
                }
            }
            .animation(reduceMotion ? nil : Motion.gentle, value: d.sections.map(\.rows))
        } else if model.error != nil {
            VStack(alignment: .leading, spacing: 8) {
                Text("To Do could not be read.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                Button("Try Again") { Task { await load() } }
                    .glassButton()
            }
            .padding(10)
        } else {
            ProgressView()
                .controlSize(.small)
                .frame(maxWidth: .infinity)
                .padding(.top, 24)
        }
    }

    /// (the web's compact summary) The ring of what is done, about 30 points across; the most pressing line beside it —
    /// overdue in red, else due today, else still to do, else all done — and "1 of 18 done" under it.
    private func summary(_ d: TodoData) -> some View {
        let line = PreviewFormat.text(d.sumText) ?? "\(d.left ?? max(0, d.total - d.done)) to do"
        return HStack(spacing: 10) {
            Ring(value: Double(d.pct), color: .green, lineWidth: 4, key: "todo-panel")
                .frame(width: 30, height: 30)
            VStack(alignment: .leading, spacing: 1) {
                Text(line)
                    .font(.system(size: 14, weight: .semibold))
                    .foregroundStyle(d.sumTone == "over" ? Color.red : Color.primary)
                    .contentTransition(.numericText())
                Text(PreviewFormat.text(d.doneText) ?? "\(d.done) of \(d.total) done")
                    .font(.system(size: 11.5))
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(d.done)))
            }
            .monospacedDigit()
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.045)))
        .accessibilityElement(children: .combine)
    }

    /// Add a Task, or the composer open in its place.
    @ViewBuilder
    private func composer(_ d: TodoData) -> some View {
        if composing {
            TaskComposer(courses: d.courses ?? [], repeats: d.repeats ?? [], day: composeDay) {
                withAnimation(reduceMotion ? nil : Motion.snappy) { composing = false }
            }
            .id(composeSerial) // (asked again for another day: a composer of its own)
            .transition(.opacity.combined(with: .scale(scale: 0.97, anchor: .top)))
        } else {
            Button {
                compose(on: nil)
            } label: {
                HStack(spacing: 8) {
                    Image(systemName: "plus")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(Theme.accent)
                        .frame(width: 22, height: 22)
                        .background(Circle().fill(Theme.accent.opacity(0.14)))
                    Text("Add a Task")
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(.primary)
                    Spacer(minLength: 0)
                }
                .padding(.horizontal, 8)
                .padding(.vertical, 6)
                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous).strokeBorder(Color.primary.opacity(0.14), style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
            }
            .buttonStyle(RowButtonStyle(radius: 10))
            .help("A task of your own: its day, a course, a repeat and its priority (⌘N)")
            .transition(.opacity)
        }
    }

    /// A group's name in small capitals — Overdue in red — with how many in it are still open, kept at the top as the
    /// list scrolls under it.
    private func header(_ s: TodoSection) -> some View {
        let open = s.rows.filter { !$0.done && $0.dismissed != true }.count
        return HStack(spacing: 6) {
            Text(s.title.uppercased())
                .font(.sCaption2.weight(.semibold))
                .tracking(0.5)
                .foregroundStyle(s.title == "Overdue" ? Color.red : Color.secondary)
            if open > 0 {
                Text("\(open)")
                    .font(.sCaption2.monospacedDigit())
                    .foregroundStyle(.tertiary)
            }
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 8)
        .padding(.top, 10)
        .padding(.bottom, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.card)
    }

    private func row(_ r: WorkRow) -> some View {
        // (a row dismissed has no Mark Done on its card: it is restored from its menu)
        let tick: ((Bool) -> Void)? = r.dismissed == true ? nil : { toggle(r, $0) }
        return TodoPanelRow(
            row: r,
            preview: .work(r, engine: engine, toggle: tick),
            toggle: { toggle(r, $0) },
            setPriority: { setPriority(r, $0) },
            delete: { askDelete(r) },
            dismiss: { dismiss(r, back: false) },
            restore: { dismiss(r, back: true) }
        )
        .transition(.opacity)
    }

    // MARK: - Reading and acting

    private func load(animated: Bool = false, group: String? = nil, showDone: Bool? = nil) async {
        var args = Self.args
        if let group { args["group"] = group }
        if let showDone { args["showDone"] = showDone }
        await model.load(engine, "todo", args, animated: animated)
    }

    /// (the screenshot suite) -SimplPlanFirst YES: the plan rail open on the first piece of Canvas's work still open that
    /// is not late (in view, near the top, so the rail hangs on screen); -SimplTodoMenu YES: the ⋯'s menu open.
    private func shots() {
        guard let d = model.data else { return }
        if UserDefaults.standard.bool(forKey: "SimplPlanFirst"), !planShown,
           let r = d.sections.filter({ $0.title != "Overdue" }).flatMap(\.rows).first(where: { !$0.done && $0.custom != true && $0.dismissed != true }) {
            planShown = true
            planShownID = r.id
            Task {
                try? await Task.sleep(nanoseconds: 600_000_000)
                PlanAsk.shared.id = r.id
            }
        }
        if Self.menuAsked { openMenuSoon() }
    }

    /// The composer opened in place of Add a Task, on `day` (today when none is given), and scrolled to.
    private func compose(on day: Date?) {
        composeDay = day
        composeSerial += 1
        withAnimation(reduceMotion ? nil : Motion.snappy) { composing = true }
    }

    private func regroup(group: String? = nil, showDone: Bool? = nil) {
        if let group { pendingGroup = group }
        if let showDone { pendingShowDone = showDone }
        regrouping += 1
        let ticket = regrouping
        Task {
            await load(animated: true, group: group, showDone: showDone)
            guard ticket == regrouping else { return } // (a later choice is on its way: it clears these)
            pendingGroup = nil
            pendingShowDone = nil
        }
    }

    private func toggle(_ row: WorkRow, _ done: Bool) {
        mutate(row.id) { $0.done = done }
        Task {
            if await engine.act("complete", ["id": row.id, "done": done]) {
                try? await Task.sleep(nanoseconds: 450_000_000) // (the tick shows before the row moves)
                await load(animated: true)
                engine.changed()
            } else {
                mutate(row.id) { $0.done = !done }
            }
        }
    }

    private func setPriority(_ row: WorkRow, _ level: Int) {
        mutate(row.id) {
            $0.pri = level
            $0.priShort = TodoPriority.of(level).short
        }
        Task {
            await engine.act("setPriority", ["id": row.id, "level": level])
            await load(animated: true)
        }
    }

    /// Off To Do and not done (Canvas's planner keeps it so), or (`back`) on it again.
    private func dismiss(_ row: WorkRow, back: Bool) {
        Task {
            if await engine.act(back ? "todoRestore" : "todoDismiss", ["id": row.id]) {
                await load(animated: true)
                engine.changed()
            }
        }
    }

    /// A task of your own: gone at once, or — one of a series — once it is said which goes (asked after the menu that
    /// asked for it has closed: (the web's 2.99.112) asked inside the same press, the question closed with it).
    private func askDelete(_ row: WorkRow) {
        guard (row.series ?? 0) > 0 else { return delete(row, series: false) }
        DispatchQueue.main.async { deleting = row }
    }

    private func delete(_ row: WorkRow, series: Bool) {
        deleting = nil
        Task {
            if await engine.act("deleteTask", ["id": row.id, "series": series]) {
                await load(animated: true)
                engine.changed()
            }
        }
    }

    /// A row changed before the page has said so (a tick, a priority).
    private func mutate(_ id: String, _ change: (inout WorkRow) -> Void) {
        guard var d = model.data else { return }
        for s in d.sections.indices {
            guard let i = d.sections[s].rows.firstIndex(where: { $0.id == id }) else { continue }
            change(&d.sections[s].rows[i])
            break
        }
        withAnimation(Motion.snappy) { model.data = d }
    }
}

// MARK: - A row

/// A row beside the calendar: its tick, what it is and when it is due (in red once late) — planned: when — its priority's
/// flag when it has one, and its ⋯ under the pointer, the right-click's menu: its priority, a time for it, Submit, and
/// Delete or Dismiss and Restore. Open work is dragged onto the calendar to plan it; its rail (Plan a Time…) hangs under
/// it.
private struct TodoPanelRow: View {
    let row: WorkRow
    let preview: PreviewItem
    let toggle: (Bool) -> Void
    let setPriority: (Int) -> Void
    let delete: () -> Void
    let dismiss: () -> Void
    let restore: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var hover = false

    /// Done or dismissed: struck through, and no longer dragged or planned.
    private var finished: Bool { row.done || row.dismissed == true }
    private var level: Int { row.pri ?? 0 }

    var body: some View {
        HStack(alignment: .top, spacing: 7) {
            tick
            PreviewLink(item: preview, padded: false, radius: 8, inline: false) { words }
            if level > 0 {
                Image(systemName: "flag.fill")
                    .font(.system(size: 10.5, weight: .semibold))
                    .foregroundStyle(TodoPriority.of(level).color)
                    .padding(.top, 4)
                    .help("\(TodoPriority.of(level).label) priority")
                    .accessibilityHidden(true)
            }
            Menu { menu } label: {
                Image(systemName: "ellipsis")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(.secondary)
                    .frame(width: 22, height: 22)
                    .contentShape(Rectangle())
            }
            .menuStyle(.button)
            .buttonStyle(.plain)
            .menuIndicator(.hidden)
            .fixedSize()
            .opacity(hover ? 1 : 0)
            .animation(Motion.hover, value: hover)
            .help("More")
            .accessibilityLabel("More for \(row.title)")
        }
        .padding(.leading, 6)
        .padding(.trailing, 2)
        .padding(.vertical, 5)
        .contentShape(Rectangle())
        .onHover { hover = $0 }
        .modifier(TodoRowDrag(row: row, enabled: !finished))
        .pullToPlan(id: row.id, title: row.title, enabled: false) // (its rail, asked for by its menu: never pulled here)
        .contextMenu { menu }
        .accessibilityElement(children: .contain)
    }

    private var tick: some View {
        Button {
            toggle(!row.done)
        } label: {
            Image(systemName: row.done ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 15))
                .foregroundStyle(row.done ? Theme.accent : Color.secondary)
                .frame(width: 18, height: 18)
                .contentShape(Circle())
                .contentTransition(.symbolEffect(.replace))
        }
        .buttonStyle(.plain)
        .padding(.top, 1)
        .disabled(row.dismissed == true)
        .help(row.done ? "Mark as Not Done" : "Mark as Done")
        .accessibilityLabel(row.done ? "Mark not done: \(row.title)" : "Mark done: \(row.title)")
    }

    /// Its course, its repeat, when it is due: "BIO 101 · Due Fri 11:59 PM".
    private var meta: String {
        let parts = row.meta?.components(separatedBy: " · ") ?? []
        let repeats = row.custom == true ? parts.first(where: { $0.hasPrefix("repeats") }) : nil
        return [row.courseName, repeats, row.dueText].compactMap { PreviewFormat.text($0) }.joined(separator: " · ")
    }

    private var words: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(row.title)
                .font(.sCallout.weight(.medium))
                .strikethrough(finished, color: .secondary)
                .foregroundStyle(finished ? Color.secondary : Color.primary)
                .lineLimit(2)
                .multilineTextAlignment(.leading)
            HStack(spacing: 5) {
                Circle().fill(Color(hex: row.color)).frame(width: 6, height: 6)
                Text(meta)
                    .font(.sCaption)
                    .foregroundStyle(row.late == true ? Color.red : Color.secondary)
                    .lineLimit(1)
            }
            if let p = row.plan, !finished {
                Label(p.words, systemImage: "clock")
                    .font(.sCaption2.weight(.semibold))
                    .foregroundStyle(p.past == true ? Color.secondary : Theme.accent)
                    .lineLimit(1)
                    .padding(.top, 1)
            }
        }
        .padding(.vertical, 1)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// A plain assignment still to hand in (Submit opens it at its hand-in).
    private var submittable: Bool {
        guard !finished, row.custom != true, row.type == "assignment", let url = row.url else { return false }
        return url.range(of: #"/assignments/\d+$"#, options: .regularExpression) != nil
    }

    /// The row's own menu, its ⋯'s and its right-click's alike.
    @ViewBuilder
    private var menu: some View {
        ForEach(TodoPriority.all) { p in
            Toggle(p.id > 0 ? "\(p.label) Priority" : "No Priority", isOn: Binding(get: { level == p.id }, set: { if $0 { setPriority(p.id) } }))
        }
        planMenu(id: row.id, planned: row.plan != nil, done: finished, engine: engine)
        if submittable, let url = row.url {
            Divider()
            Button("Submit") { engine.act(on: url, title: row.title, feedback: false) }
        }
        Divider()
        if row.custom == true {
            Button((row.series ?? 0) > 0 ? "Delete…" : "Delete Task", role: .destructive) { delete() }
        } else if row.dismissed == true {
            Button("Restore") { restore() }
        } else {
            Button("Dismiss") { dismiss() }
                .help("Off To Do, not done")
        }
        if row.custom != true, let url = PreviewFormat.text(row.url) {
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(url, title: row.title) }
            Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
        }
    }
}

/// (1.3.169) A row of open work dragged onto the calendar: its id said as the drag starts, its name on a small card
/// following the pointer.
private struct TodoRowDrag: ViewModifier {
    let row: WorkRow
    let enabled: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if enabled {
            content
                .onDrag {
                    TodoDragging.id = row.id
                    return NSItemProvider(object: "simpl-work:\(row.id)" as NSString)
                } preview: {
                    HStack(spacing: 8) {
                        RoundedRectangle(cornerRadius: 2, style: .continuous)
                            .fill(Color(hex: row.color))
                            .frame(width: 3)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(row.title).font(.sCallout.weight(.semibold)).lineLimit(1)
                            Text("Drop on a day or a time to plan it").font(.sCaption2).foregroundStyle(.secondary)
                        }
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 7)
                    .frame(width: 230, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Theme.card))
                }
        } else {
            content
        }
    }
}

// MARK: - Adding a task

/// A task of your own (a Canvas planner note), composed where Add a Task was: what it is, its day, a course, a repeat
/// and until when, and its priority, in chips that wrap to the panel's width. Return adds it, Escape puts it away.
/// (1.3.79's popup, and To Do's own screen's, before 1.3.170.)
private struct TaskComposer: View {
    let courses: [NamedColor]
    let repeats: [RepeatChoice]
    let close: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var title = ""
    @State private var date: Date
    @State private var course = ""
    @State private var repeatKey = ""
    @State private var until: Date
    @State private var priority = 2
    @State private var busy = false
    @State private var failure: String?
    @FocusState private var focused: Bool

    init(courses: [NamedColor], repeats: [RepeatChoice], day: Date?, close: @escaping () -> Void) {
        self.courses = courses
        self.repeats = repeats
        self.close = close
        let cal = Calendar.current
        let start = cal.startOfDay(for: day ?? Date())
        _date = State(initialValue: start)
        _until = State(initialValue: cal.date(byAdding: .day, value: 56, to: start) ?? start)
    }

    private var trimmed: String { title.trimmingCharacters(in: .whitespacesAndNewlines) }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("Task", text: $title, prompt: Text("What do you need to do?"))
                .textFieldStyle(.plain)
                .labelsHidden()
                .font(.sCallout.weight(.medium))
                .focused($focused)
                .onSubmit(add)
                .onExitCommand(perform: close)
            PillFlow(spacing: 6, lineSpacing: 6) {
                DatePicker("Day", selection: $date, displayedComponents: .date)
                    .labelsHidden()
                    .datePickerStyle(.compact)
                    .fixedSize()
                    .help("It’s due by the end of that day")
                courseChip
                repeatChip
                if !repeatKey.isEmpty {
                    HStack(spacing: 4) {
                        Text("until")
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                        DatePicker("Until", selection: $until, in: date..., displayedComponents: .date)
                            .labelsHidden()
                            .datePickerStyle(.compact)
                            .fixedSize()
                    }
                }
                priorityChip
            }
            if let failure {
                Label(failure, systemImage: "exclamationmark.triangle.fill")
                    .font(.sCaption)
                    .foregroundStyle(.red)
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(spacing: 8) {
                if busy { ProgressView().controlSize(.small) }
                Spacer(minLength: 0)
                Button("Cancel", action: close)
                    .glassButton()
                Button("Add Task", action: add)
                    .glassButton(prominent: true)
                    .disabled(trimmed.isEmpty || busy)
            }
            .controlSize(.small)
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.045)))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Theme.accent.opacity(0.35), lineWidth: 1))
        .animation(Motion.snappy, value: repeatKey.isEmpty) // (the until slides in and out)
        .onAppear { DispatchQueue.main.async { focused = true } }
        .onChange(of: date) { _, d in
            // (a repeat ends after it starts)
            if until < d { until = Calendar.current.date(byAdding: .day, value: 56, to: d) ?? d }
        }
    }

    /// A chip that is a menu: its symbol, its words, a small chevron; washed in its colour when it has one.
    private func chip(_ text: String, symbol: String, color: Color?) -> some View {
        HStack(spacing: 4) {
            Image(systemName: symbol).font(.system(size: 9.5, weight: .semibold))
            Text(text).lineLimit(1)
            Image(systemName: "chevron.down").font(.system(size: 7.5, weight: .bold)).opacity(0.6)
        }
        .font(.sCaption.weight(.medium))
        .foregroundStyle(color ?? Color.secondary)
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .frame(maxWidth: 170, alignment: .leading)
        .background(Capsule().fill((color ?? Color.primary).opacity(color == nil ? 0.07 : 0.14)))
        .contentShape(Capsule())
    }

    private var courseChip: some View {
        let c = courses.first { $0.id == course }
        return Menu {
            Button("No Course") { course = "" }
            if !courses.isEmpty { Divider() }
            ForEach(courses) { c in Button(c.name) { course = c.id } }
        } label: {
            chip(c?.name ?? "No Course", symbol: c == nil ? "book" : "circle.fill", color: c.map { Color(hex: $0.color) })
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Course")
    }

    private var repeatChip: some View {
        let r = repeats.first { $0.key == repeatKey }
        return Menu {
            Button("Doesn’t Repeat") { repeatKey = "" }
            if !repeats.isEmpty { Divider() }
            ForEach(repeats) { r in Button(r.label) { repeatKey = r.key } }
        } label: {
            chip(r?.label ?? "Doesn’t Repeat", symbol: "repeat", color: r == nil ? nil : Theme.accent)
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Repeat")
    }

    private var priorityChip: some View {
        let p = TodoPriority.of(priority)
        return Menu {
            todoPriorityChoices(level: priority) { priority = $0 }
        } label: {
            chip(priority > 0 ? p.label : "No Priority", symbol: "flag.fill", color: priority > 0 ? p.color : nil)
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .fixedSize()
        .help("Priority is yours alone and never reaches \(engine.lmsName)")
    }

    private func add() {
        let t = trimmed
        guard !t.isEmpty, !busy else { return }
        busy = true
        failure = nil
        let iso = ISO8601DateFormatter()
        var args: [String: Any] = ["title": t, "date": iso.string(from: date), "priority": priority]
        if !course.isEmpty { args["courseId"] = course }
        if !repeatKey.isEmpty {
            args["repeat"] = repeatKey
            args["until"] = iso.string(from: until)
        }
        Task {
            do {
                _ = try await engine.call("addTask", args, as: OK.self)
                close()
                engine.changed()
            } catch {
                failure = error.localizedDescription
                busy = false
            }
        }
    }
}

// MARK: - Priority

/// The four levels as checked menu items (a priority is yours alone; it never reaches Canvas).
@MainActor @ViewBuilder
private func todoPriorityChoices(level: Int, set: @escaping (Int) -> Void) -> some View {
    ForEach(TodoPriority.all) { p in
        Toggle(p.label, isOn: Binding(get: { level == p.id }, set: { if $0 { set(p.id) } }))
    }
}

/// A level of priority: its word, its short word on a row, its colour.
private struct TodoPriority: Identifiable {
    let id: Int
    let label: String
    let short: String
    let color: Color

    static let all = [
        TodoPriority(id: 3, label: "High", short: "High", color: .red),
        TodoPriority(id: 2, label: "Medium", short: "Med", color: .orange),
        TodoPriority(id: 1, label: "Low", short: "Low", color: .teal),
        TodoPriority(id: 0, label: "None", short: "", color: .gray),
    ]

    static func of(_ level: Int) -> TodoPriority {
        all.first { $0.id == level } ?? all[all.count - 1]
    }
}

// MARK: - A menu under a button

/// (1.3.170) AppKit's own menu, down from a view as a pull-down's comes (To Do's ⋯, the week's five days or seven): each
/// choice a closure, the one in force ticked, a line between groups. Without the view (gone from its window), under the
/// pointer.
@MainActor
enum PopMenu {
    enum Entry {
        case choice(String, on: Bool = false, act: () -> Void)
        case line
    }

    /// A menu of these is up (it holds what opened it until it closes).
    private(set) static var isOpen = false

    private final class Item: NSMenuItem {
        let act: () -> Void
        init(_ title: String, _ act: @escaping () -> Void) {
            self.act = act
            super.init(title: title, action: #selector(run), keyEquivalent: "")
            target = self
        }
        required init(coder: NSCoder) { fatalError("not from a nib") }
        @objc private func run() { act() }
    }

    /// The menu under `view` (its right edges lined up, `trailing`), or under the pointer.
    static func show(_ entries: [Entry], under view: NSView?, trailing: Bool = false) {
        guard !isOpen else { return }
        let menu = NSMenu()
        menu.autoenablesItems = false
        for e in entries {
            switch e {
            case .line:
                menu.addItem(.separator())
            case .choice(let title, let on, let act):
                let item = Item(title, act)
                item.state = on ? .on : .off
                menu.addItem(item)
            }
        }
        isOpen = true
        defer { isOpen = false }
        guard let view, view.window != nil else {
            _ = menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
            return
        }
        let x = trailing ? view.bounds.maxX - menu.size.width : view.bounds.minX
        let y = view.isFlipped ? view.bounds.maxY + 4 : view.bounds.minY - 4
        _ = menu.popUp(positioning: nil, at: NSPoint(x: x, y: y), in: view)
    }
}

/// (1.3.170) Whether To Do is shown beside the calendar (To Do & Calendar's To Do button), kept from one launch to the
/// next.
enum TodoShown {
    static let key = "SimplCalTodo"

    /// To Do shown beside the calendar from now on (what went to To Do goes to To Do & Calendar).
    static func reveal() { UserDefaults.standard.set(true, forKey: key) }
}
