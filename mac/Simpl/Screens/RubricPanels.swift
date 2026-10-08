import SwiftUI

// The rubric on an assignment's page (1.2): the ring (RubricRing.swift) with the criterion picked beside it — its
// levels up a bar at the height of their points, the one given marked, the marker's note — or the same rubric as a grid
// of criteria and levels. A switch at the section's head turns between the two; the choice is kept for the next rubric.

/// An assignment's rubric as a section of its page: its title, its score, the Ring / Grid switch, and the rubric.
struct RubricSection: View {
    let data: AssignmentData
    /// The screenshot suite's way in: ":2" opens the second criterion, ":grid" shows the grid (not kept).
    var shot: String? = nil
    @AppStorage("rubricView") private var view = "ring"
    @State private var selected: Int?
    @State private var forced: String?

    var body: some View {
        let model = RubricModel(rows: data.rubric)
        let showing = forced ?? view
        return PageSection(title: title, trailing: trailing(model), accessory: {
            RubricViewSwitch(view: Binding(get: { forced ?? view }, set: { forced = nil; view = $0 }))
        }) {
            if showing == "grid" {
                RubricGrid(model: model)
                    .transition(.opacity)
            } else {
                ringAndDetail(model)
                    .transition(.opacity)
            }
        }
        .animation(Motion.gentle, value: showing)
        .onAppear(perform: takeShot)
        .onChange(of: shot) { _, _ in takeShot() }
    }

    private var title: String {
        let t = (data.rubricTitle ?? "").trimmingCharacters(in: .whitespaces)
        return t.isEmpty ? "Rubric" : t
    }

    private func trailing(_ m: RubricModel) -> String {
        if !m.graded && data.held == true { return "\(m.summary) · marks not posted yet" }
        return "\(m.summary) · \(m.n == 1 ? "1 criterion" : "\(m.n) criteria")"
    }

    /// The ring with the criterion beside it on a wide page; under it on a narrow one (the ring smaller as it narrows).
    private func ringAndDetail(_ m: RubricModel) -> some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 26) {
                RubricRingView(model: m, stage: .large, selected: $selected)
                detail(m)
            }
            stacked(m, .large)
            stacked(m, .medium)
            stacked(m, .small)
        }
    }

    private func stacked(_ m: RubricModel, _ stage: RingStage) -> some View {
        VStack(alignment: .center, spacing: 18) {
            RubricRingView(model: m, stage: stage, selected: $selected)
            detail(m)
        }
    }

    private func detail(_ m: RubricModel) -> some View {
        RubricDetail(model: m, held: data.held == true, selected: $selected)
            .frame(minWidth: 300, idealWidth: 320, maxWidth: .infinity, alignment: .topLeading)
    }

    private func takeShot() {
        guard let shot else { return }
        if shot == ":grid" {
            forced = "grid"
        } else if let k = Int(shot.dropFirst()), k >= 1, k <= data.rubric.count {
            forced = "ring"
            selected = k - 1
        }
    }
}

/// Ring or Grid: two words in a glass capsule, the one showing lit.
struct RubricViewSwitch: View {
    @Binding var view: String
    @Namespace private var knob

    var body: some View {
        HStack(spacing: 2) {
            option("ring", "Ring", "circle.dashed")
            option("grid", "Grid", "square.grid.2x2")
        }
        .padding(3)
        .glassCapsule()
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Show the rubric as")
    }

