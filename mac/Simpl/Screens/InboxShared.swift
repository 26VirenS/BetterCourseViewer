import Combine
import SwiftUI

// What the Inbox's screens share (InboxView, InboxConversation, InboxCompose): the replies begun and the mailboxes and
// conversations as last read, a person's round picture, the lines of chips that wrap, and a message's live links.

/// (1.2) What the Inbox keeps while the app is open: a reply begun in a conversation (kept while others are read, while
/// the window goes elsewhere, and when the conversation is opened on a screen of its own), and each mailbox and
/// conversation as last read, shown at once when it is opened again while it is read afresh. All of it is let go on
/// signing out and when the window's school changes.
@MainActor
final class InboxStore: ObservableObject {
    static let shared = InboxStore()

    @Published var drafts: [String: String] = [:]
    var lists: [String: InboxData] = [:]
    var conversations: [String: ConversationData] = [:]
    private var owner: ObjectIdentifier?
    private var signedOut: AnyCancellable?

    private init() {
        signedOut = NotificationCenter.default.publisher(for: .simplSignedOut)
            .sink { _ in
                Task { @MainActor in InboxStore.shared.forget() }
            }
    }

    /// The engine whose school is showing: another one (another school) lets go of what the last one read.
    func bind(_ engine: Engine) {
        let id = ObjectIdentifier(engine)
        guard id != owner else { return }
        forget()
        owner = id
    }

    func forget() {
        if !drafts.isEmpty { drafts = [:] }
        lists = [:]
        conversations = [:]
    }
}

/// What the Inbox hands the conversation it shows beside its list: the list's word on its star (nil once the mailbox
/// showing no longer holds it — Unread, after it is read), and the star itself.
struct InboxLink {
    var starred: Bool?
    var star: (Bool) -> Void
}

/// A person's picture, or their initials on a colour of their own (the same colour every time for the same name); for a
/// conversation with several people, two figures.
struct InboxAvatar: View {
    let name: String
    var avatar: String? = nil
    var size: CGFloat = 36

    var body: some View {
        Group {
            if let s = avatar, let url = URL(string: s) {
                AsyncImage(url: url) { image in
                    image.resizable().scaledToFill()
                } placeholder: {
                    initials
                }
            } else {
                initials
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
        .accessibilityHidden(true)
    }

    /// Several people ("Ana Ruiz, Ben Ode +2").
    private var many: Bool { name.contains(",") || name.contains("+") }

    private var initials: some View {
        ZStack {
            Circle().fill(tone.gradient)
            if many {
                Image(systemName: "person.2.fill")
                    .font(.system(size: size * 0.38, weight: .semibold))
                    .foregroundStyle(.white)
            } else if letters.isEmpty {
                Image(systemName: "person.fill")
                    .font(.system(size: size * 0.46))
                    .foregroundStyle(.white)
            } else {
                Text(letters)
                    .font(.system(size: size * 0.38, weight: .semibold))
                    .foregroundStyle(.white)
            }
        }
    }

    private var letters: String {
        let firsts = name.split(separator: " ").prefix(2).compactMap { word in word.first(where: { $0.isLetter }) }
        return String(firsts).uppercased()
    }

    /// The name's own colour (from its letters, so the same on every launch).
    private var tone: Color {
        let seed = name.unicodeScalars.reduce(0) { sum, scalar in (sum &* 31 &+ Int(scalar.value)) & 0xFFFF }
        return InboxAvatar.tones[seed % InboxAvatar.tones.count]
    }

    private static let tones: [Color] = [.blue, .indigo, .purple, .pink, .orange, .teal, .green, .mint, .cyan, .brown]
}

/// Chips in lines that wrap as words do (the people a message is to, the files sent with a message).
struct InboxFlow: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let lines = arrange(subviews, in: proposal.width ?? .infinity)
        let height = lines.reduce(0) { $0 + $1.height } + spacing * CGFloat(max(lines.count - 1, 0))
        let widest = lines.map(\.width).max() ?? 0
        return CGSize(width: proposal.width ?? widest, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for line in arrange(subviews, in: bounds.width) {
            var x = bounds.minX
            for item in line.items {
                subviews[item.index].place(at: CGPoint(x: x, y: y + (line.height - item.size.height) / 2), proposal: ProposedViewSize(item.size))
                x += item.size.width + spacing
            }
            y += line.height + spacing
        }
    }

    /// A line of chips: which, how wide, how tall.
    private struct Line {
        var items: [(index: Int, size: CGSize)] = []
        var width: CGFloat = 0
        var height: CGFloat = 0
    }

    private func arrange(_ subviews: Subviews, in width: CGFloat) -> [Line] {
        var lines: [Line] = []
        var line = Line()
        for index in subviews.indices {
            let size = subviews[index].sizeThatFits(ProposedViewSize(width: width, height: nil))
            if !line.items.isEmpty && line.width + spacing + size.width > width {
                lines.append(line)
                line = Line()
            }
            line.width = line.items.isEmpty ? size.width : line.width + spacing + size.width
            line.height = max(line.height, size.height)
            line.items.append((index: index, size: size))
        }
        if !line.items.isEmpty { lines.append(line) }
        return lines
    }
}

/// A conversation's own address (Canvas's links name it so, and Simpl's web Inbox opens at it).
func inboxConversationAddress(_ id: String) -> String { "/conversations?id=\(id)" }

/// What finds the web addresses in a message's words.
private let inboxLinkDetector = try? NSDataDetector(types: NSTextCheckingResult.CheckingType.link.rawValue)

/// A message's words with their web addresses made links.
func inboxLinkedText(_ text: String) -> AttributedString {
    var out = AttributedString(text)
    guard let detector = inboxLinkDetector else { return out }
    for match in detector.matches(in: text, options: [], range: NSRange(text.startIndex..., in: text)) {
        guard let url = match.url, let range = Range(match.range, in: out) else { continue }
        out[range].link = url
    }
    return out
}
