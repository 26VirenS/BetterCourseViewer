import SwiftUI
import AppKit

/// (1.3.165) Sync, as the web's sheet has it (extension/content/app/screens/calendar.js), read through the page
/// (native-app.js `calSync`): Canvas's own calendar feed handed to a calendar app — Apple's Calendar subscribes to it
/// straight away (webcal), Google's and Outlook's add-by-address pages open in the browser, or the link is copied; the
/// student's own calendars linked by their private address (the app reads the file, the page parses it: background.js
/// and lib/ics.js), each shown or hidden on the Calendar; and Send plans, which puts each plan on the Canvas calendar so
/// the feed carries it out to the calendar app with everything else.
struct SyncPopup: View {
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var data: SyncData?
    @State private var draft = ""
    @State private var linking = false
    @State private var said: String?
    @State private var saidBad = false
    /// (1.3.167) What came of a link, said under its field (the web's 2.99.109): linked, or why not.
    @State private var linkSaid: String?
    @State private var linkBad = false
    @State private var unlinking: SyncLink?
    @State private var showWhere = false

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            ScrollView {
                content
                    .padding(20)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 560)
        .frame(minHeight: 360, idealHeight: 600, maxHeight: 720)
        .task { await load() }
        .confirmationDialog("Unlink \(unlinking?.name ?? "this calendar")?", isPresented: Binding(get: { unlinking != nil }, set: { if !$0 { unlinking = nil } }), presenting: unlinking) { l in
            Button("Unlink", role: .destructive) { Task { await unlink(l) } }
        } message: { _ in
            Text("Its events leave Simpl's Calendar. The calendar itself is not touched.")
        }
    }

    private var header: some View {
        HStack(spacing: 12) {
            IconTile(symbol: "arrow.triangle.2.circlepath", color: Theme.accent, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text("Sync")
                    .font(.sTitle3.weight(.semibold))
                Text("Your Canvas work in your calendar app, and your calendars here")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            PopupCloseButton()
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 12)
        .popupHeader()
    }

    @ViewBuilder
    private var content: some View {
        if let d = data {
            VStack(alignment: .leading, spacing: 18) {
                section("Your Canvas work, in your calendar app") { feedCard(d.feed) }
                section("Your calendars, in Simpl") { linksCard(d.links) }
                DisclosureGroup("Where is my calendar's link?", isExpanded: $showWhere) {
                    VStack(alignment: .leading, spacing: 6) {
                        whereLine("Google", "Settings → your calendar → Integrate calendar → Secret address in iCal format.")
                        whereLine("iCloud", "Calendar → the share button beside the calendar → Public Calendar → Copy Link.")
                        whereLine("Outlook", "Settings → Calendar → Shared calendars → Publish a calendar → the ICS link.")
                        Text("The link stays on this Mac, and Simpl reads it every half hour. Anyone with a private link can read that calendar, so keep it to yourself.")
                            .font(.sCaption)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    .padding(.top, 6)
                }
                .font(.sCallout)
                if d.canSend {
                    section("Plans") {
                        HStack(alignment: .center, spacing: 12) {
                            IconTile(symbol: "clock", color: Theme.accent, size: 30)
                            VStack(alignment: .leading, spacing: 2) {
                                Text("Send plans to your calendar app").font(.sBody)
                                Text("Each plan goes on your \(engine.lmsName) calendar too, so the feed above carries it out with everything else.")
                                    .font(.sCaption)
                                    .foregroundStyle(.secondary)
                                    .fixedSize(horizontal: false, vertical: true)
                            }
                            Spacer(minLength: 8)
                            Toggle("Send plans to your calendar app", isOn: Binding(get: { data?.send ?? false }, set: { on in
                                data?.send = on
                                Task {
                                    _ = try? await engine.call("planSend", ["on": on], as: ActReply.self)
                                    say(on ? "Plans go to your calendar now." : "Plans stay in Simpl now.", bad: false)
                                    changed()
                                }
                            }))
                            .toggleStyle(.switch)
                            .labelsHidden()
                        }
                        .padding(14)
                        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
                    }
                }
                // (1.3.167) what Send plans did, under it (a link's outcome is said under its field)
                if let said { saidLine(said) }
                Text("Pull any piece of work to the right on To Do or the Dashboard to plan a time for it.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
        } else {
            ProgressView()
                .frame(maxWidth: .infinity, minHeight: 240)
        }
    }

    private func section<V: View>(_ title: String, @ViewBuilder _ body: () -> V) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title.uppercased())
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
            body()
        }
    }

    private func whereLine(_ name: String, _ words: String) -> some View {
        (Text("\(name): ").bold() + Text(words))
            .font(.sCallout)
            .fixedSize(horizontal: false, vertical: true)
    }

    // MARK: Canvas out

    @ViewBuilder
    private func feedCard(_ feed: String?) -> some View {
        if let feed, let links = FeedLinks(feed, name: "\(engine.lmsName) (Simpl)") {
            VStack(alignment: .leading, spacing: 10) {
                LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                    appButton("Apple Calendar", symbol: "calendar.badge.plus") { NSWorkspace.shared.open(links.apple) }
                    appButton("Google Calendar", symbol: "calendar.badge.plus") { NSWorkspace.shared.open(links.google) }
                    // (a button like the others, its two accounts a menu under the pointer: a SwiftUI Menu would not fill its cell)
                    appButton("Outlook", symbol: "calendar.badge.plus") {
                        ChoiceMenu.show([
                            ("School or Work Account (Microsoft 365)", { NSWorkspace.shared.open(links.office) }),
                            ("Personal Account (Outlook.com)", { NSWorkspace.shared.open(links.outlook) }),
                        ])
                    }
                    appButton("Copy Link", symbol: "doc.on.doc") {
                        copyToPasteboard(links.https.absoluteString)
                        say("Calendar link copied. Paste it where your calendar app adds one by address.", bad: false)
                    }
                }
                Text("Every course's work and events, kept up to date: new work and moved due dates follow on their own. Google and Outlook look for changes every few hours.")
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
        } else {
            Text("\(engine.lmsName) has no calendar feed for your account here.")
                .font(.sCallout)
                .foregroundStyle(.secondary)
        }
    }

    private func appButton(_ title: String, symbol: String, _ act: @escaping () -> Void) -> some View {
        Button(action: act) {
            Label(title, systemImage: symbol)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 14)
                .frame(height: 40)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.07)))
                .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
    }

    // MARK: Calendars in

    private func linksCard(_ links: [SyncLink]) -> some View {
        VStack(spacing: 0) {
            ForEach(links) { l in
                HStack(spacing: 12) {
                    Circle() // (1.3.167: round, as Calendar marks its calendars)
                        .fill(Color(hex: l.color))
                        .frame(width: 12, height: 12)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(l.name).font(.sBody).lineLimit(1)
                        Text(subLine(l))
                            .font(.sCaption)
                            .foregroundStyle(l.error.isEmpty ? Color.secondary : Color.red)
                            .lineLimit(2)
                    }
                    Spacer(minLength: 8)
                    Toggle("Show \(l.name) on the calendar", isOn: Binding(get: { l.on }, set: { on in
                        if let i = data?.links.firstIndex(where: { $0.id == l.id }) { data?.links[i].on = on }
                        Task {
                            _ = try? await engine.call("calShow", ["id": l.id, "on": on], as: ActReply.self)
                            changed()
                        }
                    }))
                    .toggleStyle(.switch)
                    .labelsHidden()
                    Button {
                        unlinking = l
                    } label: {
                        Image(systemName: "xmark.circle.fill").foregroundStyle(.tertiary)
                    }
                    .buttonStyle(.plain)
                    .help("Unlink \(l.name)")
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                Divider().padding(.leading, 38)
            }
            HStack(spacing: 8) {
                TextField("Paste a calendar link (.ics or webcal://)", text: $draft)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit { Task { await link() } }
                Button(linking ? "Linking…" : "Link") { Task { await link() } }
                    .disabled(linking || draft.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding(14)
            // (1.3.167) what came of it, under the field it came from: reading, linked (a tick), or why not (in red)
            if linking {
                HStack(spacing: 7) {
                    ProgressView().controlSize(.mini)
                    Text("Reading that calendar…")
                }
                .font(.sCaption)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 14)
                .padding(.top, -6)
                .padding(.bottom, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .transition(.opacity)
            } else if let linkSaid {
                HStack(alignment: .firstTextBaseline, spacing: 7) {
                    Image(systemName: linkBad ? "exclamationmark.triangle.fill" : "checkmark")
                        .font(.system(size: 11, weight: .bold))
                    Text(linkSaid)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .font(.sCaption.weight(.medium))
                .foregroundStyle(linkBad ? Color.red : Color.green)
                .padding(.horizontal, 14)
                .padding(.top, -6)
                .padding(.bottom, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .transition(.opacity)
            }
        }
        .animation(Motion.snappy, value: linking)
        .animation(Motion.snappy, value: linkSaid)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.05)))
    }

    private func subLine(_ l: SyncLink) -> String {
        if !l.error.isEmpty { return l.error }
        guard let read = l.read, let at = ISO8601DateFormatter.simplPageTime.date(from: read) ?? ISO8601DateFormatter().date(from: read) else { return "Not read yet" }
        let ago = Date().timeIntervalSince(at) < 60 ? "just now" : RelativeDateTimeFormatter().localizedString(for: at, relativeTo: Date())
        return "Read \(ago) · \(l.count == 1 ? "1 event" : "\(l.count) events")"
    }

    private func saidLine(_ text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: saidBad ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                .foregroundStyle(saidBad ? Color.orange : Color.green)
            Text(text)
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill((saidBad ? Color.orange : Color.green).opacity(0.12)))
    }

    private func say(_ text: String, bad: Bool) {
        withAnimation(Motion.snappy) {
            said = text
            saidBad = bad
        }
    }

    // MARK: Asking the page

    private func load() async {
        let got = try? await engine.call("calSync", as: SyncData.self)
        withAnimation(Motion.snappy) { data = got ?? SyncData(feed: nil, send: false, canSend: false, links: []) }
    }

    private func link() async {
        let url = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !url.isEmpty, !linking else { return }
        linking = true
        linkSaid = nil
        let r = try? await engine.call("calLink", ["url": url], as: CalLinkReply.self)
        linking = false
        if r?.ok == true {
            draft = ""
            linkSaid = "\(r?.name ?? "Your calendar") is linked: \(r?.count == 1 ? "1 event" : "\(r?.count ?? 0) events") read."
            linkBad = false
            changed()
        } else {
            linkSaid = r?.error ?? "That calendar could not be linked."
            linkBad = true
        }
        await load()
    }

    private func unlink(_ l: SyncLink) async {
        _ = try? await engine.call("calUnlink", ["id": l.id], as: ActReply.self)
        unlinking = nil
        changed()
        await load()
    }
}

