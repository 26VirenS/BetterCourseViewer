import SwiftUI

// Genetics Crosses (spec §4.5): a cross of one to three genes — complete, incomplete and codominance, ABO, X-linked,
// lethal — with each parent's gametes, the Punnett square (or the forked-line diagram), and its genotypes and
// phenotypes kept in separate boxes (confusing the two ratios is a named exam error); the product and sum rules; the
// chi-square test with its null hypothesis written out; and a pedigree's mode checker. The arithmetic is exact
// (BioCross, CrossesModel.swift); this file has the tool, its state, the Cross page and the pin's quick version. The other
// pages are CrossesProbability.swift, CrossesChiSquare.swift and CrossesPedigree.swift; the pieces CrossesParts.swift.

/// The Crosses tool's state: kept while its pages change, remembered between launches (SimplBio.cross.*).
@MainActor
final class CrossesState: ObservableObject {
    static let shared = CrossesState()

    enum Diagram: String { case square, forked }

    // MARK: Cross
    @Published var setup: CrossSetup {
        didSet {
            guard setup != oldValue else { return }
            typed = setup.text
            typedError = nil
            BioPrefs.set(.cross, "setup", setup.encoded)
            // (the chi-square categories follow the cross they were filled from, keeping counts already typed)
            if chiSource == oldValue.text {
                chiRows = ChiSquareWork.rows(from: setup.cross.result(), keeping: chiRows)
                chiSource = setup.text
            }
        }
    }
    /// The typed cross, as the field holds it.
    @Published var typed: String
    @Published var typedError: String?
    /// What the test cross could not do.
    @Published var notes: [String] = []
    /// The square or the forked line; nil until chosen (the forked line for three genes, else the square).
    @Published var diagram: Diagram?
    @Published var forkedByGenotype = false
    @Published var whyShown = false

    // MARK: Probability
    @Published var probSetup: CrossSetup {
        didSet {
            guard probSetup != oldValue else { return }
            probTyped = probSetup.text
            probError = nil
            fitTargets()
            BioPrefs.set(.cross, "probSetup", probSetup.encoded)
        }
    }
    @Published var probTyped: String
    @Published var probError: String?
    /// The chip chosen for each gene (CrossTarget ids).
    @Published var probTargets: [String]
    @Published var sumGene = 0
    @Published var sumTargets: Set<String> = ["g00", "g11"]
    @Published var children = 3
    @Published var exactlyK = 1

    // MARK: Chi-square
    @Published var chiRows: [ChiCategory] { didSet { saveChi() } }
    /// Where the rows came from ("Mendel's seed shapes", "AaBb × AaBb").
    @Published var chiSource: String { didSet { BioPrefs.set(.cross, "chiSource", chiSource) } }

    // MARK: Pedigree
    @Published var pedigree: Pedigree {
        didSet {
            guard pedigree != oldValue else { return }
            BioPrefs.set(.cross, "pedigree", pedigree.encoded)
            verdicts = PedigreeChecker.check(pedigree)
        }
    }
    /// The mode checker's verdicts for the pedigree (worked out when it changes, not on every redraw).
    @Published private(set) var verdicts: [PedigreeVerdict] = []
    @Published var selected: Int?
    /// The mode whose ruling-out people are ringed.
    @Published var shownMode: PedigreeMode?

