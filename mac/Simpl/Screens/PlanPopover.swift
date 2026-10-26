import AppKit
import SwiftUI

/// (1.3.172, the web's 2.99.113 and 2.99.114) A plan's popover on the calendar, out of its block: when it is, what it is
/// for (or the tasks of its group, each opened with a click and taken out with ×), its alert and its notes; Open opens
/// its work, and Edit turns the popover to the plan's settings — a name of its own, the day, a whole day or its times,
/// how it repeats, an alert, a colour, notes — with Delete Plan to let it go (its group with it). Done keeps them
/// (native-app.js planEdit): a repeating plan's day changed from one of its later times moves the series by as much.
struct PlanPopover: View {
    let event: CalEvent
    /// (the screenshot suite's -SimplPlanEdit YES) opened at Edit
    var startEditing = false
    let close: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var detail: PlanDetail?
    @State private var problem: String?
    @State private var editing = false
    @State private var draft = PlanDraft()
    @State private var saving = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private let cal = Calendar.current

    var body: some View {
        Group {
            if let d = detail {
                if editing { editor(d) } else { overview(d) }
            } else if let problem {
                Text(problem).foregroundStyle(.secondary)
            } else {
                ProgressView().controlSize(.small).frame(maxWidth: .infinity, minHeight: 60)
            }
        }
        .font(.sBody)
        .padding(18)
        // (wider while editing: the two times and the ten colours each take a row's whole width beside the names)
        .frame(width: editing && detail != nil ? 384 : 340)
        .animation(reduceMotion ? nil : Motion.snappy, value: editing)
        .task { await load() }
    }

    // MARK: The plan as it is

