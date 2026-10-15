import SwiftUI

// (1.3.91) The Matrix Calculator, a maths tool: two matrices A and B typed into grids (1 to 6 rows and columns each,
// Tab moving from cell to cell, a block of rows pasted into a cell filling the grid), an operation chosen from a row of
// chips — A + B, A − B, A × B, k·A, Aᵀ, det A, A⁻¹, rank, RREF, trace, A·x = b with B as the column b, the eigenvalues,
// Aⁿ — and the result under them: exact fractions while the cells are whole numbers, fractions or short decimals (a
// toggle shows them as decimals), decimals otherwise; the RREF's row operations listed, each with the matrix after it,
// in a disclosure. The arithmetic is MatrixMath's (Model/MatrixModel.swift, checked by the Models job). The matrices,
// the operation, k and n are kept (StudyPrefs).

/// The operations, in the chips' order.
enum MatrixOp: String, CaseIterable, Identifiable {
    case add, subtract, multiply, scale, transpose, det, inverse, rank, rref, trace, solve, eigen, power

    var id: String { rawValue }

    var title: String {
        switch self {
        case .add: return "A + B"
        case .subtract: return "A − B"
        case .multiply: return "A × B"
        case .scale: return "k·A"
        case .transpose: return "Aᵀ"
        case .det: return "det A"
        case .inverse: return "A⁻¹"
        case .rank: return "rank A"
        case .rref: return "RREF A"
        case .trace: return "trace A"
        case .solve: return "A·x = b"
        case .eigen: return "Eigenvalues"
        case .power: return "Aⁿ"
        }
    }

    /// What the result is called over it.
    var heading: String {
        switch self {
        case .solve: return "x, with B as b"
        case .eigen: return "Eigenvalues of A"
        case .rref: return "Reduced row echelon form of A"
        default: return title
        }
    }

    var usesB: Bool { self == .add || self == .subtract || self == .multiply || self == .solve }
}

/// A matrix as typed: its size, and six rows of six cells kept whole, so a size stepped down and up again finds what
/// was there.
struct MatrixDraft: Equatable {
    var rows: Int
    var cols: Int
    var cells: [[String]]

    static let most = 6

    init(rows: Int = 2, cols: Int = 2) {
        self.rows = min(max(rows, 1), MatrixDraft.most)
        self.cols = min(max(cols, 1), MatrixDraft.most)
        cells = [[String]](repeating: [String](repeating: "", count: MatrixDraft.most), count: MatrixDraft.most)
    }

    /// From "1 2;3 4" (rows by ";" or new lines, cells by spaces, commas or tabs), up to 6 × 6; nil otherwise.
    init?(text: String) {
        let lines = text.replacingOccurrences(of: ";", with: "\n").split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        guard !lines.isEmpty, lines.count <= MatrixDraft.most else { return nil }
        let grid = lines.map { $0.replacingOccurrences(of: ",", with: " ").replacingOccurrences(of: "\t", with: " ").split(separator: " ").map(String.init) }
        guard let width = grid.first?.count, width > 0, width <= MatrixDraft.most, grid.allSatisfy({ $0.count == width }) else { return nil }
        self.init(rows: lines.count, cols: width)
        for r in 0..<rows { for c in 0..<cols { cells[r][c] = grid[r][c] } }
    }

    /// The cells on show, written as they are read ("1 2;3 4"; an empty cell is 0).
    var text: String {
        (0..<rows).map { r in (0..<cols).map { c in cells[r][c].trimmingCharacters(in: .whitespaces).isEmpty ? "0" : cells[r][c] }.joined(separator: " ") }.joined(separator: ";")
    }

    var isEmpty: Bool { (0..<rows).allSatisfy { r in (0..<cols).allSatisfy { cells[r][$0].trimmingCharacters(in: .whitespaces).isEmpty } } }

