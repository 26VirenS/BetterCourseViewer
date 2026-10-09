import AppKit
import SwiftUI
import WebKit

/// (1.3.8) Your work and what was said about it, in a popup over the assignment: on the left the file in Canvas's own
/// viewer (DocViewer: the teacher's marks drawn on it, as Canvas shows them), one file at a time where there are several;
/// on the right the comments. A file Canvas has no viewer for is shown in Quick Look; work with no file at all shows the
/// submission's own page in Canvas. Escape or Done closes it.
struct SubmissionReview: View {
    let assignment: AssignmentData
    @State var picked: Attachment?
    @EnvironmentObject private var engine: Engine
    @Environment(\.dismiss) private var dismiss
    @State private var size = SubmissionReview.idealSize()

    /// What the popup opens on: a file, or the work as a whole.
    struct Target: Identifiable {
        let id = UUID()
        let file: Attachment?
    }

    /// Most of the window it opens over, as the quiz's sheet is (a sheet keeps to its least width, so it is set exactly).
    private static func idealSize() -> CGSize {
        let windows = NSApp.windows.filter { $0.isVisible && $0.sheetParent == nil && !($0 is NSPanel) }
        guard let w = NSApp.mainWindow ?? windows.max(by: { $0.frame.width < $1.frame.width }), w.sheetParent == nil else {
            return CGSize(width: 1100, height: 720)
        }
        let room = w.contentLayoutRect.size
        return CGSize(width: min(max(room.width - 64, 760), 1500), height: min(max(room.height - 36, 520), 1000))
    }

    private var files: [Attachment] { assignment.submission?.files ?? [] }

    var body: some View {
        VStack(spacing: 0) {
            top
            Divider()
            HStack(spacing: 0) {
                viewer
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                Divider()
                comments
                    .frame(width: min(360, max(280, size.width * 0.28)))
            }
        }
        .frame(width: size.width, height: size.height)
        .background(PageGround())
        .onAppear { if picked == nil { picked = files.first } }
    }

    // MARK: The top

    private var top: some View {
        HStack(spacing: 12) {
            IconTile(symbol: "text.bubble.fill", color: Color(hex: assignment.color), size: 30)
            VStack(alignment: .leading, spacing: 1) {
                Text("Feedback")
                    .font(.sHeadline)
                Text(assignment.title)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 12)
            if files.count > 1 {
                Picker("File", selection: Binding(get: { picked?.id ?? files.first?.id ?? "" }, set: { id in picked = files.first { $0.id == id } })) {
                    ForEach(files) { f in Text(f.name).tag(f.id) }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                .frame(maxWidth: 260)
                .help("Which file to look at")
            }
            if let f = picked {
                Menu {
                    FileMenuItems(engine: engine, url: f.url, name: f.name)
                } label: {
                    Image(systemName: "ellipsis")
                }
                .menuStyle(.button)
                .menuIndicator(.hidden)
                .fixedSize()
                .help("More for \(f.name)")
            }
            Button("Done") { dismiss() }
                .glassButton(prominent: true)
                .keyboardShortcut(.cancelAction)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
    }

    // MARK: The work

    @ViewBuilder
    private var viewer: some View {
        if let f = picked {
            if let p = f.preview, let url = engine.absolute(p) {
                CanvasViewer(url: url)
                    .id(url)
            } else {
                QuickLookFile(file: f)
                    .id(f.id)
            }
        } else if let v = assignment.submission?.viewer, let url = engine.absolute(v) {
            CanvasViewer(url: url)
                .id(url)
        } else if let text = assignment.submission?.text, !text.isEmpty {
            ScrollView {
                Text(text)
                    .font(.sBody)
                    .textSelection(.enabled)
                    .frame(maxWidth: 760, alignment: .leading)
                    .padding(28)
                    .frame(maxWidth: .infinity)
            }
        } else {
            ContentUnavailableView("Nothing to show", systemImage: "doc", description: Text("This work has no file to look at."))
        }
    }

    // MARK: The comments

    private var comments: some View {
        let list = assignment.comments
        return VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Comments")
                    .font(.sHeadline)
                Spacer()
                if !list.isEmpty {
                    Text("\(list.count)")
                        .font(.sCallout.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 14)
            if list.isEmpty {
                EmptyNote(text: "No comments yet.", symbol: "text.bubble")
                    .padding(.horizontal, 18)
                Spacer()
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        ForEach(list) { c in comment(c) }
                    }
                    .padding(.horizontal, 18)
                    .padding(.bottom, 20)
                }
            }
        }
    }

