import AppKit
import Charts
import SwiftUI

// MARK: - Grades

/// Grades: the term GPA, your goal for it and how it has moved, then every course as rings inside rings — its total
/// outermost, its assignment groups inside — with each group's weight and score as a bar, and every graded assignment
/// by its letter. A course clicked opens its own grades, where scores can be tried.
struct GradesView: View {
    @EnvironmentObject private var engine: Engine
    @State private var data: GradesData?
    @State private var error: String?
    @State private var goal: Double = 4
    @State private var goalSave: Task<Void, Never>?
    @State private var wide = true

    var body: some View {
        Group {
            if let d = data {
                Page {
                    ScreenHeading(title: "Grades", sub: headline(d))
                    summary(d)
                    courses(d)
                    if !d.items.isEmpty {
                        ItemGradesCard(items: d.items, counts: counts(d))
                    }
                    Fact(symbol: "info.circle", text: "Term GPA is worked out here from the scores \(engine.lmsName) reports, on a 4.0 scale with every course counting equally. It is not your school’s official GPA.")
                        .padding(.horizontal, 6)
                }
            } else {
                LoadState(error: error) { Task { await load() } }
            }
        }
        .navigationTitle("Grades")
        .navigationSubtitle(data?.term ?? "")
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: engine.canvasURL(for: .grades)?.absoluteString, title: "Grades")
            }
        }
        .task(id: engine.dataVersion) { await load() }
        .onChange(of: goal) { _, g in goalChanged(g) }
    }

    private func headline(_ d: GradesData) -> String {
        var parts: [String] = []
        if let t = d.term, !t.isEmpty { parts.append(t) }
        parts.append(d.rows.count == 1 ? "1 course" : "\(d.rows.count) courses")
        if !d.items.isEmpty { parts.append(d.items.count == 1 ? "1 graded item" : "\(d.items.count) graded items") }
        return parts.joined(separator: " · ")
    }

    private func counts(_ d: GradesData) -> [String: Int] {
        if let c = d.counts { return c }
        var out: [String: Int] = [:]
        for item in d.items { out[item.band, default: 0] += 1 }
        return out
    }

    // MARK: - The term

    /// The term GPA, the goal and the trend: side by side on a wide window, the trend under the other two on a narrow one.
    @ViewBuilder
    private func summary(_ d: GradesData) -> some View {
        let points = GradesView.trendPoints(d.trend ?? [])
        if points.count >= 2 {
            let layout = wide ? AnyLayout(HStackLayout(alignment: .top, spacing: 18)) : AnyLayout(VStackLayout(alignment: .leading, spacing: 18))
            layout {
                HStack(alignment: .top, spacing: 18) {
                    gpaCard(d)
                    goalCard(d)
                }
                .frame(width: wide ? 540 : nil)
                TrendCard(points: points, range: GradesView.domain(d), goal: goal)
            }
            .fixedSize(horizontal: false, vertical: true) // (the cards of a row as tall as the tallest)
            .modifier(WidthProbe(threshold: 940, wide: $wide))
        } else {
            HStack(alignment: .top, spacing: 18) {
                gpaCard(d)
                goalCard(d)
            }
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func gpaCard(_ d: GradesData) -> some View {
        let g = d.gpa
        let diff = g.map { $0 - goal }
        let scored = d.rows.filter { $0.pct != nil }.count
        let counted: String = scored == 0 ? "No course has a score yet." : "From \(scored) \(scored == 1 ? "course" : "courses"), each counting equally."
        let barColor: Color = diff.map { $0 >= 0 ? Color.green : Color.orange } ?? Color.accentColor
        return VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: "Term GPA", trailing: "4.0 scale")
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(g.map { String(format: "%.2f", $0) } ?? "—")
                    .font(.system(size: 44, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .contentTransition(.numericText(value: g ?? 0))
                Text("of 4.00")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            GradeBar(value: g.map { $0 / 4 }, color: barColor, height: 8, mark: goal / 4, key: "grades:gpa")
            Text(counted)
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card()
        .accessibilityElement(children: .combine)
    }

    private func goalCard(_ d: GradesData) -> some View {
        let diff = d.gpa.map { $0 - goal }
        return VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: "Goal", trailing: "saved as you change it")
            HStack(alignment: .center, spacing: 10) {
                Text(String(format: "%.2f", goal))
                    .font(.system(size: 44, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .contentTransition(.numericText(value: goal))
                    .accessibilityHidden(true)
                // (the above/below line rolls, and its colour and the bar's turn, with the goal)
                Stepper("Goal", value: $goal.animation(Motion.snappy), in: 0...4, step: 0.05)
                    .labelsHidden()
                    .accessibilityValue(String(format: "%.2f", goal))
                    .help("Raise or lower your goal")
                Spacer(minLength: 0)
            }
            if let diff {
                Label(diff >= 0 ? String(format: "+%.2f above goal", diff) : String(format: "%.2f below goal", abs(diff)),
                      systemImage: diff >= 0 ? "arrow.up.circle.fill" : "arrow.down.circle.fill")
                    .font(.sCallout.weight(.medium))
                    .foregroundStyle(diff >= 0 ? Color.green : Color.orange)
                    .contentTransition(.numericText(value: diff))
            } else {
                Text("No score to compare yet")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card()
    }

    private static let dayFormat: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    /// The page's snapshots as days, oldest first (one a day).
    private static func trendPoints(_ trend: [TrendPoint]) -> [DayGPA] {
        var seen = Set<Date>()
        var out: [DayGPA] = []
        for p in trend {
            guard p.gpa.isFinite, let day = dayFormat.date(from: p.date), seen.insert(day).inserted else { continue }
            out.append(DayGPA(id: day, gpa: p.gpa))
        }
        return out.sorted { $0.id < $1.id }
    }

    private static func domain(_ d: GradesData) -> ClosedRange<Double> {
        let a = d.minY ?? 2.4
        let b = d.maxY ?? 4
        let low = min(a, b)
        let high = max(a, b)
        return high > low ? low...high : low...(low + 1)
    }

    // MARK: - Courses

    private func courses(_ d: GradesData) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: "Courses", trailing: d.rows.isEmpty ? nil : "Click a course for each assignment’s grade and what-if scores")
                .padding(.horizontal, 6)
            if d.rows.isEmpty {
                EmptyNote(text: "No current courses.", symbol: "books.vertical")
                    .padding(.horizontal, 14)
                    .card()
            } else {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 300), spacing: 14)], spacing: 14) {
                    ForEach(d.rows) { r in courseCard(r) }
                }
            }
        }
    }

    private func courseCard(_ r: GradeRow) -> some View {
        let link = r.url ?? "/courses/\(r.id)/grades"
        return Button {
            engine.go(.section("courses/\(r.id)", "grades"))
        } label: {
            CourseGradeCard(row: r)
        }
        .buttonStyle(CardButtonStyle())
        .help("\(r.code): every assignment’s grade, and what-if scores")
        .accessibilityLabel(spoken(r))
        .contextMenu {
            Button { engine.go(.home("courses/\(r.id)")) } label: { Label("Open Course", systemImage: "arrow.up.right") }
            Button { engine.go(.section("courses/\(r.id)", "grades")) } label: { Label("Course Grades", systemImage: "chart.bar") }
            Divider()
            Button { engine.openWebScreen(link, title: r.code) } label: { Label("Open in \(engine.lmsName)", systemImage: "globe") }
            Button {
                if let u = engine.absolute(link) { copyToPasteboard(u.absoluteString) }
            } label: {
                Label("Copy Link", systemImage: "link")
            }
        }
    }

    private func spoken(_ r: GradeRow) -> String {
        let score = r.pct.map { String(format: "%.1f percent", $0) } ?? "no score"
        let letter = r.letter.map { ", \($0)" } ?? ""
        return "\(r.code), \(score)\(letter)"
    }

    // MARK: - Reading and saving

    private func load() async {
        do {
            let d = try await engine.call("grades", as: GradesData.self)
            var t = Transaction()
            // a redraw is not an arrival — but a term GPA that really changed rolls to its new value, as the rings beside it do
            if let old = data, old.gpa != d.gpa { t.animation = Motion.snappy } else { t.disablesAnimations = true }
            withTransaction(t) {
                if data == nil { goal = d.goal }
                data = d
                error = nil
            }
            // (the screenshot suite: -SimplOpen grades:101 opens that course's grades)
            if let id = LaunchOpen.take("grades:"), d.rows.contains(where: { $0.id == id }) {
                engine.go(.section("courses/\(id)", "grades"))
            }
        } catch {
            if data == nil { self.error = error.localizedDescription }
        }
    }

    /// The goal, saved half a second after the last change (a run of clicks on the stepper is one save).
    private func goalChanged(_ g: Double) {
        guard let saved = data?.goal, abs(g - saved) > 0.001 || goalSave != nil else { return }
        goalSave?.cancel()
        goalSave = Task {
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard !Task.isCancelled else { return }
            await engine.act("setGoal", ["goal": (g * 100).rounded() / 100])
        }
    }
}

