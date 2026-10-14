import SwiftUI

// Cell Division's Cell cycle page (spec §4.6): a wheel drawn in proportion — G1 about 11 h, S about 8 h, G2 about 4 h,
// M about 1 h of a typical 24 h human cell (Alberts), M at the top — with G0 as a side branch off G1 and the three
// checkpoints as gates: G1/S (the restriction point), G2/M and the spindle assembly checkpoint in M. A press on a gate
// or a phase (on the wheel or in the list) opens its card, with the cyclins and CDKs that drive it; under the wheel the
// cyclins' levels rise and fall through one cycle. "Cancer" shows what goes wrong when the checkpoints fail; "What
// happens in S phase" opens DNA & RNA's Replication page.

struct CellCyclePage: View {
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        PTWorkLayout {
            CellCycleList()
        } centre: {
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .firstTextBaseline) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("The cell cycle").font(.system(size: 22, weight: .bold))
                        Text("A typical human cell, about 24 hours from one division to the next")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Toggle(isOn: $model.cancer.animation(Motion.snappy)) { Text("Cancer") }
                        .toggleStyle(.switch)
                        .help("Show what goes wrong when the checkpoints fail")
                }
                .padding(16)
                CellCycleWheel(selected: $model.cycleItem, cancer: model.cancer)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .padding(.horizontal, 12)
                CyclinChart()
                    .frame(height: 124)
                    .padding(.horizontal, 20)
                    .padding(.bottom, 16)
            }
            .card(radius: 20)
        } right: {
            CellCycleCard(item: model.cycleItem, cancer: model.cancer)
        }
    }
}

/// The wheel's colours for each phase.
enum CellCycleColors {
    static func phase(_ id: String) -> Color {
        switch id {
        case "g1": return Color(hex: "#14b8a6")
        case "s": return Color(hex: "#7d5cf5")
        case "g2": return Color(hex: "#3b82f6")
        case "m": return Color(hex: "#f2994a")
        default: return Color(hex: "#8e8e93")
        }
    }
}

