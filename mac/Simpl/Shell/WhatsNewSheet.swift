import SwiftUI

/// What changed in Simpl for Mac (1.3.3: its own notes, Support/MacWhatsNew.swift), after an update (or from Help ▸ What's New): each version's notes with their kind's symbol —
/// new, better, fixed — under the app's mark, and Continue. (home-36) The version said once: under the title for a
/// single version, else at the head of each version's notes, with its day as every date is written ("Oct 13").
/// (M16) It opens on a few highlights of the versions it is about — five at most, new before improved before fixed,
/// the newest first, no notes for developers (MacWhatsNew) — with See All Changes for every version's notes; its × at
/// the top right, where every popup's is.
struct WhatsNewSheet: View {
    let data: WhatsNewData
    /// The popup is up (What's New after an update is marked seen only then).
    var onShown: (() -> Void)? = nil
    /// (1.3.79) Its popup over the window, which Continue closes.
    @Environment(\.popup) private var dismiss
    /// (M16) Every version's notes shown, in place of the highlights.
    @State private var everything = false

    /// (M16) What it opens on: the highlights of the versions it is about.
    private var highlights: [ReleaseNote] { MacWhatsNew.highlights(data.releases) }

    var body: some View {
        VStack(spacing: 0) {
            VStack(spacing: 8) {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .frame(width: 64, height: 64)
                    .accessibilityHidden(true)
                Text("What’s New in Simpl for Mac")
                    .font(.system(size: 24, weight: .bold))
                    .tracking(-0.3)
                if let line = versionLine {
                    Text(line)
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.top, 28)
            .padding(.bottom, 16)
            .frame(maxWidth: .infinity)
            .popupHeader() // (1.3.79: dragged by its head, the popup moves)
            ScrollView {
                Group {
                    if everything {
                        allChanges
                    } else {
                        VStack(alignment: .leading, spacing: 14) {
                            ForEach(Array(highlights.enumerated()), id: \.offset) { _, note in
                                noteRow(note)
                            }
                        }
                    }
                }
                .padding(.horizontal, 32)
                .padding(.bottom, 16)
            }
            Divider()
            HStack {
                // (M16) every version's notes, and back to the highlights
                Button(everything ? "Show Highlights" : "See All Changes") {
                    withAnimation(Motion.gentle) { everything.toggle() }
                }
                .controlSize(.large)
                Spacer()
                Button("Continue") { dismiss() }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
            }
            .padding(16)
        }
        .frame(width: 500, height: 560)
        .background(PageGround())
        // (1.3.79: its ×, as Continue — (M16) at the top right, where the tools' popups and every other popup have it)
        .overlay(alignment: .topTrailing) { PopupCloseButton().padding(14) }
        .onAppear {
            said()
            onShown?()
        }
    }

    /// Under the title: the version it is about ("Version 1.3.211 · Nov 1"), or the versions ("Versions 1.3.207 to
    /// 1.3.211").
    private var versionLine: String? {
        guard let newest = data.releases.first else { return data.version.map { "Version \($0)" } }
        if data.releases.count == 1 {
            return WhatsNewSheet.day(newest).map { "Version \(newest.version) · \($0)" } ?? "Version \(newest.version)"
        }
        return "Versions \(data.releases.last?.version ?? newest.version) to \(newest.version)"
    }

    /// See All Changes: every version's notes a student sees, each under its version and its day.
    private var allChanges: some View {
        VStack(alignment: .leading, spacing: 18) {
            ForEach(MacWhatsNew.all.releases) { release in
                VStack(alignment: .leading, spacing: 10) {
                    CardHeading(text: "Version \(release.version)", trailing: WhatsNewSheet.day(release))
                    ForEach(Array(release.notes.enumerated()), id: \.offset) { _, note in
                        noteRow(note)
                    }
                }
            }
        }
    }

    private func noteRow(_ note: ReleaseNote) -> some View {
        HStack(alignment: .top, spacing: 12) {
            IconTile(symbol: icon(note.kind), color: tone(note.kind), size: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(note.title).font(.sBody.weight(.semibold))
                if let body = note.body, !body.isEmpty {
                    Text(body).font(.sCallout).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 0)
        }
    }

    /// (M16) What it opened on, on the console: how many highlights of how many notes, from which versions, how many
    /// notes for developers were left out of them, and the highlights' titles (mac-shots.sh's whatsnew_check).
    private func said() {
        let notes = data.releases.reduce(0) { $0 + $1.notes.count }
        let newest = data.releases.first?.version ?? "?"
        let oldest = data.releases.last?.version ?? newest
        NSLog("[Simpl] what's new: %ld highlights of %ld notes from %ld versions (%@ to %@), %ld for developers left out: %@",
              highlights.count, notes, data.releases.count, oldest, newest, MacWhatsNew.leftOut(from: oldest, to: newest),
              highlights.map(\.title).joined(separator: "; "))
    }

    /// (home-36) A release's day as every date is written: the engine's words where it sends them, else its ISO day as
    /// "Oct 13" ("Oct 13, 2025" from another year).
    static func day(_ release: Release) -> String? {
        if let t = release.dateText, !t.isEmpty { return t }
        guard let iso = release.date, !iso.isEmpty else { return nil }
        let parse = DateFormatter()
        parse.locale = Locale(identifier: "en_US_POSIX")
        parse.dateFormat = "yyyy-MM-dd"
        guard let d = parse.date(from: String(iso.prefix(10))) else { return iso }
        let out = DateFormatter()
        out.locale = Locale(identifier: "en_US_POSIX")
        let thisYear = Calendar.current.component(.year, from: d) == Calendar.current.component(.year, from: Date())
        out.dateFormat = thisYear ? "MMM d" : "MMM d, yyyy"
        return out.string(from: d)
    }

    private func icon(_ kind: String) -> String {
        switch kind {
        case "new": return "sparkles"
        case "fixed": return "wrench.adjustable"
        default: return "arrow.up.circle"
        }
    }

    private func tone(_ kind: String) -> Color {
        switch kind {
        case "new": return .green
        case "fixed": return .orange
        default: return .blue
        }
    }
}
