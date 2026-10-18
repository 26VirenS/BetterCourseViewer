import AppKit
import Charts
import Darwin
import OSLog
import SwiftUI

// (1.3.109) The Developer window's live pages: the app's own log, what it costs the Mac (processor, memory, threads,
// storage) and what the engine has been asked. Shell/Developer.swift opens them; nothing here runs until one is shown.

// MARK: - Stats: every call the engine makes

/// Every call the engine has made since launch — how many, how long, how many failed — kept by `Engine.fetch`.
@MainActor
final class DevStats: ObservableObject {
    static let shared = DevStats()

    struct Call: Identifiable {
        let name: String
        var count = 0
        var failures = 0
        var readsAhead = 0
        var totalMs = 0.0
        var slowestMs = 0.0
        var lastMs = 0.0
        var last = Date.distantPast
        var id: String { name }
        var averageMs: Double { count == 0 ? 0 : totalMs / Double(count) }
    }

    @Published private(set) var calls: [String: Call] = [:]
    let since = Date()
    private(set) var reset = Date()

    func record(_ name: String, ms: Double, ok: Bool, quiet: Bool) {
        var c = calls[name] ?? Call(name: name)
        c.count += 1
        if !ok { c.failures += 1 }
        if quiet { c.readsAhead += 1 }
        c.totalMs += ms
        c.slowestMs = max(c.slowestMs, ms)
        c.lastMs = ms
        c.last = Date()
        calls[name] = c
    }

    func clear() {
        calls = [:]
        reset = Date()
    }

    var total: Int { calls.values.reduce(0) { $0 + $1.count } }
    var failures: Int { calls.values.reduce(0) { $0 + $1.failures } }

    /// The table as text, for a bug report.
    var text: String {
        var out = "Engine calls since \(reset.formatted(date: .omitted, time: .standard)): \(total), \(failures) failed\n"
        out += "name\tcount\tfailed\tahead\tavg ms\tslowest ms\tlast ms\n"
        for c in calls.values.sorted(by: { $0.count > $1.count }) {
            out += "\(c.name)\t\(c.count)\t\(c.failures)\t\(c.readsAhead)\t\(Int(c.averageMs))\t\(Int(c.slowestMs))\t\(Int(c.lastMs))\n"
        }
        return out
    }
}

// MARK: - System: what the app costs the Mac

struct DevSample: Identifiable {
    let at: Date
    let cpu: Double      // percent of one core
    let memoryMB: Double // the footprint Activity Monitor shows
    let threads: Int
    var id: Date { at }
}

@MainActor
final class DevMonitor: ObservableObject {
    static let shared = DevMonitor()
    @Published private(set) var samples: [DevSample] = []
    @Published private(set) var storage: [(String, Int64)] = []
    private var timer: Timer?
    private var watchers = 0

