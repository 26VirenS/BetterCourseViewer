import UIKit

/// Which Canvas addresses the app draws itself (1.2): a course and what is in it — its home,
/// announcements, discussions and a discussion, assignments and an assignment, modules, pages and a
/// page, files, people, quizzes, the syllabus, its grades — a group and what is in it, the list of
/// groups, and the Inbox. A quiz opens in the app's own quiz screen and an external tool in its sheet; everything
/// else (a file, a page of the interface asked for by name with ?bcv=) stays the web interface's screen.
extension Engine {
    func nativeRoute(for raw: String, title: String) -> Route? {
        guard let url = absolute(raw), let host = url.host?.lowercased(), host == web.baseURL.host?.lowercased() else { return nil }
        if let q = url.query, q.contains("bcv=") || q.contains("display=") { return nil } // (a screen asked for by name, a frame's view)
        let p = url.path.split(separator: "/").map(String.init)
        guard let first = p.first else { return nil }
        if first == "conversations" { return p.count == 1 ? .inbox : nil }
        if p == ["groups"] { return .groups }
        guard p.count >= 2, first == "courses" || first == "groups", Engine.numeric(p[1]) else { return nil }
        let id = p[1]
        let ctx = "\(first)/\(id)"
        let isCourse = first == "courses"
        if p.count == 2 { return isCourse ? .course(id: id) : .group(id: id) }
        let rest = Array(p.dropFirst(2))
        let one = rest.count == 1
        switch rest[0] {
        case "announcements":
            return one ? .section(ctx: ctx, kind: "announcements") : nil
        case "discussion_topics":
            if one { return .section(ctx: ctx, kind: "discussions") }
            return rest.count == 2 && Engine.numeric(rest[1]) ? .topic(ctx: ctx, id: rest[1]) : nil
        case "assignments" where isCourse:
            if one { return .section(ctx: ctx, kind: "assignments") }
            if rest.count == 2 && rest[1] == "syllabus" { return .section(ctx: ctx, kind: "syllabus") }
            return rest.count == 2 && Engine.numeric(rest[1]) ? .assignment(course: id, id: rest[1]) : nil
        case "modules":
            return one ? .section(ctx: ctx, kind: "modules") : nil
        case "pages":
            if one { return .section(ctx: ctx, kind: "pages") }
            return rest.count == 2 ? .page(ctx: ctx, slug: rest[1]) : nil
        case "wiki":
            return one ? .page(ctx: ctx, slug: "") : nil
        case "files":
            return one ? .section(ctx: ctx, kind: "files") : nil
        case "users":
            return one ? .section(ctx: ctx, kind: "people") : nil
        case "quizzes" where isCourse:
            return one ? .section(ctx: ctx, kind: "quizzes") : nil
        case "grades" where isCourse:
            return one ? .section(ctx: ctx, kind: "grades") : nil
        default:
            return nil
        }
    }

    static func numeric(_ s: String) -> Bool { !s.isEmpty && s.allSatisfy(\.isNumber) }

    /// A course's own external tool (…/courses/1/external_tools/9): opened in the tool sheet, not a screen.
    func toolLaunch(for raw: String, title: String) -> ToolLaunch? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        let p = url.path.split(separator: "/").map(String.init)
        guard p.count == 4, p[0] == "courses", Engine.numeric(p[1]), p[2] == "external_tools", Engine.numeric(p[3]) else { return nil }
        return .courseTool(course: p[1], id: p[3], title: title.isEmpty ? "Tool" : title)
    }

    /// A Classic quiz (…/courses/1/quizzes/9, or its take page): the app's own quiz screen, over everything.
    /// Canvas's own quiz page asked for by name (?bcv=…) stays the web screen.
    func quizLaunch(for raw: String, title: String) -> QuizLaunch? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        if let q = url.query, q.contains("bcv=") || q.contains("display=") { return nil }
        let p = url.path.split(separator: "/").map(String.init)
        guard p.count == 4 || (p.count == 5 && p[4] == "take"), p[0] == "courses", Engine.numeric(p[1]), p[2] == "quizzes", Engine.numeric(p[3]) else { return nil }
        return QuizLaunch(course: p[1], quiz: p[3], title: title.isEmpty ? "Quiz" : title)
    }

    /// A course file's own address (…/files/12/download): it opens in the phone's viewer, not a screen.
    static func isDownload(_ url: URL) -> Bool {
        url.path.contains("/files/") && (url.path.hasSuffix("/download") || url.query?.contains("download") == true)
    }

    private func isCanvas(_ url: URL) -> Bool {
        let scheme = url.scheme?.lowercased() ?? ""
        return (scheme == "http" || scheme == "https") && url.host?.lowercased() == web.baseURL.host?.lowercased()
    }

    /// A link pressed in a native screen's text: one of the school's addresses goes through the app's
    /// screens, another site opens in an in-app Safari sheet, anything else (mailto:, tel:) in its app.
    func openLink(_ url: URL) {
        if isCanvas(url) {
            if Engine.isDownload(url) {
                FilePreview.shared.open(url, name: url.lastPathComponent, in: web.webView)
                return
            }
            openWeb(url.absoluteString, title: "")
        } else {
            web.openExternally(url)
        }
    }

    /// A row's address, wherever it leads: a native screen, the web interface's screen (with the row's
    /// title while it loads), a file in the phone's viewer, another site in Safari.
    func go(_ raw: String?, title: String) {
        guard let raw, !raw.isEmpty, let url = absolute(raw) else { return }
        Haptics.tap()
        if isCanvas(url) && !Engine.isDownload(url) {
            openWeb(raw, title: title)
        } else {
            openLink(url)
        }
    }

    /// A course file in the phone's own viewer.
    func openFile(_ url: String, name: String) {
        guard let u = absolute(url) else { return }
        FilePreview.shared.open(u, name: name, in: web.webView)
    }
}
