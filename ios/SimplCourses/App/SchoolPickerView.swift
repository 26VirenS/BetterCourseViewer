import SwiftUI

/// First run: which Canvas site to open (the school's portal link). The sign-in that follows is the
/// school's own page, with the app's own form over it where the page has a username and password.
struct SchoolPickerView: View {
    @EnvironmentObject private var session: AppSession
    @State private var address = ""
    @State private var invalid = false

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("school.instructure.com", text: $address)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .keyboardType(.URL)
                        .textContentType(.URL)
                        .onSubmit(go)
                } header: {
                    Text("Your school's Canvas portal link")
                } footer: {
                    Text("The address you open Canvas at in a browser, for example school.instructure.com or canvas.school.edu. If your school's sign-in page has a username and password, Simpl shows its own sign-in form for it, and can keep you logged in on this iPhone if you choose.")
                }
                Section {
                    Button("Continue", action: go)
                        .disabled(address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .navigationTitle("Simpl Courses")
            .alert("That does not look like a Canvas address", isPresented: $invalid) {
                Button("OK", role: .cancel) {}
            } message: {
                Text("Enter the site's address, such as school.instructure.com.")
            }
        }
    }

    private func go() {
        if !session.setHost(address) { invalid = true }
    }
}