    /// The matrix, or the first cell that is not a number.
    var read: Result<Matrix, MatrixCell> {
        var out: [MNum] = []
        for r in 0..<rows {
            for c in 0..<cols {
                let t = cells[r][c].trimmingCharacters(in: .whitespaces)
                if t.isEmpty { out.append(.zero); continue }
                guard let n = MNum(text: t) else { return .failure(MatrixCell(row: r, col: c)) }
                out.append(n)
            }
        }
        return .success(Matrix(rows: rows, cols: cols, cells: out))
    }

    /// A block of rows pasted into one cell (it holds a new line, a tab or a ";"): the whole grid filled from it.
    mutating func absorbPaste() {
        for r in 0..<rows {
            for c in 0..<cols where cells[r][c].contains(where: { $0.isNewline || $0 == "\t" || $0 == ";" }) {
                if let d = MatrixDraft(text: cells[r][c]) { self = d } else { cells[r][c] = cells[r][c].trimmingCharacters(in: .whitespacesAndNewlines) }
                return
            }
        }
    }

    mutating func clear() {
        for r in 0..<MatrixDraft.most { for c in 0..<MatrixDraft.most { cells[r][c] = "" } }
    }
}

/// A cell of a matrix that could not be read.
struct MatrixCell: Error, Equatable {
    let row: Int
    let col: Int
}

/// What an operation gave.
enum MatrixOutcome: Equatable {
    case matrix(Matrix)
    case scalar(MNum)
    case count(Int)
    case values([String])
    case rref(Matrix, [RowStep])
    case trouble(String)

    /// Whether any cell shown could be a fraction.
    var hasExact: Bool {
        switch self {
        case .matrix(let m), .rref(let m, _): return m.cells.contains { $0.isExact && (($0.text().contains("/"))) }
        case .scalar(let s): return s.isExact && s.text().contains("/")
        default: return false
        }
    }
}

/// The work: both matrices read, the operation done, what went wrong said plainly.
enum MatrixWork {
    static func size(_ m: Matrix) -> String { "\(m.rows) × \(m.cols)" }

    static func run(_ op: MatrixOp, a draftA: MatrixDraft, b draftB: MatrixDraft, k: String, n: String) -> MatrixOutcome {
        let a: Matrix
        switch draftA.read {
        case .success(let m): a = m
        case .failure(let cell): return .trouble("Row \(cell.row + 1), column \(cell.col + 1) of A isn't a number — type a whole number, a fraction like 3/4, or a decimal.")
        }
        var b: Matrix? = nil
        if op.usesB {
            switch draftB.read {
            case .success(let m): b = m
            case .failure(let cell): return .trouble("Row \(cell.row + 1), column \(cell.col + 1) of B isn't a number — type a whole number, a fraction like 3/4, or a decimal.")
            }
        }
        func square(_ what: String) -> MatrixOutcome? { a.isSquare ? nil : .trouble("\(what) needs a square matrix — A is \(size(a)).") }
        switch op {
        case .add:
            guard let b, let s = MatrixMath.add(a, b) else { return .trouble("A and B must be the same size to add them — A is \(size(a)), B is \(size(b!)).") }
            return .matrix(s)
        case .subtract:
            guard let b, let s = MatrixMath.subtract(a, b) else { return .trouble("A and B must be the same size to subtract them — A is \(size(a)), B is \(size(b!)).") }
            return .matrix(s)
        case .multiply:
            guard let b, let p = MatrixMath.multiply(a, b) else { return .trouble("A × B needs A's columns to match B's rows — A has \(a.cols), B has \(b!.rows).") }
            return .matrix(p)
        case .scale:
            guard let kv = MNum(text: k) else { return .trouble("k must be a number.") }
            return .matrix(MatrixMath.scale(a, by: kv))
        case .transpose:
            return .matrix(MatrixMath.transpose(a))
        case .det:
            if let t = square("The determinant") { return t }
            return .scalar(MatrixMath.determinant(a) ?? .zero)
        case .inverse:
            if let t = square("The inverse") { return t }
            guard let inv = MatrixMath.inverse(a) else { return .trouble("A has no inverse: its determinant is 0.") }
            return .matrix(inv)
        case .rank:
            return .count(MatrixMath.rank(a))
        case .rref:
            let r = MatrixMath.rref(a)
            return .rref(r.result, r.steps)
        case .trace:
            if let t = square("The trace") { return t }
            return .scalar(MatrixMath.trace(a) ?? .zero)
        case .solve:
            guard let b, let s = MatrixMath.solve(a, b) else { return .trouble("b must be one column with a row for each of A's — B is \(size(b!)), A has \(a.rows) rows.") }
            switch s {
            case .unique(let x): return .matrix(x)
            case .noSolution: return .trouble("No solution: the equations contradict each other.")
            case .many(let free): return .trouble("Infinitely many solutions (\(toolPlural(free, "free variable"))) — RREF A shows how the unknowns are tied.")
            }
        case .eigen:
            if let t = square("Eigenvalues") { return t }
            guard let e = MatrixMath.eigenvalues(a) else { return .trouble("The eigenvalues did not settle — try a matrix with smaller entries.") }
            return .values(MatrixMath.pairText(e))
        case .power:
            if let t = square("A power") { return t }
            guard let nv = StudyMath.number(n), nv == nv.rounded(), abs(nv) <= 60 else { return .trouble("n must be a whole number between −60 and 60.") }
            guard let p = MatrixMath.power(a, Int(nv)) else { return .trouble("A has no inverse, so no negative power.") }
            return .matrix(p)
        }
    }

