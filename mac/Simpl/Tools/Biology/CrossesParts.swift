import SwiftUI

// The Genetics Crosses tool's pieces (spec §4.5, §9): alleles written with their superscripts, the phenotypes' tints,
// the Punnett square, the forked-line diagram, a parent's gametes, a menu in the tool's style, and the picture behind
// "Why?" — two chromosome pairs lining up either way at metaphase I (independent assortment).

/// Alleles as genetics writes them: "C^R" with its R raised; "X^a Y". The model keeps the caret; these draw it.
enum Allele {
    /// The text with each "^x" raised and smaller.
    static func text(_ s: String, size: CGFloat = 15, weight: Font.Weight = .semibold) -> Text {
        var out = Text(verbatim: "")
        var run = ""
        let chars = Array(s)
        var i = 0
        func flush() {
            guard !run.isEmpty else { return }
            out = Text("\(out)\(Text(verbatim: run).font(.system(size: size, weight: weight)))")
            run = ""
        }
        while i < chars.count {
            if chars[i] == "^", i + 1 < chars.count {
                flush()
                let sup = Text(verbatim: String(chars[i + 1])).font(.system(size: size * 0.66, weight: weight)).baselineOffset(size * 0.42)
                out = Text("\(out)\(sup)")
                i += 2
            } else {
                run.append(chars[i])
                i += 1
            }
        }
        flush()
        return out
    }

    /// For a menu, which draws plain text: each superscript as a modifier letter where Unicode has one.
    static func plain(_ s: String) -> String {
        let sup: [Character: String] = [
            "A": "ᴬ", "B": "ᴮ", "D": "ᴰ", "E": "ᴱ", "G": "ᴳ", "H": "ᴴ", "I": "ᴵ", "J": "ᴶ", "K": "ᴷ", "L": "ᴸ", "M": "ᴹ", "N": "ᴺ",
            "O": "ᴼ", "P": "ᴾ", "R": "ᴿ", "T": "ᵀ", "U": "ᵁ", "V": "ⱽ", "W": "ᵂ", "C": "ᶜ", "F": "ᶠ", "S": "ˢ", "X": "ˣ", "Y": "ʸ", "Z": "ᶻ",
            "a": "ᵃ", "b": "ᵇ", "c": "ᶜ", "d": "ᵈ", "e": "ᵉ", "f": "ᶠ", "g": "ᵍ", "h": "ʰ", "i": "ⁱ", "j": "ʲ", "k": "ᵏ", "l": "ˡ", "m": "ᵐ",
            "n": "ⁿ", "o": "ᵒ", "p": "ᵖ", "r": "ʳ", "s": "ˢ", "t": "ᵗ", "u": "ᵘ", "v": "ᵛ", "w": "ʷ", "x": "ˣ", "y": "ʸ", "z": "ᶻ",
        ]
        var out = ""
        let chars = Array(s)
        var i = 0
        while i < chars.count {
            if chars[i] == "^", i + 1 < chars.count, let m = sup[chars[i + 1]] {
                out += m
                i += 2
            } else {
                out.append(chars[i])
                i += 1
            }
        }
        return out
    }

    /// For VoiceOver: the letters read out ("C R, C W").
    static func spoken(_ s: String) -> String {
        s.replacingOccurrences(of: "^", with: " ").replacingOccurrences(of: "_", with: " blank ")
    }
}

/// The phenotypes' tints (never colour alone: every cell and row also says its phenotype). Eight categorical hues in a
/// fixed order (the order of the phenotypic ratio), validated for colour-vision deficiency on light and dark; flower and
/// coat colours wear their own.
enum CrossColors {
    static let phenotypes: [BioHexPair] = [
        BioHexPair("#2a78d6", "#3987e5"), BioHexPair("#eb6834", "#d95926"), BioHexPair("#1baf7a", "#199e70"), BioHexPair("#eda100", "#c98500"),
        BioHexPair("#e87ba4", "#d55181"), BioHexPair("#008300", "#008300"), BioHexPair("#6250d6", "#9085e9"), BioHexPair("#e34948", "#e66767"),
    ]

