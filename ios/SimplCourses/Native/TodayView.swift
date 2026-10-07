import SwiftUI

/// Today: the six counters (each opens what it counted), the day's work with its ticks, and the week's
/// load per course — the web Today's numbers, drawn by the phone.
struct TodayView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: Today?
    @State private var counts: TodayCounts?
    @State private var error: String?
    @State private var sheet: SheetKey?
    @State private var sheetShown = false

    struct SheetKey: Identifiable { let id: String }

    static let dateInBar: Bool = {
        if #available(iOS 26.0, *) { return true } else { return false }
    }()

    private let columns = [GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10), GridItem(.flexible(), spacing: 10)]

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section {
                        LazyVGrid(columns: columns, spacing: 10) {
                            ForEach(d.counters) { c in counterTile(c) }
                        }
                        .listRowInsets(EdgeInsets())
                        .listRowBackground(Color.clear)
                    } header: {
                        // iOS 26: the date is the title's own subtitle, on its edge (as a header it sat a cell's inset
                        // further in, out of line with the title and the cards; in the row, the list's round corner
                        // clipped its first letter). Before iOS 26 it stays the header.
                        if !TodayView.dateInBar, let line = d.dateLine { Text(line).textCase(nil) }
                    }
                    Section {
                        if d.list.rows.isEmpty {
                            Text(d.list.empty ?? "Nothing due.").foregroundStyle(.secondary)
                        }
                        ForEach(d.list.rows) { row in
                            WorkRowView(row: row) { done in toggle(row, done) }
                                .contentShape(Rectangle())
                                .onTapGesture { open(row) }
                                .workSwipe(WorkAction(row), engine: engine)
                                .swipeActions(edge: .trailing) {
                                    Button { toggle(row, !row.done) } label: {
                                        Label(row.done ? "Not Done" : "Done", systemImage: row.done ? "arrow.uturn.backward" : "checkmark")
                                    }
                                    .tint(row.done ? .gray : .green)
                                }
                        }
                    } header: {
                        HStack {
                            Text(d.list.heading)
                            Spacer()
                            if let note = d.list.note, !note.isEmpty { Text(note).textCase(nil) }
                        }
                    }
                    if let load = d.load, d.hasCourses ?? true {
                        Section("Week load") {
                            ForEach(load) { r in
                                HStack(spacing: 12) {
                                    Text(r.code).font(.subheadline.weight(.medium)).lineLimit(1).frame(width: 96, alignment: .leading)
                                    ProgressView(value: Double(r.done), total: Double(max(r.total, 1)))
                                        .tint(Color(hex: r.color))
                                    Text("\(r.done)/\(r.total)").font(.subheadline.monospacedDigit()).foregroundStyle(.secondary)
                                }
                            }
                            if load.isEmpty {
                                Text("Nothing assigned this week.").foregroundStyle(.secondary)
                            }
                            if let idle = d.idle, idle > 0 {
                                Text("\(idle) \(idle == 1 ? "course" : "courses") with nothing assigned this week")
                                    .font(.footnote).foregroundStyle(.secondary)
                            }
                        }
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Today")
        .modifier(TitleSubtitle(text: data?.dateLine))
        .shellToolbar(bell: true)
        .task(id: engine.dataVersion) { await load() }
        .sheet(item: $sheet) { key in
            ItemsSheet(key: key.id) { url in
                sheet = nil
                engine.openWeb(url, title: "")
            }
            .environmentObject(engine)
        }
    }

    private func counterTile(_ c: Counter) -> some View {
        let value: Int? = c.key == "overdue" ? counts?.overdue : c.key == "graded" ? counts?.graded : c.value
        let red = c.tone == "red" && (value ?? 0) > 0
        return Button {
            Haptics.tap()
            sheet = SheetKey(id: c.key)
        } label: {
            VStack(alignment: .leading, spacing: 6) {
                Text(c.label)
                    .font(.caption.weight(.medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
                Group {
                    if let v = value {
                        Text("\(v)").contentTransition(.numericText(value: Double(v)))
                    } else {
                        Text("–").foregroundStyle(.tertiary)
                    }
                }
                .font(.system(size: 30, weight: .bold, design: .rounded))
                .foregroundStyle(red ? Color.red : Color.primary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .contentCard(cornerRadius: 20, tint: red ? .red : nil)
            .contentShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        }
        .buttonStyle(PressScale())
        .animation(.snappy, value: value)
    }

    private func load(force: Bool = false) async {
        if force { _ = try? await engine.call("refresh", as: OK.self) }
        do {
            let d = try await engine.call("today", as: Today.self)
            data = d
            error = nil
            // (the simulator suite: -SimplSheet next opens a counter's list)
            if let key = UserDefaults.standard.string(forKey: "SimplSheet"), !key.isEmpty, !sheetShown {
                sheetShown = true
                sheet = SheetKey(id: key)
            }
            Task {
                if let kept = try? await engine.call("todayCounts", ["kept": true], as: TodayCounts.self), kept.overdue != nil { counts = kept }
                if let fresh = try? await engine.call("todayCounts", ["kept": false], as: TodayCounts.self) { counts = fresh }
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    private func toggle(_ row: WorkRow, _ done: Bool) {
        done ? Haptics.success() : Haptics.tap()
        setDone(row.id, done)
        Task {
            if !(await engine.act("complete", ["id": row.id, "done": done])) { setDone(row.id, !done) }
            else { engine.changed() }
        }
    }

    private func setDone(_ id: String, _ done: Bool) {
        guard var d = data, let i = d.list.rows.firstIndex(where: { $0.id == id }) else { return }
        withAnimation(.snappy) {
            d.list.rows[i].done = done
            data = d
        }
    }

    private func open(_ row: WorkRow) {
        guard let url = row.url else { return }
        Haptics.tap()
        engine.openWeb(url, title: row.title)
    }
}

/// A row of work: its tick, its title and line, where it stands, its course and its time.
struct WorkRowView: View {
    let row: WorkRow
    var showCourse = true
    let toggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: 12) {
            CheckCircle(done: row.done, color: .green) { toggle(!row.done) }
            VStack(alignment: .leading, spacing: 2) {
                Text(row.title)
                    .font(.body)
                    .lineLimit(2)
                    .strikethrough(row.done, color: .secondary)
                    .foregroundStyle(row.done ? .secondary : .primary)
                if let sub = row.sub, !sub.isEmpty {
                    Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            VStack(alignment: .trailing, spacing: 4) {
                if let flag = row.flag { FlagBadge(flag: flag) }
                HStack(spacing: 6) {
                    if showCourse, let course = row.course, !course.isEmpty { CourseChip(text: course, color: row.color) }
                    if let time = row.time, !time.isEmpty { Text(time).font(.caption.monospacedDigit()).foregroundStyle(.secondary) }
                }
            }
        }
        .padding(.vertical, 2)
    }
}

/// A counter's list (Due today, Next 7 days, Unread, Overdue, Tomorrow, Graded), as Apple's sheet.
struct ItemsSheet: View {
    let key: String
    let open: (String) -> Void
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var data: ItemsSheetData?
    @State private var error: String?
    /// Half the screen first, every time (1.4): a sheet left to pick its own height came up at the top
    /// whenever its list was long enough to scroll; a swipe up on the list takes it to the top first.
    @State private var detent: PresentationDetent = .medium

    var body: some View {
        NavigationStack {
            Group {
                if let d = data {
                    List {
                        if let note = d.note, !note.isEmpty {
                            Section { Text(note).font(.subheadline).foregroundStyle(.secondary) }
                        }
                        if d.sections.isEmpty {
                            ContentUnavailableView(d.empty ?? "Nothing here.", systemImage: "checkmark.circle")
                        }
                        ForEach(d.sections) { section in
                            Section {
                                ForEach(section.rows) { row in
                                    Button { if let u = row.url { open(u) } } label: {
                                        HStack(spacing: 12) {
                                            RoundedRectangle(cornerRadius: 2).fill(Color(hex: row.color)).frame(width: 4, height: 34)
                                            VStack(alignment: .leading, spacing: 2) {
                                                Text(row.title).foregroundStyle(row.quiet == true ? .secondary : .primary).lineLimit(2)
                                                if let sub = row.sub { Text(sub).font(.footnote).foregroundStyle(.secondary).lineLimit(2) }
                                            }
                                            Spacer()
                                            Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
                                        }
                                    }
                                    .buttonStyle(.plain)
                                    .swipeActions {
                                        if row.clearable == true, let k = row.key {
                                            Button(role: .destructive) { clear(k) } label: { Label("Clear", systemImage: "xmark") }
                                        }
                                    }
                                }
                            } header: {
                                if !section.title.isEmpty { Text(section.title) }
                            }
                        }
                    }
                } else {
                    LoadState(error: error) { Task { await load() } }
                }
            }
            .navigationTitle(data?.title ?? "")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } }
            }
        }
        .presentationDetents([.medium, .large], selection: $detent)
        .presentationContentInteraction(.resizes)
        .presentationDragIndicator(.visible)
        .task { await load() }
    }

    private func load() async {
        do {
            data = try await engine.call("todaySheet", ["key": key], as: ItemsSheetData.self)
            error = nil
        } catch {
            self.error = error.localizedDescription
        }
    }

    private func clear(_ k: String) {
        Haptics.play("rigid")
        Task {
            if await engine.act("clearOverdue", ["key": k]) {
                engine.changed()
                await load()
            }
        }
    }
}