    private func option(_ key: String, _ title: String, _ symbol: String) -> some View {
        let on = view == key
        return Button {
            withAnimation(Motion.snappy) { view = key }
        } label: {
            Label(title, systemImage: symbol)
                .font(.sCallout.weight(.semibold))
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .foregroundStyle(on ? Color.primary : Color.secondary)
                .background {
                    if on {
                        Capsule()
                            .fill(Color.primary.opacity(0.1))
                            .matchedGeometryEffect(id: "knob", in: knob)
                    }
                }
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .help("Show the rubric as a \(title.lowercased())")
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

// MARK: - Beside the ring

/// Beside the ring: with nothing picked, every criterion in a line (its colour, its name, how far it got, its points);
/// with one picked, that criterion — which of how many, its name and description, its levels up a bar at the height of
/// their points with the one given marked, and the marker's note — with ‹ › and the dots to move along.
struct RubricDetail: View {
    let model: RubricModel
    var held = false
    @Binding var selected: Int?

    var body: some View {
        Group {
            if let k = selected, k < model.n {
                CriterionDetail(model: model, k: k, selected: $selected)
                    .id(k)
                    .transition(.opacity)
            } else {
                overview
                    .transition(.opacity)
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .card(radius: 20)
    }

    private var overview: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(model.graded ? "How it was marked" : "How it will be marked")
                .font(.sHeadline)
            Text(hint)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 2)
                .padding(.bottom, 10)
            ForEach(model.criteria, id: \.index) { c in
                if c.index > 0 { RowDivider(inset: 32) }
                Button {
                    withAnimation(Motion.gentle) { selected = c.index }
                } label: {
                    overviewRow(c)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 8)
                }
                .buttonStyle(RowButtonStyle())
                .accessibilityLabel(model.spoken(c))
                .accessibilityHint("Shows this criterion’s levels and comments")
            }
        }
    }

    private var hint: String {
        if model.graded { return "The ring pushes out where you scored well and pulls in where you lost points. Pick a criterion to see how it was marked." }
        if held { return "Marked, but your teacher has not posted the marks yet." }
        return "Each colour’s stretch is its share of the points. Pick a criterion to see its levels."
    }

    private func overviewRow(_ c: RubricModel.Criterion) -> some View {
        let colour = model.colors[c.index]
        return HStack(spacing: 12) {
            Circle()
                .fill(colour.color)
                .frame(width: 12, height: 12)
            VStack(alignment: .leading, spacing: 5) {
                Text(c.name)
                    .font(.sBody)
                    .lineLimit(2)
                    .multilineTextAlignment(.leading)
                if model.graded {
                    LevelTrack(fraction: c.score == nil ? 0 : c.frac, color: colour.color)
                }
            }
            Spacer(minLength: 8)
            Text(model.pointsLabel(c))
                .font(.sCallout.monospacedDigit())
                .foregroundStyle(.secondary)
                .lineLimit(1)
            Image(systemName: "chevron.right")
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.tertiary)
        }
        .contentShape(Rectangle())
    }
}

/// A thin track filled as far as a mark reached.
struct LevelTrack: View {
    let fraction: Double
    let color: Color

    var body: some View {
        Capsule()
            .fill(Theme.well)
            .frame(height: 5)
            .overlay(alignment: .leading) {
                GeometryReader { g in
                    Capsule()
                        .fill(color)
                        .frame(width: CGFloat(max(0, min(1, fraction))) * g.size.width)
                }
            }
            .accessibilityHidden(true)
    }
}

/// One criterion, picked from the ring.
private struct CriterionDetail: View {
    let model: RubricModel
    let k: Int
    @Binding var selected: Int?
    @State private var more = false

