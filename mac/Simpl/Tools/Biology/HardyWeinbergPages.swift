import Charts
import SwiftUI

// Hardy–Weinberg's Test and Simulate pages (spec §4.7, §5.4.2–§5.4.3). Test: a sample's genotype counts against the
// proportions its own allele frequency predicts, χ² with df = 1 (editable, with the reason). Simulate: discrete
// generations of a diploid population — mutation, migration, mating with inbreeding, selection, then drift, in that
// order — p drawn for each replicate over the generations, the genotypes in bars, and the five conditions as chips that
// light up red when the settings break them.

// MARK: - Test

struct HWTestPage: View {
    @ObservedObject var model: HardyWeinbergState

    var body: some View {
        let r = model.test
        PTWorkLayout {
            controls(error: r.error)
        } centre: {
            centre(r.test)
        } right: {
            VStack(alignment: .leading, spacing: 14) {
                ChiCriticalTable(df: model.df, chi2: r.test?.result.chi2, tint: ToolKind.popgen.color)
                if let t = r.test { working(t) }
            }
        }
    }

    private func controls(error: String?) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Genotype counts")
            HStack(spacing: 8) {
                HWCountField(genotype: "AA", colour: HWColors.AA.color, text: $model.testAA)
                HWCountField(genotype: "Aa", colour: HWColors.Aa.color, text: $model.testAa)
                HWCountField(genotype: "aa", colour: HWColors.aa.color, text: $model.testaa)
            }
            if let error {
                Label(error, systemImage: "exclamationmark.circle").font(.sCaption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            }
            Button {
                (model.testAA, model.testAa, model.testaa) = ("50", "30", "20")
            } label: {
                Text("Example: 50 AA, 30 Aa, 20 aa").frame(maxWidth: .infinity)
            }
            .glassButton()
            BioHeading("Degrees of freedom")
            Picker("", selection: $model.df) {
                Text("df = 1").tag(1)
                Text("df = 2").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("1 for population genetics; some courses use 2")
            Text(HWTest.dfNote).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    private func centre(_ t: HWTest?) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                if let t {
                    BioFactRow(label: "Null hypothesis (H₀)", text: t.nullHypothesis, tint: ToolKind.popgen.color)
                    table(t)
                    chart(t)
                    VStack(spacing: 0) {
                        BioValueRow(label: "χ²", value: String(format: "%.4f", t.result.chi2))
                        BioValueRow(label: "Degrees of freedom", value: "\(t.result.df)")
                        BioValueRow(label: "Critical value (p = 0.05)", value: String(format: "%.2f", t.result.critical))
                        BioValueRow(label: "p-value", value: t.pText, last: true)
                    }
                    .card(radius: 14)
                    conclusion(t)
                } else {
                    Text("Enter the counts of AA, Aa and aa individuals in a sample.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
            .padding(20)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card(radius: 20)
    }

    private func head(_ s: String) -> some View { Text(s).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary) }
    private func cell(_ s: String, bold: Bool = false) -> some View { Text(s).font(.sCallout.weight(bold ? .bold : .regular).monospacedDigit()) }

    private func table(_ t: HWTest) -> some View {
        let names = ["AA", "Aa", "aa"], formulas = ["N·p²", "N·2pq", "N·q²"]
        let observed = [t.counts.AA, t.counts.Aa, t.counts.aa]
        return Grid(alignment: .trailing, horizontalSpacing: 14, verticalSpacing: 8) {
            GridRow {
                head("Genotype").gridColumnAlignment(.leading)
                head("Observed")
                head("Expected")
                head("(o − e)² ÷ e")
            }
            Divider().gridCellUnsizedAxes(.horizontal)
            ForEach(0..<3, id: \.self) { i in
                GridRow {
                    HStack(spacing: 6) {
                        Circle().fill(HWColors.all[i]).frame(width: 9, height: 9)
                        Text(names[i]).font(.sCallout.weight(.semibold))
                    }
                    cell("\(observed[i])")
                    VStack(alignment: .trailing, spacing: 0) {
                        cell(String(format: "%.2f", t.expected[i]))
                        Text(formulas[i]).font(.sCaption2).foregroundStyle(.tertiary)
                    }
                    cell(String(format: "%.4f", t.result.terms[i]))
                }
            }
            Divider().gridCellUnsizedAxes(.horizontal)
            GridRow {
                cell("N = \(t.counts.n)")
                cell("\(t.counts.n)")
                cell(String(format: "%.2f", t.expected.reduce(0, +)))
                cell("χ² = " + String(format: "%.4f", t.result.chi2), bold: true)
            }
        }
        .padding(14)
        .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }

    private func chart(_ t: HWTest) -> some View {
        let names = ["AA", "Aa", "aa"]
        let observed = [Double(t.counts.AA), Double(t.counts.Aa), Double(t.counts.aa)]
        return VStack(alignment: .leading, spacing: 6) {
            Chart {
                ForEach(0..<3, id: \.self) { i in
                    BarMark(x: .value("Genotype", names[i]), y: .value("Observed", observed[i]), width: .ratio(0.55))
                        .foregroundStyle(HWColors.all[i].opacity(0.85))
                        .cornerRadius(4)
                    RectangleMark(x: .value("Genotype", names[i]), y: .value("Expected", t.expected[i]), width: .ratio(0.75), height: .fixed(3))
                        .foregroundStyle(Color.primary.opacity(0.75))
                }
            }
            .chartLegend(.hidden)
            .frame(height: 150)
            .accessibilityLabel("Observed genotype counts as bars, expected under equilibrium as lines")
            HStack(spacing: 14) {
                Text("bars: observed").font(.sCaption)
                HStack(spacing: 5) {
                    Rectangle().fill(Color.primary.opacity(0.75)).frame(width: 14, height: 3)
                    Text("expected under equilibrium").font(.sCaption)
                }
            }
            .foregroundStyle(.secondary)
            .accessibilityHidden(true)
        }
    }

    private func conclusion(_ t: HWTest) -> some View {
        let reject = t.result.reject
        return VStack(alignment: .leading, spacing: 8) {
            Label {
                Text(t.result.conclusion).font(.sHeadline).fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: reject ? "xmark.octagon.fill" : "checkmark.circle.fill").foregroundStyle(reject ? Color.orange : Color.green)
            }
            Text(t.detail).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            if t.lowExpected {
                Label("An expected count is under 5, where chi-square is unreliable.", systemImage: "exclamationmark.triangle")
                    .font(.sCaption)
                    .foregroundStyle(.orange)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background((reject ? Color.orange : Color.green).opacity(0.08), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }

    private func working(_ t: HWTest) -> some View {
        BioCard(title: "From the counts") {
            BioFactRow(label: "Allele frequency", text: "p = (2 × \(t.counts.AA) + \(t.counts.Aa)) ÷ (2 × \(t.counts.n)) = \(HardyWeinberg.num(t.p)); q = 1 − p = \(HardyWeinberg.num(t.q))")
            BioFactRow(label: "Expected", text: "N·p², N·2pq and N·q² with this p — the proportions the population would have in equilibrium.")
            BioFactRow(label: "df = 1", text: "Three classes, less one, less one more for p, which was estimated from these same counts.")
        }
    }
}

// MARK: - Simulate

struct HWSimulatePage: View {
    @ObservedObject var model: HardyWeinbergState

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            centre
        } right: {
            side
        }
    }

    private var s: HWSimParams { model.sim }

    /// The generation shown in the bars and the readout: the one under the pointer, else the last.
    private var shown: Int {
        let last = model.populations.map { $0.generations.count - 1 }.max() ?? 0
        return min(model.hovered ?? last, max(last, 0))
    }

    // MARK: Settings

    private func slider(_ title: String, _ key: WritableKeyPath<HWSimParams, Double>, _ range: ClosedRange<Double>, step: Double, digits: Int = 2, help: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(title).font(.sCallout)
                Spacer()
                Text(String(format: "%.\(digits)f", model.sim[keyPath: key])).font(.sCallout.weight(.semibold).monospacedDigit())
            }
            Slider(value: Binding(get: { model.sim[keyPath: key] }, set: { model.sim[keyPath: key] = ($0 / step).rounded() * step }), in: range)
                .help(help)
                .accessibilityLabel(title)
                .accessibilityValue(String(format: "%.\(digits)f", model.sim[keyPath: key]))
        }
    }

