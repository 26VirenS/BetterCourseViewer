import SwiftUI

/// What covers the web view while Simpl signs in (LoginAssist decides which): the app's own sign-in
/// form, when the school's page has a username and password the reader found; "Logging you in" while
/// details go into the page; and, once Canvas is reached, "Stay logged in?".
struct LoginLayer: View {
    @ObservedObject var assist: LoginAssist
    /// The web view is what is on screen (the app's own screens are not up over it).
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
        .animation(.spring(duration: 0.4, bounce: 0.15), value: assist.handoff)
        .animation(.easeOut(duration: 0.2), value: assist.onSchoolPage)
        .alert("Stay logged in?", isPresented: $assist.askStay) {
            Button("Not now", role: .cancel) { assist.stay(false) }
            Button("Stay logged in") { assist.stay(true) }
        } message: {
            Text("Simpl keeps your username and password on this iPhone only, locked in its Keychain, and signs you in when your school asks again. Two-step codes still come to you.")
        }
    }
}

/// "Logging you in": a ring going round, over the whole screen, while the page underneath is filled
/// in and sent — and, after a moment, "Show the page", for a sign-in that wants something done by hand.
struct LoggingInView: View {
    var showPage: () -> Void = {}
    var restart: () -> Void = {}
    @State private var turning = false
    @State private var offer = false

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
                .accessibilityAddTraits(.isHeader)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .overlay(alignment: .bottom) {
            if offer {
                VStack(spacing: 8) {
                    Text("Something to do on your school’s page?")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    Button {
                        Haptics.tap()
                        showPage()
                    } label: {
                        Label("Show the Page", systemImage: "safari")
                            .font(.headline)
                            .frame(maxWidth: .infinity, minHeight: 50)
                    }
                    .glassButton()
                    .buttonBorderShape(.capsule)
                    Button("Start Over") {
                        Haptics.tap()
                        restart()
                    }
                    .font(.subheadline.weight(.semibold))
                    .padding(.top, 4)
                }
                .padding(.horizontal, 24)
                .padding(.bottom, 12)
                .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .background(Color(uiColor: .systemBackground).ignoresSafeArea())
        .onAppear { turning = true }
        .task {
            // (a sign-in that goes through by itself never shows it; one that waits, a two-step code, a page the
            // reader cannot see, gets a way to the page)
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            withAnimation(.spring(duration: 0.45, bounce: 0.1)) { offer = true }
        }
    }
}

/// Over the school's page in the middle of a sign-in: what to do there, and that Simpl carries on by itself.
struct HandoffNote: View {
    var restart: () -> Void = {}
    let close: () -> Void

    var body: some View {
        VStack {
            HStack(spacing: 12) {
                Image(systemName: "hand.point.up.left.fill")
                    .font(.title3)
                    .foregroundStyle(.tint)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Finish signing in below").font(.subheadline.weight(.semibold))
                    Text("Simpl carries on once you’re through.").font(.caption).foregroundStyle(.secondary)
                }
                Spacer(minLength: 4)
                Button {
                    Haptics.tap()
                    restart()
                } label: {
                    Image(systemName: "arrow.counterclockwise").font(.footnote.weight(.bold)).frame(width: 30, height: 30)
                }
                .buttonStyle(.plain)
                .foregroundStyle(.tint)
                .accessibilityLabel("Start Over")
                Button(action: close) {
                    Image(systemName: "xmark").font(.footnote.weight(.bold)).frame(width: 30, height: 30)
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .accessibilityLabel("Hide")
            }
            .padding(.leading, 16)
            .padding(.trailing, 8)
            .padding(.vertical, 10)
            .modifier(GlassCapsule())
            .padding(.horizontal, 12)
            .padding(.top, 4)
            Spacer()
        }
        .accessibilityElement(children: .contain)
    }
}

/// On the school's sign-in pages, top right: Start Over — the sign-in begun again from Canvas, for a page that has
/// gone wrong (a "Stale Request" after going back, a request that expired while the app was away).
struct StartOverButton: View {
    let action: () -> Void

    var body: some View {
        VStack {
            HStack {
                Spacer()
                Button {
                    Haptics.tap()
                    action()
                } label: {
                    Label("Start Over", systemImage: "arrow.counterclockwise")
                        .labelStyle(.titleAndIcon)
                        .font(.subheadline.weight(.semibold))
                        .padding(.horizontal, 14)
                        .padding(.vertical, 9)
                }
                .buttonStyle(.plain)
                .foregroundStyle(.tint)
                .modifier(GlassCapsule())
                .accessibilityHint("Begins signing in again from the start")
            }
            .padding(.horizontal, 12)
            .padding(.top, 4)
            Spacer()
        }
    }
}

/// A wrong password: the fields shake once, side to side (0.44s), as the phone's own passcode does. Under
/// Reduce Motion they stay still (the error haptic and the red words say it).
struct Shake: ViewModifier {
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
                    LinearKeyframe(-8, duration: 0.06)
                    LinearKeyframe(8, duration: 0.08)
                    LinearKeyframe(-6, duration: 0.08)
                    LinearKeyframe(4, duration: 0.07)
                    SpringKeyframe(0, duration: 0.15)
                }
            }
        }
    }
}

/// Liquid Glass in a capsule on iOS 26, a material before it.
struct GlassCapsule: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOS 26.0, *) {
            content.glassEffect(.regular, in: Capsule())
        } else {
            content.background(.regularMaterial, in: Capsule())
        }
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
    /// Counts the wrong passwords said here: each one shakes the fields once.
    @State private var shake = 0

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
                    .modifier(Shake(trigger: shake))
                SecureField("Password", text: $pass)
                    .textContentType(.password)
                    .submitLabel(.go)
                    .focused($focus, equals: .pass)
                    .onSubmit(signIn)
                    .modifier(Shake(trigger: shake))
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
            if error != nil { refused() }
        }
        .onChange(of: error) { if error != nil { refused() } }
    }

    /// The school said no: an error tap under the finger and the fields shake.
    private func refused() {
        Haptics.error()
        Task {
            try? await Task.sleep(nanoseconds: 250_000_000) // (once the form has faded back in)
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
