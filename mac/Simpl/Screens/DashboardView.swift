import SwiftUI

/// The Dashboard: the day at a glance, as Simpl's web Today has it. Six counters (each opens what it counted in a
/// popover from its card), the day's work with its ticks, and the week's load per course.
struct DashboardView: View {
    @EnvironmentObject private var engine: Engine
    @StateObject private var model = Loader<Today>()
    @State private var counts: TodayCounts?
    @State private var open: String?
    @State private var sheetAsked = false

    var body: some View {
        Group {
            if let d = model.data {
                Page {
                    ScreenHeading(title: greeting(d), sub: d.dateLine)
                    counters(d)
                    ViewThatFits(in: .horizontal) {
                        HStack(alignment: .top, spacing: 18) {
                            dayList(d).frame(minWidth: 520)
                            if let load = d.load, d.hasCourses ?? true { weekLoad(load, idle: d.idle).frame(width: 330) }
                        }
                        VStack(alignment: .leading, spacing: 18) {
                            dayList(d)
                            if let load = d.load, d.hasCourses ?? true { weekLoad(load, idle: d.idle) }
                        }
                    }
                }
            } else {
                LoadState(error: model.error) { Task { await load() } }
            }
        }
        .navigationTitle("Dashboard")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button { engine.newTask = true } label: { Label("New Task", systemImage: "plus") }
                    .help("New Task (⌘N)")
            }
        }
        .task(id: engine.dataVersion) { await load() }
    }

    private func greeting(_ d: Today) -> String {
        let first = d.me?.name.split(separator: " ").first.map(String.init) ?? ""
        let hour = Calendar.current.component(.hour, from: Date())
        let part = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening"
        return first.isEmpty ? part : "\(part), \(first)"
    }

    // MARK: - Counters

    private func counters(_ d: Today) -> some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 14)], spacing: 14) {
            ForEach(d.counters) { c in counterTile(c) }
        }
    }

    private func counterTile(_ c: Counter) -> some View {
        let value: Int? = c.key == "overdue" ? counts?.overdue : c.key == "graded" ? counts?.graded : c.value
        let red = c.tone == "red" && (value ?? 0) > 0
        return Button {
            open = c.key
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                Text(c.label)
                    .font(.callout.weight(.medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                Group {
                    if let v = value {
                        Text("\(v)").contentTransition(.numericText(value: Double(v)))
                    } else {
                        Text("–").foregroundStyle(.tertiary)
                    }
                }
                .font(.system(size: 32, weight: .bold, design: .rounded))
                .foregroundStyle(red ? Color.red : Color.primary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 16)
            .padding(.vertical, 13)
        }
        .buttonStyle(CardButtonStyle(tint: red ? .red : nil))
        .animation(Motion.snappy, value: value)
        .help("Show \(c.label.lowercased())")
        .popover(isPresented: Binding(get: { open == c.key }, set: { if !$0 && open == c.key { open = nil } }), arrowEdge: .bottom) {
            CounterList(key: c.key) { url in
                open = nil
                engine.openWeb(url, title: "")
            }
            .environmentObject(engine)
        }
    }

    // MARK: - The day's work

    private func dayList(_ d: Today) -> some View {
        CardSection(title: d.list.heading, trailing: d.list.note) {
            if d.list.rows.isEmpty {
                EmptyNote(text: d.list.empty ?? "Nothing due.")
                    .padding(.horizontal, 8)
            }
            ForEach(Array(d.list.rows.enumerated()), id: \.element.id) { i, row in
                if i > 0 { RowDivider(inset: 42) }
                RowLink { openRow(row) } label: {
                    WorkRowView(row: row) { done in toggle(row, done) }
                }
                .workMenu(WorkAction(row), engine: engine)
                .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
    }

    // MARK: - The week's load

    private func weekLoad(_ load: [LoadRow], idle: Int?) -> some View {
        CardSection(title: "Week load") {
            VStack(alignment: .leading, spacing: 12) {
                if load.isEmpty {
                    EmptyNote(text: "Nothing assigned this week.", symbol: "sun.max")
                }
                ForEach(load) { r in
                    Button { engine.go(.section("courses/\(r.id)", "assignments")) } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            HStack {
                                Text(r.code).font(.callout.weight(.semibold)).lineLimit(1)
                                Spacer()
                                Text("\(r.done) of \(r.total)")
                                    .font(.callout.monospacedDigit())
                                    .foregroundStyle(.secondary)
                                    .contentTransition(.numericText(value: Double(r.done)))
                            }
                            LoadBar(fraction: r.total > 0 ? Double(r.done) / Double(r.total) : 0, color: Color(hex: r.color))
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help("\(r.code)’s assignments")
                }
                if let idle, idle > 0 {
                    Text("\(idle) \(idle == 1 ? "course" : "courses") with nothing assigned this week")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 4)
        }
    }

    // MARK: - Doing

    /// `animated`: after a tick here, the row ticked off leaves and the next one slides in (as To Do's do); any other
    /// load is a redraw, not an arrival.
    private func load(animated: Bool = false) async {
        await model.load(engine, "today", animated: animated)
        guard model.data != nil else { return }
        // (the screenshot suite: -SimplSheet next opens a counter's list)
        if let key = UserDefaults.standard.string(forKey: "SimplSheet"), !key.isEmpty, !sheetAsked {
            sheetAsked = true
            open = key
        }
        if let kept = try? await engine.call("todayCounts", ["kept": true], as: TodayCounts.self), kept.overdue != nil { counts = kept }
        if let fresh = try? await engine.call("todayCounts", ["kept": false], as: TodayCounts.self) {
            withAnimation(Motion.snappy) { counts = fresh }
        }
    }

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

    private func openRow(_ row: WorkRow) {
        guard let url = row.url else { return }
        engine.openWeb(url, title: row.title)
    }
}

/// A course's share of the week handed in: a bar in its colour that fills on the house spring, from where it was.
private struct LoadBar: View {
    let fraction: Double
    let color: Color
    @State private var shown = 0.0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(color.opacity(0.16))
                Capsule().fill(color).frame(width: max(g.size.width * shown, shown > 0 ? 6 : 0))
            }
        }
        .frame(height: 6)
        .onAppear {
            if reduceMotion { shown = fraction } else { withAnimation(Motion.fill.delay(0.05)) { shown = fraction } }
        }
        .onChange(of: fraction) { _, f in
            withAnimation(reduceMotion ? nil : Motion.gentle) { shown = f }
        }
        .accessibilityHidden(true)
    }
}

/// A counter's list (Due today, Next 7 days, Unread, Overdue, Tomorrow, Graded) in a popover from its card: each row
/// opens its work; an overdue one the student has let go can be cleared.
private struct CounterList: View {
    let key: String
    let open: (String) -> Void
    @EnvironmentObject private var engine: Engine
    @State private var data: ItemsSheetData?
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let d = data {
                Text(d.title)
                    .font(.headline)
                    .padding(.horizontal, 16)
                    .padding(.top, 14)
                    .padding(.bottom, 6)
                if let note = d.note, !note.isEmpty {
                    Text(note).font(.callout).foregroundStyle(.secondary)
                        .padding(.horizontal, 16)
                        .padding(.bottom, 6)
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 10) {
                        if d.sections.isEmpty {
                            EmptyNote(text: d.empty ?? "Nothing here.")
                                .padding(.horizontal, 8)
                        }
                        ForEach(d.sections) { section in
                            VStack(alignment: .leading, spacing: 2) {
                                if !section.title.isEmpty {
                                    CardHeading(text: section.title).padding(.horizontal, 8).padding(.bottom, 2)
                                }
                                ForEach(section.rows) { row in item(row) }
                            }
                        }
                    }
                    .padding(.horizontal, 8)
                    .padding(.bottom, 12)
                }
            } else if let error {
                Text(error).foregroundStyle(.secondary).padding(20)
            } else {
                ProgressView().frame(maxWidth: .infinity).padding(30)
            }
        }
        .frame(width: 380)
        .frame(maxHeight: 460)
        .task { await load() }
    }

    private func item(_ row: SheetRow) -> some View {
        RowLink { if let u = row.url { open(u) } } label: {
            HStack(spacing: 10) {
                RoundedRectangle(cornerRadius: 2).fill(Color(hex: row.color)).frame(width: 4, height: 30)
                VStack(alignment: .leading, spacing: 1) {
                    Text(row.title).foregroundStyle(row.quiet == true ? .secondary : .primary).lineLimit(2)
                    if let sub = row.sub { Text(sub).font(.caption).foregroundStyle(.secondary).lineLimit(2) }
                }
                Spacer(minLength: 4)
                if row.clearable == true, let k = row.key {
                    Button { clear(k) } label: { Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary) }
                        .buttonStyle(.borderless)
                        .help("Clear from Overdue")
                        .accessibilityLabel("Clear")
                }
            }
        }
        .contextMenu {
            if let u = row.url { Button("Open") { open(u) } }
            if row.clearable == true, let k = row.key { Button("Clear from Overdue") { clear(k) } }
        }
    }

    private func load() async {
        do {
            let d = try await engine.call("todaySheet", ["key": key], as: ItemsSheetData.self)
            withAnimation(data == nil ? nil : Motion.gentle) { data = d }
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func clear(_ k: String) {
        Task {
            if await engine.act("clearOverdue", ["key": k]) {
                engine.changed()
                await load()
            }
        }
    }
}