    /// The size slider runs on log₁₀ N (10 to 10,000), rounded to 1, 10 or 100 as N grows.
    private var sizeExponent: Binding<Double> {
        Binding(get: { log10(Double(model.sim.size ?? 100)) },
                set: { e in
                    let raw = pow(10, e)
                    let step: Double = raw < 100 ? 1 : (raw < 1000 ? 10 : 100)
                    model.sim.size = min(max(Int((raw / step).rounded() * step), 10), 10_000)
                })
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 18) {
            populationSection
            selectionSection
            forcesSection
            runsSection
        }
    }

    private var populationSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            BioHeading("Population")
            Toggle("Infinite (no drift)", isOn: Binding(get: { model.sim.size == nil }, set: { model.sim.size = $0 ? nil : 100 }))
                .help("An infinite population follows the equations exactly; a finite one drifts by chance")
            if let n = s.size {
                VStack(alignment: .leading, spacing: 2) {
                    HStack {
                        Text("Size N").font(.sCallout)
                        Spacer()
                        Text("\(n)").font(.sCallout.weight(.semibold).monospacedDigit())
                    }
                    Slider(value: sizeExponent, in: 1...4)
                        .help("10 to 10,000 individuals (2N alleles drawn each generation)")
                        .accessibilityLabel("Population size")
                        .accessibilityValue("\(n)")
                }
            }
            slider("Starting p", \.p0, 0...1, step: 0.01, help: "The A allele's frequency in generation 0")
        }
    }

    private var selectionSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            BioHeading("Selection")
            CrossMenu(help: "Fitnesses of AA, Aa and aa") {
                ForEach(HWSelection.allCases) { p in
                    Button {
                        (model.sim.wAA, model.sim.wAa, model.sim.waa) = (p.fitness.AA, p.fitness.Aa, p.fitness.aa)
                    } label: {
                        CrossMenuItem(title: p.title, on: HWSelection.matching(s) == p)
                    }
                }
            } label: {
                Text(HWSelection.matching(s)?.title ?? "Your own").font(.sCallout.weight(.semibold))
            }
            slider("w AA", \.wAA, 0...1, step: 0.05, help: "Fitness of AA: its share of offspring")
            slider("w Aa", \.wAa, 0...1, step: 0.05, help: "Fitness of Aa")
            slider("w aa", \.waa, 0...1, step: 0.05, help: "Fitness of aa")
        }
    }

    private var forcesSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            BioHeading("Mutation, migration, mating")
            slider("u (A → a)", \.u, 0...0.01, step: 0.0005, digits: 4, help: "Mutation from A to a, per generation")
            slider("v (a → A)", \.v, 0...0.01, step: 0.0005, digits: 4, help: "Mutation from a to A, per generation")
            slider("Migration m", \.m, 0...0.5, step: 0.01, help: "The share of each generation's alleles from migrants")
            slider("Migrants' p", \.pm, 0...1, step: 0.01, help: "The A allele's frequency among the migrants")
            slider("Inbreeding F", \.F, 0...1, step: 0.05, help: "0 is random mating; 1 is complete inbreeding")
        }
    }

    private var runsSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            BioHeading("Runs")
            Stepper(value: Binding(get: { model.sim.replicates }, set: { model.sim.replicates = $0 }), in: 1...10) {
                Text("\(s.replicates) population\(s.replicates == 1 ? "" : "s")").font(.sCallout)
            }
            .disabled(s.size == nil)
            .help("Replicate populations: drift differs in each")
            Picker("", selection: Binding(get: { model.sim.generations }, set: { model.sim.generations = $0 })) {
                ForEach(HWSimParams.generationChoices, id: \.self) { g in Text("\(g)").tag(g) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("Generations to run")
            HStack(spacing: 8) {
                Text("Seed").font(.sCallout)
                TextField("1", text: Binding(get: { String(model.sim.seed) }, set: { if let v = UInt64($0) { model.sim.seed = v } }))
                    .textFieldStyle(.roundedBorder)
                    .font(.sCallout.monospacedDigit())
                    .accessibilityLabel("Seed")
                Button {
                    model.sim.seed &+= 1
                } label: {
                    Image(systemName: "dice")
                }
                .glassButton()
                .keyboardShortcut("r", modifiers: [.command, .shift])
                .help("Run again with the next seed (⇧⌘R)")
                .accessibilityLabel("Next seed")
            }
            Text("Population r (from 0) draws from seed + r: a seed gives the same populations every time, here and in Simpl for Windows.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Button("Back to no forces") {
                var x = HWSimParams()
                x.size = model.sim.size
                x.seed = model.sim.seed
                x.replicates = model.sim.replicates
                x.generations = model.sim.generations
                x.p0 = model.sim.p0
                model.sim = x
            }
            .buttonStyle(.link)
            .help("Selection, mutation, migration and inbreeding off")
        }
    }

    // MARK: The chart

    private var centre: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                chips
                chart
                bars
            }
            .padding(18)
            .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .card(radius: 20)
    }

    private var chips: some View {
        let conditions = HWSimulation.assumptions(s)
        let broken = conditions.contains { !$0.holds }
        return VStack(alignment: .leading, spacing: 8) {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 6)], alignment: .leading, spacing: 6) {
                ForEach(conditions) { a in
                    HStack(spacing: 4) {
                        Image(systemName: a.holds ? "checkmark" : "xmark").font(.system(size: 10, weight: .bold))
                        Text(a.title).font(.sCaption.weight(.semibold)).lineLimit(1)
                    }
                    .padding(.horizontal, 9)
                    .padding(.vertical, 5)
                    .foregroundStyle(a.holds ? Color.secondary : Color.white)
                    .background(Capsule().fill(a.holds ? Color.primary.opacity(0.07) : Color.red.opacity(0.85)))
                    .help(a.holds ? "\(a.title): holds" : a.detail)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("\(a.title): \(a.holds ? "holds" : "broken. \(a.detail)")")
                }
            }
            Text(HWSimulation.verdict(s))
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(broken ? Color.red : Color.secondary)
        }
    }

    private var chart: some View {
        let pops = model.populations
        let line = HWColors.line.color
        return VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text("p, the A allele's frequency").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Spacer()
                if model.running { ProgressView().controlSize(.small) }
                Text(readout(pops)).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
            }
            Chart {
                ForEach(pops.indices, id: \.self) { r in
                    ForEach(pops[r].generations.indices, id: \.self) { t in
                        LineMark(x: .value("Generation", Double(t)), y: .value("p", pops[r].generations[t].p), series: .value("Population", "\(r + 1)"))
                            .foregroundStyle(line.opacity(pops.count > 1 ? 0.8 : 1))
                            .lineStyle(StrokeStyle(lineWidth: 2, lineCap: .round, lineJoin: .round))
                    }
                }
                if let h = model.hovered {
                    RuleMark(x: .value("Generation", Double(h)))
                        .foregroundStyle(Color.secondary.opacity(0.5))
                        .lineStyle(StrokeStyle(lineWidth: 1))
                }
            }
            .chartXScale(domain: 0...Double(max(s.generations, 1)))
            .chartYScale(domain: 0.0...1.0)
            .chartXAxisLabel("Generation")
            .chartLegend(.hidden)
            .chartOverlay { proxy in
                GeometryReader { geo in
                    Rectangle()
                        .fill(Color.clear)
                        .contentShape(Rectangle())
                        .onContinuousHover { phase in
                            switch phase {
                            case .active(let location):
                                guard let plot = proxy.plotFrame else { return }
                                let x = location.x - geo[plot].origin.x
                                if let g = proxy.value(atX: x, as: Double.self) {
                                    model.hovered = min(max(Int(g.rounded()), 0), s.generations)
                                }
                            case .ended:
                                model.hovered = nil
                            }
                        }
                }
            }
            .frame(height: 260)
            .accessibilityLabel("p over \(s.generations) generations for \(pops.count) population\(pops.count == 1 ? "" : "s"). \(readout(pops))")
        }
    }

    private func readout(_ pops: [HWPopulation]) -> String {
        let g = shown
        let ps = pops.compactMap { g < $0.generations.count ? $0.generations[g].p : nil }
        guard !ps.isEmpty else { return "" }
        if ps.count == 1 { return "generation \(g): p = \(HardyWeinberg.num(ps[0]))" }
        return "generation \(g): mean p = \(HardyWeinberg.num(ps.reduce(0, +) / Double(ps.count)))"
    }

    private var bars: some View {
        let pops = model.populations
        let g = shown
        return VStack(alignment: .leading, spacing: 6) {
            Text("Genotype frequencies in generation \(g)\(g == 0 ? "" : ", after selection")").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
            ForEach(pops.indices, id: \.self) { r in
                if g < pops[r].generations.count {
                    let x = pops[r].generations[g]
                    HStack(spacing: 8) {
                        Text(pops.count > 1 ? "\(r + 1)" : "").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary).frame(width: 18, alignment: .trailing)
                        GenotypeBar(AA: x.AA, Aa: x.Aa, aa: x.aa, height: pops.count > 5 ? 10 : 14, labelled: r == pops.count - 1)
                    }
                } else {
                    Text("Population \(r + 1) died out in generation \(pops[r].extinctAt ?? 0): mean fitness reached 0.")
                        .font(.sCaption)
                        .foregroundStyle(.red)
                }
            }
        }
    }

    // MARK: The side

    private var side: some View {
        let pops = model.populations
        let g = shown
        let fixed = pops.filter { ($0.generations.last?.p ?? 0) >= 1 }.count
        let lost = pops.filter { ($0.generations.last?.p ?? 1) <= 0 }.count
        let dead = pops.filter { $0.extinctAt != nil }.count
        return VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "Generation \(g)", subtitle: model.hovered == nil ? "The last; point at the chart for another" : "Under the pointer") {
                VStack(spacing: 0) {
                    ForEach(pops.indices, id: \.self) { r in
                        let gens = pops[r].generations
                        BioValueRow(label: pops.count > 1 ? "Population \(r + 1)" : "p",
                                    value: g < gens.count ? HardyWeinberg.num(gens[g].p) + (gens[g].k.map { "  (\($0) of \(2 * (s.size ?? 0)))" } ?? "") : "died out",
                                    last: r == pops.count - 1)
                    }
                }
                if pops.count > 1 || s.size != nil {
                    Text("At the end: A fixed in \(fixed), lost in \(lost)\(dead > 0 ? ", \(dead) died out" : "") of \(pops.count).")
                        .font(.sCallout)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            BioCard(title: "One generation, in order") {
                BioFactRow(label: "1. Mutation", text: "p₁ = p(1 − u) + (1 − p)v")
                BioFactRow(label: "2. Migration", text: "p₂ = (1 − m)p₁ + m·p_m")
                BioFactRow(label: "3. Mating", text: "AA = p₂² + Fp₂q₂ · Aa = 2p₂q₂(1 − F) · aa = q₂² + Fp₂q₂")
                BioFactRow(label: "4. Selection", text: "w̄ = Σ genotype × w; each genotype × w ÷ w̄; p₃ = AA + Aa ÷ 2")
                BioFactRow(label: "5. Drift", text: s.size == nil ? "None: an infinite population keeps p₃." : "2N alleles drawn, each A with chance p₃: p = k ÷ 2N.")
            }
            Text("Drift is strongest in small populations: try N = 10 against N = 1,000 with several populations. Selection against a recessive slows as it gets rare, because most of its alleles hide in carriers.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
