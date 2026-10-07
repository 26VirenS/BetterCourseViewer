import SwiftUI

/// Native bits (school, sign out) plus the extension's own settings page, loaded from the bundle.
struct SettingsSheet: View {
    @EnvironmentObject private var session: AppSession
    @Environment(\.dismiss) private var dismiss
    @StateObject private var web = WebController(mode: .settings)
    @State private var confirmSignOut = false
    @State private var saved = LoginVault.load()

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
                    if let s = saved {
                        LabeledContent("Saved for", value: s.user)
                        LabeledContent("Sign-in page", value: s.host)
                        Button("Forget saved sign-in", role: .destructive) {
                            LoginVault.clear()
                            saved = nil
                        }
                    } else {
                        Text("Not saved. When your school's sign-in page comes up, sign in with Simpl's form and answer Stay logged in.")
                            .foregroundStyle(.secondary)
                    }
                } header: {
                    Text("Sign-in")
                } footer: {
                    Text("Kept in this iPhone's Keychain only: never synced, never backed up, and only ever typed into your school's own sign-in page. Signing out forgets it too.")
                }
                Section {
                    Button {
                        dismiss()
                        NotificationCenter.default.post(name: .simplOpenSetup, object: nil)
                    } label: {
                        Label("Courses and Goals", systemImage: "checklist")
                    }
                    NavigationLink("Look and appearance") {
                        CanvasWebView(controller: web)
                            .ignoresSafeArea(edges: .bottom)
                            .navigationTitle("Settings")
                            .navigationBarTitleDisplayMode(.inline)
                            .onAppear { web.loadIfNeeded() }
                    }
                    LabeledContent("Version", value: AppSession.version)
                    LabeledContent("Interface", value: AppSession.extensionVersion)
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
            .onReceive(NotificationCenter.default.publisher(for: .simplLoginChanged)) { _ in saved = LoginVault.load() }
            .confirmationDialog("Sign out of Canvas on this device?", isPresented: $confirmSignOut, titleVisibility: .visible) {
                Button("Sign out", role: .destructive) {
                    session.signOut()
                    dismiss()
                }
            } message: {
                Text("Your Canvas session and any saved sign-in are cleared; settings stay.")
            }
        }
    }
}
