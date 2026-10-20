import SwiftUI
import AppKit

/// (1.3.8) The head of the sidebar, over Dashboard. (1.3.142) Simpl's own, as the web's sidebar has it (app.js brandRow):
/// the logo's tile, then the word as the intro draws it, cut in three bands, with the blue dot after it. The school
/// it is signed in to stays its accessibility label.
struct SchoolHeader: View {
    @EnvironmentObject private var engine: Engine

    var body: some View {
        let school = engine.snapshot?.site.flatMap { $0.isEmpty ? nil : $0 }
        HStack(spacing: 10) {
            Image("SimplTile")
                .resizable()
                .interpolation(.high)
                .frame(width: 32, height: 32)
                .overlay(RoundedRectangle(cornerRadius: 7.3, style: .continuous).strokeBorder(Color.white.opacity(0.18), lineWidth: 0.5)) // (black on black: its edge)
            SimplWordmark(size: 27)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 18)
        .padding(.top, 4)
        .padding(.bottom, 10)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(school.map { "Simpl, \($0)" } ?? "Simpl")
    }
}

/// "Simpl" as the web draws it (app.css .bcv-brand__word): semibold, tight, in the ink, cut by two thin gaps across
/// its capitals (at 28–31 % and 66–69 % of their height down), and a blue dot sitting on the baseline after it.
struct SimplWordmark: View {
    var size: CGFloat

    var body: some View {
        let font = NSFont.systemFont(ofSize: size, weight: .semibold)
        let base = font.ascender                 // the baseline, down from the top of the line
        let cap = font.capHeight
        let unit = size / 116                    // the web's drawing is at 116
        HStack(alignment: .lastTextBaseline, spacing: 5 * unit) {
            Text("Simpl")
                .font(.system(size: size, weight: .semibold))
                .tracking(-5 * unit)
                .foregroundStyle(.primary)
                .fixedSize()
                .mask(
                    GeometryReader { g in
                        let top = base - cap
                        Path { p in
                            let w = g.size.width + 20
                            let cut1 = top + cap * 0.279, cut2 = top + cap * 0.315
                            let cut3 = top + cap * 0.658, cut4 = top + cap * 0.694
                            p.addRect(CGRect(x: -10, y: -20, width: w, height: cut1 + 20))
                            p.addRect(CGRect(x: -10, y: cut2, width: w, height: cut3 - cut2))
                            p.addRect(CGRect(x: -10, y: cut4, width: w, height: g.size.height - cut4 + 20))
                        }
                    }
                )
            Circle()
                .fill(Color(red: 10 / 255, green: 132 / 255, blue: 1))
                .frame(width: 18 * unit, height: 18 * unit)
                .alignmentGuide(.lastTextBaseline) { d in d[.bottom] - 1 * unit }
        }
        .accessibilityHidden(true)
    }
}