    private static let named: [String: BioHexPair] = [
        "red": BioHexPair("#d63a34", "#ff6961"), "pink": BioHexPair("#e87ba4", "#ff9fc4"), "white": BioHexPair("#b8b8c0", "#d8d8de"),
        "roan": BioHexPair("#a0603a", "#d0906a"),
    ]

    /// A phenotype's tint by its place in the phenotypic ratio (and its name, for red, pink, white and roan).
    static func phenotype(_ index: Int?, label: String? = nil) -> Color {
        if let label, let c = named[label] { return c.color }
        guard let index else { return Color.secondary }
        return phenotypes[index % phenotypes.count].color
    }

    /// The tint of each phenotype of a result, by name.
    static func tints(_ r: BioCrossResult) -> [String: Color] {
        var out: [String: Color] = [:]
        for (i, p) in r.phenotypes.enumerated() { out[p.text] = phenotype(i, label: p.text) }
        return out
    }
}

/// A menu in the tool's style (the Lewis maker's preset menu): a filled well with its value and the up-down chevrons.
struct CrossMenu<Items: View, Face: View>: View {
    var help: String
    @ViewBuilder var items: Items
    @ViewBuilder var label: Face

    var body: some View {
        Menu {
            items
        } label: {
            HStack(spacing: 6) {
                label
                Spacer(minLength: 2)
                Image(systemName: "chevron.up.chevron.down").font(.system(size: 10, weight: .semibold)).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 10)
            .frame(maxWidth: .infinity, minHeight: 32)
            .contentShape(Rectangle())
        }
        .menuStyle(.button)
        .buttonStyle(.plain)
        .menuIndicator(.hidden)
        .background(Color.primary.opacity(0.07), in: RoundedRectangle(cornerRadius: 9, style: .continuous))
        .help(help)
    }
}

/// A menu item, ticked when it is the one chosen.
struct CrossMenuItem: View {
    let title: String
    let on: Bool

    var body: some View {
        if on { Label(title, systemImage: "checkmark") } else { Text(title) }
    }
}

// MARK: - Gametes

/// A gamete drawn as a cell: its alleles in a circle, its chance under it.
struct GameteBubble: View {
    let gamete: CrossGamete
    let tint: Color
    var size: CGFloat = 46

    var body: some View {
        VStack(spacing: 4) {
            Allele.text(gamete.text, size: gamete.text.count > 4 ? 12 : 14.5, weight: .bold)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
                .padding(4)
                .frame(width: size, height: size)
                .background(Circle().fill(tint.opacity(0.14)))
                .overlay(Circle().strokeBorder(tint.opacity(0.55), lineWidth: 1.2))
            Text(gamete.p.text)
                .font(.sCaption.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("Gamete \(Allele.spoken(gamete.text)), chance \(gamete.p.text)")
    }
}

/// A parent's gametes: its genotype, then each gamete with its chance.
struct GameteRow: View {
    let title: String
    let genotype: String
    let gametes: [CrossGamete]
    let tint: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(title).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                Allele.text(genotype, size: 13.5, weight: .semibold)
                Text("· \(gametes.count) kind\(gametes.count == 1 ? "" : "s") of gamete").font(.sCaption).foregroundStyle(.secondary)
            }
            HStack(alignment: .top, spacing: 8) {
                ForEach(gametes.indices, id: \.self) { i in GameteBubble(gamete: gametes[i], tint: tint) }
            }
        }
    }
}

// MARK: - The Punnett square

/// A Punnett square: parent 1's gametes down the left, parent 2's across the top, each cell its genotype, tinted by its
/// phenotype (and named under it while there is room); a cell that dies before birth is hatched grey. Hovering a cell
/// lights every cell with the same genotype. `highlight`, if given, lights the cells that meet a target and dims the rest
/// (the Probability page's small squares).
struct PunnettGrid: View {
    let square: CrossPunnett
    var highlight: Set<[[Int]]>? = nil
    var small = false
    @State private var hovered: [[Int]]?

    private var n: Int { max(square.rows.count, square.columns.count) }
    private var cellWidth: CGFloat { small ? 52 : (n <= 2 ? 96 : (n <= 4 ? 84 : 70)) }
    private var cellHeight: CGFloat { small ? 40 : (n <= 2 ? 72 : (n <= 4 ? 58 : 44)) }
    private var fontSize: CGFloat { small ? 13 : (n <= 2 ? 18 : (n <= 4 ? 15 : 12.5)) }
    private var named: Bool { !small && n <= 4 }

