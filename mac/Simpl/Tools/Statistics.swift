import SwiftUI

// (tools) Statistics, a maths tool: four pages under one heading — Describe (a data set's n, mean, median, modes,
// quartiles, spreads and outliers, with a histogram and a box plot), Regression (two columns, the least-squares line,
// r and r², a scatter with the line, a prediction), Distributions (the normal curve's probabilities, z and inverse;
// Student's t and its p) and Tests (one-sample, Welch's two-sample and paired t-tests with their 95% intervals). The
// arithmetic is Stats' (Model/StatsModel.swift, checked by the Models job); every number shown is StudyMath.text's.
// The page and what is typed on each are kept (StudyPrefs).

/// Statistics' four pages.
enum StatsTab: String, CaseIterable, Identifiable {
    case describe, regression, dist, tests

    var id: String { rawValue }

    var title: String {
        switch self {
        case .describe: return "Describe"
        case .regression: return "Regression"
        case .dist: return "Distributions"
        case .tests: return "Tests"
        }
    }
}

/// What the launch asked of Statistics (-SimplTool stats -SimplStats <tab> -SimplStatsData "…"): read only when the
/// launch opened it.
enum StatsLaunch {
    static var here: Bool { ChemTools.launched == .stats }
    static let tab: StatsTab? = here ? UserDefaults.standard.string(forKey: "SimplStats").flatMap { StatsTab(rawValue: $0) } : nil
    static let data: String? = here ? UserDefaults.standard.string(forKey: "SimplStatsData") : nil

    /// The launch's data for a page, when that page is the one launched (else nil).
    static func data(for t: StatsTab) -> String? { tab == t || (tab == nil && t == .describe) ? data : nil }
}

/// A number shown, and a p-value ("0.227", "< 0.001").
enum StatsText {
    static func v(_ x: Double) -> String { StudyMath.text(x) }
    static func p(_ x: Double) -> String { x.isNaN ? "—" : (x < 0.001 ? "< 0.001" : StudyMath.fixed(x, 3)) }
}

struct StatisticsTool: View {
    @State private var tab: StatsTab = StatsLaunch.tab ?? StudyPrefs.string("stats", "tab").flatMap { StatsTab(rawValue: $0) } ?? .describe
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ToolPage {
            Picker("Page", selection: $tab) {
                ForEach(StatsTab.allCases) { t in Text(t.title).tag(t) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .controlSize(.large)
            .frame(maxWidth: 560, alignment: .leading)
            Group {
                switch tab {
                case .describe: StatsDescribePage()
                case .regression: StatsRegressionPage()
                case .dist: StatsDistributionsPage()
                case .tests: StatsTestsPage()
                }
            }
            .transition(.opacity)
        }
        .animation(reduceMotion ? nil : Motion.gentle, value: tab)
        .onChange(of: tab) { _, t in StudyPrefs.set("stats", "tab", t.rawValue) }
        .onAppear {
            // (1.3.83) the pin's data, its panel opened full screen: the Describe page on it
            if let d = ToolsCenter.shared.takeInfo("data"), !d.isEmpty {
                StudyPrefs.set("stats", "data", d)
                tab = .describe
            }
        }
    }
}

// MARK: - Shared pieces

/// A column of numbers typed or pasted: a small text area with its label and a hint.
struct StatsDataField: View {
    let label: String
    @Binding var text: String
    var hint = "Numbers set apart by spaces, commas or new lines — a column pasted from a spreadsheet works."
    var height: CGFloat = 120

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(.system(size: 14, design: .monospaced))
                .scrollContentBackground(.hidden)
                .padding(8)
                .frame(height: height)
                .background(Theme.well, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                .accessibilityLabel(label)
            Text(hint).font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }
}

/// A result's row: its name, and its value in figures.
struct StatsRow: View {
    let label: String
    let value: String
    var note: String? = nil

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 1) {
                Text(label).font(.sBody)
                if let note { Text(note).font(.sCaption).foregroundStyle(.secondary) }
            }
            Spacer(minLength: 12)
            Text(value)
                .font(.sBody.weight(.semibold).monospacedDigit())
                .textSelection(.enabled)
                .multilineTextAlignment(.trailing)
        }
        .padding(.vertical, 7)
    }
}

/// The rows of results in a card, with Copy under them.
struct StatsResults: View {
    let title: String
    let rows: [(String, String, String?)]
    var footnote: String? = nil
    @State private var copied = false

