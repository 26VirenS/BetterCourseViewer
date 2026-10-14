import Charts
import SwiftUI

// Enzyme Lab (spec §4.9, §5.7): a simulator with three linked graphs — rate against substrate (Michaelis–Menten, with a
// competitive or noncompetitive inhibitor), against temperature and against pH — beside a 2D active site (the
// substrate fits and the enzyme closes slightly round it; a competitive inhibitor takes the site; a noncompetitive one
// binds elsewhere and bends it; heat unfolds it for good) and the activation-energy diagram with and without the
// enzyme. The graphs are linked: the rate against [S] uses the Vmax the temperature and pH leave, and each marks where
// the conditions are now. The models are illustrative (Model/EnzymeLabModel.swift); the settings are remembered
// (SimplBio.enzyme.conditions).
//
// Launch: -SimplTool enzyme -SimplToolState [competitive|noncompetitive][:denatured][:enzyme=human|taq|fish]
// [:phpreset=pepsin|amylase|catalase|trypsin][:temp=<°C>][:ph=<pH>][:s=<mM>].

/// The simulator's settings, remembered on this Mac.
@MainActor
final class EnzymeLabStore: ObservableObject {
    static let shared = EnzymeLabStore()

    @Published var c: EnzymeConditions {
        didSet { save() }
    }

    private init() {
        if let s = BioPrefs.string(.enzyme, "conditions"), let saved = try? JSONDecoder().decode(EnzymeConditions.self, from: Data(s.utf8)) {
            c = saved
        } else {
            c = EnzymeConditions()
        }
    }

    private func save() {
        if let data = try? JSONEncoder().encode(c) { BioPrefs.set(.enzyme, "conditions", String(decoding: data, as: UTF8.self)) }
    }
}

/// The enzyme's, the substrate's and the inhibitor's colours (never alone: each is labelled in the picture).
enum EnzymeColors {
    static let enzyme = BioHexPair("#2f8fa3", "#6fd3e6")
    static let substrate = BioHexPair("#c79a00", "#ffd60a")
    static let inhibitor = BioHexPair("#d63a34", "#ff6961")
}

struct EnzymeLabTool: View {
    /// What the launch asked of it, if it opened it (BioLaunchState).
    var state: BioLaunchState? = nil

    /// One page: none in its header.
    static let pages: [BioPage] = []

    @ObservedObject private var lab = EnzymeLabStore.shared
    @State private var applied = false

    var body: some View {
        PTWorkLayout {
            EnzymeControls(lab: lab)
        } centre: {
            EnzymeGraphs(c: lab.c)
        } right: {
            EnzymeSide(c: lab.c)
        }
        .onAppear(perform: apply)
    }

    /// What the launch asked for, once.
    private func apply() {
        guard !applied else { return }
        applied = true
        guard let state else { return }
        var c = lab.c
        if state.has("none") { c.inhibitor = .none }
        if state.has("competitive") { c.inhibitor = .competitive }
        if state.has("noncompetitive") { c.inhibitor = .noncompetitive }
        if let e = state.value("enzyme"), EnzymeModel.temperaturePresets.contains(where: { $0.id == e }) { c.setTemperaturePreset(e) }
        if let p = state.value("phpreset"), EnzymeModel.phPresets.contains(where: { $0.id == p }) { c.phPreset = p }
        if let s = state.value("s").flatMap({ Double($0) }) { c.substrate = min(max(s, 0), EnzymeConditions.substrateMax) }
        if let ph = state.value("ph").flatMap({ Double($0) }) { c.ph = min(max(ph, 0), 14) }
        if state.has("denatured") {
            // (heated past Tm + 2w, then cooled back to where it was)
            let t = c.temperature
            c.setTemperature(c.tempPreset.denatureAt + 2)
            c.setTemperature(t)
        }
        if let t = state.value("temp").flatMap({ Double($0) }) { c.setTemperature(min(max(t, 0), 100)) }
        lab.c = c
    }
}

// MARK: - The controls

private struct EnzymeControls: View {
    @ObservedObject var lab: EnzymeLabStore

