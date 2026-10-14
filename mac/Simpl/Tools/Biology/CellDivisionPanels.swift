import SwiftUI

// Cell Division's right column (spec §4.6, §9 Cards): the counts panel — always there, updated each step, each value
// with an info button that says the rule (§5.2's definitions) and a mark where it changed — then the card for what is
// picked (a chromosome, or a labelled part), the step's card (what happens, what to notice), and at the end the cells
// it made, drawn small with their chromosomes. The Compare page's compact counts are here too.

/// The counts' rules, as §5.2 defines them (each value's info button).
enum DivCountRule: String, CaseIterable, Identifiable {
    case chromosomes, chromatids, ploidy, dna, homologs, sisters

    var id: String { rawValue }

    var label: String {
        switch self {
        case .chromosomes: return "Chromosomes"
        case .chromatids: return "DNA molecules (chromatids)"
        case .ploidy: return "Ploidy"
        case .dna: return "DNA amount"
        case .homologs: return "Homologous pairs present?"
        case .sisters: return "Sister chromatids attached?"
        }
    }

    var rule: String {
        switch self {
        case .chromosomes:
            return "One DNA molecule with its proteins or, after replication, two identical sister chromatids joined at a centromere. Either way it counts as one chromosome. Count chromosomes by counting centromeres. A replicated chromosome is still ONE chromosome."
        case .chromatids:
            return "Each chromatid is one DNA molecule. A chromosome is one before S phase and two (sister chromatids) after it, until they separate."
        case .ploidy:
            return "Ploidy = number of complete sets of chromosomes. Count sets, not chromatids. n = haploid (one set), 2n = diploid (two sets). A cell after meiosis I is haploid even though each chromosome still has two chromatids."
        case .dna:
            return "c: DNA amount relative to one haploid set of unreplicated chromosomes (1c). A diploid G1 cell is 2c, and after S it is 4c."
        case .homologs:
            return "Homologous chromosomes: one from each parent, same genes in the same order (alleles may differ). Present in every diploid cell, in mitosis too. They pair up only in meiosis I."
        case .sisters:
            return "Sister chromatids: the two identical copies made in S phase, joined until anaphase (mitosis) or anaphase II (meiosis)."
        }
    }

    func value(_ c: DivisionCounts) -> String {
        switch self {
        case .chromosomes: return c.chromosomes
        case .chromatids: return c.chromatids
        case .ploidy: return c.ploidy
        case .dna: return c.dna
        case .homologs: return c.homologs
        case .sisters: return c.sisters
        }
    }

    func human(_ c: DivisionCounts) -> String? {
        switch self {
        case .chromosomes: return c.chromosomesHuman
        case .chromatids: return c.chromatidsHuman
        default: return nil
        }
    }

    /// "8 (in two nuclei)" → "8" and "in two nuclei" (a single bracket at the end only).
    static func split(_ s: String) -> (main: String, note: String?) {
        guard s.hasSuffix(")"), s.filter({ $0 == "(" }).count == 1, let open = s.range(of: " (") else { return (s, nil) }
        return (String(s[..<open.lowerBound]), String(s[open.upperBound...].dropLast()))
    }
}

/// The right column of the Mitosis and Meiosis pages.
struct DivisionFactsColumn: View {
    let process: DivisionProcess
    @ObservedObject var steps: BioStepController
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        let seq = model.sequence(process)
        let i = min(steps.index, seq.counts.count - 1)
        VStack(alignment: .leading, spacing: 14) {
            DivisionCountsCard(counts: seq.counts[i], previous: i > 0 ? seq.counts[i - 1] : nil)
            if let p = model.picked, seq.frames[i].chromatids.indices.contains(p) {
                DivisionChromosomeCard(key: seq.frames[i].chromatids[p], frame: seq.frames[i]) {
                    withAnimation(Motion.snappy) { model.picked = nil }
                }
            } else if let part = model.part, let info = DivisionModel.parts[part] {
                DivisionPartCard(info: info) { withAnimation(Motion.snappy) { model.part = nil } }
            }
            DivisionStepCard(stage: seq.stages[i])
            if i == seq.stages.count - 1 { DivisionProductsCard(sequence: seq) }
        }
    }
}