/// A menu of choices under the pointer, each a closure (Sync's Outlook: which account).
@MainActor
enum ChoiceMenu {
    private final class Item: NSMenuItem {
        let act: () -> Void
        init(_ title: String, _ act: @escaping () -> Void) {
            self.act = act
            super.init(title: title, action: #selector(run), keyEquivalent: "")
            target = self
        }
        required init(coder: NSCoder) { fatalError("not from a nib") }
        @objc private func run() { act() }
    }

    static func show(_ choices: [(String, () -> Void)]) {
        let menu = NSMenu()
        for (title, act) in choices { menu.addItem(Item(title, act)) }
        _ = menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }
}

/// What linking a calendar answers: its name and how many events it holds, or why not.
struct CalLinkReply: Codable {
    var ok: Bool?
    var name: String?
    var count: Int?
    var error: String?
}

/// Canvas's calendar feed as each calendar app takes one: Apple's by webcal, Google's and Outlook's add-by-address pages.
struct FeedLinks {
    let https: URL
    let apple: URL
    let google: URL
    let office: URL
    let outlook: URL

    init?(_ feed: String, name: String) {
        let plain = feed.replacingOccurrences(of: "^webcal:", with: "https:", options: .regularExpression)
        let webcal = plain.replacingOccurrences(of: "^https?:", with: "webcal:", options: .regularExpression)
        let enc = { (s: String) in s.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? s }
        guard let h = URL(string: plain), let a = URL(string: webcal),
              let g = URL(string: "https://calendar.google.com/calendar/r?cid=\(enc(webcal))"),
              let o = URL(string: "https://outlook.office.com/calendar/0/addfromweb?url=\(enc(plain))&name=\(enc(name))"),
              let l = URL(string: "https://outlook.live.com/calendar/0/addfromweb?url=\(enc(plain))&name=\(enc(name))") else { return nil }
        https = h
        apple = a
        google = g
        office = o
        outlook = l
    }
}

extension ISO8601DateFormatter {
    /// "2026-10-25T16:00:00.000Z", as the page writes a time.
    static let simplPageTime: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
}
