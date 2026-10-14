import SwiftUI

/// What covers the window while Simpl signs in (LoginAssist decides which): the app's own sign-in card, when the
/// school's page has a username and password the reader found; "Logging you in" while they go into the page; a note
/// over the school's page while something there is the student's to do; and, once Canvas is reached, "Stay logged in?".
struct LoginLayer: View {
    @ObservedObject var assist: LoginAssist
    /// The school's page is what the window shows (the app's own screens are not up over it).
    var pageShown = true

    private static let shotLoggingIn = UserDefaults.standard.bool(forKey: "SimplShowLoggingIn")

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
            // (the screenshot suite: -SimplShowLoggingIn YES puts "Logging you in" over the window for the run, its
            // "Still signing in…" offer coming after a moment as it does; its buttons do nothing)
            if LoginLayer.shotLoggingIn {
                LoggingInView()
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
/// after a moment, (onboarding-9) why it may be taking a while and the one thing to do about it: the school's sign-in
/// page opened, where a code or a click is most often waiting; Start Over beside it.
private struct LoggingInView: View {
    var showPage: () -> Void = {}
    var restart: () -> Void = {}
    @State private var offer = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            PageGround().ignoresSafeArea()
            VStack(spacing: 16) {
                ProgressView()
                    .controlSize(.large)
                Text(offer ? "Still signing in…" : "Logging you in")
                    .font(.sTitle3.weight(.semibold))
                    .contentTransition(.opacity)
                    .accessibilityAddTraits(.isHeader)
                if offer {
                    VStack(spacing: 12) {
                        Text("Your school may be asking for a code or a click.")
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                        HStack(spacing: 10) {
                            Button("Start Over", action: restart)
                                .controlSize(.large)
                            Button(action: showPage) {
                                Label("Open Sign-In Page", systemImage: "safari")
                            }
                            .buttonStyle(.borderedProminent)
                            .controlSize(.large)
                            .keyboardShortcut(.defaultAction)
                        }
                    }
                    .transition(reduceMotion ? .opacity : .move(edge: .bottom).combined(with: .opacity))
                }
            }
            .frame(maxWidth: 420)
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
/// fields, and its own button is pressed. (onboarding-13) It names the school the student chose ("Sign in to University
/// of California Merced"), with the address the details go to under it in grey; (onboarding-2) a school chosen wrongly
/// is changed from here.
private struct LoginForm: View {
    @ObservedObject var assist: LoginAssist
    let host: String
    let error: String?
    @EnvironmentObject private var session: AppSession
    @Environment(\.colorScheme) private var scheme
    @State private var user = ""
    @State private var pass = ""
    @FocusState private var focus: Field?
    /// Counts the wrong passwords said here: each one shakes the fields once.
    @State private var shake = 0

    private enum Field { case user, pass }

    private var ready: Bool { !user.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !pass.isEmpty }

    /// The school's name as the search listed it when it was chosen (none for an address typed by hand).
    private var school: String? { SchoolNames.name(for: session.host) }

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 20, style: .continuous)
        ZStack {
            PageGround().ignoresSafeArea()
            VStack(spacing: 18) {
                VStack(spacing: 8) {
                    Image(systemName: "lock.shield.fill")
                        .font(.system(size: 40))
                        .foregroundStyle(.tint)
                    Text(school.map { "Sign in to \($0)" } ?? "Sign in to your school")
                        .font(.sTitle2.bold())
                        .lineLimit(3)
                        .fixedSize(horizontal: false, vertical: true)
                    Text("at \(host)")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .help("Your username and password go into the sign-in page at \(host).")
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
                // (onboarding-14, onboarding-15) one short line — the dialog after it says the rest — that fits the
                // card on one line, never a word alone on a second
                Group {
                    if let error {
                        Text(error).foregroundStyle(.red)
                    } else {
                        Text("Simpl types these into your school’s page.")
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
                    // (onboarding-13) says which page: the school's own, full-window, not Simpl's site or Canvas's
                    Button("Sign In on My School’s Own Page") { assist.useWebsite() }
                        .buttonStyle(.link)
                    // (onboarding-2) the most likely first-run slip, put right where it is noticed (Simpl ▸ Change
                    // School… does the same)
                    HStack(spacing: 4) {
                        Text("Not your school?")
                            .foregroundStyle(.secondary)
                        Button("Change School…") { session.changeSchool() }
                            .buttonStyle(.link)
                    }
                    .font(.sCallout)
                }
            }
            .padding(28)
            .frame(width: 380)
            // (onboarding-15) in dark, a card a shade lighter than the page with an edge, so it reads as a card and
            // the fields do not float; in light, the card as every other
            .background { if scheme == .dark { shape.fill(Color.white.opacity(0.06)) } }
            .card(radius: 20)
            .overlay { if scheme == .dark { shape.strokeBorder(Color.white.opacity(0.14), lineWidth: 1) } }
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
