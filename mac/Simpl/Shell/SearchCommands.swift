import AppKit
import SwiftUI

// The toolbar's search field as the web's search box (1.2, extension/content/app/search.js and hub.js): what is typed
// is answered in the field's own suggestions as each letter goes in — a sum worked out, the commands whose names it
// starts, your courses (and a course's section: "bio files"), your groups, and Search for the words. "/" (or ">") lists
// every command the box knows, narrowing as its name is typed; a command that takes something (/course, /grades, /due,
// /file …) lists what it can be once a space follows its name. A suggestion picked — clicked, or the arrows and Return —
// runs; Return on words alone searches everything, as before. Edit ▸ Search Commands (⌘K) opens the field on "/".

/// What a suggestion does when it is picked.
enum PaletteAction: Hashable {
    case go(Place)
    case open(url: String, title: String, external: Bool)
    case work(url: String?, title: String)
    case search(String)
    case fill(String)
    case newTask
    case addTask(String)
    case newMessage
    case refresh
    case reloadPage
    case settings
    case whatsNew
    case setup
    case tour
    case look(AppLook)
    case back
    case forward
    case browser
    case copy(String)
    case nothing
}

/// One suggestion: what it says, its symbol (in a course's colour where it is one of a course's), a shortcut or a score at
/// its end, and what it does.
struct PaletteItem: Identifiable, Hashable {
    var id: String
    var title: String
    var sub: String? = nil
    var symbol: String
    var color: String? = nil
    var trailing: String? = nil
    var action: PaletteAction
    /// What the field holds once this is picked (SearchPalette.publish): its title behind a mark no keyboard types.
    var token = ""
}

struct PaletteSection: Identifiable, Hashable {
    var title: String
    var items: [PaletteItem]
    var id: String { title }
}

/// A command of the search field's, as the web box's (hub.js COMMANDS) in the Mac's own terms: its name and the other
/// names it answers to, the line under it, what it takes (picking it then puts "/name " in the field, to list that), and
/// what it does on its own.
struct PaletteCommand {
    enum Lists: Equatable {
        case courses, groups, grades, due, overdue, words, task, commands
        case kinds([String]) // (the search's own groups: Assignments, Files …)
    }

    var name: String
    var aliases: [String] = []
    var title: String
    var hint: String
    var symbol: String
    var key: String? = nil
    var takes: String? = nil
    var lists: Lists? = nil
    /// Runs as it stands even though it takes something (/grades opens Grades; "/grades bio" lists the course's).
    var quick = false
    var action: PaletteAction = .nothing

    var names: [String] { [name] + aliases }
    private var fills: Bool { lists != nil && !quick }

    /// How well a typed name fits: the name itself, a name it starts or the title it starts, then (unless strict) a word
    /// inside a name or the line under it. Nil: not at all.
    func rank(_ typed: String, strict: Bool) -> Int? {
        let s = typed.lowercased()
        if s.isEmpty { return 3 }
        if names.contains(s) { return 0 }
        if names.contains(where: { $0.hasPrefix(s) }) || title.lowercased().hasPrefix(s) { return 1 }
        if !strict && (names.contains(where: { $0.contains(s) }) || hint.lowercased().contains(s) || title.lowercased().contains(s)) { return 2 }
        return nil
    }

    /// Its row under "/": "/course a course, then a section".
    var slashRow: PaletteItem {
        let extra = fills ? (takes.map { " " + $0 } ?? "") : ""
        return PaletteItem(id: "cmd.\(name)", title: "/\(name)\(extra)", sub: hint, symbol: symbol, trailing: key, action: fills ? .fill("/\(name) ") : action)
    }

    /// Its row among words typed: "Grades", what it does, and its shortcut (or its name, for one that takes something).
    var plainRow: PaletteItem {
        PaletteItem(id: "cmd.\(name)", title: title, sub: hint, symbol: symbol, trailing: key ?? (fills ? "/\(name)" : nil), action: fills ? .fill("/\(name) ") : action)
    }
}

/// The search field's suggestions for what it holds, and the running of the one picked.
@MainActor
final class SearchPalette: ObservableObject {
    @Published private(set) var sections: [PaletteSection] = []
    /// What the field holds once a suggestion is picked, and that suggestion.
    private var picked: [String: PaletteItem] = [:]
    /// The words last answered (a hint picked puts them back).
    private var lastRaw = ""
    private var seq = 0
    private var lookup: Task<Void, Never>?
    private var todoCache: (at: Date, data: TodoData)?
    private var gradesCache: (at: Date, data: GradesData)?

    /// The mark before a picked suggestion's title in the field (an invisible separator: nothing a keyboard types).
    static let mark = "\u{2063}"

