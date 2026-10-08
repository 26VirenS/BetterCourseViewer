import SwiftUI

/// What covers the window while Simpl signs in (LoginAssist decides which): the app's own sign-in card, when the
/// school's page has a username and password the reader found; "Logging you in" while they go into the page; a note
/// over the school's page while something there is the student's to do; and, once Canvas is reached, "Stay logged in?".
struct LoginLayer: View {
    @ObservedObject var assist: LoginAssist
    /// The school's page is what the window shows (the app's own screens are not up over it).
    var pageShown = true

    var body: some View {
        ZStack {
            switch assist.phase {
            case .idle:
                // (a view, not nothing: "Stay logged in?" is asked from here once the cover is gone)
                Color.clear.allowsHitTesting(false)
                if pageShown && assist.handoff {
                    HandoffNote(restart: assist.restart) { assist.handoff = false }
                        .transition(.move(edge: .top).combined(with: .opacity))
                } else if pageShown && assist.onSchoolPage {
                    StartOverButton(action: assist.restart)
                        .transition(.opacity)
                }
            case .working:
                LoggingInView(showPage: { assist.showPage() }, restart: { assist.restart() })
                    .transition(.opacity)
            case .capture(let host, let error):
                LoginForm(assist: assist, host: host, error: error)
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.22), value: assist.phase)
        .animation(Motion.gentle, value: assist.handoff)
        .animation(.easeOut(duration: 0.2), value: assist.onSchoolPage)
        .alert("Stay logged in?", isPresented: $assist.askStay) {
            Button("Not Now", role: .cancel) { assist.stay(false) }
            Button("Stay Logged In") { assist.stay(true) }
        } message: {
            Text("Simpl keeps your username and password on this Mac only, locked in your Keychain, and signs you in when your school asks again. Two-step codes still come to you.")
        }
    }
}

/// "Logging you in": the Mac's own spinner over the window while the page underneath is filled in and sent — and,
/// after a moment, a way to the page, for a sign-in that wants something done by hand.
private struct LoggingInView: View {
    var showPage: () -> Void = {}
    var restart: () -> Void = {}
    @State private var offer = false

    var body: some View {
        ZStack {
            Theme.page.ignoresSafeArea()
            VStack(spacing: 16) {
                ProgressView()
                    .controlSize(.large)
                Text("Logging you in")
                    .font(.sTitle3.weight(.semibold))
                    .accessibilityAddTraits(.isHeader)
                if offer {
                    VStack(spacing: 10) {
                        Text("Something to do on your school’s page?")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                        HStack(spacing: 10) {
                            Button("Start Over", action: restart)
                                .controlSize(.large)
                            Button(action: showPage) {
                                Label("Show the Page", systemImage: "safari")
                            }
                            .buttonStyle(.borderedProminent)
                            .controlSize(.large)
                            .keyboardShortcut(.defaultAction)
                        }
                    }
                    .padding(.top, 10)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .animation(Motion.gentle, value: offer)
        }
        .task {
            // (a sign-in that goes through by itself never shows it; one that waits, a two-step code, a page the
            // reader cannot see, gets a way to the page)
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            offer = true
        }
    }
}

/// Over the school's page in the middle of a sign-in: what to do there, and that Simpl carries on by itself.
private struct HandoffNote: View {
    var restart: () -> Void = {}
    let close: () -> Void

    var body: some View {
        VStack {
            HStack(spacing: 12) {
                Image(systemName: "hand.point.up.left.fill")
                    .font(.sTitle3)
                    .foregroundStyle(.tint)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Finish signing in below").font(.sCallout.weight(.semibold))
                    Text("Simpl carries on once you’re through.").font(.sCaption).foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                Button("Start Over", action: restart)
                    .controlSize(.small)
                Button(action: close) {
                    Image(systemName: "xmark")
                        .font(.sCaption.weight(.bold))
                        .frame(width: 20, height: 20)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.borderless)
                .foregroundStyle(.secondary)
                .help("Hide")
                .accessibilityLabel("Hide")
            }
            .padding(.leading, 16)
            .padding(.trailing, 10)
            .padding(.vertical, 10)
            .frame(maxWidth: 520)
            .background(.regularMaterial, in: Capsule())
            .overlay(Capsule().strokeBorder(Theme.edge))
            .shadow(color: Theme.shadow, radius: 14, y: 5)
            .padding(.top, 12)
            Spacer()
        }
        .accessibilityElement(children: .contain)
    }
}

/// On the school's sign-in pages, top right: Start Over — the sign-in begun again from Canvas, for a page that has gone
/// wrong (a "Stale Request" after going back, a request that expired while the Mac slept).
private struct StartOverButton: View {
    let action: () -> Void

    var body: some View {
        VStack {
            HStack {
                Spacer()
                Button(action: action) {
                    Label("Start Over", systemImage: "arrow.counterclockwise")
                }
                .controlSize(.large)
                .help("Begin signing in again from the start")
                .shadow(color: Theme.shadow, radius: 10, y: 3)
            }
            .padding(14)
            Spacer()
        }
    }
}

/// A wrong password: the fields shake once, side to side, as the Mac's own login window does. Under Reduce Motion
/// they stay still (the red words say it).
private struct Shake: ViewModifier {
    let trigger: Int
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        if reduceMotion {
            content
        } else {
            content.keyframeAnimator(initialValue: 0.0, trigger: trigger) { view, x in
                view.offset(x: x)
            } keyframes: { _ in
                KeyframeTrack {
                    LinearKeyframe(-10, duration: 0.06)
                    LinearKeyframe(10, duration: 0.08)
                    LinearKeyframe(-7, duration: 0.08)
                    LinearKeyframe(4, duration: 0.07)
                    SpringKeyframe(0, duration: 0.15)
                }
            }
        }
    }
}

/// The app's own sign-in card over the school's page. What is typed goes into that page's own username and password
/// fields, and its own button is pressed; the address it goes to is shown.
private struct LoginForm: View {
    @ObservedObject var assist: LoginAssist
    let host: String
    let error: String?
    @State private var user = ""
    @State private var pass = ""
    @FocusState private var focus: Field?
    /// Counts the wrong passwords said here: each one shakes the fields once.
    @State private var shake = 0

