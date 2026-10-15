import AppKit
import SwiftUI

// (1.3.89) The Equation Balancer: one field for a whole chemical equation — arrows as ->, →, = or ⟶, phase labels,
// charges (Fe3+, SO4^2-), parentheses, hydrates, coefficients already typed (passed over) — balanced as it is typed
// (a moment after the last key, or on Return) with exact fractions (Model/ChemBalance.swift). The balanced equation
// is typeset with its coefficients bold; beside it the atom count table (each element, and the charge, on each side,
// ticked when they match) and the mass check (the total mass of each side, from the molar masses). What can't be
// balanced says why in plain words: an element on one side only, named; two equations in one; a token that is no
// formula. The equation is kept between openings (ChemPrefs `balance.equation`); -SimplBalance "<equation>" opens on
// one for a picture; its pin is the field and the balanced line.

struct EquationBalancerTool: View {
    @State private var equation = EquationBalancerTool.first
    @State private var result: Result<ChemBalanced, ChemBalanceError> = .failure(.empty)
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    static let examples = ["Fe + O2 → Fe2O3", "C3H8 + O2 → CO2 + H2O", "Al + HCl → AlCl3 + H2", "KMnO4 + HCl → KCl + MnCl2 + H2O + Cl2"]

    /// The equation it opens on: the launch's (-SimplBalance, when this tool was the one launched), else the one it
    /// was left with, else the first example.
    private static var first: String {
        if ChemTools.launched == .balance, let e = UserDefaults.standard.string(forKey: "SimplBalance"), !e.isEmpty { return e }
        return ChemPrefs.string(.balance, "equation") ?? examples[0]
    }

    var body: some View {
        ToolPage {
            ToolColumns(sideWidth: 380, breakpoint: 980) {
                main
            } side: {
                side
            }
        }
        .onAppear(perform: takeHandOff)
        // (live as it is typed: a moment after the last key, so half-typed formulas are not called wrong at once)
        .task(id: equation) {
            try? await Task.sleep(nanoseconds: 150_000_000)
            guard !Task.isCancelled else { return }
            balanceNow()
        }
    }

    /// (1.3.83) What the pin's field had, its panel opened full screen.
    private func takeHandOff() {
        if let e = ToolsCenter.shared.takeInfo("equation"), !e.isEmpty { equation = e }
        balanceNow()
    }

    private func balanceNow() {
        let r = ChemBalancer.balance(equation)
        withAnimation(reduceMotion ? nil : Motion.snappy) { result = r }
        ChemPrefs.set(.balance, "equation", equation.isEmpty ? nil : equation)
    }

    private var balanced: ChemBalanced? { try? result.get() }

    // MARK: The equation and its answer