    /// Words that are a command (or a suggestion picked): nothing is searched for them.
    static func isCommand(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespaces)
        return t.hasPrefix("/") || t.hasPrefix(">") || t.hasPrefix(mark)
    }

    // MARK: - The field's moves

    /// The field holds a suggestion just picked: it runs (once — the field's Return and its text changing both say so).
    func take(_ text: String, engine: Engine) -> Bool {
        guard text.hasPrefix(Self.mark) else { return false }
        guard engine.query == text else { return true } // (run already, the other way)
        guard let item = picked[text] else {
            engine.query = lastRaw
            return true
        }
        if case .nothing = item.action {
            engine.query = lastRaw // (a hint, or "Looking…": the words as they were)
            return true
        }
        Self.perform(item.action, engine: engine, fromField: true)
        return true
    }

    /// Return in the field: a command's first suggestion runs (as the web box chooses its first row as you type); words
    /// are searched.
    func submit(_ text: String, engine: Engine) {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { return }
        if Self.isCommand(t) {
            for s in sections {
                if let first = s.items.first(where: { $0.action != .nothing }) {
                    Self.perform(first.action, engine: engine, fromField: true)
                    return
                }
            }
            return
        }
        engine.search(t)
    }

    /// The suggestions for what the field holds now: what the app holds at once, what it must ask for after a pause.
    func update(_ raw: String, engine: Engine) {
        seq += 1
        let mine = seq
        lookup?.cancel()
        lookup = nil
        lastRaw = raw
        let typed = Typed(raw)
        let built: (sections: [PaletteSection], job: Job?)
        if typed.command {
            built = commandSections(typed, engine)
        } else {
            built = (sections: plainSections(typed, engine), job: nil)
        }
        publish(built.sections)
        guard let job = built.job else { return }
        let base = built.sections
        lookup = Task { [weak self] in
            guard let self else { return }
            let extra = await self.run(job, engine: engine)
            guard !Task.isCancelled, self.seq == mine, let extra else { return }
            self.publish(SearchPalette.merge(base, extra))
        }
    }

    /// The screenshot suite's way to the field's suggestions (-SimplOpen palette:/gr): the field focused, that typed.
    func launch(_ engine: Engine) {
        guard let text = LaunchOpen.take("palette:") else { return }
        Task {
            try? await Task.sleep(nanoseconds: 2_500_000_000)
            engine.query = text
            SearchField.focus(engine)
        }
    }

    /// Search Commands (⌘K): the field with "/" in it, every command listed.
    static func summon(_ engine: Engine) {
        engine.query = "/"
        SearchField.focus(engine)
    }

    /// The commands whose names the words start (every one, for no words), as the Search screen shows them.
    static func commandItems(matching words: String, engine: Engine) -> [PaletteItem] {
        let all = catalog(engine)
        let q = words.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        if q.isEmpty { return all.filter { $0.name != "help" }.map(\.plainRow) }
        guard q.count >= 2 else { return [] }
        return matching(all, q, strict: true).prefix(4).map(\.plainRow)
    }

    // MARK: - Running one

    /// A suggestion's action. From the field (`fromField`), the field is let go of first — emptied, or left holding the
    /// words searched — so its suggestions close and the keys go to the screen.
    static func perform(_ action: PaletteAction, engine: Engine, fromField: Bool) {
        switch action {
        case .fill(let text):
            engine.query = text // (the field keeps the cursor, listing what the command takes)
            SearchField.focus(engine)
            return
        case .nothing:
            return
        default:
            break
        }
        if fromField {
            if case .search(let q) = action { engine.query = q } else { engine.query = "" }
            SearchField.resign(engine)
        }
        switch action {
        case .go(let place): engine.go(place)
        case .open(let url, let title, let external): engine.openFromSearch(url, title: title, external: external)
        case .work(let url, let title):
            if let url, !url.isEmpty { engine.openWeb(url, title: title) } else { engine.go(.todo) }
        case .search(let q): engine.search(q)
        case .newTask: engine.newTask = true
        case .addTask(let title):
            Task {
                if await engine.act("addTask", ["title": title]) {
                    engine.changed()
                    engine.go(.todo)
                } else {
                    engine.newTask = true
                }
            }
        case .newMessage: engine.newMessage = true
        case .refresh: engine.refresh()
        case .reloadPage: engine.reload()
        case .settings: NotificationCenter.default.post(name: .simplOpenSettings, object: nil)
        case .whatsNew:
            Task {
                if let wn = try? await engine.call("whatsNew", as: WhatsNewData.self) { engine.whatsNew = WhatsNewSheetItem(data: wn) }
            }
        case .setup: engine.setup = true
        case .tour: MacTour.shared.start()
        case .look(let look): AppLook.set(look)
        case .back: engine.goBack()
        case .forward: engine.goForward()
        case .browser: NSWorkspace.shared.open(engine.web.baseURL)
        case .copy(let text): copyToPasteboard(text)
        case .fill, .nothing: break
        }
    }

    // MARK: - The commands

    /// Every command the field knows, as the web box's (hub.js COMMANDS) where the Mac has the same thing.
    static func catalog(_ engine: Engine) -> [PaletteCommand] {
        let lms = engine.lmsName
        let canvas = !engine.onBrightspace // (Brightspace has no Inbox or groups here)
        var c: [PaletteCommand] = []
        c.append(PaletteCommand(name: "dashboard", aliases: ["home", "today"], title: "Dashboard", hint: "What’s due and new, at a glance", symbol: "square.grid.2x2", key: "⌘1", action: .go(.dashboard)))
        c.append(PaletteCommand(name: "todo", aliases: ["tasks", "planner"], title: "To Do", hint: "Everything to do this week", symbol: "checklist", key: "⌘2", action: .go(.todo)))
        c.append(PaletteCommand(name: "calendar", aliases: ["cal", "schedule"], title: "Calendar", hint: "Your courses’ calendar", symbol: "calendar", key: "⌘3", action: .go(.calendar)))
        c.append(PaletteCommand(name: "grades", aliases: ["gpa", "grade", "marks"], title: "Grades", hint: "Your GPA and every course’s grade — or one: /grades bio", symbol: "chart.bar.xaxis", key: "⌘4", takes: "a course", lists: .grades, quick: true, action: .go(.grades)))
        c.append(PaletteCommand(name: "notifications", aliases: ["alerts", "activity"], title: "Notifications", hint: "What’s new in your courses", symbol: "bell", key: "⌘5", action: .go(.notifications)))
        if canvas {
            c.append(PaletteCommand(name: "inbox", aliases: ["messages", "mail", "conversations"], title: "Inbox", hint: "Your messages", symbol: "tray", key: "⌘6", action: .go(.inbox)))
        }
        c.append(PaletteCommand(name: "courses", aliases: ["classes"], title: "All Courses", hint: "Every course you are in", symbol: "books.vertical", action: .go(.courses)))
        c.append(PaletteCommand(name: "course", aliases: ["class", "go", "open"], title: "Open a Course", hint: "A course — or one of its sections: /course bio files", symbol: "book", takes: "a course, then a section", lists: .courses))
        if canvas {
            c.append(PaletteCommand(name: "groups", title: "Groups", hint: "Your groups", symbol: "person.2", action: .go(.groups)))
            c.append(PaletteCommand(name: "group", aliases: ["team"], title: "Open a Group", hint: "One of your groups", symbol: "person.2.fill", takes: "a group", lists: .groups))
        }
        // TODO(tools): a "tools" command (aliases "widgets", "tool") going to Place.tools — Simpl's tools — once it is in.
        c.append(PaletteCommand(name: "due", aliases: ["upcoming", "soon", "week"], title: "Due", hint: "What is due: today, tomorrow, this week, or by name", symbol: "clock", takes: "today, tomorrow, week or a name", lists: .due))
        c.append(PaletteCommand(name: "overdue", aliases: ["late", "missing"], title: "Overdue", hint: "Past its date and not handed in", symbol: "exclamationmark.circle", takes: "a name (or nothing)", lists: .overdue))
        c.append(PaletteCommand(name: "assignment", aliases: ["hw", "homework", "assignments"], title: "Assignments", hint: "Find an assignment", symbol: "doc.text", takes: "an assignment", lists: .kinds(["Assignments"])))
        c.append(PaletteCommand(name: "quiz", aliases: ["quizzes", "test", "exam"], title: "Quizzes", hint: "Find a quiz", symbol: "checklist.checked", takes: "a quiz", lists: .kinds(["Assignments"])))
        c.append(PaletteCommand(name: "announcement", aliases: ["ann", "news", "announcements"], title: "Announcements", hint: "Find an announcement", symbol: "megaphone", takes: "an announcement", lists: .kinds(["Announcements"])))
        c.append(PaletteCommand(name: "discussion", aliases: ["disc", "thread", "discussions"], title: "Discussions", hint: "Find a discussion", symbol: "bubble.left.and.bubble.right", takes: "a discussion", lists: .kinds(["Discussions"])))
        c.append(PaletteCommand(name: "page", aliases: ["wiki", "pages"], title: "Pages", hint: "Find a page in your courses", symbol: "doc.richtext", takes: "a page", lists: .kinds(["Pages"])))
        c.append(PaletteCommand(name: "file", aliases: ["files", "read", "download", "pdf"], title: "Files", hint: "Find a course file — it opens in Quick Look", symbol: "folder", takes: "a file", lists: .kinds(["Files"])))
        c.append(PaletteCommand(name: "people", aliases: ["person", "who", "classmate", "teacher", "ta"], title: "People", hint: "Find someone in your courses", symbol: "person.crop.circle", takes: "a name", lists: .kinds(["People"])))
        c.append(PaletteCommand(name: "find", aliases: ["search", "show", "what", "whats"], title: "Search", hint: "Search everything for the words", symbol: "magnifyingglass", takes: "anything", lists: .words))
        c.append(PaletteCommand(name: "note", aliases: ["task", "remind", "add"], title: "Add a Task", hint: "A task for today in To Do: /note read chapter 4", symbol: "plus.circle", key: "⌘N", takes: "what to do", lists: .task))
        if canvas {
            c.append(PaletteCommand(name: "message", aliases: ["compose", "write", "email", "send"], title: "New Message", hint: "Write to a teacher or a classmate", symbol: "square.and.pencil", key: "⇧⌘N", action: .newMessage))
        }
        c.append(PaletteCommand(name: "reload", aliases: ["refresh", "update"], title: "Reload", hint: "Everything asked of \(lms) afresh", symbol: "arrow.clockwise", key: "⌘R", action: .refresh))
        c.append(PaletteCommand(name: "back", aliases: ["previous"], title: "Back", hint: "Where you were before", symbol: "chevron.left", key: "⌘[", action: .back))
        c.append(PaletteCommand(name: "forward", aliases: ["next"], title: "Forward", hint: "Where you went next", symbol: "chevron.right", key: "⌘]", action: .forward))
        c.append(PaletteCommand(name: "dark", aliases: ["night"], title: "Dark Appearance", hint: "Simpl in dark, whatever the Mac is set to", symbol: "moon", action: .look(.dark)))
        c.append(PaletteCommand(name: "light", aliases: ["day"], title: "Light Appearance", hint: "Simpl in light, whatever the Mac is set to", symbol: "sun.max", action: .look(.light)))
        c.append(PaletteCommand(name: "system", aliases: ["auto", "appearance", "theme", "look"], title: "System Appearance", hint: "Light or dark as the Mac is set", symbol: "circle.lefthalf.filled", action: .look(.system)))
        c.append(PaletteCommand(name: "settings", aliases: ["preferences", "options", "prefs"], title: "Settings", hint: "Simpl’s settings", symbol: "gearshape", key: "⌘,", action: .settings))
        c.append(PaletteCommand(name: "whatsnew", aliases: ["changes", "version", "new", "release"], title: "What’s New", hint: "What changed in this version", symbol: "sparkles", action: .whatsNew))
        c.append(PaletteCommand(name: "setup", aliases: ["goals"], title: "Guided Setup", hint: "Choose your courses and goals again", symbol: "checkmark.circle", action: .setup))
        c.append(PaletteCommand(name: "tour", aliases: ["welcome", "guide", "intro", "onboarding"], title: "Take the Tour", hint: "A quick look round the app", symbol: "signpost.right", action: .tour))
        c.append(PaletteCommand(name: "browser", aliases: ["web", "site", "canvas", "brightspace"], title: "Open \(lms) in Browser", hint: "The school’s own site", symbol: "safari", action: .browser))
        c.append(PaletteCommand(name: "help", aliases: ["?", "commands"], title: "Commands", hint: "Every command", symbol: "command", lists: .commands))
        return c
    }

    /// The commands a typed name could mean, best first.
    static func matching(_ all: [PaletteCommand], _ typed: String, strict: Bool) -> [PaletteCommand] {
        var ranked: [(rank: Int, index: Int, command: PaletteCommand)] = []
        for (i, c) in all.enumerated() {
            if let r = c.rank(typed, strict: strict) { ranked.append((rank: r, index: i, command: c)) }
        }
        ranked.sort { $0.rank != $1.rank ? $0.rank < $1.rank : $0.index < $1.index }
        return ranked.map { $0.command }
    }

    /// The command by its name or one it answers to ("/what's due" is /what).
    static func named(_ all: [PaletteCommand], _ typed: String) -> PaletteCommand? {
        var s = typed.lowercased()
        if s.hasSuffix("'s") || s.hasSuffix("’s") { s.removeLast(2) }
        guard !s.isEmpty else { return nil }
        return all.first { $0.names.contains(s) }
    }

    // MARK: - What is typed

    private struct Typed {
        let raw: String
        let command: Bool
        let name: String
        let arg: String
        let hasArg: Bool

        init(_ raw: String) {
            self.raw = raw
            let t = raw.drop(while: { $0 == " " })
            guard let f = t.first, f == "/" || f == ">" else {
                command = false
                name = ""
                arg = ""
                hasArg = false
                return
            }
            command = true
            let rest = t.dropFirst()
            if let sp = rest.firstIndex(where: { $0 == " " }) {
                let n = String(rest[..<sp])
                let a = rest[sp...].trimmingCharacters(in: .whitespaces)
                if n.isEmpty { // ("/ grades": the name after the space)
                    name = a
                    arg = ""
                    hasArg = false
                } else {
                    name = n
                    arg = a
                    hasArg = true
                }
            } else {
                name = String(rest)
                arg = ""
                hasArg = false
            }
        }
    }

    /// What the app answers later: Canvas's search for one kind, the planner, the grades.
    private enum Job {
        case kinds(title: String, kinds: [String], q: String)
        case due(title: String, q: String, overdue: Bool)
        case grades(q: String)
    }

    // MARK: - Words typed

    private func plainSections(_ t: Typed, _ engine: Engine) -> [PaletteSection] {
        let words = t.raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let q = words.lowercased()
        guard !q.isEmpty, !q.hasPrefix(Self.mark) else { return [] }
        var out: [PaletteSection] = []
        if let a = QuickSum.answer(words) {
            out.append(PaletteSection(title: "Answer", items: [PaletteItem(id: "answer", title: a.value, sub: a.sub, symbol: "equal.circle.fill", trailing: "Copy", action: .copy(a.value))]))
        }
        if q.count >= 2 {
            let cmds = Self.matching(Self.catalog(engine), q, strict: true).prefix(4).map(\.plainRow)
            if !cmds.isEmpty { out.append(PaletteSection(title: "Commands", items: cmds)) }
        }
        let courses = Self.courseItems(q, engine, limit: 5)
        if !courses.isEmpty { out.append(PaletteSection(title: "Courses", items: courses)) }
        if !engine.onBrightspace {
            let groups = Self.groupItems(q, engine, limit: 3)
            if !groups.isEmpty { out.append(PaletteSection(title: "Groups", items: groups)) }
        }
        out.append(PaletteSection(title: "Search", items: [Self.searchRow(words, engine)]))
        return out
    }

    // MARK: - A command typed

    private func commandSections(_ t: Typed, _ engine: Engine) -> ([PaletteSection], Job?) {
        let all = Self.catalog(engine)
        if !t.hasArg {
            let list = Self.matching(all, t.name, strict: false)
            if list.isEmpty {
                let help = PaletteItem(id: "help", title: "/help", sub: "Every command", symbol: "command", action: .fill("/"))
                return ([PaletteSection(title: "No Command by That Name", items: [help, Self.searchRow(t.name, engine)])], nil)
            }
            return ([PaletteSection(title: "Commands", items: list.map(\.slashRow))], nil)
        }
        guard let cmd = Self.named(all, t.name) else {
            // ("/physics quiz tomorrow": no command by that name — the words searched)
            let words = "\(t.name) \(t.arg)".trimmingCharacters(in: .whitespaces)
            return ([PaletteSection(title: "Search", items: [Self.searchRow(words, engine)])], nil)
        }
        return argumentSections(cmd, t.arg, engine, all: all)
    }

    /// What a command takes, as a list that narrows as the rest is typed.
    private func argumentSections(_ cmd: PaletteCommand, _ arg: String, _ engine: Engine, all: [PaletteCommand]) -> ([PaletteSection], Job?) {
        let q = arg.lowercased()
        guard let lists = cmd.lists else {
            return ([PaletteSection(title: cmd.title, items: [cmd.slashRow])], nil)
        }
        switch lists {
        case .courses:
            let items = Self.courseItems(q, engine, limit: 10)
            return ([PaletteSection(title: "Courses", items: items.isEmpty ? [Self.hint("No course by that name", "book")] : items)], nil)
        case .groups:
            let items = Self.groupItems(q, engine, limit: 10)
            return ([PaletteSection(title: "Groups", items: items.isEmpty ? [Self.hint("No group by that name", "person.2")] : items)], nil)
        case .grades:
            return ([PaletteSection(title: "Grades", items: gradeItems(q, engine))], .grades(q: q))
        case .due:
            return ([PaletteSection(title: cmd.title, items: [Self.looking])], .due(title: cmd.title, q: q, overdue: false))
        case .overdue:
            return ([PaletteSection(title: cmd.title, items: [Self.looking])], .due(title: cmd.title, q: q, overdue: true))
        case .kinds(let kinds):
            if q.count < 2 { return ([PaletteSection(title: cmd.title, items: [Self.hint("Type part of its name…", cmd.symbol)])], nil) }
            return ([PaletteSection(title: cmd.title, items: [Self.looking])], .kinds(title: cmd.title, kinds: kinds, q: arg))
        case .words:
            if q.isEmpty { return ([PaletteSection(title: cmd.title, items: [Self.hint("Type what you are after…", cmd.symbol)])], nil) }
            return ([PaletteSection(title: cmd.title, items: [Self.searchRow(arg, engine)])], nil)
        case .task:
            var items: [PaletteItem] = []
            if !arg.isEmpty {
                items.append(PaletteItem(id: "task.add", title: "Add “\(arg)” to To Do", sub: "A task for today", symbol: "plus.circle.fill", trailing: "↩", action: .addTask(arg)))
            }
            items.append(PaletteItem(id: "task.new", title: "New Task…", sub: "With a day, a course, a priority and a repeat", symbol: "square.and.pencil", trailing: "⌘N", action: .newTask))
            return ([PaletteSection(title: cmd.title, items: items)], nil)
        case .commands:
            let list = Self.matching(all, q, strict: false).filter { $0.name != "help" }
            return ([PaletteSection(title: "Commands", items: list.map(\.slashRow))], nil)
        }
    }

    // MARK: - Rows

    private static let looking = PaletteItem(id: "looking", title: "Looking…", symbol: "hourglass", action: .nothing)

    private static func hint(_ text: String, _ symbol: String) -> PaletteItem {
        PaletteItem(id: "hint", title: text, symbol: symbol, action: .nothing)
    }

    private static func searchRow(_ words: String, _ engine: Engine) -> PaletteItem {
        PaletteItem(id: "search", title: "Search for “\(words)”", sub: "Everything in \(engine.lmsName)", symbol: "magnifyingglass", trailing: "↩", action: .search(words))
    }

    /// The courses whose code or name holds the words, the codes that start with them first.
    private static func courses(_ q: String, _ engine: Engine) -> [CourseRow] {
        let s = q.lowercased()
        guard !s.isEmpty else { return engine.courses }
        let found = engine.courses.filter { c in
            [c.code, c.name ?? "", c.nickname ?? "", c.original ?? ""].contains { $0.lowercased().contains(s) }
        }
        return found.filter { $0.code.lowercased().hasPrefix(s) } + found.filter { !$0.code.lowercased().hasPrefix(s) }
    }

    /// Your courses for the words — or, when none has them all, a course and one of its sections ("bio files").
    static func courseItems(_ q: String, _ engine: Engine, limit: Int) -> [PaletteItem] {
        let found = courses(q, engine)
        if !found.isEmpty {
            return found.prefix(limit).map { c in
                PaletteItem(id: "c.\(c.id)", title: c.code, sub: c.name == c.code ? nil : c.name, symbol: "book.closed.fill", color: c.color, trailing: (c.unread ?? 0) > 0 ? "\(c.unread ?? 0) new" : nil, action: .go(.home("courses/\(c.id)")))
            }
        }
        return sectionItems(q, engine, limit: limit)
    }

    private static func sectionItems(_ q: String, _ engine: Engine, limit: Int) -> [PaletteItem] {
        var words = q.split(separator: " ").map(String.init)
        guard words.count >= 2, let last = words.popLast(), last.count >= 2 else { return [] }
        let name = words.joined(separator: " ")
        var out: [PaletteItem] = []
        for c in courses(name, engine) {
            let ctx = "courses/\(c.id)"
            for s in engine.sections[ctx] ?? Sidebar.defaultSections {
                guard s.kind != "home", s.kind.lowercased().hasPrefix(last) || s.label.lowercased().hasPrefix(last) else { continue }
                out.append(PaletteItem(id: "s.\(ctx).\(s.kind)", title: "\(s.label) — \(c.code)", sub: c.name, symbol: Glyph.section(s.kind), color: c.color, action: .go(.section(ctx, s.kind))))
            }
        }
        return Array(out.prefix(limit))
    }

    static func groupItems(_ q: String, _ engine: Engine, limit: Int) -> [PaletteItem] {
        let s = q.lowercased()
        let found = s.isEmpty ? engine.groups : engine.groups.filter { $0.name.lowercased().contains(s) }
        return found.prefix(limit).map { g in
            PaletteItem(id: "g.\(g.id)", title: g.name, sub: g.sub ?? "Group", symbol: "person.2.fill", color: g.color, action: .go(.home("groups/\(g.id)")))
        }
    }

    /// /grades: the overview (with the GPA once read) and each course's grade — the sidebar's scores at once, the
    /// Grades screen's own (with the letter) once they are in.
    private func gradeItems(_ q: String, _ engine: Engine) -> [PaletteItem] {
        var items: [PaletteItem] = []
        let data = gradesCache?.data
        if q.isEmpty {
            let gpa = data?.gpa.map { String(format: "%.2f", $0) }
            items.append(PaletteItem(id: "grades.all", title: "Grades Overview", sub: gpa.map { "GPA \($0) · every course" } ?? "Every course, and your GPA", symbol: "chart.bar.xaxis", trailing: gpa, action: .go(.grades)))
        }
        if let data {
            for r in data.rows {
                guard q.isEmpty || r.code.lowercased().contains(q) || (r.name ?? "").lowercased().contains(q) else { continue }
                let score = [r.pctText, r.letter ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
                items.append(PaletteItem(id: "grade.\(r.id)", title: r.code, sub: "Grades in \(r.name ?? r.code)", symbol: "chart.bar.fill", color: r.color, trailing: score, action: .go(.section("courses/\(r.id)", "grades"))))
            }
        } else {
            for c in Self.courses(q, engine) {
                items.append(PaletteItem(id: "grade.\(c.id)", title: c.code, sub: "Grades in \(c.name ?? c.code)", symbol: "chart.bar.fill", color: c.color, trailing: c.scoreText, action: .go(.section("courses/\(c.id)", "grades"))))
            }
        }
        if items.isEmpty { items.append(Self.hint("No course by that name", "chart.bar")) }
        return items
    }

    // MARK: - What is asked for

    private func run(_ job: Job, engine: Engine) async -> PaletteSection? {
        switch job {
        case .kinds(let title, let kinds, let q):
            try? await Task.sleep(nanoseconds: 280_000_000) // (a pause in the typing before Canvas is asked)
            guard !Task.isCancelled else { return nil }
            guard let d = try? await engine.call("search", ["q": q], as: SearchData.self) else {
                return PaletteSection(title: title, items: [Self.hint("\(engine.lmsName) could not be searched just now.", "exclamationmark.triangle")])
            }
            var items: [PaletteItem] = []
            for g in d.groups where kinds.contains(g.title) {
                for (i, r) in g.rows.enumerated() {
                    items.append(PaletteItem(id: "r.\(g.title).\(i).\(r.id)", title: r.title, sub: r.sub, symbol: SearchGlyph.of(g.title), color: r.color, trailing: r.external == true ? "↗" : nil, action: .open(url: r.url, title: r.title, external: r.external == true)))
                }
            }
            if items.isEmpty {
                items = [PaletteItem(id: "none", title: "Nothing for “\(q)”", sub: "Search everything instead", symbol: "magnifyingglass", action: .search(q))]
            }
            return PaletteSection(title: title, items: Array(items.prefix(8)))
        case .due(let title, let q, let overdue):
            guard let d = await todo(engine) else {
                return PaletteSection(title: title, items: [Self.hint("To Do could not be read just now.", "exclamationmark.triangle")])
            }
            let items = Self.dueItems(d, q: q, overdue: overdue)
            if items.isEmpty {
                return PaletteSection(title: title, items: [Self.hint(overdue ? "Nothing overdue." : "Nothing due in the next seven days.", "checkmark.circle")])
            }
            return PaletteSection(title: title, items: items)
        case .grades(let q):
            guard await grades(engine) != nil else { return nil } // (the sidebar's scores stay)
            return PaletteSection(title: "Grades", items: gradeItems(q, engine))
        }
    }

    /// The planner's work for /due (today, tomorrow, week, a name, or all that is still to come) and /overdue.
    private static func dueItems(_ d: TodoData, q: String, overdue: Bool) -> [PaletteItem] {
        let now = Date()
        let cal = Calendar.current
        let today = cal.startOfDay(for: now)
        let span: ClosedRange<Int>?
        switch q {
        case "today": span = 0...0
        case "tomorrow": span = 1...1
        case "week", "this week": span = 0...6
        default: span = nil
        }
        var seen = Set<String>()
        var found: [(date: Date?, row: WorkRow)] = []
        for r in d.sections.flatMap(\.rows) {
            guard !r.done, seen.insert(r.id).inserted else { continue }
            let date = PaletteDates.parse(r.date)
            if overdue {
                guard let date, date < now else { continue }
            } else if let span {
                guard let date else { continue }
                let n = cal.dateComponents([.day], from: today, to: cal.startOfDay(for: date)).day ?? 0
                guard span.contains(n) else { continue }
            } else if let date, date < today {
                continue // (what is past its date is /overdue's)
            }
            if span == nil, !q.isEmpty {
                let hit = r.title.lowercased().contains(q) || (r.course ?? "").lowercased().contains(q)
                guard hit else { continue }
            }
            found.append((date: date, row: r))
        }
        found.sort { ($0.date ?? .distantFuture) < ($1.date ?? .distantFuture) }
        return found.prefix(8).map { (f) -> PaletteItem in
            let r = f.row
            let sub = [r.course ?? "", r.when ?? r.time ?? ""].filter { !$0.isEmpty }.joined(separator: " · ")
            return PaletteItem(id: "w.\(r.id)", title: r.title, sub: sub, symbol: Glyph.item(r.type ?? "assignment"), color: r.color, trailing: r.flag?.word, action: .work(url: r.custom == true ? nil : r.url, title: r.title))
        }
    }

    private func todo(_ engine: Engine) async -> TodoData? {
        if let c = todoCache, Date().timeIntervalSince(c.at) < 90 { return c.data }
        guard let d = try? await engine.call("todo", as: TodoData.self) else { return nil }
        todoCache = (at: Date(), data: d)
        return d
    }

    private func grades(_ engine: Engine) async -> GradesData? {
        if let c = gradesCache, Date().timeIntervalSince(c.at) < 120 { return c.data }
        guard let d = try? await engine.call("grades", as: GradesData.self) else { return nil }
        gradesCache = (at: Date(), data: d)
        return d
    }

    // MARK: - Publishing

    /// The sections shown, each row given what the field holds once it is picked.
    private func publish(_ list: [PaletteSection]) {
        var map: [String: PaletteItem] = [:]
        var out: [PaletteSection] = []
        for s in list where !s.items.isEmpty {
            var items: [PaletteItem] = []
            for var it in s.items {
                var token = Self.mark + it.title
                while map[token] != nil { token += Self.mark }
                it.token = token
                map[token] = it
                items.append(it)
            }
            out.append(PaletteSection(title: s.title, items: items))
        }
        picked = map
        sections = out
    }

    /// A section that answered later in the place of the one that waited for it (or after the rest).
    private static func merge(_ base: [PaletteSection], _ extra: PaletteSection) -> [PaletteSection] {
        var out = base
        if let i = out.firstIndex(where: { $0.title == extra.title }) { out[i] = extra } else { out.append(extra) }
        return out
    }
}

