import AppKit
import SwiftUI

/// Calendar: a month as a calendar's grid — each day's work and events on it in their calendar's colour, today marked —
/// with the day picked listed beside it (and a task of your own added to it); a week day by day; or the next three weeks
/// as a list. On the calendars chosen (Canvas shows ten at most). ‹ and › — or ← and → once the calendar has been
/// clicked — move a month or a week, sliding the way they go; Today comes back. (1.2.3) An item clicked — on the grid, in
/// the day, the week or the list — grows its preview out of itself (Components/WorkPreview.swift); a double-click opens it.
/// (1.3.78) As the Mac's own Calendar has them: Month, Week and List in the toolbar, ‹ Today › beside the period's title
/// on the page (planning-8: the toolbar had no room left, and the Calendars chooser fell into its » overflow).
struct CalendarView: View {
    @EnvironmentObject private var engine: Engine
    /// Month, week or list, as the page keeps it (`calView`).
    @State private var mode = "month"
    @State private var anchor = CalendarView.monthStart(Date())
    @State private var weekStart = CalendarView.firstOfWeek(Date())
    @State private var selected = Calendar.current.startOfDay(for: Date())
    @State private var data: CalendarData?
    @State private var error: String?
    /// Why the last reading failed, once something was already shown.
    @State private var failure: String?
    @State private var loading = false
    /// The span last asked for: an answer for another (a move made since) is not shown.
    @State private var asked = ""
    @State private var modeRead = false
    @State private var choosing = false
    /// (1.3.78) Where what is shown first came from, for the console's open line (OpenClock).
    @State private var source = "fetched"
    /// The way the last move went (the month slides in from that side): set a frame before the move itself, so the
    /// month leaving reads it as it leaves.
    @State private var dir = 1
    @FocusState private var focused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let modes = ["month", "week", "list"]
    private static let line = Color(nsColor: .separatorColor)
    /// A day as the page names it ("2026-10-08"), whatever calendar the Mac is set to.
    private static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static func monthStart(_ d: Date) -> Date {
        let cal = Calendar.current
        return cal.date(from: cal.dateComponents([.year, .month], from: d)) ?? cal.startOfDay(for: d)
    }

    /// The first day of the week a day is in, as the Mac is set (a Sunday, a Monday …).
    private static func firstOfWeek(_ d: Date) -> Date {
        let cal = Calendar.current
        let s = cal.startOfDay(for: d)
        let back = (cal.component(.weekday, from: s) - cal.firstWeekday + 7) % 7
        return cal.date(byAdding: .day, value: -back, to: s) ?? s
    }

    private var cal: Calendar { Calendar.current }

    var body: some View {
        Group {
            if let d = data {
                Page {
                    heading(d)
                    if let note = notice(d) {
                        noticeBar(note.text, problem: note.problem)
                    }
                    content(d)
                        .focusable()
                        .focused($focused)
                        .focusEffectDisabled()
                        .onKeyPress(.leftArrow) { step(-1) }
                        .onKeyPress(.rightArrow) { step(1) }
                }
                .font(.sBody)
            } else {
                LoadState(error: error) { Task { await load(animated: false) } }
            }
        }
        .navigationTitle("Calendar")
        .navigationSubtitle(periodTitle)
        .openTimed("Calendar", ready: data != nil, source: source)
        .toolbar { toolbarItems }
        .onAppear(perform: seed)
        .task(id: engine.dataVersion) {
            // (the screenshot suite: -SimplCalView week|list opens on that view, once, in place of the one kept — the kept
            // one then not read at all, so a read still on its way when the task starts again cannot undo it)
            let asked = LaunchOpen.take("", key: "SimplCalView").flatMap { Self.modes.contains($0) ? $0 : nil }
            if !modeRead {
                modeRead = true
                if asked == nil, let v = try? await engine.read("calView", as: OK.self).view, Self.modes.contains(v) { mode = v }
            }
            if let asked { mode = asked }
            await load(animated: false)
        }
    }

    // MARK: - The toolbar

    @ToolbarContentBuilder
    private var toolbarItems: some ToolbarContent {
        ToolbarItem(placement: .primaryAction) {
            Picker("View", selection: Binding(get: { mode }, set: { setMode($0) })) {
                Text("Month").tag("month")
                Text("Week").tag("week")
                Text("List").tag("list")
            }
            .pickerStyle(.segmented)
            .help("A month, a week, or the next three weeks")
        }
        ToolbarItem(placement: .primaryAction) {
            Button {
                choosing.toggle()
            } label: {
                Label("Calendars", systemImage: "calendar.badge.checkmark")
            }
            .help("Choose the calendars shown")
            .popover(isPresented: $choosing) {
                CalendarsPopover(choices: data?.calendars ?? []) { Task { await load(animated: true) } }
                    .environmentObject(engine)
            }
        }
        ToolbarItem(placement: .primaryAction) {
            CanvasMenu(url: "/calendar", title: "Calendar")
        }
    }

    // MARK: - The heading and the notice

