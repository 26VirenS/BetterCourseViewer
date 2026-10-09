import SwiftUI

// The rubric ring's panels (1.2.2): the Ring / Grid switch at the top right of the ring over the window
// (RubricPopup.swift), and the grid — the same rubric as rows and columns, a criterion a row, its levels lined up under
// columns by what they are worth, the one given marked. Drawn on the ring's dimmed ground: light words in both
// appearances, as the web's ring has them.

/// Two or three choices in a glass capsule, the one showing lit on a knob that slides.
struct RubricSegments: View {
    struct Option: Hashable {
        let key: String
        let title: String
        var symbol: String? = nil
    }

    let options: [Option]
    @Binding var selection: String
    let label: String
    @Namespace private var knob

    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.key) { o in option(o) }
        }
        .padding(3)
        .glassCapsule()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }

    private func option(_ o: Option) -> some View {
        let on = selection == o.key
        return Button {
            withAnimation(Motion.snappy) { selection = o.key }
        } label: {
            Group {
                if let s = o.symbol {
                    Label(o.title, systemImage: s)
                } else {
                    Text(o.title)
                }
            }
            .font(.sCallout.weight(.semibold))
            .lineLimit(1)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .foregroundStyle(on ? Color.primary : Color.secondary)
            .background {
                if on {
                    Capsule()
                        .fill(Color.primary.opacity(0.14))
                        .matchedGeometryEffect(id: "knob", in: knob)
                }
            }
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// Ring or Grid. The choice is kept: the next rubric opens as this one was left.
struct RubricViewSwitch: View {
    @Binding var view: String

    var body: some View {
        RubricSegments(options: [RubricSegments.Option(key: "ring", title: "Ring", symbol: "circle.dashed"),
                                 RubricSegments.Option(key: "grid", title: "Grid", symbol: "square.grid.2x2")],
                       selection: $view, label: "Show the rubric as")
            .help("Show the rubric as a ring or a grid")
    }
}

/// A thin track filled as far as a mark reached.
struct LevelTrack: View {
    let fraction: Double
    let color: Color

    var body: some View {
        Capsule()
            .fill(Color.primary.opacity(0.14))
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

// MARK: - The grid

/// The rubric as a grid over the window: what it is and for which assignment, its score and how many criteria, Before
/// grading or Graded (marked), then the grid — scrolling when it is taller than the window has room for.
struct RubricGridPanel: View {
    let model: RubricModel
    let eyebrow: String
    let title: String
    @State private var shown = "graded"

    var body: some View {
        let marks = shown == "graded" && model.graded
        VStack(alignment: .leading, spacing: 16) {
            head(marks)
            ViewThatFits(in: .vertical) {
                RubricGrid(model: model, showMarks: marks)
                ScrollView {
                    RubricGrid(model: model, showMarks: marks)
                        .padding(.trailing, 8)
                }
            }
            Text(marks ? "Your mark is outlined on each row." : "How each criterion will be graded.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
        .padding(24)
        .glassCard(radius: 28, tint: Color.black.opacity(0.3))
        .contentShape(RoundedRectangle(cornerRadius: 28, style: .continuous))
        .onTapGesture {} // (a press on the grid is not a press beside it)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(title) rubric as a grid")
    }

    private func head(_ marks: Bool) -> some View {
        HStack(alignment: .bottom, spacing: 18) {
            VStack(alignment: .leading, spacing: 5) {
                Text(eyebrow.uppercased())
                    .font(.sCaption.weight(.semibold))
                    .tracking(0.9)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                Text(title)
                    .font(.sTitle2)
                    .lineLimit(2)
            }
            Spacer(minLength: 12)
            HStack(alignment: .firstTextBaseline, spacing: 4) {
                Text(RubricModel.num(marks ? model.earned : model.possible))
                    .font(.system(size: 30, weight: .bold, design: .rounded).monospacedDigit())
                Text(marks ? "/ \(RubricModel.num(model.possible))" : (model.possible == 1 ? "pt" : "pts"))
                    .font(.sHeadline)
                    .foregroundStyle(.secondary)
            }
            .accessibilityElement(children: .combine)
            Text(model.n == 1 ? "1 criterion" : "\(model.n) criteria")
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .padding(.bottom, 4)
            if model.graded {
                RubricSegments(options: [RubricSegments.Option(key: "before", title: "Before grading"),
                                         RubricSegments.Option(key: "graded", title: "Graded")],
                               selection: $shown, label: "Show")
                    .help("The rubric as it read before grading, or with your marks")
            }
        }
    }
}

/// The same rubric as rows and columns: a criterion a row, its levels lined up under the columns by what they are
/// worth (the best on the left, nothing on the right), the one given marked.
struct RubricGrid: View {
    let model: RubricModel
    let showMarks: Bool

    var body: some View {
        let plan = GridPlan(model)
        Grid(alignment: .topLeading, horizontalSpacing: 16, verticalSpacing: 0) {
            GridRow {
                Text("Criterion")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 210, alignment: .leading)
                    .padding(.bottom, 10)
                ForEach(plan.heads.indices, id: \.self) { j in headCell(plan.heads[j]) }
            }
            ForEach(model.criteria, id: \.index) { c in
                Rectangle()
                    .fill(Color.primary.opacity(0.12))
                    .frame(height: 1)
                    .gridCellUnsizedAxes(.horizontal)
                GridRow {
                    criterionCell(c)
                    ForEach(0..<plan.cols, id: \.self) { j in levelCell(c, j, plan) }
                }
            }
        }
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

    private func criterionCell(_ c: RubricModel.Criterion) -> some View {
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
        .frame(width: 210, alignment: .leading)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(model.spoken(c))
    }

    @ViewBuilder
    private func levelCell(_ c: RubricModel.Criterion, _ j: Int, _ plan: GridPlan) -> some View {
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