/// The planner's dates, as the page writes them (ISO 8601, with or without its fraction of a second).
private enum PaletteDates {
    static let full: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    static let plain = ISO8601DateFormatter()

    static func parse(_ s: String?) -> Date? {
        guard let s, !s.isEmpty else { return nil }
        return full.date(from: s) ?? plain.date(from: s)
    }
}

// MARK: - The suggestions

/// The field's suggestions: each section under its heading, each row picked as a whole.
struct PaletteSuggestions: View {
    @ObservedObject var palette: SearchPalette

    var body: some View {
        ForEach(palette.sections) { section in
            Section(section.title) {
                ForEach(section.items) { item in
                    PaletteRow(item: item)
                        .searchCompletion(item.token)
                }
            }
        }
    }
}

/// A suggestion: its symbol, what it is and the line after it, a shortcut or a score at its end.
struct PaletteRow: View {
    let item: PaletteItem

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: item.symbol)
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(tint)
                .frame(width: 22)
            Text(item.title)
                .font(.sBody)
                .lineLimit(1)
            if let sub = item.sub, !sub.isEmpty {
                Text(sub)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 12)
            if let t = item.trailing, !t.isEmpty {
                Text(t)
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
        }
        .accessibilityElement(children: .combine)
    }

    private var tint: Color { item.color.map { Color(hex: $0) } ?? Color.accentColor }
}