    init() {
        let s = BioPrefs.string(.cross, "setup").flatMap { CrossSetup(encoded: $0) } ?? .standard
        setup = s
        typed = s.text
        let p = BioPrefs.string(.cross, "probSetup").flatMap { CrossSetup(encoded: $0) } ?? CrossSetup.parse("AaBbCc × AaBbCc").setup ?? .standard
        probSetup = p
        probTyped = p.text
        probTargets = []
        if let d = BioPrefs.string(.cross, "chiRows").flatMap({ try? JSONDecoder().decode([ChiCategory].self, from: Data($0.utf8)) }), d.count >= 2 {
            chiRows = d
            chiSource = BioPrefs.string(.cross, "chiSource") ?? "Your own"
        } else {
            // (§4.5: prefilled from the current cross — its phenotypes and their ratio — for the student's own counts)
            chiRows = ChiSquareWork.rows(from: s.cross.result())
            chiSource = s.text
        }
        pedigree = BioPrefs.string(.cross, "pedigree").flatMap { Pedigree.decode($0) } ?? Pedigree.examples[0].pedigree
        verdicts = PedigreeChecker.check(pedigree)
        fitTargets()
        // (the product rule's example from §4.5: aaB_Cc)
        if probSetup.text == "AaBbCc × AaBbCc" { probTargets = ["g11", "dominant", "g01"] }
    }

    var shownDiagram: Diagram { diagram ?? (setup.genes.count >= 3 ? .forked : .square) }

    /// The typed cross put in place (if it changed), or what is wrong with it said.
    func applyTyped() {
        let t = typed.trimmingCharacters(in: .whitespaces)
        guard t != setup.text else { typedError = nil; return }
        let r = CrossSetup.parse(t)
        if let s = r.setup {
            notes = []
            setup = s
        } else {
            typedError = r.error
        }
    }

    func testCross() {
        var s = setup
        notes = s.testCross()
        setup = s
    }

    // MARK: Probability

    /// Each gene's chip kept if it still exists, else "any".
    func fitTargets() {
        let genes = probSetup.genes
        var out: [String] = []
        for (k, g) in genes.enumerated() {
            let ids = CrossProbability.targets(g).map(\.id)
            let old = k < probTargets.count ? probTargets[k] : (ids.contains("g11") ? "g11" : "any")
            out.append(ids.contains(old) ? old : "any")
        }
        probTargets = out
        if sumGene >= genes.count { sumGene = 0 }
    }

    func applyProbTyped() {
        let t = probTyped.trimmingCharacters(in: .whitespaces)
        guard t != probSetup.text else { probError = nil; return }
        let r = CrossSetup.parse(t)
        if let s = r.setup { probSetup = s } else { probError = r.error }
    }

    /// The chips chosen, as targets.
    var chosenTargets: [CrossTarget] {
        probSetup.genes.enumerated().map { k, g in
            let all = CrossProbability.targets(g)
            let id = k < probTargets.count ? probTargets[k] : "any"
            return all.first { $0.id == id } ?? all[all.count - 1]
        }
    }

    // MARK: Chi-square

    static let mendelSeeds = [ChiCategory(id: 0, name: "round", ratio: "3", observed: "5474"),
                              ChiCategory(id: 1, name: "wrinkled", ratio: "1", observed: "1850")]
    static let mendelDihybrid = [ChiCategory(id: 0, name: "round yellow", ratio: "9", observed: "315"),
                                 ChiCategory(id: 1, name: "round green", ratio: "3", observed: "108"),
                                 ChiCategory(id: 2, name: "wrinkled yellow", ratio: "3", observed: "101"),
                                 ChiCategory(id: 3, name: "wrinkled green", ratio: "1", observed: "32")]

    func fillChiFromCross() {
        chiRows = ChiSquareWork.rows(from: setup.cross.result())
        chiSource = setup.text
    }

    func addChiRow() {
        let id = (chiRows.map(\.id).max() ?? -1) + 1
        chiRows.append(ChiCategory(id: id, name: "", ratio: "1", observed: ""))
        chiSource = "Your own"
    }

    func removeChiRow(_ id: Int) {
        guard chiRows.count > 2 else { return }
        chiRows.removeAll { $0.id == id }
        chiSource = "Your own"
    }

    private func saveChi() {
        guard let d = try? JSONEncoder().encode(chiRows) else { return }
        BioPrefs.set(.cross, "chiRows", String(decoding: d, as: UTF8.self))
    }

    // MARK: A launch's state, a hand-off

    private static var launched = false

