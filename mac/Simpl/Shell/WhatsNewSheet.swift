import SwiftUI

/// What changed in Simpl for Mac (1.3.3: its own notes, Support/MacWhatsNew.swift), after an update (or from Help ▸ What's New): each version's notes with their kind's symbol —
/// new, better, fixed — under the app's mark, and Continue. (home-36) The version said once: under the title for a
/// single version, else at the head of each version's notes, with its day as every date is written ("Oct 13").
struct WhatsNewSheet: View {
    let data: WhatsNewData
    /// The popup is up (What's New after an update is marked seen only then).
    var onShown: (() -> Void)? = nil
    /// (1.3.79) Its popup over the window, which Continue closes.
    @Environment(\.popup) private var dismiss

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
                if data.releases.count <= 1, let v = data.version ?? data.releases.first?.version {
                    let day = data.releases.first.flatMap(WhatsNewSheet.day)
                    Text(day.map { "Version \(v) · \($0)" } ?? "Version \(v)")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.top, 28)
            .padding(.bottom, 16)
            .frame(maxWidth: .infinity)
            .popupHeader() // (1.3.79: dragged by its head, the popup moves)
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    ForEach(data.releases) { release in
                        VStack(alignment: .leading, spacing: 10) {
                            if data.releases.count > 1 {
                                CardHeading(text: "Version \(release.version)", trailing: WhatsNewSheet.day(release))
                            }
                            ForEach(release.notes, id: \.self) { note in
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
                        }
                    }
                }
                .padding(.horizontal, 32)
                .padding(.bottom, 16)
            }
            Divider()
            HStack {
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
        .overlay(alignment: .topLeading) { PopupCloseButton().padding(14) } // (1.3.79: its ×, as Continue)
        .onAppear { onShown?() }
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
