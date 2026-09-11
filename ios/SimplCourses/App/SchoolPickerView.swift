import SwiftUI

/// First run: which Canvas site to open. The sign-in itself happens on that site's own login page.
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
                    Text("Your school's Canvas address")
                } footer: {
                    Text("The address you open Canvas at in a browser, for example school.instructure.com or canvas.school.edu. You sign in on the next screen exactly as you would in Safari; Simpl Courses never sees your password.")
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