    var body: some View {
        PageSection(title: title) {
            VStack(alignment: .leading, spacing: 10) {
                VStack(spacing: 0) {
                    ForEach(Array(rows.enumerated()), id: \.offset) { i, r in
                        if i > 0 { RowDivider(inset: 0) }
                        StatsRow(label: r.0, value: r.1, note: r.2)
                    }
                }
                .padding(.vertical, 4)
                .padding(.horizontal, 14)
                .card()
                if let footnote {
                    Text(footnote).font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                Button {
                    copyToPasteboard(rows.map { "\($0.0): \($0.1.replacingOccurrences(of: "−", with: "-"))" }.joined(separator: "\n"))
                    withAnimation(Motion.snappy) { copied = true }
                } label: {
                    Label(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc").font(.sCallout)
                }
                .glassButton()
                .disabled(rows.isEmpty)
                .help("Copy the results as lines of text")
            }
        }
        .onChange(of: rows.map(\.1)) { _, _ in copied = false }
    }
}

/// A drawing's frame: an axis line along the foot and a label at each end.
enum StatsDraw {
    static let accent = ToolKind.stats.color
    static let inset: CGFloat = 8

    /// A value's place across a width, from `lo` at the left to `hi` at the right.
    static func across(_ v: Double, lo: Double, hi: Double, width: CGFloat) -> CGFloat {
        guard hi > lo else { return width / 2 }
        return inset + CGFloat((v - lo) / (hi - lo)) * (width - 2 * inset)
    }

    static func axis(_ ctx: inout GraphicsContext, size: CGSize, lo: Double, hi: Double, baseline: CGFloat) {
        var line = Path()
        line.move(to: CGPoint(x: inset, y: baseline))
        line.addLine(to: CGPoint(x: size.width - inset, y: baseline))
        ctx.stroke(line, with: .color(.secondary.opacity(0.5)), lineWidth: 1)
        ctx.draw(Text(StudyMath.text(lo, sig: 4)).font(.sCaption2).foregroundStyle(.secondary), at: CGPoint(x: inset, y: baseline + 4), anchor: .topLeading)
        ctx.draw(Text(StudyMath.text(hi, sig: 4)).font(.sCaption2).foregroundStyle(.secondary), at: CGPoint(x: size.width - inset, y: baseline + 4), anchor: .topTrailing)
    }
}

/// The histogram: a bar a bin, its count over it.
struct HistogramChart: View {
    let bins: [Stats.Bin]

    var body: some View {
        Canvas { ctx, size in
            guard let lo = bins.first?.lower, let hi = bins.last?.upper, let top = bins.map(\.count).max(), top > 0 else { return }
            let baseline = size.height - 18
            let plotTop: CGFloat = 16
            let span = hi > lo ? hi - lo : 1
            for b in bins {
                let x0 = StatsDraw.across(b.lower, lo: lo, hi: lo + span, width: size.width)
                let x1 = StatsDraw.across(b.upper == b.lower ? b.lower + span : b.upper, lo: lo, hi: lo + span, width: size.width)
                let h = CGFloat(Double(b.count) / Double(top)) * (baseline - plotTop)
                let rect = CGRect(x: x0 + 1, y: baseline - h, width: max(x1 - x0 - 2, 2), height: h)
                ctx.fill(Path(roundedRect: rect, cornerRadius: 3), with: .color(StatsDraw.accent.opacity(0.75)))
                if b.count > 0 {
                    ctx.draw(Text("\(b.count)").font(.sCaption2.monospacedDigit()).foregroundStyle(.secondary), at: CGPoint(x: rect.midX, y: rect.minY - 2), anchor: .bottom)
                }
            }
            StatsDraw.axis(&ctx, size: size, lo: lo, hi: hi, baseline: baseline)
        }
        .accessibilityLabel("Histogram: \(bins.map { "\($0.count) from \(StudyMath.text($0.lower, sig: 4)) to \(StudyMath.text($0.upper, sig: 4))" }.joined(separator: ", "))")
    }
}

/// The box plot: the box from Q1 to Q3 with the median across it, whiskers to the last values within 1.5 IQR, and the
/// outliers as dots.
struct BoxPlotChart: View {
    let d: Stats.Description

    var body: some View {
        Canvas { ctx, size in
            let lo = d.min, hi = d.max
            let mid = size.height / 2 - 6
            let half: CGFloat = min(22, size.height / 3)
            let inside = d.sorted.filter { !d.outliers.contains($0) }
            let wLo = inside.first ?? d.min, wHi = inside.last ?? d.max
            func x(_ v: Double) -> CGFloat { StatsDraw.across(v, lo: lo, hi: hi, width: size.width) }
            var whisk = Path()
            whisk.move(to: CGPoint(x: x(wLo), y: mid)); whisk.addLine(to: CGPoint(x: x(d.q1), y: mid))
            whisk.move(to: CGPoint(x: x(d.q3), y: mid)); whisk.addLine(to: CGPoint(x: x(wHi), y: mid))
            whisk.move(to: CGPoint(x: x(wLo), y: mid - half / 2)); whisk.addLine(to: CGPoint(x: x(wLo), y: mid + half / 2))
            whisk.move(to: CGPoint(x: x(wHi), y: mid - half / 2)); whisk.addLine(to: CGPoint(x: x(wHi), y: mid + half / 2))
            ctx.stroke(whisk, with: .color(.secondary), lineWidth: 1.5)
            let box = CGRect(x: x(d.q1), y: mid - half, width: max(x(d.q3) - x(d.q1), 2), height: half * 2)
            ctx.fill(Path(roundedRect: box, cornerRadius: 4), with: .color(StatsDraw.accent.opacity(0.3)))
            ctx.stroke(Path(roundedRect: box, cornerRadius: 4), with: .color(StatsDraw.accent), lineWidth: 1.5)
            var med = Path()
            med.move(to: CGPoint(x: x(d.median), y: mid - half)); med.addLine(to: CGPoint(x: x(d.median), y: mid + half))
            ctx.stroke(med, with: .color(StatsDraw.accent), lineWidth: 2.5)
            for o in d.outliers {
                ctx.fill(Path(ellipseIn: CGRect(x: x(o) - 3.5, y: mid - 3.5, width: 7, height: 7)), with: .color(.orange))
            }
            StatsDraw.axis(&ctx, size: size, lo: lo, hi: hi, baseline: size.height - 18)
        }
        .accessibilityLabel("Box plot: Q1 \(StudyMath.text(d.q1)), median \(StudyMath.text(d.median)), Q3 \(StudyMath.text(d.q3))")
    }
}

/// The scatter of the points with the regression line through them.
struct ScatterChart: View {
    let x: [Double]
    let y: [Double]
    let line: Stats.Regression?

    var body: some View {
        Canvas { ctx, size in
            guard let xLo = x.min(), let xHi = x.max(), let yLo = y.min(), let yHi = y.max(), x.count == y.count else { return }
            let baseline = size.height - 18
            let plotTop: CGFloat = 8
            let ySpan = yHi > yLo ? yHi - yLo : 1
            func px(_ v: Double) -> CGFloat { StatsDraw.across(v, lo: xLo, hi: xHi, width: size.width) }
            func py(_ v: Double) -> CGFloat { baseline - CGFloat((v - yLo) / ySpan) * (baseline - plotTop) }
            if let line, xHi > xLo {
                var p = Path()
                p.move(to: CGPoint(x: px(xLo), y: py(line.predict(xLo))))
                p.addLine(to: CGPoint(x: px(xHi), y: py(line.predict(xHi))))
                ctx.stroke(p, with: .color(StatsDraw.accent), lineWidth: 2)
            }
            for i in 0..<x.count {
                ctx.fill(Path(ellipseIn: CGRect(x: px(x[i]) - 4, y: py(y[i]) - 4, width: 8, height: 8)), with: .color(.primary.opacity(0.75)))
            }
            StatsDraw.axis(&ctx, size: size, lo: xLo, hi: xHi, baseline: baseline)
            ctx.draw(Text(StudyMath.text(yHi, sig: 4)).font(.sCaption2).foregroundStyle(.secondary), at: CGPoint(x: StatsDraw.inset, y: plotTop), anchor: .topLeading)
        }
        .accessibilityLabel("Scatter of \(toolPlural(x.count, "point")) with the regression line")
    }
}

/// A bell curve with an area under it shaded: the normal's from `from` to `to`, or Student's t's.
struct CurveChart: View {
    let lo: Double
    let hi: Double
    let density: (Double) -> Double
    /// The parts shaded, each from one x to another.
    let shaded: [(Double, Double)]
    var marks: [Double] = []

    var body: some View {
        Canvas { ctx, size in
            guard hi > lo else { return }
            let baseline = size.height - 18
            let plotTop: CGFloat = 8
            let n = 240
            let xs = (0...n).map { lo + (hi - lo) * Double($0) / Double(n) }
            let ys = xs.map(density)
            guard let top = ys.max(), top > 0 else { return }
            func px(_ v: Double) -> CGFloat { StatsDraw.across(v, lo: lo, hi: hi, width: size.width) }
            func py(_ v: Double) -> CGFloat { baseline - CGFloat(v / top) * (baseline - plotTop) }
            for (a, b) in shaded where b > a {
                var area = Path()
                let a0 = max(a, lo), b0 = min(b, hi)
                guard b0 > a0 else { continue }
                area.move(to: CGPoint(x: px(a0), y: baseline))
                let m = 120
                for k in 0...m {
                    let v = a0 + (b0 - a0) * Double(k) / Double(m)
                    area.addLine(to: CGPoint(x: px(v), y: py(density(v))))
                }
                area.addLine(to: CGPoint(x: px(b0), y: baseline))
                area.closeSubpath()
                ctx.fill(area, with: .color(StatsDraw.accent.opacity(0.35)))
            }
            var curve = Path()
            for (i, v) in xs.enumerated() {
                let pt = CGPoint(x: px(v), y: py(ys[i]))
                if i == 0 { curve.move(to: pt) } else { curve.addLine(to: pt) }
            }
            ctx.stroke(curve, with: .color(StatsDraw.accent), lineWidth: 2)
            for m in marks where m >= lo && m <= hi {
                var tick = Path()
                tick.move(to: CGPoint(x: px(m), y: py(density(m))))
                tick.addLine(to: CGPoint(x: px(m), y: baseline))
                ctx.stroke(tick, with: .color(.secondary), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
                ctx.draw(Text(StudyMath.text(m, sig: 4)).font(.sCaption2).foregroundStyle(.secondary), at: CGPoint(x: px(m), y: baseline + 4), anchor: .top)
            }
            StatsDraw.axis(&ctx, size: size, lo: lo, hi: hi, baseline: baseline)
        }
    }
}

// MARK: - Describe

struct StatsDescribePage: View {
    @State private var data = StatsLaunch.data(for: .describe) ?? StudyPrefs.string("stats", "data") ?? ""

    private var numbers: [Double] { StudyMath.numbers(data) }

    var body: some View {
        let d = Stats.describe(numbers)
        ToolColumns(sideWidth: 400, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                StatsDataField(label: "Data", text: $data, height: 140)
                HStack(spacing: 10) {
                    Button {
                        if let s = NSPasteboard.general.string(forType: .string) { data = s }
                    } label: {
                        Label("Paste", systemImage: "doc.on.clipboard").font(.sCallout)
                    }
                    .glassButton()
                    Button("Example") { data = "72 85 90 66 78 95 88 70 81 77 84 69 91 73 86 79 83 58 88 76" }
                        .glassButton()
                        .help("A class's exam scores")
                    Button("Clear") { data = "" }
                        .glassButton()
                        .disabled(data.isEmpty)
                    Spacer()
                    if let d {
                        Text(toolPlural(d.n, "value")).font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                    }
                }
                if let d {
                    PageSection(title: "Histogram", trailing: "bins by Sturges' rule") {
                        HistogramChart(bins: Stats.histogram(d.sorted))
                            .frame(height: 170)
                            .padding(10)
                            .card()
                    }
                    PageSection(title: "Box plot", trailing: d.outliers.isEmpty ? nil : "outliers in orange") {
                        BoxPlotChart(d: d)
                            .frame(height: 110)
                            .padding(10)
                            .card()
                    }
                } else if !data.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    ToolNote(text: "No numbers found — set them apart by spaces, commas or new lines.")
                }
            }
        } side: {
            if let d {
                StatsResults(title: "Statistics", rows: rows(d),
                             footnote: "Quartiles are the medians of the lower and upper halves of the sorted data, the median itself left out when n is odd. Outliers lie more than 1.5 IQR outside the quartiles.")
            } else {
                PageSection(title: "Statistics") {
                    Text("Type or paste numbers to see their mean, median, spread and more.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
        .onChange(of: data) { _, t in StudyPrefs.set("stats", "data", t.count <= 100_000 ? t : nil) }
    }

    private func rows(_ d: Stats.Description) -> [(String, String, String?)] {
        let v = StatsText.v
        var r: [(String, String, String?)] = [
            ("n", "\(d.n)", nil), ("Mean", v(d.mean), nil), ("Median", v(d.median), nil),
            ("Mode", d.modes.isEmpty ? "none" : d.modes.map { v($0) }.joined(separator: ", "), nil),
            ("Min", v(d.min), nil), ("Max", v(d.max), nil), ("Range", v(d.range), nil),
            ("Q1", v(d.q1), nil), ("Q3", v(d.q3), nil), ("IQR", v(d.iqr), nil),
            ("Sample SD", v(d.sampleSD), "s, over n − 1"), ("Population SD", v(d.populationSD), "σ, over n"),
            ("Sample variance", v(d.sampleVariance), "s²"), ("Population variance", v(d.populationVariance), "σ²"),
            ("SE of the mean", v(d.standardError), "s ⁄ √n"), ("Sum", v(d.sum), nil), ("Sum of squares", v(d.sumOfSquares), "Σx²"),
        ]
        r.append(("Outliers", d.outliers.isEmpty ? "none" : d.outliers.map { v($0) }.joined(separator: ", "), "past 1.5 IQR"))
        return r
    }
}

// MARK: - Regression

struct StatsRegressionPage: View {
    @State private var xText: String
    @State private var yText: String
    @State private var predict = StudyPrefs.string("stats", "predict") ?? ""

    init() {
        // (the launch's columns: "1 2 3 4 5; 2 4 5 4 5", x then y)
        if let d = StatsLaunch.data(for: .regression) {
            let parts = d.components(separatedBy: ";")
            _xText = State(initialValue: parts.first ?? "")
            _yText = State(initialValue: parts.count > 1 ? parts[1] : "")
        } else {
            _xText = State(initialValue: StudyPrefs.string("stats", "x") ?? "")
            _yText = State(initialValue: StudyPrefs.string("stats", "y") ?? "")
        }
    }

    private var xs: [Double] { StudyMath.numbers(xText) }
    private var ys: [Double] { StudyMath.numbers(yText) }

    var body: some View {
        let x = xs, y = ys
        let line = Stats.regression(x: x, y: y)
        ToolColumns(sideWidth: 400, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                HStack(alignment: .top, spacing: 16) {
                    StatsDataField(label: "x", text: $xText, hint: "One column, in order.", height: 140)
                    StatsDataField(label: "y", text: $yText, hint: "The same number of values, in the same order.", height: 140)
                }
                HStack(spacing: 10) {
                    Button("Example") {
                        xText = "1 2 3 4 5"
                        yText = "2 4 5 4 5"
                    }
                    .glassButton()
                    Button("Clear") {
                        xText = ""
                        yText = ""
                    }
                    .glassButton()
                    .disabled(xText.isEmpty && yText.isEmpty)
                    Spacer()
                    Text(toolPlural(min(x.count, y.count), "point")).font(.sCallout.monospacedDigit()).foregroundStyle(.secondary)
                }
                if !x.isEmpty, x.count != y.count {
                    ToolNote(text: "x has \(toolPlural(x.count, "value")) and y has \(y.count) — they must match, one y for each x.")
                } else if x.count >= 2, line == nil {
                    ToolNote(text: "Every x is the same, so no line can be fitted.")
                }
                if let line {
                    PageSection(title: "Scatter") {
                        ScatterChart(x: x, y: y, line: line)
                            .frame(height: 220)
                            .padding(10)
                            .card()
                    }
                }
            }
        } side: {
            if let line {
                StatsResults(title: "Line of best fit", rows: rows(line), footnote: "ŷ = slope · x + intercept, fitted by least squares.")
                PageSection(title: "Predict") {
                    VStack(alignment: .leading, spacing: 10) {
                        HStack(spacing: 8) {
                            Text("x =").font(.sBody).foregroundStyle(.secondary)
                            TextField("6", text: $predict)
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody)
                                .controlSize(.large)
                                .frame(width: 110)
                                .accessibilityLabel("x to predict y from")
                            if let px = StudyMath.number(predict) {
                                Text("ŷ = \(StatsText.v(line.predict(px)))")
                                    .font(.sBody.weight(.semibold).monospacedDigit())
                                    .textSelection(.enabled)
                            }
                        }
                    }
                    .padding(14)
                    .card()
                }
            } else {
                PageSection(title: "Line of best fit") {
                    Text("Two columns of the same length, and the line through them appears here.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
        .onChange(of: xText) { _, t in StudyPrefs.set("stats", "x", t) }
        .onChange(of: yText) { _, t in StudyPrefs.set("stats", "y", t) }
        .onChange(of: predict) { _, t in StudyPrefs.set("stats", "predict", t) }
    }

    private func rows(_ l: Stats.Regression) -> [(String, String, String?)] {
        let v = StatsText.v
        let sign = l.intercept < 0 ? "−" : "+"
        return [
            ("Equation", "ŷ = \(v(l.slope))x \(sign) \(v(abs(l.intercept)))", nil),
            ("Slope", v(l.slope), "b"), ("Intercept", v(l.intercept), "a"),
            ("r", v(l.r), "correlation"), ("r²", v(l.rSquared), "share of y's variation explained"),
            ("n", "\(l.n)", nil), ("Mean of x", v(l.meanX), nil), ("Mean of y", v(l.meanY), nil),
        ]
    }
}

// MARK: - Distributions

struct StatsDistributionsPage: View {
    enum Kind: String, CaseIterable, Identifiable {
        case normal, t
        var id: String { rawValue }
        var title: String { self == .normal ? "Normal" : "Student's t" }
    }

    @State private var kind: Kind = StudyPrefs.string("stats", "dist").flatMap { Kind(rawValue: $0) } ?? .normal
    @State private var mu = StudyPrefs.string("stats", "mu") ?? "100"
    @State private var sigma = StudyPrefs.string("stats", "sigma") ?? "15"
    @State private var x = StudyPrefs.string("stats", "xValue") ?? "115"
    @State private var a = StudyPrefs.string("stats", "a") ?? "85"
    @State private var b = StudyPrefs.string("stats", "b") ?? "115"
    @State private var p = StudyPrefs.string("stats", "p") ?? "0.95"
    @State private var df = StudyPrefs.string("stats", "df") ?? "10"
    @State private var t = StudyPrefs.string("stats", "t") ?? "2.228"
    @State private var tp = StudyPrefs.string("stats", "tp") ?? "0.05"

    var body: some View {
        ToolColumns(sideWidth: 400, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                Picker("Distribution", selection: $kind) {
                    ForEach(Kind.allCases) { k in Text(k.title).tag(k) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(maxWidth: 300, alignment: .leading)
                if kind == .normal { normalFields } else { tFields }
                curve
            }
        } side: {
            if kind == .normal { normalResults } else { tResults }
        }
        .onChange(of: kind) { _, k in StudyPrefs.set("stats", "dist", k.rawValue) }
        .onChange(of: mu) { _, v in StudyPrefs.set("stats", "mu", v) }
        .onChange(of: sigma) { _, v in StudyPrefs.set("stats", "sigma", v) }
        .onChange(of: x) { _, v in StudyPrefs.set("stats", "xValue", v) }
        .onChange(of: a) { _, v in StudyPrefs.set("stats", "a", v) }
        .onChange(of: b) { _, v in StudyPrefs.set("stats", "b", v) }
        .onChange(of: p) { _, v in StudyPrefs.set("stats", "p", v) }
        .onChange(of: df) { _, v in StudyPrefs.set("stats", "df", v) }
        .onChange(of: t) { _, v in StudyPrefs.set("stats", "t", v) }
        .onChange(of: tp) { _, v in StudyPrefs.set("stats", "tp", v) }
    }

    private var muV: Double? { StudyMath.number(mu) }
    private var sigmaV: Double? { StudyMath.number(sigma).flatMap { $0 > 0 ? $0 : nil } }
    private var dfV: Double? { StudyMath.number(df).flatMap { $0 > 0 ? $0 : nil } }

    private var normalFields: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                ToolField(label: "Mean μ", text: $mu, placeholder: "100", width: 120)
                ToolField(label: "SD σ", text: $sigma, placeholder: "15", width: 120)
                ToolField(label: "x", text: $x, placeholder: "115", width: 120)
            }
            HStack(alignment: .top, spacing: 12) {
                ToolField(label: "a", text: $a, placeholder: "85", width: 120)
                ToolField(label: "b", text: $b, placeholder: "115", width: 120)
                ToolField(label: "Inverse: P(X < x) =", text: $p, placeholder: "0.95", width: 120)
            }
            if sigmaV == nil { ToolNote(text: "σ must be a number above zero.") }
        }
    }

    private var tFields: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                ToolField(label: "Degrees of freedom", text: $df, placeholder: "10", width: 150)
                ToolField(label: "t", text: $t, placeholder: "2.228", width: 120)
                ToolField(label: "Inverse: two-tailed p =", text: $tp, placeholder: "0.05", width: 150)
            }
            if dfV == nil { ToolNote(text: "The degrees of freedom must be a number above zero.") }
        }
    }

    @ViewBuilder
    private var curve: some View {
        if kind == .normal, let m = muV, let s = sigmaV {
            let xv = StudyMath.number(x)
            PageSection(title: "The curve", trailing: xv.map { "P(X < \(StatsText.v($0))) shaded" }) {
                CurveChart(lo: m - 4 * s, hi: m + 4 * s, density: { Stats.normalPDF($0, mean: m, sd: s) },
                           shaded: xv.map { [(m - 4 * s, $0)] } ?? [], marks: [m] + (xv.map { [$0] } ?? []))
                    .frame(height: 200)
                    .padding(10)
                    .card()
            }
        } else if kind == .t, let d = dfV {
            let tv = StudyMath.number(t)
            PageSection(title: "The curve", trailing: tv.map { "both tails past |\(StatsText.v($0))| shaded" }) {
                CurveChart(lo: -5, hi: 5, density: { Stats.tPDF($0, df: d) },
                           shaded: tv.map { [(-5, -abs($0)), (abs($0), 5)] } ?? [], marks: tv.map { [-abs($0), abs($0)] } ?? [])
                    .frame(height: 200)
                    .padding(10)
                    .card()
            }
        }
    }

    @ViewBuilder
    private var normalResults: some View {
        if let m = muV, let s = sigmaV {
            StatsResults(title: "Normal, μ \(StatsText.v(m)), σ \(StatsText.v(s))", rows: normalRows(m, s),
                         footnote: "By the error function; the inverse by Acklam's approximation refined to the last figure.")
        } else {
            PageSection(title: "Normal") {
                Text("A mean and a standard deviation above zero, and the probabilities appear here.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
    }

    private func normalRows(_ m: Double, _ s: Double) -> [(String, String, String?)] {
        let v = StatsText.v
        var rows: [(String, String, String?)] = []
        if let xv = StudyMath.number(x) {
            let cdf = Stats.normalCDF(xv, mean: m, sd: s)
            rows.append(("z-score of x", v((xv - m) / s), "(x − μ) ⁄ σ"))
            rows.append(("P(X < \(v(xv)))", StudyMath.fixed(cdf, 4), nil))
            rows.append(("P(X > \(v(xv)))", StudyMath.fixed(1 - cdf, 4), nil))
        }
        if let av = StudyMath.number(a), let bv = StudyMath.number(b) {
            let between = Stats.normalCDF(max(av, bv), mean: m, sd: s) - Stats.normalCDF(min(av, bv), mean: m, sd: s)
            rows.append(("P(\(v(av)) < X < \(v(bv)))", StudyMath.fixed(between, 4), nil))
        }
        if let pv = StudyMath.number(p) {
            if let inv = Stats.normalInverse(pv, mean: m, sd: s) {
                rows.append(("x with P(X < x) = \(v(pv))", v(inv), "z = \(v((inv - m) / s))"))
            } else {
                rows.append(("x with P(X < x) = \(v(pv))", "—", "p must be between 0 and 1"))
            }
        }
        return rows
    }

    @ViewBuilder
    private var tResults: some View {
        if let d = dfV {
            StatsResults(title: "Student's t, \(StatsText.v(d)) df", rows: tRows(d), footnote: "By the regularized incomplete beta function (its continued fraction).")
        } else {
            PageSection(title: "Student's t") {
                Text("Degrees of freedom above zero, and the p-values appear here.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
    }

    private func tRows(_ d: Double) -> [(String, String, String?)] {
        let v = StatsText.v
        var rows: [(String, String, String?)] = []
        if let tv = StudyMath.number(t) {
            let two = Stats.tTwoTailed(tv, df: d)
            rows.append(("Two-tailed p", StatsText.p(two), "P(|T| ≥ \(v(abs(tv))))"))
            rows.append(("One-tailed p", StatsText.p(two / 2), "P(T ≥ \(v(abs(tv))))"))
            rows.append(("P(T < \(v(tv)))", StudyMath.fixed(Stats.tCDF(tv, df: d), 4), nil))
        }
        if let pv = StudyMath.number(tp) {
            if let crit = Stats.tCritical(alpha: pv, df: d) {
                rows.append(("t for two-tailed p = \(v(pv))", "±\(v(crit))", "the critical t"))
                rows.append(("t for one-tailed p = \(v(pv))", v(Stats.tQuantile(1 - pv, df: d) ?? .nan), nil))
            } else {
                rows.append(("t for p = \(v(pv))", "—", "p must be between 0 and 1"))
            }
        }
        return rows
    }
}

// MARK: - Tests

struct StatsTestsPage: View {
    enum Kind: String, CaseIterable, Identifiable {
        case one, two, paired
        var id: String { rawValue }
        var title: String {
            switch self {
            case .one: return "One sample"
            case .two: return "Two samples (Welch)"
            case .paired: return "Paired"
            }
        }
    }

    @State private var kind: Kind = StudyPrefs.string("stats", "test").flatMap { Kind(rawValue: $0) } ?? .one
    @State private var summary = StudyPrefs.bool("stats", "testSummary", default: false)
    @State private var data1: String
    @State private var data2: String
    @State private var mu0 = StudyPrefs.string("stats", "mu0") ?? "0"
    @State private var n1 = StudyPrefs.string("stats", "n1") ?? ""
    @State private var m1 = StudyPrefs.string("stats", "m1") ?? ""
    @State private var sd1 = StudyPrefs.string("stats", "sd1") ?? ""
    @State private var n2 = StudyPrefs.string("stats", "n2") ?? ""
    @State private var m2 = StudyPrefs.string("stats", "m2") ?? ""
    @State private var sd2 = StudyPrefs.string("stats", "sd2") ?? ""

    init() {
        // (the launch's samples: "2 4 4 4 5 5 7 9; 3 5 6 6 7 8 8 10", the first then the second)
        if let d = StatsLaunch.data(for: .tests) {
            let parts = d.components(separatedBy: ";")
            _data1 = State(initialValue: parts.first ?? "")
            _data2 = State(initialValue: parts.count > 1 ? parts[1] : "")
        } else {
            _data1 = State(initialValue: StudyPrefs.string("stats", "sample1") ?? "")
            _data2 = State(initialValue: StudyPrefs.string("stats", "sample2") ?? "")
        }
    }

    private var sample1: Stats.Sample? {
        if summary && kind != .paired {
            guard let n = StudyMath.number(n1), let m = StudyMath.number(m1), let s = StudyMath.number(sd1) else { return nil }
            return Stats.Sample(n: Int(n.rounded()), mean: m, sd: s)
        }
        return Stats.Sample(StudyMath.numbers(data1))
    }

    private var sample2: Stats.Sample? {
        if summary {
            guard let n = StudyMath.number(n2), let m = StudyMath.number(m2), let s = StudyMath.number(sd2) else { return nil }
            return Stats.Sample(n: Int(n.rounded()), mean: m, sd: s)
        }
        return Stats.Sample(StudyMath.numbers(data2))
    }

    private var result: Stats.TTest? {
        switch kind {
        case .one:
            guard let s = sample1, let m = StudyMath.number(mu0) else { return nil }
            return Stats.oneSample(s, mu0: m)
        case .two:
            guard let a = sample1, let b = sample2 else { return nil }
            return Stats.welch(a, b)
        case .paired:
            return Stats.paired(StudyMath.numbers(data1), StudyMath.numbers(data2))
        }
    }

    var body: some View {
        ToolColumns(sideWidth: 400, breakpoint: 980) {
            VStack(alignment: .leading, spacing: 20) {
                Picker("Test", selection: $kind) {
                    ForEach(Kind.allCases) { k in Text(k.title).tag(k) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(maxWidth: 440, alignment: .leading)
                if kind != .paired {
                    Picker("Given", selection: $summary) {
                        Text("Data").tag(false)
                        Text("Summary (n, mean, SD)").tag(true)
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()
                    .frame(maxWidth: 300, alignment: .leading)
                }
                inputs
                if let note { ToolNote(text: note) }
            }
        } side: {
            if let r = result {
                StatsResults(title: "Result", rows: rows(r), footnote: footnote)
            } else {
                PageSection(title: "Result") {
                    Text(kind == .one ? "A sample of two or more values and μ₀, and the test appears here." : "Two samples of two or more values each, and the test appears here.")
                        .font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
        .onChange(of: kind) { _, k in StudyPrefs.set("stats", "test", k.rawValue) }
        .onChange(of: summary) { _, s in StudyPrefs.set("stats", "testSummary", s) }
        .onChange(of: data1) { _, t in StudyPrefs.set("stats", "sample1", t) }
        .onChange(of: data2) { _, t in StudyPrefs.set("stats", "sample2", t) }
        .onChange(of: mu0) { _, t in StudyPrefs.set("stats", "mu0", t) }
        .onChange(of: n1) { _, t in StudyPrefs.set("stats", "n1", t) }
        .onChange(of: m1) { _, t in StudyPrefs.set("stats", "m1", t) }
        .onChange(of: sd1) { _, t in StudyPrefs.set("stats", "sd1", t) }
        .onChange(of: n2) { _, t in StudyPrefs.set("stats", "n2", t) }
        .onChange(of: m2) { _, t in StudyPrefs.set("stats", "m2", t) }
        .onChange(of: sd2) { _, t in StudyPrefs.set("stats", "sd2", t) }
    }

    @ViewBuilder
    private var inputs: some View {
        switch kind {
        case .one:
            if summary {
                summaryFields("Sample", $n1, $m1, $sd1)
            } else {
                StatsDataField(label: "Sample", text: $data1, height: 120)
            }
            ToolField(label: "μ₀, the mean tested against", text: $mu0, placeholder: "0", width: 160)
        case .two:
            if summary {
                summaryFields("Sample 1", $n1, $m1, $sd1)
                summaryFields("Sample 2", $n2, $m2, $sd2)
            } else {
                HStack(alignment: .top, spacing: 16) {
                    StatsDataField(label: "Sample 1", text: $data1, hint: "", height: 120)
                    StatsDataField(label: "Sample 2", text: $data2, hint: "", height: 120)
                }
                Text("Numbers set apart by spaces, commas or new lines. The two samples need not be the same size.")
                    .font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        case .paired:
            HStack(alignment: .top, spacing: 16) {
                StatsDataField(label: "Before", text: $data1, hint: "", height: 120)
                StatsDataField(label: "After", text: $data2, hint: "", height: 120)
            }
            Text("One value a row in each, in the same order: the test is on the differences, before − after.")
                .font(.sFootnote).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func summaryFields(_ title: String, _ n: Binding<String>, _ m: Binding<String>, _ sd: Binding<String>) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            HStack(alignment: .top, spacing: 12) {
                ToolField(label: "n", text: n, placeholder: "8", width: 90)
                ToolField(label: "Mean", text: m, placeholder: "5", width: 120)
                ToolField(label: "Sample SD", text: sd, placeholder: "2.138", width: 120)
            }
        }
    }

    /// What stops the test, said plainly.
    private var note: String? {
        switch kind {
        case .one:
            if !summary, !data1.isEmpty, StudyMath.numbers(data1).count < 2 { return "A sample needs at least two values." }
            if sample1 != nil, sample1?.sd == 0 { return "Every value is the same, so there is no spread to test against." }
            if sample1 != nil, StudyMath.number(mu0) == nil { return "μ₀ must be a number." }
        case .two:
            if !summary, !data1.isEmpty, !data2.isEmpty, StudyMath.numbers(data1).count < 2 || StudyMath.numbers(data2).count < 2 { return "Each sample needs at least two values." }
            if let a = sample1, let b = sample2, a.sd == 0, b.sd == 0 { return "Neither sample has any spread, so there is nothing to test." }
        case .paired:
            let a = StudyMath.numbers(data1), b = StudyMath.numbers(data2)
            if !a.isEmpty, !b.isEmpty, a.count != b.count { return "Before has \(toolPlural(a.count, "value")) and After has \(b.count) — they must pair up." }
            if a.count == b.count, a.count >= 2, result == nil { return "Every difference is the same, so there is no spread to test against." }
        }
        return nil
    }

    private var footnote: String {
        switch kind {
        case .one: return "t = (x̄ − μ₀) ⁄ (s ⁄ √n), with n − 1 degrees of freedom; the interval is the mean's."
        case .two: return "Welch's t-test: the variances are not assumed equal, the degrees of freedom by Welch–Satterthwaite; the interval is the difference's."
        case .paired: return "The differences' mean tested against zero; the interval is the mean difference's."
        }
    }

    private func rows(_ r: Stats.TTest) -> [(String, String, String?)] {
        let v = StatsText.v
        var rows: [(String, String, String?)] = [
            ("t", v(r.t), nil), ("Degrees of freedom", v(r.df), nil),
            ("p (two-tailed)", StatsText.p(r.pTwoTailed), r.pTwoTailed < 0.05 ? "significant at 5%" : "not significant at 5%"),
            ("p (one-tailed)", StatsText.p(r.pOneTailed), nil),
        ]
        switch kind {
        case .one: rows.append(("Sample mean", v(r.estimate), nil))
        case .two: rows.append(("Difference of means", v(r.estimate), "sample 1 − sample 2"))
        case .paired: rows.append(("Mean difference", v(r.estimate), "before − after"))
        }
        rows.append(("Standard error", v(r.standardError), nil))
        rows.append(("95% CI", "\(v(r.ciLow)) to \(v(r.ciHigh))", nil))
        return rows
    }
}

// MARK: - The pin

/// Statistics' pin, opened: a line of data, and its mean, median and sample SD; Open Full Screen takes the data to the
/// Describe page.
struct StatisticsCompact: View {
    @State private var data = ""

    var body: some View {
        let d = Stats.describe(StudyMath.numbers(data))
        VStack(alignment: .leading, spacing: 10) {
            TextField("2, 4, 4, 4, 5, 5, 7, 9", text: $data)
                .textFieldStyle(.roundedBorder)
                .font(.sBody.monospacedDigit())
                .accessibilityLabel("Data, set apart by spaces or commas")
            if let d {
                Grid(alignment: .leading, horizontalSpacing: 14, verticalSpacing: 4) {
                    GridRow {
                        Text("Mean").font(.sCallout).foregroundStyle(.secondary)
                        Text(StatsText.v(d.mean)).font(.sHeadline.monospacedDigit()).textSelection(.enabled)
                    }
                    GridRow {
                        Text("Median").font(.sCallout).foregroundStyle(.secondary)
                        Text(StatsText.v(d.median)).font(.sHeadline.monospacedDigit()).textSelection(.enabled)
                    }
                    GridRow {
                        Text("Sample SD").font(.sCallout).foregroundStyle(.secondary)
                        Text(StatsText.v(d.sampleSD)).font(.sHeadline.monospacedDigit()).textSelection(.enabled)
                    }
                }
                Text("n = \(d.n)").font(.sCaption).foregroundStyle(.secondary)
            } else {
                Text("Numbers set apart by spaces or commas: their mean, median and SD.").font(.sCallout).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .quickCarry(data.isEmpty ? [:] : ["data": data])
        // (the screenshot suite: -SimplStatsData "…" typed in)
        .onAppear {
            guard data.isEmpty, let t = UserDefaults.standard.string(forKey: "SimplStatsData") else { return }
            data = t
        }
    }
}
