import AppKit
import SwiftUI

/// Calendar: a month as a calendar's grid — each day's work and events on it in their calendar's colour, today marked —
/// with the day picked listed beside it; a week day by day; or the next three weeks as a list. On the calendars chosen
/// (Canvas shows ten at most). ‹ and › — or ← and → once the calendar has been clicked — move a month or a week, sliding
/// the way they go; Today comes back.
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
                    heading
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
            } else {
                LoadState(error: error) { Task { await load(animated: false) } }
            }
        }
        .navigationTitle("Calendar")
        .navigationSubtitle(periodTitle)
        .toolbar { toolbarItems }
        .task(id: engine.dataVersion) {
            if !modeRead {
                modeRead = true
                if let v = try? await engine.call("calView", as: OK.self).view, Self.modes.contains(v) { mode = v }
            }
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
            ControlGroup {
                Button {
                    shift(-1)
                } label: {
                    Label(mode == "week" ? "Previous Week" : "Previous Month", systemImage: "chevron.left")
                }
                .disabled(mode == "list")
                .help(mode == "week" ? "Previous week (←)" : "Previous month (←)")
                Button("Today") { goToday() }
                    .keyboardShortcut("t", modifiers: .command)
                    .help("Today (⌘T)")
                Button {
                    shift(1)
                } label: {
                    Label(mode == "week" ? "Next Week" : "Next Month", systemImage: "chevron.right")
                }
                .disabled(mode == "list")
                .help(mode == "week" ? "Next week (→)" : "Next month (→)")
            }
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

    private var heading: some View {
        HStack(alignment: .center, spacing: 12) {
            ScreenHeading(title: periodTitle, sub: mode == "list" ? "The next three weeks" : nil)
            Spacer(minLength: 8)
            if loading {
                ProgressView()
                    .controlSize(.small)
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: loading)
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
                .font(.callout)
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
            Spacer(minLength: 8)
            if problem {
                Button("Try Again") { Task { await load(animated: true) } }
            } else {
                Button("Choose Calendars…") { choosing = true }
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .card(radius: 12)
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

    /// The month's grid with the day picked beside it on a wide window, under it on a narrow one.
    private func monthView(_ byDay: [String: [CalEvent]]) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 18) {
                monthGrid(byDay)
                    .frame(minWidth: 560, idealWidth: 560, maxWidth: .infinity)
                dayCard(selected, events: byDay[key(selected)] ?? [])
                    .frame(width: 320)
            }
            VStack(alignment: .leading, spacing: 18) {
                monthGrid(byDay)
                dayCard(selected, events: byDay[key(selected)] ?? [])
            }
        }
    }

    private func monthGrid(_ byDay: [String: [CalEvent]]) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ForEach(Array(weekdaySymbols.enumerated()), id: \.offset) { _, s in
                    Text(s.uppercased())
                        .font(.system(size: 11, weight: .semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .trailing)
                        .padding(.trailing, 10)
                }
            }
            .padding(.vertical, 8)
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

    /// Six weeks from the first day of the week the month starts in: the grid is always as tall.
    private func monthWeeks(_ byDay: [String: [CalEvent]]) -> some View {
        let first = Self.firstOfWeek(anchor)
        let days = (0..<42).compactMap { cal.date(byAdding: .day, value: $0, to: first) }
        return VStack(spacing: 0) {
            ForEach(0..<6, id: \.self) { w in
                if w > 0 { Self.line.frame(height: 1) }
                HStack(spacing: 0) {
                    ForEach(Array(days.dropFirst(w * 7).prefix(7).enumerated()), id: \.element) { i, day in
                        dayCell(day, events: byDay[key(day)] ?? [])
                            .overlay(alignment: .leading) {
                                if i > 0 { Self.line.frame(width: 1) }
                            }
                    }
                }
            }
        }
    }

    private func dayCell(_ day: Date, events evs: [CalEvent]) -> some View {
        let off = !cal.isDate(day, equalTo: anchor, toGranularity: .month)
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        let fits = evs.count > 3 ? 2 : evs.count
        return Button {
            pick(day)
        } label: {
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Spacer(minLength: 0)
                    Text("\(cal.component(.day, from: day))")
                        .font(.callout.weight(isToday ? .semibold : .regular).monospacedDigit())
                        .foregroundStyle(isToday ? Color.white : (off ? Color.secondary : Color.primary))
                        .frame(minWidth: 24, minHeight: 24)
                        .background {
                            if isToday { Circle().fill(Color.accentColor) }
                        }
                }
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(evs.prefix(fits)) { ev in CalendarChip(event: ev) }
                    if evs.count > fits {
                        Text("\(evs.count - fits) more")
                            .font(.caption2.weight(.medium))
                            .foregroundStyle(.secondary)
                            .padding(.leading, 6)
                    }
                }
                .opacity(off ? 0.6 : 1)
                Spacer(minLength: 0)
            }
            .padding(5)
            .frame(maxWidth: .infinity, minHeight: 96, maxHeight: 96, alignment: .topLeading)
            .background {
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(Color.accentColor.opacity(isSel ? 0.12 : 0))
                    .overlay {
                        RoundedRectangle(cornerRadius: 9, style: .continuous)
                            .strokeBorder(Color.accentColor.opacity(isSel ? 0.6 : 0), lineWidth: 1.5)
                    }
                    .padding(2)
            }
            .animation(Motion.snappy, value: isSel)
        }
        .buttonStyle(RowButtonStyle(radius: 9))
        .accessibilityLabel(Self.spoken(day, count: evs.count))
        .accessibilityAddTraits(isSel ? .isSelected : [])
    }

    private static func spoken(_ day: Date, count: Int) -> String {
        let d = day.formatted(.dateTime.weekday(.wide).month(.wide).day())
        return count == 0 ? "\(d), nothing" : "\(d), \(count) \(count == 1 ? "item" : "items")"
    }

    // MARK: A day

    /// A day's work and events, each opening where it lives.
    private func dayCard(_ day: Date, events evs: [CalEvent]) -> some View {
        CardSection(title: day.formatted(.dateTime.weekday(.wide).month(.wide).day()), trailing: countLine(day, evs.count)) {
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
    }

    /// "Today · 3 items", "Tomorrow · 1 item", "2 items".
    private func countLine(_ day: Date, _ n: Int) -> String {
        let rel = cal.isDateInToday(day) ? "Today · " : cal.isDateInTomorrow(day) ? "Tomorrow · " : ""
        return "\(rel)\(n) \(n == 1 ? "item" : "items")"
    }

    @ViewBuilder
    private func eventRow(_ ev: CalEvent) -> some View {
        if let url = ev.url, !url.isEmpty {
            RowLink {
                engine.openWeb(url, title: ev.title)
            } label: {
                CalendarEventLine(event: ev)
            }
            .contextMenu { eventMenu(ev, url: url) }
        } else {
            CalendarEventLine(event: ev)
                .padding(.horizontal, 8)
                .padding(.vertical, 7)
        }
    }

    @ViewBuilder
    private func eventMenu(_ ev: CalEvent, url: String) -> some View {
        Button("Open") { engine.openWeb(url, title: ev.title) }
        Divider()
        Button("Open in Canvas") { engine.openWebScreen(url, title: ev.title) }
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
                VStack(spacing: 2) {
                    Text(day.formatted(.dateTime.weekday(.abbreviated)).uppercased())
                        .font(.system(size: 11, weight: .semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                    Text("\(cal.component(.day, from: day))")
                        .font(.title3.weight(isToday ? .bold : .medium).monospacedDigit())
                        .foregroundStyle(isToday ? Color.white : Color.primary)
                        .frame(width: 32, height: 32)
                        .background {
                            if isToday { Circle().fill(Color.accentColor) }
                        }
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
                .background {
                    RoundedRectangle(cornerRadius: 9, style: .continuous)
                        .fill(Color.accentColor.opacity(isSel ? 0.12 : 0))
                }
                .animation(Motion.snappy, value: isSel)
            }
            .buttonStyle(RowButtonStyle(radius: 9))
            .accessibilityLabel(Self.spoken(day, count: evs.count))
            .accessibilityAddTraits(isSel ? .isSelected : [])
            ForEach(evs) { ev in weekBlock(ev) }
        }
        .padding(6)
        .frame(maxWidth: .infinity, minHeight: 320, maxHeight: .infinity, alignment: .top)
    }

    @ViewBuilder
    private func weekBlock(_ ev: CalEvent) -> some View {
        if let url = ev.url, !url.isEmpty {
            Button {
                engine.openWeb(url, title: ev.title)
            } label: {
                CalendarWeekBlock(event: ev)
            }
            .buttonStyle(RowButtonStyle(radius: 8))
            .contextMenu { eventMenu(ev, url: url) }
            .help(ev.sub ?? ev.title)
        } else {
            CalendarWeekBlock(event: ev)
        }
    }

    // MARK: List

    /// The next three weeks, a card for each day with something on it.
    private func listView(_ byDay: [String: [CalEvent]]) -> some View {
        let keys = byDay.keys.sorted()
        return VStack(alignment: .leading, spacing: 18) {
            if keys.isEmpty {
                EmptyNote(text: "Nothing in the next three weeks.", symbol: "calendar")
                    .padding(.horizontal, 20)
                    .padding(.vertical, 6)
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

    private func load(animated: Bool) async {
        let (s, e) = range
        let ask = "\(mode)|\(Int(s.timeIntervalSince1970))|\(Int(e.timeIntervalSince1970))"
        asked = ask
        loading = true
        let iso = ISO8601DateFormatter()
        do {
            let d = try await engine.call("calendar", ["from": iso.string(from: s), "to": iso.string(from: e)], as: CalendarData.self)
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

/// A piece of work or an event on a day of the month: a capsule in its calendar's colour with its name — a red mark
/// when it is missing, greyed once done (work handed in struck through).
private struct CalendarChip: View {
    let event: CalEvent

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        let done = event.done == true && !missing
        let work = ["Assignment", "Quiz", "Discussion"].contains(event.kind ?? "")
        HStack(spacing: 4) {
            if missing {
                Image(systemName: "exclamationmark.circle.fill")
                    .font(.system(size: 8, weight: .bold))
                    .foregroundStyle(.red)
            } else {
                Circle().fill(c).frame(width: 6, height: 6)
            }
            Text(event.title)
                .strikethrough(done && work, color: .secondary)
                .foregroundStyle(done ? Color.secondary : Color.primary)
                .lineLimit(1)
        }
        .font(.caption)
        .padding(.horizontal, 5)
        .padding(.vertical, 1.5)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background((missing ? Color.red : c).opacity(0.13), in: Capsule())
        .accessibilityHidden(true)
    }
}

/// A piece of work or an event in a day's list: its kind's icon in its calendar's colour, its name (struck through once
/// done), its course, kind, points and place, whether it is missing or excused, and its time.
private struct CalendarEventLine: View {
    let event: CalEvent

    var body: some View {
        let done = event.done == true
        let missing = event.missing == true
        HStack(spacing: 12) {
            IconTile(symbol: CalendarEventLine.glyph(event.kind), color: Color(hex: event.color))
            VStack(alignment: .leading, spacing: 2) {
                Text(event.title)
                    .strikethrough(done && !missing, color: .secondary)
                    .foregroundStyle(done ? Color.secondary : Color.primary)
                    .lineLimit(2)
                if let sub = event.sub, !sub.isEmpty {
                    Text(sub).font(.callout).foregroundStyle(.secondary).lineLimit(1)
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
                    .font(.callout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: 58, alignment: .trailing)
            }
        }
        .contentShape(Rectangle())
    }

    static func glyph(_ kind: String?) -> String {
        switch kind ?? "" {
        case "Assignment": return "doc.text"
        case "Quiz": return "checklist"
        case "Discussion": return "bubble.left.and.bubble.right"
        case "Appointment": return "person.2"
        default: return "calendar"
        }
    }
}

/// A piece of work or an event in a column of the week: its name on a wash of its calendar's colour with the colour's bar
/// at its edge, its time, and whether it is missing or excused.
private struct CalendarWeekBlock: View {
    let event: CalEvent

    var body: some View {
        let c = Color(hex: event.color)
        let done = event.done == true
        let missing = event.missing == true
        VStack(alignment: .leading, spacing: 3) {
            Text(event.title)
                .strikethrough(done && !missing, color: .secondary)
                .font(.callout.weight(.medium))
                .foregroundStyle(done ? Color.secondary : Color.primary)
                .lineLimit(3)
                .multilineTextAlignment(.leading)
            HStack(spacing: 5) {
                if let time = event.time, !time.isEmpty {
                    Text(time).font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                }
                if missing {
                    FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad"))
                } else if event.excused == true {
                    FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted"))
                }
            }
        }
        .padding(.leading, 10)
        .padding(.trailing, 6)
        .padding(.vertical, 6)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(c.opacity(0.1), in: RoundedRectangle(cornerRadius: 8, style: .continuous))
        .overlay(alignment: .leading) {
            Capsule()
                .fill(c)
                .frame(width: 3)
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
        VStack(alignment: .leading, spacing: 10) {
            Text("Calendars").font(.headline)
            if choices.isEmpty {
                Text("No calendars to show.").foregroundStyle(.secondary)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 7) {
                        ForEach(own) { row($0) }
                        if !other.isEmpty {
                            Text("Other Calendars")
                                .font(.caption.weight(.semibold))
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
            Text(full ? "Ten calendars at most: turn one off to show another." : "Canvas shows at most 10 calendars at once.")
                .font(.caption)
                .foregroundStyle(full ? Color.orange : Color.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .frame(width: 300)
        .animation(Motion.snappy, value: full)
        .onAppear { on = Set(choices.filter { $0.on }.map { $0.code }) }
    }

    private var listHeight: CGFloat {
        let rows = CGFloat(choices.count) * 24 + (other.isEmpty ? 0 : 30)
        return min(max(rows, 24), 380)
    }

    private func row(_ c: CalendarChoice) -> some View {
        Toggle(isOn: Binding(get: { on.contains(c.code) }, set: { set(c, $0) })) {
            HStack(spacing: 7) {
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .fill(Color(hex: c.color))
                    .frame(width: 12, height: 12)
                Text(c.name).lineLimit(1)
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
