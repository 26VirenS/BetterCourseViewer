import Foundation

/// A place in the sidebar: a root of the app (Dashboard, To Do …), a course's or a group's home, or one of its sections.
/// A context (`ctx`) is "courses/<id>" or "groups/<id>", as in the iPhone app.
enum Place: Hashable {
    case dashboard
    case courses
    case todo
    case calendar
    case grades
    case notifications
    case inbox
    case groups
    case search(String)
    case home(String)
    case section(String, String)

    /// The context a place belongs to (a course's home or one of its sections), else nil.
    var ctx: String? {
        switch self {
        case .home(let c), .section(let c, _): return c
        default: return nil
        }
    }

    /// What the window keeps one screen for: every search is the one Search screen (a search typed further is the same
    /// screen answering again, not a new one cross-fading in).
    var identity: Place {
        if case .search = self { return .search("") }
        return self
    }

    /// The section a place shows ("home" for a context's home).
    var kind: String? {
        switch self {
        case .home: return "home"
        case .section(_, let k): return k
        default: return nil
        }
    }
}

/// Where the main window is and has been: the place in the sidebar, what is pushed on it, and the history behind and
/// ahead of it — Back and Forward walk it as a browser's do, across places as well as within one.
struct Location: Equatable {
    var place: Place
    var path: [Route] = []
}

struct Navigator: Equatable {
    private(set) var current = Location(place: .dashboard)
    private(set) var back: [Location] = []
    private(set) var forward: [Location] = []

    var canGoBack: Bool { !back.isEmpty }
    var canGoForward: Bool { !forward.isEmpty }

    /// A place picked (the sidebar, a link to a course): shown at its root.
    mutating func go(_ place: Place) {
        if current.place == place && current.path.isEmpty { return }
        record()
        current = Location(place: place)
    }

    /// A screen pushed on the place showing (not twice in a row).
    mutating func push(_ route: Route) {
        if current.path.last == route { return }
        record()
        current.path.append(route)
    }

    /// The stack changed from below (a screen's own Back): taken as a step like any other.
    mutating func setPath(_ path: [Route]) {
        guard path != current.path else { return }
        record()
        current.path = path
    }

    mutating func goBack() {
        guard let previous = back.popLast() else { return }
        forward.append(current)
        current = previous
    }

    mutating func goForward() {
        guard let next = forward.popLast() else { return }
        back.append(current)
        current = next
    }

    /// The place showing taken over by another without a step in the history (a search typed further).
    mutating func replace(_ place: Place) {
        current = Location(place: place)
    }

    /// Back to a place's root (its sidebar row pressed again).
    mutating func popToRoot() {
        guard !current.path.isEmpty else { return }
        record()
        current.path = []
    }

    private mutating func record() {
        back.append(current)
        if back.count > 120 { back.removeFirst(back.count - 120) }
        forward.removeAll()
    }
}
