import Darwin
import MetricKit
import SwiftUI
import UserNotifications

/// Simpl Courses for iOS: the student's own Canvas site in a full-screen web view, with the
/// extension injected into it. Signing in happens on Canvas's real login page inside the app.
@main
struct SimplCoursesApp: App {
    @StateObject private var session = AppSession()

    init() {
        CrashCatcher.shared.start() // (build 46: first, so what goes wrong from here on is kept)
        UNUserNotificationCenter.current().delegate = Reminders.shared // (a reminder pressed, even from a cold launch, opens its work)
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(session)
        }
        // iOS woke the app to look for new activity (Background App Refresh: it picks the moment)
        .backgroundTask(.appRefresh(Activity.taskID)) {
            await Activity.shared.backgroundCheck()
        }
    }
}

/// (iPhone 1.8.1, build 46) What made the app close unexpectedly, kept so the student can pass it on: the next time the
/// app opens it says so and offers to copy the details (ContentView). Three ways in, as each sees what the others miss:
/// - the app's error output, written to a file of its own (Application Support ▸ Diagnostics ▸ errors.log) — Swift's
///   "Fatal error: …" and an uncaught exception's "Terminating app … reason: …" are written there just before the end;
///   read back at the next launch for the lines round the last such message. (Not under the debugger, nor in the
///   simulator suite, whose consoles read that output themselves.)
/// - an uncaught Objective-C exception, its name, reason and the calls it came through, kept as it happens;
/// - MetricKit's crash report, which iOS hands over at a later launch: the signal, the exception, and the crashed
///   thread's calls (by library and offset).
/// Nothing leaves the phone unless the student copies it and sends it themselves.
final class CrashCatcher: NSObject, ObservableObject, MXMetricManagerSubscriber {
    static let shared = CrashCatcher()
    /// What went wrong last time, not yet seen.
    @Published var report: String?

    private static let key = "SimplCrashReport"
    private static let markers = ["Fatal error", "Terminating app due to uncaught exception", "Precondition failed",
                                  "Assertion failed", "Unexpectedly found nil", "Simultaneous accesses", "Index out of range"]

    private static var folder: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Diagnostics", isDirectory: true)
    }

    private static var log: URL { folder.appendingPathComponent("errors.log") }

    func start() {
        let fm = FileManager.default
        try? fm.createDirectory(at: Self.folder, withIntermediateDirectories: true)
        // the last run's error output: the lines round its last fatal message, if it ended with one
        if let data = try? Data(contentsOf: Self.log), let text = String(data: data, encoding: .utf8) {
            let lines = text.components(separatedBy: "\n")
            if let i = lines.lastIndex(where: { line in Self.markers.contains { line.contains($0) } }) {
                let excerpt = lines[max(0, i - 8)..<min(lines.count, i + 40)].joined(separator: "\n")
                Self.store(kind: "error output", excerpt)
            }
        }
        report = UserDefaults.standard.string(forKey: Self.key)
        // this run's error output into the file
        if !Self.debugging, !UserDefaults.standard.bool(forKey: "SimplShotSettle") {
            fm.createFile(atPath: Self.log.path, contents: nil)
            let fd = open(Self.log.path, O_WRONLY | O_TRUNC)
            if fd >= 0 {
                dup2(fd, STDERR_FILENO)
                close(fd)
            }
        }
        NSSetUncaughtExceptionHandler { e in
            let calls = e.callStackSymbols.prefix(40).joined(separator: "\n")
            CrashCatcher.store(kind: "uncaught exception", "\(e.name.rawValue): \(e.reason ?? "")\n\(calls)")
        }
        MXMetricManager.shared.add(self)
    }

    /// Seen: copied, or let go.
    func clear() {
        UserDefaults.standard.removeObject(forKey: Self.key)
        report = nil
    }

    /// Kept for the next launch (at most the last 12,000 characters of what has been kept).
    static func store(kind: String, _ text: String) {
        let info = Bundle.main.infoDictionary ?? [:]
        let version = "\(info["CFBundleShortVersionString"] as? String ?? "?") (\(info["CFBundleVersion"] as? String ?? "?"))"
        let head = "Simpl Courses \(version) · \(ProcessInfo.processInfo.operatingSystemVersionString) · \(kind)"
        let kept = [UserDefaults.standard.string(forKey: key), "\(head)\n\(text)"].compactMap { $0 }.joined(separator: "\n\n")
        UserDefaults.standard.set(String(kept.suffix(12_000)), forKey: key)
        UserDefaults.standard.synchronize() // (an uncaught exception: the app ends straight after this)
    }

    // MARK: MetricKit

    func didReceive(_ payloads: [MXDiagnosticPayload]) {
        var any = false
        for payload in payloads {
            for c in payload.crashDiagnostics ?? [] {
                var lines: [String] = ["build \(c.metaData.applicationBuildVersion)"]
                if let t = c.exceptionType { lines.append("exception type \(t)") }
                if let s = c.signal { lines.append("signal \(s)") }
                if let r = c.terminationReason { lines.append("termination: \(r)") }
                if let e = c.exceptionReason { lines.append("\(e.exceptionName): \(e.composedMessage)") }
                lines.append(contentsOf: Self.calls(c.callStackTree))
                Self.store(kind: "MetricKit crash report", lines.joined(separator: "\n"))
                any = true
            }
            // (build 47) a freeze the app came through — or did not (iOS ending an app that froze is a crash report of
            // its own, 0x8BADF00D, above)
            for h in payload.hangDiagnostics ?? [] {
                var lines: [String] = ["build \(h.metaData.applicationBuildVersion)", "froze for \(h.hangDuration)"]
                lines.append(contentsOf: Self.calls(h.callStackTree))
                Self.store(kind: "MetricKit hang report", lines.joined(separator: "\n"))
                any = true
            }
        }
        guard any else { return }
        DispatchQueue.main.async { self.report = UserDefaults.standard.string(forKey: Self.key) }
    }

    /// The crashed thread's calls, innermost first, by library and offset (MetricKit's tree, flattened).
    private static func calls(_ tree: MXCallStackTree) -> [String] {
        guard let root = try? JSONSerialization.jsonObject(with: tree.jsonRepresentation()) as? [String: Any],
              let stacks = root["callStacks"] as? [[String: Any]] else { return [] }
        let stack = stacks.first { ($0["threadAttributed"] as? Bool) == true } ?? stacks.first
        var frame = (stack?["callStackRootFrames"] as? [[String: Any]])?.first
        var out: [String] = []
        while let f = frame, out.count < 40 {
            out.append("\(f["binaryName"] as? String ?? "?") +\(f["offsetIntoBinaryTextSegment"] ?? "?")")
            frame = (f["subFrames"] as? [[String: Any]])?.first
        }
        return out
    }

    /// The app is being run under the debugger (Xcode's console shows its error output: it is left there).
    private static var debugging: Bool {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid()]
        guard sysctl(&mib, u_int(mib.count), &info, &size, nil, 0) == 0 else { return false }
        return (info.kp_proc.p_flag & P_TRACED) != 0
    }
}
