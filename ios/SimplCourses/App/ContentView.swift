import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var session: AppSession

    var body: some View {
        if let host = session.host {
            BrowserScreen(host: host)
                .id(host) // a different school is a different browser
        } else {
            SchoolPickerView()
        }
    }
}
