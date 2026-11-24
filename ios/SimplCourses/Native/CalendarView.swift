import SwiftUI

/// Calendar: a month with a dot per course on each day and the day picked listed under it, a week day
/// by day, or the next three weeks as a list — on the calendars chosen (Canvas shows ten at most).
/// (1.9 build 68) Find Appointment, by Today (Canvas only): the Mac's sheet on the phone (AppointmentsSheet). A time
/// reserved there shows on its day as yours, and a swipe gives it back.
struct CalendarView: View {
    @EnvironmentObject private var engine: Engine
    /// (1.9) Month, week or list: the one last chosen on this phone at once (kept beside the page's own, calView).
    @State private var view = UserDefaults.standard.string(forKey: CalendarView.viewKey) ?? "month"
    static let viewKey = "SimplCalView"
    @State private var anchor = CalendarView.monthStart(Date())
    @State private var weekStart = CalendarView.sunday(Date())
    @State private var selected = Calendar.current.startOfDay(for: Date())
    @State private var data: CalendarData?
    @State private var error: String?
    /// (1.9) The stretch of days the screen shows now (its saved answer's key).
    @State private var shownKey: String?
    @State private var viewKnown = false
    @State private var loading = false
    @State private var choosing = false
    /// (build 68) Find Appointment open; a time of yours being given back from its row, and what Canvas said if it
    /// would not.
    @State private var finding = false
    @State private var givingBack: CalEvent?
    @State private var problem: String?
    /// The way the last move went (the month slides in from that side): set a frame before the move itself,
    /// so the month leaving reads it as it leaves.
    @State private var dir = 1
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let cal = Calendar.current
    private static let dayKey: DateFormatter = { let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; return f }()
    static func monthStart(_ d: Date) -> Date { cal.date(from: cal.dateComponents([.year, .month], from: d)) ?? d }
    static func sunday(_ d: Date) -> Date {
        let s = cal.startOfDay(for: d)
        return cal.date(byAdding: .day, value: -(cal.component(.weekday, from: s) - 1), to: s) ?? s
    }