/// A day of the term GPA's history.
private struct DayGPA: Identifiable, Equatable {
    let id: Date
    let gpa: Double
}

/// The term GPA's history, kept by the page day by day once tracking is on: a smooth line over its area, the goal as a
/// dashed rule. The pointer over it reads out that day's GPA.
private struct TrendCard: View {
    let points: [DayGPA]
    let range: ClosedRange<Double>
    let goal: Double
    @State private var hovered: DayGPA?

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            CardHeading(text: "Trend", trailing: readout)
            chart
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card()
    }

    private var showsGoal: Bool { goal > 0 && range.contains(goal) }

    private var readout: String {
        if let h = hovered {
            return "\(h.id.formatted(.dateTime.month(.abbreviated).day())) · \(String(format: "%.2f", h.gpa))"
        }
        let span = String(format: "%.2f – %.2f", range.lowerBound, range.upperBound)
        return showsGoal ? "\(span) · dashed is your goal" : span
    }

    /// A GPA drawn inside the chart's scale (the readout says the real one).
    private func shown(_ v: Double) -> Double { min(max(v, range.lowerBound), range.upperBound) }

    private var chart: some View {
        Chart {
            ForEach(points) { p in
                AreaMark(x: .value("Day", p.id), yStart: .value("Low", range.lowerBound), yEnd: .value("GPA", shown(p.gpa)))
                    .interpolationMethod(.monotone)
                    .foregroundStyle(LinearGradient(colors: [Color.accentColor.opacity(0.25), Color.accentColor.opacity(0)], startPoint: .top, endPoint: .bottom))
                LineMark(x: .value("Day", p.id), y: .value("GPA", shown(p.gpa)))
                    .interpolationMethod(.monotone)
                    .foregroundStyle(Color.accentColor)
                    .lineStyle(StrokeStyle(lineWidth: 2.2, lineCap: .round, lineJoin: .round))
            }
            if showsGoal {
                RuleMark(y: .value("Goal", goal))
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [4, 4]))
                    .foregroundStyle(Color.secondary)
            }
            if let h = hovered {
                RuleMark(x: .value("Day", h.id))
                    .lineStyle(StrokeStyle(lineWidth: 1))
                    .foregroundStyle(Color.secondary.opacity(0.5))
                PointMark(x: .value("Day", h.id), y: .value("GPA", shown(h.gpa)))
                    .foregroundStyle(Color.accentColor)
                    .symbolSize(70)
            }
        }
        .chartYScale(domain: range)
        .chartOverlay { proxy in
            GeometryReader { geo in
                Rectangle()
                    .fill(Color.clear)
                    .contentShape(Rectangle())
                    .onContinuousHover { phase in
                        switch phase {
                        case .active(let location):
                            hover(at: location, proxy: proxy, geo: geo)
                        case .ended:
                            hovered = nil
                        }
                    }
            }
        }
        .frame(height: 140)
    }

    private func hover(at location: CGPoint, proxy: ChartProxy, geo: GeometryProxy) {
        guard let plot = proxy.plotFrame else { return }
        let x = location.x - geo[plot].origin.x
        guard let day = proxy.value(atX: x, as: Date.self) else { return }
        let nearest = points.min { abs($0.id.timeIntervalSince(day)) < abs($1.id.timeIntervalSince(day)) }
        if nearest != hovered { hovered = nearest }
    }
}