    private var main: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 8) {
                Text("Equation").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                TextField("Fe + O2 → Fe2O3", text: $equation)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 18).monospacedDigit())
                    .controlSize(.large)
                    .onSubmit(balanceNow)
                    .accessibilityLabel("The equation to balance")
                Text("Use -> or → for the arrow. Charges as Fe3+ or SO4^2-, hydrates as CuSO4·5H2O; (s), (l), (g) and (aq) are fine.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            ChipScroller {
                HStack(spacing: 8) {
                    ForEach(Self.examples, id: \.self) { e in
                        Button(e) { equation = e }
                            .font(.sCallout)
                            .glassChip(ToolKind.balance.color, on: equation == e)
                            .help("Balance \(e)")
                    }
                }
                .padding(.vertical, 2)
            }
            answer
        }
    }

    /// The balanced equation typeset — or why it could not be, or what to type.
    private var answer: some View {
        VStack(alignment: .leading, spacing: 14) {
            switch result {
            case .success(let b):
                EquationText(balanced: b)
                    .font(.system(size: 30, weight: .semibold, design: .rounded))
                    .lineLimit(nil)
                    .fixedSize(horizontal: false, vertical: true)
                    .textSelection(.enabled)
                    .accessibilityLabel("Balanced: \(b.text(arrow: "gives"))")
                HStack(spacing: 10) {
                    Label("Balanced", systemImage: "checkmark.circle.fill")
                        .font(.sCallout.weight(.medium))
                        .foregroundStyle(.green)
                    if b.changed, b.species.contains(where: { $0.typedCoefficient != nil }) {
                        Text("· the coefficients typed were changed").font(.sCallout).foregroundStyle(.secondary)
                    }
                }
            case .failure(let e):
                switch e {
                case .empty, .noArrow, .emptySide:
                    Text(equation.trimmingCharacters(in: .whitespaces).isEmpty ? "Type an equation, or pick one above." : e.message)
                        .font(.sBody)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                default:
                    ToolNote(text: e.message)
                }
            }
            HStack(spacing: 8) {
                Button {
                    if let b = balanced { copyToPasteboard(b.text(arrow: "→")) }
                } label: {
                    Label("Copy", systemImage: "doc.on.doc")
                }
                .glassButton()
                .disabled(balanced == nil)
                .help(balanced.map { "Copy \($0.text(arrow: "→"))" } ?? "Copy the balanced equation")
                Button {
                    equation = ""
                } label: {
                    Label("Clear", systemImage: "xmark.circle")
                }
                .glassButton()
                .disabled(equation.isEmpty)
                .help("Clear the equation")
            }
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card(radius: 22)
    }

    // MARK: The counts and the mass check

    private var side: some View {
        VStack(alignment: .leading, spacing: 20) {
            PageSection(title: "Atom count") {
                if let b = balanced {
                    countTable(b)
                } else {
                    Text("Each element on each side, once the equation balances.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
            PageSection(title: "Mass check") {
                if let b = balanced {
                    VStack(spacing: 0) {
                        BioValueRow(label: "Reactants", value: "\(ChemNumber.sig(b.massLeft, 6)) g")
                        BioValueRow(label: "Products", value: "\(ChemNumber.sig(b.massRight, 6)) g", last: true)
                    }
                    .card(radius: 14)
                    Text("The molar masses times the coefficients: the same on both sides, as mass is kept.")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                } else {
                    Text("The total mass of each side, from the molar masses.").font(.sCallout).foregroundStyle(.secondary)
                }
            }
        }
    }

    private func countTable(_ b: ChemBalanced) -> some View {
        VStack(spacing: 0) {
            Grid(alignment: .trailing, horizontalSpacing: 12, verticalSpacing: 0) {
                GridRow {
                    Text("Element").gridColumnAlignment(.leading)
                    Text("Reactants")
                    Text("Products")
                    Text("").frame(width: 18)
                }
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                ForEach(b.counts, id: \.label) { row in
                    Divider().padding(.leading, 14).gridCellColumns(4)
                    GridRow {
                        Text(row.label).font(.sCallout.weight(.medium)).gridColumnAlignment(.leading)
                        Text(Self.count(row.left, charge: row.label == "Charge")).font(.sCallout.monospacedDigit())
                        Text(Self.count(row.right, charge: row.label == "Charge")).font(.sCallout.monospacedDigit())
                        Image(systemName: row.equal ? "checkmark.circle.fill" : "xmark.circle")
                            .foregroundStyle(row.equal ? Color.green : Color.red)
                            .accessibilityLabel(row.equal ? "equal" : "not equal")
                    }
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .accessibilityElement(children: .combine)
                }
            }
        }
        .card(radius: 14)
    }

    /// A count as it reads: "4", and a charge "+17", "−2" or "0".
    private static func count(_ n: Int, charge: Bool) -> String {
        guard charge else { return String(n) }
        return n > 0 ? "+\(n)" : (n < 0 ? "−\(-n)" : "0")
    }
}

/// A balanced equation typeset: each coefficient bold (1 left out), each formula with its subscripts and charge, +
/// between, → from the reactants to the products.
struct EquationText: View {
    let balanced: ChemBalanced

    var body: some View { text }

    private var text: Text {
        var out = Text("")
        for (k, pair) in balanced.reactants.enumerated() {
            if k > 0 { out = out + Text(" + ") }
            out = out + term(pair.species, pair.coefficient)
        }
        out = out + Text(" → ")
        for (k, pair) in balanced.products.enumerated() {
            if k > 0 { out = out + Text(" + ") }
            out = out + term(pair.species, pair.coefficient)
        }
        return out
    }

    private func term(_ s: ChemSpecies, _ c: Int) -> Text {
        (c == 1 ? Text("") : Text(String(c)).bold()) + Text(s.formula.pretty)
    }
}

/// The Equation Balancer's pin, opened: the equation field and the balanced line; Return, or a press on the line,
/// opens the tool whole on it.
struct EquationBalancerCompact: View {
    var openFull: ([String: String]) -> Void
    @State private var equation = UserDefaults.standard.string(forKey: "SimplBalance") ?? ""

    private var result: Result<ChemBalanced, ChemBalanceError> { ChemBalancer.balance(equation) }

    /// (1.3.83) What Open Full Screen carries to the whole tool: the equation typed (nothing typed: nothing — the tool
    /// opens on the one it was left with).
    private var carried: [String: String] { equation.trimmingCharacters(in: .whitespaces).isEmpty ? [:] : ["equation": equation] }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("Fe + O2 → Fe2O3", text: $equation)
                .textFieldStyle(.roundedBorder)
                .font(.sCallout)
                .onSubmit { openFull(carried) }
                .accessibilityLabel("The equation to balance")
            switch result {
            case .success(let b):
                Button {
                    openFull(carried)
                } label: {
                    EquationText(balanced: b)
                        .font(.system(size: 20, weight: .semibold, design: .rounded))
                        .lineLimit(nil)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("The atom counts and the mass check in the Equation Balancer (Return)")
                .accessibilityLabel("Balanced: \(b.text(arrow: "gives")). Open the Equation Balancer")
            case .failure(let e):
                switch e {
                case .empty, .noArrow, .emptySide:
                    Text("Type an equation, with → or -> between its sides.").font(.sCallout).foregroundStyle(.secondary)
                default:
                    Text(e.message).font(.sCallout).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
                }
            }
        }
        .quickCarry(carried)
    }
}
