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
        self.background {
            RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                .fill(Color(.secondarySystemGroupedBackground))
                .overlay {
                    if let tint {
                        RoundedRectangle(cornerRadius: cornerRadius, style: .continuous).fill(tint.opacity(0.14))
                    }
                }
        }
    }

    /// A filter chip: the accent when chosen (white words), the cell colour when not.
    func chipBackground(on: Bool, tint: Color = .accentColor) -> some View {
        self.background(on ? AnyShapeStyle(tint) : AnyShapeStyle(Color(.secondarySystemGroupedBackground)), in: Capsule())
    }
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