    /// What a launch asked (-SimplTool cross -SimplToolState …, -SimplBioCross "AaBb x AaBb"), once.
    func applyLaunch(_ state: BioLaunchState?) {
        guard let state, !CrossesState.launched else { return }
        CrossesState.launched = true
        if let c = state.value("cross") {
            typed = c
            applyTyped()
        }
        // (a textbook gene alone: example=snapdragon, yellow, colourblind … — §5.3.1's)
        if let e = state.value("example").flatMap({ CrossExamples.find($0) }) {
            var s = CrossSetup.standard
            if s.applyExample(e, gene: 0) { setup = s }
        }
        if let v = state.value("view"), let d = Diagram(rawValue: v) { diagram = d }
        if state.has("forked") { diagram = .forked }
        if state.has("square") { diagram = .square }
        if state.has("genotypes") { forkedByGenotype = true }
        if state.has("testcross") { testCross() }
        if state.has("why") { whyShown = true }
        if let p = state.value("prob") {
            probTyped = p
            applyProbTyped()
        }
        if let t = state.value("target") {
            let parts = t.split(separator: ",").map { String($0).trimmingCharacters(in: .whitespaces) }
            for (k, g) in probSetup.genes.enumerated() where k < parts.count {
                if let hit = CrossProbability.targets(g).first(where: { $0.label == parts[k] || $0.id == parts[k] }), k < probTargets.count { probTargets[k] = hit.id }
            }
        }
        if let n = state.value("children").flatMap({ Int($0) }) { children = min(max(n, 1), 20) }
        if state.has("fromcross") { fillChiFromCross() }
        if let o = state.value("obs") {
            let counts = o.split(separator: ",").map { String($0).trimmingCharacters(in: .whitespaces) }
            if counts.count == 4 && chiRows.count != 4 { chiRows = CrossesState.mendelDihybrid; chiSource = "Mendel's dihybrid cross" }
            for i in chiRows.indices where i < counts.count { chiRows[i].observed = counts[i] }
        }
        if let e = state.value("example"), let ex = Pedigree.examples.first(where: { $0.id == e }) {
            pedigree = ex.pedigree
            selected = nil
        }
        if let m = state.value("mode")?.lowercased() {
            shownMode = PedigreeMode.allCases.first { $0.rawValue.lowercased() == m || $0.short.lowercased() == m }
        }
        if let s = state.value("select").flatMap({ Int($0) }), pedigree.person(s) != nil { selected = s }
    }

    /// What another tool or the pin handed over: a cross (["cross": "Aa × Aa"]), or chi-square rows
    /// (["chi": "round:3:5474;wrinkled:1:1850"]).
    func takeHandoff() {
        let tools = ToolsCenter.shared
        if let c = tools.takeInfo("cross") {
            typed = c
            applyTyped()
            BioNav.shared.set(.cross, "cross")
        }
        if let chi = tools.takeInfo("chi") {
            let rows = chi.split(separator: ";").enumerated().compactMap { item -> ChiCategory? in
                let f = item.element.split(separator: ":", omittingEmptySubsequences: false).map(String.init)
                guard f.count >= 2 else { return nil }
                return ChiCategory(id: item.offset, name: f[0], ratio: f[1], observed: f.count > 2 ? f[2] : "")
            }
            if rows.count >= 2 {
                chiRows = rows
                chiSource = "Handed over"
                BioNav.shared.set(.cross, "chi")
            }
        }
    }
}

struct CrossesTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// Its pages, picked in its header; the page shown is `BioNav.shared.page(.cross)`.
    static let pages: [BioPage] = [
        BioPage(id: "cross", title: "Cross", symbol: "square.grid.2x2", help: "A cross and its Punnett square"),
        BioPage(id: "probability", title: "Probability", symbol: "percent", help: "The product and sum rules"),
        BioPage(id: "chi", title: "Chi-square", symbol: "chart.bar", help: "Observed against expected"),
        BioPage(id: "pedigree", title: "Pedigree", symbol: "person.2", help: "Which inheritance a pedigree fits"),
    ]

    @ObservedObject private var nav = BioNav.shared
    @ObservedObject private var model = CrossesState.shared

    var body: some View {
        page
            .onAppear {
                model.applyLaunch(state)
                model.takeHandoff()
            }
    }

    @ViewBuilder
    private var page: some View {
        switch nav.page(.cross) {
        case "probability": CrossProbabilityPage(model: model)
        case "chi": CrossChiSquarePage(model: model)
        case "pedigree": CrossPedigreePage(model: model)
        default: CrossPage(model: model)
        }
    }
}