/// A command as a glass button (the Search screen's): pressed, it runs.
struct CommandPill: View {
    let item: PaletteItem
    @EnvironmentObject private var engine: Engine

    var body: some View {
        Button {
            SearchPalette.perform(item.action, engine: engine, fromField: false)
        } label: {
            HStack(spacing: 8) {
                Image(systemName: item.symbol)
                    .font(.sCallout.weight(.semibold))
                    .foregroundStyle(item.color.map { Color(hex: $0) } ?? Color.accentColor)
                Text(item.title)
                    .font(.sCallout.weight(.medium))
                    .lineLimit(1)
                if let t = item.trailing, !t.isEmpty {
                    Text(t)
                        .font(.sFootnote.monospacedDigit())
                        .foregroundStyle(.secondary)
                }
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 9)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .glassCapsule(interactive: true)
        .help(item.sub ?? item.title)
    }
}

/// The symbol for a kind of search result.
enum SearchGlyph {
    static func of(_ group: String) -> String {
        switch group {
        case "Courses": return "books.vertical.fill"
        case "Assignments": return "doc.text.fill"
        case "Announcements": return "megaphone.fill"
        case "Pages": return "doc.richtext.fill"
        case "Discussions": return "bubble.left.and.bubble.right.fill"
        case "Files": return "folder.fill"
        case "People": return "person.crop.circle.fill"
        default: return "magnifyingglass"
        }
    }
}

// MARK: - A sum

/// A sum typed in the search field, worked out (hub.js quickAnswers): "12*4", "(3+4)/2", "2^10", "15% of 80". A date or
/// a number with dashes ("10-12") is not one.
enum QuickSum {
    static func answer(_ raw: String) -> (value: String, sub: String)? {
        let q = raw.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty, q.count <= 80 else { return nil }
        if let p = percentOf(q) { return p }
        let ops = CharacterSet(charactersIn: "+-*/^×÷−()")
        let allowed = CharacterSet(charactersIn: "0123456789.+-*/^×÷−()% ")
        let dashed = CharacterSet(charactersIn: "0123456789.- ")
        guard q.rangeOfCharacter(from: .decimalDigits) != nil,
              q.unicodeScalars.contains(where: { ops.contains($0) }),
              q.unicodeScalars.allSatisfy({ allowed.contains($0) }),
              !q.unicodeScalars.allSatisfy({ dashed.contains($0) }) else { return nil }
        var parser = SumParser(q)
        guard let v = parser.parse(), v.isFinite else { return nil }
        return (format(v), "\(q) =")
    }