    private enum Field { case user, pass }

    private var ready: Bool { !user.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !pass.isEmpty }

    var body: some View {
        ZStack {
            Theme.page.ignoresSafeArea()
            VStack(spacing: 18) {
                VStack(spacing: 8) {
                    Image(systemName: "lock.shield.fill")
                        .font(.system(size: 40))
                        .foregroundStyle(.tint)
                    Text("Sign in to your school")
                        .font(.sTitle2.bold())
                    Text("Signing in at \(host)")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
                .multilineTextAlignment(.center)
                VStack(spacing: 10) {
                    TextField("Username", text: $user)
                        .textContentType(.username)
                        .autocorrectionDisabled()
                        .focused($focus, equals: .user)
                        .onSubmit { focus = .pass }
                    SecureField("Password", text: $pass)
                        .textContentType(.password)
                        .focused($focus, equals: .pass)
                        .onSubmit(signIn)
                }
                .textFieldStyle(.roundedBorder)
                .controlSize(.large)
                .modifier(Shake(trigger: shake))
                Group {
                    if let error {
                        Text(error).foregroundStyle(.red)
                    } else {
                        Text("These go into your school’s own sign-in page. After you sign in, you can choose to stay logged in.")
                            .foregroundStyle(.secondary)
                    }
                }
                .font(.sCallout)
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
                VStack(spacing: 10) {
                    Button(action: signIn) {
                        Text("Sign In").frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!ready)
                    Button("Use the website instead") { assist.useWebsite() }
                        .buttonStyle(.link)
                }
            }
            .padding(28)
            .frame(width: 380)
            .card(radius: 20)
            .shadow(color: Theme.shadow, radius: 24, y: 10)
        }
        .onAppear {
            user = assist.prefillUser
            focus = user.isEmpty ? .user : .pass
            if error != nil { refused() }
        }
        .onChange(of: error) { _, e in if e != nil { refused() } }
    }

    /// The school said no: the fields shake.
    private func refused() {
        Task {
            try? await Task.sleep(nanoseconds: 250_000_000) // (once the card has faded back in)
            shake += 1
        }
    }

    private func signIn() {
        guard ready else { return }
        focus = nil
        assist.submit(user: user.trimmingCharacters(in: .whitespacesAndNewlines), pass: pass)
        pass = ""
    }
}
