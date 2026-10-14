import Charts
import SwiftUI

// Genetics Crosses's Chi-square page (spec §4.5 Chi-square, §5.3.3–§5.3.4): categories with their expected ratio and
// observed counts — filled from the cross, from Mendel's own counts, or typed — worked the way the AP sheet writes it: the
// null hypothesis in words, expected = ratio × total, the table of o, e, o − e and (o − e)²/e, χ², df = categories − 1,
// the critical value at p = 0.05 from the AP table, the p-value, and the conclusion: reject or fail to reject the null
// hypothesis, never "accept", never "prove".

struct CrossChiSquarePage: View {
    @ObservedObject var model: CrossesState

    var body: some View {
        let made = ChiSquareWork.make(model.chiRows)
        PTWorkLayout {
            controls
        } centre: {
            centre(made.work, problems: made.problems)
        } right: {
            ChiCriticalTable(df: made.work?.result.df ?? max(model.chiRows.count - 1, 1), chi2: made.work?.result.chi2, tint: ToolKind.cross.color)
        }
    }

    // MARK: The rows

    private var controls: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioHeading("Start from")
            CrossMenu(help: "Fill the categories from the cross or from Mendel's counts") {
                Button("The Cross page's cross (\(Allele.plain(model.setup.text)))") { model.fillChiFromCross() }
                Button("Mendel's seed shapes (3 : 1)") {
                    model.chiRows = CrossesState.mendelSeeds
                    model.chiSource = "Mendel's seed shapes"
                }
                Button("Mendel's dihybrid cross (9 : 3 : 3 : 1)") {
                    model.chiRows = CrossesState.mendelDihybrid
                    model.chiSource = "Mendel's dihybrid cross"
                }
            } label: {
                Allele.text(model.chiSource, size: 14, weight: .semibold).lineLimit(1)
            }
            BioHeading("Categories")
            Grid(alignment: .leading, horizontalSpacing: 6, verticalSpacing: 6) {
                GridRow {
                    head("Category")
                    head("Ratio")
                    head("Observed")
                    Color.clear.frame(width: 16, height: 1)
                }
                ForEach($model.chiRows) { $row in
                    GridRow {
                        TextField("Name", text: $row.name)
                            .textFieldStyle(.roundedBorder)
                            .accessibilityLabel("Category name")
                        TextField("1", text: $row.ratio)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 46)
                            .multilineTextAlignment(.trailing)
                            .accessibilityLabel("\(row.name) expected ratio")
                        TextField("0", text: $row.observed)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 64)
                            .multilineTextAlignment(.trailing)
                            .accessibilityLabel("\(row.name) observed count")
                        Button {
                            model.removeChiRow(row.id)
                        } label: {
                            Image(systemName: "minus.circle")
                        }
                        .buttonStyle(.borderless)
                        .disabled(model.chiRows.count <= 2)
                        .help("Take this category out")
                        .accessibilityLabel("Remove \(row.name)")
                    }
                }
            }
            Button {
                model.addChiRow()
            } label: {
                Label("Add a category", systemImage: "plus").frame(maxWidth: .infinity)
            }
            .glassButton()
            .controlSize(.large)
            .help("Another category of offspring")
            Text("Ratio: the parts the null hypothesis expects (9, 3, 3, 1). Observed: the counts you got, as whole numbers.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .font(.sCallout)
    }

    // MARK: The working

    private func centre(_ w: ChiSquareWork?, problems: [String]) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                if let w {
                    BioFactRow(label: "Null hypothesis (H₀)", text: w.nullHypothesis, tint: ToolKind.cross.color)
                    table(w)
                    chart(w)
                    summary(w)
                    conclusion(w)
                } else {
                    // (the null hypothesis needs only the ratio: it is stated before any count is in)
                    if let ratio = ChiSquareWork.ratioText(model.chiRows) {
                        BioFactRow(label: "Null hypothesis (H₀)", text: ChiSquareWork.nullHypothesis(ratio), tint: ToolKind.cross.color)
                    }
                    let waiting = model.chiRows.allSatisfy { $0.observed.trimmingCharacters(in: .whitespaces).isEmpty }
                    VStack(alignment: .leading, spacing: 8) {
                        Text(waiting ? "Enter the observed counts" : "Not yet").font(.sTitle3)
                        if waiting {
                            Text("Type how many offspring of each kind were counted, on the left. The test is worked out as you type.")
                                .font(.sCallout)
                                .foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        } else {
                            ForEach(problems, id: \.self) { p in
                                Label(p, systemImage: "exclamationmark.circle").font(.sCallout).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                            }
                        }
                    }
                }
            }
            .padding(20)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card(radius: 20)
    }

    private func f2(_ x: Double) -> String { String(halfUp: "%.2f", x) }

    private func head(_ s: String) -> some View {
        Text(s).font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
    }

    private func cell(_ s: String, colour: Color = .primary, bold: Bool = false) -> some View {
        Text(s).font(.sCallout.weight(bold ? .bold : .regular).monospacedDigit()).foregroundStyle(colour)
    }

    private func table(_ w: ChiSquareWork) -> some View {
        Grid(alignment: .trailing, horizontalSpacing: 14, verticalSpacing: 8) {
            GridRow {
                head("Category").gridColumnAlignment(.leading)
                head("o")
                head("e")
                head("o − e")
                head("(o − e)² ÷ e")
            }
            Divider().gridCellUnsizedAxes(.horizontal)
            ForEach(w.names.indices, id: \.self) { i in
                GridRow {
                    Allele.text(w.names[i], size: 14, weight: .semibold)
                        .lineLimit(1)
                        .accessibilityLabel(Allele.spoken(w.names[i]))
                    cell(ChiSquareWork.number(w.observed[i]))
                        .accessibilityLabel("observed \(ChiSquareWork.number(w.observed[i]))")
                    cell(f2(w.expected[i]), colour: w.expected[i] < 5 ? .orange : .primary)
                        .accessibilityLabel("expected \(f2(w.expected[i]))")
                    cell(f2(w.observed[i] - w.expected[i]))
                    cell(String(halfUp: "%.3f", w.result.terms[i]))
                }
            }
            Divider().gridCellUnsizedAxes(.horizontal)
            GridRow {
                cell("Total")
                cell(ChiSquareWork.number(w.total))
                cell(f2(w.expected.reduce(0, +)))
                cell("")
                cell("χ² = " + String(halfUp: "%.3f", w.result.chi2), bold: true)
            }
        }
        .padding(14)
        .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }

    private func chart(_ w: ChiSquareWork) -> some View {
        let tint = ToolKind.cross.color
        return VStack(alignment: .leading, spacing: 6) {
            Chart {
                ForEach(w.names.indices, id: \.self) { i in
                    BarMark(x: .value("Category", w.names[i]), y: .value("Observed", w.observed[i]), width: .ratio(0.55))
                        .foregroundStyle(tint.opacity(0.75))
                        .cornerRadius(4)
                    RectangleMark(x: .value("Category", w.names[i]), y: .value("Expected", w.expected[i]), width: .ratio(0.75), height: .fixed(3))
                        .foregroundStyle(Color.primary.opacity(0.75))
                }
            }
            .chartLegend(.hidden)
            .frame(height: 150)
            .accessibilityLabel("Observed counts as bars, expected counts as lines")
            HStack(spacing: 14) {
                HStack(spacing: 5) {
                    RoundedRectangle(cornerRadius: 2).fill(tint.opacity(0.75)).frame(width: 12, height: 10)
                    Text("observed").font(.sCaption)
                }
                HStack(spacing: 5) {
                    Rectangle().fill(Color.primary.opacity(0.75)).frame(width: 14, height: 3)
                    Text("expected").font(.sCaption)
                }
            }
            .foregroundStyle(.secondary)
            .accessibilityHidden(true)
        }
    }

    private func summary(_ w: ChiSquareWork) -> some View {
        VStack(spacing: 0) {
            BioValueRow(label: "χ² = Σ (o − e)² ÷ e", value: String(halfUp: "%.3f", w.result.chi2))
            BioValueRow(label: "Degrees of freedom", value: "\(w.names.count) − 1 = \(w.result.df)")
            BioValueRow(label: "Critical value (p = 0.05)", value: String(halfUp: "%.2f", w.result.critical))
            BioValueRow(label: "p-value", value: w.pText, last: true)
        }
        .card(radius: 14)
    }

    private func conclusion(_ w: ChiSquareWork) -> some View {
        let reject = w.result.reject
        return VStack(alignment: .leading, spacing: 8) {
            Label {
                Text(w.result.conclusion).font(.sHeadline).fixedSize(horizontal: false, vertical: true)
            } icon: {
                Image(systemName: reject ? "xmark.octagon.fill" : "checkmark.circle.fill").foregroundStyle(reject ? Color.orange : Color.green)
            }
            Text(w.conclusionDetail).font(.sCallout).fixedSize(horizontal: false, vertical: true)
            Text(w.meaning).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            if w.lowExpected {
                Label("An expected count is under 5, where chi-square is unreliable: combine categories or count more.", systemImage: "exclamationmark.triangle")
                    .font(.sCaption)
                    .foregroundStyle(.orange)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Text("We never “accept” the null hypothesis, and a test never “proves” it: we reject it, or fail to reject it.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background((reject ? Color.orange : Color.green).opacity(0.08), in: RoundedRectangle(cornerRadius: 14, style: .continuous))
    }
}

/// §5.3.4's critical values (the AP sheet's p = 0.05 and 0.01 columns, with 0.10 and 0.001), the row for this df lit and
/// where χ² falls in it said. Shared with Hardy–Weinberg's test.
struct ChiCriticalTable: View {
    let df: Int
    let chi2: Double?
    var tint: Color = Theme.accent

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            BioCard(title: "Critical values", subtitle: "The χ² a difference must pass, by df and p") {
                Grid(alignment: .trailing, horizontalSpacing: 0, verticalSpacing: 2) {
                    GridRow {
                        cell("df", row: 0, bold: true).gridColumnAlignment(.leading)
                        ForEach(BioChiSquare.tableP, id: \.self) { p in
                            cell(p == 0.001 ? "0.001" : String(halfUp: "%.2f", p), row: 0, bold: true)
                        }
                    }
                    ForEach(1...10, id: \.self) { d in
                        GridRow {
                            cell("\(d)", row: d, bold: d == df)
                            ForEach(Array(BioChiSquare.tableP.enumerated()), id: \.offset) { i, p in
                                cell(String(halfUp: "%.2f", BioChiSquare.table[d]?[i] ?? 0), row: d, bold: d == df && p == 0.05)
                            }
                        }
                    }
                }
                if df > 10 {
                    Text("df \(df): the critical value at p = 0.05 is \(String(halfUp: "%.2f", BioChiSquare.critical(df: df))) (computed).")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
                if let chi2 {
                    Text(placement(chi2)).font(.sCallout).fixedSize(horizontal: false, vertical: true)
                }
            }
            Text("p is the chance of a difference at least this large if the null hypothesis is true. Below 0.05, the difference is called significant.")
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    /// A cell of the table: the header row (0) small and grey, this df's row lit.
    private func cell(_ s: String, row: Int, bold: Bool) -> some View {
        Text(s)
            .font(row == 0 ? .sCaption.weight(.semibold) : .sCallout.weight(bold ? .bold : .regular).monospacedDigit())
            .foregroundStyle(row == 0 || row != df ? Color.secondary : Color.primary)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .frame(maxWidth: .infinity, alignment: .trailing)
            .background(row == df ? tint.opacity(0.14) : Color.clear)
    }

    /// Where χ² falls in this df's row.
    private func placement(_ chi2: Double) -> String {
        guard let row = BioChiSquare.table[df], row.count == BioChiSquare.tableP.count else { return "" }
        var beyond: Int? = nil
        for i in row.indices where chi2 > row[i] { beyond = i }
        guard let i = beyond else { return String(halfUp: "χ² = %.2f is below %.2f, so p > 0.10.", chi2, row[0]) }
        let p = BioChiSquare.tableP[i]
        let pText = p == 0.001 ? "0.001" : String(halfUp: "%.2f", p)
        return String(halfUp: "χ² = %.2f is beyond %.2f, so p < ", chi2, row[i]) + pText + "."
    }
}