/// A course's card on Grades: its rings (the total outermost, then its groups), its score and letter, its target, how
/// much of it is graded, and each group's weight and score as a bar.
private struct CourseGradeCard: View {
    let row: GradeRow

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header
            groups
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var header: some View {
        let color = Color(hex: row.color)
        let bands = NestedRings.bands(total: row.pct, color: color, groups: row.cats.map { (pct: $0.pct, color: $0.color) }, limit: 3)
        let graded: String = row.total > 0 ? "\(row.graded) of \(row.total) graded" : "Nothing graded yet"
        return HStack(alignment: .center, spacing: 14) {
            NestedRings(bands: bands, outerWidth: 7, innerWidth: 4.5, gap: 2, key: "grades:\(row.id)")
                .frame(width: 62, height: 62)
            VStack(alignment: .leading, spacing: 2) {
                Text(row.code)
                    .font(.sHeadline)
                    .lineLimit(1)
                if let name = row.name, !name.isEmpty, name != row.code {
                    Text(name)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Text(graded)
                    .font(.sCaption)
                    .foregroundStyle(.tertiary)
            }
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 4) {
                Text(row.pct.map { String(format: "%.1f%%", $0) } ?? "N/A")
                    .font(.sTitle2.weight(.semibold))
                    .monospacedDigit()
                    .foregroundStyle(row.pct == nil ? .secondary : .primary)
                    .contentTransition(.numericText(value: row.pct ?? 0))
                HStack(spacing: 6) {
                    if let t = row.target {
                        Text("Target \(t)")
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                    }
                    if let l = row.letter { LetterChip(letter: l) }
                }
            }
        }
    }

    @ViewBuilder
    private var groups: some View {
        let cats = Array(row.cats.prefix(4))
        let colors = categoryColors(cats)
        if cats.isEmpty {
            Text("No graded groups yet.")
                .font(.sCallout)
                .foregroundStyle(.tertiary)
        } else {
            VStack(alignment: .leading, spacing: 9) {
                ForEach(Array(cats.enumerated()), id: \.offset) { i, cat in
                    CategoryLine(name: cat.label, weight: cat.weight, value: cat.value, pct: cat.pct, color: colors[i], key: "grades:\(row.id)#cat\(i)")
                }
            }
        }
    }

    /// Each group's colour, as its ring has it (a group with no score yet has no ring: its own colour, or grey).
    private func categoryColors(_ cats: [GradeCategory]) -> [Color] {
        var out: [Color] = []
        var graded = 0
        for c in cats {
            if c.pct != nil {
                out.append(c.color.map { Color(hex: $0) } ?? NestedRings.palette[graded % NestedRings.palette.count])
                graded += 1
            } else {
                out.append(c.color.map { Color(hex: $0) } ?? Color.secondary)
            }
        }
        return out
    }
}

/// One assignment group's line: its colour, its name, its weight and its score, and a bar filled to the score.
private struct CategoryLine: View {
    let name: String
    let weight: String?
    let value: String?
    let pct: Double?
    let color: Color
    var key: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 7) {
                Circle().fill(color).frame(width: 8, height: 8)
                Text(name)
                    .font(.sCallout)
                    .lineLimit(1)
                Spacer(minLength: 6)
                if let weight, !weight.isEmpty {
                    Text(weight)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Text(value ?? "—")
                    .font(.sCallout.weight(.semibold))
                    .monospacedDigit()
                    .contentTransition(.numericText(value: pct ?? 0))
            }
            GradeBar(value: pct.map { $0 / 100 }, color: color, height: 5, key: key)
        }
        .accessibilityElement(children: .combine)
    }
}

/// Every graded assignment across the courses, by its letter: a switch of All, A, B, C, D and F with how many each holds,
/// and the work under it — the first dozen, then all of it. A row clicked opens its assignment.
private struct ItemGradesCard: View {
    let items: [GradeItem]
    let counts: [String: Int]
    @EnvironmentObject private var engine: Engine
    @State private var band = "all"
    @State private var showAll = false

    private static let letters = ["A", "B", "C", "D", "F"]
    private static let firstShown = 12

    private var total: Int { ItemGradesCard.letters.reduce(0) { $0 + (counts[$1] ?? 0) } }

    var body: some View {
        let picked = band == "all" ? items : items.filter { $0.band == band }
        let shown = showAll ? picked : Array(picked.prefix(ItemGradesCard.firstShown))
        CardSection(title: "Item Grades", trailing: "by letter") {
            picker
            if shown.isEmpty {
                EmptyNote(text: "No \(band) grades in your courses.", symbol: "line.3.horizontal.decrease.circle")
                    .padding(.horizontal, 8)
            }
            ForEach(Array(shown.enumerated()), id: \.element.id) { i, item in
                if i > 0 { RowDivider(inset: 30) }
                row(item)
            }
            more(picked.count, shown: shown.count)
        }
        .onChange(of: band) { _, _ in showAll = false }
    }

