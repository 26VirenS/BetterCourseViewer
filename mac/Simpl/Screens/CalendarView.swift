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
    /// The screenshot suite's -SimplCalView looked at (each Calendar drawn looks once).
    @State private var launchViewRead = false
    @State private var choosing = false
    /// (1.3.78) Where what is shown first came from, for the console's open line (OpenClock).
    @State private var source = "fetched"
    /// (1.3.80) What the live checks know of the span shown (its fingerprint: LiveRefresh).
    @State private var marks = LiveMarks()
    /// The way the last move went (the month slides in from that side): set a frame before the move itself, so the
    /// month leaving reads it as it leaves.
    @State private var dir = 1
    /// (1.3.147) The list's days, picked beside it: the next three weeks until another span is picked (kept).
    @State private var agenda = AgendaSpan.saved() ?? .standard
    /// The first day of a span being picked, while its last is still to come.
    @State private var agendaFrom: Date?
    @FocusState private var focused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.pageWidth) private var pageWidth

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
            // (the screenshot suite: -SimplCalView week|list opens on that view, in place of the one kept — the kept one
            // then not read at all, so a read still on its way when the task starts again cannot undo it; read by each
            // Calendar drawn, once, as the window's screens are made again while the app's answers come in)
            var asked: String?
            if !launchViewRead {
                launchViewRead = true
                asked = UserDefaults.standard.string(forKey: "SimplCalView").flatMap { Self.modes.contains($0) ? $0 : nil }
            }
            if !modeRead {
                modeRead = true
                if asked == nil, let v = try? await engine.read("calView", as: OK.self).view, Self.modes.contains(v) { mode = v }
            }
            if let asked { mode = asked }
            await load(animated: false)
        }
        // (1.3.80: checked every minute while it is on show; an event or a due date that changed moves into place)
        .liveChecks("calendar", since: { marks.at }) { engine, age in try await liveCheck(engine, age) }
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
            // (1.3.147) the list pages too, by as many days as it shows
            pager
        }
        .animation(Motion.gentle, value: loading)
    }

    /// What ‹ and › move by, as their help names it.
    private var stepWords: (back: String, on: String) {
        switch mode {
        case "week": return ("Previous Week", "Next Week")
        case "list": return ("Earlier Days", "Later Days")
        default: return ("Previous Month", "Next Month")
        }
    }

    /// ‹ Today › beside the month's, the week's or the list's title, as the Mac's Calendar has it.
    private var pager: some View {
        GlassGroup(spacing: 6) {
            HStack(spacing: 6) {
                Button {
                    shift(-1)
                } label: {
                    Label(stepWords.back, systemImage: "chevron.left")
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 14)
                }
                .glassButton()
                .help("\(stepWords.back) (←)")
                Button("Today") { goToday() }
                    .glassButton()
                    .keyboardShortcut("t", modifiers: .command)
                    .help("Today (⌘T)")
                Button {
                    shift(1)
                } label: {
                    Label(stepWords.on, systemImage: "chevron.right")
                        .labelStyle(.iconOnly)
                        .frame(minWidth: 14)
                }
                .glassButton()
                .help("\(stepWords.on) (→)")
            }
            .controlSize(.large)
        }
    }

    /// (1.2) Under the heading: how much the month or the week holds and how much of it is missing ("14 items · 2
    /// missing"); the list's span.
    private func periodLine(_ d: CalendarData) -> String {
        if mode == "list" && agenda.isStandard { return "The next three weeks" }
        let month = String(Self.dayKey.string(from: anchor).prefix(7))
        let shown = mode == "month" ? d.events.filter { $0.day.hasPrefix(month) } : d.events
        guard !shown.isEmpty else { return mode == "month" ? "Nothing this month" : mode == "week" ? "Nothing this week" : "Nothing in these days" }
        let missing = shown.filter { $0.missing == true }.count
        var parts = ["\(shown.count) \(shown.count == 1 ? "item" : "items")"]
        if missing > 0 { parts.append("\(missing) missing") }
        return parts.joined(separator: " · ")
    }

    /// "October 2026", "Oct 4 – 10, 2026", "Upcoming" (the next three weeks) or the days picked for the list.
    private var periodTitle: String {
        switch mode {
        case "list":
            guard !agenda.isStandard else { return "Upcoming" }
            let f = DateIntervalFormatter()
            f.dateStyle = .medium
            f.timeStyle = .none
            return f.string(from: agenda.start, to: agenda.last)
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

    /// The day panel's width beside the month, and the least the grid keeps beside it.
    private static let dayWidth: CGFloat = 300
    private static let gridLeast: CGFloat = 476

    /// The month's grid with the day picked beside it on a wide window, under it on a narrow one. (1.3.78) Chosen by the
    /// page's width, not by building both and measuring them: the grid is the heaviest thing drawn. (1.3.147) The grid's
    /// weeks as tall as its days are wide (84 to 118 points), so a narrow window is not given a month taller than wide;
    /// the day beside it from a grid of 476 points.
    @ViewBuilder
    private func monthView(_ byDay: [String: [CalEvent]]) -> some View {
        let width = pageWidth ?? 1000
        let beside = width >= Self.gridLeast + 20 + Self.dayWidth
        let gridWidth = beside ? width - 20 - Self.dayWidth : width
        let row = min(118, max(84, (gridWidth / 7).rounded(.down)))
        if beside {
            HStack(alignment: .top, spacing: 20) {
                monthGrid(byDay, height: row * 6)
                    .frame(maxWidth: .infinity)
                dayPanel(selected, events: byDay[key(selected)] ?? [])
                    .frame(width: Self.dayWidth)
            }
        } else {
            VStack(alignment: .leading, spacing: 22) {
                monthGrid(byDay, height: row * 6)
                dayPanel(selected, events: byDay[key(selected)] ?? [])
            }
        }
    }

    private func monthGrid(_ byDay: [String: [CalEvent]], height: CGFloat) -> some View {
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
                monthWeeks(byDay, total: height)
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

    /// (planning-23) The weeks the month touches — five or six, four for a February that starts on the week's first day —
    /// counted from the first day of the week it starts in. A row of the next month's days is never drawn.
    private func weekCount() -> Int {
        let first = Self.firstOfWeek(anchor)
        let next = cal.date(byAdding: .month, value: 1, to: anchor) ?? anchor
        let days = cal.dateComponents([.day], from: first, to: next).day ?? 42
        return min(6, max(4, (days + 6) / 7))
    }

    /// The month's weeks from the first day of the week it starts in; the grid is always as tall (six weeks' worth), its
    /// days taller when there are fewer weeks (more of each name fits).
    private func monthWeeks(_ byDay: [String: [CalEvent]], total: CGFloat) -> some View {
        let first = Self.firstOfWeek(anchor)
        let weeks = weekCount()
        let height = (total / CGFloat(weeks)).rounded(.down)
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
            // (1.3.147: the days of the months either side a shade deeper, so the month's own stand out)
            .background(isSel ? Theme.accent.opacity(0.07) : Color.primary.opacity(off ? 0.035 : 0))
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

    /// An hour of the week's grid, and the room for the hours' names before the days.
    private static let hourHeight: CGFloat = 42
    private static let gutter: CGFloat = 46

    /// (1.3.147) The week as the Mac's Calendar draws one: its seven days side by side over the hours, each item at its
    /// time, the day-long ones over the hours — down to a page of 520 points; on a narrower one, its days one under
    /// another.
    private func weekView(_ byDay: [String: [CalEvent]]) -> some View {
        let days = (0..<7).compactMap { cal.date(byAdding: .day, value: $0, to: weekStart) }
        return ZStack {
            Group {
                if (pageWidth ?? 1000) >= 520 {
                    weekGrid(days, byDay: byDay)
                } else {
                    VStack(alignment: .leading, spacing: 18) {
                        ForEach(days, id: \.self) { day in
                            dayCard(day, events: byDay[key(day)] ?? [])
                        }
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

    /// How far down the week's hours a time of day is: `minutes` into the day, the hours drawn from `first`.
    private static func weekY(_ minutes: Int, from first: Int) -> CGFloat {
        let into = Double(minutes - first * 60)
        return CGFloat(into / 60) * hourHeight
    }

    /// The minutes into its day an item is at; nil for one that lasts the day (or has no time).
    private func minutes(_ ev: CalEvent) -> Int? {
        guard ev.allDay != true, let t = ev.time, !t.isEmpty, let d = PreviewFormat.date(ev.date) else { return nil }
        let c = cal.dateComponents([.hour, .minute], from: d)
        return (c.hour ?? 0) * 60 + (c.minute ?? 0)
    }

    /// "8 AM", "Noon", "1 PM" — or "08", "13" on a Mac set to the 24-hour clock — as the Mac writes an hour.
    private func hourName(_ h: Int) -> String {
        guard let d = cal.date(bySettingHour: h, minute: 0, second: 0, of: Date()) else { return "\(h)" }
        let name = d.formatted(.dateTime.hour())
        return h == 12 && name.contains("PM") ? "Noon" : name
    }

    private func weekGrid(_ days: [Date], byDay: [String: [CalEvent]]) -> some View {
        let timed = days.map { day in
            (byDay[key(day)] ?? []).compactMap { ev in minutes(ev).map { WeekItem(ev: ev, at: $0) } }.sorted { $0.at < $1.at }
        }
        let allDay = days.map { day in (byDay[key(day)] ?? []).filter { minutes($0) == nil } }
        // (from 8 in the morning, or the hour of the week's earliest item, to midnight)
        let first = min(8, timed.flatMap { $0 }.map { $0.at / 60 }.min() ?? 8)
        let hours = Array(first..<24)
        let span = CGFloat(hours.count) * Self.hourHeight
        return VStack(spacing: 0) {
            HStack(spacing: 0) {
                Color.clear.frame(width: Self.gutter)
                ForEach(days, id: \.self) { day in
                    weekHeader(day, count: (byDay[key(day)] ?? []).count)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.vertical, 4)
            Self.line.frame(height: 1)
            if allDay.contains(where: { !$0.isEmpty }) {
                HStack(alignment: .top, spacing: 0) {
                    Text("all-day")
                        .font(.sCaption2)
                        .foregroundStyle(.secondary)
                        .frame(width: Self.gutter - 8, alignment: .trailing)
                        .padding(.trailing, 8)
                        .padding(.top, 7)
                    ForEach(Array(days.enumerated()), id: \.element) { i, _ in
                        VStack(spacing: 3) {
                            ForEach(allDay[i]) { weekBlock($0) }
                        }
                        .padding(3)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                        .overlay(alignment: .leading) { Self.line.frame(width: 1) }
                    }
                }
                .fixedSize(horizontal: false, vertical: true) // (every day's strip as tall as the fullest)
                Self.line.frame(height: 1)
            }
            HStack(alignment: .top, spacing: 0) {
                VStack(spacing: 0) {
                    ForEach(hours, id: \.self) { h in
                        Text(hourName(h))
                            .font(.sCaption2.monospacedDigit())
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.8)
                            .padding(.trailing, 8)
                            .padding(.top, 3)
                            .frame(width: Self.gutter, height: Self.hourHeight, alignment: .topTrailing)
                    }
                }
                ForEach(Array(days.enumerated()), id: \.element) { i, day in
                    WeekDayStack {
                        ForEach(timed[i]) { item in
                            weekBlock(item.ev)
                                .layoutValue(key: WeekAt.self, value: Self.weekY(item.at, from: first))
                        }
                    }
                    .padding(.horizontal, 3)
                    .frame(maxWidth: .infinity, minHeight: span, maxHeight: .infinity, alignment: .top)
                    .background(Theme.accent.opacity(cal.isDateInToday(day) ? 0.045 : 0))
                    .overlay(alignment: .top) {
                        if cal.isDateInToday(day) { nowLine(from: first) }
                    }
                    .overlay(alignment: .leading) { Self.line.frame(width: 1) }
                }
            }
            .fixedSize(horizontal: false, vertical: true) // (every day as tall as the hours, or the fullest day)
            .background(alignment: .top) {
                // (a hairline at each hour, past the hours' names)
                VStack(spacing: 0) {
                    ForEach(hours, id: \.self) { h in
                        VStack(spacing: 0) {
                            if h > first { Self.line.opacity(0.7).frame(height: 1) }
                            Spacer(minLength: 0)
                        }
                        .frame(height: Self.hourHeight)
                    }
                }
                .padding(.leading, Self.gutter)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .card()
    }

    /// Now, on today's column: a red line at the minute, kept there as the minutes go by.
    private func nowLine(from first: Int) -> some View {
        TimelineView(.everyMinute) { context in
            let c = cal.dateComponents([.hour, .minute], from: context.date)
            let minutes: Int = (c.hour ?? 0) * 60 + (c.minute ?? 0)
            let at: CGFloat = Self.weekY(minutes, from: first)
            if at >= 0 {
                HStack(spacing: 0) {
                    Circle().fill(Color.red).frame(width: 7, height: 7)
                    Rectangle().fill(Color.red).frame(height: 1.5)
                }
                .offset(x: -3.5, y: at - 3.5)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
            }
        }
    }

    /// A day's name and number over its column; a click picks the day (its month, when Month is chosen).
    private func weekHeader(_ day: Date, count: Int) -> some View {
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        return Button {
            pick(day)
        } label: {
            VStack(spacing: 2) {
                Text(day.formatted(.dateTime.weekday(.abbreviated)).uppercased())
                    .font(.sCaption.weight(.semibold))
                    .tracking(0.6)
                    .foregroundStyle(.secondary)
                Text("\(cal.component(.day, from: day))")
                    .font(.sTitle3.weight(isToday || isSel ? .bold : .medium).monospacedDigit())
                    .foregroundStyle(isToday ? Color.white : (isSel ? Theme.accent : Color.primary))
                    .frame(width: 32, height: 32)
                    .background {
                        if isToday {
                            Circle().fill(Theme.accent)
                        } else if isSel {
                            Circle().fill(Theme.accent.opacity(0.16))
                        }
                    }
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 4)
            .animation(Motion.snappy, value: isSel)
        }
        .buttonStyle(RowButtonStyle(radius: 9))
        .padding(.horizontal, 2)
        .accessibilityLabel(Self.spoken(day, count: count))
        .accessibilityAddTraits(isSel ? .isSelected : [])
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

    /// The list's days — the next three weeks, or the days picked — each day with something on it as a section of its
    /// own; (1.3.147) the days' picker beside it on a page of 620 points or more, over it on a narrower one.
    @ViewBuilder
    private func listView(_ byDay: [String: [CalEvent]]) -> some View {
        if (pageWidth ?? 1000) >= 620 {
            HStack(alignment: .top, spacing: 20) {
                listDays(byDay)
                    .frame(maxWidth: .infinity, alignment: .leading)
                agendaPicker
                    .frame(width: 256)
            }
        } else {
            VStack(alignment: .leading, spacing: 22) {
                agendaPicker
                    .frame(maxWidth: 380, alignment: .leading)
                listDays(byDay)
            }
        }
    }

    private func listDays(_ byDay: [String: [CalEvent]]) -> some View {
        let keys = byDay.keys.sorted()
        return VStack(alignment: .leading, spacing: 22) {
            if keys.isEmpty {
                EmptyNote(text: agenda.isStandard ? "Nothing in the next three weeks." : "Nothing in these days.", symbol: "calendar")
                    .padding(.horizontal, 18)
                    .frame(maxWidth: .infinity, alignment: .leading)
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

    private var agendaPicker: some View {
        AgendaPicker(span: agenda, from: agendaFrom, pick: pickAgenda, reset: resetAgenda)
    }

    /// (1.3.147) A day clicked in the list's picker: the first of a new span, then its last (one before the first makes
    /// that one the first) — at most 120 days, as the web's agenda has it. The list moves once both are picked.
    private func pickAgenda(_ day: Date) {
        focused = true
        guard let from = agendaFrom else {
            withAnimation(Motion.snappy) { agendaFrom = day }
            return
        }
        let start = min(from, day)
        var end = max(from, day)
        if let most = cal.date(byAdding: .day, value: 119, to: start), end > most { end = most }
        let next = AgendaSpan(start: start, end: end)
        Task {
            await move(start < agenda.start ? -1 : 1) {
                agendaFrom = nil
                agenda = next
            }
        }
        next.save()
    }

    /// Back to the next three weeks.
    private func resetAgenda() {
        focused = true
        let next = AgendaSpan.standard
        AgendaSpan.forget()
        guard next != agenda else {
            withAnimation(Motion.snappy) { agendaFrom = nil }
            return
        }
        Task {
            await move(next.start < agenda.start ? -1 : 1) {
                agendaFrom = nil
                agenda = next
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
        shift(way)
        return .handled
    }

    /// A month or a week on or back; the day picked becomes today when it is in it, else its first day. (1.3.147) The
    /// list moves by as many days as it shows.
    private func shift(_ way: Int) {
        focused = true
        Task {
            await move(way) {
                let today = cal.startOfDay(for: Date())
                if mode == "list" {
                    let days = (cal.dateComponents([.day], from: agenda.start, to: agenda.last).day ?? 20) + 1
                    let start = cal.date(byAdding: .day, value: days * way, to: agenda.start) ?? agenda.start
                    agendaFrom = nil
                    agenda = AgendaSpan(start: start, end: cal.date(byAdding: .day, value: days - 1, to: start))
                    agenda.isStandard ? AgendaSpan.forget() : agenda.save()
                } else if mode == "week" {
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
        if mode == "list" { return resetAgenda() }
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

    /// The span shown: six weeks round the month, the week, or the list's days.
    private var range: (Date, Date) {
        Self.span(mode, anchor: anchor, weekStart: weekStart, agenda: agenda)
    }

    private static func span(_ mode: String, anchor: Date, weekStart: Date, agenda: AgendaSpan) -> (Date, Date) {
        let cal = Calendar.current
        switch mode {
        case "week":
            return (weekStart, cal.date(byAdding: .day, value: 7, to: weekStart) ?? weekStart)
        case "list":
            return (agenda.start, cal.date(byAdding: .day, value: 1, to: agenda.last) ?? agenda.last)
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
            let shown = Self.span(Self.modes.contains(v) ? v : "month", anchor: Self.monthStart(Date()), weekStart: Self.firstOfWeek(Date()),
                                  agenda: AgendaSpan.saved() ?? .standard)
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
            marks.shown("calendar", Self.args((s, e)), by: engine) // (1.3.80: the answer a live check asks after)
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

    /// (1.3.80) The live check (LiveRefresh): the span shown asked again with the check's age; a new answer put in place
    /// — an event that came, a due date that moved — the rest left as it is. A move to another span made meanwhile is
    /// never undone by it.
    private func liveCheck(_ engine: Engine, _ age: Double) async throws -> LiveFound {
        let ask = asked
        let args = Self.args(range)
        guard let shown = data, !loading,
              let c = try await engine.liveCheck("calendar", args, as: CalendarData.self, shown: shown, marks: marks, maxAge: age) else {
            marks.at = Date()
            return .unchanged
        }
        return LiveFound(parts: ["calendar"]) {
            guard asked == ask else { return }
            c.put {
                withAnimation(LiveRefresh.motion) {
                    data = c.value
                    failure = nil
                }
            }
            marks.at = Date()
        }
    }
}

// MARK: - Pieces

/// (1.3.147) The list's days: its first and its last, both shown — the next three weeks from today until other days are
/// picked beside it (kept, as the web's agenda keeps its range).
struct AgendaSpan: Equatable {
    var start: Date
    var end: Date?

    /// The last day shown (three weeks' worth when no end was given).
    var last: Date { end ?? Calendar.current.date(byAdding: .day, value: 20, to: start) ?? start }

    static var standard: AgendaSpan {
        let s = Calendar.current.startOfDay(for: Date())
        return AgendaSpan(start: s, end: Calendar.current.date(byAdding: .day, value: 20, to: s))
    }

    var isStandard: Bool {
        let cal = Calendar.current
        let std = AgendaSpan.standard
        return cal.isDate(start, inSameDayAs: std.start) && cal.isDate(last, inSameDayAs: std.last)
    }

    private static let key = "SimplAgendaSpan"
    private static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    /// The days last picked, while they are not the next three weeks.
    static func saved() -> AgendaSpan? {
        guard let days = UserDefaults.standard.stringArray(forKey: key), days.count == 2,
              let s = dayKey.date(from: days[0]), let e = dayKey.date(from: days[1]), e >= s else { return nil }
        return AgendaSpan(start: s, end: e)
    }

    func save() {
        UserDefaults.standard.set([Self.dayKey.string(from: start), Self.dayKey.string(from: last)], forKey: Self.key)
    }

    static func forget() { UserDefaults.standard.removeObject(forKey: key) }
}

/// (1.3.147) The list's days picked on a small month beside it, as the web's agenda has them: a day clicked starts the
/// span, the next ends it; the days between washed in the accent, its ends on it. ‹ and › move the small month; Reset
/// goes back to the next three weeks.
private struct AgendaPicker: View {
    let span: AgendaSpan
    let from: Date?
    let pick: (Date) -> Void
    let reset: () -> Void
    /// The month shown when another than the span's first was moved to.
    @State private var month: Date?
    private var cal: Calendar { Calendar.current }

    private var shown: Date {
        let m = month ?? from ?? span.start
        return cal.date(from: cal.dateComponents([.year, .month], from: m)) ?? m
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 6) {
                Text(shown.formatted(.dateTime.month(.wide).year()))
                    .font(.sHeadline)
                Spacer(minLength: 4)
                Button {
                    turn(-1)
                } label: {
                    Image(systemName: "chevron.left").frame(width: 14, height: 14)
                }
                .buttonStyle(.borderless)
                .help("Previous month")
                .accessibilityLabel("Previous Month")
                Button {
                    turn(1)
                } label: {
                    Image(systemName: "chevron.right").frame(width: 14, height: 14)
                }
                .buttonStyle(.borderless)
                .help("Next month")
                .accessibilityLabel("Next Month")
            }
            grid
            Divider()
            HStack(alignment: .center, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(from.map { "\(Self.words($0)) – …" } ?? "\(Self.words(span.start)) – \(Self.words(span.last))")
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .contentTransition(.numericText())
                    Text(from == nil ? "Click a day to start new days" : "Click the last day")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 4)
                Button("Reset", action: reset)
                    .glassButton()
                    .disabled(from == nil && span.isStandard)
                    .help("Back to the next three weeks")
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .onChange(of: span) { _, _ in month = nil }
        .animation(Motion.snappy, value: from)
        .animation(Motion.snappy, value: span)
    }

    private func turn(_ way: Int) {
        withAnimation(Motion.snappy) { month = cal.date(byAdding: .month, value: way, to: shown) }
    }

    private static func words(_ d: Date) -> String { d.formatted(.dateTime.month(.abbreviated).day()) }

    private var grid: some View {
        let back = (cal.component(.weekday, from: shown) - cal.firstWeekday + 7) % 7
        let first = cal.date(byAdding: .day, value: -back, to: shown) ?? shown
        let days = (0..<42).compactMap { cal.date(byAdding: .day, value: $0, to: first) }
        let symbols: [String] = {
            let s = cal.veryShortWeekdaySymbols
            guard s.count == 7 else { return s }
            let f = (cal.firstWeekday - 1 + 7) % 7
            return Array(s[f...] + s[..<f])
        }()
        return VStack(spacing: 2) {
            HStack(spacing: 0) {
                ForEach(Array(symbols.enumerated()), id: \.offset) { _, s in
                    Text(s)
                        .font(.sCaption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                }
            }
            .accessibilityHidden(true)
            ForEach(0..<6, id: \.self) { w in
                HStack(spacing: 0) {
                    ForEach(days[(w * 7)..<(w * 7 + 7)], id: \.self) { day in
                        cell(day)
                    }
                }
            }
        }
    }

    private func cell(_ day: Date) -> some View {
        let start = from ?? span.start
        let last = from ?? span.last
        let isStart = cal.isDate(day, inSameDayAs: start)
        let isEnd = cal.isDate(day, inSameDayAs: last)
        let inside = day > start && day < last
        let off = !cal.isDate(day, equalTo: shown, toGranularity: .month)
        let today = cal.isDateInToday(day)
        return Button {
            pick(cal.startOfDay(for: day))
        } label: {
            Text("\(cal.component(.day, from: day))")
                .font(.sCallout.weight(isStart || isEnd || today ? .semibold : .regular).monospacedDigit())
                .foregroundStyle(isStart || isEnd ? Color.white : (today ? Theme.accent : (off ? Color.secondary.opacity(0.6) : Color.primary)))
                .frame(width: 28, height: 28)
                .background {
                    if isStart || isEnd { Circle().fill(Theme.accent) }
                }
                .frame(maxWidth: .infinity)
                .frame(height: 30)
                // (the days between, and the half of each end toward them, washed in the accent: one band)
                .background {
                    HStack(spacing: 0) {
                        Theme.accent.opacity(inside || (isEnd && !isStart) ? 0.16 : 0)
                        Theme.accent.opacity(inside || (isStart && !isEnd) ? 0.16 : 0)
                    }
                    .padding(.vertical, 1)
                }
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(day.formatted(.dateTime.weekday(.wide).month(.wide).day()))
        .accessibilityAddTraits(isStart || isEnd ? .isSelected : [])
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

/// A piece of work or an event in a day of the week: (1.3.147) a small block washed in its calendar's colour, a bar of it
/// before its name, its time under it (or that it is missing: then in red, a mark before the time); greyed once over,
/// struck through only once handed in. The names wrap to three lines in a narrow day.
private struct CalendarWeekBlock: View {
    let event: CalEvent

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        let grey = event.isPast && !missing
        let time = event.allDay == true ? nil : PreviewFormat.text(event.time)
        VStack(alignment: .leading, spacing: 1) {
            // (planning-24) struck through only once handed in, greyed once over: as on the month's grid
            Text(event.title)
                .font(.sCaption.weight(.semibold))
                .strikethrough(event.isStruck && !missing, color: .secondary)
                .foregroundStyle(grey ? Color.secondary : Color.primary)
                .lineLimit(3)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
            if time != nil || missing || event.excused == true {
                HStack(spacing: 3) {
                    if missing {
                        Image(systemName: "exclamationmark.circle.fill")
                    }
                    Text(time ?? (missing ? "Missing" : "Excused"))
                        .monospacedDigit()
                }
                .font(.sCaption2)
                .foregroundStyle(missing ? Color.red : Color.secondary)
                .lineLimit(1)
            }
        }
        .padding(.leading, 8)
        .padding(.trailing, 4)
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background {
            RoundedRectangle(cornerRadius: 6, style: .continuous)
                .fill(c.opacity(grey ? 0.07 : 0.15))
        }
        .overlay(alignment: .leading) {
            Capsule()
                .fill(c.opacity(grey ? 0.5 : 1))
                .frame(width: 3)
                .padding(.vertical, 3)
                .padding(.leading, 2)
        }
        .contentShape(Rectangle())
    }
}

/// (1.3.147) A day of the week's grid: each item at its time, the next one under it where they would meet (Canvas gives
/// an item no end, so each takes the room its name needs); the day as tall as its last item reaches.
private struct WeekDayStack: Layout {
    var gap: CGFloat = 2

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 100
        return CGSize(width: width, height: frames(subviews, width: width).last?.maxY ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (s, f) in zip(subviews, frames(subviews, width: bounds.width)) {
            s.place(at: CGPoint(x: bounds.minX, y: bounds.minY + f.minY), proposal: ProposedViewSize(width: bounds.width, height: f.height))
        }
    }

    private func frames(_ subviews: Subviews, width: CGFloat) -> [CGRect] {
        var out: [CGRect] = []
        for s in subviews {
            let h = s.sizeThatFits(ProposedViewSize(width: width, height: nil)).height
            let y = max(s[WeekAt.self], (out.last?.maxY ?? -gap) + gap)
            out.append(CGRect(x: 0, y: y, width: width, height: h))
        }
        return out
    }
}

/// An item of a day of the week's grid, and the minutes into the day it is at.
private struct WeekItem: Identifiable {
    let ev: CalEvent
    let at: Int
    var id: String { ev.id }
}

/// Where an item's time puts it in its day of the week's grid.
private struct WeekAt: LayoutValueKey {
    static let defaultValue: CGFloat = 0
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
                // (1.3.82: shorter where the screen has less room for the popover — a popover of one height only, kept
                // shorter by AppKit, is sized back up by SwiftUI pass after pass: ExternalToolsView's list)
                .frame(minHeight: min(84, listHeight), idealHeight: listHeight, maxHeight: listHeight)
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
