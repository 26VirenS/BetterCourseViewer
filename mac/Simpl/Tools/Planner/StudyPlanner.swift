import AppKit
import SwiftUI

// (tools) The Study Planner, in the Study group: study blocks from the student's due dates. It reads the upcoming work
// the Dashboard, To Do and the Calendar read — the engine's `reminders` (three weeks of open work with its due dates, a
// task of the student's own among it), with each piece's points and colour from the Dashboard's list and, past that
// list's eight days, from the Calendar's events — and plans blocks of study for it (Model/StudyPlan.swift: each
// piece's need from its points and its kind, spread over the days to its due date, each day's hours filled by
// priority). Settings: hours a day for each weekday, the block's length, the time of day, how many days before the due
// date to finish, which courses. A week view (seven columns, today first) and a list; a block ticked is done — kept
// on this Mac (PlanPrefs), taken off its work's need — and Regenerate plans again round what is done; Add to Calendar
// saves the blocks as an .ics file in Downloads. The pin is today's blocks as a checklist. Nothing is written to Canvas.

// MARK: - The work, from the engine

enum StudySource {
    private static let isoFractional: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()
    private static let isoPlain = ISO8601DateFormatter()
    private static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    static func date(_ s: String?) -> Date? {
        guard let s, !s.isEmpty else { return nil }
        return isoFractional.date(from: s) ?? isoPlain.date(from: s)
    }

    /// The student's upcoming work as the planner takes it: `reminders` for the pieces and their due dates, the
    /// Dashboard's list for their points and colours by id, the Calendar's three weeks for the points of what the list
    /// (eight days) leaves out, matched by name and day.
    static func items(_ engine: Engine) async throws -> [StudyItem] {
        let reminders = try await engine.read("reminders", as: RemindersData.self)
        let list = try? await engine.read("dashList", as: DashListData.self)
        var byId: [String: DashRow] = [:]
        for day in list?.days ?? [] { for r in day.rows { byId[r.id] = r } }
        let today = Calendar.current.startOfDay(for: Date())
        let iso = ISO8601DateFormatter()
        let span: [String: Any] = ["from": iso.string(from: today), "to": iso.string(from: Calendar.current.date(byAdding: .day, value: 21, to: today) ?? today)]
        let events = (try? await engine.read("calendar", span, as: CalendarData.self))?.events ?? []
        var byName: [String: CalEvent] = [:]
        for ev in events where ev.kind != "Event" { byName[ev.title.lowercased() + "|" + ev.day] = ev }
        var out: [StudyItem] = []
        for r in reminders.items {
            guard let due = date(r.due) else { continue }
            let row = byId[r.id]
            let ev = byName[r.title.lowercased() + "|" + dayKey.string(from: due)]
            let points = StudyPoints.parse(row?.points) ?? ev.flatMap { StudyPoints.parse(PreviewFormat.points(in: $0.sub)) }
            let kind = r.kind.isEmpty ? (row?.kind ?? "Assignment") : r.kind
            out.append(StudyItem(id: r.id, title: r.title, course: r.course ?? "", color: row?.color ?? ev?.color, kind: kind, points: points, due: due))
        }
        return out
    }
}

// MARK: - What the planner keeps

/// What the Study Planner keeps on this Mac (UserDefaults `SimplPlan.<field>`): the settings (`settings`,
/// StudySettings.text), the blocks done (`done`, StudyDone's line) and the view last shown (`view`). A picture's launch
/// (-SimplShotFile) keeps them in a suite of its own that starts empty, as ChemPrefs does.
enum PlanPrefs {
    private static let store: UserDefaults = {
        guard UserDefaults.standard.string(forKey: "SimplShotFile") != nil else { return .standard }
        let name = "SimplPlanPicture"
        guard let d = UserDefaults(suiteName: name) else { return .standard }
        d.removePersistentDomain(forName: name)
        return d
    }()

    static func string(_ field: String) -> String? { store.string(forKey: "SimplPlan.\(field)") }

    static func set(_ field: String, _ value: String?) {
        guard store.string(forKey: "SimplPlan.\(field)") != value else { return }
        store.set(value, forKey: "SimplPlan.\(field)")
    }

    /// The launch opened this tool (-SimplTool plan): its own flags are read as the tool opens.
    static var launchedHere: Bool { ChemTools.launched == .plan }

