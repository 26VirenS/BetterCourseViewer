import SwiftUI

/// (1.3.59) A short tour of the full Periodic Table, shown the first time it opens (and from its Tour button): a small
/// glass card at the bottom, a line or two a step, each step setting the tool to what it describes — the table, a
/// colour view, an element's page, the Lewis builder's quick and drawn ways and its resonance, the 3D builder, search.
struct PTTourStep {
    let symbol: String
    let title: String
    let text: String
    var page: PeriodicTableTool.Page = .table
    var colouring: PTColouring = .category
    var element: Int? = nil
    var lewis: String? = nil
    var molecule: String? = nil

    static let all: [PTTourStep] = [
        PTTourStep(symbol: "square.grid.3x3", title: "The periodic table",
                   text: "Hover an element to preview it. Click one to open it."),
        PTTourStep(symbol: "paintpalette", title: "Colour by a property",
                   text: "Pick a view above the table. The arrows show its trend.", colouring: .electronegativity),
        PTTourStep(symbol: "atom", title: "Every element, in depth",
                   text: "Its atom in 3D, its orbitals, its electrons and every fact. ← → step through.", element: 6),
        PTTourStep(symbol: "circle.grid.cross", title: "Lewis structures",
                   text: "Pick a centre atom and the atoms round it.", page: .lewis, lewis: "H₂O"),
        PTTourStep(symbol: "pencil", title: "Draw your own",
                   text: "Click to place atoms, drag to bond, click a bond to change it.", page: .lewis, lewis: "Benzene"),
        PTTourStep(symbol: "arrow.left.arrow.right", title: "Resonance and charges",
                   text: "Equivalent structures side by side, with formal charges and octet checks.", page: .lewis, lewis: "NO₃⁻"),
        PTTourStep(symbol: "rotate.3d", title: "Build it in 3D",
                   text: "Add atoms, fill with hydrogen, drag to turn. Open in 3D sends a Lewis structure here.",
                   page: .molecules, molecule: "Ethanol"),
        PTTourStep(symbol: "magnifyingglass", title: "Find anything",
                   text: "Type a symbol, name or number in the search at the top."),
    ]
}

/// What the tour asks of the Lewis builder, which keeps its own state: a preset to load, raised each step.
final class PTTour: ObservableObject {
    static let shared = PTTour()
    @Published var lewisPreset: String?
    @Published var lewisNonce = 0

    func showLewis(_ name: String?) {
        lewisPreset = name
        if name != nil { lewisNonce += 1 }
    }
}

/// The tour's card: the step's sign, title and line; dots for where it is; Skip, Back and Next (Return) or Done.
struct PTTourCard: View {
    let step: Int
    let back: () -> Void
    let next: () -> Void
    let skip: () -> Void

    var body: some View {
        let s = PTTourStep.all[step]
        let last = step == PTTourStep.all.count - 1
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .top, spacing: 12) {
                Image(systemName: s.symbol)
                    .font(.system(size: 17, weight: .semibold))
                    .foregroundStyle(Theme.accent)
                    .frame(width: 36, height: 36)
                    .background(Theme.accent.opacity(0.14), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                VStack(alignment: .leading, spacing: 3) {
                    Text(s.title).font(.sBody.weight(.semibold))
                    Text(s.text).font(.sCallout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                .id(step)
                .transition(.opacity)
                Spacer(minLength: 0)
            }
            HStack(spacing: 10) {
                HStack(spacing: 5) {
                    ForEach(PTTourStep.all.indices, id: \.self) { i in
                        Capsule()
                            .fill(i == step ? Theme.accent : Color.primary.opacity(0.18))
                            .frame(width: i == step ? 16 : 6, height: 6)
                    }
                }
                Spacer(minLength: 8)
                if !last {
                    Button("Skip", action: skip)
                        .buttonStyle(.plain)
                        .foregroundStyle(.secondary)
                        .keyboardShortcut(.cancelAction)
                }
                if step > 0 {
                    Button("Back", action: back).glassButton()
                }
                Button(last ? "Done" : "Next", action: next)
                    .glassButton(prominent: true)
                    .keyboardShortcut(.defaultAction)
            }
            .font(.sCallout.weight(.medium))
        }
        .padding(16)
        .frame(width: 440)
        .glassCard(radius: 20)
        .shadow(color: .black.opacity(0.18), radius: 24, y: 10)
    }
}
