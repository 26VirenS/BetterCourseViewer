import Foundation
import WebKit

/// Content rules for the Canvas web view, the biggest lever on load time. Two lists:
/// - telemetry no page needs (New Relic, Pendo, analytics), applied everywhere;
/// - Canvas's own script and stylesheet bundles, applied only on pages the interface draws itself
///   from the API. There they would be downloaded, parsed and run to build a page that stays hidden.
///   They stay on for pages handed back to Canvas (a quiz being taken, a file preview, an external
///   tool, anything with ?bcv=native, everything when the interface is off) and inside frames, so
///   tools keep working. The page's inline scripts (ENV, the CSRF token) are not resources and are
///   never affected.
enum ContentRules {
    private static let version = "1"
    private static var telemetry: WKContentRuleList?
    private static var bundles: WKContentRuleList?

    /// Compiles (or looks up) both lists; call before the first load so the first page is already fast.
    static func prepare(host: String, completion: @escaping () -> Void) {
        let group = DispatchGroup()
        group.enter()
        load(identifier: "simpl-telemetry-\(version)", rules: telemetryRules()) { telemetry = $0; group.leave() }
        group.enter()
        load(identifier: "simpl-bundles-\(version)-\(host.lowercased())", rules: bundleRules(host: host)) { bundles = $0; group.leave() }
        group.notify(queue: .main, execute: completion)
    }

    /// Sets the lists for the navigation about to start.
    static func apply(to controller: WKUserContentController, blockCanvasBundles: Bool) {
        controller.removeAllContentRuleLists()
        if let list = telemetry { controller.add(list) }
        if blockCanvasBundles, let list = bundles { controller.add(list) }
    }

    private static func load(identifier: String, rules: [[String: Any]], completion: @escaping (WKContentRuleList?) -> Void) {
        guard let store = WKContentRuleListStore.default(),
              let data = try? JSONSerialization.data(withJSONObject: rules),
              let json = String(data: data, encoding: .utf8) else {
            completion(nil)
            return
        }
        store.lookUpContentRuleList(forIdentifier: identifier) { list, _ in
            if let list = list {
                completion(list)
                return
            }
            store.compileContentRuleList(forIdentifier: identifier, encodedContentRuleList: json) { list, error in
                if let error = error { print("[Simpl Courses] content rules:", error.localizedDescription) }
                completion(list)
            }
        }
    }

    private static func block(_ filter: String, types: [String], topFrameOnly: Bool = false) -> [String: Any] {
        var trigger: [String: Any] = ["url-filter": filter, "resource-type": types]
        if topFrameOnly { trigger["load-context"] = ["top-frame"] }
        return ["trigger": trigger, "action": ["type": "block"]]
    }

    private static func telemetryRules() -> [[String: Any]] {
        [
            "^https://js-agent\\.newrelic\\.com/",
            "^https://bam\\.nr-data\\.net/",
            "^https://.*\\.pendo\\.io/",
            "^https://.*\\.sentry\\.io/",
            "^https://www\\.google-analytics\\.com/",
            "^https://www\\.googletagmanager\\.com/",
            "^https://.*\\.fullstory\\.com/",
        ].map { block($0, types: ["script", "raw", "image"]) }
    }

    /// Canvas serves its bundles from its CDN (cloudfront for Instructure-hosted sites), from /dist/ on
    /// the site itself when self-hosted, and a school's theme JS/CSS from instructure-uploads.
    private static func bundleRules(host: String) -> [[String: Any]] {
        let site = host.lowercased().replacingOccurrences(of: ".", with: "\\.")
        return [
            "^https://.*\\.cloudfront\\.net/dist/",
            "^https://\(site)/dist/",
            "^https://.*\\.instructure\\.com/dist/",
            "^https://instructure-uploads.*\\.amazonaws\\.com/",
        ].map { block($0, types: ["script", "style-sheet"], topFrameOnly: true) }
    }
}

/// Which Canvas pages the interface draws itself: a port of app.js's parseRoute and the course and
/// group tab switches. Anything not listed is Canvas's own page (drawn with the shell over it).
enum RenderedRoutes {
    static func isRendered(_ url: URL, host: String, interfaceOn: Bool) -> Bool {
        guard interfaceOn, url.host?.lowercased() == host.lowercased() else { return false }
        if (url.query ?? "").contains("bcv=native") { return false }
        var path = url.path
        while path.count > 1 && path.hasSuffix("/") { path.removeLast() }
        if path.isEmpty { path = "/" }
        if ["/", "/dashboard", "/courses", "/groups", "/calendar", "/calendar2", "/conversations", "/grades", "/todo"].contains(path) { return true }
        let parts = path.split(separator: "/").map(String.init)
        guard parts.count >= 2, parts[0] == "courses" || parts[0] == "groups", Int(parts[1]) != nil else { return false }
        let inCourse = parts[0] == "courses"
        let rest = Array(parts.dropFirst(2))
        if rest.isEmpty { return true } // home and the stream
        let isNumber = { (s: String) in Int(s) != nil }
        switch rest[0] {
        case "announcements", "discussion_topics": return rest.count == 1 || (rest.count == 2 && isNumber(rest[1]))
        case "users", "wiki": return rest.count == 1
        case "pages": return rest.count <= 2
        case "files": return rest.count == 1 || rest[1] == "folder" // a file preview is Canvas's
        case "groups": return inCourse && rest.count == 1
        case "assignments": return inCourse && (rest.count == 1 || (rest.count == 2 && (isNumber(rest[1]) || rest[1] == "syllabus")))
        case "quizzes", "grades": return inCourse && (rest.count == 1 || (rest.count == 2 && isNumber(rest[1])))
        case "modules": return inCourse && rest.count == 1
        default: return false
        }
    }
}
