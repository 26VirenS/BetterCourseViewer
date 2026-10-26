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

    /// (1.3.54) A chip drawn in glass tinted with its colour (macOS 26), filled with it when on; bordered before.
    @ViewBuilder
    func glassChip(_ color: Color, on: Bool) -> some View {
        if #available(macOS 26.0, *) {
            if on { self.buttonStyle(.glassProminent).tint(color) } else { self.buttonStyle(.glass).tint(color.opacity(0.5)) }
        } else {
            self.buttonStyle(.bordered).tint(on ? color : color.opacity(0.6))
        }
    }

    /// A button drawn in glass (macOS 26), prominent in the accent if asked; bordered before. (tools-25) `tint` colours
    /// it instead of the accent: a `.tint` put on the button from outside never showed, as the accent set here is nearer
    /// the button and wins.
    @ViewBuilder
    func glassButton(prominent: Bool = false, tint: Color? = nil) -> some View {
        if #available(macOS 26.0, *) {
            // (1.3: the window's tint is the theme's accent — a prominent button wears it; a plain glass one stays clear)
            if prominent { self.buttonStyle(.glassProminent).tint(tint ?? Theme.accent) } else { self.buttonStyle(.glass).tint(tint) }
        } else if let tint {
            if prominent { self.buttonStyle(.borderedProminent).tint(tint) } else { self.buttonStyle(.bordered).tint(tint) }
        } else {
            if prominent { self.buttonStyle(.borderedProminent) } else { self.buttonStyle(.bordered) }
        }
    }

    /// (1.3.171, 1.3.172) A button drawn as its symbol alone says its name while the pointer rests on it: after a short
    /// pause the button itself widens to show the name beside its symbol (its label drawn with `HoverWordsLabel`), and
    /// narrows again as the pointer leaves or presses. Nothing floats over what is near it. `when: false` (its word
    /// already shown beside the symbol) leaves it as it is.
    func hoverName(_ name: String, when shown: Bool = true) -> some View {
        modifier(HoverName(name: name, enabled: shown))
    }
}

/// The pointer resting on a symbol-only button (`hoverName`): its name handed to its label, which widens to show it.
private struct HoverName: ViewModifier {
    let name: String
    let enabled: Bool
    @State private var shown = false
    @State private var wait: Task<Void, Never>?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .environment(\.hoverWord, shown ? name : nil)
            .onHover { inside in
                wait?.cancel()
                guard inside, enabled else { hide(); return }
                wait = Task { @MainActor in
                    try? await Task.sleep(for: .milliseconds(250))
                    guard !Task.isCancelled else { return }
                    withAnimation(reduceMotion ? nil : Motion.snappy) { shown = true }
                }
            }
            .simultaneousGesture(TapGesture().onEnded { wait?.cancel(); hide() })
            .onChange(of: enabled) { _, on in if !on { wait?.cancel(); hide() } }
    }

    private func hide() {
        guard shown else { return }
        withAnimation(reduceMotion ? nil : Motion.snappy) { shown = false }
    }
}

private struct HoverWordKey: EnvironmentKey {
    static let defaultValue: String? = nil
}

extension EnvironmentValues {
    /// (1.3.172) The name a symbol-only button shows beside its symbol while the pointer rests on it (nil: the symbol alone).
    var hoverWord: String? {
        get { self[HoverWordKey.self] }
        set { self[HoverWordKey.self] = newValue }
    }
}

/// (1.3.172) A label drawn as its symbol alone, with its button's name beside it while the pointer rests on the button
/// (`hoverName`): the button grows to hold it.
struct HoverWordsLabel: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HoverWordsBody(icon: configuration.icon)
    }
}

/// A symbol, and the name `hoverName` hands down beside it while it is shown.
struct HoverWordsBody<Icon: View>: View {
    let icon: Icon
    @Environment(\.hoverWord) private var word

    var body: some View {
        HStack(spacing: 6) {
            icon.frame(minWidth: 16)
            if let word {
                Text(word)
                    .lineLimit(1)
                    .fixedSize()
                    .transition(.opacity)
                    .accessibilityHidden(true) // (the button's own label already names it)
            }
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
    /// (courses-35) How many the section holds, right after its heading ("Current 1") — a count at the far end of the
    /// line read as belonging to something else. `trailing` stays for a note that is not a count.
    var count: Int? = nil
    @ViewBuilder var accessory: () -> Accessory
    @ViewBuilder var content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Text(title)
                    .font(.sTitle3)
                if let count {
                    Text("\(count)")
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText(value: Double(count)))
                }
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
    init(title: String, trailing: String? = nil, count: Int? = nil, @ViewBuilder content: @escaping () -> Content) {
        self.init(title: title, trailing: trailing, count: count, accessory: { EmptyView() }, content: content)
    }
}
