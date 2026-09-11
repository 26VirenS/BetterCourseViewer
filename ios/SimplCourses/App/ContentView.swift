import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var session: AppSession

    var body: some View {
        Group {
            if let host = session.host {
                BrowserScreen(host: host)
                    .id(host) // a different school is a different browser
            } else {
                SchoolPickerView()
            }
        }
        .preferredColorScheme(session.colorScheme) // the status bar follows the interface's appearance
    }
}