    /// Sampling runs while a page shows it (once a second, the last two minutes kept).
    func watch() {
        watchers += 1
        guard timer == nil else { return }
        sample()
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in Task { @MainActor in DevMonitor.shared.sample() } }
    }

    func unwatch() {
        watchers = max(0, watchers - 1)
        if watchers == 0 { timer?.invalidate(); timer = nil }
    }

    func sample() {
        let s = DevSample(at: Date(), cpu: Self.cpu(), memoryMB: Double(Self.footprint()) / 1_048_576, threads: Self.threadCount())
        samples.append(s)
        if samples.count > 120 { samples.removeFirst(samples.count - 120) }
    }

    /// The app's folders and what they hold, measured off the main thread.
    func measureStorage() {
        Task.detached(priority: .utility) {
            let fm = FileManager.default
            let id = Bundle.main.bundleIdentifier ?? "Simpl"
            var places: [(String, URL)] = []
            if let a = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first { places.append(("Application Support", a.appendingPathComponent(id))) }
            if let c = fm.urls(for: .cachesDirectory, in: .userDomainMask).first { places.append(("Caches", c.appendingPathComponent(id))) }
            if let l = fm.urls(for: .libraryDirectory, in: .userDomainMask).first {
                places.append(("Web data", l.appendingPathComponent("WebKit").appendingPathComponent(id)))
                places.append(("Preferences", l.appendingPathComponent("Preferences").appendingPathComponent("\(id).plist")))
            }
            let sized = places.map { ($0.0, Self.size(of: $0.1)) }
            await MainActor.run { DevMonitor.shared.storage = sized }
        }
    }

    nonisolated private static func size(of url: URL) -> Int64 {
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: url.path, isDirectory: &isDir) else { return 0 }
        if !isDir.boolValue { return (try? fm.attributesOfItem(atPath: url.path)[.size] as? Int64) ?? 0 }
        var total: Int64 = 0
        if let e = fm.enumerator(at: url, includingPropertiesForKeys: [.totalFileAllocatedSizeKey], options: [], errorHandler: nil) {
            for case let f as URL in e {
                total += Int64((try? f.resourceValues(forKeys: [.totalFileAllocatedSizeKey]).totalFileAllocatedSize) ?? 0)
            }
        }
        return total
    }

    /// The processor the app's threads are using now, as a percent of one core (Activity Monitor's % CPU).
    static func cpu() -> Double {
        var list: thread_act_array_t?
        var count: mach_msg_type_number_t = 0
        guard task_threads(mach_task_self_, &list, &count) == KERN_SUCCESS, let list else { return 0 }
        defer { vm_deallocate(mach_task_self_, vm_address_t(UInt(bitPattern: list)), vm_size_t(Int(count) * MemoryLayout<thread_t>.stride)) }
        var total = 0.0
        for i in 0..<Int(count) {
            var info = thread_basic_info()
            var n = mach_msg_type_number_t(MemoryLayout<thread_basic_info>.size / MemoryLayout<integer_t>.size)
            let r = withUnsafeMutablePointer(to: &info) { p in
                p.withMemoryRebound(to: integer_t.self, capacity: Int(n)) { thread_info(list[i], thread_flavor_t(THREAD_BASIC_INFO), $0, &n) }
            }
            if r == KERN_SUCCESS, info.flags & TH_FLAGS_IDLE == 0 { total += Double(info.cpu_usage) / Double(TH_USAGE_SCALE) * 100 }
        }
        return total
    }

    static func threadCount() -> Int {
        var list: thread_act_array_t?
        var count: mach_msg_type_number_t = 0
        guard task_threads(mach_task_self_, &list, &count) == KERN_SUCCESS, let list else { return 0 }
        vm_deallocate(mach_task_self_, vm_address_t(UInt(bitPattern: list)), vm_size_t(Int(count) * MemoryLayout<thread_t>.stride))
        return Int(count)
    }

    /// The memory footprint (what Activity Monitor calls Memory), in bytes.
    static func footprint() -> UInt64 {
        var info = task_vm_info_data_t()
        var n = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<integer_t>.size)
        let r = withUnsafeMutablePointer(to: &info) { p in
            p.withMemoryRebound(to: integer_t.self, capacity: Int(n)) { task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &n) }
        }
        return r == KERN_SUCCESS ? info.phys_footprint : 0
    }

    /// When this process started (the kernel's own note of it).
    static let started: Date = {
        var info = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid()]
        guard sysctl(&mib, 4, &info, &size, nil, 0) == 0 else { return Date() }
        let t = info.kp_proc.p_un.__p_starttime
        return Date(timeIntervalSince1970: Double(t.tv_sec) + Double(t.tv_usec) / 1_000_000)
    }()
}

// MARK: - Logs: the app's own log

struct DevLogLine: Identifiable, Hashable {
    let id: Int
    let at: Date
    let level: String
    let category: String
    let text: String
}

/// The app's own log (what it writes with NSLog and os_log), read from the system's log store for this process.
@MainActor
final class DevLog: ObservableObject {
    static let shared = DevLog()
    @Published private(set) var lines: [DevLogLine] = []
    @Published private(set) var problem: String?
    @Published var following = true
    private var last = Date.distantPast
    private var next = 0
    private var timer: Timer?
    private var reading = false

    func watch() {
        guard timer == nil else { return }
        read()
        timer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { _ in Task { @MainActor in if DevLog.shared.following { DevLog.shared.read() } } }
    }

    func unwatch() { timer?.invalidate(); timer = nil }

    func clear() { lines = [] }

    func read() {
        guard !reading else { return }
        reading = true
        let since = last == .distantPast ? DevMonitor.started : last
        Task.detached(priority: .utility) {
            var got: [(Date, String, String, String)] = []
            var failed: String?
            do {
                let store = try OSLogStore(scope: .currentProcessIdentifier)
                for e in try store.getEntries(at: store.position(date: since)) {
                    guard let l = e as? OSLogEntryLog, l.date > since else { continue }
                    let level: String
                    switch l.level {
                    case .error: level = "error"
                    case .fault: level = "fault"
                    case .debug: level = "debug"
                    case .info: level = "info"
                    default: level = "notice"
                    }
                    got.append((l.date, level, l.subsystem.isEmpty ? l.sender : l.category.isEmpty ? l.subsystem : l.category, l.composedMessage))
                }
            } catch {
                failed = error.localizedDescription
            }
            await MainActor.run {
                let log = DevLog.shared
                log.reading = false
                log.problem = failed
                guard !got.isEmpty else { return }
                for g in got {
                    log.lines.append(DevLogLine(id: log.next, at: g.0, level: g.1, category: g.2, text: g.3))
                    log.next += 1
                }
                log.last = got.last!.0
                if log.lines.count > 5000 { log.lines.removeFirst(log.lines.count - 5000) }
            }
        }
    }

