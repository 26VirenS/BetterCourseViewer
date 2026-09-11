import SwiftUI

/// The Canvas site, edge to edge, with the extension running inside it. The web interface draws its
/// own chrome (the large titles, the back bar, the tab bar) under the status bar, so there is no
/// native navigation bar; Settings and Sign out live under the avatar on the Today screen.
struct BrowserScreen: View {
    let host: String
    @EnvironmentObject private var session: AppSession
    @StateObject private var web: WebController

    init(host: String) {
        self.host = host
        _web = StateObject(wrappedValue: WebController(mode: .canvas(host: host)))
    }

    var body: some View {
        CanvasWebView(controller: web)
            .ignoresSafeArea()
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