    private static func percentOf(_ q: String) -> (value: String, sub: String)? {
        guard let rx = try? NSRegularExpression(pattern: "^(\\d+(?:\\.\\d+)?)\\s*%\\s*(?:of|×|\\*)\\s*(\\d+(?:\\.\\d+)?)$", options: [.caseInsensitive]),
              let m = rx.firstMatch(in: q, range: NSRange(q.startIndex..., in: q)), m.numberOfRanges == 3,
              let r1 = Range(m.range(at: 1), in: q), let r2 = Range(m.range(at: 2), in: q),
              let a = Double(q[r1]), let b = Double(q[r2]) else { return nil }
        return (format(a / 100 * b), "\(q[r1])% of \(q[r2])")
    }

    static func format(_ v: Double) -> String {
        if v == v.rounded(), abs(v) < 1e15 { return String(Int64(v)) }
        return String(format: "%.10g", v)
    }
}

/// + − × ÷ ^ ( ) and a percent sign after a number, in the usual order.
private struct SumParser {
    private let c: [Character]
    private var i = 0

    init(_ s: String) {
        c = s.compactMap { ch -> Character? in
            switch ch {
            case " ": return nil
            case "×": return "*"
            case "÷": return "/"
            case "−": return "-"
            default: return ch
            }
        }
    }

