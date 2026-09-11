import SwiftUI

/// The Canvas site, full screen, with the extension running inside it. The navigation bar carries
/// only what the web interface cannot do itself: settings, reload, open in Safari, sign out.
struct BrowserScreen: View {
    let host: String
    @EnvironmentObject private var session: AppSession
    @StateObject private var web: WebController

    init(host: String) {
        self.host = host
        _web = StateObject(wrappedValue: WebController(mode: .canvas(host: host)))
    }

    var body: some View {
        NavigationStack {
            CanvasWebView(controller: web)
                .ignoresSafeArea(edges: .bottom)
                .navigationTitle(host)
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .navigationBarTrailing) {
                        Menu {
                            Button { session.showSettings = true } label: { Label("Settings", systemImage: "gearshape") }
                            Button { web.reload() } label: { Label("Reload", systemImage: "arrow.clockwise") }
                            if let url = web.currentURL, let scheme = url.scheme, scheme.hasPrefix("http") {
                                Button { web.openExternally(url) } label: { Label("Open in Safari", systemImage: "safari") }
                            }
                            Divider()
                            Button(role: .destructive) { session.signOut() } label: { Label("Sign out", systemImage: "rectangle.portrait.and.arrow.right") }
                        } label: {
                            Image(systemName: "ellipsis.circle")
                        }
                    }
                }
        }
        .sheet(isPresented: $session.showSettings) {
            SettingsSheet()
        }
        .onAppear {
            if web.currentURL == nil { web.load() }
        }
        .onReceive(NotificationCenter.default.publisher(for: .simplSignedOut)) { _ in
            web.load() // back to the login page
        }
    }
}