    private var picker: some View {
        Picker("Show grades by letter", selection: $band.animation(Motion.gentle)) {
            Text("All \(total)").tag("all")
            ForEach(ItemGradesCard.letters, id: \.self) { b in
                Text("\(b) \(counts[b] ?? 0)").tag(b)
            }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .frame(maxWidth: 460)
        .padding(.horizontal, 6)
        .padding(.bottom, 8)
    }

    private func row(_ item: GradeItem) -> some View {
        RowLink {
            engine.go(item.url, title: item.name)
        } label: {
            HStack(spacing: 12) {
                Circle()
                    .fill(Color(hex: item.color))
                    .frame(width: 10, height: 10)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.name).lineLimit(1)
                    Text("\(item.course) · \(item.pctText)")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer(minLength: 8)
                LetterChip(letter: item.band)
                Text(item.score)
                    .font(.sCallout.weight(.medium))
                    .monospacedDigit()
                    .frame(minWidth: 72, alignment: .trailing)
            }
        }
        .contextMenu {
            if let u = item.url, !u.isEmpty {
                Button("Open") { engine.go(u, title: item.name) }
                Divider()
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(u, title: item.name) }
                Button("Copy Link") { if let x = engine.absolute(u) { copyToPasteboard(x.absoluteString) } }
            }
        }
    }

    @ViewBuilder
    private func more(_ count: Int, shown: Int) -> some View {
        if count > shown {
            Button("Show All \(count)") { withAnimation(Motion.gentle) { showAll = true } }
                .buttonStyle(.link)
                .padding(.horizontal, 8)
                .padding(.top, 8)
        } else if showAll && count > ItemGradesCard.firstShown {
            Button("Show Fewer") { withAnimation(Motion.gentle) { showAll = false } }
                .buttonStyle(.link)
                .padding(.horizontal, 8)
                .padding(.top, 8)
        }
    }
}

// MARK: - One course's grades

/// How a course's assignments are listed: by their group, or as one list by grade, due date or name.
private enum GradeSort: String, CaseIterable, Identifiable {
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

/// A what-if assignment the student adds to a group: its name and what it is out of (its score is tried as any other's).
private struct AddedWork: Identifiable, Hashable {
    let id: String
    let groupId: String
    var name: String
    var possible: Double
}

/// One course's grades (its Grades section): the total and each assignment group as rings inside rings, each group's
/// weight and score, and every assignment's mark — by group, or sorted by grade, due date or name. With What-If on,
/// every mark becomes a field to type a score into and each group takes what-if assignments; the total, the letter and
/// the term GPA follow, worked out by the grade rules the web screens use. Nothing is saved.
struct CourseGradesView: View {
    let courseId: String
    @EnvironmentObject private var engine: Engine
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var data: CourseGradesData?
    @State private var error: String?
    @State private var whatIf = false
    @State private var drafts: [String: String] = [:]
    @State private var added: [AddedWork] = []
    @State private var sort: GradeSort = .groups
    @State private var addingTo: CGGroup?
    @State private var applying: Task<Void, Never>?
    @State private var reads = 0
    @State private var wide = true
    @FocusState private var focused: String?

    // (written out: the state above is of this file's own types, which would keep a synthesized one in this file)
    init(courseId: String) {
        self.courseId = courseId
    }

    var body: some View {
        Group {
            if let d = data {
                Page {
                    ScreenHeading(title: sectionTitle, sub: headline(d), color: Color(hex: d.color))
                    overview(d)
                    whatIfCard(d)
                    assignments(d)
                }
            } else {
                LoadState(error: error) { Task { await load(fresh: true) } }
            }
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            if whatIf, let d = data {
                WhatIfBar(total: d.totalText, value: d.total, letter: d.letter, gpaIf: d.gpaIf)
                    .transition(slide(.bottom))
            }
        }
        .navigationTitle(sectionTitle)
        .navigationSubtitle(data?.code ?? "")
        .toolbar { toolbarItems }
        .task(id: engine.dataVersion) { await load(fresh: true, animated: false) }
        .sheet(item: $addingTo) { g in
            AddWhatIfSheet(group: g, lmsName: engine.lmsName) { name, possible, score in
                addWhatIf(to: g, name: name, possible: possible, score: score)
            }
        }
    }

    @ToolbarContentBuilder
    private var toolbarItems: some ToolbarContent {
        ToolbarItem(placement: .primaryAction) {
            Toggle(isOn: Binding(get: { whatIf }, set: { setWhatIf($0) })) {
                Label("What-If", systemImage: "wand.and.stars")
            }
            .toggleStyle(.button)
            .disabled(data == nil)
            .help(whatIfTip)
        }
        ToolbarItem(placement: .primaryAction) {
            Menu {
                Picker("Sort By", selection: $sort.animation(Motion.snappy)) { // (each row slides to its new place)
                    ForEach(GradeSort.allCases) { s in
                        Label(s.rawValue, systemImage: s.symbol).tag(s)
                    }
                }
                .pickerStyle(.inline)
            } label: {
                Label("Sort", systemImage: "arrow.up.arrow.down")
            }
            .menuIndicator(.hidden)
            .help("Sort the assignments")
        }
        ToolbarItem(placement: .primaryAction) {
            CanvasMenu(url: "/courses/\(courseId)/grades", title: data.map { "\($0.code) · \(sectionTitle)" } ?? sectionTitle)
        }
    }

    /// The section's name as the course's own tabs give it (Grades, unless the course calls it otherwise).
    private var sectionTitle: String {
        engine.sections["courses/\(courseId)"]?.first(where: { $0.kind == "grades" })?.label ?? "Grades"
    }

    private var whatIfTip: String {
        whatIf ? "Turn What-If off: your real scores come back" : "What-If: try scores and see where your grade would land"
    }

    private func headline(_ d: CourseGradesData) -> String {
        let real = d.rows.filter { $0.added != true }
        let graded = real.filter { $0.earned != nil }.count
        var parts = [d.code]
        if let n = d.name, !n.isEmpty, n != d.code { parts.append(n) }
        if !real.isEmpty { parts.append("\(graded) of \(real.count) graded") }
        return parts.joined(separator: " · ")
    }

