import SwiftUI
import UIKit

/// The phone's own feel for a tap: the Taptic Engine, by name (the web page asks for the same names
/// through the bridge's `haptic` op: light, medium, rigid, soft, select, success, warning, error).
enum Haptics {
    static func play(_ kind: String) {
        switch kind {
        case "select":
            UISelectionFeedbackGenerator().selectionChanged()
        case "success":
            UINotificationFeedbackGenerator().notificationOccurred(.success)
        case "warning":
            UINotificationFeedbackGenerator().notificationOccurred(.warning)
        case "error":
            UINotificationFeedbackGenerator().notificationOccurred(.error)
        case "medium":
            UIImpactFeedbackGenerator(style: .medium).impactOccurred()
        case "rigid":
            UIImpactFeedbackGenerator(style: .rigid).impactOccurred()
        case "soft":
            UIImpactFeedbackGenerator(style: .soft).impactOccurred()
        case "heavy":
            UIImpactFeedbackGenerator(style: .heavy).impactOccurred()
        default:
            UIImpactFeedbackGenerator(style: .light).impactOccurred()
        }
    }

    static func select() { play("select") }
    static func tap() { play("light") }
    static func success() { play("success") }
    static func error() { play("error") }
}

extension Color {
    /// "#34c759", "34c759", "#3c9" → the colour; anything else → gray.
    init(hex: String?) {
        var s = (hex ?? "").trimmingCharacters(in: .whitespaces)
        if s.hasPrefix("#") { s.removeFirst() }
        if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
        guard s.count == 6, let v = UInt32(s, radix: 16) else {
            self = Color(.systemGray)
            return
        }
        self = Color(red: Double((v >> 16) & 0xff) / 255, green: Double((v >> 8) & 0xff) / 255, blue: Double(v & 0xff) / 255)
    }
}

extension View {
    /// A card in the content (a counter): the grouped list's own cell colour, a tint over it when it calls for
    /// attention. (Liquid Glass is for the bars and controls over the content, as in Apple's apps — glass tiles
    /// in a list drew a grey band behind their row.)
    func contentCard(cornerRadius: CGFloat = 20, tint: Color? = nil) -> some View {
        self.containerShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)) // (what is drawn inside is concentric with it)
            .background {
            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .fill(Color(.secondarySystemGroupedBackground))
                .overlay {
                    if let tint {
                        RoundedRectangle(cornerRadius: cornerRadius, style: .continuous).fill(tint.opacity(0.14))
                    }
                }
        }
    }

    /// A fill inside a rounded container (a list's cell, a card, an answer's row) whose corners are concentric
    /// with it on iOS 26 — `ConcentricRectangle`, the radius taken from the container less the gap between them,
    /// never under `minimum` — and a fixed `radius` before.
    @ViewBuilder
    func innerFill<S: ShapeStyle>(_ style: S, radius: CGFloat, minimum: CGFloat = 6) -> some View {
        if #available(iOS 26.0, *) {
            self.background(style, in: ConcentricRectangle(corners: .concentric(minimum: .fixed(minimum)), isUniform: true))
        } else {
            self.background(style, in: RoundedRectangle(cornerRadius: radius, style: .continuous))
        }
    }

    /// A filter chip: the accent when chosen (white words), the cell colour when not.
    func chipBackground(on: Bool, tint: Color = .accentColor) -> some View {
        self.background(on ? AnyShapeStyle(tint) : AnyShapeStyle(Color(.secondarySystemGroupedBackground)), in: Capsule())
    }
}

/// A rare moment's pieces arriving one after another (a quiz handed in, the setup's welcome and its end):
/// each rises 8pt and fades in, 60ms after the one before, on the setup's own spring. Under Reduce Motion a
/// plain fade, all at once. Never on an everyday screen: a redraw is not an arrival.
struct Arrive: ViewModifier {
    let index: Int
    let shown: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .opacity(shown ? 1 : 0)
            .offset(y: shown || reduceMotion ? 0 : 8)
            .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.42, dampingFraction: 0.88).delay(0.06 * Double(index)), value: shown)
    }
}

extension View {
    func arrive(_ index: Int, _ shown: Bool) -> some View { modifier(Arrive(index: index, shown: shown)) }
}

/// A card's press: it gives a little under the finger and springs back, as Apple's own tiles do.
struct PressScale: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: configuration.isPressed)
    }
}

extension View {

    /// The tab bar shrinks to its active tab as a list scrolls down (iOS 26), as Apple's own apps do.
    @ViewBuilder
    func minimizingTabBar(_ on: Bool = true) -> some View {
        if #available(iOS 26.0, *) {
            self.tabBarMinimizeBehavior(on ? .onScrollDown : .never)
        } else {
            self
        }
    }

    /// A prominent glass button on iOS 26, a bordered prominent one before it.
    @ViewBuilder
    func glassProminentButton() -> some View {
        if #available(iOS 26.0, *) {
            self.buttonStyle(.glassProminent)
        } else {
            self.buttonStyle(.borderedProminent)
        }
    }

    @ViewBuilder
    func glassButton() -> some View {
        if #available(iOS 26.0, *) {
            self.buttonStyle(.glass)
        } else {
            self.buttonStyle(.bordered)
        }
    }
}

