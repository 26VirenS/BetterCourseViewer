import AppKit
import SceneKit
import SwiftUI

// Cell Explorer (spec §4.1): animal, plant and bacterial cells in 3D, built from §5.1's recipes at fixed seeds
// (CellExplorerBuild.swift), with labels and leader lines, a card for each part (its function, structure, size, how the
// model draws it, where it is found and the common mix-up), the cutaway, the explode slider, teaching and true scale
// with a scale bar, a part isolated or looked inside, a quiz and flashcards; a patch of membrane up close
// (CellExplorerPages.swift); and the three compared, at their true sizes and in §5.1.4's table. Laid out as the
// chemistry builders are (PTWorkLayout): the parts and options on the left, the cell in the middle, its card on the
// right. A launch's state (-SimplTool cell -SimplToolState plant:select=chloroplast) opens a page as asked.

struct CellExplorerTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.cell)`.
    static let pages: [BioPage] = [
        BioPage(id: "animal", title: "Animal", symbol: "pawprint", help: "An animal cell"),
        BioPage(id: "plant", title: "Plant", symbol: "leaf", help: "A plant cell"),
        BioPage(id: "bacterium", title: "Bacterium", symbol: "capsule", help: "A bacterial cell (E. coli)"),
        BioPage(id: "membrane", title: "Membrane", symbol: "line.3.horizontal", help: "The plasma membrane up close"),
        BioPage(id: "compare", title: "Compare", symbol: "square.split.2x1", help: "Animal, plant and bacterial cells side by side"),
    ]

    @ObservedObject private var nav = BioNav.shared

    var body: some View {
        Group {
            switch nav.page(.cell) {
            case "membrane": CEMembranePage(state: state)
            case "compare": CEComparePage()
            case "plant": CECellPage(kind: .plant, state: state)
            case "bacterium": CECellPage(kind: .bacterium, state: state)
            default: CECellPage(kind: .animal, state: state)
            }
        }
        .onAppear {
            // (§9's -SimplBioCell animal|plant|bacterium picks the cell, once)
            guard !CENav.shared.applied.contains("cell"), let c = state?.value("Cell")?.lowercased(), CEKind(rawValue: c) != nil else { return }
            CENav.shared.applied.insert("cell")
            nav.set(.cell, c)
        }
    }
}

/// What the Cell Explorer's pages hand each other: a part to select when a cell's page next appears (a Compare row
/// pressed), and which launch states have been applied (each once).
@MainActor
final class CENav {
    static let shared = CENav()
    var pending: (kind: CEKind, part: String)?
    var applied: Set<String> = []

    /// A cell opened with one of its parts selected.
    func open(_ kind: CEKind, part: String) {
        pending = (kind, part)
        BioNav.shared.set(.cell, kind.rawValue)
    }
}

// MARK: - A cell

struct CECellPage: View {
    let kind: CEKind
    var state: BioLaunchState?

    @Environment(\.colorScheme) private var scheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// The part selected ("organelle:<part>").
    @State private var selection: String?
    @State private var isolate: String?
    @State private var lookInside = false
    @State private var explode = 0.0
    @State private var framedExplode = 0.0
    @State private var cutaway = BioPrefs.bool(.cell, "cutaway", default: true)
    @State private var labels = BioPrefs.bool(.cell, "labels", default: true)
    @State private var trueScale = BioPrefs.bool(.cell, "trueScale", default: false)
    @State private var gramPositive = BioPrefs.bool(.cell, "gramPositive", default: false)
    @State private var hidden: Set<String>
    @State private var turning = false
    @State private var reset = 0
    @State private var zoom: BioZoomRequest?
    @State private var scale = 0.0
    @State private var hovered: String?
    @State private var model: CEModel?
    @State private var quiz: CEQuiz?
    /// The part glowing for the quiz ("Name it"'s question, or a "Find" answer shown), and what was said.
    @State private var quizGlow: String?
    @State private var feedback: (right: Bool, text: String)?
    @State private var shake = 0.0
    /// A wrong "Find" answer's part, wobbling for a moment (drawn as extras, left out of the batched scene).
    @State private var wobble: String?
    @State private var wobbleSerial = 0
    @State private var madeSet: String?
    @FocusState private var listFocused: Bool
    /// The launch's state has been told on the console (the screenshot suite's).
    @State private var launchTold = false

    init(kind: CEKind, state: BioLaunchState?) {
        self.kind = kind
        self.state = state
        // (the parts hidden: as last left, else the "(Nice)" parts that start off)
        let saved = BioPrefs.string(.cell, "hidden.\(kind.rawValue)")
        var initial = saved.map { Set($0.split(separator: ",").map(String.init)) } ?? Set(CEParts.all(kind).filter { !$0.defaultVisible }.map(\.id))
        // (-SimplTool cell -SimplToolState animal:select=mito:explode=0.5 …: the page starts as its launch asks — nocut,
        // nolabels, true, gram+, explode=, show=a,b, select=, isolate=, inside=, quiz=find|name — from its first frame, in
        // any page made for it. Applied once as a page appeared, a launch's state was lost when the page was made again,
        // and its pictures showed the cell as it opens. Nothing a launch sets is remembered.)
        if let s = state, s.page == kind.rawValue || s.value("Cell")?.lowercased() == kind.rawValue {
            if s.has("nocut") { _cutaway = State(initialValue: false) }
            if s.has("cutaway") { _cutaway = State(initialValue: true) }
            if s.has("nolabels") { _labels = State(initialValue: false) }
            if s.has("true") { _trueScale = State(initialValue: true) }
            if s.has("teaching") { _trueScale = State(initialValue: false) }
            if s.has("gram+") || s.value("gram")?.lowercased() == "positive" { _gramPositive = State(initialValue: true) }
            if let e = s.value("explode"), let x = Double(e) {
                _explode = State(initialValue: min(max(x, 0), 1))
                _framedExplode = State(initialValue: min(max(x, 0), 1))
            }
            if let show = s.value("show") { for p in show.split(separator: ",") { initial.remove(String(p)) } }
            if let p = s.value("select"), CEParts.part(kind, p) != nil { _selection = State(initialValue: "organelle:\(p)") }
            if let p = s.value("isolate"), CEParts.part(kind, p) != nil {
                _selection = State(initialValue: "organelle:\(p)")
                _isolate = State(initialValue: p)
            }
            if let p = s.value("inside"), CEScene.canLookInside(p), CEParts.part(kind, p) != nil {
                _selection = State(initialValue: "organelle:\(p)")
                _isolate = State(initialValue: p)
                _lookInside = State(initialValue: true)
            }
            if let q = s.value("quiz") {
                let mode: CEQuiz.Mode = q.lowercased() == "name" ? .name : .find
                let hiddenNow = initial
                let ids = CEParts.all(kind).filter { $0.defaultVisible && !hiddenNow.contains($0.id) && $0.id != "cytoplasm" }.map(\.id)
                var g = SystemRandomNumberGenerator()
                let quiz = CEQuiz(mode: mode, kind: kind, parts: ids, using: &g)
                _quiz = State(initialValue: quiz)
                _quizGlow = State(initialValue: mode == .name ? quiz.current.map { "organelle:\($0.part)" } : nil)
                _selection = State(initialValue: nil)
                _isolate = State(initialValue: nil)
                _lookInside = State(initialValue: false)
            }
        }
        _hidden = State(initialValue: initial)
    }

    private var parts: [CEPart] { CEParts.all(kind) }
    private var anim: Animation? { reduceMotion ? nil : Motion.gentle }
    private var options: CEBuildOptions {
        CEBuildOptions(kind: kind, trueScale: trueScale, cutaway: cutaway, gramPositive: kind == .bacterium && gramPositive)
    }
    private var dark: Bool { scheme == .dark }
    /// The explode slider in steps of 0.02 (each a scene to make).
    private var explodeStep: Double { (explode * 50).rounded() / 50 }
    /// What the scene shows of the built cell.
    private var look: CEView {
        CEView(explode: explodeStep, hidden: hidden.union(wobble.map { [$0] } ?? []), isolate: isolate, lookInside: lookInside, dark: dark)
    }
    private var optionsKey: String { "\(kind.rawValue)|\(trueScale)|\(cutaway)|\(options.gramPositive)" }
    private var sceneKey: String {
        [optionsKey, String(explodeStep), hidden.sorted().joined(separator: ","), isolate ?? "-", String(lookInside), String(dark),
         wobble.map { "\($0)\(wobbleSerial)" } ?? "-"].joined(separator: "|")
    }
    /// The wobbling part's nodes, while there is one.
    private var wobbleNodes: (() -> [SCNNode])? {
        guard let w = wobble else { return nil }
        let o = options, e = explodeStep, d = dark
        return { CEWobble.nodes(CEBuildCache.shared.model(o), part: w, explode: e, dark: d) }
    }
    private var selectedID: String? { selection.flatMap { CECellPage.partID($0) } }
    private var selectedPart: CEPart? { selectedID.flatMap { CEParts.part(kind, $0) } }

    /// A picked id's part: "organelle:mito:3" → "mito", "inside:mito:cristae" → "mito".
    static func partID(_ id: String) -> String? {
        let c = id.split(separator: ":").map(String.init)
        guard c.count >= 2, c[0] == "organelle" || c[0] == "inside" else { return nil }
        return c[1]
    }

    var body: some View {
        PTWorkLayout {
            leftColumn
        } centre: {
            sceneCard
        } right: {
            rightColumn
                .animation(anim, value: selectedID)
                .animation(anim, value: quiz?.index)
        }
        .onAppear(perform: applyLaunch)
        .task(id: optionsKey) {
            let o = options
            Shot.began() // (the screenshot suite's picture waits for the cell, Support/Shot.swift)
            let built = await Task.detached(priority: .userInitiated) { CEBuildCache.shared.model(o) }.value
            Shot.ended()
            model = built
        }
        .onExitCommand { pick(nil) }
    }

    // MARK: Left column: the parts, the view, learning

    private var leftColumn: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Parts")
            partsList
            BioHeading("View")
            Toggle("Cutaway", isOn: saved($cutaway, "cutaway"))
                .help("Cut away the front upper-right eighth of the membrane, nuclear envelope and wall, showing how thick each is")
            Toggle("Labels", isOn: saved($labels, "labels"))
                .help("Name the parts at the sides, with lines to them")
            if kind == .bacterium {
                Toggle("Gram-positive wall", isOn: saved($gramPositive, "gramPositive"))
                    .help("A thick peptidoglycan wall and no outer membrane, instead of E. coli's thin wall and outer membrane")
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Scale")
                Picker("Scale", selection: saved($trueScale, "trueScale")) {
                    Text("Teaching").tag(false)
                    Text("True").tag(true)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .help("Teaching scale draws tiny parts larger and fewer of them; true scale draws everything at its real size")
                Text(trueScale ? "Every part at its real size, with 20,000 ribosomes."
                     : "Ribosomes, vesicles and filaments drawn larger, and fewer of them, so you can see them.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Explode")
                    Spacer()
                    Text(explode < 0.005 ? "Off" : "\(Int((explode * 100).rounded()))%").monospacedDigit().foregroundStyle(.secondary)
                }
                Slider(value: $explode, in: 0...1, onEditingChanged: { editing in
                    if !editing { refit() }
                })
                .disabled(isolate != nil)
                .help("Move every part outward from the centre; the membrane fades")
                .accessibilityLabel("Explode")
            }
            BioHeading("Learn")
            learnButtons
        }
        .font(.sCallout)
    }

    /// A toggle's binding that remembers what the student chose (a launch's state is not remembered).
    private func saved(_ b: Binding<Bool>, _ field: String) -> Binding<Bool> {
        Binding(get: { b.wrappedValue }, set: { v in
            withAnimation(anim) { b.wrappedValue = v }
            BioPrefs.set(.cell, field, v)
        })
    }

    private var partsList: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(CEGroup.allCases, id: \.self) { g in
                let items = parts.filter { $0.group == g }
                if !items.isEmpty {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(g.title)
                            .font(.sCaption.weight(.semibold))
                            .foregroundStyle(.secondary)
                            .padding(.bottom, 3)
                        ForEach(items) { p in partRow(p) }
                    }
                }
            }
        }
        .focusable()
        .focused($listFocused)
        .focusEffectDisabled()
        .onKeyPress(keys: [.tab, .upArrow, .downArrow], phases: [.down, .repeat]) { press in step(press) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Parts of the \(kind.title.lowercased())")
    }

    private func partRow(_ p: CEPart) -> some View {
        let on = !hidden.contains(p.id)
        let selected = selectedID == p.id
        return HStack(spacing: 6) {
            Toggle(p.label, isOn: Binding(get: { on }, set: { v in setVisible(p.id, v) }))
                .toggleStyle(.checkbox)
                .labelsHidden()
                .help(on ? "Hide the \(p.labelInSentence)" : "Show the \(p.labelInSentence)")
            Button { select(p.id, follow: true) } label: {
                HStack(spacing: 8) {
                    RoundedRectangle(cornerRadius: 3, style: .continuous)
                        .fill(p.colour.color)
                        .frame(width: 12, height: 12)
                        .overlay(RoundedRectangle(cornerRadius: 3, style: .continuous).strokeBorder(Color.primary.opacity(0.2), lineWidth: 0.5))
                    Text(p.label)
                        .lineLimit(1)
                        .foregroundStyle(on ? Color.primary : Color.secondary)
                    Spacer(minLength: 0)
                }
                .padding(.vertical, 4)
                .padding(.horizontal, 6)
                .background(selected ? Theme.accent.opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled(quiz != nil)
            .help(p.name)
            .accessibilityLabel(p.name)
            .accessibilityHint(on ? "Shows its card" : "Hidden in the model; shows its card")
            .accessibilityAddTraits(selected ? .isSelected : [])
        }
    }

    private var learnButtons: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Button { startQuiz(.find) } label: { Label("Find the…", systemImage: "scope").frame(maxWidth: .infinity) }
                    .glassButton()
                    .help("A quiz: press the part named, ten questions")
                Button { startQuiz(.name) } label: { Label("Name it", systemImage: "questionmark.circle").frame(maxWidth: .infinity) }
                    .glassButton()
                    .help("A quiz: one part glows; choose its name from four, ten questions")
            }
            .disabled(quiz != nil)
            Button(action: makeFlashcards) {
                Label("Make flashcards", systemImage: "rectangle.on.rectangle.angled").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("A Flashcards set of this cell's parts and what each does")
            if let madeSet {
                Text("Made “\(madeSet)” in Flashcards.").font(.sCaption).foregroundStyle(.secondary)
            }
            Button(action: whySmall) {
                Label("Why are cells small?", systemImage: "cube.transparent").frame(maxWidth: .infinity)
            }
            .glassButton()
            .help("Surface area to volume, in the Bio Lab Calculator, with this cell's size")
        }
        .controlSize(.large)
    }

    // MARK: The scene

    private var sceneCard: some View {
        BioSceneCard {
            Text(kind.title).font(.system(size: 22, weight: .bold))
            CEScaleChip(trueScale: trueScale)
            if let iso = isolate, let p = CEParts.part(kind, iso) {
                Button { withAnimation(anim) { isolate = nil; lookInside = false } } label: {
                    Label(lookInside ? "Inside: \(p.label)" : "Only: \(p.label)", systemImage: "xmark.circle.fill").font(.sCallout.weight(.medium))
                }
                .glassButton(prominent: true)
                .help("Show the whole cell again (Escape)")
            }
            Spacer(minLength: 8)
            BioSceneButtons(turning: $turning) { reset += 1 }
        } scene: {
            ZStack(alignment: .bottomLeading) {
                BioSceneView(key: sceneKey,
                             build: BioSceneBuild { [o = options, v = look] in CEScene.content(CEBuildCache.shared.model(o), v) },
                             extras: wobbleNodes,
                             selection: Binding(get: { isolate == nil ? (quizGlow ?? selection) : nil }, set: { selection = $0 }),
                             showLabels: labels && quiz == nil,
                             turning: turning,
                             frameKey: "\(kind.rawValue)|\(isolate ?? "")|\(lookInside)",
                             reset: reset,
                             zoom: zoom,
                             accessibility: CEScene.accessibility(model, kind: kind, cutaway: cutaway, hidden: hidden, selected: selectedID),
                             onPick: pick,
                             onDoubleClick: doubleClick,
                             onHover: { hovered = $0 },
                             onScale: { scale = $0 },
                             labelBottom: CEScaleBar.room)
                CEScaleBar(pointsPerUnit: scale, badge: trueScale ? nil : "Not to scale")
                    .padding(14)
                    .allowsHitTesting(false)
                VStack {
                    if let q = quiz, let current = q.current {
                        CEQuizPrompt(quiz: q, question: current, feedback: feedback, shake: shake)
                    } else if let h = hovered.flatMap({ CECellPage.partID($0) }), let p = CEParts.part(kind, h) {
                        Text(p.label)
                            .font(.system(size: 12, weight: .semibold))
                            .padding(.horizontal, 10)
                            .padding(.vertical, 4)
                            .background(.regularMaterial, in: Capsule())
                            .transition(.opacity)
                            .accessibilityHidden(true)
                    }
                    Spacer()
                }
                .frame(maxWidth: .infinity)
                .padding(.top, 6)
                .allowsHitTesting(false)
                .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: hovered)
            }
        }
    }

    // MARK: The right column: the card, the quiz, or about this cell

    @ViewBuilder
    private var rightColumn: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let q = quiz {
                CEQuizPanel(quiz: q, kind: kind, feedback: feedback, answer: answerName, skip: skipQuestion,
                            again: { startQuiz(q.mode) }, end: endQuiz)
                    .transition(.opacity)
            } else if let p = selectedPart {
                card(p).id(p.id).transition(.opacity)
            } else {
                about.transition(.opacity)
            }
        }
    }

    private func card(_ p: CEPart) -> some View {
        BioCard(title: p.name, swatch: p.colour.color, subtitle: p.group.title) {
            CEFactRow(label: "Function", markdown: p.function)
            BioFactRow(label: "Structure", text: p.structureText)
            if p.size != "—" {
                BioFactRow(label: "Size", text: p.size)
            }
            if let m = model, !m.modelLine(p).isEmpty {
                BioFactRow(label: "In this model", text: m.modelLine(p))
            }
            VStack(alignment: .leading, spacing: 3) {
                Text("Found in").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Text(p.foundInText).font(.sCallout)
                ForEach(p.foundInNotes, id: \.self) { n in
                    Text(n).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
            }
            .accessibilityElement(children: .combine)
            if let mix = p.mixup {
                CEFactRow(label: "Common mix-up", markdown: mix, tint: .orange)
            }
            if hidden.contains(p.id) {
                Button { setVisible(p.id, true) } label: { Label("Show it in the model", systemImage: "eye") }
                    .glassButton()
            }
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], alignment: .leading, spacing: 8) {
                Button { toggleIsolate(p.id) } label: {
                    Label(isolate == p.id && !lookInside ? "Show all" : "Isolate", systemImage: "circle.dashed.inset.filled").frame(maxWidth: .infinity)
                }
                .glassButton(prominent: isolate == p.id && !lookInside)
                .help("Show only the \(p.labelInSentence), framed and enlarged (Escape to come back)")
                Button { zoomTo(p.id) } label: { Label("Zoom to", systemImage: "plus.magnifyingglass").frame(maxWidth: .infinity) }
                    .glassButton()
                    .help("Bring the camera to it (or double-click it)")
                if CEScene.canLookInside(p.id) {
                    Button { toggleInside(p.id) } label: {
                        Label(isolate == p.id && lookInside ? "Whole cell" : "Look inside", systemImage: "scissors").frame(maxWidth: .infinity)
                    }
                    .glassButton(prominent: isolate == p.id && lookInside)
                    .help(p.id == "nucleus" ? "The nucleus cut open: envelope, pores, nucleolus and chromatin" : "One cut lengthwise, to see inside")
                }
                Button { withAnimation(anim) { selection = nil } } label: { Label("Done", systemImage: "checkmark").frame(maxWidth: .infinity) }
                    .glassButton()
                    .help("Close the card (Escape)")
            }
            .controlSize(.regular)
        }
    }

    private var about: some View {
        let a = CEAbout.of(kind)
        return BioCard(title: "About this cell", subtitle: kind.title) {
            BioFactRow(label: "Size", text: a.size)
            BioFactRow(label: "What makes it distinctive", text: a.distinctive)
            if let m = model {
                BioFactRow(label: "Surface and volume", text: m.surfaceLine + ". " + (kind == .bacterium
                    ? "A small cell has far more surface for its volume, so materials reach every part quickly."
                    : "The bigger a cell, the less surface it has for each µm³ of inside to feed."))
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Try this").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                ForEach(a.tryThis, id: \.self) { t in
                    HStack(alignment: .firstTextBaseline, spacing: 6) {
                        Text("•").foregroundStyle(.secondary)
                        Text(t).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                    }
                }
            }
            Text("Click any part, its label or its row on the left for its card. Tab and the arrow keys step through the parts.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Actions

    /// A part's card. Chosen from the parts list (`follow`) while another part is isolated, the new part is isolated
    /// instead (seen inside only if it can be); a click in the scene keeps the view as it is (the nucleolus inside the
    /// nucleus opened).
    private func select(_ part: String, follow: Bool = false) {
        withAnimation(anim) {
            selection = "organelle:\(part)"
            if follow, let iso = isolate, iso != part {
                isolate = part
                if lookInside && !CEScene.canLookInside(part) { lookInside = false }
                if hidden.contains(part) { setVisible(part, true) }
            }
        }
    }

    private func setVisible(_ part: String, _ v: Bool) {
        if v { hidden.remove(part) } else { hidden.insert(part) }
        BioPrefs.set(.cell, "hidden.\(kind.rawValue)", hidden.sorted().joined(separator: ","))
    }

    /// A click in the scene (or on a label): its part's card; on nothing, back out of isolation, or let go.
    private func pick(_ id: String?) {
        guard let id else {
            if quiz != nil { return }
            withAnimation(anim) {
                if isolate != nil { isolate = nil; lookInside = false } else { selection = nil }
            }
            return
        }
        guard let part = CECellPage.partID(id) else { return }
        if quiz != nil { answerFind(part); return }
        select(part)
    }

    private func doubleClick(_ id: String?) {
        guard let id, quiz == nil else { return }
        if let part = CECellPage.partID(id) { select(part) }
        zoom = BioZoomRequest(id: id, serial: (zoom?.serial ?? 0) + 1)
    }

    /// The camera brought to the part's instance its label points to.
    private func zoomTo(_ part: String) {
        var id = "organelle:\(part)"
        if let m = model, let item = CEScene.anchorItemID(m, part: part) { id = item }
        if lookInside && isolate == part { id = "organelle:\(part)" }
        zoom = BioZoomRequest(id: id, serial: (zoom?.serial ?? 0) + 1)
    }

    private func toggleIsolate(_ part: String) {
        if hidden.contains(part) { setVisible(part, true) }
        withAnimation(anim) {
            if isolate == part && !lookInside { isolate = nil } else { isolate = part; lookInside = false }
        }
    }

    private func toggleInside(_ part: String) {
        if hidden.contains(part) { setVisible(part, true) }
        withAnimation(anim) {
            if isolate == part && lookInside { isolate = nil; lookInside = false } else { isolate = part; lookInside = true }
        }
    }

    /// After the explode slider is let go: the camera framing the cell as far apart as it now is.
    private func refit() {
        guard abs(explode - framedExplode) > 0.2 else { return }
        framedExplode = explode
        reset += 1
    }

    /// Tab, ↑ and ↓ in the parts list: the next or previous part shown, selected (Tab past either end leaves the list).
    private func step(_ press: KeyPress) -> KeyPress.Result {
        guard quiz == nil else { return .ignored }
        let order = parts.filter { !hidden.contains($0.id) }.map(\.id)
        guard !order.isEmpty else { return .ignored }
        let back = press.key == .upArrow || (press.key == .tab && press.modifiers.contains(.shift))
        var next: Int
        if let s = selectedID, let i = order.firstIndex(of: s) { next = i + (back ? -1 : 1) } else { next = back ? order.count - 1 : 0 }
        if press.key == .tab && (next < 0 || next >= order.count) { return .ignored }
        next = min(max(next, 0), order.count - 1)
        select(order[next], follow: true)
        return .handled
    }

    private func makeFlashcards() {
        let name = "Organelles (\(kind.rawValue))"
        let cards = parts.map { p in FCCard(term: p.name, def: p.function.replacingOccurrences(of: "**", with: "")) }
        let id = FlashcardStore.shared.add(FCDeck(name: name, cards: cards))
        madeSet = name
        BioTools.open(.fc, info: ["deck": id])
    }

    /// The Bio Lab Calculator's Cells page (surface area to volume), with this cell as the shape the AP sheet has for it:
    /// a sphere of the animal cell's volume, the plant cell's box, the bacterium's cylinder.
    private func whySmall() {
        var info: [String: String] = ["from": "cell"]
        switch kind {
        case .animal:
            let d = model.map { pow(6 * $0.volume / Double.pi, 1.0 / 3) } ?? 19
            info["savShape"] = "sphere"
            info["savSize"] = String(halfUp: "%.1f", d)
        case .plant:
            info["savShape"] = "box"
            info["savSize"] = "44,26,26"
        case .bacterium:
            info["savShape"] = "cylinder"
            info["savSize"] = "1,2"
        }
        info["savUnit"] = "µm"
        BioNav.shared.set(.biolab, "cells")
        BioTools.open(.biolab, info: info)
    }

    // MARK: The quiz

    private var quizParts: [String] {
        let shown = model?.partsShown ?? Set(parts.filter(\.defaultVisible).map(\.id))
        return parts.map(\.id).filter { shown.contains($0) && !hidden.contains($0) && $0 != "cytoplasm" }
    }

    private func startQuiz(_ mode: CEQuiz.Mode) {
        var g = SystemRandomNumberGenerator()
        let q = CEQuiz(mode: mode, kind: kind, parts: quizParts, using: &g)
        withAnimation(anim) {
            isolate = nil
            lookInside = false
            selection = nil
            feedback = nil
            quiz = q
            quizGlow = mode == .name ? q.current.map { "organelle:\($0.part)" } : nil
        }
    }

    private func endQuiz() {
        withAnimation(anim) {
            quiz = nil
            quizGlow = nil
            feedback = nil
        }
    }

    /// "Find the …": the part pressed. Right: on to the next. Wrong: its name, and the prompt wobbles.
    private func answerFind(_ part: String) {
        guard var q = quiz, q.mode == .find, q.current != nil else { return }
        let name = CEParts.part(kind, part)?.name ?? part
        let right = q.answer(part)
        quiz = q
        withAnimation(anim) {
            feedback = right ? (right: true, text: "Yes: the \(CEPart.inSentence(name)).") : (right: false, text: "That’s the \(CEPart.inSentence(name)).")
            quizGlow = "organelle:\(part)"
        }
        if !right && !reduceMotion {
            withAnimation(.linear(duration: 0.4)) { shake += 1 }
            // (the part pressed wobbles, then goes back into the scene)
            wobbleSerial += 1
            wobble = part
            let serial = wobbleSerial
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 900_000_000)
                if wobbleSerial == serial { wobble = nil }
            }
        }
        let shownFeedback = feedback?.text
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: right ? 700_000_000 : 1_100_000_000)
            guard feedback?.text == shownFeedback else { return }
            withAnimation(anim) {
                quizGlow = nil
                if right { feedback = nil }
            }
        }
    }

    /// "Name it": a name chosen; the answer shown a moment, then the next part glows.
    private func answerName(_ name: String) {
        guard var q = quiz, q.mode == .name, let current = q.current else { return }
        let rightName = CEParts.part(kind, current.part)?.name ?? current.part
        let right = q.answer(name)
        quiz = q
        withAnimation(anim) { feedback = right ? (right: true, text: "Yes: \(rightName).") : (right: false, text: "It’s the \(CEPart.inSentence(rightName)).") }
        let shown = feedback?.text
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: right ? 800_000_000 : 1_600_000_000)
            guard feedback?.text == shown else { return }
            withAnimation(anim) {
                feedback = nil
                quizGlow = quiz?.current.map { "organelle:\($0.part)" }
            }
        }
    }

    private func skipQuestion() {
        guard var q = quiz else { return }
        q.skip()
        withAnimation(anim) {
            quiz = q
            feedback = nil
            quizGlow = q.mode == .name ? q.current.map { "organelle:\($0.part)" } : nil
        }
    }

    // MARK: The launch's state

    /// A part a Compare row asked for, selected; and (the screenshot suite) the launch's state told on the console as
    /// the page shows it — the state itself is the page's from its first frame (init).
    private func applyLaunch() {
        if let p = CENav.shared.pending, p.kind == kind {
            CENav.shared.pending = nil
            hidden.remove(p.part)
            selection = "organelle:\(p.part)"
        }
        guard OpenClock.verbose, !launchTold, let s = state else { return }
        launchTold = true
        NSLog("[Simpl] bio launch: cell %@ on the %@ page: scale %@, cutaway %@, explode %.2f, selected %@, quiz %@", s.raw, kind.rawValue,
              trueScale ? "true" : "teaching", cutaway ? "on" : "off", explode, selection ?? "none", quiz.map { $0.mode.rawValue } ?? "none")
    }
}