    private var c: RubricModel.Criterion { model.criteria[k] }
    private var colour: RingRGB { model.colors[k] }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            head
            dots
            Text(c.name)
                .font(.sTitle3)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            if !c.desc.isEmpty { descriptionText }
            ladder
            if !c.comment.isEmpty { note }
        }
    }

    private var ladder: some View {
        let layout = LevelLadder(positions: positions)
        let marked = model.graded && c.score != nil
        return layout {
            ForEach(Array(levels.enumerated()), id: \.offset) { i, l in
                LevelRow(level: l, picked: isPicked(i), dim: marked && !isPicked(i), colour: colour)
            }
        }
        .background(alignment: .topLeading) { LevelBar(fraction: marked ? c.frac : 1, colour: colour) }
    }

    private var head: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(colour.color)
                .frame(width: 12, height: 12)
            Text("Criterion \(k + 1) of \(model.n) · \(model.pointsLabel(c))")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(1)
            Spacer(minLength: 8)
            GlassGroup(spacing: 6) {
                HStack(spacing: 6) {
                    roundButton("chevron.left", "Previous criterion") { go(-1) }
                    roundButton("chevron.right", "Next criterion") { go(1) }
                    roundButton("xmark", "All criteria") { withAnimation(Motion.gentle) { selected = nil } }
                }
            }
        }
    }

    /// A dot per criterion, in its colour: the one showing larger; a press moves to another.
    private var dots: some View {
        HStack(spacing: model.n > 15 ? 3 : 6) {
            ForEach(model.criteria, id: \.index) { other in
                let on = other.index == k
                Button {
                    withAnimation(Motion.gentle) { selected = other.index }
                } label: {
                    Circle()
                        .fill(model.colors[other.index].color)
                        .frame(width: on ? 12 : 8, height: on ? 12 : 8)
                        .overlay {
                            if on { Circle().strokeBorder(Color.primary.opacity(0.35), lineWidth: 1.5).frame(width: 18, height: 18) }
                        }
                        .frame(width: 18, height: 18)
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .help(other.name)
                .accessibilityLabel("\(other.name), criterion \(other.index + 1) of \(model.n)")
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }

    private var descriptionText: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(c.desc)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(more ? nil : 3)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            if c.desc.count > 180 {
                Button(more ? "Less" : "More") {
                    withAnimation(Motion.gentle) { more.toggle() }
                }
                .buttonStyle(.link)
                .font(.sCallout)
            }
        }
    }

    /// The marker's note on this criterion.
    private var note: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "text.bubble.fill")
                .font(.sBody)
                .foregroundStyle(colour.color)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Comment")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
                Text(c.comment)
                    .font(.sBody)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
            }
        }
        .padding(.top, 4)
        .accessibilityElement(children: .combine)
    }

    private func roundButton(_ symbol: String, _ help: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.sCallout.weight(.semibold))
                .frame(width: 30, height: 30)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .glass(Circle(), interactive: true)
        .help(help)
        .accessibilityLabel(help)
    }

    private func go(_ d: Int) {
        let next = ((k + d) % model.n + model.n) % model.n
        withAnimation(Motion.gentle) { selected = next }
    }

    /// Its levels — or, marked freely, the one score (or what it is worth) standing alone.
    private var levels: [RubricModel.Level] {
        if !c.levels.isEmpty { return c.levels }
        if model.graded, let s = c.score {
            return [RubricModel.Level(label: "Your score", text: "Out of \(RubricModel.num(c.worth)) — marked without set levels.", pts: s)]
        }
        return [RubricModel.Level(label: "Marked freely", text: "No set levels: your teacher gives a score up to this.", pts: c.worth)]
    }

    private var positions: [Double] {
        c.levels.isEmpty ? [c.score == nil ? 1 : c.frac] : RubricModel.positions(c.levels)
    }

    private func isPicked(_ i: Int) -> Bool {
        guard model.graded else { return false }
        return c.levels.isEmpty ? c.score != nil : c.mark == i
    }
}

/// A level of a criterion: its points, its name (and "Your mark" on the one given), what it asks for; a tick on the
/// bar beside it.
private struct LevelRow: View {
    let level: RubricModel.Level
    let picked: Bool
    let dim: Bool
    let colour: RingRGB

    var body: some View {
        HStack(alignment: .center, spacing: 10) {
            tick
                .frame(width: LevelBar.gutter)
            Text(level.pts.map { RubricModel.num($0) } ?? "–")
                .font(.sTitle3.monospacedDigit())
                .foregroundStyle(picked ? colour.color : Color.primary)
                .frame(minWidth: 34, alignment: .trailing)
            VStack(alignment: .leading, spacing: 2) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(level.label)
                        .font(picked ? Font.sBody.weight(.semibold) : Font.sBody)
                        .fixedSize(horizontal: false, vertical: true)
                    if picked { YourMark(colour: colour) }
                }
                if !level.text.isEmpty {
                    Text(level.text)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
        }
        .opacity(dim ? 0.5 : 1)
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(picked ? .isSelected : [])
    }

