import SwiftUI

// The app's type and surfaces (1.2): a larger scale than the Mac's own (whose body is 13 pt — small for a screen read
// all day), and Liquid Glass where macOS 26 has it, a material before it. Every screen takes its sizes from here.

/// Simpl's type scale for the Mac: each step a little larger than AppKit's own.
extension Font {
    static let sLargeTitle = Font.system(size: 34, weight: .bold)
    static let sTitle = Font.system(size: 28, weight: .bold)
    static let sTitle2 = Font.system(size: 22, weight: .bold)
    static let sTitle3 = Font.system(size: 19, weight: .semibold)
    static let sHeadline = Font.system(size: 16, weight: .semibold)
    static let sBody = Font.system(size: 15)
    static let sCallout = Font.system(size: 14)
    static let sSubheadline = Font.system(size: 13)
    static let sFootnote = Font.system(size: 12.5)
    static let sCaption = Font.system(size: 12)
    static let sCaption2 = Font.system(size: 11)
}

extension View {
    /// Liquid Glass in a shape (macOS 26), tinted and answering the pointer if asked; a material with a hairline before.
    @ViewBuilder
    func glass<S: Shape>(_ shape: S, tint: Color? = nil, interactive: Bool = false) -> some View {
        if #available(macOS 26.0, *) {
            self.glassEffect(Glass.regular.tint(tint).interactive(interactive), in: shape)
        } else {
            self.background {
                ZStack {
                    shape.fill(.regularMaterial)
                    if let tint { shape.fill(tint.opacity(0.14)) }
                }
            }
            .overlay(shape.stroke(Theme.edge, lineWidth: 1))
        }
    }

    /// Glass in a rounded rectangle (a card, a panel, a bar).
    func glassCard(radius: CGFloat = 18, tint: Color? = nil, interactive: Bool = false) -> some View {
        glass(RoundedRectangle(cornerRadius: radius, style: .continuous), tint: tint, interactive: interactive)
    }

    /// Glass in a capsule (a chip, a pill of buttons).
    func glassCapsule(tint: Color? = nil, interactive: Bool = false) -> some View {
        glass(Capsule(), tint: tint, interactive: interactive)
    }

    /// A button drawn in glass (macOS 26), prominent in the accent if asked; bordered before.
    @ViewBuilder
    func glassButton(prominent: Bool = false) -> some View {
        if #available(macOS 26.0, *) {
            // (1.3: the window's tint is the theme's accent — a prominent button wears it; a plain glass one stays clear)
            if prominent { self.buttonStyle(.glassProminent).tint(Theme.accent) } else { self.buttonStyle(.glass).tint(nil) }
        } else {
            if prominent { self.buttonStyle(.borderedProminent) } else { self.buttonStyle(.bordered) }
        }
    }
}

/// Glass shapes near each other drawn as one (they blend and morph into each other on macOS 26); a plain stack before.
struct GlassGroup<Content: View>: View {
    var spacing: CGFloat? = nil
    @ViewBuilder var content: Content

    var body: some View {
        if #available(macOS 26.0, *) {
            GlassEffectContainer(spacing: spacing) { content }
        } else {
            content
        }
    }
}

/// A section of a page with its heading and no box round it: the page itself is the surface, so what is inside it can
/// be a card without a card in a card.
struct PageSection<Content: View, Accessory: View>: View {
    let title: String
    var trailing: String? = nil
    @ViewBuilder var accessory: () -> Accessory
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(title)
                    .font(.sTitle3)
                Spacer(minLength: 8)
                if let trailing, !trailing.isEmpty {
                    Text(trailing).font(.sCallout).foregroundStyle(.secondary)
                }
                accessory()
            }
            content()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

extension PageSection where Accessory == EmptyView {
    init(title: String, trailing: String? = nil, @ViewBuilder content: @escaping () -> Content) {
        self.init(title: title, trailing: trailing, accessory: { EmptyView() }, content: content)
    }
}