    private func heading(_ d: CalendarData) -> some View {
        HStack(alignment: .center, spacing: 12) {
            ScreenHeading(title: periodTitle, sub: periodLine(d))
            Spacer(minLength: 8)
            if loading {
                ProgressView()
                    .controlSize(.small)
                    .transition(.opacity)
            }
            // (planning-25) the list does not page — it is always the three weeks from today — so it has no ‹ Today ›
            if mode != "list" {
                pager
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: loading)
    }

    /// ‹ Today › beside the month's or the week's title, as the Mac's Calendar has it.
    private var pager: some View {
        GlassGroup(spacing: 6) {
            HStack(spacing: 6) {
                Button {
                    shift(-1)
                } label: {
                    Label(mode == "week" ? "Previous Week" : "Previous Month", systemImage: "chevron.left")
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 14)
                }
                .glassButton()
                .help(mode == "week" ? "Previous week (←)" : "Previous month (←)")
                Button("Today") { goToday() }
                    .glassButton()
                    .keyboardShortcut("t", modifiers: .command)
                    .help("Today (⌘T)")
                Button {
                    shift(1)
                } label: {
                    Label(mode == "week" ? "Next Week" : "Next Month", systemImage: "chevron.right")
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 14)
                }
                .glassButton()
                .help(mode == "week" ? "Next week (→)" : "Next month (→)")
            }
            .controlSize(.large)
        }
    }

    /// (1.2) Under the heading: how much the month or the week holds and how much of it is missing ("14 items · 2
    /// missing"); the list's span.
    private func periodLine(_ d: CalendarData) -> String {
        if mode == "list" { return "The next three weeks" }
        let month = String(Self.dayKey.string(from: anchor).prefix(7))
        let shown = mode == "month" ? d.events.filter { $0.day.hasPrefix(month) } : d.events
        guard !shown.isEmpty else { return mode == "month" ? "Nothing this month" : "Nothing this week" }
        let missing = shown.filter { $0.missing == true }.count
        var parts = ["\(shown.count) \(shown.count == 1 ? "item" : "items")"]
        if missing > 0 { parts.append("\(missing) missing") }
        return parts.joined(separator: " · ")
    }

    /// "October 2026", "Oct 4 – 10, 2026", "Upcoming".
    private var periodTitle: String {
        switch mode {
        case "list":
            return "Upcoming"
        case "week":
            let end = cal.date(byAdding: .day, value: 6, to: weekStart) ?? weekStart
            let f = DateIntervalFormatter()
            f.dateStyle = .medium
            f.timeStyle = .none
            return f.string(from: weekStart, to: end)
        default:
            return anchor.formatted(.dateTime.month(.wide).year())
        }
    }

    /// What the page says over the calendar (no calendar chosen), or why it could not be read.
    private func notice(_ d: CalendarData) -> (text: String, problem: Bool)? {
        if let failure, !failure.isEmpty { return (failure, true) }
        if let e = d.error, !e.isEmpty { return (e, true) }
        if let n = d.notice, !n.isEmpty { return (n, false) }
        return nil
    }

    private func noticeBar(_ text: String, problem: Bool) -> some View {
        HStack(spacing: 10) {
            Image(systemName: problem ? "exclamationmark.triangle.fill" : "info.circle")
                .foregroundStyle(problem ? Color.orange : Color.secondary)
            Text(text)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
            Spacer(minLength: 8)
            if problem {
                Button("Try Again") { Task { await load(animated: true) } }
                    .glassButton()
            } else {
                Button("Choose Calendars…") { choosing = true }
                    .glassButton()
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .card(radius: 14)
        .transition(.opacity)
    }

    // MARK: - The views

    @ViewBuilder
    private func content(_ d: CalendarData) -> some View {
        let byDay = Dictionary(grouping: d.events, by: { $0.day })
        switch mode {
        case "week":
            weekView(byDay)
                .transition(.opacity)
        case "list":
            listView(byDay)
                .transition(.opacity)
        default:
            monthView(byDay)
                .transition(.opacity)
        }
    }

    /// A month moves in from the side the move went; under Reduce Motion it cross-fades.
    private var slide: AnyTransition {
        reduceMotion ? .opacity : .push(from: dir > 0 ? .trailing : .leading)
    }

    private func key(_ day: Date) -> String { Self.dayKey.string(from: day) }

    // MARK: Month

    /// The month's grid with the day picked beside it on a wide window, under it on a narrow one. (1.3.78) Chosen by the
    /// page's width (MonthLayout), not by building both and measuring them: the grid is the heaviest thing drawn.
    private func monthView(_ byDay: [String: [CalEvent]]) -> some View {
        MonthLayout {
            monthGrid(byDay)
        } day: {
            dayPanel(selected, events: byDay[key(selected)] ?? [])
        }
    }

    private func monthGrid(_ byDay: [String: [CalEvent]]) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ForEach(Array(weekdaySymbols.enumerated()), id: \.offset) { _, s in
                    Text(s.uppercased())
                        .font(.sFootnote.weight(.semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .trailing)
                        .padding(.trailing, 12)
                }
            }
            .padding(.vertical, 10)
            .accessibilityHidden(true)
            Self.line.frame(height: 1)
            ZStack {
                monthWeeks(byDay)
                    .id(anchor) // (a new month is a new grid: it slides in from the side the move went)
                    .transition(slide)
            }
            .clipped()
        }
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .card()
    }

    /// The days of the week in the Mac's order ("SUN MON …" or "MON TUE …").
    private var weekdaySymbols: [String] {
        let s = cal.shortWeekdaySymbols
        guard s.count == 7 else { return s }
        let first = (cal.firstWeekday - 1 + 7) % 7
        return Array(s[first...] + s[..<first])
    }

    /// The grid's height whatever the month (six weeks of 112 points): a month of five weeks has taller days.
    private static let monthHeight: CGFloat = 6 * 112

    /// (planning-23) The weeks the month touches — five or six, four for a February that starts on the week's first day —
    /// counted from the first day of the week it starts in. A row of the next month's days is never drawn.
    private func weekCount() -> Int {
        let first = Self.firstOfWeek(anchor)
        let next = cal.date(byAdding: .month, value: 1, to: anchor) ?? anchor
        let days = cal.dateComponents([.day], from: first, to: next).day ?? 42
        return min(6, max(4, (days + 6) / 7))
    }

    /// The month's weeks from the first day of the week it starts in; the grid is always as tall, its days taller when
    /// there are fewer weeks (more of each name fits).
    private func monthWeeks(_ byDay: [String: [CalEvent]]) -> some View {
        let first = Self.firstOfWeek(anchor)
        let weeks = weekCount()
        let height = (Self.monthHeight / CGFloat(weeks)).rounded(.down)
        let days = (0..<(weeks * 7)).compactMap { cal.date(byAdding: .day, value: $0, to: first) }
        return VStack(spacing: 0) {
            ForEach(0..<weeks, id: \.self) { w in
                if w > 0 { Self.line.frame(height: 1) }
                HStack(spacing: 0) {
                    ForEach(Array(days.dropFirst(w * 7).prefix(7).enumerated()), id: \.element) { i, day in
                        dayCell(day, events: byDay[key(day)] ?? [], height: height)
                            .overlay(alignment: .leading) {
                                if i > 0 { Self.line.frame(width: 1) }
                            }
                    }
                }
            }
        }
    }

    /// A day of the month: its number (today on the accent, the day picked on its wash), and its items as lines in their
    /// calendar's colour — (1.2) flat on the grid, no box round each. (planning-13) The open ones first, as many as the
    /// day has room for, a name taking a second line while there is room for it; work handed in folded into one line
    /// ("3 done", with what did not fit: "2 more · 3 done"), so the names still to do get the room.
    private func dayCell(_ day: Date, events evs: [CalEvent], height: CGFloat) -> some View {
        let off = !cal.isDate(day, equalTo: anchor, toGranularity: .month)
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        let open = evs.filter { !$0.isStruck }
        let done = evs.count - open.count
        // (lines of about 18 points under the day's number: four in a day of 112 points, five in one of 134)
        let lines = max(2, Int(((height - 39 + 2) / 18.3).rounded(.down)))
        let tail = done > 0 || open.count > lines
        let room = lines - (tail ? 1 : 0)
        let shown = Array(open.prefix(room))
        let spare = room - shown.count
        let hidden = open.count - shown.count
        let rest = [hidden > 0 ? "\(hidden) more" : nil, done > 0 ? "\(done) done" : nil].compactMap { $0 }.joined(separator: " · ")
        return Button {
            pick(day)
        } label: {
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Spacer(minLength: 0)
                    Text("\(cal.component(.day, from: day))")
                        .font(.sBody.weight(isToday || isSel ? .semibold : .regular).monospacedDigit())
                        .foregroundStyle(isToday ? Color.white : (isSel ? Theme.accent : (off ? Color.secondary : Color.primary)))
                        .frame(minWidth: 26, minHeight: 26)
                        .background {
                            if isToday {
                                Circle().fill(Theme.accent)
                            } else if isSel {
                                Circle().fill(Theme.accent.opacity(0.16))
                            }
                        }
                }
                VStack(alignment: .leading, spacing: 2) {
                    // (1.2.3) an item clicked on the grid grows its preview out of it; the rest of the day picks the day
                    ForEach(Array(shown.enumerated()), id: \.element.id) { i, ev in
                        PreviewLink(item: .event(ev, engine: engine), padded: false, radius: 5, inline: false) {
                            CalendarChip(event: ev, lines: i < spare ? 2 : 1)
                        }
                        .help(ev.title)
                    }
                    if tail {
                        Text(rest)
                            .font(.sCaption.weight(.medium))
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .padding(.leading, 11)
                    }
                }
                .opacity(off ? 0.6 : 1)
                Spacer(minLength: 0)
            }
            .padding(5)
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: height, alignment: .topLeading)
            .background(Theme.accent.opacity(isSel ? 0.07 : 0))
            .animation(Motion.snappy, value: isSel)
        }
        .buttonStyle(RowButtonStyle(radius: 0))
        .accessibilityLabel(Self.spoken(day, count: evs.count))
        .accessibilityAddTraits(isSel ? .isSelected : [])
    }

    private static func spoken(_ day: Date, count: Int) -> String {
        let d = day.formatted(.dateTime.weekday(.wide).month(.wide).day())
        return count == 0 ? "\(d), nothing" : "\(d), \(count) \(count == 1 ? "item" : "items")"
    }

    // MARK: A day

    /// The day picked beside the month: its name, how much is on it and New Task over one card of its work and events.
    private func dayPanel(_ day: Date, events evs: [CalEvent]) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(day.formatted(.dateTime.weekday(.wide)))
                        .font(.sTitle3)
                    Text("\(Self.dateWords(day)) · \(countLine(day, evs.count))")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText())
                }
                Spacer(minLength: 8)
                Button {
                    newTask(on: day)
                } label: {
                    Label("New Task", systemImage: "plus")
                }
                .glassButton()
                .help("A task of your own, due this day (it shows in To Do)")
            }
            dayRows(evs)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    /// A day's work and events in one card, each opening where it lives.
    private func dayRows(_ evs: [CalEvent]) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if evs.isEmpty {
                EmptyNote(text: "Nothing on this day.", symbol: "calendar")
                    .padding(.horizontal, 8)
            }
            ForEach(Array(evs.enumerated()), id: \.element.id) { i, ev in
                VStack(spacing: 0) {
                    if i > 0 { RowDivider() }
                    eventRow(ev)
                }
                .transition(.opacity)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    /// A day of the week or of the list: its name as the section's heading ("Tue, Oct 13"), its work and events in one card
    /// under it.
    private func dayCard(_ day: Date, events evs: [CalEvent]) -> some View {
        PageSection(title: Self.dateWords(day, weekday: true), trailing: countLine(day, evs.count)) {
            dayRows(evs)
        }
    }

    /// (planning-28) A day as every date is written: "Oct 13", "Tue, Oct 13" with its weekday; the year only when it is
    /// not this one ("Jan 5, 2027").
    private static func dateWords(_ day: Date, weekday: Bool = false) -> String {
        let thisYear = Calendar.current.isDate(day, equalTo: Date(), toGranularity: .year)
        let format = (weekday ? "EEE, " : "") + (thisYear ? "MMM d" : "MMM d, yyyy")
        return dateFormats[format]?.string(from: day) ?? day.formatted(date: .abbreviated, time: .omitted)
    }

    /// The four ways `dateWords` writes a day, made once.
    private static let dateFormats: [String: DateFormatter] = Dictionary(uniqueKeysWithValues:
        ["MMM d", "MMM d, yyyy", "EEE, MMM d", "EEE, MMM d, yyyy"].map { format -> (String, DateFormatter) in
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = format
            return (format, f)
        })

    /// "Today · 3 items", "Tomorrow · 1 item", "2 items".
    private func countLine(_ day: Date, _ n: Int) -> String {
        let rel = cal.isDateInToday(day) ? "Today · " : cal.isDateInTomorrow(day) ? "Tomorrow · " : ""
        return "\(rel)\(n) \(n == 1 ? "item" : "items")"
    }

    /// (1.2) A task of your own for a day: New Task's sheet, opened on that day.
    private func newTask(on day: Date) {
        NewTaskSheet.startDay = cal.startOfDay(for: day)
        engine.newTask = true
    }

    /// (1.2.3) A press grows the item's preview out of its row (a double-click opens it where it lives).
    private func eventRow(_ ev: CalEvent) -> some View {
        withEventMenu(ev) {
            PreviewLink(item: .event(ev, engine: engine), inline: false) {
                CalendarEventLine(event: ev)
            }
        }
    }

    /// An item's own menu (open it, open it in Canvas, copy its link), where it has an address.
    @ViewBuilder
    private func withEventMenu<V: View>(_ ev: CalEvent, @ViewBuilder _ content: () -> V) -> some View {
        if let url = ev.url, !url.isEmpty {
            content().contextMenu { eventMenu(ev, url: url) }
        } else {
            content()
        }
    }

    @ViewBuilder
    private func eventMenu(_ ev: CalEvent, url: String) -> some View {
        Button("Open") { engine.openWeb(url, title: ev.title) }
        Divider()
        Button("Open in \(engine.lmsName)") { engine.openWebScreen(url, title: ev.title) }
        Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
    }

    // MARK: Week

    /// The week as seven columns on a wide window, as seven days one under another on a narrow one.
    private func weekView(_ byDay: [String: [CalEvent]]) -> some View {
        let days = (0..<7).compactMap { cal.date(byAdding: .day, value: $0, to: weekStart) }
        return ZStack {
            ViewThatFits(in: .horizontal) {
                weekColumns(days, byDay: byDay)
                    .frame(minWidth: 770, idealWidth: 770, maxWidth: .infinity)
                VStack(alignment: .leading, spacing: 18) {
                    ForEach(days, id: \.self) { day in
                        dayCard(day, events: byDay[key(day)] ?? [])
                    }
                }
            }
            .padding(6) // (room for the cards' shadows inside the clip)
            .id(weekStart)
            .transition(slide)
        }
        .clipped()
        .padding(-6)
    }

    private func weekColumns(_ days: [Date], byDay: [String: [CalEvent]]) -> some View {
        HStack(alignment: .top, spacing: 0) {
            ForEach(Array(days.enumerated()), id: \.element) { i, day in
                weekColumn(day, events: byDay[key(day)] ?? [])
                    .overlay(alignment: .leading) {
                        if i > 0 { Self.line.frame(width: 1) }
                    }
            }
        }
        .fixedSize(horizontal: false, vertical: true) // (every column as tall as the fullest)
        .card()
    }

    private func weekColumn(_ day: Date, events evs: [CalEvent]) -> some View {
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        return VStack(alignment: .leading, spacing: 6) {
            Button {
                pick(day)
            } label: {
                VStack(spacing: 3) {
                    Text(day.formatted(.dateTime.weekday(.abbreviated)).uppercased())
                        .font(.sFootnote.weight(.semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                    Text("\(cal.component(.day, from: day))")
                        .font(.sTitle3.weight(isToday || isSel ? .bold : .medium).monospacedDigit())
                        .foregroundStyle(isToday ? Color.white : (isSel ? Theme.accent : Color.primary))
                        .frame(width: 34, height: 34)
                        .background {
                            if isToday {
                                Circle().fill(Theme.accent)
                            } else if isSel {
                                Circle().fill(Theme.accent.opacity(0.16))
                            }
                        }
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
                .animation(Motion.snappy, value: isSel)
            }
            .buttonStyle(RowButtonStyle(radius: 9))
            .accessibilityLabel(Self.spoken(day, count: evs.count))
            .accessibilityAddTraits(isSel ? .isSelected : [])
            ForEach(evs) { ev in weekBlock(ev) }
        }
        .padding(6)
        .frame(maxWidth: .infinity, minHeight: 380, maxHeight: .infinity, alignment: .top)
    }

    private func weekBlock(_ ev: CalEvent) -> some View {
        withEventMenu(ev) {
            PreviewLink(item: .event(ev, engine: engine), padded: false, radius: 8, inline: false) {
                CalendarWeekBlock(event: ev)
            }
        }
        .help(ev.sub ?? ev.title)
    }

    // MARK: List

    /// The next three weeks, each day with something on it as a section of its own.
    private func listView(_ byDay: [String: [CalEvent]]) -> some View {
        let keys = byDay.keys.sorted()
        return VStack(alignment: .leading, spacing: 22) {
            if keys.isEmpty {
                EmptyNote(text: "Nothing in the next three weeks.", symbol: "calendar")
                    .padding(.horizontal, 18)
                    .card()
            }
            ForEach(keys, id: \.self) { k in
                if let day = Self.dayKey.date(from: k) {
                    dayCard(day, events: byDay[k] ?? [])
                        .transition(.opacity)
                }
            }
        }
    }

    // MARK: - Moving

    private func setMode(_ v: String) {
        guard v != mode else { return }
        focused = true
        withAnimation(Motion.gentle) {
            // (the week or the month of the day picked)
            if v == "week" { weekStart = Self.firstOfWeek(selected) }
            if v == "month" { anchor = Self.monthStart(selected) }
            mode = v
        }
        Task {
            await engine.act("calView", ["view": v])
            await load(animated: true)
        }
    }

    private func step(_ way: Int) -> KeyPress.Result {
        guard mode != "list" else { return .ignored }
        shift(way)
        return .handled
    }

    /// A month or a week on or back; the day picked becomes today when it is in it, else its first day.
    private func shift(_ way: Int) {
        guard mode != "list" else { return }
        focused = true
        Task {
            await move(way) {
                let today = cal.startOfDay(for: Date())
                if mode == "week" {
                    let next = cal.date(byAdding: .day, value: 7 * way, to: weekStart) ?? weekStart
                    let end = cal.date(byAdding: .day, value: 7, to: next) ?? next
                    weekStart = next
                    selected = today >= next && today < end ? today : next
                } else {
                    let next = cal.date(byAdding: .month, value: way, to: anchor) ?? anchor
                    anchor = next
                    selected = cal.isDate(today, equalTo: next, toGranularity: .month) ? today : next
                }
            }
        }
    }

    private func goToday() {
        focused = true
        let today = cal.startOfDay(for: Date())
        let month = Self.monthStart(today)
        let week = Self.firstOfWeek(today)
        let here = mode == "week" ? weekStart : anchor
        let there = mode == "week" ? week : month
        guard here != there else {
            withAnimation(Motion.snappy) { selected = today }
            return
        }
        Task {
            await move(there < here ? -1 : 1) {
                anchor = month
                weekStart = week
                selected = today
            }
        }
    }

    /// A move to another month or week, on the house spring, then read afresh. The way it goes is set a frame before the
    /// move itself, so the one leaving reads it as it leaves.
    private func move(_ way: Int, _ change: () -> Void) async {
        if dir != way {
            dir = way
            try? await Task.sleep(nanoseconds: 20_000_000)
        }
        withAnimation(Motion.gentle) { change() }
        await load(animated: true)
    }

    /// A day picked: one of the month before or after brings its month over, the way it lies.
    private func pick(_ day: Date) {
        focused = true
        let month = Self.monthStart(day)
        if mode == "month", month != anchor {
            Task {
                await move(month < anchor ? -1 : 1) {
                    anchor = month
                    selected = day
                }
            }
        } else {
            withAnimation(Motion.snappy) { selected = day }
        }
    }

    // MARK: - Reading

    /// The span shown: six weeks round the month, the week, or three weeks from today.
    private var range: (Date, Date) {
        Self.span(mode, anchor: anchor, weekStart: weekStart)
    }

    private static func span(_ mode: String, anchor: Date, weekStart: Date) -> (Date, Date) {
        let cal = Calendar.current
        switch mode {
        case "week":
            return (weekStart, cal.date(byAdding: .day, value: 7, to: weekStart) ?? weekStart)
        case "list":
            let s = cal.startOfDay(for: Date())
            return (s, cal.date(byAdding: .day, value: 21, to: s) ?? s)
        default:
            let s = Self.firstOfWeek(anchor)
            return (s, cal.date(byAdding: .day, value: 42, to: s) ?? s)
        }
    }

    /// The calendar's read for a span, as the page takes it.
    private static func args(_ span: (Date, Date)) -> [String: Any] {
        let iso = ISO8601DateFormatter()
        return ["from": iso.string(from: span.0), "to": iso.string(from: span.1)]
    }

    /// (1.3.78) What the Calendar opens on — its view as last chosen, the span round today — read ahead.
    static func readAhead() -> ReadAhead.Ask? {
        ReadAhead.Ask(key: "calendar|now") { engine in
            let v = (try? await engine.read("calView", as: OK.self, quiet: true))?.view ?? "month"
            let shown = Self.span(Self.modes.contains(v) ? v : "month", anchor: Self.monthStart(Date()), weekStart: Self.firstOfWeek(Date()))
            _ = try? await engine.read("calendar", Self.args(shown), as: CalendarData.self, quiet: true)
        }
    }

    /// (1.3.78) This session's answers at once (read ahead, or from a visit before): its view, and the span round today
    /// in it — whole on its first frame, asked again only once stale.
    private func seed() {
        guard data == nil else { return }
        if !modeRead, let v = engine.recent("calView", as: OK.self)?.value.view, Self.modes.contains(v) {
            mode = v
            modeRead = true
        }
        guard let held = engine.recent("calendar", Self.args(range), as: CalendarData.self) else { return }
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) { data = held.value }
        source = "cached"
    }

    private func load(animated: Bool) async {
        let (s, e) = range
        let ask = "\(mode)|\(Int(s.timeIntervalSince1970))|\(Int(e.timeIntervalSince1970))"
        asked = ask
        loading = true
        do {
            let d = try await engine.read("calendar", Self.args((s, e)), as: CalendarData.self)
            guard asked == ask else { return } // (a later move asked for another span)
            if data == nil || !animated {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) {
                    data = d
                    failure = nil
                }
            } else {
                withAnimation(Motion.gentle) {
                    data = d
                    failure = nil
                }
            }
            error = nil
        } catch {
            guard asked == ask else { return }
            if data == nil {
                self.error = error.localizedDescription
            } else {
                let why = error.localizedDescription
                withAnimation(Motion.gentle) { failure = why }
            }
        }
        loading = false
    }
}

// MARK: - Pieces

/// (1.3.78) The month's grid with the day picked beside it when the page has room for both at their ideal widths (the
/// grid's 640 and the day's 340, 22 apart: as a ViewThatFits of the two chose), else the day under the grid — only the
/// one shown is built.
private struct MonthLayout<Grid: View, Day: View>: View {
    @ViewBuilder var grid: () -> Grid
    @ViewBuilder var day: () -> Day
    @Environment(\.pageWidth) private var width

    var body: some View {
        if (width ?? 0) >= 640 + 22 + 340 {
            HStack(alignment: .top, spacing: 22) {
                grid()
                    .frame(minWidth: 540, idealWidth: 640, maxWidth: .infinity)
                day()
                    .frame(width: 340)
            }
        } else {
            VStack(alignment: .leading, spacing: 22) {
                grid()
                day()
            }
        }
    }
}

private extension PreviewItem {
    /// (1.2.3) A piece of work or an event of the calendar, for its preview: its line under the title is "course · kind ·
    /// points · place" (native-app.js calendar). An event whose address is the calendar itself opens on Canvas's page.
    @MainActor
    static func event(_ ev: CalEvent, engine: Engine) -> PreviewItem {
        let kind = PreviewFormat.text(ev.kind) ?? "Event"
        let work = ["Assignment", "Quiz", "Discussion"].contains(kind)
        let parts = (ev.sub ?? "").components(separatedBy: " · ").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        let course = parts.first.flatMap { c -> String? in c == kind || c.hasSuffix(" pts") ? nil : c }
        let place = parts.filter { $0 != course && $0 != kind && !$0.hasSuffix(" pts") && !$0.hasSuffix(" pt") }
        var item = PreviewItem(id: "cal:\(ev.id)", title: ev.title, kind: kind, symbol: CalendarEventLine.glyph(kind), course: course, color: ev.color)
        let day = PreviewFormat.date(ev.date).map { PreviewFormat.dayWord($0) }
        item.when = PreviewFormat.when(day: day, time: ev.time, due: work)
        item.points = PreviewFormat.points(in: ev.sub)
        item.detail = place.isEmpty ? nil : place.joined(separator: " · ")
        if ev.missing == true {
            item.flags = [WorkFlag(word: "Missing", kind: "bad")]
        } else if ev.excused == true {
            item.flags = [WorkFlag(word: "Excused", kind: "muted")]
        } else if work && ev.done == true {
            item.flags = [WorkFlag(word: "Submitted", kind: "good")]
        }
        if let url = PreviewFormat.text(ev.url) {
            item.url = url
            if engine.tabName(for: url) == "calendar" {
                // (Canvas's own page for an event: here the calendar would only open on itself)
                item.openLabel = "Open in \(engine.lmsName)"
                item.openWhole = { engine.openWebScreen(url, title: ev.title) }
            } else {
                item.openWhole = { engine.openWeb(url, title: ev.title) }
            }
            if work {
                item.work = WorkAction(url: url, title: ev.title, kind: kind.lowercased(), handedIn: ev.done == true && ev.missing != true)
            }
        }
        return item
    }
}

extension CalEvent {
    /// (planning-24) Struck through: work handed in or excused, in every view (the engine's `struck`; before 2.99.33,
    /// work handed in) — never a lecture or a lab that is only over.
    var isStruck: Bool {
        if let struck { return struck }
        return missing != true && (excused == true || (done == true && ["Assignment", "Quiz", "Discussion"].contains(kind ?? "")))
    }

    /// Greyed: over (its due time or its end gone by), or struck.
    var isPast: Bool { (past ?? (done == true)) || isStruck }
}

/// A piece of work or an event on a day of the month: (1.2) a line on the grid, (planning-13) a thin bar in its
/// calendar's colour before its name — a red mark when it is missing; greyed once over, struck through only once
/// handed in. A name may take `lines` lines.
private struct CalendarChip: View {
    let event: CalEvent
    var lines = 1

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        let grey = event.isPast && !missing
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            if missing {
                Image(systemName: "exclamationmark.circle.fill")
                    .font(.sCaption2.weight(.bold))
                    .foregroundStyle(.red)
                    .frame(width: 8)
            }
            // (a name on two lines only where each of its words fits a line: a word wider than the day — "Composition" —
            // was broken in the middle, "Compositio / n"; such a name keeps to one line, cut at its end)
            ViewThatFits(in: .horizontal) {
                if lines > 1 {
                    title(lines: lines)
                        .frame(idealWidth: CalendarChip.longestWord(event.title), alignment: .leading)
                }
                title(lines: 1)
            }
            .foregroundStyle(grey ? Color.secondary : (missing ? Color.red : Color.primary))
            .padding(.leading, missing ? 0 : 7)
                .overlay(alignment: .leading) {
                    if !missing {
                        Capsule()
                            .fill(c.opacity(grey ? 0.55 : 1))
                            .frame(width: 3)
                            .padding(.vertical, 1.5)
                    }
                }
        }
        .font(.sCaption)
        .padding(.horizontal, 4)
        .padding(.vertical, 1)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityHidden(true)
    }

    private func title(lines: Int) -> some View {
        Text(event.title)
            .strikethrough(event.isStruck && event.missing != true, color: .secondary)
            .lineLimit(lines)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// The width of a name's widest word at the chip's size (12 points, as `.sCaption`), and a point to spare.
    static func longestWord(_ title: String) -> CGFloat {
        let font = NSFont.systemFont(ofSize: 12)
        let widest = title.split(whereSeparator: { $0 == " " || $0 == "\n" })
            .map { (String($0) as NSString).size(withAttributes: [.font: font]).width }
            .max() ?? 0
        return ceil(widest) + 1
    }
}

