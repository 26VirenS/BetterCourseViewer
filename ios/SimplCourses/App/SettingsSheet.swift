import SwiftUI

/// Native bits (school, sign out) plus the extension's own settings page, loaded from the bundle.
struct SettingsSheet: View {
    @EnvironmentObject private var session: AppSession
    @Environment(\.dismiss) private var dismiss
    @StateObject private var web = WebController(mode: .settings)
    @State private var confirmSignOut = false

    var body: some View {
        NavigationStack {
            List {
                Section("School") {
                    LabeledContent("Canvas", value: session.host ?? "")
                    Button("Change school…") {
                        session.changeSchool()
                        dismiss()
                    }
                    Button("Sign out", role: .destructive) { confirmSignOut = true }
                }
                Section {
                    NavigationLink("Look, appearance and smart panel") {
                        CanvasWebView(controller: web)
                            .ignoresSafeArea(edges: .bottom)
                            .navigationTitle("Settings")
                            .navigationBarTitleDisplayMode(.inline)
                            .onAppear { web.loadIfNeeded() }
                    }
                    LabeledContent("Version", value: AppSession.version)
                } header: {
                    Text("Simpl Courses")
                } footer: {
                    Text("To take Simpl Courses off this iPhone with everything it stored, delete the app from the Home Screen: touch and hold its icon, then Remove App → Delete App. iOS removes the app's data with it (settings, keys, grade history and the saved session); your Canvas account is not affected.")
                }
            }
            .navigationTitle("Settings")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") { dismiss() }
                }
            }
            .confirmationDialog("Sign out of Canvas on this device?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button("Sign out", role: .destructive) {
                    session.signOut()
                    dismiss()
                }
            } message: {
                Text("Your Canvas session is cleared; settings and keys stay.")
            }
        }
    }
}
