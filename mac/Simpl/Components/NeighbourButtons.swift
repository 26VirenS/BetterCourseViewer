import SwiftUI

/// The items either side of one (native-app.js `neighbours`): each a name and its Canvas address.
struct ItemNeighbours: Decodable, Equatable {
    struct Item: Decodable, Equatable {
        var name: String
        var url: String
    }

    var prev: Item?
    var next: Item?
}

/// (1.3.7) Previous and Next in an item's toolbar, as the web has them: the assignment, quiz, discussion, announcement or
/// page before and after this one — in its own tab's order, else its modules' — each naming where it goes. Asked for
/// once the screen is up; nothing is shown where there is nothing either side. Words on them, not bare chevrons, so they
/// are never taken for Back and Forward.
struct NeighbourButtons: View {
    let ctx: String
    /// Assignment, Quiz, Discussion, Announcement or Page.
    let type: String
    /// Its id (a page's: its url name).
    let id: String
    @EnvironmentObject private var engine: Engine
    @State private var around: ItemNeighbours?

    var body: some View {
        if around.map({ $0.prev == nil && $0.next == nil }) != true {
            ControlGroup {
                Button { open(around?.prev) } label: {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                        Text("Previous")
                    }
                }
                .disabled(around?.prev == nil)
                .help(around?.prev.map { "Previous: \($0.name)" } ?? "Nothing before this")
                Button { open(around?.next) } label: {
                    HStack(spacing: 4) {
                        Text("Next")
                        Image(systemName: "chevron.right")
                    }
                }
                .disabled(around?.next == nil)
                .help(around?.next.map { "Next: \($0.name)" } ?? "Nothing after this")
            }
            .task(id: "\(ctx)|\(type)|\(id)") { await load() }
        }
    }

    private func load() async {
        guard !id.isEmpty else { return }
        around = try? await engine.call("neighbours", ["ctx": ctx, "type": type, "id": id], as: ItemNeighbours.self)
    }

    private func open(_ item: ItemNeighbours.Item?) {
        guard let item else { return }
        engine.openWeb(item.url, title: item.name)
    }
}
