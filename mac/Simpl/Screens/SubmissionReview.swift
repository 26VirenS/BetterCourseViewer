import AppKit
import SwiftUI
import WebKit

/// (1.3.8) Your work and what was said about it, in a popup over the assignment (1.3.79: one that never blocks the
/// window — the assignment, and every other screen, stay usable under it): on the left the file in Canvas's own
/// viewer (DocViewer: the teacher's marks drawn on it, as Canvas shows them), one file at a time where there are several;
/// on the right the comments. A file Canvas has no viewer for is shown in Quick Look; work with no file at all shows the
/// submission's own page in Canvas. (1.3.78) Escape or Close — at the bar's right end, as on every full popup — closes
/// it; with no comments there is no empty pane beside the work saying so next to the teacher's marks on it: the viewer
/// takes the width, and the bar says there are no comments on this submission.
struct SubmissionReview: View {
    let assignment: AssignmentData
    @State var picked: Attachment?
    @EnvironmentObject private var engine: Engine
    /// (1.3.79) Its popup over the window (never blocking it: Shell/FloatingPopup.swift), which Close closes.
    @Environment(\.popup) private var dismiss
    /// (1.3.79) The popup's size, as the window gives it — sized again with the window.
    @Environment(\.popupSize) private var popupSize

    /// What the popup opens on: a file, or the work as a whole.
    struct Target: Identifiable {
        let id = UUID()
        let file: Attachment?
    }

    /// Most of the window it is over: the large popup's size.
    private var size: CGSize { popupSize ?? CGSize(width: 1100, height: 720) }

    private var files: [Attachment] { assignment.submission?.files ?? [] }

