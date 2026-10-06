import SwiftUI
import WebKit

/// A screen of the web interface (a course, an assignment, a page, the Inbox) pushed on a native stack:
/// Apple's navigation bar over it with the screen's title and Back, the page under the bar and the tab
/// bar, the edge swipe the stack's own. The one web view moves in while the screen is showing.
struct WebScreen: View {
    let url: String
    let title: String
    @EnvironmentObject private var engine: Engine
    @State private var id = UUID()

    var body: some View {
        WebSlot(id: id)
            .ignoresSafeArea()
            .background(Color(.systemBackground))
            .navigationTitle(engine.titles[id] ?? title)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar(engine.focus && engine.active == id ? .hidden : .automatic, for: .navigationBar)
            .toolbar(engine.focus && engine.active == id ? .hidden : .automatic, for: .tabBar)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        if let link = engine.absolute(url) {
                            ShareLink(item: link) { Label("Share Link", systemImage: "square.and.arrow.up") }
                        }
                        Button {
                            Haptics.tap()
                            engine.reload()
                        } label: { Label("Reload", systemImage: "arrow.clockwise") }
                    } label: {
                        Image(systemName: "ellipsis")
                    }
                    .accessibilityLabel("More")
                }
            }
            .onAppear { engine.attach(id, url: url) }
            .onDisappear { engine.detach(id) }
    }
}

/// The body of a web screen: a place the web view is moved into (Engine.attach).
struct WebSlot: UIViewRepresentable {
    let id: UUID
    @EnvironmentObject private var engine: Engine

    func makeUIView(context: Context) -> SlotView {
        let v = SlotView()
        let engine = self.engine
        let id = self.id
        v.onInsets = { [weak engine] insets in
            guard let engine = engine, engine.active == id else { return }
            engine.setInsets(top: insets.top, bottom: insets.bottom)
        }
        v.slotID = id
        v.owner = engine
        engine.register(v, for: id)
        return v
    }

    func updateUIView(_ uiView: SlotView, context: Context) {}

    static func dismantleUIView(_ uiView: SlotView, coordinator: ()) {
        uiView.onInsets = nil
        if let id = uiView.slotID { uiView.owner?.unregister(id) }
    }
}
