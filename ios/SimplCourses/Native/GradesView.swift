import Charts
import SwiftUI

private struct DayGPA: Identifiable {
    let id: Date
    let gpa: Double
}

/// Grades: the term GPA against your goal, its trend, each course's ring, letter and categories (and
/// the target you set), what-if scores that are never saved, and every graded item by letter.
struct GradesView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: GradesData?
    @State private var error: String?
    @State private var goal: Double = 4
    @State private var open: String?
    @State private var whatIf = false
    @State private var tried: [String: Double] = [:]
    @State private var band = "all"
    @State private var showAll = false
    @State private var goalSave: Task<Void, Never>?

    var body: some View {
        Group {
            if let d = data {
                List {
                    Section { hero(d) }
                    if let trend = d.trend, trend.count >= 2 {
                        Section("Trend") { trendChart(trend, d) }
                    }
                    Section {
                        Toggle(isOn: $whatIf.animation()) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text("What-if scores")
                                Text("Test outcomes. Nothing is saved.").font(.footnote).foregroundStyle(.secondary)
                            }
                        }
                        .onChange(of: whatIf) {
                            Haptics.select()
                            if !whatIf { tried = [:] }
                        }
                        if whatIf {
                            Label("This is not your actual score.", systemImage: "exclamationmark.triangle.fill")
                                .foregroundStyle(.red).font(.subheadline.weight(.medium))
                        }
                    }
                    Section("Courses") {
                        if d.rows.isEmpty { Text("No current courses.").foregroundStyle(.secondary) }
                        ForEach(d.rows) { r in courseRow(r, d) }
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
    }

    // MARK: - The hero

    private func termGPA(_ d: GradesData) -> Double? {
        guard whatIf else { return d.gpa }
        let pts: [Double] = d.rows.compactMap { r in
            if let t = tried[r.id] { return points(t, d) }
            return r.points
        }
        return pts.isEmpty ? nil : pts.reduce(0, +) / Double(pts.count)
    }

    private func points(_ pct: Double, _ d: GradesData) -> Double {
        (d.scale.first { pct >= $0.min } ?? d.scale.last)?.points ?? 0
    }

    private func letter(_ pct: Double, _ d: GradesData) -> String {
        (d.scale.first { pct >= $0.min } ?? d.scale.last)?.letter ?? ""
    }

    private func hero(_ d: GradesData) -> some View {
        let g = termGPA(d)
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
            HStack {
                if let diff = diff {
                    Label(diff >= 0 ? String(format: "+%.2f above goal", diff) : String(format: "%.2f below goal", abs(diff)), systemImage: diff >= 0 ? "arrow.up.circle.fill" : "arrow.down.circle.fill")
                        .font(.subheadline.weight(.medium))
                        .foregroundStyle(diff >= 0 ? .green : .orange)
                } else {
                    Text("No score to compare yet").font(.subheadline).foregroundStyle(.secondary)
                }
                Spacer()
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
        .animation(.snappy, value: g)
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

    @ViewBuilder
    private func courseRow(_ r: GradeRow, _ d: GradesData) -> some View {
        let t = whatIf ? tried[r.id] : nil
        let pct = t ?? r.pct
        let letterText = t.map { letter($0, d) } ?? r.letter
        let color = Color(hex: r.color)
        let expanded = open == r.id
        VStack(alignment: .leading, spacing: 12) {
            Button {
                Haptics.tap()
                withAnimation(.snappy) { open = expanded ? nil : r.id }
            } label: {
                HStack(spacing: 14) {
                    Gauge(value: min(max(pct ?? 0, 0), 100), in: 0...100) { EmptyView() }
                        .gaugeStyle(.accessoryCircularCapacity)
                        .tint(color)
                        .scaleEffect(0.78)
                        .frame(width: 44, height: 44)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(r.code).font(.headline).lineLimit(1)
                        Text("\(r.name ?? "") · \(r.total > 0 ? "\(r.graded) of \(r.total) graded" : "nothing graded")")
                            .font(.footnote).foregroundStyle(.secondary).lineLimit(1)
                    }
                    Spacer(minLength: 6)
                    VStack(alignment: .trailing, spacing: 3) {
                        Text(pct.map { String(format: "%.1f%%", $0) } ?? "N/A").font(.body.weight(.semibold).monospacedDigit())
                        if let l = letterText {
                            Text(l)
                                .font(.caption.weight(.bold))
                                .padding(.horizontal, 7).padding(.vertical, 2)
                                .foregroundStyle(color)
                                .background(color.opacity(0.15), in: Capsule())
                        }
                    }
                    Image(systemName: "chevron.down")
                        .font(.footnote.weight(.semibold))
                        .foregroundStyle(.tertiary)
                        .rotationEffect(.degrees(expanded ? 180 : 0))
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            if expanded {
                VStack(alignment: .leading, spacing: 10) {
                    if r.cats.isEmpty { Text("No graded groups yet.").font(.footnote).foregroundStyle(.secondary) }
                    ForEach(r.cats) { c in
                        VStack(alignment: .leading, spacing: 4) {
                            HStack {
                                Circle().fill(Color(hex: c.color)).frame(width: 8, height: 8)
                                Text(c.label).font(.subheadline).lineLimit(1)
                                if let w = c.weight, !w.isEmpty { Text(w).font(.caption).foregroundStyle(.secondary) }
                                Spacer()
                                Text(c.value ?? "—").font(.subheadline.monospacedDigit())
                            }
                            ProgressView(value: min(max(c.pct ?? 0, 0), 100), total: 100).tint(Color(hex: c.color))
                        }
                    }
                    if whatIf {
                        HStack {
                            Text("What-if score")
                            Spacer()
                            TextField(r.pct.map { String(format: "%.1f", $0) } ?? "—", value: Binding(get: { tried[r.id] }, set: { v in tried[r.id] = v.map { min(max($0, 0), 100) } }), format: .number)
                                .keyboardType(.decimalPad)
                                .multilineTextAlignment(.trailing)
                                .frame(width: 80)
                            Text("%").foregroundStyle(.secondary)
                        }
                    }
                    if r.pct != nil {
                        Picker("Target", selection: Binding(get: { r.target ?? "" }, set: { v in setTarget(r, v) })) {
                            Text("None").tag("")
                            ForEach(d.scale, id: \.letter) { s in Text(s.letter).tag(s.letter) }
                        }
                        .pickerStyle(.menu)
                    }
                    if let url = r.url {
                        Button { engine.openWeb(url, title: "Grades") } label: {
                            Label("Open the course’s grades", systemImage: "arrow.up.right")
                        }
                        .font(.subheadline)
                    }
                }
                .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
        .padding(.vertical, 4)
    }

    private func setTarget(_ r: GradeRow, _ letter: String) {
        Haptics.select()
        Task {
            await engine.act("setTarget", letter.isEmpty ? ["id": r.id] : ["id": r.id, "letter": letter])
            await load()
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
                        Text(it.band).font(.caption.weight(.bold)).foregroundStyle(bandColor(it.band))
                        Text(it.score).font(.subheadline.monospacedDigit()).foregroundStyle(.secondary)
                    }
                }
                .buttonStyle(.plain)
            }
            if pick.count > shown.count {
                Button("Show all \(pick.count)") { withAnimation { showAll = true } }
            }
        } header: {
            Text("Item grades")
        }
    }

    private func bandColor(_ b: String) -> Color {
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
            if data == nil { goal = d.goal }
            data = d
            error = nil
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }
}