    private func comment(_ c: CommentRow) -> some View {
        HStack(alignment: .top, spacing: 10) {
            PersonAvatar(name: c.author, avatar: c.avatar, size: 28)
            VStack(alignment: .leading, spacing: 4) {
                HStack(alignment: .firstTextBaseline, spacing: 6) {
                    Text(c.author)
                        .font(.sCallout.weight(.semibold))
                        .lineLimit(1)
                    if let a = c.attempt, a > 0 {
                        Text("Attempt \(a)")
                            .font(.sCaption)
                            .foregroundStyle(.tertiary)
                    }
                }
                if let w = c.when, !w.isEmpty {
                    Text(w)
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
                if !c.text.isEmpty {
                    Text(c.text.trimmingCharacters(in: .whitespacesAndNewlines))
                        .font(.sCallout)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                        .padding(.top, 2)
                }
                ForEach(c.attachments ?? []) { f in
                    Button { engine.openFile(f.url, name: f.name) } label: {
                        HStack(spacing: 6) {
                            FileIcon(name: f.name, size: 16)
                            Text(f.name).lineLimit(1).truncationMode(.middle)
                        }
                    }
                    .buttonStyle(.link)
                    .font(.sCallout)
                    .help("Open \(f.name) in Quick Look")
                }
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// Canvas's own page for the work, with the Canvas session (its viewer, its marks), in the popup.
private struct CanvasViewer: View {
    let url: URL
    @StateObject private var browser = ToolBrowser()

    var body: some View {
        ToolWebView(url: url, browser: browser, scripts: [DocViewerSkin.script()])
            .overlay(alignment: .top) {
                if browser.loading {
                    ProgressView(value: browser.progress)
                        .progressViewStyle(.linear)
                        .tint(Theme.accent)
                        .frame(height: 2)
                }
            }
    }
}

/// A file Canvas has no viewer for, fetched with the session and shown in Quick Look.
private struct QuickLookFile: View {
    let file: Attachment
    @EnvironmentObject private var engine: Engine
    @State private var local: URL?
    @State private var problem: String?

    var body: some View {
        Group {
            if let local {
                QuickLookView(file: local)
            } else if let problem {
                ContentUnavailableView {
                    Label("No Preview", systemImage: "exclamationmark.triangle")
                } description: {
                    Text(problem)
                } actions: {
                    Button("Try Again") { Task { await fetch() } }
                }
            } else {
                ProgressView("Loading \(file.name)…")
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .task { if local == nil { await fetch() } }
    }

    private func fetch() async {
        problem = nil
        do {
            local = try await engine.fetchFile(file.url, name: file.name)
        } catch is CancellationError {
            return
        } catch {
            problem = error.localizedDescription
        }
    }
}

/// (1.3.10) Canvas's file viewer (DocViewer) dressed to sit in a Mac window: its toolbar becomes a translucent bar in
/// the system's font, its buttons rounded and lit under the pointer, the tool in use in the accent, its dashed rules
/// hairlines, its page field a rounded field — light or dark with the Mac. DocViewer's markup is its own and unnamed,
/// so the bar is found by its shape (a bar across the top of the page holding several buttons), looked for again as the
/// viewer draws itself; a viewer it does not find is left exactly as Canvas draws it.
enum DocViewerSkin {
    @MainActor
    static func script() -> WKUserScript {
        let accent = AppearanceStore.shared.accentHex ?? "#0a84ff"
        let source = """
        (() => {
          if (window.__simplSkin) return; window.__simplSkin = true;
          const accent = \(String(reflecting: accent));
          const css = `
            .simpl-bar { background: rgba(246,246,248,.82) !important; -webkit-backdrop-filter: blur(24px) saturate(180%); backdrop-filter: blur(24px) saturate(180%);
              border: 0 !important; border-bottom: 1px solid rgba(0,0,0,.1) !important; box-shadow: none !important; color: rgba(0,0,0,.82) !important; }
            .simpl-bar, .simpl-bar * { font-family: -apple-system, BlinkMacSystemFont, "SF Pro Text", sans-serif !important; letter-spacing: 0 !important; text-transform: none !important; }
            .simpl-bar button, .simpl-bar [role="button"], .simpl-bar a { border-radius: 8px !important; background: transparent !important; color: inherit !important;
              box-shadow: none !important; transition: background-color .12s ease, color .12s ease; }
            .simpl-bar button:hover, .simpl-bar [role="button"]:hover, .simpl-bar a:hover { background: rgba(0,0,0,.07) !important; }
            .simpl-bar button:active, .simpl-bar [role="button"]:active { background: rgba(0,0,0,.12) !important; }
            .simpl-bar :is(button, [role="button"])[aria-pressed="true"], .simpl-bar :is(button, [role="button"])[aria-checked="true"],
            .simpl-bar :is(button, [role="button"])[aria-selected="true"], .simpl-bar .simpl-on { background: ${accent} !important; color: #fff !important; }
            .simpl-bar :is(button, [role="button"])[aria-pressed="true"] *, .simpl-bar .simpl-on * { color: #fff !important; fill: currentColor; }
            .simpl-bar input { border-radius: 7px !important; border: 1px solid rgba(0,0,0,.14) !important; background: #fff !important; color: rgba(0,0,0,.85) !important;
              box-shadow: inset 0 .5px 1px rgba(0,0,0,.06) !important; text-align: center; }
            .simpl-bar input:focus { outline: 3px solid color-mix(in srgb, ${accent} 45%, transparent) !important; outline-offset: 0; }
            .simpl-bar .simpl-rule { border-style: solid !important; border-color: rgba(0,0,0,.12) !important; }
            @media (prefers-color-scheme: dark) {
              .simpl-bar { background: rgba(40,40,44,.82) !important; border-bottom-color: rgba(255,255,255,.08) !important; color: rgba(255,255,255,.88) !important; }
              .simpl-bar button:hover, .simpl-bar [role="button"]:hover, .simpl-bar a:hover { background: rgba(255,255,255,.1) !important; }
              .simpl-bar button:active, .simpl-bar [role="button"]:active { background: rgba(255,255,255,.16) !important; }
              .simpl-bar input { background: rgba(255,255,255,.08) !important; border-color: rgba(255,255,255,.14) !important; color: rgba(255,255,255,.9) !important; }
              .simpl-bar .simpl-rule { border-color: rgba(255,255,255,.12) !important; }
            }`;
          const put = () => { if (document.head && !document.getElementById('simpl-skin')) { const st = document.createElement('style'); st.id = 'simpl-skin'; st.textContent = css; document.head.appendChild(st); } };
          // the bar: across the top of the page, short, holding three buttons or more
          const find = () => {
            put();
            const W = window.innerWidth || 1;
            for (const b of document.querySelectorAll('button, [role="button"]')) {
              let el = b.parentElement;
              while (el && el !== document.body) {
                const r = el.getBoundingClientRect();
                if (r.top <= 4 && r.width >= W * .7 && r.height >= 30 && r.height <= 160 && el.querySelectorAll('button, [role="button"]').length >= 3) {
                  if (!el.classList.contains('simpl-bar')) {
                    el.classList.add('simpl-bar');
                    for (const x of el.querySelectorAll('*')) { const cs = getComputedStyle(x); if (/dashed|dotted/.test(cs.borderLeftStyle + cs.borderRightStyle + cs.borderTopStyle + cs.borderBottomStyle)) x.classList.add('simpl-rule'); }
                  }
                  return;
                }
                el = el.parentElement;
              }
            }
          };
          let t = 0;
          const soon = () => { clearTimeout(t); t = setTimeout(find, 120); };
          const go = () => { find(); new MutationObserver(soon).observe(document.documentElement, { childList: true, subtree: true }); };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', go, { once: true }); else go();
        })();
        """
        return WKUserScript(source: source, injectionTime: .atDocumentEnd, forMainFrameOnly: false)
    }
}