    /// The settings as kept, with the launch's over them (-SimplPlanHours 2: that many hours every day; -SimplPlanBlock
    /// 45).
    static func settings() -> StudySettings {
        var s = string("settings").flatMap { StudySettings(text: $0) } ?? StudySettings()
        guard launchedHere else { return s }
        let d = UserDefaults.standard
        if let h = d.string(forKey: "SimplPlanHours").flatMap({ Double($0) }), h >= 0, h <= 24 { s.hours = Array(repeating: h, count: 7) }
        if let b = d.string(forKey: "SimplPlanBlock").flatMap({ Int($0) }), StudySettings.blockChoices.contains(b) { s.blockMinutes = b }
        return s
    }

    /// The blocks done, with the launch's (-SimplPlanDone "<item id>:2").
    static func done() -> [String: Int] {
        var d = StudyDone.parse(string("done"))
        if launchedHere { d.merge(StudyDone.parse(UserDefaults.standard.string(forKey: "SimplPlanDone"))) { _, new in new } }
        return d
    }

    /// The view: the launch's (-SimplPlanView list), else the one kept, else the week.
    static func view() -> String {
        if launchedHere, let v = UserDefaults.standard.string(forKey: "SimplPlanView"), ["week", "list"].contains(v) { return v }
        return string("view") == "list" ? "list" : "week"
    }

    static var downloads: URL {
        FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Downloads", isDirectory: true)
    }
}

// MARK: - The tool

/// Study Planner: the week (or the list) and each piece of work's progress on the left, the settings on the right.
struct StudyPlannerTool: View {
    @EnvironmentObject private var engine: Engine
    @State private var items: [StudyItem]?
    @State private var failed: String?
    @State private var settings = PlanPrefs.settings()
    @State private var done = PlanPrefs.done()
    @State private var view = PlanPrefs.view()
    @State private var plan = StudyPlan.empty
    /// The blocks ticked since the plan was made (they stay in place, struck, until Regenerate).
    @State private var ticked: Set<String> = []
    @State private var note: String?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private var cal: Calendar { Calendar.current }
    private var courses: [String] { Array(Set((items ?? []).map(\.course).filter { !$0.isEmpty })).sorted() }

    var body: some View {
        ToolPage {
            if let items {
                ToolColumns(sideWidth: 340, breakpoint: 1000) {
                    VStack(alignment: .leading, spacing: 22) {
                        bar
                        if let note { ToolNote(text: note, symbol: "checkmark.circle.fill", tint: .green) }
                        if items.isEmpty {
                            EmptyNote(text: "Nothing coming up in the next three weeks: nothing to plan.", symbol: "calendar")
                                .padding(.horizontal, 18)
                                .card()
                        } else {
                            if view == "week" { week } else { list }
                            progress
                        }
                    }
                } side: {
                    settingsPanel
                }
            } else if let failed {
                ToolNote(text: failed)
                Button("Try Again") { Task { await load() } }
                    .glassButton()
            } else {
                HStack(spacing: 10) {
                    ProgressView().controlSize(.small)
                    Text("Reading what is coming up…").font(.sCallout).foregroundStyle(.secondary)
                }
                .padding(.top, 20)
            }
        }
        .task { await load() }
        .onChange(of: settings) { _, s in
            PlanPrefs.set("settings", s.text)
            replan()
        }
        .onChange(of: view) { _, v in PlanPrefs.set("view", v) }
    }

    private func load() async {
        failed = nil
        do {
            let got = try await StudySource.items(engine)
            items = got
            replan()
        } catch {
            if items == nil { failed = "Your upcoming work could not be read: \(error.localizedDescription)" }
        }
    }

    /// The plan made again round what is done (Regenerate, a setting changed, the work read afresh).
    private func replan() {
        guard let items else { return }
        let p = StudyPlanner.plan(items, settings: settings, done: done, now: Date(), calendar: cal)
        withAnimation(reduceMotion ? nil : Motion.gentle) {
            plan = p
            ticked = []
        }
    }

    /// A block ticked (or unticked): done, kept, taken off its work's need at the next Regenerate.
    private func tick(_ b: StudyBlock, _ on: Bool) {
        if on { ticked.insert(b.id) } else { ticked.remove(b.id) }
        done[b.itemId] = max(0, (done[b.itemId] ?? 0) + (on ? 1 : -1))
        PlanPrefs.set("done", StudyDone.text(done))
    }

    // MARK: The bar