    var body: some View {
        List {
            Section {
                // (pushed from Today or To Do, its bar holds Back: the arrows sit by the view they move)
                HStack(spacing: 10) {
                    if view != "list" {
                        Button { shift(-1) } label: { Image(systemName: "chevron.left").frame(width: 34, height: 34) }
                            .accessibilityLabel("Previous")
                    }
                    Picker("View", selection: $view) {
                        Text("Week").tag("week")
                        Text("Month").tag("month")
                        Text("List").tag("list")
                    }
                    .pickerStyle(.segmented)
                    if view != "list" {
                        Button { shift(1) } label: { Image(systemName: "chevron.right").frame(width: 34, height: 34) }
                            .accessibilityLabel("Next")
                    }
                }
                .buttonStyle(.borderless)
                .font(.body.weight(.semibold))
                .listRowBackground(Color.clear)
                .listRowInsets(EdgeInsets())
            }
            if let notice = data?.notice ?? data?.error, !notice.isEmpty {
                Section { Label(notice, systemImage: "info.circle").foregroundStyle(.secondary) }
            }
            switch view {
            case "week": weekSections
            case "list": listSections
            default: monthSections
            }
        }
        .listStyle(.insetGrouped)
        .overlay { if data == nil { LoadState(error: error) { Task { await load() } } } }
        .navigationTitle(title)
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button("Today") {
                    Haptics.tap()
                    today()
                }
            }
            if !engine.onBrightspace {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Haptics.tap()
                        finding = true
                    } label: { Image(systemName: "person.badge.clock") }
                        .accessibilityLabel("Find Appointment")
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button { choosing = true } label: { Image(systemName: "calendar.badge.checkmark") }
                    .accessibilityLabel("Calendars")
            }
        }
        .refreshable { await load() }
        .task(id: engine.dataVersion) { // (1.9: Refresh reads it again too)
            if !viewKnown { // (the view chosen, asked once: a pick made here since is not undone)
                showKept() // (1.9: the days as they were, at once, while it is asked)
                if let v = try? await engine.call("calView", as: OK.self).view, v != view { view = v }
                viewKnown = true
            }
            await load()
            // (the simulator suite: -SimplOpen appointments)
            if LaunchOpen.take("appointments") != nil { finding = true }
        }
        .onChange(of: view) {
            UserDefaults.standard.set(view, forKey: CalendarView.viewKey)
            Haptics.select()
            Task {
                await engine.act("calView", ["view": view])
                await load()
            }
        }
        .sheet(isPresented: $choosing) {
            CalendarsSheet(choices: data?.calendars ?? []) { Task { await load() } }
                .environmentObject(engine)
        }
        .sheet(isPresented: $finding) {
            AppointmentsSheet { Task { await load() } }
                .environmentObject(engine)
        }
        .confirmationDialog(givingBack.map { "Give back \($0.title)?" } ?? "",
                            isPresented: Binding(get: { givingBack != nil }, set: { if !$0 { givingBack = nil } }),
                            titleVisibility: .visible, presenting: givingBack) { ev in
            Button("Give Back", role: .destructive) { Task { await giveBack(ev) } }
            Button("Keep It", role: .cancel) {}
        } message: { ev in
            Text([ev.time, "The time goes back for someone else to take."].compactMap { $0 }.joined(separator: " · "))
        }
        .alert("Not given back", isPresented: Binding(get: { problem != nil }, set: { if !$0 { problem = nil } })) {
            Button("OK", role: .cancel) {}
        } message: { Text(problem ?? "") }
    }

    /// (build 68) A time of yours given back, from its row's swipe.
    private func giveBack(_ ev: CalEvent) async {
        guard let id = ev.reservationId else { return }
        let r = try? await engine.call("cancelReservation", ["id": id], as: ActReply.self)
        if r?.ok == true {
            Haptics.success()
            await load()
        } else {
            Haptics.error()
            problem = "\(engine.lmsName) would not give it back: \(r?.error ?? "no answer")."
        }
    }

    private var title: String {
        let f = DateFormatter()
        switch view {
        case "list": return "Upcoming"
        case "week":
            f.dateFormat = "MMM d"
            let end = Self.cal.date(byAdding: .day, value: 6, to: weekStart) ?? weekStart
            return "\(f.string(from: weekStart)) – \(Self.cal.component(.day, from: end))"
        default:
            f.dateFormat = Self.cal.component(.year, from: anchor) == Self.cal.component(.year, from: Date()) ? "MMMM" : "MMMM yyyy"
            return f.string(from: anchor)
        }
    }

    /// (build 70) What the calendar asks for as it opens on today, in the view last chosen — read ahead (ReadAhead).
    static func readAheadArgs() -> [String: Any] {
        let view = UserDefaults.standard.string(forKey: viewKey) ?? "month"
        let now = Date()
        let s: Date, e: Date
        switch view {
        case "week":
            s = sunday(now)
            e = cal.date(byAdding: .day, value: 7, to: s) ?? s
        case "list":
            s = cal.startOfDay(for: now)
            e = cal.date(byAdding: .day, value: 21, to: s) ?? s
        default:
            s = sunday(monthStart(now))
            e = cal.date(byAdding: .day, value: 42, to: s) ?? s
        }
        let iso = ISO8601DateFormatter()
        return ["from": iso.string(from: s), "to": iso.string(from: e)]
    }

    private var range: (Date, Date) {
        switch view {
        case "week": return (weekStart, Self.cal.date(byAdding: .day, value: 7, to: weekStart)!)
        case "list":
            let s = Self.cal.startOfDay(for: Date())
            return (s, Self.cal.date(byAdding: .day, value: 21, to: s)!)
        default:
            let s = Self.sunday(anchor)
            return (s, Self.cal.date(byAdding: .day, value: 42, to: s)!)
        }
    }

    private func shift(_ step: Int) {
        Haptics.select()
        Task {
            if dir != step {
                dir = step
                try? await Task.sleep(nanoseconds: 20_000_000)
            }
            withAnimation(.snappy) {
                if view == "month" { anchor = Self.cal.date(byAdding: .month, value: step, to: anchor) ?? anchor }
                else { weekStart = Self.cal.date(byAdding: .day, value: 7 * step, to: weekStart) ?? weekStart }
            }
            await load()
        }
    }

    /// Back to today as an arrow moves (1.7.5): the month (or week) slides in from the side today lies on, and
    /// the day picked moves on the spring a tap does. Already there, only the day picked moves.
    private func today() {
        let now = Date()
        let month = Self.monthStart(now)
        let week = Self.sunday(now)
        let from = view == "week" ? weekStart : anchor
        let to = view == "week" ? week : month
        let step = to > from ? 1 : to < from ? -1 : dir
        Task {
            if dir != step {
                dir = step
                try? await Task.sleep(nanoseconds: 20_000_000)
            }
            withAnimation(.snappy) {
                anchor = month
                weekStart = week
                selected = Self.cal.startOfDay(for: now)
            }
            await load()
        }
    }

    private func rangeArgs(_ s: Date, _ e: Date) -> [String: Any] {
        let iso = ISO8601DateFormatter()
        return ["from": iso.string(from: s), "to": iso.string(from: e)]
    }

    /// (1.9) A stretch of days shown before comes up at once as it was (another month: in place of the last one's).
    private func showKept() {
        let (s, e) = range
        let args = rangeArgs(s, e)
        let key = SavedAnswers.key("calendar", args)
        if shownKey != key, let kept = engine.saved("calendar", args, as: CalendarData.self) {
            data = kept
            error = nil
        }
        shownKey = key
    }

    private func load() async {
        let (s, e) = range
        loading = true
        defer { loading = false }
        let args = rangeArgs(s, e)
        showKept()
        do {
            let d = try await engine.fetch("calendar", args, as: CalendarData.self)
            withAnimation(.snappy) { data = d }
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    private func events(on day: Date) -> [CalEvent] {
        let key = Self.dayKey.string(from: day)
        return (data?.events ?? []).filter { $0.day == key }
    }

    // MARK: - Month

    @ViewBuilder
    private var monthSections: some View {
        Section {
            let start = Self.sunday(anchor)
            let days = (0..<42).compactMap { Self.cal.date(byAdding: .day, value: $0, to: start) }
            VStack(spacing: 6) {
                HStack {
                    ForEach(["S", "M", "T", "W", "T", "F", "S"].indices, id: \.self) { i in
                        Text(["S", "M", "T", "W", "T", "F", "S"][i]).font(.caption2.weight(.semibold)).foregroundStyle(.secondary).frame(maxWidth: .infinity)
                    }
                }
                ZStack {
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 0), count: 7), spacing: 4) {
                        ForEach(days, id: \.self) { day in dayCell(day) }
                    }
                    .id(anchor) // (a new month is a new grid: it slides in from the side the arrow points to)
                    .transition(reduceMotion ? .opacity : .push(from: dir > 0 ? .trailing : .leading))
                }
                .clipped()
            }
            .padding(.vertical, 6)
        }
        dayList(selected)
    }

    private func dayCell(_ day: Date) -> some View {
        let off = Self.cal.component(.month, from: day) != Self.cal.component(.month, from: anchor)
        let isToday = Self.cal.isDateInToday(day)
        let isSel = Self.cal.isDate(day, inSameDayAs: selected)
        let colors = Array(Set(events(on: day).compactMap { $0.color })).sorted().prefix(3)
        return Button {
            Haptics.select()
            withAnimation(.snappy) { selected = day }
        } label: {
            VStack(spacing: 3) {
                Text("\(Self.cal.component(.day, from: day))")
                    .font(.callout.weight(isToday || isSel ? .bold : .regular))
                    .foregroundStyle(isSel ? Color.white : isToday ? Color.accentColor : off ? Color.secondary : Color.primary)
                    .frame(width: 32, height: 32)
                    .background(Circle().fill(isSel ? (isToday ? Color.accentColor : Color.primary) : Color.clear))
                HStack(spacing: 2) {
                    ForEach(Array(colors), id: \.self) { c in Circle().fill(Color(hex: c)).frame(width: 5, height: 5) }
                }
                .frame(height: 5)
            }
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(Self.dayKey.string(from: day))
    }

    // MARK: - Week and list

    @ViewBuilder
    private var weekSections: some View {
        ForEach(0..<7, id: \.self) { i in
            if let day = Self.cal.date(byAdding: .day, value: i, to: weekStart) { dayList(day) }
        }
    }

    @ViewBuilder
    private var listSections: some View {
        let byDay = Dictionary(grouping: data?.events ?? [], by: { $0.day })
        let keys = byDay.keys.sorted()
        if keys.isEmpty, data != nil {
            Section { Text("Nothing in the next three weeks.").foregroundStyle(.secondary) }
        }
        ForEach(keys, id: \.self) { k in
            if let day = Self.dayKey.date(from: k) { dayList(day) }
        }
    }

    private func dayList(_ day: Date) -> some View {
        let evs = events(on: day)
        let f = DateFormatter()
        f.dateFormat = "EEEE d"
        let rel = Self.cal.isDateInToday(day) ? "Today · " : Self.cal.isDateInTomorrow(day) ? "Tomorrow · " : ""
        return Section {
            if evs.isEmpty { Text("Nothing on this day.").foregroundStyle(.secondary) }
            ForEach(evs) { ev in eventRow(ev) }
        } header: {
            HStack {
                Text(f.string(from: day))
                Spacer()
                Text("\(rel)\(evs.count) \(evs.count == 1 ? "item" : "items")").textCase(nil)
            }
        }
    }

    private func eventRow(_ ev: CalEvent) -> some View {
        Button {
            if let u = ev.url { Haptics.tap(); engine.openWeb(u, title: ev.title) }
        } label: {
            HStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 2).fill(Color(hex: ev.color)).frame(width: 4, height: 36)
                VStack(alignment: .leading, spacing: 2) {
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text(ev.title).lineLimit(2)
                            .strikethrough(ev.done == true && ev.missing != true, color: .secondary)
                            .foregroundStyle(ev.done == true ? .secondary : .primary)
                        // (build 68: a time reserved off Find Appointment)
                        if ev.reservationId != nil {
                            Text("Yours")
                                .font(.caption2.weight(.semibold))
                                .foregroundStyle(.green)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 1)
                                .background(Color.green.opacity(0.15), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                        }
                    }
                    if let sub = ev.sub, !sub.isEmpty { Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(1) }
                }
                Spacer(minLength: 6)
                if ev.missing == true { FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad")) }
                else if ev.excused == true { FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted")) }
                Text(ev.time ?? "").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }
        }
        .buttonStyle(.plain)
        .swipeActions(edge: .trailing) {
            if ev.reservationId != nil {
                Button { givingBack = ev } label: { Label("Give Back", systemImage: "arrow.uturn.backward") }
                    .tint(.red)
            }
        }
    }
}