    var body: some View {
        VStack(spacing: 0) {
            top
            Divider()
            HStack(spacing: 0) {
                viewer
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                if !assignment.comments.isEmpty {
                    Divider()
                    comments
                        .frame(width: min(360, max(280, size.width * 0.28)))
                }
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
            if assignment.comments.isEmpty {
                // (1.3.78) said once, in the bar, rather than an empty pane beside the teacher's marks on the work
                ViewThatFits(in: .horizontal) {
                    Label("No comments on this submission", systemImage: "text.bubble")
                    Label("No comments", systemImage: "text.bubble")
                }
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .help("Your teacher left no comments on this submission. Any marks they made on the file show on it.")
            }
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
            // (1.3.78) Close, as the quiz and Knewton Alta popups have it: the same word in the same place
            Button("Close") { dismiss() }
                .glassButton()
                .keyboardShortcut(.cancelAction)
                .help("Close the feedback")
        }
        .controlSize(.large)
        .padding(.horizontal, 20)
        .padding(.vertical, 14)
        .popupHeader() // (1.3.79: dragged by its head, the popup moves)
    }

    // MARK: The work

    @ViewBuilder
    private var viewer: some View {
        if let f = picked {
            if let p = f.preview, let url = engine.absolute(p) {
                CanvasViewer(url: url, file: f)
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
            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    ForEach(list) { c in comment(c) }
                }
                .padding(.horizontal, 18)
                .padding(.bottom, 20)
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

/// Canvas's own page for the work, with the Canvas session (its viewer, its marks), in the popup. (1.3.105) For a file
/// in DocViewer, watched until a page of it is drawn: DocViewer can sit on its spinner for good (a session that never
/// answers, a document still being made ready) — so after a while the viewer is opened again from the file's preview
/// address (a new session, as a fresh open makes), and if that one stalls too, the popup says so, with Try Again and the
/// file itself in Quick Look (without the teacher's marks), never a spinner for ever.
private struct CanvasViewer: View {
    let url: URL
    /// The file shown (DocViewer's), or nil for the submission's own page — which is not watched.
    var file: Attachment? = nil
    @StateObject private var browser = ToolBrowser()
    /// How many times the viewer has been opened afresh for this file.
    @State private var attempt = 0
    @State private var stuck = false
    @State private var plain = false

    /// How long DocViewer is given to draw a page before it is opened again, and after that before it is given up on.
    private static let patience: [Double] = [12, 20]

    /// A page drawn: DocViewer draws a document's pages as pictures (a canvas or an image of a fair size).
    private static let drawn = """
    (() => { const big = (e) => { const r = e.getBoundingClientRect(); return r.width > 160 && r.height > 160; };
      return [...document.querySelectorAll('canvas, img')].some(big); })()
    """

    var body: some View {
        if plain, let file {
            QuickLookFile(file: file)
        } else {
            ToolWebView(url: url, browser: browser, scripts: [DocViewerSkin.script()])
                .id(attempt) // (a new web view, the preview address opened again: a new session)
                .overlay(alignment: .top) {
                    if browser.loading {
                        ProgressView(value: browser.progress)
                            .progressViewStyle(.linear)
                            .tint(Theme.accent)
                            .frame(height: 2)
                    }
                }
                .overlay {
                    if stuck {
                        ContentUnavailableView {
                            Label("The viewer isn\u{2019}t loading", systemImage: "doc.text.magnifyingglass")
                        } description: {
                            Text("Canvas\u{2019}s viewer didn\u{2019}t show this file. Try again, or open the file itself (without the marks on it).")
                        } actions: {
                            HStack {
                                Button("Try Again") { stuck = false; attempt += 1 }
                                    .keyboardShortcut(.defaultAction)
                                Button("Open the File") { plain = true }
                            }
                        }
                        .background(.regularMaterial)
                    }
                }
                .task(id: attempt) { await watch() }
        }
    }

    /// Until a page is drawn: looked at every second and a half; past the patience, opened again once, then given up on.
    private func watch() async {
        guard file != nil else { return }
        let wait = Self.patience[min(attempt, Self.patience.count - 1)]
        let start = Date()
        while Date().timeIntervalSince(start) < wait {
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            if Task.isCancelled { return }
            if let web = browser.webView, (try? await web.evaluateJavaScript(Self.drawn)) as? Bool == true { return }
        }
        if Task.isCancelled { return }
        NSLog("[Simpl] feedback: DocViewer drew no page in %.0f s (try %d)", wait, attempt + 1)
        if attempt == 0 { attempt = 1 } else { stuck = true }
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
            .simpl-empty { display: none !important; }
            .simpl-bar .simpl-bar { background: transparent !important; border-bottom: 0 !important; -webkit-backdrop-filter: none; backdrop-filter: none; }
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
                  // the whole bar across the top it sits in (Canvas's grey band, the options' row with it), dressed the same
                  let top = el;
                  while (top.parentElement && top.parentElement !== document.body) {
                    const pr = top.parentElement.getBoundingClientRect();
                    if (pr.top <= 4 && pr.width >= W * .7 && pr.height <= 220) top = top.parentElement; else break;
                  }
                  top.classList.add('simpl-bar');
                  tidy(top);
                  return;
                }
                el = el.parentElement;
              }
            }
          };
          // (1.3.11) a row of the bar with nothing in it (the drawing tools' options, while the pointer is the tool) folded
          // away; it comes back the moment something is put in it
          const tidy = (bar) => {
            const bw = bar.getBoundingClientRect().width;
            const rows = [...bar.children].flatMap((c) => [c, ...c.children]);
            for (const r of rows) {
              if (/^(INPUT|SELECT|TEXTAREA|BUTTON|IMG|SVG|CANVAS)$/i.test(r.tagName)) continue;
              const was = r.classList.contains('simpl-empty');
              if (was) r.classList.remove('simpl-empty'); // (measured as it would be shown)
              const rr = r.getBoundingClientRect();
              const row = rr.width >= bw * .6 && rr.height >= 16; // (a row across the bar, not a divider or a field)
              const empty = row && !r.querySelector('button, [role="button"], input, select, textarea, svg, canvas, img') && !r.textContent.trim();
              r.classList.toggle('simpl-empty', empty);
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