    /// A result as plain text, for Copy: rows on lines, cells apart by tabs.
    static func plain(_ outcome: MatrixOutcome, decimals: Bool) -> String {
        func lines(_ m: Matrix) -> String {
            (0..<m.rows).map { r in (0..<m.cols).map { m[r, $0].text(decimals: decimals).replacingOccurrences(of: "−", with: "-") }.joined(separator: "\t") }.joined(separator: "\n")
        }
        switch outcome {
        case .matrix(let m), .rref(let m, _): return lines(m)
        case .scalar(let s): return s.text(decimals: decimals).replacingOccurrences(of: "−", with: "-")
        case .count(let n): return "\(n)"
        case .values(let v): return v.joined(separator: ", ").replacingOccurrences(of: "−", with: "-")
        case .trouble(let t): return t
        }
    }
}

/// What the launch asked (-SimplTool matrix -SimplMatrixA "1 2;3 4" -SimplMatrixB "…" -SimplMatrixOp inverse
/// -SimplMatrixSteps YES): read only when the launch opened the tool.
enum MatrixLaunch {
    static var here: Bool { ChemTools.launched == .matrix }
    static func draft(_ key: String) -> MatrixDraft? { here ? UserDefaults.standard.string(forKey: key).flatMap { MatrixDraft(text: $0) } : nil }
    static var op: MatrixOp? { here ? UserDefaults.standard.string(forKey: "SimplMatrixOp").flatMap { MatrixOp(rawValue: $0) } : nil }
    static var steps: Bool { here && UserDefaults.standard.bool(forKey: "SimplMatrixSteps") }
}

struct MatrixCalculatorTool: View {
    @State private var a = MatrixLaunch.draft("SimplMatrixA") ?? StudyPrefs.string("matrix", "a").flatMap { MatrixDraft(text: $0) } ?? MatrixDraft(text: "1 2;3 4")!
    @State private var b = MatrixLaunch.draft("SimplMatrixB") ?? StudyPrefs.string("matrix", "b").flatMap { MatrixDraft(text: $0) } ?? MatrixDraft(text: "0 1;1 0")!
    @State private var op: MatrixOp = MatrixLaunch.op ?? StudyPrefs.string("matrix", "op").flatMap { MatrixOp(rawValue: $0) } ?? .det
    @State private var k = StudyPrefs.string("matrix", "k") ?? "2"
    @State private var n = StudyPrefs.string("matrix", "n") ?? "2"
    @State private var decimals = StudyPrefs.bool("matrix", "decimals", default: false)
    @State private var stepsOpen = MatrixLaunch.steps
    @State private var copied = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var outcome: MatrixOutcome { MatrixWork.run(op, a: a, b: b, k: k, n: n) }