    private var bar: some View {
        ViewThatFits(in: .horizontal) {
            HStack(spacing: 10) {
                viewPicker
                Spacer(minLength: 8)
                summary
                actions
            }
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 10) {
                    viewPicker
                    Spacer(minLength: 8)
                    actions
                }
                summary
            }
        }
    }

    private var viewPicker: some View {
        Picker("View", selection: $view) {
            Text("Week").tag("week")
            Text("List").tag("list")
        }
        .pickerStyle(.segmented)
        .labelsHidden()
        .controlSize(.large)
        .frame(width: 180)
    }

    private var actions: some View {
        HStack(spacing: 8) {
            Button {
                replan()
            } label: {
                Label("Regenerate", systemImage: "arrow.clockwise").font(.sCallout)
            }
            .glassButton()
            .controlSize(.large)
            .help("Plan again round the blocks you have done")
            Button {
                addToCalendar()
            } label: {
                Label("Add to Calendar", systemImage: "calendar.badge.plus").font(.sCallout)
            }
            .glassButton(prominent: true, tint: ToolKind.plan.color)
            .controlSize(.large)
            .disabled(plan.blocks.isEmpty)
            .help("Save the blocks as a calendar file (.ics) in Downloads, for Calendar to take")
        }
    }

    /// "12 blocks · 9 h planned · 2 won't fit"
    private var summary: some View {
        let left = plan.blocks.filter { !ticked.contains($0.id) }
        let minutes = left.reduce(0) { $0 + $1.minutes }
        var parts = [toolPlural(left.count, "block"), "\(hoursText(minutes)) planned"]
        if plan.unplanned > 0 { parts.append("\(plan.unplanned) won't fit") }
        return Text(parts.joined(separator: " · "))
            .font(.sCallout)
            .foregroundStyle(plan.unplanned > 0 ? Color.orange : Color.secondary)
            .lineLimit(1)
            .help(plan.unplanned > 0 ? "Some blocks have no day before their due date: more hours a day, or fewer days before, would fit them." : "")
    }

    private func hoursText(_ minutes: Int) -> String {
        let h = Double(minutes) / 60
        if h == h.rounded() { return "\(Int(h)) h" }
        return String(format: "%.1f h", h)
    }

    // MARK: The week

    private var weekDays: [Date] { (0..<7).compactMap { cal.date(byAdding: .day, value: $0, to: cal.startOfDay(for: Date())) } }

    /// Seven columns from today on a wide window; the seven days one under another on a narrow one.
    private var week: some View {
        ViewThatFits(in: .horizontal) {
            HStack(alignment: .top, spacing: 0) {
                ForEach(Array(weekDays.enumerated()), id: \.element) { i, day in
                    column(day)
                        .overlay(alignment: .leading) {
                            if i > 0 { Divider() }
                        }
                }
            }
            .fixedSize(horizontal: false, vertical: true)
            .frame(minWidth: 7 * 118)
            .card()
            VStack(alignment: .leading, spacing: 18) {
                ForEach(weekDays, id: \.self) { day in dayCard(day, blocks: plan.blocks(on: day, calendar: cal)) }
            }
        }
    }

    private func column(_ day: Date) -> some View {
        let blocks = plan.blocks(on: day, calendar: cal)
        let today = cal.isDateInToday(day)
        return VStack(alignment: .leading, spacing: 8) {
            VStack(spacing: 2) {
                Text(day.formatted(.dateTime.weekday(.abbreviated)).uppercased())
                    .font(.sFootnote.weight(.semibold))
                    .tracking(0.6)
                    .foregroundStyle(today ? Theme.accent : Color.secondary)
                Text("\(cal.component(.day, from: day))")
                    .font(.sTitle3.weight(today ? .bold : .medium).monospacedDigit())
                    .foregroundStyle(today ? Color.white : Color.primary)
                    .frame(width: 34, height: 34)
                    .background { if today { Circle().fill(Theme.accent) } }
                Text(blocks.isEmpty ? "free" : hoursText(blocks.reduce(0) { $0 + $1.minutes }))
                    .font(.sCaption)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 6)
            .accessibilityElement(children: .combine)
            ForEach(blocks) { b in blockCard(b) }
        }
        .padding(6)
        .frame(maxWidth: .infinity, minHeight: 300, alignment: .top)
    }

    /// A block in a column: its tick, its minutes and time, its course and its work.
    private func blockCard(_ b: StudyBlock) -> some View {
        let on = ticked.contains(b.id)
        return VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 6) {
                CheckCircle(done: on, color: ToolKind.plan.color) { tick(b, !on) }
                Text("\(b.minutes) min · \(b.start.formatted(date: .omitted, time: .shortened))")
                    .font(.sCaption.weight(.semibold).monospacedDigit())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            if !b.course.isEmpty { CourseChip(text: b.course, color: b.color) }
            Text(b.title)
                .font(.sCallout.weight(.medium))
                .strikethrough(on, color: .secondary)
                .foregroundStyle(on ? Color.secondary : Color.primary)
                .lineLimit(3)
                .multilineTextAlignment(.leading)
            Text("Block \(b.number) of \(b.of)")
                .font(.sCaption2)
                .foregroundStyle(.tertiary)
        }
        .padding(8)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(hex: b.color ?? ToolKind.plan.hex).opacity(on ? 0.06 : 0.12), in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .help(b.line)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(b.line), \(b.start.formatted(date: .omitted, time: .shortened))\(on ? ", done" : "")")
    }

    // MARK: The list

    /// Every day with a block on it, as sections.
    private var list: some View {
        let days = Dictionary(grouping: plan.blocks) { cal.startOfDay(for: $0.start) }.keys.sorted()
        return VStack(alignment: .leading, spacing: 18) {
            if days.isEmpty {
                EmptyNote(text: plan.unplanned > 0 ? "No block fits before the due dates: give the days more hours." : "Nothing to plan.", symbol: "calendar")
                    .padding(.horizontal, 18)
                    .card()
            }
            ForEach(days, id: \.self) { day in dayCard(day, blocks: plan.blocks(on: day, calendar: cal)) }
        }
    }

    private func dayCard(_ day: Date, blocks: [StudyBlock]) -> some View {
        let title = cal.isDateInToday(day) ? "Today" : cal.isDateInTomorrow(day) ? "Tomorrow" : day.formatted(.dateTime.weekday(.wide))
        let minutes = blocks.reduce(0) { $0 + $1.minutes }
        return PageSection(title: title, trailing: "\(day.formatted(.dateTime.month(.abbreviated).day())) · \(blocks.isEmpty ? "free" : hoursText(minutes))") {
            VStack(alignment: .leading, spacing: 0) {
                if blocks.isEmpty {
                    EmptyNote(text: "Nothing planned.", symbol: "calendar").padding(.horizontal, 8)
                }
                ForEach(Array(blocks.enumerated()), id: \.element.id) { i, b in
                    VStack(spacing: 0) {
                        if i > 0 { RowDivider(inset: 42) }
                        StudyBlockRow(block: b, done: ticked.contains(b.id)) { tick(b, $0) }
                    }
                }
            }
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .card()
        }
    }

    // MARK: Progress

    /// Each piece of work: its name, course and due date, and how far its blocks have come.
    private var progress: some View {
        PageSection(title: "Progress", trailing: toolPlural(plan.progress.count, "piece of work", "pieces of work")) {
            VStack(spacing: 0) {
                ForEach(Array(plan.progress.enumerated()), id: \.element.id) { i, p in
                    if i > 0 { RowDivider(inset: 14) }
                    progressRow(p)
                }
            }
            .padding(.vertical, 4)
            .card()
        }
    }

    private func progressRow(_ p: StudyProgress) -> some View {
        let doneNow = min(p.blocks, done[p.id] ?? 0)
        let tickedHere = ticked.filter { $0.hasPrefix(p.id + ":") }.count
        let planned = max(0, p.planned - tickedHere)
        let share = p.blocks > 0 ? Double(doneNow) / Double(p.blocks) : 0
        let plannedShare = p.blocks > 0 ? Double(doneNow + planned) / Double(p.blocks) : 0
        var line = "\(doneNow) of \(toolPlural(p.blocks, "block")) done"
        if planned > 0 { line += " · \(planned) planned" }
        if p.unplanned > 0 { line += " · \(p.unplanned) won't fit" }
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                IconTile(symbol: DashGlyph.kind(p.item.kind), color: Color(hex: p.item.color ?? ToolKind.plan.hex), size: 28)
                VStack(alignment: .leading, spacing: 2) {
                    Text(p.item.title).font(.sBody).lineLimit(1)
                    Text([p.item.course, p.item.kind, p.item.points.map { UnitMath.text($0) + " pts" }].compactMap { $0 }.filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                Spacer(minLength: 8)
                Text("Due \(dueWords(p.item.due))")
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(p.unplanned > 0 ? Color.orange : Color.secondary)
            }
            GeometryReader { g in
                ZStack(alignment: .leading) {
                    Capsule().fill(Theme.well)
                    Capsule().fill(ToolKind.plan.color.opacity(0.35)).frame(width: g.size.width * plannedShare)
                    Capsule().fill(ToolKind.plan.color).frame(width: g.size.width * share)
                }
            }
            .frame(height: 6)
            .animation(reduceMotion ? nil : Motion.fill, value: share)
            Text(line).font(.sCaption).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(p.item.title), due \(dueWords(p.item.due)): \(line)")
    }

    /// "Fri 11:59 PM", "Oct 30 10:30 AM".
    private func dueWords(_ d: Date) -> String {
        let apart = cal.dateComponents([.day], from: cal.startOfDay(for: Date()), to: cal.startOfDay(for: d)).day ?? 0
        let day = cal.isDateInToday(d) ? "today" : cal.isDateInTomorrow(d) ? "tomorrow" : apart < 7 ? d.formatted(.dateTime.weekday(.abbreviated)) : d.formatted(.dateTime.month(.abbreviated).day())
        return "\(day) \(d.formatted(date: .omitted, time: .shortened))"
    }

    // MARK: Settings

    private var settingsPanel: some View {
        VStack(alignment: .leading, spacing: 22) {
            PageSection(title: "Hours a day") {
                VStack(spacing: 0) {
                    ForEach(0..<7, id: \.self) { i in
                        let weekday = (cal.firstWeekday - 1 + i) % 7 // (the week in the Mac's order)
                        if i > 0 { RowDivider(inset: 14) }
                        HStack {
                            Text(cal.weekdaySymbols[weekday]).font(.sBody)
                            Spacer()
                            Stepper(value: Binding(get: { settings.hours[weekday] }, set: { settings.hours[weekday] = max(0, min(12, $0)) }), in: 0...12, step: 0.5) {
                                Text(hoursText(Int(settings.hours[weekday] * 60)))
                                    .font(.sBody.monospacedDigit())
                                    .frame(width: 44, alignment: .trailing)
                            }
                            .accessibilityLabel("\(cal.weekdaySymbols[weekday]): \(hoursText(Int(settings.hours[weekday] * 60)))")
                        }
                        .padding(.horizontal, 14)
                        .padding(.vertical, 7)
                    }
                }
                .padding(.vertical, 4)
                .card()
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Block length").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                Picker("Block length", selection: $settings.blockMinutes) {
                    ForEach(StudySettings.blockChoices, id: \.self) { m in Text("\(m) min").tag(m) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }
            VStack(alignment: .leading, spacing: 6) {
                Text("Time of day").font(.sSubheadline.weight(.semibold)).foregroundStyle(.secondary)
                Picker("Time of day", selection: $settings.timeOfDay) {
                    ForEach(StudyTime.allCases, id: \.self) { t in Text(t.label).tag(t) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }
            HStack {
                Text("Finish before the due date").font(.sBody)
                Spacer()
                Stepper(value: $settings.finishDaysBefore, in: 0...7) {
                    Text(settings.finishDaysBefore == 0 ? "on the day" : toolPlural(settings.finishDaysBefore, "day"))
                        .font(.sBody.monospacedDigit())
                }
            }
            if !courses.isEmpty {
                PageSection(title: "Courses") {
                    VStack(alignment: .leading, spacing: 8) {
                        ForEach(courses, id: \.self) { c in
                            Toggle(c, isOn: Binding(get: { !settings.excludedCourses.contains(c) }, set: { on in
                                if on { settings.excludedCourses.remove(c) } else { settings.excludedCourses.insert(c) }
                            }))
                            .toggleStyle(.checkbox)
                            .font(.sBody)
                        }
                    }
                }
            }
            Text("Each piece of work needs about 4½ minutes a point (an exam three times that, a quiz one and a half, a discussion half), spread over the days before it is due. A block you tick is done; Regenerate plans again round it. Nothing is sent to \(engine.lmsName).")
                .font(.sFootnote)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: Add to Calendar

    /// The blocks not yet done, as an .ics file in Downloads, shown in the Finder.
    private func addToCalendar() {
        let left = plan.blocks.filter { !ticked.contains($0.id) }
        guard !left.isEmpty else { return }
        let folder = PlanPrefs.downloads
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let url = ToolFiles.free("Study plan", ext: "ics", in: folder)
            try StudyICS.text(left).write(to: url, atomically: true, encoding: .utf8)
            note = "Saved \(url.lastPathComponent) to Downloads: open it to add the blocks to Calendar."
            if UserDefaults.standard.string(forKey: "SimplShotFile") == nil { ToolFiles.reveal([url]) }
        } catch {
            note = "The calendar file could not be saved: \(error.localizedDescription)"
        }
    }
}

/// A block as a row of a day's card: its tick, its work and course, its time.
private struct StudyBlockRow: View {
    let block: StudyBlock
    let done: Bool
    let toggle: (Bool) -> Void

    var body: some View {
        HStack(spacing: 12) {
            CheckCircle(done: done, color: ToolKind.plan.color) { toggle(!done) }
            VStack(alignment: .leading, spacing: 2) {
                Text(block.title)
                    .font(.sBody)
                    .lineLimit(2)
                    .strikethrough(done, color: .secondary)
                    .foregroundStyle(done ? Color.secondary : Color.primary)
                Text("\(block.minutes) min · block \(block.number) of \(block.of)")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 6)
            if !block.course.isEmpty { CourseChip(text: block.course, color: block.color) }
            Text(block.start.formatted(date: .omitted, time: .shortened))
                .font(.sCallout.monospacedDigit())
                .foregroundStyle(.secondary)
                .frame(minWidth: 58, alignment: .trailing)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 8)
        .animation(Motion.snappy, value: done)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(block.line), \(block.start.formatted(date: .omitted, time: .shortened))\(done ? ", done" : "")")
    }
}

// MARK: - The pin

/// The Study Planner's pin, opened: today's blocks as a checklist, from the plan as the settings and the blocks done
/// have it; Open Full Screen (or Return) opens the whole planner.
struct StudyPlannerCompact: View {
    let openFull: ([String: String]) -> Void
    @EnvironmentObject private var engine: Engine
    @State private var blocks: [StudyBlock]?
    @State private var failed = false
    @State private var done = PlanPrefs.done()
    @State private var ticked: Set<String> = []

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let blocks {
                if blocks.isEmpty {
                    Text(failed ? "Your upcoming work could not be read." : "Nothing planned for today.")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                } else {
                    Text("Today").font(.sCaption.weight(.semibold)).foregroundStyle(.secondary)
                    ForEach(blocks) { b in
                        let on = ticked.contains(b.id)
                        HStack(spacing: 10) {
                            CheckCircle(done: on, color: ToolKind.plan.color) { tick(b, !on) }
                            VStack(alignment: .leading, spacing: 1) {
                                Text(b.title)
                                    .font(.sBody)
                                    .lineLimit(1)
                                    .strikethrough(on, color: .secondary)
                                    .foregroundStyle(on ? Color.secondary : Color.primary)
                                Text("\(b.minutes) min · \(b.start.formatted(date: .omitted, time: .shortened))\(b.course.isEmpty ? "" : " · \(b.course)")")
                                    .font(.sCaption)
                                    .foregroundStyle(.secondary)
                                    .lineLimit(1)
                            }
                        }
                        .accessibilityElement(children: .combine)
                    }
                }
            } else {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Planning today…").font(.sCallout).foregroundStyle(.secondary)
                }
            }
            Button {
                openFull([:])
            } label: {
                Text("Open Study Planner").frame(maxWidth: .infinity)
            }
            .glassButton(prominent: true, tint: ToolKind.plan.color)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .help("Open the Study Planner whole (Return)")
        }
        .task {
            do {
                let items = try await StudySource.items(engine)
                let cal = Calendar.current
                let plan = StudyPlanner.plan(items, settings: PlanPrefs.settings(), done: done, now: Date(), calendar: cal)
                blocks = plan.blocks(on: Date(), calendar: cal)
            } catch {
                failed = true
                blocks = []
            }
        }
    }

    private func tick(_ b: StudyBlock, _ on: Bool) {
        if on { ticked.insert(b.id) } else { ticked.remove(b.id) }
        done[b.itemId] = max(0, (done[b.itemId] ?? 0) + (on ? 1 : -1))
        PlanPrefs.set("done", StudyDone.text(done))
    }
}
