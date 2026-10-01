import SwiftUI

/// What covers the web view while Simpl signs in (LoginAssist decides which): the app's own sign-in
/// form, when the school's page has a username and password the reader found; "Logging you in" while
/// details go into the page; and, once Canvas is reached, "Stay logged in?".
struct LoginLayer: View {
    @ObservedObject var assist: LoginAssist

    var body: some View {
        ZStack {
            switch assist.phase {
            case .idle:
                EmptyView()
            case .working:
                LoggingInView()
                    .transition(.opacity)
            case .capture(let host, let error):
                LoginForm(assist: assist, host: host, error: error)
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.22), value: assist.phase)
        .alert("Stay logged in?", isPresented: $assist.askStay) {
            Button("Not now", role: .cancel) { assist.stay(false) }
            Button("Stay logged in") { assist.stay(true) }
        } message: {
            Text("Simpl keeps your username and password on this iPhone only, locked in its Keychain, and signs you in when your school asks again. Two-step codes still come to you.")
        }
    }
}

/// "Logging you in": a ring going round, over the whole screen, while the page underneath is filled
/// in and sent.
struct LoggingInView: View {
    @State private var turning = false

    var body: some View {
        VStack(spacing: 20) {
            ZStack {
                Circle()
                    .stroke(Color.accentColor.opacity(0.16), lineWidth: 5)
                Circle()
                    .trim(from: 0, to: 0.3)
                    .stroke(Color.accentColor, style: StrokeStyle(lineWidth: 5, lineCap: .round))
                    .rotationEffect(.degrees(turning ? 360 : 0))
                    .animation(.linear(duration: 0.9).repeatForever(autoreverses: false), value: turning)
            }
            .frame(width: 54, height: 54)
            Text("Logging you in")
                .font(.headline)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(uiColor: .systemBackground).ignoresSafeArea())
        .onAppear { turning = true }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Logging you in")
    }
}

/// The app's own sign-in form over the school's page. What is typed goes into that page's own
/// username and password fields, and its own button is pressed; the address it goes to is shown.
struct LoginForm: View {
    @ObservedObject var assist: LoginAssist
    let host: String
    let error: String?
    @State private var user = ""
    @State private var pass = ""
    @FocusState private var focus: Field?

    private enum Field { case user, pass }

    private var ready: Bool { !user.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !pass.isEmpty }

    var body: some View {
        Form {
            Section {
                VStack(spacing: 10) {
                    Image(systemName: "lock.shield.fill")
                        .font(.system(size: 46))
                        .foregroundStyle(.tint)
                    Text("Sign in to your school")
                        .font(.title2.bold())
                        .multilineTextAlignment(.center)
                    Text("Signing in at \(host)")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .multilineTextAlignment(.center)
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 8)
                .listRowBackground(Color.clear)
            }
            Section {
                TextField("Username", text: $user)
                    .textContentType(.username)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .submitLabel(.next)
                    .focused($focus, equals: .user)
                    .onSubmit { focus = .pass }
                SecureField("Password", text: $pass)
                    .textContentType(.password)
                    .submitLabel(.go)
                    .focused($focus, equals: .pass)
                    .onSubmit(signIn)
            } footer: {
                if let error {
                    Text(error).foregroundStyle(.red)
                } else {
                    Text("These go into your school's own sign-in page. After you sign in, you can choose to stay logged in.")
                }
            }
            Section {
                Button(action: signIn) {
                    Text("Sign in")
                        .fontWeight(.semibold)
                        .frame(maxWidth: .infinity)
                        .foregroundStyle(ready ? Color.white : Color.secondary)
                }
                .disabled(!ready)
                .listRowBackground(ready ? Color.accentColor : Color(uiColor: .tertiarySystemFill))
                Button {
                    assist.useWebsite()
                } label: {
                    Text("Use the website instead")
                        .frame(maxWidth: .infinity)
                }
            }
        }
        .scrollDismissesKeyboard(.interactively)
        .background(Color(uiColor: .systemGroupedBackground).ignoresSafeArea())
        .onAppear {
            user = assist.prefillUser
            focus = user.isEmpty ? .user : .pass
        }
    }

    private func signIn() {
        guard ready else { return }
        focus = nil
        assist.submit(user: user.trimmingCharacters(in: .whitespacesAndNewlines), pass: pass)
        pass = ""
    }
}