/// The calendars shown: your courses on, the rest off until turned on (ten at most, as Canvas allows).
struct CalendarsSheet: View {
    let choices: [CalendarChoice]
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var on: Set<String> = []
    @State private var tooMany = false

    var body: some View {
        NavigationStack {
            List {
                Section {
                    ForEach(choices.filter { $0.own == true }) { toggleRow($0) }
                } footer: {
                    Text(engine.onBrightspace ? "At most 10 calendars show at once." : "Canvas shows at most 10 calendars at once.")
                }
                let other = choices.filter { $0.own != true }
                if !other.isEmpty {
                    Section("Other calendars") {
                        ForEach(other) { toggleRow($0) }
                    }
                }
            }
            .navigationTitle("Calendars")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } }
            }
            .alert("Ten calendars at most", isPresented: $tooMany) { Button("OK", role: .cancel) {} } message: { Text("Turn one off first.") }
        }
        .presentationDetents([.medium, .large])
        .onAppear { on = Set(choices.filter { $0.on }.map { $0.code }) }
    }

    private func toggleRow(_ c: CalendarChoice) -> some View {
        Toggle(isOn: Binding(get: { on.contains(c.code) }, set: { v in set(c, v) })) {
            Label {
                Text(c.name)
            } icon: {
                Circle().fill(Color(hex: c.color)).frame(width: 12, height: 12)
            }
        }
        .tint(Color(hex: c.color))
    }

    private func set(_ c: CalendarChoice, _ value: Bool) {
        if value && on.count >= 10 {
            Haptics.error()
            tooMany = true
            return
        }
        Haptics.select()
        if value { on.insert(c.code) } else { on.remove(c.code) }
        let codes = choices.map { $0.code }.filter { on.contains($0) }
        Task {
            await engine.act("setCalendars", ["codes": codes])
            changed()
        }
    }
}

