import SwiftUI

// Genetics Crosses's Probability page (spec §4.5 Probability): "the probability that an offspring of AaBbCc × AaBbCc is
// aaB_Cc", asked gene by gene with chips and worked by the product rule — each factor from that gene's own small Punnett
// square — then the sum rule (outcomes that exclude each other add), and the chance over several children: at least one
// of n, and exactly k of n.

struct CrossProbabilityPage: View {
    @ObservedObject var model: CrossesState
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @FocusState private var typing: Bool

    var body: some View {
        PTWorkLayout {
            controls
        } centre: {
            centre
        } right: {
            side
        }
    }

    private func change(_ edit: @escaping () -> Void) {
        if reduceMotion { edit() } else { withAnimation(Motion.snappy) { edit() } }
    }

    private var cross: BioCross { model.probSetup.cross }

    private var product: (factors: [BioFrac], product: BioFrac) { CrossProbability.product(cross, model.chosenTargets) }

    // MARK: The cross and the chips

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Cross")
            TextField("AaBbCc × AaBbCc", text: $model.probTyped)
                .textFieldStyle(.roundedBorder)
                .font(.system(size: 15, weight: .medium))
                .focused($typing)
                .onSubmit { change { model.applyProbTyped() } }
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.red, lineWidth: 1.5).opacity(model.probError == nil ? 0 : 1))
                .help("Type a cross and press Return (⌘F)")
                .background {
                    Button("") { typing = true }
                        .keyboardShortcut("f", modifiers: .command)
                        .opacity(0)
                        .frame(width: 0, height: 0)
                        .accessibilityHidden(true)
                }
            if let e = model.probError {
                Label(e, systemImage: "exclamationmark.circle").font(.sCaption).foregroundStyle(.red).fixedSize(horizontal: false, vertical: true)
            }
            Button {
                change { model.probSetup = model.setup }
            } label: {
                Text("Use the Cross page's cross").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .disabled(model.setup == model.probSetup)
            .help("Ask about \(Allele.plain(model.setup.text))")
            BioHeading("Offspring asked for")
            ForEach(model.probSetup.genes.indices, id: \.self) { k in
                chips(k)
            }
            Text("Pick a genotype for each gene, a group (A_ is AA or Aa), or any.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    @ViewBuilder
    private func chips(_ k: Int) -> some View {
        if model.probSetup.genes.indices.contains(k) {
            let g = model.probSetup.genes[k]
            let chosen = k < model.probTargets.count ? model.probTargets[k] : "any"
            VStack(alignment: .leading, spacing: 6) {
                Text("Gene \(String(g.letter)) · \(g.pattern.label.lowercased())").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 76), spacing: 6)], alignment: .leading, spacing: 6) {
                    ForEach(CrossProbability.targets(g)) { t in
                        Button {
                            change { if k < model.probTargets.count { model.probTargets[k] = t.id } }
                        } label: {
                            Allele.text(t.label, size: 13, weight: .semibold).lineLimit(1).minimumScaleFactor(0.7).frame(maxWidth: .infinity)
                        }
                        .glassChip(ToolKind.cross.color, on: t.id == chosen)
                        .help(t.isAny ? "Any genotype for gene \(String(g.letter))" : "Gene \(String(g.letter)): \(Allele.plain(t.label))")
                        .accessibilityLabel("Gene \(String(g.letter)): \(Allele.spoken(t.label))")
                        .accessibilityAddTraits(t.id == chosen ? .isSelected : [])
                    }
                }
            }
        }
    }

    // MARK: The product rule

    private var centre: some View {
        let targets = model.chosenTargets
        let pr = product
        let wanted = CrossProbability.targetText(cross, targets)
        let lethal = cross.genes.contains { $0.pattern == .lethalDominantHomozygote || $0.pattern == .lethalRecessive }
        return ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                VStack(alignment: .leading, spacing: 6) {
                    Text("The question").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                    Allele.text("Probability that an offspring of \(model.probSetup.text) is \(wanted)", size: 19, weight: .semibold)
                        .fixedSize(horizontal: false, vertical: true)
                        .accessibilityLabel("Probability that an offspring of \(Allele.spoken(model.probSetup.text)) is \(Allele.spoken(wanted))")
                }
                VStack(alignment: .leading, spacing: 8) {
                    Text("The product rule").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                    Allele.text("P = " + targets.indices.map { "P(\(chip(targets[$0], gene: $0)))" }.joined(separator: " × "), size: 17, weight: .medium)
                    Text("= " + pr.factors.map(\.text).joined(separator: " × "))
                        .font(.system(size: 17, weight: .medium).monospacedDigit())
                    HStack(alignment: .firstTextBaseline, spacing: 10) {
                        Text("= \(pr.product.text)")
                            .font(.system(size: 30, weight: .bold).monospacedDigit())
                            .foregroundStyle(ToolKind.cross.color)
                            .contentTransition(.numericText())
                        Text("≈ \(CrossProbability.percent(pr.product.double))")
                            .font(.system(size: 20, weight: .semibold).monospacedDigit())
                            .foregroundStyle(.secondary)
                    }
                    .accessibilityElement(children: .combine)
                    Text("The genes are on different chromosomes and assort independently, so the chances for each gene multiply."
                         + (lethal ? " For a lethal gene, each chance is among the offspring that are born." : ""))
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Text("Each gene's own square").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 190), spacing: 14, alignment: .top)], alignment: .leading, spacing: 14) {
                    ForEach(cross.genes.indices, id: \.self) { k in
                        geneSquare(k, targets: targets, factors: pr.factors)
                    }
                }
            }
            .padding(20)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card(radius: 20)
    }

    @ViewBuilder
    private func geneSquare(_ k: Int, targets: [CrossTarget], factors: [BioFrac]) -> some View {
        if k < targets.count, k < factors.count, cross.genes.indices.contains(k) {
            let one = BioCross(genes: [cross.genes[k]], parent1: [cross.parent1[k]], parent2: [cross.parent2[k]])
            let t = targets[k]
            let lit: Set<[[Int]]>? = t.isAny ? nil : Set(t.genotypes.map { [$0] })
            VStack(alignment: .leading, spacing: 8) {
                Allele.text(one.genotypeText(one.parent1) + " × " + one.genotypeText(one.parent2), size: 14, weight: .semibold)
                PunnettGrid(square: CrossPunnett(one), highlight: lit, small: true)
                Allele.text("P(\(chip(t, gene: k))) = \(factors[k].text)", size: 14, weight: .bold)
                    .foregroundStyle(ToolKind.cross.color)
            }
            .padding(12)
            .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
    }

    /// A chip as the working writes it: its genotype, or "any B".
    private func chip(_ t: CrossTarget, gene k: Int) -> String {
        guard t.isAny else { return t.label }
        return cross.genes.indices.contains(k) ? "any \(String(cross.genes[k].letter))" : "any"
    }

    // MARK: The sum rule and several children

    private var side: some View {
        VStack(alignment: .leading, spacing: 14) {
            sumCard
            childrenCard
            Text("The product rule: independent events happen together with the product of their chances (and). The sum rule: outcomes that exclude each other happen with the sum of their chances (or).")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var sumCard: some View {
        let genes = model.probSetup.genes
        let k = min(model.sumGene, max(genes.count - 1, 0))
        let singles = genes.indices.contains(k) ? CrossProbability.targets(genes[k]).filter(\.single) : []
        let picked = singles.filter { model.sumTargets.contains($0.id) }
        let sum = CrossProbability.sum(cross, gene: k, picked)
        return BioCard(title: "Sum rule", subtitle: "Either one outcome or another") {
            if genes.count > 1 {
                Picker("", selection: Binding(get: { k }, set: { v in change { model.sumGene = v } })) {
                    ForEach(genes.indices, id: \.self) { i in Text("Gene \(String(genes[i].letter))").tag(i) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 72), spacing: 6)], alignment: .leading, spacing: 6) {
                ForEach(singles) { t in
                    let on = model.sumTargets.contains(t.id)
                    Button {
                        change { if on { model.sumTargets.remove(t.id) } else { model.sumTargets.insert(t.id) } }
                    } label: {
                        Allele.text(t.label, size: 13, weight: .semibold).lineLimit(1).minimumScaleFactor(0.7).frame(maxWidth: .infinity)
                    }
                    .glassChip(ToolKind.cross.color, on: on)
                    .accessibilityLabel(Allele.spoken(t.label))
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
            if picked.isEmpty {
                Text("Pick two or more genotypes.").font(.sCallout).foregroundStyle(.secondary)
            } else {
                VStack(alignment: .leading, spacing: 4) {
                    Allele.text("P(" + picked.map(\.label).joined(separator: " or ") + ")", size: 15, weight: .semibold)
                    Allele.text("= " + picked.map { "P(\($0.label))" }.joined(separator: " + "), size: 14, weight: .regular)
                    Text("= " + sum.terms.map(\.text).joined(separator: " + ")).font(.sCallout.monospacedDigit())
                    Text("= \(sum.total.text) (\(sum.total.percentText))").font(.system(size: 18, weight: .bold).monospacedDigit())
                }
                .accessibilityElement(children: .combine)
                Text("An offspring has only one genotype, so these outcomes exclude each other and their chances add.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var childrenCard: some View {
        let p = product.product
        let n = model.children
        let k = min(model.exactlyK, n)
        let atLeast = CrossProbability.atLeastOne(p, of: n)
        let exactly = CrossProbability.exactly(k, of: n, p)
        let q = BioFrac.one - p
        return BioCard(title: "Several children", subtitle: "Each child is a new, independent event") {
            Text("p = \(p.text) for one child (the question in the middle)").font(.sCallout).foregroundStyle(.secondary)
            Stepper(value: $model.children, in: 1...20) {
                Text("\(n) children").font(.sCallout.weight(.semibold))
            }
            .help("How many children")
            VStack(alignment: .leading, spacing: 4) {
                Text("At least one of \(n)").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Text("1 − (1 − p)\(HardyWeinberg.superscript(n)) = 1 − (\(q.text))\(HardyWeinberg.superscript(n))").font(.sCallout.monospacedDigit())
                Text("= \(atLeast.text)").font(.system(size: 17, weight: .bold).monospacedDigit())
            }
            .accessibilityElement(children: .combine)
            Divider()
            Stepper(value: Binding(get: { k }, set: { model.exactlyK = $0 }), in: 0...n) {
                Text("Exactly \(k) of \(n)").font(.sCallout.weight(.semibold))
            }
            .help("How many of the children")
            VStack(alignment: .leading, spacing: 4) {
                Text("C(\(n), \(k)) × p\(HardyWeinberg.superscript(k)) × (1 − p)\(HardyWeinberg.superscript(n - k))").font(.sCallout.monospacedDigit())
                Text("= \(CrossProbability.choose(n, k)) × (\(p.text))\(HardyWeinberg.superscript(k)) × (\(q.text))\(HardyWeinberg.superscript(n - k))").font(.sCallout.monospacedDigit())
                Text("= \(exactly.text)").font(.system(size: 17, weight: .bold).monospacedDigit())
            }
            .accessibilityElement(children: .combine)
            Text("C(n, k) counts the orders the k children could come in (the binomial).")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
