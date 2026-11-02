import AppKit
import SwiftUI
import WebKit

/// Export Questions, in a publisher's popup (Macmillan Learning, ALEKS, Knewton Alta): a picture of every question the
/// activity lets the popup open, in one zip in Downloads — "Week 3 Homework questions.zip", holding "Question 1.png",
/// "Question 2.png" and on. Macmillan's assignment lists its questions, so each is opened in turn, pictured as it is on
/// screen, and the one that was open is opened again at the end; ALEKS and Alta give one question at a time (each comes
/// from the answer before), so theirs is the one on screen. A picture is the tool's page as it shows the question, laid
/// on the popup's own ground (the page is clear over the popup's glass), at the screen's own sharpness.
@MainActor
final class QuestionExport: ObservableObject {
    /// While it runs: how many are pictured, of how many.
    @Published private(set) var running = false
    @Published private(set) var done = 0
    @Published private(set) var count = 0
    /// Its end: the zip saved, or why there is none.
    @Published var saved: URL?
    @Published var failed: String?

    /// "Exporting 2 of 4…", for the button's tooltip and its place in the header.
    var progressText: String { count > 1 ? "Exporting \(min(done + 1, count)) of \(count)…" : "Exporting…" }

    /// One question opened and on screen: false when it never came (it is left out, and said so).
    typealias Open = (Int) async -> Bool

    /// Pictures each of `questions` (opening each first with `open`), then zips them into Downloads.
    func run(title: String, tool: String, web: WKWebView?, ground: NSColor, questions: [Int], open: @escaping Open) {
        guard !running, let web else { return }
        let list = questions.isEmpty ? [0] : questions
        running = true
        done = 0
        count = list.count
        saved = nil
        failed = nil
        Task { @MainActor in
            let fm = FileManager.default
            let name = Self.fileName(title)
            let work = fm.temporaryDirectory.appendingPathComponent("simpl-questions-\(UUID().uuidString)", isDirectory: true)
            let folder = work.appendingPathComponent("\(name) questions", isDirectory: true)
            defer { try? fm.removeItem(at: work) }
            var pictured = 0
            var missed: [Int] = []
            do { try fm.createDirectory(at: folder, withIntermediateDirectories: true) } catch {
                finish(failed: "A folder for the pictures could not be made.", tool: tool)
                return
            }
            for (i, q) in list.enumerated() {
                done = i
                if q > 0, await !open(q) { missed.append(q); continue }
                // (1.3.214: the whole question, however far it scrolls — QuestionShot)
                guard let shot = await QuestionShot.picture(web, ground: ground) else { missed.append(q); continue }
                let label = q > 0 ? "Question \(q)" : "Question"
                do {
                    try shot.png.write(to: folder.appendingPathComponent("\(label).png"))
                    pictured += 1
                    if shot.views > 1 {
                        NSLog("[Simpl] export questions: %@ pictured whole, %ld views laid together (%ld px tall, the view %ld)",
                              label.lowercased(), shot.views, shot.height, shot.viewHeight)
                    }
                } catch { missed.append(q) }
            }
            done = list.count
            guard pictured > 0 else {
                finish(failed: "No question could be pictured.", tool: tool)
                return
            }
            let downloads = fm.urls(for: .downloadsDirectory, in: .userDomainMask).first
                ?? fm.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
            let zip = PhoneLAN.unique("\(name) questions.zip", in: downloads)
            // (ditto, as the updater unpacks with it: the folder whole, its name the zip's top folder)
            let status = await Task.detached { ShellCommand.run("/usr/bin/ditto", ["-c", "-k", "--keepParent", folder.path, zip.path]) }.value
            guard status == 0, fm.fileExists(atPath: zip.path) else {
                finish(failed: "The zip could not be saved in Downloads.", tool: tool)
                return
            }
            NSLog("[Simpl] export questions: %ld of %ld pictured, saved to %@ (%@)%@", pictured, list.count, zip.lastPathComponent, tool,
                  missed.isEmpty ? "" : " — not pictured: \(missed.map(String.init).joined(separator: ", "))")
            saved = zip
            running = false
        }
    }

    private func finish(failed why: String, tool: String) {
        NSLog("[Simpl] export questions: none saved (%@): %@", tool, why)
        failed = why
        running = false
    }

    /// The assignment's name made safe for a file name.
    static func fileName(_ title: String) -> String {
        let bad = CharacterSet(charactersIn: "/\\:<>\"|?*").union(.controlCharacters)
        var s = String(String.UnicodeScalarView(title.unicodeScalars.filter { !bad.contains($0) }))
        s = s.trimmingCharacters(in: CharacterSet.whitespaces.union(CharacterSet(charactersIn: ".")))
        if s.count > 80 { s = String(s.prefix(80)).trimmingCharacters(in: .whitespaces) }
        return s.isEmpty ? "Simpl" : s
    }
}

/// The header's Export Questions button: a picture of every question, zipped into Downloads; while it works, a spinner
/// and "Exporting 2 of 4…"; at its end, where the zip is (Show in Finder) or why there is none.
struct ExportQuestionsButton: View {
    @ObservedObject var export: QuestionExport
    let enabled: Bool
    let help: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            if export.running {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text(export.count > 1 ? "\(min(export.done + 1, export.count)) of \(export.count)" : "Exporting")
                        .font(.sCaption)
                        .monospacedDigit()
                }
            } else {
                Label("Export Questions", systemImage: "square.and.arrow.down.on.square")
                    .labelStyle(.iconOnly)
            }
        }
        .glassButton()
        .disabled(!enabled || export.running)
        .help(export.running ? export.progressText : help)
        .accessibilityLabel(export.running ? export.progressText : "Export Questions")
        .alert("Questions Exported", isPresented: Binding(get: { export.saved != nil }, set: { if !$0 { export.saved = nil } })) {
            Button("Show in Finder") { if let s = export.saved { NSWorkspace.shared.activateFileViewerSelecting([s]) } }
            Button("OK", role: .cancel) {}
        } message: {
            Text("A picture of each question is in Downloads, in “\(export.saved?.lastPathComponent ?? "")”.")
        }
        .alert("Questions Not Exported", isPresented: Binding(get: { export.failed != nil }, set: { if !$0 { export.failed = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(export.failed ?? "")
        }
    }
}