    mutating func parse() -> Double? {
        guard let v = expr(), i == c.count else { return nil }
        return v
    }

    private mutating func expr() -> Double? {
        guard var v = term() else { return nil }
        while i < c.count, c[i] == "+" || c[i] == "-" {
            let op = c[i]
            i += 1
            guard let r = term() else { return nil }
            v = op == "+" ? v + r : v - r
        }
        return v
    }

    private mutating func term() -> Double? {
        guard var v = power() else { return nil }
        while i < c.count, c[i] == "*" || c[i] == "/" {
            let op = c[i]
            i += 1
            guard let r = power() else { return nil }
            v = op == "*" ? v * r : v / r
        }
        return v
    }

    private mutating func power() -> Double? {
        guard let b = unary() else { return nil }
        if i < c.count, c[i] == "^" {
            i += 1
            guard let e = power() else { return nil }
            return pow(b, e)
        }
        return b
    }

    private mutating func unary() -> Double? {
        if i < c.count, c[i] == "-" {
            i += 1
            return unary().map { -$0 }
        }
        if i < c.count, c[i] == "+" {
            i += 1
            return unary()
        }
        return postfix()
    }

    private mutating func postfix() -> Double? {
        guard var v = primary() else { return nil }
        while i < c.count, c[i] == "%" {
            i += 1
            v /= 100
        }
        return v
    }