    private func overview(_ d: PlanDetail) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 3) {
                Text(kindLine(d))
                    .font(.sFootnote.weight(.semibold))
                    .foregroundStyle(.secondary)
                Text(d.shownName)
                    .font(.sHeadline)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 14, verticalSpacing: 8) {
                fact("When", whenWords)
                if d.members.isEmpty {
                    GridRow {
                        key("For")
                        HStack(alignment: .firstTextBaseline, spacing: 6) {
                            Circle().fill(Color(hex: d.color ?? "#0a84ff")).frame(width: 9, height: 9)
                                .alignmentGuide(.firstTextBaseline) { $0[.bottom] + 1 }
                            Text(forWords(d)).fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    if let due = d.dueWords, !due.isEmpty { fact("Due", due) }
                }
                if let a = d.alert { fact("Alert", d.alerts.first(where: { $0.minutes == a })?.label ?? "\(a) minutes before") }
                if let n = d.notes, !n.isEmpty { fact("Notes", n) }
            }
            if !d.members.isEmpty {
                VStack(alignment: .leading, spacing: 2) {
                    ForEach(d.members) { m in member(m) }
                }
                .padding(4)
                .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.primary.opacity(0.045)))
                Text("Take a task out with ×: it goes back to To Do.")
                    .font(.sFootnote)
                    .foregroundStyle(.secondary)
            }
            if let problem { Text(problem).font(.sFootnote).foregroundStyle(.orange) }
            HStack {
                Spacer()
                if d.members.isEmpty, let url = d.url, !url.isEmpty {
                    Button("Open") {
                        close()
                        engine.openWeb(url, title: d.title)
                    }
                }
                Button("Edit") { edit(d) }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
            }
        }
    }

    private func kindLine(_ d: PlanDetail) -> String {
        var s = d.members.isEmpty ? "Plan" : "Plan · \(d.members.count) tasks"
        if let r = d.repeatKey, !r.isEmpty, let word = d.repeats.first(where: { $0.key == r })?.label { s += " · \(word.lowercased())" }
        return s
    }

    private func forWords(_ d: PlanDetail) -> String {
        let course = d.course ?? ""
        if let n = d.name, !n.isEmpty { return course.isEmpty ? d.title : "\(d.title) · \(course)" }
        return course.isEmpty ? "—" : course
    }

    /// The block's own time ("Wed, Oct 28 · 9:00 – 10:30 AM", or "· all day").
    private var whenWords: String {
        guard let start = PreviewFormat.date(event.date) else { return event.time ?? "" }
        let day = start.formatted(.dateTime.weekday(.abbreviated).month(.abbreviated).day())
        if event.allDay == true { return "\(day) · all day" }
        let from = start.formatted(.dateTime.hour().minute())
        guard let e = event.end, let end = PreviewFormat.date(e), end > start else { return "\(day) · \(from)" }
        return "\(day) · \(from) – \(end.formatted(.dateTime.hour().minute()))"
    }

    private func fact(_ k: String, _ v: String) -> some View {
        GridRow {
            key(k)
            Text(v).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func key(_ k: String) -> some View {
        Text(k)
            .font(.sFootnote.weight(.semibold))
            .foregroundStyle(.secondary)
            .fixedSize() // (the names whole, whatever the row beside them asks for)
            .gridColumnAlignment(.trailing)
    }

    /// A task of a group: what it is and when it is due, opened with a click; × takes it out (back to To Do).
    private func member(_ m: PlanDetail.Member) -> some View {
        HStack(spacing: 8) {
            Circle().fill(Color(hex: m.color ?? "#8e8e93")).frame(width: 9, height: 9)
            Button {
                guard let url = m.url, !url.isEmpty else { return }
                close()
                engine.openWeb(url, title: m.title)
            } label: {
                VStack(alignment: .leading, spacing: 1) {
                    Text(m.title).lineLimit(1)
                    Text([m.course ?? "", m.dueWords ?? ""].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .disabled((m.url ?? "").isEmpty)
            Button { Task { await takeOut(m) } } label: {
                Image(systemName: "xmark")
                    .font(.system(size: 9, weight: .bold))
                    .frame(width: 20, height: 20)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .foregroundStyle(.secondary)
            .help("Take it out")
            .accessibilityLabel("Take \(m.title) out of the group")
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 5)
    }

    // MARK: Edit

    private func editor(_ d: PlanDetail) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Edit plan")
                .font(.sFootnote.weight(.semibold))
                .foregroundStyle(.secondary)
            TextField("Work on \(d.title)", text: $draft.name)
                .textFieldStyle(.roundedBorder)
                .onSubmit { Task { await save(d) } }
            Grid(alignment: .leadingFirstTextBaseline, horizontalSpacing: 14, verticalSpacing: 10) {
                GridRow {
                    key("Day")
                    DatePicker("Day", selection: $draft.day, displayedComponents: .date)
                        .labelsHidden()
                        .datePickerStyle(.field)
                        .fixedSize()
                }
                GridRow {
                    key("All day")
                    Toggle("All day", isOn: $draft.allDay)
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.small)
                }
                if !draft.allDay {
                    GridRow {
                        key("Time")
                        HStack(spacing: 6) {
                            Picker("Starts", selection: Binding(get: { draft.start }, set: { draft.setStart($0) })) {
                                ForEach(Array(stride(from: 0, to: 1440, by: 15)), id: \.self) { Text(Self.clock($0)).tag($0) }
                            }
                            .labelsHidden()
                            .fixedSize()
                            Text("to").foregroundStyle(.secondary)
                            Picker("Ends", selection: Binding(get: { draft.start + draft.mins }, set: { draft.mins = max(15, $0 - draft.start) })) {
                                ForEach(Array(stride(from: draft.start + 15, through: 1440, by: 15)), id: \.self) { end in
                                    Text("\(end == 1440 ? "Midnight" : Self.clock(end)) (\(Self.length(end - draft.start)))").tag(end)
                                }
                            }
                            .labelsHidden()
                            .fixedSize()
                        }
                    }
                }
                GridRow {
                    key("Repeat")
                    Picker("Repeat", selection: $draft.repeatKey) {
                        ForEach(d.repeats, id: \.key) { Text($0.label).tag($0.key) }
                    }
                    .labelsHidden()
                    .fixedSize()
                }
                GridRow {
                    key("Alert")
                    Picker("Alert", selection: $draft.alert) {
                        ForEach(d.alerts, id: \.self) { Text($0.label).tag($0.minutes) }
                    }
                    .labelsHidden()
                    .fixedSize()
                }
                GridRow {
                    key("Colour")
                    HStack(spacing: 5) {
                        swatch(nil, shown: d.color ?? "#0a84ff")
                        ForEach(d.tints, id: \.self) { swatch($0, shown: $0) }
                    }
                }
            }
            TextField("Notes", text: $draft.notes, axis: .vertical)
                .lineLimit(3...6)
                .textFieldStyle(.roundedBorder)
            if let problem { Text(problem).font(.sFootnote).foregroundStyle(.orange) }
            HStack {
                Button("Delete Plan", role: .destructive) { Task { await delete(d) } }
                Spacer()
                Button("Cancel") {
                    problem = nil
                    editing = false
                }
                .keyboardShortcut(.cancelAction)
                Button("Done") { Task { await save(d) } }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(saving)
            }
        }
    }

    /// A colour to pick: the course's own (`tint` nil), or one of the plan's own.
    private func swatch(_ tint: String?, shown: String) -> some View {
        let on = draft.tint == tint
        return Button { draft.tint = tint } label: {
            Circle()
                .fill(Color(hex: shown))
                .frame(width: 16, height: 16)
                .overlay(Circle().strokeBorder(Color.primary.opacity(on ? 0.55 : 0), lineWidth: 2).padding(-3))
                .padding(3)
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .help(tint == nil ? "The course’s colour" : "")
        .accessibilityLabel(tint == nil ? "The course’s colour" : "Colour \(shown)")
        .accessibilityAddTraits(on ? .isSelected : [])
    }

    /// "4:15 PM", `minutes` into a day.
    static func clock(_ minutes: Int) -> String {
        let at = Calendar.current.date(bySettingHour: min(23, minutes / 60), minute: minutes % 60, second: 0, of: Date()) ?? Date()
        return at.formatted(.dateTime.hour().minute())
    }

    /// "45m", "1h", "1h 15m".
    static func length(_ m: Int) -> String {
        m < 60 ? "\(m)m" : m % 60 == 0 ? "\(m / 60)h" : "\(m / 60)h \(m % 60)m"
    }

    // MARK: The page

    private func load() async {
        guard let pid = event.planId else { return }
        do {
            let d = try await engine.call("planDetail", ["id": pid], as: PlanDetail.self)
            detail = d
            problem = nil
            if startEditing { edit(d) }
        } catch {
            problem = error.localizedDescription
        }
    }

    private func edit(_ d: PlanDetail) {
        let day = PreviewFormat.date(event.date) ?? Date()
        let time = PreviewFormat.date(d.at) ?? day // (the series' time of day: a whole day's block starts at midnight)
        let into = (cal.component(.hour, from: time) * 60 + cal.component(.minute, from: time)) / 15 * 15
        draft = PlanDraft(
            name: d.name ?? "",
            day: cal.startOfDay(for: day),
            allDay: d.allDay == true,
            start: min(1425, d.allDay == true && into == 0 ? 9 * 60 : into),
            mins: max(15, d.mins),
            repeatKey: d.repeatKey ?? "",
            alert: d.alert,
            notes: d.notes ?? "",
            tint: d.tint
        )
        if draft.start + draft.mins > 1440 { draft.mins = 1440 - draft.start }
        problem = nil
        editing = true
    }

    private func save(_ d: PlanDetail) async {
        guard !saving else { return }
        saving = true
        defer { saving = false }
        let at = draft.allDay ? draft.day : (cal.date(byAdding: .minute, value: draft.start, to: draft.day) ?? draft.day)
        let iso = ISO8601DateFormatter()
        var args: [String: Any] = [
            "id": d.id, "name": draft.name, "at": iso.string(from: at), "allDay": draft.allDay, "repeat": draft.repeatKey,
            "notes": draft.notes, "alert": draft.alert.map { $0 as Any } ?? NSNull(), "tint": draft.tint.map { $0 as Any } ?? NSNull(),
        ]
        if !draft.allDay { args["mins"] = draft.mins }
        if let occurs = event.occurs { args["occurs"] = occurs } // (one of a repeating plan's times: the series moves by as much)
        do {
            let r = try await engine.call("planEdit", args, as: ActReply.self)
            guard r.ok == true else {
                problem = r.error ?? "The plan could not be saved."
                return
            }
            NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
            close()
            engine.changed()
        } catch {
            problem = error.localizedDescription
        }
    }

    private func delete(_ d: PlanDetail) async {
        do {
            _ = try await engine.call("planClear", ["id": d.id, "group": true], as: ActReply.self)
            close()
            engine.changed()
        } catch {
            problem = error.localizedDescription
        }
    }

    /// A task taken out of its group: back on To Do, unplanned; the popover follows the group (closed with the last).
    private func takeOut(_ m: PlanDetail.Member) async {
        do {
            _ = try await engine.call("planClear", ["id": m.id], as: ActReply.self)
            engine.changed()
            guard let rest = detail?.members.first(where: { $0.id != m.id }) else {
                close()
                return
            }
            detail = try await engine.call("planDetail", ["id": rest.id], as: PlanDetail.self)
        } catch {
            problem = error.localizedDescription
        }
    }
}

/// (1.3.172) What native-app.js planDetail answers: a plan's settings, its group's tasks (none for a plan alone), and the
/// repeats, alerts and colours Edit offers.
struct PlanDetail: Decodable {
    struct Member: Decodable, Identifiable {
        var id: String
        var title: String
        var course: String?
        var color: String?
        var dueWords: String?
        var url: String?
    }
    struct Repeat: Decodable, Hashable {
        var key: String
        var label: String
    }
    struct Alert: Decodable, Hashable {
        var minutes: Int?
        var label: String
    }
    var id: String
    var name: String?
    var title: String
    var course: String?
    var color: String?
    var tint: String?
    var at: String
    var mins: Int
    var allDay: Bool?
    var repeatKey: String?
    var alert: Int?
    var notes: String?
    var dueWords: String?
    var url: String?
    var members: [Member]
    var repeats: [Repeat]
    var alerts: [Alert]
    var tints: [String]

    enum CodingKeys: String, CodingKey {
        case id, name, title, course, color, tint, at, mins, allDay, alert, notes, dueWords, url, repeats, alerts, tints
        case repeatKey = "repeat"
        case members = "tasks"
    }

    /// Its own name, or its group's tasks' one after another, or its work's.
    var shownName: String {
        if let n = name, !n.isEmpty { return n }
        return members.isEmpty ? title : members.map(\.title).joined(separator: " + ")
    }
}

/// (1.3.172) Edit's settings as they are being changed: the day at its midnight, the start and length in minutes.
struct PlanDraft {
    var name = ""
    var day = Calendar.current.startOfDay(for: Date())
    var allDay = false
    var start = 9 * 60
    var mins = 60
    var repeatKey = ""
    var alert: Int?
    var notes = ""
    var tint: String?

    /// A new start keeps the length, as far as midnight allows.
    mutating func setStart(_ s: Int) {
        start = s
        mins = min(mins, 1440 - s)
    }
}
