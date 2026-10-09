import SwiftUI

/// (1.3.8) The school at the head of the sidebar, as the web's sidebar has it: its logo in a small rounded tile and its
/// name beside it, over Dashboard. The logo is the one the web finds (app.js schoolLogo: a square mark filling the tile,
/// or a wide one drawn whole on the school's own colour); a school with none, or one that will not load, shows its
/// initial in the accent instead.
struct SchoolHeader: View {
    @EnvironmentObject private var engine: Engine

    private static let size: CGFloat = 34

    var body: some View {
        let name = engine.snapshot?.site.flatMap { $0.isEmpty ? nil : $0 } ?? "Simpl"
        HStack(spacing: 11) {
            tile(name)
            Text(name)
                .font(.sHeadline)
                .lineLimit(2)
                .foregroundStyle(.primary)
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 18)
        .padding(.top, 4)
        .padding(.bottom, 10)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(name)
    }

    @ViewBuilder
    private func tile(_ name: String) -> some View {
        let shape = RoundedRectangle(cornerRadius: 9, style: .continuous)
        if let logo = engine.snapshot?.logo, let url = URL(string: logo.url) {
            let wide = logo.square == false
            AsyncImage(url: url) { phase in
                if let image = phase.image {
                    image
                        .resizable()
                        .aspectRatio(contentMode: wide ? .fit : .fill)
                        .padding(wide ? 3 : 0)
                } else if phase.error != nil {
                    initial(name)
                } else {
                    Color.clear
                }
            }
            .frame(width: Self.size, height: Self.size)
            .background(Self.ground(logo.bg))
            .clipShape(shape)
            .overlay(shape.strokeBorder(Theme.edge, lineWidth: 0.5))
        } else {
            initial(name)
                .frame(width: Self.size, height: Self.size)
                .clipShape(shape)
        }
    }

    private func initial(_ name: String) -> some View {
        Text(String(name.prefix(1)).uppercased())
            .font(.system(size: 16, weight: .bold))
            .foregroundStyle(.white)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(Theme.accent)
    }

    /// A wide logo's ground: the school's nav colour where it is a plain hex, else none.
    private static func ground(_ bg: String?) -> Color {
        guard let bg = bg?.trimmingCharacters(in: .whitespaces), bg.hasPrefix("#") else { return .clear }
        return Color(hex: bg)
    }
}