    var body: some View {
        let out = outcome
        ToolPage {
            ViewThatFits(in: .horizontal) {
                HStack(alignment: .top, spacing: 28) {
                    MatrixEditor(title: "A", draft: $a)
                    MatrixEditor(title: "B", draft: $b, dimmed: !op.usesB)
                }
                VStack(alignment: .leading, spacing: 20) {
                    MatrixEditor(title: "A", draft: $a)
                    MatrixEditor(title: "B", draft: $b, dimmed: !op.usesB)
                }
            }
            operations
            result(out)
        }
        .onAppear(perform: takeHandOff)
        .onChange(of: a) { _, d in StudyPrefs.set("matrix", "a", d.text) }
        .onChange(of: b) { _, d in StudyPrefs.set("matrix", "b", d.text) }
        .onChange(of: op) { _, o in StudyPrefs.set("matrix", "op", o.rawValue) }
        .onChange(of: k) { _, v in StudyPrefs.set("matrix", "k", v) }
        .onChange(of: n) { _, v in StudyPrefs.set("matrix", "n", v) }
        .onChange(of: decimals) { _, v in StudyPrefs.set("matrix", "decimals", v) }
        .onChange(of: out) { _, _ in copied = false }
    }

    /// (1.3.83) What the pin's calculator had, its panel opened full screen: its A.
    private func takeHandOff() {
        if let t = ToolsCenter.shared.takeInfo("a"), let d = MatrixDraft(text: t) { a = d }
    }