    var body: some View {
        Grid(horizontalSpacing: 4, verticalSpacing: 4) {
            GridRow {
                corner
                ForEach(square.columns.indices, id: \.self) { j in header(square.columns[j]) }
            }
            ForEach(square.rows.indices, id: \.self) { i in
                GridRow {
                    header(square.rows[i])
                    ForEach(square.columns.indices, id: \.self) { j in cell(i, j) }
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Punnett square, \(square.rows.count) by \(square.columns.count)")
    }

    private var corner: some View {
        VStack(spacing: 0) {
            Text("♂ →").font(.sCaption2.weight(.semibold))
            Text("♀ ↓").font(.sCaption2.weight(.semibold))
        }
        .foregroundStyle(.tertiary)
        .frame(width: small ? 30 : 44, height: small ? 30 : 40)
        .accessibilityHidden(true)
    }

    private func header(_ g: CrossGamete) -> some View {
        Allele.text(g.text, size: small ? 12 : 14, weight: .bold)
            .lineLimit(1)
            .minimumScaleFactor(0.6)
            .frame(minWidth: small ? 30 : 44, minHeight: small ? 30 : 40)
            .padding(.horizontal, 2)
            .background(Circle().fill(Color.primary.opacity(0.06)).frame(width: small ? 30 : 40, height: small ? 30 : 40))
            .accessibilityLabel("Gamete \(Allele.spoken(g.text))")
    }

    @ViewBuilder
    private func cell(_ i: Int, _ j: Int) -> some View {
        let c = square.cells[i][j]
        let tint = c.dies ? Color.secondary : CrossColors.phenotype(c.tint, label: c.phenotype)
        let lit = highlight.map { $0.contains(c.genotype) } ?? true
        let hover = hovered == c.genotype
        VStack(spacing: 2) {
            HStack(spacing: 2) {
                if let s = c.sex { Text(s == .male ? "♂" : "♀").font(.system(size: fontSize * 0.62, weight: .semibold)).foregroundStyle(.secondary) }
                Allele.text(c.text, size: fontSize, weight: .semibold)
                    .lineLimit(1)
                    .minimumScaleFactor(0.55)
            }
            if named {
                Text(c.dies ? "dies" : (c.carrier ? "\(c.phenotype) · carrier" : c.phenotype))
                    .font(.system(size: 10.5, weight: .medium))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
        }
        .padding(.horizontal, 3)
        .frame(width: cellWidth, height: cellHeight)
        .background {
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .fill(tint.opacity(c.dies ? 0.08 : (lit ? 0.24 : 0.06)))
        }
        .overlay {
            if c.dies { Hatching().clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous)).opacity(0.35) }
        }
        .overlay {
            RoundedRectangle(cornerRadius: 8, style: .continuous)
                .strokeBorder(stroke(tint: tint, lit: lit, hover: hover), lineWidth: hover || (highlight != nil && lit) ? 2 : 1)
        }
        .opacity(lit ? 1 : 0.45)
        .onHover { inside in hovered = inside ? c.genotype : (hovered == c.genotype ? nil : hovered) }
        .help(c.dies ? "\(Allele.plain(c.text)) dies before birth" : "\(Allele.plain(c.text)): \(c.phenotype)\(c.carrier ? ", a carrier" : "")")
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(Allele.spoken(square.rows[i].text)) with \(Allele.spoken(square.columns[j].text)): \(Allele.spoken(c.text)), \(c.dies ? "dies before birth" : c.phenotype)\(c.carrier ? ", carrier" : "")")
    }

    /// A cell's edge: dark under the pointer, the accent when it meets the target, else its tint.
    private func stroke(tint: Color, lit: Bool, hover: Bool) -> Color {
        if hover { return Color.primary.opacity(0.7) }
        if highlight != nil && lit { return Theme.accent }
        return tint.opacity(0.45)
    }
}

/// Diagonal hatching for a cell that dies before birth.
struct Hatching: View {
    var body: some View {
        Canvas { ctx, size in
            var p = Path()
            var x: CGFloat = -size.height
            while x < size.width {
                p.move(to: CGPoint(x: x, y: size.height))
                p.addLine(to: CGPoint(x: x + size.height, y: 0))
                x += 7
            }
            ctx.stroke(p, with: .color(.secondary), lineWidth: 1)
        }
        .accessibilityHidden(true)
    }
}

// MARK: - The forked line

/// The forked-line (branch) diagram: each gene's outcomes in a column, every path through them a row, its chance the
/// product along the path — the product rule drawn out.
struct ForkedLineView: View {
    let line: CrossForkedLine
    let genes: [BioGene]
    /// Each leaf's tint, by its label (by phenotype).
    var tints: [String: Color] = [:]

    private var rowHeight: CGFloat { 30 }
    private var column: CGFloat { 136 }
    private var leafWidth: CGFloat { 250 }

    var body: some View {
        let rows = max(line.leaves.count, 1)
        let height = CGFloat(rows) * rowHeight + 30
        let width = CGFloat(line.levels.count) * column + leafWidth
        ZStack(alignment: .topLeading) {
            Canvas { ctx, _ in
                var p = Path()
                for k in line.levels.indices {
                    let span = spanBelow(k)
                    let nodes = nodeCount(k)
                    for node in 0..<nodes {
                        let y = centreY(node, span)
                        let xEnd = CGFloat(k) * column + column - 14
                        // (to each child: across, down, across)
                        if k + 1 < line.levels.count {
                            let childSpan = spanBelow(k + 1)
                            for c in 0..<line.levels[k + 1].count {
                                let childNode = node * line.levels[k + 1].count + c
                                let cy = centreY(childNode, childSpan)
                                let x0 = CGFloat(k + 1) * column
                                p.move(to: CGPoint(x: xEnd, y: y))
                                p.addLine(to: CGPoint(x: x0 - 8, y: y))
                                p.addLine(to: CGPoint(x: x0 - 8, y: cy))
                                p.addLine(to: CGPoint(x: x0 + 2, y: cy))
                            }
                        } else {
                            let x0 = CGFloat(line.levels.count) * column
                            p.move(to: CGPoint(x: xEnd, y: y))
                            p.addLine(to: CGPoint(x: x0 + 2, y: y))
                        }
                    }
                }
                ctx.stroke(p, with: .color(.secondary.opacity(0.55)), lineWidth: 1)
            }
            .frame(width: width, height: height)
            .accessibilityHidden(true)
            // (each gene's heading over its column)
            ForEach(genes.indices, id: \.self) { k in
                Text("Gene \(String(genes[k].letter))")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: column - 16, alignment: .leading)
                    .position(x: CGFloat(k) * column + (column - 16) / 2 + 2, y: 9)
                    .accessibilityHidden(true)
            }
            Text("Offspring")
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
                .frame(width: leafWidth - 12, alignment: .leading)
                .position(x: CGFloat(line.levels.count) * column + (leafWidth - 12) / 2 + 8, y: 9)
                .accessibilityHidden(true)
            ForEach(nodes(), id: \.id) { node in
                HStack(spacing: 5) {
                    Text(node.p).font(.sCaption.monospacedDigit()).foregroundStyle(.secondary)
                    Allele.text(node.label, size: 13, weight: .semibold).lineLimit(1).minimumScaleFactor(0.7)
                }
                .frame(width: column - 22, alignment: .leading)
                .position(x: CGFloat(node.level) * column + (column - 22) / 2 + 2, y: node.y)
                .accessibilityHidden(true)
            }
            ForEach(line.leaves.indices, id: \.self) { i in leaf(i) }
        }
        .frame(width: width, height: height, alignment: .topLeading)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Forked-line diagram, \(line.leaves.count) outcomes")
    }

    private func leaf(_ i: Int) -> some View {
        let l = line.leaves[i]
        let tint = tints[l.label] ?? Color.secondary
        return HStack(spacing: 7) {
            Circle().fill(l.dies ? Color.secondary.opacity(0.4) : tint).frame(width: 8, height: 8)
            Text(l.p.text).font(.sCallout.weight(.semibold).monospacedDigit())
            Allele.text(l.label, size: 13.5, weight: .medium).lineLimit(1).minimumScaleFactor(0.7)
            if l.dies { Text("dies").font(.sCaption).foregroundStyle(.secondary) }
        }
        .frame(width: leafWidth - 12, alignment: .leading)
        .position(x: CGFloat(line.levels.count) * column + (leafWidth - 12) / 2 + 8, y: 30 + (CGFloat(i) + 0.5) * rowHeight)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(Allele.spoken(l.label)): \(l.p.text)\(l.dies ? ", dies" : "")")
    }

    /// How many leaves sit under one node of level k.
    private func spanBelow(_ k: Int) -> Int {
        line.levels.dropFirst(k + 1).reduce(1) { $0 * max($1.count, 1) }
    }

    private func nodeCount(_ k: Int) -> Int {
        line.levels.prefix(k + 1).reduce(1) { $0 * max($1.count, 1) }
    }

    private func centreY(_ node: Int, _ span: Int) -> CGFloat {
        30 + (CGFloat(node * span) + CGFloat(span) / 2) * rowHeight
    }

    private struct Node {
        let id: String
        let level: Int
        let y: CGFloat
        let label: String
        let p: String
    }

    private func nodes() -> [Node] {
        var out: [Node] = []
        for (k, level) in line.levels.enumerated() where !level.isEmpty {
            let span = spanBelow(k)
            for node in 0..<nodeCount(k) {
                let b = level[node % level.count]
                out.append(Node(id: "\(k).\(node)", level: k, y: centreY(node, span), label: b.label, p: b.p.text))
            }
        }
        return out
    }
}


// MARK: - Why? Independent assortment

/// "Why?": the parent's genes on different chromosome pairs, lining up at metaphase I one way or the other — each way
/// as likely — so every combination of alleles reaches a gamete equally often (independent assortment).
struct AssortmentSheet: View {
    let setup: CrossSetup
    let close: () -> Void
    @State private var parent = 1
    @State private var flips: [Bool] = [false, false, false]
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// A gene's pair of homologs: the allele on the maternal one (a) and on the paternal one (b).
    private struct Pair {
        let gene: BioGene
        let a: String
        let b: String
        let flipped: Bool
        var het: Bool { a != b }
        /// The allele facing the left pole, and the right.
        var left: String { flipped ? b : a }
        var right: String { flipped ? a : b }
    }