/// The left column: the phases and the gates as a list (each opens its card), the link to replication, the cancer view.
struct CellCycleList: View {
    @ObservedObject private var model = DivisionState.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Phases")
            VStack(alignment: .leading, spacing: 2) {
                ForEach(DivisionModel.cyclePhases + [DivisionModel.g0], id: \.id) { p in
                    item(p.id, p.hours > 0 ? "\(p.title) · \(Int(p.hours)) h" : p.title, swatch: CellCycleColors.phase(p.id))
                }
            }
            BioHeading("Checkpoints")
            VStack(alignment: .leading, spacing: 2) {
                ForEach(DivisionModel.checkpoints, id: \.id) { c in
                    item(c.id, c.name, symbol: model.cancer ? "xmark.shield.fill" : "checkmark.shield.fill")
                }
            }
            BioHeading("S phase up close")
            Button {
                BioNav.shared.set(.dna, "replication")
                BioTools.open(.dna, info: ["page": "replication"])
            } label: {
                Label("What happens in S phase", systemImage: "arrow.triangle.branch").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Open DNA & RNA's Replication page: a replication fork, step by step")
            Text("Mitosis and meiosis, stage by stage, are on this tool's other pages.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    private func item(_ id: String, _ title: String, swatch: Color? = nil, symbol: String? = nil) -> some View {
        let on = model.cycleItem == id
        return Button {
            withAnimation(Motion.snappy) { model.cycleItem = on ? nil : id }
        } label: {
            HStack(spacing: 10) {
                if let swatch { RoundedRectangle(cornerRadius: 3, style: .continuous).fill(swatch).frame(width: 12, height: 12) }
                if let symbol { Image(systemName: symbol).foregroundStyle(model.cancer ? Color.red : Color.green).frame(width: 14) }
                Text(title).font(.sCallout.weight(on ? .semibold : .regular)).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 5)
            .background(on ? Theme.accent.opacity(0.14) : Color.clear, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// The wheel: each phase an arc in proportion to its hours (clockwise, M at the top), the interphase bracket, G0 off G1,
/// and a gate at each checkpoint — a bar across the ring and a button outside it.
struct CellCycleWheel: View {
    @Binding var selected: String?
    let cancer: Bool
    @Environment(\.colorScheme) private var scheme

    /// A point on the wheel: degrees clockwise from the top, at a radius.
    static func point(_ c: CGPoint, _ degrees: Double, _ r: CGFloat) -> CGPoint {
        let a = degrees * .pi / 180
        return CGPoint(x: c.x + r * CGFloat(sin(a)), y: c.y - r * CGFloat(cos(a)))
    }

    static func sector(_ c: CGPoint, from a: Double, to b: Double, inner: CGFloat, outer: CGFloat) -> Path {
        var p = Path()
        let steps = max(Int(abs(b - a) / 2), 2)
        p.move(to: point(c, a, outer))
        for i in 1...steps { p.addLine(to: point(c, a + (b - a) * Double(i) / Double(steps), outer)) }
        for i in stride(from: steps, through: 0, by: -1) { p.addLine(to: point(c, a + (b - a) * Double(i) / Double(steps), inner)) }
        p.closeSubpath()
        return p
    }

    static func arc(_ c: CGPoint, from a: Double, to b: Double, r: CGFloat) -> Path {
        var p = Path()
        let steps = max(Int(abs(b - a) / 2), 2)
        p.move(to: point(c, a, r))
        for i in 1...steps { p.addLine(to: point(c, a + (b - a) * Double(i) / Double(steps), r)) }
        return p
    }

    /// Where each gate's button sits (the two near the top spread apart so both can be pressed, and M's name fits between
    /// them).
    static func buttonAngle(_ id: String) -> Double {
        switch id {
        case "g2m": return 310
        case "spindle": return 42
        default: return 172.5
        }
    }

    var body: some View {
        GeometryReader { geo in
            let size = geo.size
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            // (the ring small enough that the gates' buttons and M's name outside it stay inside the view: in a short
            // window they ran up into the heading)
            let r = max(min(min(size.width, size.height) * 0.36, size.height / 2 - 56), 48)
            let inner = r * 0.66
            let g0 = CellCycleWheel.point(c, 75, r * 1.42)
            ZStack {
                Canvas { gc, _ in
                    let arcs = DivisionModel.cycleArcs
                    for a in arcs {
                        let on = selected == a.phase.id
                        gc.fill(CellCycleWheel.sector(c, from: a.start, to: a.end, inner: inner, outer: on ? r + 6 : r),
                                with: .color(CellCycleColors.phase(a.phase.id).opacity(on ? 0.95 : 0.75)))
                    }
                    // (interphase and the mitotic phase, bracketed outside)
                    let edge = Color.primary.opacity(0.35)
                    gc.stroke(CellCycleWheel.arc(c, from: arcs[0].start + 1, to: arcs[2].end - 1, r: r + 16), with: .color(edge), lineWidth: 1.5)
                    gc.stroke(CellCycleWheel.arc(c, from: arcs[3].start + 0.5, to: arcs[3].end - 0.5, r: r + 16), with: .color(edge), lineWidth: 1.5)
                    // (G0: off G1 and back)
                    let out = CellCycleWheel.point(c, 62, r + 2), back = CellCycleWheel.point(c, 88, r + 2)
                    var branch = Path()
                    branch.move(to: out)
                    branch.addQuadCurve(to: CGPoint(x: g0.x - 22, y: g0.y - 12), control: CellCycleWheel.point(c, 64, r * 1.3))
                    branch.move(to: CGPoint(x: g0.x - 20, y: g0.y + 14))
                    branch.addQuadCurve(to: back, control: CellCycleWheel.point(c, 86, r * 1.3))
                    gc.stroke(branch, with: .color(Color.primary.opacity(0.45)), style: StrokeStyle(lineWidth: 1.5, lineCap: .round, dash: [4, 3]))
                    gc.fill(Path(ellipseIn: CGRect(x: g0.x - 24, y: g0.y - 24, width: 48, height: 48)),
                            with: .color(CellCycleColors.phase("g0").opacity(selected == "g0" ? 0.5 : 0.25)))
                    // (the gates: a bar across the ring at each checkpoint)
                    for cp in DivisionModel.checkpoints {
                        var bar = Path()
                        bar.move(to: CellCycleWheel.point(c, cp.angle, inner - 6))
                        bar.addLine(to: CellCycleWheel.point(c, cp.angle, r + 6))
                        let col: Color = cancer ? .red : (scheme == .dark ? .white : .black)
                        gc.stroke(bar, with: .color(col.opacity(selected == cp.id ? 0.95 : 0.7)),
                                  style: StrokeStyle(lineWidth: selected == cp.id ? 5 : 3.5, lineCap: .round, dash: cancer ? [3, 3] : []))
                        // (a leader to its button when that is moved aside)
                        let b = CellCycleWheel.buttonAngle(cp.id)
                        if abs(b - cp.angle) > 1 {
                            var lead = Path()
                            lead.move(to: CellCycleWheel.point(c, cp.angle, r + 8))
                            lead.addLine(to: CellCycleWheel.point(c, b, r + 30))
                            gc.stroke(lead, with: .color(Color.primary.opacity(0.4)), lineWidth: 1)
                        }
                    }
                }
                .contentShape(Rectangle())
                .gesture(SpatialTapGesture().onEnded { v in pick(v.location, c: c, r: r, inner: inner, g0: g0) })
                // (the phases' names on the ring; M's outside, it is thin)
                ForEach(Array(DivisionModel.cycleArcs.enumerated()), id: \.offset) { _, a in
                    let mid = (a.start + a.end) / 2
                    let thin = a.end - a.start < 30
                    VStack(spacing: 0) {
                        Text(a.phase.name).font(.system(size: thin ? 13 : 17, weight: .bold))
                        Text("\(Int(a.phase.hours)) h").font(.sCaption.monospacedDigit())
                    }
                    .foregroundStyle(thin ? Color.primary : Color.white)
                    // (M's name straight above its arc, between the two gates' buttons — beside it, G2/M's covered it)
                    .position(thin ? CellCycleWheel.point(c, mid, r + 34) : CellCycleWheel.point(c, mid, (r + inner) / 2))
                    .allowsHitTesting(false)
                }
                Text("Interphase · 23 h")
                    .font(.sCaption.weight(.semibold))
                    .foregroundStyle(.secondary)
                    .position(CellCycleWheel.point(c, 250, r + 66))
                    .allowsHitTesting(false)
                Text("G0").font(.system(size: 15, weight: .bold)).position(g0).allowsHitTesting(false)
                // (held inside the ring's hole, on two lines if need be: wider, it ran into G1's name)
                VStack(spacing: 2) {
                    Text(cancer ? "Checkpoints failing" : "One cell cycle").font(.sHeadline).foregroundStyle(cancer ? Color.red : Color.primary)
                    Text(cancer ? "The cell keeps dividing" : "G1 → S → G2 → M").font(.sCaption).foregroundStyle(.secondary)
                }
                .multilineTextAlignment(.center)
                .lineLimit(2)
                .minimumScaleFactor(0.75)
                .frame(width: max(inner * 2 - 16, 40))
                .position(c)
                .allowsHitTesting(false)
                ForEach(DivisionModel.checkpoints, id: \.id) { cp in
                    Button {
                        withAnimation(Motion.snappy) { selected = selected == cp.id ? nil : cp.id }
                    } label: {
                        Label(cp.short, systemImage: cancer ? "xmark.shield.fill" : "checkmark.shield.fill")
                            .font(.sCaption.weight(.semibold))
                            .foregroundStyle(cancer ? Color.red : Color.primary)
                            .padding(.horizontal, 8)
                            .padding(.vertical, 4)
                            .background(.regularMaterial, in: Capsule())
                            .overlay(Capsule().strokeBorder(selected == cp.id ? Theme.accent : Color.primary.opacity(0.15), lineWidth: selected == cp.id ? 2 : 1))
                    }
                    .buttonStyle(.plain)
                    .position(CellCycleWheel.point(c, CellCycleWheel.buttonAngle(cp.id), r + 44))
                    .help(cp.name)
                    .accessibilityLabel(cp.name)
                    .accessibilityAddTraits(selected == cp.id ? .isSelected : [])
                }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("The cell cycle: G1 11 hours, S 8 hours, G2 4 hours, M 1 hour, with G0 off G1 and three checkpoints")
    }

    /// A press on the wheel: the phase under it (or G0).
    private func pick(_ p: CGPoint, c: CGPoint, r: CGFloat, inner: CGFloat, g0: CGPoint) {
        if hypot(p.x - g0.x, p.y - g0.y) < 26 {
            withAnimation(Motion.snappy) { selected = selected == "g0" ? nil : "g0" }
            return
        }
        let d = hypot(p.x - c.x, p.y - c.y)
        guard d >= inner - 4, d <= r + 8 else { return }
        var deg = atan2(Double(p.x - c.x), Double(c.y - p.y)) * 180 / .pi
        if deg < DivisionModel.cycleArcs[0].start { deg += 360 }
        if let a = DivisionModel.cycleArcs.first(where: { deg >= $0.start && deg < $0.end }) ?? DivisionModel.cycleArcs.last {
            withAnimation(Motion.snappy) { selected = selected == a.phase.id ? nil : a.phase.id }
        }
    }
}

/// The cyclins through one cycle (illustrative shapes): the G1/S cyclin peaking at the G1/S boundary, the M cyclin
/// building through S and G2 and destroyed at anaphase; the CDK level steady. Phase bands along the bottom.
struct CyclinChart: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 14) {
                Text("Cyclins rise and fall each cycle").font(.sCallout.weight(.semibold))
                Spacer()
                key(Color(hex: "#14b8a6"), "G1/S cyclin", dashed: false)
                key(Color(hex: "#f2994a"), "M cyclin", dashed: false)
                key(Color.secondary, "CDK", dashed: true)
            }
            Canvas { gc, size in
                let band: CGFloat = 16
                let h = size.height - band - 4
                func x(_ t: Double) -> CGFloat { CGFloat(t / 24) * size.width }
                // (the phases along the bottom, G1 from 0 h)
                var start = 0.0
                for p in DivisionModel.cyclePhases {
                    let rect = CGRect(x: x(start), y: size.height - band, width: x(start + p.hours) - x(start) - 1, height: band)
                    gc.fill(Path(roundedRect: rect, cornerRadius: 3), with: .color(CellCycleColors.phase(p.id).opacity(0.75)))
                    if p.hours >= 4 {
                        gc.draw(Text(p.name).font(.system(size: 10, weight: .bold)).foregroundColor(.white), at: CGPoint(x: rect.midX, y: rect.midY))
                    }
                    start += p.hours
                }
                func curve(_ f: (Double) -> Double) -> Path {
                    var p = Path()
                    p.move(to: CGPoint(x: x(0), y: 2 + h * CGFloat(1 - f(0))))
                    for i in 1...240 {
                        let t = Double(i) / 10
                        p.addLine(to: CGPoint(x: x(t), y: 2 + h * CGFloat(1 - f(t))))
                    }
                    return p
                }
                gc.stroke(curve { _ in 0.82 }, with: .color(.secondary.opacity(0.7)), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3]))
                gc.stroke(curve(DivisionModel.cyclinG1S), with: .color(Color(hex: "#14b8a6")), style: StrokeStyle(lineWidth: 2.2, lineCap: .round, lineJoin: .round))
                gc.stroke(curve(DivisionModel.cyclinM), with: .color(Color(hex: "#f2994a")), style: StrokeStyle(lineWidth: 2.2, lineCap: .round, lineJoin: .round))
            }
            Text("Illustrative levels. Cyclins are made and destroyed each cycle; CDKs stay, working only when bound to a cyclin.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Cyclin levels: the G1/S cyclin peaks at the start of S phase; the M cyclin builds through S and G2, peaks in M and drops at anaphase; the CDK level stays steady.")
    }

    private func key(_ c: Color, _ text: String, dashed: Bool) -> some View {
        HStack(spacing: 5) {
            Capsule().fill(c).frame(width: dashed ? 8 : 16, height: 3)
            Text(text).font(.sCaption)
        }
    }
}

/// The right column: the card for the gate or phase pressed, else how to read the wheel.
struct CellCycleCard: View {
    let item: String?
    let cancer: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            if let cp = DivisionModel.checkpoints.first(where: { $0.id == item }) {
                BioCard(title: cp.name, swatch: cancer ? Color.red : Color.green) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("It checks").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                        ForEach(cp.checks, id: \.self) { c in
                            HStack(alignment: .firstTextBaseline, spacing: 6) {
                                Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)).foregroundStyle(.green)
                                Text(c).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                            }
                        }
                    }
                    BioFactRow(label: "Why it matters", text: cp.role)
                    BioFactRow(label: "Cyclins and CDKs", text: cp.cyclin)
                    if cancer { BioFactRow(label: "When it fails", text: cp.cancer, tint: Color.red) }
                }
            } else if let p = (DivisionModel.cyclePhases + [DivisionModel.g0]).first(where: { $0.id == item }) {
                BioCard(title: p.title, swatch: CellCycleColors.phase(p.id),
                        subtitle: p.hours > 0 ? "About \(Int(p.hours)) h of a 24 h cycle (Alberts)" : nil) {
                    BioFactRow(label: "What happens", text: p.text)
                    if p.id == "s" {
                        BioFactRow(label: "Common mix-up", text: "DNA replicates here, in interphase, never in prophase; and it doubles the DNA, not the number of chromosomes.", tint: Color.orange)
                    }
                }
            } else {
                BioCard(title: "Reading the wheel") {
                    BioFactRow(label: "In proportion", text: "Each phase's arc is its share of a typical 24 h human cell's cycle: interphase (G1, S, G2) takes 23 h, mitosis and cytokinesis about 1 h.")
                    BioFactRow(label: "Checkpoints", text: "Press a gate (a bar across the ring) to see what it checks before letting the cell go on.")
                }
            }
            BioCard(title: "Cyclins and CDKs") {
                Text(DivisionModel.cyclinRule).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            }
            if cancer {
                BioCard(title: "When checkpoints fail", swatch: Color.red) {
                    Text("Cancer cells divide when they should not: mutations stick the accelerators on (oncogenes, from proto-oncogenes such as growth-factor receptors and Ras) and take the brakes off (tumour suppressors such as p53 and Rb). Damaged DNA is copied, unfinished DNA is divided, and chromosomes are shared out wrongly.")
                        .font(.sCallout)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }
}
