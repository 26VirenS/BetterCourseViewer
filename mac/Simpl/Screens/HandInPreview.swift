import AppKit
import SwiftUI

/// (1.3.2) A file chosen for Hand In, looked at before it goes: Quick Look's own view of it in a pane that opens at the
/// sheet's right, the sheet widening for it — a sheet stays centred on its window, so the form moves left as the
/// preview comes in (no window of its own beside the sheet, which ran off the screen). The same file pressed again, the
/// pane's ✕, the file taken out or the sheet closed puts it away.
@MainActor
final class HandInPreview: ObservableObject {
    static let shared = HandInPreview()

    /// The file being looked at, and where Quick Look reads it.
    @Published private(set) var showing: UUID?
    @Published private(set) var file: URL?
    @Published private(set) var name = ""
    private var folder: URL?

    static let width: CGFloat = 540

    /// A file's row pressed: its preview opened in the sheet, or — the one already open — put away.
    func toggle(_ f: PickedFile) {
        if showing == f.id {
            close()
            return
        }
        guard let url = write(f) else { return }
        withAnimation(Motion.gentle) {
            showing = f.id
            file = url
            name = f.name
        }
    }

    func close(animated: Bool = true) {
        let put = {
            self.showing = nil
            self.file = nil
        }
        if animated { withAnimation(Motion.gentle, put) } else { put() }
        clean()
    }

    /// A file taken out of the hand-in: its preview, if it is the one open, put away.
    func removed(_ f: PickedFile) {
        if showing == f.id { close() }
    }

    /// The file written where Quick Look can read it, under its own name (a folder of its own each time).
    private func write(_ f: PickedFile) -> URL? {
        clean()
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("SimplHandInPreview-\(f.id.uuidString)", isDirectory: true)
        do {
            try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let n = f.name.replacingOccurrences(of: "/", with: "-")
            let url = dir.appendingPathComponent(n.isEmpty ? "File" : n)
            try f.data.write(to: url, options: .atomic)
            folder = dir
            return url
        } catch {
            return nil
        }
    }

    private func clean() {
        if let folder { try? FileManager.default.removeItem(at: folder) }
        folder = nil
    }
}

/// The preview pane: the file's name and ✕ over Quick Look's view of it.
struct HandInPreviewPane: View {
    @ObservedObject var preview: HandInPreview

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Image(systemName: "eye")
                    .foregroundStyle(.secondary)
                Text(preview.name)
                    .font(.sHeadline)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Spacer(minLength: 8)
                Button { preview.close() } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 16))
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .keyboardShortcut("w", modifiers: [.command, .shift])
                .help("Close the preview")
                .accessibilityLabel("Close the preview")
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 12)
            Divider()
            if let url = preview.file {
                QuickLookView(file: url)
                    .id(url)
            }
        }
        .frame(width: HandInPreview.width)
        .background(Color(nsColor: .textBackgroundColor).opacity(0.35))
    }
}