    private mutating func primary() -> Double? {
        guard i < c.count else { return nil }
        if c[i] == "(" {
            i += 1
            guard let v = expr(), i < c.count, c[i] == ")" else { return nil }
            i += 1
            return v
        }
        let start = i
        while i < c.count, c[i].isASCII, c[i].isNumber || c[i] == "." { i += 1 }
        guard i > start else { return nil }
        return Double(String(c[start..<i]))
    }
}

// MARK: - The field itself, and the app's look

/// The main window's toolbar search field, from outside it (SwiftUI has no way to focus a toolbar search field before
/// macOS 15): focused for Search Commands, let go of once a suggestion has run.
@MainActor
enum SearchField {
    static func focus(_ engine: Engine) {
        DispatchQueue.main.async {
            guard let window = engine.hostView.window else { return }
            if !window.isKeyWindow { window.makeKeyAndOrderFront(nil) }
            if let item = searchItem(window) {
                item.beginSearchInteraction()
            } else if let field = find(in: window.toolbar?.items.compactMap(\.view) ?? []) ?? window.contentView?.superview.flatMap({ find(in: [$0]) }) {
                window.makeFirstResponder(field)
            }
        }
    }

    static func resign(_ engine: Engine) {
        DispatchQueue.main.async {
            guard let window = engine.hostView.window else { return }
            if let item = searchItem(window) { item.endSearchInteraction() }
            if let editor = window.firstResponder as? NSText, editor.isFieldEditor { window.makeFirstResponder(nil) }
        }
    }

    private static func searchItem(_ window: NSWindow) -> NSSearchToolbarItem? {
        for item in window.toolbar?.items ?? [] {
            if let s = item as? NSSearchToolbarItem { return s }
        }
        return nil
    }

    private static func find(in views: [NSView]) -> NSSearchField? {
        for v in views {
            if let f = v as? NSSearchField { return f }
            if let f = find(in: v.subviews) { return f }
        }
        return nil
    }
}

/// Light or dark for the app's windows (/dark, /light, /system), kept for the next launch under the key the app reads as
/// it starts (SimplApp.swift's AppDelegate: SimplAppearance, which the screenshot suite passes as an argument).
enum AppLook: String, Hashable {
    case light, dark, system

    @MainActor
    static func set(_ look: AppLook) {
        let d = UserDefaults.standard
        switch look {
        case .system:
            d.removeObject(forKey: "SimplAppearance")
            NSApp.appearance = nil
        case .light:
            d.set("light", forKey: "SimplAppearance")
            NSApp.appearance = NSAppearance(named: .aqua)
        case .dark:
            d.set("dark", forKey: "SimplAppearance")
            NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }
}