    static func text(_ lines: [DevLogLine]) -> String {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return lines.map { "\(f.string(from: $0.at)) [\($0.level)] \($0.category): \($0.text)" }.joined(separator: "\n")
    }
}

// MARK: - The pages

struct DevLogsPage: View {
    @ObservedObject private var log = DevLog.shared
    @State private var filter = ""
    @State private var simplOnly = true
    @State private var level = "all"

    private var shown: [DevLogLine] {
        log.lines.filter { l in
            (!simplOnly || l.text.contains("[Simpl]") || l.category == "Simpl")
                && (level == "all" || (level == "problems" ? (l.level == "error" || l.level == "fault" || l.text.localizedCaseInsensitiveContains("error") || l.text.localizedCaseInsensitiveContains("fail")) : true))
                && (filter.isEmpty || l.text.localizedCaseInsensitiveContains(filter) || l.category.localizedCaseInsensitiveContains(filter))
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                TextField("Filter", text: $filter).textFieldStyle(.roundedBorder).frame(maxWidth: 260)
                Picker("", selection: $level) {
                    Text("Everything").tag("all")
                    Text("Problems").tag("problems")
                }
                .pickerStyle(.segmented).frame(width: 190).labelsHidden()
                Toggle("Simpl's own", isOn: $simplOnly).toggleStyle(.checkbox)
                Toggle("Follow", isOn: $log.following).toggleStyle(.checkbox)
                Spacer()
                Text("\(shown.count) of \(log.lines.count)").font(.caption.monospacedDigit()).foregroundStyle(.secondary)
                Button("Copy") { copyToPasteboard(DevLog.text(shown)) }
                Button("Save…") { save() }
                Button("Clear") { log.clear() }
            }
            .padding(10)
            Divider()
            if let p = log.problem, log.lines.isEmpty {
                ContentUnavailableView("The log could not be read", systemImage: "exclamationmark.triangle", description: Text(p))
            } else {
                ScrollViewReader { proxy in
                    List(shown) { l in
                        HStack(alignment: .firstTextBaseline, spacing: 8) {
                            Text(l.at.formatted(.dateTime.hour().minute().second().secondFraction(.fractional(3))))
                                .foregroundStyle(.secondary)
                            Text(l.level)
                                .foregroundStyle(l.level == "error" || l.level == "fault" ? .red : l.level == "debug" ? .secondary : .primary)
                                .frame(width: 44, alignment: .leading)
                            Text(l.text).textSelection(.enabled)
                        }
                        .font(.system(size: 11, design: .monospaced))
                        .id(l.id)
                    }
                    .onChange(of: log.lines.count) { _, _ in
                        if log.following, let last = shown.last { proxy.scrollTo(last.id, anchor: .bottom) }
                    }
                }
            }
        }
        .onAppear { log.watch() }
        .onDisappear { log.unwatch() }
    }

    private func save() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "Simpl Log \(Date().formatted(.iso8601.year().month().day())).txt"
        if panel.runModal() == .OK, let url = panel.url { try? DevLog.text(shown).write(to: url, atomically: true, encoding: .utf8) }
    }
}

struct DevSystemPage: View {
    @ObservedObject private var mon = DevMonitor.shared
    private let info = ProcessInfo.processInfo