// MARK: - The Cross page

/// The Cross page: the genes and parents on the left, the gametes and the square (or forked line) in the middle, the
/// genotypes and the phenotypes on the right, in boxes of their own.
struct CrossPage: View {
    @ObservedObject var model: CrossesState
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var typing: Bool

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            centre
        } right: {
            CrossResults(setup: model.setup)
        }
        .floatingPopup("cross-why", title: "Why these gametes?", isPresented: $model.whyShown) { // (1.3.79: never blocking)
            AssortmentSheet(setup: model.setup) { model.whyShown = false }
        }
    }

    private func change(_ edit: @escaping () -> Void) {
        if reduceMotion { edit() } else { withAnimation(Motion.snappy) { edit() } }
    }

    // MARK: Controls

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Cross")
            VStack(alignment: .leading, spacing: 6) {
                TextField("AaBb × aaBb", text: $model.typed)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 15, weight: .medium))
                    .focused($typing)
                    .onSubmit { change { model.applyTyped() } }
                    .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.red, lineWidth: 1.5).opacity(model.typedError == nil ? 0 : 1))
                    .help("Type a cross and press Return (⌘F): AaBb × aaBb, X^A X^a × X^a Y, I^A i × I^B i, C^R C^W × C^R C^W")
                    .background {
                        Button("") { typing = true }
                            .keyboardShortcut("f", modifiers: .command)
                            .opacity(0)
                            .frame(width: 0, height: 0)
                            .accessibilityHidden(true)
                    }
                if let e = model.typedError {
                    Label(e, systemImage: "exclamationmark.circle")
                        .font(.sCaption)
                        .foregroundStyle(.red)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    Text("Type one with × or x between the parents, and ^ before a superscript. Return reads it.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            BioHeading("Genes")
            ForEach(model.setup.genes.indices, id: \.self) { k in
                CrossGeneEditor(model: model, k: k)
            }
            Button {
                change { model.setup.addGene() }
            } label: {
                Label("Add a gene", systemImage: "plus").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .disabled(model.setup.genes.count >= CrossSetup.maxGenes)
            .help("Up to three genes, each on its own pair of chromosomes")
            BioHeading("Test cross")
            Button {
                change { model.testCross() }
            } label: {
                Text("Cross parent 2 with the recessive").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Make parent 2 homozygous recessive for every gene")
            Text("A test cross finds an unknown genotype: crossed with aa, an AA parent gives all dominant offspring, an Aa parent 1 dominant : 1 recessive.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            ForEach(model.notes, id: \.self) { n in
                Label(n, systemImage: "info.circle").font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .font(.sCallout)
    }

    // MARK: Centre

    private var centre: some View {
        let cross = model.setup.cross
        let parents = model.setup.xIndex != nil ? ("♀ Mother", "♂ Father") : ("Parent 1", "Parent 2")
        return VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 12) {
                Allele.text(model.setup.text, size: 22, weight: .bold)
                    .lineLimit(1)
                    .minimumScaleFactor(0.6)
                    .accessibilityLabel("Cross: \(Allele.spoken(model.setup.text))")
                Spacer(minLength: 8)
                Picker("", selection: Binding(get: { model.shownDiagram }, set: { d in change { model.diagram = d } })) {
                    Text("Punnett square").tag(CrossesState.Diagram.square)
                    Text("Forked line").tag(CrossesState.Diagram.forked)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .help("The whole square, or each gene's outcomes multiplied along branches")
            }
            .padding(16)
            ScrollView([.horizontal, .vertical]) {
                VStack(alignment: .leading, spacing: 18) {
                    gametes(cross, parents)
                    if model.shownDiagram == .square {
                        PunnettGrid(square: CrossPunnett(cross))
                        if cross.gametes1.count * cross.gametes2.count >= 64 {
                            Text("64 cells: the forked line shows the same thing more clearly.")
                                .font(.sCaption)
                                .foregroundStyle(.secondary)
                        }
                    } else {
                        forked(cross)
                    }
                    legend(cross.result())
                }
                .padding(.horizontal, 18)
                .padding(.bottom, 18)
            }
            Text(footer(cross))
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .multilineTextAlignment(.center)
                .padding(12)
        }
        .card(radius: 20)
    }

    /// Each parent's gametes (side by side while they are few, one over the other when there are eight), and "Why?".
    @ViewBuilder
    private func gametes(_ cross: BioCross, _ parents: (String, String)) -> some View {
        let g1 = cross.gametes1.map { CrossGamete(text: cross.gameteText($0.gamete), p: $0.p) }
        let g2 = cross.gametes2.map { CrossGamete(text: cross.gameteText($0.gamete), p: $0.p) }
        let first = GameteRow(title: parents.0, genotype: cross.genotypeText(model.setup.parent1), gametes: g1, tint: BioPalette.maternal.color)
        let second = GameteRow(title: parents.1, genotype: cross.genotypeText(model.setup.parent2), gametes: g2, tint: BioPalette.paternal.color)
        let why = Button {
            model.whyShown = true
        } label: {
            Label("Why these gametes?", systemImage: "questionmark.circle")
        }
        .buttonStyle(.link)
        .help("How meiosis makes them: segregation and independent assortment")
        if max(g1.count, g2.count) > 4 {
            VStack(alignment: .leading, spacing: 12) {
                first
                second
                why
            }
        } else {
            // ("Why?" under the two, so the pair stays narrow enough for a small window's card)
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .top, spacing: 24) {
                    first
                    second
                }
                why
            }
        }
    }

    private func forked(_ cross: BioCross) -> some View {
        let line = CrossForkedLine(cross, genotypes: model.forkedByGenotype)
        return VStack(alignment: .leading, spacing: 10) {
            Picker("", selection: Binding(get: { model.forkedByGenotype }, set: { v in change { model.forkedByGenotype = v } })) {
                Text("Phenotypes").tag(false)
                Text("Genotypes").tag(true)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            ForkedLineView(line: line, genes: cross.genes, tints: model.forkedByGenotype ? [:] : CrossColors.tints(cross.result()))
            Text(model.forkedByGenotype
                 ? "Each gene's genotypes from its own small square, multiplied along each branch: \(line.leaves.count) genotypes."
                 : "Each gene's phenotypes from its own small square, multiplied along each branch (the product rule).")
                .font(.sCaption)
                .foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private func legend(_ r: BioCrossResult) -> some View {
        if r.phenotypes.count > 1 || r.genotypes.contains(where: \.dies) {
            HStack(spacing: 14) {
                ForEach(r.phenotypes.indices, id: \.self) { i in
                    HStack(spacing: 6) {
                        RoundedRectangle(cornerRadius: 3).fill(CrossColors.phenotype(i, label: r.phenotypes[i].text).opacity(0.6)).frame(width: 12, height: 12)
                        Allele.text(r.phenotypes[i].text, size: 12, weight: .medium)
                    }
                }
                if r.genotypes.contains(where: \.dies) {
                    HStack(spacing: 6) {
                        Hatching().frame(width: 12, height: 12).clipShape(RoundedRectangle(cornerRadius: 3))
                        Text("dies before birth").font(.sCaption)
                    }
                }
            }
            .accessibilityElement(children: .combine)
        }
    }

    private func footer(_ cross: BioCross) -> String {
        let n1 = cross.gametes1.count, n2 = cross.gametes2.count
        // (the forked line has no cells to hover: its own line, not the square's)
        if model.shownDiagram == .forked {
            return "\(cross.genes.count) gene\(cross.genes.count == 1 ? "" : "s"), each split on its own; multiply along a branch for each outcome's chance."
        }
        return "\(n1) × \(n2) = \(n1 * n2) cell\(n1 * n2 == 1 ? "" : "s"), each as likely as the next. Hover a cell to light every cell with its genotype."
    }
}

/// One gene of the cross: its letter, its pattern, an example from §5.3.1, and each parent's genotype (menus listing only
/// the genotypes that parent can have).
struct CrossGeneEditor: View {
    @ObservedObject var model: CrossesState
    let k: Int
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        if model.setup.genes.indices.contains(k) {
            content(model.setup.genes[k])
        }
    }

    private func change(_ edit: @escaping () -> Void) {
        if reduceMotion { edit() } else { withAnimation(Motion.snappy) { edit() } }
    }

    private func content(_ g: BioGene) -> some View {
        let x = model.setup.xIndex != nil
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                letterMenu(g).frame(width: 64)
                patternMenu(g)
                if model.setup.genes.count > 1 {
                    Button {
                        change { model.setup.removeGene(k) }
                    } label: {
                        Image(systemName: "minus.circle").font(.system(size: 15))
                    }
                    .buttonStyle(.borderless)
                    .help("Take gene \(String(g.letter)) out of the cross")
                    .accessibilityLabel("Remove gene \(String(g.letter))")
                }
            }
            exampleMenu
            parentRow(g, parent: 1, title: x ? "♀ Mother" : "Parent 1")
            parentRow(g, parent: 2, title: x ? "♂ Father" : "Parent 2")
        }
        .padding(12)
        .card(radius: 14)
    }

    private func letterMenu(_ g: BioGene) -> some View {
        CrossMenu(help: g.pattern == .abo ? "The ABO gene is I" : "The gene's letter") {
            ForEach(model.setup.freeLetters(gene: k), id: \.self) { l in
                Button { change { model.setup.setLetter(l, gene: k) } } label: { CrossMenuItem(title: String(l), on: l == g.letter) }
            }
        } label: {
            Text(String(g.letter)).font(.system(size: 16, weight: .bold))
        }
        .disabled(g.pattern == .abo)
        .accessibilityLabel("Gene letter \(String(g.letter))")
    }

    private func patternMenu(_ g: BioGene) -> some View {
        CrossMenu(help: "How the gene's alleles show") {
            ForEach(BioInheritance.allCases, id: \.self) { p in
                Button { change { model.setup.setPattern(p, gene: k) } } label: { CrossMenuItem(title: p.label, on: p == g.pattern) }
                    .disabled(!model.setup.allowed(p, gene: k))
            }
        } label: {
            Text(g.pattern.label).font(.sCallout.weight(.semibold)).lineLimit(1).minimumScaleFactor(0.8)
        }
        .accessibilityLabel("Pattern: \(g.pattern.label)")
    }

    private var exampleMenu: some View {
        let current = k < model.setup.examples.count ? model.setup.examples[k] : nil
        let title = current.flatMap { CrossExamples.find($0)?.title } ?? "Example…"
        return CrossMenu(help: "A gene from the textbook: its alleles, pattern and usual parents") {
            ForEach(CrossExamples.all) { e in
                Button { change { _ = model.setup.applyExample(e, gene: k) } } label: { CrossMenuItem(title: e.title, on: e.id == current) }
                    .disabled(!model.setup.allowed(e.gene.pattern, gene: k))
            }
        } label: {
            Text(title).font(.sCallout).foregroundStyle(current == nil ? Color.secondary : Color.primary).lineLimit(1)
        }
    }

    private func parentRow(_ g: BioGene, parent: Int, title: String) -> some View {
        let current = parent == 1 ? model.setup.parent1[k] : model.setup.parent2[k]
        return HStack(spacing: 8) {
            Text(title).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary).frame(width: 70, alignment: .leading)
            CrossMenu(help: "\(title)'s genotype for gene \(String(g.letter))") {
                ForEach(model.setup.genotypes(gene: k, parent: parent), id: \.self) { gt in
                    Button {
                        change {
                            if parent == 1 { model.setup.parent1[k] = gt } else { model.setup.parent2[k] = gt }
                        }
                    } label: {
                        CrossMenuItem(title: Allele.plain(g.text(gt)) + "   " + g.phenotype(gt).label, on: gt == current)
                    }
                }
            } label: {
                HStack(spacing: 8) {
                    Allele.text(g.text(current), size: 15, weight: .bold)
                    Text(g.phenotype(current).label).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            .accessibilityLabel("\(title): \(Allele.spoken(g.text(current))), \(g.phenotype(current).label)")
        }
    }
}

// MARK: - The results

/// The right column: genotypes and phenotypes in boxes of their own (a named exam error is confusing their ratios),
/// daughters and sons apart for an X-linked gene, the dead counted apart from the born.
struct CrossResults: View {
    let setup: CrossSetup

    var body: some View {
        let c = setup.cross
        let all = c.result()
        let tints = CrossColors.tints(all)
        VStack(alignment: .leading, spacing: 14) {
            if c.hasXLinked {
                let d = c.result(sex: .female), s = c.result(sex: .male)
                genotypes([("Daughters", d), ("Sons", s), ("All offspring", all)], whole: all)
                phenotypes([("Daughters", d), ("Sons", s), ("All offspring", all)], tints: tints)
            } else {
                genotypes([(nil, all)], whole: all)
                phenotypes([(nil, all)], tints: tints)
            }
            Text("Genotypes count allele combinations; phenotypes count what shows. 1 AA : 2 Aa : 1 aa is one cross's genotypic ratio, and 3 : 1 its phenotypic ratio.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func genotypes(_ sections: [(String?, BioCrossResult)], whole: BioCrossResult) -> some View {
        BioCard(title: "Genotypes", subtitle: "Genotypic ratio") {
            ForEach(sections.indices, id: \.self) { i in
                let name = sections[i].0
                let r = sections[i].1
                VStack(alignment: .leading, spacing: 8) {
                    if let name { Text(name).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary) }
                    Allele.text(r.genotypes.count == 1 ? "all \(r.genotypes[0].text)" : r.genotypicRatioText, size: 16, weight: .semibold)
                        .fixedSize(horizontal: false, vertical: true)
                        .textSelection(.enabled)
                        .accessibilityLabel("Genotypic ratio: \(Allele.spoken(r.genotypicRatioText))")
                    if r.genotypes.contains(where: \.dies) {
                        Text("Born: \(BioRatio.text(r.bornGenotypicRatio)) — \((BioFrac.one - r.bornFraction).text) die before birth")
                            .font(.sCaption.weight(.semibold))
                            .foregroundStyle(.orange)
                    }
                    ForEach(r.genotypes.indices, id: \.self) { j in
                        // (the count in the square only where the shares are of the whole square)
                        genotypeRow(r.genotypes[j], total: name == nil || name == "All offspring" ? whole.squareSize : nil)
                    }
                }
                if i < sections.count - 1 { Divider() }
            }
        }
    }

    private func genotypeRow(_ g: BioCrossResult.GenotypeRow, total: Int?) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Allele.text(g.text, size: 14, weight: .semibold)
                .strikethrough(g.dies)
            if g.dies { Text("dies").font(.sCaption).foregroundStyle(.orange) }
            if g.carrier { Text("carrier").font(.sCaption).foregroundStyle(.secondary) }
            Spacer(minLength: 4)
            if let total { Text("\(g.count)/\(total)").font(.sCaption.monospacedDigit()).foregroundStyle(.tertiary) }
            Text(g.p.text).font(.sCallout.weight(.semibold).monospacedDigit())
            Text(g.p.percentText).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 46, alignment: .trailing)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(Allele.spoken(g.text))\(g.dies ? ", dies" : ""): \(g.p.text), \(g.p.percentText)")
    }

    private func phenotypes(_ sections: [(String?, BioCrossResult)], tints: [String: Color]) -> some View {
        BioCard(title: "Phenotypes", subtitle: "Phenotypic ratio (of those born)") {
            ForEach(sections.indices, id: \.self) { i in
                let name = sections[i].0
                let r = sections[i].1
                VStack(alignment: .leading, spacing: 8) {
                    if let name { Text(name).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary) }
                    Text(r.phenotypes.count == 1 ? "all \(r.phenotypes[0].text)" : BioRatio.text(r.phenotypicRatio))
                        .font(.system(size: 20, weight: .bold).monospacedDigit())
                        .textSelection(.enabled)
                    if r.phenotypes.count > 1 {
                        Allele.text(r.phenotypicRatioText, size: 13, weight: .medium)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    ForEach(r.phenotypes.indices, id: \.self) { j in
                        phenotypeRow(r.phenotypes[j], tint: tints[r.phenotypes[j].text] ?? .secondary)
                    }
                }
                if i < sections.count - 1 { Divider() }
            }
        }
    }

    private func phenotypeRow(_ p: BioCrossResult.PhenotypeRow, tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                RoundedRectangle(cornerRadius: 3).fill(tint.opacity(0.7)).frame(width: 11, height: 11)
                Allele.text(p.text, size: 14, weight: .semibold)
                Spacer(minLength: 4)
                Text(p.p.text).font(.sCallout.weight(.semibold).monospacedDigit())
                Text(p.p.percentText).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 46, alignment: .trailing)
            }
            if !p.carriers.isZero {
                Text("\((p.carriers / p.p).text) of these are carriers").font(.sCaption).foregroundStyle(.secondary).padding(.leading, 19)
            }
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - The pin's quick version