/// A piece of work or an event in a day's list: its kind's icon in its calendar's colour, its name (struck through once
/// done), its course, kind, points and place, whether it is missing or excused, and its time.
private struct CalendarEventLine: View {
    let event: CalEvent

    var body: some View {
        let missing = event.missing == true
        HStack(spacing: 12) {
            IconTile(symbol: CalendarEventLine.glyph(event.kind), color: Color(hex: event.color))
            VStack(alignment: .leading, spacing: 2) {
                // (planning-24) struck through only once handed in, greyed once over: as on the month's grid
                Text(event.title)
                    .font(.sBody)
                    .strikethrough(event.isStruck && !missing, color: .secondary)
                    .foregroundStyle(event.isPast && !missing ? Color.secondary : Color.primary)
                    .lineLimit(2)
                if let sub = event.sub, !sub.isEmpty {
                    Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            if missing {
                FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad"))
            } else if event.excused == true {
                FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted"))
            }
            if let time = event.time, !time.isEmpty {
                Text(time)
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: 58, alignment: .trailing)
            }
        }
        .contentShape(Rectangle())
    }

    static func glyph(_ kind: String?) -> String {
        switch kind ?? "" {
        case "Assignment": return "doc.text"
        case "Quiz": return "questionmark.square" // (courses-8: a quiz's own symbol everywhere; the checklist is To Do's)
        case "Discussion": return "bubble.left.and.bubble.right"
        case "Appointment": return "person.2"
        default: return "calendar"
        }
    }
}

/// A piece of work or an event in a column of the week: (1.2) its name beside a bar in its calendar's colour, flat in the
/// column, its time, and whether it is missing or excused.
private struct CalendarWeekBlock: View {
    let event: CalEvent

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        VStack(alignment: .leading, spacing: 3) {
            // (planning-24) struck through only once handed in, greyed once over: as on the month's grid
            Text(event.title)
                .strikethrough(event.isStruck && !missing, color: .secondary)
                .font(.sCallout.weight(.medium))
                .foregroundStyle(event.isPast && !missing ? Color.secondary : Color.primary)
                .lineLimit(3)
                .multilineTextAlignment(.leading)
            HStack(spacing: 5) {
                if let time = event.time, !time.isEmpty {
                    Text(time).font(.sFootnote.monospacedDigit()).foregroundStyle(.secondary)
                }
                if missing {
                    FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad"))
                } else if event.excused == true {
                    FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted"))
                }
            }
        }
        .padding(.leading, 12)
        .padding(.trailing, 6)
        .padding(.vertical, 6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .leading) {
            Capsule()
                .fill(c)
                .frame(width: 3.5)
                .padding(.vertical, 5)
                .padding(.leading, 3)
        }
        .contentShape(Rectangle())
    }
}

