import Charts
import SwiftUI

private struct DayGPA: Identifiable {
    let id: Date
    let gpa: Double
}

/// Grades: the term GPA against your goal and its trend, a ring per course (pressed, the course's grades
/// in a sheet of their own, with what-if scores), and every graded item by letter.
struct GradesView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: GradesData?
    @State private var error: String?
    @State private var goal: Double = 4
    @State private var band = "all"
    @State private var showAll = false
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
                        Text("Press a course for its grades and what-if scores.")
                    }
                    if !d.items.isEmpty { itemsSection(d) }
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
                ZStack {
                    Ring(value: r.pct, color: color, lineWidth: 5)
                    if let l = r.letter {
                        Text(l).font(.caption.weight(.bold)).foregroundStyle(color).minimumScaleFactor(0.6)
                    }
                }
                .frame(width: 46, height: 46)
                VStack(alignment: .leading, spacing: 3) {
                    Text(r.code).font(.headline).lineLimit(1)
                    Text("\(r.name ?? "") · \(r.total > 0 ? "\(r.graded) of \(r.total) graded" : "nothing graded")")
                        .font(.footnote).foregroundStyle(.secondary).lineLimit(1)
                }
                .separatorAtText() // (else a ring with a letter starts its divider under the letter)
                Spacer(minLength: 6)
                VStack(alignment: .trailing, spacing: 2) {
                    Text(r.pct.map { String(format: "%.1f%%", $0) } ?? "N/A").font(.body.weight(.semibold).monospacedDigit())
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

    // MARK: - Item grades

    private func itemsSection(_ d: GradesData) -> some View {
        let bands = ["all", "A", "B", "C", "D", "F"]
        let pick = band == "all" ? d.items : d.items.filter { $0.band == band }
        let shown = showAll ? pick : Array(pick.prefix(12))
        return Section {
            Picker("Letter", selection: $band) {
                ForEach(bands, id: \.self) { b in
                    Text(b == "all" ? "All" : "\(b) \(d.counts?[b] ?? 0)").tag(b)
                }
            }
            .pickerStyle(.segmented)
            .onChange(of: band) {
                Haptics.select()
                showAll = false
            }
            if shown.isEmpty { Text("No \(band) grades in your courses.").foregroundStyle(.secondary) }
            ForEach(shown) { it in
                Button { if let u = it.url { engine.openWeb(u, title: it.name) } } label: {
                    HStack(spacing: 12) {
                        Circle().fill(Color(hex: it.color)).frame(width: 9, height: 9)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(it.name).lineLimit(1)
                            Text("\(it.course) · \(it.pctText)").font(.footnote).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(it.band).font(.caption.weight(.bold)).foregroundStyle(GradesView.bandColor(it.band))
                        Text(it.score).font(.subheadline.monospacedDigit()).foregroundStyle(.secondary)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            if pick.count > shown.count {
                Button("Show all \(pick.count)") { showAll = true }
            }
        } header: {
            Text("Item grades")
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

/// One course's grades: the total in a ring with its letter, each assignment group with its weight and
/// score, every assignment's mark — and what-if scores: press an assignment to try a score, and the
/// total, the letter and the term GPA follow, worked out by the same grade rules the web screens use.
/// Nothing is saved. As a sheet from Grades, or pushed as a course's Grades.
struct CourseGradesView: View {
    let courseId: String
    var inSheet = false
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var data: CourseGradesData?
    @State private var error: String?
    @State private var tried: [String: Double?] = [:]
    @State private var editing: CGRow?
    @State private var entry = ""

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section { header(d) }
                    if d.whatIf {
                        Section {
                            Label("What-if scores: this is not your actual grade.", systemImage: "wand.and.stars")
                                .font(.subheadline.weight(.medium))
                                .foregroundStyle(.orange)
                            if let g = d.gpaIf {
                                HStack {
                                    Text("Term GPA would be")
                                    Spacer()
                                    Text(String(format: "%.2f", g)).bold().monospacedDigit().contentTransition(.numericText(value: g))
                                    if let now = d.gpa { Text(String(format: "(now %.2f)", now)).font(.footnote).foregroundStyle(.secondary) }
                                }
                            }
                            Button(role: .destructive) { reset() } label: { Label("Clear What-If Scores", systemImage: "arrow.uturn.backward") }
                        }
                    }
                    ForEach(d.groups) { g in
                        let rows = d.rows.filter { $0.groupId == g.id }
                        if !rows.isEmpty {
                            Section {
                                ForEach(rows) { r in row(r, d) }
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
                    Section {
                        Text("Press an assignment to try a score. Nothing you try here is saved or sent to Canvas.")
                            .font(.footnote).foregroundStyle(.secondary)
                    }
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
        }
        .task { await load(fresh: true) }
        .alert(editing?.name ?? "What-if score", isPresented: Binding(get: { editing != nil }, set: { if !$0 { editing = nil } })) {
            TextField("Score", text: $entry).keyboardType(.decimalPad)
            Button("Try It") { apply(entry) }
            if let e = editing, tried[e.id] != nil { Button("Use My Real Score", role: .destructive) { clear(e) } }
            Button("Cancel", role: .cancel) { editing = nil }
        } message: {
            if let e = editing { Text(e.possible > 0 ? "Out of \(CourseGradesView.num(e.possible)) points." : "A score in points.") }
        }
    }

    private func header(_ d: CourseGradesData) -> some View {
        let color = Color(hex: d.color)
        return HStack(spacing: 18) {
            ZStack {
                Ring(value: d.total, color: d.whatIf ? .orange : color, lineWidth: 10)
                VStack(spacing: 0) {
                    Text(d.totalText).font(.system(.title3, design: .rounded).weight(.bold)).monospacedDigit()
                        .contentTransition(.numericText(value: d.total ?? 0))
                    if let l = d.letter { Text(l).font(.subheadline.weight(.semibold)).foregroundStyle(d.whatIf ? .orange : color) }
                }
            }
            .frame(width: 110, height: 110)
            VStack(alignment: .leading, spacing: 6) {
                Text(d.name ?? d.code).font(.headline).lineLimit(2)
                if let note = d.note, !note.isEmpty { Text(note).font(.footnote).foregroundStyle(.secondary) }
                if let f = d.final, !f.isEmpty { Text(f).font(.caption).foregroundStyle(.tertiary) }
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
        .padding(.vertical, 8)
    }

    private func row(_ r: CGRow, _ d: CourseGradesData) -> some View {
        let tried = r.hypothetical == true
        return Button {
            guard r.counted != false else { return }
            Haptics.tap()
            entry = r.effective.map { CourseGradesView.num($0) } ?? ""
            editing = r
        } label: {
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(r.name).lineLimit(2).foregroundStyle(r.dropped == true ? .secondary : .primary)
                    HStack(spacing: 6) {
                        if let b = r.badge, !b.isEmpty { StatusChip(text: b, tone: CourseGradesView.tone(b)) }
                        if r.dropped == true { StatusChip(text: "Dropped") }
                        if let due = r.dueText, !due.isEmpty { Text(due).font(.caption).foregroundStyle(.secondary) }
                    }
                }
                Spacer(minLength: 8)
                VStack(alignment: .trailing, spacing: 1) {
                    Text(r.scoreText)
                        .font(.body.weight(tried ? .bold : .medium).monospacedDigit())
                        .foregroundStyle(tried ? Color.orange : (r.effective == nil ? Color.secondary : Color.primary))
                        .contentTransition(.numericText())
                    if tried { Text("what-if").font(.caption2.weight(.semibold)).foregroundStyle(.orange) }
                    else if let g = r.grade, !g.isEmpty { Text(g).font(.caption2).foregroundStyle(.secondary) }
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .swipeActions {
            if tried { Button { clear(r) } label: { Label("Real Score", systemImage: "arrow.uturn.backward") }.tint(.gray) }
        }
        .contextMenu {
            if let u = r.url { Button { engine.openWeb(u, title: r.name) } label: { Label("Open Assignment", systemImage: "arrow.up.right") } }
            if tried { Button { clear(r) } label: { Label("Use My Real Score", systemImage: "arrow.uturn.backward") } }
        }
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

    private func apply(_ text: String) {
        guard let e = editing else { return }
        editing = nil
        let cleaned = text.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
        guard let v = Double(cleaned), v >= 0 else { Haptics.error(); return }
        Haptics.select()
        tried[e.id] = v
        Task { await load() }
    }

    private func clear(_ r: CGRow) {
        editing = nil
        Haptics.select()
        tried.removeValue(forKey: r.id)
        Task { await load() }
    }

    private func reset() {
        Haptics.play("warning")
        tried = [:]
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
        var t: [String: Any] = [:]
        for (k, v) in tried { t[k] = v ?? NSNull() }
        do {
            let d = try await engine.call("courseGrades", ["id": courseId, "tried": t, "fresh": fresh], as: CourseGradesData.self)
            withAnimation(.snappy) { data = d }
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}