// MARK: - The one thing to do next, at the bottom (1.4)

/// A screen's main action (Hand In, Take Quiz, Reply, See Feedback…) at the very bottom, where the tab bar was —
/// Liquid Glass on iOS 26 in a rectangle whose corners are concentric with the phone's own (`ConcentricRectangle`:
/// the farther from the screen's corner, the smaller the radius, never under 22), a capsule before it.
/// `prominent`: tinted with the course's colour, white words; otherwise clear glass.
struct ActionButton: View {
    let title: String
    var symbol: String? = nil
    var tint: Color = .accentColor
    var prominent = true
    var busy = false
    let action: () -> Void

    var body: some View {
        Button {
            Haptics.tap()
            action()
        } label: {
            HStack(spacing: 8) {
                if busy { ProgressView().tint(prominent ? .white : nil) }
                else if let symbol { Image(systemName: symbol) }
                Text(title).lineLimit(1).minimumScaleFactor(0.8)
            }
            .font(.headline)
            .frame(maxWidth: .infinity, minHeight: 52)
            .contentShape(Rectangle())
        }
        .buttonStyle(ActionStyle(tint: tint, prominent: prominent))
    }
}

/// The surface of an `ActionButton`: tinted or clear glass, concentric with the screen's corners.
struct ActionStyle: ButtonStyle {
    let tint: Color
    let prominent: Bool
    @Environment(\.isEnabled) private var enabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(prominent ? Color.white : Color.primary)
            .modifier(ActionSurface(tint: enabled ? tint : Color(.systemGray3), prominent: prominent, pressed: configuration.isPressed))
            .opacity(enabled ? 1 : 0.55)
    }
}

struct ActionSurface: ViewModifier {
    let tint: Color
    let prominent: Bool
    let pressed: Bool

    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            let shape = ConcentricRectangle(corners: .concentric(minimum: .fixed(22)), isUniform: true)
            content
                .glassEffect(prominent ? .regular.tint(tint).interactive() : .regular.interactive(), in: shape)
                .contentShape(shape)
        } else {
            content
                .background(prominent ? AnyShapeStyle(tint) : AnyShapeStyle(Color(.secondarySystemFill)), in: Capsule())
                .scaleEffect(pressed ? 0.97 : 1)
                .animation(.spring(duration: 0.25, bounce: 0.3), value: pressed)
        }
    }
}

/// The bar the actions sit in: the screen's edges inset as Apple's own bottom bars are, side by side when two.
struct ActionBar<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        HStack(spacing: 10) { content }
            .padding(.horizontal, 16)
            .padding(.top, 8)
            .padding(.bottom, 2)
    }
}

/// A course's chip: its short name on a wash of its colour.
struct CourseChip: View {
    let text: String
    let color: String?

    var body: some View {
        Text(text)
            .font(.caption2.weight(.semibold))
            .lineLimit(1)
            .padding(.horizontal, 7)
            .padding(.vertical, 3)
            .foregroundStyle(Color(hex: color))
            .background(Color(hex: color).opacity(0.15), in: Capsule())
    }
}

/// Where a piece of work stands (Missing, Late, Excused, Feedback, New) — the words the web screens use.
struct FlagBadge: View {
    let flag: WorkFlag

    var body: some View {
        Text(flag.word)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
            .foregroundStyle(tone)
            .background(tone.opacity(0.14), in: Capsule())
    }

    private var tone: Color {
        switch flag.kind {
        case "bad": return .red
        case "warn": return .orange
        case "good": return .green
        case "info": return .blue
        default: return .secondary
        }
    }
}

/// The round tick of a task, done or not, with the phone's own feel.
struct CheckCircle: View {
    let done: Bool
    let color: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            ZStack {
                Circle()
                    .strokeBorder(done ? color : Color(.tertiaryLabel), lineWidth: 1.6)
                    .background(Circle().fill(done ? color : .clear))
                if done {
                    Image(systemName: "checkmark")
                        .font(.system(size: 11, weight: .bold))
                        .foregroundStyle(.white)
                        .transition(.scale.combined(with: .opacity))
                }
            }
            .frame(width: 24, height: 24)
            .contentShape(Circle())
            .animation(.spring(response: 0.3, dampingFraction: 0.7), value: done)
        }
        .buttonStyle(.plain)
        .accessibilityLabel(done ? "Mark not done" : "Mark done")
    }
}

/// A screen's state while its first answer is on the way, or when it could not be read.
struct LoadState: View {
    let error: String?
    let retry: () -> Void

    var body: some View {
        if let error = error {
            ContentUnavailableView {
                Label("Could not load", systemImage: "exclamationmark.triangle")
            } description: {
                Text(error)
            } actions: {
                Button("Try again", action: retry).glassButton()
            }
        } else {
            ProgressView().controlSize(.large).frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}