/// The calendars shown, each with its colour: your own courses on, the rest off until turned on — ten at most, as Canvas
/// allows (the others wait, greyed, until one is turned off).
private struct CalendarsPopover: View {
    let choices: [CalendarChoice]
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var on: Set<String> = []

    private var own: [CalendarChoice] { choices.filter { $0.own == true } }
    private var other: [CalendarChoice] { choices.filter { $0.own != true } }
    private var full: Bool { on.count >= 10 }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Calendars").font(.sHeadline)
                Spacer(minLength: 8)
                Text("\(on.count) of \(choices.count) on")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(on.count)))
            }
            if choices.isEmpty {
                Text("No calendars to show.").foregroundStyle(.secondary)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 9) {
                        ForEach(own) { row($0) }
                        if !other.isEmpty {
                            Text("Other Calendars")
                                .font(.sFootnote.weight(.semibold))
                                .foregroundStyle(.secondary)
                                .padding(.top, own.isEmpty ? 0 : 8)
                            ForEach(other) { row($0) }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .frame(height: listHeight)
            }
            Divider()
            Text(full ? "Ten calendars at most: turn one off to show another." : (engine.onBrightspace ? "At most 10 calendars show at once." : "Canvas shows at most 10 calendars at once."))
                .font(.sFootnote)
                .foregroundStyle(full ? Color.orange : Color.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sBody)
        .padding(18)
        .frame(width: 340)
        .animation(Motion.snappy, value: full)
        .onAppear { on = Set(choices.filter { $0.on }.map { $0.code }) }
    }

    private var listHeight: CGFloat {
        let rows = CGFloat(choices.count) * 28 + (other.isEmpty ? 0 : 34)
        return min(max(rows, 28), 420)
    }

    private func row(_ c: CalendarChoice) -> some View {
        Toggle(isOn: Binding(get: { on.contains(c.code) }, set: { set(c, $0) })) {
            HStack(spacing: 8) {
                Circle()
                    .fill(Color(hex: c.color))
                    .frame(width: 11, height: 11)
                Text(c.name).font(.sBody).lineLimit(1)
            }
        }
        .toggleStyle(.checkbox)
        .disabled(!on.contains(c.code) && full)
        .help(c.name)
    }

    private func set(_ c: CalendarChoice, _ value: Bool) {
        if value && on.count >= 10 { return }
        if value { on.insert(c.code) } else { on.remove(c.code) }
        let codes = choices.map { $0.code }.filter { on.contains($0) }
        Task {
            await engine.act("setCalendars", ["codes": codes])
            changed()
        }
    }
}
