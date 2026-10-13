import SwiftUI

// Cell Division (spec §4.6): mitosis and meiosis stage by stage, with the counts at every step, side by side, and the
// cell cycle. Four pages, chosen in the header: Mitosis and Meiosis (the cell drawn in 2D and stepped through, the
// counts panel always beside it, the options and the stages on the left), Compare (the two stepping together over
// §5.2.3's table) and Cell cycle (the wheel, its checkpoints and the cyclins; CellCycleView.swift). The model, its
// keyframes, counts and drawing are in Model/CellDivision*.swift; the drawing's view is CellDivisionCanvas.swift.
//
// Launch state (-SimplTool divide -SimplToolState <state>, parts split at ":"): a page (mitosis, meiosis, compare,
// cycle), then any of plant, 2n=6, nocross, chiasma=0.3, flip, nd=1 or nd=2, nolabels, step=<n> (1-based, on the page
// shown), pick=<chromatid index>, part=<label part>, gate=g1s|g2m|spindle or phase=g1|s|g2|m|g0 (Cell cycle), cancer.
// -SimplBioCell plant and -SimplBioStep <n> read the same.

struct CellDivisionTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.divide)`.
    static let pages: [BioPage] = [
        BioPage(id: "mitosis", title: "Mitosis", symbol: "circle.grid.2x1", help: "Mitosis, stage by stage"),
        BioPage(id: "meiosis", title: "Meiosis", symbol: "circle.grid.2x2", help: "Meiosis I and II, stage by stage"),
        BioPage(id: "compare", title: "Compare", symbol: "square.split.2x1", help: "Mitosis and meiosis side by side"),
        BioPage(id: "cycle", title: "Cell cycle", symbol: "arrow.triangle.2.circlepath", help: "The cell cycle and its checkpoints"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        Group {
            switch nav.page(.divide) {
            case "meiosis": DivisionProcessPage(process: .meiosis, steps: model.meiosisSteps)
            case "compare": DivisionComparePage(steps: model.compareSteps)
            case "cycle": CellCyclePage()
            default: DivisionProcessPage(process: .mitosis, steps: model.mitosisSteps)
            }
        }
        .onAppear { model.applyLaunch(state) }
        .onChange(of: nav.page(.divide)) { _, _ in
            // (a picked chromosome or part belongs to the page it was picked on)
            model.picked = nil
            model.part = nil
        }
    }
}

/// What the tool holds while it is open and between visits: the options, both processes worked out for them, a step
/// controller for each page, what is picked, and the Cell cycle page's choices. Options and labels are remembered
/// (SimplBio.divide.<field>).
@MainActor
final class DivisionState: ObservableObject {
    static let shared = DivisionState()

    @Published private(set) var options: DivisionOptions
    @Published private(set) var mitosis: DivisionSequence
    @Published private(set) var meiosis: DivisionSequence
    @Published var showLabels: Bool {
        didSet { BioPrefs.set(.divide, "labels", showLabels) }
    }
    /// The chromatid picked on the drawing (its index), shown with its card.
    @Published var picked: Int?
    /// The part whose label was pressed (DivisionModel.parts).
    @Published var part: String?
    /// The Cell cycle page's gate or phase shown.
    @Published var cycleItem: String?
    @Published var cancer = false

    let mitosisSteps: BioStepController
    let meiosisSteps: BioStepController
    let compareSteps: BioStepController
    private var launched = false

    private init() {
        var o = DivisionOptions()
        o.plant = BioPrefs.bool(.divide, "plant", default: false)
        o.pairs = BioPrefs.string(.divide, "pairs") == "3" ? 3 : 2
        o.crossingOver = BioPrefs.bool(.divide, "crossing", default: true)
        if let c = BioPrefs.string(.divide, "chiasma").flatMap(Double.init) { o.chiasma = min(max(c, 0.2), 0.8) }
        o.flipped = [false, BioPrefs.bool(.divide, "flip1", default: false), BioPrefs.bool(.divide, "flip2", default: false)]
        o.nondisjunction = DivisionNondisjunction(rawValue: BioPrefs.string(.divide, "nd") ?? "") ?? .none
        options = o
        let mit = DivisionModel.sequence(.mitosis, o), mei = DivisionModel.sequence(.meiosis, o)
        mitosis = mit
        meiosis = mei
        mitosisSteps = BioStepController(steps: mit.steps)
        meiosisSteps = BioStepController(steps: mei.steps)
        compareSteps = BioStepController(steps: DivisionModel.compareBioSteps)
        showLabels = BioPrefs.bool(.divide, "labels", default: true)
    }

    func sequence(_ p: DivisionProcess) -> DivisionSequence { p == .mitosis ? mitosis : meiosis }
    func steps(_ p: DivisionProcess) -> BioStepController { p == .mitosis ? mitosisSteps : meiosisSteps }

    /// Change the options: both processes are worked out again, each kept on its step.
    func update(_ change: (inout DivisionOptions) -> Void) {
        var o = options
        change(&o)
        guard o != options else { return }
        options = o
        BioPrefs.set(.divide, "plant", o.plant)
        BioPrefs.set(.divide, "pairs", "\(o.pairs)")
        BioPrefs.set(.divide, "crossing", o.crossingOver)
        BioPrefs.set(.divide, "chiasma", String(format: "%.2f", o.chiasma))
        BioPrefs.set(.divide, "flip1", o.flipped[1])
        BioPrefs.set(.divide, "flip2", o.flipped[2])
        BioPrefs.set(.divide, "nd", o.nondisjunction.rawValue)
        mitosis = DivisionModel.sequence(.mitosis, o)
        meiosis = DivisionModel.sequence(.meiosis, o)
        if mitosisSteps.steps != mitosis.steps { mitosisSteps.setSteps(mitosis.steps, keepIndex: true) }
        if meiosisSteps.steps != meiosis.steps { meiosisSteps.setSteps(meiosis.steps, keepIndex: true) }
        if let p = picked, p >= 4 * o.pairs { picked = nil }
    }

    /// The launch's state, once (the screenshot suite's way to each page and step).
    func applyLaunch(_ s: BioLaunchState?) {
        guard !launched else { return }
        launched = true
        guard let s else { return }
        update { o in
            let cell = s.value("cell")?.lowercased()
            if s.has("plant") || cell == "plant" { o.plant = true }
            if s.has("animal") || cell == "animal" { o.plant = false }
            if s.value("2n") == "6" || s.has("six") { o.pairs = 3 } else if s.value("2n") == "4" { o.pairs = 2 }
            if s.has("nocross") { o.crossingOver = false }
            if let c = s.value("chiasma").flatMap(Double.init) { o.chiasma = min(max(c, 0.2), 0.8) }
            if s.has("flip") { o.flipped[1] = true }
            switch s.value("nd") ?? "" {
            case "1": o.nondisjunction = .meiosisI
            case "2": o.nondisjunction = .meiosisII
            case "0": o.nondisjunction = .none
            default: break
            }
        }
        if s.has("nolabels") || s.value("labels") == "off" { showLabels = false }
        if let n = s.value("step").flatMap(Int.init) {
            let page = BioNav.shared.page(.divide)
            let c = page == "meiosis" ? meiosisSteps : (page == "compare" ? compareSteps : mitosisSteps)
            c.go(to: n - 1, animated: false)
        }
        if let i = s.value("pick").flatMap(Int.init), i >= 0, i < 4 * options.pairs { picked = i }
        if let p = s.value("part"), DivisionModel.parts[p] != nil { part = p }
        if let g = s.value("gate") ?? s.value("phase") { cycleItem = g }
        if s.has("cancer") { cancer = true }
    }
}

// MARK: - Mitosis and Meiosis

/// A process's page: options and stages on the left, the drawing and its step-through in the middle, the counts and
/// cards on the right (the chemistry tools' three columns, stacked when the window is narrow).
struct DivisionProcessPage: View {
    let process: DivisionProcess
    @ObservedObject var steps: BioStepController
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        let seq = model.sequence(process)
        PTWorkLayout {
            DivisionOptionsColumn(process: process, steps: steps)
        } centre: {
            VStack(spacing: 0) {
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(seq.stages[min(steps.index, seq.stages.count - 1)].title)
                            .font(.system(size: 22, weight: .bold))
                            .lineLimit(1)
                        Text("\(process.title) · \(model.options.plant ? "plant" : "animal") cell · model 2n = \(2 * model.options.pairs) (human 2n = 46)")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                    }
                    Spacer(minLength: 8)
                    Button {
                        withAnimation(Motion.snappy) { model.showLabels.toggle() }
                    } label: {
                        Label("Labels", systemImage: model.showLabels ? "tag.fill" : "tag").font(.sCallout.weight(.medium))
                    }
                    .glassButton(prominent: model.showLabels)
                    .help(model.showLabels ? "Hide the labels" : "Show the labels")
                }
                .padding(16)
                DivisionCanvas(sequence: seq, steps: steps, labels: model.showLabels, picked: $model.picked) { part in
                    withAnimation(Motion.gentle) {
                        model.picked = nil
                        model.part = part
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .bioStepKeys(steps)
                .onKeyPress(.escape) {
                    guard model.picked != nil || model.part != nil else { return .ignored }
                    withAnimation(Motion.snappy) {
                        model.picked = nil
                        model.part = nil
                    }
                    return .handled
                }
                BioStepControls(controller: steps, groups: DivisionModel.groups(process))
                    .padding(.horizontal, 16)
                    .padding(.top, 6)
                    .padding(.bottom, 14)
                Text("Click a chromosome or a label to learn about it · ← → step · 1–\(DivisionModel.groups(process).count) jump to a stage")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .padding(.bottom, 12)
            }
            .card(radius: 20)
        } right: {
            DivisionFactsColumn(process: process, steps: steps)
        }
    }
}

/// The left column: the cell and chromosome choices, labels, the stages (the number keys), meiosis's options, and the
/// legend.
struct DivisionOptionsColumn: View {
    let process: DivisionProcess
    @ObservedObject var steps: BioStepController
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Group {
                BioHeading("Cell")
                Picker("Cell", selection: Binding(get: { model.options.plant }, set: { v in model.update { $0.plant = v } })) {
                    Text("Animal").tag(false)
                    Text("Plant").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .help("Animal cells have centrioles and pinch in two; plant cells have none, and build a cell plate")
                BioHeading("Chromosomes")
                Picker("Chromosomes", selection: Binding(get: { model.options.pairs }, set: { v in model.update { $0.pairs = v } })) {
                    Text("2n = 4").tag(2)
                    Text("2n = 6").tag(3)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .help("The model organism's chromosome number: two pairs, or three")
                Text("A long pair and a short pair\(model.options.pairs == 3 ? ", and a middle pair" : ""): one of each from the mother (red), one from the father (blue). Human cells have 2n = 46.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Toggle("Show labels", isOn: $model.showLabels)
            }
            BioHeading("Stages")
            DivisionStageList(process: process, steps: steps)
            if process == .meiosis { DivisionMeiosisOptions() }
            BioHeading("Key")
            BioLegend(items: [(BioPalette.maternal.color, "From the mother (maternal)"),
                              (BioPalette.paternal.color, "From the father (paternal)"),
                              (Color.primary.opacity(0.8), "Centromere (one dot per chromosome)"),
                              (Color(hex: "#2e9e78"), "Spindle microtubules")])
        }
        .font(.sCallout)
    }
}

/// The stages as a list: a press goes to a stage group's first step (as its number key does).
struct DivisionStageList: View {
    let process: DivisionProcess
    @ObservedObject var steps: BioStepController

    var body: some View {
        let groups = DivisionModel.groups(process)
        let current = steps.current?.group
        VStack(alignment: .leading, spacing: 2) {
            ForEach(groups.keys.sorted(), id: \.self) { g in
                Button { steps.jump(toGroup: g) } label: {
                    HStack(spacing: 10) {
                        Text("\(g)")
                            .font(.system(size: 11, weight: .semibold, design: .rounded).monospacedDigit())
                            .foregroundStyle(.secondary)
                            .frame(width: 18, height: 18)
                            .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
                        Text(groups[g] ?? "")
                            .font(.sCallout.weight(current == g ? .semibold : .regular))
                        Spacer(minLength: 0)
                    }
                    .padding(.horizontal, 8)
                    .padding(.vertical, 5)
                    .background(current == g ? Theme.accent.opacity(0.14) : Color.clear, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("\(groups[g] ?? "") (key \(g))")
                .accessibilityAddTraits(current == g ? .isSelected : [])
            }
        }
    }
}

/// Meiosis's own options (spec §4.6): crossing over and where its chiasma is, independent assortment, nondisjunction.
struct DivisionMeiosisOptions: View {
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        let o = model.options
        VStack(alignment: .leading, spacing: 14) {
            crossing(o)
            assortment(o)
            nondisjunction(o)
        }
    }

    private func crossing(_ o: DivisionOptions) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Crossing over")
            Toggle("Crossing over in prophase I", isOn: Binding(get: { o.crossingOver }, set: { v in model.update { $0.crossingOver = v } }))
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Chiasma on the long pair")
                    Spacer()
                    Text("\(Int((o.chiasma * 100).rounded()))% along its long arm").foregroundStyle(.secondary).monospacedDigit()
                }
                .font(.sCaption)
                Slider(value: Binding(get: { o.chiasma }, set: { v in model.update { $0.chiasma = (v * 20).rounded() / 20 } }),
                       in: DivisionOptions.chiasmaRange)
                    .disabled(!o.crossingOver)
                    .accessibilityLabel("Chiasma position")
                    .accessibilityValue("\(Int((o.chiasma * 100).rounded())) percent along the long arm")
            }
            .opacity(o.crossingOver ? 1 : 0.5)
        }
    }

    private func assortment(_ o: DivisionOptions) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Independent assortment")
            Text("Which homolog of each pair faces which pole in metaphase I is chance. Flip a pair to see the other gametes.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(o.pairs == 3 ? [1, 2] : [1], id: \.self) { p in
                Button {
                    model.update { $0.flipped[p].toggle() }
                } label: {
                    Label("Flip the \(DivGeometry.pairs[p].name) pair", systemImage: "arrow.up.arrow.down").frame(maxWidth: .infinity)
                }
                .glassButton(prominent: o.flipped[p])
                .controlSize(.large)
                .help("Turn the \(DivGeometry.pairs[p].name) pair round at metaphase I")
            }
            Text(arrangement(o))
                .font(.sCaption)
                .fixedSize(horizontal: false, vertical: true)
            Text(DivisionModel.assortments(pairs: o.pairs))
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func nondisjunction(_ o: DivisionOptions) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Nondisjunction")
            Picker("Nondisjunction", selection: Binding(get: { o.nondisjunction }, set: { v in model.update { $0.nondisjunction = v } })) {
                ForEach(DivisionNondisjunction.allCases, id: \.self) { Text($0.title).tag($0) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("Let the short pair fail to separate, in meiosis I or meiosis II")
            if o.nondisjunction != .none {
                Text(o.nondisjunction == .meiosisI
                     ? "The short pair's homologs both go to one pole: gametes n+1, n+1, n−1, n−1."
                     : "In one cell the short chromosome's sisters stay together: gametes n+1, n−1, n, n.")
                    .font(.sCaption)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    /// Which homolog of each pair faces the top pole now.
    private func arrangement(_ o: DivisionOptions) -> String {
        let names = (0..<o.pairs).map { p -> String in
            "\(DivGeometry.pairs[p].name) \(o.flipped[p] ? "father's" : "mother's")"
        }
        return "Facing the top pole: " + names.joined(separator: ", ") + "."
    }
}

// MARK: - Compare

/// Mitosis and meiosis side by side, stepping together (spec §4.6), over the comparison table (§5.2.3) with the rows
/// about the step marked.
struct DivisionComparePage: View {
    @ObservedObject var steps: BioStepController
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        GeometryReader { geo in
            if geo.size.width >= 1250 {
                HStack(alignment: .top, spacing: 18) {
                    VStack(spacing: 14) {
                        panels
                        controls
                    }
                    ScrollView { table.padding(.bottom, 20) }
                        .frame(width: 400)
                }
            } else {
                ScrollView {
                    VStack(spacing: 14) {
                        panels.frame(height: max(420, min(560, geo.size.height * 0.62)))
                        controls
                        table
                    }
                    .padding(.bottom, 24)
                }
            }
        }
        .padding(.horizontal, 40)
        .padding(.bottom, 24)
        .frame(maxWidth: 1480 + 80)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    private var step: DivCompareStep { DivisionModel.compareSteps[min(steps.index, DivisionModel.compareSteps.count - 1)] }

    private var panels: some View {
        HStack(spacing: 14) {
            panel(.mitosis, keyframe: { DivisionModel.compareSteps[min($0, DivisionModel.compareSteps.count - 1)].mitosis })
            panel(.meiosis, keyframe: { DivisionModel.compareSteps[min($0, DivisionModel.compareSteps.count - 1)].meiosis })
        }
    }

    private func panel(_ p: DivisionProcess, keyframe: @escaping (Int) -> Int) -> some View {
        let seq = model.sequence(p)
        let i = keyframe(steps.index)
        let done = p == .mitosis && steps.index >= 4
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text(p.title).font(.sTitle3)
                Text(seq.stages[i].title).font(.sCallout).foregroundStyle(.secondary)
                Spacer()
                if done {
                    Label("Finished: 2 cells", systemImage: "checkmark.circle")
                        .font(.sCaption.weight(.medium))
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 16)
            .padding(.top, 14)
            DivisionCanvas(sequence: seq, steps: steps, keyframe: keyframe, labels: false, picked: .constant(nil))
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .bioStepKeys(steps)
            DivisionCompactCounts(counts: seq.counts[i])
                .padding(.horizontal, 12)
                .padding(.bottom, 12)
        }
        .card(radius: 20)
    }

    private var controls: some View {
        BioStepControls(controller: steps, groups: DivisionModel.compareGroups)
    }

    private var table: some View {
        let rows = DivisionModel.compareTable
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                Text("").frame(width: 112, alignment: .leading)
                Text("Mitosis").font(.sCallout.weight(.semibold)).frame(maxWidth: .infinity, alignment: .leading)
                Text("Meiosis").font(.sCallout.weight(.semibold)).frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            Divider()
            ForEach(rows.indices, id: \.self) { r in
                let on = step.rows.contains(r)
                HStack(alignment: .top, spacing: 8) {
                    Text(rows[r][0]).font(.sCaption.weight(.semibold)).foregroundStyle(on ? Theme.accent : .secondary)
                        .frame(width: 104, alignment: .leading)
                    Text(rows[r][1]).font(.sCallout).frame(maxWidth: .infinity, alignment: .leading)
                    Text(rows[r][2]).font(.sCallout).frame(maxWidth: .infinity, alignment: .leading)
                }
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal, 14)
                .padding(.vertical, 9)
                .background(on ? Theme.accent.opacity(0.1) : Color.clear)
                .accessibilityElement(children: .combine)
                .accessibilityAddTraits(on ? .isSelected : [])
                if r < rows.count - 1 { Divider().padding(.leading, 14) }
            }
        }
        .card(radius: 16)
        .animation(Motion.gentle, value: steps.index)
    }
}