    private var tick: some View {
        ZStack {
            if picked {
                Circle().fill(colour.color).frame(width: 16, height: 16)
                Circle().strokeBorder(Color.white.opacity(0.9), lineWidth: 2).frame(width: 16, height: 16)
            } else {
                Circle().fill(Theme.card).frame(width: 10, height: 10)
                Circle().strokeBorder(colour.color.opacity(0.8), lineWidth: 2).frame(width: 10, height: 10)
            }
        }
        .accessibilityHidden(true)
    }
}

/// "Your mark", on the criterion's colour.
struct YourMark: View {
    let colour: RingRGB

    var body: some View {
        Text("Your mark")
            .font(.sCaption.weight(.semibold))
            .foregroundStyle(colour.ink)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(colour.color, in: Capsule())
            .fixedSize()
    }
}

/// The criterion's bar, down the levels' left: its colour from the foot to its mark (darker at the foot, lighter at
/// the top), the stretch above it not earned.
private struct LevelBar: View {
    let fraction: Double
    let colour: RingRGB
    static let gutter: CGFloat = 22
    static let inset: CGFloat = 18

    var body: some View {
        GeometryReader { g in
            let h = max(0, g.size.height - 2 * LevelBar.inset)
            let f = max(0, min(1, fraction))
            ZStack(alignment: .bottom) {
                Capsule().fill(Theme.well)
                Capsule()
                    .fill(LinearGradient(colors: [colour.mix(.black, 0.18).color, colour.color, colour.mix(.white, 0.22).color],
                                         startPoint: .bottom, endPoint: .top))
                    .frame(height: max(6, h * CGFloat(f)))
            }
            .frame(width: 6, height: h)
            .position(x: LevelBar.gutter / 2, y: LevelBar.inset + h / 2)
        }
        .accessibilityHidden(true)
    }
}

/// The levels at the height of their points (the best at the top), pushed apart where two would overlap.
private struct LevelLadder: Layout {
    /// Each row's place: 1 at the top, 0 at the foot.
    let positions: [Double]
    var minHeight: CGFloat = 230
    var gap: CGFloat = 10

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width.flatMap { $0.isFinite ? $0 : nil } ?? 380
        let heights = subviews.map { $0.sizeThatFits(ProposedViewSize(width: width, height: nil)).height }
        return CGSize(width: width, height: total(heights))
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let heights = subviews.map { $0.sizeThatFits(ProposedViewSize(width: bounds.width, height: nil)).height }
        let ys = centres(heights, total(heights))
        for (i, s) in subviews.enumerated() {
            s.place(at: CGPoint(x: bounds.minX, y: bounds.minY + ys[i] - heights[i] / 2), anchor: .topLeading,
                    proposal: ProposedViewSize(width: bounds.width, height: heights[i]))
        }
    }

    private func total(_ heights: [CGFloat]) -> CGFloat {
        max(minHeight, heights.reduce(0, +) + gap * CGFloat(max(0, heights.count - 1)) + 8)
    }

    /// Each row's middle: where its points put it on the bar, then pushed down clear of the one above, then — at the
    /// foot — back up clear of the one below.
    private func centres(_ heights: [CGFloat], _ height: CGFloat) -> [CGFloat] {
        let span = max(1, height - 2 * LevelBar.inset)
        var ys = heights.indices.map { i -> CGFloat in
            let p = i < positions.count ? positions[i] : 0
            let want = LevelBar.inset + CGFloat(1 - max(0, min(1, p))) * span
            return min(max(want, heights[i] / 2), height - heights[i] / 2)
        }
        guard !ys.isEmpty else { return ys }
        for i in ys.indices.dropFirst() {
            ys[i] = max(ys[i], ys[i - 1] + heights[i - 1] / 2 + gap + heights[i] / 2)
        }
        let last = ys.count - 1
        if ys[last] + heights[last] / 2 > height {
            ys[last] = height - heights[last] / 2
            for i in stride(from: last - 1, through: 0, by: -1) {
                ys[i] = min(ys[i], ys[i + 1] - heights[i + 1] / 2 - gap - heights[i] / 2)
            }
        }
        return ys
    }
}