// MARK: - Find Appointment

/// (1.9 build 68) Find Appointment on the phone, as the Mac's popup (mac/Simpl/Screens/Appointments.swift) and the web's
/// sheet have it: Canvas's Scheduler — office hours, conferences, any times a teacher opened — read through the page
/// (native-app.js `appointments`). The class first, as chips; then each group as a card: its place, how long a time is
/// and how many each student may hold, the time held (Give Back) and its days, each opening to its times in a grid. A
/// tap on an open time asks first (a stray tap reserves nothing), then reserves it; Canvas's own words when it will not.
/// A time reserved goes on the calendar (`changed`).
struct AppointmentsSheet: View {
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var data: AppointmentsData?
    @State private var course: String?
    /// The days opened to their times (a day key each): the day held, else the first with a time open, to begin with.
    @State private var open: Set<String> = []
    /// The time (or reservation) Canvas is being asked about.
    @State private var busy: String?
    @State private var said: String?
    @State private var saidBad = false
    /// The time tapped, asked about before it is reserved; the time of yours about to be given back.
    @State private var asking: Ask?
    @State private var givingBack: AppointmentHeld?

    struct Ask: Identifiable {
        let group: AppointmentGroup
        let day: AppointmentDay
        let slot: AppointmentSlot
        var id: String { slot.id }
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                content
                    .padding(.horizontal, 16)
                    .padding(.bottom, 28)
            }
            .background(Color(.systemGroupedBackground))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .principal) {
                    VStack(spacing: 1) {
                        Text("Find Appointment").font(.headline)
                        Text("Office hours and sign-ups").font(.caption).foregroundStyle(.secondary)
                    }
                    .accessibilityElement(children: .combine)
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button { dismiss() } label: { Image(systemName: "xmark") }
                        .accessibilityLabel("Close")
                }
            }
            .refreshable { await load() }
        }
        .task { await load() }
        .confirmationDialog(asking.map { "Reserve \($0.day.label) at \($0.slot.time)?" } ?? "",
                            isPresented: Binding(get: { asking != nil }, set: { if !$0 { asking = nil } }),
                            titleVisibility: .visible, presenting: asking) { a in
            Button("Reserve") { Task { await reserve(a) } }
            Button("Cancel", role: .cancel) {}
        } message: { a in
            Text(([a.group.title] + Self.meta(a.group, short: true)).joined(separator: " · "))
        }
        .confirmationDialog(givingBack.map { "Give back \($0.when)?" } ?? "",
                            isPresented: Binding(get: { givingBack != nil }, set: { if !$0 { givingBack = nil } }),
                            titleVisibility: .visible, presenting: givingBack) { held in
            Button("Give Back", role: .destructive) { Task { await giveBack(held) } }
            Button("Keep It", role: .cancel) {}
        } message: { _ in
            Text("The time goes back for someone else to take.")
        }
    }

    @ViewBuilder
    private var content: some View {
        if let d = data {
            VStack(alignment: .leading, spacing: 12) {
                if let said { saidLine(said, bad: saidBad) }
                if let e = d.error { saidLine(e, bad: true) }
                if d.groups.isEmpty {
                    if d.error == nil { empty }
                } else {
                    if d.classes.count > 1 { classes(d.classes) }
                    ForEach(shown(d)) { g in card(g) }
                    Text("A time you reserve goes on your calendar.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 6)
                }
            }
            .padding(.top, 8)
        } else {
            ProgressView()
                .frame(maxWidth: .infinity, minHeight: 320)
        }
    }

    private var empty: some View {
        VStack(spacing: 8) {
            Image(systemName: "calendar.badge.checkmark")
                .font(.system(size: 34))
                .foregroundStyle(.tertiary)
            Text("Nothing to sign up for right now")
                .font(.headline)
            Text("Office hours and conferences your teachers open will wait here.")
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 60)
        .padding(.horizontal, 20)
    }

    private func saidLine(_ text: String, bad: Bool) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Image(systemName: bad ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                .foregroundStyle(bad ? Color.orange : Color.green)
            Text(text)
                .font(.subheadline)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .background((bad ? Color.orange : Color.green).opacity(0.14), in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .transition(.opacity)
    }

    /// The class first, as Canvas asks: one chip a course with something to sign up for.
    private func classes(_ list: [AppointmentClass]) -> some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(list) { c in
                    let on = c.code == pickedClass
                    Button {
                        Haptics.select()
                        withAnimation(reduceMotion ? nil : .snappy) { course = c.code }
                    } label: {
                        HStack(spacing: 7) {
                            Circle().fill(Color(hex: c.color)).frame(width: 8, height: 8)
                            Text(c.name).lineLimit(1)
                        }
                        .font(.subheadline.weight(on ? .semibold : .medium))
                        .foregroundStyle(on ? Color.accentColor : Color.primary)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 8)
                        .background(Capsule().fill(on ? Color.accentColor.opacity(0.15) : Color(.tertiarySystemFill)))
                        .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
        }
        .scrollClipDisabled()
    }

    private var pickedClass: String? {
        guard let d = data else { return nil }
        if let course, d.classes.contains(where: { $0.code == course }) { return course }
        return d.classes.first?.code
    }

    private func shown(_ d: AppointmentsData) -> [AppointmentGroup] {
        guard let code = pickedClass else { return d.groups }
        return d.groups.filter { $0.codes.contains(code) }
    }

    /// Where it is, how long a time is, and how many each may hold ("Online · 15 min each · one time each").
    static func meta(_ g: AppointmentGroup, short: Bool = false) -> [String] {
        [
            g.location.flatMap { $0.isEmpty ? nil : $0 },
            (g.minutes ?? 0) > 0 ? (short ? "\(g.minutes ?? 0) min" : "\(g.minutes ?? 0) min each") : nil,
            short ? nil : (g.max ?? 0) > 0 ? (g.max == 1 ? "one time each" : "\(g.max ?? 0) times each") : nil,
        ].compactMap { $0 }
    }

    private func card(_ g: AppointmentGroup) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .fill(Color(hex: g.color))
                        .frame(width: 11, height: 11)
                    Text(g.title)
                        .font(.headline)
                        .fixedSize(horizontal: false, vertical: true)
                }
                let m = Self.meta(g)
                if !m.isEmpty {
                    Text(m.joined(separator: " · "))
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .padding(.leading, 21)
                }
            }
            if let desc = g.description, !desc.isEmpty {
                Text(desc)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(4)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if g.groupSignup == true {
                Text("A sign-up for your group: reserve it as the group in \(engine.lmsName).")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(g.url ?? "/calendar", title: g.title) }
                    .buttonStyle(.bordered)
            } else {
                ForEach(g.mine) { held in heldRow(held) }
                VStack(spacing: 0) {
                    ForEach(Array(g.days.enumerated()), id: \.element.key) { i, day in
                        if i > 0 { Divider().padding(.leading, 14) }
                        dayRow(g, day)
                    }
                }
                .background(Color(.tertiarySystemFill), in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(.secondarySystemGroupedBackground), in: RoundedRectangle(cornerRadius: 26, style: .continuous))
    }

    private func heldRow(_ held: AppointmentHeld) -> some View {
        HStack(spacing: 10) {
            Image(systemName: "checkmark.circle.fill")
                .font(.title3)
                .foregroundStyle(Color.green)
            VStack(alignment: .leading, spacing: 1) {
                Text("Your time").font(.subheadline.weight(.medium))
                Text(held.when).font(.footnote).foregroundStyle(.secondary)
            }
            Spacer(minLength: 8)
            if busy != nil, busy == held.reservationId { ProgressView() }
            Button("Give Back") { givingBack = held }
                .buttonStyle(.bordered)
                .tint(.red)
                .disabled(busy != nil)
        }
        .padding(.leading, 14)
        .padding(.trailing, 10)
        .padding(.vertical, 10)
        .background(Color.green.opacity(0.14), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
    }

    private func dayRow(_ g: AppointmentGroup, _ day: AppointmentDay) -> some View {
        let isOpen = open.contains(day.key)
        return VStack(alignment: .leading, spacing: 12) {
            Button {
                Haptics.select()
                withAnimation(reduceMotion ? nil : .snappy) {
                    if isOpen { open.remove(day.key) } else { open.insert(day.key) }
                }
            } label: {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(day.label)
                        .font(.body.weight(.semibold))
                    Text(day.sub)
                        .font(.subheadline)
                        .foregroundStyle(day.held == true ? Color.green : Color.secondary)
                        .lineLimit(1)
                    Spacer(minLength: 0)
                    Image(systemName: "chevron.right")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(.tertiary)
                        .rotationEffect(.degrees(isOpen ? 90 : 0))
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityValue(isOpen ? "Open" : "Closed")
            if isOpen {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                    ForEach(day.slots) { s in slotButton(g, day, s) }
                }
                .transition(.opacity)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
    }

    private func slotButton(_ g: AppointmentGroup, _ day: AppointmentDay, _ s: AppointmentSlot) -> some View {
        let full = g.full == true
        let canPick = s.open && !s.reserved && !full && busy == nil
        let taken = !s.open && !s.reserved
        return Button {
            Haptics.tap()
            asking = Ask(group: g, day: day, slot: s)
        } label: {
            HStack(spacing: 4) {
                if s.reserved {
                    Image(systemName: "checkmark").font(.caption.weight(.bold))
                }
                Text(s.time).strikethrough(taken)
                if busy == s.id { ProgressView().controlSize(.mini) }
            }
            .font(.subheadline.weight(.semibold).monospacedDigit())
            .lineLimit(1)
            .minimumScaleFactor(0.75)
            .foregroundStyle(s.reserved ? Color.white : canPick ? Color.accentColor : Color.secondary)
            .frame(maxWidth: .infinity, minHeight: 40)
            .background {
                if s.reserved { Capsule().fill(Color.green) }
                else if canPick { Capsule().fill(Color.accentColor.opacity(0.14)) }
                else if taken { Capsule().strokeBorder(Color(.separator), lineWidth: 1) }
                else { Capsule().fill(Color(.quaternarySystemFill)) }
            }
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .disabled(!canPick)
        .accessibilityLabel(Text("\(day.label), \(s.time), \(s.reserved ? "your time" : s.open ? (full ? "you hold the most times this allows" : "open") : "taken")"))
    }

    // MARK: - Asking Canvas

    private func load() async {
        let got = (try? await engine.call("appointments", ["force": true], as: AppointmentsData.self))
            ?? AppointmentsData(groups: [], classes: [], error: "Appointments could not be loaded.")
        withAnimation(reduceMotion ? nil : .snappy) {
            data = got
            for g in got.groups where !g.days.contains(where: { open.contains($0.key) }) {
                if let pick = g.days.first(where: { $0.held == true }) ?? g.days.first(where: { ($0.open ?? 0) > 0 }) ?? g.days.first {
                    open.insert(pick.key)
                }
            }
        }
    }

    private func reserve(_ a: Ask) async {
        busy = a.slot.id
        let r = try? await engine.call("reserveAppointment", ["id": a.slot.id], as: ActReply.self)
        busy = nil
        if r?.ok == true {
            Haptics.success()
            say("Reserved: \(a.day.label) at \(a.slot.time). It’s on your calendar.", bad: false)
            changed()
        } else {
            Haptics.error()
            say("\(engine.lmsName) would not reserve that time: \(r?.error ?? "no answer").", bad: true)
        }
        await load()
    }

    private func giveBack(_ held: AppointmentHeld) async {
        guard let id = held.reservationId else {
            // (held, Canvas says, but not by which event: its own page can undo it)
            engine.openWebScreen("/calendar", title: "Calendar")
            return
        }
        busy = id
        let r = try? await engine.call("cancelReservation", ["id": id], as: ActReply.self)
        busy = nil
        if r?.ok == true {
            Haptics.success()
            say("Your time is given back.", bad: false)
            changed()
        } else {
            Haptics.error()
            say("\(engine.lmsName) would not give it back: \(r?.error ?? "no answer").", bad: true)
        }
        await load()
    }

    private func say(_ text: String, bad: Bool) {
        withAnimation(reduceMotion ? nil : .snappy) {
            said = text
            saidBad = bad
        }
    }
}
