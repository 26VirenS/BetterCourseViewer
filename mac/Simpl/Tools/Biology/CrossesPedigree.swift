import SwiftUI

// Genetics Crosses's Pedigree page (spec §4.5 Pedigree): a family drawn the standard way — squares for males, circles for
// females, filled when affected, half-filled for a known carrier, marriage lines and lines of descent, generations
// numbered I, II, III — built from an example or from one couple by adding partners and children, and its mode
// checker: autosomal dominant, autosomal recessive, X-linked dominant and X-linked recessive, each consistent or ruled out,
// with the people who rule it out ringed. Full penetrance and no new mutations are assumed, and said.

struct CrossPedigreePage: View {
    @ObservedObject var model: CrossesState
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let verdicts = model.verdicts
        let labels = model.pedigree.labels
        PTWorkLayout {
            controls(labels)
        } centre: {
            drawing(verdicts, labels)
        } right: {
            side(verdicts, labels)
        }
    }

    private func change(_ edit: @escaping () -> Void) {
        if reduceMotion { edit() } else { withAnimation(Motion.snappy) { edit() } }
    }

    // MARK: Editing

    private func controls(_ labels: [Int: String]) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Start from")
            CrossMenu(help: "A pedigree to begin with") {
                ForEach(Pedigree.examples) { e in
                    Button(e.title) {
                        change {
                            model.pedigree = e.pedigree
                            model.selected = nil
                            model.shownMode = nil
                        }
                    }
                }
            } label: {
                Text(Pedigree.examples.first { $0.pedigree == model.pedigree }?.title ?? "Your own").font(.sCallout.weight(.semibold)).lineLimit(1)
            }
            if let e = Pedigree.examples.first(where: { $0.pedigree == model.pedigree }) {
                Text(e.note).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            BioHeading("Selected")
            if let id = model.selected, let p = model.pedigree.person(id) {
                editor(p, label: labels[id] ?? "")
            } else {
                Text("Click someone to set whether they are affected, or to add a partner or children.")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            BioHeading("Key")
            VStack(alignment: .leading, spacing: 6) {
                keyRow(male: true, affected: false, carrier: false, "Male")
                keyRow(male: false, affected: false, carrier: false, "Female")
                keyRow(male: true, affected: true, carrier: false, "Affected")
                keyRow(male: false, affected: false, carrier: true, "Known carrier")
            }
            Text("\(model.pedigree.people.count) of \(Pedigree.maxPeople) people").font(.sCaption).foregroundStyle(.tertiary)
        }
        .font(.sCallout)
    }

    private func keyRow(male: Bool, affected: Bool, carrier: Bool, _ text: String) -> some View {
        HStack(spacing: 8) {
            PedigreeSymbol(male: male, affected: affected, carrier: carrier, size: 16)
            Text(text).font(.sCaption)
        }
        .accessibilityElement(children: .combine)
    }

    private func editor(_ p: PedigreePerson, label: String) -> some View {
        let status = p.affected ? 2 : (p.carrier ? 1 : 0)
        let hasPartner = model.pedigree.partner(of: p.id) != nil
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                PedigreeSymbol(male: p.male, affected: p.affected, carrier: p.carrier, size: 22)
                Text("\(label) · \(p.male ? "male" : "female")").font(.sHeadline)
            }
            Picker("", selection: Binding(get: { status }, set: { v in
                change { model.pedigree.update(p.id) { $0.affected = v == 2; $0.carrier = v == 1 } }
            })) {
                Text("Unaffected").tag(0)
                Text("Carrier").tag(1)
                Text("Affected").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("Affected shows the trait; a carrier is known to have one recessive allele without showing it")
            Picker("", selection: Binding(get: { p.male }, set: { v in change { model.pedigree.update(p.id) { $0.male = v } } })) {
                Text("Male").tag(true)
                Text("Female").tag(false)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .disabled(!model.pedigree.canChangeSex(p.id))
            .help(model.pedigree.canChangeSex(p.id) ? "Male or female" : "A partner's sex is fixed: a couple is a father and a mother")
            HStack(spacing: 8) {
                Button {
                    change { if let n = model.pedigree.addPartner(to: p.id) { model.selected = n } }
                } label: {
                    Label("Partner", systemImage: "heart").frame(maxWidth: .infinity)
                }
                .disabled(hasPartner || model.pedigree.isFull)
                .help(hasPartner ? "Already has a partner" : "Add a partner")
                Button {
                    change { _ = model.pedigree.addChild(of: p.id, male: true) }
                } label: {
                    Label("Son", systemImage: "square").frame(maxWidth: .infinity)
                }
                .disabled(!hasPartner || model.pedigree.isFull)
                .help(hasPartner ? "Add a son to this couple" : "Add a partner first")
                Button {
                    change { _ = model.pedigree.addChild(of: p.id, male: false) }
                } label: {
                    Label("Daughter", systemImage: "circle").frame(maxWidth: .infinity)
                }
                .disabled(!hasPartner || model.pedigree.isFull)
                .help(hasPartner ? "Add a daughter to this couple" : "Add a partner first")
            }
            .glassButton()
            .controlSize(.regular)
            Button(role: .destructive) {
                change {
                    model.pedigree.remove(p.id)
                    model.selected = nil
                }
            } label: {
                Text("Remove \(label) and their descendants").frame(maxWidth: .infinity)
            }
            .glassButton()
            .disabled(!model.pedigree.canRemove(p.id))
            .help(model.pedigree.canRemove(p.id) ? "Take them out, with their children and a partner who joined through them" : "The first person stays")
        }
        .padding(12)
        .card(radius: 14)
    }

    // MARK: The drawing

    private var slot: CGFloat { 76 }
    private var rowHeight: CGFloat { 112 }
    private var symbol: CGFloat { 36 }
    private var margin: CGFloat { 40 }

    private func drawing(_ verdicts: [PedigreeVerdict], _ labels: [Int: String]) -> some View {
        let spots = model.pedigree.layout()
        let ringed = Set(verdicts.first { $0.mode == model.shownMode }?.culprits ?? [])
        let maxX = spots.map(\.x).max() ?? 0
        let gens = (spots.map(\.generation).max() ?? 0) + 1
        let width = (CGFloat(maxX) + 0.5) * slot + 2 * margin
        let height = CGFloat(gens) * rowHeight + 40
        return VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Pedigree").font(.system(size: 22, weight: .bold))
                Spacer()
                if let m = model.shownMode {
                    Label("Ringed: who rules out \(m.title.lowercased())", systemImage: "circle.dashed")
                        .font(.sCallout)
                        .foregroundStyle(.red)
                }
            }
            .padding(16)
            ScrollView([.horizontal, .vertical]) {
                ZStack(alignment: .topLeading) {
                    Canvas { ctx, _ in
                        ctx.stroke(lines(spots), with: .color(.primary.opacity(0.75)), lineWidth: 1.5)
                    }
                    .frame(width: width, height: height)
                    .accessibilityHidden(true)
                    ForEach(0..<gens, id: \.self) { g in
                        Text(roman(g))
                            .font(.sCallout.weight(.bold))
                            .foregroundStyle(.secondary)
                            .position(x: 16, y: y(g))
                            .accessibilityHidden(true)
                    }
                    ForEach(spots, id: \.id) { s in
                        person(s, ringed: ringed.contains(s.id))
                    }
                }
                .frame(width: width, height: height, alignment: .topLeading)
                .padding(.horizontal, 10)
            }
            Text("Click a person to select them · ← → move between people")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .padding(12)
        }
        .card(radius: 20)
        .focusable()
        .focusEffectDisabled()
        .onKeyPress(.leftArrow) { moveSelection(-1, spots); return .handled }
        .onKeyPress(.rightArrow) { moveSelection(1, spots); return .handled }
        .onKeyPress(.escape) { model.selected = nil; return .handled }
    }

    private func moveSelection(_ step: Int, _ spots: [PedigreeSpot]) {
        let order = spots.sorted { $0.generation != $1.generation ? $0.generation < $1.generation : $0.x < $1.x }.map(\.id)
        guard !order.isEmpty else { return }
        if let s = model.selected, let i = order.firstIndex(of: s) {
            model.selected = order[(i + step + order.count) % order.count]
        } else {
            model.selected = order[0]
        }
    }

    private func roman(_ g: Int) -> String {
        let r = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"]
        return g < r.count ? r[g] : "\(g + 1)"
    }

    private func x(_ slotX: Double) -> CGFloat { margin + CGFloat(slotX) * slot }
    private func y(_ gen: Int) -> CGFloat { 30 + CGFloat(gen) * rowHeight + symbol / 2 }

    /// Marriage lines between partners, a line down from each couple to the line over their children, and a line down to
    /// each child.
    private func lines(_ spots: [PedigreeSpot]) -> Path {
        var p = Path()
        let at = Dictionary(uniqueKeysWithValues: spots.map { ($0.id, $0) })
        for c in model.pedigree.couples {
            guard let a = at[c[0]], let b = at[c[1]] else { continue }
            let (l, r) = a.x < b.x ? (a, b) : (b, a)
            let yy = y(l.generation)
            p.move(to: CGPoint(x: x(l.x) + symbol / 2, y: yy))
            p.addLine(to: CGPoint(x: x(r.x) - symbol / 2, y: yy))
            let kids = model.pedigree.people.filter { $0.father == c[0] && $0.mother == c[1] }.compactMap { at[$0.id] }
            guard !kids.isEmpty else { continue }
            let mid = (x(l.x) + x(r.x)) / 2
            let sib = y(l.generation + 1) - symbol / 2 - 24
            p.move(to: CGPoint(x: mid, y: yy))
            p.addLine(to: CGPoint(x: mid, y: sib))
            let xs = kids.map { x($0.x) } + [mid]
            p.move(to: CGPoint(x: xs.min() ?? mid, y: sib))
            p.addLine(to: CGPoint(x: xs.max() ?? mid, y: sib))
            for k in kids {
                p.move(to: CGPoint(x: x(k.x), y: sib))
                p.addLine(to: CGPoint(x: x(k.x), y: y(k.generation) - symbol / 2))
            }
        }
        return p
    }

    private func person(_ s: PedigreeSpot, ringed: Bool) -> some View {
        let p = model.pedigree.person(s.id)
        let selected = model.selected == s.id
        return Button {
            change { model.selected = selected ? nil : s.id }
        } label: {
            VStack(spacing: 6) {
                PedigreeSymbol(male: p?.male ?? true, affected: p?.affected ?? false, carrier: p?.carrier ?? false, size: symbol)
                    .overlay {
                        if ringed {
                            Circle().strokeBorder(Color.red, style: StrokeStyle(lineWidth: 2, dash: [4, 3])).frame(width: symbol + 18, height: symbol + 18)
                        }
                    }
                    .overlay {
                        if selected {
                            RoundedRectangle(cornerRadius: 9, style: .continuous).strokeBorder(Theme.accent, lineWidth: 2.5).frame(width: symbol + 10, height: symbol + 10)
                        }
                    }
                Text(s.label)
                    .font(.sCaption.weight(selected ? .bold : .medium))
                    .foregroundStyle(ringed ? Color.red : Color.secondary)
            }
            .frame(width: slot - 6, height: symbol + 30)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .position(x: x(s.x), y: y(s.generation) + 12)
        .help("\(s.label): \(description(p))")
        .accessibilityLabel("\(s.label), \(description(p))\(ringed ? ", rules out the mode shown" : "")")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func description(_ p: PedigreePerson?) -> String {
        guard let p else { return "" }
        let sex = p.male ? "male" : "female"
        return p.affected ? "\(sex), affected" : (p.carrier ? "\(sex), carrier" : "\(sex), unaffected")
    }

    // MARK: The verdicts

    private func side(_ verdicts: [PedigreeVerdict], _ labels: [Int: String]) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            BioHeading("Which modes fit?")
            ForEach(verdicts) { v in verdictCard(v, labels) }
            Text("Consistent means some set of genotypes explains everyone — it does not prove the mode. Assumes full penetrance (everyone with the genotype shows it) and no new mutations.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            BioCard(title: "Clues") {
                BioFactRow(label: "Recessive", text: "An affected child of two unaffected parents.")
                BioFactRow(label: "Dominant", text: "An unaffected child of two affected parents; affected in every generation.")
                BioFactRow(label: "X-linked recessive", text: "Mostly males; an affected mother's sons are all affected; it passes through unaffected carrier mothers.")
                BioFactRow(label: "X-linked dominant", text: "An affected father's daughters are all affected and his sons never are.")
            }
        }
    }

    private func verdictCard(_ v: PedigreeVerdict, _ labels: [Int: String]) -> some View {
        let shown = model.shownMode == v.mode
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Image(systemName: v.consistent ? "checkmark.circle.fill" : "xmark.circle.fill")
                    .foregroundStyle(v.consistent ? Color.green : Color.red)
                Text(v.mode.title).font(.sHeadline)
                Spacer(minLength: 4)
                Text(v.consistent ? "Consistent" : "Ruled out").font(.sCallout.weight(.semibold)).foregroundStyle(v.consistent ? Color.green : Color.red)
            }
            if !v.consistent {
                Text("By " + v.culprits.compactMap { labels[$0] }.joined(separator: ", "))
                    .font(.sCallout.weight(.medium))
                ForEach(Array(v.reasons.prefix(3)), id: \.self) { r in
                    Text(r).font(.sCaption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                if v.reasons.count > 3 {
                    Text("and \(v.reasons.count - 3) more like these").font(.sCaption).foregroundStyle(.tertiary)
                }
                Button(shown ? "Hide them" : "Ring them") {
                    change { model.shownMode = shown ? nil : v.mode }
                }
                .buttonStyle(.link)
                .help("Ring the people who rule out \(v.mode.title.lowercased())")
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 14, tint: shown ? Color.red : nil)
        .accessibilityElement(children: .contain)
    }
}

/// A pedigree symbol: a square (male) or a circle (female), filled when affected, its left half filled for a known
/// carrier.
struct PedigreeSymbol: View {
    let male: Bool
    let affected: Bool
    let carrier: Bool
    var size: CGFloat = 36

    var body: some View {
        let shape = male ? AnyShape(RoundedRectangle(cornerRadius: size * 0.06)) : AnyShape(Circle())
        ZStack {
            shape.fill(Theme.card)
            if affected {
                shape.fill(Color.primary)
            } else if carrier {
                shape.fill(Color.primary).mask {
                    HStack(spacing: 0) {
                        Rectangle()
                        Color.clear
                    }
                }
            }
            shape.stroke(Color.primary, lineWidth: max(1.2, size / 18))
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}