// MARK: - The grid

/// The same rubric as rows and columns: a criterion a row, its levels lined up under the columns by what they are
/// worth (the best on the left, nothing on the right), the one given marked. Marked, Before grading shows the rubric as
/// it read before any marks.
struct RubricGrid: View {
    let model: RubricModel
    @State private var marks = true

    var body: some View {
        let plan = GridPlan(model)
        let showMarks = marks && model.graded
        return VStack(alignment: .leading, spacing: 14) {
            if model.graded {
                Picker("Show", selection: $marks) {
                    Text("Before grading").tag(false)
                    Text("Graded").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
            }
            Grid(alignment: .topLeading, horizontalSpacing: 16, verticalSpacing: 0) {
                GridRow {
                    Text("Criterion")
                        .font(.sCaption.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(width: 200, alignment: .leading)
                        .padding(.bottom, 10)
                    ForEach(plan.heads.indices, id: \.self) { j in headCell(plan.heads[j]) }
                }
                ForEach(model.criteria, id: \.index) { c in
                    Divider()
                    GridRow {
                        criterionCell(c, showMarks)
                        ForEach(0..<plan.cols, id: \.self) { j in levelCell(c, j, plan, showMarks) }
                    }
                }
            }
            Text(showMarks ? "Your mark is outlined on each row." : "How each criterion will be graded.")
                .font(.sCaption)
                .foregroundStyle(.tertiary)
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 20)
    }

    private func headCell(_ h: GridPlan.Head) -> some View {
        HStack(spacing: 6) {
            Circle().fill(h.tone).frame(width: 8, height: 8)
            Text(h.name)
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
                .lineLimit(1)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.bottom, 10)
    }

    private func criterionCell(_ c: RubricModel.Criterion, _ showMarks: Bool) -> some View {
        let scored = showMarks && c.score != nil
        let colour = scored ? model.grades[c.index] : model.hues[c.index]
        return VStack(alignment: .leading, spacing: 5) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Circle().fill(colour.color).frame(width: 9, height: 9)
                Text(c.name)
                    .font(.sBody.weight(.semibold))
                    .fixedSize(horizontal: false, vertical: true)
            }
            HStack(alignment: .firstTextBaseline, spacing: 3) {
                Text(RubricModel.num(scored ? (c.score ?? 0) : c.worth))
                    .font(.sTitle3.monospacedDigit())
                Text(scored ? "/ \(RubricModel.num(c.worth))" : (c.worth == 1 ? "pt" : "pts"))
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
            }
            if showMarks && !c.comment.isEmpty {
                Label(c.comment, systemImage: "text.bubble")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                    .help(c.comment)
            }
            LevelTrack(fraction: scored ? c.frac : 1, color: colour.color)
                .frame(maxWidth: 160)
        }
        .padding(.vertical, 12)
        .frame(width: 200, alignment: .leading)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(model.spoken(c))
    }

    @ViewBuilder
    private func levelCell(_ c: RubricModel.Criterion, _ j: Int, _ plan: GridPlan, _ showMarks: Bool) -> some View {
        if let i = plan.slots[c.index].firstIndex(of: j) {
            let l = c.levels[i]
            let head = plan.heads[j]
            let mark = showMarks && c.mark == i
            let dim = showMarks && c.score != nil && !mark
            let main = head.own ? l.text : l.label
            let sub = head.own ? "" : l.text
            let colour = model.grades[c.index]
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 3) {
                    Text(l.pts.map { RubricModel.num($0) } ?? "–")
                        .font(.sHeadline.monospacedDigit())
                        .foregroundStyle(mark ? colour.color : Color.primary)
                    Text(l.pts == 1 ? "pt" : "pts")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
                if mark { YourMark(colour: colour) }
                if !main.isEmpty {
                    Text(main)
                        .font(mark ? Font.sCallout.weight(.semibold) : Font.sCallout)
                        .fixedSize(horizontal: false, vertical: true)
                }
                if !sub.isEmpty {
                    Text(sub)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.vertical, 12)
            .padding(.leading, mark ? 10 : 0)
            .overlay(alignment: .leading) {
                if mark {
                    Capsule().fill(colour.color).frame(width: 3).padding(.vertical, 12)
                }
            }
            .frame(maxWidth: .infinity, alignment: .topLeading)
            .opacity(dim ? 0.45 : 1)
            .help([l.label, l.text].filter { !$0.isEmpty }.joined(separator: " — "))
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(mark ? .isSelected : [])
        } else {
            Color.clear
                .frame(maxWidth: .infinity, maxHeight: 1)
                .accessibilityHidden(true)
        }
    }
}