    var body: some View {
        let c = lab.c
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Enzyme")
            VStack(alignment: .leading, spacing: 6) {
                Text("Temperature curve").font(.sCaption).foregroundStyle(.secondary)
                Picker("Temperature curve", selection: Binding(get: { lab.c.temperaturePreset }, set: { lab.c.setTemperaturePreset($0) })) {
                    ForEach(EnzymeModel.temperaturePresets) { p in
                        Text("\(p.name), best at \(Int(p.peak.rounded())) °C").tag(p.id)
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
                Text("pH curve").font(.sCaption).foregroundStyle(.secondary)
                Picker("pH curve", selection: $lab.c.phPreset) {
                    ForEach(EnzymeModel.phPresets) { p in
                        Text("\(p.name), best at pH \(String(halfUp: "%.1f", p.optimum))").tag(p.id)
                    }
                }
                .labelsHidden()
                .pickerStyle(.menu)
            }

            BioHeading("Conditions")
            EnzymeSlider(title: "Substrate [S]", value: $lab.c.substrate, range: 0...EnzymeConditions.substrateMax, step: 0.5,
                         text: "\(EnzymeText.number(c.substrate)) mM", help: "How much substrate there is")
            EnzymeSlider(title: "Temperature", value: Binding(get: { lab.c.temperature }, set: { lab.c.setTemperature($0) }),
                         range: EnzymeConditions.temperatureRange, step: 0.5, text: "\(EnzymeText.number(c.temperature)) °C",
                         help: "Past \(EnzymeText.number(c.tempPreset.denatureAt)) °C this enzyme unfolds for good")
            EnzymeSlider(title: "pH", value: $lab.c.ph, range: EnzymeConditions.phRange, step: 0.1, text: String(halfUp: "%.1f", c.ph),
                         help: "Acidity: each unit is a tenfold change in H⁺")

            BioHeading("Kinetics")
            EnzymeSlider(title: "Vmax", value: $lab.c.vmax, range: 10...200, step: 1, text: "\(EnzymeText.number(c.vmax)) µmol/min",
                         help: "The fastest rate, with every active site full")
            EnzymeSlider(title: "Km", value: $lab.c.km, range: 0.5...20, step: 0.5, text: "\(EnzymeText.number(c.km)) mM",
                         help: "The [S] at which the rate is half Vmax: lower means the enzyme binds substrate more tightly")

            BioHeading("Inhibitor")
            Picker("Inhibitor", selection: $lab.c.inhibitor) {
                ForEach(EnzymeInhibitor.allCases) { i in Text(i.title).tag(i) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .help("None, one that competes for the active site, or one that binds elsewhere")
            if c.inhibitor != .none {
                EnzymeSlider(title: "Inhibitor [I]", value: $lab.c.inhibitorConc, range: 0...20, step: 0.5,
                             text: "\(EnzymeText.number(c.inhibitorConc)) mM", help: "How much inhibitor there is")
                EnzymeSlider(title: "Ki", value: $lab.c.ki, range: 0.5...20, step: 0.5, text: "\(EnzymeText.number(c.ki)) mM",
                             help: "How tightly the inhibitor binds: lower is tighter")
            }

            HStack(spacing: 8) {
                Button("Fresh enzyme") { lab.c.freshEnzyme() }
                    .glassButton(prominent: c.denatured)
                    .disabled(!c.denatured)
                    .help("A new sample of the enzyme, never heated (put at its best temperature if the sample is too hot)")
                Button("Reset") { lab.c = EnzymeConditions() }
                    .glassButton()
                    .help("Every setting back to where it started")
            }
            .controlSize(.large)
            Text("These curves are illustrative models, not measured data.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }
}

/// A labelled slider with its value.
private struct EnzymeSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let step: Double
    let text: String
    let help: String

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack {
                Text(title)
                Spacer(minLength: 6)
                Text(text).monospacedDigit().foregroundStyle(.secondary)
            }
            .accessibilityHidden(true)
            // (rounded to the step in the binding: a stepped slider would draw a tick for every step)
            Slider(value: Binding(get: { value }, set: { value = min(max(($0 / step).rounded() * step, range.lowerBound), range.upperBound) }), in: range)
                .accessibilityLabel(title)
                .accessibilityValue(text)
        }
        .help(help)
    }
}

enum EnzymeText {
    /// 20 → "20", 7.5 → "7.5".
    static func number(_ x: Double) -> String {
        abs(x - x.rounded()) < 0.05 ? String(Int(x.rounded())) : String(halfUp: "%.1f", x)
    }
}

// MARK: - The three linked graphs

private struct EnzymeGraphs: View {
    let c: EnzymeConditions
    @Environment(\.colorScheme) private var scheme

    private var tint: Color { EnzymeColors.enzyme.color }

    var body: some View {
        // (the two small graphs 210 high, so the rate against [S] keeps room for its curve when the window is short)
        VStack(spacing: 16) {
            substrateCard
                .frame(maxHeight: .infinity)
            HStack(spacing: 16) {
                temperatureCard
                phCard
            }
            .frame(height: 210)
        }
    }

    private func card<Content: View>(_ title: String, _ line: String, @ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.sHeadline).accessibilityAddTraits(.isHeader)
                Text(line).font(.sCaption).foregroundStyle(.secondary).lineLimit(2)
            }
            content()
        }
        .padding(16)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .card(radius: 20)
    }

