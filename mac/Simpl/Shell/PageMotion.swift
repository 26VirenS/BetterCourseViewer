import AppKit
import SwiftUI

/// (1.3.78) How the window last moved — one motion language with Simpl on the web and on Windows. Going deeper (a
/// link, a row, Forward) the new page comes in from the trailing edge a short way as it fades in, and the page left
/// behind drifts a little the other way as it fades out; Back is the same in reverse; another place picked in the
/// sidebar cross-fades. The distances are the web's (32 points in from the side). All on `Motion.page`, critically
/// damped: a move made mid-way
/// starts from wherever things are, and nothing waits in line. Under Reduce Motion, every move is a cross-fade.
struct PageMove: Equatable {
    enum Kind: Equatable {
        case none, deeper, back, across
    }

    var kind: Kind = .none
    /// Counts the moves, so a screen arrives once for each.
    var serial = 0
}

extension Motion {
    /// (1.3.78) A page arriving or leaving: no overshoot, most of the way in a quarter of a second.
    static let page = Animation.spring(response: 0.34, dampingFraction: 1)
}

/// The distances a page moves by, and the way the page leaving goes for the move under way (read as it leaves).
@MainActor
enum PageMotion {
    /// How far a page comes in from the side it comes from, and how far the one it covers drifts the other way.
    static let travel: CGFloat = 32
    static let drift: CGFloat = 12

    /// Where a page leaving goes for the move under way (its x offset, fully gone): read by the page as it leaves, each
    /// frame, so it goes the way of the move that took it away.
    nonisolated(unsafe) private(set) static var exit: CGFloat = 0

    static var reduced: Bool { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }

    /// A move begins: the page leaving goes the move's way.
    static func leaving(_ kind: PageMove.Kind) {
        guard !reduced else { exit = 0; return }
        switch kind {
        case .deeper: exit = -drift
        case .back: exit = travel
        case .across, .none: exit = 0
        }
    }

    /// Where a page arriving by a move starts from.
    static func entrance(_ kind: PageMove.Kind) -> CGSize {
        switch kind {
        case .deeper: return CGSize(width: travel, height: 0)
        case .back: return CGSize(width: -drift, height: 0)
        case .across, .none: return .zero
        }
    }
}

/// (1.3.78) Whether a screen has its content (OpenClock's `ready`, as each screen says it): a page arriving waits for
/// it, a third of a second at most, so it never slides in empty and fills once it has stopped. A screen that says
/// nothing is ready.
struct PageReadyKey: PreferenceKey {
    static let defaultValue = true
    static func reduce(value: inout Bool, nextValue: () -> Bool) { value = value && nextValue() }
}

/// (1.3.78) A screen arriving: from where its move says (PageMove), to rest, fading in, on `Motion.page`. A screen made
/// by the move arrives as it appears; one that was already there (the place's own screen, uncovered by Back) arrives
/// when the move makes it the one on top again.
struct PageArrival: ViewModifier {
    let move: PageMove
    /// It is the screen on top: the place's own with nothing pushed on it, or the last pushed.
    let top: Bool
    @State private var shown: Bool
    @State private var from: CGSize
    @State private var pending: Bool
    @State private var arrivedFor: Int
    @State private var ready = true
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(move: PageMove, top: Bool) {
        self.move = move
        self.top = top
        let arriving = top && move.kind != .none
        _shown = State(initialValue: !arriving)
        _from = State(initialValue: arriving ? PageMotion.entrance(move.kind) : .zero)
        _pending = State(initialValue: arriving)
        _arrivedFor = State(initialValue: move.serial)
    }

    func body(content: Content) -> some View {
        // (made the one on top by a move it has not arrived for yet: unseen from this very frame, before the move is
        // taken up below — a screen uncovered never shows for a frame where it was and then starts over)
        let unseen = top && move.kind != .none && move.serial != arrivedFor
        let hidden = unseen || !shown
        content
            .opacity(hidden ? 0 : 1)
            .offset(hidden && !reduceMotion ? (unseen ? PageMotion.entrance(move.kind) : from) : .zero)
            .onPreferenceChange(PageReadyKey.self) { now in
                MainActor.assumeIsolated {
                    ready = now
                    if now && pending { settle() }
                }
            }
            .onAppear { if pending { begin() } }
            .onChange(of: move) { _, _ in takeUp() }
            .onChange(of: top) { _, _ in takeUp() }
    }

    /// Already there, and on top again by a move (the place's own screen as Back uncovers it): it arrives.
    private func takeUp() {
        guard top, move.kind != .none, move.serial != arrivedFor else { return }
        arrivedFor = move.serial
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) {
            shown = false
            from = PageMotion.entrance(move.kind)
            pending = true
        }
        begin()
    }

    /// On the run loop's next turn (the screen has said by then whether it has its content): at once if it has, else
    /// once it has or a third of a second has gone.
    private func begin() {
        DispatchQueue.main.async {
            guard pending else { return }
            if ready {
                settle()
            } else {
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.33) { if pending { settle() } }
            }
        }
    }

    private func settle() {
        pending = false
        withAnimation(reduceMotion ? .easeOut(duration: 0.18) : Motion.page) { shown = true }
    }
}

/// (1.3.78) A page leaving as the window moves on: the way the move goes (PageMotion.exit, read as it leaves), fading
/// as it goes.
struct PageExit: ViewModifier, Animatable {
    var gone: Double

    var animatableData: Double {
        get { gone }
        set { gone = newValue }
    }

    func body(content: Content) -> some View {
        content
            .offset(x: gone * PageMotion.exit)
            .opacity(1 - gone)
    }
}

extension AnyTransition {
    /// (1.3.78) A place's page: it arrives by itself (PageArrival) and leaves the way the move goes (PageExit).
    static var page: AnyTransition {
        .asymmetric(insertion: .identity, removal: .modifier(active: PageExit(gone: 1), identity: PageExit(gone: 0)))
    }
}