    var body: some View {
        let last = mon.samples.last
        Form {
            Section("Now") {
                LabeledContent("Processor", value: String(format: "%.1f %% of one core", last?.cpu ?? 0))
                LabeledContent("Memory", value: String(format: "%.0f MB", last?.memoryMB ?? 0))
                LabeledContent("Threads", value: "\(last?.threads ?? 0)")
                LabeledContent("Running for", value: Duration.seconds(Date().timeIntervalSince(DevMonitor.started)).formatted(.units(allowed: [.hours, .minutes, .seconds], width: .abbreviated)))
                LabeledContent("Thermal state", value: thermal)
                LabeledContent("Low Power Mode", value: info.isLowPowerModeEnabled ? "On" : "Off")
                LabeledContent("This Mac", value: "\(info.processorCount) cores, \(ByteCountFormatter.string(fromByteCount: Int64(info.physicalMemory), countStyle: .memory)), macOS \(info.operatingSystemVersionString)")
            }
            Section("Processor, the last two minutes") {
                Chart(mon.samples) { s in
                    AreaMark(x: .value("Time", s.at), y: .value("%", s.cpu)).foregroundStyle(.blue.opacity(0.25))
                    LineMark(x: .value("Time", s.at), y: .value("%", s.cpu)).foregroundStyle(.blue)
                }
                .chartYAxisLabel("% of one core")
                .frame(height: 130)
            }
            Section("Memory, the last two minutes") {
                Chart(mon.samples) { s in
                    AreaMark(x: .value("Time", s.at), y: .value("MB", s.memoryMB)).foregroundStyle(.purple.opacity(0.25))
                    LineMark(x: .value("Time", s.at), y: .value("MB", s.memoryMB)).foregroundStyle(.purple)
                }
                .chartYAxisLabel("MB")
                .frame(height: 130)
            }
            Section("Storage") {
                ForEach(mon.storage, id: \.0) { s in
                    LabeledContent(s.0, value: ByteCountFormatter.string(fromByteCount: s.1, countStyle: .file))
                }
                Button("Measure Again") { mon.measureStorage() }
            }
            Section {
                HStack {
                    Button("Copy Snapshot") { copyToPasteboard(snapshot) }
                    Button("Open Activity Monitor") { NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Activity Monitor.app")) }
                }
            }
        }
        .formStyle(.grouped)
        .onAppear { mon.watch(); mon.measureStorage() }
        .onDisappear { mon.unwatch() }
    }

    private var thermal: String {
        switch info.thermalState {
        case .nominal: return "Nominal"
        case .fair: return "Fair"
        case .serious: return "Serious"
        case .critical: return "Critical"
        @unknown default: return "Unknown"
        }
    }

    private var snapshot: String {
        let s = mon.samples
        let cpu = s.map(\.cpu), mem = s.map(\.memoryMB)
        let avg = { (v: [Double]) in v.isEmpty ? 0 : v.reduce(0, +) / Double(v.count) }
        var out = "Simpl for Mac system snapshot, \(Date().formatted())\n"
        out += String(format: "Processor now %.1f%%, average %.1f%%, peak %.1f%% (last %d s)\n", cpu.last ?? 0, avg(cpu), cpu.max() ?? 0, s.count)
        out += String(format: "Memory now %.0f MB, average %.0f MB, peak %.0f MB\n", mem.last ?? 0, avg(mem), mem.max() ?? 0)
        out += "Threads \(s.last?.threads ?? 0), thermal \(thermal)\n"
        for st in mon.storage { out += "\(st.0): \(ByteCountFormatter.string(fromByteCount: st.1, countStyle: .file))\n" }
        return out
    }
}

struct DevStatsPage: View {
    @ObservedObject private var stats = DevStats.shared
    @State private var order = [KeyPathComparator(\DevStats.Call.count, order: .reverse)]

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 16) {
                stat("Calls", "\(stats.total)")
                stat("Failed", "\(stats.failures)")
                stat("Kinds", "\(stats.calls.count)")
                stat("Since", stats.reset.formatted(date: .omitted, time: .shortened))
                Spacer()
                Button("Copy") { copyToPasteboard(stats.text) }
                Button("Reset") { stats.clear() }
            }
            .padding(12)
            Divider()
            Table(stats.calls.values.sorted(using: order), sortOrder: $order) {
                TableColumn("Call", value: \.name)
                TableColumn("Count", value: \.count) { Text("\($0.count)").monospacedDigit() }.width(60)
                TableColumn("Failed", value: \.failures) { c in Text("\(c.failures)").monospacedDigit().foregroundStyle(c.failures > 0 ? .red : .primary) }.width(60)
                TableColumn("Read ahead", value: \.readsAhead) { Text("\($0.readsAhead)").monospacedDigit() }.width(80)
                TableColumn("Average", value: \.averageMs) { Text("\(Int($0.averageMs)) ms").monospacedDigit() }.width(80)
                TableColumn("Slowest", value: \.slowestMs) { Text("\(Int($0.slowestMs)) ms").monospacedDigit() }.width(80)
                TableColumn("Last", value: \.last) { Text($0.last.formatted(date: .omitted, time: .standard)) }.width(90)
            }
        }
    }

    private func stat(_ title: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.caption).foregroundStyle(.secondary)
            Text(value).font(.title3.monospacedDigit().weight(.semibold))
        }
    }
}