/// Genetics Crosses in its pin's popover (spec §3.2 item 3): a monohybrid cross — each parent AA, Aa or aa — its square
/// and both ratios; Open takes the cross to the whole tool.
struct CrossesCompact: View {
    let openFull: (_ info: [String: String]) -> Void
    @State private var p1 = [0, 1]
    @State private var p2 = [0, 1]

    private static let options: [[Int]] = [[0, 0], [0, 1], [1, 1]]

    var body: some View {
        let setup = CrossSetup(genes: [BioGene("A")], parent1: [p1], parent2: [p2])
        let cross = setup.cross
        let r = cross.result()
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 10) {
                picker($p1, "Parent 1")
                Text("×").font(.system(size: 18, weight: .semibold)).foregroundStyle(.secondary)
                picker($p2, "Parent 2")
            }
            HStack(alignment: .top, spacing: 14) {
                PunnettGrid(square: CrossPunnett(cross), small: true)
                VStack(alignment: .leading, spacing: 8) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Genotypes").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        Text(r.genotypes.count == 1 ? "all \(r.genotypes[0].text)" : r.genotypicRatioText).font(.sCallout.weight(.semibold))
                    }
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Phenotypes").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        Text(r.phenotypes.count == 1 ? "all \(r.phenotypes[0].text)" : r.phenotypicRatioText).font(.sCallout.weight(.semibold))
                    }
                }
            }
            Button {
                openFull(["cross": setup.text])
            } label: {
                Text("Open in Genetics Crosses").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .help("This cross in the whole tool, with dihybrids, other patterns, probability and chi-square (Return)")
        }
    }

    private func picker(_ value: Binding<[Int]>, _ label: String) -> some View {
        Picker(label, selection: value) {
            ForEach(Self.options, id: \.self) { g in Text(BioGene("A").text(g)).tag(g) }
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .help("\(label)'s genotype")
        .accessibilityLabel(label)
    }
}