/// The grid's columns: how many, which column each criterion's levels go in (by what each is worth out of its best;
/// nothing is the last column), and each column's name and colour (the levels' own name where every criterion calls
/// that level the same, else by rank).
private struct GridPlan {
    struct Head {
        let name: String
        let own: Bool
        let tone: Color
    }

    let cols: Int
    let slots: [[Int]]
    let heads: [Head]

    init(_ m: RubricModel) {
        let cols = max(1, m.criteria.map(\.levels.count).max() ?? 1)
        self.cols = cols
        let slots: [[Int]] = m.criteria.map { c in
            let levels = c.levels
            let k = levels.count
            if k == 0 { return [] }
            if k == cols { return Array(0..<k) }
            let vals = levels.compactMap(\.pts)
            let best = vals.max() ?? 0
            let lo = min(0, vals.min() ?? 0)
            var want = levels.enumerated().map { i, l -> Int in
                if let p = l.pts, best - lo > 1e-9 {
                    return Int(((1 - (p - lo) / (best - lo)) * Double(cols - 1)).rounded())
                }
                return Int((Double(i * (cols - 1)) / Double(max(1, k - 1))).rounded())
            }
            for i in 1..<k { want[i] = max(want[i], want[i - 1] + 1) }
            for i in stride(from: k - 1, through: 0, by: -1) { want[i] = min(want[i], cols - 1 - (k - 1 - i)) }
            return want
        }
        self.slots = slots
        func inColumn(_ j: Int) -> [RubricModel.Level] {
            m.criteria.enumerated().compactMap { k, c in
                slots[k].firstIndex(of: j).map { c.levels[$0] }
            }
        }
        let lastNone = inColumn(cols - 1).allSatisfy { ($0.pts ?? 0) == 0 }
        let ladder: [String]?
        switch cols {
        case 1: ladder = ["Full marks"]
        case 2: ladder = ["Excellent"]
        case 3: ladder = ["Excellent", "Developing"]
        case 4: ladder = ["Excellent", "Good", "Developing"]
        case 5: ladder = ["Excellent", "Good", "Fair", "Developing"]
        default: ladder = nil
        }
        heads = (0..<cols).map { j in
            let names = inColumn(j).map { $0.label.trimmingCharacters(in: .whitespaces) }
            let shared: String? = {
                guard let first = names.first, first.count <= 22 else { return nil }
                return names.allSatisfy { $0.lowercased() == first.lowercased() } ? first : nil
            }()
            let name: String
            if let shared {
                name = shared
            } else if let ladder {
                name = j == cols - 1 && cols > 1 ? (lastNone ? "Missing" : "Beginning") : ladder[min(j, ladder.count - 1)]
            } else {
                name = "Level \(j + 1)"
            }
            let t = cols > 1 ? Double(j) / Double(cols - 1) : 0
            let tone: String
            if j == 0 {
                tone = "#30d158"
            } else if j == cols - 1 {
                tone = lastNone ? "#8e8e93" : "#ff6b3d"
            } else if t <= 0.34 {
                tone = "#0a84ff"
            } else if t < 0.55 && cols >= 5 {
                tone = "#ffd60a"
            } else {
                tone = "#ff9f0a"
            }
            return Head(name: name, own: shared != nil, tone: RingRGB(hex: tone).color)
        }
    }
}
