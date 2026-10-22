import SwiftUI

/// (1.3.151) Find Appointment, as the web's sheet has it (extension/content/app/screens/calendar.js): Canvas's Scheduler —
/// office hours, conferences, any times a teacher opened — read through the page (native-app.js `appointments`). The class
/// first; then each group as a card: its place, how long a time is and how many each student may hold, the time held (Give
/// Back), and its days, each opening to its times. A press on an open time reserves it; Canvas's own words when it will not.
/// A time reserved goes on the calendar (`changed`).
struct AppointmentsPopup: View {
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @State private var data: AppointmentsData?
    @State private var course: String?
    /// The days opened to their times (a day key each): the day held, else the first with a time open, to begin with.
    @State private var open: Set<String> = []
    /// The time (or reservation) Canvas is being asked about.
    @State private var busy: String?
    @State private var said: String?
    @State private var saidBad = false

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            ScrollView {
                content
                    .padding(20)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 580)
        .frame(minHeight: 360, idealHeight: 560, maxHeight: 680)
        .task { await load() }
    }

    private var header: some View {
        HStack(spacing: 12) {
            IconTile(symbol: "calendar.badge.clock", color: Theme.accent, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text("Find Appointment")
                    .font(.sTitle3.weight(.semibold))
                Text("Office hours and sign-ups with a time to reserve")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 0)
            PopupCloseButton()
        }
        .padding(.horizontal, 20)
        .padding(.top, 18)
        .padding(.bottom, 12)
        .popupHeader()
    }

    @ViewBuilder
    private var content: some View {
        if let d = data {
            VStack(alignment: .leading, spacing: 16) {
                if let said { saidLine(said) }
                if let e = d.error { saidLine(e, bad: true) }
                if d.groups.isEmpty {
                    if d.error == nil {
                        VStack(spacing: 8) {
                            Image(systemName: "calendar.badge.checkmark")
                                .font(.system(size: 30))
                                .foregroundStyle(.tertiary)
                            Text("Nothing to sign up for right now")
                                .font(.sHeadline)
                            Text("Office hours and conferences your teachers open will wait here.")
                                .font(.sCallout)
                                .foregroundStyle(.secondary)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 40)
                    }
                } else {
                    if d.classes.count > 1 { classes(d.classes) }
                    ForEach(shown(d)) { g in card(g) }
                    Text("A time you reserve goes on the calendar; the open ones wait here.")
                        .font(.sCaption)
                        .foregroundStyle(.secondary)
                }
            }
        } else {
            ProgressView()
                .frame(maxWidth: .infinity, minHeight: 240)
        }
    }

    private func saidLine(_ text: String, bad: Bool? = nil) -> some View {
        let isBad = bad ?? saidBad
        return HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: isBad ? "exclamationmark.triangle.fill" : "checkmark.circle.fill")
                .foregroundStyle(isBad ? Color.orange : Color.green)
            Text(text)
                .font(.sCallout)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill((isBad ? Color.orange : Color.green).opacity(0.12)))
    }

    /// The class first, as Canvas asks: one chip a course with something to sign up for.
    private func classes(_ list: [AppointmentClass]) -> some View {
        HStack(alignment: .center, spacing: 10) {
            Text("Class")
                .font(.sCallout.weight(.semibold))
                .foregroundStyle(.secondary)
            PillFlow(spacing: 6, lineSpacing: 6) {
                ForEach(list) { c in
                    let on = c.code == pickedClass
                    Button {
                        withAnimation(Motion.snappy) { course = c.code }
                    } label: {
                        HStack(spacing: 6) {
                            Circle().fill(Color(hex: c.color)).frame(width: 8, height: 8)
                            Text(c.name).lineLimit(1)
                        }
                        .font(.sCallout.weight(on ? .semibold : .regular))
                        .padding(.horizontal, 11)
                        .padding(.vertical, 6)
                        .background(Capsule().fill(on ? Theme.accent.opacity(0.18) : Color.primary.opacity(0.06)))
                        .contentShape(Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(on ? .isSelected : [])
                }
            }
        }
    }

    private var pickedClass: String? {
        guard let d = data else { return nil }
        if let course, d.classes.contains(where: { $0.code == course }) { return course }
        return d.classes.first?.code
    }

    private func shown(_ d: AppointmentsData) -> [AppointmentGroup] {
        guard let code = pickedClass else { return d.groups }
        return d.groups.filter { $0.codes.contains(code) }
    }

    private func meta(_ g: AppointmentGroup) -> String? {
        let parts = [
            g.location.flatMap { $0.isEmpty ? nil : $0 },
            (g.minutes ?? 0) > 0 ? "\(g.minutes ?? 0) min each" : nil,
            (g.max ?? 0) > 0 ? (g.max == 1 ? "one time each" : "\(g.max ?? 0) times each") : nil,
        ].compactMap { $0 }
        return parts.isEmpty ? nil : parts.joined(separator: " · ")
    }

    private func card(_ g: AppointmentGroup) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top, spacing: 10) {
                RoundedRectangle(cornerRadius: 3, style: .continuous)
                    .fill(Color(hex: g.color))
                    .frame(width: 12, height: 12)
                    .padding(.top, 4)
                VStack(alignment: .leading, spacing: 3) {
                    Text(g.title)
                        .font(.sHeadline)
                        .fixedSize(horizontal: false, vertical: true)
                    if let m = meta(g) {
                        Text(m)
                            .font(.sCallout)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                }
            }
            if let desc = g.description, !desc.isEmpty {
                Text(desc)
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                    .lineLimit(4)
                    .fixedSize(horizontal: false, vertical: true)
            }
            if g.groupSignup == true {
                Text("A sign-up for your group: reserve it as the group in \(engine.lmsName).")
                    .font(.sCallout)
                    .foregroundStyle(.secondary)
                Button("Open in \(engine.lmsName)") { engine.openWebScreen(g.url ?? "/calendar", title: g.title) }
                    .glassButton()
            } else {
                ForEach(g.mine) { held in heldRow(held) }
                VStack(spacing: 0) {
                    ForEach(Array(g.days.enumerated()), id: \.element.key) { i, day in
                        if i > 0 { Divider().padding(.leading, 12) }
                        dayRow(g, day)
                    }
                }
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.primary.opacity(0.045)))
            }
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color.primary.opacity(0.05)))
        .overlay(RoundedRectangle(cornerRadius: 16, style: .continuous).strokeBorder(Color.primary.opacity(0.07), lineWidth: 1))
    }

    private func heldRow(_ held: AppointmentHeld) -> some View {
        HStack(spacing: 10) {
            Image(systemName: "checkmark.circle.fill")
                .foregroundStyle(Color.green)
            Text("Your time: \(held.when)")
                .font(.sCallout.weight(.medium))
            Spacer(minLength: 8)
            if busy == held.reservationId, held.reservationId != nil {
                ProgressView().controlSize(.small)
            }
            Button("Give Back") { Task { await giveBack(held) } }
                .glassButton()
                .disabled(busy != nil)
                .help("Give this time back, so someone else can take it")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Color.green.opacity(0.1)))
    }

    private func dayRow(_ g: AppointmentGroup, _ day: AppointmentDay) -> some View {
        let isOpen = open.contains(day.key)
        return VStack(alignment: .leading, spacing: 10) {
            Button {
                withAnimation(Motion.snappy) {
                    if isOpen { open.remove(day.key) } else { open.insert(day.key) }
                }
            } label: {
                HStack(spacing: 10) {
                    Text(day.label)
                        .font(.sBody.weight(.semibold))
                    Text(day.sub)
                        .font(.sCallout)
                        .foregroundStyle(day.held == true ? Color.green : Color.secondary)
                        .lineLimit(1)
                    Spacer(minLength: 0)
                    Image(systemName: "chevron.right")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.tertiary)
                        .rotationEffect(.degrees(isOpen ? 90 : 0))
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityValue(isOpen ? "Open" : "Closed")
            if isOpen {
                PillFlow(spacing: 6, lineSpacing: 6) {
                    ForEach(day.slots) { s in slotButton(g, day, s) }
                }
                .transition(.opacity)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
    }

    private func slotButton(_ g: AppointmentGroup, _ day: AppointmentDay, _ s: AppointmentSlot) -> some View {
        let full = g.full == true
        let canPick = s.open && !full && busy == nil
        let word = s.reserved ? "Your time" : !s.open ? "Taken" : full ? "You hold the most times this allows: give one back first" : "Reserve \(day.label) at \(s.time)"
        return Button {
            Task { await reserve(day, s) }
        } label: {
            HStack(spacing: 5) {
                if s.reserved {
                    Image(systemName: "checkmark")
                        .font(.system(size: 10, weight: .bold))
                }
                Text(s.time)
                    .strikethrough(!s.open && !s.reserved)
                if busy == s.id { ProgressView().controlSize(.mini) }
            }
            .font(.sCallout.weight(.medium).monospacedDigit())
            .foregroundStyle(s.reserved ? Color.white : s.open && !full ? Color.primary : Color.secondary)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(Capsule().fill(s.reserved ? Color.green : s.open && !full ? Theme.accent.opacity(0.14) : Color.primary.opacity(0.04)))
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .disabled(!canPick)
        .help(word)
        .accessibilityLabel(Text("\(s.time), \(s.reserved ? "your time" : s.open ? "open" : "taken")"))
    }

    // MARK: - Asking Canvas

    private func load() async {
        let got = (try? await engine.call("appointments", ["force": true], as: AppointmentsData.self))
            ?? AppointmentsData(groups: [], classes: [], error: "Appointments could not be loaded.")
        withAnimation(Motion.snappy) {
            data = got
            for g in got.groups where !g.days.contains(where: { open.contains($0.key) }) {
                if let pick = g.days.first(where: { $0.held == true }) ?? g.days.first(where: { ($0.open ?? 0) > 0 }) ?? g.days.first {
                    open.insert(pick.key)
                }
            }
        }
    }

    private func reserve(_ day: AppointmentDay, _ s: AppointmentSlot) async {
        busy = s.id
        let r = try? await engine.call("reserveAppointment", ["id": s.id], as: ActReply.self)
        busy = nil
        if r?.ok == true {
            say("Reserved: \(day.label) at \(s.time). It is on your calendar.", bad: false)
            changed()
        } else {
            say("\(engine.lmsName) would not reserve that time: \(r?.error ?? "no answer").", bad: true)
        }
        await load()
    }

    private func giveBack(_ held: AppointmentHeld) async {
        guard let id = held.reservationId else {
            // (held, Canvas says, but not by which event: its own page can undo it)
            engine.openWebScreen("/calendar", title: "Calendar")
            return
        }
        busy = id
        let r = try? await engine.call("cancelReservation", ["id": id], as: ActReply.self)
        busy = nil
        if r?.ok == true {
            say("Your time is given back.", bad: false)
            changed()
        } else {
            say("\(engine.lmsName) would not give it back: \(r?.error ?? "no answer").", bad: true)
        }
        await load()
    }

    private func say(_ text: String, bad: Bool) {
        withAnimation(Motion.snappy) {
            said = text
            saidBad = bad
        }
    }
}