    private var substrateCard: some View {
        let a = c.apparent, v = c.effectiveVmax
        let line = "v = Vmax·[S] / (Km + [S]) = \(EnzymeText.number(v)) × \(EnzymeText.number(c.substrate)) / (\(EnzymeText.number(a.km)) + \(EnzymeText.number(c.substrate))) = \(String(halfUp: "%.1f", c.rate)) µmol/min"
        return card("Rate against substrate", line) {
            Chart {
                if c.inhibitor != .none {
                    ForEach(c.uninhibitedCurve) { p in
                        LineMark(x: .value("[S]", p.x), y: .value("Rate", p.y), series: .value("Curve", "Without inhibitor"))
                            .foregroundStyle(Color.secondary)
                            .lineStyle(StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
                    }
                }
                ForEach(c.substrateCurve) { p in
                    LineMark(x: .value("[S]", p.x), y: .value("Rate", p.y), series: .value("Curve", "Rate"))
                        .foregroundStyle(tint)
                        .lineStyle(StrokeStyle(lineWidth: 2.6, lineCap: .round))
                }
                RuleMark(y: .value("Vmax", v))
                    .foregroundStyle(tint.opacity(0.6))
                    .lineStyle(StrokeStyle(lineWidth: 1, dash: [3, 3]))
                    .annotation(position: .top, alignment: .trailing) {
                        Text("Vmax \(EnzymeText.number(v))").font(.sCaption2).foregroundStyle(.secondary)
                    }
                if a.km <= EnzymeConditions.substrateMax {
                    RuleMark(x: .value("Km", a.km), yStart: .value("Zero", 0), yEnd: .value("Half Vmax", v / 2))
                        .foregroundStyle(tint.opacity(0.6))
                        .lineStyle(StrokeStyle(lineWidth: 1, dash: [3, 3]))
                        .annotation(position: .top, alignment: .center) {
                            Text("Km \(EnzymeText.number(a.km))").font(.sCaption2).foregroundStyle(.secondary)
                        }
                }
                PointMark(x: .value("[S]", c.substrate), y: .value("Rate", c.rate))
                    .foregroundStyle(Theme.accent)
                    .symbolSize(80)
                    // (below the curve, to the right of the point: above it, it ran into Km's label)
                    .annotation(position: .bottomTrailing, alignment: .topLeading, spacing: 4) {
                        Text("v = \(String(halfUp: "%.1f", c.rate))").font(.sCaption.weight(.semibold)).foregroundStyle(Theme.accent)
                    }
            }
            .chartXScale(domain: 0...EnzymeConditions.substrateMax)
            .chartYScale(domain: 0...max(c.vmax * 1.12, 1))
            .frame(minHeight: 130)
            .chartXAxisLabel("Substrate concentration [S] (mM)")
            .chartYAxisLabel("Rate v (µmol/min)")
            .accessibilityLabel("Rate against substrate concentration. Now \(String(halfUp: "%.1f", c.rate)) at \(EnzymeText.number(c.substrate)) millimolar")
            if c.inhibitor != .none {
                Text(c.inhibitor == .competitive
                     ? "Dashed: without the inhibitor. Competitive: Km rises to \(EnzymeText.number(a.km)) mM, Vmax stays; enough substrate wins."
                     : "Dashed: without the inhibitor. Noncompetitive: Vmax falls to \(EnzymeText.number(v)), Km stays; more substrate doesn't help.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var temperatureCard: some View {
        let p = c.tempPreset
        return card("Rate against temperature", c.denatured ? "Denatured: dashed is the enzyme cooled back down, at a tenth." : "Faster as it warms, until it unfolds.") {
            Chart {
                RectangleMark(xStart: .value("From", p.denatureAt), xEnd: .value("To", EnzymeConditions.temperatureRange.upperBound),
                              yStart: .value("Bottom", 0), yEnd: .value("Top", 1.15))
                    .foregroundStyle(Color.red.opacity(scheme == .dark ? 0.12 : 0.06))
                ForEach(EnzymeModel.temperaturePresets) { q in
                    ForEach(EnzymeConditions.temperatureCurve(q)) { pt in
                        LineMark(x: .value("Temperature", pt.x), y: .value("Relative rate", pt.y), series: .value("Enzyme", q.name))
                            .foregroundStyle(q.id == p.id ? tint : Color.secondary.opacity(0.35))
                            .lineStyle(StrokeStyle(lineWidth: q.id == p.id ? 2.4 : 1.2))
                    }
                    PointMark(x: .value("Temperature", q.peak), y: .value("Relative rate", 1))
                        .opacity(0)
                        .annotation(position: .top) {
                            Text(EnzymeText.short(q)).font(.sCaption2).foregroundStyle(q.id == p.id ? Color.primary : Color.secondary)
                        }
                }
                if c.denatured {
                    ForEach(EnzymeConditions.temperatureCurve(p, cooled: true)) { pt in
                        LineMark(x: .value("Temperature", pt.x), y: .value("Relative rate", pt.y), series: .value("Enzyme", "Cooled back down"))
                            .foregroundStyle(tint)
                            .lineStyle(StrokeStyle(lineWidth: 1.6, dash: [4, 3]))
                    }
                }
                RuleMark(x: .value("Now", c.temperature))
                    .foregroundStyle(Theme.accent.opacity(0.45))
                    .lineStyle(StrokeStyle(lineWidth: 1))
                PointMark(x: .value("Now", c.temperature), y: .value("Relative rate", c.temperatureFactor))
                    .foregroundStyle(Theme.accent)
                    .symbolSize(60)
            }
            .chartXScale(domain: EnzymeConditions.temperatureRange)
            .chartYScale(domain: 0...1.15)
            .chartXAxisLabel("Temperature (°C)")
            .accessibilityLabel("Rate against temperature for \(p.name): best at \(Int(p.peak.rounded())) degrees; now \(Int((c.temperatureFactor * 100).rounded())) percent of the best")
        }
    }

    private var phCard: some View {
        let p = c.pHPreset
        return card("Rate against pH", "Each enzyme works best near its own pH.") {
            Chart {
                ForEach(EnzymeModel.phPresets) { q in
                    ForEach(EnzymeConditions.phCurve(q)) { pt in
                        LineMark(x: .value("pH", pt.x), y: .value("Relative rate", pt.y), series: .value("Enzyme", q.name))
                            .foregroundStyle(q.id == p.id ? tint : Color.secondary.opacity(0.35))
                            .lineStyle(StrokeStyle(lineWidth: q.id == p.id ? 2.4 : 1.2))
                    }
                }
                // (only the chosen curve named at its peak: amylase's, catalase's and trypsin's sit close together)
                PointMark(x: .value("pH", p.optimum), y: .value("Relative rate", 1))
                    .opacity(0)
                    .annotation(position: .top) {
                        Text("\(p.name), pH \(String(halfUp: "%.1f", p.optimum))").font(.sCaption2)
                    }
                RuleMark(x: .value("Now", c.ph))
                    .foregroundStyle(Theme.accent.opacity(0.45))
                    .lineStyle(StrokeStyle(lineWidth: 1))
                PointMark(x: .value("Now", c.ph), y: .value("Relative rate", c.phFactor))
                    .foregroundStyle(Theme.accent)
                    .symbolSize(60)
            }
            .chartXScale(domain: EnzymeConditions.phRange)
            .chartYScale(domain: 0...1.15)
            .chartXAxisLabel("pH")
            .accessibilityLabel("Rate against pH for \(p.name): best at pH \(String(halfUp: "%.1f", p.optimum)); now \(Int((c.phFactor * 100).rounded())) percent of the best")
        }
    }
}

extension EnzymeText {
    /// A preset's short name for its curve's label.
    static func short(_ p: EnzymeTemperaturePreset) -> String {
        switch p.id {
        case "taq": return "Taq"
        case "fish": return "Fish"
        default: return "Human"
        }
    }
}

// MARK: - The side: the active site, the numbers now, the energy diagram, the mix-ups

private struct EnzymeSide: View {
    let c: EnzymeConditions

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            EnzymeSiteCard(c: c)
            now
            EnzymeEnergyCard()
            BioCard(title: "Common mix-ups") {
                ForEach(EnzymeModel.mixUps, id: \.title) { m in BioFactRow(label: m.title, text: m.text) }
            }
        }
    }

    private var now: some View {
        let a = c.apparent
        let percent = c.vmax > 0 ? c.rate / c.vmax * 100 : 0
        return VStack(spacing: 0) {
            BioValueRow(label: "Rate v", value: String(halfUp: "%.1f µmol/min", c.rate), color: Theme.accent)
            BioValueRow(label: "Of the most it could be", value: String(halfUp: "%.0f%%", percent))
            BioValueRow(label: "Vmax now", value: "\(EnzymeText.number(c.effectiveVmax)) µmol/min")
            BioValueRow(label: "Km now", value: "\(EnzymeText.number(a.km)) mM")
            BioValueRow(label: "Temperature factor", value: String(halfUp: "%.2f", c.temperatureFactor) + (c.denatured ? " (denatured)" : ""),
                        color: c.denatured ? .red : .primary)
            BioValueRow(label: "pH factor", value: String(halfUp: "%.2f", c.phFactor), last: true)
        }
        .card(radius: 14)
    }
}

/// The active site in 2D, with its caption; "Show binding" plays the substrate arriving and the enzyme closing round it.
private struct EnzymeSiteCard: View {
    let c: EnzymeConditions
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme
    @State private var started: Date?
    /// Under Reduce Motion: the picture before binding, crossfaded in and out.
    @State private var apart = false

    private static let duration = 1.6

    var body: some View {
        let site = c.site
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Active site").font(.sHeadline).accessibilityAddTraits(.isHeader)
                Spacer(minLength: 6)
                if site == .bound {
                    Button("Show binding", action: play)
                        .glassButton()
                        .help("The substrate arriving and the enzyme closing round it")
                }
            }
            TimelineView(.animation(paused: started == nil)) { ctx in
                let p = progress(at: ctx.date)
                EnzymeSiteCanvas(site: site, strain: c.strain, approach: p.approach, close: p.close, dark: scheme == .dark)
                    .id(apart)
                    .transition(.opacity)
            }
            .frame(height: 190)
            Text(site.caption).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            if site != .denatured, let why = strainNote {
                Text(why)
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Active site picture. \(site.caption)")
    }

    /// Why the outline wavers, if it does.
    private var strainNote: String? {
        if c.heatStrain > 0.3 {
            return "Above its optimum the enzyme starts to unfold, and the active site loses its shape. Past \(EnzymeText.number(c.tempPreset.denatureAt)) °C it is denatured for good."
        }
        if c.phStrain > 0.5 { return "Far from its best pH, the active site's charges change and it loses its shape: the rate drops." }
        return nil
    }

    /// The substrate's approach (0 far, 1 in the site) and the enzyme's closing (0 open, 1 closed) at a moment.
    private func progress(at date: Date) -> (approach: Double, close: Double) {
        guard let started else { return apart ? (0, 0) : (1, 1) }
        let u = date.timeIntervalSince(started) / Self.duration
        return (BioStepTiming.ease(u / 0.6), BioStepTiming.ease((u - 0.6) / 0.4))
    }

    private func play() {
        if reduceMotion {
            withAnimation(.easeInOut(duration: 0.2)) { apart = true }
            Task { @MainActor in
                try? await Task.sleep(nanoseconds: 900_000_000)
                withAnimation(.easeInOut(duration: 0.2)) { apart = false }
            }
            return
        }
        let start = Date()
        started = start
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: UInt64((Self.duration + 0.1) * 1_000_000_000))
            if started == start { started = nil }
        }
    }
}

