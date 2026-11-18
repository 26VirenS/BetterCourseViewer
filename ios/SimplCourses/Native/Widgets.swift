import Foundation
import WidgetKit

/// (iPhone 1.9) The widgets' feed kept up to date. Asked of the page (widgetFeed) when the app's screens come up, when
/// something changes (a tick, a hand-in, a refresh, the courses chosen) and when the app comes back to the front — a
/// moment after, so a burst of changes asks once — and kept in the container the app shares with its widgets, which are
/// then told to draw again (only when it says something new). Erased with the screens kept (Sign Out, Change School,
/// Reset Everything: SavedAnswers.eraseAll).
@MainActor
final class WidgetFeeder {
    static let shared = WidgetFeeder()

    private var pending: Task<Void, Never>?

    func refresh(_ engine: Engine, after delay: Double = 1.5) {
        pending?.cancel()
        pending = Task { [weak engine] in
            try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
            guard !Task.isCancelled, let engine, engine.phase == .native,
                  let feed = try? await engine.call("widgetFeed", as: WidgetFeed.self), !Task.isCancelled else { return }
            if let kept = WidgetShelf.read(), kept.sameAs(feed) { return }
            guard WidgetShelf.write(feed) else { return }
            WidgetCenter.shared.reloadAllTimelines()
        }
    }

    /// The feed gone, and the widgets told (they say to open the app).
    nonisolated static func eraseAll() {
        WidgetShelf.erase()
        WidgetCenter.shared.reloadAllTimelines()
    }
}