/// The counts panel.
struct DivisionCountsCard: View {
    let counts: DivisionCounts
    let previous: DivisionCounts?
    @State private var info: DivCountRule?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline) {
                BioHeading("Counts")
                Spacer()
                Text("in the model cell · in a human cell").font(.sCaption2).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 14)
            .padding(.top, 12)
            .padding(.bottom, 6)
            if let l = counts.listing {
                Label("Cells differ — values are \(l)", systemImage: "exclamationmark.triangle")
                    .font(.sCaption)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 14)
                    .padding(.bottom, 6)
            }
            ForEach(DivCountRule.allCases) { r in
                row(r)
                if r != .sisters { Divider().padding(.leading, 14) }
            }
        }
        .card(radius: 14)
        .animation(reduceMotion ? nil : Motion.snappy, value: counts)
    }

    private func row(_ r: DivCountRule) -> some View {
        let value = r.value(counts)
        let parts = DivCountRule.split(value)
        let changed = previous.map { r.value($0) != value } ?? false
        return HStack(alignment: .top, spacing: 8) {
            VStack(alignment: .leading, spacing: 3) {
                Text(r.label).font(.sCaption).foregroundStyle(.secondary)
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(parts.main)
                        .font(.system(size: 17, weight: .semibold).monospacedDigit())
                        .contentTransition(.numericText())
                        .fixedSize(horizontal: false, vertical: true)
                    if changed {
                        Circle().fill(Theme.accent).frame(width: 6, height: 6)
                            .help("Changed at this step")
                            .accessibilityHidden(true)
                    }
                }
                if let note = parts.note {
                    Text(note).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                if let h = r.human(counts) {
                    Text("Human: \(h)").font(.sCaption.monospacedDigit()).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 4)
            Button { info = info == r ? nil : r } label: {
                Image(systemName: "info.circle").font(.system(size: 14)).foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("The rule for \(r.label.lowercased())")
            .accessibilityLabel("The rule for \(r.label)")
            .popover(isPresented: Binding(get: { info == r }, set: { if !$0, info == r { info = nil } }), arrowEdge: .leading) {
                VStack(alignment: .leading, spacing: 8) {
                    Text(r.label).font(.sHeadline)
                    Text(r.rule).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
                .padding(16)
                .frame(width: 300)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
        .accessibilityElement(children: .combine)
        .accessibilityValue(changed ? "changed at this step" : "")
    }
}

/// The step's card: what happens (the specification's bold key terms kept) and what to notice.
struct DivisionStepCard: View {
    let stage: DivisionStage

    var body: some View {
        BioCard(title: stage.title) {
            Text((try? AttributedString(markdown: stage.rich)) ?? AttributedString(stage.text))
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
                .textSelection(.enabled)
            if !stage.notice.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Text("What to notice").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                    ForEach(stage.notice, id: \.self) { n in
                        HStack(alignment: .firstTextBaseline, spacing: 6) {
                            Image(systemName: "eye").font(.system(size: 10)).foregroundStyle(Theme.accent)
                            Text(n).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }
            }
        }
        .id(stage.key)
    }
}

/// A labelled part's card.
struct DivisionPartCard: View {
    let info: DivPartInfo
    let close: () -> Void

    var body: some View {
        BioCard(title: info.title, swatch: Theme.accent) {
            BioFactRow(label: "What it is", text: info.what)
            if let m = info.mixUp { BioFactRow(label: "Common mix-up", text: m, tint: Color.orange) }
            closeButton(close)
        }
    }
}

/// A picked chromosome's card: which pair, which parent, what it is right now, its homolog and sister, and whether it
/// crossed over.
struct DivisionChromosomeCard: View {
    let key: DivChromatidKey
    let frame: DivisionFrame
    let close: () -> Void

    var body: some View {
        let id = key.id
        let pair = DivGeometry.pairs[id.pair]
        let parent = id.homolog == 0 ? "mother" : "father", other = id.homolog == 0 ? "father" : "mother"
        BioCard(title: "\(pair.name.capitalized) chromosome, from the \(parent)", swatch: (id.homolog == 0 ? BioPalette.maternal : BioPalette.paternal).color,
                subtitle: "\(Int(pair.units)) units long; centromere \(Int(pair.centromere * 100))% along") {
            BioFactRow(label: "Right now", text: now)
            BioFactRow(label: "Its homolog", text: "The \(other)'s \(pair.name) chromosome (\(id.homolog == 0 ? "blue" : "red")): the same genes in the same order, perhaps different alleles. It is not a copy of this one.")
            if key.recombined > 0.5 {
                BioFactRow(label: "Crossed over", text: "Beyond the chiasma this chromatid carries the \(other)'s DNA: a recombinant chromatid, from crossing over in prophase I.")
            }
            closeButton(close)
        }
    }

    private var now: String {
        let sister = frame.chromatids.first { $0.id == DivChromatidID(pair: key.id.pair, homolog: key.id.homolog, sister: 1 - key.id.sister) }
        if key.joined { return "Two sister chromatids joined at one centromere: 1 chromosome, 2 DNA molecules. Its sister is an identical copy made in S phase." }
        if sister.map({ !$0.exists }) ?? false { return "One chromosome, one DNA molecule: not copied yet." }
        return "A single chromatid with its own centromere, so it counts as a chromosome: 1 chromosome, 1 DNA molecule."
    }
}

private func closeButton(_ close: @escaping () -> Void) -> some View {
    Button("Close", action: close)
        .buttonStyle(.plain)
        .font(.sCallout)
        .foregroundStyle(.secondary)
        .help("Close this card (Escape)")
}

/// The cells a process ends with, each drawn small with its chromosomes (spec §4.6: gametes labelled n, n+1, n−1).
struct DivisionProductsCard: View {
    let sequence: DivisionSequence

    var body: some View {
        let products = sequence.products
        let meiosis = sequence.process == .meiosis
        BioCard(title: meiosis ? "The four cells (gametes)" : "The two daughter cells",
                subtitle: meiosis ? subtitle(products) : "Identical to each other and to the parent cell: 2n, 2c.") {
            HStack(spacing: 8) {
                ForEach(products, id: \.cell) { p in
                    VStack(spacing: 4) {
                        DivisionMiniCell(chromatids: p.chromatids, chiasma: sequence.options.chiasma)
                            .frame(height: 70)
                        Text(p.tally.ploidy)
                            .font(.sCallout.weight(.bold).monospacedDigit())
                            .foregroundStyle(p.tally.ploidy.count > 2 ? Color.orange : Theme.accent)
                        Text("\(p.tally.chromosomes) chr · \(DivisionModel.cText(p.tally.c))")
                            .font(.sCaption2.monospacedDigit())
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity)
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("A cell: \(p.tally.ploidy), \(p.tally.chromosomes) chromosomes")
                }
            }
            if meiosis, sequence.options.nondisjunction != .none, let info = DivisionModel.parts["nondisjunction"], let m = info.mixUp {
                BioFactRow(label: "Trisomy 21", text: m, tint: Color.orange)
            }
        }
    }

    private func subtitle(_ products: [DivProduct]) -> String {
        let o = sequence.options
        if o.nondisjunction != .none { return "Nondisjunction: some have one chromosome too many (n+1) or too few (n−1)." }
        let kinds = Set(products.map { p in p.chromatids.map { "\($0.id.pair)\($0.id.homolog)\($0.recombined > 0.5)" }.joined() }).count
        return "Haploid (n), 1c each; \(kinds) different kinds\(o.crossingOver ? " (crossing over and independent assortment)" : " (independent assortment only)")."
    }
}

/// A cell drawn small: each chromatid a bar as long as its chromosome, in its parent's colour, a recombinant part in
/// the other's, with its centromere.
struct DivisionMiniCell: View {
    let chromatids: [DivChromatidKey]
    let chiasma: Double
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        Canvas { gc, size in
            let dark = scheme == .dark
            let rect = CGRect(origin: .zero, size: size).insetBy(dx: 2, dy: 2)
            gc.fill(Path(roundedRect: rect, cornerRadius: 10), with: .color(DivColors.color(.cytoplasm, dark: dark, alpha: 1.6)))
            gc.stroke(Path(roundedRect: rect, cornerRadius: 10), with: .color(DivColors.color(.membrane, dark: dark)), lineWidth: 1)
            let n = max(chromatids.count, 1)
            let gap = rect.width / CGFloat(n + 1)
            let unit = (rect.height - 16) / 7
            for (j, c) in chromatids.enumerated() {
                let pair = DivGeometry.pairs[c.id.pair]
                let len = unit * CGFloat(pair.units)
                let x = rect.minX + gap * CGFloat(j + 1)
                let top = rect.midY - len / 2
                let cy = top + len * CGFloat(pair.centromere)
                let own = DivInk.parent(c.id.homolog), other = DivInk.parent(1 - c.id.homolog)
                let style = StrokeStyle(lineWidth: 5, lineCap: .round)
                var whole = Path()
                whole.move(to: CGPoint(x: x, y: top))
                whole.addLine(to: CGPoint(x: x, y: top + len))
                gc.stroke(whole, with: .color(DivColors.color(own, dark: dark)), style: style)
                if c.recombined > 0.5 {
                    // (the long arm, below the centromere: beyond the chiasma, the other parent's)
                    let from = cy + (top + len - cy) * CGFloat(chiasma)
                    var part = Path()
                    part.move(to: CGPoint(x: x, y: from))
                    part.addLine(to: CGPoint(x: x, y: top + len))
                    gc.stroke(part, with: .color(DivColors.color(other, dark: dark)), style: style)
                }
                gc.fill(Path(ellipseIn: CGRect(x: x - 2.5, y: cy - 2.5, width: 5, height: 5)), with: .color(DivColors.color(.centromere, dark: dark)))
            }
        }
    }
}

/// The Compare page's counts under each drawing: chromosomes, chromatids, ploidy, DNA.
struct DivisionCompactCounts: View {
    let counts: DivisionCounts

    var body: some View {
        let items: [(String, String, DivCountRule)] = [("Chromosomes", counts.chromosomes, .chromosomes), ("Chromatids", counts.chromatids, .chromatids),
                                                        ("Ploidy", counts.ploidy, .ploidy), ("DNA", counts.dna, .dna)]
        // (four in a row where their names fit; else two rows of two, each name beside its value — in a narrow window
        // "Chromosomes" and "Chromatids" were broken in the middle of the word)
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 8) {
                ForEach(items.indices, id: \.self) { i in tile(items[i].0, items[i].1, items[i].2, inline: false) }
            }
            Grid(horizontalSpacing: 8, verticalSpacing: 6) {
                GridRow {
                    tile(items[0].0, items[0].1, items[0].2, inline: true)
                    tile(items[1].0, items[1].1, items[1].2, inline: true)
                }
                GridRow {
                    tile(items[2].0, items[2].1, items[2].2, inline: true)
                    tile(items[3].0, items[3].1, items[3].2, inline: true)
                }
            }
        }
    }

    @ViewBuilder
    private func tile(_ label: String, _ value: String, _ rule: DivCountRule, inline: Bool) -> some View {
        let parts = DivCountRule.split(value)
        let name = Text(label).font(.sCaption2).foregroundStyle(.secondary).lineLimit(1).fixedSize()
        let number = Text(parts.main)
            .font(.system(size: inline ? 14 : 15, weight: .semibold).monospacedDigit())
            .lineLimit(inline ? 1 : 2)
            .minimumScaleFactor(0.8)
            .contentTransition(.numericText())
        Group {
            if inline {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    name
                    Spacer(minLength: 4)
                    number
                }
            } else {
                VStack(alignment: .leading, spacing: 2) {
                    name
                    number
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 10)
        .padding(.vertical, inline ? 5 : 7)
        .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .help("\(value)\n\(rule.rule)")
        .accessibilityElement(children: .combine)
    }
}