/// The picture: an enzyme with a pocket (its active site) on the left and a smaller one (an allosteric site) on the
/// right; the substrate, shaped to the pocket; the inhibitors; or the unfolded chain.
struct EnzymeSiteCanvas: View {
    let site: EnzymeSite
    /// 0 at the best pH, toward 1 far from it: the outline wavers.
    let strain: Double
    /// The substrate's way in: 0 away to the left, 1 in the site.
    let approach: Double
    /// The enzyme closing round the substrate: 0 open, 1 closed (induced fit).
    let close: Double
    let dark: Bool

    private func colour(_ pair: BioHexPair) -> Color { Color(hex: pair.hex(dark: dark)) }

    var body: some View {
        Canvas { ctx, size in
            let R = Double(min(size.width * 0.27, size.height * 0.4))
            let cx = Double(size.width) * 0.6, cy = Double(size.height) * 0.52
            let enzyme = colour(EnzymeColors.enzyme), substrate = colour(EnzymeColors.substrate), inhibitor = colour(EnzymeColors.inhibitor)
            let ink = dark ? Color.white : Color.black

            func pt(_ theta: Double, _ r: Double, dx: Double = 0, dy: Double = 0) -> CGPoint {
                CGPoint(x: cx + r * cos(theta) + dx, y: cy + r * sin(theta) + dy)
            }
            func label(_ s: String, _ p: CGPoint, _ anchor: UnitPoint = .center) {
                ctx.draw(Text(s).font(.system(size: 11, weight: .semibold)).foregroundColor(ink.opacity(0.75)), at: p, anchor: anchor)
            }
            // (the substrate: a wedge shaped to the pocket's floor, its outer end square)
            let open = site == .bound ? close : 0
            let depth = R * 0.52 * (site == .noncompetitive ? 0.45 : 1)
            func wedge(dx: Double, dy: Double, notch: Bool) -> Path {
                let a = 0.19, inner = R * 0.48 + 1.5, outer = R * 1.16
                var path = Path()
                let steps = 12
                for i in 0...steps {
                    let t = Double.pi - a + 2 * a * Double(i) / Double(steps)
                    let p = pt(t, inner, dx: dx, dy: dy)
                    if i == 0 { path.move(to: p) } else { path.addLine(to: p) }
                }
                path.addLine(to: pt(.pi + a, outer, dx: dx, dy: dy))
                if notch { path.addLine(to: pt(.pi, outer - R * 0.16, dx: dx, dy: dy)) }
                path.addLine(to: pt(.pi - a, outer, dx: dx, dy: dy))
                path.closeSubpath()
                return path
            }

            if site == .denatured {
                // (unfolded: a loose chain, no pocket left)
                var chain = Path()
                for i in 0...120 {
                    let t = Double(i) / 120
                    let p = CGPoint(x: cx - 1.3 * R + 2.6 * R * t, y: cy + R * (0.3 * sin(9 * t + 0.5) + 0.14 * sin(23 * t + 1.2)))
                    if i == 0 { chain.move(to: p) } else { chain.addLine(to: p) }
                }
                ctx.stroke(chain, with: .color(enzyme), style: StrokeStyle(lineWidth: max(R * 0.09, 3), lineCap: .round, lineJoin: .round))
                label("Unfolded enzyme", CGPoint(x: cx, y: cy - R * 0.62))
                let s = wedge(dx: -R * 0.95, dy: -R * 0.55, notch: false)
                ctx.fill(s, with: .color(substrate.opacity(0.85)))
                label("Substrate", CGPoint(x: cx - R * 2.0, y: cy - R * 0.95), .leading)
                return
            }

            // (the enzyme's outline: lumpy, a pocket on the left, a small allosteric pocket on the right)
            func plateau(_ a: Double) -> Double { a < 0.55 ? 1 : (a < 1 ? 0.5 * (1 + cos(Double.pi * (a - 0.55) / 0.45)) : 0) }
            func diff(_ a: Double, _ b: Double) -> Double { atan2(sin(a - b), cos(a - b)) }
            func radius(_ t: Double) -> Double {
                var r = R * (1 + 0.035 * sin(3 * t + 0.4) + 0.025 * cos(5 * t))
                let alpha = 0.46 - 0.06 * open
                let u = diff(t, .pi) / alpha
                r -= depth * plateau(abs(u))
                r += open * R * 0.09 * exp(-pow((abs(u) - 1.15) / 0.18, 2))
                if site == .noncompetitive {
                    // (the inhibitor bound on the right bends the active site out of shape)
                    r += R * 0.16 * exp(-pow((u + 0.3) / 0.28, 2))
                    r -= R * 0.17 * plateau(abs(diff(t, 0)) / 0.28)
                } else {
                    r -= R * 0.1 * plateau(abs(diff(t, 0)) / 0.24)
                }
                r += strain * R * 0.06 * sin(7 * t)
                return r
            }
            var outline = Path()
            for i in 0...240 {
                let t = 2 * Double.pi * Double(i) / 240
                let p = pt(t, radius(t))
                if i == 0 { outline.move(to: p) } else { outline.addLine(to: p) }
            }
            outline.closeSubpath()
            ctx.fill(outline, with: .color(enzyme.opacity(dark ? 0.55 : 0.8)))
            ctx.stroke(outline, with: .color(enzyme), lineWidth: 1.5)
            label("Enzyme", CGPoint(x: cx + R * 0.15, y: cy - R * 1.14))
            label("Active site", CGPoint(x: cx - R * 0.85, y: cy + R * 0.86))

            switch site {
            case .competitive:
                let i = wedge(dx: 0, dy: 0, notch: true)
                ctx.fill(i, with: .color(inhibitor.opacity(0.9)))
                ctx.stroke(i, with: .color(inhibitor), lineWidth: 1)
                label("Inhibitor", CGPoint(x: cx - R * 1.45, y: cy + R * 0.36), .trailing)
                let s = wedge(dx: -R * 0.95, dy: -R * 0.62, notch: false)
                ctx.fill(s, with: .color(substrate.opacity(0.85)))
                label("Substrate", CGPoint(x: cx - R * 2.0, y: cy - R * 1.02), .leading)
            case .noncompetitive:
                let r = R * 0.15
                let i = Path(ellipseIn: CGRect(x: cx + R * 0.9 - r, y: cy - r, width: 2 * r, height: 2 * r))
                ctx.fill(i, with: .color(inhibitor.opacity(0.9)))
                label("Inhibitor", CGPoint(x: cx + R * 0.95, y: cy - R * 0.36))
                label("Allosteric site", CGPoint(x: cx + R * 0.7, y: cy + R * 0.42))
                let s = wedge(dx: -R * 0.95, dy: -R * 0.62, notch: false)
                ctx.fill(s, with: .color(substrate.opacity(0.85)))
                label("Substrate", CGPoint(x: cx - R * 2.0, y: cy - R * 1.02), .leading)
            default:
                let away = 1 - approach
                let s = wedge(dx: -R * 1.0 * away, dy: -R * 0.5 * away, notch: false)
                ctx.fill(s, with: .color(substrate.opacity(0.9)))
                ctx.stroke(s, with: .color(substrate), lineWidth: 1)
                label("Substrate", CGPoint(x: cx - R * 1.45 - R * away, y: cy - R * 0.36 - R * 0.5 * away), .trailing)
            }
        }
        .accessibilityHidden(true)
    }
}

