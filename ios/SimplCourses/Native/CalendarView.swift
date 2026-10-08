import SwiftUI

/// Calendar: a month with a dot per course on each day and the day picked listed under it, a week day
/// by day, or the next three weeks as a list — on the calendars chosen (Canvas shows ten at most).
struct CalendarView: View {
    @EnvironmentObject private var engine: Engine
    @State private var view = "month"
    @State private var anchor = CalendarView.monthStart(Date())
    @State private var weekStart = CalendarView.sunday(Date())
    @State private var selected = Calendar.current.startOfDay(for: Date())
    @State private var data: CalendarData?
    @State private var error: String?
    @State private var loading = false
    @State private var choosing = false
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
                    anchor = Self.monthStart(Date())
                    weekStart = Self.sunday(Date())
                    selected = Self.cal.startOfDay(for: Date())
                    Task { await load() }
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button { choosing = true } label: { Image(systemName: "calendar.badge.checkmark") }
                    .accessibilityLabel("Calendars")
            }
        }
        .refreshable { await load() }
        .task {
            if let v = try? await engine.call("calView", as: OK.self).view { view = v }
            await load()
        }
        .onChange(of: view) {
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

    private func load() async {
        let (s, e) = range
        let iso = ISO8601DateFormatter()
        loading = true
        defer { loading = false }
        do {
            let d = try await engine.call("calendar", ["from": iso.string(from: s), "to": iso.string(from: e)], as: CalendarData.self)
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
                    Text(ev.title).lineLimit(2)
                        .strikethrough(ev.done == true && ev.missing != true, color: .secondary)
                        .foregroundStyle(ev.done == true ? .secondary : .primary)
                    if let sub = ev.sub, !sub.isEmpty { Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(1) }
                }
                Spacer(minLength: 6)
                if ev.missing == true { FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad")) }
                else if ev.excused == true { FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted")) }
                Text(ev.time ?? "").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
            }
        }
        .buttonStyle(.plain)
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