    private var pairs: [Pair] {
        let genotype = parent == 1 ? setup.parent1 : setup.parent2
        return setup.genes.indices.map { k in
            let g = genotype[k]
            let gene = setup.genes[k]
            return Pair(gene: gene, a: gene.alleles[g[0]], b: gene.alleles[g.count > 1 ? g[1] : g[0]], flipped: k < flips.count && flips[k])
        }
    }

    var body: some View {
        let ps = pairs
        let hets = ps.filter { $0.het }.count
        let made = gametesNow(ps)
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text("Why these gametes?").font(.sTitle3)
                Spacer()
                Button("Done", action: close).glassButton().keyboardShortcut(.cancelAction)
            }
            Picker("", selection: $parent) {
                Text(setup.xIndex != nil ? "Mother" : "Parent 1").tag(1)
                Text(setup.xIndex != nil ? "Father" : "Parent 2").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(maxWidth: 260)
            Text(explanation(hets))
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
            metaphase(ps)
            HStack(spacing: 10) {
                Button {
                    if reduceMotion { flipOne(ps) } else { withAnimation(.spring(response: 0.5, dampingFraction: 1)) { flipOne(ps) } }
                } label: {
                    Label("Line up the other way", systemImage: "arrow.left.arrow.right")
                }
                .glassButton(prominent: true)
                .disabled(hets == 0)
                .help("Turn a pair round at the plate: the other arrangement, just as likely")
                Button {
                    BioNav.shared.set(.divide, "meiosis")
                    close()
                    BioTools.open(.divide, info: ["focus": "assortment"])
                } label: {
                    Label("See it in Cell Division", systemImage: "arrow.triangle.branch")
                }
                .glassButton()
                .help("Open meiosis in Cell Division")
            }
            HStack(spacing: 6) {
                Text("This arrangement gives").font(.sCallout).foregroundStyle(.secondary)
                ForEach(made.indices, id: \.self) { i in
                    Allele.text(made[i], size: 15, weight: .bold)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(Capsule().fill(ToolKind.cross.color.opacity(0.14)))
                }
            }
            .accessibilityElement(children: .combine)
        }
        .padding(24)
        .frame(width: 560)
    }