    private func slide(_ edge: Edge) -> AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .move(edge: edge))
    }

    // MARK: - The total and the groups

    /// The total and the groups: side by side on a wide window, one over the other on a narrow one.
    private func overview(_ d: CourseGradesData) -> some View {
        let colors = groupColors(d)
        let layout = wide ? AnyLayout(HStackLayout(alignment: .top, spacing: 18)) : AnyLayout(VStackLayout(alignment: .leading, spacing: 18))
        return layout {
            totalCard(d)
                .frame(width: wide ? 440 : nil)
            groupsCard(d, colors: colors)
        }
        .fixedSize(horizontal: false, vertical: true) // (the two cards as tall as the taller)
        .modifier(WidthProbe(threshold: 800, wide: $wide))
    }

    private func totalCard(_ d: CourseGradesData) -> some View {
        let color = Color(hex: d.color)
        let rings = NestedRings.bands(total: d.total, color: whatIf ? .orange : color, groups: d.groups.map { (pct: $0.pct, color: $0.color) }, limit: 4)
        return VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .center, spacing: 22) {
                NestedRings(bands: rings, outerWidth: 12, innerWidth: 7.5, gap: 2.5, key: "course-grades:\(courseId)")
                    .frame(width: 140, height: 140)
                VStack(alignment: .leading, spacing: 6) {
                    Text(d.totalText)
                        .font(.system(size: 38, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .contentTransition(.numericText(value: d.total ?? 0))
                    if let l = d.letter {
                        Text(whatIf ? "\(l) with what-if" : l)
                            .font(.sTitle3.weight(.semibold))
                            .foregroundStyle(whatIf ? Color.orange : color)
                    }
                    targetPicker(d)
                }
                Spacer(minLength: 0)
            }
            notes(d)
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card()
    }

    private func targetPicker(_ d: CourseGradesData) -> some View {
        Picker("Target", selection: Binding(get: { d.target ?? "" }, set: { setTarget($0) })) {
            Text("No Target").tag("")
            if let t = d.target, !d.scale.contains(where: { $0.letter == t }) {
                Text(t).tag(t) // (a target the scale has no letter for, as Pass/Fail: still shown as chosen)
            }
            ForEach(d.scale, id: \.letter) { s in
                Text(s.letter).tag(s.letter)
            }
        }
        .pickerStyle(.menu)
        .fixedSize()
        .help("The grade you are aiming for in this course")
    }

    @ViewBuilder
    private func notes(_ d: CourseGradesData) -> some View {
        if let note = d.note, !note.isEmpty {
            Text(note)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        if let f = d.final, !f.isEmpty {
            Text(f)
                .font(.sCaption)
                .foregroundStyle(.tertiary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func groupsCard(_ d: CourseGradesData, colors: [String: Color]) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            CardHeading(text: "Assignment Groups", trailing: d.weighted == true ? "Weighted" : nil)
                .padding(.horizontal, 6)
            if d.groups.isEmpty {
                EmptyNote(text: "No assignment groups.", symbol: "square.stack")
                    .padding(.horizontal, 6)
            }
            VStack(alignment: .leading, spacing: 11) {
                ForEach(d.groups) { g in
                    CategoryLine(name: g.name, weight: g.weightText, value: g.value, pct: g.pct, color: colors[g.id] ?? Color.secondary, key: "course-grades:\(courseId)#\(g.id)")
                }
            }
            .padding(.horizontal, 6)
            .padding(.top, 2)
        }
        .padding(14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card()
    }

    /// Each group's colour, as its ring has it: those with a score in order on the rings' palette (or their own colour),
    /// those without in their own colour or grey.
    private func groupColors(_ d: CourseGradesData) -> [String: Color] {
        var out: [String: Color] = [:]
        var graded = 0
        for g in d.groups {
            if g.pct != nil {
                out[g.id] = g.color.map { Color(hex: $0) } ?? NestedRings.palette[graded % NestedRings.palette.count]
                graded += 1
            } else {
                out[g.id] = g.color.map { Color(hex: $0) } ?? Color.secondary
            }
        }
        return out
    }

    // MARK: - What-if

    private func whatIfCard(_ d: CourseGradesData) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .center, spacing: 12) {
                IconTile(symbol: "wand.and.stars", color: .orange)
                VStack(alignment: .leading, spacing: 2) {
                    Text("What-If Scores")
                        .font(.sBody.weight(.semibold))
                    Text(whatIf ? "Type a score into any assignment, or add one. Nothing is saved." : "Try scores and see where your grade would land.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                Toggle("What-If Scores", isOn: Binding(get: { whatIf }, set: { setWhatIf($0) }))
                    .toggleStyle(.switch)
                    .labelsHidden()
                    .tint(.orange)
            }
            if whatIf && (d.gpaIf != nil || !drafts.isEmpty || !added.isEmpty) {
                HStack(spacing: 8) {
                    if let g = d.gpaIf {
                        Text("Term GPA would be")
                        Text(String(format: "%.2f", g))
                            .bold()
                            .monospacedDigit()
                            .contentTransition(.numericText(value: g))
                        if let now = d.gpa {
                            Text(String(format: "(now %.2f)", now))
                                .foregroundStyle(.secondary)
                        }
                    }
                    Spacer(minLength: 8)
                    if !drafts.isEmpty || !added.isEmpty {
                        Button(role: .destructive) { reset() } label: {
                            Label("Clear What-If Scores", systemImage: "arrow.uturn.backward")
                        }
                    }
                }
                .font(.sCallout)
                .padding(.leading, 42)
                .transition(slide(.top))
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(tint: whatIf ? .orange : nil)
    }

    // MARK: - The assignments

    @ViewBuilder
    private func assignments(_ d: CourseGradesData) -> some View {
        let colors = groupColors(d)
        if d.rows.isEmpty && !whatIf {
            CardSection(title: "Assignments") {
                EmptyNote(text: "Nothing with points in this course yet.", symbol: "doc.text")
                    .padding(.horizontal, 8)
            }
        } else if sort == .groups {
            ForEach(d.groups) { g in
                let rows = d.rows.filter { $0.groupId == g.id }
                if !rows.isEmpty || whatIf {
                    groupSection(g, rows: rows, d, colors: colors)
                }
            }
        } else {
            CardSection(title: "All Assignments", trailing: sort.rawValue) {
                rowList(sorted(d), d, colors: colors, showGroup: true)
                if whatIf && !d.groups.isEmpty { addMenu(d) }
            }
        }
    }

    private func groupSection(_ g: CGGroup, rows: [CGRow], _ d: CourseGradesData, colors: [String: Color]) -> some View {
        let meta = [g.weightText, g.value].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")
        return CardSection(title: g.name, trailing: meta) {
            rowList(rows, d, colors: colors, showGroup: false)
            if whatIf {
                Button { addingTo = g } label: {
                    Label("Add What-If Assignment", systemImage: "plus.circle")
                }
                .buttonStyle(.borderless)
                .padding(.horizontal, 8)
                .padding(.top, rows.isEmpty ? 2 : 8)
                .transition(slide(.top))
            }
            if let detail = g.detail, !detail.isEmpty {
                Text(detail)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 8)
                    .padding(.top, 8)
            }
        }
    }

    /// In a sorted list, a what-if assignment is added to the group picked here.
    private func addMenu(_ d: CourseGradesData) -> some View {
        Menu {
            ForEach(d.groups) { g in
                Button(g.name) { addingTo = g }
            }
        } label: {
            Label("Add What-If Assignment", systemImage: "plus.circle")
        }
        .menuStyle(.button)
        .buttonStyle(.borderless)
        .fixedSize()
        .padding(.horizontal, 8)
        .padding(.top, 8)
    }

    @ViewBuilder
    private func rowList(_ rows: [CGRow], _ d: CourseGradesData, colors: [String: Color], showGroup: Bool) -> some View {
        ForEach(Array(rows.enumerated()), id: \.element.id) { i, r in
            if i > 0 { RowDivider() }
            row(r, d, color: colors[r.groupId] ?? Color(hex: d.color), showGroup: showGroup)
        }
    }

    @ViewBuilder
    private func row(_ r: CGRow, _ d: CourseGradesData, color: Color, showGroup: Bool) -> some View {
        if whatIf {
            rowContent(r, d, color: color, showGroup: showGroup) // (the score field takes the clicks)
                .padding(.horizontal, 8)
                .padding(.vertical, 7)
                .contextMenu { rowMenu(r) }
        } else {
            RowLink {
                if let u = r.url { engine.go(u, title: r.name) }
            } label: {
                rowContent(r, d, color: color, showGroup: showGroup)
            }
            .contextMenu { rowMenu(r) }
        }
    }

    private func rowContent(_ r: CGRow, _ d: CourseGradesData, color: Color, showGroup: Bool) -> some View {
        let tried = r.hypothetical == true || r.added == true
        let group = showGroup ? d.groups.first(where: { $0.id == r.groupId })?.name : nil
        return HStack(spacing: 12) {
            IconTile(symbol: r.added == true ? "wand.and.stars" : "doc.text", color: r.added == true ? .orange : color, size: 28)
            VStack(alignment: .leading, spacing: 3) {
                Text(r.name)
                    .lineLimit(2)
                    .foregroundStyle(r.dropped == true ? .secondary : .primary)
                facts(r, group: group)
            }
            Spacer(minLength: 8)
            if whatIf && r.counted != false {
                scoreField(r, tried: tried)
            } else {
                score(r, d)
            }
            if whatIf { rowAction(r) }
        }
        .contentShape(Rectangle())
    }

    /// Under an assignment's name: Missing, Late or Excused, Dropped, its group (in a sorted list) and when it is due.
    @ViewBuilder
    private func facts(_ r: CGRow, group: String?) -> some View {
        let badge = (r.badge ?? "").isEmpty ? nil : r.badge
        let line = [group, r.dueText].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · ")
        if badge != nil || r.dropped == true || !line.isEmpty {
            HStack(spacing: 6) {
                if let badge { StatusChip(text: badge, tone: CourseGradesView.tone(badge)) }
                if r.dropped == true { StatusChip(text: "Dropped") }
                if !line.isEmpty {
                    Text(line)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
    }

    private func score(_ r: CGRow, _ d: CourseGradesData) -> some View {
        VStack(alignment: .trailing, spacing: 1) {
            HStack(spacing: 6) {
                if let l = letter(r, d) { LetterChip(letter: l) }
                Text(r.scoreText)
                    .font(.sBody.weight(.medium))
                    .monospacedDigit()
                    .foregroundStyle(r.effective == nil ? Color.secondary : Color.primary)
                    .contentTransition(.numericText(value: r.effective ?? 0))
            }
            if let g = r.grade, !g.isEmpty {
                Text(g)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
        }
        .frame(minWidth: 120, alignment: .trailing)
    }

    /// A what-if score typed straight into the row: the score, then what it is out of. Return tries it at once.
    private func scoreField(_ r: CGRow, tried: Bool) -> some View {
        let real = r.effective.map { CourseGradesView.num($0) } ?? ""
        return HStack(spacing: 6) {
            TextField("—", text: Binding(
                get: { drafts[r.id] ?? real },
                set: { v in
                    guard v != (drafts[r.id] ?? real) else { return } // (a field ending its edit hands back what it had: no score tried)
                    drafts[r.id] = v
                    scheduleApply()
                }
            ))
            .textFieldStyle(.roundedBorder)
            .multilineTextAlignment(.trailing)
            .font(.sBody.weight(tried ? .semibold : .regular).monospacedDigit())
            .foregroundStyle(tried ? Color.orange : Color.primary)
            .focused($focused, equals: r.id)
            .frame(width: 66)
            .onSubmit { applyNow() }
            .onExitCommand { focused = nil }
            .accessibilityLabel("What-if score for \(r.name), out of \(CourseGradesView.num(r.possible))")
            Text("/ \(CourseGradesView.num(r.possible))")
                .font(.sCallout)
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(minWidth: 44, alignment: .leading)
                .accessibilityHidden(true)
        }
        .frame(minWidth: 120, alignment: .trailing)
    }

    /// While What-If is on, a row's own undo: remove a what-if assignment, or go back to the real score.
    @ViewBuilder
    private func rowAction(_ r: CGRow) -> some View {
        if r.added == true {
            Button { remove(r) } label: {
                Image(systemName: "minus.circle.fill").foregroundStyle(.secondary)
            }
            .buttonStyle(.borderless)
            .frame(width: 22, height: 22)
            .help("Remove this what-if assignment")
            .accessibilityLabel("Remove \(r.name)")
        } else if drafts[r.id] != nil {
            Button { useReal(r) } label: {
                Image(systemName: "arrow.uturn.backward.circle").foregroundStyle(.secondary)
            }
            .buttonStyle(.borderless)
            .frame(width: 22, height: 22)
            .help("Use my real score")
            .accessibilityLabel("Use my real score for \(r.name)")
        } else {
            Color.clear.frame(width: 22, height: 22)
        }
    }

    @ViewBuilder
    private func rowMenu(_ r: CGRow) -> some View {
        if let u = r.url {
            Button("Open Assignment") { engine.go(u, title: r.name) }
        }
        if r.added == true {
            Button("Remove What-If Assignment") { remove(r) }
        } else if drafts[r.id] != nil {
            Button("Use My Real Score") { useReal(r) }
        }
        if let u = r.url {
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(u, title: r.name) }
            Button("Copy Link") { if let x = engine.absolute(u) { copyToPasteboard(x.absoluteString) } }
        }
    }

    private func sorted(_ d: CourseGradesData) -> [CGRow] {
        func pct(_ r: CGRow) -> Double? {
            guard let e = r.effective, r.possible > 0 else { return nil }
            return e / r.possible * 100
        }
        switch sort {
        case .high, .low:
            let highest = sort == .high
            var graded: [(row: CGRow, pct: Double)] = []
            for r in d.rows {
                if let p = pct(r) { graded.append((row: r, pct: p)) }
            }
            graded.sort { highest ? $0.pct > $1.pct : $0.pct < $1.pct }
            return graded.map { $0.row } + d.rows.filter { pct($0) == nil }
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
        return d.scale.first(where: { p >= $0.min })?.letter ?? d.scale.last?.letter
    }

    /// Points as Canvas writes them: whole numbers bare, others as short as they go.
    static func num(_ v: Double) -> String {
        !v.isFinite ? "—" : v == v.rounded() && abs(v) < 1e15 ? String(Int(v)) : String(format: "%g", v)
    }

    private static func tone(_ badge: String) -> String {
        switch badge {
        case "Missing": return "bad"
        case "Late": return "warn"
        case "Excused": return "info"
        default: return ""
        }
    }

    // MARK: - Doing

    private func setWhatIf(_ on: Bool) {
        guard on != whatIf else { return }
        applying?.cancel()
        if !on { focused = nil }
        withAnimation(Motion.snappy) {
            whatIf = on
            if !on {
                drafts = [:]
                added = []
            }
        }
        Task { await load() }
    }

    /// A score typed: tried once typing pauses.
    private func scheduleApply() {
        applying?.cancel()
        applying = Task {
            try? await Task.sleep(nanoseconds: 450_000_000)
            guard !Task.isCancelled else { return }
            await load()
        }
    }

    private func applyNow() {
        applying?.cancel()
        applying = nil
        Task { await load() }
    }

    private func useReal(_ r: CGRow) {
        drafts.removeValue(forKey: r.id)
        Task { await load() }
    }

    private func remove(_ r: CGRow) {
        added.removeAll { $0.id == r.id }
        drafts.removeValue(forKey: r.id)
        Task { await load() }
    }

    private func reset() {
        focused = nil
        applying?.cancel()
        drafts = [:]
        added = []
        Task { await load() }
    }

    private func addWhatIf(to g: CGGroup, name: String, possible: Double, score: Double?) {
        let id = "whatif-\(UUID().uuidString.prefix(8))"
        added.append(AddedWork(id: id, groupId: g.id, name: name, possible: possible))
        drafts[id] = score.map { CourseGradesView.num($0) } ?? ""
        Task { await load() }
    }

    private func setTarget(_ letter: String) {
        let before = data?.target
        let next: String? = letter.isEmpty ? nil : letter
        guard before != next else { return }
        withAnimation(Motion.snappy) { data?.target = next }
        Task {
            let ok = await engine.act("setTarget", letter.isEmpty ? ["id": courseId] : ["id": courseId, "letter": letter])
            if !ok { withAnimation(Motion.snappy) { data?.target = before } }
            await load(fresh: true) // (the page keeps a course's target with its groups: read afresh, or the old one comes back)
            engine.changed()
        }
    }

    /// The course's grades with the scores tried and the assignments added. `animated`: the answer to something just done
    /// (rings move, numbers roll); a first read or a reload is a redraw. Only the latest read is shown.
    private func load(fresh: Bool = false, animated: Bool = true) async {
        reads += 1
        let read = reads
        var tried: [String: Any] = [:]
        for (k, v) in drafts {
            tried[k] = v.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)
        }
        let extra: [[String: Any]] = added.map { ["id": $0.id, "groupId": $0.groupId, "name": $0.name, "possible": $0.possible] }
        do {
            let d = try await engine.call("courseGrades", ["id": courseId, "tried": tried, "added": extra, "on": whatIf, "fresh": fresh], as: CourseGradesData.self)
            guard read == reads else { return }
            if animated && data != nil {
                withAnimation(Motion.snappy) {
                    data = d
                    error = nil
                }
            } else {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) {
                    data = d
                    error = nil
                }
            }
        } catch {
            guard read == reads else { return }
            if data == nil { self.error = error.localizedDescription }
        }
    }
}

/// While What-If is on, the total it gives (and the term GPA it would make) at the window's foot, so it stays in sight
/// while scores are typed far down the list.
private struct WhatIfBar: View {
    let total: String
    let value: Double?
    let letter: String?
    let gpaIf: Double?

    var body: some View {
        HStack(spacing: 10) {
            Label("What-If", systemImage: "wand.and.stars")
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(.orange)
            Text(total)
                .font(.sCallout.weight(.bold))
                .monospacedDigit()
                .contentTransition(.numericText(value: value ?? 0))
            if let letter { LetterChip(letter: letter) }
            if let g = gpaIf {
                Divider().frame(height: 14)
                Text("Term GPA \(String(format: "%.2f", g))")
                    .font(.sCallout)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: g))
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 9)
        .background(.regularMaterial, in: Capsule())
        .overlay(Capsule().strokeBorder(Theme.edge))
        .shadow(color: Theme.shadow, radius: 12, y: 4)
        .padding(.bottom, 14)
        .accessibilityElement(children: .combine)
    }
}

/// A what-if assignment for a group: its name, the score to try and what it is out of, in a small sheet. Nothing is
/// saved to Canvas.
private struct AddWhatIfSheet: View {
    let group: CGGroup
    /// The school's site by name (Engine.lmsName), for the note that nothing is saved to it.
    let lmsName: String
    let add: (String, Double, Double?) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var name = ""
    @State private var score = ""
    @State private var possible = "100"
    @FocusState private var focus: Field?

    /// The sheet's fields, for the one that starts focused.
    private enum Field { case name, score, possible }

    init(group: CGGroup, lmsName: String, add: @escaping (String, Double, Double?) -> Void) {
        self.group = group
        self.lmsName = lmsName
        self.add = add
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    TextField("Name", text: $name, prompt: Text("What-if assignment"))
                        .focused($focus, equals: .name)
                    TextField("Score", text: $score, prompt: Text("Optional"))
                        .focused($focus, equals: .score)
                    TextField("Out of", text: $possible, prompt: Text("Points"))
                        .focused($focus, equals: .possible)
                } header: {
                    Text("What-If Assignment")
                } footer: {
                    Text(footnote)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .formStyle(.grouped)
            HStack {
                Spacer()
                Button("Cancel", role: .cancel) { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("Add") { submit() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(points == nil)
            }
            .padding(.horizontal, 20)
            .padding(.bottom, 18)
        }
        .frame(minWidth: 400, idealWidth: 440, minHeight: 300, idealHeight: 320)
        .onAppear { focus = .name }
    }

    private static func number(_ s: String) -> Double? {
        guard let v = Double(s.replacingOccurrences(of: ",", with: ".").trimmingCharacters(in: .whitespaces)), v.isFinite else { return nil }
        return v
    }

    /// What it is out of, when that is more than nothing.
    private var points: Double? {
        guard let p = AddWhatIfSheet.number(possible), p > 0 else { return nil }
        return p
    }

    private var footnote: String {
        let weight = group.weightText.map { $0.isEmpty ? "" : " (\($0))" } ?? ""
        let counts = "Counts in \(group.name)\(weight). Nothing is saved to \(lmsName)."
        guard let p = points, let s = AddWhatIfSheet.number(score) else { return counts }
        return String(format: "%.1f%%. ", s / p * 100) + counts
    }

    private func submit() {
        guard let p = points else { return }
        let n = name.trimmingCharacters(in: .whitespaces)
        add(n.isEmpty ? "What-if assignment" : n, p, AddWhatIfSheet.number(score))
        dismiss()
    }
}

// MARK: - Pieces

/// A letter grade on a wash of its letter's colour, as the web's grade tags.
private struct LetterChip: View {
    let letter: String

    var body: some View {
        let color = LetterChip.color(String(letter.prefix(1)))
        Text(letter)
            .font(.sCaption.weight(.bold))
            .lineLimit(1)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .frame(minWidth: 26)
            .foregroundStyle(color)
            .background(color.opacity(0.15), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
    }

    /// A letter's colour: A green, B teal, C yellow (darker in light, to be read on white), D orange, F red; anything
    /// else (Pass, Complete) grey.
    static func color(_ band: String) -> Color {
        switch band {
        case "A": return .green
        case "B": return .teal
        case "C": return yellow
        case "D": return .orange
        case "F", "E": return .red
        default: return .secondary
        }
    }

    private static let yellow = Theme.dynamic(light: NSColor(srgbRed: 0.72, green: 0.53, blue: 0, alpha: 1), dark: .systemYellow)
}

/// A bar filled to a share (a group's score, the GPA out of 4) in its colour. It fills once, on the house spring, when it
/// first appears, and moves from where it is when the share changes, as the rings do. `mark` draws a tick across it
/// (the goal).
private struct GradeBar: View {
    let value: Double? // 0…1; nil draws the track alone
    let color: Color
    var height: CGFloat = 6
    var mark: Double? = nil
    var key: String? = nil
    @MainActor private static var filled = Set<String>()
    @State private var shown: Double = 0
    @State private var appeared = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var target: Double { min(max(value ?? 0, 0), 1) }

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            ZStack(alignment: .leading) {
                Capsule().fill(color.opacity(0.16))
                Capsule()
                    .fill(color)
                    .frame(width: shown > 0 ? max(w * CGFloat(shown), height) : 0)
            }
            .frame(width: w, height: geo.size.height, alignment: .leading)
            .overlay(alignment: .leading) {
                if let mark {
                    Capsule()
                        .fill(Color.primary.opacity(0.55))
                        .frame(width: 2, height: height + 6)
                        .offset(x: GradeBar.offset(of: mark, in: w))
                }
            }
        }
        .frame(height: height)
        .onAppear {
            guard !appeared else { return }
            appeared = true
            let seen = key.map { !GradeBar.filled.insert($0).inserted } ?? false
            if reduceMotion || seen { shown = target } else { withAnimation(Motion.fill.delay(0.05)) { shown = target } }
        }
        .onChange(of: target) { _, t in
            if reduceMotion { shown = t } else { withAnimation(.spring(response: 0.5, dampingFraction: 0.85)) { shown = t } }
        }
        .accessibilityHidden(true)
    }

    /// Where the tick for `mark` (0…1) stands on a bar `width` wide: centred on its place, never past either end.
    private static func offset(of mark: Double, in width: CGFloat) -> CGFloat {
        let share = CGFloat(min(max(mark, 0), 1))
        let x = width * share - 1
        return min(max(x, 0), max(width - 2, 0))
    }
}

/// Whether the column is wide enough for its cards side by side: read from the width it is given, and again as the
/// window is resized.
private struct WidthProbe: ViewModifier {
    let threshold: CGFloat
    @Binding var wide: Bool

    func body(content: Content) -> some View {
        content.background {
            GeometryReader { geo in
                Color.clear
                    .onAppear { update(geo.size.width) }
                    .onChange(of: geo.size.width) { _, w in update(w) }
            }
        }
    }

    private func update(_ width: CGFloat) {
        let w = width >= threshold
        if w != wide { wide = w }
    }
}