/// The activation-energy diagram: the same reactants and products (ΔG unchanged), a lower barrier with the enzyme.
private struct EnzymeEnergyCard: View {
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Activation energy").font(.sHeadline).accessibilityAddTraits(.isHeader)
            EnzymeEnergyDiagram(dark: scheme == .dark)
                .frame(height: 180)
            Text("With the enzyme the barrier is lower (Ea 40 instead of 75), so more molecules get over it each second. The products end at the same level: ΔG is unchanged.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 16)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Energy diagram: the enzyme lowers the activation energy from 75 to 40; delta G is unchanged at minus 30. Illustrative units.")
    }
}

private struct EnzymeEnergyDiagram: View {
    let dark: Bool

    var body: some View {
        Canvas { ctx, size in
            let ink = dark ? Color.white : Color.black
            let tint = Color(hex: EnzymeColors.enzyme.hex(dark: dark))
            let left = 16.0, right = Double(size.width) - 8, top = 14.0, bottom = Double(size.height) - 18
            func X(_ x: Double) -> Double { left + (right - left) * x }
            func Y(_ e: Double) -> Double { top + (bottom - top) * (88 - e) / 128 }
            func P(_ x: Double, _ e: Double) -> CGPoint { CGPoint(x: X(x), y: Y(e)) }
            func text(_ s: String, _ p: CGPoint, _ anchor: UnitPoint, _ colour: Color? = nil) {
                ctx.draw(Text(s).font(.system(size: 10.5, weight: .semibold)).foregroundColor(colour ?? ink.opacity(0.7)), at: p, anchor: anchor)
            }
            func arrow(_ x: Double, from a: Double, to b: Double, _ colour: Color) {
                var line = Path()
                line.move(to: P(x, a))
                line.addLine(to: P(x, b))
                ctx.stroke(line, with: .color(colour), lineWidth: 1.4)
                let dir = b > a ? -1.0 : 1.0
                var head = Path()
                head.move(to: P(x, b))
                head.addLine(to: CGPoint(x: X(x) - 4, y: Y(b) + 6 * dir))
                head.addLine(to: CGPoint(x: X(x) + 4, y: Y(b) + 6 * dir))
                head.closeSubpath()
                ctx.fill(head, with: .color(colour))
            }
            // (axes)
            var axes = Path()
            axes.move(to: CGPoint(x: left, y: top))
            axes.addLine(to: CGPoint(x: left, y: bottom))
            axes.addLine(to: CGPoint(x: right, y: bottom))
            ctx.stroke(axes, with: .color(ink.opacity(0.35)), lineWidth: 1)
            text("Energy", CGPoint(x: left + 4, y: top - 4), .topLeading)
            text("Progress of the reaction →", CGPoint(x: right, y: bottom + 3), .topTrailing)
            // (the reactants' level, across)
            var level = Path()
            level.move(to: P(0.02, 0))
            level.addLine(to: P(0.98, 0))
            ctx.stroke(level, with: .color(ink.opacity(0.18)), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))
            // (the two paths)
            func curve(_ ea: Double) -> Path {
                var p = Path()
                for (i, pt) in EnzymeModel.energyCurve(ea: ea).enumerated() {
                    let q = P(0.02 + 0.96 * pt.x, pt.y)
                    if i == 0 { p.move(to: q) } else { p.addLine(to: q) }
                }
                return p
            }
            ctx.stroke(curve(EnzymeModel.eaUncatalysed), with: .color(ink.opacity(0.45)), style: StrokeStyle(lineWidth: 1.8, dash: [5, 4]))
            ctx.stroke(curve(EnzymeModel.eaCatalysed), with: .color(tint), style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
            // (the arrows: Ea without and with, and ΔG)
            arrow(0.47, from: 0, to: EnzymeModel.eaUncatalysed, ink.opacity(0.55))
            arrow(0.53, from: 0, to: EnzymeModel.eaCatalysed, tint)
            arrow(0.94, from: 0, to: EnzymeModel.deltaG, ink.opacity(0.7))
            text("Ea without enzyme", CGPoint(x: X(0.45), y: Y(EnzymeModel.eaUncatalysed - 6)), .trailing)
            text("Ea with enzyme", CGPoint(x: X(0.55), y: Y(EnzymeModel.eaCatalysed - 10)), .leading, tint)
            text("ΔG unchanged", CGPoint(x: X(0.92), y: Y(EnzymeModel.deltaG / 2)), .trailing)
            text("Reactants", CGPoint(x: X(0.03), y: Y(0) - 3), .bottomLeading)
            text("Products", CGPoint(x: X(0.8), y: Y(EnzymeModel.deltaG) + 3), .top)
        }
        .accessibilityHidden(true)
    }
}
