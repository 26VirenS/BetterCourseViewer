import SwiftUI

/// Which Canvas addresses the app draws itself (1.2): a course and what is in it — its home,
/// announcements, discussions and a discussion, assignments and an assignment, modules, pages and a
/// page, files, people, quizzes, the syllabus, its grades — a group and what is in it, the list of
/// groups, and the Inbox. A quiz opens in the app's own quiz screen and an external tool in its sheet.
/// (1.6) Every address that names one of those by another way in goes to its screen too — an announcement,
/// a discussion's reply, a submission (the assignment, at its feedback), a quiz's history, a page's history,
/// a module, a person, a grade, a conversation, a calendar event, the dashboard; a file to the phone's viewer;
/// a module item, or anything else, by where Canvas sends it (`openWeb`). Nothing opens the web interface.
extension Engine {
    func nativeRoute(for raw: String, title: String) -> Route? {
        guard let url = absolute(raw), let host = url.host?.lowercased(), host == web.baseURL.host?.lowercased() else { return nil }
        if let q = url.query, q.contains("bcv=") || q.contains("display=") { return nil } // (a screen asked for by name, a frame's view)
        let p = url.path.split(separator: "/").map(String.init)
        guard let first = p.first else { return nil }
        if first == "conversations" {
            if p.count == 2, Engine.numeric(p[1]) { return .conversation(id: p[1]) }
            if let id = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "id" })?.value, Engine.numeric(id) { return .conversation(id: id) }
            return .inbox
        }
        if p == ["groups"] { return .groups }
        guard p.count >= 2, first == "courses" || first == "groups", Engine.numeric(p[1]) else { return nil }
        let id = p[1]
        let ctx = "\(first)/\(id)"
        let isCourse = first == "courses"
        if p.count == 2 { return isCourse ? .course(id: id) : .group(id: id) }
        let rest = Array(p.dropFirst(2))
        let one = rest.count == 1
        switch rest[0] {
        // (an announcement is a discussion topic: its own address and the topic's open the same screen; so does a reply's)
        case "announcements":
            if one { return .section(ctx: ctx, kind: "announcements") }
            return Engine.numeric(rest[1]) ? .topic(ctx: ctx, id: rest[1]) : .section(ctx: ctx, kind: "announcements")
        case "discussion_topics":
            if one { return .section(ctx: ctx, kind: "discussions") }
            return Engine.numeric(rest[1]) ? .topic(ctx: ctx, id: rest[1]) : .section(ctx: ctx, kind: "discussions")
        case "assignments" where isCourse:
            if one { return .section(ctx: ctx, kind: "assignments") }
            if rest[1] == "syllabus" { return .section(ctx: ctx, kind: "syllabus") }
            return Engine.numeric(rest[1]) ? .assignment(course: id, id: rest[1]) : .section(ctx: ctx, kind: "assignments")
        case "syllabus":
            return .section(ctx: ctx, kind: "syllabus")
        case "modules":
            // (a module item's own address is resolved first, in openWeb: it is the item it names, not the list)
            if rest.count >= 3 && rest[1] == "items" { return nil }
            return .section(ctx: ctx, kind: "modules")
        case "pages":
            if one { return .section(ctx: ctx, kind: "pages") }
            return .page(ctx: ctx, slug: rest[1])
        case "wiki":
            return .page(ctx: ctx, slug: rest.count >= 2 ? rest[1] : "")
        case "files":
            // (a file itself goes to the phone's viewer, in openWeb, before this: here, the files and their folders)
            return .section(ctx: ctx, kind: "files")
        case "users", "people":
            return .section(ctx: ctx, kind: "people")
        case "groups" where isCourse:
            return .groups
        case "quizzes" where isCourse:
            return .section(ctx: ctx, kind: "quizzes")
        case "grades" where isCourse:
            return .section(ctx: ctx, kind: "grades")
        default:
            return nil
        }
    }

    /// A tab's own address: the dashboard, the course list, the grades, the calendar, the notifications.
    func tabName(for raw: String) -> String? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        let p = url.path.split(separator: "/").map(String.init)
        guard let first = p.first else { return "today" }
        switch first {
        case "courses" where p.count == 1: return "courses"
        case "grades": return "grades"
        case "calendar", "calendar2": return "calendar"
        case "profile" where p.count >= 2 && p[1] == "communication": return "notifications"
        case "courses" where p.count >= 3 && p[2] == "calendar_events": return "calendar"
        default: return nil
        }
    }

    /// A course file's page (…/files/12, its preview, ?wrap=1): the file itself, for the phone's viewer.
    func fileDownload(for raw: String) -> URL? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        if Engine.isDownload(url) { return url }
        let p = url.path.split(separator: "/").map(String.init)
        guard let i = p.firstIndex(of: "files"), i + 1 < p.count, Engine.numeric(p[i + 1]) else { return nil }
        let base = p[...(i + 1)].joined(separator: "/")
        return absolute("/\(base)/download?download_frd=1")
    }

    /// A hand-in's own address (…/assignments/5/submissions/7): the assignment, opened at its feedback.
    static func isSubmission(_ url: URL) -> Bool {
        let p = url.path.split(separator: "/").map(String.init)
        return p.count >= 5 && p[2] == "assignments" && p[4] == "submissions"
    }

    static func numeric(_ s: String) -> Bool { !s.isEmpty && s.allSatisfy(\.isNumber) }

    /// A course's own external tool (…/courses/1/external_tools/9): opened in the tool sheet, not a screen.
    func toolLaunch(for raw: String, title: String) -> ToolLaunch? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        let p = url.path.split(separator: "/").map(String.init)
        guard p.count == 4, p[0] == "courses", Engine.numeric(p[1]), p[2] == "external_tools" else { return nil }
        if Engine.numeric(p[3]) { return .courseTool(course: p[1], id: p[3], title: title.isEmpty ? "Tool" : title) }
        // (a tool launched by its address, …/external_tools/retrieve?url=…: the same sheet)
        if p[3] == "retrieve", let u = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "url" })?.value, !u.isEmpty {
            return ToolLaunch(title: title.isEmpty ? "Tool" : title, args: ["course": p[1], "url": u])
        }
        return nil
    }

    /// A Classic quiz (…/courses/1/quizzes/9, or its take page): the app's own quiz screen, over everything.
    /// Canvas's own quiz page asked for by name (?bcv=…) stays the web screen.
    func quizLaunch(for raw: String, title: String) -> QuizLaunch? {
        guard let url = absolute(raw), url.host?.lowercased() == web.baseURL.host?.lowercased() else { return nil }
        if let q = url.query, q.contains("bcv=") || q.contains("display=") { return nil }
        let p = url.path.split(separator: "/").map(String.init)
        // (1.6: any of a quiz's own addresses — its take page, a question in it, its history — is the quiz; its history, at its feedback)
        guard p.count >= 4, p[0] == "courses", Engine.numeric(p[1]), p[2] == "quizzes", Engine.numeric(p[3]) else { return nil }
        var q = QuizLaunch(course: p[1], quiz: p[3], title: title.isEmpty ? "Quiz" : title)
        if p.count >= 5 && p[4] == "history" { q.feedback = true }
        return q
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
        if isCanvas(url) { openWeb(raw, title: title) } else { openLink(url) }
    }

    /// A course file in the phone's own viewer.
    func openFile(_ url: String, name: String) {
        guard let u = absolute(url) else { return }
        FilePreview.shared.open(u, name: name, in: web.webView)
    }
}