    private func explanation(_ hets: Int) -> String {
        switch hets {
        case 0: return "This parent is homozygous for every gene, so every gamete gets the same alleles."
        case 1: return "The two alleles of the heterozygous gene sit on a pair of homologous chromosomes. Meiosis I pulls the pair apart, so half the gametes get one allele and half the other (segregation)."
        default:
            return "Each gene is on a different pair of chromosomes. At metaphase I each pair lines up at the plate with either homolog facing either pole, independently of the others, so all \(1 << hets) combinations reach the gametes equally often (independent assortment)."
        }
    }

    private func flipOne(_ ps: [Pair]) {
        // (the first heterozygous pair stays put and the others count through every arrangement, like a binary counter;
        // with one such pair, it simply turns round)
        let hets = ps.indices.filter { ps[$0].het && $0 < flips.count }
        guard let first = hets.first else { return }
        if hets.count == 1 {
            flips[first].toggle()
            return
        }
        for k in hets.dropFirst() {
            if flips[k] {
                flips[k] = false
            } else {
                flips[k] = true
                return
            }
        }
    }

    private func gametesNow(_ ps: [Pair]) -> [String] {
        func join(_ parts: [String]) -> String { parts.allSatisfy { $0.count == 1 } ? parts.joined() : parts.joined(separator: " ") }
        let l = join(ps.map { $0.left }), r = join(ps.map { $0.right })
        return l == r ? [l] : [l, r]
    }

