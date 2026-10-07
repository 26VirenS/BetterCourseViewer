import Charts
import SwiftUI

private struct DayGPA: Identifiable {
    let id: Date
    let gpa: Double
}

/// Grades: the term GPA against your goal and its trend, and each course as rings inside rings — its total and
/// its assignment groups — pressed, the course's grades in a sheet of their own (every assignment, sorted as
/// you like, and what-if scores).
struct GradesView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: GradesData?
    @State private var error: String?
    @State private var goal: Double = 4
    @State private var goalSave: Task<Void, Never>?
    @State private var sheet: GradeRow?

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section { hero(d) }
                    if let trend = d.trend, trend.count >= 2 {
                        Section("Trend") { trendChart(trend, d) }
                    }
                    Section {
                        if d.rows.isEmpty { Text("No current courses.").foregroundStyle(.secondary) }
                        ForEach(d.rows) { r in courseRow(r) }
                    } header: {
                        Text("Courses")
                    } footer: {
                        Text("Press a course for each assignment's grade and what-if scores.")
                    }
                    Section {
                        Text("Term GPA is worked out here from the scores Canvas reports, on a 4.0 scale with every course counting equally. It is not your school’s official GPA.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(force: true) }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Grades")
        .shellToolbar()
        .task(id: engine.dataVersion) { await load() }
        .sheet(item: $sheet) { r in
            NavigationStack {
                CourseGradesView(courseId: r.id, inSheet: true)
            }
            .environmentObject(engine)
            .presentationDetents([.large])
            .presentationDragIndicator(.visible)
        }
    }

    // MARK: - The term

    private func hero(_ d: GradesData) -> some View {
        let g = d.gpa
        let diff = g.map { $0 - goal }
        return VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(g.map { String(format: "%.2f", $0) } ?? "—")
                    .font(.system(size: 48, weight: .bold, design: .rounded))
                    .contentTransition(.numericText(value: g ?? 0))
                VStack(alignment: .leading, spacing: 0) {
                    Text("term GPA").font(.subheadline.weight(.medium)).foregroundStyle(.secondary)
                    if let t = d.term, !t.isEmpty { Text(t).font(.caption).foregroundStyle(.tertiary) }
                }
                Spacer()
            }
            ProgressView(value: min(max(g ?? 0, 0), 4), total: 4)
                .tint(diff.map { $0 >= 0 ? Color.green : Color.orange } ?? .accentColor)
            if let diff {
                Label(diff >= 0 ? String(format: "+%.2f above goal", diff) : String(format: "%.2f below goal", abs(diff)), systemImage: diff >= 0 ? "arrow.up.circle.fill" : "arrow.down.circle.fill")
                    .font(.subheadline.weight(.medium))
                    .foregroundStyle(diff >= 0 ? .green : .orange)
                    .contentTransition(.numericText())
            } else {
                Text("No score to compare yet").font(.subheadline).foregroundStyle(.secondary)
            }
            Stepper(value: $goal, in: 0...4, step: 0.05) {
                Text("Goal ") + Text(String(format: "%.2f", goal)).bold().monospacedDigit()
            }
            .onChange(of: goal) {
                guard let saved = data?.goal, abs(goal - saved) > 0.001 || goalSave != nil else { return }
                Haptics.select()
                goalSave?.cancel()
                let g = goal
                goalSave = Task {
                    try? await Task.sleep(nanoseconds: 500_000_000)
                    guard !Task.isCancelled else { return }
                    await engine.act("setGoal", ["goal": g])
                }
            }
        }
        .padding(.vertical, 6)
    }

    private func trendChart(_ trend: [TrendPoint], _ d: GradesData) -> some View {
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyy-MM-dd"
        let points = trend.compactMap { p in fmt.date(from: p.date).map { DayGPA(id: $0, gpa: p.gpa) } }
        return Chart {
            ForEach(points) { p in
                LineMark(x: .value("Day", p.id), y: .value("GPA", p.gpa))
                    .interpolationMethod(.catmullRom)
                    .foregroundStyle(Color.accentColor)
                AreaMark(x: .value("Day", p.id), yStart: .value("Low", d.minY ?? 2.4), yEnd: .value("GPA", p.gpa))
                    .interpolationMethod(.catmullRom)
                    .foregroundStyle(LinearGradient(colors: [Color.accentColor.opacity(0.25), .clear], startPoint: .top, endPoint: .bottom))
            }
            if goal > 0 {
                RuleMark(y: .value("Goal", goal))
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 4]))
                    .foregroundStyle(.secondary)
            }
        }
        .chartYScale(domain: (d.minY ?? 2.4)...(d.maxY ?? 4))
        .frame(height: 150)
        .padding(.vertical, 6)
    }

    // MARK: - Courses

    private func courseRow(_ r: GradeRow) -> some View {
        let color = Color(hex: r.color)
        return Button {
            Haptics.tap()
            sheet = r
        } label: {
            HStack(spacing: 14) {
                NestedRings(bands: NestedRings.bands(total: r.pct, color: color, groups: r.cats.map { (pct: $0.pct, color: $0.color) }, limit: 3))
                    .frame(width: 54, height: 54)
                VStack(alignment: .leading, spacing: 3) {
                    Text(r.code).font(.headline).lineLimit(1)
                    Text("\(r.name ?? "") · \(r.total > 0 ? "\(r.graded) of \(r.total) graded" : "nothing graded")")
                        .font(.footnote).foregroundStyle(.secondary).lineLimit(1)
                }
                .separatorAtText()
                Spacer(minLength: 6)
                VStack(alignment: .trailing, spacing: 2) {
                    HStack(spacing: 6) {
                        if let l = r.letter {
                            Text(l).font(.caption.weight(.bold)).foregroundStyle(GradesView.bandColor(String(l.prefix(1))))
                        }
                        Text(r.pct.map { String(format: "%.1f%%", $0) } ?? "N/A").font(.body.weight(.semibold).monospacedDigit())
                    }
                    if let t = r.target { Text("Target \(t)").font(.caption2).foregroundStyle(.secondary) }
                }
                Image(systemName: "chevron.right").font(.footnote.weight(.semibold)).foregroundStyle(.tertiary)
            }
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(r.code), \(r.pct.map { String(format: "%.1f percent", $0) } ?? "no score")\(r.letter.map { ", \($0)" } ?? "")")
        .contextMenu {
            Button { engine.push(.course(id: r.id)) } label: { Label("Open Course", systemImage: "arrow.up.right") }
            Button { sheet = r } label: { Label("Grades & What-If", systemImage: "chart.bar") }
        }
    }

    static func bandColor(_ b: String) -> Color {
        switch b {
        case "A": return .green
        case "B": return .teal
        case "C": return .yellow
        case "D": return .orange
        default: return .red
        }
    }

    private func load(force: Bool = false) async {
        if force { _ = try? await engine.call("refresh", as: OK.self) }
        do {
            let d = try await engine.call("grades", as: GradesData.self)
            var t = Transaction()
            t.disablesAnimations = true
            withTransaction(t) {
                if data == nil { goal = d.goal }
                data = d
                error = nil
            }
            if let id = LaunchOpen.take("grades:") { sheet = d.rows.first { $0.id == id } }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

/// What-if assignments the student adds to a group: a name, the points it is out of, the score tried.
struct AddedWork: Identifiable, Hashable {
    let id: String
    let groupId: String
    var name: String
    var possible: Double
}

/// One course's grades: the total and each assignment group as rings inside rings, with a legend; every
/// assignment's mark, by group or sorted by grade, due date or name — and What-If: switched on, every mark
/// becomes a field to type a score into, and each group takes what-if assignments; the total, the letter
/// and the term GPA follow, worked out by the grade rules the web screens use. Nothing is saved. As a sheet
/// from Grades, or pushed as a course's Grades.
struct CourseGradesView: View {
    let courseId: String
    var inSheet = false
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var data: CourseGradesData?
    @State private var error: String?
    @State private var whatIf = false
    @State private var drafts: [String: String] = [:]
    @State private var added: [AddedWork] = []
    @State private var sort: Sort = .groups
    @State private var addingTo: CGGroup?
    @State private var applying: Task<Void, Never>?
    @FocusState private var focused: String?

    enum Sort: String, CaseIterable, Identifiable {
        case groups = "By Group", high = "Highest Grade", low = "Lowest Grade", due = "Due Date", name = "Name"
        var id: String { rawValue }
        var symbol: String {
            switch self {
            case .groups: return "square.grid.2x2"
            case .high: return "arrow.down"
            case .low: return "arrow.up"
            case .due: return "calendar"
            case .name: return "textformat"
            }
        }
    }

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section { header(d) }
                    Section {
                        Toggle(isOn: Binding(get: { whatIf }, set: { setWhatIf($0) })) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("What-If Scores").font(.body.weight(.semibold))
                                Text(whatIf ? "Type a score into any assignment, or add one. Nothing is saved." : "Try scores and see where your grade would land.")
                                    .font(.footnote).foregroundStyle(.secondary)
                            }
                        }
                        .tint(.orange)
                        if whatIf, let g = d.gpaIf {
                            HStack {
                                Text("Term GPA would be")
                                Spacer()
                                Text(String(format: "%.2f", g)).bold().monospacedDigit().contentTransition(.numericText(value: g))
                                if let now = d.gpa { Text(String(format: "(now %.2f)", now)).font(.footnote).foregroundStyle(.secondary) }
                            }
                        }
                        if whatIf && (!drafts.isEmpty || !added.isEmpty) {
                            Button(role: .destructive) { reset() } label: { Label("Clear What-If Scores", systemImage: "arrow.uturn.backward") }
                        }
                    }
                    rowsSections(d)
                }
                .listStyle(.insetGrouped)
                .refreshable { await load(fresh: true) }
            } else {
                LoadState(error: error) { Task { await load(fresh: true) } }
            }
        }
        .navigationTitle(data?.code ?? "Grades")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            if inSheet {
                ToolbarItem(placement: .confirmationAction) { Button("Done") { dismiss() } }
                ToolbarItem(placement: .topBarLeading) {
                    Button { dismiss(); engine.push(.course(id: courseId)) } label: { Text("Course") }
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Picker("Sort", selection: $sort) {
                        ForEach(Sort.allCases) { s in Label(s.rawValue, systemImage: s.symbol).tag(s) }
                    }
                } label: {
                    Image(systemName: "arrow.up.arrow.down")
                }
                .accessibilityLabel("Sort")
            }
            ToolbarItemGroup(placement: .keyboard) {
                Spacer()
                Button("Done") { focused = nil }
            }
        }
        .onChange(of: sort) { Haptics.select() }
        .task { await load(fresh: true) }
        .sheet(item: $addingTo) { g in
            AddWhatIfSheet(group: g) { name, possible, score in
                let id = "whatif-\(UUID().uuidString.prefix(8))"
                added.append(AddedWork(id: id, groupId: g.id, name: name, possible: possible))
                drafts[id] = score.map { CourseGradesView.num($0) } ?? ""
                Haptics.success()
                Task { await load() }
            }
        }
    }

    // MARK: - The total and the groups

    private func header(_ d: CourseGradesData) -> some View {
        let color = Color(hex: d.color)
        let rings = NestedRings.bands(total: d.total, color: whatIf ? .orange : color, groups: d.groups.map { (pct: $0.pct, color: $0.color) }, limit: 4)
        let legend = Array(d.groups.filter { $0.pct != nil }.prefix(rings.count - 1))
        return VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 18) {
                NestedRings(bands: rings, outerWidth: 11, innerWidth: 7, gap: 2.5)
                    .frame(width: 124, height: 124)
                VStack(alignment: .leading, spacing: 4) {
                    Text(d.totalText)
                        .font(.system(size: 34, weight: .bold, design: .rounded)).monospacedDigit()
                        .contentTransition(.numericText(value: d.total ?? 0))
                    if let l = d.letter {
                        Text(whatIf ? "\(l) with what-if" : l)
                            .font(.headline).foregroundStyle(whatIf ? .orange : color)
                    }
                    Menu {
                        Picker("Target", selection: Binding(get: { d.target ?? "" }, set: { setTarget($0) })) {
                            Text("No target").tag("")
                            ForEach(d.scale, id: \.letter) { s in Text(s.letter).tag(s.letter) }
                        }
                    } label: {
                        Label(d.target.map { "Target \($0)" } ?? "Set a target", systemImage: "scope").font(.subheadline)
                    }
                }
            }
            // the legend: each ring's group, its weight and its score
            if !legend.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(Array(legend.enumerated()), id: \.offset) { i, g in
                        HStack(spacing: 8) {
                            Circle().fill(rings[i + 1].color).frame(width: 9, height: 9)
                            Text(g.name).font(.subheadline).lineLimit(1)
                            Spacer(minLength: 6)
                            if let w = g.weightText, !w.isEmpty { Text(w).font(.caption).foregroundStyle(.secondary) }
                            Text(g.value ?? "—").font(.subheadline.weight(.semibold).monospacedDigit())
                        }
                    }
                }
            }
            VStack(alignment: .leading, spacing: 2) {
                if let note = d.note, !note.isEmpty { Text(note).font(.footnote).foregroundStyle(.secondary) }
                if let f = d.final, !f.isEmpty { Text(f).font(.caption).foregroundStyle(.tertiary) }
            }
        }
        .padding(.vertical, 8)
    }

    // MARK: - The assignments

    @ViewBuilder
    private func rowsSections(_ d: CourseGradesData) -> some View {
        if sort == .groups {
            ForEach(d.groups) { g in
                let rows = d.rows.filter { $0.groupId == g.id }
                if !rows.isEmpty || whatIf {
                    Section {
                        ForEach(rows) { r in row(r, d, showGroup: false) }
                        if whatIf {
                            Button { addingTo = g } label: { Label("Add What-If Assignment", systemImage: "plus.circle") }
                        }
                    } header: {
                        HStack {
                            Text(g.name)
                            Spacer()
                            Text([g.weightText, g.value].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")).textCase(nil)
                        }
                    } footer: {
                        if let detail = g.detail, !detail.isEmpty { Text(detail) }
                    }
                }
            }
        } else {
            Section {
                ForEach(sorted(d)) { r in row(r, d, showGroup: true) }
            } header: {
                Text("All assignments · \(sort.rawValue)")
            }
        }
    }

    private func sorted(_ d: CourseGradesData) -> [CGRow] {
        let pct: (CGRow) -> Double? = { r in
            guard let e = r.effective, r.possible > 0 else { return nil }
            return e / r.possible * 100
        }
        switch sort {
        case .high, .low:
            let graded = d.rows.filter { pct($0) != nil }.sorted { a, b in
                sort == .high ? pct(a)! > pct(b)! : pct(a)! < pct(b)!
            }
            return graded + d.rows.filter { pct($0) == nil }
        case .due:
            return d.rows.sorted { ($0.due ?? "9999") < ($1.due ?? "9999") }
        case .name:
            return d.rows.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        case .groups:
            return d.rows
        }
    }

    private func letter(_ r: CGRow, _ d: CourseGradesData) -> String? {
        guard let e = r.effective, r.possible > 0 else { return nil }
        let p = e / r.possible * 100
        return d.scale.first { p >= $0.min }?.letter ?? d.scale.last?.letter
    }

    @ViewBuilder
    private func row(_ r: CGRow, _ d: CourseGradesData, showGroup: Bool) -> some View {
        if whatIf {
            rowContent(r, d, showGroup: showGroup) // (the score field takes the taps)
        } else {
            Button {
                guard let u = r.url else { return }
                if inSheet { dismiss() }
                engine.go(u, title: r.name)
            } label: {
                rowContent(r, d, showGroup: showGroup)
            }
            .buttonStyle(.plain)
        }
    }

    private func rowContent(_ r: CGRow, _ d: CourseGradesData, showGroup: Bool) -> some View {
        let tried = r.hypothetical == true || r.added == true
        let groupName = showGroup ? d.groups.first { $0.id == r.groupId }?.name : nil
        return HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 6) {
                    if r.added == true { Image(systemName: "wand.and.stars").font(.caption).foregroundStyle(.orange) }
                    Text(r.name).lineLimit(2).foregroundStyle(r.dropped == true ? .secondary : .primary)
                }
                HStack(spacing: 6) {
                    if let b = r.badge, !b.isEmpty { StatusChip(text: b, tone: CourseGradesView.tone(b)) }
                    if r.dropped == true { StatusChip(text: "Dropped") }
                    if let gn = groupName { Text(gn).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                    if let due = r.dueText, !due.isEmpty { Text(due).font(.caption).foregroundStyle(.secondary) }
                }
            }
            .separatorAtText()
            Spacer(minLength: 8)
            if whatIf && r.counted != false {
                scoreField(r, tried: tried)
            } else {
                VStack(alignment: .trailing, spacing: 1) {
                    HStack(spacing: 6) {
                        if let l = letter(r, d) {
                            Text(l).font(.caption.weight(.bold)).foregroundStyle(GradesView.bandColor(String(l.prefix(1))))
                        }
                        Text(r.scoreText)
                            .font(.body.weight(.medium).monospacedDigit())
                            .foregroundStyle(r.effective == nil ? Color.secondary : Color.primary)
                    }
                    if let g = r.grade, !g.isEmpty { Text(g).font(.caption2).foregroundStyle(.secondary) }
                }
            }
        }
        .contentShape(Rectangle())
        .swipeActions {
            if r.added == true {
                Button(role: .destructive) { remove(r) } label: { Label("Remove", systemImage: "trash") }
            } else if drafts[r.id] != nil {
                Button { useReal(r) } label: { Label("Real Score", systemImage: "arrow.uturn.backward") }.tint(.gray)
            }
        }
        .contextMenu {
            if let u = r.url {
                Button { if inSheet { dismiss() }; engine.go(u, title: r.name) } label: { Label("Open Assignment", systemImage: "arrow.up.right") }
            }
            if drafts[r.id] != nil && r.added != true { Button { useReal(r) } label: { Label("Use My Real Score", systemImage: "arrow.uturn.backward") } }
        }
    }

    /// A what-if score typed straight into the row (no window over the list): the score, then what it is out of.
    private func scoreField(_ r: CGRow, tried: Bool) -> some View {
        HStack(spacing: 4) {
            TextField("—", text: Binding(
                get: { drafts[r.id] ?? r.effective.map { CourseGradesView.num($0) } ?? "" },
                set: { v in
                    drafts[r.id] = v
                    scheduleApply()
                }
            ))
            .keyboardType(.decimalPad)
            .multilineTextAlignment(.trailing)
            .font(.body.weight(tried ? .bold : .regular).monospacedDigit())
            .foregroundStyle(tried ? Color.orange : Color.primary)
            .focused($focused, equals: r.id)
            .frame(width: 58)
            .padding(.vertical, 6)
            .padding(.horizontal, 8)
            .background(tried ? Color.orange.opacity(0.14) : Color(.tertiarySystemFill), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
            Text("/ \(CourseGradesView.num(r.possible))")
                .font(.subheadline.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("What-if score for \(r.name), out of \(CourseGradesView.num(r.possible))")
    }

    static func num(_ v: Double) -> String {
        v == v.rounded() ? String(Int(v)) : String(format: "%g", v)
    }

    static func tone(_ badge: String) -> String {
        switch badge {
        case "Missing": return "bad"
        case "Late": return "warn"
        case "Excused": return "info"
        default: return ""
        }
    }

    // MARK: - What-if

    private func setWhatIf(_ on: Bool) {
        Haptics.select()
        withAnimation(.snappy) { whatIf = on }
        if !on {
            focused = nil
            drafts = [:]
            added = []
        }
        Task { await load() }
    }

    private func scheduleApply() {
        applying?.cancel()
        applying = Task {
            try? await Task.sleep(nanoseconds: 450_000_000)
            guard !Task.isCancelled else { return }
            await load()
        }
    }

    private func useReal(_ r: CGRow) {
        Haptics.select()
        drafts.removeValue(forKey: r.id)
        Task { await load() }
    }

    private func remove(_ r: CGRow) {
        Haptics.select()
        added.removeAll { $0.id == r.id }
        drafts.removeValue(forKey: r.id)
        Task { await load() }
    }

    private func reset() {
        Haptics.play("warning")
        focused = nil
        drafts = [:]
        added = []
        Task { await load() }
    }

    private func setTarget(_ letter: String) {
        Haptics.select()
        Task {
            await engine.act("setTarget", letter.isEmpty ? ["id": courseId] : ["id": courseId, "letter": letter])
            await load()
            engine.changed()
        }
    }

    private func load(fresh: Bool = false) async {
        var tried: [String: Any] = [:]
        for (k, v) in drafts {
            let t = v.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
            tried[k] = t
        }
        let extra: [[String: Any]] = added.map { ["id": $0.id, "groupId": $0.groupId, "name": $0.name, "possible": $0.possible] }
        do {
            let d = try await engine.call("courseGrades", ["id": courseId, "tried": tried, "added": extra, "on": whatIf, "fresh": fresh], as: CourseGradesData.self)
            withAnimation(.snappy) { data = d }
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

/// A what-if assignment for a group: its name, what it is out of, and the score to try — a small sheet.
struct AddWhatIfSheet: View {
    let group: CGGroup
    let add: (String, Double, Double?) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var possible = "100"
    @State private var score = ""
    @FocusState private var focus: Bool

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("Name (optional)", text: $name).focused($focus)
                    HStack {
                        TextField("Score", text: $score).keyboardType(.decimalPad).multilineTextAlignment(.trailing)
                        Text("out of").foregroundStyle(.secondary)
                        TextField("Points", text: $possible).keyboardType(.decimalPad).frame(width: 64)
                    }
                } footer: {
                    Text("Counts in \(group.name)\(group.weightText.map { $0.isEmpty ? "" : " (\($0))" } ?? ""). Nothing is saved to Canvas.")
                }
            }
            .navigationTitle("What-If Assignment")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Add") {
                        let p = Double(possible.replacingOccurrences(of: ",", with: ".")) ?? 0
                        let s = Double(score.replacingOccurrences(of: ",", with: "."))
                        add(name.trimmingCharacters(in: .whitespaces).isEmpty ? "What-if assignment" : name, p, s)
                        dismiss()
                    }
                    .disabled((Double(possible.replacingOccurrences(of: ",", with: ".")) ?? 0) <= 0)
                }
            }
            .onAppear { focus = true }
        }
        .presentationDetents([.height(300), .medium])
        .presentationDragIndicator(.visible)
    }
}
