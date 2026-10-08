import SwiftUI

/// What changed in Simpl, after an update (or from Help ▸ What's New): each version's notes with their kind's symbol —
/// new, better, fixed — under the app's mark, and Continue.
struct WhatsNewSheet: View {
    let data: WhatsNewData
    /// The sheet is up (What's New after an update is marked seen only then).
    var onShown: (() -> Void)? = nil
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            VStack(spacing: 8) {
                Image(nsImage: NSApp.applicationIconImage)
                    .resizable()
                    .frame(width: 64, height: 64)
                    .accessibilityHidden(true)
                Text("What’s New in Simpl")
                    .font(.system(size: 24, weight: .bold))
                    .tracking(-0.3)
                if let v = data.version ?? data.releases.first?.version {
                    Text("Version \(v)").font(.sCallout).foregroundStyle(.secondary)
                }
            }
            .padding(.top, 28)
            .padding(.bottom, 16)
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    ForEach(data.releases) { release in
                        VStack(alignment: .leading, spacing: 10) {
                            if data.releases.count > 1 {
                                CardHeading(text: "Version \(release.version)", trailing: release.date)
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
        .background(Theme.page)
        .onAppear { onShown?() }
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