    private func metaphase(_ ps: [Pair]) -> some View {
        let lengths: [CGFloat] = [150, 96, 122]
        return ZStack {
            RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Color.primary.opacity(0.04))
            // (the plate, and the poles either side)
            Rectangle()
                .fill(Color.secondary.opacity(0.5))
                .frame(width: 1.5)
                .padding(.top, 26)
                .padding(.bottom, 10)
            HStack {
                Text("pole").font(.sCaption2).foregroundStyle(.tertiary)
                Spacer()
                Text("pole").font(.sCaption2).foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 14)
            VStack(spacing: 14) {
                ForEach(ps.indices, id: \.self) { k in
                    // (each homolog keeps its identity, so turning the pair round slides them past each other)
                    let order = ps[k].flipped ? [1, 0] : [0, 1]
                    HStack(spacing: 10) {
                        ForEach(order, id: \.self) { h in
                            chromosome(h == 0 ? ps[k].a : ps[k].b, maternal: h == 0, length: lengths[k % lengths.count])
                        }
                    }
                    .accessibilityElement(children: .ignore)
                    .accessibilityLabel("Pair \(k + 1): \(Allele.spoken(ps[k].left)) facing the left pole, \(Allele.spoken(ps[k].right)) facing the right")
                }
            }
            .padding(.top, 30)
            .padding(.bottom, 14)
        }
        .overlay(alignment: .top) {
            Text("metaphase plate").font(.sCaption2).foregroundStyle(.tertiary).padding(.top, 8)
        }
        .frame(height: max(150, CGFloat(ps.count) * 56 + 50))
    }

    /// A replicated chromosome lying across the spindle: two sister chromatids (maternal red, paternal blue), its allele
    /// on it.
    private func chromosome(_ allele: String, maternal: Bool, length: CGFloat) -> some View {
        let c = maternal ? BioPalette.maternal.color : BioPalette.paternal.color
        return VStack(spacing: 2) {
            Capsule().fill(c).frame(width: length, height: 9)
            Capsule().fill(c.opacity(0.85)).frame(width: length, height: 9)
        }
        .overlay {
            Allele.text(allele, size: 13, weight: .bold)
                .foregroundStyle(.white)
                .padding(.horizontal, 6)
                .background(Capsule().fill(c))
        }
    }
}