    /// The operations as chips, and k or n beside them when the operation wants one.
    private var operations: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Operation").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
            PillFlow(spacing: 8, lineSpacing: 8) {
                ForEach(MatrixOp.allCases) { o in
                    Button {
                        withAnimation(reduceMotion ? nil : Motion.snappy) { op = o }
                    } label: {
                        Text(o.title).font(.sCallout.weight(op == o ? .semibold : .regular)).padding(.horizontal, 4)
                    }
                    .glassChip(ToolKind.matrix.color, on: op == o)
                    .controlSize(.large)
                    .accessibilityAddTraits(op == o ? .isSelected : [])
                }
            }
            if op == .scale {
                ToolField(label: "k", text: $k, placeholder: "2", hint: "A whole number, a fraction or a decimal.", width: 140)
            } else if op == .power {
                ToolField(label: "n", text: $n, placeholder: "2", hint: "A whole number; 0 gives the identity, a negative n a power of the inverse.", width: 140)
            }
        }
    }

    /// The result in a card: a matrix, a number, a list of eigenvalues or what went wrong; Fractions or Decimals,
    /// Copy; the RREF's steps under it.
    private func result(_ out: MatrixOutcome) -> some View {
        PageSection(title: op.heading) {
            VStack(alignment: .leading, spacing: 14) {
                Group {
                    switch out {
                    case .matrix(let m): MatrixGrid(matrix: m, decimals: decimals)
                    case .rref(let m, _): MatrixGrid(matrix: m, decimals: decimals)
                    case .scalar(let s):
                        Text(s.text(decimals: decimals))
                            .font(.system(size: 40, weight: .bold, design: .rounded).monospacedDigit())
                            .textSelection(.enabled)
                    case .count(let c):
                        Text("\(c)")
                            .font(.system(size: 40, weight: .bold, design: .rounded).monospacedDigit())
                            .textSelection(.enabled)
                    case .values(let v):
                        VStack(alignment: .leading, spacing: 6) {
                            ForEach(Array(v.enumerated()), id: \.offset) { i, s in
                                Text("λ\(MatrixMath.sub(i + 1)) = \(s)")
                                    .font(.sTitle3.monospacedDigit())
                                    .textSelection(.enabled)
                            }
                            Text("Complex pairs are written once, as a ± bi.").font(.sFootnote).foregroundStyle(.secondary)
                        }
                    case .trouble(let t): ToolNote(text: t)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                HStack(spacing: 10) {
                    if out.hasExact {
                        Picker("Shown as", selection: $decimals) {
                            Text("Fractions").tag(false)
                            Text("Decimals").tag(true)
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                        .frame(width: 190)
                    }
                    Button {
                        copyToPasteboard(MatrixWork.plain(out, decimals: decimals))
                        withAnimation(reduceMotion ? nil : Motion.snappy) { copied = true }
                    } label: {
                        Label(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc").font(.sCallout)
                    }
                    .glassButton()
                    .help("Copy the result as text, rows on lines")
                    Spacer()
                }
                if case .rref(_, let steps) = out {
                    DisclosureGroup(isExpanded: $stepsOpen) {
                        if steps.isEmpty {
                            Text("A is already in reduced row echelon form.").font(.sCallout).foregroundStyle(.secondary).padding(.top, 8)
                        } else {
                            VStack(alignment: .leading, spacing: 14) {
                                ForEach(Array(steps.enumerated()), id: \.offset) { i, s in
                                    HStack(alignment: .top, spacing: 14) {
                                        Text("\(i + 1). \(s.text)")
                                            .font(.sBody.monospacedDigit())
                                            .frame(width: 170, alignment: .leading)
                                        MatrixGrid(matrix: s.after, decimals: decimals, small: true)
                                    }
                                }
                            }
                            .padding(.top, 8)
                        }
                    } label: {
                        Text(toolPlural(steps.count, "step")).font(.sCallout.weight(.medium))
                    }
                }
            }
            .padding(18)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card(radius: 20)
            .animation(reduceMotion ? nil : Motion.snappy, value: out)
        }
    }
}

/// A matrix's grid of cells to type into, its size steppers, Paste and Clear.
struct MatrixEditor: View {
    let title: String
    @Binding var draft: MatrixDraft
    var dimmed = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                Text(title).font(.sTitle3).accessibilityAddTraits(.isHeader)
                Stepper(value: $draft.rows, in: 1...MatrixDraft.most) {
                    Text("\(draft.rows) rows").font(.sCallout).monospacedDigit()
                }
                .accessibilityLabel("Rows of \(title)")
                Stepper(value: $draft.cols, in: 1...MatrixDraft.most) {
                    Text("\(draft.cols) columns").font(.sCallout).monospacedDigit()
                }
                .accessibilityLabel("Columns of \(title)")
                Spacer(minLength: 4)
                Button {
                    if let s = NSPasteboard.general.string(forType: .string), let d = MatrixDraft(text: s) { draft = d }
                } label: {
                    Label("Paste", systemImage: "doc.on.clipboard").font(.sCallout).labelStyle(.iconOnly)
                }
                .glassButton()
                .help("Fill \(title) from the clipboard: rows on lines, cells apart by spaces, commas or tabs")
                .accessibilityLabel("Paste into \(title)")
                Button {
                    draft.clear()
                } label: {
                    Label("Clear", systemImage: "xmark.circle").font(.sCallout).labelStyle(.iconOnly)
                }
                .glassButton()
                .disabled(draft.isEmpty)
                .help("Clear \(title)")
                .accessibilityLabel("Clear \(title)")
            }
            Grid(horizontalSpacing: 6, verticalSpacing: 6) {
                ForEach(0..<draft.rows, id: \.self) { r in
                    GridRow {
                        ForEach(0..<draft.cols, id: \.self) { c in
                            TextField("0", text: $draft.cells[r][c])
                                .textFieldStyle(.roundedBorder)
                                .font(.sBody.monospacedDigit())
                                .multilineTextAlignment(.center)
                                .frame(width: 64)
                                .accessibilityLabel("\(title), row \(r + 1), column \(c + 1)")
                        }
                    }
                }
            }
            .padding(12)
            .card()
            .opacity(dimmed ? 0.55 : 1)
            Text(dimmed ? "Not used by this operation." : "Tab moves between cells; paste a block of rows into any cell to fill the grid.")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .onChange(of: draft.cells) { _, _ in draft.absorbPaste() }
    }
}

/// A matrix shown: its cells between brackets.
struct MatrixGrid: View {
    let matrix: Matrix
    var decimals = false
    var small = false

    var body: some View {
        HStack(spacing: 6) {
            bracket
            Grid(horizontalSpacing: small ? 10 : 16, verticalSpacing: small ? 4 : 8) {
                ForEach(0..<matrix.rows, id: \.self) { r in
                    GridRow {
                        ForEach(0..<matrix.cols, id: \.self) { c in
                            Text(matrix[r, c].text(decimals: decimals))
                                .font(small ? .sCallout.monospacedDigit() : .sTitle3.monospacedDigit())
                                .lineLimit(1)
                                .gridColumnAlignment(.trailing)
                        }
                    }
                }
            }
            .padding(.vertical, 4)
            .textSelection(.enabled)
            bracket
        }
        .fixedSize()
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("\(matrix.rows) by \(matrix.cols) matrix: " + (0..<matrix.rows).map { r in (0..<matrix.cols).map { matrix[r, $0].text(decimals: decimals) }.joined(separator: ", ") }.joined(separator: "; "))
    }

    private var bracket: some View {
        RoundedRectangle(cornerRadius: 1.5)
            .fill(Color.secondary.opacity(0.7))
            .frame(width: 2)
    }
}

/// The Matrix Calculator's pin, opened: a 2 × 2 or 3 × 3 A, its determinant and its inverse; Open Full Screen takes A.
struct MatrixCompact: View {
    @State private var draft = MatrixDraft(text: UserDefaults.standard.string(forKey: "SimplMatrixA") ?? "") ?? MatrixDraft(rows: 2, cols: 2)

    var body: some View {
        let read = draft.read
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Picker("Size", selection: Binding(get: { draft.rows }, set: { s in draft.rows = s; draft.cols = s })) {
                    Text("2 × 2").tag(2)
                    Text("3 × 3").tag(3)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(width: 140)
                Spacer()
            }
            Grid(horizontalSpacing: 5, verticalSpacing: 5) {
                ForEach(0..<draft.rows, id: \.self) { r in
                    GridRow {
                        ForEach(0..<draft.cols, id: \.self) { c in
                            TextField("0", text: $draft.cells[r][c])
                                .textFieldStyle(.roundedBorder)
                                .font(.sCallout.monospacedDigit())
                                .multilineTextAlignment(.center)
                                .frame(width: 54)
                                .accessibilityLabel("Row \(r + 1), column \(c + 1)")
                        }
                    }
                }
            }
            switch read {
            case .success(let m):
                HStack(alignment: .top, spacing: 16) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("det A").font(.sCaption).foregroundStyle(.secondary)
                        Text((MatrixMath.determinant(m) ?? .zero).text())
                            .font(.sHeadline.monospacedDigit())
                            .textSelection(.enabled)
                    }
                    VStack(alignment: .leading, spacing: 2) {
                        Text("A⁻¹").font(.sCaption).foregroundStyle(.secondary)
                        if let inv = MatrixMath.inverse(m) {
                            MatrixGrid(matrix: inv, small: true)
                        } else {
                            Text("none (det 0)").font(.sCallout).foregroundStyle(.secondary)
                        }
                    }
                }
            case .failure(let cell):
                Text("Row \(cell.row + 1), column \(cell.col + 1) isn't a number.").font(.sCallout).foregroundStyle(.secondary)
            }
        }
        .quickCarry(draft.isEmpty ? [:] : ["a": draft.text])
        .onChange(of: draft.cells) { _, _ in draft.absorbPaste() }
    }
}
